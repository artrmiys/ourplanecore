using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OurPlanCore;

/// <summary>
/// Identifies one local file generation. On Windows, ChangeTime and FileId make
/// this suitable for deciding whether a previously calculated SHA-256 can be
/// reused; length and LastWriteTime alone are deliberately not sufficient.
/// </summary>
public sealed class OurPlanLocalFileStamp
{
    public long Length { get; set; }
    public long LastWriteUtcTicks { get; set; }
    public long CreationUtcTicks { get; set; }
    public long ChangeTimeFileTime { get; set; }
    public uint VolumeSerialNumber { get; set; }
    public uint FileIdHigh { get; set; }
    public uint FileIdLow { get; set; }
    public bool IsStrong { get; set; }

    public bool SameGeneration(OurPlanLocalFileStamp? other) =>
        other != null &&
        IsStrong &&
        other.IsStrong &&
        Length == other.Length &&
        LastWriteUtcTicks == other.LastWriteUtcTicks &&
        CreationUtcTicks == other.CreationUtcTicks &&
        ChangeTimeFileTime == other.ChangeTimeFileTime &&
        VolumeSerialNumber == other.VolumeSerialNumber &&
        FileIdHigh == other.FileIdHigh &&
        FileIdLow == other.FileIdLow;

    public static OurPlanLocalFileStamp Read(string path)
    {
        string fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException("The file does not exist.", fullPath);

        var fallback = new OurPlanLocalFileStamp
        {
            Length = info.Length,
            LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
            CreationUtcTicks = info.CreationTimeUtc.Ticks,
        };
        if (!OperatingSystem.IsWindows())
            return fallback;

        try
        {
            using SafeFileHandle handle = File.OpenHandle(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.None);
            if (!GetFileInformationByHandle(handle, out ByHandleFileInformation identity))
                return fallback;
            if (!GetFileInformationByHandleEx(
                    handle,
                    FileInfoByHandleClass.FileBasicInfo,
                    out FileBasicInfo basic,
                    (uint)Marshal.SizeOf<FileBasicInfo>()))
            {
                return fallback;
            }

            info.Refresh();
            if (!info.Exists)
                throw new FileNotFoundException("The file disappeared while its identity was read.", fullPath);
            fallback.Length = info.Length;
            fallback.LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks;
            fallback.CreationUtcTicks = info.CreationTimeUtc.Ticks;
            fallback.ChangeTimeFileTime = basic.ChangeTime;
            fallback.VolumeSerialNumber = identity.VolumeSerialNumber;
            fallback.FileIdHigh = identity.FileIndexHigh;
            fallback.FileIdLow = identity.FileIndexLow;
            fallback.IsStrong = true;
            return fallback;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out ByHandleFileInformation lpFileInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        FileInfoByHandleClass fileInformationClass,
        out FileBasicInfo lpFileInformation,
        uint dwBufferSize);

    private enum FileInfoByHandleClass
    {
        FileBasicInfo = 0,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInfo
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public NativeFileTime CreationTime;
        public NativeFileTime LastAccessTime;
        public NativeFileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
