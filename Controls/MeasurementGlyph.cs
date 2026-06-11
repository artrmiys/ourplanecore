using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OurPlaneCore;
using SkiaSharp;
using WpfShapes = System.Windows.Shapes;

namespace OurPlaneCore.Controls;

// Single source of truth for measurement-type icons.
// Glyph is drawn filled in the takeoff color with a darker stroke around it,
// so it works equally on any panel background — no separate colored swatch.
// Identical layout in WPF (tree, toolbar, active-target bar) and Skia (on-canvas legend).
public enum MeasurementGlyphKind { Point, Line, Area, Joist, Count, CountCross, CountSquare, CountStar, CountTriangle, CountDiamond, CountRing }

public static class MeasurementGlyph
{
    public static MeasurementGlyphKind Parse(string? kind, bool joist = false, string countSymbol = "")
    {
        if (joist) return MeasurementGlyphKind.Joist;
        return (kind ?? "").Trim().ToLowerInvariant() switch
        {
            "point" => CountKind(countSymbol),
            "count" => string.IsNullOrWhiteSpace(countSymbol)
                ? MeasurementGlyphKind.Count
                : CountKind(countSymbol),
            "area"  => MeasurementGlyphKind.Area,
            "joist" => MeasurementGlyphKind.Joist,
            _       => MeasurementGlyphKind.Line,
        };
    }

    public static MeasurementGlyphKind CountKind(string? countSymbol) =>
        CountDisplaySymbol.Normalize(countSymbol) switch
        {
            CountDisplaySymbol.Cross => MeasurementGlyphKind.CountCross,
            CountDisplaySymbol.Square => MeasurementGlyphKind.CountSquare,
            CountDisplaySymbol.Star => MeasurementGlyphKind.CountStar,
            CountDisplaySymbol.Triangle => MeasurementGlyphKind.CountTriangle,
            CountDisplaySymbol.Diamond => MeasurementGlyphKind.CountDiamond,
            CountDisplaySymbol.Ring => MeasurementGlyphKind.CountRing,
            _ => MeasurementGlyphKind.Point,
        };

