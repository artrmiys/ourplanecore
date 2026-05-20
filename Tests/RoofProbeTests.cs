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
            minimumValleys: 2,
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

    // Diagnostic: dumps real 3D geometry for a canonical 40x24 gable
    // (2 long eaves + 2 short rakes, 6/12). Writes bin/roof_probe.txt.
    public static void GableGeometryProbe()
    {
        var model = new ThreeDWallModel
        {
            Slabs = [RectSlab("gable", 0, 0, 40, 24)],
            RoofGuides =
            [
                Guide(ThreeDRoofGuideKinds.Eave, 0, 0, 40, 0, "south eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 40, 24, 0, 24, "north eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Rake, 40, 0, 40, 24, "east rake"),
                Guide(ThreeDRoofGuideKinds.Rake, 0, 24, 0, 0, "west rake"),
            ],
        };

        var lModel = new ThreeDWallModel
        {
            Slabs = [new ThreeDFloorSlab { Label = "L", LevelKey = "roof", ElevationFeet = 10,
                Points = [Point(0,0),Point(30,0),Point(30,12),Point(20,12),Point(20,26),Point(0,26)] }],
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

        var sb = new System.Text.StringBuilder();
        foreach ((string name, ThreeDWallModel m) in new[] { ("GABLE 40x24", model), ("L-SHAPE mixed", lModel) })
        {
            ThreeDRoofBuildResult r = ThreeDRoofBuildService.Build(m);
            sb.AppendLine($"=== {name}: blocked={r.PlaneBuildBlocked} planes={r.Planes.Count} ===");
            foreach (string msg in r.Messages) sb.AppendLine($"msg: {msg}");
            foreach (ThreeDRoofPlane p in r.Planes)
            {
                sb.AppendLine($"PLANE kind={p.Kind} label='{p.Label}' pts={p.Points.Count}");
                foreach (ThreeDRoofVertex v in p.Points)
                    sb.AppendLine($"  ({v.XFeet:F2}, {v.YFeet:F2}, {v.ZFeet:F2})");
            }
            foreach (ThreeDRoofGuide g in r.Guides.Where(g => g.Status == ThreeDRoofPreviewBuilder.GeneratedSeamStatus))
            {
                ThreeDRoofGuidePoint a = g.Points[0], b = g.Points[^1];
                sb.AppendLine($"SEAM {g.Kind} '{g.Label}' ({a.XFeet:F2},{a.YFeet:F2},{a.ZFeet:F2})->({b.XFeet:F2},{b.YFeet:F2},{b.ZFeet:F2})");
            }
        }
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "roof_probe.txt"), sb.ToString());

        // Strict geometry: 40x24 gable, base 10, pitch 0.5 -> ridge along
        // z=12 at y=16, faces tile the 960 sqft footprint.
        ThreeDRoofBuildResult gr = ThreeDRoofBuildService.Build(model);
        if (gr.PlaneBuildBlocked)
            throw new InvalidOperationException("gable probe: nothing built (see roof_probe.txt)");

        double gableArea = gr.Planes.Where(p => p.Kind == "roof_face_envelope")
            .Sum(p => Math.Abs(PolygonArea(p.Points.Select(v => (v.XFeet, v.ZFeet)))));
        if (gableArea < 960 * 0.95 || gableArea > 960 * 1.05)
            throw new InvalidOperationException($"gable faces must tile 960 sqft, got {gableArea:F0}.");

        double gableMaxY = gr.Planes.Where(p => p.Kind == "roof_face_envelope")
            .SelectMany(p => p.Points).Max(v => v.YFeet);
        if (Math.Abs(gableMaxY - 16.0) > 0.4)
            throw new InvalidOperationException($"gable ridge should reach y=16, got {gableMaxY:F2}.");

        ThreeDRoofGuide gRidge = gr.Guides.First(g =>
            g.Status == ThreeDRoofPreviewBuilder.GeneratedSeamStatus && g.Kind == ThreeDRoofGuideKinds.Ridge);
        double gRidgeLen = Math.Sqrt(
            Math.Pow(gRidge.Points[^1].XFeet - gRidge.Points[0].XFeet, 2) +
            Math.Pow(gRidge.Points[^1].ZFeet - gRidge.Points[0].ZFeet, 2));
        if (gRidgeLen < 38 || Math.Abs(gRidge.Points.Average(p => p.ZFeet) - 12) > 0.6 ||
            Math.Abs(gRidge.Points.Average(p => p.YFeet) - 16) > 0.5)
        {
            throw new InvalidOperationException(
                $"gable ridge must span ~40 at z=12,y=16; len {gRidgeLen:F1} z {gRidge.Points.Average(p => p.ZFeet):F1} y {gRidge.Points.Average(p => p.YFeet):F1}.");
        }

        // L-shape: faces tile 640 sqft and the tall main wing actually rises
        // to its own ridge (~y 16.5), not collapsed to the low wing.
        ThreeDRoofBuildResult lr = ThreeDRoofBuildService.Build(lModel);
        if (lr.PlaneBuildBlocked)
            throw new InvalidOperationException("L probe: nothing built.");
        double lArea = lr.Planes.Where(p => p.Kind == "roof_face_envelope")
            .Sum(p => Math.Abs(PolygonArea(p.Points.Select(v => (v.XFeet, v.ZFeet)))));
        if (lArea < 640 * 0.93 || lArea > 640 * 1.07)
            throw new InvalidOperationException($"L faces must tile ~640 sqft, got {lArea:F0}.");
        double lMaxY = lr.Planes.Where(p => p.Kind == "roof_face_envelope")
            .SelectMany(p => p.Points).Max(v => v.YFeet);
        if (lMaxY < 16.0)
            throw new InvalidOperationException($"L main wing must rise to ~16.5, got max {lMaxY:F2}.");
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
        {
            string detail = string.Join("; ", result.Guides
                .Where(guide => guide.Status == ThreeDRoofPreviewBuilder.GeneratedSeamStatus &&
                                guide.Kind == ThreeDRoofGuideKinds.Valley)
                .Select(guide =>
                {
                    ThreeDRoofGuidePoint a = guide.Points[0];
                    ThreeDRoofGuidePoint b = guide.Points[^1];
                    return $"{guide.Label} ({a.XFeet:F1},{a.YFeet:F1},{a.ZFeet:F1})->({b.XFeet:F1},{b.YFeet:F1},{b.ZFeet:F1})";
                }));
            throw new InvalidOperationException($"{label} roof should create at least {minimumValleys} valleys, got {valleys}: {detail}");
        }
        if (ridges < minimumRidges)
            throw new InvalidOperationException($"{label} roof should create at least {minimumRidges} ridges, got {ridges}.");
        if (maxY <= 12)
            throw new InvalidOperationException($"{label} roof should rise above base, got maxY {maxY:F2}.");
    }

    // Locks in the fix for "complex roof breaks": envelope faces must tile the
    // whole footprint - no gaps, no overlap - for a non-convex U-shape.
    public static void EnvelopeTilesUShapeFootprint()
    {
        ThreeDPoint[] footprint =
        [
            Point(0, 0), Point(40, 0), Point(40, 30), Point(30, 30),
            Point(30, 10), Point(10, 10), Point(10, 30), Point(0, 30),
        ];
        var model = new ThreeDWallModel
        {
            Slabs = [new ThreeDFloorSlab { Label = "U", LevelKey = "roof", ElevationFeet = 10, Points = [.. footprint] }],
            RoofGuides =
            [
                Guide(ThreeDRoofGuideKinds.Eave, 0, 0, 40, 0, "s", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 40, 30, 30, 30, "rn", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 30, 10, 10, 10, "court", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 10, 30, 0, 30, "ln", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 40, 0, 40, 30, "right eave", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 30, 30, 30, 10, "right inner", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 10, 10, 10, 30, "left inner", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 0, 30, 0, 0, "left eave", 0.5),
            ],
        };

        ThreeDRoofBuildResult result = ThreeDRoofBuildService.Build(model);
        if (result.PlaneBuildBlocked)
            throw new InvalidOperationException("full-hip U-shape should build.");

        double footprintArea = Math.Abs(PolygonArea(footprint.Select(p => (p.XFeet, p.ZFeet))));
        double covered = result.Planes
            .Where(plane => plane.Kind == "roof_face_envelope")
            .Sum(plane => Math.Abs(PolygonArea(plane.Points.Select(v => (v.XFeet, v.ZFeet)))));

        double ratio = covered / footprintArea;
        if (ratio < 0.97 || ratio > 1.03)
            throw new InvalidOperationException(
                $"U-shape envelope must tile the footprint; covered {covered:F1} of {footprintArea:F1} (ratio {ratio:F3}).");
    }

    public static void SteppedFootprintFacesTriangulate()
    {
        ThreeDPoint[] footprint =
        [
            Point(11.8, 0), Point(36, 0), Point(36, 19),
            Point(8.7, 19), Point(8.7, 24), Point(0, 24),
            Point(0, 9.5), Point(11.8, 9.5),
        ];
        var model = new ThreeDWallModel
        {
            Slabs =
            [
                new ThreeDFloorSlab
                {
                    Label = "Revit-like stepped roof base",
                    LevelKey = "roof",
                    ElevationFeet = 10,
                    Points = [.. footprint],
                },
            ],
            RoofGuides =
            [
                Guide(ThreeDRoofGuideKinds.Eave, 11.8, 0, 36, 0, "top eave", 0.25),
                Guide(ThreeDRoofGuideKinds.Eave, 36, 0, 36, 19, "right eave", 0.25),
                Guide(ThreeDRoofGuideKinds.Eave, 36, 19, 8.7, 19, "main lower eave", 0.25),
                Guide(ThreeDRoofGuideKinds.Rake, 8.7, 19, 8.7, 24, "step rake"),
                Guide(ThreeDRoofGuideKinds.Eave, 8.7, 24, 0, 24, "lower eave", 0.25),
                Guide(ThreeDRoofGuideKinds.Eave, 0, 24, 0, 9.5, "left eave", 0.25),
                Guide(ThreeDRoofGuideKinds.Rake, 0, 9.5, 11.8, 9.5, "notch rake"),
                Guide(ThreeDRoofGuideKinds.Rake, 11.8, 9.5, 11.8, 0, "upper rake"),
            ],
        };

        ThreeDRoofBuildResult result = ThreeDRoofBuildService.Build(model);
        if (result.PlaneBuildBlocked)
            throw new InvalidOperationException("stepped Revit-like footprint should build.");

        List<ThreeDRoofPlane> faces = result.Planes
            .Where(plane => plane.Kind == "roof_face_envelope")
            .ToList();
        if (faces.Count < 5)
            throw new InvalidOperationException($"stepped footprint should create multiple roof faces, got {faces.Count}.");

        double footprintArea = Math.Abs(PolygonArea(footprint.Select(p => (p.XFeet, p.ZFeet))));
        double covered = faces.Sum(plane => Math.Abs(PolygonArea(plane.Points.Select(v => (v.XFeet, v.ZFeet)))));
        double ratio = covered / footprintArea;
        if (ratio < 0.97 || ratio > 1.03)
            throw new InvalidOperationException($"stepped footprint faces must tile the footprint; ratio {ratio:F3}.");

        foreach (ThreeDRoofPlane face in faces)
        {
            ThreeDPolygonTriangulation triangulation = ThreeDPolygonTriangulator.Triangulate(
                face.Points.Select(point => new ThreeDPoint { XFeet = point.XFeet, ZFeet = point.ZFeet }).ToList());
            if (!triangulation.Success || triangulation.Points.Count != face.Points.Count)
            {
                throw new InvalidOperationException(
                    $"roof face '{face.Label}' must stay directly triangulatable; {triangulation.Message}");
            }
        }
    }

    public static void EagleviewSteppedFootprintHasNoFlatGeneratedSeams()
    {
        ThreeDPoint[] footprint =
        [
            Point(20.030933521412035, 50.19593641493055),
            Point(20.030933521412035, 38.088401511863424),
            Point(28.64603226273148, 38.088401511863424),
            Point(28.64603226273148, 30.209120008680554),
            Point(46.23650444878472, 30.209120008680554),
            Point(46.23650444878472, 45.23588957609953),
            Point(26.37896728515625, 45.23588957609953),
            Point(26.37896728515625, 50.22584364149305),
        ];
        var model = new ThreeDWallModel
        {
            Slabs =
            [
                new ThreeDFloorSlab
                {
                    Label = "Eagleview roof base",
                    LevelKey = "roof",
                    ElevationFeet = 0,
                    Points = [.. footprint],
                },
            ],
            RoofGuides =
            [
                Guide(ThreeDRoofGuideKinds.Eave, 20.030933521412035, 50.19593641493055, 20.030933521412035, 38.088401511863424, "edge 1", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 20.030933521412035, 38.088401511863424, 28.64603226273148, 38.088401511863424, "edge 2", 0.5),
                Guide(ThreeDRoofGuideKinds.Rake, 28.64603226273148, 38.088401511863424, 28.64603226273148, 30.209120008680554, "edge 3"),
                Guide(ThreeDRoofGuideKinds.Eave, 28.64603226273148, 30.209120008680554, 46.23650444878472, 30.209120008680554, "edge 4", 0.5),
                Guide(ThreeDRoofGuideKinds.Rake, 46.23650444878472, 30.209120008680554, 46.23650444878472, 45.23588957609953, "edge 5"),
                Guide(ThreeDRoofGuideKinds.Eave, 46.23650444878472, 45.23588957609953, 26.37896728515625, 45.23588957609953, "edge 6", 0.5),
                Guide(ThreeDRoofGuideKinds.Eave, 26.37896728515625, 45.23588957609953, 26.37896728515625, 50.22584364149305, "edge 7", 0.5),
                Guide(ThreeDRoofGuideKinds.Rake, 26.37896728515625, 50.22584364149305, 20.030933521412035, 50.19593641493055, "edge 8"),
            ],
        };

        ThreeDRoofBuildResult result = ThreeDRoofBuildService.Build(model);
        if (result.PlaneBuildBlocked)
            throw new InvalidOperationException("Eagleview stepped roof should build.");

        List<ThreeDRoofPlane> faces = result.Planes
            .Where(plane => plane.Kind == "roof_face_envelope")
            .ToList();
        double footprintArea = Math.Abs(PolygonArea(footprint.Select(p => (p.XFeet, p.ZFeet))));
        double covered = faces.Sum(plane => Math.Abs(PolygonArea(plane.Points.Select(v => (v.XFeet, v.ZFeet)))));
        double ratio = covered / footprintArea;
        if (ratio < 0.97 || ratio > 1.03)
            throw new InvalidOperationException($"Eagleview roof faces must tile the footprint; ratio {ratio:F3}.");

        List<ThreeDRoofGuide> flatGenerated = result.Guides
            .Where(guide => guide.Status == ThreeDRoofPreviewBuilder.GeneratedSeamStatus)
            .Where(guide => guide.Points.Count >= 2 && guide.Points.Max(point => point.YFeet) <= 0.05)
            .ToList();
        if (flatGenerated.Count > 0)
        {
            string labels = string.Join(", ", flatGenerated.Select(guide => guide.Label));
            throw new InvalidOperationException($"Generated roof seams must not lie flat on the base plane: {labels}.");
        }
    }

    private static double PolygonArea(IEnumerable<(double X, double Z)> points)
    {
        var pts = points.ToList();
        double area = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            (double X, double Z) a = pts[i];
            (double X, double Z) b = pts[(i + 1) % pts.Count];
            area += a.X * b.Z - b.X * a.Z;
        }

        return area / 2.0;
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
            DefinesSlope = ThreeDRoofGuideKinds.Normalize(kind) == ThreeDRoofGuideKinds.Eave,
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
