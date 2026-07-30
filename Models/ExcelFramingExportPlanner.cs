using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OurPlanCore;

public sealed record ExcelFramingInputRow(
    string Name,
    double? Quantity = null,
    string Unit = "");

public sealed record ExcelFramingCategoryPlan(
    string Id,
    string Label,
    string Mode,
    string MacroName,
    bool UseSum,
    int Order,
    IReadOnlyList<ExcelFramingInputRow> Rows);

public sealed record ExcelFramingTargetPlan(
    string Heading,
    int Order,
    IReadOnlyList<ExcelFramingCategoryPlan> Categories);

public sealed record ExcelFramingExportPlan(
    bool Success,
    string Message,
    IReadOnlyList<ExcelFramingTargetPlan> FramingTargets,
    IReadOnlyList<ExcelFramingTargetPlan> HeaderTargets,
    IReadOnlyList<string> Warnings)
{
    public int TargetCount => FramingTargets.Count + HeaderTargets.Count;

    public static ExcelFramingExportPlan Failure(string message) =>
        new(false, message, [], [], []);
}

public static class ExcelFramingExportPlanner
{
    private static readonly Regex TrailingSpacingPattern = new(
        @"\d+(?:[.,]\d+)?\s*""\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ExcelFramingExportPlan Build(
        OurPlanCoreJob job,
        IReadOnlyList<TakeoffItem> takeoffItems,
        string scopeRoot,
        double fallbackScaleMetersPerPt,
        ExcelFramingExportConfig config,
        bool requireIncludeInAll = true)
    {
        if (requireIncludeInAll && !config.IncludeInAll)
            return ExcelFramingExportPlan.Failure("Framing export is disabled in Settings.");

        string normalizedScope = NormalizePath(scopeRoot);
        if (!IsSameOrChild(normalizedScope, job.TakeoffsRoot))
            return ExcelFramingExportPlan.Failure("The framing scope is outside the open job.");

        var matched = new List<MatchedItem>();
        var warnings = new List<string>();
        foreach (TakeoffItem item in takeoffItems.Where(item => item.Measurements.Count > 0))
        {
            if (!IsSameOrChild(item.FolderPath, normalizedScope))
                continue;
            if (!TryMatch(item, normalizedScope, config, out MatchedItem? match))
                continue;
            matched.Add(match!);
        }

        if (matched.Count == 0)
        {
            return ExcelFramingExportPlan.Failure(
                "No measured rows were found under framing/<floor>/<category>.");
        }

        ExcelFramingFloorRule? highestNumeric = matched
            .Select(match => match.Floor)
            .Where(floor => !floor.IsRoof)
            .OrderByDescending(floor => floor.Order)
            .FirstOrDefault();
        highestNumeric ??= config.Floors
            .Where(floor => !floor.IsRoof)
            .OrderBy(floor => floor.Order)
            .FirstOrDefault();

        List<ExcelFramingTargetPlan> framingTargets = BuildTargets(
            matched.Where(match =>
                !string.Equals(
                    match.Category.Mode,
                    ExcelFramingCategoryModes.Headers,
                    StringComparison.OrdinalIgnoreCase)),
            match => match.Floor.FramingHeading,
            fallbackScaleMetersPerPt,
            warnings);

        List<ExcelFramingTargetPlan> headerTargets = BuildTargets(
            matched.Where(match =>
                string.Equals(
                    match.Category.Mode,
                    ExcelFramingCategoryModes.Headers,
                    StringComparison.OrdinalIgnoreCase)),
            match => match.Floor.IsRoof
                ? highestNumeric?.SameFloorWallHeading ?? ""
                : match.Floor.HeaderWallHeading,
            fallbackScaleMetersPerPt,
            warnings);

        if (framingTargets.Count == 0 && headerTargets.Count == 0)
        {
            return ExcelFramingExportPlan.Failure(
                warnings.Count > 0
                    ? string.Join(" ", warnings)
                    : "No valid framing rows were prepared.");
        }

        return new ExcelFramingExportPlan(
            true,
            $"Prepared {framingTargets.Count} framing and {headerTargets.Count} header block(s).",
            framingTargets,
            headerTargets,
            warnings);
    }

    public static ExcelFramingExportPlan ForCategory(
        ExcelFramingExportPlan plan,
        string categoryId)
    {
        if (!plan.Success)
            return plan;

        List<ExcelFramingTargetPlan> framingTargets =
            FilterTargets(plan.FramingTargets, categoryId);
        List<ExcelFramingTargetPlan> headerTargets =
            FilterTargets(plan.HeaderTargets, categoryId);
        if (framingTargets.Count == 0 && headerTargets.Count == 0)
        {
            return ExcelFramingExportPlan.Failure(
                $"No measured {categoryId} rows were found in the selected building.");
        }

        return new ExcelFramingExportPlan(
            true,
            $"Prepared {categoryId} for {framingTargets.Count + headerTargets.Count} target block(s).",
            framingTargets,
            headerTargets,
            plan.Warnings);
    }

