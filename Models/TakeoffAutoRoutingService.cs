using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OurPlaneCore;

public sealed record TakeoffAutoRouteResult(string ParentFolder, bool Routed, string Message);

public readonly record struct TakeoffAutoSortKey(
    int Category,
    int Group,
    int Rank,
    int Secondary,
    string Name);

public static class TakeoffAutoRoutingService
{
    private static readonly string[] SqftAreaTokens =
    [
        "base", "basement", "sqft", "sf", "sft", "deck", "porch",
        "blcny", "balcony", "cant", "cantilevered", "flat", "rf", "roof"
    ];

    private static readonly string[] WallTokens =
    [
        "wall", "walls", "corner", "corners", "ext", "cor", "corr",
        "dem", "demo", "2x8", "2x6", "2x4", "half"
    ];

    public static TakeoffAutoRouteResult ResolveRoute(
        OurPlaneCoreJob job,
        string requestedParentFolder,
        string itemName,
        string measurementType,
        string activePageName,
        string activePageFolder)
    {
        string parentFolder = Directory.Exists(requestedParentFolder)
            ? requestedParentFolder
            : job.TakeoffsRoot;
        string type = OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType);

        if (type == "area" && LooksLikeSqftArea(itemName))
        {
            string sqfts = EnsureChildFolder(job.TakeoffsRoot, "sqfts");
            return new TakeoffAutoRouteResult(sqfts, true, "Auto routed to Takeoffs/sqfts.");
        }

        if (type == "line" && LooksLikeWallItem(itemName))
        {
            string walls = EnsureChildFolder(job.TakeoffsRoot, "walls");
            if (TryResolveFloorLevel(job, activePageName, activePageFolder, out FloorLevel floor))
            {
                string wallFloor = EnsureFirstMatchingChildFolder(walls, WallFloorFolderCandidates(floor));
                return new TakeoffAutoRouteResult(
                    wallFloor,
                    true,
                    $"Auto routed to Takeoffs/walls/{OurPlaneCoreJobStore.DisplayName(wallFloor)}.");
            }

            return new TakeoffAutoRouteResult(walls, true, "Auto routed to Takeoffs/walls; no sheet floor was detected.");
        }

