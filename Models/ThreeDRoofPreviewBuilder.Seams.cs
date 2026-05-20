namespace OurPlaneCore;

public static partial class ThreeDRoofPreviewBuilder
{
    private const double GeneratedSeamMinimumRiseFeet = 0.05;

    private static void AddEnvelopeSeams(
        ThreeDRoofPreviewBuildResult result,
        IReadOnlyList<EnvelopeFace> faces,
        IReadOnlyList<P2> footprint)
    {
        // Collect every crease between pieces of different planes, then merge
        // collinear contiguous pieces (the triangulation splits one ridge into
        // many short segments) into a single ridge/hip/valley line.
        var raw = new List<SeamSeg>();
        for (int i = 0; i < faces.Count; i++)
        for (int j = i + 1; j < faces.Count; j++)
        {
            // Triangulated pieces of the same slope plane are flush, not a
            // crease - only different planes meet at ridge/hip/valley.
            if (AreSamePlane(faces[i].Plane, faces[j].Plane))
                continue;

            foreach (Segment seam in SharedSegments(faces[i].Points, faces[j].Points))
            {
                if (Distance(seam.Start, seam.End) < 0.02)
                    continue;
                if (!HasGeneratedSeamRise(faces[i], faces[j], seam))
                    continue;

                string kind = ClassifySeam(faces[i].Plane, faces[j].Plane, seam, footprint);
                raw.Add(new SeamSeg(seam.Start, seam.End, kind, faces[i].Plane, faces[i].RoofBase));
            }
        }

        int ridge = 0;
        int hip = 0;
        int valley = 0;
        foreach (SeamSeg seam in MergeSeamSegments(raw))
        {
            if (Distance(seam.Start, seam.End) < 0.25)
                continue;
            if (!HasGeneratedSeamRise(seam.Plane, new Segment(seam.Start, seam.End)))
                continue;

            int number = seam.Kind switch
            {
                ThreeDRoofGuideKinds.Hip => ++hip,
                ThreeDRoofGuideKinds.Valley => ++valley,
                _ => ++ridge,
            };
            double startY = seam.RoofBase + Math.Max(0, seam.Plane.HeightAt(seam.Start));
            double endY = seam.RoofBase + Math.Max(0, seam.Plane.HeightAt(seam.End));
            double midY = seam.RoofBase + Math.Max(0, seam.Plane.HeightAt(
                new P2((seam.Start.X + seam.End.X) / 2.0, (seam.Start.Z + seam.End.Z) / 2.0)));
            result.Guides.Add(new ThreeDRoofGuide
            {
                Kind = seam.Kind,
                Label = $"{ThreeDRoofGuideKinds.Title(seam.Kind)} {number}",
                PageFolder = seam.Plane.PageFolder,
                LevelKey = "roof",
                ElevationFeet = midY,
                PitchRisePerFoot = 0,
                Color = ThreeDRoofGuideKinds.Color(seam.Kind),
                Status = GeneratedSeamStatus,
                AdjustmentStatus = "generated",
                AdjustmentMessage = "Generated where roof planes meet.",
                Points =
                [
                    RoofPoint(seam.Start, seam.Plane.FeetPerPdf, startY),
                    RoofPoint(seam.End, seam.Plane.FeetPerPdf, endY),
                ],
                RawPoints =
                [
                    RoofPoint(seam.Start, seam.Plane.FeetPerPdf, startY),
                    RoofPoint(seam.End, seam.Plane.FeetPerPdf, endY),
                ],
            });
        }
    }

    private sealed record SeamSeg(P2 Start, P2 End, string Kind, SlopePlane Plane, double RoofBase);

    private static bool HasGeneratedSeamRise(EnvelopeFace first, EnvelopeFace second, Segment seam) =>
        HasGeneratedSeamRise(first.Plane, seam) || HasGeneratedSeamRise(second.Plane, seam);

    private static bool HasGeneratedSeamRise(SlopePlane plane, Segment seam) =>
        SeamMaxRelativeHeight(plane, seam) > GeneratedSeamMinimumRiseFeet;

    private static double SeamMaxRelativeHeight(SlopePlane plane, Segment seam)
    {
        P2 mid = new((seam.Start.X + seam.End.X) / 2.0, (seam.Start.Z + seam.End.Z) / 2.0);
        return Math.Max(
            Math.Max(Math.Max(0, plane.HeightAt(seam.Start)), Math.Max(0, plane.HeightAt(mid))),
            Math.Max(0, plane.HeightAt(seam.End)));
    }

    private static bool HasMeaningfulGeneratedRise(double roofBase, params double[] yValues) =>
        yValues.Length > 0 && yValues.Max() - roofBase > GeneratedSeamMinimumRiseFeet;

    // Greedily fuse seam pieces that share a kind and lie on the same line
    // (small angle + offset tolerance) and overlap or nearly touch.
    private static List<SeamSeg> MergeSeamSegments(List<SeamSeg> segments)
    {
        var merged = new List<SeamSeg>();
        foreach (SeamSeg segment in segments)
        {
            SeamSeg current = segment;
            bool fused = true;
            while (fused)
            {
                fused = false;
                for (int i = 0; i < merged.Count; i++)
                {
                    if (merged[i].Kind != current.Kind)
                        continue;
                    if (!TryFuseSeam(merged[i], current, out SeamSeg combined))
                        continue;

                    current = combined;
                    merged.RemoveAt(i);
                    fused = true;
                    break;
                }
            }

            merged.Add(current);
        }

        return merged;
    }

