using System;
using System.Collections.Generic;

namespace Topiary.Models
{
    public class DiskInsight
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public InsightType Type { get; set; }
        public double ImpactSizeBytes { get; set; }
        public List<string> AffectedPaths { get; set; }
        public string RecommendedAction { get; set; }
        public string ImpactSizeFormatted => FormatSize(ImpactSizeBytes);

        public DiskInsight()
        {
            AffectedPaths = new List<string>();
        }

        private string FormatSize(double bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            while (bytes >= 1024 && order < sizes.Length - 1)
            {
                order++;
                bytes = bytes / 1024;
            }
            return $"{bytes:0.##} {sizes[order]}";
        }
    }

    public enum InsightType
    {
        LargeFiles,
        UnusedFiles,
        Redundant,
        TemporaryFiles,
        SystemHealth,
        SecurityConcern
    }
}