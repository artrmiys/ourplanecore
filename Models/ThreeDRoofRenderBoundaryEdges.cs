namespace OurPlaneCore;

internal sealed class ThreeDRoofRenderBoundaryEdges
{
    private const double QuantizeFeet = 0.02;
    private readonly HashSet<EdgeKey> _boundaryEdges;

    private ThreeDRoofRenderBoundaryEdges(HashSet<EdgeKey> boundaryEdges)
    {
        _boundaryEdges = boundaryEdges;
    }

    public static ThreeDRoofRenderBoundaryEdges Build(IEnumerable<ThreeDRoofPlane> planes)
    {
        var counts = new Dictionary<EdgeKey, int>();
        foreach (ThreeDRoofPlane plane in planes)
        {
            if (plane.Points.Count < 3)
                continue;

            for (int i = 0; i < plane.Points.Count; i++)
            {
                EdgeKey key = EdgeKey.Create(plane.Points[i], plane.Points[(i + 1) % plane.Points.Count]);
                counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
            }
        }

        return new ThreeDRoofRenderBoundaryEdges(
            counts
                .Where(pair => pair.Value == 1)
                .Select(pair => pair.Key)
                .ToHashSet());
    }

    public bool IsBoundary(ThreeDRoofVertex a, ThreeDRoofVertex b) =>
        _boundaryEdges.Contains(EdgeKey.Create(a, b));

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
