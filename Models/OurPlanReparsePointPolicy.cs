using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Runtime.InteropServices;

namespace OurPlanCore;

internal static class OurPlanReparsePointPolicy
{
    internal const uint CloudTag = 0x9000001A;
    internal const uint LegacyFilePlaceholderTag = 0x80000015;

    private const uint CloudVariantMask = 0xFFFF0FFF;
    private const uint FileReadAttributes = 0x80;
    private const uint ShareRead = 0x1;
    private const uint ShareWrite = 0x2;
    private const uint ShareDelete = 0x4;
    private const uint OpenExisting = 3;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint BackupSemantics = 0x02000000;
    private const int FileAttributeTagInfoClass = 9;

    public static bool IsAllowedCloudItem(FileSystemInfo item)
    {
        if ((item.Attributes & FileAttributes.ReparsePoint) == 0)
            return true;
        if (!OperatingSystem.IsWindows())
            return false;

        return TryReadReparseTag(item.FullName, out uint tag) && IsAllowedCloudFileTag(tag);
    }

    internal static bool IsAllowedCloudFileTag(uint tag) =>
        (tag & CloudVariantMask) == CloudTag || tag == LegacyFilePlaceholderTag;

    private static bool TryReadReparseTag(string path, out uint tag)
    {
        tag = 0;
        using SafeFileHandle handle = CreateFileW(
            path,
            FileReadAttributes,
            ShareRead | ShareWrite | ShareDelete,
            IntPtr.Zero,
            OpenExisting,
            OpenReparsePoint | BackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
            return false;

        if (!GetFileInformationByHandleEx(
                handle,
                FileAttributeTagInfoClass,
                out FileAttributeTagInfo info,
                (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
        {
            return false;
        }

        tag = info.ReparseTag;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out FileAttributeTagInfo fileInformation,
        uint bufferSize);
}
