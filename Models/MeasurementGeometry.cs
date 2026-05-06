using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlaneCore;

public static class MeasurementGeometry
{
    public static float Distance(SKPoint a, SKPoint b) =>
        MathF.Sqrt(DistanceSquared(a, b));

    public static float DistanceSquared(SKPoint a, SKPoint b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    public static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
    {
        float vx = b.X - a.X;
        float vy = b.Y - a.Y;
        float lenSq = vx * vx + vy * vy;
        if (lenSq <= ViewportConstants.GeometryEpsilon)
            return Distance(p, a);

        float t = ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / lenSq;
        t = Math.Clamp(t, 0f, 1f);
        var projection = new SKPoint(a.X + t * vx, a.Y + t * vy);
        return Distance(p, projection);
    }

    public static SKRect NormalizeRect(SKPoint a, SKPoint b) =>
        new(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Max(a.X, b.X),
            Math.Max(a.Y, b.Y));

    public static SKPoint Centroid(IReadOnlyList<SKPoint> points)
    {
        if (points.Count == 0)
            return default;

        return new SKPoint(
            points.Average(point => point.X),
            points.Average(point => point.Y));
    }

    public static bool PointInPolygon(SKPoint point, IReadOnlyList<SKPoint> polygon)
    {
        bool inside = false;
        const float eps = 0.000001f;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            SKPoint pi = polygon[i];
            SKPoint pj = polygon[j];
            float denom = pj.Y - pi.Y;
            if (Math.Abs(denom) < eps)
                continue;

            bool crosses = pi.Y > point.Y != pj.Y > point.Y &&
                           point.X < (pj.X - pi.X) * (point.Y - pi.Y) / denom + pi.X;
            if (crosses)
                inside = !inside;
        }

        return inside;
    }
}
