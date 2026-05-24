using System;
using System.Globalization;

namespace OurPlaneCore;

public static class OpeningTakeoffService
{
    public static string FormatSizeFeet(double widthFeet, double heightFeet) =>
        $"{FormatDimensionFeet(widthFeet)}x{FormatDimensionFeet(heightFeet)}";

    public static string BuildDefaultCountName(string sizeText) =>
        string.IsNullOrWhiteSpace(sizeText) ? "0.0x0.0" : sizeText.Trim();

    private static string FormatDimensionFeet(double feet) =>
        (!double.IsFinite(feet) || feet < 0 ? 0 : feet).ToString("0.0", CultureInfo.InvariantCulture);
}
