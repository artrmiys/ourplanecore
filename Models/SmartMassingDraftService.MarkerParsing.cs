using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SkiaSharp;


namespace OurPlaneCore;

public static partial class SmartMassingDraftService
{
    // Marker height/level parsing, marker filters, safe names, source records, and regexes.

    private static bool TryResolveWallHeightFeet(
        List<SmartAiMarker> heightMarkers,
        int level,
        out double heightFeet,
        out SmartAiMarker? source)
    {
        foreach (SmartAiMarker marker in heightMarkers.Where(marker => MarkerAppliesToLevel(marker, level)))
        {
            if (TryParseMarkerHeightFeet(MarkerText(marker), out heightFeet))
            {
                source = marker;
                return true;
            }
        }

        heightFeet = 0;
        source = null;
        return false;
    }

    private static bool TryResolveBaseElevationFeet(
        List<SmartAiMarker> markers,
        int level,
        out double baseElevationFeet,
        out SmartAiMarker? source)
    {
        foreach (SmartAiMarker marker in markers.Where(marker => MarkerAppliesToLevel(marker, level)))
        {
            if (TryParseMarkerBaseElevationFeet(MarkerText(marker), out baseElevationFeet))
            {
                source = marker;
                return true;
            }
        }

        baseElevationFeet = 0;
        source = null;
        return false;
    }

    private static int MarkerLevel(SmartAiMarker marker)
    {
        Match match = LevelRegex().Match(MarkerText(marker));
        if (match.Success &&
            int.TryParse(match.Groups["level"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level))
        {
            return Math.Max(1, level);
        }

        return 1;
    }

    private static bool MarkerAppliesToLevel(SmartAiMarker marker, int level) =>
        !LevelRegex().IsMatch(MarkerText(marker)) || MarkerLevel(marker) == level;

    private static bool TryParseMarkerHeightFeet(string text, out double heightFeet)
    {
        Match keyed = HeightKeyRegex().Match(text ?? "");
        if (keyed.Success && TryParseHeightFeet(keyed.Groups["value"].Value ?? "", out heightFeet))
            return true;

        return TryParseHeightFeet(text ?? "", out heightFeet);
    }

    private static bool TryParseMarkerBaseElevationFeet(string text, out double baseElevationFeet)
    {
        Match keyed = BaseElevationKeyRegex().Match(text ?? "");
        if (keyed.Success && TryParseHeightFeet(keyed.Groups["value"].Value ?? "", out baseElevationFeet))
            return true;

        baseElevationFeet = 0;
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

    private static double ResolveMarkerScale(OurPlaneCoreJob job, SmartAiMarker marker)
    {
        if (string.IsNullOrWhiteSpace(marker.PageFolder))
            return 0;

        string folder = Path.IsPathFullyQualified(marker.PageFolder)
            ? marker.PageFolder
            : Path.GetFullPath(Path.Combine(job.RootPath, marker.PageFolder));
        PageInfo? page = OurPlaneCoreJobStore.TryReadPage(folder);
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

    private sealed record WallFloorSource(int Level, string FolderPath, string DisplayName, List<TakeoffItem> Items);
    private sealed record WallMeasurementPoint(double X, double Y, string SourceId);

    [GeneratedRegex(@"(?:^|[\s,;])(?:level|lvl|floor|story|storey|fl)\s*[:=]\s*(?<level>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LevelRegex();

    [GeneratedRegex(@"(?:^|[\s,;])(?:height|wall_height|wallheight|h)\s*[:=]\s*(?<value>[^,;\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex HeightKeyRegex();

    [GeneratedRegex(@"(?:^|[\s,;])(?:base_elevation|base_z|elevation|elev|base|z)\s*[:=]\s*(?<value>[^,;\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BaseElevationKeyRegex();

    [GeneratedRegex(@"(?<feet>\d+(?:\.\d+)?)\s*(?:'|ft|feet)\s*(?:(?<inches>\d+(?:\.\d+)?)\s*(?:""|in|inch|inches))?", RegexOptions.IgnoreCase)]
    private static partial Regex FeetInchesRegex();

    [GeneratedRegex(@"(?<meters>\d+(?:\.\d+)?)\s*(?:m|meter|meters)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MetersRegex();

    [GeneratedRegex(@"(?<value>\d+(?:\.\d+)?)")]
    private static partial Regex PlainNumberRegex();

    [GeneratedRegex(@"\b\d+(?:\.\d+)?\s*x\s*\d+(?:\.\d+)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex StudSizeRegex();

    [GeneratedRegex(@"(?<value>\d+(?:\.\d+)?)")]
    private static partial Regex DecimalNumberRegex();

    [GeneratedRegex(@"\b(?:level|lvl|floor|story|storey|fl)?\s*(?<level>\d+)\s*(?:st|nd|rd|th)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex WallLevelRegex();

    [GeneratedRegex(@"\b(?<rise>\d+(?:\.\d+)?)\s*:\s*(?<run>12)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PitchRegex();
}
