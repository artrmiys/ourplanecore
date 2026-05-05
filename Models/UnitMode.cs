namespace OurPlaneCore;

public enum UnitMode { Metric, Imperial }

public static class Units
{
    public static string FormatLength(double meters, UnitMode mode) =>
        mode == UnitMode.Imperial
            ? $"{meters / 0.3048:F2} ft"
            : $"{meters:F3} m";

    public static string FormatArea(double sqMeters, UnitMode mode) =>
        mode == UnitMode.Imperial
            ? $"{sqMeters / 0.0929030:F2} sf"
            : $"{sqMeters:F3} m²";

    public static string FormatCount(double n) => $"{(int)n} ea";
}
