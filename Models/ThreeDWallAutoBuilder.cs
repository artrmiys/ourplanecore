using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace OurPlaneCore;

public sealed class ThreeDWallAutoBuildResult
{
    public ThreeDWallModel Model { get; init; } = new();
    public int WallTakeoffCount { get; set; }
    public int SlabTakeoffCount { get; set; }
    public int SkippedNoScaleMeasurements { get; set; }
    public int SkippedNonLineMeasurements { get; set; }
    public int SkippedShortSegments { get; set; }
    public int SkippedAreaMeasurements { get; set; }
}

public static class ThreeDWallAutoBuilder
{
    private const double MetersPerFoot = 0.3048;
    private static readonly Regex LevelRegex = new(
        @"(?<![\d.])\b(?:(?<n>[1-8])\s*(?:st|nd|rd|th)?|(?<w>first|second|third|fourth|fifth|sixth|seventh|eighth))\b(?!\s*\.)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WallTokenRegex = new(
        @"\b(wall|walls|ext|dem|demo|corner|corners|cor|corr|half|\d+\s*x\s*\d+)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ThreeDWallAutoBuildResult Build(
        OurPlaneCoreJob job,
        Func<Measurement, double> scaleResolver)
    {
        IReadOnlyList<TakeoffItem> items = OurPlaneCoreJobStore.LoadTakeoffItems(job);
        var result = new ThreeDWallAutoBuildResult
        {
            Model = new ThreeDWallModel { Source = "auto_takeoffs" },
        };

        var wallGroups = items
            .Select(item => new { Item = item, Level = ResolveLevel(job, item) })
            .Where(entry => entry.Level != null &&
                            IsWallCandidate(job, entry.Item) &&
                            entry.Item.Measurements.Any(IsLineMeasurement))
            .GroupBy(entry => entry.Level!.Value.Ordinal)
            .OrderBy(group => group.Key)
            .ToList();

        var heightByLevel = new Dictionary<int, double>();
        foreach (var group in wallGroups)
        {
            var groupItems = group.Select(entry => entry.Item).ToList();
            ThreeDWallBuildResult wallResult = ThreeDWallTakeoffBuilder.BuildWalls(groupItems, job, scaleResolver);
            result.WallTakeoffCount += groupItems.Count;
            result.SkippedNoScaleMeasurements += wallResult.SkippedNoScaleMeasurements;
            result.SkippedNonLineMeasurements += wallResult.SkippedNonLineMeasurements;
            result.SkippedShortSegments += wallResult.SkippedShortSegments;

            string levelLabel = LevelLabel(group.Key);
            foreach (ThreeDWallSegment wall in wallResult.Walls)
            {
                wall.LevelKey = levelLabel;
                wall.GroupKey = $"{levelLabel}|{wall.TakeoffFolder}";
                result.Model.Walls.Add(wall);
            }

            if (wallResult.Walls.Count > 0)
                heightByLevel[group.Key] = wallResult.Walls.Max(wall => wall.HeightFeet);
        }

        double defaultHeight = heightByLevel.Count > 0
            ? heightByLevel.Values.Max()
            : ThreeDWallTakeoffBuilder.DefaultWallHeightFeet;
        Dictionary<int, double> baseByLevel = BuildBaseElevations(heightByLevel, defaultHeight);

        foreach (ThreeDWallSegment wall in result.Model.Walls)
        {
            if (TryParseLevel(wall.LevelKey, out ThreeDLevelKey level) &&
                baseByLevel.TryGetValue(level.Ordinal, out double baseElevation))
            {
                wall.BaseElevationFeet = baseElevation;
            }
        }

        AddLevels(result.Model, heightByLevel, baseByLevel, defaultHeight);
        AddSqftSlabs(result, job, items, scaleResolver, baseByLevel, defaultHeight);
        return result;
    }

