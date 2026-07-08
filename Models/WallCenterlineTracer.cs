using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlaneCore.Models;

/// <summary>
/// Finds wall centerlines inside an area polygon from raw PDF vector segments.
/// A "wall" is a pair of near-parallel segments (the two drawn faces) whose
/// perpendicular distance falls inside the requested thickness range; the
/// result is one centerline in the middle of each pair, with collinear pieces
/// merged and endpoints snapped so the trace reads as a connected outline.
/// All coordinates are PDF points.
/// </summary>
public static class WallCenterlineTracer
{
    public readonly record struct Segment(SKPoint A, SKPoint B);

    public sealed class Options
    {
        /// <summary>Minimum wall thickness (face-to-face distance), PDF pt.</summary>
        public float MinThicknessPt { get; init; }

        /// <summary>Maximum wall thickness (face-to-face distance), PDF pt.</summary>
        public float MaxThicknessPt { get; init; }

        /// <summary>Faces must stay within this angle of each other, degrees.</summary>
        public float ParallelToleranceDeg { get; init; } = 4.0f;

        /// <summary>Ignore face segments shorter than this, PDF pt (culls text/hatch noise).</summary>
        public float MinFaceLengthPt { get; init; }

        /// <summary>Discard centerlines shorter than this, PDF pt.</summary>
        public float MinWallLengthPt { get; init; }

        /// <summary>Hard cap on produced centerlines (safety against hatch-heavy areas).</summary>
        public int MaxResults { get; init; } = 600;
    }

    private const float AngleBucketDeg = 4.0f;

    public static List<SKPoint[]> Trace(
        IReadOnlyList<Segment> pageSegments,
        IReadOnlyList<SKPoint> areaPolygon,
        Options options,
        IReadOnlyList<IReadOnlyList<SKPoint>>? holes = null)
    {
        if (pageSegments.Count == 0 || areaPolygon.Count < 3)
            return [];

        float minFaceLen = Math.Max(options.MinFaceLengthPt, options.MaxThicknessPt * 0.75f);
        List<Segment> faces = CollectCandidateFaces(pageSegments, areaPolygon, holes, minFaceLen);
        if (faces.Count < 2)
            return [];

        List<Segment> rawCenterlines = PairFacesIntoCenterlines(faces, options);
        List<Segment> merged = MergeCollinear(rawCenterlines, options.MaxThicknessPt);

        merged.RemoveAll(s => Length(s) < Math.Max(options.MinWallLengthPt, 1f));
        if (merged.Count > options.MaxResults)
        {
            merged.Sort((x, y) => Length(y).CompareTo(Length(x)));
            merged.RemoveRange(options.MaxResults, merged.Count - options.MaxResults);
        }

        SnapEndpoints(merged, options.MaxThicknessPt);
        return ChainIntoPolylines(merged, options.MaxThicknessPt * 0.35f);
    }

    // ------------------------------------------------------------------
    // Stage 1: clip page segments to the area polygon, drop short noise.
    // ------------------------------------------------------------------

    private static List<Segment> CollectCandidateFaces(
        IReadOnlyList<Segment> segments,
        IReadOnlyList<SKPoint> polygon,
        IReadOnlyList<IReadOnlyList<SKPoint>>? holes,
        float minFaceLen)
    {
        SKRect bounds = PolygonBounds(polygon);
        var result = new List<Segment>();
        foreach (Segment seg in segments)
        {
            if (Length(seg) < minFaceLen)
                continue;

            SKRect segBounds = SKRect.Create(
                Math.Min(seg.A.X, seg.B.X),
                Math.Min(seg.A.Y, seg.B.Y),
                Math.Abs(seg.B.X - seg.A.X),
                Math.Abs(seg.B.Y - seg.A.Y));
            segBounds.Inflate(0.5f, 0.5f);
            if (!bounds.IntersectsWith(segBounds))
                continue;

            foreach (Segment clipped in ClipSegmentToPolygon(seg, polygon, holes))
            {
                if (Length(clipped) >= minFaceLen)
                    result.Add(clipped);
            }
        }

        return result;
    }