    // ── WPF rendering ─────────────────────────────────────────────────────────
    // brush = the takeoff color. Glyph fills with it; the stroke is a darker
    // variant of the same color so the icon reads on any panel background.
    public static FrameworkElement CreateWpf(MeasurementGlyphKind kind, Brush brush, double size, Thickness margin)
    {
        var canvas = new Canvas
        {
            Width = size,
            Height = size,
            Margin = margin,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
        };

        double s = size;
        double pad = Math.Max(1.0, s * 0.10);
        double stroke = Math.Max(1.2, s / 9.0);
        Brush fillBrush = brush;
        Brush strokeBrush = DarkerBrush(brush, 60);

        switch (kind)
        {
            case MeasurementGlyphKind.Point:
            case MeasurementGlyphKind.Count:
            {
                double diameter = s - pad * 2 - stroke;
                if (diameter < 2) diameter = 2;
                var circle = new WpfShapes.Ellipse
                {
                    Width = diameter, Height = diameter,
                    Fill = fillBrush,
                    Stroke = strokeBrush,
                    StrokeThickness = stroke,
                };
                canvas.Children.Add(circle);
                Canvas.SetLeft(circle, (s - diameter) / 2.0);
                Canvas.SetTop(circle, (s - diameter) / 2.0);
                if (kind == MeasurementGlyphKind.Count)
                {
                    var dot = new WpfShapes.Ellipse
                    {
                        Width = Math.Max(1.5, s * 0.16),
                        Height = Math.Max(1.5, s * 0.16),
                        Fill = Brushes.White,
                    };
                    canvas.Children.Add(dot);
                    Canvas.SetLeft(dot, (s - dot.Width) / 2.0);
                    Canvas.SetTop(dot, (s - dot.Height) / 2.0);
                }
                break;
            }

            case MeasurementGlyphKind.CountCross:
            {
                double left = pad + stroke;
                double right = s - pad - stroke;
                AddLine(canvas, strokeBrush, Math.Max(2.0, stroke * 2.0), left, left, right, right);
                AddLine(canvas, strokeBrush, Math.Max(2.0, stroke * 2.0), right, left, left, right);
                AddLine(canvas, fillBrush, Math.Max(1.4, stroke * 1.25), left, left, right, right);
                AddLine(canvas, fillBrush, Math.Max(1.4, stroke * 1.25), right, left, left, right);
                break;
            }

            case MeasurementGlyphKind.CountSquare:
            {
                var rect = new WpfShapes.Rectangle
                {
                    Width = s - pad * 2,
                    Height = s - pad * 2,
                    Stroke = strokeBrush,
                    StrokeThickness = stroke,
                    Fill = fillBrush,
                    RadiusX = Math.Max(0.5, s * 0.04),
                    RadiusY = Math.Max(0.5, s * 0.04),
                };
                canvas.Children.Add(rect);
                Canvas.SetLeft(rect, pad);
                Canvas.SetTop(rect, pad);
                break;
            }

            case MeasurementGlyphKind.CountStar:
            case MeasurementGlyphKind.CountTriangle:
            case MeasurementGlyphKind.CountDiamond:
            {
                double cx = s / 2.0;
                double cy = s / 2.0;
                double r = (s - pad * 2 - stroke) / 2.0;
                if (r < 1.5) r = 1.5;
                var polygon = new WpfShapes.Polygon
                {
                    Fill = fillBrush,
                    Stroke = strokeBrush,
                    StrokeThickness = stroke,
                    StrokeLineJoin = PenLineJoin.Round,
                };
                foreach ((double x, double y) in PolygonVertices(kind, cx, cy, r))
                    polygon.Points.Add(new Point(x, y));
                canvas.Children.Add(polygon);
                break;
            }

            case MeasurementGlyphKind.CountRing:
            {
                double diameter = s - pad * 2 - stroke;
                if (diameter < 3) diameter = 3;
                double ringWidth = Math.Max(2.0, diameter * 0.28);
                var outline = new WpfShapes.Ellipse
                {
                    Width = diameter, Height = diameter,
                    Stroke = strokeBrush,
                    StrokeThickness = ringWidth + stroke,
                    Fill = Brushes.Transparent,
                };
                var ring = new WpfShapes.Ellipse
                {
                    Width = diameter, Height = diameter,
                    Stroke = fillBrush,
                    StrokeThickness = ringWidth,
                    Fill = Brushes.Transparent,
                };
                canvas.Children.Add(outline);
                canvas.Children.Add(ring);
                Canvas.SetLeft(outline, (s - diameter) / 2.0);
                Canvas.SetTop(outline, (s - diameter) / 2.0);
                Canvas.SetLeft(ring, (s - diameter) / 2.0);
                Canvas.SetTop(ring, (s - diameter) / 2.0);
                break;
            }

            case MeasurementGlyphKind.Line:
            {
                double midY = s / 2.0;
                double left = pad + stroke;
                double right = s - pad - stroke;

                // Wide colored line in the takeoff color.
                var fillLine = new WpfShapes.Line
                {
                    X1 = left, Y1 = midY,
                    X2 = right, Y2 = midY,
                    Stroke = fillBrush,
                    StrokeThickness = Math.Max(2.0, stroke * 1.7),
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                };
                canvas.Children.Add(fillLine);

                // Filled dot endpoints with darker stroke.
                double dotR = Math.Max(2.0, stroke * 1.4);
                AddDot(canvas, fillBrush, strokeBrush, stroke * 0.6, left, midY, dotR);
                AddDot(canvas, fillBrush, strokeBrush, stroke * 0.6, right, midY, dotR);
                break;
            }

            case MeasurementGlyphKind.Area:
            {
                double r = Math.Max(1.0, s * 0.22);
                var rect = new WpfShapes.Rectangle
                {
                    Width = s - pad * 2,
                    Height = s - pad * 2,
                    Stroke = strokeBrush,
                    StrokeThickness = stroke,
                    Fill = fillBrush,
                    RadiusX = r, RadiusY = r,
                };
                canvas.Children.Add(rect);
                Canvas.SetLeft(rect, pad);
                Canvas.SetTop(rect, pad);
                break;
            }

            case MeasurementGlyphKind.Joist:
            {
                double r = Math.Max(1.0, s * 0.20);
                var rect = new WpfShapes.Rectangle
                {
                    Width = s - pad * 2,
                    Height = s - pad * 2,
                    Stroke = strokeBrush,
                    StrokeThickness = stroke,
                    Fill = fillBrush,
                    RadiusX = r, RadiusY = r,
                };
                canvas.Children.Add(rect);
                Canvas.SetLeft(rect, pad);
                Canvas.SetTop(rect, pad);

                // Three parallel "joists" inside, drawn with the darker stroke
                // so they read on top of the colored fill.
                Brush bars = strokeBrush;
                double inner = s - pad * 2 - stroke * 2;
                int barCount = 3;
                double step = inner / (barCount + 1);
                double yTop = pad + stroke;
                double xLeft = pad + stroke + 1;
                double xRight = s - pad - stroke - 1;
                for (int i = 1; i <= barCount; i++)
                {
                    var bar = new WpfShapes.Line
                    {
                        X1 = xLeft, X2 = xRight,
                        Y1 = yTop + step * i, Y2 = yTop + step * i,
                        Stroke = bars,
                        StrokeThickness = Math.Max(0.9, stroke * 0.65),
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                    };
                    canvas.Children.Add(bar);
                }
                break;
            }
        }

        return canvas;
    }

