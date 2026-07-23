using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore;

internal readonly record struct CutRegionRef(Measurement Parent, int HoleIndex);

internal sealed record CutRegionClipboardTemplate(
    string SourceParentId,
    IReadOnlyList<SKPoint> Points);

internal readonly record struct CutRegionPasteTargetResult(
    Measurement? Target,
    string Error)
{
    public bool Success => Target != null;
}

internal sealed record CutRegionPasteReservation(
    Measurement Target,
    List<SKPoint> Points);

internal static class CutRegionSelectionService
{
    private const float BoundaryTolerance = 0.001f;
    private const float ParameterTolerance = 0.000001f;
    private const float CrossTolerance = 0.000001f;

    public static IReadOnlyList<CutRegionRef> FindInMarquee(
        IEnumerable<Measurement> measurements,
        SKRect rect,
        bool selectTouched)
    {
        var hits = new List<CutRegionRef>();
        foreach (Measurement measurement in measurements)
        {
            if (measurement.MType != "area")
                continue;

            for (int holeIndex = 0; holeIndex < measurement.Holes.Count; holeIndex++)
            {
                IReadOnlyList<SKPoint> hole = measurement.Holes[holeIndex];
                if (hole.Count < 3)
                    continue;

                bool hit = selectTouched
                    ? PolygonIntersectsRect(hole, rect)
                    : PolygonContainedInRect(hole, rect);
                if (hit)
                    hits.Add(new CutRegionRef(measurement, holeIndex));
            }
        }

        return hits;
    }

    public static void ApplyGeometryTransform(
        IReadOnlyList<Measurement> wholeMeasurements,
        IReadOnlyList<CutRegionRef> cutRegions,
        Func<SKPoint, SKPoint> transform)
    {
        var wholeSet = new HashSet<Measurement>(wholeMeasurements);
        foreach (Measurement measurement in wholeMeasurements)
        {
            TransformPoints(measurement.Points, transform);
            foreach (List<SKPoint> hole in measurement.Holes)
                TransformPoints(hole, transform);
        }

        foreach (CutRegionRef cutRegion in cutRegions)
        {
            if (wholeSet.Contains(cutRegion.Parent) ||
                cutRegion.HoleIndex < 0 ||
                cutRegion.HoleIndex >= cutRegion.Parent.Holes.Count)
            {
                continue;
            }

            TransformPoints(cutRegion.Parent.Holes[cutRegion.HoleIndex], transform);
        }
    }

    public static CutRegionPasteTargetResult ResolvePasteTarget(
        IReadOnlyList<SKPoint> movedHole,
        string sourceParentId,
        Measurement? explicitTarget,
        IEnumerable<Measurement> candidates,
        ISet<Measurement>? excluded = null)
    {
        if (movedHole.Count < 3)
            return new CutRegionPasteTargetResult(null, "Copied cutout has no valid geometry.");

        var eligible = candidates
            .Where(candidate =>
                candidate.MType == "area" &&
                candidate.Points.Count >= 3 &&
                (excluded == null || !excluded.Contains(candidate)))
            .Distinct()
            .ToList();

        Measurement? source = eligible.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(sourceParentId) &&
            string.Equals(candidate.Id, sourceParentId, StringComparison.Ordinal));
        if (source != null && FitsInsideOuterBoundary(movedHole, source.Points))
            return new CutRegionPasteTargetResult(source, "");

        if (explicitTarget != null &&
            eligible.Contains(explicitTarget) &&
            FitsInsideOuterBoundary(movedHole, explicitTarget.Points))
        {
            return new CutRegionPasteTargetResult(explicitTarget, "");
        }

