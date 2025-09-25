using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Topiary.App.Models;

namespace Topiary.App.Services;

/// <summary>
/// Performance-adaptive scan service that will intelligently choose the best scanning approach
/// based on drive type, permissions, and system capabilities.
/// 
/// Currently implements intelligent fallback with future NTFS optimization placeholder.
/// </summary>
public class AdaptivePerformanceScanService : IScanService
{
    private readonly ILogger<AdaptivePerformanceScanService> _logger;
    private readonly ResponsiveDiskScanService _responsiveScanner;

    public AdaptivePerformanceScanService(ILogger<AdaptivePerformanceScanService> logger)
    {
        _logger = logger;
        _responsiveScanner = new ResponsiveDiskScanService();
    }

    public async Task<string[]> GetAvailableDrivesAsync()
    {
        return await _responsiveScanner.GetAvailableDrivesAsync();
    }

    public async Task<ScanResult> ScanDriveAsync(string drivePath, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var driveInfo = new DriveInfo(drivePath);
        
        _logger.LogInformation("Starting adaptive scan of {Drive} (Format: {Format}, Type: {Type})", 
            drivePath, driveInfo.DriveFormat, driveInfo.DriveType);

        // Phase 1: Determine optimal scanning strategy
        var strategy = DetermineOptimalStrategy(driveInfo);
        _logger.LogInformation("Selected strategy: {Strategy} (Expected speedup: {Speedup}x)", 
            strategy.Name, strategy.ExpectedSpeedup);

        // Phase 2: Execute the chosen strategy
        try
        {
            return strategy.Name switch
            {
                "NTFS_MFT_DIRECT" => await ScanWithNtfsMftDirect(drivePath, progress, cancellationToken),
                "NTFS_USN_JOURNAL" => await ScanWithNtfsUsnJournal(drivePath, progress, cancellationToken),
                "RESPONSIVE_TRADITIONAL" => await _responsiveScanner.ScanDriveAsync(drivePath, progress!, cancellationToken),
                _ => await _responsiveScanner.ScanDriveAsync(drivePath, progress!, cancellationToken)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Strategy {Strategy} failed, falling back to responsive traditional scanning", strategy.Name);
            return await _responsiveScanner.ScanDriveAsync(drivePath, progress!, cancellationToken);
        }
    }

    private ScanStrategy DetermineOptimalStrategy(DriveInfo driveInfo)
    {
        // Strategy 1: NTFS MFT Direct Parsing (WizTree-level performance)
        if (CanUseMftDirect(driveInfo))
        {
            return new ScanStrategy("NTFS_MFT_DIRECT", 100, 
                "Direct MFT parsing for maximum performance on NTFS volumes");
        }

        // Strategy 2: NTFS USN Journal Enumeration (High performance)  
        if (CanUseUsnJournal(driveInfo))
        {
            return new ScanStrategy("NTFS_USN_JOURNAL", 25,
                "USN Journal enumeration for high performance on NTFS volumes");
        }

        // Strategy 3: Responsive Traditional Scanning (Universal fallback)
        return new ScanStrategy("RESPONSIVE_TRADITIONAL", 1,
            "Cross-platform responsive directory scanning with UI threading optimizations");
    }

    private static bool CanUseMftDirect(DriveInfo driveInfo)
    {
        return OperatingSystem.IsWindows() &&
               driveInfo.IsReady &&
               string.Equals(driveInfo.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase) &&
               driveInfo.DriveType == DriveType.Fixed; // Fixed drives only for MFT access
    }

    private static bool CanUseUsnJournal(DriveInfo driveInfo)
    {
        return OperatingSystem.IsWindows() &&
               driveInfo.IsReady &&
               string.Equals(driveInfo.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ScanResult> ScanWithNtfsMftDirect(string drivePath, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting NTFS MFT direct parsing...");
        
        // TODO: Implement when MFT service is fully debugged
        // For now, fall back to traditional scanning
        _logger.LogWarning("NTFS MFT parsing not yet fully implemented, using responsive scanning");
        return await _responsiveScanner.ScanDriveAsync(drivePath, progress!, cancellationToken);
    }

    private async Task<ScanResult> ScanWithNtfsUsnJournal(string drivePath, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting NTFS USN Journal enumeration...");
        
        // TODO: Implement when USN service is fully debugged  
        // For now, fall back to traditional scanning
        _logger.LogWarning("NTFS USN enumeration not yet fully implemented, using responsive scanning");
        return await _responsiveScanner.ScanDriveAsync(drivePath, progress!, cancellationToken);
    }

    private record ScanStrategy(string Name, int ExpectedSpeedup, string Description);
}