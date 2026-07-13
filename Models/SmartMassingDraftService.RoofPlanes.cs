using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SkiaSharp;


namespace OurPlanCore;

public static partial class SmartMassingDraftService
{
    // Roof footprint selection and roof plane generation.

    private static SmartMassingFootprint? RoofFootprint(SmartMassingDraft draft) =>
        draft.Footprints
            .Where(footprint => footprint.Points.Count >= 3)
            .OrderByDescending(footprint => DisplayBaseElevation(draft, footprint))
            .ThenByDescending(footprint => footprint.Level)
            .FirstOrDefault();

    private static SmartMassingFootprint? FootprintForMarkerLevel(SmartMassingDraft draft, SmartAiMarker marker)
    {
        int level = MarkerLevel(marker);
        SmartMassingFootprint? exact = draft.Footprints
            .Where(footprint => footprint.Points.Count >= 3)
            .FirstOrDefault(footprint => footprint.Level == level);
        return exact ?? RoofFootprint(draft);
    }

    private static List<SmartMassingPlane> BuildRoofPlanes(SmartMassingDraft draft)
    {
        SmartMassingFootprint? footprint = RoofFootprint(draft);
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
        double wallHeight = DisplayWallHeight(draft, footprint);
        double wallTopZ = DisplayRoofSeedElevation(draft, footprint);
        double roofRise = ResolveRoofRise(draft.Roof.Pitch, width, depth, wallHeight);

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
        double wallTopZ = DisplayRoofSeedElevation(draft, footprint);
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
}
