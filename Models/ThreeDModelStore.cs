using System.Globalization;
using System.IO;
using System.Text.Json;

namespace OurPlaneCore;

public sealed class ThreeDPoint
{
    public double XFeet { get; set; }
    public double ZFeet { get; set; }
}

public sealed class ThreeDFloorLevel
{
    public string Label { get; set; } = "";
    public int Ordinal { get; set; }
    public double BaseElevationFeet { get; set; }
    public double HeightFeet { get; set; }
}

public sealed class ThreeDFloorSlab
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TakeoffName { get; set; } = "";
    public string TakeoffFolder { get; set; } = "";
    public string MeasurementId { get; set; } = "";
    public string PageFolder { get; set; } = "";
    public List<ThreeDPoint> Points { get; set; } = [];
    public double ElevationFeet { get; set; }
    public double ThicknessFeet { get; set; } = 0.18;
    public string Label { get; set; } = "";
    public string Color { get; set; } = "#6BAED6";
    public string GroupKey { get; set; } = "";
    public string LevelKey { get; set; } = "";
    public string RoofGroupId { get; set; } = "";
    public string RoofGroupLabel { get; set; } = "";
}

public sealed class ThreeDRoofPlacement
{
    public string RoofGroupId { get; set; } = "";
    public string Label { get; set; } = "";
    public double OffsetXFeet { get; set; }
    public double OffsetYFeet { get; set; }
    public double OffsetZFeet { get; set; }
}

public sealed class ThreeDWallModel
{
    public string Source { get; set; } = "";
    public string UpdatedAtUtc { get; set; } = "";
    public List<ThreeDFloorLevel> Levels { get; set; } = [];
    public List<ThreeDWallSegment> Walls { get; set; } = [];
    public List<ThreeDFloorSlab> Slabs { get; set; } = [];
    public List<ThreeDRoofGuide> RoofGuides { get; set; } = [];
    public List<ThreeDRoofPlane> RoofPlanes { get; set; } = [];
    public List<ThreeDRoofIssue> RoofIssues { get; set; } = [];
    public List<ThreeDRoofPlacement> RoofPlacements { get; set; } = [];
    public List<ThreeDRoofRafterSettings> RoofRafters { get; set; } = [];

    // 3D placement nudge for the roof relative to the walls/slabs. Walls and
    // roof can come from different sheets and not sit on top of each other;
    // this offset (feet) lets the user move the roof into place in the viewer
    // without touching generation. Applied at render only.
    public double RoofOffsetXFeet { get; set; }
    public double RoofOffsetYFeet { get; set; }
    public double RoofOffsetZFeet { get; set; }
}

public static class ThreeDModelStore
{
    public static string ModelPath(OurPlaneCoreJob job) =>
        Path.Combine(job.RootPath, "3D_Context", "walls_model.json");

    public static ThreeDWallModel? Load(OurPlaneCoreJob job)
    {
        string path = ModelPath(job);
        if (!File.Exists(path))
            return null;

        try
        {
            ThreeDWallModel? model = JsonSerializer.Deserialize<ThreeDWallModel>(File.ReadAllText(path), OurPlaneCoreJobStore.JsonOptions);
            if (model != null)
            {
                NormalizeLegacyRoofGuides(model);
                NormalizeLegacyRoofGroups(model);
            }
            return model;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException)
        {
            AppLog.Warn(ex, $"Failed to load 3D wall model {path}");
            return null;
        }
    }

    // Pre-DefinesSlope models stored slope intent only in Kind == "eave".
    // If no guide carries the new flag, infer it from Kind so old jobs still
    // build a roof. New models always serialize DefinesSlope explicitly.
    private static void NormalizeLegacyRoofGuides(ThreeDWallModel model)
    {
        if (model.RoofGuides.Count == 0 ||
            model.RoofGuides.Any(guide => guide.DefinesSlope))
        {
            return;
        }

        foreach (ThreeDRoofGuide guide in model.RoofGuides)
        {
            guide.DefinesSlope =
                ThreeDRoofGuideKinds.Normalize(guide.Kind) == ThreeDRoofGuideKinds.Eave;
        }
    }