    private static List<ExcelFramingTargetPlan> FilterTargets(
        IReadOnlyList<ExcelFramingTargetPlan> targets,
        string categoryId) =>
        targets
            .Select(target => target with
            {
                Categories = target.Categories
                    .Where(category => string.Equals(
                        category.Id,
                        categoryId,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList(),
            })
            .Where(target => target.Categories.Count > 0)
            .ToList();

    private static List<ExcelFramingTargetPlan> BuildTargets(
        IEnumerable<MatchedItem> source,
        Func<MatchedItem, string> headingFor,
        double fallbackScaleMetersPerPt,
        List<string> warnings)
    {
        var targets = new List<ExcelFramingTargetPlan>();
        foreach (IGrouping<string, MatchedItem> targetGroup in source
                     .Where(match => !string.IsNullOrWhiteSpace(headingFor(match)))
                     .GroupBy(headingFor, StringComparer.OrdinalIgnoreCase))
        {
            var categoryPlans = new List<ExcelFramingCategoryPlan>();
            foreach (IGrouping<string, MatchedItem> categoryGroup in targetGroup
                         .GroupBy(match => match.Category.Id, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => group.First().Category.Order))
            {
                ExcelFramingCategoryConfig category = categoryGroup.First().Category;
                IReadOnlyList<ExcelFramingInputRow> rows = BuildCategoryRows(
                    categoryGroup.ToList(),
                    fallbackScaleMetersPerPt,
                    warnings);
                if (rows.Count == 0)
                    continue;
                categoryPlans.Add(new ExcelFramingCategoryPlan(
                    category.Id,
                    category.Label,
                    category.Mode,
                    category.MacroName,
                    category.UseSum,
                    category.Order,
                    rows));
            }
            if (categoryPlans.Count == 0)
                continue;

            targets.Add(new ExcelFramingTargetPlan(
                targetGroup.Key,
                targetGroup.Min(match => match.Floor.Order),
                categoryPlans));
        }

        return targets
            .OrderBy(target => target.Order)
            .ThenBy(target => target.Heading, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<ExcelFramingInputRow> BuildCategoryRows(
        IReadOnlyList<MatchedItem> matches,
        double fallbackScaleMetersPerPt,
        List<string> warnings)
    {
        ExcelFramingCategoryConfig category = matches[0].Category;
        if (string.Equals(
                category.Mode,
                ExcelFramingCategoryModes.Joists,
                StringComparison.OrdinalIgnoreCase))
        {
            return BuildJoistRows(matches, fallbackScaleMetersPerPt, warnings);
        }

        IEnumerable<MatchedItem> ordered = matches;
        if (string.Equals(
                category.Mode,
                ExcelFramingCategoryModes.Details,
                StringComparison.OrdinalIgnoreCase))
        {
            ordered = matches.OrderBy(
                match => match.Item.Name,
                TakeoffDetailSheetNameComparer.Instance);
        }

        var rows = new List<ExcelFramingInputRow>();
        foreach (MatchedItem match in ordered)
        {
            if (!TryBuildQuantityRow(
                    match.Item,
                    fallbackScaleMetersPerPt,
                    out ExcelFramingInputRow row))
            {
                warnings.Add($"{category.Label}: skipped '{match.Item.Name}' because its scale is unresolved.");
                continue;
            }

            if (string.Equals(
                    category.Mode,
                    ExcelFramingCategoryModes.Headers,
                    StringComparison.OrdinalIgnoreCase))
            {
                string side = match.HeaderSide.Length > 0 ? match.HeaderSide : "ext";
                row = row with { Name = $"{side} {row.Name}".Trim() };
            }
            rows.Add(row);
        }
        return rows;
    }

    private static IReadOnlyList<ExcelFramingInputRow> BuildJoistRows(
        IReadOnlyList<MatchedItem> matches,
        double fallbackScaleMetersPerPt,
        List<string> warnings)
    {
        var rows = new List<ExcelFramingInputRow>();
        foreach (IGrouping<string, MatchedItem> nameGroup in matches
                     .GroupBy(
                         match => match.Item.Name.Trim(),
                         StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            List<TakeoffItem> readyItems = nameGroup
                .Select(match => match.Item)
                .Where(item => item.IsJoistArea && !item.HasPendingJoistDirections)
                .ToList();
            int skippedCount = nameGroup.Count() - readyItems.Count;
            if (skippedCount > 0)
            {
                warnings.Add(
                    $"Joists: skipped {skippedCount} '{nameGroup.Key}' takeoff(s) " +
                    "because their joist direction is not ready.");
            }
            if (readyItems.Count == 0)
                continue;

            List<Measurement> measurements = readyItems
                .SelectMany(item => item.Measurements)
                .ToList();
            IReadOnlyList<JoistLengthGroup> regular =
                JoistTakeoffCalculator.RegularLengthGroups(
                    measurements,
                    fallbackScaleMetersPerPt,
                    UnitMode.Imperial);
            IReadOnlyList<JoistLengthGroup> extra =
                JoistTakeoffCalculator.ExtraLengthGroups(
                    measurements,
                    fallbackScaleMetersPerPt,
                    UnitMode.Imperial);
            foreach (JoistLengthGroup group in regular.Concat(extra)
                         .OrderByDescending(group => group.Length))
            {
                rows.Add(new ExcelFramingInputRow(
                    $"({group.Count} / {FormatNumber(group.Length)})"));
            }
            if (regular.Count == 0 && extra.Count == 0)
            {
                warnings.Add(
                    $"Joists: skipped '{nameGroup.Key}' because it has no calculated lengths.");
                continue;
            }

            TakeoffItem firstItem = readyItems[0];
            string name = nameGroup.Key;
            if (!TrailingSpacingPattern.IsMatch(name))
            {
                name =
                    $"{name} {FormatNumber(firstItem.JoistSpacingInches)}\"".Trim();
            }
            rows.Add(new ExcelFramingInputRow(name));
        }
        return rows;
    }

    private static bool TryBuildQuantityRow(
        TakeoffItem item,
        double fallbackScaleMetersPerPt,
        out ExcelFramingInputRow row)
    {
        row = new ExcelFramingInputRow(item.Name);
        string type = OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double raw = item.Total(fallbackScaleMetersPerPt);
        if ((type is "line" or "area") && raw <= 0)
            return false;

        row = type switch
        {
            "point" => new ExcelFramingInputRow(item.Name, raw, "EA"),
            "line" => new ExcelFramingInputRow(item.Name, raw / 0.3048, "FT"),
            "area" => new ExcelFramingInputRow(item.Name, raw / 0.09290304, "SF"),
            _ => new ExcelFramingInputRow(item.Name, raw, ""),
        };
        return true;
    }

    private static bool TryMatch(
        TakeoffItem item,
        string scopeRoot,
        ExcelFramingExportConfig config,
        out MatchedItem? match)
    {
        match = null;
        string relative;
        try
        {
            relative = Path.GetRelativePath(scopeRoot, item.FolderPath);
        }
        catch
        {
            return false;
        }
        string[] segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int framingIndex = FindSegment(segments, 0, config.FramingFolderAliases);
        if (framingIndex < 0)
            return false;

        int floorIndex = framingIndex + 1;
        if (floorIndex >= segments.Length)
            return false;
        ExcelFramingFloorRule? floor = config.Floors
            .FirstOrDefault(rule =>
                MatchesAlias(segments[floorIndex], rule.Aliases));
        if (floor == null)
            return false;

        int categoryIndex = floorIndex + 1;
        if (categoryIndex >= segments.Length)
            return false;
        ExcelFramingCategoryConfig? category = config.Categories
            .FirstOrDefault(candidate =>
                MatchesAlias(segments[categoryIndex], candidate.FolderAliases));
        if (category == null)
            return false;
        string headerSide = categoryIndex >= 0
            ? HeaderSide(segments.Skip(categoryIndex + 1))
            : "";
        match = new MatchedItem(item, floor, category, headerSide);
        return true;
    }

    private static int FindSegment(
        IReadOnlyList<string> segments,
        int start,
        IEnumerable<string> aliases)
    {
        HashSet<string> normalizedAliases = aliases
            .Select(NormalizeName)
            .Where(alias => alias.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int index = Math.Max(0, start); index < segments.Count; index++)
        {
            if (normalizedAliases.Contains(NormalizeName(segments[index])))
                return index;
        }
        return -1;
    }

    private static bool MatchesAlias(
        string segment,
        IEnumerable<string> aliases)
    {
        string normalizedSegment = NormalizeName(segment);
        return aliases.Any(alias =>
            string.Equals(
                NormalizeName(alias),
                normalizedSegment,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string HeaderSide(IEnumerable<string> segments)
    {
        foreach (string segment in segments)
        {
            string normalized = NormalizeName(segment);
            if (normalized is "ext" or "exterior")
                return "ext";
            if (normalized is "int" or "interior")
                return "int";
        }
        return "";
    }

    private static string NormalizeName(string value) =>
        string.Join(
            " ",
            (value ?? "").Trim().ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string FormatNumber(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

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

    private sealed record MatchedItem(
        TakeoffItem Item,
        ExcelFramingFloorRule Floor,
        ExcelFramingCategoryConfig Category,
        string HeaderSide);
}
