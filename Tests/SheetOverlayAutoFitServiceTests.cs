using OurPlaneCore;
using SkiaSharp;

internal static class SheetOverlayAutoFitServiceTests
{
    public static void RecoversScaleAndOffsetFromRepeatedPlanGeometry()
    {
        PdfGeometrySnapResult overlay = BuildPlanSnap(scale: 1.0f, offsetX: 0, offsetY: 0);
        PdfGeometrySnapResult target = BuildPlanSnap(scale: 1.25f, offsetX: 42, offsetY: 67);

        bool ok = SheetOverlayAutoFitService.TryFit(target, overlay, out SheetOverlayAutoFitResult result);

        AssertTrue(ok, result.Message);
        AssertClose(1.25, result.OverlayScale, "overlay fit scale", 0.01);
        AssertClose(42, result.OffsetXPt, "overlay fit x offset", 1.0);
        AssertClose(67, result.OffsetYPt, "overlay fit y offset", 1.0);
        AssertClose(0, result.OverlayRotationDegrees, "overlay fit rotation", 0.25);
        AssertTrue(result.MatchedSamples >= 12, "overlay fit should verify against several geometry samples");
    }

    public static void RecoversRotationFromRepeatedPlanGeometry()
    {
        PdfGeometrySnapResult overlay = BuildPlanSnap(scale: 1.0f, offsetX: 0, offsetY: 0);
        PdfGeometrySnapResult target = BuildPlanSnap(scale: 0.85f, offsetX: 64, offsetY: 31, rotationDegrees: 7.5f);

        bool ok = SheetOverlayAutoFitService.TryFit(target, overlay, out SheetOverlayAutoFitResult result);

        AssertTrue(ok, result.Message);
        AssertClose(0.85, result.OverlayScale, "overlay fit rotated scale", 0.01);
        AssertClose(64, result.OffsetXPt, "overlay fit rotated x offset", 1.0);
        AssertClose(31, result.OffsetYPt, "overlay fit rotated y offset", 1.0);
        AssertClose(7.5, result.OverlayRotationDegrees, "overlay fit rotated angle", 0.3);
        AssertTrue(result.MatchedSamples >= 12, "rotated overlay fit should verify against several geometry samples");
    }

    public static void RecoversRotationFromJunctionShapeWithoutSegments()
    {
        PdfGeometrySnapResult overlay = BuildPointOnlyPlanSnap(scale: 1.0f, offsetX: 0, offsetY: 0);
        PdfGeometrySnapResult target = BuildPointOnlyPlanSnap(scale: 0.92f, offsetX: 58, offsetY: 44, rotationDegrees: 11f);

        bool ok = SheetOverlayAutoFitService.TryFit(target, overlay, out SheetOverlayAutoFitResult result);

        AssertTrue(ok, result.Message);
        AssertClose(0.92, result.OverlayScale, "overlay fit point-pair scale", 0.02);
        AssertClose(58, result.OffsetXPt, "overlay fit point-pair x offset", 1.5);
        AssertClose(44, result.OffsetYPt, "overlay fit point-pair y offset", 1.5);
        AssertClose(11, result.OverlayRotationDegrees, "overlay fit point-pair angle", 0.4);
        AssertEqual("shape points", result.Method, "overlay fit should use shape-point matching");
        AssertTrue(result.MatchedSamples >= 12, "point-pair overlay fit should verify against several junction samples");
    }

    public static void RejectsSparseGeometry()
    {
        var target = new PdfGeometrySnapResult
        {
            Segments =
            [
                Segment(0, 0, 100, 0),
                Segment(0, 20, 100, 20),
            ],
        };
        var overlay = new PdfGeometrySnapResult
        {
            Segments =
            [
                Segment(0, 0, 80, 0),
                Segment(0, 20, 80, 20),
            ],
        };

        bool ok = SheetOverlayAutoFitService.TryFit(target, overlay, out _);

        AssertFalse(ok, "overlay auto fit should not apply a transform from sparse geometry");
    }

