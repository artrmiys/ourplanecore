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
            return JsonSerializer.Deserialize<ThreeDWallModel>(File.ReadAllText(path), OurPlaneCoreJobStore.JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException)
        {
            AppLog.Warn(ex, $"Failed to load 3D wall model {path}");
            return null;
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
