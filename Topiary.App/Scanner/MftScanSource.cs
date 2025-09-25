using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Topiary.App.Interop;

namespace Topiary.App.Scanner;

/// <summary>
/// High-performance Windows NTFS scanner using MFT (Master File Table) enumeration.
/// Uses USN (Update Sequence Number) journal to stream file records directly from NTFS.
/// This is 5-10x faster than traditional directory walking on large NTFS volumes.
/// </summary>
public sealed class MftScanSource : IScanSource
{
    private const int BUFFER_SIZE = 64 * 1024; // 64KB read buffer
    private const int MAX_RECORDS_PER_BATCH = 1000; // Batch size for processing
    
    public string Description => "Windows NTFS MFT Fast Path";
    
    /// <summary>
    /// Scan NTFS volume using MFT enumeration for maximum performance.
    /// Only works on Windows with NTFS volumes.
    /// </summary>
    public async IAsyncEnumerable<Entry> ScanAsync(string volumeRoot, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("MftScanSource only works on Windows");
            
        // Normalize volume root to \\.\C: format
        var volumeName = GetVolumeDeviceName(volumeRoot);
        
        using var volumeHandle = OpenVolume(volumeName);
        if (volumeHandle.IsInvalid)
        {
            throw new IOException($"Failed to open volume {volumeName}: Error {Win32.GetLastError()}");
        }
        
        // Query USN journal for progress estimation
        var journalData = QueryUsnJournal(volumeHandle);
        
        // Create enumeration data structure
        var enumData = new Win32.MFT_ENUM_DATA_V1
        {
            StartFileReferenceNumber = 0,
            LowUsn = 0,
            HighUsn = (ulong)journalData.NextUsn,
            MinMajorVersion = 2,
            MaxMajorVersion = 3
        };
        
        var buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
        try
        {
            var enumDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Win32.MFT_ENUM_DATA_V1>());
            try
            {
                Marshal.StructureToPtr(enumData, enumDataPtr, false);
                
                await foreach (var entry in EnumerateMftRecords(volumeHandle, enumDataPtr, buffer, cancellationToken))
                {
                    yield return entry;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(enumDataPtr);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    
    public ValueTask<long> GetEstimatedEntryCountAsync(string volumeRoot, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return ValueTask.FromResult(0L);
            
        try
        {
            var volumeName = GetVolumeDeviceName(volumeRoot);
            using var volumeHandle = OpenVolume(volumeName);
            if (volumeHandle.IsInvalid)
                return ValueTask.FromResult(0L);
                
            var journalData = QueryUsnJournal(volumeHandle);
            
            // Rough estimate: assume average USN record size of 80 bytes
            // This gives us ballpark figures for progress reporting
            var estimatedRecords = (journalData.NextUsn - journalData.FirstUsn) / 80;
            return ValueTask.FromResult(Math.Max(1000, estimatedRecords)); // Minimum reasonable estimate
        }
        catch
        {
            return ValueTask.FromResult(0L); // Estimation failed, progress will be indeterminate
        }
    }
    
    private static SafeFileHandle OpenVolume(string volumeName)
    {
        return Win32.CreateFileW(
            volumeName,
            Win32.GENERIC_READ,
            Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE | Win32.FILE_SHARE_DELETE,
            IntPtr.Zero,
            Win32.OPEN_EXISTING,
            Win32.FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);
    }
    
    private static Win32.USN_JOURNAL_DATA_V1 QueryUsnJournal(SafeFileHandle volumeHandle)
    {
        var journalData = new Win32.USN_JOURNAL_DATA_V1();
        var size = Marshal.SizeOf<Win32.USN_JOURNAL_DATA_V1>();
        var dataPtr = Marshal.AllocHGlobal(size);
        
        try
        {
            var success = Win32.DeviceIoControl(
                volumeHandle,
                Win32.FSCTL_QUERY_USN_JOURNAL,
                IntPtr.Zero, 0,
                dataPtr, (uint)size,
                out _,
                IntPtr.Zero);
                
            if (!success)
            {
                var error = Win32.GetLastError();
                throw new IOException($"Failed to query USN journal: Error {error}");
            }
            
            return Marshal.PtrToStructure<Win32.USN_JOURNAL_DATA_V1>(dataPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(dataPtr);
        }
    }
    
    private static async IAsyncEnumerable<Entry> EnumerateMftRecords(
        SafeFileHandle volumeHandle,
        IntPtr enumDataPtr,
        byte[] buffer,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var processedCount = 0;
        var bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        var bufferPtr = bufferHandle.AddrOfPinnedObject();
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Call DeviceIoControl to get next batch of USN records
                var success = Win32.DeviceIoControl(
                    volumeHandle,
                    Win32.FSCTL_ENUM_USN_DATA,
                    enumDataPtr, (uint)Marshal.SizeOf<Win32.MFT_ENUM_DATA_V1>(),
                    bufferPtr, (uint)buffer.Length,
                    out var bytesReturned,
                    IntPtr.Zero);
                
                if (!success)
                {
                    var error = Win32.GetLastError();
                    if (error == 38) // ERROR_HANDLE_EOF - End of enumeration
                        break;
                        
                    throw new IOException($"MFT enumeration failed: Error {error}");
                }
                
                if (bytesReturned == 0)
                    break;
                
                // Parse USN records from buffer
                var offset = sizeof(ulong); // Skip first 8 bytes (next USN)
                var batchCount = 0;
                
                while (offset < bytesReturned && batchCount < MAX_RECORDS_PER_BATCH)
                {
                    if (offset + Marshal.SizeOf<Win32.USN_RECORD_V2>() > bytesReturned)
                        break;
                    
                    var recordPtr = IntPtr.Add(bufferPtr, offset);
                    var recordLength = Marshal.ReadInt32(recordPtr);
                    
                    if (recordLength < Marshal.SizeOf<Win32.USN_RECORD_V2>() || offset + recordLength > bytesReturned)
                        break;
                    
                    var entry = ParseUsnRecord(recordPtr);
                    if (entry.HasValue)
                    {
                        yield return entry.Value;
                        batchCount++;
                    }
                    
                    offset += recordLength;
                    
                    // Align to 8-byte boundary
                    offset = (offset + 7) & ~7;
                }
                
                processedCount += batchCount;
                
                // Yield periodically to prevent thread starvation
                if (processedCount % (MAX_RECORDS_PER_BATCH * 10) == 0)
                {
                    await Task.Yield();
                }
            }
        }
        finally
        {
            bufferHandle.Free();
        }
    }
    
    private static Entry? ParseUsnRecord(IntPtr recordPtr)
    {
        try
        {
            var majorVersion = Marshal.ReadInt16(recordPtr, 4); // MajorVersion offset
            
            if (majorVersion == 2)
            {
                var record = Marshal.PtrToStructure<Win32.USN_RECORD_V2>(recordPtr);
                return ConvertUsnRecordV2(record, recordPtr);
            }
            else if (majorVersion == 3)
            {
                var record = Marshal.PtrToStructure<Win32.USN_RECORD_V3>(recordPtr);
                return ConvertUsnRecordV3(record, recordPtr);
            }
            
            return null; // Unsupported version
        }
        catch
        {
            return null; // Skip corrupted records
        }
    }
    
    private static Entry ConvertUsnRecordV2(Win32.USN_RECORD_V2 record, IntPtr recordPtr)
    {
        // Extract filename from the record
        var namePtr = IntPtr.Add(recordPtr, record.FileNameOffset);
        var nameLength = record.FileNameLength / 2; // UTF-16 character count
        var name = Marshal.PtrToStringUni(namePtr, nameLength) ?? "";
        
        return new Entry(
            fileId: (UInt128)record.FileReferenceNumber,
            parentFileId: (UInt128)record.ParentFileReferenceNumber,
            attributes: Win32.ConvertAttributes(record.FileAttributes),
            size: 0, // USN records don't contain file size - would need follow-up query
            allocationSize: 0,
            creationTime: record.TimeStamp,
            lastWriteTime: record.TimeStamp,
            name: name.AsMemory(),
            linkCount: 1);
    }
    
    private static Entry ConvertUsnRecordV3(Win32.USN_RECORD_V3 record, IntPtr recordPtr)
    {
        // Extract filename from the record
        var namePtr = IntPtr.Add(recordPtr, record.FileNameOffset);
        var nameLength = record.FileNameLength / 2; // UTF-16 character count
        var name = Marshal.PtrToStringUni(namePtr, nameLength) ?? "";
        
        return new Entry(
            fileId: record.FileReferenceNumber,
            parentFileId: record.ParentFileReferenceNumber,
            attributes: Win32.ConvertAttributes(record.FileAttributes),
            size: 0, // USN records don't contain file size - would need follow-up query
            allocationSize: 0,
            creationTime: record.TimeStamp,
            lastWriteTime: record.TimeStamp,
            name: name.AsMemory(),
            linkCount: 1);
    }
    
    private static string GetVolumeDeviceName(string volumeRoot)
    {
        // Convert "C:\" to "\\.\C:" format
        var driveLetter = Path.GetPathRoot(volumeRoot)?.TrimEnd('\\', ':') ?? throw new ArgumentException("Invalid volume root", nameof(volumeRoot));
        return $"\\\\.\\{driveLetter}:";
    }
}