    public static void AutoSelectsBestCandidateByShapeFit()
    {
        PdfGeometrySnapResult target = BuildPlanSnap(scale: 1.18f, offsetX: 36, offsetY: 52, rotationDegrees: 4);
        PdfGeometrySnapResult overlay = BuildPlanSnap(scale: 1.0f, offsetX: 0, offsetY: 0);
        PdfGeometrySnapResult sparse = BuildSparseSnap();
        PageInfo weakPage = Page("A100 wrong", "weak");
        PageInfo goodPage = Page("A101 match", "good");

        bool ok = SheetOverlayAutoFitCandidateSearchService.TryFindBest(
            target,
            [
                new SheetOverlayAutoFitCandidateInput(weakPage, sparse, "test sparse", 1),
                new SheetOverlayAutoFitCandidateInput(goodPage, overlay, "test plan", 2),
            ],
            out SheetOverlayAutoFitCandidateMatch match);

        AssertTrue(ok, "overlay auto-select should find the candidate with matching plan geometry");
        AssertEqual(goodPage.FolderPath, match.Page.FolderPath, "auto-selected overlay candidate");
        AssertClose(1.18, match.Fit.OverlayScale, "auto-selected overlay scale", 0.02);
        AssertClose(4, match.Fit.OverlayRotationDegrees, "auto-selected overlay rotation", 0.4);
    }

    public static void AutoSelectPrefersCloserSheetWhenScoresTie()
    {
        PdfGeometrySnapResult target = BuildPlanSnap(scale: 1.05f, offsetX: 16, offsetY: 24);
        PdfGeometrySnapResult overlay = BuildPlanSnap(scale: 1.0f, offsetX: 0, offsetY: 0);
        PageInfo farPage = Page("A300 far", "far");
        PageInfo nearPage = Page("A102 near", "near");

        bool ok = SheetOverlayAutoFitCandidateSearchService.TryFindBest(
            target,
            [
                new SheetOverlayAutoFitCandidateInput(farPage, overlay, "test plan", 20),
                new SheetOverlayAutoFitCandidateInput(nearPage, overlay, "test plan", 1),
            ],
            out SheetOverlayAutoFitCandidateMatch match);

        AssertTrue(ok, "overlay auto-select should accept tied matching sheets");
        AssertEqual(nearPage.FolderPath, match.Page.FolderPath, "auto-select tie should prefer closer sheet rank");
    }

    public static void AutoSelectReportsRankedAlternatives()
    {
        PdfGeometrySnapResult target = BuildPlanSnap(scale: 1.05f, offsetX: 16, offsetY: 24);
        PdfGeometrySnapResult overlay = BuildPlanSnap(scale: 1.0f, offsetX: 0, offsetY: 0);
        PageInfo firstPage = Page("A101 first", "first");
        PageInfo secondPage = Page("A102 second", "second");
        PageInfo thirdPage = Page("A103 third", "third");

        bool ok = SheetOverlayAutoFitCandidateSearchService.TryFindBest(
            target,
            [
                new SheetOverlayAutoFitCandidateInput(thirdPage, overlay, "test plan", 3),
                new SheetOverlayAutoFitCandidateInput(firstPage, overlay, "test plan", 1),
                new SheetOverlayAutoFitCandidateInput(secondPage, overlay, "test plan", 2),
            ],
            out SheetOverlayAutoFitCandidateMatch match,
            out IReadOnlyList<SheetOverlayAutoFitCandidateMatch> topMatches);

        AssertTrue(ok, "overlay auto-select should produce ranked alternatives");
        AssertEqual(firstPage.FolderPath, match.Page.FolderPath, "best auto-selected overlay candidate");
        AssertTrue(topMatches.Count == 3, "auto-select should retain ranked alternatives");
        AssertEqual(firstPage.FolderPath, topMatches[0].Page.FolderPath, "first ranked overlay candidate");
        AssertEqual(secondPage.FolderPath, topMatches[1].Page.FolderPath, "second ranked overlay candidate");
        AssertEqual(thirdPage.FolderPath, topMatches[2].Page.FolderPath, "third ranked overlay candidate");
    }

    public static void ReviewCandidatesCanIncludeWeakGeometryWithoutAutoSelecting()
    {
        PdfGeometrySnapResult target = BuildPointOnlyPlanSnap(scale: 1.0f, offsetX: 0, offsetY: 0);
        PdfGeometrySnapResult noisyOverlay = BuildWeakReviewPointSnap();
        PageInfo weakPage = Page("A101 weak review", "weak-review");

        bool autoOk = SheetOverlayAutoFitCandidateSearchService.TryFindBest(
            target,
            [new SheetOverlayAutoFitCandidateInput(weakPage, noisyOverlay, "test noisy plan", 1)],
            out _);
        bool reviewOk = SheetOverlayAutoFitCandidateSearchService.TryRankReviewCandidates(
            target,
            [new SheetOverlayAutoFitCandidateInput(weakPage, noisyOverlay, "test noisy plan", 1)],
            out IReadOnlyList<SheetOverlayAutoFitCandidateMatch> reviewMatches);

        AssertFalse(autoOk, "weak overlay candidates should not be auto-selected silently");
        AssertTrue(reviewOk, "weak overlay candidates should still be available for manual review");
        AssertEqual(weakPage.FolderPath, reviewMatches[0].Page.FolderPath, "review-only overlay candidate");
        AssertFalse(reviewMatches[0].IsAutoSelectable, "review-only candidate should be marked as not auto-selectable");
    }

