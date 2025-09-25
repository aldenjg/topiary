using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Topiary.App.Interop;

/// <summary>
/// Windows API P/Invoke definitions for high-performance file system access.
/// Guards all Windows-specific code behind OperatingSystem.IsWindows() checks.
/// </summary>
internal static class Win32
{
    #region File System Access
    
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        FILE_INFO_BY_HANDLE_CLASS FileInformationClass,
        IntPtr lpFileInformation,
        uint dwBufferSize);
    
    #endregion
    
    #region Directory Enumeration (Fallback)
    
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindFirstFileExW(
        string lpFileName,
        FINDEX_INFO_LEVELS fInfoLevelId,
        out WIN32_FIND_DATA lpFindFileData,
        FINDEX_SEARCH_OPS fSearchOp,
        IntPtr lpSearchFilter,
        FIND_FIRST_EX_FLAGS dwAdditionalFlags);
    
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FindClose(IntPtr hFindFile);
    
    #endregion
    
    #region Constants
    
    internal const uint GENERIC_READ = 0x80000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint FILE_SHARE_DELETE = 0x00000004;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    internal const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    
    internal const uint FSCTL_ENUM_USN_DATA = 0x900b3;
    internal const uint FSCTL_QUERY_USN_JOURNAL = 0x900f4;
    internal const uint FSCTL_READ_USN_JOURNAL = 0x900bb;
    internal const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x90064;
    internal const uint FSCTL_GET_NTFS_FILE_RECORD = 0x90068;
    
    // MFT/NTFS attribute types
    internal const uint ATTRIBUTE_TYPE_STANDARD_INFORMATION = 0x10;
    internal const uint ATTRIBUTE_TYPE_ATTRIBUTE_LIST = 0x20;
    internal const uint ATTRIBUTE_TYPE_FILE_NAME = 0x30;
    internal const uint ATTRIBUTE_TYPE_OBJECT_ID = 0x40;
    internal const uint ATTRIBUTE_TYPE_VOLUME_NAME = 0x60;
    internal const uint ATTRIBUTE_TYPE_DATA = 0x80;
    
    // File attributes
    internal const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    internal const uint FILE_ATTRIBUTE_COMPRESSED = 0x800;
    internal const uint FILE_ATTRIBUTE_SPARSE_FILE = 0x200;
    
    internal const FIND_FIRST_EX_FLAGS FIND_FIRST_EX_LARGE_FETCH = FIND_FIRST_EX_FLAGS.FIND_FIRST_EX_LARGE_FETCH;
    
    #endregion
    
    #region Structures
    
    [StructLayout(LayoutKind.Sequential)]
    internal struct MFT_ENUM_DATA_V1
    {
        public ulong StartFileReferenceNumber;
        public ulong LowUsn;
        public ulong HighUsn;
        public ushort MinMajorVersion;
        public ushort MaxMajorVersion;
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct USN_RECORD_V2
    {
        public uint RecordLength;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public ulong FileReferenceNumber;
        public ulong ParentFileReferenceNumber;
        public long Usn;
        public long TimeStamp;
        public uint Reason;
        public uint SourceInfo;
        public uint SecurityId;
        public uint FileAttributes;
        public ushort FileNameLength;
        public ushort FileNameOffset;
        // Followed by FileName (UTF-16 string)
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct USN_RECORD_V3
    {
        public uint RecordLength;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public UInt128 FileReferenceNumber;
        public UInt128 ParentFileReferenceNumber;
        public long Usn;
        public long TimeStamp;
        public uint Reason;
        public uint SourceInfo;
        public uint SecurityId;
        public uint FileAttributes;
        public ushort FileNameLength;
        public ushort FileNameOffset;
        // Followed by FileName (UTF-16 string)
    }
    
    [StructLayout(LayoutKind.Sequential)]
    internal struct USN_JOURNAL_DATA_V1
    {
        public ulong UsnJournalID;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
        public ushort MinSupportedMajorVersion;
        public ushort MaxSupportedMajorVersion;
    }
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WIN32_FIND_DATA
    {
        public uint dwFileAttributes;
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
        
        public long FileSize => ((long)nFileSizeHigh << 32) | nFileSizeLow;
        
        public long CreationFileTime => ((long)ftCreationTime.dwHighDateTime << 32) | (uint)ftCreationTime.dwLowDateTime;
        public long LastWriteFileTime => ((long)ftLastWriteTime.dwHighDateTime << 32) | (uint)ftLastWriteTime.dwLowDateTime;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_ID_INFO
    {
        public ulong VolumeSerialNumber;
        public UInt128 FileId;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_STANDARD_INFO
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        [MarshalAs(UnmanagedType.Bool)]
        public bool DeletePending;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Directory;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_BASIC_INFO
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    internal struct NTFS_VOLUME_DATA_BUFFER
    {
        public long VolumeSerialNumber;
        public long NumberSectors;
        public long TotalClusters;
        public long FreeClusters;
        public long TotalReserved;
        public uint BytesPerSector;
        public uint BytesPerCluster;
        public uint BytesPerFileRecordSegment;
        public uint ClustersPerFileRecordSegment;
        public long MftValidDataLength;
        public long MftStartLcn;
        public long Mft2StartLcn;
        public long MftZoneStart;
        public long MftZoneEnd;
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct NTFS_FILE_RECORD_INPUT_BUFFER
    {
        public ulong FileReferenceNumber;
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct NTFS_FILE_RECORD_OUTPUT_BUFFER
    {
        public ulong FileReferenceNumber;
        public uint FileRecordLength;
        // Followed by FILE_RECORD_SEGMENT_HEADER
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct FILE_RECORD_SEGMENT_HEADER
    {
        public uint Signature; // "FILE" (0x454C4946)
        public ushort UpdateSequenceOffset;
        public ushort UpdateSequenceSize;
        public ulong LogFileSequenceNumber;
        public ushort SequenceNumber;
        public ushort LinkCount;
        public ushort FirstAttributeOffset;
        public ushort Flags;
        public uint RealSize;
        public uint AllocatedSize;
        public ulong BaseFileRecord;
        public ushort NextAttributeId;
        public ushort Align;
        public uint RecordNumber;
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct ATTRIBUTE_RECORD_HEADER
    {
        public uint AttributeType;
        public uint RecordLength;
        public byte NonResidentFlag;
        public byte NameLength;
        public ushort NameOffset;
        public ushort Flags;
        public ushort AttributeId;
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct RESIDENT_ATTRIBUTE
    {
        public ATTRIBUTE_RECORD_HEADER Header;
        public uint ValueLength;
        public ushort ValueOffset;
        public byte IndexedFlag;
        public byte Reserved;
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct NON_RESIDENT_ATTRIBUTE
    {
        public ATTRIBUTE_RECORD_HEADER Header;
        public ulong StartingVCN;
        public ulong EndingVCN;
        public ushort DataRunsOffset;
        public ushort CompressionUnit;
        public uint Reserved;
        public ulong AllocatedSize;
        public ulong DataSize;
        public ulong InitializedSize;
        public ulong CompressedSize; // Only if compressed
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct STANDARD_INFORMATION
    {
        public long CreationTime;
        public long LastModificationTime;
        public long LastChangeTime;
        public long LastAccessTime;
        public uint FileAttributes;
        public uint MaximumVersions;
        public uint VersionNumber;
        public uint ClassId;
        public uint OwnerId;        // NTFS 3.0+
        public uint SecurityId;     // NTFS 3.0+
        public ulong QuotaCharged;  // NTFS 3.0+
        public ulong Usn;          // NTFS 3.0+
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct FILE_NAME_ATTRIBUTE
    {
        public ulong ParentDirectory;
        public long CreationTime;
        public long LastModificationTime;
        public long LastChangeTime;
        public long LastAccessTime;
        public ulong AllocatedSize;
        public ulong DataSize;
        public uint FileAttributes;
        public uint ReparsePointTag;
        public byte FileNameLength;
        public byte FileNameType;
        // Followed by FileName (UTF-16 string)
    }
    
    #endregion
    
    #region Enums
    
    internal enum FILE_INFO_BY_HANDLE_CLASS
    {
        FileBasicInfo = 0,
        FileStandardInfo = 1,
        FileNameInfo = 2,
        FileRenameInfo = 3,
        FileDispositionInfo = 4,
        FileAllocationInfo = 5,
        FileEndOfFileInfo = 6,
        FileStreamInfo = 7,
        FileCompressionInfo = 8,
        FileAttributeTagInfo = 9,
        FileIdBothDirectoryInfo = 10,
        FileIdBothDirectoryRestartInfo = 11,
        FileIoPriorityHintInfo = 12,
        FileRemoteProtocolInfo = 13,
        FileFullDirectoryInfo = 14,
        FileFullDirectoryRestartInfo = 15,
        FileStorageInfo = 16,
        FileAlignmentInfo = 17,
        FileIdInfo = 18,
        FileIdExtdDirectoryInfo = 19,
        FileIdExtdDirectoryRestartInfo = 20
    }
    
    internal enum FINDEX_INFO_LEVELS
    {
        FindExInfoStandard = 0,
        FindExInfoBasic = 1,
        FindExInfoMaxInfoLevel = 2
    }
    
    internal enum FINDEX_SEARCH_OPS
    {
        FindExSearchNameMatch = 0,
        FindExSearchLimitToDirectories = 1,
        FindExSearchLimitToDevices = 2,
        FindExSearchMaxSearchOp = 3
    }
    
    [Flags]
    internal enum FIND_FIRST_EX_FLAGS : uint
    {
        FIND_FIRST_EX_CASE_SENSITIVE = 0x1,
        FIND_FIRST_EX_LARGE_FETCH = 0x2,
        FIND_FIRST_EX_ON_DISK_ENTRIES_ONLY = 0x4
    }
    
    #endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Convert Windows file attributes to our FileAttributes enum
    /// </summary>
    internal static Scanner.FileAttributes ConvertAttributes(uint winAttributes)
    {
        return (Scanner.FileAttributes)winAttributes;
    }
    
    /// <summary>
    /// Get Windows error code from Marshal.GetLastWin32Error()
    /// </summary>
    internal static int GetLastError() => Marshal.GetLastWin32Error();
    
    #endregion
}