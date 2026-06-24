using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlaneCore;

public sealed record AreaLineGridOptions(
    bool IncludeHorizontal,
    double HorizontalSpacingInches,
    bool IncludeVertical,
    double VerticalSpacingInches);

public sealed record AreaLineGridSegment(
    SKPoint Start,
    SKPoint End,
    double LengthMeters,
    AreaLineGridAxis Axis);

public sealed record AreaLineGridResult(
    IReadOnlyList<AreaLineGridSegment> Segments,
    int HorizontalCount,
    int VerticalCount,
    double TotalLengthMeters,
    double AreaMetersSquared)
{
    public int Count => Segments.Count;
}

public enum AreaLineGridAxis
{
    Horizontal,
    Vertical,
}

public static class AreaLineGridService
{
    public const int MaxGeneratedSegments = 5000;

    public static AreaLineGridResult Generate(
        Measurement area,
        double fallbackScaleMetersPerPt,
        AreaLineGridOptions options)
    {
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(area.MType) != "area" || area.Points.Count < 3)
            throw new ArgumentException("A valid Area measurement is required.", nameof(area));
        if (!options.IncludeHorizontal && !options.IncludeVertical)
            throw new ArgumentException("Select horizontal, vertical, or both directions.", nameof(options));

        double scale = area.ScaleMetersPerPt > 0 ? area.ScaleMetersPerPt : fallbackScaleMetersPerPt;
        if (scale <= 0)
            throw new InvalidOperationException("Set the sheet scale before creating line grid segments.");

        var segments = new List<AreaLineGridSegment>();
        int horizontalCount = 0;
        int verticalCount = 0;
        double areaMetersSquared = 0;

        if (options.IncludeHorizontal)
        {
            ValidateSpacing(options.HorizontalSpacingInches, nameof(options.HorizontalSpacingInches));
            JoistLayoutResult layout = GenerateAxis(area, scale, options.HorizontalSpacingInches, 0);
            areaMetersSquared = Math.Max(areaMetersSquared, layout.AreaMetersSquared);
            AddSegments(segments, layout, AreaLineGridAxis.Horizontal);
            horizontalCount = layout.Count;
        }

        if (options.IncludeVertical)
        {
            ValidateSpacing(options.VerticalSpacingInches, nameof(options.VerticalSpacingInches));
            JoistLayoutResult layout = GenerateAxis(area, scale, options.VerticalSpacingInches, 90);
            areaMetersSquared = Math.Max(areaMetersSquared, layout.AreaMetersSquared);
            AddSegments(segments, layout, AreaLineGridAxis.Vertical);
            verticalCount = layout.Count;
        }

        if (segments.Count > MaxGeneratedSegments)
        {
            throw new InvalidOperationException(
                $"Grid would create {segments.Count} line segments. Increase spacing or split the area; limit is {MaxGeneratedSegments}.");
        }

        return new AreaLineGridResult(
            segments,
            horizontalCount,
            verticalCount,
            segments.Sum(segment => segment.LengthMeters),
            areaMetersSquared);
    }

    private static JoistLayoutResult GenerateAxis(
        Measurement area,
        double scaleMetersPerPt,
        double spacingInches,
        double directionDegrees)
    {
        return JoistTakeoffCalculator.Calculate(
            area.Points,
            area.Holes,
            scaleMetersPerPt,
            spacingInches,
            directionDegrees,
            JoistTakeoffCalculator.RoundingNone,
            pitch: "",
            addEndJoist: true);
    }

    private static void AddSegments(
        List<AreaLineGridSegment> segments,
        JoistLayoutResult layout,
        AreaLineGridAxis axis)
    {
        foreach (JoistSegment segment in layout.Segments)
            segments.Add(new AreaLineGridSegment(
                segment.Start,
                segment.End,
                segment.FlatLengthMeters,
                axis));
    }

    private static void ValidateSpacing(double spacingInches, string parameterName)
    {
        if (!double.IsFinite(spacingInches) || spacingInches <= 0)
            throw new ArgumentOutOfRangeException(parameterName, "Spacing must be a positive number of inches.");
    }
}
