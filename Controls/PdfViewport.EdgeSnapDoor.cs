using System;
using System.Collections.Generic;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private static bool PdfSnapGeometrySegmentLooksLikeInteriorDoorCandidate(
        IReadOnlyList<PdfGeometrySnapSegment> segments,
        int selectedIndex,
        float bridgeTolerancePt)
    {
        if (selectedIndex < 0 || selectedIndex >= segments.Count)
            return false;

        PdfGeometrySnapSegment selected = segments[selectedIndex];
        var trace = new PdfSnapBoundaryTraceSegment(selected.Start, selected.End, selectedIndex, false);
        if (!TryClassifyPdfSnapAxisSegment(trace, 2.5f, 4f, out bool horizontal, out _, out _, out _))
            return PdfSnapGeometrySegmentLooksLikeDoorSwingCandidate(segments, selectedIndex, bridgeTolerancePt);

        float length = MeasurementGeometry.Distance(selected.Start, selected.End);
        float doorCandidateMax = PdfSnapDoorAxisCandidateMax(bridgeTolerancePt);
        if (length > doorCandidateMax)
            return false;

        bool hasSwing = PdfSnapGeometryAxisSegmentHasDoorArc(segments, selectedIndex, bridgeTolerancePt);
        if (!hasSwing)
            return false;

        bool hasPair = PdfSnapGeometryAxisSegmentHasDoorPair(segments, selectedIndex, horizontal, bridgeTolerancePt);
        float singleLeafMax = Math.Clamp(bridgeTolerancePt * 1.45f, 28f, 112f);
        return hasPair || length <= singleLeafMax;
    }

    private static bool PdfSnapDoorSelectedBoundaryLooksLikeExterior(
        IReadOnlyList<SKPoint> boundary,
        IReadOnlyList<PdfGeometrySnapSegment> segments,
        int selectedIndex,
        float bridgeTolerancePt)
    {
        if (boundary.Count < 3)
            return false;

        float left = boundary.Min(point => point.X);
        float top = boundary.Min(point => point.Y);
        float right = boundary.Max(point => point.X);
        float bottom = boundary.Max(point => point.Y);
        float width = right - left;
        float height = bottom - top;
        if (width <= 0 || height <= 0)
            return false;

        double area = Math.Abs(PdfSnapBoundarySignedArea(boundary));
        float minWidth = Math.Clamp(bridgeTolerancePt * 2.30f, 56f, 180f);
        float minHeight = Math.Clamp(bridgeTolerancePt * 1.60f, 44f, 120f);
        double minArea = Math.Max(3_000.0, bridgeTolerancePt * bridgeTolerancePt * 3.20);
        if (width < minWidth || height < minHeight || area < minArea)
            return false;

        SKRect supportBounds = new(left, top, right, bottom);
        supportBounds.Inflate(Math.Clamp(bridgeTolerancePt * 0.18f, 4f, 18f), Math.Clamp(bridgeTolerancePt * 0.18f, 4f, 18f));
        float minWallLength = Math.Clamp(bridgeTolerancePt * 1.15f, 30f, 96f);
        float supportedLength = 0f;
        int supportedCount = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            if (i == selectedIndex)
                continue;

            PdfGeometrySnapSegment segment = segments[i];
            float length = MeasurementGeometry.Distance(segment.Start, segment.End);
            if (length < minWallLength)
                continue;

            var trace = new PdfSnapBoundaryTraceSegment(segment.Start, segment.End, i, false);
            if (!TryClassifyPdfSnapAxisSegment(trace, 2.5f, minWallLength, out _, out _, out _, out _))
                continue;

            SKPoint midpoint = new(
                (segment.Start.X + segment.End.X) * 0.5f,
                (segment.Start.Y + segment.End.Y) * 0.5f);
            if (!supportBounds.Contains(midpoint.X, midpoint.Y))
                continue;

            supportedLength += length;
            supportedCount++;
        }

        float minSupportedLength = Math.Max(minWallLength * 2f, (minWidth + minHeight) * 0.75f);
        return supportedCount >= 2 &&
            supportedLength >= minSupportedLength &&
            PdfSnapDoorBoundaryHasFootprintSideSupport(new SKRect(left, top, right, bottom), segments, selectedIndex, bridgeTolerancePt);
    }

    private static bool PdfSnapDoorBoundaryHasFootprintSideSupport(
        SKRect bounds,
        IReadOnlyList<PdfGeometrySnapSegment> segments,
        int selectedIndex,
        float bridgeTolerancePt)
    {
        float sideTolerance = Math.Clamp(bridgeTolerancePt * 0.70f, 8f, 40f);
        float minHorizontalOverlap = Math.Max(bridgeTolerancePt * 1.25f, bounds.Width * 0.30f);
        float minVerticalOverlap = Math.Max(bridgeTolerancePt * 1.25f, bounds.Height * 0.30f);
        bool left = false;
        bool right = false;
        bool top = false;
        bool bottom = false;

        for (int i = 0; i < segments.Count; i++)
        {
            if (i == selectedIndex)
                continue;

            PdfGeometrySnapSegment segment = segments[i];
            var trace = new PdfSnapBoundaryTraceSegment(segment.Start, segment.End, i, false);
            if (!TryClassifyPdfSnapAxisSegment(trace, 2.5f, 4f, out bool horizontal, out float coordinate, out float a0, out float a1))
                continue;

            float minAlong = Math.Min(a0, a1);
            float maxAlong = Math.Max(a0, a1);
            if (horizontal)
            {
                float overlap = Math.Min(maxAlong, bounds.Right) - Math.Max(minAlong, bounds.Left);
                if (overlap < minHorizontalOverlap)
                    continue;
                if (Math.Abs(coordinate - bounds.Top) <= sideTolerance)
                    top = true;
                if (Math.Abs(coordinate - bounds.Bottom) <= sideTolerance)
                    bottom = true;
                continue;
            }

            float verticalOverlap = Math.Min(maxAlong, bounds.Bottom) - Math.Max(minAlong, bounds.Top);
            if (verticalOverlap < minVerticalOverlap)
                continue;
            if (Math.Abs(coordinate - bounds.Left) <= sideTolerance)
                left = true;
            if (Math.Abs(coordinate - bounds.Right) <= sideTolerance)
                right = true;
        }

        return left && right && top && bottom;
    }

    private static bool PdfSnapBoundaryAxisSegmentHasDoorPair(
        IReadOnlyList<PdfSnapBoundaryTraceSegment> component,
        PdfSnapBoundaryTraceSegment segment,
        bool horizontal,
        float bridgeTolerancePt)
    {
        PdfSnapAxisSpan current = PdfSnapAxisSpanFor(segment, horizontal);
        float maxSeparation = Math.Clamp(bridgeTolerancePt * 0.24f, 3.5f, 10f);
        float minOverlap = Math.Clamp(bridgeTolerancePt * 0.20f, 3f, 18f);

        foreach (PdfSnapBoundaryTraceSegment other in component)
        {
            if (other.SegmentIndex == segment.SegmentIndex && segment.SegmentIndex >= 0)
                continue;
            if (other.Bridge)
                continue;
            if (!TryClassifyPdfSnapAxisSegment(other, 2.5f, 4f, out bool otherHorizontal, out _, out _, out _) ||
                otherHorizontal != horizontal)
            {
                continue;
            }

            PdfSnapAxisSpan pair = PdfSnapAxisSpanFor(other, horizontal);
            if (PdfSnapAxisSpansLookLikeDoorPair(current, pair, maxSeparation, minOverlap))
                return true;
        }

        return false;
    }

    private static bool PdfSnapGeometryAxisSegmentHasDoorPair(
        IReadOnlyList<PdfGeometrySnapSegment> segments,
        int selectedIndex,
        bool horizontal,
        float bridgeTolerancePt)
    {
        PdfGeometrySnapSegment selected = segments[selectedIndex];
        PdfSnapAxisSpan current = PdfSnapAxisSpanFor(selected.Start, selected.End, horizontal);
        float maxSeparation = Math.Clamp(bridgeTolerancePt * 0.24f, 3.5f, 10f);
        float minOverlap = Math.Clamp(bridgeTolerancePt * 0.20f, 3f, 18f);

        for (int i = 0; i < segments.Count; i++)
        {
            if (i == selectedIndex)
                continue;

            var trace = new PdfSnapBoundaryTraceSegment(segments[i].Start, segments[i].End, i, false);
            if (!TryClassifyPdfSnapAxisSegment(trace, 2.5f, 4f, out bool otherHorizontal, out _, out _, out _) ||
                otherHorizontal != horizontal)
            {
                continue;
            }

            PdfSnapAxisSpan pair = PdfSnapAxisSpanFor(segments[i].Start, segments[i].End, horizontal);
            if (PdfSnapAxisSpansLookLikeDoorPair(current, pair, maxSeparation, minOverlap))
                return true;
        }

        return false;
    }

    private static bool PdfSnapAxisSpansLookLikeDoorPair(
        PdfSnapAxisSpan current,
        PdfSnapAxisSpan pair,
        float maxSeparation,
        float minOverlap)
    {
        float separation = Math.Abs(current.Coordinate - pair.Coordinate);
        if (separation < 0.35f || separation > maxSeparation)
            return false;

        float overlap = Math.Min(current.MaxAlong, pair.MaxAlong) - Math.Max(current.MinAlong, pair.MinAlong);
        if (overlap < minOverlap)
            return false;

        float shortest = Math.Min(current.Length, pair.Length);
        return overlap >= shortest * 0.55f;
    }

    private static bool PdfSnapBoundaryAxisSegmentHasDoorArc(
        IReadOnlyList<PdfSnapBoundaryTraceSegment> component,
        PdfSnapBoundaryTraceSegment segment,
        bool horizontal,
        float bridgeTolerancePt)
    {
        float near = Math.Clamp(bridgeTolerancePt * 0.45f, 6f, 24f);
        float maxArcSegment = Math.Clamp(bridgeTolerancePt * 0.85f, 8f, 60f);
        foreach (PdfSnapBoundaryTraceSegment other in component)
        {
            if (other.Bridge)
                continue;
            if (other.SegmentIndex == segment.SegmentIndex && segment.SegmentIndex >= 0)
                continue;
            if (!PdfSnapSegmentLooksLikeDoorSwingPiece(other.Start, other.End, maxArcSegment))
                continue;
            if (PdfSnapDoorArcTouchesAxisSegment(segment.Start, segment.End, other.Start, other.End, near))
                return true;
        }

        return false;
    }

    private static bool PdfSnapGeometryAxisSegmentHasDoorArc(
        IReadOnlyList<PdfGeometrySnapSegment> segments,
        int selectedIndex,
        float bridgeTolerancePt)
    {
        PdfGeometrySnapSegment selected = segments[selectedIndex];
        float near = Math.Clamp(bridgeTolerancePt * 0.45f, 6f, 24f);
        float maxArcSegment = Math.Clamp(bridgeTolerancePt * 0.85f, 8f, 60f);
        for (int i = 0; i < segments.Count; i++)
        {
            if (i == selectedIndex)
                continue;

            if (!PdfSnapSegmentLooksLikeDoorSwingPiece(segments[i].Start, segments[i].End, maxArcSegment))
                continue;
            if (PdfSnapDoorArcTouchesAxisSegment(selected.Start, selected.End, segments[i].Start, segments[i].End, near))
                return true;
        }

        return false;
    }

    private static bool PdfSnapGeometrySegmentLooksLikeDoorSwingCandidate(
        IReadOnlyList<PdfGeometrySnapSegment> segments,
        int selectedIndex,
        float bridgeTolerancePt)
    {
        PdfGeometrySnapSegment selected = segments[selectedIndex];
        float maxArcSegment = Math.Clamp(bridgeTolerancePt * 0.85f, 8f, 60f);
        if (!PdfSnapSegmentLooksLikeDoorSwingPiece(selected.Start, selected.End, maxArcSegment))
            return false;

        float near = Math.Clamp(bridgeTolerancePt * 0.55f, 8f, 28f);
        float doorCandidateMax = PdfSnapDoorAxisCandidateMax(bridgeTolerancePt);
        for (int i = 0; i < segments.Count; i++)
        {
            if (i == selectedIndex)
                continue;

            var trace = new PdfSnapBoundaryTraceSegment(segments[i].Start, segments[i].End, i, false);
            if (!TryClassifyPdfSnapAxisSegment(trace, 2.5f, 4f, out _, out _, out _, out _))
                continue;
            if (MeasurementGeometry.Distance(segments[i].Start, segments[i].End) > doorCandidateMax)
                continue;
            if (PdfSnapDoorArcTouchesAxisSegment(segments[i].Start, segments[i].End, selected.Start, selected.End, near))
                return true;
        }

        return false;
    }

    private static bool PdfSnapSegmentLooksLikeDoorSwingPiece(SKPoint start, SKPoint end, float maxLength)
    {
        float dx = Math.Abs(end.X - start.X);
        float dy = Math.Abs(end.Y - start.Y);
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 1.8f || length > maxLength)
            return false;

        float minor = Math.Min(dx, dy);
        float major = Math.Max(dx, dy);
        if (minor < 0.35f || major < 1.8f)
            return false;

        return minor / Math.Max(major, 0.001f) >= 0.08f;
    }

    private static float PdfSnapDoorAxisCandidateMax(float bridgeTolerancePt) =>
        Math.Clamp(bridgeTolerancePt * 1.80f, 32f, 140f);

    private static bool PdfSnapDoorArcTouchesAxisSegment(SKPoint axisStart, SKPoint axisEnd, SKPoint arcStart, SKPoint arcEnd, float near)
    {
        float nearSq = near * near;
        return DistanceSquared(axisStart, arcStart) <= nearSq ||
            DistanceSquared(axisStart, arcEnd) <= nearSq ||
            DistanceSquared(axisEnd, arcStart) <= nearSq ||
            DistanceSquared(axisEnd, arcEnd) <= nearSq ||
            DistanceToSegment(arcStart, axisStart, axisEnd) <= near ||
            DistanceToSegment(arcEnd, axisStart, axisEnd) <= near;
    }

    private static PdfSnapAxisSpan PdfSnapAxisSpanFor(PdfSnapBoundaryTraceSegment segment, bool horizontal) =>
        PdfSnapAxisSpanFor(segment.Start, segment.End, horizontal);

    private static PdfSnapAxisSpan PdfSnapAxisSpanFor(SKPoint start, SKPoint end, bool horizontal)
    {
        float coordinate = horizontal ? (start.Y + end.Y) * 0.5f : (start.X + end.X) * 0.5f;
        float a0 = horizontal ? start.X : start.Y;
        float a1 = horizontal ? end.X : end.Y;
        float min = Math.Min(a0, a1);
        float max = Math.Max(a0, a1);
        return new PdfSnapAxisSpan(coordinate, min, max);
    }

    private readonly record struct PdfSnapAxisSpan(float Coordinate, float MinAlong, float MaxAlong)
    {
        public float Length => MaxAlong - MinAlong;
    }
}
