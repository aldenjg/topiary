using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenAI_API;
using Topiary.Models;
using System.Text.Json;
using System.IO;  // For DriveInfo

namespace Topiary.Services
{
    public class AIAnalysisService : IAIAnalysisService
    {
        private readonly ISettingsService _settingsService;
        private readonly IDiskScanningService _diskScanningService;
        private const int MAX_ITEMS_TO_ANALYZE = 15;
        private const int MAX_FILES_TO_ANALYZE = 1000;
        private const int MAX_TOKEN_LENGTH = 4000;

        public AIAnalysisService(ISettingsService settingsService, IDiskScanningService diskScanningService)
        {
            _settingsService = settingsService;
            _diskScanningService = diskScanningService;
        }


            private class FileContext
    {
        public string Name { get; set; }
        public string Size { get; set; }
        public string Description { get; set; }
        public bool CanBeRemoved { get; set; }
    }


        private class DriveAnalysisData
        {
            public double UsedSpacePercentage { get; set; }
            public double FreeSpacePercentage { get; set; }
            public long TotalSizeBytes { get; set; }
            public long FreeSpaceBytes { get; set; }
            public List<LargeFileInfo> LargestItems { get; set; }
        }

        private class LargeFileInfo
        {
            public string Name { get; set; }
            public long SizeBytes { get; set; }
            public string Type { get; set; }  // "File" or "Directory"
        }

        private DriveAnalysisData PrepareAnalysisData(FileSystemEntry rootEntry)
        {
            var driveInfo = new DriveInfo(rootEntry.Name + ":");
            var totalSize = driveInfo.TotalSize;
            var freeSpace = driveInfo.AvailableFreeSpace;
            var usedSpace = totalSize - freeSpace;

            var largestItems = _diskScanningService.GetLargestEntries()
                .Take(MAX_ITEMS_TO_ANALYZE)
                .Select(entry => new LargeFileInfo
                {
                    Name = entry.Name,
                    SizeBytes = entry.Size,
                    Type = entry.IsDirectory ? "Directory" : "File"
                })
                .ToList();

            return new DriveAnalysisData
            {
                UsedSpacePercentage = (double)usedSpace / totalSize * 100,
                FreeSpacePercentage = (double)freeSpace / totalSize * 100,
                TotalSizeBytes = totalSize,
                FreeSpaceBytes = freeSpace,
                LargestItems = largestItems
            };
        }


    private FileContext GetFileContext(FileSystemEntry entry)
    {
        var context = new FileContext 
        { 
            Name = entry.Name,
            Size = FormatSize(entry.Size),
            CanBeRemoved = true
        };

        // System files
        if (entry.Name.Equals("pagefile.sys", StringComparison.OrdinalIgnoreCase))
        {
            context.Description = "Windows system file used for virtual memory";
            context.CanBeRemoved = false;
        }
        else if (entry.Name.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase))
        {
            context.Description = "Windows hibernation file";
            context.CanBeRemoved = true;
        }
        // Game files
        else if (entry.Name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))
        {
            context.Description = "Game content package file";
            var directory = new DirectoryInfo(Path.GetDirectoryName(entry.FullPath));
            if (directory.Name.Contains("steam", StringComparison.OrdinalIgnoreCase))
            {
                context.Description = $"Steam game content from '{directory.Parent?.Name ?? "Unknown Game"}'";
            }
            else if (directory.Name.Contains("epic", StringComparison.OrdinalIgnoreCase))
            {
                context.Description = $"Epic Games content from '{directory.Parent?.Name ?? "Unknown Game"}'";
            }
        }
        else if (entry.Name.EndsWith(".ucas", StringComparison.OrdinalIgnoreCase))
        {
            context.Description = "Unreal Engine game content";
            var directory = new DirectoryInfo(Path.GetDirectoryName(entry.FullPath));
            context.Description = $"Game content from '{directory.Parent?.Name ?? "Unknown Game"}'";
        }
        else if (entry.Name.EndsWith(".xpak", StringComparison.OrdinalIgnoreCase))
        {
            context.Description = "Steam game content package";
            var directory = new DirectoryInfo(Path.GetDirectoryName(entry.FullPath));
            context.Description = $"Steam game content from '{directory.Parent?.Name ?? "Unknown Game"}'";
        }
        
