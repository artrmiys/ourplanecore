using System;
using System.Collections.Generic;

namespace OurPlanCore;

public sealed record AnnotationColorChoice(string Label, string Hex);

public static class AnnotationColorPalette
{
    public static IReadOnlyList<AnnotationColorChoice> Presets { get; } =
    [
        new("Red", "#FF4444"),
        new("Blue", "#2196F3"),
        new("Green", "#4CAF50"),
        new("Orange", "#FF9800"),
        new("Purple", "#9C27B0"),
        new("Cyan", "#00BCD4"),
        new("Yellow", "#FFC107"),
        new("Black", "#212121"),
    ];

    public static string DisplayName(string? color)
    {
        string normalized = BeamAnnotationConfig.NormalizeColor(color);
        foreach (AnnotationColorChoice choice in Presets)
        {
            if (string.Equals(choice.Hex, normalized, StringComparison.OrdinalIgnoreCase))
                return choice.Label;
        }

        return string.Equals(normalized, "#FF0000", StringComparison.OrdinalIgnoreCase)
            ? "Red"
            : "Saved color";
    }
}
