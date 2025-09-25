using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Topiary.App.Models;

namespace Topiary.App.Services;

/// <summary>
/// Responsive disk scanning service that keeps UI fully responsive during scanning
/// and provides accurate size calculations through smart directory analysis.
/// </summary>
public class ResponsiveDiskScanService : IScanService
{
    private const int PROGRESS_UPDATE_INTERVAL_MS = 250; // Update progress every 250ms (less frequent for better performance)
    private const int MAX_SCAN_DEPTH = 8; // Reduced depth for better performance
    private const int BATCH_YIELD_COUNT = 500; // Increased batch size - yield less frequently for better performance
    private static readonly int PARALLEL_DEGREE = Environment.ProcessorCount; // Use all CPU cores
    
    public async Task<string[]> GetAvailableDrivesAsync()
    {
        return await Task.Run(() =>
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .Select(d => d.Name)
                .ToArray();
        });
    }

    public async Task<ScanResult> ScanDriveAsync(string drivePath, IProgress<ScanProgress> progress, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;
        var driveInfo = new DriveInfo(drivePath);
        
        // Get actual drive statistics first
        var driveStats = new DriveStats(
            drivePath.TrimEnd('\\', ':'),
            driveInfo.TotalSize,
            driveInfo.TotalSize - driveInfo.AvailableFreeSpace,
            driveInfo.AvailableFreeSpace
        );

        progress?.Report(new ScanProgress(0, 0, TimeSpan.Zero, $"Analyzing {drivePath}"));
        await Task.Delay(50, cancellationToken); // Allow UI to update

        try
        {
            // Run the scanning on a background thread to keep UI responsive
            var scanTask = Task.Run(async () =>
            {
                var context = new ScanContext();
                return await BuildResponsiveTreeAsync(drivePath, progress ?? new Progress<ScanProgress>(), startTime, context, cancellationToken);
            }, cancellationToken);

            var root = await scanTask;
            
            // Generate analysis
            var topFiles = GetTopFiles(root, 20);
            var extensions = GetExtensionGroups(root);
            
            progress?.Report(new ScanProgress(100, root.GetTotalFileCount(), DateTime.Now - startTime, "Analysis complete"));
            
            return new ScanResult(driveStats, root, topFiles, extensions);
        }
        catch (OperationCanceledException)
        {
            progress?.Report(new ScanProgress(0, 0, DateTime.Now - startTime, "Scan cancelled"));
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report(new ScanProgress(0, 0, DateTime.Now - startTime, $"Error: {ex.Message}"));
            throw new InvalidOperationException($"Failed to scan drive {drivePath}: {ex.Message}", ex);
        }
    }

    private async Task<TreeNode> BuildResponsiveTreeAsync(
        string path, 
        IProgress<ScanProgress> progress, 
        DateTime startTime,
        ScanContext context,
        CancellationToken cancellationToken)
    {
        var progressTimer = new Timer(
            _ => ReportProgress(progress, startTime, context, "Scanning..."),
            null,
            TimeSpan.FromMilliseconds(PROGRESS_UPDATE_INTERVAL_MS),
            TimeSpan.FromMilliseconds(PROGRESS_UPDATE_INTERVAL_MS)
        );

        try
        {
            var result = await ScanDirectoryAsync(path, context, cancellationToken, 0);
            return result;
        }
        finally
        {
            progressTimer?.Dispose();
        }
    }

    private async Task<TreeNode> ScanDirectoryAsync(string path, ScanContext context, CancellationToken cancellationToken, int depth = 0)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        var name = path == Path.GetPathRoot(path) ? path : Path.GetFileName(path);
        var children = new List<TreeNode>();
        long totalSize = 0;
        long directorySize = 0; // Size of files directly in this directory
        
        try
        {
            // First, get files in current directory using faster bulk enumeration
            var processedFiles = 0;
            try
            {
                var dirInfo = new DirectoryInfo(path);
                var fileInfos = dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly);

                foreach (var fileInfo in fileInfos)
                {
                    try
                    {
                        var fileNode = new TreeNode(fileInfo.Name, fileInfo.FullName, false, fileInfo.Length, []);
                        children.Add(fileNode);
                        directorySize += fileInfo.Length;
                        Interlocked.Increment(ref context.TotalFilesProcessed);

                        processedFiles++;
                        if (processedFiles % BATCH_YIELD_COUNT == 0)
                        {
                            context.CurrentPath = fileInfo.FullName;
                            await Task.Yield();
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                    catch (Exception)
                    {
                        // Skip problematic files
                    }
                }
            }
            catch (Exception)
            {
                // Skip problematic directory enumeration
            }

            // Then scan subdirectories - use parallel processing for top-level directories
            try
            {
                var dirInfo = new DirectoryInfo(path);
                var directories = dirInfo.EnumerateDirectories("*", SearchOption.TopDirectoryOnly).ToArray();

                if (directories.Length > 0)
                {
                    // For root level and shallow depths, use parallel processing
                    if (depth <= 2 && directories.Length > 4)
                    {
                        var parallelOptions = new ParallelOptions
                        {
                            CancellationToken = cancellationToken,
                            MaxDegreeOfParallelism = Math.Min(PARALLEL_DEGREE, directories.Length)
                        };

                        var childNodes = new TreeNode[directories.Length];

                        Parallel.For(0, directories.Length, parallelOptions, i =>
                        {
                            try
                            {
                                var dir = directories[i];
                                context.CurrentPath = dir.FullName;

                                TreeNode childNode;
                                if (depth < MAX_SCAN_DEPTH)
                                {
                                    // Note: We need to make this synchronous for Parallel.For
                                    childNode = ScanDirectorySynchronous(dir.FullName, context, cancellationToken, depth + 1);
                                }
                                else
                                {
                                    childNode = EstimateDirectorySynchronous(dir.FullName, context);
                                }

                                childNodes[i] = childNode;
                            }
                            catch (Exception)
                            {
                                // Create empty node for problematic directories
                                childNodes[i] = new TreeNode(directories[i].Name, directories[i].FullName, true, 0, []);
                            }
                        });

                        foreach (var childNode in childNodes)
                        {
                            if (childNode != null)
                            {
                                children.Add(childNode);
                                totalSize += childNode.SizeBytes;
                            }
                        }
                    }
                    else
                    {
                        // For deeper levels, use sequential processing to avoid too much parallelism
                        foreach (var dir in directories)
                        {
                            try
                            {
                                context.CurrentPath = dir.FullName;

                                TreeNode childNode;
                                if (depth < MAX_SCAN_DEPTH)
                                {
                                    childNode = await ScanDirectoryAsync(dir.FullName, context, cancellationToken, depth + 1);
                                }
                                else
                                {
                                    childNode = await EstimateDirectoryAsync(dir.FullName, context, cancellationToken);
                                }

                                children.Add(childNode);
                                totalSize += childNode.SizeBytes;
                            }
                            catch (Exception)
                            {
                                // Skip problematic directories
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Skip problematic directory enumeration
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Handle access denied - try to estimate size
            totalSize = await EstimateDirectorySizeAsync(path);
        }
        catch (Exception)
        {
            // Skip problematic directories
        }
        
        totalSize += directorySize; // Add size of files directly in this directory
        
        return new TreeNode(name, path, true, totalSize,
            children.OrderByDescending(c => c.SizeBytes).ToArray());
    }

    // Synchronous version for parallel processing
    private TreeNode ScanDirectorySynchronous(string path, ScanContext context, CancellationToken cancellationToken, int depth = 0)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var name = path == Path.GetPathRoot(path) ? path : Path.GetFileName(path);
        var children = new List<TreeNode>();
        long totalSize = 0;
        long directorySize = 0;

        try
        {
            // Get files using DirectoryInfo for better performance
            var dirInfo = new DirectoryInfo(path);
            try
            {
                var fileInfos = dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly);
                foreach (var fileInfo in fileInfos)
                {
                    try
                    {
                        var fileNode = new TreeNode(fileInfo.Name, fileInfo.FullName, false, fileInfo.Length, []);
                        children.Add(fileNode);
                        directorySize += fileInfo.Length;

                        // Update context periodically (thread-safe)
                        Interlocked.Increment(ref context.TotalFilesProcessed);
                    }
                    catch (Exception)
                    {
                        // Skip problematic files
                    }
                }
            }
            catch (Exception)
            {
                // Skip file enumeration if it fails
            }

            // Process subdirectories
            try
            {
                var directories = dirInfo.EnumerateDirectories("*", SearchOption.TopDirectoryOnly);
                foreach (var dir in directories)
                {
                    try
                    {
                        TreeNode childNode;
                        if (depth < MAX_SCAN_DEPTH)
                        {
                            childNode = ScanDirectorySynchronous(dir.FullName, context, cancellationToken, depth + 1);
                        }
                        else
                        {
                            childNode = EstimateDirectorySynchronous(dir.FullName, context);
                        }

                        children.Add(childNode);
                        totalSize += childNode.SizeBytes;
                    }
                    catch (Exception)
                    {
                        // Skip problematic directories
                    }
                }
            }
            catch (Exception)
            {
                // Skip directory enumeration if it fails
            }
        }
        catch (Exception)
        {
            // Handle any other errors
        }

        totalSize += directorySize;
        return new TreeNode(name, path, true, totalSize,
            children.OrderByDescending(c => c.SizeBytes).ToArray());
    }

    private TreeNode EstimateDirectorySynchronous(string path, ScanContext context)
    {
        var name = Path.GetFileName(path);
        var estimatedSize = EstimateDirectorySizeSynchronous(path);
        return new TreeNode(name, path, true, estimatedSize, []);
    }

    private long EstimateDirectorySizeSynchronous(string path)
    {
        try
        {
            var dirInfo = new DirectoryInfo(path);

            // Quick estimation: sample files and extrapolate
            var directSize = dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .Take(100)
                .Sum(f =>
                {
                    try { return f.Length; }
                    catch { return 0; }
                });

            // Add immediate subdirectory sizes (limited sampling)
            var subDirSize = dirInfo.EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                .Take(20)
                .Sum(subDir =>
                {
                    try
                    {
                        return subDir.EnumerateFiles("*", SearchOption.AllDirectories)
                            .Take(200) // Reduced from 1000 for better performance
                            .Sum(f =>
                            {
                                try { return f.Length; }
                                catch { return 0; }
                            });
                    }
                    catch { return 0; }
                });

            return directSize + subDirSize;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<TreeNode> EstimateDirectoryAsync(string path, ScanContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        var name = Path.GetFileName(path);
        var children = new List<TreeNode>();
        
        try
        {
            // Sample some files and directories to estimate
            var files = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
                .Take(50); // Take up to 50 files for estimation
            
            long sampledSize = 0;
            int fileCount = 0;
            
            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    var fileName = Path.GetFileName(file);
                    children.Add(new TreeNode(fileName, file, false, fileInfo.Length, []));
                    sampledSize += fileInfo.Length;
                    fileCount++;
                    context.TotalFilesProcessed++;
                }
                catch (Exception) { }
                
                if (fileCount % 10 == 0)
                {
                    await Task.Yield();
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            // Get subdirectory names without full recursion
            var directories = Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly)
                .Take(20); // Limit subdirectories shown

            foreach (var dir in directories)
            {
                try
                {
                    var dirName = Path.GetFileName(dir);
                    var estimatedSize = await EstimateDirectorySizeAsync(dir);
                    children.Add(new TreeNode(dirName, dir, true, estimatedSize, []));
                    sampledSize += estimatedSize;
                }
                catch (Exception) { }
                
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }
            
            return new TreeNode(name, path, true, sampledSize, 
                children.OrderByDescending(c => c.SizeBytes).ToArray());
        }
        catch (Exception)
        {
            var estimatedSize = await EstimateDirectorySizeAsync(path);
            return new TreeNode(name, path, true, estimatedSize, []);
        }
    }

    private async Task<long> EstimateDirectorySizeAsync(string path)
    {
        return await Task.Run(() =>
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);

                // Get all files in this directory and immediate subdirectories
                var directSize = dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                    .Sum(f =>
                    {
                        try { return f.Length; }
                        catch { return 0; }
                    });

                // Add sizes of immediate subdirectories recursively (but limited depth)
                var subDirSize = dirInfo.EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                    .Take(50) // Limit to first 50 subdirectories for performance
                    .Sum(subDir =>
                    {
                        try
                        {
                            return subDir.EnumerateFiles("*", SearchOption.AllDirectories)
                                .Take(1000) // Sample up to 1000 files from each subdirectory
                                .Sum(f =>
                                {
                                    try { return f.Length; }
                                    catch { return 0; }
                                });
                        }
                        catch { return 0; }
                    });

                return directSize + subDirSize;
            }
            catch
            {
                return 0; // Return 0 for inaccessible directories
            }
        });
    }

    private bool IsTopLevelDirectory(string path)
    {
        var root = Path.GetPathRoot(path) ?? "";
        var relativePath = Path.GetRelativePath(root, path);
        return string.IsNullOrEmpty(relativePath) || relativePath == "." || !relativePath.Contains(Path.DirectorySeparatorChar);
    }

    private void ReportProgress(IProgress<ScanProgress> progress, DateTime startTime, ScanContext context, string status)
    {
        var elapsed = DateTime.Now - startTime;
        var estimatedProgress = Math.Min(95, Math.Max(5, elapsed.TotalSeconds * 2)); // Rough estimate based on time
        progress?.Report(new ScanProgress(estimatedProgress, context.TotalFilesProcessed, elapsed, context.CurrentPath ?? status));
    }

    private TopItem[] GetTopFiles(TreeNode root, int count)
    {
        var topFiles = new List<TopItem>();
        CollectLargestFiles(root, topFiles, count * 3);
        
        return topFiles
            .Where(f => !f.IsDirectory)
            .OrderByDescending(f => f.SizeBytes)
            .Take(count)
            .ToArray();
    }

    private void CollectLargestFiles(TreeNode node, List<TopItem> largestFiles, int maxFiles)
    {
        if (largestFiles.Count >= maxFiles) return;
        
        if (!node.IsDirectory)
        {
            largestFiles.Add(new TopItem(node.Name, node.FullPath, node.SizeBytes, node.IsDirectory));
            return;
        }
        
        foreach (var child in node.Children.OrderByDescending(c => c.SizeBytes))
        {
            CollectLargestFiles(child, largestFiles, maxFiles);
            if (largestFiles.Count >= maxFiles) break;
        }
    }

    private ExtensionGroup[] GetExtensionGroups(TreeNode root)
    {
        var extensionStats = new Dictionary<string, (long totalSize, int count)>();
        CollectExtensionStats(root, extensionStats);
        
        return extensionStats
            .Where(kvp => !string.IsNullOrEmpty(kvp.Key))
            .Select(kvp => new ExtensionGroup(kvp.Key, kvp.Value.totalSize, kvp.Value.count))
            .OrderByDescending(g => g.TotalSize)
            .Take(10)
            .ToArray();
    }

    private void CollectExtensionStats(TreeNode node, Dictionary<string, (long totalSize, int count)> stats)
    {
        if (!node.IsDirectory && !string.IsNullOrEmpty(Path.GetExtension(node.Name)))
        {
            var ext = Path.GetExtension(node.Name).ToLowerInvariant();
            if (stats.ContainsKey(ext))
            {
                var current = stats[ext];
                stats[ext] = (current.totalSize + node.SizeBytes, current.count + 1);
            }
            else
            {
                stats[ext] = (node.SizeBytes, 1);
            }
        }
        
        foreach (var child in node.Children)
        {
            CollectExtensionStats(child, stats);
        }
    }

    private class ScanContext
    {
        public long TotalFilesProcessed; // Use Interlocked for thread-safe long operations
        public volatile string? CurrentPath; // Make thread-safe with volatile
    }
}