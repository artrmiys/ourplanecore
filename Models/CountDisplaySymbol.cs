using System.Collections.Generic;

namespace OurPlaneCore;

public static class CountDisplaySymbol
{
    public const string Circle = "circle";
    public const string Cross = "cross";
    public const string Square = "square";
    public const string Star = "star";
    public const string Triangle = "triangle";
    public const string Diamond = "diamond";
    public const string Ring = "ring";

    public static IReadOnlyList<string> All { get; } = [Circle, Cross, Square, Star, Triangle, Diamond, Ring];

    public static string Normalize(string? value)
    {
        string clean = (value ?? "").Trim().ToLowerInvariant();
        return clean switch
        {
            "x" or "cross" or "plus" or "крест" or "крестик" => Cross,
            "box" or "square" or "rect" or "квадрат" or "квадратик" => Square,
            "*" or "star" or "звезда" or "звездочка" or "звёздочка" => Star,
            "tri" or "triangle" or "треугольник" => Triangle,
            "rhomb" or "rhombus" or "diamond" or "ромб" or "ромбик" => Diamond,
            "donut" or "ring" or "кольцо" or "колечко" => Ring,
            _ => Circle,
        };
    }

    public static string Title(string? value) =>
        Normalize(value) switch
        {
            Cross => "Cross",
            Square => "Square",
            Star => "Star",
            Triangle => "Triangle",
            Diamond => "Diamond",
            Ring => "Ring",
            _ => "Circle",
        };
}
