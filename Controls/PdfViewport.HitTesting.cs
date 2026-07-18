using System;
using System.Collections.Generic;
using System.Linq;
using OurPlanCore;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private bool TryHitSelectedVertex(SKPoint pdf, out Measurement measurement, out int vertexIndex)
    {
        foreach (Measurement candidate in GetSelectedMeasurements().Reverse())
        {
            if (IsMeasurementOnActivePage(candidate) &&
                CanEditMeasurementVertices(candidate) &&
                TryHitVertexOnMeasurement(candidate, pdf, SelectedVertexHitToleranceScreenPx, out vertexIndex))
            {
                measurement = candidate;
                return true;
            }
        }

        measurement = null!;
        vertexIndex = -1;
        return false;
    }

    private bool TryHitSelectedMeasurementSelectedVertex(SKPoint pdf, out Measurement measurement, out int vertexIndex)
    {
        foreach (Measurement candidate in GetSelectedMeasurements().Reverse())
        {
            if (!IsMeasurementOnActivePage(candidate) ||
                !CanEditMeasurementVertices(candidate))
            {
                continue;
            }

            if (TryHitSelectedVertexOnMeasurement(candidate, pdf, SelectedVertexHitToleranceScreenPx, out vertexIndex))
            {
                measurement = candidate;
                return true;
            }
        }

        measurement = null!;
        vertexIndex = -1;
        return false;
    }

    private bool TryHitVertexOnSelectedMeasurement(SKPoint pdf, out Measurement measurement, out int vertexIndex)
    {
        foreach (Measurement candidate in GetSelectedMeasurements().Reverse())
        {
            if (IsMeasurementOnActivePage(candidate) &&
                CanEditMeasurementVertices(candidate) &&
                TryHitVertexOnMeasurement(candidate, pdf, SelectedVertexHitToleranceScreenPx, out vertexIndex))
            {
                measurement = candidate;
                return true;
            }
        }

        measurement = null!;
        vertexIndex = -1;
        return false;
    }

    private bool TryHitVertex(SKPoint pdf, out Measurement measurement, out int vertexIndex)
    {
        SKRect searchRect = MeasurementHitSearchRect(pdf, VertexHitToleranceScreenPx);
        IReadOnlyList<ViewportMeasurementVertexCandidate> candidates =
            ActivePageMeasurementVerticesNear(searchRect);
        float tol = VertexHitToleranceScreenPx / Math.Max(_zoom, 0.01f);
        float tolSq = tol * tol;
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            ViewportMeasurementVertexCandidate candidate = candidates[i];
            if (DistanceSquared(pdf, candidate.Point) <= tolSq)
            {
                measurement = candidate.Measurement;
                vertexIndex = candidate.GlobalIndex;
                return true;
            }
        }

        measurement = null!;
        vertexIndex = -1;
        return false;
    }

    private bool TryHitEditableVertex(SKPoint pdf, out Measurement measurement, out int vertexIndex)
    {
        if (TryHitVertex(pdf, out measurement, out vertexIndex) &&
            CanEditMeasurementVertices(measurement))
        {
            return true;
        }

        measurement = null!;
        vertexIndex = -1;
        return false;
    }

    private bool TryHitVertexOnMeasurement(Measurement measurement, SKPoint pdf, float screenTolerancePx, out int vertexIndex)
    {
        float tol = screenTolerancePx / Math.Max(_zoom, 0.01f);
        float tolSq = tol * tol;

        foreach (MeasurementVertexRef vertex in MeasurementVertices(measurement).Reverse())
        {
            if (DistanceSquared(pdf, vertex.Point) <= tolSq)
            {
                vertexIndex = vertex.GlobalIndex;
                return true;
            }
        }

        vertexIndex = -1;
        return false;
    }

    private bool TryHitSelectedVertexOnMeasurement(Measurement measurement, SKPoint pdf, float screenTolerancePx, out int vertexIndex)
    {
        float tol = screenTolerancePx / Math.Max(_zoom, 0.01f);
        float tolSq = tol * tol;

        foreach (MeasurementVertexRef vertex in MeasurementVertices(measurement).Reverse())
        {
            if (IsMeasurementVertexSelected(measurement, vertex.GlobalIndex) &&
                DistanceSquared(pdf, vertex.Point) <= tolSq)
            {
                vertexIndex = vertex.GlobalIndex;
                return true;
            }
        }

        vertexIndex = -1;
        return false;
    }

    private bool TryHitSelectedAnnotationVertex(SKPoint pdf, out PageAnnotation annotation, out int vertexIndex)
    {
        if (_selectedAnnotation != null &&
            IsAnnotationVisibleOnActivePage(_selectedAnnotation) &&
            TryHitVertexOnAnnotation(_selectedAnnotation, pdf, SelectedVertexHitToleranceScreenPx, out vertexIndex))
        {
            annotation = _selectedAnnotation;
            return true;
        }

        annotation = null!;
        vertexIndex = -1;
        return false;
    }

    private bool TryHitAnnotationVertex(SKPoint pdf, out PageAnnotation annotation, out int vertexIndex)
    {
        for (int i = _annotations.Count - 1; i >= 0; i--)
        {
            PageAnnotation candidate = _annotations[i];
            if (!IsAnnotationVisibleOnActivePage(candidate))
                continue;

            if (TryHitVertexOnAnnotation(candidate, pdf, VertexHitToleranceScreenPx, out vertexIndex))
            {
                annotation = candidate;
                return true;
            }
        }

        annotation = null!;
        vertexIndex = -1;
        return false;
    }

    private bool TryHitVertexOnAnnotation(PageAnnotation annotation, SKPoint pdf, float screenTolerancePx, out int vertexIndex)
    {
        float tol = screenTolerancePx / Math.Max(_zoom, 0.01f);
        float tolSq = tol * tol;

        for (int p = annotation.Points.Count - 1; p >= 0; p--)
        {
            if (DistanceSquared(pdf, annotation.Points[p]) <= tolSq)
            {
                vertexIndex = p;
                return true;
            }
        }

        vertexIndex = -1;
        return false;
    }

    private bool TryHitSelectedMeasurement(SKPoint pdf, out Measurement measurement)
    {
        return TryHitMeasurementWithSpatialIndex(
            pdf,
            SelectedMeasurementHitToleranceScreenPx,
            m => _selectedMeasurements.Contains(m),
            out measurement);
    }

    private bool TryHitMeasurement(SKPoint pdf, out Measurement measurement)
    {
        return TryHitMeasurementWithSpatialIndex(
            pdf,
            MeasurementHitToleranceScreenPx,
            predicate: null,
            out measurement);
    }

    private bool TryHitMeasurementWithSpatialIndex(
        SKPoint pdf,
        float screenTolerancePx,
        Func<Measurement, bool>? predicate,
        out Measurement measurement)
    {
        SKRect searchRect = MeasurementHitSearchRect(pdf, screenTolerancePx);
        IReadOnlyList<Measurement> candidates = ActivePageMeasurementsNear(searchRect);
        HashSet<Measurement> pointHits = MeasurementPointHits(pdf, searchRect, screenTolerancePx);
        HashSet<Measurement> segmentHits = MeasurementSegmentHits(pdf, searchRect, screenTolerancePx);
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            Measurement m = candidates[i];
            if (predicate != null && !predicate(m))
                continue;

            if (pointHits.Contains(m) ||
                segmentHits.Contains(m) ||
                m.MType == "area" && PointInMeasurementFill(m, pdf))
            {
                measurement = m;
                return true;
            }
        }

        measurement = null!;
        return false;
    }

    private HashSet<Measurement> MeasurementPointHits(
        SKPoint pdf,
        SKRect searchRect,
        float screenTolerancePx)
    {
        float tol = screenTolerancePx / Math.Max(_zoom, 0.01f);
        float tolSq = tol * tol;
        var hits = new HashSet<Measurement>();
        foreach (ViewportMeasurementVertexCandidate vertex in ActivePageMeasurementVerticesNear(searchRect))
        {
            if (DistanceSquared(pdf, vertex.Point) <= tolSq)
                hits.Add(vertex.Measurement);
        }

        return hits;
    }

    private HashSet<Measurement> MeasurementSegmentHits(
        SKPoint pdf,
        SKRect searchRect,
        float screenTolerancePx)
    {
        float tol = screenTolerancePx / Math.Max(_zoom, 0.01f);
        var hits = new HashSet<Measurement>();
        foreach (ViewportMeasurementSegmentCandidate segment in ActivePageMeasurementSegmentsNear(searchRect))
        {
            if (DistanceToSegment(pdf, segment.Start, segment.End) <= tol)
                hits.Add(segment.Measurement);
        }

        return hits;
    }

    private bool TryHitSelectedAnnotation(SKPoint pdf, out PageAnnotation annotation)
    {
        foreach (PageAnnotation candidate in _annotations.AsEnumerable().Reverse())
        {
            if (!_selectedAnnotations.Contains(candidate) ||
                !IsAnnotationVisibleOnActivePage(candidate))
            {
                continue;
            }

            if (IsAnnotationHit(candidate, pdf, SelectedMeasurementHitToleranceScreenPx))
            {
                annotation = candidate;
                return true;
            }
        }

        annotation = null!;
        return false;
    }

    private bool TryHitAnnotation(SKPoint pdf, out PageAnnotation annotation)
    {
        for (int i = _annotations.Count - 1; i >= 0; i--)
        {
            PageAnnotation candidate = _annotations[i];
            if (!IsAnnotationVisibleOnActivePage(candidate))
                continue;

            if (IsAnnotationHit(candidate, pdf, MeasurementHitToleranceScreenPx))
            {
                annotation = candidate;
                return true;
            }
        }

        annotation = null!;
        return false;
    }

    private bool IsMeasurementHit(Measurement measurement, SKPoint pdf, float screenTolerancePx)
    {
        float tol = screenTolerancePx / Math.Max(_zoom, 0.01f);

        if (measurement.MType == "point")
            return measurement.Points.Any(p => DistanceSquared(pdf, p) <= tol * tol);

        for (int p = 1; p < measurement.Points.Count; p++)
        {
            if (DistanceToSegment(pdf, measurement.Points[p - 1], measurement.Points[p]) <= tol)
                return true;
        }

        if (measurement.MType != "area" || measurement.Points.Count <= 2)
            return false;

        if (DistanceToSegment(pdf, measurement.Points[^1], measurement.Points[0]) <= tol)
            return true;

        foreach (var hole in measurement.Holes)
        {
            if (hole.Count < 3)
                continue;

            for (int p = 1; p < hole.Count; p++)
                if (DistanceToSegment(pdf, hole[p - 1], hole[p]) <= tol)
                    return true;

            if (DistanceToSegment(pdf, hole[^1], hole[0]) <= tol)
                return true;
        }

        return PointInMeasurementFill(measurement, pdf);
    }

    private bool IsAnnotationHit(PageAnnotation annotation, SKPoint pdf, float screenTolerancePx)
    {
        if (annotation.Points.Count < 2)
            return false;

        float tol = screenTolerancePx / Math.Max(_zoom, 0.01f);
        SKPoint start = annotation.Points[0];
        SKPoint end = annotation.Points[1];
        string kind = OurPlanCoreJobStore.NormalizePageAnnotationKind(annotation.Kind);
        if (kind is "rectangle" or "note" or "cloud" or "highlight")
        {
            IReadOnlyList<SKPoint> points = AnnotationTransformPoints(annotation);
            if (points.Count < 2)
                return false;

            if (points.Count == 2)
            {
                SKRect rect = NormalizeRect(points[0], points[1]);
                SKRect expanded = rect;
                expanded.Inflate(tol, tol);
                return RectContains(expanded, pdf) && RectContains(rect, pdf);
            }

            for (int i = 1; i < points.Count; i++)
                if (DistanceToSegment(pdf, points[i - 1], points[i]) <= tol)
                    return true;

            return DistanceToSegment(pdf, points[^1], points[0]) <= tol ||
                   PointInPolygon(pdf, points);
        }

        if (kind == "area")
        {
            if (annotation.Points.Count < 3)
                return false;

            for (int i = 1; i < annotation.Points.Count; i++)
                if (DistanceToSegment(pdf, annotation.Points[i - 1], annotation.Points[i]) <= tol)
                    return true;

            return DistanceToSegment(pdf, annotation.Points[^1], annotation.Points[0]) <= tol ||
                   PointInPolygon(pdf, annotation.Points);
        }

        if (kind == "line")
        {
            for (int i = 1; i < annotation.Points.Count; i++)
                if (DistanceToSegment(pdf, annotation.Points[i - 1], annotation.Points[i]) <= tol)
                    return true;

            return false;
        }

        return DistanceToSegment(pdf, start, end) <= tol;
    }
}