    private static bool TryFuseSeam(SeamSeg a, SeamSeg b, out SeamSeg fused)
    {
        fused = a;
        P2 dir = Subtract(a.End, a.Start);
        double len = Math.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
        if (len <= 0.000001)
            return false;

        P2 unit = new(dir.X / len, dir.Z / len);
        if (DistanceToLine(b.Start, a.Start, a.End) > 0.06 ||
            DistanceToLine(b.End, a.Start, a.End) > 0.06)
        {
            return false;
        }

        double a0 = 0;
        double a1 = len;
        double b0 = Dot(Subtract(b.Start, a.Start), unit);
        double b1 = Dot(Subtract(b.End, a.Start), unit);
        double lo = Math.Min(a0, Math.Min(b0, b1));
        double hi = Math.Max(a1, Math.Max(b0, b1));

        // overlap or a small gap between the projected intervals
        if (Math.Max(b0, b1) < a0 - 0.35 || Math.Min(b0, b1) > a1 + 0.35)
            return false;

        fused = a with
        {
            Start = new P2(a.Start.X + unit.X * lo, a.Start.Z + unit.Z * lo),
            End = new P2(a.Start.X + unit.X * hi, a.Start.Z + unit.Z * hi),
        };
        return true;
    }

    private static IEnumerable<Segment> SharedSegments(IReadOnlyList<P2> first, IReadOnlyList<P2> second)
    {
        foreach (Segment a in Edges(first))
        foreach (Segment b in Edges(second))
        {
            if (!TrySharedSegment(a, b, out Segment shared))
                continue;

            yield return shared;
        }
    }

    private static IEnumerable<Segment> Edges(IReadOnlyList<P2> polygon)
    {
        for (int i = 0; i < polygon.Count; i++)
            yield return new Segment(polygon[i], polygon[(i + 1) % polygon.Count]);
    }

    private static bool TrySharedSegment(Segment a, Segment b, out Segment shared)
    {
        shared = default;
        double ax = a.End.X - a.Start.X;
        double az = a.End.Z - a.Start.Z;
        double bx = b.End.X - b.Start.X;
        double bz = b.End.Z - b.Start.Z;
        double aLen = Math.Sqrt(ax * ax + az * az);
        double bLen = Math.Sqrt(bx * bx + bz * bz);
        if (aLen <= 0.000001 || bLen <= 0.000001)
            return false;

        double parallel = Math.Abs((ax * bz - az * bx) / (aLen * bLen));
        if (parallel > 0.001)
            return false;

        if (DistanceToLine(b.Start, a.Start, a.End) > 0.03 ||
            DistanceToLine(b.End, a.Start, a.End) > 0.03)
        {
            return false;
        }

        P2 unit = new(ax / aLen, az / aLen);
        double b0 = Dot(Subtract(b.Start, a.Start), unit);
        double b1 = Dot(Subtract(b.End, a.Start), unit);
        double start = Math.Max(0, Math.Min(b0, b1));
        double end = Math.Min(aLen, Math.Max(b0, b1));
        if (end - start <= 0.03)
            return false;

        shared = new Segment(
            new P2(a.Start.X + unit.X * start, a.Start.Z + unit.Z * start),
            new P2(a.Start.X + unit.X * end, a.Start.Z + unit.Z * end));
        return true;
    }

    private static string ClassifySeam(
        SlopePlane first,
        SlopePlane second,
        Segment seam,
        IReadOnlyList<P2> footprint)
    {
        if (SourceEdgesAreParallel(first, second))
            return ThreeDRoofGuideKinds.Ridge;

        if (TryFindTouchedFootprintVertex(seam, footprint, out bool concave))
            return concave ? ThreeDRoofGuideKinds.Valley : ThreeDRoofGuideKinds.Hip;

        return ThreeDRoofGuideKinds.Ridge;
    }

    private static bool SourceEdgesAreParallel(SlopePlane first, SlopePlane second)
    {
        P2 a = Subtract(first.End, first.Start);
        P2 b = Subtract(second.End, second.Start);
        double aLen = Math.Sqrt(a.X * a.X + a.Z * a.Z);
        double bLen = Math.Sqrt(b.X * b.X + b.Z * b.Z);
        if (aLen <= 0.000001 || bLen <= 0.000001)
            return false;

        return Math.Abs((a.X * b.Z - a.Z * b.X) / (aLen * bLen)) <= 0.08;
    }

    private static bool TryFindTouchedFootprintVertex(Segment seam, IReadOnlyList<P2> footprint, out bool concave)
    {
        concave = false;
        for (int i = 0; i < footprint.Count; i++)
        {
            P2 vertex = footprint[i];
            if (DistanceToSegment(vertex, seam.Start, seam.End) > 0.35)
                continue;

            P2 previous = footprint[(i - 1 + footprint.Count) % footprint.Count];
            P2 next = footprint[(i + 1) % footprint.Count];
            double cross = Cross(Subtract(vertex, previous), Subtract(next, vertex));
            concave = cross < -0.001;
            return true;
        }

        return false;
    }

    private static string SegmentKey(Segment segment)
    {
        static string PointKey(P2 point) => $"{Math.Round(point.X, 3):F3},{Math.Round(point.Z, 3):F3}";
        string a = PointKey(segment.Start);
        string b = PointKey(segment.End);
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
    }
}
