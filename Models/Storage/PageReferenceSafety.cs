using System.IO;

namespace OurPlanCore;

internal static class PageReferenceSafety
{
    public static void ValidateSource(string pageFolder, SourceInfo source)
    {
        string? root = FindRoot(pageFolder);
        if (root == null) return; // Standalone rendering inputs have their own explicitly selected source root.
        Validate(root, pageFolder, source.Pdf);
        Validate(root, pageFolder, source.OverlayPageFolder);
        if (source.RasterSheet is { } raster)
        {
            Validate(root, pageFolder, raster.Image);
            Validate(root, pageFolder, raster.OverviewImage);
            Validate(root, pageFolder, raster.SnapIndex);
        }
    }

    public static string Resolve(string pageFolder, string value)
    {
        string? root = FindRoot(pageFolder);
        return root == null ? Path.GetFullPath(value, pageFolder) : SafeJobPathResolver.ResolveInside(root, value, pageFolder);
    }

    private static void Validate(string root, string folder, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) _ = SafeJobPathResolver.ResolveInside(root, value, folder);
    }

    private static string? FindRoot(string folder)
    {
        string? registered = JobWriteAccess.RegisteredRootForPath(folder);
        if (registered != null) return registered;
        string? current = Path.GetFullPath(folder);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current, "Pages")) && Directory.Exists(Path.Combine(current, "Takeoffs"))) return current;
            current = Path.GetDirectoryName(current);
        }
        return null;
    }
}
