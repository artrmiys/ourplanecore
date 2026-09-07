using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SkiaSharp;


namespace OurPlanCore;

public static partial class SmartMassingDraftService
{
    // Public draft paths, persistence, loading, and marker-based entry points.

    private const double MetersPerFoot = 0.3048;
    private const double DefaultWallHeightFeet = 9.0;
    public const double DefaultFloorAssemblyFeet = 2.0;
    public const double DefaultLevelSpacingFeet = 10.0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string ModelPath(OurPlanCoreJob job) =>
        Path.Combine(job.AIContextRoot, "3d_massing", "model.json");

    public static string SnapshotsRoot(OurPlanCoreJob job) =>
        Path.Combine(job.AIContextRoot, "3d_massing", "snapshots");

    public static SmartMassingDraft SaveDraftFromMarkers(OurPlanCoreJob job)
    {
        SmartMassingDraft draft = BuildDraftFromMarkers(job);
        SaveDraft(job, draft);
        return draft;
    }

    public static SmartMassingDraft SaveDraftFromWallTakeoffs(OurPlanCoreJob job, double levelSpacingFeet)
    {
        SmartMassingDraft draft = BuildDraftFromWallTakeoffs(job, levelSpacingFeet);
        SaveDraft(job, draft);
        return draft;
    }

    public static SmartMassingDraft SaveDraftFromWallTakeoffs(
        OurPlanCoreJob job,
        double levelSpacingFeet,
        SmartMassingTakeoffAiPlan? aiPlan)
    {
        SmartMassingDraft draft = BuildDraftFromWallTakeoffs(job, levelSpacingFeet, aiPlan);
        SaveDraft(job, draft);
        return draft;
    }

    public static void SaveDraft(OurPlanCoreJob job, SmartMassingDraft draft)
    {
        string path = ModelPath(job);
        JobWriteAccess.Demand(path, "save 3D massing draft");
        RefreshDerivedGeometry(draft);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? job.AIContextRoot);
        IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(draft, JsonOptions));
    }

    public static string SaveSnapshot(OurPlanCoreJob job, SmartMassingDraft draft)
    {
        string root = SnapshotsRoot(job);
        JobWriteAccess.Demand(root, "save 3D massing snapshot");
        RefreshDerivedGeometry(draft);
        Directory.CreateDirectory(root);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string id = SafeFilePart(string.IsNullOrWhiteSpace(draft.Id) ? "massing" : draft.Id);
        string path = Path.Combine(root, $"{stamp}_{id}.json");
        IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(draft, JsonOptions));
        return path;
    }

    public static void RefreshDerivedGeometry(SmartMassingDraft draft)
    {
        draft.Roof.Planes = BuildRoofPlanes(draft);
    }

    public static SmartMassingDraft? LoadDraft(OurPlanCoreJob job)
    {
        string path = ModelPath(job);
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<SmartMassingDraft>(File.ReadAllText(path));
    }

    public static SmartMassingDraft BuildDraftFromMarkers(OurPlanCoreJob job)
    {
        IReadOnlyList<SmartAiMarker> markers = SmartContextStore.LoadAiMarkers(job);
        var draft = new SmartMassingDraft
        {
            Id = $"massing_{DateTime.UtcNow:yyyyMMdd_HHmmss}",
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
        };

        List<SmartAiMarker> corners = markers
            .Where(marker => MarkerTypeEquals(marker, "exterior_corner"))
            .Where(marker => !string.Equals(marker.SampleKind, "ignore", StringComparison.OrdinalIgnoreCase))
            .ToList();
        List<SmartAiMarker> heights = markers
            .Where(marker => MarkerTypeEquals(marker, "wall_height_sample"))
            .Where(marker => !string.Equals(marker.SampleKind, "ignore", StringComparison.OrdinalIgnoreCase))
            .ToList();
        List<SmartAiMarker> roofs = markers
            .Where(IsRoofMarker)
            .Where(marker => !string.Equals(marker.SampleKind, "ignore", StringComparison.OrdinalIgnoreCase))
            .ToList();
        List<SmartAiMarker> openings = markers
            .Where(IsOpeningMarker)
            .Where(marker => !string.Equals(marker.SampleKind, "ignore", StringComparison.OrdinalIgnoreCase))
            .ToList();

        draft.SourceMarkerIds = corners
            .Concat(heights)
            .Concat(roofs)
            .Concat(openings)
            .Select(marker => marker.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (corners.Count < 3)
        {
            draft.UnresolvedQuestions.Add("Place at least three exterior_corner markers to build a footprint draft.");
        }
        else
        {
            double inferredBaseElevationFeet = 0;
            foreach (IGrouping<int, SmartAiMarker> levelCorners in corners.GroupBy(MarkerLevel).OrderBy(group => group.Key))
            {
                List<SmartAiMarker> levelCornerList = levelCorners.ToList();
                if (levelCornerList.Count < 3)
                {
                    draft.UnresolvedQuestions.Add($"Level {levelCorners.Key} has only {levelCornerList.Count} exterior_corner marker(s); add at least three.");
                    continue;
                }

                SmartMassingFootprint? footprint = AddFootprint(
                    job,
                    draft,
                    levelCornerList,
                    heights,
                    levelCorners.Key,
                    inferredBaseElevationFeet);
                if (footprint != null)
                    inferredBaseElevationFeet = Math.Max(
                        inferredBaseElevationFeet,
                        footprint.BaseElevation + Math.Max(footprint.Height, DefaultWallHeightFeet));
            }
        }

        AddRoof(job, draft, markers, roofs);
        AddOpenings(job, draft, markers, openings);
        if (draft.Footprints.Count == 0)
            draft.Status = "needs_markers";

        return draft;
    }

}
