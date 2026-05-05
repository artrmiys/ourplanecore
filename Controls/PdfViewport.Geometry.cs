using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Docnet.Core;
using Docnet.Core.Models;
using OurPlaneCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private static float DistanceSquared(SKPoint a, SKPoint b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static double DistanceSquared(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
    {
        float vx = b.X - a.X;
        float vy = b.Y - a.Y;
        float lenSq = vx * vx + vy * vy;
        if (lenSq <= 0.0001f)
            return MathF.Sqrt(DistanceSquared(p, a));

        float t = ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / lenSq;
        t = Math.Clamp(t, 0f, 1f);
        var projection = new SKPoint(a.X + t * vx, a.Y + t * vy);
        return MathF.Sqrt(DistanceSquared(p, projection));
    }

    private static SKRect NormalizeRect(SKPoint a, SKPoint b) =>
        new(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Max(a.X, b.X),
            Math.Max(a.Y, b.Y));

    private static bool RectContains(SKRect rect, SKPoint point) =>
        point.X >= rect.Left &&
        point.X <= rect.Right &&
        point.Y >= rect.Top &&
        point.Y <= rect.Bottom;

    private static bool RectsIntersect(SKRect a, SKRect b) =>
        a.Left <= b.Right &&
        a.Right >= b.Left &&
        a.Top <= b.Bottom &&
        a.Bottom >= b.Top;

    private static bool SegmentIntersectsRect(SKPoint a, SKPoint b, SKRect rect)
    {
        if (RectContains(rect, a) || RectContains(rect, b))
            return true;

        var topLeft = new SKPoint(rect.Left, rect.Top);
        var topRight = new SKPoint(rect.Right, rect.Top);
        var bottomRight = new SKPoint(rect.Right, rect.Bottom);
        var bottomLeft = new SKPoint(rect.Left, rect.Bottom);
        return SegmentsIntersect(a, b, topLeft, topRight) ||
               SegmentsIntersect(a, b, topRight, bottomRight) ||
               SegmentsIntersect(a, b, bottomRight, bottomLeft) ||
               SegmentsIntersect(a, b, bottomLeft, topLeft);
    }

    private static bool SegmentsIntersect(SKPoint a, SKPoint b, SKPoint c, SKPoint d)
    {
        float d1 = Cross(a, b, c);
        float d2 = Cross(a, b, d);
        float d3 = Cross(c, d, a);
        float d4 = Cross(c, d, b);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
            ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
        {
            return true;
        }

        const float eps = 0.0001f;
        return Math.Abs(d1) <= eps && PointOnSegment(c, a, b) ||
               Math.Abs(d2) <= eps && PointOnSegment(d, a, b) ||
               Math.Abs(d3) <= eps && PointOnSegment(a, c, d) ||
               Math.Abs(d4) <= eps && PointOnSegment(b, c, d);
    }

    private static float Cross(SKPoint a, SKPoint b, SKPoint c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool PointOnSegment(SKPoint p, SKPoint a, SKPoint b) =>
        p.X >= Math.Min(a.X, b.X) - 0.0001f &&
        p.X <= Math.Max(a.X, b.X) + 0.0001f &&
        p.Y >= Math.Min(a.Y, b.Y) - 0.0001f &&
        p.Y <= Math.Max(a.Y, b.Y) + 0.0001f;

    private static bool PointInPolygon(SKPoint point, IReadOnlyList<SKPoint> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            SKPoint pi = polygon[i];
            SKPoint pj = polygon[j];
            float denom = Math.Abs(pj.Y - pi.Y) < 0.000001f ? 0.000001f : pj.Y - pi.Y;
            bool crosses = (pi.Y > point.Y) != (pj.Y > point.Y) &&
                           point.X < (pj.X - pi.X) * (point.Y - pi.Y) / denom + pi.X;
            if (crosses)
                inside = !inside;
        }

        return inside;
    }

    private static bool IsMeasurementVisible(Measurement measurement, SKRect visiblePdf)
    {
        if (measurement.Points.Count == 0)
            return false;

        SKRect bounds = MeasurementBounds(measurement);
        return bounds.Left <= visiblePdf.Right &&
               bounds.Right >= visiblePdf.Left &&
               bounds.Top <= visiblePdf.Bottom &&
               bounds.Bottom >= visiblePdf.Top;
    }

    private static bool MeasurementIntersectsRect(Measurement measurement, SKRect rect)
    {
        if (measurement.Points.Count == 0)
            return false;

        SKRect bounds = RawMeasurementBounds(measurement);
        if (!RectsIntersect(bounds, rect))
            return false;

        if (measurement.Points.Any(point => RectContains(rect, point)))
            return true;

        if (measurement.MType == "point")
            return false;

        for (int i = 1; i < measurement.Points.Count; i++)
        {
            if (SegmentIntersectsRect(measurement.Points[i - 1], measurement.Points[i], rect))
                return true;
        }

        if (measurement.MType == "area" && measurement.Points.Count > 2)
        {
            if (SegmentIntersectsRect(measurement.Points[^1], measurement.Points[0], rect))
                return true;

            var center = new SKPoint((rect.Left + rect.Right) / 2f, (rect.Top + rect.Bottom) / 2f);
            return PointInPolygon(center, measurement.Points);
        }

        return false;
    }

    private static SKRect RawMeasurementBounds(Measurement measurement) =>
        PointsBounds(measurement.Points);

    private static SKRect MeasurementBounds(Measurement measurement)
    {
        float left = float.PositiveInfinity;
        float top = float.PositiveInfinity;
        float right = float.NegativeInfinity;
        float bottom = float.NegativeInfinity;

        foreach (var point in measurement.Points)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }

        var bounds = new SKRect(left, top, right, bottom);
        bounds.Inflate(96f, 96f);
        return bounds;
    }

    private static SKPoint Centroid(List<SKPoint> pts)
        => new(pts.Average(p => p.X), pts.Average(p => p.Y));

}
