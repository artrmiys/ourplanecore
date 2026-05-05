using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkiaSharp;

namespace OurPlaneCore;

public static class JoistTakeoffCalculator
{
    public const string RoundingNone = "None";
    public const string RoundingNearestFoot = "NearestFoot";
    public const string RoundingNearestEvenFoot = "NearestEvenFoot";
    public const string RoundingNearestTwoFeet = "NearestTwoFeet";

    private const double MetersPerFoot = 0.3048;
    private const double MetersPerInch = 0.0254;
    private const double ProjectionEpsilon = 0.001;

    public static JoistLayoutResult Empty { get; } = new(false, [], 0, 0, 0);

    public static JoistLayoutResult Calculate(Measurement measurement, double fallbackScaleMetersPerPt)
    {
        if (!measurement.JoistEnabled || !measurement.JoistDirectionLocked)
            return Empty;

        double scale = measurement.ScaleMetersPerPt > 0
            ? measurement.ScaleMetersPerPt
            : fallbackScaleMetersPerPt;

        return Calculate(
            measurement.Points,
            scale,
            measurement.JoistSpacingInches,
            measurement.JoistDirectionDegrees,
            measurement.JoistLengthRounding);
    }

    public static JoistLayoutResult Calculate(
        IReadOnlyList<SKPoint> polygon,
        double scaleMetersPerPt,
        double spacingInches,
        double directionDegrees,
        string lengthRounding)
    {
        if (polygon.Count < 3 || scaleMetersPerPt <= 0 || spacingInches <= 0)
            return Empty;

        double spacingPt = spacingInches * MetersPerInch / scaleMetersPerPt;
        if (spacingPt <= ProjectionEpsilon)
            return Empty;

        double radians = directionDegrees * Math.PI / 180.0;
        double dirX = Math.Cos(radians);
        double dirY = Math.Sin(radians);
        double length = Math.Sqrt(dirX * dirX + dirY * dirY);
        if (length <= double.Epsilon)
            return Empty;

        dirX /= length;
        dirY /= length;
        double normalX = -dirY;
        double normalY = dirX;

        var projections = polygon.Select(p => Dot(p, normalX, normalY)).ToList();
        double min = projections.Min();
        double max = projections.Max();
        if (max - min <= ProjectionEpsilon)
            return Empty;

        var segments = new List<JoistSegment>();
        string normalizedRounding = NormalizeLengthRounding(lengthRounding);
        var offsets = JoistOffsets(min, max, spacingPt);
        foreach (double offset in offsets)
        {
            var intersections = LinePolygonIntersections(polygon, offset, dirX, dirY, normalX, normalY);
            if (intersections.Count < 2)
                continue;

            for (int i = 0; i + 1 < intersections.Count; i += 2)
            {
                LineIntersection a = intersections[i];
                LineIntersection b = intersections[i + 1];
                double lengthPt = b.T - a.T;
                if (lengthPt <= ProjectionEpsilon)
                    continue;

                double rawMeters = lengthPt * scaleMetersPerPt;
                double rawFeet = rawMeters / MetersPerFoot;
                double orderFeet = RoundLengthFeet(rawFeet, normalizedRounding);
                segments.Add(new JoistSegment(
                    a.Point,
                    b.Point,
                    rawMeters,
                    orderFeet * MetersPerFoot,
                    orderFeet));
            }
        }

        double rawTotal = segments.Sum(segment => segment.RawLengthMeters);
        double orderTotal = segments.Sum(segment => segment.OrderLengthMeters);
        double area = PolygonAreaPt(polygon) * scaleMetersPerPt * scaleMetersPerPt;
        double spacingSpanMeters = (max - min) * scaleMetersPerPt;
        double spacingMeters = spacingInches * MetersPerInch;
        return new JoistLayoutResult(
            true,
            segments,
            rawTotal,
            orderTotal,
            area,
            spacingSpanMeters,
            spacingMeters,
            offsets.Count,
            NormalizeDirectionDegrees(directionDegrees));
    }

    private static IReadOnlyList<double> JoistOffsets(double min, double max, double spacingPt)
    {
        var offsets = new List<double>();
        int maxLines = Math.Min(8000, (int)Math.Ceiling((max - min) / spacingPt) + 2);
        for (int lineIndex = 0; lineIndex < maxLines; lineIndex++)
        {
            double offset = min + lineIndex * spacingPt;
            if (offset >= max - ProjectionEpsilon)
                break;
            offsets.Add(offset);
        }

        if (offsets.Count == 0 || Math.Abs(offsets[^1] - max) > ProjectionEpsilon)
            offsets.Add(max);
        return offsets;
    }

