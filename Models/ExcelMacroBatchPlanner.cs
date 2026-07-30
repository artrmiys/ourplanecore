using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OurPlanCore;

public sealed record ExcelMacroBatchScopeResult(
    bool Success,
    string Message,
    string RootPath)
{
    public static ExcelMacroBatchScopeResult Failure(string message) =>
        new(false, message, "");
}

public static class ExcelMacroBatchPlanner
{
    public static IReadOnlyList<string> ActionIds(ExcelMacroExportConfig config)
    {
        var known = config.Actions
            .Select(action => action.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> ordered = (config.BatchActionOrder ?? [])
            .Where(known.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return ordered.Count > 0
            ? ordered
            : [.. ExcelMacroExportConfig.BuildDefault().BatchActionOrder];
    }

    public static ExcelMacroBatchScopeResult ResolveScope(
        OurPlanCoreJob job,
        IReadOnlyList<TakeoffItem> takeoffItems,
        IReadOnlyList<string> selectedRoots,
        ExcelMacroExportConfig config)
    {
        if (selectedRoots.Count == 0)
        {
            return ExcelMacroBatchScopeResult.Failure(
                "Select one building, export folder, or takeoff item before running Excel ALL.");
        }

        string takeoffsRoot = NormalizePath(job.TakeoffsRoot);
        List<string> validRoots = selectedRoots
            .Select(NormalizePath)
            .Where(path => IsSameOrChild(path, takeoffsRoot))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (validRoots.Count == 0)
        {
            return ExcelMacroBatchScopeResult.Failure(
                "The selected Takeoffs scope is outside the open job.");
        }

        HashSet<string> aliases = config.Actions
            .SelectMany(action => action.FolderAliases ?? [])
            .Concat(config.Framing?.FramingFolderAliases ?? [])
            .Concat(
                (config.Framing?.Categories ?? [])
                .SelectMany(category => category.FolderAliases ?? []))
            .Select(alias => alias.Trim())
            .Where(alias => alias.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> candidates = takeoffItems
            .Where(item => item.Measurements.Count > 0)
            .Where(item => validRoots.Any(root => IsSameOrChild(item.FolderPath, root)))
            .Select(item => BatchRootForPath(takeoffsRoot, item.FolderPath, aliases))
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = validRoots
                .Select(path => BatchRootForPath(takeoffsRoot, path, aliases))
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (candidates.Count != 1)
        {
            return ExcelMacroBatchScopeResult.Failure(
                "Excel ALL found more than one building/root in the selection. " +
                "Select only one building or one of its export folders.");
        }

        return new ExcelMacroBatchScopeResult(
            true,
            $"Excel ALL scope: {OurPlanCoreJobStore.DisplayName(candidates[0])}.",
            candidates[0]);
    }

    private static string BatchRootForPath(
        string takeoffsRoot,
        string candidate,
        IReadOnlySet<string> aliases)
    {
        string path = NormalizePath(candidate);
        if (!IsSameOrChild(path, takeoffsRoot))
            return "";
        if (string.Equals(path, takeoffsRoot, StringComparison.OrdinalIgnoreCase))
            return takeoffsRoot;

        string relative = Path.GetRelativePath(takeoffsRoot, path);
        string[] segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int roleIndex = Array.FindIndex(
            segments,
            segment => aliases.Contains(segment));
        if (roleIndex < 0)
            return path;
        if (roleIndex == 0)
            return takeoffsRoot;

        string result = takeoffsRoot;
        for (int index = 0; index < roleIndex; index++)
            result = Path.Combine(result, segments[index]);
        return NormalizePath(result);
    }

    private static bool IsSameOrChild(string candidate, string root)
    {
        string candidatePath = NormalizePath(candidate);
        string rootPath = NormalizePath(root);
        return string.Equals(candidatePath, rootPath, StringComparison.OrdinalIgnoreCase) ||
               candidatePath.StartsWith(
                   rootPath + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return (path ?? "")
                .Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
