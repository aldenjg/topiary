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
using System.Threading;
using System.Diagnostics;

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
        private const int MAX_LARGEST_ENTRIES = 20;
        private const int PROGRESS_UPDATE_INTERVAL_MS = 500;
        private static readonly int MAX_PARALLEL_DIRECTORIES = Environment.ProcessorCount * 2;
        
        private volatile List<FileSystemEntry> _largestEntries = new();
        private readonly object _largestEntriesLock = new();
        private long _processedItems;
        private long _totalBytes;
        private readonly Stopwatch _scanStopwatch = new();
        private DateTime _lastProgressUpdate = DateTime.MinValue;
        private readonly ParallelOptions _parallelOptions;

        public DiskScanningService()
        {
            _parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = MAX_PARALLEL_DIRECTORIES,
                CancellationToken = CancellationToken.None
            };
        }

        public List<FileSystemEntry> GetLargestEntries()
        {
            lock (_largestEntriesLock)
            {
                return _largestEntries.OrderByDescending(e => e.Size).Take(10).ToList();
            }
        }

        private void UpdateLargestEntries(FileSystemEntry entry)
        {
            if (entry.Size == 0) return;
            
            lock (_largestEntriesLock)
            {
                _largestEntries.Add(entry);
                if (_largestEntries.Count > MAX_LARGEST_ENTRIES)
                {
                    _largestEntries = _largestEntries
                        .OrderByDescending(e => e.Size)
                        .Take(MAX_LARGEST_ENTRIES)
                        .ToList();
                }
            }
        }
        
        private void ReportProgressIfNeeded(IProgress<double> progress, long itemsProcessed, long totalBytes)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastProgressUpdate).TotalMilliseconds >= PROGRESS_UPDATE_INTERVAL_MS)
            {
                _lastProgressUpdate = now;
                var progressPercent = Math.Min(99.0, (double)itemsProcessed / 10000.0 * 50.0);
                progress?.Report(progressPercent);
            }
        }

        public async Task<FileSystemEntry> ScanDriveAsync(string driveLetter, IProgress<double> progress = null)
        {
            if (string.IsNullOrEmpty(driveLetter))
            {
                throw new ArgumentNullException(nameof(driveLetter), "Drive letter cannot be null or empty");
            }

            // Reset state
            Interlocked.Exchange(ref _processedItems, 0);
            Interlocked.Exchange(ref _totalBytes, 0);
            lock (_largestEntriesLock)
            {
                _largestEntries.Clear();
            }
            _lastProgressUpdate = DateTime.MinValue;
            _scanStopwatch.Restart();

            var rootPath = $"{driveLetter}:\\";
            
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

            try
            {
                Debug.WriteLine($"Starting optimized scan of {rootPath}");
                
                await Task.Run(() => 
                {
                    rootEntry.Size = ScanDirectoryOptimized(rootPath, rootEntry, progress);
                    progress?.Report(100);
                });
                
                _scanStopwatch.Stop();
                Debug.WriteLine($"Scan completed in {_scanStopwatch.ElapsedMilliseconds}ms, processed {_processedItems:N0} items, {FormatBytes(_totalBytes)}");
                
                // Build UI tree structure asynchronously
                await BuildUITreeAsync(rootEntry);
                
                return rootEntry;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scanning drive: {ex.Message}");
                throw;
            }
        }

        private async Task BuildUITreeAsync(FileSystemEntry rootEntry)
        {
            await Task.Run(() =>
            {
                BuildUITreeRecursive(rootEntry);
            });
        }
        
        private void BuildUITreeRecursive(FileSystemEntry entry)
        {
            if (entry.Children.Count > 1)
            {
                var sortedChildren = entry.Children.OrderByDescending(c => c.Size).ToList();
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    entry.Children.Clear();
                    foreach (var child in sortedChildren)
                    {
                        entry.Children.Add(child);
                        if (child.IsDirectory)
                        {
                            BuildUITreeRecursive(child);
                        }
                    }
                });
            }
        }

        private long ScanDirectoryOptimized(string path, FileSystemEntry parent, IProgress<double> progress)
        {
            IntPtr findHandle = FindFirstFile(Path.Combine(path, "*"), out WIN32_FIND_DATA findData);
            if (findHandle.ToInt64() == INVALID_HANDLE_VALUE) return 0;

            long directorySize = 0;
            var files = new List<FileSystemEntry>();
            var subdirectories = new List<FileSystemEntry>();

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
                        Interlocked.Add(ref _totalBytes, size);
                        
                        var fileEntry = new FileSystemEntry
                        {
                            Name = findData.cFileName,
                            FullPath = fullPath,
                            Size = size,
                            IsDirectory = false,
                            Parent = parent
                        };
                        
                        files.Add(fileEntry);
                        UpdateLargestEntries(fileEntry);
                    }
                    else
                    {
                        var dirEntry = new FileSystemEntry
                        {
                            Name = findData.cFileName,
                            FullPath = fullPath,
                            Size = 0,
                            IsDirectory = true,
                            Parent = parent
                        };
                        subdirectories.Add(dirEntry);
                    }

                    var itemsProcessed = Interlocked.Increment(ref _processedItems);
                    ReportProgressIfNeeded(progress, itemsProcessed, _totalBytes);

                } while (FindNextFile(findHandle, out findData));
            }
            finally
            {
                FindClose(findHandle);
            }

            // Add files to parent (no UI updates during scan)
            foreach (var file in files)
            {
                parent.Children.Add(file);
            }

            // Process subdirectories - use parallel processing for better performance
            if (subdirectories.Count > 0)
            {
                var subdirectorySizes = new long[subdirectories.Count];
                
                if (subdirectories.Count > 3 && path.Split('\\').Length <= 4) // Parallel only for shallow directories
                {
                    Parallel.For(0, subdirectories.Count, _parallelOptions, i =>
                    {
                        try
                        {
                            subdirectorySizes[i] = ScanDirectoryOptimized(subdirectories[i].FullPath, subdirectories[i], progress);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            subdirectorySizes[i] = 0; // Skip inaccessible directories
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error scanning {subdirectories[i].FullPath}: {ex.Message}");
                            subdirectorySizes[i] = 0;
                        }
                    });
                }
                else
                {
                    // Sequential processing for deep or few directories
                    for (int i = 0; i < subdirectories.Count; i++)
                    {
                        try
                        {
                            subdirectorySizes[i] = ScanDirectoryOptimized(subdirectories[i].FullPath, subdirectories[i], progress);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            subdirectorySizes[i] = 0;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error scanning {subdirectories[i].FullPath}: {ex.Message}");
                            subdirectorySizes[i] = 0;
                        }
                    }
                }
                
                // Update directory sizes and add to parent
                for (int i = 0; i < subdirectories.Count; i++)
                {
                    subdirectories[i].Size = subdirectorySizes[i];
                    directorySize += subdirectorySizes[i];
                    parent.Children.Add(subdirectories[i]);
                    
                    if (subdirectorySizes[i] > 0)
                    {
                        UpdateLargestEntries(subdirectories[i]);
                    }
                }
            }

            parent.Size = directorySize;
            return directorySize;
        }
        
        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dblSByte = bytes;
            while (dblSByte >= 1024 && i < suffixes.Length - 1)
            {
                dblSByte /= 1024;
                i++;
            }
            return $"{dblSByte:F2} {suffixes[i]}";
        }

        public FileSystemEntry GetEntryByPath(string path)
        {
            // Simple implementation - could be optimized with caching if needed
            return null;
        }
    }
}