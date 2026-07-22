using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore;

public static partial class JoistTakeoffCalculator
{
    public const string RoundingNone = "None";
    public const string RoundingNearestFoot = "NearestFoot";
    public const string RoundingNearestEvenFoot = "NearestEvenFoot";
    public const string RoundingNearestTwoFeet = "NearestTwoFeet";
    private const double DefaultPitchRun = 12.0;

    private const double MetersPerFoot = 0.3048;
    private const double MetersPerInch = 0.0254;
    private const double ProjectionEpsilon = 0.001;
    private const double LengthLabelEpsilon = 0.005;

    public static JoistLayoutResult Empty { get; } = new(false, [], 0, 0, 0);

    public static JoistLayoutResult Calculate(Measurement measurement, double fallbackScaleMetersPerPt)
    {
        if (!measurement.JoistEnabled || !measurement.JoistDirectionLocked)
            return Empty;

        double scale = measurement.ScaleMetersPerPt > 0
            ? measurement.ScaleMetersPerPt
            : fallbackScaleMetersPerPt;

        JoistLayoutResult layout = Calculate(
            measurement.Points,
            measurement.Holes,
            scale,
            measurement.JoistSpacingInches,
            measurement.JoistDirectionDegrees,
            measurement.JoistLengthRounding,
            measurement.JoistPitch,
            measurement.JoistAddEndJoist);
        return IncludeExtraJoists(
            layout,
            measurement.ExtraJoists,
            scale,
            measurement.JoistLengthRounding);
    }

    public static JoistLayoutResult Calculate(
        IReadOnlyList<SKPoint> polygon,
        double scaleMetersPerPt,
        double spacingInches,
        double directionDegrees,
        string lengthRounding,
        string pitch = "",
        bool addEndJoist = true)
    {
        return Calculate(
            polygon,
            [],
            scaleMetersPerPt,
            spacingInches,
            directionDegrees,
            lengthRounding,
            pitch,
            addEndJoist);
    }

