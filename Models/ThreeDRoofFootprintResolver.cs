namespace OurPlaneCore;

public sealed class ThreeDRoofFootprint
{
    public List<ThreeDPoint> Points { get; set; } = [];
    public double ElevationFeet { get; set; }
    public string LevelKey { get; set; } = "";
    public string Source { get; set; } = "";
    public bool IsFallbackBounds => string.Equals(Source, "bounds", StringComparison.OrdinalIgnoreCase);
}

public static class ThreeDRoofFootprintResolver
{
    public const double EndpointToleranceFeet = 0.75;

    public static bool TryResolve(ThreeDWallModel model, out ThreeDRoofFootprint footprint)
    {
        footprint = new ThreeDRoofFootprint();
        ThreeDFloorSlab? slab = model.Slabs
            .Where(candidate => candidate.Points.Count >= 3)
            .OrderByDescending(candidate => candidate.ElevationFeet)
            .ThenByDescending(candidate => Math.Abs(SignedArea(candidate.Points)))
            .FirstOrDefault();
        if (slab != null)
        {
            footprint = new ThreeDRoofFootprint
            {
                Points = slab.Points.Select(ClonePoint).ToList(),
                ElevationFeet = slab.ElevationFeet,
                LevelKey = slab.LevelKey,
                Source = "slab",
            };
            return true;
        }

        if (TryResolveClosedEaveLoop(model.RoofGuides, out List<ThreeDPoint> eaveLoop))
        {
            footprint = new ThreeDRoofFootprint
            {
                Points = eaveLoop,
                ElevationFeet = model.RoofGuides.Select(guide => guide.ElevationFeet).DefaultIfEmpty(0).Max(),
                LevelKey = model.RoofGuides.Select(guide => guide.LevelKey).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "",
                Source = "eave_loop",
            };
            return true;
        }

        var points = new List<ThreeDPoint>();
        foreach (ThreeDWallSegment wall in model.Walls)
        {
            points.Add(new ThreeDPoint { XFeet = wall.StartXFeet, ZFeet = wall.StartZFeet });
            points.Add(new ThreeDPoint { XFeet = wall.EndXFeet, ZFeet = wall.EndZFeet });
        }

        foreach (ThreeDRoofGuide guide in model.RoofGuides)
            points.AddRange(guide.Points.Select(ClonePoint));

        if (points.Count == 0)
            return false;

        double minX = points.Min(point => point.XFeet);
        double maxX = points.Max(point => point.XFeet);
        double minZ = points.Min(point => point.ZFeet);
        double maxZ = points.Max(point => point.ZFeet);
        double pad = Math.Max(2, Math.Max(maxX - minX, maxZ - minZ) * 0.08);
        footprint = new ThreeDRoofFootprint
        {
            Points =
            [
                new ThreeDPoint { XFeet = minX - pad, ZFeet = minZ - pad },
                new ThreeDPoint { XFeet = maxX + pad, ZFeet = minZ - pad },
                new ThreeDPoint { XFeet = maxX + pad, ZFeet = maxZ + pad },
                new ThreeDPoint { XFeet = minX - pad, ZFeet = maxZ + pad },
            ],
            Source = "bounds",
        };
        return true;
    }

    public static bool TryResolveClosedEaveLoop(
        IEnumerable<ThreeDRoofGuide> guides,
        out List<ThreeDPoint> loop)
    {
        var segments = guides
            .Where(guide => ThreeDRoofGuideKinds.Normalize(guide.Kind) == ThreeDRoofGuideKinds.Eave)
            .Where(guide => guide.Points.Count >= 2)
            .Select(guide => new Segment(
                ToP2(guide.Points[0]),
                ToP2(guide.Points[^1])))
            .Where(segment => Distance(segment.Start, segment.End) > 0.25)
            .ToList();

        loop = [];
        if (segments.Count < 3)
            return false;

        var ordered = new List<P2> { segments[0].Start, segments[0].End };
        var used = new bool[segments.Count];
        used[0] = true;
        bool closed = false;

        while (used.Any(value => !value))
        {
            P2 current = ordered[^1];
            int nextIndex = -1;
            P2 nextPoint = default;
            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i])
                    continue;

                if (Distance(current, segments[i].Start) <= EndpointToleranceFeet)
                {
                    nextIndex = i;
                    nextPoint = segments[i].End;
                    break;
                }

                if (Distance(current, segments[i].End) <= EndpointToleranceFeet)
                {
                    nextIndex = i;
                    nextPoint = segments[i].Start;
                    break;
                }
            }

            if (nextIndex < 0)
                return false;

            used[nextIndex] = true;
            if (Distance(nextPoint, ordered[0]) <= EndpointToleranceFeet && used.All(value => value))
            {
                closed = true;
                break;
            }

            ordered.Add(nextPoint);
        }

        if (ordered.Count < 3 || !closed)
            return false;

        loop = ordered
            .Select(point => new ThreeDPoint { XFeet = point.X, ZFeet = point.Z })
            .ToList();
        return Math.Abs(SignedArea(loop)) > 1.0;
    }

    private static ThreeDPoint ClonePoint(ThreeDPoint point) =>
        new() { XFeet = point.XFeet, ZFeet = point.ZFeet };

    private static ThreeDPoint ClonePoint(ThreeDRoofGuidePoint point) =>
        new() { XFeet = point.XFeet, ZFeet = point.ZFeet };

    private static P2 ToP2(ThreeDRoofGuidePoint point) => new(point.XFeet, point.ZFeet);

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

    private static double Distance(P2 a, P2 b)
    {
        double dx = b.X - a.X;
        double dz = b.Z - a.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private readonly record struct Segment(P2 Start, P2 End);

    private readonly record struct P2(double X, double Z);
}