        var containing = eligible
            .Where(candidate => FitsInsideOuterBoundary(movedHole, candidate.Points))
            .ToList();
        return containing.Count switch
        {
            1 => new CutRegionPasteTargetResult(containing[0], ""),
            0 => new CutRegionPasteTargetResult(
                null,
                "No Area fully contains the pasted cutout. Select the destination Area and paste again."),
            _ => new CutRegionPasteTargetResult(
                null,
                "More than one Area contains the pasted cutout. Select the intended destination Area and paste again."),
        };
    }

    public static bool TryResolvePasteBundle(
        IReadOnlyList<CutRegionClipboardTemplate> templates,
        SKPoint offset,
        Measurement? explicitTarget,
        IEnumerable<Measurement> candidates,
        ISet<Measurement>? excluded,
        out List<CutRegionPasteReservation> reservations,
        out string error)
    {
        reservations = [];
        error = "";
        var candidateList = candidates.Distinct().ToList();
        foreach (CutRegionClipboardTemplate template in templates)
        {
            var moved = template.Points
                .Select(point => new SKPoint(point.X + offset.X, point.Y + offset.Y))
                .ToList();
            CutRegionPasteTargetResult result = ResolvePasteTarget(
                moved,
                template.SourceParentId,
                explicitTarget,
                candidateList,
                excluded);
            if (!result.Success)
            {
                reservations.Clear();
                error = result.Error;
                return false;
            }

            reservations.Add(new CutRegionPasteReservation(result.Target!, moved));
        }

        if (reservations.Count == templates.Count && reservations.Count > 0)
            return true;

        reservations.Clear();
        error = "The copied cutout bundle is empty.";
        return false;
    }

    public static bool FitsInsideOuterBoundary(
        IReadOnlyList<SKPoint> shape,
        IReadOnlyList<SKPoint> outer)
    {
        if (shape.Count < 3 || outer.Count < 3)
            return false;

        for (int i = 0; i < shape.Count; i++)
        {
            SKPoint start = shape[i];
            SKPoint end = shape[(i + 1) % shape.Count];
            if (!PointInsideOrOnBoundary(start, outer))
                return false;

            if (!SegmentStaysInsidePolygon(start, end, outer))
                return false;
        }

        return true;
    }

    public static bool OuterBoundaryIntersectsRect(
        IReadOnlyList<SKPoint> outer,
        SKRect rect)
    {
        if (outer.Any(point =>
                point.X >= rect.Left &&
                point.X <= rect.Right &&
                point.Y >= rect.Top &&
                point.Y <= rect.Bottom))
        {
            return true;
        }

        SKPoint[] corners =
        [
            new(rect.Left, rect.Top),
            new(rect.Right, rect.Top),
            new(rect.Right, rect.Bottom),
            new(rect.Left, rect.Bottom),
        ];
        for (int i = 0; i < outer.Count; i++)
        {
            SKPoint a = outer[i];
            SKPoint b = outer[(i + 1) % outer.Count];
            for (int edge = 0; edge < corners.Length; edge++)
            {
                if (SegmentsIntersect(a, b, corners[edge], corners[(edge + 1) % corners.Length]))
                    return true;
            }
        }

        return false;
    }

    private static bool PolygonContainedInRect(IReadOnlyList<SKPoint> polygon, SKRect rect) =>
        polygon.All(point =>
            point.X >= rect.Left &&
            point.X <= rect.Right &&
            point.Y >= rect.Top &&
            point.Y <= rect.Bottom);

    private static bool PolygonIntersectsRect(IReadOnlyList<SKPoint> polygon, SKRect rect)
    {
        if (polygon.Any(point =>
                point.X >= rect.Left &&
                point.X <= rect.Right &&
                point.Y >= rect.Top &&
                point.Y <= rect.Bottom))
        {
            return true;
        }

        SKPoint[] corners =
        [
            new(rect.Left, rect.Top),
            new(rect.Right, rect.Top),
            new(rect.Right, rect.Bottom),
            new(rect.Left, rect.Bottom),
        ];
        if (corners.Any(corner => MeasurementGeometry.PointInPolygon(corner, polygon)))
            return true;

        for (int i = 0; i < polygon.Count; i++)
        {
            SKPoint a = polygon[i];
            SKPoint b = polygon[(i + 1) % polygon.Count];
            for (int edge = 0; edge < corners.Length; edge++)
            {
                SKPoint c = corners[edge];
                SKPoint d = corners[(edge + 1) % corners.Length];
                if (SegmentsIntersect(a, b, c, d))
                    return true;
            }
        }

        return false;
    }

    private static void TransformPoints(
        IList<SKPoint> points,
        Func<SKPoint, SKPoint> transform)
    {
        for (int i = 0; i < points.Count; i++)
            points[i] = transform(points[i]);
    }

    private static bool PointInsideOrOnBoundary(
        SKPoint point,
        IReadOnlyList<SKPoint> polygon)
    {
        if (MeasurementGeometry.PointInPolygon(point, polygon))
            return true;

        for (int i = 0; i < polygon.Count; i++)
        {
            if (MeasurementGeometry.DistanceToSegment(
                    point,
                    polygon[i],
                    polygon[(i + 1) % polygon.Count]) <= BoundaryTolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static bool SegmentStaysInsidePolygon(
        SKPoint start,
        SKPoint end,
        IReadOnlyList<SKPoint> polygon)
    {
        var parameters = new List<float> { 0f, 1f };
        for (int i = 0; i < polygon.Count; i++)
        {
            AddIntersectionParameters(
                start,
                end,
                polygon[i],
                polygon[(i + 1) % polygon.Count],
                parameters);
        }

        parameters.Sort();
        var distinct = new List<float>(parameters.Count);
        foreach (float parameter in parameters)
        {
            float clamped = Math.Clamp(parameter, 0f, 1f);
            if (distinct.Count == 0 ||
                Math.Abs(clamped - distinct[^1]) > ParameterTolerance)
            {
                distinct.Add(clamped);
            }
        }

        for (int i = 1; i < distinct.Count; i++)
        {
            float left = distinct[i - 1];
            float right = distinct[i];
            if (right - left <= ParameterTolerance)
                continue;

            float t = (left + right) / 2f;
            var midpoint = new SKPoint(
                start.X + (end.X - start.X) * t,
                start.Y + (end.Y - start.Y) * t);
            if (!PointInsideOrOnBoundary(midpoint, polygon))
                return false;
        }

        return true;
    }

    private static void AddIntersectionParameters(
        SKPoint a,
        SKPoint b,
        SKPoint c,
        SKPoint d,
        ICollection<float> parameters)
    {
        var r = new SKPoint(b.X - a.X, b.Y - a.Y);
        var s = new SKPoint(d.X - c.X, d.Y - c.Y);
        float denominator = CrossVector(r, s);
        var delta = new SKPoint(c.X - a.X, c.Y - a.Y);
        if (Math.Abs(denominator) > CrossTolerance)
        {
            float t = CrossVector(delta, s) / denominator;
            float u = CrossVector(delta, r) / denominator;
            if (t >= -ParameterTolerance &&
                t <= 1f + ParameterTolerance &&
                u >= -ParameterTolerance &&
                u <= 1f + ParameterTolerance)
            {
                parameters.Add(t);
            }
            return;
        }

        if (Math.Abs(CrossVector(delta, r)) > CrossTolerance)
            return;

        float lengthSquared = r.X * r.X + r.Y * r.Y;
        if (lengthSquared <= BoundaryTolerance)
            return;

        float start = ((c.X - a.X) * r.X + (c.Y - a.Y) * r.Y) / lengthSquared;
        float end = ((d.X - a.X) * r.X + (d.Y - a.Y) * r.Y) / lengthSquared;
        if (start >= -ParameterTolerance && start <= 1f + ParameterTolerance)
            parameters.Add(start);
        if (end >= -ParameterTolerance && end <= 1f + ParameterTolerance)
            parameters.Add(end);
    }

    private static bool SegmentsIntersect(SKPoint a, SKPoint b, SKPoint c, SKPoint d)
    {
        float o1 = Cross(a, b, c);
        float o2 = Cross(a, b, d);
        float o3 = Cross(c, d, a);
        float o4 = Cross(c, d, b);
        if (OppositeSides(o1, o2) && OppositeSides(o3, o4))
            return true;

        return Math.Abs(o1) <= BoundaryTolerance && OnSegment(a, b, c) ||
               Math.Abs(o2) <= BoundaryTolerance && OnSegment(a, b, d) ||
               Math.Abs(o3) <= BoundaryTolerance && OnSegment(c, d, a) ||
               Math.Abs(o4) <= BoundaryTolerance && OnSegment(c, d, b);
    }

    private static bool OppositeSides(float left, float right) =>
        left > BoundaryTolerance && right < -BoundaryTolerance ||
        left < -BoundaryTolerance && right > BoundaryTolerance;

    private static float Cross(SKPoint a, SKPoint b, SKPoint c) =>
        (b.X - a.X) * (c.Y - a.Y) -
        (b.Y - a.Y) * (c.X - a.X);

    private static float CrossVector(SKPoint left, SKPoint right) =>
        left.X * right.Y - left.Y * right.X;

    private static bool OnSegment(SKPoint a, SKPoint b, SKPoint point) =>
        point.X >= Math.Min(a.X, b.X) - BoundaryTolerance &&
        point.X <= Math.Max(a.X, b.X) + BoundaryTolerance &&
        point.Y >= Math.Min(a.Y, b.Y) - BoundaryTolerance &&
        point.Y <= Math.Max(a.Y, b.Y) + BoundaryTolerance;
}
