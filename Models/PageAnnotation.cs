using System;
using System.Collections.Generic;
using SkiaSharp;

namespace SmartTakeoffs;

public sealed class PageAnnotation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Kind { get; set; } = "line";
    public string Text { get; set; } = "";
    public string Color { get; set; } = "#1565C0";
    public string PageFolder { get; set; } = "";
    public double ScaleMetersPerPt { get; set; }
    public List<SKPoint> Points { get; set; } = [];
}
