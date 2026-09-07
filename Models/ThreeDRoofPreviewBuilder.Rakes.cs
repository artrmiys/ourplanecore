namespace OurPlanCore;

public static partial class ThreeDRoofPreviewBuilder
{
    private const double RakeFaceMinHeightFeet = 0.12;
    private const double RakeProfileTolerance = 0.0001;
    private const double RakeBoundaryToleranceFeet = 0.18;

    private static void AddRakeEndFaces(
        ThreeDRoofPreviewBuildResult result,
        RoofBoundary boundary,
        IReadOnlyList<ThreeDRoofGuide> boundaryGuides,
        IReadOnlyList<SlopePlane> slopePlanes,
        double roofBase)
    {
        if (slopePlanes.Count == 0)
            return;

        List<P2> footprint = EnsureCounterClockwise(boundary.Points.Select(ToP2).ToList());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int count = 0;
        foreach (ThreeDRoofGuide guide in boundaryGuides.Where(IsRakeGuide))
        {
            for (int i = 1; i < guide.Points.Count; i++)
            {
                P2 start = new(guide.Points[i - 1].XFeet, guide.Points[i - 1].ZFeet);
                P2 end = new(guide.Points[i].XFeet, guide.Points[i].ZFeet);
                if (Distance(start, end) < 0.25 || !IsBoundarySegment(start, end, footprint))
                    continue;

                string key = SegmentKey(new Segment(start, end));
                if (!seen.Add(key))
                    continue;

                if (TryCreateRakeEndFace(guide, start, end, slopePlanes, roofBase, ++count, out ThreeDRoofPlane? plane) &&
                    plane != null)
                {
                    result.Planes.Add(plane);
                }
                else
                {
                    count--;
                }
            }
        }

        if (count > 0)
            result.Messages.Add($"Rake/gable closure generated for {count} rake edge(s).");
    }

    private static bool IsRakeGuide(ThreeDRoofGuide guide) =>
        ThreeDRoofGuideKinds.Normalize(guide.Kind) == ThreeDRoofGuideKinds.Rake &&
        !string.Equals(guide.Status, GeneratedSeamStatus, StringComparison.OrdinalIgnoreCase);

    private static bool IsBoundarySegment(P2 start, P2 end, IReadOnlyList<P2> footprint)
    {
        P2 mid = new((start.X + end.X) / 2.0, (start.Z + end.Z) / 2.0);
        return DistanceToPolygon(start, footprint) <= RakeBoundaryToleranceFeet &&
               DistanceToPolygon(mid, footprint) <= RakeBoundaryToleranceFeet &&
               DistanceToPolygon(end, footprint) <= RakeBoundaryToleranceFeet;
    }

