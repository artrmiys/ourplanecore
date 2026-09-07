using System;
using System.Collections.Generic;
using System.Linq;
using OurPlanCore;

// Rafter layout on 3D roof planes: a 6/12 gable face with known dimensions
// must produce the textbook count, slope-corrected lengths, lumber rounding
// and plumb drop; flat faces produce nothing; anchors resolve faces.
internal static class ThreeDRoofRafterServiceTests
{
    public static void GableFaceLayoutMatchesTextbookNumbers()
    {
        // Eave along X at z=0 (y=10), ridge at z=8 (y=14): slope 0.5/ft = 6/12,
        // 24 ft of eave, 8 ft of horizontal run per rafter.
        ThreeDRoofPlane plane = Face(
            (0, 10, 0), (24, 10, 0), (24, 14, 8), (0, 14, 8));
        var settings = new ThreeDRoofRafterSettings
        {
            RoofGroupId = plane.RoofGroupId,
            Mode = ThreeDRoofRafterSettings.ModeAll,
            SpacingInches = 16,
            MemberSize = "2x10",
            LengthRounding = JoistTakeoffCalculator.RoundingNearestEvenFoot,
        };

        ThreeDRoofRafterPlaneLayout? layout = ThreeDRoofRafterService.ComputeForPlane(plane, settings);

        AssertTrue(layout != null, "gable face must produce a rafter layout");
        AssertTrue(layout!.Bars.Count == 19,
            $"24 ft of eave at 16\" o.c. is 19 rafters, got {layout.Bars.Count}");
        AssertClose(0.5, layout.PitchRisePerFoot, "fitted slope must be 6/12", 0.001);

        double expectedRaw = 8.0 * Math.Sqrt(1.0 + 0.25); // run x slope factor
        foreach (ThreeDRoofRafterBar bar in layout.Bars)
        {
            AssertClose(expectedRaw, bar.RawLengthFeet, "slope length must be run x sqrt(1+m^2)", 0.02);
            AssertClose(10.0, bar.OrderLengthFeet, "8.94 ft rounds up to a 10 ft board", 0.001);
        }

        // Bars must climb the slope: one end on the eave height, one on the ridge.
        ThreeDRoofRafterBar first = layout.Bars[0];
        double low = Math.Min(first.Y1Feet, first.Y2Feet);
        double high = Math.Max(first.Y1Feet, first.Y2Feet);
        AssertClose(10.0, low, "rafter foot must sit at the eave elevation", 0.01);
        AssertClose(14.0, high, "rafter top must reach the ridge elevation", 0.01);

        // 2x10 = 9.25" deep, plumb drop = depth * sqrt(1+m^2).
        AssertClose(9.25 / 12.0 * Math.Sqrt(1.25), layout.PlumbDropFeet, "plumb drop", 0.001);
    }

    public static void FlatFaceProducesNoRafters()
    {
        ThreeDRoofPlane plane = Face(
            (0, 10, 0), (24, 10, 0), (24, 10, 8), (0, 10, 8));
        var settings = new ThreeDRoofRafterSettings
        {
            RoofGroupId = plane.RoofGroupId,
            Mode = ThreeDRoofRafterSettings.ModeAll,
        };

        AssertTrue(
            ThreeDRoofRafterService.ComputeForPlane(plane, settings) == null,
            "flat faces must not get rafters");
    }

    public static void FaceAnchorsResolveAcrossRebuilds()
    {
        ThreeDRoofPlane south = Face((0, 10, 0), (24, 10, 0), (24, 14, 8), (0, 14, 8));
        ThreeDRoofPlane north = Face((0, 14, 8), (24, 14, 8), (24, 10, 16), (0, 10, 16));
        var settings = new ThreeDRoofRafterSettings
        {
            RoofGroupId = south.RoofGroupId,
            Mode = ThreeDRoofRafterSettings.ModeFaces,
        };
        (double x, double z) = ThreeDRoofRafterService.PlanCentroid(south);
        settings.Faces.Add(new ThreeDRoofRafterFaceAnchor { XFeet = x, ZFeet = z });
        north.RoofGroupId = south.RoofGroupId;

        IReadOnlyList<ThreeDRoofPlane> resolved =
            ThreeDRoofRafterService.ResolvePlanes(settings, [south, north]);
        AssertTrue(resolved.Count == 1 && ReferenceEquals(resolved[0], south),
            "the anchor must resolve to exactly the picked face");

        // Simulate a rebuild: same geometry, brand-new plane instance and id.
        ThreeDRoofPlane rebuilt = Face((0, 10, 0), (24, 10, 0), (24, 14, 8), (0, 14, 8));
        rebuilt.RoofGroupId = south.RoofGroupId;
        resolved = ThreeDRoofRafterService.ResolvePlanes(settings, [rebuilt, north]);
        AssertTrue(resolved.Count == 1 && ReferenceEquals(resolved[0], rebuilt),
            "the anchor must survive plane regeneration");
    }

    private static ThreeDRoofPlane Face(params (double X, double Y, double Z)[] vertices) =>
        new()
        {
            Kind = "roof_face_envelope",
            RoofGroupId = "roof-test",
            Points = vertices
                .Select(v => new ThreeDRoofVertex { XFeet = v.X, YFeet = v.Y, ZFeet = v.Z })
                .ToList(),
        };

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertClose(double expected, double actual, string message, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
