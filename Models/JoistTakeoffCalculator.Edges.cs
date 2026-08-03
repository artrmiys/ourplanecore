using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore;

public static partial class JoistTakeoffCalculator
{
    private const double EdgeParallelToleranceDegrees = 2.0;

    private static IReadOnlyList<double> JoistOffsets(
        IReadOnlyList<SKPoint> polygon,
        IReadOnlyList<IReadOnlyList<SKPoint>> contours,
        double min,
        double max,
        double spacingPt,
        double dirX,
        double dirY,
        double normalX,
        double normalY,
        bool startEdgeEnabled,
        bool endEdgeEnabled)
    {
        var offsets = new List<double>();
        double startOffset = startEdgeEnabled
            ? ResolveBoundaryJoistOffset(
                polygon, contours, min, max, spacingPt,
                dirX, dirY, normalX, normalY, startBoundary: true)
            : min;
        int maxLines = Math.Min(8000, (int)Math.Ceiling((max - min) / spacingPt) + 2);
        for (int lineIndex = 0; lineIndex < maxLines; lineIndex++)
        {
            double offset = min + lineIndex * spacingPt;
            if (offset >= max - ProjectionEpsilon)
                break;
            if (lineIndex == 0 && !startEdgeEnabled)
                continue;
            AddUniqueOffset(offsets, lineIndex == 0 ? startOffset : offset);
        }

        if (endEdgeEnabled)
        {
            double endOffset = ResolveBoundaryJoistOffset(
                polygon, contours, min, max, spacingPt,
                dirX, dirY, normalX, normalY, startBoundary: false);
            AddUniqueOffset(offsets, endOffset);
        }

        offsets.Sort();
        return offsets;
    }

    private static double ResolveBoundaryJoistOffset(
        IReadOnlyList<SKPoint> polygon,
        IReadOnlyList<IReadOnlyList<SKPoint>> contours,
        double min,
        double max,
        double spacingPt,
        double dirX,
        double dirY,
        double normalX,
        double normalY,
        bool startBoundary)
    {
        double boundary = startBoundary ? min : max;
        if (JoistSpanLength(contours, boundary, dirX, dirY, normalX, normalY) > ProjectionEpsilon)
            return boundary;

        double span = max - min;
        double maxBand = Math.Max(
            ProjectionEpsilon * 4,
            Math.Min(spacingPt * 0.5, span * 0.2));
        double parallelTolerance = Math.Sin(EdgeParallelToleranceDegrees * Math.PI / 180.0);
        double bestOffset = double.NaN;
        double bestLength = 0;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < polygon.Count; i++)
        {
            SKPoint first = polygon[i];
            SKPoint second = polygon[(i + 1) % polygon.Count];
            double edgeLength = MeasurementGeometry.Distance(first, second);
            if (edgeLength <= ProjectionEpsilon)
                continue;

            double firstProjection = Dot(first, normalX, normalY);
            double secondProjection = Dot(second, normalX, normalY);
            if (Math.Abs(firstProjection - secondProjection) / edgeLength > parallelTolerance)
                continue;

            double distance = startBoundary
                ? Math.Max(firstProjection, secondProjection) - min
                : max - Math.Min(firstProjection, secondProjection);
            if (distance < -ProjectionEpsilon || distance > maxBand)
                continue;

            double candidate = (firstProjection + secondProjection) / 2.0;
            double candidateLength = JoistSpanLength(
                contours, candidate, dirX, dirY, normalX, normalY);
            bool betterLength = candidateLength > bestLength + ProjectionEpsilon;
            bool sameLengthNearer = Math.Abs(candidateLength - bestLength) <= ProjectionEpsilon &&
                                    distance < bestDistance;
            if (betterLength || sameLengthNearer)
            {
                bestOffset = candidate;
                bestLength = candidateLength;
                bestDistance = distance;
            }
        }

        return double.IsNaN(bestOffset)
            ? FindFirstUsableBoundaryOffset(
                contours, min, max, spacingPt,
                dirX, dirY, normalX, normalY, startBoundary)
            : bestOffset;
    }

    private static double FindFirstUsableBoundaryOffset(
        IReadOnlyList<IReadOnlyList<SKPoint>> contours,
        double min,
        double max,
        double spacingPt,
        double dirX,
        double dirY,
        double normalX,
        double normalY,
        bool startBoundary)
    {
        double boundary = startBoundary ? min : max;
        double maxInset = Math.Max(
            ProjectionEpsilon * 4,
            Math.Min(spacingPt * 0.25, (max - min) * 0.1));
        double targetLength = spacingPt * 0.5;
        double bestOffset = boundary;
        double bestLength = 0;

        const int steps = 24;
        for (int step = 1; step <= steps; step++)
        {
            double inset = maxInset * step / steps;
            double candidate = startBoundary ? min + inset : max - inset;
            double length = JoistSpanLength(contours, candidate, dirX, dirY, normalX, normalY);
            if (length > bestLength)
            {
                bestOffset = candidate;
                bestLength = length;
            }
            if (length >= targetLength)
                return candidate;
        }

        return bestLength > ProjectionEpsilon ? bestOffset : boundary;
    }

    private static double JoistSpanLength(
        IReadOnlyList<IReadOnlyList<SKPoint>> contours,
        double offset,
        double dirX,
        double dirY,
        double normalX,
        double normalY)
    {
        List<LineIntersection> intersections = LineAreaIntersections(
            contours, offset, dirX, dirY, normalX, normalY);
        double length = 0;
        for (int i = 0; i + 1 < intersections.Count; i += 2)
            length += Math.Max(0, intersections[i + 1].T - intersections[i].T);
        return length;
    }

    private static void AddUniqueOffset(List<double> offsets, double offset)
    {
        if (offsets.All(existing => Math.Abs(existing - offset) > ProjectionEpsilon))
            offsets.Add(offset);
    }
}
