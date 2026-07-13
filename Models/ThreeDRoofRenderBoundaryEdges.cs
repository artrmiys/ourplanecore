namespace OurPlanCore;

internal sealed class ThreeDRoofRenderBoundaryEdges
{
    private const double QuantizeFeet = 0.02;

    // How far apart two edges can sit and still count as "the same line": a
    // sliver between two clipped envelope pieces is only a few hundredths of a
    // foot wide, so this perpendicular slack lets near-coincident seam edges
    // recognise each other while staying well under any real slope spacing.
    private const double CollinearPerpFeet = 0.08;

    // Shared run two collinear edges must overlap before they cancel. Above a
    // touch-at-a-vertex point but well below a drawable seam, so consecutive
    // edges of one polygon are not mistaken for a shared seam.
    private const double MinOverlapFeet = 0.05;

    private readonly HashSet<GroupedEdgeKey> _boundaryEdges;

    private ThreeDRoofRenderBoundaryEdges(HashSet<GroupedEdgeKey> boundaryEdges)
    {
        _boundaryEdges = boundaryEdges;
    }

    // An interior plane seam (ridge/hip/valley, or a triangulation diagonal
    // inside one slope) is shared by two clipped pieces and must not be drawn.
    // Exact endpoint matching is not enough: the envelope is built per triangle
    // cell, so the seam is often T-split - one piece has edge P->Q while the
    // neighbour has P->M and M->Q. Those never match exactly and used to render
    // as 2-3 parallel bars over the slope. Instead an edge is treated as a
    // boundary only when no other piece has a collinear, overlapping edge along
    // it; that cancels T-split seams while the true outer silhouette (touched by
    // a single piece) survives.
    public static ThreeDRoofRenderBoundaryEdges Build(IEnumerable<ThreeDRoofPlane> planes)
    {
        var byGroup = new Dictionary<string, List<Seg>>();
        foreach (ThreeDRoofPlane plane in planes)
        {
            if (plane.Points.Count < 3)
                continue;

            string groupId = plane.RoofGroupId ?? "";
            if (!byGroup.TryGetValue(groupId, out List<Seg>? segs))
                byGroup[groupId] = segs = [];

            for (int i = 0; i < plane.Points.Count; i++)
                segs.Add(new Seg(plane.Points[i], plane.Points[(i + 1) % plane.Points.Count]));
        }

        var boundary = new HashSet<GroupedEdgeKey>();
        foreach ((string groupId, List<Seg> segs) in byGroup)
        {
            for (int i = 0; i < segs.Count; i++)
            {
                if (HasCollinearOverlap(segs, i))
                    continue;
                boundary.Add(new GroupedEdgeKey(groupId, EdgeKey.Create(segs[i].A, segs[i].B)));
            }
        }

        return new ThreeDRoofRenderBoundaryEdges(boundary);
    }

    public bool IsBoundary(string groupId, ThreeDRoofVertex a, ThreeDRoofVertex b) =>
        _boundaryEdges.Contains(new GroupedEdgeKey(groupId ?? "", EdgeKey.Create(a, b)));

    private static bool HasCollinearOverlap(List<Seg> segs, int index)
    {
        Seg e = segs[index];
        if (e.Length < 1e-6)
            return false;

        for (int j = 0; j < segs.Count; j++)
        {
            if (j == index)
                continue;
            if (Overlaps(e, segs[j]))
                return true;
        }

        return false;
    }

    private static bool Overlaps(Seg e, Seg other)
    {
        if (other.Length < 1e-6)
            return false;

        // Both endpoints of 'other' must lie on e's infinite line.
        if (PerpDistance(e, other.AX, other.AZ) > CollinearPerpFeet ||
            PerpDistance(e, other.BX, other.BZ) > CollinearPerpFeet)
        {
            return false;
        }

        // Project 'other' onto e's unit direction and intersect the [0,len]
        // spans; a shared run longer than the touch tolerance means a seam.
        double ta = Project(e, other.AX, other.AZ);
        double tb = Project(e, other.BX, other.BZ);
        double lo = Math.Max(0, Math.Min(ta, tb));
        double hi = Math.Min(e.Length, Math.Max(ta, tb));
        return hi - lo > MinOverlapFeet;
    }

    private static double PerpDistance(Seg e, double px, double pz)
    {
        // |cross(dir, point - A)| with dir normalised.
        double cross = e.DirX * (pz - e.AZ) - e.DirZ * (px - e.AX);
        return Math.Abs(cross);
    }

    private static double Project(Seg e, double px, double pz) =>
        e.DirX * (px - e.AX) + e.DirZ * (pz - e.AZ);

    private readonly struct Seg
    {
        public readonly ThreeDRoofVertex A;
        public readonly ThreeDRoofVertex B;
        public readonly double AX;
        public readonly double AZ;
        public readonly double BX;
        public readonly double BZ;
        public readonly double Length;
        public readonly double DirX; // unit direction
        public readonly double DirZ;

        public Seg(ThreeDRoofVertex a, ThreeDRoofVertex b)
        {
            A = a;
            B = b;
            AX = a.XFeet;
            AZ = a.ZFeet;
            BX = b.XFeet;
            BZ = b.ZFeet;
            double dx = BX - AX;
            double dz = BZ - AZ;
            Length = Math.Sqrt(dx * dx + dz * dz);
            if (Length > 1e-9)
            {
                DirX = dx / Length;
                DirZ = dz / Length;
            }
            else
            {
                DirX = 0;
                DirZ = 0;
            }
        }
    }

    private readonly record struct GroupedEdgeKey(string GroupId, EdgeKey Edge);

    private readonly record struct EdgeKey(PointKey A, PointKey B)
    {
        public static EdgeKey Create(ThreeDRoofVertex a, ThreeDRoofVertex b)
        {
            PointKey pa = PointKey.Create(a);
            PointKey pb = PointKey.Create(b);
            return pa.CompareTo(pb) <= 0 ? new EdgeKey(pa, pb) : new EdgeKey(pb, pa);
        }
    }

    private readonly record struct PointKey(long X, long Z) : IComparable<PointKey>
    {
        public static PointKey Create(ThreeDRoofVertex point) =>
            new(
                (long)Math.Round(point.XFeet / QuantizeFeet),
                (long)Math.Round(point.ZFeet / QuantizeFeet));

        public int CompareTo(PointKey other)
        {
            int x = X.CompareTo(other.X);
            return x != 0 ? x : Z.CompareTo(other.Z);
        }
    }
}
