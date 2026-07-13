namespace OurPlanCore;

public static partial class ThreeDRoofPreviewBuilder
{
    private static bool AreSamePlane(SlopePlane a, SlopePlane b) =>
        Math.Abs(a.A - b.A) <= 0.00001 &&
        Math.Abs(a.B - b.B) <= 0.00001 &&
        Math.Abs(a.C - b.C) <= 0.0001;

    private static void OrientEdgeToFootprintInterior(IReadOnlyList<P2> footprint, ref P2 start, ref P2 end)
    {
        double dx = end.X - start.X;
        double dz = end.Z - start.Z;
        double len = Math.Sqrt(dx * dx + dz * dz);
        if (len <= 0.000001)
            return;

        (double minX, double maxX, double minZ, double maxZ) = Bounds(footprint);
        double probe = Math.Max(0.1, Math.Max(maxX - minX, maxZ - minZ) * 0.002);
        P2 mid = new((start.X + end.X) / 2.0, (start.Z + end.Z) / 2.0);
        P2 left = new(mid.X - dz / len * probe, mid.Z + dx / len * probe);
        if (!PointInPolygon(left, footprint))
            (start, end) = (end, start);
    }

    private static List<P2> ClipToLowerPlane(
        IReadOnlyList<P2> polygon,
        SlopePlane plane,
        SlopePlane other)
    {
        return ClipPolygon(
            polygon,
            point => PlaneDelta(plane, other, point) <= FaceClipTolerance,
            (a, b) => IntersectPlaneBoundary(a, b, plane, other));
    }

    private static List<P2> ClipPolygon(
        IReadOnlyList<P2> polygon,
        Func<P2, bool> inside,
        Func<P2, P2, P2> intersection)
    {
        var output = new List<P2>();
        if (polygon.Count == 0)
            return output;

        P2 previous = polygon[^1];
        bool previousInside = inside(previous);
        foreach (P2 current in polygon)
        {
            bool currentInside = inside(current);
            if (currentInside)
            {
                if (!previousInside)
                    output.Add(intersection(previous, current));
                output.Add(current);
            }
            else if (previousInside)
            {
                output.Add(intersection(previous, current));
            }

            previous = current;
            previousInside = currentInside;
        }

        return output;
    }

    private static double PlaneDelta(SlopePlane plane, SlopePlane other, P2 point) =>
        plane.HeightAt(point) - other.HeightAt(point);

    private static P2 IntersectPlaneBoundary(P2 a, P2 b, SlopePlane plane, SlopePlane other)
    {
        double da = PlaneDelta(plane, other, a);
        double db = PlaneDelta(plane, other, b);
        double denom = da - db;
        if (Math.Abs(denom) <= 0.000001)
            return a;

        double t = Math.Clamp(da / denom, 0, 1);
        double x = a.X + (b.X - a.X) * t;
        double z = a.Z + (b.Z - a.Z) * t;
        return new P2(x, z);
    }

    private static List<P2> CleanPolygon(IReadOnlyList<P2> polygon)
    {
        var clean = new List<P2>();
        foreach (P2 point in polygon)
        {
            if (clean.Count == 0 || Distance(clean[^1], point) > 0.03)
                clean.Add(point);
        }

        if (clean.Count > 1 && Distance(clean[0], clean[^1]) <= 0.03)
            clean.RemoveAt(clean.Count - 1);

        return clean;
    }

    private static bool TryResolveBoundary(ThreeDWallModel model, out RoofBoundary boundary)
    {
        boundary = default;
        if (!ThreeDRoofFootprintResolver.TryResolve(model, out ThreeDRoofFootprint footprint))
            return false;

        boundary = new RoofBoundary(
            footprint.Points,
            footprint.ElevationFeet,
            footprint.LevelKey,
            footprint.IsFallbackBounds);
        return true;
    }

    private static IReadOnlyList<RoofBoundary> ResolveRoofBoundaries(ThreeDWallModel model)
    {
        List<RoofBoundary> roofSlabs = model.Slabs
            .Where(slab => string.Equals(slab.LevelKey, "roof", StringComparison.OrdinalIgnoreCase))
            .Where(slab => slab.Points.Count >= 3)
            .Select(slab => new RoofBoundary(
                slab.Points.Select(ClonePoint).ToList(),
                slab.ElevationFeet,
                slab.LevelKey,
                false))
            .ToList();
        if (roofSlabs.Count > 0)
            return roofSlabs;

        return TryResolveBoundary(model, out RoofBoundary boundary)
            ? [boundary]
            : [];
    }

    private static bool GuideBelongsToBoundary(ThreeDRoofGuide guide, RoofBoundary boundary)
    {
        List<P2> footprint = EnsureCounterClockwise(boundary.Points.Select(ToP2).ToList());
        for (int i = 1; i < guide.Points.Count; i++)
        {
            P2 a = new(guide.Points[i - 1].XFeet, guide.Points[i - 1].ZFeet);
            P2 b = new(guide.Points[i].XFeet, guide.Points[i].ZFeet);
            P2 mid = new((a.X + b.X) / 2.0, (a.Z + b.Z) / 2.0);
            if (PointInPolygon(mid, footprint) || DistanceToPolygon(mid, footprint) <= ThreeDRoofFootprintResolver.EndpointToleranceFeet)
                return true;
        }

        return false;
    }

