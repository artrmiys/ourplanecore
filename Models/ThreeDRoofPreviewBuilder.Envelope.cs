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

            existing.OverhangFeet = Math.Max(existing.OverhangFeet, plane.OverhangFeet);
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

            same.OverhangFeet = Math.Max(same.OverhangFeet, plane.OverhangFeet);
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

        // Weighted straight skeleton (Revit "roof by footprint"): exact for any
        // mix of per-edge pitches - adjacent slopes join at one elevation along
        // ridges/hips/valleys. Falls through to the legacy lower-envelope when it
        // cannot produce a clean tiling, so unusual footprints never regress.
        List<EnvelopeFace>? skeleton = TryBuildEnvelopeFacesViaSkeleton(footprint, planes, roofBase);
        if (skeleton != null)
            return skeleton;

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
                // A footprint-roof slope edge is finite, but its support is
                // mitered at the endpoints. A hard perpendicular strip chops
                // off concave valleys; an infinite line lets one short edge
                // cut the whole roof. The mitered active domain is the middle
                // ground used by footprint-roof hips/valleys.
                List<P2> front = ClipToActiveDomain(triangle, plane);
                if (front.Count < 3 || Math.Abs(SignedArea(front)) < 0.0004)
                    continue;

                // Exact lower envelope as convex pieces. For each competitor:
                // keep the area outside its finite mitered domain as-is, and
                // clip only the active part by i <= j.
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
    // Map the footprint + its eave slope planes onto the weighted straight
    // skeleton, then turn each sloped edge's skeleton cell into an EnvelopeFace.
    // Gable (rake) edges carry no slope plane and become stationary (speed 0)
    // walls the eaves run up to. Returns null (caller falls back) if no eave is
    // matched, the skeleton fails, or the sloped cells do not tile the footprint.
    private static List<EnvelopeFace>? TryBuildEnvelopeFacesViaSkeleton(
        IReadOnlyList<P2> footprint,
        IReadOnlyList<SlopePlane> planes,
        double roofBase)
    {
        int n = footprint.Count;
        var pts = new List<(double X, double Z)>(n);
        foreach (P2 p in footprint)
            pts.Add((p.X, p.Z));

        var edgePlane = new SlopePlane?[n];
        var speed = new double[n];
        int sloped = 0;
        for (int i = 0; i < n; i++)
        {
            P2 a = footprint[i];
            P2 b = footprint[(i + 1) % n];
            SlopePlane? match = null;
            foreach (SlopePlane plane in planes)
            {
                if (plane.PitchRisePerFoot <= 1e-6)
                    continue;
                if (EdgeMatches(plane.Start, plane.End, a, b) || EdgeMatches(plane.End, plane.Start, a, b))
                {
                    match = plane;
                    break;
                }
            }

            edgePlane[i] = match;
            if (match != null)
            {
                speed[i] = 1.0 / match.PitchRisePerFoot;
                sloped++;
            }
            else
            {
                speed[i] = 0.0; // gable / rake: vertical
            }
        }

        if (sloped == 0)
            return null;

        // The skeleton fixes the *mixed-pitch* seam (faces meeting at different
        // heights). With a single pitch the legacy lower-envelope is already
        // exact and battle-tested, so only take over when eaves actually differ
        // in pitch.
        int distinctPitches = planes
            .Where(p => p.PitchRisePerFoot > 1e-6)
            .Select(p => Math.Round(p.PitchRisePerFoot, 3))
            .Distinct()
            .Count();
        if (distinctPitches < 2)
            return null;

        List<RoofWeightedSkeleton.Facet>? facets = RoofWeightedSkeleton.Build(pts, speed);
        if (facets == null)
            return null;

        // First the directly-sloped (eave) facets.
        var eaveFacets = new List<(SlopePlane Plane, List<P2> Poly)>();
        var gableFacets = new List<List<P2>>();
        foreach (RoofWeightedSkeleton.Facet facet in facets)
        {
            List<P2> poly = facet.Polygon.Select(p => new P2(p.X, p.Z)).ToList();
            if (poly.Count < 3 || Math.Abs(SignedArea(poly)) < 0.05)
                continue;
            if (SignedArea(poly) < 0)
                poly.Reverse();

            SlopePlane? plane = edgePlane[facet.EdgeIndex];
            if (plane != null)
                eaveFacets.Add((plane, poly));
            else
                gableFacets.Add(poly); // vertical gable strip: assigned below
        }

        var result = new List<EnvelopeFace>();
        double covered = 0;
        foreach ((SlopePlane Plane, List<P2> Poly) f in eaveFacets)
        {
            covered += Math.Abs(SignedArea(f.Poly));
            result.Add(new EnvelopeFace(f.Plane, f.Poly, roofBase));
        }

        // A gable (rake) edge is vertical; the plan strip it owns is roofed by
        // the slope it abuts. Continue the eave facet that shares the longest
        // edge with the strip (so the surface stays continuous and no stray
        // distant slope is projected onto it).
        List<SlopePlane> allEavePlanes = edgePlane.Where(p => p != null).Select(p => p!).Distinct().ToList();
        foreach (List<P2> poly in gableFacets)
        {
            SlopePlane? plane = AdjacentEavePlane(poly, eaveFacets)
                ?? LowestPlaneAt(allEavePlanes, Centroid2(poly));
            if (plane == null)
                continue;
            covered += Math.Abs(SignedArea(poly));
            result.Add(new EnvelopeFace(plane, poly, roofBase));
        }

        double area = Math.Abs(SignedArea(footprint));
        if (result.Count == 0 || Math.Abs(covered - area) > Math.Max(2.0, area * 0.05))
            return null;

        return result;
    }

    private static bool EdgeMatches(P2 s, P2 e, P2 a, P2 b) =>
        Distance(s, a) < 0.2 && Distance(e, b) < 0.2;

    // The eave facet sharing the longest (collinear, overlapping) edge with the
    // gable strip - i.e. the slope the strip physically continues.
    private static SlopePlane? AdjacentEavePlane(
        List<P2> strip,
        List<(SlopePlane Plane, List<P2> Poly)> eaveFacets)
    {
        SlopePlane? best = null;
        double bestShared = 0.1;
        foreach ((SlopePlane Plane, List<P2> Poly) f in eaveFacets)
        {
            double shared = SharedEdgeLength(strip, f.Poly);
            if (shared > bestShared)
            {
                bestShared = shared;
                best = f.Plane;
            }
        }

        return best;
    }

    private static double SharedEdgeLength(List<P2> a, List<P2> b)
    {
        double total = 0;
        for (int i = 0; i < a.Count; i++)
        {
            P2 a0 = a[i], a1 = a[(i + 1) % a.Count];
            P2 dir = new(a1.X - a0.X, a1.Z - a0.Z);
            double len = Math.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
            if (len < 1e-6)
                continue;
            P2 u = new(dir.X / len, dir.Z / len);

            for (int j = 0; j < b.Count; j++)
            {
                P2 b0 = b[j], b1 = b[(j + 1) % b.Count];
                // Both b endpoints on a's line?
                if (PerpDist(a0, u, b0) > 0.05 || PerpDist(a0, u, b1) > 0.05)
                    continue;
                double t0 = (b0.X - a0.X) * u.X + (b0.Z - a0.Z) * u.Z;
                double t1 = (b1.X - a0.X) * u.X + (b1.Z - a0.Z) * u.Z;
                double lo = Math.Max(0, Math.Min(t0, t1));
                double hi = Math.Min(len, Math.Max(t0, t1));
                if (hi - lo > 0.05)
                    total += hi - lo;
            }
        }

        return total;
    }

    private static double PerpDist(P2 origin, P2 unitDir, P2 p) =>
        Math.Abs(unitDir.X * (p.Z - origin.Z) - unitDir.Z * (p.X - origin.X));

    private static P2 Centroid2(IReadOnlyList<P2> poly)
    {
        double x = 0, z = 0;
        foreach (P2 p in poly)
        {
            x += p.X;
            z += p.Z;
        }
        return new P2(x / poly.Count, z / poly.Count);
    }

    // Lowest front-facing eave plane over a point - lower-envelope fallback for a
    // gable strip with no clearly adjacent slope facet.
    private static SlopePlane? LowestPlaneAt(IReadOnlyList<SlopePlane> planes, P2 point)
    {
        SlopePlane? best = null;
        double bestH = double.PositiveInfinity;
        foreach (SlopePlane plane in planes)
        {
            double h = plane.HeightAt(point);
            if (h < -0.01)
                continue;
            if (h < bestH)
            {
                bestH = h;
                best = plane;
            }
        }

        return best;
    }

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
        List<P2> active = CleanPolygon(ClipFrontHalfPlane(poly, plane));
        if (active.Count < 3)
            return [];

        active = CleanPolygon(ClipSourceStartMiterHalfPlane(active, plane));
        if (active.Count < 3)
            return [];

        return CleanPolygon(ClipSourceEndMiterHalfPlane(active, plane));
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
        if (active.Count < 3)
            return inactive;

        AddEnvelopePiece(inactive, ClipSourceStartMiterHalfPlane(active, plane, invert: true));
        active = CleanPolygon(ClipSourceStartMiterHalfPlane(active, plane));
        if (active.Count < 3)
            return inactive;

        AddEnvelopePiece(inactive, ClipSourceEndMiterHalfPlane(active, plane, invert: true));
        active = CleanPolygon(ClipSourceEndMiterHalfPlane(active, plane));
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

    private static List<P2> ClipSourceStartMiterHalfPlane(IReadOnlyList<P2> poly, SlopePlane plane, bool invert = false)
    {
        double tolerance = SourceDomainTolerance(plane);
        return ClipPolygon(
            poly,
            point => invert
                ? SourceStartMiterValue(plane, point) <= -tolerance
                : SourceStartMiterValue(plane, point) >= -tolerance,
            (a, b) => IntersectMiterBoundary(a, b, plane, SourceStartMiterValue, -tolerance));
    }

    private static List<P2> ClipSourceEndMiterHalfPlane(IReadOnlyList<P2> poly, SlopePlane plane, bool invert = false)
    {
        double tolerance = SourceDomainTolerance(plane);
        return ClipPolygon(
            poly,
            point => invert
                ? SourceEndMiterValue(plane, point) >= tolerance
                : SourceEndMiterValue(plane, point) <= tolerance,
            (a, b) => IntersectMiterBoundary(a, b, plane, SourceEndMiterValue, tolerance));
    }

    private static P2 IntersectMiterBoundary(
        P2 a,
        P2 b,
        SlopePlane plane,
        Func<SlopePlane, P2, double> value,
        double target)
    {
        double da = value(plane, a) - target;
        double db = value(plane, b) - target;
        double denom = da - db;
        if (Math.Abs(denom) <= 0.000001)
            return a;

        double t = Math.Clamp(da / denom, 0, 1);
        return new P2(
            a.X + (b.X - a.X) * t,
            a.Z + (b.Z - a.Z) * t);
    }

    private static double SourceStartMiterValue(SlopePlane plane, P2 point) =>
        SourceProjection(plane, point) + SourceMiterRun(plane, point);

    private static double SourceEndMiterValue(SlopePlane plane, P2 point) =>
        SourceProjection(plane, point) - Distance(plane.Start, plane.End) - SourceMiterRun(plane, point);

    private static double SourceMiterRun(SlopePlane plane, P2 point) =>
        plane.PitchRisePerFoot <= 0.000001
            ? 0
            : Math.Max(0, plane.HeightAt(point)) / plane.PitchRisePerFoot;

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
