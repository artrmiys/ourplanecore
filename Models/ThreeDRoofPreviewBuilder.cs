namespace OurPlaneCore;

public sealed class ThreeDRoofPreviewBuildResult
{
    public List<ThreeDRoofPlane> Planes { get; } = [];
    public List<ThreeDRoofGuide> Guides { get; } = [];
    public List<string> Messages { get; } = [];
}

public static partial class ThreeDRoofPreviewBuilder
{
    public const double DefaultPitchRisePerFoot = 0.5;
    public const string GeneratedSeamStatus = "generated_roof_seam";
    private const double FaceClipTolerance = 0.0001;

    public static ThreeDRoofPreviewBuildResult BuildPreview(ThreeDWallModel model)
    {
        var result = new ThreeDRoofPreviewBuildResult();
        IReadOnlyList<RoofBoundary> boundaries = ResolveRoofBoundaries(model);
        if (boundaries.Count == 0)
        {
            result.Messages.Add("Roof generation needs a roof base layer from one or more area takeoffs.");
            return result;
        }

        List<ThreeDRoofGuide> roofGuides = model.RoofGuides
            .Where(guide => !string.Equals(guide.Status, GeneratedSeamStatus, StringComparison.OrdinalIgnoreCase))
            .Where(guide => guide.Points.Count >= 2)
            .ToList();
        foreach (RoofBoundary boundary in boundaries.Where(boundary => !boundary.IsFallbackBounds))
        {
            List<ThreeDRoofGuide> boundaryGuides = roofGuides
                .Where(guide => GuideBelongsToBoundary(guide, boundary))
                .ToList();
            List<ThreeDRoofGuide> boundarySlopeEdges = boundaryGuides
                .Where(IsSlopeDefiningGuide)
                .ToList();
            if (boundarySlopeEdges.Count == 0)
                continue;

            double roofBase = ResolveRoofBaseElevation(model, boundary);
            AddFootprintSlopeMesh(result, boundary, boundarySlopeEdges, boundaryGuides, roofBase);
        }

        if (result.Planes.Count > 0)
            return result;

        bool hasEaveEdges = roofGuides.Any(IsSlopeDefiningGuide);
        result.Messages.Add(!hasEaveEdges
            ? "Roof generation needs one or more roof base edges set to Defines Slope with pitch."
            : "Roof generation needs a roof base layer, not loose guide lines.");
        return result;
    }

    // Slope is driven by the explicit Revit-style flag. Kind (eave/rake) only
    // mirrors it for color/labeling.
    internal static bool IsSlopeDefiningGuide(ThreeDRoofGuide guide) => guide.DefinesSlope;

    private static void AddFootprintSlopeMesh(
        ThreeDRoofPreviewBuildResult result,
        RoofBoundary boundary,
        IReadOnlyList<ThreeDRoofGuide> slopeGuides,
        IReadOnlyList<ThreeDRoofGuide> boundaryGuides,
        double roofBase)
    {
        List<P2> footprint = EnsureCounterClockwise(boundary.Points.Select(ToP2).ToList());
        if (footprint.Count < 3 || Math.Abs(SignedArea(footprint)) < 1.0)
            return;

        List<SlopePlane> slopePlanes = [];
        foreach (ThreeDRoofGuide guide in slopeGuides)
        {
            if (TryCreateSlopePlane(guide, footprint, out SlopePlane? plane) && plane != null)
                slopePlanes.Add(plane);
        }

        slopePlanes = MergeCoplanarSlopePlanes(slopePlanes);
        if (slopePlanes.Count == 0)
            return;

        // Revit footprint-roof = lower envelope of the edge slope planes.
        // Computing it as min(plane) clipped to the raw polygon fails on
        // non-convex U/S/L footprints (half-plane clipping mangles concave
        // rings). Instead triangulate the footprint into convex cells and
        // clip the envelope inside each triangle, where half-plane clipping
        // is exact. Union of cells = the exact hip/valley roof.
        List<SlopePlane> envelopePlanes = DistinctSlopePlanes(slopePlanes);
        List<EnvelopeFace> envelopeFaces = BuildEnvelopeFaces(footprint, envelopePlanes, roofBase);

        for (int i = 0; i < envelopeFaces.Count; i++)
            AddEnvelopeFace(result, envelopeFaces[i], envelopePlanes.IndexOf(envelopeFaces[i].Plane));
        AddEnvelopeSeams(result, envelopeFaces, footprint);
        AddParallelEaveRidges(result, envelopeFaces, footprint);
        AddConcaveCornerValleys(result, envelopeFaces, footprint);
        AddRakeEndFaces(result, boundary, boundaryGuides, slopePlanes, roofBase);

        if (envelopeFaces.Count > 0)
        {
            string message = slopePlanes.Count == 1
                ? "Roof face generated from one slope-defining eave edge."
                : $"Roof faces generated from {slopePlanes.Count} slope-defining eave edge(s).";
            result.Messages.Add(message + " Ridges, hips, and valleys are the exact seams where adjacent eave planes meet.");
        }
    }