    private static double ResolveRoofBaseElevation(ThreeDWallModel model, RoofBoundary boundary)
    {
        double levelTop = model.Levels
            .Where(level => string.IsNullOrWhiteSpace(boundary.LevelKey) ||
                            string.Equals(level.Label, boundary.LevelKey, StringComparison.OrdinalIgnoreCase))
            .Select(level => level.BaseElevationFeet + level.HeightFeet)
            .DefaultIfEmpty(0)
            .Max();

        double wallTop = model.Walls
            .Where(wall => string.IsNullOrWhiteSpace(boundary.LevelKey) ||
                           string.Equals(wall.LevelKey, boundary.LevelKey, StringComparison.OrdinalIgnoreCase))
            .Select(wall => wall.BaseElevationFeet + wall.HeightFeet)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Max(boundary.ElevationFeet, Math.Max(levelTop, wallTop));
    }

    private static (double MinX, double MaxX, double MinZ, double MaxZ) Bounds(IReadOnlyList<ThreeDPoint> points) =>
        (
            points.Min(point => point.XFeet),
            points.Max(point => point.XFeet),
            points.Min(point => point.ZFeet),
            points.Max(point => point.ZFeet)
        );

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

    private static double SignedArea(IReadOnlyList<P2> points)
    {
        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            P2 a = points[i];
            P2 b = points[(i + 1) % points.Count];
            area += a.X * b.Z - b.X * a.Z;
        }

        return area / 2.0;
    }

    private static List<P2> EnsureCounterClockwise(List<P2> points)
    {
        if (SignedArea(points) < 0)
            points.Reverse();
        return points;
    }

    private static bool PointInPolygon(P2 point, IReadOnlyList<P2> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            P2 a = polygon[i];
            P2 b = polygon[j];
            if (DistanceToSegment(point, a, b) <= 0.001)
                return true;

            bool crosses = a.Z > point.Z != b.Z > point.Z &&
                           point.X < (b.X - a.X) * (point.Z - a.Z) / ((b.Z - a.Z) + 0.0000001) + a.X;
            if (crosses)
                inside = !inside;
        }

        return inside;
    }

    private static double Distance(P2 a, P2 b)
    {
        double dx = b.X - a.X;
        double dz = b.Z - a.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private static double DistanceToLine(P2 point, P2 a, P2 b)
    {
        double dx = b.X - a.X;
        double dz = b.Z - a.Z;
        double len = Math.Sqrt(dx * dx + dz * dz);
        if (len <= 0.000001)
            return Distance(point, a);

        return Math.Abs(dx * (point.Z - a.Z) - dz * (point.X - a.X)) / len;
    }

    private static double DistanceToSegment(P2 point, P2 a, P2 b)
    {
        double dx = b.X - a.X;
        double dz = b.Z - a.Z;
        double len2 = dx * dx + dz * dz;
        if (len2 <= 0.000001)
            return Distance(point, a);

        double t = ((point.X - a.X) * dx + (point.Z - a.Z) * dz) / len2;
        t = Math.Clamp(t, 0, 1);
        return Distance(point, new P2(a.X + dx * t, a.Z + dz * t));
    }

    private static double DistanceToPolygon(P2 point, IReadOnlyList<P2> polygon)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i < polygon.Count; i++)
        {
            P2 a = polygon[i];
            P2 b = polygon[(i + 1) % polygon.Count];
            best = Math.Min(best, DistanceToSegment(point, a, b));
        }

        return best;
    }

    private static (double MinX, double MaxX, double MinZ, double MaxZ) Bounds(IReadOnlyList<P2> points) =>
        (
            points.Min(point => point.X),
            points.Max(point => point.X),
            points.Min(point => point.Z),
            points.Max(point => point.Z)
        );

    private static P2 ToP2(ThreeDPoint point) => new(point.XFeet, point.ZFeet);

    private static ThreeDPoint ClonePoint(ThreeDPoint point) =>
        new() { XFeet = point.XFeet, ZFeet = point.ZFeet };

    private static ThreeDRoofGuidePoint RoofPoint(P2 point, double feetPerPdf, double yFeet = 0)
    {
        double scale = feetPerPdf > 0 ? feetPerPdf : 1;
        return new ThreeDRoofGuidePoint
        {
            XFeet = point.X,
            YFeet = yFeet,
            ZFeet = point.Z,
            PdfX = point.X / scale,
            PdfY = point.Z / scale,
        };
    }

    private static ThreeDRoofVertex Vertex(double x, double y, double z) =>
        new() { XFeet = x, YFeet = y, ZFeet = z };

    private static double ResolveFeetPerPdf(ThreeDRoofGuide guide, P2 start, P2 end)
    {
        ThreeDRoofGuidePoint a = guide.Points[0];
        ThreeDRoofGuidePoint b = guide.Points[^1];
        double pdf = Distance(new P2(a.PdfX, a.PdfY), new P2(b.PdfX, b.PdfY));
        double feet = Distance(start, end);
        return pdf > 0.000001 ? feet / pdf : 1;
    }

    private static P2 Subtract(P2 a, P2 b) => new(a.X - b.X, a.Z - b.Z);

    private static double Dot(P2 a, P2 b) => a.X * b.X + a.Z * b.Z;

    private static double Cross(P2 a, P2 b) => a.X * b.Z - a.Z * b.X;

    private static string RoofFaceColor(int index)
    {
        string[] colors =
        [
            "#B45309",
            "#A16207",
            "#92400E",
            "#CA8A04",
            "#854D0E",
            "#D97706",
        ];
        return colors[index % colors.Length];
    }
}
