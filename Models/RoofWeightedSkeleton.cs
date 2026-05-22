namespace OurPlaneCore;

// Weighted straight skeleton for footprint roofs (Revit "roof by footprint"
// with a per-edge slope; the multiplicatively-weighted straight skeleton).
//
// Each eave edge sweeps inward at speed 1/pitch. Because height = pitch * inward
// run, the sweep "time" equals roof height, so every skeleton node sits at a
// single height shared by all faces meeting there - adjacent slopes of ANY pitch
// mix join at one elevation along ridges/hips/valleys (no side gap).
//
// Build returns one plan polygon per input edge (the roof facet footprint) or
// null when the event simulation cannot complete (caller falls back to the
// legacy lower-envelope builder, so existing roofs never regress).
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

        var edges = new Edge[n];
        for (int i = 0; i < n; i++)
        {
            Vec a = new(footprintCcw[i].X, footprintCcw[i].Z);
            Vec b = new(footprintCcw[(i + 1) % n].X, footprintCcw[(i + 1) % n].Z);
            Vec dir = (b - a).Normalized();
            if (dir.LengthSq < Eps)
                return null;
            // CCW interior is to the left; inward normal of dir (dx,dz) = (-dz,dx).
            Vec inward = new(-dir.Z, dir.X);
            double speed = edgeSpeed[i] > Eps ? edgeSpeed[i] : 1.0;
            edges[i] = new Edge(i, a, b, dir, inward, speed, inward.Dot(a));
        }

        // Per-facet ordered boundary chains (left chain grows from edge start,
        // right chain from edge end); the facet is leftChain + reversed right.
        var leftChain = new List<Vec>[n];
        var rightChain = new List<Vec>[n];
        for (int i = 0; i < n; i++)
        {
            leftChain[i] = [edges[i].A];
            rightChain[i] = [edges[i].B];
        }

        // Active list of wavefront vertices (circular, doubly linked).
        var verts = new List<Vertex>(n);
        for (int i = 0; i < n; i++)
        {
            Edge left = edges[(i - 1 + n) % n];
            Edge right = edges[i];
            var v = new Vertex(edges[(i - 1 + n) % n].B, left, right, 0);
            verts.Add(v);
        }
        for (int i = 0; i < n; i++)
        {
            verts[i].Prev = verts[(i - 1 + n) % n];
            verts[i].Next = verts[(i + 1) % n];
        }
        foreach (Vertex v in verts)
            if (!v.ComputeMotion())
                return null;

        var events = new List<Event>();
        foreach (Vertex v in verts)
            QueueEdgeEvent(events, v);
        foreach (Vertex v in verts)
            QueueSplitEvents(events, v, edges);

        int active = n;
        int guard = 0;
        int guardMax = 50 * n + 200;

        while (active > 2 && events.Count > 0)
        {
            if (++guard > guardMax)
                return null;

            int bi = -1;
            for (int i = 0; i < events.Count; i++)
                if (bi < 0 || events[i].Time < events[bi].Time)
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

                // p closes edge a.Right (== b.Left): node shared by facets
                // a.Left, a.Right(=b.Left), b.Right.
                AddToFacet(rightChain, a.Left.Index, p);
                AddToFacet(leftChain, a.Right.Index, p);
                AddToFacet(rightChain, a.Right.Index, p); // a.Right == b.Left
                AddToFacet(leftChain, b.Right.Index, p);

                a.Processed = true;
                b.Processed = true;
                var w = new Vertex(p, a.Left, b.Right, ev.Time) { Prev = a.Prev, Next = b.Next };
                a.Prev!.Next = w;
                b.Next!.Prev = w;
                if (!w.ComputeMotion())
                    return null;
                active--;
                QueueEdgeEvent(events, w);
                QueueEdgeEvent(events, w.Prev!);
                if (w.IsReflex)
                    QueueSplitEvents(events, w, edges);
            }
            else
            {
                // Split event: reflex vertex a hits opposite edge ev.Edge at p.
                Vertex a = ev.A;
                Vec p = ev.Point;
                Edge hit = ev.Edge!;
                AddToFacet(rightChain, a.Left.Index, p);
                AddToFacet(leftChain, a.Right.Index, p);
                // The split node also bounds the hit facet on both sides.
                AddToFacet(leftChain, hit.Index, p);
                AddToFacet(rightChain, hit.Index, p);
                // Conservative: splitting the LAV correctly needs locating the
                // opposite chain link. To stay robust we bail to fallback when a
                // split would be needed, rather than risk a malformed loop.
                return null;
            }
        }

        // Remaining active vertices converge to the apex region: dump them into
        // each incident facet.
        Vertex? start = verts.FirstOrDefault(v => !v.Processed);
        if (start != null)
        {
            Vertex cur = start;
            for (int i = 0; i < active; i++)
            {
                Vec p = cur.PositionAt(cur.StartTime); // last known position
                AddToFacet(rightChain, cur.Left.Index, p);
                AddToFacet(leftChain, cur.Right.Index, p);
                cur = cur.Next!;
                if (ReferenceEquals(cur, start))
                    break;
            }
        }

        var facets = new List<Facet>(n);
        for (int i = 0; i < n; i++)
        {
            var poly = new List<(double X, double Z)>();
            foreach (Vec v in leftChain[i])
                poly.Add((v.X, v.Z));
            for (int k = rightChain[i].Count - 1; k >= 0; k--)
                poly.Add((rightChain[i][k].X, rightChain[i][k].Z));
            poly = DedupeClose(poly);
            if (poly.Count >= 3)
                facets.Add(new Facet(i, poly));
        }

        return facets.Count >= 1 ? facets : null;
    }

    private static void AddToFacet(List<Vec>[] chain, int edgeIndex, Vec p)
    {
        List<Vec> list = chain[edgeIndex];
        if (list.Count == 0 || (list[^1] - p).LengthSq > Eps)
            list.Add(p);
    }

    private static List<(double X, double Z)> DedupeClose(List<(double X, double Z)> poly)
    {
        var clean = new List<(double X, double Z)>();
        foreach ((double X, double Z) p in poly)
        {
            if (clean.Count == 0)
            {
                clean.Add(p);
                continue;
            }
            double dx = p.X - clean[^1].X, dz = p.Z - clean[^1].Z;
            if (dx * dx + dz * dz > 1e-6)
                clean.Add(p);
        }
        if (clean.Count > 1)
        {
            double dx = clean[0].X - clean[^1].X, dz = clean[0].Z - clean[^1].Z;
            if (dx * dx + dz * dz <= 1e-6)
                clean.RemoveAt(clean.Count - 1);
        }
        return clean;
    }

    private static void QueueEdgeEvent(List<Event> events, Vertex v)
    {
        if (v.Processed || v.Next == null || v.Next.Processed)
            return;
        if (TryVertexCollision(v, v.Next, out double t, out Vec p) && t > v.StartTime - Eps && t > v.Next.StartTime - Eps)
            events.Add(new Event(EventKind.Edge, t, p, v, v.Next, null));
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

    // Two adjacent moving vertices meet when their trajectories coincide.
    private static bool TryVertexCollision(Vertex a, Vertex b, out double t, out Vec p)
    {
        t = 0;
        p = default;
        Vec dBase = a.Base - b.Base;
        Vec dVel = a.Vel - b.Vel;
        double denom = dVel.LengthSq;
        if (denom < Eps)
            return false;
        t = -(dBase.Dot(dVel)) / denom;
        Vec pa = a.Base + a.Vel * t;
        Vec pb = b.Base + b.Vel * t;
        if ((pa - pb).LengthSq > 1e-4)
            return false;
        p = pa;
        return true;
    }

    // Reflex vertex a sweeps inward; it splits the wavefront when it reaches the
    // moving line of edge e. Solve a.PositionAt(t) on e's offset line.
    private static bool TrySplit(Vertex a, Edge e, out double t, out Vec p)
    {
        t = 0;
        p = default;
        // e offset line at time s: e.N . x = e.D0 + e.Speed * s. The vertex is on
        // it when e.N . a.PositionAt(t) = e.D0 + e.Speed * t.
        double nDotBase = e.N.Dot(a.Base);
        double nDotVel = e.N.Dot(a.Vel);
        double denom = nDotVel - e.Speed;
        if (Math.Abs(denom) < Eps)
            return false;
        t = (e.D0 - nDotBase) / denom;
        if (t < Eps)
            return false;
        p = a.Base + a.Vel * t;
        // The hit point must fall within the (moving) edge's extent.
        double along = (p - (e.A + e.Dir * 0)).Dot(e.Dir);
        double len = (e.B - e.A).Dot(e.Dir);
        if (along < -0.5 || along > len + 0.5)
            return false;
        return true;
    }

    private enum EventKind { Edge, Split }

    private sealed record Event(EventKind Kind, double Time, Vec Point, Vertex A, Vertex? B, Edge? Edge);

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

    private sealed class Vertex(Vec start, Edge left, Edge right, double startTime)
    {
        public Edge Left { get; } = left;
        public Edge Right { get; } = right;
        public double StartTime { get; } = startTime;
        public Vertex? Prev { get; set; }
        public Vertex? Next { get; set; }
        public bool Processed { get; set; }
        public bool IsReflex { get; private set; }
        public Vec Base { get; private set; } = start;
        public Vec Vel { get; private set; }

        public Vec PositionAt(double t) => Base + Vel * (t - StartTime + StartTime); // Base already at t=0 frame

        // Solve the vertex as the intersection of its two edges' moving offset
        // lines: N_l . x = D_l + speed_l t ; N_r . x = D_r + speed_r t.
        public bool ComputeMotion()
        {
            double a11 = Left.N.X, a12 = Left.N.Z;
            double a21 = Right.N.X, a22 = Right.N.Z;
            double det = a11 * a22 - a12 * a21;
            if (Math.Abs(det) < Eps)
            {
                // Collinear edges: vertex translates along the shared normal.
                Vel = Left.N * Left.Speed;
                Base = start;
                IsReflex = false;
                return true;
            }

            double bx0 = Left.D0, by0 = Right.D0;
            double bxv = Left.Speed, byv = Right.Speed;
            // Base (t=0) and velocity from Cramer's rule.
            Base = new Vec((bx0 * a22 - a12 * by0) / det, (a11 * by0 - bx0 * a21) / det);
            Vel = new Vec((bxv * a22 - a12 * byv) / det, (a11 * byv - bxv * a21) / det);

            // Reflex if the turn from Left.Dir to Right.Dir is a right turn (CCW
            // interior). cross(Left.Dir, Right.Dir) < 0 => reflex.
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
