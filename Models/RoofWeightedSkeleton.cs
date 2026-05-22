namespace OurPlaneCore;

// Weighted straight skeleton for footprint roofs (Revit "roof by footprint"
// with a per-edge slope; the multiplicatively-weighted straight skeleton).
//
// Each eave edge sweeps inward at speed 1/pitch. Because height = pitch * inward
// run, the sweep "time" equals roof height, so every skeleton node sits at a
// single height shared by all faces meeting there - adjacent slopes of ANY pitch
// mix join at one elevation along ridges/hips/valleys (no side gap).
//
// Faces are extracted from skeleton ARCS: every wavefront vertex, over its life,
// traces one arc that separates the two eave faces it lies between. A facet =
// its eave segment plus every arc that borders it, walked into a loop. This is
// robust to split events (concave valleys), where a reflex vertex divides the
// wavefront into two loops.
//
// Build returns one plan polygon per input edge or null when the simulation
// cannot complete (caller falls back to the legacy lower-envelope builder, so
// existing roofs never regress).
public static class RoofWeightedSkeleton
{
    private const double Eps = 1e-7;

    public sealed record Facet(int EdgeIndex, List<(double X, double Z)> Polygon);

    public static List<Facet>? Build(
        IReadOnlyList<(double X, double Z)> footprintCcw,
        IReadOnlyList<double> edgeSpeed)
    {
        int n = footprintCcw.Count;
        if (n < 3 || edgeSpeed.Count != n)
            return null;

        // The sweep needs the winding whose left normal (-dz,dx) points into the
        // footprint; in these (X,Z) coords that is the SignedAreaXz < 0 winding.
        // Reverse the other winding (and its per-edge speeds) to match.
        if (SignedAreaXz(footprintCcw) > 0)
        {
            var rp = new List<(double X, double Z)>(n);
            var rs = new List<double>(n);
            for (int i = 0; i < n; i++)
            {
                rp.Add(footprintCcw[(n - i) % n]);
                rs.Add(edgeSpeed[(n - 1 - i + n) % n]);
            }
            footprintCcw = rp;
            edgeSpeed = rs;
        }

        var edges = new Edge[n];
        for (int i = 0; i < n; i++)
        {
            Vec a = new(footprintCcw[i].X, footprintCcw[i].Z);
            Vec b = new(footprintCcw[(i + 1) % n].X, footprintCcw[(i + 1) % n].Z);
            Vec dir = (b - a).Normalized();
            if (dir.LengthSq < Eps)
                return null;
            Vec inward = new(-dir.Z, dir.X); // CCW interior is to the left
            // speed 0 == a gable/rake edge (infinite pitch, stationary wavefront);
            // the sloped eaves run up to it. Only a negative value is invalid.
            double speed = edgeSpeed[i] >= 0 ? edgeSpeed[i] : 1.0;
            edges[i] = new Edge(i, a, b, dir, inward, speed, inward.Dot(a));
        }

        var verts = new List<Vertex>(n);
        for (int i = 0; i < n; i++)
        {
            Edge left = edges[(i - 1 + n) % n];
            Edge right = edges[i];
            verts.Add(new Vertex(left.B, left, right, 0));
        }
        for (int i = 0; i < n; i++)
        {
            verts[i].Prev = verts[(i - 1 + n) % n];
            verts[i].Next = verts[(i + 1) % n];
        }
        foreach (Vertex v in verts)
            if (!v.ComputeMotion())
                return null;

        var arcs = new List<Arc>();
        var events = new List<Event>();
        foreach (Vertex v in verts)
        {
            QueueEdgeEvent(events, v);
            QueueSplitEvents(events, v, edges);
        }

        var active = new HashSet<Vertex>(verts);
        int guard = 0;
        int guardMax = 80 * n + 400;

        while (events.Count > 0)
        {
            if (++guard > guardMax)
                return null;

            int bi = 0;
            for (int i = 1; i < events.Count; i++)
                if (events[i].Time < events[bi].Time)
                    bi = i;
            Event ev = events[bi];
            events.RemoveAt(bi);

            if (ev.A.Processed || (ev.B != null && ev.B.Processed))
                continue;
            if (ev.Time < -Eps)
                continue;

            if (ev.Kind == EventKind.Edge)
            {
                Vertex a = ev.A, b = ev.B!;
                if (!ReferenceEquals(a.Next, b))
                    continue;
                Vec p = ev.Point;

                arcs.Add(new Arc(a.BornPos, p, a.Left.Index, a.Right.Index));
                arcs.Add(new Arc(b.BornPos, p, b.Left.Index, b.Right.Index));
                a.Processed = b.Processed = true;
                active.Remove(a);
                active.Remove(b);

                var w = new Vertex(p, a.Left, b.Right, ev.Time) { Prev = a.Prev, Next = b.Next };
                a.Prev!.Next = w;
                b.Next!.Prev = w;
                if (!w.ComputeMotion())
                    return null;
                active.Add(w);
                QueueEdgeEvent(events, w);
                QueueEdgeEvent(events, w.Prev!);
                QueueSplitEvents(events, w, edges);
            }
            else
            {
                Vertex v = ev.A;
                Vec p = ev.Point;
                Edge hit = ev.Edge!;

                // Find the wavefront edge of `hit` whose current span contains p:
                // an active vertex t with t.Right == hit and t.Next.Left == hit.
                Vertex? t = null;
                foreach (Vertex c in active)
                {
                    if (c.Right.Index != hit.Index || c.Next == null || c.Next.Left.Index != hit.Index)
                        continue;
                    if (SpanContains(c, c.Next!, p, ev.Time))
                    {
                        t = c;
                        break;
                    }
                }
                if (t == null || ReferenceEquals(t, v) || ReferenceEquals(t.Next, v))
                    continue; // cannot place the split safely -> let other events run

                Vertex headE = t.Next!;
                arcs.Add(new Arc(v.BornPos, p, v.Left.Index, v.Right.Index));
                v.Processed = true;
                active.Remove(v);

                // Loop A: v.Prev -> w1(L=v.Left,R=hit) -> headE -> ... -> v.Prev
                var w1 = new Vertex(p, v.Left, hit, ev.Time) { Prev = v.Prev, Next = headE };
                v.Prev!.Next = w1;
                headE.Prev = w1;
                // Loop B: t -> w2(L=hit,R=v.Right) -> v.Next -> ... -> t
                var w2 = new Vertex(p, hit, v.Right, ev.Time) { Prev = t, Next = v.Next };
                t.Next = w2;
                v.Next!.Prev = w2;

                if (!w1.ComputeMotion() || !w2.ComputeMotion())
                    return null;
                active.Add(w1);
                active.Add(w2);
                QueueEdgeEvent(events, w1);
                QueueEdgeEvent(events, w1.Prev!);
                QueueEdgeEvent(events, w2);
                QueueEdgeEvent(events, w2.Prev!);
                QueueSplitEvents(events, w1, edges);
                QueueSplitEvents(events, w2, edges);
            }
        }

        // Terminal: after splits the wavefront may be several independent loops.
        // Finalize each on its own - a 2-vertex loop is a ridge, 3+ meet at a
        // peak.
        var seen = new HashSet<Vertex>();
        foreach (Vertex s in active)
        {
            if (s.Processed || seen.Contains(s))
                continue;

            var loop = new List<Vertex>();
            Vertex c = s;
            int g = 0;
            do
            {
                if (c.Processed)
                    break;
                loop.Add(c);
                seen.Add(c);
                c = c.Next!;
            }
            while (c != null && !ReferenceEquals(c, s) && ++g < active.Count + 2);

            if (loop.Count == 2)
            {
                arcs.Add(new Arc(loop[0].BornPos, loop[1].BornPos, loop[0].Right.Index, loop[0].Left.Index));
            }
            else if (loop.Count >= 3)
            {
                Vec apex = Centroid(loop);
                foreach (Vertex v in loop)
                    arcs.Add(new Arc(v.BornPos, apex, v.Left.Index, v.Right.Index));
            }
        }

        return ExtractFacets(edges, arcs, n);
    }

