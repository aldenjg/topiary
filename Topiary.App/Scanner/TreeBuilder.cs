using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Topiary.App.Models;

namespace Topiary.App.Scanner;

/// <summary>
/// Builds directory tree structure from streaming file system entries.
/// Maintains hierarchy using FileId relationships and aggregates sizes in a single pass.
/// </summary>
public sealed class TreeBuilder : INodeSink
{
    private readonly Dictionary<UInt128, TreeNodeBuilder> _nodesByFileId;
    private readonly Dictionary<UInt128, List<UInt128>> _childrenByParentId;
    private readonly string _rootPath;
    private UInt128 _rootFileId;
    private long _totalFiles;
    private long _totalDirectories;
    private readonly HashSet<UInt128> _visitedFileIds; // For cycle detection
    
    public TreeBuilder(string rootPath)
    {
        _rootPath = rootPath;
        _nodesByFileId = new Dictionary<UInt128, TreeNodeBuilder>();
        _childrenByParentId = new Dictionary<UInt128, List<UInt128>>();
        _visitedFileIds = new HashSet<UInt128>();
    }
    
    public long TotalFiles => _totalFiles;
    public long TotalDirectories => _totalDirectories;
    
    public void OnDirectoryStart(in Entry entry)
    {
        // Called when starting to process a directory - used for progress tracking
        // Directory counting is done in OnEntry to avoid double-counting
    }
    
    public void OnEntry(in Entry entry)
    {
        // Skip duplicate file IDs to avoid cycles and double-counting
        if (!_visitedFileIds.Add(entry.FileId))
        {
            return;
        }
        
        // Count directories when we encounter them
        if (entry.IsDirectory)
        {
            _totalDirectories++;
        }
        else
        {
            _totalFiles++;
        }
        
        var name = entry.Name.ToString();
        var isRoot = IsRootEntry(entry, name);
        
        // Build full path
        string fullPath;
        if (isRoot)
        {
            fullPath = _rootPath;
            _rootFileId = entry.FileId;
        }
        else
        {
            fullPath = BuildFullPath(entry, name);
        }
        
        // Create tree node builder
        var nodeBuilder = new TreeNodeBuilder
        {
            FileId = entry.FileId,
            ParentFileId = entry.ParentFileId,
            Name = name,
            FullPath = fullPath,
            IsDirectory = entry.IsDirectory,
            Size = entry.Size,
            AllocationSize = entry.AllocationSize,
            Attributes = entry.Attributes,
            CreationTime = entry.CreationTime,
            LastWriteTime = entry.LastWriteTime,
            LinkCount = entry.LinkCount
        };
        
        _nodesByFileId[entry.FileId] = nodeBuilder;
        
        // Track parent-child relationships
        if (!isRoot)
        {
            if (!_childrenByParentId.TryGetValue(entry.ParentFileId, out var children))
            {
                children = new List<UInt128>();
                _childrenByParentId[entry.ParentFileId] = children;
            }
            children.Add(entry.FileId);
        }
        
        // Counters already updated above
    }
    
    public void OnDirectoryEnd(in Entry entry, int childCount, long totalSize)
    {
        // Called when directory processing is complete - could update progress
    }
    
    public void OnError(string path, Exception exception)
    {
        // Log error but continue building tree
        Console.WriteLine($"TreeBuilder error at {path}: {exception.Message}");
    }
    
    /// <summary>
    /// Build the final TreeNode structure with aggregated sizes.
    /// Must be called after all entries have been processed.
    /// </summary>
    public TreeNode BuildTree()
    {
        if (!_nodesByFileId.TryGetValue(_rootFileId, out var rootBuilder))
        {
            // Create synthetic root if we didn't find it
            rootBuilder = new TreeNodeBuilder
            {
                FileId = _rootFileId,
                ParentFileId = default,
                Name = Path.GetFileName(_rootPath.TrimEnd('\\', '/')),
                FullPath = _rootPath,
                IsDirectory = true,
                Size = 0,
                AllocationSize = 0,
                Attributes = Scanner.FileAttributes.Directory,
                CreationTime = DateTime.Now.ToFileTime(),
                LastWriteTime = DateTime.Now.ToFileTime(),
                LinkCount = 1
            };
            _nodesByFileId[_rootFileId] = rootBuilder;
        }
        
        // Build tree recursively and calculate aggregated sizes
        var rootNode = BuildNodeRecursive(rootBuilder);
        
        return rootNode;
    }
    
    private TreeNode BuildNodeRecursive(TreeNodeBuilder nodeBuilder)
    {
        var children = new List<TreeNode>();
        long aggregatedSize = nodeBuilder.Size;
        
        // Process children if this is a directory
        if (nodeBuilder.IsDirectory && _childrenByParentId.TryGetValue(nodeBuilder.FileId, out var childFileIds))
        {
            foreach (var childFileId in childFileIds)
            {
                if (_nodesByFileId.TryGetValue(childFileId, out var childBuilder))
                {
                    var childNode = BuildNodeRecursive(childBuilder);
                    children.Add(childNode);
                    aggregatedSize += childNode.SizeBytes;
                }
            }
            
            // Sort children by size (descending) for better UI experience
            children.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        }
        
        return new TreeNode(
            nodeBuilder.Name,
            nodeBuilder.FullPath,
            nodeBuilder.IsDirectory,
            aggregatedSize,
            children.ToArray());
    }
    
    private bool IsRootEntry(in Entry entry, string name)
    {
        // Root is typically empty name or matches the volume root
        return string.IsNullOrEmpty(name) || 
               name == Path.GetFileName(_rootPath.TrimEnd('\\', '/')) ||
               _rootPath.EndsWith(name, StringComparison.OrdinalIgnoreCase);
    }
    
    private string BuildFullPath(in Entry entry, string name)
    {
        // Try to reconstruct path from parent chain (limited depth to avoid infinite loops)
        var pathComponents = new List<string>();
        var currentFileId = entry.ParentFileId;
        var depth = 0;
        const int maxDepth = 100; // Prevent infinite loops
        
        while (depth < maxDepth && _nodesByFileId.TryGetValue(currentFileId, out var parentBuilder))
        {
            if (parentBuilder.FileId == _rootFileId || string.IsNullOrEmpty(parentBuilder.Name))
                break;
                
            pathComponents.Add(parentBuilder.Name);
            currentFileId = parentBuilder.ParentFileId;
            depth++;
        }
        
        // Reverse to get correct order (root to leaf)
        pathComponents.Reverse();
        pathComponents.Add(name);
        
        // Construct full path
        var basePath = _rootPath.TrimEnd('\\', '/');
        return Path.Combine(new[] { basePath }.Concat(pathComponents).ToArray());
    }
    
    /// <summary>
    /// Internal node builder structure for constructing the tree.
    /// </summary>
    private sealed class TreeNodeBuilder
    {
        public UInt128 FileId { get; set; }
        public UInt128 ParentFileId { get; set; }
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public long AllocationSize { get; set; }
        public Scanner.FileAttributes Attributes { get; set; }
        public long CreationTime { get; set; }
        public long LastWriteTime { get; set; }
        public uint LinkCount { get; set; }
    }
}