using System.IO;
using System.Text.Json;

namespace OurPlanCore;

internal static class OurPlanPackageFileSelector
{
    private const long MaxPageSourceMetadataBytes = 16L * 1024 * 1024;
    private static readonly HashSet<string> OsJunkNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop.ini",
        "Thumbs.db",
        ".DS_Store",
    };

    public static IReadOnlyList<OurPlanPackageSourceFile> Collect(string workspaceRoot)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);
        if (!File.Exists(Path.Combine(root, "Data.xml")))
            throw new OurPlanPackageValidationException("The working project has no root Data.xml file.");

        List<string> discoveredFiles = EnumerateFilesSafe(root).ToList();
        HashSet<string> activeRasterPaths = ReadActiveRasterPaths(root, discoveredFiles);
        ValidateCurrentPageSources(root, discoveredFiles, activeRasterPaths);

        var files = new List<OurPlanPackageSourceFile>();
        foreach (string fullPath in discoveredFiles)
        {
            string logicalPath = ToLogicalPath(root, fullPath);
            if (ShouldExclude(root, fullPath, logicalPath, activeRasterPaths))
                continue;

            var info = new FileInfo(fullPath);
            files.Add(new OurPlanPackageSourceFile(
                fullPath,
                logicalPath,
                info.Length,
                info.LastWriteTimeUtc.Ticks));
        }

        return files
            .OrderBy(file => file.LogicalPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static void ValidateRecoveryTreeSafety(string workspaceRoot)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "Data.xml")))
            throw new OurPlanPackageValidationException("The recovery workspace has no root Data.xml file.");

        // Recovery is explicitly selected because a crash may have left one JSON store
        // half-written. Keep the reparse/enumeration security boundary, then let the
        // normal job loaders quarantine damaged stores while preserving everything else.
        foreach (string _ in EnumerateFilesSafe(root))
        {
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            IEnumerable<FileSystemInfo> children;
            try
            {
                children = new DirectoryInfo(directory).EnumerateFileSystemInfos().ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new OurPlanPackageValidationException(
                    $"Cannot inspect project folder '{directory}': {ex.Message}", ex);
            }

            foreach (FileSystemInfo child in children)
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0 &&
                    !OurPlanReparsePointPolicy.IsAllowedCloudItem(child))
                {
                    throw new OurPlanPackageValidationException(
                        $"The project contains a symbolic link, junction, or unsupported reparse point, " +
                        $"which cannot be packed safely: {child.FullName}");
                }

                if (child is DirectoryInfo childDirectory)
                {
                    string relative = ToLogicalPath(root, childDirectory.FullName);
                    if (!ShouldSkipWholeDirectory(relative))
                        pending.Push(childDirectory.FullName);
                }
                else if (child is FileInfo childFile)
                {
                    yield return childFile.FullName;
                }
            }
        }
    }

    private static bool ShouldSkipWholeDirectory(string logicalPath)
    {
        string first = logicalPath.Split('/')[0];
        return first.Equals(".snapshots", StringComparison.OrdinalIgnoreCase) ||
               first.Equals(".undo", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldExclude(
        string root,
        string fullPath,
        string logicalPath,
        IReadOnlySet<string> activeRasterPaths)
    {
        string fileName = Path.GetFileName(fullPath);
        if (OsJunkNames.Contains(fileName) ||
            fileName.Equals(OurPlanPackageFormat.WorkspaceMarkerFileName, StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(OurPlanPackageFormat.WorkspaceClaimFileName, StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(".~lock", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(".~lock.guard", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsAppAtomicTemp(fileName))
            return true;

        if (!IsRasterCachePath(root, fullPath))
            return false;

        return !activeRasterPaths.Contains(Path.GetFullPath(fullPath));
    }

    private static bool IsAppAtomicTemp(string fileName)
    {
        if (!fileName.StartsWith(".", StringComparison.Ordinal) ||
            !fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = fileName.Split('.');
        return parts.Length >= 4 &&
               parts[^2].Length == 32 &&
               parts[^2].All(Uri.IsHexDigit);
    }

    private static HashSet<string> ReadActiveRasterPaths(
        string root,
        IReadOnlyList<string> discoveredFiles)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string pagesRoot = Path.Combine(root, "Pages");
        if (!Directory.Exists(pagesRoot))
            return active;

        foreach (string sourcePath in discoveredFiles.Where(path => IsOwnedPageSource(path, pagesRoot)))
        {
            try
            {
                using JsonDocument document = ReadPageSourceDocument(sourcePath);
                if (!TryGetProperty(document.RootElement, "raster_sheet", out JsonElement raster) ||
                    raster.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string pageFolder = Path.GetDirectoryName(sourcePath)!;
                AddRasterPath(pageFolder, raster, "image", active);
                AddRasterPath(pageFolder, raster, "overview_image", active);
                AddRasterPath(pageFolder, raster, "snap_index", active);
            }
            catch (JsonException ex)
            {
                throw new OurPlanPackageValidationException(
                    $"Cannot pack malformed page metadata '{sourcePath}': {ex.Message}", ex);
            }
        }

        return active;
    }

    private static void AddRasterPath(
        string pageFolder,
        JsonElement raster,
        string propertyName,
        ISet<string> active)
    {
        if (!TryGetProperty(raster, propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            return;
        }

        try
        {
            string fullPath = Path.GetFullPath(Path.Combine(pageFolder, value.GetString()!));
            if (File.Exists(fullPath))
                active.Add(fullPath);
        }
        catch
        {
            // Invalid cache metadata is ignored; the cache is rebuildable.
        }
    }

    private static void ValidateCurrentPageSources(
        string root,
        IReadOnlyList<string> discoveredFiles,
        IReadOnlySet<string> activeRasterPaths)
    {
        string pagesRoot = Path.Combine(root, "Pages");
        if (!Directory.Exists(pagesRoot))
            return;

        var errors = new List<string>();
        var discovered = discoveredFiles
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string sourcePath in discoveredFiles.Where(path => IsOwnedPageSource(path, pagesRoot)))
        {
            try
            {
                using JsonDocument document = ReadPageSourceDocument(sourcePath);
                if (!TryGetProperty(document.RootElement, "pdf", out JsonElement pdf) ||
                    pdf.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(pdf.GetString()))
                {
                    errors.Add($"{ToLogicalPath(root, sourcePath)} has no valid pdf reference");
                    continue;
                }

                string pageFolder = Path.GetDirectoryName(sourcePath)!;
                string resolved = Path.IsPathRooted(pdf.GetString())
                    ? Path.GetFullPath(pdf.GetString()!)
                    : Path.GetFullPath(Path.Combine(pageFolder, pdf.GetString()!));
                if (!IsInside(resolved, root))
                    errors.Add($"{ToLogicalPath(root, sourcePath)} points outside the project: {resolved}");
                else if (!File.Exists(resolved))
                    errors.Add($"{ToLogicalPath(root, sourcePath)} points to a missing PDF: {resolved}");
                else if (!discovered.Contains(resolved) ||
                         ShouldExclude(
                             root,
                             resolved,
                             ToLogicalPath(root, resolved),
                             activeRasterPaths))
                {
                    errors.Add(
                        $"{ToLogicalPath(root, sourcePath)} points to project data excluded from portable packages: " +
                        ToLogicalPath(root, resolved));
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                errors.Add($"{ToLogicalPath(root, sourcePath)} cannot be read: {ex.Message}");
            }
        }

        if (errors.Count == 0)
            return;

        string details = string.Join(Environment.NewLine, errors.Take(8));
        if (errors.Count > 8)
            details += $"{Environment.NewLine}...and {errors.Count - 8} more";
        throw new OurPlanPackageValidationException(
            "This project is not portable yet because one or more active page PDFs are external or missing:" +
            Environment.NewLine + details);
    }

    private static bool IsOwnedPageSource(string path, string pagesRoot)
    {
        if (!IsInside(path, pagesRoot) ||
            !Path.GetFileName(path).Equals("source.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        string? pageFolder = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(pageFolder);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static JsonDocument ReadPageSourceDocument(string sourcePath)
    {
        var info = new FileInfo(sourcePath);
        if (info.Length > MaxPageSourceMetadataBytes)
        {
            throw new OurPlanPackageValidationException(
                $"Page metadata is unexpectedly large and cannot be packed safely: {sourcePath}");
        }

        using var stream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        return JsonDocument.Parse(
            stream,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 256,
            });
    }

    private static bool IsRasterCachePath(string root, string path)
    {
        string pagesRoot = Path.Combine(root, "Pages");
        if (!IsInside(path, pagesRoot))
            return false;

        DirectoryInfo? folder = Directory.GetParent(path);
        while (folder != null && IsInside(folder.FullName, pagesRoot))
        {
            if (folder.Name.Equals(RasterSheetCacheService.CacheFolderName, StringComparison.OrdinalIgnoreCase) &&
                folder.Parent != null &&
                File.Exists(Path.Combine(folder.Parent.FullName, "source.json")))
            {
                return true;
            }
            folder = folder.Parent;
        }
        return false;
    }

    private static string ToLogicalPath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsInside(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