    private static void AddDot(Canvas host, Brush fill, Brush stroke, double strokeWidth, double cx, double cy, double r)
    {
        var dot = new WpfShapes.Ellipse
        {
            Width = r * 2, Height = r * 2,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = strokeWidth,
        };
        host.Children.Add(dot);
        Canvas.SetLeft(dot, cx - r);
        Canvas.SetTop(dot, cy - r);
    }

    private static void AddLine(Canvas host, Brush stroke, double strokeWidth, double x1, double y1, double x2, double y2)
    {
        host.Children.Add(new WpfShapes.Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = stroke,
            StrokeThickness = strokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
    }

    // Shared vertex layout for star/triangle/diamond so WPF and Skia render
    // the exact same silhouette.
    private static IEnumerable<(double X, double Y)> PolygonVertices(
        MeasurementGlyphKind kind, double cx, double cy, double r)
    {
        switch (kind)
        {
            case MeasurementGlyphKind.CountStar:
            {
                double inner = r * 0.45;
                for (int i = 0; i < 10; i++)
                {
                    double radius = i % 2 == 0 ? r : inner;
                    double angle = -Math.PI / 2.0 + i * Math.PI / 5.0;
                    yield return (cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
                }
                break;
            }

            case MeasurementGlyphKind.CountTriangle:
            {
                for (int i = 0; i < 3; i++)
                {
                    double angle = -Math.PI / 2.0 + i * 2.0 * Math.PI / 3.0;
                    yield return (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
                }
                break;
            }

            case MeasurementGlyphKind.CountDiamond:
            {
                double half = r * 0.8;
                yield return (cx, cy - r);
                yield return (cx + half, cy);
                yield return (cx, cy + r);
                yield return (cx - half, cy);
                break;
            }
        }
    }

    private static Brush DarkerBrush(Brush source, int amount)
    {
        if (source is SolidColorBrush scb)
        {
            byte r = (byte)Math.Max(0, scb.Color.R - amount);
            byte g = (byte)Math.Max(0, scb.Color.G - amount);
            byte b = (byte)Math.Max(0, scb.Color.B - amount);
            return new SolidColorBrush(Color.FromArgb(scb.Color.A, r, g, b));
        }
        return Brushes.Black;
    }

    // ── Skia rendering ────────────────────────────────────────────────────────
    public static void DrawSkia(SKCanvas canvas, MeasurementGlyphKind kind, SKColor color, SKRect bounds)
    {
        float s = Math.Min(bounds.Width, bounds.Height);
        if (s <= 0) return;

        float pad = Math.Max(1f, s * 0.10f);
        float stroke = Math.Max(1.1f, s / 9f);
        SKColor strokeColor = DarkerColor(color, 60);

        using var fillPaint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        using var strokePaint = new SKPaint
        {
            Color = strokeColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };

        float cx = bounds.MidX;
        float cy = bounds.MidY;
        SKRect inner = new(
            bounds.Left + pad,
            bounds.Top + pad,
            bounds.Right - pad,
            bounds.Bottom - pad);

        switch (kind)
        {
            case MeasurementGlyphKind.Point:
            case MeasurementGlyphKind.Count:
            {
                float r = (Math.Min(inner.Width, inner.Height) - stroke) / 2f;
                if (r < 1f) r = 1f;
                canvas.DrawCircle(cx, cy, r, fillPaint);
                canvas.DrawCircle(cx, cy, r, strokePaint);
                if (kind == MeasurementGlyphKind.Count)
                {
                    using var hole = new SKPaint
                    {
                        Color = SKColors.White,
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true,
                    };
                    canvas.DrawCircle(cx, cy, Math.Max(1f, s * 0.08f), hole);
                }
                break;
            }

            case MeasurementGlyphKind.CountCross:
            {
                float left = inner.Left + stroke;
                float right = inner.Right - stroke;
                float top = inner.Top + stroke;
                float bottom = inner.Bottom - stroke;
                using var outline = new SKPaint
                {
                    Color = strokeColor,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = Math.Max(2.0f, stroke * 1.95f),
                    StrokeCap = SKStrokeCap.Round,
                    IsAntialias = true,
                };
                using var mark = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = Math.Max(1.2f, stroke * 1.2f),
                    StrokeCap = SKStrokeCap.Round,
                    IsAntialias = true,
                };
                canvas.DrawLine(left, top, right, bottom, outline);
                canvas.DrawLine(right, top, left, bottom, outline);
                canvas.DrawLine(left, top, right, bottom, mark);
                canvas.DrawLine(right, top, left, bottom, mark);
                break;
            }

            case MeasurementGlyphKind.CountSquare:
            {
                canvas.DrawRect(inner, fillPaint);
                canvas.DrawRect(inner, strokePaint);
                break;
            }

            case MeasurementGlyphKind.CountStar:
            case MeasurementGlyphKind.CountTriangle:
            case MeasurementGlyphKind.CountDiamond:
            {
                float r = (Math.Min(inner.Width, inner.Height) - stroke) / 2f;
                if (r < 1.5f) r = 1.5f;
                using var path = new SKPath();
                bool first = true;
                foreach ((double x, double y) in PolygonVertices(kind, cx, cy, r))
                {
                    if (first) { path.MoveTo((float)x, (float)y); first = false; }
                    else path.LineTo((float)x, (float)y);
                }
                path.Close();
                canvas.DrawPath(path, fillPaint);
                canvas.DrawPath(path, strokePaint);
                break;
            }

            case MeasurementGlyphKind.CountRing:
            {
                float r = (Math.Min(inner.Width, inner.Height) - stroke) / 2f;
                if (r < 1.5f) r = 1.5f;
                float ringWidth = Math.Max(1.6f, r * 0.55f);
                float ringR = r - ringWidth / 2f;
                if (ringR < 1f) ringR = 1f;
                using var outline = new SKPaint
                {
                    Color = strokeColor,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = ringWidth + stroke,
                    IsAntialias = true,
                };
                using var ring = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = ringWidth,
                    IsAntialias = true,
                };
                canvas.DrawCircle(cx, cy, ringR, outline);
                canvas.DrawCircle(cx, cy, ringR, ring);
                break;
            }

            case MeasurementGlyphKind.Line:
            {
                float midY = cy;
                float xL = inner.Left + stroke;
                float xR = inner.Right - stroke;
                using var thick = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = Math.Max(1.8f, stroke * 1.7f),
                    StrokeCap = SKStrokeCap.Round,
                    IsAntialias = true,
                };
                canvas.DrawLine(xL, midY, xR, midY, thick);

                float dotR = Math.Max(1.8f, stroke * 1.35f);
                canvas.DrawCircle(xL, midY, dotR, fillPaint);
                canvas.DrawCircle(xR, midY, dotR, fillPaint);
                using var dotStroke = new SKPaint
                {
                    Color = strokeColor,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = Math.Max(0.8f, stroke * 0.55f),
                    IsAntialias = true,
                };
                canvas.DrawCircle(xL, midY, dotR, dotStroke);
                canvas.DrawCircle(xR, midY, dotR, dotStroke);
                break;
            }

            case MeasurementGlyphKind.Area:
            {
                float r = Math.Max(1f, s * 0.22f);
                canvas.DrawRoundRect(inner, r, r, fillPaint);
                canvas.DrawRoundRect(inner, r, r, strokePaint);
                break;
            }

            case MeasurementGlyphKind.Joist:
            {
                float r = Math.Max(1f, s * 0.20f);
                canvas.DrawRoundRect(inner, r, r, fillPaint);
                canvas.DrawRoundRect(inner, r, r, strokePaint);

                int barCount = 3;
                float step = (inner.Height - stroke * 2) / (barCount + 1);
                using var bar = new SKPaint
                {
                    Color = strokeColor,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = Math.Max(0.9f, stroke * 0.65f),
                    StrokeCap = SKStrokeCap.Round,
                    IsAntialias = true,
                };
                for (int i = 1; i <= barCount; i++)
                {
                    float y = inner.Top + stroke + step * i;
                    canvas.DrawLine(inner.Left + stroke + 1f, y, inner.Right - stroke - 1f, y, bar);
                }
                break;
            }
        }
    }

    private static SKColor DarkerColor(SKColor c, int amount)
    {
        byte r = (byte)Math.Max(0, c.Red   - amount);
        byte g = (byte)Math.Max(0, c.Green - amount);
        byte b = (byte)Math.Max(0, c.Blue  - amount);
        return new SKColor(r, g, b, c.Alpha);
    }

    // Map from legacy short signs ("○", "□", "╱", "□╱") to glyph kind for callers
    // that historically passed a string sign through the legend pipeline.
    public static MeasurementGlyphKind FromSign(string? sign)
    {
        if (string.IsNullOrEmpty(sign)) return MeasurementGlyphKind.Line;
        if (sign.Contains('□') && sign.Contains('╱')) return MeasurementGlyphKind.Joist;
        if (sign.Contains('○')) return MeasurementGlyphKind.Point;
        if (sign.Contains('□')) return MeasurementGlyphKind.Area;
        return MeasurementGlyphKind.Line;
    }
}
