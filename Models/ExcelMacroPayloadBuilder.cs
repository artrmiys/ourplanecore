using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OurPlanCore;

public sealed record ExcelMacroPayloadRow(
    string Name,
    double? Value,
    string Unit,
    bool IsFloorHeader = false);

public sealed record ExcelMacroPayloadResult(
    bool Success,
    string Message,
    IReadOnlyList<ExcelMacroPayloadRow> Rows,
    IReadOnlyList<string> Warnings)
{
    public static ExcelMacroPayloadResult Failure(string message) =>
        new(false, message, [], []);
}

public static class ExcelMacroPayloadBuilder
{
    private const double SquareFeetPerSquareMeter = 10.7639104167097;
    private const double FeetPerMeter = 3.28083989501312;

    public static ExcelMacroPayloadResult Build(
        OurPlanCoreJob job,
        IReadOnlyList<TakeoffItem> takeoffItems,
        IReadOnlyList<string> selectedRoots,
        double fallbackScaleMetersPerPt,
        ExcelMacroExportConfig config,
        string actionId)
    {
        ExcelMacroExportActionConfig action = config.Action(actionId);
        List<TakeoffItem> selectedItems = takeoffItems
            .Where(item => item.Measurements.Count > 0)
            .Where(item => selectedRoots.Any(root => IsSameOrChild(item.FolderPath, root)))
            .DistinctBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selectedItems.Count == 0)
            return ExcelMacroPayloadResult.Failure(
                $"Selected Takeoffs scope has no measured rows for {action.Label}.");

        var matches = selectedItems
            .Select(item => MatchRole(job, item, action))
            .Where(match => match != null)
            .Cast<RoleMatch>()
            .ToList();
        if (matches.Count == 0)
        {
            string aliases = string.Join(", ", action.FolderAliases);
            return ExcelMacroPayloadResult.Failure(
                $"The selection does not contain the {action.Label} folder ({aliases}).");
        }

        List<string> roleRoots = matches
            .Select(match => match.RoleRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (roleRoots.Count > 1)
        {
            return ExcelMacroPayloadResult.Failure(
                $"The selection contains {roleRoots.Count} separate {action.Label} folders. " +
                "Select one building or one export folder so floor groups are not mixed.");
        }

        return action.UseFloorHeaders
            ? BuildFloored(matches, fallbackScaleMetersPerPt, config, action)
            : BuildFlat(matches, fallbackScaleMetersPerPt, action);
    }

    private static ExcelMacroPayloadResult BuildFlat(
        IReadOnlyList<RoleMatch> matches,
        double fallbackScaleMetersPerPt,
        ExcelMacroExportActionConfig action)
    {
        List<ExcelMacroPayloadRow> rows = matches
            .Select(match => BuildItemRow(match.Item, fallbackScaleMetersPerPt, action))
            .Where(row => row.Value is > 0)
            .ToList();
        rows = OrderRows(rows, action);

        return rows.Count == 0
            ? ExcelMacroPayloadResult.Failure(
                $"{action.Label} items are present, but their measured totals are zero.")
            : new ExcelMacroPayloadResult(
                true,
                $"Prepared {rows.Count} {action.Label} row(s).",
                rows,
                []);
    }

    private static ExcelMacroPayloadResult BuildFloored(
        IReadOnlyList<RoleMatch> matches,
        double fallbackScaleMetersPerPt,
        ExcelMacroExportConfig config,
        ExcelMacroExportActionConfig action)
    {
        var warnings = new List<string>();
        var groups = new SortedDictionary<int, List<ExcelMacroPayloadRow>>();

        foreach (RoleMatch match in matches)
        {
            int? floor = ResolveFloor(match.FloorSegment, config.FloorRules);
            if (floor == null)
            {
                warnings.Add(
                    $"Skipped '{match.Item.Name}': floor folder '{match.FloorSegment}' " +
                    "does not match floor rules 0-5.");
                continue;
            }

            ExcelMacroPayloadRow row = BuildItemRow(match.Item, fallbackScaleMetersPerPt, action);
            if (row.Value is not > 0)
                continue;
            if (!groups.TryGetValue(floor.Value, out List<ExcelMacroPayloadRow>? floorRows))
            {
                floorRows = [];
                groups[floor.Value] = floorRows;
            }
            floorRows.Add(row);
        }

        var rows = new List<ExcelMacroPayloadRow>();
        foreach ((int floor, List<ExcelMacroPayloadRow> floorRows) in groups)
        {
            rows.Add(new ExcelMacroPayloadRow(
                floor.ToString(CultureInfo.InvariantCulture),
                null,
                "",
                IsFloorHeader: true));
            rows.AddRange(OrderRows(floorRows, action));
        }

        if (rows.Count == 0)
        {
            return new ExcelMacroPayloadResult(
                false,
                $"{action.Label} items are present, but no measured rows matched floor rules 0-5.",
                [],
                warnings);
        }

        int itemCount = rows.Count(row => !row.IsFloorHeader);
        return new ExcelMacroPayloadResult(
            true,
            $"Prepared {itemCount} {action.Label} row(s) in {groups.Count} floor group(s).",
            rows,
            warnings);
    }

