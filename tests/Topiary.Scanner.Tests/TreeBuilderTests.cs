using System;
using System.IO;
using FluentAssertions;
using Topiary.App.Scanner;
using Xunit;

namespace Topiary.Scanner.Tests;

/// <summary>
/// Tests for TreeBuilder - streaming tree construction and size aggregation.
/// </summary>
public class TreeBuilderTests
{
    [Fact]
    public void TreeBuilder_EmptyInput_ReturnsRootOnly()
    {
        // Arrange
        var builder = new TreeBuilder("C:\\");
        
        // Act
        var tree = builder.BuildTree();
        
        // Assert
        tree.Should().NotBeNull();
        tree.Name.Should().Be("");  // Root typically has empty name
        tree.FullPath.Should().Be("C:\\");
        tree.IsDirectory.Should().BeTrue();
        tree.Children.Should().BeEmpty();
        tree.SizeBytes.Should().Be(0);
    }
    
    [Fact]
    public void TreeBuilder_SingleFile_BuildsCorrectTree()
    {
        // Arrange
        var builder = new TreeBuilder("C:\\");
        var rootFileId = (UInt128)1;
        var fileFileId = (UInt128)2;
        
        // Add root directory
        var rootEntry = new Entry(rootFileId, default, Topiary.App.Scanner.FileAttributes.Directory, 0, 0, 0, 0, "".AsMemory());
        builder.OnEntry(rootEntry);
        
        // Add a single file
        var fileEntry = new Entry(fileFileId, rootFileId, Topiary.App.Scanner.FileAttributes.Normal, 1024, 4096, 0, 0, "test.txt".AsMemory());
        builder.OnEntry(fileEntry);
        
        // Act
        var tree = builder.BuildTree();
        
        // Assert
        tree.SizeBytes.Should().Be(1024); // File size aggregated to root
        tree.Children.Should().HaveCount(1);
        tree.Children[0].Name.Should().Be("test.txt");
        tree.Children[0].SizeBytes.Should().Be(1024);
        tree.Children[0].IsDirectory.Should().BeFalse();
        
        builder.TotalFiles.Should().Be(1);
        builder.TotalDirectories.Should().Be(1); // Root counts as directory
    }
    
    [Fact]
    public void TreeBuilder_NestedDirectories_BuildsHierarchy()
    {
        // Arrange
        var builder = new TreeBuilder("C:\\");
        var rootId = (UInt128)1;
        var dir1Id = (UInt128)2;
        var dir2Id = (UInt128)3;
        var file1Id = (UInt128)4;
        var file2Id = (UInt128)5;
        
        // Build: C:\ -> folder1 -> folder2 -> file.txt, and C:\folder1\another.txt
        builder.OnEntry(new Entry(rootId, default, Topiary.App.Scanner.FileAttributes.Directory, 0, 0, 0, 0, "".AsMemory()));
        builder.OnEntry(new Entry(dir1Id, rootId, Topiary.App.Scanner.FileAttributes.Directory, 0, 0, 0, 0, "folder1".AsMemory()));
        builder.OnEntry(new Entry(dir2Id, dir1Id, Topiary.App.Scanner.FileAttributes.Directory, 0, 0, 0, 0, "folder2".AsMemory()));
        builder.OnEntry(new Entry(file1Id, dir2Id, Topiary.App.Scanner.FileAttributes.Normal, 2048, 4096, 0, 0, "deep.txt".AsMemory()));
        builder.OnEntry(new Entry(file2Id, dir1Id, Topiary.App.Scanner.FileAttributes.Normal, 1024, 4096, 0, 0, "another.txt".AsMemory()));
        
        // Act
        var tree = builder.BuildTree();
        
        // Assert
        tree.SizeBytes.Should().Be(3072); // Total of both files
        tree.Children.Should().HaveCount(1); // One top-level folder
        
        var folder1 = tree.Children[0];
        folder1.Name.Should().Be("folder1");
        folder1.SizeBytes.Should().Be(3072); // Both files under folder1
        folder1.Children.Should().HaveCount(2); // folder2 + another.txt
        
        // Should be sorted by size (folder2=2048 > another.txt=1024)
        var folder2 = folder1.Children[0];
        folder2.Name.Should().Be("folder2");
        folder2.SizeBytes.Should().Be(2048);
        
        var anotherFile = folder1.Children[1];
        anotherFile.Name.Should().Be("another.txt");
        anotherFile.SizeBytes.Should().Be(1024);
        
        builder.TotalFiles.Should().Be(2);
        builder.TotalDirectories.Should().Be(3); // Root + folder1 + folder2
    }
    