    public static JoistLayoutSummary Summarize(
        IEnumerable<Measurement> measurements,
        double fallbackScaleMetersPerPt)
    {
        bool hasScale = false;
        int count = 0;
        double totalLength = 0;
        double rawLength = 0;
        double area = 0;

        foreach (Measurement measurement in measurements)
        {
            JoistLayoutResult layout = Calculate(measurement, fallbackScaleMetersPerPt);
            hasScale |= layout.HasScale;
            count += layout.Count;
            totalLength += layout.TotalLengthMeters;
            rawLength += layout.TotalRawLengthMeters;
            area += layout.AreaMetersSquared;
        }

        return new JoistLayoutSummary(hasScale, count, rawLength, totalLength, area);
    }

    public static string FormatSummary(JoistLayoutSummary summary, UnitMode unitMode)
    {
        if (!summary.HasScale)
            return "set direction";

        string length = Units.FormatLength(summary.TotalLengthMeters, unitMode);
        return $"{length} ({summary.Count} pcs)";
    }

    public static string FormatMeasurementLabel(JoistLayoutResult layout, UnitMode unitMode, string joistType)
    {
        if (!layout.HasScale)
            return "joists: set direction";

        string name = string.IsNullOrWhiteSpace(joistType) ? "Joists" : joistType.Trim();
        var lines = new List<string>
        {
            $"{name} {FormatLegendLength(layout.TotalLengthMeters, unitMode)} ({layout.Count} pcs)",
        };
        var allGroups = LengthGroups(layout, unitMode).ToList();
        // Show every distinct length group on the canvas label. Previously the
        // list was hard-capped at 4 entries via Take(5), so a layout with five
        // or more rounded lengths silently dropped the rest. We now cap at a
        // generous limit and surface overflow explicitly so the user knows
        // some groups are off-screen.
        const int maxGroupLines = 24;
        var groupLines = FormatLengthGroupLines(allGroups, unitMode).ToList();
        if (groupLines.Count <= maxGroupLines)
        {
            lines.AddRange(groupLines);
        }
        else
        {
            lines.AddRange(groupLines.Take(maxGroupLines));
            int hiddenGroups = allGroups.Count - maxGroupLines;
            int hiddenPieces = allGroups.Skip(maxGroupLines).Sum(group => group.Count);
            lines.Add($"(+{hiddenGroups} more / {hiddenPieces} pcs)");
        }
        return string.Join("\n", lines);
    }

    public static string FormatCountLengthLabel(JoistLayoutResult layout, UnitMode unitMode)
    {
        if (!layout.HasScale)
            return "joists: set direction/scale";
        if (layout.Count == 0)
            return "0 / -";

        bool sameLength = layout.Segments
            .Select(segment => segment.OrderLengthMeters)
            .DistinctBy(length => Math.Round(length, 3))
            .Take(2)
            .Count() <= 1;
        double labelLengthMeters = sameLength
            ? layout.Segments[0].OrderLengthMeters
            : layout.TotalLengthMeters / layout.Count;
        string lengthText = FormatCompactLength(labelLengthMeters, unitMode);
        return sameLength
            ? $"{layout.Count} / {lengthText}"
            : $"{layout.Count} / {lengthText} avg";
    }

    public static string FormatSegmentLength(JoistSegment segment, UnitMode unitMode) =>
        unitMode == UnitMode.Imperial
            ? $"{segment.OrderLengthFeet.ToString("0.##", CultureInfo.InvariantCulture)} ft"
            : $"{segment.OrderLengthMeters.ToString("0.###", CultureInfo.InvariantCulture)} m";

    public static string FormatDiagnostics(JoistLayoutResult layout, UnitMode unitMode)
    {
        if (!layout.HasScale)
            return "joists: set direction/scale";

        string span = FormatDiagnosticLength(layout.SpacingSpanMeters, unitMode);
        string spacing = unitMode == UnitMode.Imperial
            ? $"{(layout.SpacingMeters / MetersPerInch).ToString("0.##", CultureInfo.InvariantCulture)} in"
            : $"{layout.SpacingMeters.ToString("0.###", CultureInfo.InvariantCulture)} m";
        return $"joists {layout.Count} pcs, spacing span {span}, spacing {spacing} o.c., candidate lines {layout.CandidateLineCount}";
    }

    public static string FormatCompactLength(double meters, UnitMode unitMode)
    {
        if (unitMode != UnitMode.Imperial)
            return $"{meters.ToString("0.###", CultureInfo.InvariantCulture)} m";

        double feet = meters / MetersPerFoot;
        string value = Math.Abs(feet - Math.Round(feet)) < 0.01
            ? Math.Round(feet).ToString("0", CultureInfo.InvariantCulture)
            : feet.ToString("0.##", CultureInfo.InvariantCulture);
        return $"{value}'";
    }

