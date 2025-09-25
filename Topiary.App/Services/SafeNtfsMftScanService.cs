using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Topiary.App.Interop;
using Topiary.App.Models;
using Avalonia.Threading;

namespace Topiary.App.Services;

/// <summary>
/// Safe NTFS MFT scanning service with proper fallback mechanisms and admin privilege detection.
/// This is a safer version of NtfsMftScanService that includes proper error handling and fallback to ResponsiveDiskScanService.
/// </summary>
public class SafeNtfsMftScanService : IScanService
{
    private readonly ResponsiveDiskScanService _fallbackScanner;
    private readonly Dictionary<ulong, SafeMftEntry> _entriesByFrn = new();
    private readonly List<SafeMftEntry> _rootEntries = new();
    private Timer? _progressTimer;

    public SafeNtfsMftScanService()
    {
        _fallbackScanner = new ResponsiveDiskScanService();
    }

    public async Task<string[]> GetAvailableDrivesAsync()
    {
        // Use the fallback scanner's drive enumeration since it's more appropriate
        return await _fallbackScanner.GetAvailableDrivesAsync();
    }

    public async Task<ScanResult> ScanDriveAsync(string drivePath, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // Pre-flight checks to determine if MFT scanning is possible
        var canUseMft = await CanUseMftScanningAsync(drivePath, progress);

        if (!canUseMft.CanUse)
        {
            progress?.Report(new ScanProgress(0, 0, TimeSpan.Zero,
                $"MFT scanning not available: {canUseMft.Reason}. Using standard scanning."));

            // Fallback to responsive scanner
            return await _fallbackScanner.ScanDriveAsync(drivePath, progress ?? new Progress<ScanProgress>(), cancellationToken);
        }

        // Try MFT scanning with fallback on any error
        try
        {
            return await ScanWithMftAsync(drivePath, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            progress?.Report(new ScanProgress(0, 0, TimeSpan.Zero,
                $"MFT scanning failed ({ex.Message}). Falling back to standard scanning..."));

            // Clear any partial MFT data
            _entriesByFrn.Clear();
            _rootEntries.Clear();

            // Fallback to responsive scanner
            return await _fallbackScanner.ScanDriveAsync(drivePath, progress ?? new Progress<ScanProgress>(), cancellationToken);
        }
    }

    private async Task<(bool CanUse, string Reason)> CanUseMftScanningAsync(string drivePath, IProgress<ScanProgress>? progress)
    {
        await Task.Delay(1, CancellationToken.None); // Make it async

        // Check 1: Windows only
        if (!OperatingSystem.IsWindows())
        {
            return (false, "MFT scanning requires Windows");
        }

        // Check 2: Admin privileges
        if (!IsRunningAsAdministrator())
        {
            return (false, "MFT scanning requires administrator privileges");
        }

        // Check 3: Drive exists and is ready
        try
        {
            var driveInfo = new DriveInfo(drivePath);
            if (!driveInfo.IsReady)
            {
                return (false, "Drive is not ready");
            }

            // Check 4: NTFS file system
            if (!string.Equals(driveInfo.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Drive uses {driveInfo.DriveFormat} filesystem (NTFS required)");
            }

            // Check 5: Fixed drive (removable drives may not support low-level access)
            if (driveInfo.DriveType != DriveType.Fixed)
            {
                return (false, $"Drive type {driveInfo.DriveType} not supported for MFT scanning");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Drive access error: {ex.Message}");
        }

        // Check 6: Test actual volume handle access
        try
        {
            var volumePath = $@"\\.\{drivePath.TrimEnd('\\')}";
            using var volumeHandle = Win32.CreateFileW(
                volumePath,
                Win32.GENERIC_READ,
                Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE | Win32.FILE_SHARE_DELETE,
                IntPtr.Zero,
                Win32.OPEN_EXISTING,
                Win32.FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero
            );

            if (volumeHandle.IsInvalid)
            {
                return (false, $"Cannot open volume handle (Error: {Win32.GetLastError()})");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Volume handle test failed: {ex.Message}");
        }

        return (true, "MFT scanning is available");
    }

    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private async Task<ScanResult> ScanWithMftAsync(string drivePath, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var context = new SafeMftScanContext
        {
            DrivePath = drivePath,
            Progress = progress,
            CancellationToken = cancellationToken,
            StartTime = DateTime.UtcNow
        };

        try
        {
            _progressTimer = new Timer(UpdateProgress, context, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

            progress?.Report(new ScanProgress(0, 0, TimeSpan.Zero, "Accessing NTFS Master File Table..."));

            // Phase 1: Read NTFS volume metadata
            await Task.Run(() => ReadVolumeInfo(context), cancellationToken);

            // Phase 2: Parse MFT records directly
            progress?.Report(new ScanProgress(10, 0, DateTime.UtcNow - context.StartTime, "Parsing MFT records..."));
            await Task.Run(() => ParseMftRecordsImproved(context), cancellationToken);

            // Phase 3: Build FRN tree from FILE_NAME attributes
            progress?.Report(new ScanProgress(80, context.ProcessedCount, DateTime.UtcNow - context.StartTime, "Building directory tree..."));
            await Task.Run(() => BuildMftTree(context), cancellationToken);

            progress?.Report(new ScanProgress(95, context.ProcessedCount, DateTime.UtcNow - context.StartTime, "Finalizing results..."));
            return await Task.Run(() => BuildScanResult(context), cancellationToken);
        }
        finally
        {
            _progressTimer?.Dispose();
            _progressTimer = null;
        }
    }

    private unsafe void ReadVolumeInfo(SafeMftScanContext context)
    {
        var volumePath = $@"\\.\{context.DrivePath.TrimEnd('\\')}";

        using var volumeHandle = Win32.CreateFileW(
            volumePath,
            Win32.GENERIC_READ,
            Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE | Win32.FILE_SHARE_DELETE,
            IntPtr.Zero,
            Win32.OPEN_EXISTING,
            Win32.FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero
        );

        if (volumeHandle.IsInvalid)
        {
            throw new InvalidOperationException($"Cannot open volume {volumePath}: {Win32.GetLastError()}");
        }

        // Get NTFS volume data
        var volumeData = new Win32.NTFS_VOLUME_DATA_BUFFER();
        var volumeDataSize = Marshal.SizeOf<Win32.NTFS_VOLUME_DATA_BUFFER>();

        if (!Win32.DeviceIoControl(
            volumeHandle,
            Win32.FSCTL_GET_NTFS_VOLUME_DATA,
            IntPtr.Zero, 0,
            new IntPtr(&volumeData), (uint)volumeDataSize,
            out uint bytesReturned,
            IntPtr.Zero))
        {
            throw new InvalidOperationException($"FSCTL_GET_NTFS_VOLUME_DATA failed: {Win32.GetLastError()}");
        }

        context.VolumeData = volumeData;
        context.TotalRecords = (int)(volumeData.MftValidDataLength / volumeData.BytesPerFileRecordSegment);
    }

    // Improved version with better memory management and progress reporting
    private unsafe void ParseMftRecordsImproved(SafeMftScanContext context)
    {
        var volumePath = $@"\\.\{context.DrivePath.TrimEnd('\\')}";

        using var volumeHandle = Win32.CreateFileW(
            volumePath,
            Win32.GENERIC_READ,
            Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE | Win32.FILE_SHARE_DELETE,
            IntPtr.Zero,
            Win32.OPEN_EXISTING,
            Win32.FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero
        );

        if (volumeHandle.IsInvalid)
            throw new InvalidOperationException($"Cannot open volume {volumePath}");

        var recordSize = context.VolumeData.BytesPerFileRecordSegment;
        const int batchSize = 100; // Smaller batches for better responsiveness

        // Reuse buffers to reduce allocations
        var inputPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Win32.NTFS_FILE_RECORD_INPUT_BUFFER>());
        var outputPtr = Marshal.AllocHGlobal(1024 + (int)recordSize);

        try
        {
            for (ulong frn = 0; frn < (ulong)context.TotalRecords; frn += batchSize)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var actualBatchSize = Math.Min(batchSize, context.TotalRecords - (int)frn);

                for (int i = 0; i < actualBatchSize; i++)
                {
                    var currentFrn = frn + (ulong)i;
                    var inputBuffer = new Win32.NTFS_FILE_RECORD_INPUT_BUFFER
                    {
                        FileReferenceNumber = currentFrn
                    };

                    Marshal.StructureToPtr(inputBuffer, inputPtr, true);

                    if (Win32.DeviceIoControl(
                        volumeHandle,
                        Win32.FSCTL_GET_NTFS_FILE_RECORD,
                        inputPtr, (uint)Marshal.SizeOf<Win32.NTFS_FILE_RECORD_INPUT_BUFFER>(),
                        outputPtr, 1024 + recordSize,
                        out uint bytesReturned,
                        IntPtr.Zero))
                    {
                        ParseFileRecord(outputPtr, (int)bytesReturned, currentFrn, context);
                    }

                    // Update progress per record, not per batch
                    context.ProcessedCount = (int)(frn + (ulong)i + 1);
                }

                // Yield every batch for UI responsiveness
                Thread.Yield();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inputPtr);
            Marshal.FreeHGlobal(outputPtr);
        }
    }

    // The rest of the methods are similar to NtfsMftScanService but with the corrected FullPath
    private unsafe void ParseFileRecord(IntPtr outputBuffer, int bufferSize, ulong frn, SafeMftScanContext context)
    {
        if (bufferSize < Marshal.SizeOf<Win32.NTFS_FILE_RECORD_OUTPUT_BUFFER>())
            return;

        var outputHeader = Marshal.PtrToStructure<Win32.NTFS_FILE_RECORD_OUTPUT_BUFFER>(outputBuffer);

        var fileRecordPtr = (byte*)outputBuffer.ToPointer() + Marshal.SizeOf<Win32.NTFS_FILE_RECORD_OUTPUT_BUFFER>();
        var fileRecord = (Win32.FILE_RECORD_SEGMENT_HEADER*)fileRecordPtr;

        if (fileRecord->Signature != 0x454C4946) // "FILE"
            return;

        if ((fileRecord->Flags & 0x01) == 0) // Skip deleted records
            return;

        var entry = new SafeMftEntry
        {
            FileReferenceNumber = frn,
            InUse = (fileRecord->Flags & 0x01) != 0,
            IsDirectory = (fileRecord->Flags & 0x02) != 0
        };

        // Parse attributes
        var attributePtr = fileRecordPtr + fileRecord->FirstAttributeOffset;
        var recordEnd = fileRecordPtr + fileRecord->RealSize;

        while (attributePtr < recordEnd)
        {
            var attrHeader = (Win32.ATTRIBUTE_RECORD_HEADER*)attributePtr;

            if (attrHeader->AttributeType == 0xFFFFFFFF) // End marker
                break;

            ParseAttribute(attrHeader, entry, context);

            if (attrHeader->RecordLength == 0)
                break;

            attributePtr += attrHeader->RecordLength;
        }

        if (!string.IsNullOrEmpty(entry.FileName))
        {
            _entriesByFrn[frn] = entry;
        }
    }

    private unsafe void ParseAttribute(Win32.ATTRIBUTE_RECORD_HEADER* attrHeader, SafeMftEntry entry, SafeMftScanContext context)
    {
        switch (attrHeader->AttributeType)
        {
            case Win32.ATTRIBUTE_TYPE_STANDARD_INFORMATION:
                ParseStandardInformation(attrHeader, entry);
                break;

            case Win32.ATTRIBUTE_TYPE_FILE_NAME:
                ParseFileName(attrHeader, entry);
                break;

            case Win32.ATTRIBUTE_TYPE_DATA:
                ParseDataAttribute(attrHeader, entry);
                break;
        }
    }

    private unsafe void ParseStandardInformation(Win32.ATTRIBUTE_RECORD_HEADER* attrHeader, SafeMftEntry entry)
    {
        if (attrHeader->NonResidentFlag != 0)
            return;

        var resAttr = (Win32.RESIDENT_ATTRIBUTE*)attrHeader;
        var stdInfo = (Win32.STANDARD_INFORMATION*)((byte*)attrHeader + resAttr->ValueOffset);

        entry.FileAttributes = stdInfo->FileAttributes;
        entry.CreationTime = DateTime.FromFileTime(stdInfo->CreationTime);
        entry.LastWriteTime = DateTime.FromFileTime(stdInfo->LastModificationTime);
        entry.IsDirectory = (stdInfo->FileAttributes & Win32.FILE_ATTRIBUTE_DIRECTORY) != 0;
    }

    private unsafe void ParseFileName(Win32.ATTRIBUTE_RECORD_HEADER* attrHeader, SafeMftEntry entry)
    {
        if (attrHeader->NonResidentFlag != 0)
            return;

        var resAttr = (Win32.RESIDENT_ATTRIBUTE*)attrHeader;
        var fileName = (Win32.FILE_NAME_ATTRIBUTE*)((byte*)attrHeader + resAttr->ValueOffset);

        if (fileName->FileNameType == 1 || string.IsNullOrEmpty(entry.FileName))
        {
            var namePtr = (char*)((byte*)fileName + Marshal.SizeOf<Win32.FILE_NAME_ATTRIBUTE>());
            entry.FileName = new string(namePtr, 0, fileName->FileNameLength);
            entry.ParentFileReferenceNumber = fileName->ParentDirectory;

            if (entry.Size == 0)
            {
                entry.Size = (long)fileName->DataSize;
                entry.AllocatedSize = (long)fileName->AllocatedSize;
            }
        }
    }

    private unsafe void ParseDataAttribute(Win32.ATTRIBUTE_RECORD_HEADER* attrHeader, SafeMftEntry entry)
    {
        if (entry.IsDirectory)
            return;

        if (attrHeader->NonResidentFlag == 0)
        {
            var resAttr = (Win32.RESIDENT_ATTRIBUTE*)attrHeader;
            entry.Size = resAttr->ValueLength;
            entry.AllocatedSize = entry.Size;
        }
        else
        {
            var nonResAttr = (Win32.NON_RESIDENT_ATTRIBUTE*)attrHeader;
            entry.Size = (long)nonResAttr->DataSize;
            entry.AllocatedSize = (long)nonResAttr->AllocatedSize;

            if (nonResAttr->CompressionUnit != 0)
            {
                entry.CompressedSize = (long)nonResAttr->CompressedSize;
            }
        }
    }

    private void BuildMftTree(SafeMftScanContext context)
    {
        foreach (var entry in _entriesByFrn.Values)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (entry.ParentFileReferenceNumber == 5 || // Root directory FRN
                entry.ParentFileReferenceNumber == entry.FileReferenceNumber)
            {
                _rootEntries.Add(entry);
            }
            else if (_entriesByFrn.TryGetValue(entry.ParentFileReferenceNumber, out var parent))
            {
                parent.Children.Add(entry);
                entry.Parent = parent;
            }
        }

        // Build full paths for all entries
        foreach (var rootEntry in _rootEntries)
        {
            BuildFullPaths(rootEntry, context.DrivePath);
        }

        // Aggregate directory sizes
        foreach (var rootEntry in _rootEntries)
        {
            AggregateDirectorySize(rootEntry);
        }
    }

    // Fix the FullPath issue by building proper full paths
    private void BuildFullPaths(SafeMftEntry entry, string rootPath)
    {
        if (entry.Parent == null)
        {
            entry.FullPath = Path.Combine(rootPath, entry.FileName);
        }
        else
        {
            entry.FullPath = Path.Combine(entry.Parent.FullPath ?? rootPath, entry.FileName);
        }

        foreach (var child in entry.Children)
        {
            BuildFullPaths(child, rootPath);
        }
    }

    private long AggregateDirectorySize(SafeMftEntry entry)
    {
        if (!entry.IsDirectory)
            return entry.Size;

        long totalSize = 0;
        foreach (var child in entry.Children)
        {
            totalSize += AggregateDirectorySize(child);
        }

        entry.Size = totalSize;
        return totalSize;
    }

    private ScanResult BuildScanResult(SafeMftScanContext context)
    {
        var driveInfo = new DriveInfo(context.DrivePath);
        var driveStats = new DriveStats(
            context.DrivePath.TrimEnd('\\').TrimEnd(':'),
            driveInfo.TotalSize,
            driveInfo.TotalSize - driveInfo.AvailableFreeSpace,
            driveInfo.AvailableFreeSpace
        );

        var driveRoot = _rootEntries.FirstOrDefault(e => e.FileReferenceNumber == 5);
        driveRoot ??= _rootEntries.FirstOrDefault();

        var rootNode = driveRoot != null ? ConvertToTreeNode(driveRoot) : new TreeNode("Drive", context.DrivePath, true, 0, []);

        var topFiles = GetTopFiles(rootNode, 20);
        var byExtension = GetExtensionGroups(rootNode);

        return new ScanResult(driveStats, rootNode, topFiles, byExtension);
    }

    // Fixed ConvertToTreeNode to use proper FullPath
    private TreeNode ConvertToTreeNode(SafeMftEntry entry)
    {
        var children = entry.Children
            .OrderByDescending(c => c.Size)
            .Select(c => ConvertToTreeNode(c))
            .ToArray();

        return new TreeNode(entry.FileName, entry.FullPath ?? entry.FileName, entry.IsDirectory, entry.Size, children);
    }

    private static TopItem[] GetTopFiles(TreeNode root, int count)
    {
        var list = new List<TopItem>();
        Traverse(root);
        return list
            .OrderByDescending(x => x.SizeBytes)
            .Take(count)
            .ToArray();

        void Traverse(TreeNode node)
        {
            if (!node.IsDirectory)
            {
                list.Add(new TopItem(node.Name, node.FullPath, node.SizeBytes, false));
                return;
            }
            foreach (var child in node.Children)
            {
                Traverse(child);
            }
        }
    }

    private static ExtensionGroup[] GetExtensionGroups(TreeNode root)
    {
        var dict = new Dictionary<string, (long size, int count)>(StringComparer.OrdinalIgnoreCase);
        Accumulate(root);
        return dict
            .OrderByDescending(kv => kv.Value.size)
            .Select(kv => new ExtensionGroup(kv.Key, kv.Value.size, kv.Value.count))
            .ToArray();

        void Accumulate(TreeNode node)
        {
            if (!node.IsDirectory)
            {
                var ext = GetExtension(node.Name);
                if (!dict.TryGetValue(ext, out var agg)) agg = (0, 0);
                agg.size += node.SizeBytes;
                agg.count += 1;
                dict[ext] = agg;
                return;
            }
            foreach (var child in node.Children)
            {
                Accumulate(child);
            }
        }

        static string GetExtension(string name)
        {
            var dot = name.LastIndexOf('.');
            return dot > 0 && dot < name.Length - 1 ? name.Substring(dot).ToLowerInvariant() : "<none>";
        }
    }

    private void UpdateProgress(object? state)
    {
        if (state is not SafeMftScanContext context || context.Progress == null)
            return;

        var elapsed = DateTime.UtcNow - context.StartTime;
        var percentComplete = context.TotalRecords > 0 ?
            (double)context.ProcessedCount / context.TotalRecords * 70 + 10 : 10; // Scale to 10-80% for MFT parsing phase

        var progress = new ScanProgress(
            percentComplete,
            context.ProcessedCount,
            elapsed,
            $"Parsing MFT records ({context.ProcessedCount:N0}/{context.TotalRecords:N0})"
        );

        Dispatcher.UIThread.Post(() => context.Progress.Report(progress));
    }
}

internal class SafeMftScanContext
{
    public required string DrivePath { get; init; }
    public IProgress<ScanProgress>? Progress { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public DateTime StartTime { get; init; }
    public int ProcessedCount { get; set; }
    public int TotalRecords { get; set; }
    public Win32.NTFS_VOLUME_DATA_BUFFER VolumeData { get; set; }
}

internal class SafeMftEntry
{
    public ulong FileReferenceNumber { get; set; }
    public ulong ParentFileReferenceNumber { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? FullPath { get; set; }
    public uint FileAttributes { get; set; }
    public bool IsDirectory { get; set; }
    public bool InUse { get; set; }
    public long Size { get; set; }
    public long AllocatedSize { get; set; }
    public long CompressedSize { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime LastWriteTime { get; set; }
    public SafeMftEntry? Parent { get; set; }
    public List<SafeMftEntry> Children { get; } = new();
}