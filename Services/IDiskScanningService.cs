using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Topiary.Models;
using System.IO;

namespace Topiary.Services
{
    public interface IDiskScanningService
    {
        Task<FileSystemEntry> ScanDriveAsync(string driveLetter, IProgress<double> progress = null);
        FileSystemEntry GetEntryByPath(string path);
        List<FileSystemEntry> GetLargestEntries();
    }
}