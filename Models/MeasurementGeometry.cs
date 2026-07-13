using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore;

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

    // Parallel copy of an open polyline at the given signed distance.
    // Positive distance offsets to the left of the drawing direction
    // (screen-up when drawing left-to-right; page coords are y-down).
    // Interior vertices use mitered joins, clamped to 4x the distance so
    // sharp angles do not shoot spikes; clamped joins fall back to a bevel.
    public static List<SKPoint> OffsetPolyline(IReadOnlyList<SKPoint> points, float distance)
    {
        var path = new List<SKPoint>(points.Count);
        foreach (SKPoint point in points)
        {
            if (path.Count == 0 || DistanceSquared(path[^1], point) > ViewportConstants.GeometryEpsilon)
                path.Add(point);
        }

        if (path.Count < 2 || Math.Abs(distance) < 0.0001f)
            return path;

        var normals = new SKPoint[path.Count - 1];
        for (int i = 0; i < path.Count - 1; i++)
        {
            float dx = path[i + 1].X - path[i].X;
            float dy = path[i + 1].Y - path[i].Y;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            normals[i] = new SKPoint(dy / len, -dx / len);
        }

        float miterLimit = Math.Abs(distance) * 4f;
        var result = new List<SKPoint>(path.Count)
        {
            new(path[0].X + normals[0].X * distance, path[0].Y + normals[0].Y * distance),
        };
        for (int v = 1; v < path.Count - 1; v++)
        {
            SKPoint n1 = normals[v - 1];
            SKPoint n2 = normals[v];
            float mx = n1.X + n2.X;
            float my = n1.Y + n2.Y;
            float mLen = MathF.Sqrt(mx * mx + my * my);
            bool beveled = true;
            if (mLen > 0.001f)
            {
                mx /= mLen;
                my /= mLen;
                float cos = mx * n1.X + my * n1.Y;
                if (cos > 0.001f)
                {
                    float miter = distance / cos;
                    if (Math.Abs(miter) <= miterLimit)
                    {
                        result.Add(new SKPoint(path[v].X + mx * miter, path[v].Y + my * miter));
                        beveled = false;
                    }
                }
            }

            if (beveled)
            {
                result.Add(new SKPoint(path[v].X + n1.X * distance, path[v].Y + n1.Y * distance));
                result.Add(new SKPoint(path[v].X + n2.X * distance, path[v].Y + n2.Y * distance));
            }
        }
        result.Add(new SKPoint(
            path[^1].X + normals[^1].X * distance,
            path[^1].Y + normals[^1].Y * distance));
        return result;
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
