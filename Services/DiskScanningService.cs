using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Topiary.Models;
using System.Collections.Concurrent;

namespace Topiary.Services
{
    public class DiskScanningService : IDiskScanningService
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindFirstFile(string lpFileName, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool FindNextFile(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr hFindFile);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATA
        {
            public FileAttributes dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        private const int INVALID_HANDLE_VALUE = -1;
        private Dictionary<string, FileSystemEntry> _pathCache = new();
        private ConcurrentBag<FileSystemEntry> _largestEntries = new();
        private int _processedItems;
        private readonly object _lockObject = new();
        private int _totalDirectories;
        private int _foldersProcessed;
        private ConcurrentDictionary<string, long> _directorySizes = new();
        private List<FileSystemEntry> _batchBuffer = new List<FileSystemEntry>(100);

        public List<FileSystemEntry> GetLargestEntries() =>
            _largestEntries.OrderByDescending(e => e.Size).Take(10).ToList();

        private void UpdateLargestEntries(FileSystemEntry entry)
        {
            lock (_lockObject)
            {
                // Include directories and files
                _largestEntries.Add(entry);
                var newList = _largestEntries.OrderByDescending(e => e.Size)
                    .Take(15)
                    .ToList();
                _largestEntries = new ConcurrentBag<FileSystemEntry>(newList);
            }
        }

        public async Task<FileSystemEntry> ScanDriveAsync(string driveLetter, IProgress<double> progress = null)
        {
            if (string.IsNullOrEmpty(driveLetter))
            {
                throw new ArgumentNullException(nameof(driveLetter), "Drive letter cannot be null or empty");
            }

            _processedItems = 0;
            _totalDirectories = 0;
            _foldersProcessed = 0;
            _largestEntries = new ConcurrentBag<FileSystemEntry>();
            _pathCache.Clear();
            _directorySizes.Clear();

            var rootPath = $"{driveLetter}:\\";
            
            // Verify drive exists and is ready
            if (!Directory.Exists(rootPath))
            {
                throw new DirectoryNotFoundException($"Drive {rootPath} not found or not ready");
            }

            var rootEntry = new FileSystemEntry
            {
                Name = driveLetter,
                FullPath = rootPath,
                IsDirectory = true
            };
            _pathCache[rootEntry.FullPath.ToLower()] = rootEntry;

            try
            {
                await Task.Run(() => 
                {
                    rootEntry.Size = ScanDirectoryFast(rootPath, rootEntry, progress);
                    progress?.Report(100); // Direct completion after scan
                });
                return rootEntry;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning drive: {ex.Message}");
                throw;
            }
        }

        private void FlushBatchToUI(FileSystemEntry parent)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var item in _batchBuffer)
                {
                    parent.Children.Add(item);
                }
                _batchBuffer.Clear();
            });
        }

        private long ScanDirectoryFast(string path, FileSystemEntry parent, IProgress<double> progress)
        {
            IntPtr findHandle = FindFirstFile(Path.Combine(path, "*"), out WIN32_FIND_DATA findData);
            if (findHandle.ToInt64() == INVALID_HANDLE_VALUE) return 0;

            long directorySize = 0;
            var children = new List<FileSystemEntry>();

            try
            {
                do
                {
                    if (findData.cFileName == "." || findData.cFileName == "..") continue;

                    string fullPath = Path.Combine(path, findData.cFileName);
                    bool isDirectory = (findData.dwFileAttributes & FileAttributes.Directory) != 0;
                    long size = 0;

                    if (!isDirectory)
                    {
                        size = ((long)findData.nFileSizeHigh << 32) + findData.nFileSizeLow;
                        directorySize += size;
                        UpdateLargestEntries(new FileSystemEntry 
                        { 
                            Size = size, 
                            Name = findData.cFileName,
                            FullPath = fullPath,
                            IsDirectory = false
                        });
                    }

                    var entry = new FileSystemEntry
                    {
                        Name = findData.cFileName,
                        FullPath = fullPath,
                        Size = size,
                        IsDirectory = isDirectory,
                        Parent = parent
                    };

                    children.Add(entry); // Collect children first

                    if (isDirectory)
                    {
                        entry.Size = ScanDirectoryFast(fullPath, entry, progress); // Recursive call
                        directorySize += entry.Size; // Aggregate AFTER subdirectory completes
                    }

                    _processedItems++;
                    if (_processedItems % 100 == 0) // More frequent updates
                        progress?.Report(Math.Min(99, _processedItems / 1000.0 * 0.7));

                } while (FindNextFile(findHandle, out findData));
            }
            finally
            {
                FindClose(findHandle);
            }

            // Batch UI update after directory completes
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var child in children.OrderByDescending(c => c.Size))
                {
                    parent.Children.Add(child);
                }
            });

            parent.Size = directorySize; // Set parent size AFTER children are processed
            return directorySize;
        }

        public FileSystemEntry GetEntryByPath(string path)
        {
            return _pathCache.TryGetValue(path?.ToLower() ?? "", out var entry) ? entry : null!;
        }
    }
}