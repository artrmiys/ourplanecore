using System.Collections.Generic;

namespace OurPlaneCore;

public static class CountDisplaySymbol
{
    public const string Circle = "circle";
    public const string Cross = "cross";
    public const string Square = "square";

    public static IReadOnlyList<string> All { get; } = [Circle, Cross, Square];

    public static string Normalize(string? value)
    {
        string clean = (value ?? "").Trim().ToLowerInvariant();
        return clean switch
        {
            "x" or "cross" or "plus" or "крест" or "крестик" => Cross,
            "box" or "square" or "rect" or "квадрат" or "квадратик" => Square,
            _ => Circle,
        };
    }

    public static string Title(string? value) =>
        Normalize(value) switch
        {
            Cross => "Cross",
            Square => "Square",
            _ => "Circle",
        };
}