    private static bool TryCreateRakeEndFace(
        ThreeDRoofGuide guide,
        P2 start,
        P2 end,
        IReadOnlyList<SlopePlane> slopePlanes,
        double roofBase,
        int index,
        out ThreeDRoofPlane? plane)
    {
        plane = null;
        List<RakeProfilePoint> profile = BuildRakeProfile(start, end, slopePlanes);
        if (profile.Count < 2 || profile.Max(point => point.HeightFeet) < RakeFaceMinHeightFeet)
            return false;

        List<ThreeDRoofVertex> vertices = TryCreateSingleRakeTriangle(profile, roofBase, out List<ThreeDRoofVertex>? triangle)
            ? triangle!
            : CreateRakeProfileFace(profile, roofBase);
        vertices = CleanRakeVertices(vertices);
        if (vertices.Count < 3)
            return false;

        bool triangleFace = vertices.Count == 3;
        plane = new ThreeDRoofPlane
        {
            Kind = triangleFace ? "roof_rake_triangle" : "roof_rake_face",
            Label = triangleFace ? $"Rake triangle {index}" : $"Rake face {index}",
            Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Rake),
            Opacity = 0.5,
            Status = "generated_rake_face",
            Message = "Generated vertical rake/gable closure from the lower roof envelope.",
            Points = vertices,
            SourceGuideIds = [guide.Id],
        };
        return true;
    }

    private static List<RakeProfilePoint> BuildRakeProfile(P2 start, P2 end, IReadOnlyList<SlopePlane> slopePlanes)
    {
        List<double> parameters = [0, 1];
        for (int i = 0; i < slopePlanes.Count; i++)
        for (int j = i + 1; j < slopePlanes.Count; j++)
        {
            double startDelta = PlaneDelta(slopePlanes[i], slopePlanes[j], start);
            double endDelta = PlaneDelta(slopePlanes[i], slopePlanes[j], end);
            double denom = startDelta - endDelta;
            if (Math.Abs(denom) <= 0.000001)
                continue;

            double t = startDelta / denom;
            if (t > RakeProfileTolerance && t < 1 - RakeProfileTolerance)
                parameters.Add(t);
        }

        return parameters
            .OrderBy(value => value)
            .DistinctByTolerance(RakeProfileTolerance)
            .Select(t =>
            {
                P2 point = Lerp(start, end, t);
                return new RakeProfilePoint(point, Math.Max(0, slopePlanes.Min(plane => plane.HeightAt(point))));
            })
            .ToList();
    }

    private static bool TryCreateSingleRakeTriangle(
        IReadOnlyList<RakeProfilePoint> profile,
        double roofBase,
        out List<ThreeDRoofVertex>? vertices)
    {
        vertices = null;
        if (profile.Count != 3 ||
            profile[0].HeightFeet > RakeFaceMinHeightFeet ||
            profile[^1].HeightFeet > RakeFaceMinHeightFeet ||
            profile[1].HeightFeet < RakeFaceMinHeightFeet)
        {
            return false;
        }

        vertices =
        [
            Vertex(profile[0].Point.X, roofBase, profile[0].Point.Z),
            Vertex(profile[^1].Point.X, roofBase, profile[^1].Point.Z),
            Vertex(profile[1].Point.X, roofBase + profile[1].HeightFeet, profile[1].Point.Z),
        ];
        return true;
    }

    private static List<ThreeDRoofVertex> CreateRakeProfileFace(
        IReadOnlyList<RakeProfilePoint> profile,
        double roofBase)
    {
        var vertices = new List<ThreeDRoofVertex>
        {
            Vertex(profile[0].Point.X, roofBase, profile[0].Point.Z),
            Vertex(profile[^1].Point.X, roofBase, profile[^1].Point.Z),
        };

        for (int i = profile.Count - 1; i >= 0; i--)
        {
            RakeProfilePoint point = profile[i];
            if (point.HeightFeet > RakeFaceMinHeightFeet)
                vertices.Add(Vertex(point.Point.X, roofBase + point.HeightFeet, point.Point.Z));
        }

        return vertices;
    }

    private static List<ThreeDRoofVertex> CleanRakeVertices(IEnumerable<ThreeDRoofVertex> vertices)
    {
        var clean = new List<ThreeDRoofVertex>();
        foreach (ThreeDRoofVertex vertex in vertices)
        {
            if (clean.Count == 0 || Distance3D(clean[^1], vertex) > 0.03)
                clean.Add(vertex);
        }

        if (clean.Count > 1 && Distance3D(clean[0], clean[^1]) <= 0.03)
            clean.RemoveAt(clean.Count - 1);
        return clean;
    }

    private static P2 Lerp(P2 start, P2 end, double t) =>
        new(start.X + (end.X - start.X) * t, start.Z + (end.Z - start.Z) * t);

    private static double Distance3D(ThreeDRoofVertex a, ThreeDRoofVertex b)
    {
        double dx = b.XFeet - a.XFeet;
        double dy = b.YFeet - a.YFeet;
        double dz = b.ZFeet - a.ZFeet;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private readonly record struct RakeProfilePoint(P2 Point, double HeightFeet);
}

file static class ThreeDRoofRakeEnumerableExtensions
{
    public static IEnumerable<double> DistinctByTolerance(this IEnumerable<double> values, double tolerance)
    {
        double? previous = null;
        foreach (double value in values)
        {
            if (previous == null || Math.Abs(value - previous.Value) > tolerance)
            {
                previous = value;
                yield return value;
            }
        }
    }
}