    private static List<Facet>? ExtractFacets(Edge[] edges, List<Arc> arcs, int n)
    {
        var facets = new List<Facet>(n);
        for (int i = 0; i < n; i++)
        {
            // Segments bounding facet i: its eave plus every arc touching it.
            var segs = new List<(Vec A, Vec B)> { (edges[i].A, edges[i].B) };
            foreach (Arc arc in arcs)
            {
                if (arc.FaceA == i || arc.FaceB == i)
                {
                    if ((arc.P - arc.Q).LengthSq > 1e-6)
                        segs.Add((arc.P, arc.Q));
                }
            }
            List<(double X, double Z)>? loop = WalkLoop(segs);
            if (loop != null && loop.Count >= 3 && Math.Abs(SignedArea(loop)) > 0.05)
                facets.Add(new Facet(i, loop));
        }

        return facets.Count >= 1 ? facets : null;
    }

    // Order undirected segments into one closed loop by endpoint matching.
    private static List<(double X, double Z)>? WalkLoop(List<(Vec A, Vec B)> segs)
    {
        if (segs.Count < 3)
            return null;

        const double q = 1e3; // quantize for endpoint matching (0.001 ft)
        (long, long) Key(Vec p) => ((long)Math.Round(p.X * q), (long)Math.Round(p.Z * q));

        var adj = new Dictionary<(long, long), List<Vec>>();
        foreach ((Vec A, Vec B) s in segs)
        {
            (adj.TryGetValue(Key(s.A), out List<Vec>? la) ? la : adj[Key(s.A)] = []).Add(s.B);
            (adj.TryGetValue(Key(s.B), out List<Vec>? lb) ? lb : adj[Key(s.B)] = []).Add(s.A);
        }

        Vec start = segs[0].A;
        var loop = new List<Vec> { start };
        Vec prev = start;
        Vec cur = segs[0].B;
        int guard = 0;
        while ((cur - start).LengthSq > 1e-6)
        {
            if (++guard > segs.Count + 4)
                return null;
            loop.Add(cur);
            if (!adj.TryGetValue(Key(cur), out List<Vec>? nbrs))
                return null;
            Vec? nxt = null;
            foreach (Vec cand in nbrs)
            {
                if ((cand - prev).LengthSq > 1e-6)
                {
                    nxt = cand;
                    break;
                }
            }
            if (nxt == null)
                return null;
            prev = cur;
            cur = nxt.Value;
        }

        return loop.Select(v => (v.X, v.Z)).ToList();
    }