    private static void AddSqftSlabs(
        ThreeDWallAutoBuildResult result,
        OurPlaneCoreJob job,
        IReadOnlyList<TakeoffItem> items,
        Func<Measurement, double> scaleResolver,
        Dictionary<int, double> baseByLevel,
        double defaultHeight)
    {
        foreach (TakeoffItem item in items.Where(item => IsSqftCandidate(job, item) || IsRoofFootprintCandidate(job, item)))
        {
            bool roofFootprint = IsRoofFootprintCandidate(job, item);
            ThreeDLevelKey? resolvedLevel = ResolveLevel(job, item);
            if (resolvedLevel == null && !roofFootprint)
                continue;

            ThreeDLevelKey level = resolvedLevel ?? RoofLevel(baseByLevel);
            result.SlabTakeoffCount++;
            double elevation = roofFootprint
                ? ResolveRoofElevation(baseByLevel, defaultHeight)
                : ResolveBaseElevation(level.Ordinal, baseByLevel, defaultHeight);
            foreach (Measurement measurement in item.Measurements)
            {
                if (OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) != "area" ||
                    measurement.Points.Count < 3)
                {
                    result.SkippedAreaMeasurements++;
                    continue;
                }

                double scaleMetersPerPt = scaleResolver(measurement);
                if (scaleMetersPerPt <= 0)
                {
                    result.SkippedNoScaleMeasurements++;
                    continue;
                }

                ThreeDFloorSlab slab = BuildSlab(item, measurement, level, elevation, scaleMetersPerPt);
                if (slab.Points.Count >= 3)
                    result.Model.Slabs.Add(slab);
            }
        }
    }

    private static ThreeDFloorSlab BuildSlab(
        TakeoffItem item,
        Measurement measurement,
        ThreeDLevelKey level,
        double elevation,
        double scaleMetersPerPt)
    {
        double feetPerPt = scaleMetersPerPt / MetersPerFoot;
        return new ThreeDFloorSlab
        {
            TakeoffName = item.Name,
            TakeoffFolder = item.FolderPath,
            MeasurementId = measurement.Id,
            PageFolder = measurement.PageFolder,
            ElevationFeet = elevation,
            Label = string.IsNullOrWhiteSpace(item.Name) ? $"{level.Label} sqft" : item.Name.Trim(),
            Color = item.Color,
            GroupKey = $"{level.Label}|{item.FolderPath}",
            LevelKey = level.Label,
            Points = measurement.Points
                .Select(point => new ThreeDPoint
                {
                    XFeet = point.X * feetPerPt,
                    ZFeet = point.Y * feetPerPt,
                })
                .ToList(),
        };
    }

    private static Dictionary<int, double> BuildBaseElevations(
        IReadOnlyDictionary<int, double> heightByLevel,
        double defaultHeight)
    {
        var baseByLevel = new Dictionary<int, double>();
        int maxLevel = Math.Max(1, heightByLevel.Keys.DefaultIfEmpty(1).Max());
        double current = 0;
        for (int ordinal = 1; ordinal <= maxLevel; ordinal++)
        {
            baseByLevel[ordinal] = current;
            current += heightByLevel.TryGetValue(ordinal, out double height)
                ? height
                : defaultHeight;
        }

        return baseByLevel;
    }

    private static double ResolveBaseElevation(
        int ordinal,
        IReadOnlyDictionary<int, double> baseByLevel,
        double defaultHeight)
    {
        if (baseByLevel.TryGetValue(ordinal, out double elevation))
            return elevation;

        return Math.Max(0, ordinal - 1) * defaultHeight;
    }

    private static double ResolveRoofElevation(
        IReadOnlyDictionary<int, double> baseByLevel,
        double defaultHeight)
    {
        int topOrdinal = baseByLevel.Keys.DefaultIfEmpty(0).Max();
        if (topOrdinal > 0 && baseByLevel.TryGetValue(topOrdinal, out double baseElevation))
            return baseElevation + defaultHeight;

        return defaultHeight;
    }

    private static ThreeDLevelKey RoofLevel(IReadOnlyDictionary<int, double> baseByLevel)
    {
        int topOrdinal = baseByLevel.Keys.DefaultIfEmpty(0).Max();
        return new ThreeDLevelKey("roof", Math.Max(1, topOrdinal + 1));
    }

    private static void AddLevels(
        ThreeDWallModel model,
        IReadOnlyDictionary<int, double> heightByLevel,
        IReadOnlyDictionary<int, double> baseByLevel,
        double defaultHeight)
    {
        foreach (int ordinal in heightByLevel.Keys.OrderBy(key => key))
        {
            model.Levels.Add(new ThreeDFloorLevel
            {
                Label = LevelLabel(ordinal),
                Ordinal = ordinal,
                BaseElevationFeet = ResolveBaseElevation(ordinal, baseByLevel, defaultHeight),
                HeightFeet = heightByLevel[ordinal],
            });
        }
    }

    private static bool IsLineMeasurement(Measurement measurement) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "line" &&
        measurement.Points.Count >= 2;

    private static bool IsWallCandidate(OurPlaneCoreJob job, TakeoffItem item)
    {
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) != "line")
            return false;

        string source = SourceText(job, item);
        return WallTokenRegex.IsMatch(source) ||
               SourceParts(job, item).Any(part => string.Equals(part, "walls", StringComparison.OrdinalIgnoreCase) ||
                                                  string.Equals(part, "wall", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSqftCandidate(OurPlaneCoreJob job, TakeoffItem item)
    {
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) != "area")
            return false;

        foreach (string part in SourceParts(job, item))
        {
            string clean = Normalize(part);
            if (clean is "sqft" or "sqfts" or "sq ft" or "square feet" or "square footage" or "sf" or "sft")
                return true;
        }

        string source = Normalize(SourceText(job, item));
        return source.Contains("sqft", StringComparison.Ordinal) ||
               source.Contains("sq ft", StringComparison.Ordinal) ||
               source.Contains("sqfts", StringComparison.Ordinal);
    }

    private static bool IsRoofFootprintCandidate(OurPlaneCoreJob job, TakeoffItem item)
    {
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) != "area")
            return false;

        bool underSqfts = SourceParts(job, item).Any(part =>
            Normalize(part) is "sqft" or "sqfts" or "sft" or "sf");
        string name = Normalize(item.Name);
        bool exactRoofName = name is "rf" or "roof" or "roof area" or "roof sqft" or "roof sq ft";
        bool roofInSqfts = underSqfts && Regex.IsMatch(name, @"\b(rf|roof)\b", RegexOptions.IgnoreCase);
        return exactRoofName || roofInSqfts;
    }

    private static ThreeDLevelKey? ResolveLevel(OurPlaneCoreJob job, TakeoffItem item)
    {
        foreach (string text in SourceTexts(job, item))
        {
            if (TryParseLevel(text, out ThreeDLevelKey level))
                return level;
        }

        return null;
    }

    private static IEnumerable<string> SourceTexts(OurPlaneCoreJob job, TakeoffItem item)
    {
        List<string> parts = SourceParts(job, item).ToList();
        for (int i = parts.Count - 2; i >= 0; i--)
            yield return parts[i];

        yield return item.Name;
        yield return item.Notes;
        if (parts.Count > 0)
            yield return parts[^1];
    }

    private static IEnumerable<string> SourceParts(OurPlaneCoreJob job, TakeoffItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FolderPath))
            yield break;

        string relative;
        try
        {
            relative = Path.GetRelativePath(job.TakeoffsRoot, item.FolderPath);
        }
        catch
        {
            yield break;
        }

        string current = job.TakeoffsRoot;
        foreach (string part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (!string.IsNullOrWhiteSpace(part) && part != "." && part != "..")
            {
                current = Path.Combine(current, part);
                yield return OurPlaneCoreJobStore.DisplayName(current);
            }
        }
    }

    private static string SourceText(OurPlaneCoreJob job, TakeoffItem item) =>
        string.Join(" ", SourceTexts(job, item).Where(text => !string.IsNullOrWhiteSpace(text)));

    private static bool TryParseLevel(string text, out ThreeDLevelKey level)
    {
        string normalized = Normalize(text);
        if (Regex.IsMatch(normalized, @"\b(base|basement|bsmnt)\b", RegexOptions.IgnoreCase))
        {
            level = new ThreeDLevelKey("basement", 0);
            return true;
        }

        Match match = LevelRegex.Match(normalized);
        if (match.Success)
        {
            int ordinal = 0;
            if (match.Groups["n"].Success)
                int.TryParse(match.Groups["n"].Value, out ordinal);
            else
                ordinal = WordOrdinal(match.Groups["w"].Value);

            if (ordinal > 0)
            {
                level = new ThreeDLevelKey(LevelLabel(ordinal), ordinal);
                return true;
            }
        }

        level = default;
        return false;
    }

    private static int WordOrdinal(string word) =>
        Normalize(word) switch
        {
            "first" => 1,
            "second" => 2,
            "third" => 3,
            "fourth" => 4,
            "fifth" => 5,
            "sixth" => 6,
            "seventh" => 7,
            "eighth" => 8,
            _ => 0,
        };

    private static string LevelLabel(int ordinal) =>
        ordinal switch
        {
            0 => "basement",
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{ordinal}th",
        };

    private static string Normalize(string value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private readonly record struct ThreeDLevelKey(string Label, int Ordinal);
}