    private static void NormalizeLegacyRoofGroups(ThreeDWallModel model)
    {
        List<string> groupIds = model.Slabs
            .Where(IsRoofSlab)
            .Select(slab => slab.RoofGroupId)
            .Concat(model.RoofGuides.Select(guide => guide.RoofGroupId))
            .Concat(model.RoofPlanes.Select(plane => plane.RoofGroupId))
            .Concat(model.RoofIssues.Select(issue => issue.RoofGroupId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string legacyId = groupIds.FirstOrDefault() ?? "roof-legacy";
        string legacyLabel = model.Slabs.FirstOrDefault(IsRoofSlab)?.Label ?? "";
        if (string.IsNullOrWhiteSpace(legacyLabel))
            legacyLabel = model.RoofGuides.FirstOrDefault()?.Label ?? "";
        if (string.IsNullOrWhiteSpace(legacyLabel))
            legacyLabel = "Roof 1";

        foreach (ThreeDFloorSlab slab in model.Slabs.Where(IsRoofSlab))
        {
            if (string.IsNullOrWhiteSpace(slab.RoofGroupId))
                slab.RoofGroupId = legacyId;
            if (string.IsNullOrWhiteSpace(slab.RoofGroupLabel))
                slab.RoofGroupLabel = legacyLabel;
        }

        foreach (ThreeDRoofGuide guide in model.RoofGuides)
            NormalizeRoofGroup(guide, legacyId, legacyLabel);
        foreach (ThreeDRoofPlane plane in model.RoofPlanes)
            NormalizeRoofGroup(plane, legacyId, legacyLabel);
        foreach (ThreeDRoofIssue issue in model.RoofIssues)
            NormalizeRoofGroup(issue, legacyId, legacyLabel);

        groupIds = model.Slabs
            .Where(IsRoofSlab)
            .Select(slab => slab.RoofGroupId)
            .Concat(model.RoofGuides.Select(guide => guide.RoofGroupId))
            .Concat(model.RoofPlanes.Select(plane => plane.RoofGroupId))
            .Concat(model.RoofIssues.Select(issue => issue.RoofGroupId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string groupId in groupIds)
        {
            if (model.RoofPlacements.Any(placement =>
                    string.Equals(placement.RoofGroupId, groupId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string label = model.Slabs.FirstOrDefault(slab =>
                    IsRoofSlab(slab) &&
                    string.Equals(slab.RoofGroupId, groupId, StringComparison.OrdinalIgnoreCase))?.RoofGroupLabel ??
                model.RoofGuides.FirstOrDefault(guide =>
                    string.Equals(guide.RoofGroupId, groupId, StringComparison.OrdinalIgnoreCase))?.RoofGroupLabel ??
                legacyLabel;
            model.RoofPlacements.Add(new ThreeDRoofPlacement
            {
                RoofGroupId = groupId,
                Label = string.IsNullOrWhiteSpace(label) ? "Roof" : label,
                OffsetXFeet = model.RoofOffsetXFeet,
                OffsetYFeet = model.RoofOffsetYFeet,
                OffsetZFeet = model.RoofOffsetZFeet,
            });
        }
    }

    private static bool IsRoofSlab(ThreeDFloorSlab slab) =>
        string.Equals(slab.LevelKey, "roof", StringComparison.OrdinalIgnoreCase) ||
        slab.GroupKey.StartsWith(ThreeDRoofFootprintBuildService.GeneratedSlabGroupPrefix, StringComparison.OrdinalIgnoreCase);

    private static void NormalizeRoofGroup(ThreeDRoofGuide guide, string groupId, string label)
    {
        if (string.IsNullOrWhiteSpace(guide.RoofGroupId))
            guide.RoofGroupId = groupId;
        if (string.IsNullOrWhiteSpace(guide.RoofGroupLabel))
            guide.RoofGroupLabel = label;
    }

    private static void NormalizeRoofGroup(ThreeDRoofPlane plane, string groupId, string label)
    {
        if (string.IsNullOrWhiteSpace(plane.RoofGroupId))
            plane.RoofGroupId = groupId;
        if (string.IsNullOrWhiteSpace(plane.RoofGroupLabel))
            plane.RoofGroupLabel = label;
    }

    private static void NormalizeRoofGroup(ThreeDRoofIssue issue, string groupId, string label)
    {
        if (string.IsNullOrWhiteSpace(issue.RoofGroupId))
            issue.RoofGroupId = groupId;
        if (string.IsNullOrWhiteSpace(issue.RoofGroupLabel))
            issue.RoofGroupLabel = label;
    }

    public static void Save(OurPlaneCoreJob job, ThreeDWallModel model)
    {
        string path = ModelPath(job);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? job.RootPath);
        model.UpdatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(model, OurPlaneCoreJobStore.JsonOptions));
    }
}
