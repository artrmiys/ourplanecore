using OurPlanCore;
using OurPlanCore.Models;
using SkiaSharp;

internal static class WallCenterlineTracerTests
{
    // Coordinates below are plain PDF points; thicknesses are chosen so the
    // wall faces (distance 6) sit inside the [4, 10] window.
    private static WallCenterlineTracer.Options DefaultOptions() => new()
    {
        MinThicknessPt = 4,
        MaxThicknessPt = 10,
        MinFaceLengthPt = 10,
        MinWallLengthPt = 15,
    };

    public static void ParallelPairYieldsSingleCenterline()
    {
        // One horizontal wall: faces at y=100 and y=106, from x=0..200.
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 100, 200, 100),
            Seg(0, 106, 200, 106),
        ];

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-50, 50, 300, 300), DefaultOptions());

        AssertEqual(1, result.Count, "one wall pair must give one centerline");
        SKPoint[] line = result[0];
        AssertEqual(2, line.Length, "centerline is a straight two-point line");
        AssertClose(103, line[0].Y, "centerline sits midway between the faces");
        AssertClose(103, line[1].Y, "centerline stays midway at the other end");
        AssertClose(200, Distance(line[0], line[^1]), "centerline spans the overlapping extent", 1.0);
    }

    public static void FacesOutsideAreaAreIgnored()
    {
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 100, 200, 100),
            Seg(0, 106, 200, 106),
        ];

        // The selection polygon covers only x in [0, 90].
        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-10, 50, 90, 200), DefaultOptions());

        AssertEqual(1, result.Count, "clipped wall still detected inside the area");
        SKPoint[] line = result[0];
        float maxX = Math.Max(line[0].X, line[^1].X);
        AssertTrue(maxX <= 91, $"centerline must not escape the selection area (maxX={maxX})");
    }

    public static void TooFarOrTooCloseFacesAreRejected()
    {
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 100, 200, 100),
            Seg(0, 102, 200, 102),   // 2pt apart: thinner than MinThicknessPt
            Seg(0, 130, 200, 130),   // 28-30pt away from the others: too thick
        ];

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-50, 50, 300, 300), DefaultOptions());

        AssertEqual(0, result.Count, "no face pair inside the thickness window");
    }

    public static void PerpendicularLinesDoNotPair()
    {
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 100, 200, 100),
            Seg(100, 0, 100, 200),
        ];

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-50, -50, 300, 300), DefaultOptions());

        AssertEqual(0, result.Count, "perpendicular lines are not wall faces");
    }

    public static void BrokenFaceSegmentsMergeIntoOneWall()
    {
        // Bottom face drawn as two collinear pieces (door swing gap closed by
        // merge tolerance is NOT the case here: pieces touch end to end).
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 100, 90, 100),
            Seg(90, 100, 200, 100),
            Seg(0, 106, 200, 106),
        ];

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-50, 50, 300, 300), DefaultOptions());

        AssertEqual(1, result.Count, "split face pieces must merge into one centerline");
        AssertClose(200, PolylineLength(result[0]), "merged centerline spans the full wall", 2.0);
    }

    public static void LShapedWallsChainAtTheCorner()
    {
        // Horizontal wall then vertical wall meeting at (200, 103)-ish corner.
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 100, 200, 100),
            Seg(0, 106, 194, 106),
            Seg(200, 100, 200, 300),
            Seg(194, 106, 194, 300),
        ];

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-50, 50, 300, 400), DefaultOptions());

        int totalPoints = result.Sum(p => p.Length);
        AssertTrue(result.Count is 1 or 2, $"L-corner traces as one chained or two joined lines, got {result.Count}");
        double total = result.Sum(PolylineLength);
        AssertTrue(total > 350, $"both legs of the L must be traced (total={total:0.#})");
    }

    public static void WallsInsideAreaHoleAreSkipped()
    {
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(100, 100, 160, 100),
            Seg(100, 106, 160, 106),
        ];

        List<SKPoint> outer = Square(0, 0, 400, 400);
        List<List<SKPoint>> holes = [Square(80, 80, 200, 200)];

        List<SKPoint[]> result = WallCenterlineTracer.Trace(
            segments,
            outer,
            DefaultOptions(),
            holes.Select(h => (IReadOnlyList<SKPoint>)h).ToList());

        AssertEqual(0, result.Count, "wall entirely inside a hole must be skipped");
    }

    public static void FacesInsideExcludedTextZoneAreIgnored()
    {
        // A room-label underline pair: two short parallel lines that would
        // otherwise read as a wall, sitting inside a word bounding box.
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(100, 100, 160, 100),
            Seg(100, 106, 160, 106),
        ];

        var options = new WallCenterlineTracer.Options
        {
            MinThicknessPt = 4,
            MaxThicknessPt = 10,
            MinFaceLengthPt = 10,
            MinWallLengthPt = 15,
            ExcludedZones = [new SKRect(90, 90, 170, 116)],
        };

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(0, 0, 400, 400), options);
        AssertEqual(0, result.Count, "faces inside a text zone must not pair into walls");

        // Same geometry without the zone still traces (sanity check).
        List<SKPoint[]> unfiltered = WallCenterlineTracer.Trace(segments, Square(0, 0, 400, 400), DefaultOptions());
        AssertEqual(1, unfiltered.Count, "the same pair without zones is a wall");
    }

    public static void TripleFaceWallYieldsOneCenterline()
    {
        // Wall drawn with three parallel lines (two faces + a finish line):
        // pairs (1,2) and (2,3) both land in the thickness window and used to
        // leave two offset centerlines that chained into diagonal zigzags.
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 100, 200, 100),
            Seg(0, 105, 200, 105),
            Seg(0, 110, 200, 110),
        ];

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-50, 50, 300, 300), DefaultOptions());

        AssertEqual(1, result.Count, "a triple-line wall must yield exactly one centerline");
        AssertClose(200, PolylineLength(result[0]), "the kept centerline spans the wall", 2.0);
    }

    public static void CornerJoinLandsOnLineIntersection()
    {
        // L-corner: the horizontal and vertical centerlines meet where the
        // lines cross (200-ish, 103), not at an endpoint centroid that would
        // bend both lines sideways.
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 100, 200, 100),
            Seg(0, 106, 194, 106),
            Seg(200, 100, 200, 300),
            Seg(194, 106, 194, 300),
        ];

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-50, 50, 300, 400), DefaultOptions());

        bool foundCorner = result
            .SelectMany(line => line)
            .Any(p => Math.Abs(p.X - 197) <= 1.2 && Math.Abs(p.Y - 103) <= 1.2);
        AssertTrue(foundCorner, "chained corner vertex must sit at the centerline intersection");
    }

    public static void FillZonesKeepOnlyFilledWalls()
    {
        // Two identical pairs; only the first sits on a dark filled strip
        // (wall poche). With fill zones present, the hollow pair (casework
        // outline) must be discarded.
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 100, 200, 100),
            Seg(0, 106, 200, 106),
            Seg(0, 300, 200, 300),
            Seg(0, 306, 200, 306),
        ];

        var options = new WallCenterlineTracer.Options
        {
            MinThicknessPt = 4,
            MaxThicknessPt = 10,
            MinFaceLengthPt = 10,
            MinWallLengthPt = 15,
            WallFillZones = [new WallCenterlineTracer.FillZone(new SKRect(0, 100, 200, 106), 0.5f)],
        };

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-50, 50, 300, 400), options);

        AssertEqual(1, result.Count, "only the filled wall survives the fill check");
        AssertClose(103, result[0][0].Y, "the surviving centerline is the filled one", 1.0);

        // Without fill zones both pairs trace (line-only sheets stay usable).
        List<SKPoint[]> unfiltered = WallCenterlineTracer.Trace(segments, Square(-50, 50, 300, 400), DefaultOptions());
        AssertEqual(2, unfiltered.Count, "no fill data means no fill filtering");
    }

    public static void OffCenterFillStripStillConfirmsThickWall()
    {
        // Thick exterior assembly: faces 10pt apart, the dark stud strip sits
        // against one face instead of the middle. The band check must find it.
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 100, 200, 100),
            Seg(0, 110, 200, 110),
        ];

        var options = new WallCenterlineTracer.Options
        {
            MinThicknessPt = 4,
            MaxThicknessPt = 10,
            MinFaceLengthPt = 10,
            MinWallLengthPt = 15,
            WallFillZones = [new WallCenterlineTracer.FillZone(new SKRect(0, 100, 200, 103), 0.5f)],
        };

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-50, 50, 300, 300), options);
        AssertEqual(1, result.Count, "strip near one face must still confirm the wall");
    }

    public static void DarkFillOnlyDropsLightPartitions()
    {
        // Two walls: dark poche (rated) at y=100..106, light gray partition
        // fill at y=300..306. DarkFillOnly keeps just the rated wall.
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 100, 200, 100),
            Seg(0, 106, 200, 106),
            Seg(0, 300, 200, 300),
            Seg(0, 306, 200, 306),
        ];

        var options = new WallCenterlineTracer.Options
        {
            MinThicknessPt = 4,
            MaxThicknessPt = 10,
            MinFaceLengthPt = 10,
            MinWallLengthPt = 15,
            DarkFillOnly = true,
            WallFillZones =
            [
                new WallCenterlineTracer.FillZone(new SKRect(0, 100, 200, 106), 0.5f),
                new WallCenterlineTracer.FillZone(new SKRect(0, 300, 200, 306), 0.83f),
            ],
        };

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-50, 50, 300, 400), options);
        AssertEqual(1, result.Count, "only the dark-filled wall survives DarkFillOnly");
        AssertClose(103, result[0][0].Y, "the surviving centerline is the dark one", 1.0);

        // A sheet with only light fills must ignore the dark-only switch
        // instead of tracing nothing.
        var lightOnly = new WallCenterlineTracer.Options
        {
            MinThicknessPt = 4,
            MaxThicknessPt = 10,
            MinFaceLengthPt = 10,
            MinWallLengthPt = 15,
            DarkFillOnly = true,
            WallFillZones =
            [
                new WallCenterlineTracer.FillZone(new SKRect(0, 100, 200, 106), 0.83f),
                new WallCenterlineTracer.FillZone(new SKRect(0, 300, 200, 306), 0.83f),
            ],
        };
        List<SKPoint[]> fallback = WallCenterlineTracer.Trace(segments, Square(-50, 50, 300, 400), lightOnly);
        AssertEqual(2, fallback.Count, "no dark strips on the sheet disables the dark-only filter");
    }

    public static void DarkFillCutoffAdaptsToSheetLuminances()
    {
        // A plan drawn with unusual grays: rated walls at 0.15, partitions at
        // 0.55. A fixed 0.7 cutoff would call both "dark"; the auto split
        // must separate them relative to this sheet's own values.
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 100, 200, 100),
            Seg(0, 106, 200, 106),
            Seg(0, 300, 200, 300),
            Seg(0, 306, 200, 306),
        ];

        var auto = new WallCenterlineTracer.Options
        {
            MinThicknessPt = 4,
            MaxThicknessPt = 10,
            MinFaceLengthPt = 10,
            MinWallLengthPt = 15,
            DarkFillOnly = true,
            WallFillZones =
            [
                new WallCenterlineTracer.FillZone(new SKRect(0, 100, 200, 106), 0.15f),
                new WallCenterlineTracer.FillZone(new SKRect(0, 300, 200, 306), 0.55f),
            ],
        };

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-50, 50, 300, 400), auto);
        AssertEqual(1, result.Count, "auto cutoff must split this sheet's own gray families");
        AssertClose(103, result[0][0].Y, "the darker family wins on this sheet", 1.0);

        // A manual cutoff above both values keeps both walls (user override).
        var manual = new WallCenterlineTracer.Options
        {
            MinThicknessPt = 4,
            MaxThicknessPt = 10,
            MinFaceLengthPt = 10,
            MinWallLengthPt = 15,
            DarkFillOnly = true,
            DarkLuminanceMax = 0.6f,
            WallFillZones =
            [
                new WallCenterlineTracer.FillZone(new SKRect(0, 100, 200, 106), 0.15f),
                new WallCenterlineTracer.FillZone(new SKRect(0, 300, 200, 306), 0.55f),
            ],
        };
        List<SKPoint[]> overridden = WallCenterlineTracer.Trace(segments, Square(-50, 50, 300, 400), manual);
        AssertEqual(2, overridden.Count, "a manual cutoff overrides the auto split");
    }

    public static void BoundaryWallsAreExcludedByTolerance()
    {
        // Perimeter wall runs along the area's top edge (y=50); an interior
        // wall crosses the middle. With a boundary exclusion distance the
        // perimeter centerline is dropped, the interior one stays.
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 52, 300, 52),
            Seg(0, 58, 300, 58),
            Seg(0, 200, 300, 200),
            Seg(0, 206, 300, 206),
        ];

        var options = new WallCenterlineTracer.Options
        {
            MinThicknessPt = 4,
            MaxThicknessPt = 10,
            MinFaceLengthPt = 10,
            MinWallLengthPt = 15,
            BoundaryExclusionPt = 9,
        };

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-50, 50, 350, 400), options);
        AssertEqual(1, result.Count, "perimeter wall inside the edge offset must be dropped");
        AssertClose(203, result[0][0].Y, "the interior wall survives", 1.0);

        // Zero tolerance keeps both (the include-perimeter mode).
        List<SKPoint[]> keepAll = WallCenterlineTracer.Trace(segments, Square(-50, 50, 350, 400), DefaultOptions());
        AssertEqual(2, keepAll.Count, "no boundary exclusion keeps the perimeter wall");
    }

    public static void WallFaceCrossingTextZoneKeepsFullLength()
    {
        // A room tag overlaps the middle of a long wall. The face is only
        // ~15% covered by the word box, so the wall must trace end to end
        // without a gap under the label.
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 100, 400, 100),
            Seg(0, 106, 400, 106),
        ];

        var options = new WallCenterlineTracer.Options
        {
            MinThicknessPt = 4,
            MaxThicknessPt = 10,
            MinFaceLengthPt = 10,
            MinWallLengthPt = 15,
            ExcludedZones = [new SKRect(170, 90, 230, 116)],
        };

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-50, 50, 500, 300), options);
        AssertEqual(1, result.Count, "wall crossing a text zone still traces as one line");
        AssertClose(400, PolylineLength(result[0]), "no gap under the label", 2.0);
    }

    public static void RareAngleShortNoiseIsDropped()
    {
        // Two long orthogonal walls dominate the angle histogram; a short
        // tilted pair (a plumbing symbol stroke) sits alone at ~14 degrees
        // and must be discarded as noise.
        List<WallCenterlineTracer.Segment> segments =
        [
            Seg(0, 100, 800, 100),
            Seg(0, 106, 800, 106),
            Seg(50, 0, 50, 700),
            Seg(56, 0, 56, 700),
            Seg(300, 300, 330, 307.5f),
            Seg(300, 306, 330, 313.5f),
        ];

        List<SKPoint[]> result = WallCenterlineTracer.Trace(segments, Square(-50, -50, 900, 800), DefaultOptions());

        AssertEqual(2, result.Count, "only the two dominant-direction walls survive");
        foreach (SKPoint[] line in result)
        {
            foreach (SKPoint p in line)
                AssertTrue(Math.Abs(p.X - 315) > 50 || Math.Abs(p.Y - 306) > 50, "tilted noise centerline must be gone");
        }
    }

    public static void RasterLineFeaturesYieldCenterline()
    {
        using SKBitmap bitmap = BuildRasterWallTraceBitmap();
        bool ok = SheetOverlayRasterFeatureService.TryExtractSnap(
            bitmap,
            bitmap.Width,
            bitmap.Height,
            out PdfGeometrySnapResult snap,
            out string error);
        AssertTrue(ok, error);

        List<WallCenterlineTracer.Segment> segments = snap.Segments
            .Select(segment => new WallCenterlineTracer.Segment(segment.Start, segment.End))
            .ToList();
        List<SKPoint[]> result = WallCenterlineTracer.Trace(
            segments,
            Square(0, 80, 240, 130),
            DefaultOptions());

        AssertEqual(1, result.Count, "raster line features should trace the wall pair inside the area");
        AssertClose(103, result[0][0].Y, "raster centerline sits midway between wall faces", 1.2);
        AssertClose(200, PolylineLength(result[0]), "raster centerline spans the selected wall", 3.0);
    }

    // ------------------------------------------------------------------

    private static SKBitmap BuildRasterWallTraceBitmap()
    {
        var bitmap = new SKBitmap(420, 260, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = false,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
        };

        DrawWallPair(canvas, paint, 20, 100, 220, 106);
        DrawWallPair(canvas, paint, 20, 160, 220, 166);
        DrawWallPair(canvas, paint, 260, 40, 266, 220);
        DrawWallPair(canvas, paint, 320, 40, 326, 220);
        return bitmap;
    }

    private static void DrawWallPair(SKCanvas canvas, SKPaint paint, float x0, float y0, float x1, float y1)
    {
        if (Math.Abs(y1 - y0) <= Math.Abs(x1 - x0))
        {
            canvas.DrawLine(x0, y0, x1, y0, paint);
            canvas.DrawLine(x0, y1, x1, y1, paint);
            return;
        }

        canvas.DrawLine(x0, y0, x0, y1, paint);
        canvas.DrawLine(x1, y0, x1, y1, paint);
    }

    private static WallCenterlineTracer.Segment Seg(float x0, float y0, float x1, float y1) =>
        new(new SKPoint(x0, y0), new SKPoint(x1, y1));

    private static List<SKPoint> Square(float left, float top, float right, float bottom) =>
    [
        new SKPoint(left, top),
        new SKPoint(right, top),
        new SKPoint(right, bottom),
        new SKPoint(left, bottom),
    ];

    private static double PolylineLength(SKPoint[] points)
    {
        double total = 0;
        for (int i = 0; i + 1 < points.Length; i++)
            total += Distance(points[i], points[i + 1]);
        return total;
    }

    private static float Distance(SKPoint a, SKPoint b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual(int expected, int actual, string message)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static void AssertClose(double expected, double actual, string message, double tolerance = 0.01)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
