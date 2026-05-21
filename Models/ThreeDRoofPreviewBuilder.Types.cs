namespace OurPlaneCore;

public static partial class ThreeDRoofPreviewBuilder
{
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

        // Revit per-edge eave projection beyond the wall, in feet. Max across
        // merged coplanar guides. 0 = eave sits on the wall line (no overhang).
        public double OverhangFeet { get; set; }

        // Signed height of this eave's infinite slope plane (linear, so the
        // lower envelope partitions into clean polygons). A partial eave is
        // confined to its straight-skeleton cell by the bisector wedge clip
        // in BuildEnvelopeFaces, so it cannot overshoot into another wing.
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
