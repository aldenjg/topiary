using System;
using FluentAssertions;
using Topiary.App.Scanner;
using Xunit;

namespace Topiary.Scanner.Tests;

/// <summary>
/// Tests for the Entry struct - our zero-allocation file metadata structure.
/// </summary>
public class EntryTests
{
    [Fact]
    public void Entry_Constructor_SetsAllProperties()
    {
        // Arrange
        var fileId = (UInt128)12345;
        var parentFileId = (UInt128)67890;
        var attributes = Topiary.App.Scanner.FileAttributes.Normal;
        var size = 1024L;
        var allocationSize = 4096L;
        var creationTime = DateTime.UtcNow.ToFileTime();
        var lastWriteTime = DateTime.UtcNow.ToFileTime();
        var name = "test.txt".AsMemory();
        var linkCount = 1u;
        
        // Act
        var entry = new Entry(fileId, parentFileId, attributes, size, allocationSize, 
            creationTime, lastWriteTime, name, linkCount);
        
        // Assert
        entry.FileId.Should().Be(fileId);
        entry.ParentFileId.Should().Be(parentFileId);
        entry.Attributes.Should().Be(attributes);
        entry.Size.Should().Be(size);
        entry.AllocationSize.Should().Be(allocationSize);
        entry.CreationTime.Should().Be(creationTime);
        entry.LastWriteTime.Should().Be(lastWriteTime);
        entry.Name.ToString().Should().Be("test.txt");
        entry.LinkCount.Should().Be(linkCount);
    }
    
    [Fact]
    public void Entry_IsDirectory_ReturnsTrue_WhenDirectoryAttributeSet()
    {
        // Arrange
        var entry = new Entry(1, 0, Topiary.App.Scanner.FileAttributes.Directory, 0, 0, 0, 0, "folder".AsMemory());
        
        // Act & Assert
        entry.IsDirectory.Should().BeTrue();
    }
    
    [Fact]
    public void Entry_IsDirectory_ReturnsFalse_WhenDirectoryAttributeNotSet()
    {
        // Arrange
        var entry = new Entry(1, 0, Topiary.App.Scanner.FileAttributes.Normal, 1024, 4096, 0, 0, "file.txt".AsMemory());
        
        // Act & Assert
        entry.IsDirectory.Should().BeFalse();
    }
    
    [Fact]
    public void Entry_IsReparsePoint_ReturnsTrue_WhenReparsePointAttributeSet()
    {
        // Arrange
        var entry = new Entry(1, 0, Topiary.App.Scanner.FileAttributes.ReparsePoint, 0, 0, 0, 0, "symlink".AsMemory());
        
        // Act & Assert
        entry.IsReparsePoint.Should().BeTrue();
        entry.IsSymlink.Should().BeTrue();
    }
    
    [Fact]
    public void Entry_DateTime_Conversion_Works()
    {
        // Arrange
        var testTime = DateTime.Now; // Use local time to match DateTime.FromFileTime behavior
        var fileTime = testTime.ToFileTime();
        var entry = new Entry(1, 0, Topiary.App.Scanner.FileAttributes.Normal, 0, 0, fileTime, fileTime, "test".AsMemory());
        
        // Act
        var convertedCreation = entry.CreationDateTime;
        var convertedWrite = entry.LastWriteDateTime;
        
        // Assert
        // Allow small variance for conversion precision
        convertedCreation.Should().BeCloseTo(testTime, TimeSpan.FromMilliseconds(100));
        convertedWrite.Should().BeCloseTo(testTime, TimeSpan.FromMilliseconds(100));
    }
    
    [Fact]
    public void Entry_ToString_FormatsCorrectly()
    {
        // Arrange - File
        var fileEntry = new Entry(1, 0, Topiary.App.Scanner.FileAttributes.Normal, 1024, 4096, 0, 0, "test.txt".AsMemory());
        
        // Arrange - Directory
        var dirEntry = new Entry(2, 0, Topiary.App.Scanner.FileAttributes.Directory, 0, 0, 0, 0, "folder".AsMemory());
        
        // Act
        var fileString = fileEntry.ToString();
        var dirString = dirEntry.ToString();
        
        // Assert
        fileString.Should().Contain("FILE");
        fileString.Should().Contain("1,024");
        fileString.Should().Contain("test.txt");
        
        dirString.Should().Contain("DIR");
        dirString.Should().Contain("---");
        dirString.Should().Contain("folder");
    }
    
    [Theory]
    [InlineData(Topiary.App.Scanner.FileAttributes.ReadOnly)]
    [InlineData(Topiary.App.Scanner.FileAttributes.Hidden)]
    [InlineData(Topiary.App.Scanner.FileAttributes.System)]
    [InlineData(Topiary.App.Scanner.FileAttributes.Archive)]
    [InlineData(Topiary.App.Scanner.FileAttributes.Compressed)]
    [InlineData(Topiary.App.Scanner.FileAttributes.Encrypted)]
    public void Entry_FileAttributes_AllValuesSupported(Topiary.App.Scanner.FileAttributes attribute)
    {
        // Arrange & Act
        var entry = new Entry(1, 0, attribute, 0, 0, 0, 0, "test".AsMemory());
        
        // Assert
        entry.Attributes.Should().Be(attribute);
    }
    
    [Fact]
    public void Entry_CombinedAttributes_Work()
    {
        // Arrange
        var combined = Topiary.App.Scanner.FileAttributes.ReadOnly | Topiary.App.Scanner.FileAttributes.Hidden | Topiary.App.Scanner.FileAttributes.System;
        var entry = new Entry(1, 0, combined, 0, 0, 0, 0, "system".AsMemory());
        
        // Act & Assert
        entry.Attributes.Should().Be(combined);
        entry.Attributes.HasFlag(Topiary.App.Scanner.FileAttributes.ReadOnly).Should().BeTrue();
        entry.Attributes.HasFlag(Topiary.App.Scanner.FileAttributes.Hidden).Should().BeTrue();
        entry.Attributes.HasFlag(Topiary.App.Scanner.FileAttributes.System).Should().BeTrue();
        entry.Attributes.HasFlag(Topiary.App.Scanner.FileAttributes.Directory).Should().BeFalse();
    }
}