    /// <summary>Returns the parts of the segment inside the polygon but outside its holes.</summary>
    private static IEnumerable<Segment> ClipSegmentToPolygon(
        Segment seg,
        IReadOnlyList<SKPoint> polygon,
        IReadOnlyList<IReadOnlyList<SKPoint>>? holes)
    {
        // Collect parametric intersection points with every polygon and hole
        // edge, then classify midpoints of the resulting spans.
        var cuts = new List<float> { 0f, 1f };
        AddEdgeCuts(seg, polygon, cuts);
        if (holes != null)
        {
            foreach (IReadOnlyList<SKPoint> hole in holes)
            {
                if (hole.Count >= 3)
                    AddEdgeCuts(seg, hole, cuts);
            }
        }

        cuts.Sort();
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            float t0 = cuts[i], t1 = cuts[i + 1];
            if (t1 - t0 < 1e-4f)
                continue;

            SKPoint mid = Lerp(seg.A, seg.B, (t0 + t1) * 0.5f);
            if (!PointInPolygon(mid, polygon))
                continue;
            if (holes != null && holes.Any(h => h.Count >= 3 && PointInPolygon(mid, h)))
                continue;

            yield return new Segment(Lerp(seg.A, seg.B, t0), Lerp(seg.A, seg.B, t1));
        }
    }

    private static void AddEdgeCuts(Segment seg, IReadOnlyList<SKPoint> ring, List<float> cuts)
    {
        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            SKPoint c = ring[i];
            SKPoint d = ring[(i + 1) % n];
            if (TrySegmentIntersection(seg.A, seg.B, c, d, out float t))
                cuts.Add(t);
        }
    }

    // ------------------------------------------------------------------
    // Stage 2: pair near-parallel faces at wall-thickness distance.
    // ------------------------------------------------------------------

    private static List<Segment> PairFacesIntoCenterlines(List<Segment> faces, Options options)
    {
        // Bucket by direction so only near-parallel segments are compared, and
        // grid-hash by position so far-apart segments are skipped: keeps the
        // pairing near-linear on hatch-heavy sheets instead of O(n^2).
        int bucketCount = (int)MathF.Ceiling(180f / AngleBucketDeg);
        var byAngle = new List<int>[bucketCount];
        var angles = new float[faces.Count];
        for (int i = 0; i < faces.Count; i++)
        {
            float angle = SegmentAngleDeg(faces[i]);
            angles[i] = angle;
            int bucket = (int)(angle / AngleBucketDeg) % bucketCount;
            (byAngle[bucket] ??= []).Add(i);
        }

        float cell = Math.Max(options.MaxThicknessPt * 4f, 8f);
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < faces.Count; i++)
        {
            SKPoint mid = Lerp(faces[i].A, faces[i].B, 0.5f);
            var key = ((int)MathF.Floor(mid.X / cell), (int)MathF.Floor(mid.Y / cell));
            if (!grid.TryGetValue(key, out List<int>? list))
                grid[key] = list = [];
            list.Add(i);
        }

        var centerlines = new List<Segment>();
        var seenPairs = new HashSet<(int, int)>();
        for (int i = 0; i < faces.Count; i++)
        {
            SKPoint mid = Lerp(faces[i].A, faces[i].B, 0.5f);
            int cx = (int)MathF.Floor(mid.X / cell);
            int cy = (int)MathF.Floor(mid.Y / cell);
            float halfLen = Length(faces[i]) * 0.5f;
            int reach = Math.Max(1, (int)MathF.Ceiling(halfLen / cell));
            for (int dx = -reach; dx <= reach; dx++)
            for (int dy = -reach; dy <= reach; dy++)
            {
                if (!grid.TryGetValue((cx + dx, cy + dy), out List<int>? nearby))
                    continue;

                foreach (int j in nearby)
                {
                    if (j <= i)
                        continue;
                    if (AngleDeltaDeg(angles[i], angles[j]) > options.ParallelToleranceDeg)
                        continue;
                    if (!seenPairs.Add((i, j)))
                        continue;

                    if (TryBuildCenterline(faces[i], faces[j], options, out Segment centerline))
                        centerlines.Add(centerline);
                }
            }
        }

        return centerlines;
    }

    private static bool TryBuildCenterline(Segment a, Segment b, Options options, out Segment centerline)
    {
        centerline = default;

        SKPoint dir = Normalize(new SKPoint(a.B.X - a.A.X, a.B.Y - a.A.Y));

        // Perpendicular distance between the two faces, measured from b's
        // endpoints to a's infinite line; both ends must be inside the
        // thickness window so skewed pairs are rejected.
        float d1 = DistancePointToLine(b.A, a.A, dir);
        float d2 = DistancePointToLine(b.B, a.A, dir);
        if (d1 < options.MinThicknessPt || d1 > options.MaxThicknessPt ||
            d2 < options.MinThicknessPt || d2 > options.MaxThicknessPt)
            return false;

        // Overlap of the two faces along the shared direction.
        float a0 = 0f;
        float a1 = Dot(new SKPoint(a.B.X - a.A.X, a.B.Y - a.A.Y), dir);
        float b0 = Dot(new SKPoint(b.A.X - a.A.X, b.A.Y - a.A.Y), dir);
        float b1 = Dot(new SKPoint(b.B.X - a.A.X, b.B.Y - a.A.Y), dir);
        if (a1 < a0) (a0, a1) = (a1, a0);
        if (b1 < b0) (b0, b1) = (b1, b0);

        float start = Math.Max(a0, b0);
        float end = Math.Min(a1, b1);
        float thickness = (d1 + d2) * 0.5f;
        if (end - start < Math.Max(thickness * 0.9f, 2f))
            return false;

        // Midline: offset a's line by half the face distance toward b.
        SKPoint normal = new(-dir.Y, dir.X);
        float side = Math.Sign(Dot(new SKPoint(b.A.X - a.A.X, b.A.Y - a.A.Y), normal));
        SKPoint offset = new(normal.X * thickness * 0.5f * side, normal.Y * thickness * 0.5f * side);

        SKPoint p0 = new(a.A.X + dir.X * start + offset.X, a.A.Y + dir.Y * start + offset.Y);
        SKPoint p1 = new(a.A.X + dir.X * end + offset.X, a.A.Y + dir.Y * end + offset.Y);
        centerline = new Segment(p0, p1);
        return true;
    }

    // ------------------------------------------------------------------
    // Stage 3: merge collinear/overlapping centerlines from repeated pairs.
    // ------------------------------------------------------------------

    private static List<Segment> MergeCollinear(List<Segment> segments, float maxThickness)
    {
        float lateralTol = Math.Max(maxThickness * 0.3f, 0.75f);
        float gapTol = Math.Max(maxThickness * 0.75f, 2f);

        var remaining = new List<Segment>(segments);
        var merged = new List<Segment>();
        var used = new bool[remaining.Count];

        for (int i = 0; i < remaining.Count; i++)
        {
            if (used[i])
                continue;

            Segment current = remaining[i];
            used[i] = true;
            bool grew = true;
            while (grew)
            {
                grew = false;
                SKPoint dir = Normalize(new SKPoint(current.B.X - current.A.X, current.B.Y - current.A.Y));
                float curAngle = SegmentAngleDeg(current);
                for (int j = 0; j < remaining.Count; j++)
                {
                    if (used[j])
                        continue;

                    Segment other = remaining[j];
                    if (AngleDeltaDeg(curAngle, SegmentAngleDeg(other)) > 3f)
                        continue;
                    if (DistancePointToLine(other.A, current.A, dir) > lateralTol ||
                        DistancePointToLine(other.B, current.A, dir) > lateralTol)
                        continue;

                    float c0 = 0f;
                    float c1 = Dot(new SKPoint(current.B.X - current.A.X, current.B.Y - current.A.Y), dir);
                    float o0 = Dot(new SKPoint(other.A.X - current.A.X, other.A.Y - current.A.Y), dir);
                    float o1 = Dot(new SKPoint(other.B.X - current.A.X, other.B.Y - current.A.Y), dir);
                    if (o1 < o0) (o0, o1) = (o1, o0);
                    if (o0 > c1 + gapTol || o1 < c0 - gapTol)
                        continue;

                    float start = Math.Min(c0, o0);
                    float end = Math.Max(c1, o1);
                    SKPoint anchor = current.A;
                    current = new Segment(
                        new SKPoint(anchor.X + dir.X * start, anchor.Y + dir.Y * start),
                        new SKPoint(anchor.X + dir.X * end, anchor.Y + dir.Y * end));
                    used[j] = true;
                    grew = true;
                }
            }

            merged.Add(current);
        }

        return merged;
    }

    // ------------------------------------------------------------------
    // Stage 4: snap nearby endpoints and chain segments into polylines.
    // ------------------------------------------------------------------

    private static void SnapEndpoints(List<Segment> segments, float maxThickness)
    {
        float snapTol = Math.Max(maxThickness * 1.2f, 2f);
        var points = new List<(int SegIndex, bool IsA, SKPoint Point)>();
        for (int i = 0; i < segments.Count; i++)
        {
            points.Add((i, true, segments[i].A));
            points.Add((i, false, segments[i].B));
        }

        var assigned = new bool[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            if (assigned[i])
                continue;

            var cluster = new List<int> { i };
            for (int j = i + 1; j < points.Count; j++)
            {
                if (assigned[j])
                    continue;
                if (Distance(points[i].Point, points[j].Point) <= snapTol)
                    cluster.Add(j);
            }

            if (cluster.Count < 2)
                continue;

            float sx = 0, sy = 0;
            foreach (int idx in cluster)
            {
                sx += points[idx].Point.X;
                sy += points[idx].Point.Y;
            }

            var snapped = new SKPoint(sx / cluster.Count, sy / cluster.Count);
            foreach (int idx in cluster)
            {
                assigned[idx] = true;
                (int segIndex, bool isA, _) = points[idx];
                Segment seg = segments[segIndex];
                segments[segIndex] = isA ? new Segment(snapped, seg.B) : new Segment(seg.A, snapped);
            }
        }
    }

    private static List<SKPoint[]> ChainIntoPolylines(List<Segment> segments, float joinTol)
    {
        var used = new bool[segments.Count];
        var polylines = new List<SKPoint[]>();

        for (int i = 0; i < segments.Count; i++)
        {
            if (used[i])
                continue;

            used[i] = true;
            var chain = new LinkedList<SKPoint>();
            chain.AddLast(segments[i].A);
            chain.AddLast(segments[i].B);

            bool extended = true;
            while (extended)
            {
                extended = false;
                for (int j = 0; j < segments.Count; j++)
                {
                    if (used[j])
                        continue;

                    Segment seg = segments[j];
                    if (Distance(chain.Last!.Value, seg.A) <= joinTol)
                    {
                        chain.AddLast(seg.B);
                        used[j] = true;
                        extended = true;
                    }
                    else if (Distance(chain.Last!.Value, seg.B) <= joinTol)
                    {
                        chain.AddLast(seg.A);
                        used[j] = true;
                        extended = true;
                    }
                    else if (Distance(chain.First!.Value, seg.B) <= joinTol)
                    {
                        chain.AddFirst(seg.A);
                        used[j] = true;
                        extended = true;
                    }
                    else if (Distance(chain.First!.Value, seg.A) <= joinTol)
                    {
                        chain.AddFirst(seg.B);
                        used[j] = true;
                        extended = true;
                    }
                }
            }

            polylines.Add(chain.ToArray());
        }

        return polylines;
    }

    // ------------------------------------------------------------------
    // Geometry helpers
    // ------------------------------------------------------------------

    private static float Length(Segment s) => Distance(s.A, s.B);

    private static float Distance(SKPoint a, SKPoint b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static SKPoint Lerp(SKPoint a, SKPoint b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static float Dot(SKPoint a, SKPoint b) => a.X * b.X + a.Y * b.Y;

    private static SKPoint Normalize(SKPoint v)
    {
        float len = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
        return len < 1e-6f ? new SKPoint(1, 0) : new SKPoint(v.X / len, v.Y / len);
    }

    /// <summary>Angle of the segment folded into [0, 180).</summary>
    private static float SegmentAngleDeg(Segment s)
    {
        float angle = MathF.Atan2(s.B.Y - s.A.Y, s.B.X - s.A.X) * 180f / MathF.PI;
        if (angle < 0) angle += 180f;
        if (angle >= 180f) angle -= 180f;
        return angle;
    }

    private static float AngleDeltaDeg(float a, float b)
    {
        float d = Math.Abs(a - b);
        return Math.Min(d, 180f - d);
    }

    private static float DistancePointToLine(SKPoint p, SKPoint origin, SKPoint dir)
    {
        float vx = p.X - origin.X, vy = p.Y - origin.Y;
        return Math.Abs(vx * -dir.Y + vy * dir.X);
    }

    private static bool TrySegmentIntersection(SKPoint a, SKPoint b, SKPoint c, SKPoint d, out float t)
    {
        t = 0;
        float rX = b.X - a.X, rY = b.Y - a.Y;
        float sX = d.X - c.X, sY = d.Y - c.Y;
        float denom = rX * sY - rY * sX;
        if (Math.Abs(denom) < 1e-9f)
            return false;

        float qpX = c.X - a.X, qpY = c.Y - a.Y;
        float tt = (qpX * sY - qpY * sX) / denom;
        float uu = (qpX * rY - qpY * rX) / denom;
        if (tt < 0f || tt > 1f || uu < 0f || uu > 1f)
            return false;

        t = tt;
        return true;
    }

    private static bool PointInPolygon(SKPoint p, IReadOnlyList<SKPoint> polygon)
    {
        bool inside = false;
        int n = polygon.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            SKPoint pi = polygon[i], pj = polygon[j];
            if (pi.Y > p.Y != pj.Y > p.Y &&
                p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y) + pi.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static SKRect PolygonBounds(IReadOnlyList<SKPoint> polygon)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (SKPoint p in polygon)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        return new SKRect(minX, minY, maxX, maxY);
    }
}