    private static bool SpanContains(Vertex a, Vertex b, Vec p, double t)
    {
        Vec pa = a.PositionAt(t);
        Vec pb = b.PositionAt(t);
        Vec d = pb - pa;
        double len = d.LengthSq;
        if (len < Eps)
            return false;
        double s = (p - pa).Dot(d) / len;
        return s > -0.05 && s < 1.05;
    }

    private static Vec Centroid(List<Vertex> vs)
    {
        double x = 0, z = 0;
        foreach (Vertex v in vs)
        {
            Vec p = v.PositionAt(v.StartTime);
            x += p.X;
            z += p.Z;
        }
        return new Vec(x / vs.Count, z / vs.Count);
    }

    private static double SignedAreaXz(IReadOnlyList<(double X, double Z)> poly)
    {
        double a = 0;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            a += (poly[j].X + poly[i].X) * (poly[j].Z - poly[i].Z);
        return a / 2.0;
    }

    private static double SignedArea(List<(double X, double Z)> poly)
    {
        double a = 0;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            a += (poly[j].X + poly[i].X) * (poly[j].Z - poly[i].Z);
        return a / 2.0;
    }

    private static void QueueEdgeEvent(List<Event> events, Vertex v)
    {
        if (v.Processed || v.Next == null || v.Next.Processed)
            return;
        // A 2-vertex loop is a finished ridge (its two edges are parallel and
        // never collide); leave it for the per-loop terminal step.
        if (ReferenceEquals(v.Next.Next, v))
            return;
        if (TryVertexCollision(v, v.Next, out double t, out Vec p) &&
            t > v.StartTime - Eps && t > v.Next.StartTime - Eps)
        {
            events.Add(new Event(EventKind.Edge, t, p, v, v.Next, null));
        }
    }

