namespace OurPlaneCore;

public static partial class ThreeDRoofPreviewBuilder
{
    private static void AddConcaveCornerValleys(
        ThreeDRoofPreviewBuildResult result,
        IReadOnlyList<EnvelopeFace> faces,
        IReadOnlyList<P2> footprint)
    {
        if (faces.Count < 2 || HasGeneratedValley(result))
            return;

        int valley = result.Guides.Count(guide => ThreeDRoofGuideKinds.Normalize(guide.Kind) == ThreeDRoofGuideKinds.Valley);
        for (int i = 0; i < footprint.Count; i++)
        {
            P2 previous = footprint[(i - 1 + footprint.Count) % footprint.Count];
            P2 vertex = footprint[i];
            P2 next = footprint[(i + 1) % footprint.Count];
            if (!IsConcaveVertex(previous, vertex, next))
                continue;

            P2 direction = ConcaveValleyDirection(previous, vertex, next, footprint);
            double length = ConcaveValleyLength(vertex, direction, footprint);
            if (length < 0.25)
                continue;

            P2 end = new(vertex.X + direction.X * length, vertex.Z + direction.Z * length);
            P2 mid = new((vertex.X + end.X) / 2.0, (vertex.Z + end.Z) / 2.0);
            double startY = EnvelopeHeightAt(faces, vertex);
            double endY = EnvelopeHeightAt(faces, end);
            result.Guides.Add(new ThreeDRoofGuide
            {
                Kind = ThreeDRoofGuideKinds.Valley,
                Label = $"Valley {++valley}",
                PageFolder = faces.Select(face => face.Plane.PageFolder).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "",
                LevelKey = "roof",
                ElevationFeet = EnvelopeHeightAt(faces, mid),
                PitchRisePerFoot = 0,
                Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Valley),
                Status = GeneratedSeamStatus,
                AdjustmentStatus = "generated",
                AdjustmentMessage = "Generated from a concave roof footprint corner.",
                Points =
                [
                    RoofPoint(vertex, faces[0].Plane.FeetPerPdf, startY),
                    RoofPoint(end, faces[0].Plane.FeetPerPdf, endY),
                ],
                RawPoints =
                [
                    RoofPoint(vertex, faces[0].Plane.FeetPerPdf, startY),
                    RoofPoint(end, faces[0].Plane.FeetPerPdf, endY),
                ],
            });
        }
    }

    private static bool HasGeneratedValley(ThreeDRoofPreviewBuildResult result) =>
        result.Guides.Any(guide =>
            string.Equals(guide.Status, GeneratedSeamStatus, StringComparison.OrdinalIgnoreCase) &&
            ThreeDRoofGuideKinds.Normalize(guide.Kind) == ThreeDRoofGuideKinds.Valley);

    private static bool IsConcaveVertex(P2 previous, P2 vertex, P2 next) =>
        Cross(Subtract(vertex, previous), Subtract(next, vertex)) < -0.001;

    private static P2 ConcaveValleyDirection(P2 previous, P2 vertex, P2 next, IReadOnlyList<P2> footprint)
    {
        P2 toPrevious = Normalize(Subtract(previous, vertex));
        P2 toNext = Normalize(Subtract(next, vertex));
        P2 direction = Normalize(new P2(-(toPrevious.X + toNext.X), -(toPrevious.Z + toNext.Z)));
        if (Length(direction) <= 0.000001)
            direction = Normalize(new P2(-(toPrevious.Z + toNext.Z), toPrevious.X + toNext.X));

        P2 probe = new(vertex.X + direction.X * 0.2, vertex.Z + direction.Z * 0.2);
        return PointInPolygon(probe, footprint)
            ? direction
            : new P2(-direction.X, -direction.Z);
    }

    private static double ConcaveValleyLength(P2 vertex, P2 direction, IReadOnlyList<P2> footprint)
    {
        (double minX, double maxX, double minZ, double maxZ) = Bounds(footprint);
        double maxLength = Math.Max(maxX - minX, maxZ - minZ) * 0.45;
        double low = 0;
        double high = Math.Max(0.5, maxLength);
        for (int i = 0; i < 24; i++)
        {
            double mid = (low + high) / 2.0;
            P2 point = new(vertex.X + direction.X * mid, vertex.Z + direction.Z * mid);
            if (PointInPolygon(point, footprint))
                low = mid;
            else
                high = mid;
        }

        return low;
    }

    private static double EnvelopeHeightAt(IReadOnlyList<EnvelopeFace> faces, P2 point)
    {
        double best = double.PositiveInfinity;
        foreach (EnvelopeFace face in faces)
            best = Math.Min(best, face.RoofBase + Math.Max(0, face.Plane.HeightAt(point)));

        return double.IsFinite(best) ? best : 0;
    }

    private static List<P2> ClipToSourceStrip(IReadOnlyList<P2> polygon, SlopePlane plane)
    {
        P2 edge = Subtract(plane.End, plane.Start);
        double len = Length(edge);
        if (len <= 0.000001)
            return polygon.ToList();

        P2 along = new(edge.X / len, edge.Z / len);
        double tolerance = Math.Clamp(len * 0.015, 0.15, 1.5);
        List<P2> clipped = ClipPolygon(
            polygon,
            point => Dot(Subtract(point, plane.Start), along) >= -tolerance,
            (a, b) => IntersectProjectedStripBoundary(a, b, plane.Start, along, -tolerance));
        return CleanPolygon(ClipPolygon(
            clipped,
            point => Dot(Subtract(point, plane.End), along) <= tolerance,
            (a, b) => IntersectProjectedStripBoundary(a, b, plane.End, along, tolerance)));
    }

    private static P2 IntersectProjectedStripBoundary(P2 a, P2 b, P2 anchor, P2 along, double limit)
    {
        double da = Dot(Subtract(a, anchor), along) - limit;
        double db = Dot(Subtract(b, anchor), along) - limit;
        double denom = da - db;
        if (Math.Abs(denom) <= 0.000001)
            return a;

        double t = Math.Clamp(da / denom, 0, 1);
        return new P2(
            a.X + (b.X - a.X) * t,
            a.Z + (b.Z - a.Z) * t);
    }

    private static bool SourceStripsOverlap(SlopePlane first, SlopePlane second)
    {
        P2 firstAlong = UnitAlong(first);
        P2 secondAlong = UnitAlong(second);
        double firstLen = Distance(first.Start, first.End);
        double secondLen = Distance(second.Start, second.End);
        if (firstLen <= 0.000001 || secondLen <= 0.000001)
            return true;

        double parallel = Math.Abs(Cross(firstAlong, secondAlong));
        if (parallel > 0.001)
            return true;

        double overlapOnFirst = IntervalOverlapLength(
            0,
            firstLen,
            Dot(Subtract(second.Start, first.Start), firstAlong),
            Dot(Subtract(second.End, first.Start), firstAlong));
        double overlapOnSecond = IntervalOverlapLength(
            0,
            secondLen,
            Dot(Subtract(first.Start, second.Start), secondAlong),
            Dot(Subtract(first.End, second.Start), secondAlong));
        double tolerance = Math.Min(Math.Clamp(firstLen * 0.015, 0.15, 1.5), Math.Clamp(secondLen * 0.015, 0.15, 1.5));
        return overlapOnFirst > tolerance && overlapOnSecond > tolerance;
    }

    private static double IntervalOverlapLength(double a0, double a1, double b0, double b1)
    {
        double left = Math.Max(Math.Min(a0, a1), Math.Min(b0, b1));
        double right = Math.Min(Math.Max(a0, a1), Math.Max(b0, b1));
        return Math.Max(0, right - left);
    }

    private static P2 UnitAlong(SlopePlane plane)
    {
        P2 edge = Subtract(plane.End, plane.Start);
        double len = Length(edge);
        return len <= 0.000001 ? new P2(1, 0) : new P2(edge.X / len, edge.Z / len);
    }

    private static double Length(P2 point) => Math.Sqrt(point.X * point.X + point.Z * point.Z);

    private static P2 Normalize(P2 point)
    {
        double length = Length(point);
        return length <= 0.000001 ? new P2(0, 0) : new P2(point.X / length, point.Z / length);
    }
}