    public static IReadOnlyList<string> FormatLegendLines(
        string itemName,
        IEnumerable<Measurement> measurements,
        double fallbackScaleMetersPerPt,
        UnitMode unitMode)
    {
        var measurementList = measurements.ToList();
        JoistLayoutSummary summary = Summarize(measurementList, fallbackScaleMetersPerPt);
        if (!summary.HasScale)
            return ["joists: set direction/scale"];

        string name = string.IsNullOrWhiteSpace(itemName) ? "Joists" : itemName.Trim();
        var lines = new List<string>
        {
            FormatLegendAreaValue(summary.AreaMetersSquared, unitMode),
            FormatLegendAreaUnit(unitMode),
            $"{name} {FormatLegendLength(summary.TotalLengthMeters, unitMode)}",
        };
        lines.AddRange(FormatLengthGroupLines(LengthGroups(measurementList, fallbackScaleMetersPerPt, unitMode), unitMode));
        return lines;
    }

    public static IReadOnlyList<JoistLengthGroup> LengthGroups(
        IEnumerable<Measurement> measurements,
        double fallbackScaleMetersPerPt,
        UnitMode unitMode)
    {
        var groups = new Dictionary<double, int>();
        foreach (Measurement measurement in measurements)
        {
            JoistLayoutResult layout = Calculate(measurement, fallbackScaleMetersPerPt);
            foreach (JoistLengthGroup group in LengthGroups(layout, unitMode))
                groups[group.Length] = groups.TryGetValue(group.Length, out int count)
                    ? count + group.Count
                    : group.Count;
        }

        return groups
            .OrderByDescending(pair => pair.Key)
            .Select(pair => new JoistLengthGroup(pair.Value, pair.Key))
            .ToList();
    }

    public static IReadOnlyList<JoistLengthGroup> LengthGroups(JoistLayoutResult layout, UnitMode unitMode)
    {
        if (!layout.HasScale)
            return [];

        return layout.Segments
            .GroupBy(segment => unitMode == UnitMode.Imperial
                ? Math.Round(segment.OrderLengthFeet, 2)
                : Math.Round(segment.OrderLengthMeters, 2))
            .OrderByDescending(group => group.Key)
            .Select(group => new JoistLengthGroup(group.Count(), group.Key))
            .ToList();
    }

    public static IEnumerable<string> FormatLengthGroupLines(
        IEnumerable<JoistLengthGroup> groups,
        UnitMode unitMode)
    {
        foreach (JoistLengthGroup group in groups)
            yield return $"({group.Count} / {FormatLegendNumber(group.Length, 2, fixedDecimals: true)})";
    }

    public static string FormatLegendLength(double meters, UnitMode unitMode)
    {
        double value = unitMode == UnitMode.Imperial ? meters / MetersPerFoot : meters;
        string unit = unitMode == UnitMode.Imperial ? "FT" : "M";
        return $"{FormatLegendNumber(value, 2, fixedDecimals: false)} {unit}";
    }

    public static string FormatLegendAreaValue(double squareMeters, UnitMode unitMode)
    {
        double value = unitMode == UnitMode.Imperial ? squareMeters / 0.0929030 : squareMeters;
        return FormatLegendNumber(value, 2, fixedDecimals: false);
    }

    public static string FormatLegendAreaUnit(UnitMode unitMode) =>
        unitMode == UnitMode.Imperial ? "SQ FT" : "SQ M";

    private static string FormatLegendNumber(double value, int decimals, bool fixedDecimals)
    {
        string format = fixedDecimals ? "0." + new string('0', decimals) : "0." + new string('#', decimals);
        string text = value.ToString(format, CultureInfo.InvariantCulture);
        if (!fixedDecimals && text.EndsWith(".", StringComparison.Ordinal))
            text = text[..^1];
        return text;
    }

    public static string NormalizeLengthRounding(string? value)
    {
        string normalized = (value ?? "").Trim().Replace(" ", "", StringComparison.OrdinalIgnoreCase);
        return normalized.ToLowerInvariant() switch
        {
            "nearestfoot" or "foot" => RoundingNearestFoot,
            "nearestevenfoot" or "evenfoot" or "even" => RoundingNearestEvenFoot,
            "nearest2feet" or "nearesttwofeet" or "twofeet" or "2feet" => RoundingNearestTwoFeet,
            _ => RoundingNone,
        };
    }

    public static string LengthRoundingTitle(string value) =>
        NormalizeLengthRounding(value) switch
        {
            RoundingNearestFoot => "Nearest Foot",
            RoundingNearestEvenFoot => "Nearest Even Foot",
            RoundingNearestTwoFeet => "Nearest 2 Feet",
            _ => "None",
        };

