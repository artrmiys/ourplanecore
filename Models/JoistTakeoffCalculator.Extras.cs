using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore;

public static partial class JoistTakeoffCalculator
{
    public static IReadOnlyList<JoistLengthGroup> RegularLengthGroups(
        JoistLayoutResult layout,
        UnitMode unitMode) =>
        FilteredLengthGroups(layout, unitMode, isExtra: false);

    public static IReadOnlyList<JoistLengthGroup> ExtraLengthGroups(
        JoistLayoutResult layout,
        UnitMode unitMode) =>
        FilteredLengthGroups(layout, unitMode, isExtra: true);

    public static IReadOnlyList<JoistLengthGroup> RegularLengthGroups(
        IEnumerable<Measurement> measurements,
        double fallbackScaleMetersPerPt,
        UnitMode unitMode) =>
        FilteredLengthGroups(measurements, fallbackScaleMetersPerPt, unitMode, isExtra: false);

    public static IReadOnlyList<JoistLengthGroup> ExtraLengthGroups(
        IEnumerable<Measurement> measurements,
        double fallbackScaleMetersPerPt,
        UnitMode unitMode) =>
        FilteredLengthGroups(measurements, fallbackScaleMetersPerPt, unitMode, isExtra: true);

    public static bool TryClipExtraJoist(
        Measurement measurement,
        SKPoint cursor,
        out JoistExtraSegment segment)
    {
        segment = new JoistExtraSegment();
        if (!measurement.JoistEnabled ||
            !measurement.JoistDirectionLocked ||
            OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) != "area")
        {
            return false;
        }

