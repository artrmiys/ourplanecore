namespace OurPlanCore;

// Roof takeoff numbers derived from generated geometry: true sloped surface
// area (3D), flat plan area, and ridge/hip/valley/eave run lengths. Read-only;
// computed from the same faces and seam guides the 3D viewer renders.
public sealed class ThreeDRoofQuantities
{
    public double SlopedAreaSqFt { get; private set; }
    public double PlanAreaSqFt { get; private set; }
    public double RidgeLengthFeet { get; private set; }
    public double HipLengthFeet { get; private set; }
    public double ValleyLengthFeet { get; private set; }
    public double EaveLengthFeet { get; private set; }
    public int FaceCount { get; private set; }
    public bool HasRoof => FaceCount > 0 || SlopedAreaSqFt > 0;

    public static ThreeDRoofQuantities Compute(
        IEnumerable<ThreeDRoofPlane> planes,
        IEnumerable<ThreeDRoofGuide> guides)
    {
        var q = new ThreeDRoofQuantities();
        foreach (ThreeDRoofPlane plane in planes)
        {
            if (!string.Equals(plane.Kind, "roof_face_envelope", StringComparison.OrdinalIgnoreCase))
                continue;
            if (plane.Points.Count < 3)
                continue;

            q.FaceCount++;
            q.SlopedAreaSqFt += SurfaceArea3D(plane.Points);
            q.PlanAreaSqFt += Math.Abs(PlanArea(plane.Points));
        }

        foreach (ThreeDRoofGuide guide in guides)
        {
            if (string.Equals(guide.Status, ThreeDRoofPreviewBuilder.GeneratedSeamStatus, StringComparison.OrdinalIgnoreCase))
            {
                double length = PolylineLength3D(guide.Points);
                switch (ThreeDRoofGuideKinds.Normalize(guide.Kind))
                {
                    case ThreeDRoofGuideKinds.Hip: q.HipLengthFeet += length; break;
                    case ThreeDRoofGuideKinds.Valley: q.ValleyLengthFeet += length; break;
                    default: q.RidgeLengthFeet += length; break;
                }
            }
            else if (guide.DefinesSlope)
            {
                q.EaveLengthFeet += PolylineLengthPlan(guide.Points);
            }
        }

        return q;
    }

    // True 3D polygon area via the magnitude of the summed edge cross products.
    private static double SurfaceArea3D(IReadOnlyList<ThreeDRoofVertex> p)
    {
        double nx = 0, ny = 0, nz = 0;
        for (int i = 0; i < p.Count; i++)
        {
            ThreeDRoofVertex a = p[i];
            ThreeDRoofVertex b = p[(i + 1) % p.Count];
            nx += a.YFeet * b.ZFeet - a.ZFeet * b.YFeet;
            ny += a.ZFeet * b.XFeet - a.XFeet * b.ZFeet;
            nz += a.XFeet * b.YFeet - a.YFeet * b.XFeet;
        }

        return 0.5 * Math.Sqrt(nx * nx + ny * ny + nz * nz);
    }

    private static double PlanArea(IReadOnlyList<ThreeDRoofVertex> p)
    {
        double area = 0;
        for (int i = 0; i < p.Count; i++)
        {
            ThreeDRoofVertex a = p[i];
            ThreeDRoofVertex b = p[(i + 1) % p.Count];
            area += a.XFeet * b.ZFeet - b.XFeet * a.ZFeet;
        }

        return area / 2.0;
    }

    private static double PolylineLength3D(IReadOnlyList<ThreeDRoofGuidePoint> pts)
    {
        double total = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            double dx = pts[i].XFeet - pts[i - 1].XFeet;
            double dy = pts[i].YFeet - pts[i - 1].YFeet;
            double dz = pts[i].ZFeet - pts[i - 1].ZFeet;
            total += Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        return total;
    }

    private static double PolylineLengthPlan(IReadOnlyList<ThreeDRoofGuidePoint> pts)
    {
        double total = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            double dx = pts[i].XFeet - pts[i - 1].XFeet;
            double dz = pts[i].ZFeet - pts[i - 1].ZFeet;
            total += Math.Sqrt(dx * dx + dz * dz);
        }

        return total;
    }
}