    private static void QueueSplitEvents(List<Event> events, Vertex v, Edge[] edges)
    {
        if (!v.IsReflex)
            return;
        foreach (Edge e in edges)
        {
            if (ReferenceEquals(e, v.Left) || ReferenceEquals(e, v.Right))
                continue;
            if (TrySplit(v, e, out double t, out Vec p) && t > v.StartTime - Eps)
                events.Add(new Event(EventKind.Split, t, p, v, null, e));
        }
    }

    private static bool TryVertexCollision(Vertex a, Vertex b, out double t, out Vec p)
    {
        t = 0;
        p = default;
        Vec dBase = a.Base - b.Base;
        Vec dVel = a.Vel - b.Vel;
        double denom = dVel.LengthSq;
        if (denom < Eps)
            return false;
        t = -dBase.Dot(dVel) / denom;
        Vec pa = a.Base + a.Vel * t;
        Vec pb = b.Base + b.Vel * t;
        if ((pa - pb).LengthSq > 1e-4)
            return false;
        p = pa;
        return true;
    }

    private static bool TrySplit(Vertex a, Edge e, out double t, out Vec p)
    {
        t = 0;
        p = default;
        double nDotBase = e.N.Dot(a.Base);
        double nDotVel = e.N.Dot(a.Vel);
        double denom = nDotVel - e.Speed;
        if (Math.Abs(denom) < Eps)
            return false;
        t = (e.D0 - nDotBase) / denom;
        if (t < Eps)
            return false;
        p = a.Base + a.Vel * t;
        double along = (p - e.A).Dot(e.Dir);
        double len = (e.B - e.A).Dot(e.Dir);
        return along > -0.5 && along < len + 0.5;
    }

    private enum EventKind { Edge, Split }

    private sealed record Event(EventKind Kind, double Time, Vec Point, Vertex A, Vertex? B, Edge? Edge);

    private sealed record Arc(Vec P, Vec Q, int FaceA, int FaceB);

    private sealed class Edge(int index, Vec a, Vec b, Vec dir, Vec n, double speed, double d0)
    {
        public int Index { get; } = index;
        public Vec A { get; } = a;
        public Vec B { get; } = b;
        public Vec Dir { get; } = dir;
        public Vec N { get; } = n;
        public double Speed { get; } = speed;
        public double D0 { get; } = d0;
    }

    private sealed class Vertex(Vec born, Edge left, Edge right, double startTime)
    {
        public Edge Left { get; } = left;
        public Edge Right { get; } = right;
        public double StartTime { get; } = startTime;
        public Vec BornPos { get; } = born;
        public Vertex? Prev { get; set; }
        public Vertex? Next { get; set; }
        public bool Processed { get; set; }
        public bool IsReflex { get; private set; }
        public Vec Base { get; private set; }
        public Vec Vel { get; private set; }

        public Vec PositionAt(double t) => Base + Vel * t;

        public bool ComputeMotion()
        {
            double a11 = Left.N.X, a12 = Left.N.Z;
            double a21 = Right.N.X, a22 = Right.N.Z;
            double det = a11 * a22 - a12 * a21;
            if (Math.Abs(det) < Eps)
            {
                Vel = Left.N * Left.Speed;
                Base = BornPos - Vel * StartTime;
                IsReflex = false;
                return true;
            }

            double bx0 = Left.D0, by0 = Right.D0;
            double bxv = Left.Speed, byv = Right.Speed;
            Base = new Vec((bx0 * a22 - a12 * by0) / det, (a11 * by0 - bx0 * a21) / det);
            Vel = new Vec((bxv * a22 - a12 * byv) / det, (a11 * byv - bxv * a21) / det);

            double cross = Left.Dir.X * Right.Dir.Z - Left.Dir.Z * Right.Dir.X;
            IsReflex = cross < -Eps;
            return true;
        }
    }

    private readonly record struct Vec(double X, double Z)
    {
        public double LengthSq => X * X + Z * Z;
        public double Dot(Vec o) => X * o.X + Z * o.Z;
        public Vec Normalized()
        {
            double len = Math.Sqrt(LengthSq);
            return len < Eps ? new Vec(0, 0) : new Vec(X / len, Z / len);
        }
        public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Z - b.Z);
        public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Z + b.Z);
        public static Vec operator *(Vec a, double s) => new(a.X * s, a.Z * s);
    }
}