        return TryClipExtraJoist(
            measurement.Points,
            measurement.Holes,
            measurement.JoistDirectionDegrees,
            cursor,
            out segment);
    }

    public static bool TryClipExtraJoist(
        IReadOnlyList<SKPoint> polygon,
        IReadOnlyList<IReadOnlyList<SKPoint>> holes,
        double directionDegrees,
        SKPoint cursor,
        out JoistExtraSegment segment)
    {
        segment = new JoistExtraSegment();
        if (polygon.Count < 3 ||
            !double.IsFinite(directionDegrees) ||
            !IsFinite(cursor))
        {
            return false;
        }

        double radians = directionDegrees * Math.PI / 180.0;
        double dirX = Math.Cos(radians);
        double dirY = Math.Sin(radians);
        double normalX = -dirY;
        double normalY = dirX;
        double offset = Dot(cursor, normalX, normalY);
        double cursorT = Dot(cursor, dirX, dirY);
        var intersections = LineAreaIntersections(
            AreaContours(polygon, holes),
            offset,
            dirX,
            dirY,
            normalX,
            normalY);

        for (int i = 0; i + 1 < intersections.Count; i += 2)
        {
            LineIntersection a = intersections[i];
            LineIntersection b = intersections[i + 1];
            if (b.T - a.T <= ProjectionEpsilon ||
                cursorT < a.T - ProjectionEpsilon ||
                cursorT > b.T + ProjectionEpsilon)
            {
                continue;
            }

            segment.Start = a.Point;
            segment.End = b.Point;
            return true;
        }

        return false;
    }

    public static IReadOnlyList<List<JoistExtraSegment>> ClipExtraJoistsToAreaGeometries(
        IReadOnlyList<JoistExtraSegment> extraJoists,
        IReadOnlyList<AreaBooleanGeometry> geometries)
    {
        var result = geometries
            .Select(_ => new List<JoistExtraSegment>())
            .ToList();
        if (extraJoists.Count == 0 || geometries.Count == 0)
            return result;

        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (JoistExtraSegment source in extraJoists)
        {
            double sourceDx = source.End.X - source.Start.X;
            double sourceDy = source.End.Y - source.Start.Y;
            var pieces = new List<(int GeometryIndex, JoistExtraSegment Segment, double Position)>();
            for (int geometryIndex = 0; geometryIndex < geometries.Count; geometryIndex++)
            {
                foreach (JoistExtraSegment piece in ClipExtraJoistToArea(source, geometries[geometryIndex]))
                {
                    double position =
                        (piece.Start.X - source.Start.X) * sourceDx +
                        (piece.Start.Y - source.Start.Y) * sourceDy;
                    pieces.Add((geometryIndex, piece, position));
                }
            }

            pieces.Sort((left, right) => left.Position.CompareTo(right.Position));
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                (int geometryIndex, JoistExtraSegment segment, _) = pieces[pieceIndex];
                segment.Id = AllocateClippedExtraJoistId(source.Id, pieceIndex == 0, usedIds);
                result[geometryIndex].Add(segment);
            }
        }

        return result;
    }

    private static List<JoistExtraSegment> ClipExtraJoistToArea(
        JoistExtraSegment source,
        AreaBooleanGeometry geometry)
    {
        var result = new List<JoistExtraSegment>();
        if (geometry.Points.Count < 3 || !IsFinite(source.Start) || !IsFinite(source.End))
            return result;

        double dx = source.End.X - source.Start.X;
        double dy = source.End.Y - source.Start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(length) || length <= ProjectionEpsilon)
            return result;

        double dirX = dx / length;
        double dirY = dy / length;
        double normalX = -dirY;
        double normalY = dirX;
        double offset = Dot(source.Start, normalX, normalY);
        double sourceStartT = Dot(source.Start, dirX, dirY);
        double sourceEndT = sourceStartT + length;
        List<LineIntersection> intersections = LineAreaIntersections(
            AreaContours(geometry.Points, geometry.Holes),
            offset,
            dirX,
            dirY,
            normalX,
            normalY);
        var breakpoints = new List<double> { sourceStartT, sourceEndT };
        breakpoints.AddRange(intersections
            .Select(intersection => intersection.T)
            .Where(value => value > sourceStartT + ProjectionEpsilon &&
                            value < sourceEndT - ProjectionEpsilon));
        breakpoints.Sort();

        using SKPath fillPath = BuildExtraJoistClipPath(geometry);
        double? retainedStartT = null;
        double retainedEndT = 0;
        for (int i = 1; i < breakpoints.Count; i++)
        {
            double intervalStartT = breakpoints[i - 1];
            double intervalEndT = breakpoints[i];
            if (intervalEndT - intervalStartT <= ProjectionEpsilon)
                continue;

            double midpointT = (intervalStartT + intervalEndT) / 2.0;
            SKPoint midpoint = PointOnProjectedLine(
                source.Start,
                dirX,
                dirY,
                midpointT - sourceStartT);
            if (fillPath.Contains(midpoint.X, midpoint.Y))
            {
                retainedStartT ??= intervalStartT;
                retainedEndT = intervalEndT;
                continue;
            }

            AddClippedExtraJoistPiece(
                result,
                source,
                dirX,
                dirY,
                sourceStartT,
                retainedStartT,
                retainedEndT);
            retainedStartT = null;
        }

        AddClippedExtraJoistPiece(
            result,
            source,
            dirX,
            dirY,
            sourceStartT,
            retainedStartT,
            retainedEndT);

        return result;
    }

    private static void AddClippedExtraJoistPiece(
        ICollection<JoistExtraSegment> result,
        JoistExtraSegment source,
        double dirX,
        double dirY,
        double sourceStartT,
        double? retainedStartT,
        double retainedEndT)
    {
        if (!retainedStartT.HasValue || retainedEndT - retainedStartT.Value <= ProjectionEpsilon)
            return;

        result.Add(new JoistExtraSegment
        {
            Id = source.Id,
            Start = PointOnProjectedLine(source.Start, dirX, dirY, retainedStartT.Value - sourceStartT),
            End = PointOnProjectedLine(source.Start, dirX, dirY, retainedEndT - sourceStartT),
        });
    }

    private static SKPath BuildExtraJoistClipPath(AreaBooleanGeometry geometry)
    {
        var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        AddExtraJoistClipContour(path, geometry.Points);
        foreach (IReadOnlyList<SKPoint> hole in geometry.Holes)
            AddExtraJoistClipContour(path, hole);
        return path;
    }

    private static void AddExtraJoistClipContour(SKPath path, IReadOnlyList<SKPoint> points)
    {
        if (points.Count < 3)
            return;

        path.MoveTo(points[0]);
        for (int i = 1; i < points.Count; i++)
            path.LineTo(points[i]);
        path.Close();
    }

    private static SKPoint PointOnProjectedLine(
        SKPoint origin,
        double dirX,
        double dirY,
        double distance) =>
        new(
            (float)(origin.X + dirX * distance),
            (float)(origin.Y + dirY * distance));

    private static string AllocateClippedExtraJoistId(
        string? sourceId,
        bool preferSourceId,
        ISet<string> usedIds)
    {
        string preferred = (sourceId ?? "").Trim();
        if (preferSourceId && preferred.Length > 0 && usedIds.Add(preferred))
            return preferred;

        string generated;
        do
        {
            generated = Guid.NewGuid().ToString();
        }
        while (!usedIds.Add(generated));

        return generated;
    }

    private static JoistLayoutResult IncludeExtraJoists(
        JoistLayoutResult layout,
        IReadOnlyList<JoistExtraSegment>? extraJoists,
        double scaleMetersPerPt,
        string lengthRounding)
    {
        if (!layout.HasScale || extraJoists == null || extraJoists.Count == 0)
            return layout;

        string normalizedRounding = NormalizeLengthRounding(lengthRounding);
        var extras = new List<JoistSegment>();
        for (int i = 0; i < extraJoists.Count; i++)
        {
            JoistExtraSegment extra = extraJoists[i];
            string extraId = string.IsNullOrWhiteSpace(extra.Id)
                ? $"legacy-extra-{i}"
                : extra.Id;
            if (TryCreateCalculatedExtra(
                extra,
                extraId,
                scaleMetersPerPt,
                layout.PitchFactor,
                normalizedRounding,
                out JoistSegment calculated))
            {
                extras.Add(calculated);
            }
        }

        if (extras.Count == 0)
            return layout;

        return layout with
        {
            Segments = layout.Segments.Concat(extras).ToList(),
            TotalRawLengthMeters = layout.TotalRawLengthMeters + extras.Sum(segment => segment.RawLengthMeters),
            TotalLengthMeters = layout.TotalLengthMeters + extras.Sum(segment => segment.OrderLengthMeters),
        };
    }

    private static bool TryCreateCalculatedExtra(
        JoistExtraSegment extra,
        string extraId,
        double scaleMetersPerPt,
        double pitchFactor,
        string normalizedRounding,
        out JoistSegment segment)
    {
        segment = null!;
        if (!IsFinite(extra.Start) || !IsFinite(extra.End))
            return false;

        double dx = extra.End.X - extra.Start.X;
        double dy = extra.End.Y - extra.Start.Y;
        double lengthPt = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(lengthPt) || lengthPt <= ProjectionEpsilon)
            return false;

        double flatMeters = lengthPt * scaleMetersPerPt;
        double rawMeters = flatMeters * pitchFactor;
        double rawFeet = rawMeters / MetersPerFoot;
        double orderFeet = RoundLengthFeet(rawFeet, normalizedRounding);
        segment = new JoistSegment(
            extra.Start,
            extra.End,
            flatMeters,
            rawMeters,
            orderFeet * MetersPerFoot,
            orderFeet,
            extraId);
        return true;
    }

    private static bool IsFinite(SKPoint point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y);

    private static IReadOnlyList<JoistLengthGroup> FilteredLengthGroups(
        JoistLayoutResult layout,
        UnitMode unitMode,
        bool isExtra)
    {
        if (!layout.HasScale)
            return [];

        return layout.Segments
            .Where(segment => segment.IsExtra == isExtra)
            .GroupBy(segment => unitMode == UnitMode.Imperial
                ? Math.Round(segment.OrderLengthFeet, 2)
                : Math.Round(segment.OrderLengthMeters, 2))
            .OrderByDescending(group => group.Key)
            .Select(group => CreateLengthGroup(group.Key, group.ToList(), unitMode, layout.PitchFactor))
            .ToList();
    }

    private static IReadOnlyList<JoistLengthGroup> FilteredLengthGroups(
        IEnumerable<Measurement> measurements,
        double fallbackScaleMetersPerPt,
        UnitMode unitMode,
        bool isExtra)
    {
        var groups = new Dictionary<double, JoistLengthAccumulator>();
        foreach (Measurement measurement in measurements)
        {
            JoistLayoutResult layout = Calculate(measurement, fallbackScaleMetersPerPt);
            foreach (JoistLengthGroup group in FilteredLengthGroups(layout, unitMode, isExtra))
            {
                if (!groups.TryGetValue(group.Length, out JoistLengthAccumulator? accumulator))
                {
                    accumulator = new JoistLengthAccumulator(group.Length);
                    groups[group.Length] = accumulator;
                }

                accumulator.Add(group);
            }
        }

        return groups.Values
            .Select(group => group.ToGroup())
            .OrderByDescending(group => group.Length)
            .ToList();
    }

    private static void AppendExtraLengthGroupLines(
        List<string> lines,
        IReadOnlyList<JoistLengthGroup> extraGroups,
        UnitMode unitMode,
        bool detailedLabels)
    {
        if (extraGroups.Count == 0)
            return;

        lines.Add("Extra");
        const int maxGroupLines = 24;
        lines.AddRange(FormatLengthGroupLines(extraGroups.Take(maxGroupLines), unitMode, detailedLabels));
        if (extraGroups.Count <= maxGroupLines)
            return;

        int hiddenPieces = extraGroups.Skip(maxGroupLines).Sum(group => group.Count);
        lines.Add($"(+{extraGroups.Count - maxGroupLines} more / {hiddenPieces} pcs)");
    }
}

public sealed record JoistSegment(
    SKPoint Start,
    SKPoint End,
    double FlatLengthMeters,
    double RawLengthMeters,
    double OrderLengthMeters,
    double OrderLengthFeet,
    string ExtraId = "")
{
    public bool IsExtra => !string.IsNullOrWhiteSpace(ExtraId);
}
