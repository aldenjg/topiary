using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Topiary.App.Interop;

namespace Topiary.App.Scanner;

/// <summary>
/// Portable high-performance directory scanner for non-Windows systems or non-NTFS volumes.
/// Uses optimized single-pass enumeration with minimal allocations.
/// On Windows, uses FindFirstFileEx with large fetch optimization.
/// On other platforms, uses System.IO.Enumeration with custom transforms.
/// </summary>
public sealed class DirectoryScanSource : IScanSource
{
    private const int ESTIMATED_FILES_PER_DIRECTORY = 20;
    private const int BATCH_SIZE = 1000;
    private const int CHANNEL_CAPACITY = 8192;
    
    public string Description => OperatingSystem.IsWindows() 
        ? "Windows Directory Enumeration (FindFirstFileEx)" 
        : "Cross-Platform Directory Enumeration";
    
    /// <summary>
    /// Scan directory tree using single-pass enumeration for optimal performance.
    /// No FileInfo allocations, no double directory traversals.
    /// </summary>
    public async IAsyncEnumerable<Entry> ScanAsync(string volumeRoot, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Simplified approach: just scan recursively without complex channels
        await foreach (var entry in ScanDirectoryRecursivelyAsync(volumeRoot, cancellationToken))
        {
            yield return entry;
        }
    }
    
