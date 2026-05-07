using SkiaSharp;

namespace OurPlaneCore;

public static class ViewportBackgroundPolicy
{
    public const string DefaultColor = "#FFFFFF";

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
}
