using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Topiary.App.Models;

namespace Topiary.App.Services;

public interface IAiInsightsService
{
    Task<(string request, string response)> GetInsightsAsync(ScanResult scanResult);
}

public class MockAiInsightsService : IAiInsightsService
{
    public async Task<(string request, string response)> GetInsightsAsync(ScanResult scanResult)
    {
        await Task.Delay(1000); // Simulate API call

        var summary = new
        {
            drive = scanResult.Drive.DriveLetter,
            totalGB = Math.Round(scanResult.Drive.TotalBytes / 1024.0 / 1024.0 / 1024.0, 1),
            usedGB = Math.Round(scanResult.Drive.UsedBytes / 1024.0 / 1024.0 / 1024.0, 1),
            freeGB = Math.Round(scanResult.Drive.FreeBytes / 1024.0 / 1024.0 / 1024.0, 1),
            usedPercent = Math.Round(scanResult.Drive.UsedPercent, 1),
            largestFolders = scanResult.Root.Children
                .OrderByDescending(x => x.SizeBytes)
                .Take(5)
                .Select(x => new { name = x.Name, sizeGB = Math.Round(x.SizeBytes / 1024.0 / 1024.0 / 1024.0, 1) }),
            topExtensions = scanResult.ByExtension
                .OrderByDescending(x => x.TotalSize)
                .Take(5)
                .Select(x => new { ext = x.Extension, sizeGB = Math.Round(x.TotalSize / 1024.0 / 1024.0 / 1024.0, 1), count = x.FileCount })
        };

        var request = JsonSerializer.Serialize(new
        {
            model = "gpt-4",
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "Analyze disk usage data and provide cleanup recommendations. Be concise and specific."
                },
                new
                {
                    role = "user", 
                    content = $"Analyze this disk usage: {JsonSerializer.Serialize(summary)}"
                }
            },
            max_tokens = 500
        }, new JsonSerializerOptions { WriteIndented = true });

        var largestFolder = scanResult.Root.Children
            .OrderByDescending(x => x.SizeBytes)
            .FirstOrDefault();

        var topExtension = scanResult.ByExtension
            .OrderByDescending(x => x.TotalSize)
            .FirstOrDefault();

        var largestFolderLine = largestFolder != null
            ? $"• Largest folder: {largestFolder.Name} ({largestFolder.FormattedSize})"
            : "• Largest folder: not available";

        var extensionsLine = topExtension != null
            ? $"3. **Extensions**: {topExtension.Extension} files are using {topExtension.FormattedSize}"
            : "3. **Extensions**: no extension data available";

        var response = $@"Based on your {scanResult.Drive.DriveLetter} drive analysis:

**Key Findings:**
• Drive is {scanResult.Drive.UsedPercent:F1}% full ({scanResult.Drive.FormattedUsed} used of {scanResult.Drive.FormattedTotal})
{largestFolderLine}

**Cleanup Recommendations:**
1. **Large Files**: Check Downloads folder for unused ISO/video files
2. **Temporary Files**: Clear system temp folders and browser cache  
{extensionsLine}

**Quick Wins:**
• Uninstall unused programs from Program Files
• Empty Recycle Bin and Downloads folder
• Run Disk Cleanup utility

This analysis shows typical patterns. Focus on the largest folders first for maximum space savings.";

        return (request, response);
    }
}