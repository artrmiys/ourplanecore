using OurPlaneCore;
using SkiaSharp;

internal static class PointAlongLineServiceTests
{
    private const double MetersPerFoot = 0.3048;

    public static void StraightLineCreatesEndpointAndStepPoints()
    {
        Measurement line = Line([new SKPoint(0, 0), new SKPoint(10, 0)]);

        PointAlongLineResult result = PointAlongLineService.Generate(
            line,
            fallbackScaleMetersPerPt: 0,
            new PointAlongLineOptions(SpacingInches: 24));

        AssertEqual(6, result.Points.Count, "10 ft line at 24 in spacing should include both ends");
        AssertPoint(result.Points[0], 0, 0, "first point");
        AssertPoint(result.Points[1], 2, 0, "step point");
        AssertPoint(result.Points[^1], 10, 0, "end point");
        AssertClose(10 * MetersPerFoot, result.TotalLengthMeters, "total length");
    }

    public static void PolylineCarriesSpacingAcrossVertices()
    {
        Measurement line = Line([new SKPoint(0, 0), new SKPoint(4, 0), new SKPoint(4, 2)]);

        PointAlongLineResult result = PointAlongLineService.Generate(
            line,
            fallbackScaleMetersPerPt: 0,
            new PointAlongLineOptions(SpacingInches: 36));

        AssertEqual(3, result.Points.Count, "6 ft polyline at 36 in spacing");
        AssertPoint(result.Points[0], 0, 0, "start");
        AssertPoint(result.Points[1], 3, 0, "before corner");
        AssertPoint(result.Points[2], 4, 2, "end");
    }

    public static void ManyLinesAvoidDuplicateSharedEndpoint()
    {
        PointAlongLineResult result = PointAlongLineService.GenerateMany(
            [
                Line([new SKPoint(0, 0), new SKPoint(4, 0)]),
                Line([new SKPoint(4, 0), new SKPoint(8, 0)]),
            ],
            fallbackScaleMetersPerPt: 0,
            new PointAlongLineOptions(SpacingInches: 24));

        AssertEqual(5, result.Points.Count, "two connected 4 ft lines at 24 in spacing should not double-count the shared endpoint");
        AssertPoint(result.Points[0], 0, 0, "first");
        AssertPoint(result.Points[2], 4, 0, "shared endpoint");
        AssertPoint(result.Points[^1], 8, 0, "last");
        AssertClose(8 * MetersPerFoot, result.TotalLengthMeters, "total multi-line length");
    }

    public static void MissingScaleIsRejected()
    {
        Measurement line = new()
        {
            MType = "line",
            Points = [new SKPoint(0, 0), new SKPoint(10, 0)],
        };

        try
        {
            PointAlongLineService.Generate(
                line,
                fallbackScaleMetersPerPt: 0,
                new PointAlongLineOptions(SpacingInches: 16));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("missing scale should be rejected");
    }

    private static Measurement Line(IReadOnlyList<SKPoint> points) =>
        new()
        {
            MType = "line",
            ScaleMetersPerPt = MetersPerFoot,
            Points = points.ToList(),
        };

    private static void AssertPoint(SKPoint actual, float x, float y, string message)
    {
        AssertClose(x, actual.X, $"{message} x", tolerance: 0.0001);
        AssertClose(y, actual.Y, $"{message} y", tolerance: 0.0001);
    }

    private static void AssertEqual(int expected, int actual, string message)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static void AssertClose(double expected, double actual, string message, double tolerance = 0.000001)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
