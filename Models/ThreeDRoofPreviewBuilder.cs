namespace OurPlanCore;

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
        AddEaveOverhangFaces(result, envelopeFaces, envelopePlanes);
        AddEnvelopeSeams(result, envelopeFaces, footprint);
        AddRakeEndFaces(result, boundary, boundaryGuides, slopePlanes, roofBase);

        if (envelopeFaces.Count > 0)
        {
            string message = slopePlanes.Count == 1
                ? "Roof face generated from one slope-defining eave edge."
                : $"Roof faces generated from {slopePlanes.Count} slope-defining eave edge(s).";
            result.Messages.Add(message + " Ridges, hips, and valleys are the exact seams where adjacent eave planes meet.");
        }
    }

    private const double EaveHeightTolerance = 0.06;

    // Additive eave overhang: for every face edge sitting on the eave line
    // (both ends at plane height ~0), extend a strip outward by the plane's
    // OverhangFeet along the downslope plan normal. The strip's outer edge
    // drops below the wall plate exactly as a real eave projection does. Zero
    // overhang adds nothing, so existing roofs are unchanged. v1 limitation:
    // adjacent overhangs do not miter at hips/valleys (flagged in messages).
    private static void AddEaveOverhangFaces(
        ThreeDRoofPreviewBuildResult result,
        IReadOnlyList<EnvelopeFace> faces,
        List<SlopePlane> planes)
    {
        bool anyOverhang = false;
        foreach (EnvelopeFace face in faces)
        {
            SlopePlane plane = face.Plane;
            double overhang = plane.OverhangFeet;
            if (overhang <= 0.001)
                continue;

            double grad = Math.Sqrt(plane.A * plane.A + plane.B * plane.B);
            if (grad < 1e-6)
                continue;
            double nx = -plane.A / grad;
            double nz = -plane.B / grad;
            int planeIndex = planes.IndexOf(plane);

            List<P2> poly = face.Points;
            for (int i = 0; i < poly.Count; i++)
            {
                P2 a = poly[i];
                P2 b = poly[(i + 1) % poly.Count];
                if (Math.Abs(plane.HeightAt(a)) > EaveHeightTolerance ||
                    Math.Abs(plane.HeightAt(b)) > EaveHeightTolerance)
                {
                    continue;
                }

                if (Distance(a, b) < 0.1)
                    continue;

                P2 outerA = new(a.X + nx * overhang, a.Z + nz * overhang);
                P2 outerB = new(b.X + nx * overhang, b.Z + nz * overhang);
                result.Planes.Add(new ThreeDRoofPlane
                {
                    Kind = "roof_face_envelope",
                    Label = $"Eave overhang from {plane.Label}",
                    Color = RoofFaceColor(planeIndex),
                    Opacity = 0.68,
                    Points =
                    [
                        Vertex(a.X, face.RoofBase + Math.Max(0, plane.HeightAt(a)), a.Z),
                        Vertex(b.X, face.RoofBase + Math.Max(0, plane.HeightAt(b)), b.Z),
                        Vertex(outerB.X, face.RoofBase + plane.HeightAt(outerB), outerB.Z),
                        Vertex(outerA.X, face.RoofBase + plane.HeightAt(outerA), outerA.Z),
                    ],
                    SourceGuideIds = plane.GuideIds.ToList(),
                    Message = "Eave overhang projected beyond the wall line.",
                });
                anyOverhang = true;
            }
        }

        if (anyOverhang)
            result.Messages.Add("Eave overhangs projected beyond the walls; at hips/valleys they are not mitered together yet.");
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
            feetPerPdf)
        {
            OverhangFeet = Math.Max(0, guide.OverhangFeet),
        };
        return true;
    }
}