    private static double RoundLengthFeet(double feet, string lengthRounding)
    {
        if (feet <= 0)
            return 0;

        const double epsilon = 1e-9;
        return lengthRounding switch
        {
            RoundingNearestFoot => Math.Ceiling(feet - epsilon),
            RoundingNearestEvenFoot => RoundUpToEvenFoot(feet),
            RoundingNearestTwoFeet => RoundUpToNextEvenFoot(feet),
            _ => feet,
        };
    }

    private static double RoundUpToEvenFoot(double feet)
    {
        double rounded = Math.Ceiling(feet - 1e-9);
        return ((long)rounded) % 2 == 0 ? rounded : rounded + 1;
    }

    private static double RoundUpToNextEvenFoot(double feet)
    {
        // "Nearest 2 Feet" — round UP to the nearest multiple of 2 feet that is
        // greater than or equal to the raw length. Previously this added 1
        // unconditionally before the parity check, so 8.0 ft became 10.0 ft
        // even when 8 ft already satisfied the rule.
        double rounded = Math.Ceiling(feet - 1e-9);
        return ((long)rounded) % 2 == 0 ? rounded : rounded + 1;
    }

    private static List<LineIntersection> LinePolygonIntersections(
        IReadOnlyList<SKPoint> polygon,
        double offset,
        double dirX,
        double dirY,
        double normalX,
        double normalY)
    {
        var intersections = new List<LineIntersection>();
        int count = polygon.Count;

        for (int i = 0; i < count; i++)
        {
            SKPoint p0 = polygon[i];
            SKPoint p1 = polygon[(i + 1) % count];
            double s0 = Dot(p0, normalX, normalY) - offset;
            double s1 = Dot(p1, normalX, normalY) - offset;

            if (Math.Abs(s0) <= ProjectionEpsilon && Math.Abs(s1) <= ProjectionEpsilon)
            {
                AddIntersection(intersections, p0, dirX, dirY);
                AddIntersection(intersections, p1, dirX, dirY);
                continue;
            }

            if (Math.Abs(s0) <= ProjectionEpsilon)
            {
                AddIntersection(intersections, p0, dirX, dirY);
                continue;
            }

            if (s0 * s1 < 0)
            {
                double t = s0 / (s0 - s1);
                var point = new SKPoint(
                    (float)(p0.X + (p1.X - p0.X) * t),
                    (float)(p0.Y + (p1.Y - p0.Y) * t));
                AddIntersection(intersections, point, dirX, dirY);
            }
        }

        return intersections
            .OrderBy(intersection => intersection.T)
            .Aggregate(new List<LineIntersection>(), (unique, next) =>
            {
                if (unique.Count == 0 || Math.Abs(unique[^1].T - next.T) > ProjectionEpsilon)
                    unique.Add(next);
                return unique;
            });
    }

    private static void AddIntersection(List<LineIntersection> intersections, SKPoint point, double dirX, double dirY)
    {
        double t = Dot(point, dirX, dirY);
        intersections.Add(new LineIntersection(t, point));
    }

    private static double Dot(SKPoint point, double x, double y) =>
        point.X * x + point.Y * y;

    private static double NormalizeDirectionDegrees(double degrees)
    {
        double normalized = degrees % 180.0;
        if (normalized < 0)
            normalized += 180.0;
        return Math.Abs(normalized - 180.0) < 0.0001 ? 0 : normalized;
    }

    private static string FormatDiagnosticLength(double meters, UnitMode unitMode)
    {
        if (unitMode != UnitMode.Imperial)
            return $"{meters.ToString("0.###", CultureInfo.InvariantCulture)} m";

        double feet = meters / MetersPerFoot;
        return $"{feet.ToString("0.##", CultureInfo.InvariantCulture)} ft";
    }

    private static double PolygonAreaPt(IReadOnlyList<SKPoint> points)
    {
        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            SKPoint a = points[i];
            SKPoint b = points[(i + 1) % points.Count];
            area += a.X * b.Y - b.X * a.Y;
        }

        return Math.Abs(area) / 2.0;
    }

    private sealed record LineIntersection(double T, SKPoint Point);
}

public sealed record JoistLayoutResult(
    bool HasScale,
    IReadOnlyList<JoistSegment> Segments,
    double TotalRawLengthMeters,
    double TotalLengthMeters,
    double AreaMetersSquared,
    double SpacingSpanMeters = 0,
    double SpacingMeters = 0,
    int CandidateLineCount = 0,
    double DirectionDegrees = 0)
{
    public int Count => Segments.Count;
}

public sealed record JoistLayoutSummary(
    bool HasScale,
    int Count,
    double TotalRawLengthMeters,
    double TotalLengthMeters,
    double AreaMetersSquared);

public sealed record JoistSegment(
    SKPoint Start,
    SKPoint End,
    double RawLengthMeters,
    double OrderLengthMeters,
    double OrderLengthFeet);

public sealed record JoistLengthGroup(int Count, double Length);
