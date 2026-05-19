using System.Globalization;

namespace OurPlaneCore;

// Single source of truth for roof pitch text <-> rise-per-foot.
// Accepts "6/12", "6:12", "6 in 12", a bare rise over 12 ("6"), or a
// rise-per-foot fraction ("0.5"). Formats back as a reduced "rise/12".
public static class RoofPitchText
{
    public const double MinRisePerFoot = 0.001;
    public const double MaxRisePerFoot = 4.0;

    public static bool TryParse(string? value, out double risePerFoot)
    {
        risePerFoot = 0;
        string text = (value ?? "").Trim().ToLowerInvariant().Replace(',', '.');
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Replace(" in ", "/").Replace(":", "/");

        string[] parts = text.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double rise) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double run) &&
            run > 0)
        {
            risePerFoot = Clamp(rise / run);
            return true;
        }

        if (parts.Length == 1 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double single))
        {
            // > 1 reads as rise per 12 (e.g. "6" = 6/12); <= 1 as rise per foot.
            risePerFoot = Clamp(single > 1 ? single / 12.0 : single);
            return true;
        }

        return false;
    }

    public static double ParseOrDefault(string? value, double fallbackRisePerFoot) =>
        TryParse(value, out double risePerFoot) ? risePerFoot : fallbackRisePerFoot;

    // Format as rise/12 with one decimal only when needed (e.g. "6/12", "4.5/12").
    public static string Format(double risePerFoot)
    {
        double rise = Clamp(risePerFoot) * 12.0;
        string riseText = Math.Abs(rise - Math.Round(rise)) < 0.05
            ? Math.Round(rise).ToString(CultureInfo.InvariantCulture)
            : rise.ToString("0.0", CultureInfo.InvariantCulture);
        return $"{riseText}/12";
    }

    private static double Clamp(double risePerFoot) =>
        Math.Clamp(risePerFoot, MinRisePerFoot, MaxRisePerFoot);
}
