using System.IO;

namespace OurPlanCore;

/// <summary>One containment rule for persisted job references and AI attachments.</summary>
internal static class SafeJobPathResolver
{
    private static readonly HashSet<string> Devices = new(StringComparer.OrdinalIgnoreCase)
    { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    public static bool IsSafeId(string value) => !string.IsNullOrEmpty(value) && value.Length <= 128 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') && !Devices.Contains(value);

    public static string ResolveRelative(string root, string value, string? basePath = null)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains(':'))
            throw new InvalidDataException("A project reference must be a relative path.");
        return ResolveInside(root, value, basePath ?? root);
    }

    public static string ResolveInside(string root, string value, string basePath)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        basePath = Path.GetFullPath(basePath);
        if (!Inside(root, basePath)) throw new InvalidDataException("Reference base escapes the project.");
        ValidateSegments(value);
        string path = Path.GetFullPath(value, basePath);
        if (!Inside(root, path)) throw new InvalidDataException("Reference escapes the project.");
        // Reject links, including an existing ancestor of a not-yet-created file.
        // Lexical containment alone would permit a directory junction to escape the root.
        string? current = path;
        while (current != null)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    FileSystemInfo info = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(current) : new FileInfo(current);
                    if (!OurPlanReparsePointPolicy.IsAllowedCloudItem(info))
                        throw new InvalidDataException("Project paths cannot traverse a symbolic link or junction.");
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
            if (current.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current);
        }
        return path;
    }

    public static string RequireFile(string root, string value, string basePath, long maxBytes, params string[] extensions)
    {
        string path = ResolveInside(root, value, basePath);
        if (!extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported project attachment type.");
        long length = new FileInfo(path).Length;
        if (length <= 0 || length > maxBytes)
            throw new InvalidDataException($"Project attachment must contain 1–{maxBytes} bytes.");
        return path;
    }

    private static void ValidateSegments(string value)
    {
        string path = Path.IsPathFullyQualified(value) ? value[Path.GetPathRoot(value)!.Length..] : value;
        foreach (string segment in path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..") continue; // Normalized containment above decides legacy relative references.
            string stem = segment.Split('.')[0];
            if (segment.EndsWith(' ') || segment.EndsWith('.') || Devices.Contains(stem) ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("Invalid or reserved project path component.");
        }
    }

    internal static bool Inside(string root, string path) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
