using OurPlanCore;
using SkiaSharp;

internal static class AreaLineGridServiceTests
{
    private const double MetersPerFoot = 0.3048;

    public static void RectangleCreatesHorizontalAndVerticalSegments()
    {
        Measurement area = RectangleArea(10, 8);

        AreaLineGridResult result = AreaLineGridService.Generate(
            area,
            fallbackScaleMetersPerPt: 0,
            new AreaLineGridOptions(
                IncludeHorizontal: true,
                HorizontalSpacingInches: 24,
                IncludeVertical: true,
                VerticalSpacingInches: 24));

        AssertEqual(5, result.HorizontalCount, "horizontal 10x8 ft grid at 24 in o.c.");
        AssertEqual(6, result.VerticalCount, "vertical 10x8 ft grid at 24 in o.c.");
        AssertEqual(11, result.Count, "combined grid segment count");
        AssertClose(98 * MetersPerFoot, result.TotalLengthMeters, "combined editable line length");
        AssertTrue(
            result.Segments.All(segment => segment.Start != segment.End && segment.LengthMeters > 0),
            "every generated grid row must be a real two-point segment");
    }

    public static void HolesSplitGridSegments()
    {
        Measurement area = RectangleArea(10, 10);
        area.Holes.Add(
        [
            new SKPoint(3, 3),
            new SKPoint(7, 3),
            new SKPoint(7, 7),
            new SKPoint(3, 7),
        ]);

        AreaLineGridResult result = AreaLineGridService.Generate(
            area,
            fallbackScaleMetersPerPt: 0,
            new AreaLineGridOptions(
                IncludeHorizontal: true,
                HorizontalSpacingInches: 24,
                IncludeVertical: false,
                VerticalSpacingInches: 24));

        AssertEqual(8, result.HorizontalCount, "hole should split two horizontal grid rows into four segments");
        AssertEqual(0, result.VerticalCount, "vertical grid disabled");
        AssertClose(52 * MetersPerFoot, result.TotalLengthMeters, "hole-clipped horizontal grid length");

        foreach (AreaLineGridSegment segment in result.Segments)
        {
            float y = segment.Start.Y;
            if (y <= 3 || y >= 7)
                continue;

            float left = Math.Min(segment.Start.X, segment.End.X);
            float right = Math.Max(segment.Start.X, segment.End.X);
            AssertFalse(left < 3 && right > 7, "grid segment must not cross the area hole");
        }
    }

    private static Measurement RectangleArea(float widthFeet, float heightFeet) =>
        new()
        {
            MType = "area",
            ScaleMetersPerPt = MetersPerFoot,
            Points =
            [
                new SKPoint(0, 0),
                new SKPoint(widthFeet, 0),
                new SKPoint(widthFeet, heightFeet),
                new SKPoint(0, heightFeet),
            ],
        };

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);

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
