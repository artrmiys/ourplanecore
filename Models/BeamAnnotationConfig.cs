using System;

namespace OurPlanCore;

public sealed class BeamAnnotationConfig
{
    public int SchemaVersion { get; set; } = 1;
    public bool KeepLineAnnotation { get; set; }
    public string LineColor { get; set; } = "#FF0000";

    public static BeamAnnotationConfig BuildDefault() => new()
    {
        KeepLineAnnotation = false,
        LineColor = "#FF0000",
    };

    public BeamAnnotationConfig Clone() => new()
    {
        SchemaVersion = 1,
        KeepLineAnnotation = KeepLineAnnotation,
        LineColor = NormalizeColor(LineColor),
    };

    public static BeamAnnotationConfig UpgradeForCurrentSchema(BeamAnnotationConfig? config) =>
        config?.Clone() ?? BuildDefault();

    public static string NormalizeColor(string? value) =>
        TryNormalizeColor(value, out string color) ? color : BuildDefault().LineColor;

    public static bool TryNormalizeColor(string? value, out string color)
    {
        color = "";
        string clean = (value ?? "").Trim();
        if (clean.StartsWith('#'))
            clean = clean[1..];
        if (clean.Length == 8)
            clean = clean[2..];
        if (clean.Length != 6)
            return false;

        foreach (char c in clean)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        color = $"#{clean.ToUpperInvariant()}";
        return true;
    }
}

public static class BeamAnnotationConfigProvider
{
    private static BeamAnnotationConfig _current = BeamAnnotationConfig.BuildDefault();

    public static BeamAnnotationConfig Current => _current.Clone();

    public static void Install(BeamAnnotationConfig config) =>
        _current = BeamAnnotationConfig.UpgradeForCurrentSchema(config);
}
