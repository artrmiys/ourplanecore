using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OurPlanCore;

public static class TakeoffCleanupService
{
    public static IReadOnlyList<TakeoffItem> FindItemsWithoutMeasurements(
        IEnumerable<TakeoffItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items
            .Where(item => item.Measurements.Count == 0)
            .ToList();
    }

    public static IReadOnlyList<TakeoffItem> FindSafeItemsWithoutMeasurements(
        IEnumerable<TakeoffItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var allItems = items.ToList();
        HashSet<string> protectedMultiLinePaths = BuildProtectedMultiLinePaths(allItems);

        return allItems
            .Where(item => item.Measurements.Count == 0)
            .Where(item => IsConfirmedEmptyOnDisk(item, protectedMultiLinePaths))
            .ToList();
    }

    private static HashSet<string> BuildProtectedMultiLinePaths(IEnumerable<TakeoffItem> items)
    {
        var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TakeoffItem owner in items.Where(item => item.MultiLineOffsets.Count > 0))
        {
            AddNormalizedPath(protectedPaths, owner.FolderPath);
            foreach (MultiLineOffsetConfig offset in owner.MultiLineOffsets)
                AddNormalizedPath(protectedPaths, offset.CompanionFolder);
        }

        return protectedPaths;
    }

    private static bool IsConfirmedEmptyOnDisk(
        TakeoffItem item,
        IReadOnlySet<string> protectedMultiLinePaths)
    {
        string? folder = TryNormalizePath(item.FolderPath);
        if (folder == null || protectedMultiLinePaths.Contains(folder))
            return false;

        try
        {
            if (!Directory.Exists(folder) || HasCorruptMeasurementsArtifact(folder))
                return false;

            string? storedCount = OurPlanCoreJobStore.ReadProperty(folder, "MeasurementCount");
            if (!string.IsNullOrWhiteSpace(storedCount) &&
                (!int.TryParse(storedCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out int metadataCount) ||
                 metadataCount != 0))
            {
                return false;
            }

            string measurementsPath = TakeoffStore.MeasurementsJsonPath(folder);
            if (File.Exists(measurementsPath))
                return TakeoffStore.TryReadMeasurementCount(folder, out int count) && count == 0;

            return true;
        }
        catch (Exception ex) when (ex is
            IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException or
            ArgumentException or
            PathTooLongException)
        {
            AppLog.Warn(ex, $"Could not verify whether takeoff is empty: {item.FolderPath}");
            return false;
        }
    }

    private static bool HasCorruptMeasurementsArtifact(string folder) =>
        Directory.EnumerateFiles(folder, "measurements.json.corrupt-*", SearchOption.TopDirectoryOnly).Any();

    private static void AddNormalizedPath(ISet<string> paths, string? path)
    {
        string? normalized = TryNormalizePath(path);
        if (normalized != null)
            paths.Add(normalized);
    }

    private static string? TryNormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is
            ArgumentException or
            NotSupportedException or
            PathTooLongException or
            System.Security.SecurityException)
        {
            return null;
        }
    }
}
