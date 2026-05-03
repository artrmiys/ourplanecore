using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace SmartTakeoffs;

public sealed class SmartMassingDraft
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("units")]
    public string Units { get; set; } = "pdf_points";

    [JsonPropertyName("generated_at_utc")]
    public string GeneratedAtUtc { get; set; } = "";

    [JsonPropertyName("reviewed_at_utc")]
    public string ReviewedAtUtc { get; set; } = "";

    [JsonPropertyName("review_notes")]
    public string ReviewNotes { get; set; } = "";

    [JsonPropertyName("source_marker_ids")]
    public List<string> SourceMarkerIds { get; set; } = [];

    [JsonPropertyName("assumptions")]
    public List<string> Assumptions { get; set; } = [];

    [JsonPropertyName("unresolved_questions")]
    public List<string> UnresolvedQuestions { get; set; } = [];

    [JsonPropertyName("footprints")]
    public List<SmartMassingFootprint> Footprints { get; set; } = [];

    [JsonPropertyName("roof")]
    public SmartMassingRoof Roof { get; set; } = new();

    [JsonPropertyName("openings")]
    public List<SmartMassingOpening> Openings { get; set; } = [];
}

public sealed class SmartMassingFootprint
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "footprint_main";

    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("points")]
    public List<SmartMassingPoint> Points { get; set; } = [];

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("height_units")]
    public string HeightUnits { get; set; } = "feet";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("source_marker_ids")]
    public List<string> SourceMarkerIds { get; set; } = [];
}

public sealed class SmartMassingPoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("source_marker_id")]
    public string SourceMarkerId { get; set; } = "";
}

public sealed class SmartMassingRoof
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "unknown";

    [JsonPropertyName("pitch")]
    public string Pitch { get; set; } = "";

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("reviewed_at_utc")]
    public string ReviewedAtUtc { get; set; } = "";

    [JsonPropertyName("review_notes")]
    public string ReviewNotes { get; set; } = "";

    [JsonPropertyName("source_marker_ids")]
    public List<string> SourceMarkerIds { get; set; } = [];

    [JsonPropertyName("guides")]
    public List<SmartMassingRoofGuide> Guides { get; set; } = [];

    [JsonPropertyName("planes")]
    public List<SmartMassingPlane> Planes { get; set; } = [];
}

public sealed class SmartMassingRoofGuide
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("points")]
    public List<SmartMassingPoint> Points { get; set; } = [];

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("source_marker_ids")]
    public List<string> SourceMarkerIds { get; set; } = [];

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}

public sealed class SmartMassingOpening
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("source_marker_id")]
    public string SourceMarkerId { get; set; } = "";

    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("wall_index")]
    public int WallIndex { get; set; } = -1;

    [JsonPropertyName("center")]
    public SmartMassingVertex Center { get; set; } = new();

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}

public sealed class SmartMassingPlane
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("points")]
    public List<SmartMassingVertex> Points { get; set; } = [];

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("source_marker_ids")]
    public List<string> SourceMarkerIds { get; set; } = [];

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}

public sealed class SmartMassingVertex
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }

    [JsonPropertyName("source_marker_id")]
    public string SourceMarkerId { get; set; } = "";
}

public static partial class SmartMassingDraftService
{
    private const double MetersPerFoot = 0.3048;
    private const double DefaultWallHeightFeet = 9.0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string ModelPath(SmartTakeoffsJob job) =>
        Path.Combine(job.AIContextRoot, "3d_massing", "model.json");

    public static string SnapshotsRoot(SmartTakeoffsJob job) =>
        Path.Combine(job.AIContextRoot, "3d_massing", "snapshots");

    public static SmartMassingDraft SaveDraftFromMarkers(SmartTakeoffsJob job)
    {
        SmartMassingDraft draft = BuildDraftFromMarkers(job);
        SaveDraft(job, draft);
        return draft;
    }

