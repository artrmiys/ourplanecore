using System.IO;

namespace OurPlanCore;

internal static class ProjectPathSafety
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".gif", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp",
    };

    public static string? ResolveInside(string projectRoot, string value, string basePath)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        string resolved = Path.IsPathFullyQualified(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(basePath, value));
        return IsInside(resolved, root) ? resolved : null;
    }

    public static bool TryResolveInside(
        string projectRoot,
        string value,
        string basePath,
        out string resolved)
    {
        try
        {
            resolved = ResolveInside(projectRoot, value, basePath) ?? "";
            return !string.IsNullOrWhiteSpace(resolved);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            resolved = "";
            return false;
        }
    }

    public static bool IsSafeImagePath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        ImageExtensions.Contains(Path.GetExtension(path));

    private static bool IsInside(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
