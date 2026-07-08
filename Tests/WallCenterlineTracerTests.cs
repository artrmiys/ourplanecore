using OurPlaneCore.Models;
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

    // ------------------------------------------------------------------

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
