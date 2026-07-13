using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore;

/// <summary>
/// Builds and saves the sample job's 3D model headlessly so the sample opens with a finished
/// massing: auto walls + floor slab and a cross-gabled roof. The roof is one combined footprint
/// (the main rectangle plus three gable wings protruding from the front), so the engine's valley
/// generator cuts the wings cleanly into the main roof. The two end walls and each wing front are
/// Rake gables (triangular gable walls); everything else slopes.
/// </summary>
internal static class SampleThreeDModelBuilder
{
    private const double Pitch = 0.5; // 6/12
    private const double Tol = 0.6;   // feet

    public static void BuildAndSave(OurPlanCoreJob job, PageInfo page, double scaleMetersPerPt)
    {
        double Resolver(Measurement _) => scaleMetersPerPt;

        ThreeDWallAutoBuildResult walls = ThreeDWallAutoBuilder.Build(job, Resolver);
        var model = new ThreeDWallModel { Source = "sample_guide" };
        model.Walls.AddRange(walls.Model.Walls);
        model.Levels.AddRange(walls.Model.Levels);
        model.Slabs.AddRange(walls.Model.Slabs.Where(slab =>
            !string.Equals(slab.LevelKey, "roof", StringComparison.OrdinalIgnoreCase)));

        double roofElevation = model.Levels.Count > 0
            ? model.Levels.Max(level => level.BaseElevationFeet + level.HeightFeet)
            : 9.0;

        var g = SamplePlanGeometry.Instance;

        // Single rectangular roof: ridge runs along the long axis, the west short end is a Rake
        // gable (vertical triangular gable wall) and the east short end hips (slopes down).
        var source = new ThreeDRoofFootprintSource
        {
            Item = new TakeoffItem { Name = "Main Roof", Color = "#8BC34A", MeasurementType = "area" },
            Measurement = new Measurement
            {
                Name = "Main Roof",
                MType = "area",
                Color = "#8BC34A",
                PageFolder = page.FolderPath,
                ScaleMetersPerPt = scaleMetersPerPt,
                Points = [.. g.RoofFootprintPolygon],
            },
        };

        ThreeDRoofFootprintBuildResult roof = ThreeDRoofFootprintBuildService.Build(
            [source], Resolver, roofElevation, Pitch);
        if (roof.Slabs.Count == 0)
            return;

        ClassifyGableHipEdges(roof.Guides);
        model.Slabs.AddRange(roof.Slabs);
        model.RoofGuides.AddRange(roof.Guides);

        ThreeDRoofPreviewBuildResult preview = ThreeDRoofPreviewBuilder.BuildPreview(model);
        model.RoofPlanes.AddRange(preview.Planes);
        model.RoofGuides.AddRange(preview.Guides.Where(guide =>
            string.Equals(guide.Status, ThreeDRoofPreviewBuilder.GeneratedSeamStatus, StringComparison.OrdinalIgnoreCase)));

        ThreeDModelStore.Save(job, model);
    }

    // Rectangular roof: the west short end (min X) is a Rake gable; every other edge slopes
    // (Eave), so the long sides and the east end form a hip down to that end.
    private static void ClassifyGableHipEdges(List<ThreeDRoofGuide> guides)
    {
        var pts = guides.SelectMany(guide => guide.Points).ToList();
        if (pts.Count == 0)
            return;

        double minX = pts.Min(p => p.XFeet);
        foreach (ThreeDRoofGuide guide in guides)
        {
            if (guide.Points.Count < 2)
            {
                SetEdge(guide, slope: true);
                continue;
            }

            ThreeDRoofGuidePoint a = guide.Points[0];
            ThreeDRoofGuidePoint b = guide.Points[^1];
            bool vertical = Math.Abs(a.XFeet - b.XFeet) <= Math.Abs(a.ZFeet - b.ZFeet);
            double avgX = (a.XFeet + b.XFeet) / 2;
            bool westGableEnd = vertical && Math.Abs(avgX - minX) <= Tol;
            SetEdge(guide, slope: !westGableEnd);
        }
    }

    private static void SetEdge(ThreeDRoofGuide guide, bool slope)
    {
        guide.DefinesSlope = slope;
        guide.Kind = slope ? ThreeDRoofGuideKinds.Eave : ThreeDRoofGuideKinds.Rake;
        guide.PitchRisePerFoot = slope ? Pitch : 0;
        guide.Color = ThreeDRoofGuideKinds.Color(guide.Kind);
    }
}