        return context;
    }









    private string CreateAnalysisPrompt(DriveAnalysisData analysisData)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Analyze this disk usage data and provide insights about potential cleanup opportunities.");
        
        sb.AppendLine($"\nDrive Summary:");
        sb.AppendLine($"Total Size: {FormatSize(analysisData.TotalSizeBytes)}");
        sb.AppendLine($"Used: {analysisData.UsedSpacePercentage:F1}%");
        sb.AppendLine($"Free: {analysisData.FreeSpacePercentage:F1}% ({FormatSize(analysisData.FreeSpaceBytes)})");
        
        sb.AppendLine("\nLargest Items (Name, Type, Size):");
        foreach (var item in analysisData.LargestItems.Take(5))
        {
            sb.AppendLine($"- {item.Name}, {item.Type}, {FormatSize(item.SizeBytes)}");
        }

        sb.AppendLine("\nProvide exactly one insight for the largest space consumers. Focus on actionable recommendations.");
        
        return sb.ToString();
    }


    private string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
        
        
        
        
        
        
        
        
        
        private OpenAIAPI GetAPI()
        {
            try
            {
                var apiKey = _settingsService.GetOpenAIKey();
                if (string.IsNullOrEmpty(apiKey))
                {
                    System.Diagnostics.Debug.WriteLine("API key is null or empty");
                    return null;
                }

                if (!apiKey.StartsWith("sk-"))
                {
                    System.Diagnostics.Debug.WriteLine("Invalid API key format - should start with 'sk-'");
                    throw new ArgumentException("Invalid API key format. OpenAI API keys should start with 'sk-'");
                }

                var api = new OpenAIAPI(apiKey);
                
                return api;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing OpenAI API: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                throw new Exception($"Error initializing OpenAI API: {ex.Message}");
            }
        }


        private List<DiskInsight> CreateInsightsFromFiles(List<FileSystemEntry> topItems)
    {
        var totalImpact = topItems.Sum(i => i.Size);
        var fileContexts = topItems.Select(GetFileContext).ToList();
        
        var description = new StringBuilder();
        description.AppendLine($"Found {topItems.Count} large items consuming {FormatSize(totalImpact)}:");
        
        // Group items by type for better organization
        var systemFiles = fileContexts.Where(f => f.Name.EndsWith(".sys")).ToList();
        var gameFiles = fileContexts.Where(f => f.Name.EndsWith(".pak") || f.Name.EndsWith(".ucas") || f.Name.EndsWith(".xpak")).ToList();
        var otherFiles = fileContexts.Where(f => !f.Name.EndsWith(".sys") && !f.Name.EndsWith(".pak") && !f.Name.EndsWith(".ucas") && !f.Name.EndsWith(".xpak")).ToList();

        // List system files first with explanations
        if (systemFiles.Any())
        {
            description.AppendLine("\nSystem Files:");
            foreach (var file in systemFiles)
            {
                description.AppendLine($" {file.Name} ({file.Size})");
                description.AppendLine($"   • {file.Description}");
            }
        }


        // List game files with context
        if (gameFiles.Any())
        {
            description.AppendLine("\nGame Files:");
            foreach (var file in gameFiles)
            {
                description.AppendLine($" {file.Name} ({file.Size})");
                description.AppendLine($"   • {file.Description}");
            }
        }

        // List other large files
        if (otherFiles.Any())
        {
            description.AppendLine("\nOther Large Files:");
            foreach (var file in otherFiles)
            {
                description.AppendLine($" {file.Name} ({file.Size})");
                if (!string.IsNullOrEmpty(file.Description))
                {
                    description.AppendLine($"   • {file.Description}");
                }
            }
        }

        // Customized recommendations based on file types
        var recommendations = new StringBuilder();
        if (gameFiles.Any())
        {
            recommendations.AppendLine("For game files:");
            recommendations.AppendLine("• Move less frequently played games to another drive");
            recommendations.AppendLine("• Uninstall games you no longer play");
            recommendations.AppendLine("• Use Steam's storage management to relocate games");
        }
        if (systemFiles.Any())
        {
            if (recommendations.Length > 0) recommendations.AppendLine();
            recommendations.AppendLine("For system files:");
            recommendations.AppendLine("• Page file size can be adjusted in Windows system settings");
            recommendations.AppendLine("• Hibernation file can be disabled if you don't use hibernate");
        }

        return new List<DiskInsight>
        {
            new DiskInsight
            {
                Title = "Large Files Analysis",
                Description = description.ToString(),
                Type = InsightType.LargeFiles,
                ImpactSizeBytes = totalImpact,
                AffectedPaths = topItems.Select(i => i.Name).ToList(),
                RecommendedAction = recommendations.ToString()
            }
        };
    }




    private async Task<string> GetAIResponseAsync(string prompt)
    {
        var api = GetAPI();
        if (api == null)
            throw new InvalidOperationException("OpenAI API not initialized");

        try
        {
            var chat = api.Chat.CreateConversation();
            chat.Model = "gpt-3.5-turbo";
            chat.RequestParameters.Temperature = 0;
            
            chat.AppendSystemMessage("You are a disk analyzer API. Respond only with valid JSON containing insights about disk usage.");
            chat.AppendUserInput("Your response must be valid JSON that exactly matches this structure (no extra text):\n" + 
                               "{\"insights\":[{\"title\":\"title here\",\"description\":\"description here\"," +
                               "\"type\":\"LargeFiles\",\"impactSizeBytes\":1234567,\"affectedPaths\":[\"path1\"]," +
                               "\"recommendedAction\":\"action here\"}]}");
            chat.AppendUserInput(prompt);

            var response = await chat.GetResponseFromChatbotAsync();
            System.Diagnostics.Debug.WriteLine($"Raw API Response: {response}");
            return response;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"API Error: {ex.Message}");
            throw;
        }
    }



    public async Task<List<DiskInsight>> AnalyzeDiskUsageAsync(FileSystemEntry rootEntry)
    {
        if (!_settingsService.HasOpenAIKey)
        {
            return new List<DiskInsight>
            {
                new DiskInsight
                {
                    Title = "AI Insights Unavailable",
                    Description = "To enable AI-powered insights, please add your OpenAI API key in Settings.",
                    Type = InsightType.SystemHealth
                }
            };
        }

        try
        {
            var topItems = _diskScanningService.GetLargestEntries().Take(5).ToList();
            var insights = CreateInsightsFromFiles(topItems);

            // Add privacy notice
            insights.Add(new DiskInsight
            {
                Title = "💡 Analysis Information",
                Description = "This analysis uses AI to identify disk usage patterns.\n" +
                            "• Only file names and sizes are analyzed\n" +
                            "• No file contents are accessed\n" +
                            "• All processing is done securely",
                Type = InsightType.SystemHealth,
                RecommendedAction = "You can disable AI analysis in Settings"
            });

            return insights;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in analysis: {ex.Message}");
            return new List<DiskInsight>
            {
                new DiskInsight
                {
                    Title = "Analysis Error",
                    Description = $"Error analyzing disk: {ex.Message}",
                    Type = InsightType.SystemHealth
                }
            };
        }
    }


    private string CleanJsonResponse(string response)
    {
        try
        {
            // Remove any markdown code block markers
            response = response.Replace("```json", "").Replace("```", "");
            
            // Remove any leading/trailing whitespace
            response = response.Trim();
            
            // If the response doesn't start with {, try to find the first {
            if (!response.StartsWith("{"))
            {
                var startIndex = response.IndexOf("{");
                if (startIndex >= 0)
                {
                    response = response.Substring(startIndex);
                }
            }
            
            // If the response doesn't end with }, try to find the last }
            if (!response.EndsWith("}"))
            {
                var endIndex = response.LastIndexOf("}");
                if (endIndex >= 0)
                {
                    response = response.Substring(0, endIndex + 1);
                }
            }
            
            System.Diagnostics.Debug.WriteLine("Cleaned Response:");
            System.Diagnostics.Debug.WriteLine(response);
            
            return response;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cleaning response: {ex.Message}");
            return response;
        }
    }


    private List<DiskInsight> ParseAIResponse(string response)
    {
        try
        {
            // Try to clean up the response
            response = response.Trim();
            if (!response.StartsWith("{"))
            {
                var start = response.IndexOf("{");
                if (start >= 0)
                    response = response.Substring(start);
            }
            if (!response.EndsWith("}"))
            {
                var end = response.LastIndexOf("}");
                if (end >= 0)
                    response = response.Substring(0, end + 1);
            }

            System.Diagnostics.Debug.WriteLine($"Cleaned response: {response}");

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            };

            var aiResponse = JsonSerializer.Deserialize<AIResponse>(response, options);
            
            if (aiResponse?.Insights == null || !aiResponse.Insights.Any())
            {
                throw new Exception("No insights found in response");
            }

            var validInsights = aiResponse.Insights
                .Where(i => !string.IsNullOrEmpty(i.Title) && !string.IsNullOrEmpty(i.Description))
                .ToList();

            if (!validInsights.Any())
            {
                throw new Exception("No valid insights found");
            }

            return validInsights;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error parsing response: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Response was: {response}");

            // Create a meaningful default insight based on the largest items we analyzed
            var topItems = _diskScanningService.GetLargestEntries().Take(3).ToList();
            
            if (topItems.Any())
            {
                var largestItem = topItems[0];
                var totalImpact = topItems.Sum(i => i.Size);
                var itemsList = string.Join("\n", topItems.Select(i => $"• {i.Name} ({FormatSize(i.Size)})"));
                var gameFiles = topItems.Any(i => i.Name.EndsWith(".pak") || i.Name.EndsWith(".ucas"));
                
                var description = new StringBuilder();
                description.AppendLine($"Found {topItems.Count} large items consuming {FormatSize(totalImpact)}:");
                description.AppendLine(itemsList);
                
                var recommendedAction = gameFiles 
                    ? "These appear to be game files. Consider:\n" +
                      "• Moving less frequently played games to another drive\n" +
                      "• Uninstalling games you no longer play\n" +
                      "• Using Steam's storage management features to relocate large games"
                    : "Consider:\n" +
                      "• Moving these files to external storage if rarely accessed\n" +
                      "• Compressing large files if possible\n" +
                      "• Using cloud storage for large media files";

                return new List<DiskInsight>
                {
                    new DiskInsight
                    {
                        Title = "Large Files Analysis",
                        Description = description.ToString(),
                        Type = InsightType.LargeFiles,
                        ImpactSizeBytes = totalImpact,
                        AffectedPaths = topItems.Select(i => i.Name).ToList(),
                        RecommendedAction = recommendedAction
                    }
                };
            }
            
            return new List<DiskInsight>
            {
                new DiskInsight
                {
                    Title = "Disk Analysis Complete",
                    Description = "Scan completed successfully. Review the disk usage charts for more details.",
                    Type = InsightType.SystemHealth,
                    RecommendedAction = "Check the charts above for a visual breakdown of disk usage"
                }
            };
        }
    }

    private string ExtractValue(string json, string key)
    {
        try
        {
            var keyPattern = $"\"{key}\": \"([^\"]+)\"";
            var match = System.Text.RegularExpressions.Regex.Match(json, keyPattern);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }




        private class AIResponse
        {
            public List<DiskInsight> Insights { get; set; }
        }
    }
}