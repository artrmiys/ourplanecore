using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore.Models;

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

    /// <summary>A filled strip on the sheet with its fill luminance (0 = black, 1 = white).</summary>
    public readonly record struct FillZone(SKRect Rect, float Luminance);

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

        /// <summary>
        /// Zones (already inflated by the caller) where candidate faces are
        /// ignored — typically word bounding boxes, so room labels and
        /// dimension text never pair into fake walls.
        /// </summary>
        public IReadOnlyList<SKRect>? ExcludedZones { get; init; }

        /// <summary>
        /// Filled strips (wall poche boxes) with their luminance. When
        /// present, a candidate centerline must have one of them inside its
        /// face-to-face band: real walls are filled, casework outlines at
        /// wall-like spacing are hollow. Null or empty (sheets whose walls
        /// are drawn as bare line pairs) disables the check.
        /// </summary>
        public IReadOnlyList<FillZone>? WallFillZones { get; init; }

        /// <summary>
        /// When true, only dark-filled strips confirm a wall. On Revit-style
        /// sheets rated walls (demising, corridor, exterior) carry dark poche
        /// while in-unit partitions are light gray, so this traces just the
        /// structural wall set. Ignored when the sheet's fills do not split
        /// into a dark and a light family.
        /// </summary>
        public bool DarkFillOnly { get; init; }

        /// <summary>
        /// Luminance cutoff separating dark (rated) fill from light partition
        /// fill. Null (default) picks the cutoff per sheet by clustering the
        /// strip luminances, so plans drawn with other gray values keep
        /// working; set a value only to override the auto split.
        /// </summary>
        public float? DarkLuminanceMax { get; init; }

        /// <summary>
        /// Drop centerlines that run along the area polygon boundary within
        /// this distance (perimeter/exterior walls the user usually measures
        /// separately). 0 keeps them.
        /// </summary>
        public float BoundaryExclusionPt { get; init; }
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
        ZoneIndex? zones = ZoneIndex.Build(options.ExcludedZones);
        List<Segment> faces = CollectCandidateFaces(pageSegments, areaPolygon, holes, minFaceLen, zones);
        if (faces.Count < 2)
            return [];

        List<RawCenterline> rawCenterlines = PairFacesIntoCenterlines(faces, options);
        FilterToFilledWalls(rawCenterlines, options);
        List<Segment> merged = MergeCollinear(
            rawCenterlines.Select(c => c.Seg).ToList(), options.MaxThicknessPt);
        RemoveParallelDuplicates(merged, options.MaxThicknessPt);
        RemoveBoundaryWalls(merged, areaPolygon, holes, options.BoundaryExclusionPt);
        RemoveRareAngleNoise(merged, options.MaxThicknessPt);

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
        float minFaceLen,
        ZoneIndex? excludedZones)
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
                if (Length(clipped) < minFaceLen)
                    continue;
                if (IsMostlyInsideZones(clipped, excludedZones))
                    continue;

                result.Add(clipped);
            }
        }

        return result;
    }

    /// <summary>
    /// True when most of the face lies inside excluded text zones. Text
    /// strokes and label-frame lines sit entirely inside their word box and
    /// are dropped; a long wall face merely crossing a room tag keeps its
    /// full length, so walls no longer break where labels touch them.
    /// </summary>
    private static bool IsMostlyInsideZones(Segment seg, ZoneIndex? zones)
    {
        if (zones == null)
            return false;

        const int samples = 9;
        int inside = 0;
        for (int i = 0; i < samples; i++)
        {
            float t = (i + 0.5f) / samples;
            if (zones.Contains(Lerp(seg.A, seg.B, t)))
                inside++;
        }

        return inside >= samples * 0.6f;
    }

    /// <summary>Grid-hashed point-in-any-rect lookup for exclusion zones.</summary>
    private sealed class ZoneIndex
    {
        private const float CellSize = 48f;
        private readonly Dictionary<(int X, int Y), List<SKRect>> _cells = [];

        public static ZoneIndex? Build(IReadOnlyList<SKRect>? zones)
        {
            if (zones == null || zones.Count == 0)
                return null;

            var index = new ZoneIndex();
            foreach (SKRect zone in zones)
            {
                if (zone.Width <= 0 || zone.Height <= 0)
                    continue;

                for (int x = Cell(zone.Left); x <= Cell(zone.Right); x++)
                for (int y = Cell(zone.Top); y <= Cell(zone.Bottom); y++)
                {
                    if (!index._cells.TryGetValue((x, y), out List<SKRect>? list))
                        index._cells[(x, y)] = list = [];
                    list.Add(zone);
                }
            }

            return index._cells.Count == 0 ? null : index;
        }

        public bool Contains(SKPoint p)
        {
            if (!_cells.TryGetValue((Cell(p.X), Cell(p.Y)), out List<SKRect>? zones))
                return false;

            foreach (SKRect zone in zones)
            {
                if (p.X >= zone.Left && p.X <= zone.Right && p.Y >= zone.Top && p.Y <= zone.Bottom)
                    return true;
            }

            return false;
        }

        private static int Cell(float v) => (int)MathF.Floor(v / CellSize);
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

    /// <summary>A paired centerline plus the face-to-face thickness that produced it.</summary>
    private readonly record struct RawCenterline(Segment Seg, float Thickness);

    private static List<RawCenterline> PairFacesIntoCenterlines(List<Segment> faces, Options options)
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

        var centerlines = new List<RawCenterline>();
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

                    if (TryBuildCenterline(faces[i], faces[j], options, out RawCenterline centerline))
                        centerlines.Add(centerline);
                }
            }
        }

        return centerlines;
    }

    private static bool TryBuildCenterline(Segment a, Segment b, Options options, out RawCenterline centerline)
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
        centerline = new RawCenterline(new Segment(p0, p1), thickness);
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

    /// <summary>
    /// Keeps only centerlines whose wall body contains a filled strip. Fill
    /// boxes wider than a plausible wall are ignored (a big filled region
    /// would confirm everything inside it); the check is skipped entirely
    /// when the sheet offers no wall-like fill strips. The strip is looked
    /// for across the whole face-to-face band, not just on the centerline:
    /// in a thick exterior assembly the filled stud row sits off-center.
    /// </summary>
    private static void FilterToFilledWalls(List<RawCenterline> centerlines, Options options)
    {
        if (options.WallFillZones == null || options.WallFillZones.Count == 0)
            return;

        float maxStrip = options.MaxThicknessPt * 2f;
        List<FillZone> wallLike = options.WallFillZones
            .Where(z => Math.Min(z.Rect.Width, z.Rect.Height) <= maxStrip)
            .ToList();

        float? darkCutoff = null;
        if (options.DarkFillOnly)
        {
            darkCutoff = options.DarkLuminanceMax ?? AutoDarkCutoff(wallLike);
            if (darkCutoff.HasValue && !wallLike.Any(z => z.Luminance <= darkCutoff.Value))
                darkCutoff = null;
        }

        var strips = new List<SKRect>();
        foreach (FillZone zone in wallLike)
        {
            if (darkCutoff.HasValue && zone.Luminance > darkCutoff.Value)
                continue;

            SKRect inflated = zone.Rect;
            inflated.Inflate(0.75f, 0.75f);
            strips.Add(inflated);
        }

        ZoneIndex? index = ZoneIndex.Build(strips);
        if (index == null)
            return;

        centerlines.RemoveAll(c => !BandTouchesFill(c, index));
    }

    /// <summary>
    /// Splits the sheet's wall-strip luminances into a dark and a light
    /// family and returns the cutoff between them, or null when the fills
    /// form a single family (then dark-only filtering has nothing to
    /// separate). Sheets are not standardized on any particular gray values,
    /// so the split is found per sheet: candidate thresholds between
    /// luminance families are scored by between-class variance (Otsu),
    /// weighted by strip length so long structural walls dominate over
    /// small filled symbols. A family split needs a real luminance gap and
    /// both sides carrying at least 10% of the total strip length.
    /// </summary>
    private static float? AutoDarkCutoff(List<FillZone> strips)
    {
        var families = new SortedDictionary<float, float>();
        foreach (FillZone zone in strips)
        {
            float key = MathF.Round(zone.Luminance * 50f) / 50f;
            float length = Math.Max(zone.Rect.Width, zone.Rect.Height);
            families[key] = families.GetValueOrDefault(key) + length;
        }

        if (families.Count < 2)
            return null;

        float[] lums = [.. families.Keys];
        float[] weights = [.. families.Values];
        float total = weights.Sum();
        if (total <= 0f)
            return null;

        float bestVariance = 0f;
        float? best = null;
        for (int i = 0; i + 1 < lums.Length; i++)
        {
            if (lums[i + 1] - lums[i] < 0.12f)
                continue;

            float weightDark = 0f, sumDark = 0f;
            for (int k = 0; k <= i; k++)
            {
                weightDark += weights[k];
                sumDark += weights[k] * lums[k];
            }

            float weightLight = total - weightDark;
            if (weightDark < total * 0.1f || weightLight < total * 0.1f)
                continue;

            float meanDark = sumDark / weightDark;
            float meanLight = (weights.Zip(lums, (w, l) => w * l).Sum() - sumDark) / weightLight;
            float variance = weightDark * weightLight * (meanLight - meanDark) * (meanLight - meanDark);
            if (variance > bestVariance)
            {
                bestVariance = variance;
                best = (lums[i] + lums[i + 1]) * 0.5f;
            }
        }

        return best;
    }

    /// <summary>
    /// True when a fill strip is found at any lateral offset inside the wall
    /// band: on the centerline itself or shifted toward either face.
    /// </summary>
    private static bool BandTouchesFill(RawCenterline c, ZoneIndex fill)
    {
        SKPoint dir = Normalize(new SKPoint(c.Seg.B.X - c.Seg.A.X, c.Seg.B.Y - c.Seg.A.Y));
        SKPoint normal = new(-dir.Y, dir.X);
        ReadOnlySpan<float> offsets = [0f, 0.22f, -0.22f, 0.42f, -0.42f];
        foreach (float f in offsets)
        {
            float off = c.Thickness * f;
            var shift = new SKPoint(normal.X * off, normal.Y * off);
            SKPoint mid = Lerp(c.Seg.A, c.Seg.B, 0.5f);
            SKPoint q1 = Lerp(c.Seg.A, c.Seg.B, 0.25f);
            SKPoint q3 = Lerp(c.Seg.A, c.Seg.B, 0.75f);
            if (fill.Contains(new SKPoint(mid.X + shift.X, mid.Y + shift.Y)) ||
                (fill.Contains(new SKPoint(q1.X + shift.X, q1.Y + shift.Y)) &&
                 fill.Contains(new SKPoint(q3.X + shift.X, q3.Y + shift.Y))))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Drops centerlines that run along the area polygon boundary (within
    /// <paramref name="tolerancePt"/> and near-parallel to the closest edge).
    /// These are the perimeter/exterior walls; the user usually measures
    /// them separately, so the default trace keeps interior walls only.
    /// </summary>
    private static void RemoveBoundaryWalls(
        List<Segment> segments,
        IReadOnlyList<SKPoint> polygon,
        IReadOnlyList<IReadOnlyList<SKPoint>>? holes,
        float tolerancePt)
    {
        if (tolerancePt <= 0f)
            return;

        var rings = new List<IReadOnlyList<SKPoint>> { polygon };
        if (holes != null)
        {
            foreach (IReadOnlyList<SKPoint> hole in holes)
            {
                if (hole.Count >= 3)
                    rings.Add(hole);
            }
        }

        segments.RemoveAll(seg =>
            IsAlongBoundary(Lerp(seg.A, seg.B, 0.1f), SegmentAngleDeg(seg), rings, tolerancePt) &&
            IsAlongBoundary(Lerp(seg.A, seg.B, 0.5f), SegmentAngleDeg(seg), rings, tolerancePt) &&
            IsAlongBoundary(Lerp(seg.A, seg.B, 0.9f), SegmentAngleDeg(seg), rings, tolerancePt));
    }

    /// <summary>
    /// True when the point lies within the tolerance of a ring edge that is
    /// near-parallel to the wall: perpendicular interior walls that merely
    /// touch the boundary must survive.
    /// </summary>
    private static bool IsAlongBoundary(
        SKPoint p,
        float wallAngleDeg,
        List<IReadOnlyList<SKPoint>> rings,
        float tolerancePt)
    {
        foreach (IReadOnlyList<SKPoint> ring in rings)
        {
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                SKPoint a = ring[i];
                SKPoint b = ring[(i + 1) % n];
                if (DistancePointToSegment(p, a, b) > tolerancePt)
                    continue;
                if (AngleDeltaDeg(wallAngleDeg, SegmentAngleDeg(new Segment(a, b))) <= 15f)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Walls drawn with three or more parallel lines (faces plus a finish or
    /// insulation line) pair into several centerlines a few points apart.
    /// Keep the longest of every overlapping near-parallel group so each wall
    /// yields exactly one line; without this the offset twins chain into
    /// diagonal zigzags at wall ends.
    /// </summary>
    private static void RemoveParallelDuplicates(List<Segment> segments, float maxThickness)
    {
        float lateralTol = Math.Max(maxThickness * 0.6f, 1f);
        segments.Sort((x, y) => Length(y).CompareTo(Length(x)));

        var removed = new bool[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            if (removed[i])
                continue;

            Segment keeper = segments[i];
            SKPoint dir = Normalize(new SKPoint(keeper.B.X - keeper.A.X, keeper.B.Y - keeper.A.Y));
            float keeperAngle = SegmentAngleDeg(keeper);
            float k1 = Dot(new SKPoint(keeper.B.X - keeper.A.X, keeper.B.Y - keeper.A.Y), dir);

            for (int j = i + 1; j < segments.Count; j++)
            {
                if (removed[j])
                    continue;

                Segment other = segments[j];
                if (AngleDeltaDeg(keeperAngle, SegmentAngleDeg(other)) > 4f)
                    continue;
                if (DistancePointToLine(other.A, keeper.A, dir) > lateralTol ||
                    DistancePointToLine(other.B, keeper.A, dir) > lateralTol)
                    continue;

                float o0 = Dot(new SKPoint(other.A.X - keeper.A.X, other.A.Y - keeper.A.Y), dir);
                float o1 = Dot(new SKPoint(other.B.X - keeper.A.X, other.B.Y - keeper.A.Y), dir);
                if (o1 < o0) (o0, o1) = (o1, o0);

                float overlap = Math.Min(k1, o1) - Math.Max(0f, o0);
                if (overlap >= Length(other) * 0.6f)
                    removed[j] = true;
            }
        }

        for (int i = segments.Count - 1; i >= 0; i--)
        {
            if (removed[i])
                segments.RemoveAt(i);
        }
    }

    /// <summary>
    /// Real walls run in a few dominant directions (orthogonal grid, maybe an
    /// angled wing); slightly tilted strokes from plumbing and casework
    /// symbols pair into short centerlines at angles no wall family uses.
    /// Drop short centerlines whose angle family carries almost none of the
    /// total traced length; long lines are kept at any angle so a lone
    /// diagonal wall never disappears.
    /// </summary>
    private static void RemoveRareAngleNoise(List<Segment> segments, float maxThickness)
    {
        const float bucketDeg = 4f;
        const float minFamilyShare = 0.05f;
        int bucketCount = (int)MathF.Ceiling(180f / bucketDeg);
        float longEnoughToKeep = maxThickness * 6f;

        float total = 0f;
        var weight = new float[bucketCount];
        foreach (Segment seg in segments)
        {
            float len = Length(seg);
            weight[(int)(SegmentAngleDeg(seg) / bucketDeg) % bucketCount] += len;
            total += len;
        }

        if (total <= 0f)
            return;

        segments.RemoveAll(seg =>
        {
            if (Length(seg) >= longEnoughToKeep)
                return false;

            int bucket = (int)(SegmentAngleDeg(seg) / bucketDeg) % bucketCount;
            float family = weight[bucket]
                + weight[(bucket + 1) % bucketCount]
                + weight[(bucket + bucketCount - 1) % bucketCount];
            return family < total * minFamilyShare;
        });
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

            // Two walls meeting at an angle should join exactly where their
            // centerlines cross, not at the endpoint centroid: the centroid
            // pulls both lines sideways and leaves a visible diagonal kink.
            if (cluster.Count == 2 &&
                TryCornerIntersection(
                    segments[points[cluster[0]].SegIndex],
                    segments[points[cluster[1]].SegIndex],
                    snapped,
                    snapTol,
                    out SKPoint corner))
            {
                snapped = corner;
            }

            foreach (int idx in cluster)
            {
                assigned[idx] = true;
                (int segIndex, bool isA, _) = points[idx];
                Segment seg = segments[segIndex];
                segments[segIndex] = isA ? new Segment(snapped, seg.B) : new Segment(seg.A, snapped);
            }
        }
    }

    private static bool TryCornerIntersection(
        Segment a,
        Segment b,
        SKPoint near,
        float snapTol,
        out SKPoint corner)
    {
        corner = default;
        if (AngleDeltaDeg(SegmentAngleDeg(a), SegmentAngleDeg(b)) < 25f)
            return false;

        float rX = a.B.X - a.A.X, rY = a.B.Y - a.A.Y;
        float sX = b.B.X - b.A.X, sY = b.B.Y - b.A.Y;
        float denom = rX * sY - rY * sX;
        if (Math.Abs(denom) < 1e-6f)
            return false;

        float t = ((b.A.X - a.A.X) * sY - (b.A.Y - a.A.Y) * sX) / denom;
        var candidate = new SKPoint(a.A.X + rX * t, a.A.Y + rY * t);
        if (Distance(candidate, near) > snapTol * 1.5f)
            return false;

        corner = candidate;
        return true;
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
                    if (Distance(chain.Last!.Value, seg.A) <= joinTol &&
                        !ReversesDirection(chain.Last.Previous?.Value, chain.Last.Value, seg.B))
                    {
                        chain.AddLast(seg.B);
                        used[j] = true;
                        extended = true;
                    }
                    else if (Distance(chain.Last!.Value, seg.B) <= joinTol &&
                             !ReversesDirection(chain.Last.Previous?.Value, chain.Last.Value, seg.A))
                    {
                        chain.AddLast(seg.A);
                        used[j] = true;
                        extended = true;
                    }
                    else if (Distance(chain.First!.Value, seg.B) <= joinTol &&
                             !ReversesDirection(chain.First.Next?.Value, chain.First.Value, seg.A))
                    {
                        chain.AddFirst(seg.A);
                        used[j] = true;
                        extended = true;
                    }
                    else if (Distance(chain.First!.Value, seg.A) <= joinTol &&
                             !ReversesDirection(chain.First.Next?.Value, chain.First.Value, seg.B))
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

    /// <summary>
    /// True when extending the chain from <paramref name="joint"/> toward
    /// <paramref name="next"/> folds back over the previous edge (turn beyond
    /// ~135°). Walls turn at up to 90°; sharper folds are chained noise that
    /// draws as a spike.
    /// </summary>
    private static bool ReversesDirection(SKPoint? previous, SKPoint joint, SKPoint next)
    {
        if (previous == null)
            return false;

        SKPoint into = Normalize(new SKPoint(joint.X - previous.Value.X, joint.Y - previous.Value.Y));
        SKPoint outOf = Normalize(new SKPoint(next.X - joint.X, next.Y - joint.Y));
        return Dot(into, outOf) < -0.7f;
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

    private static float DistancePointToSegment(SKPoint p, SKPoint a, SKPoint b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-9f)
            return Distance(p, a);

        float t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0f, 1f);
        return Distance(p, new SKPoint(a.X + dx * t, a.Y + dy * t));
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
