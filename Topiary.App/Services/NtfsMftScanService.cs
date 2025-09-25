using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Topiary.App.Interop;
using Topiary.App.Models;
using Avalonia.Threading;

namespace Topiary.App.Services;

public class NtfsMftScanService : IScanService
{
    private readonly Dictionary<ulong, MftEntry> _entriesByFrn = new();
    private readonly List<MftEntry> _rootEntries = new();
    private Timer? _progressTimer;

    public async Task<string[]> GetAvailableDrivesAsync()
    {
        return await Task.FromResult(DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => d.Name)
            .ToArray());
    }

    public async Task<ScanResult> ScanDriveAsync(string drivePath, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("NTFS MFT scanning requires Windows");
        }

        var driveInfo = new DriveInfo(drivePath);
        if (!string.Equals(driveInfo.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            throw new PlatformNotSupportedException($"WizTree-level scanning requires NTFS. Drive {drivePath} is {driveInfo.DriveFormat}");
        }

        var context = new MftScanContext
        {
            DrivePath = drivePath,
            Progress = progress,
            CancellationToken = cancellationToken,
            StartTime = DateTime.UtcNow
        };

        try
        {
            _progressTimer = new Timer(UpdateProgress, context, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

            // WizTree-level performance: Direct MFT parsing
            ReportProgress(context, "Accessing NTFS Master File Table...");
            
            // Phase 1: Read NTFS volume metadata
            await Task.Run(() => ReadVolumeInfo(context), cancellationToken);
            
            // Phase 2: Parse MFT records directly (WizTree's secret sauce)
            ReportProgress(context, "Parsing MFT records at maximum speed...");
            await Task.Run(() => ParseMftRecords(context), cancellationToken);
            
            // Phase 3: Build FRN tree from FILE_NAME attributes  
            ReportProgress(context, "Building directory tree from File Reference Numbers...");
            await Task.Run(() => BuildMftTree(context), cancellationToken);

            return await Task.Run(() => BuildScanResult(context), cancellationToken);
        }
        finally
        {
            _progressTimer?.Dispose();
            _progressTimer = null;
        }
    }

    private unsafe void ReadVolumeInfo(MftScanContext context)
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

    private unsafe void ParseMftRecords(MftScanContext context)
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
        var batchSize = Math.Min(1024, context.TotalRecords); // Process 1024 records at a time
        var buffer = Marshal.AllocHGlobal((int)(batchSize * recordSize));
        
        try
        {
            for (ulong frn = 0; frn < (ulong)context.TotalRecords; frn += (ulong)batchSize)
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
                    
                    var inputPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Win32.NTFS_FILE_RECORD_INPUT_BUFFER>());
                    var outputPtr = Marshal.AllocHGlobal(1024 + (int)recordSize); // Header + record
                    
                    try
                    {
                        Marshal.StructureToPtr(inputBuffer, inputPtr, false);
                        
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
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(inputPtr);
                        Marshal.FreeHGlobal(outputPtr);
                    }
                }
                
                context.ProcessedCount = (int)frn;
                
                // Yield every batch
                Thread.Yield();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private unsafe void ParseFileRecord(IntPtr outputBuffer, int bufferSize, ulong frn, MftScanContext context)
    {
        if (bufferSize < Marshal.SizeOf<Win32.NTFS_FILE_RECORD_OUTPUT_BUFFER>())
            return;
            
        var outputHeader = Marshal.PtrToStructure<Win32.NTFS_FILE_RECORD_OUTPUT_BUFFER>(outputBuffer);
        
        // Skip to the actual file record
        var fileRecordPtr = (byte*)outputBuffer.ToPointer() + Marshal.SizeOf<Win32.NTFS_FILE_RECORD_OUTPUT_BUFFER>();
        var fileRecord = (Win32.FILE_RECORD_SEGMENT_HEADER*)fileRecordPtr;
        
        // Verify FILE signature
        if (fileRecord->Signature != 0x454C4946) // "FILE"
            return;
            
        // Skip deleted records
        if ((fileRecord->Flags & 0x01) == 0)
            return;
            
        var entry = new MftEntry
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

    private unsafe void ParseAttribute(Win32.ATTRIBUTE_RECORD_HEADER* attrHeader, MftEntry entry, MftScanContext context)
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

    private unsafe void ParseStandardInformation(Win32.ATTRIBUTE_RECORD_HEADER* attrHeader, MftEntry entry)
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

    private unsafe void ParseFileName(Win32.ATTRIBUTE_RECORD_HEADER* attrHeader, MftEntry entry)
    {
        if (attrHeader->NonResidentFlag != 0)
            return;
            
        var resAttr = (Win32.RESIDENT_ATTRIBUTE*)attrHeader;
        var fileName = (Win32.FILE_NAME_ATTRIBUTE*)((byte*)attrHeader + resAttr->ValueOffset);
        
        // Use Win32 name (type 1) if available, otherwise POSIX (type 0)
        if (fileName->FileNameType == 1 || string.IsNullOrEmpty(entry.FileName))
        {
            var namePtr = (char*)((byte*)fileName + Marshal.SizeOf<Win32.FILE_NAME_ATTRIBUTE>());
            entry.FileName = new string(namePtr, 0, fileName->FileNameLength);
            entry.ParentFileReferenceNumber = fileName->ParentDirectory;
            
            // Size information from FILE_NAME attribute (may be more accurate than $DATA for resident files)
            if (entry.Size == 0)
            {
                entry.Size = (long)fileName->DataSize;
                entry.AllocatedSize = (long)fileName->AllocatedSize;
            }
        }
    }

    private unsafe void ParseDataAttribute(Win32.ATTRIBUTE_RECORD_HEADER* attrHeader, MftEntry entry)
    {
        if (entry.IsDirectory)
            return; // Directories don't have meaningful $DATA size
            
        if (attrHeader->NonResidentFlag == 0)
        {
            // Resident data - small files stored directly in MFT
            var resAttr = (Win32.RESIDENT_ATTRIBUTE*)attrHeader;
            entry.Size = resAttr->ValueLength;
            entry.AllocatedSize = entry.Size; // Resident data isn't allocated separately
        }
        else
        {
            // Non-resident data - file data stored in clusters
            var nonResAttr = (Win32.NON_RESIDENT_ATTRIBUTE*)attrHeader;
            entry.Size = (long)nonResAttr->DataSize;
            entry.AllocatedSize = (long)nonResAttr->AllocatedSize;
            
            // Handle compressed files
            if (nonResAttr->CompressionUnit != 0)
            {
                entry.CompressedSize = (long)nonResAttr->CompressedSize;
            }
        }
    }

    private void BuildMftTree(MftScanContext context)
    {
        // Link children to parents using FRN relationships from FILE_NAME attributes
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
            
            if (context.ProcessedCount % 10000 == 0)
            {
                Thread.Yield();
            }
        }

        // Aggregate directory sizes bottom-up
        foreach (var rootEntry in _rootEntries)
        {
            AggregateDirectorySize(rootEntry);
        }
    }

    private long AggregateDirectorySize(MftEntry entry)
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

    private ScanResult BuildScanResult(MftScanContext context)
    {
        var driveInfo = new DriveInfo(context.DrivePath);
        var driveStats = new DriveStats(
            context.DrivePath.TrimEnd('\\').TrimEnd(':'),
            driveInfo.TotalSize,
            driveInfo.TotalSize - driveInfo.AvailableFreeSpace,
            driveInfo.AvailableFreeSpace
        );

        // Find the root directory (FRN 5 is typically the root)
        var driveRoot = _rootEntries.FirstOrDefault(e => e.FileReferenceNumber == 5);
        driveRoot ??= _rootEntries.FirstOrDefault();
        
        var rootNode = driveRoot != null ? ConvertToTreeNode(driveRoot) : new TreeNode("Drive", context.DrivePath, true, 0, []);

        // Build top files and extension groups for insights
        var topFiles = GetTopFiles(rootNode, 20);
        var byExtension = GetExtensionGroups(rootNode);

        return new ScanResult(driveStats, rootNode, topFiles, byExtension);
    }

    private TreeNode ConvertToTreeNode(MftEntry entry)
    {
        var children = entry.Children
            .OrderByDescending(c => c.Size)
            .Select(ConvertToTreeNode)
            .ToArray();
        
        return new TreeNode(entry.FileName, entry.FileName, entry.IsDirectory, entry.Size, children);
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
        if (state is not MftScanContext context || context.Progress == null)
            return;

        var elapsed = DateTime.UtcNow - context.StartTime;
        var percentComplete = context.TotalRecords > 0 ? 
            (double)context.ProcessedCount / context.TotalRecords * 100 : 0;
            
        var progress = new ScanProgress(
            percentComplete,
            context.ProcessedCount,
            elapsed,
            $"Parsing MFT records ({context.ProcessedCount:N0}/{context.TotalRecords:N0})"
        );

        Dispatcher.UIThread.Post(() => context.Progress.Report(progress));
    }

    private static void ReportProgress(MftScanContext context, string message)
    {
        if (context.Progress == null) return;
        
        var progress = new ScanProgress(0, 0, DateTime.UtcNow - context.StartTime, message);
        context.Progress.Report(progress);
    }
}

internal class MftScanContext
{
    public required string DrivePath { get; init; }
    public IProgress<ScanProgress>? Progress { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public DateTime StartTime { get; init; }
    public int ProcessedCount { get; set; }
    public int TotalRecords { get; set; }
    public Win32.NTFS_VOLUME_DATA_BUFFER VolumeData { get; set; }
}

internal class MftEntry
{
    public ulong FileReferenceNumber { get; set; }
    public ulong ParentFileReferenceNumber { get; set; }
    public string FileName { get; set; } = string.Empty;
    public uint FileAttributes { get; set; }
    public bool IsDirectory { get; set; }
    public bool InUse { get; set; }
    public long Size { get; set; }
    public long AllocatedSize { get; set; }
    public long CompressedSize { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime LastWriteTime { get; set; }
    public MftEntry? Parent { get; set; }
    public List<MftEntry> Children { get; } = new();
}