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
                NormalizeLegacyRoofGuides(model);
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

    public static void Save(OurPlaneCoreJob job, ThreeDWallModel model)
    {
        string path = ModelPath(job);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? job.RootPath);
        model.UpdatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(model, OurPlaneCoreJobStore.JsonOptions));
    }
}
