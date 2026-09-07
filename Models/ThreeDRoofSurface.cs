namespace OurPlanCore;

// A queryable lower-envelope roof surface built from generated roof faces.
// Each face lies on one slope plane (Y = a*X + b*Z + c). HeightAt returns the
// roof-top elevation over a footprint point - the min over the faces that
// cover it (the visible underside-of-roof line walls and gables stop at).
// Pure read model; no persistence. Safe to build from an empty face list.
public sealed class ThreeDRoofSurface
{
    private readonly List<Face> _faces = [];

    private ThreeDRoofSurface()
    {
    }

    public bool HasFaces => _faces.Count > 0;

    // Lowest roof-face vertex = the eave (roof base) elevation. Used to decide
    // which walls are top-of-structure walls that the roof should trim.
    public double BaseElevationFeet { get; private set; }

    // plumbDropFeet lowers individual faces vertically — used when rafters are
    // enabled: the stored plane is the TOP of the rafters, so walls must trim
    // to the rafter underside (plane minus the member's plumb depth).
    public static ThreeDRoofSurface Build(
        IEnumerable<ThreeDRoofPlane> planes,
        Func<ThreeDRoofPlane, double>? plumbDropFeet = null)
    {
        var surface = new ThreeDRoofSurface();
        double minY = double.PositiveInfinity;
        foreach (ThreeDRoofPlane plane in planes)
        {
            if (!string.Equals(plane.Kind, "roof_face_envelope", StringComparison.OrdinalIgnoreCase))
                continue;
            if (plane.Points.Count < 3)
                continue;

            if (TryFitPlane(plane.Points, out double a, out double b, out double c))
            {
                double drop = Math.Max(0, plumbDropFeet?.Invoke(plane) ?? 0);
                surface._faces.Add(new Face(
                    plane.Points.Select(p => (p.XFeet, p.ZFeet)).ToList(),
                    a, b, c - drop));
                foreach (ThreeDRoofVertex v in plane.Points)
                    minY = Math.Min(minY, v.YFeet - drop);
            }
        }

        surface.BaseElevationFeet = double.IsPositiveInfinity(minY) ? 0 : minY;
        return surface;
    }

    // Roof-top elevation over (x,z), or null when the point is outside every
    // roof face. Lower envelope -> the smallest covering-face height.
    public double? HeightAt(double xFeet, double zFeet)
    {
        double best = double.PositiveInfinity;
        foreach (Face face in _faces)
        {
            if (!PointInPolygon(xFeet, zFeet, face.Polygon))
                continue;
            double y = face.A * xFeet + face.B * zFeet + face.C;
            if (y < best)
                best = y;
        }

        return double.IsPositiveInfinity(best) ? null : best;
    }

    private static bool TryFitPlane(IReadOnlyList<ThreeDRoofVertex> points, out double a, out double b, out double c)
    {
        a = b = c = 0;
        ThreeDRoofVertex p0 = points[0];
        for (int i = 1; i < points.Count - 1; i++)
        {
            ThreeDRoofVertex p1 = points[i];
            ThreeDRoofVertex p2 = points[i + 1];
            double ux = p1.XFeet - p0.XFeet, uy = p1.YFeet - p0.YFeet, uz = p1.ZFeet - p0.ZFeet;
            double vx = p2.XFeet - p0.XFeet, vy = p2.YFeet - p0.YFeet, vz = p2.ZFeet - p0.ZFeet;
            // Plane normal in real X/Y/Z space; ny is the height component.
            double nx = uy * vz - uz * vy;
            double ny = uz * vx - ux * vz;
            double nz = ux * vy - uy * vx;
            if (Math.Abs(ny) < 1e-6)
                continue;

            // ny*Y + nx*X + nz*Z = d  ->  Y = a*X + b*Z + c
            double d = nx * p0.XFeet + ny * p0.YFeet + nz * p0.ZFeet;
            a = -nx / ny;
            b = -nz / ny;
            c = d / ny;
            return true;
        }

        return false;
    }

    private static bool PointInPolygon(double x, double z, IReadOnlyList<(double X, double Z)> poly)
    {
        bool inside = false;
        const double pad = 0.02;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            double xi = poly[i].X, zi = poly[i].Z;
            double xj = poly[j].X, zj = poly[j].Z;
            bool crosses = zi > z != zj > z &&
                           x < (xj - xi) * (z - zi) / (zj - zi + 1e-12) + xi;
            if (crosses)
                inside = !inside;
        }

        if (inside)
            return true;

        // Pad the boundary slightly so walls sitting exactly on an eave edge
        // still register as covered (avoids a 1-sample gap at the eave).
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            if (DistanceToSegment(x, z, poly[i].X, poly[i].Z, poly[j].X, poly[j].Z) <= pad)
                return true;
        }

        return false;
    }

    private static double DistanceToSegment(double px, double pz, double ax, double az, double bx, double bz)
    {
        double dx = bx - ax, dz = bz - az;
        double len2 = dx * dx + dz * dz;
        if (len2 <= 1e-12)
            return Math.Sqrt((px - ax) * (px - ax) + (pz - az) * (pz - az));
        double t = Math.Clamp(((px - ax) * dx + (pz - az) * dz) / len2, 0, 1);
        double cx = ax + dx * t, cz = az + dz * t;
        return Math.Sqrt((px - cx) * (px - cx) + (pz - cz) * (pz - cz));
    }

    private readonly record struct Face(List<(double X, double Z)> Polygon, double A, double B, double C);
}