    private static void AddEnvelopeFace(
        ThreeDRoofPreviewBuildResult result,
        EnvelopeFace face,
        int index)
    {
        result.Planes.Add(new ThreeDRoofPlane
        {
            Kind = "roof_face_envelope",
            Label = $"Roof face from {face.Plane.Label}",
            Color = RoofFaceColor(index),
            Opacity = 0.68,
            Points = face.Points
                .Select(point => Vertex(point.X, face.RoofBase + Math.Max(0, face.Plane.HeightAt(point)), point.Z))
                .ToList(),
            SourceGuideIds = face.Plane.GuideIds.ToList(),
            Message = "Generated from the lower envelope of slope-defining eave planes.",
        });
    }

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

    private static bool TryCreateSlopePlane(
        ThreeDRoofGuide guide,
        IReadOnlyList<P2> footprint,
        out SlopePlane? plane)
    {
        plane = null;
        if (guide.Points.Count < 2)
            return false;

        ThreeDRoofGuidePoint a = guide.Points[0];
        ThreeDRoofGuidePoint b = guide.Points[^1];
        P2 start = new(a.XFeet, a.ZFeet);
        P2 end = new(b.XFeet, b.ZFeet);
        if (Distance(start, end) <= 0.25)
            return false;

        OrientEdgeToFootprintInterior(footprint, ref start, ref end);
        double pitch = guide.PitchRisePerFoot > 0
            ? guide.PitchRisePerFoot
            : DefaultPitchRisePerFoot;
        double feetPerPdf = ResolveFeetPerPdf(guide, start, end);
        plane = new SlopePlane(
            guide.Id,
            string.IsNullOrWhiteSpace(guide.Label) ? "Eave" : guide.Label,
            guide.PageFolder,
            start,
            end,
            Math.Clamp(pitch, 0.001, 4.0),
            feetPerPdf);
        return true;
    }

    private static List<SlopePlane> MergeCoplanarSlopePlanes(IReadOnlyList<SlopePlane> planes)
    {
        List<SlopePlane> merged = [];
        foreach (SlopePlane plane in planes)
        {
            SlopePlane? existing = merged.FirstOrDefault(candidate =>
                AreSamePlane(candidate, plane) &&
                SourceStripsOverlap(candidate, plane));
            if (existing == null)
            {
                merged.Add(plane);
                continue;
            }

            foreach (string guideId in plane.GuideIds)
            {
                if (!existing.GuideIds.Contains(guideId, StringComparer.Ordinal))
                    existing.GuideIds.Add(guideId);
            }
        }

        return merged;
    }

    // Collapse coplanar slope planes (e.g. a split eave) so the envelope is
    // computed over the true distinct slopes; union their guide ids.
    private static List<SlopePlane> DistinctSlopePlanes(IReadOnlyList<SlopePlane> planes)
    {
        var distinct = new List<SlopePlane>();
        foreach (SlopePlane plane in planes)
        {
            SlopePlane? same = distinct.FirstOrDefault(existing => AreSamePlane(existing, plane));
            if (same == null)
            {
                distinct.Add(plane);
                continue;
            }

            foreach (string guideId in plane.GuideIds)
            {
                if (!same.GuideIds.Contains(guideId, StringComparer.Ordinal))
                    same.GuideIds.Add(guideId);
            }
        }

        return distinct;
    }

