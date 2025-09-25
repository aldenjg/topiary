using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Topiary.App.Scanner;

/// <summary>
/// Abstract interface for scanning file system entries.
/// Implementations provide different strategies for enumerating files.
/// </summary>
public interface IScanSource
{
    /// <summary>
    /// Enumerate all entries in the specified volume root.
    /// Returns entries in arbitrary order (hierarchy is built by consumer).
    /// </summary>
    /// <param name="volumeRoot">Volume to scan (e.g., "C:\" or "/").</param>
    /// <param name="cancellationToken">Cancellation token for early termination.</param>
    /// <returns>Async enumerable of file system entries.</returns>
    IAsyncEnumerable<Entry> ScanAsync(string volumeRoot, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get estimated total number of entries for progress calculation.
    /// May return 0 if estimation is not available.
    /// </summary>
    /// <param name="volumeRoot">Volume to estimate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Estimated entry count, or 0 if unknown.</returns>
    ValueTask<long> GetEstimatedEntryCountAsync(string volumeRoot, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Human-readable description of this scan source strategy.
    /// </summary>
    string Description { get; }
}

/// <summary>
/// Factory for creating the optimal scan source for a given volume.
/// </summary>
public static class ScanSourceFactory
{
    /// <summary>
    /// Create the optimal scan source for the specified volume.
    /// Uses Windows MFT scanning for NTFS, falls back to directory enumeration otherwise.
    /// </summary>
    /// <param name="volumeRoot">Volume root path (e.g., "C:\\")</param>
    /// <returns>Optimal scan source for this volume</returns>
    public static IScanSource CreateOptimal(string volumeRoot)
    {
        // TEMPORARY: Always use DirectoryScanSource until MFT issues are resolved
        // TODO: Re-enable MFT scanning after fixing permission/access issues
        return new DirectoryScanSource();
        
        // Original logic (commented out for now):
        // Check if we're on Windows with NTFS volume
        // if (OperatingSystem.IsWindows() && IsNtfsVolume(volumeRoot))
        // {
        //     return new MftScanSource();
        // }
        // 
        // // Use portable fallback for all other cases
        // return new DirectoryScanSource();
    }
    
    private static bool IsNtfsVolume(string volumeRoot)
    {
        try
        {
            var driveInfo = new System.IO.DriveInfo(volumeRoot);
            return driveInfo.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}