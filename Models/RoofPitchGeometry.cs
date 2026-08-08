using System;
using System.Globalization;
using SkiaSharp;

namespace OurPlanCore;

public readonly record struct RoofPitchResult(double PitchPerTwelve, double AngleDegrees, bool IsVertical)
{
    public string Label => IsVertical
        ? "∞:12"
        : $"{RoofPitchGeometry.FormatNumber(PitchPerTwelve)}:12";

    public string Status => $"{Label}  ({RoofPitchGeometry.FormatNumber(AngleDegrees)}°)";
}

public static class RoofPitchGeometry
{
    private const double AxisEpsilon = 0.0001;

    public static bool TryMeasure(SKPoint start, SKPoint end, out RoofPitchResult result)
    {
        double run = Math.Abs(end.X - start.X);
        double rise = Math.Abs(end.Y - start.Y);
        if (run <= AxisEpsilon && rise <= AxisEpsilon)
        {
            result = default;
            return false;
        }

        bool vertical = run <= AxisEpsilon;
        double pitch = vertical ? double.PositiveInfinity : rise / run * 12.0;
        double angle = Math.Atan2(rise, run) * 180.0 / Math.PI;
        result = new RoofPitchResult(pitch, angle, vertical);
        return true;
    }

    public static string Label(SKPoint start, SKPoint end) =>
        TryMeasure(start, end, out RoofPitchResult result) ? result.Label : "";

    internal static string FormatNumber(double value)
    {
        if (!double.IsFinite(value))
            return "∞";

        double rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
