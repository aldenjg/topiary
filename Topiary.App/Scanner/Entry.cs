using System;

namespace Topiary.App.Scanner;

/// <summary>
/// Zero-allocation file system entry structure for high-performance scanning.
/// Replaces FileInfo to eliminate per-file object allocations.
/// </summary>
public readonly struct Entry
{
    /// <summary>
    /// Unique file identifier (128-bit on NTFS, path-based hash on other systems)
    /// </summary>
    public readonly UInt128 FileId;
    
    /// <summary>
    /// Parent directory identifier for building hierarchy
    /// </summary>
    public readonly UInt128 ParentFileId;
    
    /// <summary>
    /// File attributes packed as flags
    /// </summary>
    public readonly FileAttributes Attributes;
    
    /// <summary>
    /// File size in bytes (actual data)
    /// </summary>
    public readonly long Size;
    
    /// <summary>
    /// Allocation size in bytes (disk space used, includes slack)
    /// </summary>
    public readonly long AllocationSize;
    
    /// <summary>
    /// Creation time (Windows FILETIME format for compatibility)
    /// </summary>
    public readonly long CreationTime;
    
    /// <summary>
    /// Last write time (Windows FILETIME format for compatibility)
    /// </summary>
    public readonly long LastWriteTime;
    
    /// <summary>
    /// File name (no path, just the name component)
    /// </summary>
    public readonly ReadOnlyMemory<char> Name;
    
    /// <summary>
    /// Number of hard links to this file
    /// </summary>
    public readonly uint LinkCount;
    
    public Entry(
        UInt128 fileId,
        UInt128 parentFileId,
        FileAttributes attributes,
        long size,
        long allocationSize,
        long creationTime,
        long lastWriteTime,
        ReadOnlyMemory<char> name,
        uint linkCount = 1)
    {
        FileId = fileId;
        ParentFileId = parentFileId;
        Attributes = attributes;
        Size = size;
        AllocationSize = allocationSize;
        CreationTime = creationTime;
        LastWriteTime = lastWriteTime;
        Name = name;
        LinkCount = linkCount;
    }
    
    /// <summary>
    /// True if this entry represents a directory
    /// </summary>
    public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;
    
    /// <summary>
    /// True if this entry is a reparse point (symlink, junction, etc.)
    /// </summary>
    public bool IsReparsePoint => (Attributes & FileAttributes.ReparsePoint) != 0;
    
    /// <summary>
    /// True if this entry represents a symlink
    /// </summary>
    public bool IsSymlink => IsReparsePoint; // Simplified - all reparse points treated as symlinks for now
    
    /// <summary>
    /// True if this entry represents a junction point
    /// </summary>
    public bool IsJunction => IsReparsePoint; // Simplified - could check specific reparse tag
    
    /// <summary>
    /// True if this file is offline (archived to tape/cloud)
    /// </summary>
    public bool IsOffline => (Attributes & FileAttributes.Offline) != 0;
    
    /// <summary>
    /// Convert Windows FILETIME to DateTime
    /// </summary>
    public DateTime CreationDateTime => DateTime.FromFileTime(CreationTime);
    
    /// <summary>
    /// Convert Windows FILETIME to DateTime  
    /// </summary>
    public DateTime LastWriteDateTime => DateTime.FromFileTime(LastWriteTime);
    
    public override string ToString()
    {
        var type = IsDirectory ? "DIR" : "FILE";
        var size = IsDirectory ? "---" : Size.ToString("N0");
        return $"{type,-4} {size,12} {Name}";
    }
}

[Flags]
public enum FileAttributes : uint
{
    ReadOnly = 0x1,
    Hidden = 0x2,
    System = 0x4,
    Directory = 0x10,
    Archive = 0x20,
    Device = 0x40,
    Normal = 0x80,
    Temporary = 0x100,
    SparseFile = 0x200,
    ReparsePoint = 0x400,
    Compressed = 0x800,
    Offline = 0x1000,
    NotContentIndexed = 0x2000,
    Encrypted = 0x4000
}