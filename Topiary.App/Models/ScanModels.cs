using System;

namespace Topiary.App.Models;

public record ScanProgress(double Percent, long FilesProcessed, TimeSpan Elapsed, string? CurrentPath = null);

public record ScanResult(DriveStats Drive, TreeNode Root, TopItem[] TopFiles, ExtensionGroup[] ByExtension);

public record DriveStats(string DriveLetter, long TotalBytes, long UsedBytes, long FreeBytes)
{
    public double UsedPercent => TotalBytes > 0 ? (UsedBytes / (double)TotalBytes) * 100 : 0;
    public double FreePercent => TotalBytes > 0 ? (FreeBytes / (double)TotalBytes) * 100 : 0;
    public string FormattedTotal => FormatBytes(TotalBytes);
    public string FormattedUsed => FormatBytes(UsedBytes);
    public string FormattedFree => FormatBytes(FreeBytes);
    
    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}

public record TreeNode(string Name, string FullPath, bool IsDirectory, long SizeBytes, TreeNode[] Children)
{
    public string FormattedSize => FormatBytes(SizeBytes);
    
    public double GetPercentOfParent(long parentSize) => 
        parentSize > 0 ? (SizeBytes / (double)parentSize) * 100 : 0;

    public long GetTotalFileCount()
    {
        long count = IsDirectory ? 0 : 1; // Count this node if it's a file
        foreach (var child in Children)
        {
            count += child.GetTotalFileCount();
        }
        return count;
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}

public record TopItem(string Name, string FullPath, long SizeBytes, bool IsDirectory)
{
    public string FormattedSize => FormatBytes(SizeBytes);
    
    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}

public record ExtensionGroup(string Extension, long TotalSize, int FileCount)
{
    public string FormattedSize => FormatBytes(TotalSize);
    
    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}