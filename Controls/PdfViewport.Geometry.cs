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
        => MeasurementGeometry.DistanceSquared(a, b);

    private static double DistanceSquared(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
        => MeasurementGeometry.DistanceToSegment(p, a, b);

    private static SKRect NormalizeRect(SKPoint a, SKPoint b) =>
        MeasurementGeometry.NormalizeRect(a, b);

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

        const float eps = ViewportConstants.GeometryEpsilon;
        return Math.Abs(d1) <= eps && PointOnSegment(c, a, b) ||
               Math.Abs(d2) <= eps && PointOnSegment(d, a, b) ||
               Math.Abs(d3) <= eps && PointOnSegment(a, c, d) ||
               Math.Abs(d4) <= eps && PointOnSegment(b, c, d);
    }

    private static bool TrySegmentIntersectionPoint(SKPoint a, SKPoint b, SKPoint c, SKPoint d, out SKPoint point)
    {
        point = default;
        float rx = b.X - a.X;
        float ry = b.Y - a.Y;
        float sx = d.X - c.X;
        float sy = d.Y - c.Y;
        float denom = rx * sy - ry * sx;
        if (Math.Abs(denom) <= ViewportConstants.GeometryEpsilon)
            return false;

        float qpx = c.X - a.X;
        float qpy = c.Y - a.Y;
        float t = (qpx * sy - qpy * sx) / denom;
        float u = (qpx * ry - qpy * rx) / denom;
        const float eps = ViewportConstants.GeometryEpsilon;
        if (t < -eps || t > 1f + eps || u < -eps || u > 1f + eps)
            return false;

        point = new SKPoint(a.X + t * rx, a.Y + t * ry);
        return true;
    }

    private static float Cross(SKPoint a, SKPoint b, SKPoint c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool PointOnSegment(SKPoint p, SKPoint a, SKPoint b) =>
        p.X >= Math.Min(a.X, b.X) - ViewportConstants.GeometryEpsilon &&
        p.X <= Math.Max(a.X, b.X) + ViewportConstants.GeometryEpsilon &&
        p.Y >= Math.Min(a.Y, b.Y) - ViewportConstants.GeometryEpsilon &&
        p.Y <= Math.Max(a.Y, b.Y) + ViewportConstants.GeometryEpsilon;

    private static bool PointInPolygon(SKPoint point, IReadOnlyList<SKPoint> polygon)
        => MeasurementGeometry.PointInPolygon(point, polygon);

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

            foreach (var hole in measurement.Holes)
            {
                if (hole.Count < 3)
                    continue;

                for (int i = 1; i < hole.Count; i++)
                {
                    if (SegmentIntersectsRect(hole[i - 1], hole[i], rect))
                        return true;
                }

                if (SegmentIntersectsRect(hole[^1], hole[0], rect))
                    return true;
            }

            var center = new SKPoint((rect.Left + rect.Right) / 2f, (rect.Top + rect.Bottom) / 2f);
            return PointInMeasurementFill(measurement, center);
        }

        return false;
    }

    private static bool AnnotationIntersectsRect(PageAnnotation annotation, SKRect rect)
    {
        IReadOnlyList<SKPoint> points = AnnotationTransformPoints(annotation);
        if (points.Count == 0)
            return false;

        SKRect bounds = PointsBounds(points);
        if (!RectsIntersect(bounds, rect))
            return false;

        if (points.Any(point => RectContains(rect, point)))
            return true;

        for (int i = 1; i < points.Count; i++)
            if (SegmentIntersectsRect(points[i - 1], points[i], rect))
                return true;

        if (points.Count > 2)
        {
            if (SegmentIntersectsRect(points[^1], points[0], rect))
                return true;

            var center = new SKPoint((rect.Left + rect.Right) / 2f, (rect.Top + rect.Bottom) / 2f);
            return PointInPolygon(center, points);
        }

        return false;
    }

    private static SKRect RawMeasurementBounds(Measurement measurement) =>
        PointsBounds(MeasurementGeometryPoints(measurement).ToList());

    private static SKRect MeasurementBounds(Measurement measurement)
    {
        float left = float.PositiveInfinity;
        float top = float.PositiveInfinity;
        float right = float.NegativeInfinity;
        float bottom = float.NegativeInfinity;

        foreach (var point in MeasurementGeometryPoints(measurement))
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
        => MeasurementGeometry.Centroid(pts);

    private static bool PointInMeasurementFill(Measurement measurement, SKPoint point)
    {
        if (measurement.MType != "area" || measurement.Points.Count < 3)
            return false;

        if (!PointInPolygon(point, measurement.Points))
            return false;

        return !measurement.Holes.Any(hole => hole.Count >= 3 && PointInPolygon(point, hole));
    }

    private static IEnumerable<SKPoint> MeasurementGeometryPoints(Measurement measurement)
    {
        foreach (SKPoint point in measurement.Points)
            yield return point;
        foreach (var hole in measurement.Holes)
            foreach (SKPoint point in hole)
                yield return point;
    }

}