    public static JoistLayoutResult Calculate(
        IReadOnlyList<SKPoint> polygon,
        IReadOnlyList<IReadOnlyList<SKPoint>> holes,
        double scaleMetersPerPt,
        double spacingInches,
        double directionDegrees,
        string lengthRounding,
        string pitch = "",
        bool addEndJoist = true)
    {
        if (polygon.Count < 3 || scaleMetersPerPt <= 0 || spacingInches <= 0)
            return Empty;

        if (!TryParsePitchFactor(pitch, out double pitchFactor))
            pitchFactor = 1;
        string normalizedPitch = NormalizePitch(pitch);

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
        var offsets = JoistOffsets(min, max, spacingPt, addEndJoist);
        var contours = AreaContours(polygon, holes);
        foreach (double offset in offsets)
        {
            var intersections = LineAreaIntersections(contours, offset, dirX, dirY, normalX, normalY);
            if (intersections.Count < 2)
                continue;

            for (int i = 0; i + 1 < intersections.Count; i += 2)
            {
                LineIntersection a = intersections[i];
                LineIntersection b = intersections[i + 1];
                double lengthPt = b.T - a.T;
                if (lengthPt <= ProjectionEpsilon)
                    continue;

                double flatMeters = lengthPt * scaleMetersPerPt;
                double rawMeters = flatMeters * pitchFactor;
                double rawFeet = rawMeters / MetersPerFoot;
                double orderFeet = RoundLengthFeet(rawFeet, normalizedRounding);
                segments.Add(new JoistSegment(
                    a.Point,
                    b.Point,
                    flatMeters,
                    rawMeters,
                    orderFeet * MetersPerFoot,
                    orderFeet));
            }
        }

        double rawTotal = segments.Sum(segment => segment.RawLengthMeters);
        double orderTotal = segments.Sum(segment => segment.OrderLengthMeters);
        double area = AreaPt(polygon, holes) * scaleMetersPerPt * scaleMetersPerPt;
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
            NormalizeDirectionDegrees(directionDegrees),
            normalizedPitch,
            pitchFactor);
    }

    private static IReadOnlyList<double> JoistOffsets(double min, double max, double spacingPt, bool addEndJoist)
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

        if (addEndJoist && (offsets.Count == 0 || Math.Abs(offsets[^1] - max) > ProjectionEpsilon))
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

    public static string FormatMeasurementLabel(
        JoistLayoutResult layout,
        UnitMode unitMode,
        string joistType,
        bool detailedLabels = true)
    {
        if (!layout.HasScale)
            return "joists: set direction";

        string name = string.IsNullOrWhiteSpace(joistType) ? "Joists" : joistType.Trim();
        var lines = new List<string>
        {
            $"{name} {FormatLegendLength(layout.TotalLengthMeters, unitMode)} ({layout.Count} pcs)",
        };
        if (!string.IsNullOrWhiteSpace(layout.Pitch))
            lines.Add($"Pitch {layout.Pitch}");

        var allGroups = RegularLengthGroups(layout, unitMode).ToList();
        // Show every distinct length group on the canvas label. Previously the
        // list was hard-capped at 4 entries via Take(5), so a layout with five
        // or more rounded lengths silently dropped the rest. We now cap at a
        // generous limit and surface overflow explicitly so the user knows
        // some groups are off-screen.
        const int maxGroupLines = 24;
        var groupLines = FormatLengthGroupLines(allGroups, unitMode, detailedLabels).ToList();
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
        AppendExtraLengthGroupLines(lines, ExtraLengthGroups(layout, unitMode), unitMode, detailedLabels);
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
        string pitch = string.IsNullOrWhiteSpace(layout.Pitch)
            ? ""
            : $", pitch {layout.Pitch}, slope factor {layout.PitchFactor.ToString("0.###", CultureInfo.InvariantCulture)}";
        return $"joists {layout.Count} pcs, spacing span {span}, spacing {spacing} o.c., candidate lines {layout.CandidateLineCount}{pitch}";
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
        UnitMode unitMode,
        bool detailedLabels = true)
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
        lines.AddRange(FormatLengthGroupLines(RegularLengthGroups(measurementList, fallbackScaleMetersPerPt, unitMode), unitMode, detailedLabels));
        AppendExtraLengthGroupLines(lines, ExtraLengthGroups(measurementList, fallbackScaleMetersPerPt, unitMode), unitMode, detailedLabels);
        return lines;
    }

    public static IReadOnlyList<JoistLengthGroup> LengthGroups(
        IEnumerable<Measurement> measurements,
        double fallbackScaleMetersPerPt,
        UnitMode unitMode)
    {
        var groups = new Dictionary<double, JoistLengthAccumulator>();
        foreach (Measurement measurement in measurements)
        {
            JoistLayoutResult layout = Calculate(measurement, fallbackScaleMetersPerPt);
            foreach (JoistLengthGroup group in LengthGroups(layout, unitMode))
            {
                if (!groups.TryGetValue(group.Length, out JoistLengthAccumulator? accumulator))
                {
                    accumulator = new JoistLengthAccumulator(group.Length);
                    groups[group.Length] = accumulator;
                }

                accumulator.Add(group);
            }
        }

        return groups
            .Values
            .Select(group => group.ToGroup())
            .OrderByDescending(group => group.Length)
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
            .Select(group => CreateLengthGroup(group.Key, group.ToList(), unitMode, layout.PitchFactor))
            .ToList();
    }

    public static IEnumerable<string> FormatLengthGroupLines(
        IEnumerable<JoistLengthGroup> groups,
        UnitMode unitMode,
        bool detailedLabels = true)
    {
        foreach (JoistLengthGroup group in groups)
            yield return detailedLabels
                ? FormatLengthGroupLine(group, unitMode)
                : FormatStandardLengthGroupLine(group);
    }

    private static string FormatStandardLengthGroupLine(JoistLengthGroup group) =>
        $"({group.Count} / {FormatLegendNumber(group.Length, 2, fixedDecimals: true)})";

    private static JoistLengthGroup CreateLengthGroup(
        double length,
        IReadOnlyList<JoistSegment> segments,
        UnitMode unitMode,
        double pitchFactor)
    {
        var flatLengths = segments.Select(segment => LengthValue(segment.FlatLengthMeters, unitMode)).ToList();
        var rawLengths = segments.Select(segment => LengthValue(segment.RawLengthMeters, unitMode)).ToList();
        return new JoistLengthGroup(
            segments.Count,
            length,
            rawLengths.Min(),
            rawLengths.Max(),
            flatLengths.Min(),
            flatLengths.Max(),
            Math.Abs(pitchFactor - 1.0) > 0.0001);
    }

    private static string FormatLengthGroupLine(JoistLengthGroup group, UnitMode unitMode)
    {
        string count = group.Count == 1 ? "1 pc" : $"{group.Count} pcs";
        string unit = unitMode == UnitMode.Imperial ? "FT" : "M";
        var details = new List<string>();

        bool showFlat = group.HasPitch && !SameLengthRange(
            group.FlatMinLength,
            group.FlatMaxLength,
            group.RawMinLength,
            group.RawMaxLength);
        bool showRaw = !SameLengthRange(group.RawMinLength, group.RawMaxLength, group.Length);

        if (showFlat)
            details.Add($"flat {FormatLengthRange(group.FlatMinLength, group.FlatMaxLength)}");
        if (showRaw)
            details.Add($"{(group.HasPitch ? "slope" : "raw")} {FormatLengthRange(group.RawMinLength, group.RawMaxLength)}");

        string line = $"{count} @ {FormatLegendNumber(group.Length, 2, fixedDecimals: true)} {unit}";
        if (showRaw)
            line += " order";
        if (details.Count > 0)
            line += $" ({string.Join(", ", details)})";
        return line;
    }

    private static string FormatLengthRange(double min, double max)
    {
        if (Math.Abs(min - max) <= LengthLabelEpsilon)
            return FormatLegendNumber(min, 2, fixedDecimals: true);

        return $"{FormatLegendNumber(min, 2, fixedDecimals: true)}-{FormatLegendNumber(max, 2, fixedDecimals: true)}";
    }

    private static bool SameLengthRange(double min, double max, double value) =>
        Math.Abs(min - value) <= LengthLabelEpsilon &&
        Math.Abs(max - value) <= LengthLabelEpsilon;

    private static bool SameLengthRange(double minA, double maxA, double minB, double maxB) =>
        Math.Abs(minA - minB) <= LengthLabelEpsilon &&
        Math.Abs(maxA - maxB) <= LengthLabelEpsilon;

    private static double LengthValue(double meters, UnitMode unitMode) =>
        unitMode == UnitMode.Imperial ? meters / MetersPerFoot : meters;

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
            RoundingNearestFoot => "Round Up Foot",
            RoundingNearestEvenFoot => "Round Up Even Foot",
            RoundingNearestTwoFeet => "Round Up 2 Feet",
            _ => "None",
        };

    public static string NormalizePitch(string? value) =>
        TryNormalizePitch(value, out string normalized) ? normalized : "";

    public static bool TryNormalizePitch(string? value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!TryParsePitchParts(value, out double rise, out double run))
            return false;

        if (rise <= 0)
            return true;

        normalized = $"{FormatPitchPart(rise)}:{FormatPitchPart(run)}";
        return true;
    }

    public static bool TryParsePitchFactor(string? value, out double factor)
    {
        factor = 1;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!TryParsePitchParts(value, out double rise, out double run))
            return false;

        if (rise <= 0)
            return true;

        factor = Math.Sqrt(rise * rise + run * run) / run;
        return double.IsFinite(factor) && factor > 0;
    }

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
        // Round up to the nearest multiple of 2 feet that is
        // greater than or equal to the raw length. Previously this added 1
        // unconditionally before the parity check, so 8.0 ft became 10.0 ft
        // even when 8 ft already satisfied the rule.
        double rounded = Math.Ceiling(feet - 1e-9);
        return ((long)rounded) % 2 == 0 ? rounded : rounded + 1;
    }

    private static IReadOnlyList<IReadOnlyList<SKPoint>> AreaContours(
        IReadOnlyList<SKPoint> polygon,
        IReadOnlyList<IReadOnlyList<SKPoint>> holes)
    {
        var contours = new List<IReadOnlyList<SKPoint>> { polygon };
        contours.AddRange(holes.Where(hole => hole.Count >= 3));
        return contours;
    }

    private static List<LineIntersection> LineAreaIntersections(
        IReadOnlyList<IReadOnlyList<SKPoint>> contours,
        double offset,
        double dirX,
        double dirY,
        double normalX,
        double normalY)
    {
        var intersections = new List<LineIntersection>();
        foreach (var contour in contours)
            AddLinePolygonIntersections(intersections, contour, offset, dirX, dirY, normalX, normalY);

        return UniqueLineIntersections(intersections);
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
        AddLinePolygonIntersections(intersections, polygon, offset, dirX, dirY, normalX, normalY);
        return UniqueLineIntersections(intersections);
    }

    private static void AddLinePolygonIntersections(
        List<LineIntersection> intersections,
        IReadOnlyList<SKPoint> polygon,
        double offset,
        double dirX,
        double dirY,
        double normalX,
        double normalY)
    {
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
    }

    private static List<LineIntersection> UniqueLineIntersections(List<LineIntersection> intersections) =>
        intersections
            .OrderBy(intersection => intersection.T)
            .Aggregate(new List<LineIntersection>(), (unique, next) =>
            {
                if (unique.Count == 0 || Math.Abs(unique[^1].T - next.T) > ProjectionEpsilon)
                    unique.Add(next);
                return unique;
            });

    private static void AddIntersection(List<LineIntersection> intersections, SKPoint point, double dirX, double dirY)
    {
        double t = Dot(point, dirX, dirY);
        intersections.Add(new LineIntersection(t, point));
    }

    private static double Dot(SKPoint point, double x, double y) =>
        point.X * x + point.Y * y;

    public static double NormalizeDirectionDegrees(double degrees)
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

    private static bool TryParsePitchParts(string? value, out double rise, out double run)
    {
        rise = 0;
        run = DefaultPitchRun;
        string text = (value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Replace(",", ".", StringComparison.Ordinal)
            .Replace("\"", "", StringComparison.Ordinal)
            .Replace("'", "", StringComparison.Ordinal)
            .Replace("pitch", "", StringComparison.Ordinal)
            .Replace("slope", "", StringComparison.Ordinal)
            .Trim();

        if (string.IsNullOrWhiteSpace(text))
            return true;

        text = text
            .Replace(" in ", ":", StringComparison.Ordinal)
            .Replace(" on ", ":", StringComparison.Ordinal);

        char separator = text.Contains(':', StringComparison.Ordinal)
            ? ':'
            : text.Contains('/', StringComparison.Ordinal)
                ? '/'
                : '\0';

        if (separator == '\0')
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out rise))
                return false;
            run = DefaultPitchRun;
            return IsValidPitchParts(rise, run);
        }

        string[] parts = text.Split(separator, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out rise) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out run))
        {
            return false;
        }

        return IsValidPitchParts(rise, run);
    }

    private static bool IsValidPitchParts(double rise, double run) =>
        double.IsFinite(rise) &&
        double.IsFinite(run) &&
        rise >= 0 &&
        run > 0;

    private static string FormatPitchPart(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

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

    private static double AreaPt(
        IReadOnlyList<SKPoint> polygon,
        IReadOnlyList<IReadOnlyList<SKPoint>> holes)
    {
        double area = PolygonAreaPt(polygon);
        foreach (var hole in holes.Where(hole => hole.Count >= 3))
            area -= PolygonAreaPt(hole);

        return Math.Max(0, area);
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
    double DirectionDegrees = 0,
    string Pitch = "",
    double PitchFactor = 1)
{
    public int Count => Segments.Count;
}

public sealed record JoistLayoutSummary(
    bool HasScale,
    int Count,
    double TotalRawLengthMeters,
    double TotalLengthMeters,
    double AreaMetersSquared);

public sealed record JoistLengthGroup(
    int Count,
    double Length,
    double RawMinLength,
    double RawMaxLength,
    double FlatMinLength,
    double FlatMaxLength,
    bool HasPitch);

internal sealed class JoistLengthAccumulator
{
    private double rawMinLength = double.PositiveInfinity;
    private double rawMaxLength = double.NegativeInfinity;
    private double flatMinLength = double.PositiveInfinity;
    private double flatMaxLength = double.NegativeInfinity;
    private bool hasPitch;

    public JoistLengthAccumulator(double length)
    {
        Length = length;
    }

    public double Length { get; }
    public int Count { get; private set; }

    public void Add(JoistLengthGroup group)
    {
        Count += group.Count;
        rawMinLength = Math.Min(rawMinLength, group.RawMinLength);
        rawMaxLength = Math.Max(rawMaxLength, group.RawMaxLength);
        flatMinLength = Math.Min(flatMinLength, group.FlatMinLength);
        flatMaxLength = Math.Max(flatMaxLength, group.FlatMaxLength);
        hasPitch |= group.HasPitch;
    }

    public JoistLengthGroup ToGroup() => new(
        Count,
        Length,
        rawMinLength,
        rawMaxLength,
        flatMinLength,
        flatMaxLength,
        hasPitch);
}