    private static ExcelMacroPayloadRow BuildItemRow(
        TakeoffItem item,
        double fallbackScaleMetersPerPt,
        ExcelMacroExportActionConfig action)
    {
        string type = OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double raw = item.Total(fallbackScaleMetersPerPt);
        bool imperial = !string.Equals(
            action.UnitSystem,
            "Metric",
            StringComparison.OrdinalIgnoreCase);
        return type switch
        {
            "area" => new ExcelMacroPayloadRow(
                item.Name,
                imperial ? raw * SquareFeetPerSquareMeter : raw,
                imperial ? "SF" : "M2"),
            "line" => new ExcelMacroPayloadRow(
                item.Name,
                imperial ? raw * FeetPerMeter : raw,
                imperial ? "FT" : "M"),
            "point" => new ExcelMacroPayloadRow(item.Name, raw, "EA"),
            _ => new ExcelMacroPayloadRow(item.Name, raw, ""),
        };
    }

    internal static List<ExcelMacroPayloadRow> OrderRows(
        IReadOnlyList<ExcelMacroPayloadRow> rows,
        ExcelMacroExportActionConfig action)
    {
        if (string.Equals(
                action.RowOrderMode,
                ExcelMacroRowOrderModes.WallsStrict,
                StringComparison.OrdinalIgnoreCase))
        {
            return rows
                .Select((row, index) => new { Row = row, Index = index, Rank = WallRank(row.Name) })
                .OrderBy(item => item.Rank)
                .ThenByDescending(item =>
                    item.Rank == 1 ? item.Row.Value ?? 0 : 0)
                .ThenBy(item => item.Index)
                .Select(item => item.Row)
                .ToList();
        }

        if (string.Equals(
                action.RowOrderMode,
                ExcelMacroRowOrderModes.EvesThenRakesByValue,
                StringComparison.OrdinalIgnoreCase))
        {
            return rows
                .Select((row, index) => new { Row = row, Index = index, Rank = EveRakeRank(row.Name) })
                .OrderBy(item => item.Rank)
                .ThenByDescending(item =>
                    item.Rank is 0 or 1 ? item.Row.Value ?? 0 : 0)
                .ThenBy(item => item.Index)
                .Select(item => item.Row)
                .ToList();
        }

        return [.. rows];
    }

    private static int WallRank(string name)
    {
        if (Regex.IsMatch(name ?? "", @"^\s*corners?\b", RegexOptions.IgnoreCase))
            return 0;
        if (Regex.IsMatch(name ?? "", @"^\s*ext\b", RegexOptions.IgnoreCase))
            return 1;
        if (Regex.IsMatch(name ?? "", @"^\s*corr?\b", RegexOptions.IgnoreCase))
            return 2;
        if (Regex.IsMatch(name ?? "", @"^\s*dem\b", RegexOptions.IgnoreCase))
            return 3;
        if (Regex.IsMatch(name ?? "", @"^\s*2\s*[xх×]\s*6\b", RegexOptions.IgnoreCase))
            return 4;
        if (Regex.IsMatch(name ?? "", @"^\s*2\s*[xх×]\s*4\b", RegexOptions.IgnoreCase))
            return 5;
        return 9;
    }

    private static int EveRakeRank(string name)
    {
        if (Regex.IsMatch(name ?? "", @"^\s*(?:eve|eave)s?\b", RegexOptions.IgnoreCase))
            return 0;
        if (Regex.IsMatch(name ?? "", @"^\s*rakes?\b", RegexOptions.IgnoreCase))
            return 1;
        if (Regex.IsMatch(name ?? "", @"^\s*returns?\b", RegexOptions.IgnoreCase))
            return 2;
        return 9;
    }

    private static RoleMatch? MatchRole(
        OurPlanCoreJob job,
        TakeoffItem item,
        ExcelMacroExportActionConfig action)
    {
        string relative;
        try
        {
            relative = Path.GetRelativePath(job.TakeoffsRoot, item.FolderPath);
        }
        catch
        {
            return null;
        }

        if (relative.StartsWith("..", StringComparison.Ordinal))
            return null;

        string[] segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        int roleIndex = Array.FindIndex(
            segments,
            segment => action.FolderAliases.Any(alias =>
                string.Equals(segment.Trim(), alias.Trim(), StringComparison.OrdinalIgnoreCase)));
        if (roleIndex < 0)
            return null;

        string roleRoot = job.TakeoffsRoot;
        for (int i = 0; i <= roleIndex; i++)
            roleRoot = Path.Combine(roleRoot, segments[i]);

        string floorSegment = roleIndex + 1 < segments.Length
            ? segments[roleIndex + 1]
            : "";
        return new RoleMatch(item, roleRoot, floorSegment);
    }

    private static int? ResolveFloor(
        string floorSegment,
        IReadOnlyList<ExcelMacroFloorRule> floorRules)
    {
        string normalized = NormalizeWords(floorSegment);
        if (normalized.Length == 0)
            return null;

        string[] words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (ExcelMacroFloorRule rule in floorRules.OrderBy(rule => rule.Floor))
        {
            foreach (string alias in rule.Aliases)
            {
                string normalizedAlias = NormalizeWords(alias);
                if (normalizedAlias.Length == 0)
                    continue;
                if (normalizedAlias.Contains(' '))
                {
                    if (normalized.Contains(normalizedAlias, StringComparison.OrdinalIgnoreCase))
                        return rule.Floor;
                }
                else if (words.Contains(normalizedAlias, StringComparer.OrdinalIgnoreCase))
                {
                    return rule.Floor;
                }
            }
        }
        return null;
    }

    private static string NormalizeWords(string value)
    {
        char[] chars = (value ?? "")
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray();
        return string.Join(
            ' ',
            new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
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

    private sealed record RoleMatch(
        TakeoffItem Item,
        string RoleRoot,
        string FloorSegment);
}
