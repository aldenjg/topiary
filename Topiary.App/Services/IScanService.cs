using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Topiary.App.Models;

namespace Topiary.App.Services;

public interface IScanService
{
    Task<ScanResult> ScanDriveAsync(string drivePath, IProgress<ScanProgress> progress, CancellationToken cancellationToken = default);
    Task<string[]> GetAvailableDrivesAsync();
}

public class MockScanService : IScanService
{
    public async Task<string[]> GetAvailableDrivesAsync()
    {
        await Task.Delay(100);
        return ["C:\\", "D:\\", "E:\\"];
    }

    public async Task<ScanResult> ScanDriveAsync(string drivePath, IProgress<ScanProgress> progress, CancellationToken cancellationToken = default)
    {
        var random = new Random();
        var totalFiles = random.Next(50000, 100000);
        var startTime = DateTime.Now;

        for (int i = 0; i <= totalFiles; i += 1000)
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            var percent = (double)i / totalFiles * 100;
            var elapsed = DateTime.Now - startTime;
            var currentPath = i % 5000 == 0 ? $"C:\\Users\\Documents\\File_{i}.txt" : null;
            
            progress?.Report(new ScanProgress(percent, i, elapsed, currentPath));
            await Task.Delay(50, cancellationToken);
        }

        // Mock data generation
        var children = new TreeNode[]
        {
            new("Program Files", "C:\\Program Files", true, 15_000_000_000, []),
            new("Users", "C:\\Users", true, 25_000_000_000, [
                new("Documents", "C:\\Users\\Documents", true, 8_000_000_000, []),
                new("Desktop", "C:\\Users\\Desktop", true, 2_000_000_000, []),
                new("Downloads", "C:\\Users\\Downloads", true, 15_000_000_000, [])
            ]),
            new("Windows", "C:\\Windows", true, 20_000_000_000, []),
            new("temp", "C:\\temp", true, 5_000_000_000, [])
        };

        var root = new TreeNode("C:\\", "C:\\", true, 65_000_000_000, children);
        var driveStats = new DriveStats("C", 100_000_000_000, 65_000_000_000, 35_000_000_000);
        
        var topFiles = new TopItem[]
        {
            new("bigfile1.iso", "C:\\temp\\bigfile1.iso", 4_000_000_000, false),
            new("video.mkv", "C:\\Users\\Downloads\\video.mkv", 3_500_000_000, false),
            new("archive.zip", "C:\\Users\\Documents\\archive.zip", 2_800_000_000, false)
        };

        var extensions = new ExtensionGroup[]
        {
            new(".dll", 8_500_000_000, 15420),
            new(".exe", 6_200_000_000, 1250),
            new(".iso", 4_000_000_000, 1),
            new(".mkv", 3_500_000_000, 1)
        };

        return new ScanResult(driveStats, root, topFiles, extensions);
    }
}