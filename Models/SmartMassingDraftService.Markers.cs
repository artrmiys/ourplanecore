using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SkiaSharp;


namespace OurPlanCore;

public static partial class SmartMassingDraftService
{
    // AI marker footprint, roof guide, and explicit guide construction.

    private static SmartMassingFootprint? AddFootprint(
        OurPlanCoreJob job,
        SmartMassingDraft draft,
        List<SmartAiMarker> corners,
        List<SmartAiMarker> heightMarkers,
        int level,
        double inferredBaseElevationFeet)
    {
        List<SmartAiMarker> orderedCorners = OrderCorners(corners);
        SmartAiMarker originMarker = orderedCorners[0];
        SKPoint origin = new(originMarker.PdfPoint.X, originMarker.PdfPoint.Y);
        double scaleMetersPerPt = ResolveMarkerScale(job, originMarker);
        bool canUseFeet = scaleMetersPerPt > 0;

        double heightFeet = DefaultWallHeightFeet;
        if (TryResolveWallHeightFeet(heightMarkers, level, out double parsedHeightFeet, out SmartAiMarker? heightSource))
        {
            heightFeet = parsedHeightFeet;
            draft.Assumptions.Add($"Level {level} wall height uses marker {heightSource!.Id}: {heightFeet:F2} ft.");
        }
        else
        {
            draft.Assumptions.Add($"Level {level} wall height defaults to {DefaultWallHeightFeet:F0} ft until a wall_height_sample is reviewed.");
            draft.UnresolvedQuestions.Add($"Add or edit a wall_height_sample marker with a height value for level {level}.");
        }

        draft.Units = canUseFeet ? "feet" : "pdf_points";
        if (!canUseFeet)
            draft.UnresolvedQuestions.Add("Set scale on the source footprint page so 3D draft coordinates can be converted to feet.");

        double baseElevationFeet = inferredBaseElevationFeet;
        SmartAiMarker? baseSource = null;
        if (TryResolveBaseElevationFeet(corners.Concat(heightMarkers).ToList(), level, out double parsedBaseElevationFeet, out baseSource))
        {
            baseElevationFeet = parsedBaseElevationFeet;
            draft.Assumptions.Add($"Level {level} base elevation uses marker {baseSource!.Id}: {baseElevationFeet:F2} ft.");
        }
        else if (level > 1)
        {
            draft.Assumptions.Add($"Level {level} base elevation inferred from previous levels: {baseElevationFeet:F2} ft. Add z=... to override.");
        }

        draft.Assumptions.Add($"Level {level} exterior corners were auto-ordered around their centroid and need review.");

        var footprint = new SmartMassingFootprint
        {
            Id = level == 1 ? "footprint_level_1" : $"footprint_level_{level}",
            Level = level,
            Page = originMarker.Page,
            BaseElevation = Math.Round(baseElevationFeet, 3),
            BaseElevationUnits = "feet",
            Height = heightFeet,
            Confidence = canUseFeet ? 0.45 : 0.25,
            SourceMarkerIds = orderedCorners.Select(marker => marker.Id).ToList(),
            Points = orderedCorners
                .Select(marker => ToMassingPoint(marker, origin, scaleMetersPerPt, canUseFeet))
                .ToList(),
        };

        draft.Footprints.Add(footprint);
        return footprint;
    }

    private static void AddRoof(
        OurPlanCoreJob job,
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
        OurPlanCoreJob job,
        SmartMassingDraft draft,
        IReadOnlyList<SmartAiMarker> allMarkers,
        string roofType,
        string pitch,
        IReadOnlyList<SmartAiMarker> roofMarkers)
    {
        SmartMassingFootprint? footprint = RoofFootprint(draft);
        if (footprint == null)
            return [];

        List<string> sourceMarkerIds = roofMarkers
            .Select(marker => marker.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sourceMarkerIds.Count == 0)
        {
            sourceMarkerIds = footprint.SourceMarkerIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

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
        OurPlanCoreJob job,
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
}