        return new TakeoffAutoRouteResult(parentFolder, false, "");
    }

    public static bool SortFolder(OurPlaneCoreJob job, string parentFolder)
    {
        if (string.IsNullOrWhiteSpace(parentFolder) || !Directory.Exists(parentFolder))
            return false;

        string relative = RelativeTakeoffPath(job, parentFolder);
        Func<string, TakeoffAutoSortKey>? keySelector = null;
        if (IsSqftsFolder(relative))
            keySelector = folder => SqftSortKey(OurPlaneCoreJobStore.DisplayName(folder));
        else if (IsWallsRootFolder(relative))
            keySelector = folder => WallFloorSortKey(OurPlaneCoreJobStore.DisplayName(folder));
        else if (IsWallFloorFolder(relative))
            keySelector = folder => WallItemSortKey(OurPlaneCoreJobStore.DisplayName(folder));

        if (keySelector == null)
            return false;

        var current = OurPlaneCoreJobStore.GetOrderedChildDirectories(parentFolder).ToList();
        var ordered = current
            .Select(folder => (Folder: folder, Key: keySelector(folder)))
            .OrderBy(entry => entry.Key.Category)
            .ThenBy(entry => entry.Key.Group)
            .ThenBy(entry => entry.Key.Rank)
            .ThenBy(entry => entry.Key.Secondary)
            .ThenBy(entry => entry.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Folder)
            .ToList();

        bool changed = !current.SequenceEqual(ordered, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ordered.Count; i++)
            OurPlaneCoreJobStore.SetOrderIndex(ordered[i], i + 1);

        return changed;
    }

    public static bool SortRouteContext(OurPlaneCoreJob job, string itemFolder)
    {
        string? parent = string.IsNullOrWhiteSpace(itemFolder)
            ? null
            : Path.GetDirectoryName(itemFolder);
        if (string.IsNullOrWhiteSpace(parent))
            return false;

        bool changed = SortFolder(job, parent);
        string? grandParent = Path.GetDirectoryName(parent);
        if (!string.IsNullOrWhiteSpace(grandParent))
            changed = SortFolder(job, grandParent) || changed;
        return changed;
    }

    public static TakeoffAutoSortKey PageLegendSortKey(TakeoffItem item)
    {
        string type = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        if (type == "line")
        {
            TakeoffAutoSortKey key = WallItemSortKey(item.Name);
            return key with { Category = 0 };
        }

        if (type == "area")
        {
            TakeoffAutoSortKey key = SqftSortKey(item.Name);
            return key with { Category = 1 };
        }

        if (type == "point")
            return new TakeoffAutoSortKey(2, 0, 0, 0, NormalizeName(item.Name));

        return new TakeoffAutoSortKey(9, 0, 0, 0, NormalizeName(item.Name));
    }

    public static IReadOnlyList<TakeoffItem> SortPageLegendItems(IEnumerable<TakeoffItem> items) =>
        items
            .Select(item => (Item: item, Key: PageLegendSortKey(item)))
            .OrderBy(entry => entry.Key.Category)
            .ThenBy(entry => entry.Key.Group)
            .ThenBy(entry => entry.Key.Rank)
            .ThenBy(entry => entry.Key.Secondary)
            .ThenBy(entry => entry.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Item)
            .ToList();

    public static TakeoffAutoSortKey WallItemSortKey(string name)
    {
        string low = NormalizeName(name);
        HashSet<string> tokens = TokenSet(low);

        if (Regex.IsMatch(low, @"^\s*corners?\b", RegexOptions.IgnoreCase) ||
            tokens.Contains("corner") ||
            tokens.Contains("corners"))
        {
            return new TakeoffAutoSortKey(0, 0, 0, 0, low);
        }

        if (tokens.Contains("ext"))
            return new TakeoffAutoSortKey(0, 1, -LastNumber(low), 0, low);
        if ((tokens.Contains("cor") || tokens.Contains("corr")) &&
            !tokens.Contains("corner") &&
            !tokens.Contains("corners"))
        {
            return new TakeoffAutoSortKey(0, 2, 0, 0, low);
        }

        if (tokens.Contains("dem") || tokens.Contains("demo"))
            return new TakeoffAutoSortKey(0, 3, 0, 0, low);

        string[] studs = ["2x8", "2x6", "2x4"];
        for (int i = 0; i < studs.Length; i++)
        {
            if (tokens.Contains(studs[i]))
                return new TakeoffAutoSortKey(0, 4, i, 0, low);
        }

        if (tokens.Contains("half"))
            return new TakeoffAutoSortKey(0, 5, 0, 0, low);

        return new TakeoffAutoSortKey(0, 99, 0, 0, low);
    }

    public static TakeoffAutoSortKey SqftSortKey(string name)
    {
        string low = NormalizeName(name);
        HashSet<string> tokens = TokenSet(low);

        if (tokens.Contains("base") || tokens.Contains("basement"))
            return new TakeoffAutoSortKey(0, 0, 0, 0, low);
        if (TryFindFloorLevel(low, out FloorLevel floor) && floor.Ordinal > 0)
            return new TakeoffAutoSortKey(0, 1, floor.Ordinal, 0, low);
        if (tokens.Contains("deck"))
            return new TakeoffAutoSortKey(0, 20, 0, 0, low);
        if (tokens.Contains("porch"))
            return new TakeoffAutoSortKey(0, 21, 0, 0, low);
        if (tokens.Contains("blcny") || tokens.Contains("balcony"))
            return new TakeoffAutoSortKey(0, 22, 0, 0, low);
        if (tokens.Contains("cant") || tokens.Contains("cantilevered"))
            return new TakeoffAutoSortKey(0, 23, 0, 0, low);
        if (tokens.Contains("flat"))
            return new TakeoffAutoSortKey(0, 24, 0, 0, low);
        if (tokens.Contains("rf") || tokens.Contains("roof"))
            return new TakeoffAutoSortKey(0, 25, 0, 0, low);

        return new TakeoffAutoSortKey(0, 99, 0, 0, low);
    }

    private static bool LooksLikeSqftArea(string name)
    {
        string low = NormalizeName(name);
        HashSet<string> tokens = TokenSet(low);
        return SqftAreaTokens.Any(tokens.Contains) || TryFindFloorLevel(low, out _);
    }

    private static bool LooksLikeWallItem(string name)
    {
        HashSet<string> tokens = TokenSet(name);
        return WallTokens.Any(tokens.Contains);
    }

    private static string EnsureChildFolder(string parentFolder, string name) =>
        EnsureFirstMatchingChildFolder(parentFolder, [name]);

    private static string EnsureFirstMatchingChildFolder(string parentFolder, IReadOnlyList<string> names)
    {
        foreach (string child in Directory.EnumerateDirectories(parentFolder))
        {
            string display = OurPlaneCoreJobStore.DisplayName(child);
            if (names.Any(name => string.Equals(display, name, StringComparison.OrdinalIgnoreCase)))
                return child;
        }

        return OurPlaneCoreJobStore.EnsureFolder(parentFolder, names[0]);
    }

    private static bool TryResolveFloorLevel(
        OurPlaneCoreJob job,
        string activePageName,
        string activePageFolder,
        out FloorLevel floor)
    {
        foreach (string text in PageContextTexts(job, activePageName, activePageFolder))
        {
            if (TryFindFloorLevel(text, out floor))
                return true;
        }

        floor = default;
        return false;
    }

    private static IEnumerable<string> PageContextTexts(OurPlaneCoreJob job, string activePageName, string activePageFolder)
    {
        if (!string.IsNullOrWhiteSpace(activePageName))
            yield return activePageName;

        string? current = activePageFolder;
        while (!string.IsNullOrWhiteSpace(current) &&
               Directory.Exists(current) &&
               OurPlaneCoreJobStore.IsSameOrDescendant(job.PagesRoot, current))
        {
            yield return OurPlaneCoreJobStore.DisplayName(current);
            if (string.Equals(
                    Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(job.PagesRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            current = Path.GetDirectoryName(current);
        }
    }

    private static bool TryFindFloorLevel(string text, out FloorLevel floor)
    {
        string low = NormalizeName(text);
        if (Regex.IsMatch(low, @"\b(base|basement|bsmnt)\b", RegexOptions.IgnoreCase))
        {
            floor = new FloorLevel("basement", 0);
            return true;
        }

        (string Label, int Ordinal, string[] Aliases)[] floors =
        [
            ("1st", 1, ["1st", "first"]),
            ("2nd", 2, ["2nd", "second"]),
            ("3rd", 3, ["3rd", "third"]),
            ("4th", 4, ["4th", "fourth"]),
            ("5th", 5, ["5th", "fifth"]),
            ("6th", 6, ["6th", "sixth"]),
            ("7th", 7, ["7th", "seventh"]),
            ("8th", 8, ["8th", "eighth"]),
        ];

        foreach (var entry in floors)
        {
            if (entry.Aliases.Any(alias => Regex.IsMatch(low, $@"\b{Regex.Escape(alias)}\b", RegexOptions.IgnoreCase)) ||
                Regex.IsMatch(low, $@"\b(floor|flr|level|lvl)\s*{entry.Ordinal}\b", RegexOptions.IgnoreCase))
            {
                floor = new FloorLevel(entry.Label, entry.Ordinal);
                return true;
            }
        }

        floor = default;
        return false;
    }

    private static IReadOnlyList<string> WallFloorFolderCandidates(FloorLevel floor)
    {
        if (floor.Ordinal == 0)
            return ["basement floor walls", "basement foor walls", "base floor walls"];
        return [$"{floor.Label} floor walls", $"{floor.Label} walls"];
    }

    private static TakeoffAutoSortKey WallFloorSortKey(string name)
    {
        if (TryFindFloorLevel(name, out FloorLevel floor))
            return new TakeoffAutoSortKey(0, floor.Ordinal, 0, 0, NormalizeName(name));
        return new TakeoffAutoSortKey(0, 99, 0, 0, NormalizeName(name));
    }

    private static bool IsSqftsFolder(string relative)
    {
        string[] parts = RelativeParts(relative);
        return parts.Length == 1 &&
               (string.Equals(parts[0], "sqfts", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parts[0], "sqft", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWallsRootFolder(string relative)
    {
        string[] parts = RelativeParts(relative);
        return parts.Length == 1 && string.Equals(parts[0], "walls", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWallFloorFolder(string relative)
    {
        string[] parts = RelativeParts(relative);
        return parts.Length >= 2 && string.Equals(parts[0], "walls", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] RelativeParts(string relative) =>
        relative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string RelativeTakeoffPath(OurPlaneCoreJob job, string folder)
    {
        string root = NormalizePath(job.TakeoffsRoot);
        string full = NormalizePath(folder);
        if (string.Equals(root, full, StringComparison.OrdinalIgnoreCase))
            return "";
        if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return full[(root.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
        return "";
    }

    private static HashSet<string> TokenSet(string name) =>
        Regex.Matches(NormalizeName(name), @"\d+x\d+|\d+(?:st|nd|rd|th)?|[a-z]+")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static int LastNumber(string name)
    {
        MatchCollection matches = Regex.Matches(name, @"\d+");
        return matches.Count == 0
            ? 0
            : int.TryParse(matches[matches.Count - 1].Value, out int number) ? number : 0;
    }

    private static string NormalizeName(string name) =>
        (name ?? "").Trim().ToLowerInvariant();

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private readonly record struct FloorLevel(string Label, int Ordinal);
}