    [Fact]
    public void TreeBuilder_DuplicateFileIds_IgnoresSecondOccurrence()
    {
        // Arrange
        var builder = new TreeBuilder("C:\\");
        var rootId = (UInt128)1;
        var fileId = (UInt128)2;
        
        builder.OnEntry(new Entry(rootId, default, Topiary.App.Scanner.FileAttributes.Directory, 0, 0, 0, 0, "".AsMemory()));
        builder.OnEntry(new Entry(fileId, rootId, Topiary.App.Scanner.FileAttributes.Normal, 1024, 4096, 0, 0, "test.txt".AsMemory()));
        
        // Add duplicate file ID (simulates hard link or processing error)
        builder.OnEntry(new Entry(fileId, rootId, Topiary.App.Scanner.FileAttributes.Normal, 2048, 4096, 0, 0, "duplicate.txt".AsMemory()));
        
        // Act
        var tree = builder.BuildTree();
        
        // Assert
        tree.Children.Should().HaveCount(1); // Only one file should appear
        tree.SizeBytes.Should().Be(1024); // Original file size, not duplicate
        builder.TotalFiles.Should().Be(1); // Only counted once
    }
    
    [Fact]
    public void TreeBuilder_OnError_ContinuesOperation()
    {
        // Arrange
        var builder = new TreeBuilder("C:\\");
        var rootId = (UInt128)1;
        var fileId = (UInt128)2;
        
        // Add entries and simulate error
        builder.OnEntry(new Entry(rootId, default, Topiary.App.Scanner.FileAttributes.Directory, 0, 0, 0, 0, "".AsMemory()));
        builder.OnError("C:\\inaccessible", new UnauthorizedAccessException("Access denied"));
        builder.OnEntry(new Entry(fileId, rootId, Topiary.App.Scanner.FileAttributes.Normal, 1024, 4096, 0, 0, "test.txt".AsMemory()));
        
        // Act
        var tree = builder.BuildTree();
        
        // Assert - Tree should still build successfully
        tree.Children.Should().HaveCount(1);
        tree.Children[0].Name.Should().Be("test.txt");
        builder.TotalFiles.Should().Be(1);
    }
    
    [Fact]
    public void TreeBuilder_LargeNumberOfFiles_PerformanceTest()
    {
        // Arrange
        var builder = new TreeBuilder("C:\\");
        var rootId = (UInt128)1;
        const int fileCount = 10000;
        
        builder.OnEntry(new Entry(rootId, default, Topiary.App.Scanner.FileAttributes.Directory, 0, 0, 0, 0, "".AsMemory()));
        
        var start = DateTime.UtcNow;
        
        // Act - Add many files
        for (int i = 0; i < fileCount; i++)
        {
            var fileId = (UInt128)(i + 2);
            var fileName = $"file_{i:D5}.txt";
            builder.OnEntry(new Entry(fileId, rootId, Topiary.App.Scanner.FileAttributes.Normal, 1024, 4096, 0, 0, fileName.AsMemory()));
        }
        
        var tree = builder.BuildTree();
        var elapsed = DateTime.UtcNow - start;
        
        // Assert
        tree.Children.Should().HaveCount(fileCount);
        tree.SizeBytes.Should().Be(fileCount * 1024);
        builder.TotalFiles.Should().Be(fileCount);
        
        // Performance check - should handle 10K files in under 1 second
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }
    
    [Theory]
    [InlineData("C:\\")]
    [InlineData("D:\\Projects")]
    [InlineData("/usr/local")]
    [InlineData("/home/user/documents")]
    public void TreeBuilder_DifferentRootPaths_Work(string rootPath)
    {
        // Arrange
        var builder = new TreeBuilder(rootPath);
        var rootId = (UInt128)1;
        
        builder.OnEntry(new Entry(rootId, default, Topiary.App.Scanner.FileAttributes.Directory, 0, 0, 0, 0, "".AsMemory()));
        
        // Act
        var tree = builder.BuildTree();
        
        // Assert
        tree.FullPath.Should().Be(rootPath);
    }
}