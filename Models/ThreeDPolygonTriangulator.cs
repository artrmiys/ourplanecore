namespace OurPlaneCore;

public sealed class ThreeDPolygonTriangulation
{
    public bool Success { get; init; }
    public List<ThreeDPoint> Points { get; init; } = [];
    public List<int> TriangleIndices { get; init; } = [];
    public string Message { get; init; } = "";
    public int OriginalPointCount { get; init; }
}

public static class ThreeDPolygonTriangulator
{
    private const double Epsilon = 0.000001;

    public static ThreeDPolygonTriangulation Triangulate(
        IReadOnlyList<ThreeDPoint> sourcePoints,
        double toleranceFeet = 0.01)
    {
        List<ThreeDPoint> points = CleanPolygon(sourcePoints, toleranceFeet);
        if (points.Count < 3)
            return Failure(sourcePoints.Count, points, "slab area has fewer than three unique points");

        double area = SignedArea(points);
        if (Math.Abs(area) < Epsilon)
            return Failure(sourcePoints.Count, points, "slab area is nearly flat or self-canceling");

        if (area < 0)
            points.Reverse();

        if (HasSelfIntersections(points, toleranceFeet))
            return Failure(sourcePoints.Count, points, "slab area has crossing edges; check point order in the area takeoff");

        List<int> triangles = EarClip(points);
        if (triangles.Count == 0)
            return Failure(sourcePoints.Count, points, "slab area could not be triangulated; check for duplicate or crossing points");

        string message = points.Count == sourcePoints.Count
            ? ""
            : $"slab points cleaned from {sourcePoints.Count} to {points.Count}";
        return new ThreeDPolygonTriangulation
        {
            Success = true,
            Points = points,
            TriangleIndices = triangles,
            Message = message,
            OriginalPointCount = sourcePoints.Count,
        };
    }

    private static List<int> EarClip(IReadOnlyList<ThreeDPoint> points)
    {
        var remaining = Enumerable.Range(0, points.Count).ToList();
        var triangles = new List<int>();
        int guard = points.Count * points.Count;

        while (remaining.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int previous = remaining[(i - 1 + remaining.Count) % remaining.Count];
                int current = remaining[i];
                int next = remaining[(i + 1) % remaining.Count];
                if (!IsConvex(points[previous], points[current], points[next]))
                    continue;

                if (ContainsOtherPoint(points, remaining, previous, current, next))
                    continue;

                triangles.Add(previous);
                triangles.Add(current);
                triangles.Add(next);
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }

            if (!clipped)
                return [];
        }

        if (remaining.Count == 3)
        {
            triangles.Add(remaining[0]);
            triangles.Add(remaining[1]);
            triangles.Add(remaining[2]);
        }

        return triangles;
    }

    private static List<ThreeDPoint> CleanPolygon(IReadOnlyList<ThreeDPoint> points, double toleranceFeet)
    {
        var cleaned = new List<ThreeDPoint>();
        foreach (ThreeDPoint point in points)
        {
            if (cleaned.Count == 0 || Distance(cleaned[^1], point) > toleranceFeet)
            {
                cleaned.Add(new ThreeDPoint
                {
                    XFeet = point.XFeet,
                    ZFeet = point.ZFeet,
                });
            }
        }

        while (cleaned.Count > 2 && Distance(cleaned[0], cleaned[^1]) <= toleranceFeet)
            cleaned.RemoveAt(cleaned.Count - 1);

        return cleaned;
    }

    private static bool ContainsOtherPoint(
        IReadOnlyList<ThreeDPoint> points,
        IReadOnlyList<int> remaining,
        int a,
        int b,
        int c)
    {
        foreach (int candidate in remaining)
        {
            if (candidate == a || candidate == b || candidate == c)
                continue;

            if (PointInTriangle(points[candidate], points[a], points[b], points[c]))
                return true;
        }

        return false;
    }

    private static bool PointInTriangle(ThreeDPoint p, ThreeDPoint a, ThreeDPoint b, ThreeDPoint c)
    {
        double ab = Cross(a, b, p);
        double bc = Cross(b, c, p);
        double ca = Cross(c, a, p);
        return ab >= -Epsilon && bc >= -Epsilon && ca >= -Epsilon;
    }

    private static bool HasSelfIntersections(IReadOnlyList<ThreeDPoint> points, double toleranceFeet)
    {
        for (int i = 0; i < points.Count; i++)
        {
            int iNext = (i + 1) % points.Count;
            for (int j = i + 1; j < points.Count; j++)
            {
                int jNext = (j + 1) % points.Count;
                if (i == j || iNext == j || jNext == i)
                    continue;

                if (i == 0 && jNext == points.Count - 1)
                    continue;

                if (SegmentsIntersect(points[i], points[iNext], points[j], points[jNext], toleranceFeet))
                    return true;
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(
        ThreeDPoint a,
        ThreeDPoint b,
        ThreeDPoint c,
        ThreeDPoint d,
        double toleranceFeet)
    {
        double c1 = Cross(a, b, c);
        double c2 = Cross(a, b, d);
        double c3 = Cross(c, d, a);
        double c4 = Cross(c, d, b);

        if (Math.Abs(c1) <= toleranceFeet && OnSegment(a, c, b, toleranceFeet)) return true;
        if (Math.Abs(c2) <= toleranceFeet && OnSegment(a, d, b, toleranceFeet)) return true;
        if (Math.Abs(c3) <= toleranceFeet && OnSegment(c, a, d, toleranceFeet)) return true;
        if (Math.Abs(c4) <= toleranceFeet && OnSegment(c, b, d, toleranceFeet)) return true;

        return (c1 > 0) != (c2 > 0) && (c3 > 0) != (c4 > 0);
    }

    private static bool OnSegment(ThreeDPoint a, ThreeDPoint p, ThreeDPoint b, double toleranceFeet) =>
        p.XFeet >= Math.Min(a.XFeet, b.XFeet) - toleranceFeet &&
        p.XFeet <= Math.Max(a.XFeet, b.XFeet) + toleranceFeet &&
        p.ZFeet >= Math.Min(a.ZFeet, b.ZFeet) - toleranceFeet &&
        p.ZFeet <= Math.Max(a.ZFeet, b.ZFeet) + toleranceFeet;

    private static bool IsConvex(ThreeDPoint a, ThreeDPoint b, ThreeDPoint c) =>
        Cross(a, b, c) > Epsilon;

    private static double SignedArea(IReadOnlyList<ThreeDPoint> points)
    {
        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            ThreeDPoint a = points[i];
            ThreeDPoint b = points[(i + 1) % points.Count];
            area += a.XFeet * b.ZFeet - b.XFeet * a.ZFeet;
        }

        return area / 2.0;
    }

    private static double Cross(ThreeDPoint a, ThreeDPoint b, ThreeDPoint c) =>
        (b.XFeet - a.XFeet) * (c.ZFeet - a.ZFeet) -
        (b.ZFeet - a.ZFeet) * (c.XFeet - a.XFeet);

    private static double Distance(ThreeDPoint a, ThreeDPoint b)
    {
        double dx = a.XFeet - b.XFeet;
        double dz = a.ZFeet - b.ZFeet;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private static ThreeDPolygonTriangulation Failure(
        int originalPointCount,
        List<ThreeDPoint> cleanedPoints,
        string message) =>
        new()
        {
            Success = false,
            Points = cleanedPoints,
            Message = message,
            OriginalPointCount = originalPointCount,
        };
}
