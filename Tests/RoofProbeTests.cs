using OurPlaneCore;

internal static class RoofProbeTests
{
    public static void LShapeMixedEaveRakeBuilds()
    {
        var model = new ThreeDWallModel
        {
            Slabs =
            [
                new ThreeDFloorSlab
                {
                    Label = "L roof base",
                    LevelKey = "roof",
                    ElevationFeet = 10,
                    Points =
                    [
                        Point(0, 0),
                        Point(30, 0),
                        Point(30, 12),
                        Point(20, 12),
                        Point(20, 26),
                        Point(0, 26),
                    ],
                },
            ],
            RoofGuides =
            [
                Guide(ThreeDRoofGuideKinds.Eave, 0, 0, 30, 0, "south eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 30, 12, 20, 12, "wing north eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 20, 26, 0, 26, "main north eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Rake, 30, 0, 30, 12, "right rake"),
                Guide(ThreeDRoofGuideKinds.Rake, 20, 12, 20, 26, "inside rake"),
                Guide(ThreeDRoofGuideKinds.Rake, 0, 26, 0, 0, "left rake"),
            ],
        };

        ThreeDRoofBuildResult result = ThreeDRoofBuildService.Build(model);
        int envelopeFaces = result.Planes.Count(plane => plane.Kind == "roof_face_envelope");
        int rakeFaces = result.Planes.Count(plane => plane.Kind is "roof_rake_triangle" or "roof_rake_face");
        int ridges = result.Guides.Count(guide =>
            guide.Status == ThreeDRoofPreviewBuilder.GeneratedSeamStatus &&
            guide.Kind == ThreeDRoofGuideKinds.Ridge);
        int valleys = result.Guides.Count(guide =>
            guide.Status == ThreeDRoofPreviewBuilder.GeneratedSeamStatus &&
            guide.Kind == ThreeDRoofGuideKinds.Valley);
        double maxY = result.Planes.SelectMany(plane => plane.Points).Max(point => point.YFeet);

        if (result.PlaneBuildBlocked)
            throw new InvalidOperationException("L-shape mixed eave/rake roof should build.");
        if (envelopeFaces < 3)
            throw new InvalidOperationException($"L-shape roof should create multiple envelope faces, got {envelopeFaces}.");
        if (rakeFaces < 2)
            throw new InvalidOperationException($"L-shape roof should create rake/gable closure faces, got {rakeFaces}.");
        if (ridges < 1)
            throw new InvalidOperationException("L-shape roof should create at least one ridge.");
        if (valleys < 1)
            throw new InvalidOperationException("L-shape roof should create at least one valley.");
        if (maxY <= 12)
            throw new InvalidOperationException($"L-shape roof should rise above base, got maxY {maxY:F2}.");
    }

    public static void ParallelEavesOffsetRidgeByPitch()
    {
        var model = new ThreeDWallModel
        {
            Slabs =
            [
                RectSlab("Unequal pitch gable roof", 0, 0, 40, 12),
            ],
            RoofGuides =
            [
                Guide(ThreeDRoofGuideKinds.Eave, 0, 0, 40, 0, "low-pitch south eave", 0.25),
                Guide(ThreeDRoofGuideKinds.Eave, 40, 12, 0, 12, "steeper north eave", 0.75),
                Guide(ThreeDRoofGuideKinds.Rake, 40, 0, 40, 12, "right rake"),
                Guide(ThreeDRoofGuideKinds.Rake, 0, 12, 0, 0, "left rake"),
            ],
        };

        ThreeDRoofBuildResult result = ThreeDRoofBuildService.Build(model);
        ThreeDRoofGuide ridge = result.Guides.FirstOrDefault(guide =>
            guide.Status == ThreeDRoofPreviewBuilder.GeneratedSeamStatus &&
            guide.Kind == ThreeDRoofGuideKinds.Ridge) ?? throw new InvalidOperationException("parallel eaves should create a ridge.");

        double expectedZ = 9.0;
        double averageZ = ridge.Points.Average(point => point.ZFeet);
        double ridgeLength = Math.Sqrt(
            Math.Pow(ridge.Points[^1].XFeet - ridge.Points[0].XFeet, 2) +
            Math.Pow(ridge.Points[^1].ZFeet - ridge.Points[0].ZFeet, 2));

        if (result.PlaneBuildBlocked)
            throw new InvalidOperationException("parallel eaves with pitch should build.");
        if (Math.Abs(averageZ - expectedZ) > 0.1)
            throw new InvalidOperationException($"ridge should shift to Z {expectedZ:F1} from unequal pitches, got {averageZ:F2}.");
        if (ridgeLength < 39)
            throw new InvalidOperationException($"ridge should span the overlap between parallel eaves, got length {ridgeLength:F2}.");
    }

    public static void UShapeMultipleValleysBuilds()
    {
        var model = new ThreeDWallModel
        {
            Slabs =
            [
                new ThreeDFloorSlab
                {
                    Label = "U roof base",
                    LevelKey = "roof",
                    ElevationFeet = 10,
                    Points =
                    [
                        Point(0, 0),
                        Point(40, 0),
                        Point(40, 30),
                        Point(30, 30),
                        Point(30, 10),
                        Point(10, 10),
                        Point(10, 30),
                        Point(0, 30),
                    ],
                },
            ],
            RoofGuides =
            [
                Guide(ThreeDRoofGuideKinds.Eave, 0, 0, 40, 0, "south eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 40, 30, 30, 30, "right north eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 30, 10, 10, 10, "courtyard eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 10, 30, 0, 30, "left north eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Rake, 40, 0, 40, 30, "right outer rake"),
                Guide(ThreeDRoofGuideKinds.Rake, 30, 30, 30, 10, "right inner rake"),
                Guide(ThreeDRoofGuideKinds.Rake, 10, 10, 10, 30, "left inner rake"),
                Guide(ThreeDRoofGuideKinds.Rake, 0, 30, 0, 0, "left outer rake"),
            ],
        };

        ThreeDRoofBuildResult result = ThreeDRoofBuildService.Build(model);
        int envelopeFaces = result.Planes.Count(plane => plane.Kind == "roof_face_envelope");
        int rakeFaces = result.Planes.Count(plane => plane.Kind is "roof_rake_triangle" or "roof_rake_face");
        int valleys = result.Guides.Count(guide =>
            guide.Status == ThreeDRoofPreviewBuilder.GeneratedSeamStatus &&
            guide.Kind == ThreeDRoofGuideKinds.Valley);
        double maxY = result.Planes.SelectMany(plane => plane.Points).Max(point => point.YFeet);

        if (result.PlaneBuildBlocked)
            throw new InvalidOperationException("U-shape mixed eave/rake roof should build.");
        if (envelopeFaces < 4)
            throw new InvalidOperationException($"U-shape roof should create multiple envelope faces, got {envelopeFaces}.");
        if (rakeFaces < 2)
            throw new InvalidOperationException($"U-shape roof should create rake/gable closure faces, got {rakeFaces}.");
        if (valleys < 2)
            throw new InvalidOperationException($"U-shape roof should create at least two valleys, got {valleys}.");
        if (maxY <= 12)
            throw new InvalidOperationException($"U-shape roof should rise above base, got maxY {maxY:F2}.");
    }

    public static void SteppedZigZagValleysBuilds()
    {
        var model = new ThreeDWallModel
        {
            Slabs =
            [
                new ThreeDFloorSlab
                {
                    Label = "Stepped roof base",
                    LevelKey = "roof",
                    ElevationFeet = 10,
                    Points =
                    [
                        Point(0, 0),
                        Point(48, 0),
                        Point(48, 10),
                        Point(36, 10),
                        Point(36, 20),
                        Point(24, 20),
                        Point(24, 30),
                        Point(12, 30),
                        Point(12, 40),
                        Point(0, 40),
                    ],
                },
            ],
            RoofGuides =
            [
                Guide(ThreeDRoofGuideKinds.Eave, 0, 0, 48, 0, "long south eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 48, 10, 36, 10, "step eave 1", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 36, 20, 24, 20, "step eave 2", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 24, 30, 12, 30, "step eave 3", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 12, 40, 0, 40, "top eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Rake, 48, 0, 48, 10, "outer rake 1"),
                Guide(ThreeDRoofGuideKinds.Rake, 36, 10, 36, 20, "inside rake 1"),
                Guide(ThreeDRoofGuideKinds.Rake, 24, 20, 24, 30, "inside rake 2"),
                Guide(ThreeDRoofGuideKinds.Rake, 12, 30, 12, 40, "inside rake 3"),
                Guide(ThreeDRoofGuideKinds.Rake, 0, 40, 0, 0, "outer rake 2"),
            ],
        };

        ThreeDRoofBuildResult result = ThreeDRoofBuildService.Build(model);
        AssertRoofBuild(
            result,
            "stepped zig-zag",
            minimumEnvelopeFaces: 5,
            minimumRakeFaces: 2,
            minimumValleys: 3,
            minimumRidges: 1);
    }

    public static void SkewedGableDiagonalRakeBuilds()
    {
        var model = new ThreeDWallModel
        {
            Slabs =
            [
                new ThreeDFloorSlab
                {
                    Label = "Skewed gable roof base",
                    LevelKey = "roof",
                    ElevationFeet = 10,
                    Points =
                    [
                        Point(0, 0),
                        Point(30, 4),
                        Point(34, 16),
                        Point(4, 12),
                    ],
                },
            ],
            RoofGuides =
            [
                Guide(ThreeDRoofGuideKinds.Eave, 0, 0, 30, 4, "skewed south eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 34, 16, 4, 12, "skewed north eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Rake, 30, 4, 34, 16, "right diagonal rake"),
                Guide(ThreeDRoofGuideKinds.Rake, 4, 12, 0, 0, "left diagonal rake"),
            ],
        };

        ThreeDRoofBuildResult result = ThreeDRoofBuildService.Build(model);
        AssertRoofBuild(
            result,
            "skewed gable",
            minimumEnvelopeFaces: 2,
            minimumRakeFaces: 2,
            minimumValleys: 0,
            minimumRidges: 1);
    }

    public static void SeparateGableIslandsBuild()
    {
        var model = new ThreeDWallModel
        {
            Slabs =
            [
                RectSlab("Left roof", 0, 0, 20, 10),
                RectSlab("Right roof", 35, 2, 18, 12),
            ],
            RoofGuides =
            [
                Guide(ThreeDRoofGuideKinds.Eave, 0, 0, 20, 0, "left south eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 20, 10, 0, 10, "left north eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Rake, 20, 0, 20, 10, "left right rake"),
                Guide(ThreeDRoofGuideKinds.Rake, 0, 10, 0, 0, "left left rake"),
                Guide(ThreeDRoofGuideKinds.Eave, 35, 2, 53, 2, "right south eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 53, 14, 35, 14, "right north eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Rake, 53, 2, 53, 14, "right right rake"),
                Guide(ThreeDRoofGuideKinds.Rake, 35, 14, 35, 2, "right left rake"),
            ],
        };

        ThreeDRoofBuildResult result = ThreeDRoofBuildService.Build(model);
        AssertRoofBuild(
            result,
            "separate gable islands",
            minimumEnvelopeFaces: 4,
            minimumRakeFaces: 4,
            minimumValleys: 0,
            minimumRidges: 2);
    }

    public static void CrossingFootprintDoesNotBuild()
    {
        var model = new ThreeDWallModel
        {
            Slabs =
            [
                new ThreeDFloorSlab
                {
                    Label = "Crossing roof base",
                    LevelKey = "roof",
                    ElevationFeet = 10,
                    Points =
                    [
                        Point(0, 0),
                        Point(20, 16),
                        Point(0, 16),
                        Point(20, 0),
                    ],
                },
            ],
            RoofGuides =
            [
                Guide(ThreeDRoofGuideKinds.Eave, 0, 0, 20, 16, "crossing eave 1", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 0, 16, 20, 0, "crossing eave 2", 0.5),
            ],
        };

        ThreeDRoofBuildResult result = ThreeDRoofBuildService.Build(model);
        if (!result.PlaneBuildBlocked)
            throw new InvalidOperationException("crossing roof footprint should not build generated planes.");
        if (result.Planes.Count != 0)
            throw new InvalidOperationException($"crossing roof footprint should produce 0 planes, got {result.Planes.Count}.");
    }

    public static void NoisyClockwiseFootprintBuilds()
    {
        var model = new ThreeDWallModel
        {
            Slabs =
            [
                new ThreeDFloorSlab
                {
                    Label = "Noisy roof base",
                    LevelKey = "roof",
                    ElevationFeet = 10,
                    Points =
                    [
                        Point(0, 0),
                        Point(0, 10),
                        Point(20, 10),
                        Point(40, 10),
                        Point(40, 0),
                        Point(0, 0),
                    ],
                },
            ],
            RoofGuides =
            [
                Guide(ThreeDRoofGuideKinds.Eave, 0, 0, 40, 0, "south eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 40, 10, 0, 10, "north eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Rake, 40, 0, 40, 10, "right rake"),
                Guide(ThreeDRoofGuideKinds.Rake, 0, 10, 0, 0, "left rake"),
            ],
        };

        ThreeDRoofBuildResult result = ThreeDRoofBuildService.Build(model);
        AssertRoofBuild(
            result,
            "noisy clockwise",
            minimumEnvelopeFaces: 2,
            minimumRakeFaces: 2,
            minimumValleys: 0,
            minimumRidges: 1);
    }

    private static void AssertRoofBuild(
        ThreeDRoofBuildResult result,
        string label,
        int minimumEnvelopeFaces,
        int minimumRakeFaces,
        int minimumValleys,
        int minimumRidges)
    {
        int envelopeFaces = result.Planes.Count(plane => plane.Kind == "roof_face_envelope");
        int rakeFaces = result.Planes.Count(plane => plane.Kind is "roof_rake_triangle" or "roof_rake_face");
        int valleys = result.Guides.Count(guide =>
            guide.Status == ThreeDRoofPreviewBuilder.GeneratedSeamStatus &&
            guide.Kind == ThreeDRoofGuideKinds.Valley);
        int ridges = result.Guides.Count(guide =>
            guide.Status == ThreeDRoofPreviewBuilder.GeneratedSeamStatus &&
            guide.Kind == ThreeDRoofGuideKinds.Ridge);
        double maxY = result.Planes.SelectMany(plane => plane.Points).DefaultIfEmpty(new ThreeDRoofVertex()).Max(point => point.YFeet);

        if (result.PlaneBuildBlocked)
            throw new InvalidOperationException($"{label} roof should build.");
        if (envelopeFaces < minimumEnvelopeFaces)
            throw new InvalidOperationException($"{label} roof should create at least {minimumEnvelopeFaces} envelope faces, got {envelopeFaces}.");
        if (rakeFaces < minimumRakeFaces)
            throw new InvalidOperationException($"{label} roof should create at least {minimumRakeFaces} rake/gable closure faces, got {rakeFaces}.");
        if (valleys < minimumValleys)
            throw new InvalidOperationException($"{label} roof should create at least {minimumValleys} valleys, got {valleys}.");
        if (ridges < minimumRidges)
            throw new InvalidOperationException($"{label} roof should create at least {minimumRidges} ridges, got {ridges}.");
        if (maxY <= 12)
            throw new InvalidOperationException($"{label} roof should rise above base, got maxY {maxY:F2}.");
    }

    private static ThreeDPoint Point(double x, double z) =>
        new() { XFeet = x, ZFeet = z };

    private static ThreeDFloorSlab RectSlab(string label, double x, double z, double width, double depth) =>
        new()
        {
            Label = label,
            LevelKey = "roof",
            ElevationFeet = 10,
            Points =
            [
                Point(x, z),
                Point(x + width, z),
                Point(x + width, z + depth),
                Point(x, z + depth),
            ],
        };

    private static ThreeDRoofGuide Guide(
        string kind,
        double x1,
        double z1,
        double x2,
        double z2,
        string label,
        double pitchRisePerFoot = 0)
    {
        var guide = new ThreeDRoofGuide
        {
            Kind = kind,
            Label = label,
            PageFolder = @"C:\probe\Pages\L101",
            LevelKey = "roof",
            ElevationFeet = 10,
            PitchRisePerFoot = pitchRisePerFoot,
            Color = ThreeDRoofGuideKinds.Color(kind),
            Points =
            [
                new ThreeDRoofGuidePoint { PdfX = x1, PdfY = z1, XFeet = x1, ZFeet = z1 },
                new ThreeDRoofGuidePoint { PdfX = x2, PdfY = z2, XFeet = x2, ZFeet = z2 },
            ],
        };
        guide.RawPoints = guide.Points
            .Select(point => new ThreeDRoofGuidePoint
            {
                PdfX = point.PdfX,
                PdfY = point.PdfY,
                XFeet = point.XFeet,
                ZFeet = point.ZFeet,
            })
            .ToList();
        return guide;
    }
}