    public static void AutoSelectNextCandidateCyclesRankedAlternatives()
    {
        PdfGeometrySnapResult target = BuildPlanSnap(scale: 1.05f, offsetX: 16, offsetY: 24);
        PdfGeometrySnapResult overlay = BuildPlanSnap(scale: 1.0f, offsetX: 0, offsetY: 0);
        PageInfo firstPage = Page("A101 first", "first");
        PageInfo secondPage = Page("A102 second", "second");
        PageInfo thirdPage = Page("A103 third", "third");

        bool ok = SheetOverlayAutoFitCandidateSearchService.TryFindBest(
            target,
            [
                new SheetOverlayAutoFitCandidateInput(thirdPage, overlay, "test plan", 3),
                new SheetOverlayAutoFitCandidateInput(firstPage, overlay, "test plan", 1),
                new SheetOverlayAutoFitCandidateInput(secondPage, overlay, "test plan", 2),
            ],
            out _,
            out IReadOnlyList<SheetOverlayAutoFitCandidateMatch> topMatches);

        AssertTrue(ok, "overlay auto-select should rank candidates before cycling");
        AssertTrue(topMatches.Count == 3, "overlay auto-select should expose all ranked alternatives for cycling");

        AssertTrue(
            SheetOverlayAutoFitCandidateSearchService.TrySelectNextMatch(topMatches, firstPage.FolderPath, out SheetOverlayAutoFitCandidateMatch afterFirst),
            "next candidate should advance from the first ranked sheet");
        AssertEqual(secondPage.FolderPath, afterFirst.Page.FolderPath, "next candidate after first sheet");

        AssertTrue(
            SheetOverlayAutoFitCandidateSearchService.TrySelectNextMatch(topMatches, secondPage.FolderPath, out SheetOverlayAutoFitCandidateMatch afterSecond),
            "next candidate should advance from the second ranked sheet");
        AssertEqual(thirdPage.FolderPath, afterSecond.Page.FolderPath, "next candidate after second sheet");

        AssertTrue(
            SheetOverlayAutoFitCandidateSearchService.TrySelectNextMatch(topMatches, thirdPage.FolderPath, out SheetOverlayAutoFitCandidateMatch afterThird),
            "next candidate should wrap after the last ranked sheet");
        AssertEqual(firstPage.FolderPath, afterThird.Page.FolderPath, "next candidate after third sheet should wrap to first");

        AssertFalse(
            SheetOverlayAutoFitCandidateSearchService.TrySelectNextMatch([topMatches[0]], firstPage.FolderPath, out _),
            "next candidate should reject cycling when the only ranked sheet is already selected");

        PageInfo fourthPage = Page("A104 fourth", "fourth");
        PageInfo fifthPage = Page("A105 fifth", "fifth");
        PageInfo sixthPage = Page("A106 sixth", "sixth");
        bool manyOk = SheetOverlayAutoFitCandidateSearchService.TryFindBest(
            target,
            [
                new SheetOverlayAutoFitCandidateInput(firstPage, overlay, "test plan", 1),
                new SheetOverlayAutoFitCandidateInput(secondPage, overlay, "test plan", 2),
                new SheetOverlayAutoFitCandidateInput(thirdPage, overlay, "test plan", 3),
                new SheetOverlayAutoFitCandidateInput(fourthPage, overlay, "test plan", 4),
                new SheetOverlayAutoFitCandidateInput(fifthPage, overlay, "test plan", 5),
                new SheetOverlayAutoFitCandidateInput(sixthPage, overlay, "test plan", 6),
            ],
            out _,
            out IReadOnlyList<SheetOverlayAutoFitCandidateMatch> manyMatches);

        AssertTrue(manyOk, "overlay auto-select should rank more than five matching candidates");
        AssertTrue(manyMatches.Count == 6, "next candidate should keep the full ranked match list");
        AssertTrue(
            SheetOverlayAutoFitCandidateSearchService.TrySelectNextMatch(manyMatches, fifthPage.FolderPath, out SheetOverlayAutoFitCandidateMatch afterFifth),
            "next candidate should advance beyond the fifth ranked sheet");
        AssertEqual(sixthPage.FolderPath, afterFifth.Page.FolderPath, "next candidate after fifth sheet");
    }