    private static async IAsyncEnumerable<Entry> ScanDirectoryRecursivelyAsync(
        string directoryPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var subdirectories = new List<string>();
        
        // First, yield entries from the current directory
        var scanMethod = OperatingSystem.IsWindows() 
            ? ScanDirectoryWindowsAsync(directoryPath, subdirectories, cancellationToken)
            : ScanDirectoryCrossPlatformAsync(directoryPath, subdirectories, cancellationToken);
            
        await foreach (var entry in WithErrorHandling(scanMethod, directoryPath))
        {
            yield return entry;
        }
        
        // Then recursively scan subdirectories
        foreach (var subdir in subdirectories)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;
                
            await foreach (var entry in ScanDirectoryRecursivelyAsync(subdir, cancellationToken))
            {
                yield return entry;
            }
        }
    }
    
    private static async IAsyncEnumerable<Entry> WithErrorHandling(
        IAsyncEnumerable<Entry> source,
        string directoryPath)
    {
        var results = new List<Entry>();
        
        try
        {
            await foreach (var entry in source)
            {
                results.Add(entry);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access - results will be empty
        }
        catch (DirectoryNotFoundException)
        {
            // Skip directories that no longer exist - results will be empty
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            // Log but continue - results will be partial
            Console.WriteLine($"Error processing {directoryPath}: {ex.Message}");
        }
        
        // Now yield the collected results (no try-catch around yield)
        foreach (var entry in results)
        {
            yield return entry;
        }
    }
    
    public ValueTask<long> GetEstimatedEntryCountAsync(string volumeRoot, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(EstimateSynchronously(volumeRoot));
    }
    
    private static long EstimateSynchronously(string volumeRoot)
    {
        try
        {
            // Quick estimate by sampling a few directories
            var sampleDirectories = new List<string> { volumeRoot };
            var estimatedFiles = 0L;
            var estimatedDirectories = 1L;
            var maxSample = 10; // Don't sample too many for estimation
            
            for (int i = 0; i < sampleDirectories.Count && i < maxSample; i++)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(sampleDirectories[i]);
                    var files = 0;
                    var subdirs = 0;
                    
                    foreach (var entry in dirInfo.EnumerateFileSystemInfos())
                    {
                        if (entry is DirectoryInfo)
                        {
                            subdirs++;
                            if (sampleDirectories.Count < maxSample * 3)
                                sampleDirectories.Add(entry.FullName);
                        }
                        else
                        {
                            files++;
                        }
                        
                        if (files + subdirs > 1000) // Don't spend too long on huge directories
                            break;
                    }
                    
                    estimatedFiles += files;
                    estimatedDirectories += subdirs;
                }
                catch
                {
                    // Skip directories we can't access
                }
            }
            
            // Extrapolate based on sample
            var avgFilesPerDir = estimatedDirectories > 0 ? estimatedFiles / estimatedDirectories : ESTIMATED_FILES_PER_DIRECTORY;
            var totalEstimate = estimatedDirectories * Math.Max(1, avgFilesPerDir);
            
            return Math.Max(1000, totalEstimate);
        }
        catch
        {
            return 10000L; // Reasonable default if estimation fails
        }
    }
    
    private static async IAsyncEnumerable<Entry> ScanDirectoryWindowsAsync(
        string directoryPath,
        List<string> subdirectories,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var searchPattern = Path.Combine(directoryPath, "*");
        var findHandle = Win32.FindFirstFileExW(
            searchPattern,
            Win32.FINDEX_INFO_LEVELS.FindExInfoBasic,
            out var findData,
            Win32.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
            IntPtr.Zero,
            Win32.FIND_FIRST_EX_LARGE_FETCH);
        
        if (findHandle == IntPtr.Zero || findHandle == new IntPtr(-1))
        {
            yield break; // Directory empty or inaccessible
        }
        
        try
        {
            var processedCount = 0;
            
            do
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;
                
                if (findData.cFileName == "." || findData.cFileName == "..")
                    continue;
                
                var fullPath = Path.Combine(directoryPath, findData.cFileName);
                var isDirectory = (findData.dwFileAttributes & 0x10) != 0; // FILE_ATTRIBUTE_DIRECTORY
                
                // Generate a path-based file ID for hierarchy building
                var fileId = GeneratePathBasedFileId(fullPath);
                var parentFileId = GeneratePathBasedFileId(directoryPath);
                
                var entry = new Entry(
                    fileId: fileId,
                    parentFileId: parentFileId,
                    attributes: Win32.ConvertAttributes(findData.dwFileAttributes),
                    size: findData.FileSize,
                    allocationSize: ((findData.FileSize + 4095) / 4096) * 4096, // Approximate cluster size
                    creationTime: findData.CreationFileTime,
                    lastWriteTime: findData.LastWriteFileTime,
                    name: findData.cFileName.AsMemory(),
                    linkCount: 1);
                
                yield return entry;
                
                if (isDirectory)
                {
                    subdirectories.Add(fullPath);
                }
                
                processedCount++;
                if (processedCount % 100 == 0)
                {
                    await Task.Yield(); // Yield occasionally to prevent UI blocking
                }
                
            } while (Win32.FindNextFileW(findHandle, out findData));
        }
        finally
        {
            Win32.FindClose(findHandle);
        }
    }
    
    private static async IAsyncEnumerable<Entry> ScanDirectoryCrossPlatformAsync(
        string directoryPath,
        List<string> subdirectories,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var options = new EnumerationOptions
        {
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0, // Don't skip anything
            RecurseSubdirectories = false,
            BufferSize = 16384 // Larger buffer for better performance
        };
        
        var processedCount = 0;
        
        var enumerator = new FileSystemEnumerable<(Entry entry, bool isDirectory)>(
            directoryPath,
            (ref FileSystemEntry entry) =>
            {
                var fullPath = entry.ToFullPath();
                var isDirectory = entry.IsDirectory;
                
                var fileId = GeneratePathBasedFileId(fullPath);
                var parentFileId = GeneratePathBasedFileId(directoryPath);
                
                var scanEntry = new Entry(
                    fileId: fileId,
                    parentFileId: parentFileId,
                    attributes: (Scanner.FileAttributes)(uint)entry.Attributes,
                    size: entry.Length,
                    allocationSize: ((entry.Length + 4095) / 4096) * 4096,
                    creationTime: entry.CreationTimeUtc.ToFileTime(),
                    lastWriteTime: entry.LastWriteTimeUtc.ToFileTime(),
                    name: entry.FileName.ToString().AsMemory(),
                    linkCount: 1);
                
                return (scanEntry, isDirectory);
            }, options);
        
        foreach (var (entry, isDirectory) in enumerator)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;
                
            yield return entry;
            
            if (isDirectory)
            {
                subdirectories.Add(Path.Combine(directoryPath, entry.Name.ToString()));
            }
            
            processedCount++;
            if (processedCount % 100 == 0)
            {
                await Task.Yield(); // Yield occasionally to prevent UI blocking
            }
        }
    }
    
    private static UInt128 GeneratePathBasedFileId(string path)
    {
        // Generate a deterministic file ID based on the normalized path
        var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalizedPath));
        
        // Use first 16 bytes of SHA256 hash as UInt128
        var span = hash.AsSpan(0, 16);
        return new UInt128(
            BinaryPrimitives.ReadUInt64LittleEndian(span[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(span[0..8])
        );
    }
}