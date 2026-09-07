using SkiaSharp;

namespace OurPlanCore;

public static class ViewportBackgroundPolicy
{
    public const string DefaultColor = "#FFFFFF";
    public const byte PageTintAlpha = 82;
    public const byte DarkPageTintAlpha = 118;

    public static string NormalizeColor(string? color)
    {
        string clean = string.IsNullOrWhiteSpace(color) ? DefaultColor : color.Trim();
        try
        {
            SKColor parsed = SKColor.Parse(clean);
            return $"#{parsed.Red:X2}{parsed.Green:X2}{parsed.Blue:X2}";
        }
        catch
        {
            return DefaultColor;
        }
    }

    public static bool IsDefaultColor(string? color) =>
        string.Equals(NormalizeColor(color), DefaultColor, StringComparison.Ordinal);

    public static bool ShouldTintRenderedPage(string? color)
    {
        return RenderedPageTintAlpha(color) > 0;
    }

    public static byte RenderedPageTintAlpha(string? color)
    {
        string clean = NormalizeColor(color);
        if (string.Equals(clean, DefaultColor, StringComparison.Ordinal))
            return 0;

        SKColor parsed = SKColor.Parse(clean);
        double luminance = ((0.2126 * parsed.Red) + (0.7152 * parsed.Green) + (0.0722 * parsed.Blue)) / 255.0;
        return luminance >= 0.70 ? PageTintAlpha : DarkPageTintAlpha;
    }
}
