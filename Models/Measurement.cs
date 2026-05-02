using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace SmartTakeoffs;

public sealed class Measurement
{
    public string         Id         { get; init; } = Guid.NewGuid().ToString();
    public string         Name       { get; set; } = "";
    public string         Notes      { get; set; } = "";
    public string         MType      { get; set; }  = "line";   // point | line | area
    public List<SKPoint>  Points     { get; set; }  = [];
    public string         Color      { get; set; }  = "#FF4444";
    public string         PageFolder { get; set; }  = "";
    public string         TakeoffFolder { get; set; } = "";
    public double         ScaleMetersPerPt { get; set; }

    public double Value(double scaleMetersPerPt)
    {
        double effectiveScale = ScaleMetersPerPt > 0 ? ScaleMetersPerPt : scaleMetersPerPt;
        return MType switch
        {
            "point" => Points.Count,
            "line"  => LineLengthPt() * effectiveScale,
            "area"  => PolygonAreaPt() * effectiveScale * effectiveScale,
            _       => 0,
        };
    }

    public string Label(double scaleMetersPerPt, UnitMode unit = UnitMode.Metric)
    {
        double effectiveScale = ScaleMetersPerPt > 0 ? ScaleMetersPerPt : scaleMetersPerPt;
        if (effectiveScale <= 0)
            return MType switch
            {
                "point" => $"{Points.Count} ea",
                "line"  => $"{Points.Count - 1} seg",
                "area"  => $"{Points.Count} pts",
                _       => "",
            };

        double v = Value(effectiveScale);
        return MType switch
        {
            "point" => Units.FormatCount(v),
            "line"  => Units.FormatLength(v, unit),
            "area"  => Units.FormatArea(v, unit),
            _       => "",
        };
    }

    private double LineLengthPt()
    {
        double total = 0;
        for (int i = 1; i < Points.Count; i++)
        {
            float dx = Points[i].X - Points[i - 1].X;
            float dy = Points[i].Y - Points[i - 1].Y;
            total += Math.Sqrt(dx * dx + dy * dy);
        }
        return total;
    }

    private double PolygonAreaPt()
    {
        int n = Points.Count;
        if (n < 3) return 0;
        double area = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            area += Points[i].X * Points[j].Y;
            area -= Points[j].X * Points[i].Y;
        }
        return Math.Abs(area) / 2.0;
    }
}
