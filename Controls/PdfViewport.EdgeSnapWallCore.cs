using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private static bool TryCreatePdfSnapWallCoreComponent(
        IReadOnlyList<PdfSnapBoundaryTraceSegment> component,
        PdfGeometrySnapSegment selected,
        float bridgeTolerancePt,
        out List<PdfSnapBoundaryTraceSegment> wallCore)
    {
        wallCore = [];
        if (component.Count < 24)
            return false;

        float bandSize = Math.Clamp(bridgeTolerancePt * 0.08f, 2f, 4f);
        float axisTolerance = Math.Max(1.5f, bandSize * 0.75f);
        float minLength = Math.Max(4f, bandSize * 2f);
        var horizontalBands = new Dictionary<int, PdfSnapWallCoreBand>();
        var verticalBands = new Dictionary<int, PdfSnapWallCoreBand>();
        int realAxisSegments = 0;

        foreach (PdfSnapBoundaryTraceSegment segment in component)
        {
            if (segment.Bridge || segment.SegmentIndex < 0)
                continue;

            if (!TryClassifyPdfSnapAxisSegment(segment, axisTolerance, minLength, out bool horizontal, out float coordinate, out float a0, out float a1))
                continue;

            realAxisSegments++;
            Dictionary<int, PdfSnapWallCoreBand> bands = horizontal ? horizontalBands : verticalBands;
            int key = PdfSnapWallCoreBandKey(coordinate, bandSize);
            if (!bands.TryGetValue(key, out PdfSnapWallCoreBand band))
                band = new PdfSnapWallCoreBand(coordinate, coordinate, Math.Min(a0, a1), Math.Max(a0, a1), 0f, 0);

            band = band.Add(coordinate, Math.Min(a0, a1), Math.Max(a0, a1), Math.Abs(a1 - a0));
            bands[key] = band;
        }

        if (realAxisSegments < 24)
            return false;

        int minBandCount = Math.Clamp((int)MathF.Round(realAxisSegments / 450f), 5, 10);
        var denseHorizontal = horizontalBands
            .Where(pair => pair.Value.Count >= minBandCount)
            .Select(pair => pair.Value)
            .ToList();
        var denseVertical = verticalBands
            .Where(pair => pair.Value.Count >= minBandCount)
            .Select(pair => pair.Value)
            .ToList();

        if (denseHorizontal.Count < 2 || denseVertical.Count < 2)
            return false;

        float coreLeft = Math.Min(denseHorizontal.Min(band => band.MinAlong), denseVertical.Min(band => band.MinCoordinate));
        float coreRight = Math.Max(denseHorizontal.Max(band => band.MaxAlong), denseVertical.Max(band => band.MaxCoordinate));
        float coreTop = denseHorizontal.Min(band => band.MinCoordinate);
        float coreBottom = denseHorizontal.Max(band => band.MaxCoordinate);
        if (coreRight - coreLeft < minLength * 8f || coreBottom - coreTop < minLength * 8f)
            return false;

        SKRect coreBounds = new(coreLeft, coreTop, coreRight, coreBottom);
        if (!PdfSnapWallCoreContainsSelected(coreBounds, selected, bridgeTolerancePt))
            return false;

        var denseHorizontalKeys = horizontalBands
            .Where(pair => pair.Value.Count >= minBandCount)
            .Select(pair => pair.Key)
            .ToHashSet();
        var denseVerticalKeys = verticalBands
            .Where(pair => pair.Value.Count >= minBandCount)
            .Select(pair => pair.Key)
            .ToHashSet();

        foreach (PdfSnapBoundaryTraceSegment segment in component)
        {
            if (!TryClassifyPdfSnapAxisSegment(segment, axisTolerance, minLength, out bool horizontal, out float coordinate, out float a0, out float a1))
                continue;

            int key = PdfSnapWallCoreBandKey(coordinate, bandSize);
            if (horizontal)
            {
                if (!denseHorizontalKeys.Contains(key))
                    continue;

                float x0 = Math.Clamp(segment.Start.X, coreLeft, coreRight);
                float x1 = Math.Clamp(segment.End.X, coreLeft, coreRight);
                if (Math.Abs(x1 - x0) < minLength)
                    continue;

                wallCore.Add(new PdfSnapBoundaryTraceSegment(
                    new SKPoint(x0, coordinate),
                    new SKPoint(x1, coordinate),
                    segment.SegmentIndex,
                    segment.Bridge));
                continue;
            }

            if (!denseVerticalKeys.Contains(key))
                continue;

            float y0 = Math.Clamp(segment.Start.Y, coreTop, coreBottom);
            float y1 = Math.Clamp(segment.End.Y, coreTop, coreBottom);
            if (Math.Abs(y1 - y0) < minLength)
                continue;

            wallCore.Add(new PdfSnapBoundaryTraceSegment(
                new SKPoint(coordinate, y0),
                new SKPoint(coordinate, y1),
                segment.SegmentIndex,
                segment.Bridge));
        }

        if (wallCore.Count < Math.Max(12, component.Count * 0.08))
        {
            wallCore = [];
            return false;
        }

        return true;
    }

    private static bool TryClassifyPdfSnapAxisSegment(
        PdfSnapBoundaryTraceSegment segment,
        float axisTolerance,
        float minLength,
        out bool horizontal,
        out float coordinate,
        out float a0,
        out float a1)
    {
        float dx = Math.Abs(segment.End.X - segment.Start.X);
        float dy = Math.Abs(segment.End.Y - segment.Start.Y);
        if (dy <= axisTolerance && dx >= minLength)
        {
            horizontal = true;
            coordinate = (segment.Start.Y + segment.End.Y) * 0.5f;
            a0 = segment.Start.X;
            a1 = segment.End.X;
            return true;
        }

        if (dx <= axisTolerance && dy >= minLength)
        {
            horizontal = false;
            coordinate = (segment.Start.X + segment.End.X) * 0.5f;
            a0 = segment.Start.Y;
            a1 = segment.End.Y;
            return true;
        }

        horizontal = false;
        coordinate = 0f;
        a0 = 0f;
        a1 = 0f;
        return false;
    }

    private static bool PdfSnapWallCoreContainsSelected(SKRect coreBounds, PdfGeometrySnapSegment selected, float bridgeTolerancePt)
    {
        float tolerance = Math.Clamp(bridgeTolerancePt * 0.55f, 8f, 36f);
        SKRect inflated = coreBounds;
        inflated.Inflate(tolerance, tolerance);
        return inflated.Contains((selected.Start.X + selected.End.X) * 0.5f, (selected.Start.Y + selected.End.Y) * 0.5f);
    }

    private static bool PdfSnapBoundaryLoopStaysNearSelected(
        IReadOnlyList<SKPoint> contour,
        PdfGeometrySnapSegment selected,
        float bridgeTolerancePt,
        float cell)
    {
        if (contour.Count < 3)
            return false;

        SKPoint midpoint = new(
            (selected.Start.X + selected.End.X) * 0.5f,
            (selected.Start.Y + selected.End.Y) * 0.5f);
        float distance = PdfSnapBoundaryDistanceToContour(midpoint, contour);
        float limit = Math.Clamp(Math.Max(bridgeTolerancePt * 0.65f, cell * 4f), 12f, 72f);
        return distance <= limit;
    }

    private static float PdfSnapBoundaryDistanceToContour(SKPoint point, IReadOnlyList<SKPoint> contour)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < contour.Count; i++)
        {
            float distance = DistanceToSegment(point, contour[i], contour[(i + 1) % contour.Count]);
            if (distance < best)
                best = distance;
        }

        return best;
    }

    private static int PdfSnapWallCoreBandKey(float coordinate, float bandSize) =>
        (int)MathF.Round(coordinate / Math.Max(0.001f, bandSize));

    private readonly record struct PdfSnapWallCoreBand(
        float MinCoordinate,
        float MaxCoordinate,
        float MinAlong,
        float MaxAlong,
        float Length,
        int Count)
    {
        public PdfSnapWallCoreBand Add(float coordinate, float minAlong, float maxAlong, float length) =>
            new(
                Math.Min(MinCoordinate, coordinate),
                Math.Max(MaxCoordinate, coordinate),
                Math.Min(MinAlong, minAlong),
                Math.Max(MaxAlong, maxAlong),
                Length + length,
                Count + 1);
    }
}
