namespace OurPlaneCore;

public static partial class ThreeDRoofPreviewBuilder
{
    private const double ParallelRidgeLineTolerance = 0.001;
    private const double ParallelRidgeEndpointTolerance = 0.35;
    private const double ParallelRidgeMinimumLength = 0.25;

    private static void AddParallelEaveRidges(
        ThreeDRoofPreviewBuildResult result,
        IReadOnlyList<EnvelopeFace> faces,
        IReadOnlyList<P2> footprint)
    {
        if (faces.Count < 2)
            return;

        int ridge = result.Guides.Count(guide =>
            string.Equals(guide.Status, GeneratedSeamStatus, StringComparison.OrdinalIgnoreCase) &&
            ThreeDRoofGuideKinds.Normalize(guide.Kind) == ThreeDRoofGuideKinds.Ridge);

        for (int i = 0; i < faces.Count; i++)
        for (int j = i + 1; j < faces.Count; j++)
        {
            EnvelopeFace first = faces[i];
            EnvelopeFace second = faces[j];
            if (!SourceEdgesAreParallel(first.Plane, second.Plane) ||
                !SourceStripsOverlap(first.Plane, second.Plane) ||
                !SlopePlanesFaceEachOther(first.Plane, second.Plane))
            {
                continue;
            }

            foreach (Segment segment in EqualHeightSegments(first.Plane, second.Plane, footprint))
            {
                if (!TryClipSegmentToSourceOverlap(segment, first.Plane, second.Plane, out Segment ridgeSegment) ||
                    Distance(ridgeSegment.Start, ridgeSegment.End) < ParallelRidgeMinimumLength ||
                    !IsParallelRidgeOnEnvelope(first, second, ridgeSegment, faces) ||
                    HasEquivalentGeneratedRidge(result, ridgeSegment))
                {
                    continue;
                }

                P2 mid = new(
                    (ridgeSegment.Start.X + ridgeSegment.End.X) / 2.0,
                    (ridgeSegment.Start.Z + ridgeSegment.End.Z) / 2.0);
                double y = first.RoofBase + Math.Max(0, first.Plane.HeightAt(mid));
                double startY = first.RoofBase + Math.Max(0, first.Plane.HeightAt(ridgeSegment.Start));
                double endY = first.RoofBase + Math.Max(0, first.Plane.HeightAt(ridgeSegment.End));
                if (!HasMeaningfulGeneratedRise(first.RoofBase, startY, y, endY))
                    continue;

                result.Guides.Add(new ThreeDRoofGuide
                {
                    Kind = ThreeDRoofGuideKinds.Ridge,
                    Label = $"Ridge {++ridge}",
                    PageFolder = string.IsNullOrWhiteSpace(first.Plane.PageFolder)
                        ? second.Plane.PageFolder
                        : first.Plane.PageFolder,
                    LevelKey = "roof",
                    ElevationFeet = y,
                    PitchRisePerFoot = 0,
                    Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Ridge),
                    Status = GeneratedSeamStatus,
                    AdjustmentStatus = "generated",
                    AdjustmentMessage = "Generated at equal height between opposing parallel eave pitch planes.",
                    Points =
                    [
                        RoofPoint(ridgeSegment.Start, first.Plane.FeetPerPdf, startY),
                        RoofPoint(ridgeSegment.End, first.Plane.FeetPerPdf, endY),
                    ],
                    RawPoints =
                    [
                        RoofPoint(ridgeSegment.Start, first.Plane.FeetPerPdf, startY),
                        RoofPoint(ridgeSegment.End, first.Plane.FeetPerPdf, endY),
                    ],
                });
            }
        }
    }

    private static bool SlopePlanesFaceEachOther(SlopePlane first, SlopePlane second)
    {
        double firstLength = Math.Sqrt(first.A * first.A + first.B * first.B);
        double secondLength = Math.Sqrt(second.A * second.A + second.B * second.B);
        if (firstLength <= 0.000001 || secondLength <= 0.000001)
            return false;

        double dot = (first.A * second.A + first.B * second.B) / (firstLength * secondLength);
        return dot < -0.25;
    }

    private static IReadOnlyList<Segment> EqualHeightSegments(
        SlopePlane first,
        SlopePlane second,
        IReadOnlyList<P2> footprint)
    {
        double a = first.A - second.A;
        double b = first.B - second.B;
        double c = first.C - second.C;
        if (Math.Sqrt(a * a + b * b) <= 0.000001)
            return [];

        var cuts = new List<P2>();
        for (int i = 0; i < footprint.Count; i++)
        {
            P2 start = footprint[i];
            P2 end = footprint[(i + 1) % footprint.Count];
            double startValue = LineValue(a, b, c, start);
            double endValue = LineValue(a, b, c, end);
            bool startOnLine = Math.Abs(startValue) <= ParallelRidgeLineTolerance;
            bool endOnLine = Math.Abs(endValue) <= ParallelRidgeLineTolerance;

            if (startOnLine)
                AddDistinctPoint(cuts, start);
            if (endOnLine)
                AddDistinctPoint(cuts, end);
            if (startOnLine && endOnLine)
                continue;
            if (startValue < 0 == endValue < 0)
                continue;

            double denominator = startValue - endValue;
            if (Math.Abs(denominator) <= 0.000001)
                continue;

            double t = Math.Clamp(startValue / denominator, 0, 1);
            AddDistinctPoint(
                cuts,
                new P2(
                    start.X + (end.X - start.X) * t,
                    start.Z + (end.Z - start.Z) * t));
        }

        if (cuts.Count < 2)
            return [];

        P2 direction = Normalize(new P2(-b, a));
        cuts = cuts
            .OrderBy(point => Dot(point, direction))
            .ToList();

        var segments = new List<Segment>();
        for (int i = 1; i < cuts.Count; i++)
        {
            P2 start = cuts[i - 1];
            P2 end = cuts[i];
            if (Distance(start, end) < ParallelRidgeMinimumLength)
                continue;

            P2 mid = new((start.X + end.X) / 2.0, (start.Z + end.Z) / 2.0);
            if (PointInPolygon(mid, footprint))
                segments.Add(new Segment(start, end));
        }

        return segments;
    }

    private static double LineValue(double a, double b, double c, P2 point) =>
        a * point.X + b * point.Z + c;

    private static void AddDistinctPoint(List<P2> points, P2 point)
    {
        if (points.Any(existing => Distance(existing, point) <= ParallelRidgeEndpointTolerance * 0.1))
            return;

        points.Add(point);
    }

    private static bool TryClipSegmentToSourceOverlap(
        Segment segment,
        SlopePlane first,
        SlopePlane second,
        out Segment clipped)
    {
        clipped = default;
        P2 along = UnitAlong(first);
        double firstLength = Distance(first.Start, first.End);
        if (firstLength <= 0.000001)
            return false;

        double secondStart = Dot(Subtract(second.Start, first.Start), along);
        double secondEnd = Dot(Subtract(second.End, first.Start), along);
        double overlapStart = Math.Max(0, Math.Min(secondStart, secondEnd));
        double overlapEnd = Math.Min(firstLength, Math.Max(secondStart, secondEnd));
        if (overlapEnd - overlapStart < ParallelRidgeMinimumLength)
            return false;

        double segmentStart = Dot(Subtract(segment.Start, first.Start), along);
        double segmentEnd = Dot(Subtract(segment.End, first.Start), along);
        double clippedStart = Math.Max(Math.Min(segmentStart, segmentEnd), overlapStart);
        double clippedEnd = Math.Min(Math.Max(segmentStart, segmentEnd), overlapEnd);
        if (clippedEnd - clippedStart < ParallelRidgeMinimumLength)
            return false;

        P2 start = PointAtProjectedDistance(segment, first.Start, along, clippedStart);
        P2 end = PointAtProjectedDistance(segment, first.Start, along, clippedEnd);
        clipped = new Segment(start, end);
        return true;
    }

    private static P2 PointAtProjectedDistance(Segment segment, P2 origin, P2 along, double distance)
    {
        double start = Dot(Subtract(segment.Start, origin), along);
        double end = Dot(Subtract(segment.End, origin), along);
        double denominator = end - start;
        if (Math.Abs(denominator) <= 0.000001)
            return segment.Start;

        double t = Math.Clamp((distance - start) / denominator, 0, 1);
        return new P2(
            segment.Start.X + (segment.End.X - segment.Start.X) * t,
            segment.Start.Z + (segment.End.Z - segment.Start.Z) * t);
    }

    private static bool IsParallelRidgeOnEnvelope(
        EnvelopeFace first,
        EnvelopeFace second,
        Segment segment,
        IReadOnlyList<EnvelopeFace> faces)
    {
        P2[] samples =
        [
            segment.Start,
            new((segment.Start.X + segment.End.X) / 2.0, (segment.Start.Z + segment.End.Z) / 2.0),
            segment.End,
        ];

        foreach (P2 sample in samples)
        {
            if (!PointInPolygon(sample, first.Points) || !PointInPolygon(sample, second.Points))
                return false;

            double height = first.Plane.HeightAt(sample);
            if (Math.Abs(height - second.Plane.HeightAt(sample)) > 0.05)
                return false;

            foreach (EnvelopeFace face in faces)
            {
                if (ReferenceEquals(face, first) || ReferenceEquals(face, second))
                    continue;
                if (PointInPolygon(sample, face.Points) && face.Plane.HeightAt(sample) < height - 0.05)
                    return false;
            }
        }

        return true;
    }

    private static bool HasEquivalentGeneratedRidge(ThreeDRoofPreviewBuildResult result, Segment segment)
    {
        foreach (ThreeDRoofGuide guide in result.Guides)
        {
            if (!string.Equals(guide.Status, GeneratedSeamStatus, StringComparison.OrdinalIgnoreCase) ||
                ThreeDRoofGuideKinds.Normalize(guide.Kind) != ThreeDRoofGuideKinds.Ridge ||
                guide.Points.Count < 2)
            {
                continue;
            }

            var existing = new Segment(
                new P2(guide.Points[0].XFeet, guide.Points[0].ZFeet),
                new P2(guide.Points[^1].XFeet, guide.Points[^1].ZFeet));
            if (EquivalentSegments(existing, segment))
                return true;
        }

        return false;
    }

    private static bool EquivalentSegments(Segment existing, Segment candidate)
    {
        if (Distance(existing.Start, candidate.Start) <= ParallelRidgeEndpointTolerance &&
            Distance(existing.End, candidate.End) <= ParallelRidgeEndpointTolerance)
        {
            return true;
        }

        if (Distance(existing.Start, candidate.End) <= ParallelRidgeEndpointTolerance &&
            Distance(existing.End, candidate.Start) <= ParallelRidgeEndpointTolerance)
        {
            return true;
        }

        double existingLength = Distance(existing.Start, existing.End);
        double candidateLength = Distance(candidate.Start, candidate.End);
        if (existingLength <= 0.000001 || candidateLength <= 0.000001)
            return false;

        if (DistanceToLine(candidate.Start, existing.Start, existing.End) > ParallelRidgeEndpointTolerance ||
            DistanceToLine(candidate.End, existing.Start, existing.End) > ParallelRidgeEndpointTolerance)
        {
            return false;
        }

        P2 along = Normalize(Subtract(existing.End, existing.Start));
        double overlap = IntervalOverlapLength(
            0,
            existingLength,
            Dot(Subtract(candidate.Start, existing.Start), along),
            Dot(Subtract(candidate.End, existing.Start), along));
        return overlap >= Math.Min(existingLength, candidateLength) - ParallelRidgeEndpointTolerance;
    }
}
