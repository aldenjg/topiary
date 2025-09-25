using System;
using System.Linq;
using System.Threading.Tasks;
using Topiary.App.Services;
using Topiary.App.Models;

Console.WriteLine("Testing Topiary High-Performance Scanner");
Console.WriteLine("========================================");

var scanner = new HighPerformanceScanService();

try
{
    // Get available drives
    var drives = await scanner.GetAvailableDrivesAsync();
    Console.WriteLine($"Available drives: {string.Join(", ", drives)}");
    
    if (drives.Length == 0)
    {
        Console.WriteLine("No drives found!");
        return;
    }
    
    var testDrive = drives[0];
    Console.WriteLine($"Testing scan on drive: {testDrive}");
    
    // Set up progress reporting
    var progressReports = 0;
    var progress = new Progress<ScanProgress>(p =>
    {
        progressReports++;
        if (progressReports % 10 == 0 || p.Percent >= 100)
        {
            Console.WriteLine($"Progress: {p.Percent:F1}% - {p.FilesProcessed:N0} files - {p.Elapsed.TotalSeconds:F1}s - {p.CurrentPath ?? ""}");
        }
    });
    
    var startTime = DateTime.UtcNow;
    
    // Scan the drive
    var result = await scanner.ScanDriveAsync(testDrive, progress);
    
    var elapsed = DateTime.UtcNow - startTime;
    
    Console.WriteLine("\n=== SCAN COMPLETE ===");
    Console.WriteLine($"Drive: {result.Drive.DriveLetter} ({result.Drive.FormattedTotal})");
    Console.WriteLine($"Used: {result.Drive.FormattedUsed} ({result.Drive.UsedPercent:F1}%)");
    Console.WriteLine($"Free: {result.Drive.FormattedFree} ({result.Drive.FreePercent:F1}%)");
    Console.WriteLine($"Total time: {elapsed.TotalSeconds:F1} seconds");
    Console.WriteLine($"Files processed: {result.Root.GetTotalFileCount():N0}");
    Console.WriteLine($"Tree size: {result.Root.SizeBytes:N0} bytes");
    Console.WriteLine($"Progress reports: {progressReports}");
    
    // Show top-level directories
    Console.WriteLine("\nTop-level directories:");
    foreach (var child in result.Root.Children.Take(5))
    {
        Console.WriteLine($"  {child.Name}: {child.FormattedSize}");
    }
    
    // Show largest files
    Console.WriteLine("\nLargest files:");
    foreach (var file in result.TopFiles.Take(5))
    {
        Console.WriteLine($"  {file.Name}: {file.FormattedSize}");
    }
    
    Console.WriteLine("\n✅ Scanner test PASSED!");
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Scanner test FAILED: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}