    // Exact lower envelope: triangulate the footprint into convex cells, then
    // inside each triangle keep, per plane, the region where that plane is the
    // lowest. Half-plane clipping is exact on a convex triangle, so this works
    // for any footprint - simple gable, full hip, or non-convex U/S/L.
    private static List<EnvelopeFace> BuildEnvelopeFaces(
        IReadOnlyList<P2> footprint,
        IReadOnlyList<SlopePlane> planes,
        double roofBase)
    {
        var faces = new List<EnvelopeFace>();
        ThreeDPolygonTriangulation tri = ThreeDPolygonTriangulator.Triangulate(
            footprint.Select(p => new ThreeDPoint { XFeet = p.X, ZFeet = p.Z }).ToList());

        if (!tri.Success || tri.TriangleIndices.Count < 3)
            return BuildEnvelopeFacesLegacy(footprint, planes, roofBase);

        for (int t = 0; t + 2 < tri.TriangleIndices.Count; t += 3)
        {
            List<P2> triangle =
            [
                new(tri.Points[tri.TriangleIndices[t]].XFeet, tri.Points[tri.TriangleIndices[t]].ZFeet),
                new(tri.Points[tri.TriangleIndices[t + 1]].XFeet, tri.Points[tri.TriangleIndices[t + 1]].ZFeet),
                new(tri.Points[tri.TriangleIndices[t + 2]].XFeet, tri.Points[tri.TriangleIndices[t + 2]].ZFeet),
            ];
            if (Math.Abs(SignedArea(triangle)) < 0.0005)
                continue;
            if (SignedArea(triangle) < 0)
                triangle.Reverse();

            foreach (SlopePlane plane in planes)
            {
                List<P2> region = triangle.ToList();
                foreach (SlopePlane other in planes)
                {
                    if (ReferenceEquals(plane, other) || AreSamePlane(plane, other))
                        continue;

                    region = CleanPolygon(ClipToLowerPlane(region, plane, other));
                    if (region.Count < 3 || Math.Abs(SignedArea(region)) < 0.0005)
                        break;
                }

                if (region.Count < 3 || Math.Abs(SignedArea(region)) < 0.0005)
                    continue;

                if (SignedArea(region) < 0)
                    region.Reverse();

                faces.Add(new EnvelopeFace(plane, region, roofBase));
            }
        }

        return faces.Count > 0 ? faces : BuildEnvelopeFacesLegacy(footprint, planes, roofBase);
    }

    // Pre-triangulation behavior, kept as a fallback when the footprint cannot
    // be triangulated (e.g. self-touching loops).
    private static List<EnvelopeFace> BuildEnvelopeFacesLegacy(
        IReadOnlyList<P2> footprint,
        IReadOnlyList<SlopePlane> planes,
        double roofBase)
    {
        var faces = new List<EnvelopeFace>();
        foreach (SlopePlane plane in planes)
        {
            List<P2> face = ClipToSourceStrip(footprint, plane);
            if (face.Count < 3 || Math.Abs(SignedArea(face)) < 0.05)
                continue;

            foreach (SlopePlane other in planes)
            {
                if (ReferenceEquals(plane, other) || AreSamePlane(plane, other))
                    continue;
                if (!SourceStripsOverlap(plane, other))
                    continue;

                face = CleanPolygon(ClipToLowerPlane(face, plane, other));
                if (face.Count < 3 || Math.Abs(SignedArea(face)) < 0.05)
                    break;
            }

            if (face.Count < 3 || Math.Abs(SignedArea(face)) < 0.05)
                continue;

            if (SignedArea(face) < 0)
                face.Reverse();

            faces.Add(new EnvelopeFace(plane, face, roofBase));
        }

        return faces;
    }

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

    private sealed class SlopePlane
    {
        public SlopePlane(
            string guideId,
            string label,
            string pageFolder,
            P2 start,
            P2 end,
            double pitchRisePerFoot,
            double feetPerPdf)
        {
            GuideIds.Add(guideId);
            Label = label;
            PageFolder = pageFolder;
            Start = start;
            End = end;
            PitchRisePerFoot = pitchRisePerFoot;
            FeetPerPdf = feetPerPdf;

            double dx = End.X - Start.X;
            double dz = End.Z - Start.Z;
            double len = Math.Sqrt(dx * dx + dz * dz);
            A = -PitchRisePerFoot * dz / len;
            B = PitchRisePerFoot * dx / len;
            C = PitchRisePerFoot * (dz * Start.X - dx * Start.Z) / len;
        }

        public List<string> GuideIds { get; } = [];
        public string Label { get; }
        public string PageFolder { get; }
        public P2 Start { get; }
        public P2 End { get; }
        public double PitchRisePerFoot { get; }
        public double FeetPerPdf { get; }
        public double A { get; }
        public double B { get; }
        public double C { get; }

        public double HeightAt(P2 point) => A * point.X + B * point.Z + C;
    }

    private sealed record EnvelopeFace(SlopePlane Plane, List<P2> Points, double RoofBase);

    private readonly record struct Segment(P2 Start, P2 End);

    private readonly record struct P2(double X, double Z);

    private readonly record struct RoofBoundary(
        List<ThreeDPoint> Points,
        double ElevationFeet,
        string LevelKey,
        bool IsFallbackBounds);
}
