using System.IO;

namespace OurPlanCore;

internal static class ThreeDModelPathPortability
{
    public static void RehydrateRuntimePaths(string jobRoot, ThreeDWallModel model)
    {
        string root = NormalizeRoot(jobRoot);
        foreach (ThreeDWallSegment wall in model.Walls)
        {
            string oldTakeoff = wall.TakeoffFolder;
            string oldPage = wall.PageFolder;
            wall.TakeoffFolder = ResolveComposite(oldTakeoff, root, "wall takeoff folder");
            wall.PageFolder = ResolvePath(oldPage, root, "wall page folder");
            wall.GroupKey = RebaseKnownGroupKey(
                wall.GroupKey,
                oldTakeoff,
                wall.TakeoffFolder,
                oldPage,
                wall.PageFolder);
        }

        foreach (ThreeDFloorSlab slab in model.Slabs)
        {
            string oldTakeoff = slab.TakeoffFolder;
            string oldPage = slab.PageFolder;
            slab.TakeoffFolder = ResolveComposite(oldTakeoff, root, "slab takeoff folder");
            slab.PageFolder = ResolvePath(oldPage, root, "slab page folder");
            slab.GroupKey = RebaseKnownGroupKey(
                slab.GroupKey,
                oldTakeoff,
                slab.TakeoffFolder,
                oldPage,
                slab.PageFolder);
        }

        foreach (ThreeDRoofGuide guide in model.RoofGuides)
            guide.PageFolder = ResolvePath(guide.PageFolder, root, "roof guide page folder");
        foreach (ThreeDRoofIssue issue in model.RoofIssues)
            issue.PageFolder = ResolvePath(issue.PageFolder, root, "roof issue page folder");
    }

    public static string RebaseKnownGroupKey(
        string groupKey,
        string oldTakeoff,
        string newTakeoff,
        string oldPage,
        string newPage)
    {
        string rebased = ReplaceKnownTrailingPath(groupKey, oldTakeoff, newTakeoff);
        if (!string.IsNullOrWhiteSpace(oldPage))
        {
            string prefix = ThreeDRoofFootprintBuildService.GeneratedSlabGroupPrefix;
            string expected = prefix + oldPage + "|";
            if (rebased.StartsWith(expected, StringComparison.OrdinalIgnoreCase))
                rebased = prefix + newPage + rebased[(expected.Length - 1)..];
        }
        return rebased;
    }

    private static string ReplaceKnownTrailingPath(string groupKey, string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(groupKey) || string.IsNullOrWhiteSpace(oldPath))
            return groupKey;
        if (groupKey.Equals(oldPath, StringComparison.OrdinalIgnoreCase))
            return newPath;

        string suffix = "|" + oldPath;
        return groupKey.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? groupKey[..^oldPath.Length] + newPath
            : groupKey;
    }

    private static string ResolveComposite(string value, string root, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return string.Join(
            "|",
            value.Split('|').Select(part =>
                string.IsNullOrWhiteSpace(part)
                    ? ""
                    : ResolvePath(part, root, description)));
    }

    private static string ResolvePath(string value, string root, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        try
        {
            string resolved = Path.IsPathRooted(value)
                ? Path.GetFullPath(value)
                : Path.GetFullPath(Path.Combine(root, value));
            if (!IsInside(resolved, root) &&
                !TryMapMovedLegacyPath(resolved, root, out resolved))
            {
                throw new OurPlanPackageValidationException(
                    $"The 3D model {description} points outside the project: {resolved}");
            }
            return resolved;
        }
        catch (OurPlanPackageValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            throw new OurPlanPackageValidationException(
                $"The 3D model has an invalid {description} '{value}': {ex.Message}",
                ex);
        }
    }

    private static string NormalizeRoot(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool TryMapMovedLegacyPath(string path, string root, out string mapped)
    {
        string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        foreach (string durableRoot in new[] { "Pages", "Takeoffs", "AI_Context" })
        {
            string marker = Path.DirectorySeparatorChar + durableRoot + Path.DirectorySeparatorChar;
            int index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;
            string relative = normalized[(index + 1)..];
            string candidate = Path.GetFullPath(Path.Combine(root, relative));
            if (IsInside(candidate, root) && (Directory.Exists(candidate) || File.Exists(candidate)))
            {
                mapped = candidate;
                return true;
            }
        }

        mapped = "";
        return false;
    }

    private static bool IsInside(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
