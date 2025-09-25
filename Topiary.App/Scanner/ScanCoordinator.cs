using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Topiary.App.Models;

namespace Topiary.App.Scanner;

/// <summary>
/// High-level coordinator for disk scanning operations.
/// Orchestrates scan source selection, progress reporting, and tree building with bounded concurrency.
/// </summary>
public sealed class ScanCoordinator
{
    private readonly int _maxConcurrency;
    
    public ScanCoordinator(int maxConcurrency = 0)
    {
        _maxConcurrency = maxConcurrency > 0 ? maxConcurrency : Environment.ProcessorCount;
    }
    
    /// <summary>
    /// Scan the specified volume and build a complete directory tree.
    /// Automatically selects the optimal scan source (MFT vs directory enumeration).
    /// </summary>
    /// <param name="volumeRoot">Volume root path (e.g., "C:\\")</param>
    /// <param name="progress">Progress reporting callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complete scan result with drive stats and directory tree</returns>
    public async Task<ScanResult> ScanVolumeAsync(
        string volumeRoot,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var normalizedRoot = Path.GetFullPath(volumeRoot);
        
        // Get drive statistics first
        var driveStats = GetDriveStats(normalizedRoot);
        
        // Create optimal scan source for this volume
        var scanSource = ScanSourceFactory.CreateOptimal(normalizedRoot);
        
        progress?.Report(new ScanProgress(0, 0, TimeSpan.Zero, $"Initializing {scanSource.Description}"));
        
        try
        {
            // Get estimated entry count for progress calculation
            var estimatedEntries = await scanSource.GetEstimatedEntryCountAsync(normalizedRoot, cancellationToken);
            
            // Build tree using streaming approach
            var treeBuilder = new TreeBuilder(normalizedRoot);
            var processedEntries = 0L;
            var lastProgressReport = DateTime.UtcNow;
            const int progressReportIntervalMs = 100; // Report progress every 100ms
            
            // Process entries with bounded concurrency
            await foreach (var entry in scanSource.ScanAsync(normalizedRoot, cancellationToken))
            {
                treeBuilder.OnEntry(in entry);
                
                var currentProcessed = Interlocked.Increment(ref processedEntries);
                
                // Report progress periodically
                var now = DateTime.UtcNow;
                if ((now - lastProgressReport).TotalMilliseconds >= progressReportIntervalMs)
                {
                    lastProgressReport = now;
                    var elapsed = now - startTime;
                    var progressPercent = estimatedEntries > 0 
                        ? Math.Min(95.0, (currentProcessed / (double)estimatedEntries) * 100.0)
                        : Math.Min(95.0, elapsed.TotalSeconds * 2.0); // Fallback time-based estimate
                    
                    progress?.Report(new ScanProgress(progressPercent, currentProcessed, elapsed, "Processing files..."));
                }
                
                // Yield periodically to prevent thread starvation
                if (currentProcessed % 10000 == 0)
                {
                    await Task.Yield();
                }
            }
            
            progress?.Report(new ScanProgress(95, processedEntries, DateTime.UtcNow - startTime, "Building directory tree..."));
            
            // Build the final tree structure
            var rootTree = treeBuilder.BuildTree();
            
            progress?.Report(new ScanProgress(98, processedEntries, DateTime.UtcNow - startTime, "Analyzing largest files..."));
            
            // Generate analysis data
            var topFiles = GetTopFiles(rootTree, 20);
            var extensionGroups = GetExtensionGroups(rootTree);
            
            var finalElapsed = DateTime.UtcNow - startTime;
            progress?.Report(new ScanProgress(100, processedEntries, finalElapsed, 
                $"Completed: {processedEntries:N0} entries in {finalElapsed.TotalSeconds:F1}s"));
            
            return new ScanResult(driveStats, rootTree, topFiles, extensionGroups);
        }
        catch (Exception ex)
        {
            var elapsed = DateTime.UtcNow - startTime;
            progress?.Report(new ScanProgress(0, 0, elapsed, $"Error: {ex.Message}"));
            throw;
        }
    }
    
    /// <summary>
    /// Get available drives that can be scanned.
    /// </summary>
    public async Task<string[]> GetAvailableDrivesAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                return DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                    .Select(d => d.Name)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        });
    }
    
    private static DriveStats GetDriveStats(string volumeRoot)
    {
        try
        {
            var driveInfo = new DriveInfo(volumeRoot);
            var driveLetter = Path.GetPathRoot(volumeRoot)?.TrimEnd('\\', ':') ?? "?";
            
            return new DriveStats(
                driveLetter,
                driveInfo.TotalSize,
                driveInfo.TotalSize - driveInfo.AvailableFreeSpace,
                driveInfo.AvailableFreeSpace);
        }
        catch
        {
            // Return default stats if we can't get drive info
            var driveLetter = Path.GetPathRoot(volumeRoot)?.TrimEnd('\\', ':') ?? "?";
            return new DriveStats(driveLetter, 0, 0, 0);
        }
    }
    
    private static TopItem[] GetTopFiles(TreeNode root, int count)
    {
        var topFiles = new List<TopItem>();
        CollectLargestFiles(root, topFiles, count * 3); // Collect extra to filter files vs directories
        
        return topFiles
            .Where(f => !f.IsDirectory)
            .OrderByDescending(f => f.SizeBytes)
            .Take(count)
            .ToArray();
    }
    
    private static void CollectLargestFiles(TreeNode node, List<TopItem> largestFiles, int maxFiles)
    {
        if (largestFiles.Count >= maxFiles) return;
        
        if (!node.IsDirectory)
        {
            largestFiles.Add(new TopItem(node.Name, node.FullPath, node.SizeBytes, node.IsDirectory));
            return;
        }
        
        // Process children sorted by size to find largest files first
        foreach (var child in node.Children.OrderByDescending(c => c.SizeBytes))
        {
            CollectLargestFiles(child, largestFiles, maxFiles);
            if (largestFiles.Count >= maxFiles) break;
        }
    }
    
    private static ExtensionGroup[] GetExtensionGroups(TreeNode root)
    {
        var extensionStats = new Dictionary<string, (long totalSize, int count)>();
        CollectExtensionStats(root, extensionStats);
        
        return extensionStats
            .Where(kvp => !string.IsNullOrEmpty(kvp.Key))
            .Select(kvp => new ExtensionGroup(kvp.Key, kvp.Value.totalSize, kvp.Value.count))
            .OrderByDescending(g => g.TotalSize)
            .Take(15) // Show top 15 extensions
            .ToArray();
    }
    
    private static void CollectExtensionStats(TreeNode node, Dictionary<string, (long totalSize, int count)> stats)
    {
        if (!node.IsDirectory && !string.IsNullOrEmpty(Path.GetExtension(node.Name)))
        {
            var ext = Path.GetExtension(node.Name).ToLowerInvariant();
            if (stats.TryGetValue(ext, out var current))
            {
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
}