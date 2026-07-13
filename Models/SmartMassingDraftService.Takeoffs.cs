using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SkiaSharp;


namespace OurPlanCore;

public static partial class SmartMassingDraftService
{
    // Measured-takeoff draft construction, source grouping, and footprint extraction.

    public static SmartMassingDraft BuildDraftFromWallTakeoffs(OurPlanCoreJob job, double levelSpacingFeet) =>
        BuildDraftFromWallTakeoffs(job, levelSpacingFeet, null);

    public static SmartMassingDraft BuildDraftFromWallTakeoffs(
        OurPlanCoreJob job,
        double levelSpacingFeet,
        SmartMassingTakeoffAiPlan? aiPlan)
    {
        double floorToFloorFeet = NormalizeLevelSpacingFeet(levelSpacingFeet);
        var draft = new SmartMassingDraft
        {
            Id = $"massing_takeoffs_{DateTime.UtcNow:yyyyMMdd_HHmmss}",
            Status = "draft_from_takeoffs",
            Units = "feet",
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
        };

        IReadOnlyList<TakeoffItem> loadedItems = OurPlanCoreJobStore.LoadTakeoffItems(job);
        IReadOnlyList<TakeoffItem> allItems = ApplyAiTakeoffPlan(job, loadedItems, aiPlan, draft);
        List<string> takeoffRoots = FindMassingTakeoffRoots(job).ToList();
        bool hasAiAssignments = aiPlan != null && aiPlan.Assignments.Count > 0;
        if (takeoffRoots.Count == 0 && !hasAiAssignments)
        {
            draft.Status = "needs_massing_takeoffs";
            draft.UnresolvedQuestions.Add("Create a Takeoffs/Walls or Takeoffs/Areas folder with level folders such as 1st, 2nd, 3rd and Line/Area measurements.");
            return draft;
        }

        var floorSources = BuildWallFloorSources(job, takeoffRoots, allItems);
        AddAiPlanFloorSources(job, floorSources, allItems, aiPlan);
        if (floorSources.Count == 0)
        {
            draft.Status = "needs_massing_takeoffs";
            draft.UnresolvedQuestions.Add("No level folders or Line/Area measurements were found under a Walls/Areas takeoff folder.");
            return draft;
        }

        var levelSources = floorSources
            .GroupBy(source => source.Level)
            .Select(group => new WallFloorSource(
                group.Key,
                "",
                string.Join(" + ", group.Select(source => source.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase)),
                group
                    .SelectMany(source => source.Items)
                    .GroupBy(item => Path.GetFullPath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
                    .Select(itemGroup => itemGroup.First())
                    .ToList()))
            .OrderBy(source => source.Level)
            .ThenBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var footprintTransforms = new Dictionary<int, MassingTakeoffTransform>();
        foreach (WallFloorSource floor in levelSources)
        {
            List<TakeoffItem> areaItems = floor.Items
                .Where(item => OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "area")
                .Where(item => item.Measurements.Any(measurement => measurement.Points.Count >= 3))
                .ToList();
            List<TakeoffItem> lineItems = floor.Items
                .Where(item => OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "line")
                .Where(item => item.Measurements.Any(measurement => measurement.Points.Count >= 2))
                .ToList();
            if (areaItems.Count == 0 && lineItems.Count == 0)
            {
                draft.UnresolvedQuestions.Add($"Level {floor.Level} ({floor.DisplayName}) has no Area/sqft floor plate or Line wall measurements.");
                continue;
            }

            string sourceKind;
            List<TakeoffItem> preferredAreaItems = areaItems.Where(IsFootprintTakeoff).ToList();
            List<TakeoffItem> sqftAreaItems = areaItems.Where(IsSqftTakeoff).ToList();
            List<TakeoffItem> exteriorLineItems = lineItems.Where(IsExteriorWallTakeoff).ToList();
            List<TakeoffItem> sourceItems;
            if (preferredAreaItems.Count > 0)
            {
                sourceKind = "Area";
                sourceItems = preferredAreaItems;
                draft.Assumptions.Add($"Level {floor.Level} uses {preferredAreaItems.Count} footprint/exterior area item(s) under {floor.DisplayName}.");
            }
            else if (sqftAreaItems.Count > 0)
            {
                sourceKind = "Area";
                sourceItems = sqftAreaItems;
                draft.Assumptions.Add($"Level {floor.Level} uses {sqftAreaItems.Count} sqft area item(s) under {floor.DisplayName} as the floor plate.");
            }
            else if (areaItems.Count > 0)
            {
                sourceKind = "Area";
                sourceItems = areaItems;
                draft.Assumptions.Add($"Level {floor.Level} uses all Area items under {floor.DisplayName}; add 'sqft', 'footprint', 'floor', 'ext', or 'exterior' to item names to prefer a floor plate.");
            }
            else if (exteriorLineItems.Count > 0)
            {
                sourceKind = "Line";
                sourceItems = exteriorLineItems;
                draft.Assumptions.Add($"Level {floor.Level} uses {exteriorLineItems.Count} exterior wall line item(s) under {floor.DisplayName}.");
            }
            else
            {
                sourceKind = "Line";
                sourceItems = lineItems;
                draft.Assumptions.Add($"Level {floor.Level} uses all Line items under {floor.DisplayName}; add 'ext' or 'exterior' to item names to prefer exterior walls only.");
            }

            if (!TryBuildFootprintFromTakeoffItems(job, sourceItems, sourceKind, out List<SmartMassingPoint> points, out string page, out List<string> sourceIds, out MassingTakeoffTransform? transform, out string error))
            {
                draft.UnresolvedQuestions.Add($"Level {floor.Level} ({floor.DisplayName}): {error}");
                continue;
            }

            List<TakeoffItem> heightItems = exteriorLineItems.Count > 0 ? exteriorLineItems : lineItems.Count > 0 ? lineItems : sourceItems;
            double heightFeet = ResolveWallTakeoffHeightFeet(heightItems, out string heightSource);
            double baseElevationFeet = TryParseBaseElevationFromText(floor.DisplayName, out double explicitBaseElevationFeet)
                ? explicitBaseElevationFeet
                : BaseElevationForLevel(floor.Level, floorToFloorFeet);
            if (heightSource.Length == 0)
                draft.Assumptions.Add($"Level {floor.Level} height defaults to {heightFeet:F2} ft. Put a height in the item name, e.g. 'ext 2x6 9.0'.");
            else
                draft.Assumptions.Add($"Level {floor.Level} height uses {heightSource}: {heightFeet:F2} ft.");
            if (Math.Abs(baseElevationFeet - BaseElevationForLevel(floor.Level, floorToFloorFeet)) > 0.0001)
                draft.Assumptions.Add($"Level {floor.Level} base elevation uses folder text: {baseElevationFeet:F2} ft.");
            else
                draft.Assumptions.Add($"Level {floor.Level} base elevation uses default level grid: {baseElevationFeet:F2} ft.");

            var footprint = new SmartMassingFootprint
            {
                Id = $"takeoff_{sourceKind.ToLowerInvariant()}_level_{floor.Level}",
                Level = floor.Level,
                Page = page,
                BaseElevation = Math.Round(baseElevationFeet, 3),
                BaseElevationUnits = "feet",
                Height = Math.Round(heightFeet, 3),
                HeightUnits = "feet",
                Confidence = sourceKind == "Area" ? 0.62 : sourceItems.Any(IsExteriorWallTakeoff) ? 0.55 : 0.42,
                SourceMarkerIds = sourceIds,
                Points = points,
            };
            draft.Footprints.Add(footprint);
            if (transform != null)
                footprintTransforms[footprint.Level] = transform;
        }

        if (draft.Footprints.Count == 0)
        {
            draft.Status = "needs_massing_takeoffs";
            draft.UnresolvedQuestions.Add("No valid footprint could be built. Check that Area/Line measurements have a sheet scale.");
            return draft;
        }

        draft.SourceMarkerIds = draft.Footprints
            .SelectMany(footprint => footprint.SourceMarkerIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        double roofElevationFeet = draft.Footprints.Max(footprint => footprint.BaseElevation) + floorToFloorFeet;
        draft.Roof = new SmartMassingRoof
        {
            Type = "unknown",
            Elevation = Math.Round(roofElevationFeet, 3),
            ElevationUnits = "feet",
            Notes = "Generated from measured takeoff Line/Area geometry. Add roof markers/review to refine roof planes.",
            Confidence = 0.18,
        };
        AddTakeoffRoofGuides(job, draft, allItems, footprintTransforms);
        draft.Assumptions.Add($"Default level grid: first floor 0 ft, then every {floorToFloorFeet:F2} ft; roof seed elevation {roofElevationFeet:F2} ft.");
        draft.Assumptions.Add("Simple 3D system: floor/sqft or wall takeoffs build footprint levels, footprints extrude into wall faces, and the top footprint seeds roof eave/axis guide candidates for review.");
        draft.Assumptions.Add("Area/sqft takeoff polygons preserve point order when a single area is used; combined areas/lines are auto-deduplicated and ordered around their centroid, so review concave or partial layouts.");
        RefreshDerivedGeometry(draft);
        return draft;
    }

    private static IEnumerable<string> FindMassingTakeoffRoots(OurPlanCoreJob job)
    {
        if (!Directory.Exists(job.TakeoffsRoot))
            yield break;

        foreach (string folder in Directory.EnumerateDirectories(job.TakeoffsRoot, "*", SearchOption.AllDirectories))
        {
            if (OurPlanCoreJobStore.IsTakeoffItemFolder(folder))
                continue;

            string normalized = NormalizeTakeoffName(OurPlanCoreJobStore.DisplayName(folder));
            string compact = normalized.Trim();
            if (compact is "wall" or "walls" or "area" or "areas" or "floor" or "floors" or "slab" or "slabs" or "sqft" or "sqfts" or "sft" or "sf" or "sq ft" or "square feet" or "square footage")
                yield return folder;
        }
    }

    private static List<WallFloorSource> BuildWallFloorSources(
        OurPlanCoreJob job,
        IReadOnlyList<string> wallRoots,
        IReadOnlyList<TakeoffItem> allItems)
    {
        var sources = new List<WallFloorSource>();
        foreach (string wallRoot in wallRoots)
        {
            List<string> levelFolders = OurPlanCoreJobStore.GetOrderedChildDirectories(wallRoot)
                .Where(folder => !OurPlanCoreJobStore.IsTakeoffItemFolder(folder))
                .Where(folder => TryParseLevel(OurPlanCoreJobStore.DisplayName(folder), out _))
                .ToList();

            if (levelFolders.Count == 0)
            {
                List<TakeoffItem> items = ItemsUnderFolder(allItems, wallRoot);
                if (items.Count > 0)
                    AddWallFloorSourcesFromItems(sources, wallRoot, OurPlanCoreJobStore.DisplayName(wallRoot), items, fallbackLevel: 1);
                continue;
            }

            foreach (string levelFolder in levelFolders)
            {
                int level = TryParseLevel(OurPlanCoreJobStore.DisplayName(levelFolder), out int parsedLevel)
                    ? parsedLevel
                    : sources.Count + 1;
                List<TakeoffItem> items = ItemsUnderFolder(allItems, levelFolder);
                if (items.Count > 0)
                    sources.Add(new WallFloorSource(level, levelFolder, OurPlanCoreJobStore.DisplayName(levelFolder), items));
            }
        }

        return sources
            .GroupBy(source => $"{Path.GetFullPath(source.FolderPath)}|{source.Level}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        static List<TakeoffItem> ItemsUnderFolder(IReadOnlyList<TakeoffItem> items, string folder) =>
            items
                .Where(item => OurPlanCoreJobStore.IsSameOrDescendant(folder, item.FolderPath))
                .ToList();
    }

    private static void AddWallFloorSourcesFromItems(
        List<WallFloorSource> sources,
        string folderPath,
        string displayName,
        IReadOnlyList<TakeoffItem> items,
        int fallbackLevel)
    {
        var keyedItems = items
            .Select(item => new
            {
                Item = item,
                HasLevel = TryParseLevel($"{item.Name} {item.Notes} {OurPlanCoreJobStore.DisplayName(item.FolderPath)}", out int level),
                Level = level,
            })
            .ToList();

        if (!keyedItems.Any(entry => entry.HasLevel))
        {
            sources.Add(new WallFloorSource(fallbackLevel, folderPath, displayName, items.ToList()));
            return;
        }

        foreach (var group in keyedItems
            .GroupBy(entry => entry.HasLevel ? entry.Level : fallbackLevel)
            .OrderBy(group => group.Key))
        {
            string sourceName = $"{displayName} {LevelDisplayName(group.Key)}".Trim();
            sources.Add(new WallFloorSource(
                group.Key,
                folderPath,
                sourceName,
                group.Select(entry => entry.Item).ToList()));
        }
    }

    private static bool TryBuildFootprintFromTakeoffItems(
        OurPlanCoreJob job,
        IReadOnlyList<TakeoffItem> items,
        string sourceKind,
        out List<SmartMassingPoint> footprintPoints,
        out string page,
        out List<string> sourceIds,
        out MassingTakeoffTransform? transform,
        out string error)
    {
        if (string.Equals(sourceKind, "Area", StringComparison.OrdinalIgnoreCase))
            return TryBuildFootprintFromAreaItems(job, items, out footprintPoints, out page, out sourceIds, out transform, out error);

        return TryBuildFootprintFromLineItems(job, items, out footprintPoints, out page, out sourceIds, out transform, out error);
    }

    private static bool TryBuildFootprintFromAreaItems(
        OurPlanCoreJob job,
        IReadOnlyList<TakeoffItem> items,
        out List<SmartMassingPoint> footprintPoints,
        out string page,
        out List<string> sourceIds,
        out MassingTakeoffTransform? transform,
        out string error)
    {
        footprintPoints = [];
        sourceIds = [];
        transform = null;
        error = "";
        page = "";

        var scaledAreas = new List<List<WallMeasurementPoint>>();
        SKPoint? origin = null;
        foreach (TakeoffItem item in items)
        {
            foreach (Measurement measurement in item.Measurements.Where(measurement => measurement.Points.Count >= 3))
            {
                double scaleMetersPerPt = ResolveMeasurementScaleMetersPerPt(measurement);
                if (scaleMetersPerPt <= 0)
                    continue;

                if (origin == null)
                {
                    origin = measurement.Points[0];
                    transform = new MassingTakeoffTransform(measurement.PageFolder, ResolveMeasurementPageName(measurement), origin.Value, scaleMetersPerPt);
                }

                page = ResolveMeasurementPageName(measurement);
                var polygon = new List<WallMeasurementPoint>();
                foreach (SKPoint point in measurement.Points)
                {
                    double x = (point.X - origin.Value.X) * scaleMetersPerPt / MetersPerFoot;
                    double y = (point.Y - origin.Value.Y) * scaleMetersPerPt / MetersPerFoot;
                    polygon.Add(new WallMeasurementPoint(
                        Math.Round(x, 3),
                        Math.Round(y, 3),
                        MeasurementSourceId(item, measurement)));
                }

                polygon = RemoveClosingDuplicate(polygon, toleranceFeet: 0.15);
                if (polygon.Count >= 3)
                    scaledAreas.Add(polygon);
            }
        }

        if (scaledAreas.Count == 0)
        {
            error = "Need at least one scaled Area measurement with three or more points. Set sheet scale before building 3D from areas.";
            return false;
        }

        List<WallMeasurementPoint> ordered = scaledAreas.Count == 1
            ? scaledAreas[0]
            : OrderWallPoints(DeduplicateWallPoints(scaledAreas.SelectMany(points => points).ToList(), toleranceFeet: 0.15));
        if (ordered.Count < 3)
        {
            error = "Area measurements collapsed to fewer than three unique footprint points.";
            return false;
        }

        footprintPoints = ordered
            .Select(point => new SmartMassingPoint
            {
                X = Math.Round(point.X, 3),
                Y = Math.Round(point.Y, 3),
                SourceMarkerId = point.SourceId,
            })
            .ToList();
        sourceIds = ordered
            .Select(point => point.SourceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return true;
    }

    private static bool TryBuildFootprintFromLineItems(
        OurPlanCoreJob job,
        IReadOnlyList<TakeoffItem> items,
        out List<SmartMassingPoint> footprintPoints,
        out string page,
        out List<string> sourceIds,
        out MassingTakeoffTransform? transform,
        out string error)
    {
        footprintPoints = [];
        sourceIds = [];
        transform = null;
        error = "";
        page = "";

        List<WallMeasurementPoint> rawPoints = [];
        SKPoint? origin = null;
        string originPageFolder = "";
        foreach (TakeoffItem item in items)
        {
            foreach (Measurement measurement in item.Measurements.Where(measurement => measurement.Points.Count >= 2))
            {
                double scaleMetersPerPt = ResolveMeasurementScaleMetersPerPt(measurement);
                if (scaleMetersPerPt <= 0)
                    continue;

                if (origin == null)
                {
                    origin = measurement.Points[0];
                    transform = new MassingTakeoffTransform(measurement.PageFolder, ResolveMeasurementPageName(measurement), origin.Value, scaleMetersPerPt);
                }

                if (string.IsNullOrWhiteSpace(originPageFolder))
                    originPageFolder = measurement.PageFolder;
                page = ResolveMeasurementPageName(measurement);

                foreach (SKPoint point in measurement.Points)
                {
                    double x = (point.X - origin.Value.X) * scaleMetersPerPt / MetersPerFoot;
                    double y = (point.Y - origin.Value.Y) * scaleMetersPerPt / MetersPerFoot;
                    rawPoints.Add(new WallMeasurementPoint(
                        Math.Round(x, 3),
                        Math.Round(y, 3),
                        MeasurementSourceId(item, measurement)));
                }
            }
        }

        if (rawPoints.Count < 3)
        {
            error = "Need at least three scaled Line measurement points. Set sheet scale before building 3D from takeoffs.";
            return false;
        }

        List<WallMeasurementPoint> unique = DeduplicateWallPoints(rawPoints, toleranceFeet: 0.15);
        if (unique.Count < 3)
        {
            error = "Line measurements collapsed to fewer than three unique footprint points.";
            return false;
        }

        List<WallMeasurementPoint> ordered = OrderWallPoints(unique);
        footprintPoints = ordered
            .Select(point => new SmartMassingPoint
            {
                X = Math.Round(point.X, 3),
                Y = Math.Round(point.Y, 3),
                SourceMarkerId = point.SourceId,
            })
            .ToList();
        sourceIds = ordered
            .Select(point => point.SourceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return true;
    }

    private static List<WallMeasurementPoint> RemoveClosingDuplicate(
        List<WallMeasurementPoint> points,
        double toleranceFeet)
    {
        if (points.Count < 2)
            return points;

        WallMeasurementPoint first = points[0];
        WallMeasurementPoint last = points[^1];
        double distanceSq = ((first.X - last.X) * (first.X - last.X)) +
                            ((first.Y - last.Y) * (first.Y - last.Y));
        return distanceSq <= toleranceFeet * toleranceFeet
            ? points.Take(points.Count - 1).ToList()
            : points;
    }

    private static List<WallMeasurementPoint> DeduplicateWallPoints(
        IReadOnlyList<WallMeasurementPoint> points,
        double toleranceFeet)
    {
        var unique = new List<WallMeasurementPoint>();
        double toleranceSq = toleranceFeet * toleranceFeet;
        foreach (WallMeasurementPoint point in points)
        {
            WallMeasurementPoint? existing = unique.FirstOrDefault(candidate =>
                ((candidate.X - point.X) * (candidate.X - point.X)) +
                ((candidate.Y - point.Y) * (candidate.Y - point.Y)) <= toleranceSq);
            if (existing == null)
            {
                unique.Add(point);
                continue;
            }
        }

        return unique;
    }

    private static List<WallMeasurementPoint> OrderWallPoints(IReadOnlyList<WallMeasurementPoint> points)
    {
        double centerX = points.Average(point => point.X);
        double centerY = points.Average(point => point.Y);
        return points
            .OrderBy(point => Math.Atan2(point.Y - centerY, point.X - centerX))
            .ToList();
    }

    private static double ResolveWallTakeoffHeightFeet(IReadOnlyList<TakeoffItem> items, out string source)
    {
        var parsed = new List<(double Height, string Source)>();
        foreach (TakeoffItem item in items)
        {
            if (TryParseWallHeightFeet($"{item.Name} {item.Notes}", out double itemHeight))
                parsed.Add((itemHeight, item.Name));
            foreach (Measurement measurement in item.Measurements)
            {
                if (TryParseWallHeightFeet($"{measurement.Name} {measurement.Notes}", out double measurementHeight))
                    parsed.Add((measurementHeight, string.IsNullOrWhiteSpace(measurement.Name) ? item.Name : measurement.Name));
            }
        }

        if (parsed.Count == 0)
        {
            source = "";
            return DefaultWallHeightFeet;
        }

        (double height, string heightSource) = parsed
            .OrderByDescending(candidate => candidate.Height)
            .First();
        source = heightSource;
        return height;
    }

    private static bool TryParseWallHeightFeet(string text, out double heightFeet)
    {
        heightFeet = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        Match keyed = HeightKeyRegex().Match(text);
        if (keyed.Success && TryParseHeightFeet(keyed.Groups["value"].Value ?? "", out heightFeet))
            return heightFeet > 0;

        if (TryParseHeightFeet(text, out heightFeet) &&
            (FeetInchesRegex().IsMatch(text) || MetersRegex().IsMatch(text)))
        {
            return heightFeet > 0;
        }

        string withoutStudSize = StudSizeRegex().Replace(text, " ");
        MatchCollection matches = DecimalNumberRegex().Matches(withoutStudSize);
        var values = matches
            .Select(match => double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0)
            .Where(value => value >= 4 && value <= 30)
            .ToList();
        if (values.Count == 0)
            return false;

        heightFeet = values[^1];
        return true;
    }

    private static bool TryParseBaseElevationFromText(string text, out double baseElevationFeet)
    {
        Match keyed = BaseElevationKeyRegex().Match(text ?? "");
        if (keyed.Success && TryParseHeightFeet(keyed.Groups["value"].Value ?? "", out baseElevationFeet))
            return true;

        baseElevationFeet = 0;
        return false;
    }

    private static bool IsExteriorWallTakeoff(TakeoffItem item)
    {
        string text = NormalizeTakeoffName($"{item.Name} {item.Notes} {item.FolderPath}");
        return text.Contains(" ext ", StringComparison.Ordinal) ||
               text.StartsWith("ext ", StringComparison.Ordinal) ||
               text.EndsWith(" ext", StringComparison.Ordinal) ||
               text.Contains(" exterior ", StringComparison.Ordinal) ||
               text.Contains("outside", StringComparison.Ordinal) ||
               text.Contains("perimeter", StringComparison.Ordinal);
    }

    private static bool IsFootprintTakeoff(TakeoffItem item)
    {
        string text = NormalizeTakeoffName($"{item.Name} {item.Notes} {item.FolderPath}");
        return text.Contains(" footprint ", StringComparison.Ordinal) ||
               text.Contains(" floor ", StringComparison.Ordinal) ||
               text.Contains(" slab ", StringComparison.Ordinal) ||
               text.Contains(" building ", StringComparison.Ordinal) ||
               IsExteriorWallTakeoff(item);
    }

    private static bool IsSqftTakeoff(TakeoffItem item)
    {
        string text = NormalizeTakeoffName($"{item.Name} {item.Notes} {item.FolderPath}");
        return text.Contains(" sqft ", StringComparison.Ordinal) ||
               text.Contains(" sqfts ", StringComparison.Ordinal) ||
               text.Contains(" sft ", StringComparison.Ordinal) ||
               text.Contains(" sq ft ", StringComparison.Ordinal) ||
               text.Contains(" sf ", StringComparison.Ordinal) ||
               text.Contains(" square feet ", StringComparison.Ordinal) ||
               text.Contains(" square foot ", StringComparison.Ordinal) ||
               text.Contains(" square footage ", StringComparison.Ordinal);
    }

    private static double ResolveMeasurementScaleMetersPerPt(Measurement measurement)
    {
        if (measurement.ScaleMetersPerPt > 0)
            return measurement.ScaleMetersPerPt;
        if (!string.IsNullOrWhiteSpace(measurement.PageFolder))
            return OurPlanCoreJobStore.TryReadPage(measurement.PageFolder)?.ScaleMetersPerPt ?? 0;
        return 0;
    }

    private static string ResolveMeasurementPageName(Measurement measurement)
    {
        if (!string.IsNullOrWhiteSpace(measurement.PageFolder) &&
            OurPlanCoreJobStore.TryReadPage(measurement.PageFolder) is { } page)
        {
            return page.Name;
        }

        return "";
    }

    private static string MeasurementSourceId(TakeoffItem item, Measurement measurement) =>
        $"takeoff:{Path.GetFileName(item.FolderPath)}:{measurement.Id}";

    private static double NormalizeLevelSpacingFeet(double levelSpacingFeet) =>
        double.IsNaN(levelSpacingFeet) || double.IsInfinity(levelSpacingFeet) || levelSpacingFeet <= 0
            ? DefaultLevelSpacingFeet
            : Math.Clamp(levelSpacingFeet, 1, 40);

    private static double BaseElevationForLevel(int level, double levelSpacingFeet) =>
        Math.Max(0, level - 1) * levelSpacingFeet;

    private static string LevelDisplayName(int level) =>
        level == 0
            ? "base"
            : $"{level}{OrdinalSuffix(level)}";

    private static string OrdinalSuffix(int value)
    {
        int mod100 = Math.Abs(value) % 100;
        if (mod100 is >= 11 and <= 13)
            return "th";

        return (Math.Abs(value) % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
    }

    private static string NormalizeTakeoffName(string text) =>
        $" {Regex.Replace(text ?? "", @"[^a-z0-9]+", " ").Trim().ToLowerInvariant()} ";

    private static bool TryParseLevel(string text, out int level)
    {
        string normalized = NormalizeTakeoffName(text);
        if (normalized.Contains(" basement ", StringComparison.Ordinal) ||
            normalized.Contains(" bsmnt ", StringComparison.Ordinal) ||
            normalized.Contains(" base ", StringComparison.Ordinal))
        {
            level = 0;
            return true;
        }

        if (normalized.Contains(" first ", StringComparison.Ordinal) || normalized.Contains(" 1st ", StringComparison.Ordinal))
        {
            level = 1;
            return true;
        }
        if (normalized.Contains(" second ", StringComparison.Ordinal) || normalized.Contains(" 2nd ", StringComparison.Ordinal))
        {
            level = 2;
            return true;
        }
        if (normalized.Contains(" third ", StringComparison.Ordinal) || normalized.Contains(" 3rd ", StringComparison.Ordinal))
        {
            level = 3;
            return true;
        }

        var levelAliases = new (int Level, string[] Aliases)[]
        {
            (4, [" fourth ", " 4th "]),
            (5, [" fifth ", " 5th "]),
            (6, [" sixth ", " 6th "]),
            (7, [" seventh ", " 7th "]),
            (8, [" eighth ", " 8th "]),
        };
        foreach ((int aliasLevel, string[] aliases) in levelAliases)
        {
            if (aliases.Any(alias => normalized.Contains(alias, StringComparison.Ordinal)))
            {
                level = aliasLevel;
                return true;
            }
        }

        Match match = WallLevelRegex().Match(text ?? "");
        if (match.Success &&
            int.TryParse(match.Groups["level"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            level = Math.Max(1, parsed);
            return true;
        }

        level = 0;
        return false;
    }
}