    public static void SaveDraft(SmartTakeoffsJob job, SmartMassingDraft draft)
    {
        RefreshDerivedGeometry(draft);
        string path = ModelPath(job);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? job.AIContextRoot);
        File.WriteAllText(path, JsonSerializer.Serialize(draft, JsonOptions));
    }

    public static string SaveSnapshot(SmartTakeoffsJob job, SmartMassingDraft draft)
    {
        RefreshDerivedGeometry(draft);
        string root = SnapshotsRoot(job);
        Directory.CreateDirectory(root);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string id = SafeFilePart(string.IsNullOrWhiteSpace(draft.Id) ? "massing" : draft.Id);
        string path = Path.Combine(root, $"{stamp}_{id}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(draft, JsonOptions));
        return path;
    }

    public static void RefreshDerivedGeometry(SmartMassingDraft draft)
    {
        draft.Roof.Planes = BuildRoofPlanes(draft);
    }

    public static SmartMassingDraft? LoadDraft(SmartTakeoffsJob job)
    {
        string path = ModelPath(job);
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<SmartMassingDraft>(File.ReadAllText(path));
    }

    public static SmartMassingDraft BuildDraftFromMarkers(SmartTakeoffsJob job)
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
            AddFootprint(job, draft, corners, heights);
        }

        AddRoof(job, draft, markers, roofs);
        AddOpenings(job, draft, markers, openings);
        if (draft.Footprints.Count == 0)
            draft.Status = "needs_markers";

        return draft;
    }

    private static void AddFootprint(
        SmartTakeoffsJob job,
        SmartMassingDraft draft,
        List<SmartAiMarker> corners,
        List<SmartAiMarker> heightMarkers)
    {
        List<SmartAiMarker> orderedCorners = OrderCorners(corners);
        SmartAiMarker originMarker = orderedCorners[0];
        SKPoint origin = new(originMarker.PdfPoint.X, originMarker.PdfPoint.Y);
        double scaleMetersPerPt = ResolveMarkerScale(job, originMarker);
        bool canUseFeet = scaleMetersPerPt > 0;

        double heightFeet = DefaultWallHeightFeet;
        if (TryResolveWallHeightFeet(heightMarkers, out double parsedHeightFeet, out SmartAiMarker? heightSource))
        {
            heightFeet = parsedHeightFeet;
            draft.Assumptions.Add($"Wall height uses marker {heightSource!.Id}: {heightFeet:F2} ft.");
        }
        else
        {
            draft.Assumptions.Add($"Wall height defaults to {DefaultWallHeightFeet:F0} ft until a wall_height_sample is reviewed.");
            draft.UnresolvedQuestions.Add("Add or edit a wall_height_sample marker with a height value.");
        }

        draft.Units = canUseFeet ? "feet" : "pdf_points";
        if (!canUseFeet)
            draft.UnresolvedQuestions.Add("Set scale on the source footprint page so 3D draft coordinates can be converted to feet.");

        draft.Assumptions.Add("Exterior corners were auto-ordered around their centroid and need review.");

        var footprint = new SmartMassingFootprint
        {
            Page = originMarker.Page,
            Height = heightFeet,
            Confidence = canUseFeet ? 0.45 : 0.25,
            SourceMarkerIds = orderedCorners.Select(marker => marker.Id).ToList(),
            Points = orderedCorners
                .Select(marker => ToMassingPoint(marker, origin, scaleMetersPerPt, canUseFeet))
                .ToList(),
        };

        draft.Footprints.Add(footprint);
    }

    private static void AddRoof(
        SmartTakeoffsJob job,
        SmartMassingDraft draft,
        IReadOnlyList<SmartAiMarker> allMarkers,
        List<SmartAiMarker> roofMarkers)
    {
        if (roofMarkers.Count == 0)
        {
            draft.Roof = new SmartMassingRoof
            {
                Type = "unknown",
                Notes = "No roof_note, roof_edge_sample, ridge_sample, valley_sample, roof_high_edge, roof_low_edge, or overhang_sample markers have been reviewed yet.",
                Confidence = 0.1,
                Guides = BuildRoofGuides(job, draft, allMarkers, "unknown", "", []),
            };
            draft.UnresolvedQuestions.Add("Add roof_note, roof_edge_sample, ridge_sample, valley_sample, high/low edge, or overhang markers to draft roof type/pitch.");
            return;
        }

        string notes = string.Join(
            Environment.NewLine,
            roofMarkers.Select(marker => $"{marker.Id}: {MarkerText(marker)}").Where(text => !string.IsNullOrWhiteSpace(text)));

        string pitch = ExtractPitch(notes);
        string roofType = InferRoofType(notes);

        draft.Roof = new SmartMassingRoof
        {
            Type = roofType,
            Pitch = pitch,
            Notes = notes,
            Confidence = roofType == "unknown" ? 0.35 : 0.45,
            SourceMarkerIds = roofMarkers.Select(marker => marker.Id).ToList(),
            Guides = BuildRoofGuides(job, draft, allMarkers, roofType, pitch, roofMarkers),
        };
        draft.Assumptions.Add("Roof geometry is a reviewable guide draft based on footprint bounds and roof markers.");
        if (roofMarkers.Any(IsExplicitRoofGuideMarker))
            draft.Assumptions.Add("Explicit roof guide markers are used before footprint-bound roof heuristics.");
        if (draft.Roof.Guides.Count == 0)
            draft.UnresolvedQuestions.Add("Add exterior_corner markers before roof guides can be drawn.");
    }

    private static List<SmartMassingRoofGuide> BuildRoofGuides(
        SmartTakeoffsJob job,
        SmartMassingDraft draft,
        IReadOnlyList<SmartAiMarker> allMarkers,
        string roofType,
        string pitch,
        IReadOnlyList<SmartAiMarker> roofMarkers)
    {
        SmartMassingFootprint? footprint = draft.Footprints.FirstOrDefault(footprint => footprint.Points.Count >= 3);
        if (footprint == null)
            return [];

        List<string> sourceMarkerIds = roofMarkers
            .Select(marker => marker.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        string sourceMarkerId = sourceMarkerIds.FirstOrDefault() ?? "";
        List<SmartMassingRoofGuide> guides = [];
        List<SmartMassingPoint> footprintPoints = footprint.Points
            .Select(point => CopyRoofPoint(point, sourceMarkerId))
            .ToList();

        guides.Add(new SmartMassingRoofGuide
        {
            Id = "roof_eave_outline",
            Kind = "eave_outline",
            Label = "Roof/eave outline follows footprint until overhang markers exist.",
            Points = footprintPoints,
            Confidence = sourceMarkerIds.Count == 0 ? 0.15 : 0.3,
            SourceMarkerIds = sourceMarkerIds,
            Notes = "Uses the reviewed footprint polygon as a roof cap outline. Overhangs are not modeled yet.",
        });

        double minX = footprint.Points.Min(point => point.X);
        double maxX = footprint.Points.Max(point => point.X);
        double minY = footprint.Points.Min(point => point.Y);
        double maxY = footprint.Points.Max(point => point.Y);
        double width = maxX - minX;
        double height = maxY - minY;
        if (width <= 0 || height <= 0)
            return guides;

        List<SmartMassingRoofGuide> explicitGuides = BuildExplicitRoofGuides(
            job,
            footprint,
            allMarkers,
            roofMarkers);
        if (explicitGuides.Count > 0)
        {
            guides.AddRange(explicitGuides);
            return guides;
        }

        bool longAxisX = width >= height;
        if (roofType is "flat" or "low_slope")
        {
            guides.Add(new SmartMassingRoofGuide
            {
                Id = "roof_low_slope_cap",
                Kind = "cap",
                Label = string.IsNullOrWhiteSpace(pitch) ? "Low-slope/flat roof cap" : $"Low-slope/flat roof cap ({pitch})",
                Points = footprintPoints,
                Confidence = 0.35,
                SourceMarkerIds = sourceMarkerIds,
                Notes = "Shown as a cap only; drains, crickets, parapets, and slope direction still need review.",
            });
            return guides;
        }

        if (roofType == "shed")
        {
            guides.Add(new SmartMassingRoofGuide
            {
                Id = "roof_slope_arrow",
                Kind = "slope_arrow",
                Label = string.IsNullOrWhiteSpace(pitch) ? "Draft shed roof slope direction" : $"Draft shed roof slope direction ({pitch})",
                Points = longAxisX
                    ? [RoofPoint((minX + maxX) / 2, minY + height * 0.2, sourceMarkerId), RoofPoint((minX + maxX) / 2, maxY - height * 0.2, sourceMarkerId)]
                    : [RoofPoint(minX + width * 0.2, (minY + maxY) / 2, sourceMarkerId), RoofPoint(maxX - width * 0.2, (minY + maxY) / 2, sourceMarkerId)],
                Confidence = 0.35,
                SourceMarkerIds = sourceMarkerIds,
                Notes = "Slope direction is a placeholder until roof edge/high-low markers are reviewed.",
            });
            return guides;
        }

        double insetRatio = roofType == "hip" ? 0.25 : 0.08;
        List<SmartMassingPoint> ridge = longAxisX
            ? [
                RoofPoint(minX + width * insetRatio, (minY + maxY) / 2, sourceMarkerId),
                RoofPoint(maxX - width * insetRatio, (minY + maxY) / 2, sourceMarkerId),
            ]
            : [
                RoofPoint((minX + maxX) / 2, minY + height * insetRatio, sourceMarkerId),
                RoofPoint((minX + maxX) / 2, maxY - height * insetRatio, sourceMarkerId),
            ];

        guides.Add(new SmartMassingRoofGuide
        {
            Id = roofType == "unknown" ? "roof_axis_candidate" : "roof_ridge_main",
            Kind = roofType switch
            {
                "hip" => "hip_ridge",
                "unknown" => "axis_candidate",
                _ => "ridge",
            },
            Label = RoofGuideLabel(roofType, pitch),
            Points = ridge,
            Confidence = roofType == "unknown" ? 0.2 : 0.35,
            SourceMarkerIds = sourceMarkerIds,
            Notes = roofType == "unknown"
                ? "Candidate roof axis is inferred from the footprint long axis. Confirm roof type before using it as a ridge."
                : "Ridge direction is inferred from the footprint long axis. Confirm against roof plan/elevation markers.",
        });

        return guides;
    }

    private static List<SmartAiMarker> OrderCorners(List<SmartAiMarker> corners)
    {
        double centerX = corners.Average(marker => marker.PdfPoint.X);
        double centerY = corners.Average(marker => marker.PdfPoint.Y);
        return corners
            .OrderBy(marker => Math.Atan2(marker.PdfPoint.Y - centerY, marker.PdfPoint.X - centerX))
            .ToList();
    }

    private static List<SmartMassingRoofGuide> BuildExplicitRoofGuides(
        SmartTakeoffsJob job,
        SmartMassingFootprint footprint,
        IReadOnlyList<SmartAiMarker> allMarkers,
        IReadOnlyList<SmartAiMarker> roofMarkers)
    {
        if (!TryResolveMassingTransform(job, footprint, allMarkers, out SKPoint origin, out double scaleMetersPerPt, out bool useFeet))
            return [];

        var guides = new List<SmartMassingRoofGuide>();
        AddMarkerLineGuide(
            guides,
            "roof_ridge_markers",
            "ridge",
            "Reviewed ridge markers",
            roofMarkers,
            "ridge_sample",
            origin,
            scaleMetersPerPt,
            useFeet,
            "Uses explicit ridge_sample markers placed by the reviewer.");
        AddMarkerLineGuide(
            guides,
            "roof_valley_markers",
            "valley",
            "Reviewed valley markers",
            roofMarkers,
            "valley_sample",
            origin,
            scaleMetersPerPt,
            useFeet,
            "Uses explicit valley_sample markers placed by the reviewer.");
        AddMarkerLineGuide(
            guides,
            "roof_edge_markers",
            "roof_edge",
            "Reviewed roof edge markers",
            roofMarkers,
            "roof_edge_sample",
            origin,
            scaleMetersPerPt,
            useFeet,
            "Uses explicit roof_edge_sample markers placed by the reviewer.");
        AddMarkerLineGuide(
            guides,
            "roof_high_edge_markers",
            "high_edge",
            "Reviewed high roof edge",
            roofMarkers,
            "roof_high_edge",
            origin,
            scaleMetersPerPt,
            useFeet,
            "Uses explicit roof_high_edge markers placed by the reviewer.");
        AddMarkerLineGuide(
            guides,
            "roof_low_edge_markers",
            "low_edge",
            "Reviewed low roof edge",
            roofMarkers,
            "roof_low_edge",
            origin,
            scaleMetersPerPt,
            useFeet,
            "Uses explicit roof_low_edge markers placed by the reviewer.");
        AddMarkerLineGuide(
            guides,
            "roof_overhang_markers",
            "overhang",
            "Reviewed overhang markers",
            roofMarkers,
            "overhang_sample",
            origin,
            scaleMetersPerPt,
            useFeet,
            "Uses explicit overhang_sample markers placed by the reviewer.");

        List<SmartAiMarker> high = RoofMarkersByType(roofMarkers, "roof_high_edge");
        List<SmartAiMarker> low = RoofMarkersByType(roofMarkers, "roof_low_edge");
        if (high.Count > 0 && low.Count > 0)
        {
            SmartMassingPoint highCenter = MarkerCenterPoint(high, origin, scaleMetersPerPt, useFeet);
            SmartMassingPoint lowCenter = MarkerCenterPoint(low, origin, scaleMetersPerPt, useFeet);
            guides.Add(new SmartMassingRoofGuide
            {
                Id = "roof_high_low_slope_arrow",
                Kind = "slope_arrow",
                Label = "Slope from high edge to low edge",
                Points = [highCenter, lowCenter],
                Confidence = 0.65,
                SourceMarkerIds = high
                    .Concat(low)
                    .Select(marker => marker.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Notes = "Direction is based on reviewed roof_high_edge and roof_low_edge markers.",
            });
        }

        return guides;
    }

    private static void AddMarkerLineGuide(
        List<SmartMassingRoofGuide> guides,
        string id,
        string kind,
        string label,
        IReadOnlyList<SmartAiMarker> roofMarkers,
        string markerType,
        SKPoint origin,
        double scaleMetersPerPt,
        bool useFeet,
        string notes)
    {
        List<SmartAiMarker> markers = RoofMarkersByType(roofMarkers, markerType);
        if (markers.Count == 0)
            return;

        List<SmartMassingPoint> points = markers
            .OrderBy(marker => marker.PdfPoint.X)
            .ThenBy(marker => marker.PdfPoint.Y)
            .Select(marker => ToMassingPoint(marker, origin, scaleMetersPerPt, useFeet))
            .ToList();
        if (points.Count == 1)
            points.Add(RoofPoint(points[0].X, points[0].Y, points[0].SourceMarkerId));

        guides.Add(new SmartMassingRoofGuide
        {
            Id = id,
            Kind = kind,
            Label = label,
            Points = points,
            Confidence = markers.Count >= 2 ? 0.7 : 0.45,
            SourceMarkerIds = markers.Select(marker => marker.Id).ToList(),
            Notes = markers.Count >= 2 ? notes : $"{notes} Add another {markerType} marker to define direction.",
        });
    }

    private static void AddOpenings(
        SmartTakeoffsJob job,
        SmartMassingDraft draft,
        IReadOnlyList<SmartAiMarker> allMarkers,
        IReadOnlyList<SmartAiMarker> openingMarkers)
    {
        if (openingMarkers.Count == 0)
            return;

        SmartMassingFootprint? footprint = draft.Footprints.FirstOrDefault(footprint => footprint.Points.Count >= 3);
        if (footprint == null)
        {
            draft.UnresolvedQuestions.Add("Add exterior_corner markers before opening samples can be projected to walls.");
            return;
        }

        if (!TryResolveMassingTransform(job, footprint, allMarkers, out SKPoint origin, out double scaleMetersPerPt, out bool useFeet))
        {
            draft.UnresolvedQuestions.Add("Could not resolve footprint transform for opening sample projection.");
            return;
        }

        double wallTopZ = DisplayWallHeight(draft, footprint);
        foreach (SmartAiMarker marker in openingMarkers)
        {
            SmartMassingPoint point = ToMassingPoint(marker, origin, scaleMetersPerPt, useFeet);
            if (!TryProjectPointToFootprintWall(footprint, point, out int wallIndex, out SmartMassingPoint projected, out double wallLength))
                continue;

            string type = OpeningType(marker);
            OpeningDimensions(type, wallTopZ, wallLength, out double width, out double height, out double bottom);
            double centerZ = Math.Min(wallTopZ - height / 2, bottom + height / 2);
            centerZ = Math.Max(height / 2, centerZ);

            draft.Openings.Add(new SmartMassingOpening
            {
                Status = "draft",
                Type = type,
                SourceMarkerId = marker.Id,
                Page = marker.Page,
                WallIndex = wallIndex,
                Center = new SmartMassingVertex
                {
                    X = projected.X,
                    Y = projected.Y,
                    Z = Math.Round(centerZ, 3),
                    SourceMarkerId = marker.Id,
                },
                Width = Math.Round(width, 3),
                Height = Math.Round(height, 3),
                Confidence = 0.28,
                Notes = "Projected from reviewed opening sample marker to nearest footprint wall. Position and size need review.",
            });
        }

        if (draft.Openings.Count > 0)
            draft.Assumptions.Add("Opening samples are projected to nearest footprint walls as visual placeholders.");
    }

    private static bool TryProjectPointToFootprintWall(
        SmartMassingFootprint footprint,
        SmartMassingPoint point,
        out int wallIndex,
        out SmartMassingPoint projected,
        out double wallLength)
    {
        wallIndex = -1;
        projected = new SmartMassingPoint();
        wallLength = 0;
        double bestDistanceSq = double.MaxValue;

        for (int i = 0; i < footprint.Points.Count; i++)
        {
            SmartMassingPoint start = footprint.Points[i];
            SmartMassingPoint end = footprint.Points[(i + 1) % footprint.Points.Count];
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double lengthSq = dx * dx + dy * dy;
            if (lengthSq <= 0.0001)
                continue;

            double t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSq;
            t = Math.Max(0, Math.Min(1, t));
            double x = start.X + dx * t;
            double y = start.Y + dy * t;
            double distanceSq = ((point.X - x) * (point.X - x)) + ((point.Y - y) * (point.Y - y));
            if (distanceSq >= bestDistanceSq)
                continue;

            bestDistanceSq = distanceSq;
            wallIndex = i;
            wallLength = Math.Sqrt(lengthSq);
            projected = new SmartMassingPoint
            {
                X = Math.Round(x, 3),
                Y = Math.Round(y, 3),
                SourceMarkerId = point.SourceMarkerId,
            };
        }

        return wallIndex >= 0;
    }

    private static string OpeningType(SmartAiMarker marker)
    {
        if (MarkerTypeEquals(marker, "window_sample"))
            return "window";
        if (MarkerTypeEquals(marker, "door_sample"))
            return "door";
        return "opening";
    }

    private static void OpeningDimensions(
        string type,
        double wallTopZ,
        double wallLength,
        out double width,
        out double height,
        out double bottom)
    {
        double usableWall = Math.Max(1, wallTopZ);
        double usableLength = Math.Max(1, wallLength);
        if (type == "door")
        {
            height = Math.Min(usableWall * 0.82, usableWall - usableWall * 0.04);
            width = Math.Min(usableLength * 0.22, usableWall * 0.38);
            bottom = 0;
            return;
        }

        if (type == "window")
        {
            height = usableWall * 0.32;
            width = Math.Min(usableLength * 0.24, usableWall * 0.48);
            bottom = usableWall * 0.38;
            return;
        }

        height = usableWall * 0.42;
        width = Math.Min(usableLength * 0.25, usableWall * 0.52);
        bottom = usableWall * 0.24;
    }

    private static List<SmartMassingPlane> BuildRoofPlanes(SmartMassingDraft draft)
    {
        SmartMassingFootprint? footprint = draft.Footprints.FirstOrDefault(footprint => footprint.Points.Count >= 3);
        if (footprint == null)
            return [];

        IReadOnlyList<SmartMassingRoofGuide> guides = draft.Roof.Guides
            .Where(guide => !string.Equals(guide.Status, "rejected", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (guides.Count == 0)
            return [BuildRoofCapPlane(draft, footprint, "roof_cap_from_footprint", "Roof cap from footprint", 0.15, [], "No roof guides were available.")];

        double minX = footprint.Points.Min(point => point.X);
        double maxX = footprint.Points.Max(point => point.X);
        double minY = footprint.Points.Min(point => point.Y);
        double maxY = footprint.Points.Max(point => point.Y);
        double width = Math.Max(0.001, maxX - minX);
        double depth = Math.Max(0.001, maxY - minY);
        double wallTopZ = DisplayWallHeight(draft, footprint);
        double roofRise = ResolveRoofRise(draft.Roof.Pitch, width, depth, wallTopZ);

        if (draft.Roof.Type is "flat" or "low_slope" ||
            guides.Any(guide => guide.Kind is "cap" or "eave_outline") &&
            !guides.Any(guide => guide.Kind is "ridge" or "hip_ridge" or "axis_candidate" or "slope_arrow"))
        {
            return [BuildRoofCapPlane(draft, footprint, "roof_cap_low_slope", "Reviewed low-slope roof cap", 0.35, RoofGuideSourceIds(guides), "Generated as a single roof cap plane from reviewed guides.")];
        }

        SmartMassingRoofGuide? slope = guides.FirstOrDefault(guide =>
            guide.Kind == "slope_arrow" && guide.Points.Count >= 2);
        if (slope != null || draft.Roof.Type == "shed")
        {
            return BuildShedRoofPlanes(draft, footprint, slope, wallTopZ, roofRise);
        }

        SmartMassingRoofGuide? ridge = guides.FirstOrDefault(guide =>
            guide.Points.Count >= 2 &&
            guide.Kind is "ridge" or "hip_ridge" or "axis_candidate");
        if (draft.Roof.Type == "hip" || ridge?.Kind == "hip_ridge")
        {
            List<SmartMassingPlane> hipPlanes = BuildHipRoofPlanes(draft, footprint, ridge, wallTopZ, roofRise);
            if (hipPlanes.Count > 0)
                return hipPlanes;
        }

        if (ridge != null)
        {
            List<SmartMassingPlane> ridgePlanes = BuildRidgeRoofPlanes(draft, footprint, ridge, wallTopZ, roofRise);
            if (ridgePlanes.Count > 0)
                return ridgePlanes;
        }

        return [BuildRoofCapPlane(draft, footprint, "roof_cap_fallback", "Fallback roof cap", 0.2, RoofGuideSourceIds(guides), "Could not derive roof planes from guides; shown as a cap until roof guides are reviewed.")];
    }

    private static List<SmartMassingPlane> BuildRidgeRoofPlanes(
        SmartMassingDraft draft,
        SmartMassingFootprint footprint,
        SmartMassingRoofGuide ridge,
        double wallTopZ,
        double roofRise)
    {
        SmartMassingPoint ridgeStart = ridge.Points[0];
        SmartMassingPoint ridgeEnd = ridge.Points[^1];
        double ridgeZ = wallTopZ + roofRise;
        var left = new List<SmartMassingPoint>();
        var right = new List<SmartMassingPoint>();

        foreach (SmartMassingPoint point in footprint.Points)
        {
            double side = Cross(ridgeStart, ridgeEnd, point);
            if (side >= 0)
                left.Add(point);
            else
                right.Add(point);
        }

        if (left.Count == 0 || right.Count == 0)
            return [];

        var planes = new List<SmartMassingPlane>
        {
            new()
            {
                Id = "roof_plane_left",
                Status = draft.Roof.Status == "reviewed" ? "reviewed" : "draft",
                Kind = ridge.Kind == "axis_candidate" ? "candidate_roof_plane" : "roof_plane",
                Label = ridge.Kind == "axis_candidate" ? "Candidate roof plane A" : "Roof plane A from ridge",
                Points =
                [
                    ToVertex(ridgeStart, ridgeZ),
                    ToVertex(ridgeEnd, ridgeZ),
                    ..left.Select(point => ToVertex(point, wallTopZ)).Reverse(),
                ],
                Confidence = Math.Min(0.55, Math.Max(0.15, ridge.Confidence)),
                SourceMarkerIds = ridge.SourceMarkerIds.ToList(),
                Notes = "Generated from the reviewed ridge/axis guide and footprint eave points.",
            },
            new()
            {
                Id = "roof_plane_right",
                Status = draft.Roof.Status == "reviewed" ? "reviewed" : "draft",
                Kind = ridge.Kind == "axis_candidate" ? "candidate_roof_plane" : "roof_plane",
                Label = ridge.Kind == "axis_candidate" ? "Candidate roof plane B" : "Roof plane B from ridge",
                Points =
                [
                    ToVertex(ridgeEnd, ridgeZ),
                    ToVertex(ridgeStart, ridgeZ),
                    ..right.Select(point => ToVertex(point, wallTopZ)).Reverse(),
                ],
                Confidence = Math.Min(0.55, Math.Max(0.15, ridge.Confidence)),
                SourceMarkerIds = ridge.SourceMarkerIds.ToList(),
                Notes = "Generated from the reviewed ridge/axis guide and footprint eave points.",
            },
        };

        return planes
            .Where(plane => plane.Points.Count >= 3)
            .ToList();
    }

    private static List<SmartMassingPlane> BuildHipRoofPlanes(
        SmartMassingDraft draft,
        SmartMassingFootprint footprint,
        SmartMassingRoofGuide? ridge,
        double wallTopZ,
        double roofRise)
    {
        double minX = footprint.Points.Min(point => point.X);
        double maxX = footprint.Points.Max(point => point.X);
        double minY = footprint.Points.Min(point => point.Y);
        double maxY = footprint.Points.Max(point => point.Y);
        double width = Math.Max(0.001, maxX - minX);
        double depth = Math.Max(0.001, maxY - minY);
        double centerX = (minX + maxX) / 2.0;
        double centerY = (minY + maxY) / 2.0;
        double ridgeZ = wallTopZ + roofRise;
        string sourceMarkerId = ridge?.SourceMarkerIds.FirstOrDefault() ?? "";
        List<string> sourceIds = ridge?.SourceMarkerIds.ToList() ?? draft.Roof.SourceMarkerIds.ToList();

        bool horizontal = ridge?.Points.Count >= 2
            ? Math.Abs(ridge.Points[^1].X - ridge.Points[0].X) >= Math.Abs(ridge.Points[^1].Y - ridge.Points[0].Y)
            : width >= depth;

        SmartMassingPoint ridgeStart;
        SmartMassingPoint ridgeEnd;
        if (ridge?.Points.Count >= 2)
        {
            ridgeStart = ridge.Points[0];
            ridgeEnd = ridge.Points[^1];
        }
        else if (horizontal)
        {
            ridgeStart = RoofPoint(minX + width * 0.25, centerY, sourceMarkerId);
            ridgeEnd = RoofPoint(maxX - width * 0.25, centerY, sourceMarkerId);
        }
        else
        {
            ridgeStart = RoofPoint(centerX, minY + depth * 0.25, sourceMarkerId);
            ridgeEnd = RoofPoint(centerX, maxY - depth * 0.25, sourceMarkerId);
        }

        string status = draft.Roof.Status == "reviewed" ? "reviewed" : "draft";
        double confidence = Math.Min(0.52, Math.Max(0.18, ridge?.Confidence ?? draft.Roof.Confidence));
        string notes = "Generated as first-pass hip roof planes from ridge/hip guide and footprint bounds. Review before using as accepted geometry.";

        if (horizontal)
        {
            return
            [
                HipPlane("roof_plane_hip_side_a", "Hip roof side plane A", status, confidence, sourceIds, notes,
                    ToVertex(ridgeStart, ridgeZ), ToVertex(ridgeEnd, ridgeZ), ToVertex(RoofPoint(maxX, maxY, ""), wallTopZ), ToVertex(RoofPoint(minX, maxY, ""), wallTopZ)),
                HipPlane("roof_plane_hip_side_b", "Hip roof side plane B", status, confidence, sourceIds, notes,
                    ToVertex(ridgeEnd, ridgeZ), ToVertex(ridgeStart, ridgeZ), ToVertex(RoofPoint(minX, minY, ""), wallTopZ), ToVertex(RoofPoint(maxX, minY, ""), wallTopZ)),
                HipPlane("roof_plane_hip_end_a", "Hip roof end plane A", status, confidence, sourceIds, notes,
                    ToVertex(ridgeStart, ridgeZ), ToVertex(RoofPoint(minX, minY, ""), wallTopZ), ToVertex(RoofPoint(minX, maxY, ""), wallTopZ)),
                HipPlane("roof_plane_hip_end_b", "Hip roof end plane B", status, confidence, sourceIds, notes,
                    ToVertex(ridgeEnd, ridgeZ), ToVertex(RoofPoint(maxX, maxY, ""), wallTopZ), ToVertex(RoofPoint(maxX, minY, ""), wallTopZ)),
            ];
        }

        return
        [
            HipPlane("roof_plane_hip_side_a", "Hip roof side plane A", status, confidence, sourceIds, notes,
                ToVertex(ridgeStart, ridgeZ), ToVertex(ridgeEnd, ridgeZ), ToVertex(RoofPoint(maxX, maxY, ""), wallTopZ), ToVertex(RoofPoint(maxX, minY, ""), wallTopZ)),
            HipPlane("roof_plane_hip_side_b", "Hip roof side plane B", status, confidence, sourceIds, notes,
                ToVertex(ridgeEnd, ridgeZ), ToVertex(ridgeStart, ridgeZ), ToVertex(RoofPoint(minX, minY, ""), wallTopZ), ToVertex(RoofPoint(minX, maxY, ""), wallTopZ)),
            HipPlane("roof_plane_hip_end_a", "Hip roof end plane A", status, confidence, sourceIds, notes,
                ToVertex(ridgeStart, ridgeZ), ToVertex(RoofPoint(maxX, minY, ""), wallTopZ), ToVertex(RoofPoint(minX, minY, ""), wallTopZ)),
            HipPlane("roof_plane_hip_end_b", "Hip roof end plane B", status, confidence, sourceIds, notes,
                ToVertex(ridgeEnd, ridgeZ), ToVertex(RoofPoint(minX, maxY, ""), wallTopZ), ToVertex(RoofPoint(maxX, maxY, ""), wallTopZ)),
        ];
    }

    private static SmartMassingPlane HipPlane(
        string id,
        string label,
        string status,
        double confidence,
        IReadOnlyList<string> sourceMarkerIds,
        string notes,
        params SmartMassingVertex[] points) =>
        new()
        {
            Id = id,
            Status = status,
            Kind = "hip_roof_plane",
            Label = label,
            Points = points.ToList(),
            Confidence = confidence,
            SourceMarkerIds = sourceMarkerIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Notes = notes,
        };

    private static List<SmartMassingPlane> BuildShedRoofPlanes(
        SmartMassingDraft draft,
        SmartMassingFootprint footprint,
        SmartMassingRoofGuide? slope,
        double wallTopZ,
        double roofRise)
    {
        SmartMassingPoint high;
        SmartMassingPoint low;
        if (slope?.Points.Count >= 2)
        {
            high = slope.Points[0];
            low = slope.Points[^1];
        }
        else
        {
            double minY = footprint.Points.Min(point => point.Y);
            double maxY = footprint.Points.Max(point => point.Y);
            double midX = footprint.Points.Average(point => point.X);
            high = RoofPoint(midX, minY, "");
            low = RoofPoint(midX, maxY, "");
        }

        double dx = low.X - high.X;
        double dy = low.Y - high.Y;
        double lengthSq = Math.Max(0.0001, dx * dx + dy * dy);
        var points = new List<SmartMassingVertex>();
        foreach (SmartMassingPoint point in footprint.Points)
        {
            double t = ((point.X - high.X) * dx + (point.Y - high.Y) * dy) / lengthSq;
            t = Math.Max(0, Math.Min(1, t));
            double z = wallTopZ + roofRise * (1 - t);
            points.Add(ToVertex(point, z));
        }

        return
        [
            new SmartMassingPlane
            {
                Id = "roof_plane_shed",
                Status = draft.Roof.Status == "reviewed" ? "reviewed" : "draft",
                Kind = "shed_roof_plane",
                Label = "Shed roof plane",
                Points = points,
                Confidence = Math.Min(0.6, Math.Max(0.2, slope?.Confidence ?? draft.Roof.Confidence)),
                SourceMarkerIds = slope?.SourceMarkerIds.ToList() ?? draft.Roof.SourceMarkerIds.ToList(),
                Notes = "Generated by interpolating roof height from high edge toward low edge.",
            },
        ];
    }

    private static SmartMassingPlane BuildRoofCapPlane(
        SmartMassingDraft draft,
        SmartMassingFootprint footprint,
        string id,
        string label,
        double confidence,
        IReadOnlyList<string> sourceMarkerIds,
        string notes)
    {
        double wallTopZ = DisplayWallHeight(draft, footprint);
        return new SmartMassingPlane
        {
            Id = id,
            Status = draft.Roof.Status == "reviewed" ? "reviewed" : "draft",
            Kind = "roof_cap",
            Label = label,
            Points = footprint.Points.Select(point => ToVertex(point, wallTopZ)).ToList(),
            Confidence = confidence,
            SourceMarkerIds = sourceMarkerIds.ToList(),
            Notes = notes,
        };
    }

    private static IReadOnlyList<string> RoofGuideSourceIds(IEnumerable<SmartMassingRoofGuide> guides) =>
        guides
            .SelectMany(guide => guide.SourceMarkerIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static double DisplayWallHeight(SmartMassingDraft draft, SmartMassingFootprint footprint)
    {
        double height = footprint.Height > 0 ? footprint.Height : DefaultWallHeightFeet;
        if (string.Equals(draft.Units, "feet", StringComparison.OrdinalIgnoreCase))
            return height;

        if (footprint.Points.Count < 2)
            return height;

        double width = footprint.Points.Max(point => point.X) - footprint.Points.Min(point => point.X);
        double depth = footprint.Points.Max(point => point.Y) - footprint.Points.Min(point => point.Y);
        return Math.Max(height, Math.Min(width, depth) * 0.12);
    }

    private static double ResolveRoofRise(string pitch, double width, double depth, double wallTopZ)
    {
        double run = Math.Max(0.001, Math.Min(width, depth) / 2.0);
        Match match = PitchRegex().Match(pitch ?? "");
        if (match.Success &&
            double.TryParse(match.Groups["rise"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double rise) &&
            double.TryParse(match.Groups["run"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double pitchRun) &&
            pitchRun > 0)
        {
            return Math.Max(run * rise / pitchRun, wallTopZ * 0.08);
        }

        return Math.Max(Math.Min(width, depth) * 0.16, wallTopZ * 0.25);
    }

    private static SmartMassingVertex ToVertex(SmartMassingPoint point, double z) =>
        new()
        {
            X = Math.Round(point.X, 3),
            Y = Math.Round(point.Y, 3),
            Z = Math.Round(z, 3),
            SourceMarkerId = point.SourceMarkerId,
        };

    private static double Cross(SmartMassingPoint lineStart, SmartMassingPoint lineEnd, SmartMassingPoint point) =>
        (lineEnd.X - lineStart.X) * (point.Y - lineStart.Y) -
        (lineEnd.Y - lineStart.Y) * (point.X - lineStart.X);

    private static bool TryResolveMassingTransform(
        SmartTakeoffsJob job,
        SmartMassingFootprint footprint,
        IReadOnlyList<SmartAiMarker> allMarkers,
        out SKPoint origin,
        out double scaleMetersPerPt,
        out bool useFeet)
    {
        origin = default;
        scaleMetersPerPt = 0;
        useFeet = false;

        string originMarkerId = footprint.Points
            .Select(point => point.SourceMarkerId)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)) ?? "";
        SmartAiMarker? originMarker = allMarkers.FirstOrDefault(marker =>
            string.Equals(marker.Id, originMarkerId, StringComparison.OrdinalIgnoreCase));
        if (originMarker == null)
            return false;

        origin = new SKPoint(originMarker.PdfPoint.X, originMarker.PdfPoint.Y);
        scaleMetersPerPt = ResolveMarkerScale(job, originMarker);
        useFeet = string.Equals(footprint.HeightUnits, "feet", StringComparison.OrdinalIgnoreCase) &&
                  scaleMetersPerPt > 0;
        return true;
    }

    private static List<SmartAiMarker> RoofMarkersByType(IReadOnlyList<SmartAiMarker> markers, string markerType) =>
        markers
            .Where(marker => MarkerTypeEquals(marker, markerType))
            .Where(marker => !string.Equals(marker.SampleKind, "ignore", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static SmartMassingPoint MarkerCenterPoint(
        IReadOnlyList<SmartAiMarker> markers,
        SKPoint origin,
        double scaleMetersPerPt,
        bool useFeet)
    {
        var points = markers.Select(marker => ToMassingPoint(marker, origin, scaleMetersPerPt, useFeet)).ToList();
        return RoofPoint(
            points.Average(point => point.X),
            points.Average(point => point.Y),
            string.Join(",", markers.Select(marker => marker.Id)));
    }

    private static SmartMassingPoint ToMassingPoint(
        SmartAiMarker marker,
        SKPoint origin,
        double scaleMetersPerPt,
        bool useFeet)
    {
        double x = marker.PdfPoint.X - origin.X;
        double y = marker.PdfPoint.Y - origin.Y;
        if (useFeet)
        {
            x = x * scaleMetersPerPt / MetersPerFoot;
            y = y * scaleMetersPerPt / MetersPerFoot;
        }

        return new SmartMassingPoint
        {
            X = Math.Round(x, 3),
            Y = Math.Round(y, 3),
            SourceMarkerId = marker.Id,
        };
    }

    private static bool TryResolveWallHeightFeet(
        List<SmartAiMarker> heightMarkers,
        out double heightFeet,
        out SmartAiMarker? source)
    {
        foreach (SmartAiMarker marker in heightMarkers)
        {
            if (TryParseHeightFeet(MarkerText(marker), out heightFeet))
            {
                source = marker;
                return true;
            }
        }

        heightFeet = 0;
        source = null;
        return false;
    }

    private static bool TryParseHeightFeet(string text, out double heightFeet)
    {
        heightFeet = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        Match feetInches = FeetInchesRegex().Match(text);
        if (feetInches.Success &&
            double.TryParse(feetInches.Groups["feet"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double feet))
        {
            heightFeet = feet;
            if (double.TryParse(feetInches.Groups["inches"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double inches))
                heightFeet += inches / 12.0;
            return heightFeet > 0;
        }

        Match meters = MetersRegex().Match(text);
        if (meters.Success &&
            double.TryParse(meters.Groups["meters"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double m))
        {
            heightFeet = m / MetersPerFoot;
            return heightFeet > 0;
        }

        Match number = PlainNumberRegex().Match(text);
        if (number.Success &&
            double.TryParse(number.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            heightFeet = value;
            return heightFeet > 0;
        }

        return false;
    }

    private static double ResolveMarkerScale(SmartTakeoffsJob job, SmartAiMarker marker)
    {
        if (string.IsNullOrWhiteSpace(marker.PageFolder))
            return 0;

        string folder = Path.IsPathFullyQualified(marker.PageFolder)
            ? marker.PageFolder
            : Path.GetFullPath(Path.Combine(job.RootPath, marker.PageFolder));
        PageInfo? page = SmartTakeoffsJobStore.TryReadPage(folder);
        return page?.ScaleMetersPerPt ?? 0;
    }

    private static string MarkerText(SmartAiMarker marker) =>
        $"{marker.Value} {marker.Note}".Trim();

    private static bool IsRoofMarker(SmartAiMarker marker) =>
        MarkerTypeEquals(marker, "roof_note") ||
        MarkerTypeEquals(marker, "roof_edge_sample") ||
        MarkerTypeEquals(marker, "ridge_sample") ||
        MarkerTypeEquals(marker, "valley_sample") ||
        MarkerTypeEquals(marker, "roof_high_edge") ||
        MarkerTypeEquals(marker, "roof_low_edge") ||
        MarkerTypeEquals(marker, "overhang_sample");

    private static bool IsOpeningMarker(SmartAiMarker marker) =>
        MarkerTypeEquals(marker, "window_sample") ||
        MarkerTypeEquals(marker, "door_sample") ||
        MarkerTypeEquals(marker, "opening_sample");

    private static bool IsExplicitRoofGuideMarker(SmartAiMarker marker) =>
        MarkerTypeEquals(marker, "roof_edge_sample") ||
        MarkerTypeEquals(marker, "ridge_sample") ||
        MarkerTypeEquals(marker, "valley_sample") ||
        MarkerTypeEquals(marker, "roof_high_edge") ||
        MarkerTypeEquals(marker, "roof_low_edge") ||
        MarkerTypeEquals(marker, "overhang_sample");

    private static bool MarkerTypeEquals(SmartAiMarker marker, string type) =>
        string.Equals(marker.Type, type, StringComparison.OrdinalIgnoreCase);

    private static string ExtractPitch(string text)
    {
        Match match = PitchRegex().Match(text ?? "");
        return match.Success ? match.Value : "";
    }

    private static string InferRoofType(string text)
    {
        text ??= "";
        if (ContainsAny(text, "gable", "gabled"))
            return "gable";
        if (ContainsAny(text, "hip", "hipped"))
            return "hip";
        if (ContainsAny(text, "shed", "single slope", "single-slope", "mono slope", "monoslope"))
            return "shed";
        if (ContainsAny(text, "flat", "low slope", "low-slope", "low sloped"))
            return "low_slope";
        return "unknown";
    }

    private static string RoofGuideLabel(string roofType, string pitch)
    {
        string suffix = string.IsNullOrWhiteSpace(pitch) ? "" : $" ({pitch})";
        return roofType == "unknown"
            ? $"Candidate roof axis from footprint long axis{suffix}"
            : $"Draft {roofType} ridge along long footprint axis{suffix}";
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string SafeFilePart(string value)
    {
        string clean = new(value
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "massing" : clean;
    }

    private static SmartMassingPoint CopyRoofPoint(SmartMassingPoint point, string sourceMarkerId) =>
        RoofPoint(point.X, point.Y, string.IsNullOrWhiteSpace(point.SourceMarkerId) ? sourceMarkerId : point.SourceMarkerId);

    private static SmartMassingPoint RoofPoint(double x, double y, string sourceMarkerId) =>
        new()
        {
            X = Math.Round(x, 3),
            Y = Math.Round(y, 3),
            SourceMarkerId = sourceMarkerId,
        };

    [GeneratedRegex(@"(?<feet>\d+(?:\.\d+)?)\s*(?:'|ft|feet)\s*(?:(?<inches>\d+(?:\.\d+)?)\s*(?:""|in|inch|inches))?", RegexOptions.IgnoreCase)]
    private static partial Regex FeetInchesRegex();

    [GeneratedRegex(@"(?<meters>\d+(?:\.\d+)?)\s*(?:m|meter|meters)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MetersRegex();

    [GeneratedRegex(@"(?<value>\d+(?:\.\d+)?)")]
    private static partial Regex PlainNumberRegex();

    [GeneratedRegex(@"\b(?<rise>\d+(?:\.\d+)?)\s*:\s*(?<run>12)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PitchRegex();
}
