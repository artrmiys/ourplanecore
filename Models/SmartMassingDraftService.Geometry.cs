using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SkiaSharp;


namespace OurPlaneCore;

public static partial class SmartMassingDraftService
{
    // Display dimensions, transforms, marker center conversion, and geometry helpers.

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

    public static double DisplayBaseElevation(SmartMassingDraft draft, SmartMassingFootprint footprint)
    {
        double baseElevation = footprint.BaseElevation;
        if (Math.Abs(baseElevation) < 0.0001)
            return 0;

        if (string.Equals(draft.Units, "feet", StringComparison.OrdinalIgnoreCase))
            return baseElevation;

        if (footprint.Points.Count < 2)
            return baseElevation;

        double height = footprint.Height > 0 ? footprint.Height : DefaultWallHeightFeet;
        double displayHeight = DisplayWallHeight(draft, footprint);
        if (height <= 0.0001)
            return baseElevation;

        return baseElevation * (displayHeight / height);
    }

    public static double DisplayWallTopElevation(SmartMassingDraft draft, SmartMassingFootprint footprint) =>
        DisplayBaseElevation(draft, footprint) + DisplayWallHeight(draft, footprint);

    private static double DisplayRoofSeedElevation(SmartMassingDraft draft, SmartMassingFootprint footprint)
    {
        if (draft.Roof.Elevation > 0 &&
            string.Equals(draft.Roof.ElevationUnits, "feet", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(draft.Units, "feet", StringComparison.OrdinalIgnoreCase))
        {
            return draft.Roof.Elevation;
        }

        return DisplayWallTopElevation(draft, footprint);
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
        OurPlaneCoreJob job,
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

}