    private static PdfGeometrySnapResult BuildPlanSnap(
        float scale,
        float offsetX,
        float offsetY,
        float rotationDegrees = 0)
    {
        var raw = new List<PdfGeometrySnapSegment>
        {
            Segment(0, 0, 220, 0),
            Segment(220, 0, 220, 120),
            Segment(220, 120, 0, 120),
            Segment(0, 120, 0, 0),
            Segment(40, 0, 40, 120),
            Segment(95, 0, 95, 120),
            Segment(150, 0, 150, 120),
            Segment(0, 55, 220, 55),
            Segment(0, 88, 220, 88),
            Segment(95, 30, 150, 30),
            Segment(95, 90, 150, 90),
        };

        List<PdfGeometrySnapSegment> segments = raw
            .Select(segment => Transform(segment, scale, offsetX, offsetY, rotationDegrees))
            .ToList();
        List<PdfGeometrySnapPoint> points = segments
            .SelectMany(segment => new[] { segment.Start, segment.End })
            .DistinctBy(point => $"{point.X:0.###}|{point.Y:0.###}")
            .Select(point => new PdfGeometrySnapPoint(point, "pdf-corner"))
            .ToList();

        return new PdfGeometrySnapResult
        {
            Points = points,
            Segments = segments,
        };
    }

    private static PdfGeometrySnapResult BuildPointOnlyPlanSnap(
        float scale,
        float offsetX,
        float offsetY,
        float rotationDegrees = 0)
    {
        List<PdfGeometrySnapPoint> points = RawPlanJunctions()
            .Select(point => new PdfGeometrySnapPoint(
                Transform(point, scale, offsetX, offsetY, rotationDegrees),
                "raster-junction"))
            .ToList();

        return new PdfGeometrySnapResult
        {
            Points = points,
            Segments = [],
        };
    }

    private static List<SKPoint> RawPlanJunctions() =>
    [
        new SKPoint(0, 0),
        new SKPoint(220, 0),
        new SKPoint(220, 120),
        new SKPoint(0, 120),
        new SKPoint(40, 0),
        new SKPoint(40, 55),
        new SKPoint(40, 120),
        new SKPoint(95, 0),
        new SKPoint(95, 55),
        new SKPoint(95, 88),
        new SKPoint(95, 120),
        new SKPoint(150, 0),
        new SKPoint(150, 30),
        new SKPoint(150, 88),
        new SKPoint(150, 120),
        new SKPoint(185, 30),
        new SKPoint(205, 74),
        new SKPoint(220, 55),
        new SKPoint(220, 88),
        new SKPoint(0, 55),
        new SKPoint(0, 88),
    ];

    private static PdfGeometrySnapResult BuildSparseSnap() =>
        new()
        {
            Segments =
            [
                Segment(0, 0, 100, 0),
                Segment(0, 40, 100, 40),
            ],
        };

    private static PdfGeometrySnapResult BuildWeakReviewPointSnap()
    {
        var points = new List<PdfGeometrySnapPoint>();
        points.AddRange(RawPlanJunctions()
            .Take(8)
            .Select(point => new PdfGeometrySnapPoint(point, "raster-junction")));
        for (int i = 0; i < 52; i++)
        {
            points.Add(new PdfGeometrySnapPoint(
                new SKPoint(420 + i * 9, 360 + (i % 13) * 11),
                "raster-junction"));
        }

        return new PdfGeometrySnapResult
        {
            Points = points,
            Segments = [],
        };
    }

    private static PageInfo Page(string name, string folder) =>
        new()
        {
            Name = name,
            FolderPath = folder,
            PdfPath = "source.pdf",
        };

    private static PdfGeometrySnapSegment Transform(
        PdfGeometrySnapSegment segment,
        float scale,
        float offsetX,
        float offsetY,
        float rotationDegrees) =>
        Segment(
            Transform(segment.Start, scale, offsetX, offsetY, rotationDegrees),
            Transform(segment.End, scale, offsetX, offsetY, rotationDegrees));

    private static SKPoint Transform(
        SKPoint point,
        float scale,
        float offsetX,
        float offsetY,
        float rotationDegrees)
    {
        float radians = rotationDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        float x = point.X * scale;
        float y = point.Y * scale;
        return new SKPoint(
            offsetX + x * cos - y * sin,
            offsetY + x * sin + y * cos);
    }

    private static PdfGeometrySnapSegment Segment(float x0, float y0, float x1, float y1) =>
        new(new SKPoint(x0, y0), new SKPoint(x1, y1), "pdf-line");

    private static PdfGeometrySnapSegment Segment(SKPoint start, SKPoint end) =>
        new(start, end, "pdf-line");

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }

    private static void AssertClose(double expected, double actual, string message, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
