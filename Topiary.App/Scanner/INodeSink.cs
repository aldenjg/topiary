using System;
using System.Threading;

namespace Topiary.App.Scanner;

/// <summary>
/// Consumer interface for processing file system entries during scanning.
/// Provides hooks for tree building and aggregation.
/// </summary>
public interface INodeSink
{
    /// <summary>
    /// Called when starting to process a directory.
    /// </summary>
    /// <param name="entry">Directory entry being started</param>
    void OnDirectoryStart(in Entry entry);
    
    /// <summary>
    /// Called for each file system entry (file or directory).
    /// </summary>
    /// <param name="entry">File system entry</param>
    void OnEntry(in Entry entry);
    
    /// <summary>
    /// Called when finished processing a directory and all its children.
    /// </summary>
    /// <param name="entry">Directory entry being completed</param>
    /// <param name="childCount">Number of child entries processed</param>
    /// <param name="totalSize">Total size of all children</param>
    void OnDirectoryEnd(in Entry entry, int childCount, long totalSize);
    
    /// <summary>
    /// Called when an error occurs processing an entry.
    /// </summary>
    /// <param name="path">Path where error occurred</param>
    /// <param name="exception">Exception that occurred</param>
    void OnError(string path, Exception exception);
}

/// <summary>
/// No-op implementation of INodeSink for testing or when processing is not needed.
/// </summary>
public sealed class NullNodeSink : INodeSink
{
    public static readonly NullNodeSink Instance = new();
    
    private NullNodeSink() { }
    
    public void OnDirectoryStart(in Entry entry) { }
    public void OnEntry(in Entry entry) { }
    public void OnDirectoryEnd(in Entry entry, int childCount, long totalSize) { }
    public void OnError(string path, Exception exception) { }
}

/// <summary>
/// Simple counting sink for performance testing.
/// </summary>
public sealed class CountingSink : INodeSink
{
    private long _fileCount;
    private long _directoryCount;
    private long _totalSize;
    private int _errorCount;
    
    public long FileCount => _fileCount;
    public long DirectoryCount => _directoryCount;
    public long TotalSize => _totalSize;
    public int ErrorCount => _errorCount;
    
    public void OnDirectoryStart(in Entry entry)
    {
        Interlocked.Increment(ref _directoryCount);
    }
    
    public void OnEntry(in Entry entry)
    {
        if (!entry.IsDirectory)
        {
            Interlocked.Increment(ref _fileCount);
            Interlocked.Add(ref _totalSize, entry.Size);
        }
    }
    
    public void OnDirectoryEnd(in Entry entry, int childCount, long totalSize) { }
    
    public void OnError(string path, Exception exception)
    {
        Interlocked.Increment(ref _errorCount);
    }
    
    public void Reset()
    {
        _fileCount = 0;
        _directoryCount = 0;
        _totalSize = 0;
        _errorCount = 0;
    }
    
    public override string ToString()
    {
        return $"Files: {FileCount:N0}, Directories: {DirectoryCount:N0}, Size: {TotalSize:N0} bytes, Errors: {ErrorCount}";
    }
}