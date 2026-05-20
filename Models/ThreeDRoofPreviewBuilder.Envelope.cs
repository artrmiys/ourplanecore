namespace OurPlaneCore;

public static partial class ThreeDRoofPreviewBuilder
{
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

    // Exact roof = lower envelope of the eave slope planes, computed per
    // triangulated convex cell (half-plane clipping is exact on a triangle).
    // Each plane is also clipped to its OWN eave front half-plane (height >= 0)
    // so a partial eave (L/U/S wing) cannot overshoot with its negative back
    // side; full-width perpendicular reach is kept, so there are no gaps.
    // Few clean polygon faces; exact ridge/hip/valley seams between them.
    private static List<EnvelopeFace> BuildEnvelopeFaces(
        IReadOnlyList<P2> footprint,
        IReadOnlyList<SlopePlane> planes,
        double roofBase)
    {
        if (footprint.Count < 3 || planes.Count == 0)
            return BuildEnvelopeFacesLegacy(footprint, planes, roofBase);

        ThreeDPolygonTriangulation tri = ThreeDPolygonTriangulator.Triangulate(
            footprint.Select(p => new ThreeDPoint { XFeet = p.X, ZFeet = p.Z }).ToList());
        if (!tri.Success || tri.TriangleIndices.Count < 3)
            return BuildEnvelopeFacesLegacy(footprint, planes, roofBase);

        var faces = new List<EnvelopeFace>();
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
                // A Revit footprint-roof slope edge contributes its support
                // plane across the roofed side of the edge. Do not clip it to
                // the source segment endpoints: hip/valley faces past a
                // corner are exactly where adjacent edge planes keep meeting.
                List<P2> front = ClipToActiveDomain(triangle, plane);
                if (front.Count < 3 || Math.Abs(SignedArea(front)) < 0.0004)
                    continue;

                // Exact lower envelope as convex pieces. For each competitor:
                // keep the area behind its eave line as-is, and clip only the
                // roofed side by i <= j.
                var pieces = new List<List<P2>> { front };
                foreach (SlopePlane other in planes)
                {
                    if (ReferenceEquals(plane, other) || AreSamePlane(plane, other))
                        continue;

                    var next = new List<List<P2>>();
                    foreach (List<P2> piece in pieces)
                    {
                        List<P2> jActive;
                        foreach (List<P2> inactive in SplitByActiveDomain(piece, other, out jActive))
                            AddEnvelopePiece(next, inactive);

                        if (jActive.Count < 3 || Math.Abs(SignedArea(jActive)) < 0.0004)
                            continue;

                        AddEnvelopePiece(next, ClipToLowerPlane(jActive, plane, other));
                    }

                    pieces = next;
                    if (pieces.Count == 0)
                        break;
                }

                foreach (List<P2> piece in pieces)
                {
                    if (piece.Count < 3 || Math.Abs(SignedArea(piece)) < 0.0004)
                        continue;
                    List<P2> region = piece;
                    if (SignedArea(region) < 0)
                        region.Reverse();
                    faces.Add(new EnvelopeFace(plane, region, roofBase));
                }
            }
        }

        if (faces.Count == 0)
            return BuildEnvelopeFacesLegacy(footprint, planes, roofBase);

        // Keep the clipped pieces instead of fusing coplanar regions. The
        // fused loop can become non-simple on stepped/L footprints and then
        // the 3D renderer triangulates it as torn geometry. These pieces are
        // convex cell fragments, so they render as stable roof surfaces while
        // seams still merge into clean ridge/hip/valley guide lines.
        return faces;
    }

    // Union the coplanar pieces of each plane: drop internal shared edges,
    // walk the surviving boundary edges into one loop per slope.
    private static List<EnvelopeFace> MergeCoplanarFaces(
        List<EnvelopeFace> pieces,
        double roofBase)
    {
        var merged = new List<EnvelopeFace>();
        foreach (IGrouping<SlopePlane, EnvelopeFace> group in pieces.GroupBy(f => f.Plane))
        {
            // Directed boundary edges; an edge shared by two pieces of this
            // same plane appears once each way and cancels out.
            var edges = new Dictionary<(long, long, long, long), (P2 A, P2 B)>();
            foreach (EnvelopeFace face in group)
            {
                List<P2> poly = face.Points;
                for (int i = 0; i < poly.Count; i++)
                {
                    P2 a = poly[i];
                    P2 b = poly[(i + 1) % poly.Count];
                    if (Distance(a, b) < 0.01)
                        continue;
                    (long, long) ka = Key(a);
                    (long, long) kb = Key(b);
                    var fwd = (ka.Item1, ka.Item2, kb.Item1, kb.Item2);
                    var rev = (kb.Item1, kb.Item2, ka.Item1, ka.Item2);
                    if (edges.Remove(rev))
                        continue;
                    edges[fwd] = (a, b);
                }
            }

            if (edges.Count < 3)
            {
                merged.AddRange(group);
                continue;
            }

            // Chain the surviving boundary edges into loops.
            var adjacency = new Dictionary<(long, long), List<P2>>();
            foreach ((P2 A, P2 B) e in edges.Values)
            {
                if (!adjacency.TryGetValue(Key(e.A), out List<P2>? outs))
                    adjacency[Key(e.A)] = outs = [];
                outs.Add(e.B);
            }

            var used = new HashSet<(long, long, long, long)>();
            foreach ((P2 A, P2 B) start in edges.Values)
            {
                var sk = (Key(start.A).Item1, Key(start.A).Item2, Key(start.B).Item1, Key(start.B).Item2);
                if (!used.Add(sk))
                    continue;

                var loop = new List<P2> { start.A, start.B };
                P2 cur = start.B;
                for (int guard = 0; guard < edges.Count + 4; guard++)
                {
                    if (!adjacency.TryGetValue(Key(cur), out List<P2>? nexts) || nexts.Count == 0)
                        break;
                    P2 nxt = nexts[0];
                    foreach (P2 cand in nexts)
                    {
                        var ck = (Key(cur).Item1, Key(cur).Item2, Key(cand).Item1, Key(cand).Item2);
                        if (!used.Contains(ck)) { nxt = cand; break; }
                    }

                    var nk = (Key(cur).Item1, Key(cur).Item2, Key(nxt).Item1, Key(nxt).Item2);
                    used.Add(nk);
                    if (Distance(nxt, loop[0]) < 0.05)
                        break;
                    loop.Add(nxt);
                    cur = nxt;
                }

                List<P2> clean = CleanPolygon(loop);
                if (clean.Count >= 3 && Math.Abs(SignedArea(clean)) >= 0.05)
                {
                    if (SignedArea(clean) < 0)
                        clean.Reverse();
                    merged.Add(new EnvelopeFace(group.Key, clean, roofBase));
                }
            }
        }

        return merged.Count > 0 ? merged : pieces;
    }

    private static (long, long) Key(P2 p) =>
        ((long)Math.Round(p.X * 64.0), (long)Math.Round(p.Z * 64.0));

    // Lower-envelope clip that ignores a competitor where it is BEHIND its own
    // eave (height < 0): there it does not roof anything, so it must not push
    // the winning plane out. Boundary is piecewise (the eave line and the i=j
    // line) so edge crossings are found by bisection - exact enough per cell.
    private static List<P2> ClipToLowerPlaneFrontAware(
        IReadOnlyList<P2> poly,
        SlopePlane plane,
        SlopePlane other)
    {
        const double tol = 0.01;
        double Eff(SlopePlane s, P2 p)
        {
            double h = s.HeightAt(p);
            return h >= -tol ? h : 1e9;
        }
        double F(P2 p) => Eff(plane, p) - Eff(other, p);

        return ClipPolygon(
            poly,
            p => F(p) <= tol,
            (p, q) =>
            {
                P2 lo = p;
                P2 hi = q;
                for (int k = 0; k < 28; k++)
                {
                    P2 mid = new((lo.X + hi.X) / 2.0, (lo.Z + hi.Z) / 2.0);
                    if (F(p) <= tol == F(mid) <= tol)
                        lo = mid;
                    else
                        hi = mid;
                }

                return new P2((lo.X + hi.X) / 2.0, (lo.Z + hi.Z) / 2.0);
            });
    }

    // Keep the polygon where this eave's plane is at or above its eave line
    // (HeightAt >= 0): the front, roofed side (invert -> the back side).
    // Linear -> exact on a convex piece.
    private static List<P2> ClipFrontHalfPlane(IReadOnlyList<P2> poly, SlopePlane plane, bool invert = false)
    {
        const double tol = 0.01;
        return ClipPolygon(
            poly,
            p => invert ? plane.HeightAt(p) <= tol : plane.HeightAt(p) >= -tol,
            (p, q) =>
            {
                double hp = plane.HeightAt(p);
                double hq = plane.HeightAt(q);
                double denom = hp - hq;
                double s = Math.Abs(denom) <= 1e-12 ? 0 : hp / denom;
                return new P2(p.X + (q.X - p.X) * s, p.Z + (q.Z - p.Z) * s);
            });
    }

    private static List<P2> ClipToActiveDomain(IReadOnlyList<P2> poly, SlopePlane plane)
    {
        return CleanPolygon(ClipFrontHalfPlane(poly, plane));
    }

    private static IReadOnlyList<List<P2>> SplitByActiveDomain(
        IReadOnlyList<P2> poly,
        SlopePlane plane,
        out List<P2> active)
    {
        var inactive = new List<List<P2>>();
        List<P2> remaining = CleanPolygon(poly);
        AddEnvelopePiece(inactive, ClipFrontHalfPlane(remaining, plane, invert: true));

        active = CleanPolygon(ClipFrontHalfPlane(remaining, plane));
        return inactive;
    }

    private static void AddEnvelopePiece(List<List<P2>> pieces, IReadOnlyList<P2> polygon)
    {
        List<P2> clean = CleanPolygon(polygon);
        if (clean.Count >= 3 && Math.Abs(SignedArea(clean)) >= 0.0004)
            pieces.Add(clean);
    }

    private static List<P2> ClipSourceStartHalfPlane(IReadOnlyList<P2> poly, SlopePlane plane, bool invert = false)
    {
        double tolerance = SourceDomainTolerance(plane);
        return ClipPolygon(
            poly,
            point => invert
                ? SourceProjection(plane, point) <= -tolerance
                : SourceProjection(plane, point) >= -tolerance,
            (a, b) => IntersectSourceProjectionBoundary(a, b, plane, -tolerance));
    }

    private static List<P2> ClipSourceEndHalfPlane(IReadOnlyList<P2> poly, SlopePlane plane, bool invert = false)
    {
        double length = Distance(plane.Start, plane.End);
        double tolerance = SourceDomainTolerance(plane);
        return ClipPolygon(
            poly,
            point => invert
                ? SourceProjection(plane, point) >= length + tolerance
                : SourceProjection(plane, point) <= length + tolerance,
            (a, b) => IntersectSourceProjectionBoundary(a, b, plane, length + tolerance));
    }

    private static P2 IntersectSourceProjectionBoundary(P2 a, P2 b, SlopePlane plane, double target)
    {
        double da = SourceProjection(plane, a) - target;
        double db = SourceProjection(plane, b) - target;
        double denom = da - db;
        if (Math.Abs(denom) <= 0.000001)
            return a;

        double t = Math.Clamp(da / denom, 0, 1);
        return new P2(
            a.X + (b.X - a.X) * t,
            a.Z + (b.Z - a.Z) * t);
    }

    private static double SourceProjection(SlopePlane plane, P2 point) =>
        Dot(Subtract(point, plane.Start), UnitAlong(plane));

    private static double SourceDomainTolerance(SlopePlane plane) =>
        Math.Clamp(Distance(plane.Start, plane.End) * 0.005, 0.03, 0.15);

    // The footprint edge an eave guide lies on (matched by its endpoints).
    private static int NearestFootprintEdge(IReadOnlyList<P2> footprint, SlopePlane plane)
    {
        int n = footprint.Count;
        int best = -1;
        double bestScore = double.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            P2 a = footprint[i];
            P2 b = footprint[(i + 1) % n];
            double score = Math.Min(
                Distance(a, plane.Start) + Distance(b, plane.End),
                Distance(a, plane.End) + Distance(b, plane.Start));
            P2 mid = new((plane.Start.X + plane.End.X) / 2.0, (plane.Start.Z + plane.End.Z) / 2.0);
            score = Math.Min(score, DistanceToSegment(mid, a, b) * 2.0);
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    // Keep the polygon side of line(point, dir) that contains reference.
    private static List<P2> ClipHalfPlane(IReadOnlyList<P2> poly, P2 point, P2 dir, P2 reference)
    {
        // Signed area test vs the infinite line through point along dir.
        double Side(P2 p) => dir.X * (p.Z - point.Z) - dir.Z * (p.X - point.X);
        double refSide = Side(reference);
        if (Math.Abs(refSide) <= 1e-9)
            return poly.ToList();

        bool keepPositive = refSide > 0;
        return ClipPolygon(
            poly,
            p => keepPositive ? Side(p) >= -1e-7 : Side(p) <= 1e-7,
            (p, q) =>
            {
                double sp = Side(p);
                double sq = Side(q);
                double denom = sp - sq;
                double t = Math.Abs(denom) <= 1e-12 ? 0 : sp / denom;
                return new P2(p.X + (q.X - p.X) * t, p.Z + (q.Z - p.Z) * t);
            });
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
}
