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
            return false;

        float length = MeasurementGeometry.Distance(selected.Start, selected.End);
        float doorCandidateMax = Math.Clamp(bridgeTolerancePt * 1.20f, 20f, 96f);
        if (length > doorCandidateMax)
            return false;

        return PdfSnapGeometryAxisSegmentHasDoorPair(segments, selectedIndex, horizontal, bridgeTolerancePt) &&
            PdfSnapGeometryAxisSegmentHasDoorArc(segments, selectedIndex, bridgeTolerancePt);
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
        if (separation < 1.0f || separation > maxSeparation)
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
            if (TryClassifyPdfSnapAxisSegment(other, 2.5f, 4f, out _, out _, out _, out _))
                continue;
            if (MeasurementGeometry.Distance(other.Start, other.End) > maxArcSegment)
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

            var trace = new PdfSnapBoundaryTraceSegment(segments[i].Start, segments[i].End, i, false);
            if (TryClassifyPdfSnapAxisSegment(trace, 2.5f, 4f, out _, out _, out _, out _))
                continue;
            if (MeasurementGeometry.Distance(segments[i].Start, segments[i].End) > maxArcSegment)
                continue;
            if (PdfSnapDoorArcTouchesAxisSegment(selected.Start, selected.End, segments[i].Start, segments[i].End, near))
                return true;
        }

        return false;
    }

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
