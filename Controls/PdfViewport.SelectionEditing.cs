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
    private bool TryBeginMeasurementEdit(SKPoint pdf, Point screen, bool clearSelectionOnMiss)
    {
        if (TryHitSelectedVertex(pdf, out Measurement selectedVertexMeasurement, out int selectedVertexIndex) ||
            TryHitEditableVertex(pdf, out selectedVertexMeasurement, out selectedVertexIndex))
        {
            BeginVertexEdit(selectedVertexMeasurement, selectedVertexIndex, screen);
            return true;
        }

        if (SelectedVertexCount() > 0 &&
            TryHitSelectedMeasurement(pdf, out Measurement vertexModeMeasurement) &&
            TryGetSelectedVertexDragAnchor(vertexModeMeasurement, out Measurement anchorMeasurement, out int anchorVertexIndex))
        {
            BeginVertexEdit(anchorMeasurement, anchorVertexIndex, screen);
            return true;
        }

        if (_selectedMeasurements.Count > 1 &&
            TryHitSelectedMeasurement(pdf, out Measurement groupMeasurement))
        {
            BeginMeasurementMove(groupMeasurement, screen);
            return true;
        }

        if (TryHitSelectedMeasurement(pdf, out Measurement selectedMeasurement) ||
            TryHitMeasurement(pdf, out selectedMeasurement))
        {
            BeginMeasurementMove(selectedMeasurement, screen);
            return true;
        }

        if (clearSelectionOnMiss)
        {
            ClearSelection();
            RequestRepaint();
        }

        return false;
    }

    private void BeginVertexEdit(Measurement measurement, int vertexIndex, Point screen)
    {
        ClearInProgressInputForEdit();
        PrepareMeasurementVertexDragSelection(measurement, vertexIndex);
        _draggingVertex = true;
        _draggingMeasurement = false;
        _dragMeasurementChanged = false;
        _dragScreenStart = screen;
        _dragVertexOriginalPoint = MeasurementVertexPoint(measurement, vertexIndex);
        _dragMeasurementVertexOriginalPoints.Clear();
        foreach (Measurement selectedMeasurement in ActiveVertexMeasurements())
        {
            var points = new Dictionary<int, SKPoint>();
            foreach (int index in ActiveMeasurementVertexIndices(selectedMeasurement))
            {
                if (IsValidMeasurementVertexIndex(selectedMeasurement, index))
                    points[index] = MeasurementVertexPoint(selectedMeasurement, index);
            }

            if (points.Count > 0)
                _dragMeasurementVertexOriginalPoints[selectedMeasurement] = points;
        }
        _dragSelectionOriginalPoints.Clear();
        _dragSelectionOriginalHoles.Clear();
        CaptureMouse();
        foreach (Measurement selectedMeasurement in ActiveVertexMeasurements())
        {
            _dragSelectionOriginalPoints[selectedMeasurement] = selectedMeasurement.Points.ToList();
            _dragSelectionOriginalHoles[selectedMeasurement] = CloneHoles(selectedMeasurement.Holes);
        }
        int vertexCount = DraggedVertexCount();
        PostStatus(vertexCount > 1
            ? $"Editing {vertexCount} selected vertices. Drag to move together; Delete removes selected vertices."
            : $"Editing {ToolTitle(measurement.MType)} vertex {vertexIndex + 1}. Drag to move.");
    }

    private void BeginMeasurementMove(Measurement measurement, Point screen)
    {
        ClearInProgressInputForEdit();
        _draggingVertex = false;
        _draggingMeasurement = true;
        _dragMeasurementChanged = false;
        _dragScreenStart = screen;
        CaptureMouse();
        if (_selectedMeasurements.Contains(measurement))
            SetSelectedMeasurements(GetSelectedMeasurements(), measurement, -1);
        else
            SelectMeasurement(measurement, -1);

        _dragMeasurementOriginalPoints = measurement.Points.ToList();
        _dragSelectionOriginalPoints.Clear();
        var selected = GetSelectedMeasurements();
        if (selected.Count > 1 && selected.Contains(measurement))
        {
            foreach (Measurement selectedMeasurement in selected)
            {
                _dragSelectionOriginalPoints[selectedMeasurement] = selectedMeasurement.Points.ToList();
                _dragSelectionOriginalHoles[selectedMeasurement] = CloneHoles(selectedMeasurement.Holes);
            }
        }
        _dragMeasurementOriginalHoles = CloneHoles(measurement.Holes);

        PostStatus(selected.Count > 1
            ? $"Moving {selected.Count} selected measurements."
            : $"Moving {EntryTitle(measurement.MType)}. Drag the body to move; drag blue handles to reshape.");
    }

    private void FinishMeasurementDrag()
    {
        if (!_draggingVertex && !_draggingMeasurement)
            return;

        bool wasVertexDrag = _draggingVertex;

        if (_dragMeasurementChanged && _dragMeasurementVertexOriginalPoints.Count > 0)
        {
            PushMeasurementUndoSnapshots(
                _dragSelectionOriginalPoints,
                _dragSelectionOriginalHoles,
                "move selected vertices",
                "vertex-drag");
            NotifyMeasurementsChanged(_dragMeasurementVertexOriginalPoints.Keys.ToList());
        }
        else if (_dragMeasurementChanged && _dragSelectionOriginalPoints.Count > 0)
        {
            PushMeasurementUndoSnapshots(
                _dragSelectionOriginalPoints,
                _dragSelectionOriginalHoles,
                "move selected measurements",
                "measurement-drag");
            NotifyMeasurementsChanged(_dragSelectionOriginalPoints.Keys.ToList());
        }
        else if (_dragMeasurementChanged && _selectedMeasurement != null)
        {
            PushMeasurementUndoSnapshot(
                _selectedMeasurement,
                _dragMeasurementOriginalPoints,
                _dragMeasurementOriginalHoles,
                wasVertexDrag ? "move vertex" : "move measurement",
                wasVertexDrag ? "vertex-drag" : "measurement-drag");
            NotifyMeasurementsChanged([_selectedMeasurement]);
        }

        _draggingVertex = false;
        _draggingMeasurement = false;
        _dragMeasurementChanged = false;
        _dragMeasurementOriginalPoints.Clear();
        _dragMeasurementOriginalHoles.Clear();
        _dragMeasurementVertexOriginalPoints.Clear();
        _dragSelectionOriginalPoints.Clear();
        _dragSelectionOriginalHoles.Clear();
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        RequestRepaint();
    }

    private bool TryBeginAnnotationEdit(SKPoint pdf, Point screen, bool clearSelectionOnMiss)
    {
        if (TryHitSelectedAnnotationVertex(pdf, out PageAnnotation selectedVertexAnnotation, out int selectedVertexIndex) ||
            TryHitAnnotationVertex(pdf, out selectedVertexAnnotation, out selectedVertexIndex))
        {
            BeginAnnotationVertexEdit(selectedVertexAnnotation, selectedVertexIndex, screen);
            return true;
        }

        if (TryHitSelectedAnnotation(pdf, out PageAnnotation selectedAnnotation) ||
            TryHitAnnotation(pdf, out selectedAnnotation))
        {
            BeginAnnotationMove(selectedAnnotation, screen);
            return true;
        }

        if (clearSelectionOnMiss)
        {
            ClearSelection();
            RequestRepaint();
        }

        return false;
    }

    private void BeginAnnotationVertexEdit(PageAnnotation annotation, int vertexIndex, Point screen)
    {
        ClearInProgressInputForEdit();
        _draggingAnnotationVertex = true;
        _draggingAnnotation = false;
        _dragAnnotationChanged = false;
        _dragScreenStart = screen;
        _dragAnnotationVertexOriginalPoint = annotation.Points[vertexIndex];
        _dragAnnotationOriginalPoints = annotation.Points.ToList();
        CaptureMouse();
        SelectAnnotation(annotation, vertexIndex);
        PostStatus($"Editing {ToolTitle(annotation.Kind)} markup point {vertexIndex + 1}. Drag to move.");
    }

    private void BeginAnnotationMove(PageAnnotation annotation, Point screen)
    {
        ClearInProgressInputForEdit();
        _draggingAnnotationVertex = false;
        _draggingAnnotation = true;
        _dragAnnotationChanged = false;
        _dragScreenStart = screen;
        _dragAnnotationOriginalPoints = annotation.Points.ToList();
        CaptureMouse();
        SelectAnnotation(annotation, -1);
        PostStatus($"Moving {ToolTitle(annotation.Kind)} markup. Drag body to move; blue handles reshape; orange handle rotates/scales.");
    }

    private void FinishAnnotationDrag()
    {
        if (!_draggingAnnotationVertex && !_draggingAnnotation)
            return;

        PageAnnotation? changed = _dragAnnotationChanged ? _selectedAnnotation : null;
        List<SKPoint> beforePoints = _dragAnnotationOriginalPoints.ToList();
        _draggingAnnotationVertex = false;
        _draggingAnnotation = false;
        _dragAnnotationChanged = false;
        _dragAnnotationOriginalPoints.Clear();
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        if (changed != null)
        {
            PushAnnotationUndoSnapshot(changed, beforePoints, "move markup", "annotation-drag");
            PageAnnotationChanged?.Invoke(changed);
        }
        RequestRepaint();
    }

    private void ClearInProgressInputForEdit()
    {
        if (_drawPts.Count == 0 && _scalePts.Count == 0 && !_rubberEnd.HasValue && !_snapPreview.HasValue)
            return;

        _drawPts.Clear();
        _scalePts.Clear();
        _rubberEnd = null;
        SetSnapPreview(null);
    }

    private SKPoint ScreenDragDeltaToPdf(Point screen)
    {
        float safeZoom = Math.Max(_zoom, 0.001f);
        return new SKPoint(
            (float)((screen.X - _dragScreenStart.X) / safeZoom),
            (float)((screen.Y - _dragScreenStart.Y) / safeZoom));
    }

    // While moving selected vertices or whole shapes, holding Shift (ortho)
    // locks the move to the dominant axis so lines/areas stay aligned.
    private SKPoint ConstrainDragDeltaOrtho(SKPoint delta)
    {
        if (!IsOrthoActive())
            return delta;

        return Math.Abs(delta.X) >= Math.Abs(delta.Y)
            ? new SKPoint(delta.X, 0f)
            : new SKPoint(0f, delta.Y);
    }

    private void PostDragStatus(string label, SKPoint delta)
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastPointerStatusAt).TotalMilliseconds < 120)
            return;

        _lastPointerStatusAt = now;
        float screenDx = PdfToScreenDistance(delta.X);
        float screenDy = PdfToScreenDistance(delta.Y);
        PostStatus($"{label}: dx={screenDx:F0}px dy={screenDy:F0}px.");
    }

    private void BeginBoxSelection(SKPoint pdf, bool additive, bool removeMode = false)
    {
        ClearInProgressInputForEdit();
        _boxSelecting = true;
        _boxSelectStartPdf = pdf;
        _boxSelectEndPdf = pdf;
        _boxSelectAdditive = additive;
        _boxSelectRemove = removeMode;
        CaptureMouse();
        PostStatus(removeMode
            ? "Deselect: drag box around Line/Area handles to remove them from the selection."
            : additive
                ? HasEditableMeasurementSelection()
                    ? "Select vertices: drag box around Line/Area handles on selected measurements."
                    : "Select: drag box to add measurements to the current selection."
                : "Select: drag box around measurements.");
        RequestRepaint();
    }

    private void FinishBoxSelection()
    {
        if (!_boxSelecting)
            return;

        SKRect rect = NormalizeRect(_boxSelectStartPdf, _boxSelectEndPdf);
        bool tiny = PdfToScreenDistance(rect.Width) < 4f &&
                    PdfToScreenDistance(rect.Height) < 4f;
        _boxSelecting = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();

        if (tiny)
        {
            if (_boxSelectAdditive)
            {
                if (TryToggleMeasurementVertexSelection(_boxSelectStartPdf, _boxSelectRemove))
                {
                    RequestRepaint();
                    return;
                }

                if (TryHitMeasurement(_boxSelectStartPdf, out Measurement toggled))
                {
                    ToggleMeasurementSelection(toggled);
                    RequestRepaint();
                    return;
                }

                if (TryHitAnnotation(_boxSelectStartPdf, out PageAnnotation toggledAnnotation))
                {
                    ToggleAnnotationSelection(toggledAnnotation);
                    RequestRepaint();
                    return;
                }
            }

            if (!_boxSelectAdditive)
                ClearSelection();
            RequestRepaint();
            PostStatus("Select: no box drawn.");
            return;
        }

        if (_boxSelectAdditive && TrySelectMeasurementVerticesInBox(rect, _boxSelectRemove))
        {
            RequestRepaint();
            return;
        }

        bool selectTouched = _boxSelectEndPdf.X >= _boxSelectStartPdf.X;
        var hits = ActivePageMeasurementsNear(rect)
            .Where(m => selectTouched
                ? MeasurementIntersectsRect(m, rect)
                : MeasurementContainedInRect(m, rect))
            .ToList();
        var annotationHits = hits.Count == 0
            ? _annotations.Where(annotation =>
                IsAnnotationVisibleOnActivePage(annotation) &&
                (selectTouched
                    ? AnnotationIntersectsRect(annotation, rect)
                    : AnnotationContainedInRect(annotation, rect)))
                .ToList()
            : new List<PageAnnotation>();

        if (_boxSelectRemove)
        {
            if (hits.Count > 0)
            {
                var combined = GetSelectedMeasurements().Where(m => !hits.Contains(m)).ToList();
                SetSelectedMeasurements(combined, combined.LastOrDefault(), -1);
            }
            else if (annotationHits.Count > 0)
            {
                var combinedAnnotations = GetSelectedAnnotations().Where(a => !annotationHits.Contains(a)).ToList();
                SetSelectedAnnotations(combinedAnnotations, combinedAnnotations.LastOrDefault(), -1);
            }
        }
        else if (_boxSelectAdditive)
        {
            if (hits.Count > 0)
            {
                var combined = GetSelectedMeasurements().ToList();
                foreach (Measurement hit in hits)
                {
                    if (!combined.Contains(hit))
                        combined.Add(hit);
                }
                SetSelectedMeasurements(combined, hits.LastOrDefault() ?? combined.LastOrDefault(), -1);
            }
            else if (annotationHits.Count > 0)
            {
                var combinedAnnotations = GetSelectedAnnotations().ToList();
                foreach (PageAnnotation hit in annotationHits)
                {
                    if (!combinedAnnotations.Contains(hit))
                        combinedAnnotations.Add(hit);
                }
                SetSelectedAnnotations(combinedAnnotations, annotationHits.LastOrDefault() ?? combinedAnnotations.LastOrDefault(), -1);
            }
        }
        else
        {
            if (annotationHits.Count > 0)
                SetSelectedAnnotations(annotationHits, annotationHits.LastOrDefault(), -1);
            else
                SetSelectedMeasurements(hits, hits.LastOrDefault(), -1);
        }

        RequestRepaint();
        PostStatus(annotationHits.Count > 0
            ? annotationHits.Count == 1
                ? $"Selected {ToolTitle(annotationHits[0].Kind)} markup."
                : $"Selected {annotationHits.Count} markups. Delete removes them."
            : hits.Count == 0
            ? selectTouched
                ? "Select touched: no measurements touched by box."
                : "Select inside: no measurements fully inside box."
            : selectTouched
                ? $"Selected {GetSelectedMeasurements().Count} touched measurement(s). Ctrl+C copies, Ctrl+V pastes."
                : $"Selected {GetSelectedMeasurements().Count} enclosed measurement(s). Ctrl+C copies, Ctrl+V pastes.");
    }

    private void CancelBoxSelection()
    {
        if (!_boxSelecting)
            return;

        _boxSelecting = false;
        RequestRepaint();
    }

    private void PostBoxSelectionStatus()
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastPointerStatusAt).TotalMilliseconds < 120)
            return;

        _lastPointerStatusAt = now;
        SKRect rect = NormalizeRect(_boxSelectStartPdf, _boxSelectEndPdf);
        string mode = _boxSelectEndPdf.X >= _boxSelectStartPdf.X
            ? "touched"
            : "inside only";
        PostStatus($"Select box ({mode}): {PdfToScreenDistance(rect.Width):F0}px x {PdfToScreenDistance(rect.Height):F0}px.");
    }

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
        string kind = OurPlaneCoreJobStore.NormalizePageAnnotationKind(annotation.Kind);
        if (kind is "rectangle" or "note" or "cloud")
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

        return DistanceToSegment(pdf, start, end) <= tol;
    }

    private void SelectMeasurement(Measurement measurement, int vertexIndex)
    {
        SetSelectedMeasurements([measurement], measurement, vertexIndex);
    }

    private void SetSelectedMeasurements(IReadOnlyList<Measurement> measurements, Measurement? primary, int vertexIndex)
    {
        var next = measurements
            .Where(m => _measurementSet.Contains(m) && IsMeasurementOnActivePage(m))
            .Distinct()
            .ToList();

        Measurement? nextPrimary = primary != null && next.Contains(primary)
            ? primary
            : next.LastOrDefault();

        bool setChanged = _selectedMeasurements.Count != next.Count ||
                          next.Any(m => !_selectedMeasurements.Contains(m));
        bool primaryChanged = !ReferenceEquals(_selectedMeasurement, nextPrimary);
        bool vertexChanged = _selectedVertexIndex != vertexIndex;

        _selectedMeasurements.Clear();
        foreach (Measurement measurement in next)
            _selectedMeasurements.Add(measurement);

        _selectedMeasurement = nextPrimary;
        _selectedVertexIndex = nextPrimary == null ? -1 : vertexIndex;
        ClearMeasurementVertexSelection();
        if (nextPrimary != null && vertexIndex >= 0)
            VertexSelectionSet(nextPrimary, create: true).Add(vertexIndex);

        if (next.Count > 0)
            ClearAnnotationSelection();

        if (primaryChanged)
            MeasurementSelectionChanged?.Invoke(nextPrimary);
        if (setChanged)
            MeasurementsSelectionChanged?.Invoke(next);
        if (primaryChanged || setChanged || vertexChanged)
            RequestRepaint();
        PublishTransformSelectionChanged();
    }

    private void ToggleMeasurementSelection(Measurement measurement)
    {
        if (!_measurementSet.Contains(measurement) || !IsMeasurementOnActivePage(measurement))
            return;

        var selected = GetSelectedMeasurements().ToList();
        if (selected.Contains(measurement))
            selected.Remove(measurement);
        else
            selected.Add(measurement);

        SetSelectedMeasurements(selected, selected.Contains(measurement) ? measurement : selected.LastOrDefault(), -1);
        PostStatus(selected.Count == 0
            ? "Selection cleared."
            : $"Selected {selected.Count} measurement(s). Ctrl+C copies, Ctrl+V pastes.");
    }

    private void SelectAllActivePageMeasurements()
    {
        var selected = ActivePageMeasurements().ToList();
        SetSelectedMeasurements(selected, selected.LastOrDefault(), -1);
        PostStatus(selected.Count == 0
            ? "No measurements on this sheet."
            : $"Selected all {selected.Count} measurement(s) on this sheet.");
    }

    private void SelectAnnotation(PageAnnotation annotation, int vertexIndex)
    {
        SetSelectedAnnotations([annotation], annotation, vertexIndex);
    }

    private IReadOnlyList<PageAnnotation> GetSelectedAnnotations() =>
        _selectedAnnotations
            .Where(annotation => _annotations.Contains(annotation) && IsAnnotationVisibleOnActivePage(annotation))
            .ToList();

    private void SetSelectedAnnotations(IReadOnlyList<PageAnnotation> annotations, PageAnnotation? primary, int vertexIndex)
    {
        var next = annotations
            .Where(annotation => _annotations.Contains(annotation) && IsAnnotationVisibleOnActivePage(annotation))
            .Distinct()
            .ToList();
        PageAnnotation? nextPrimary = primary != null && next.Contains(primary)
            ? primary
            : next.LastOrDefault();

        bool setChanged = _selectedAnnotations.Count != next.Count ||
                          next.Any(annotation => !_selectedAnnotations.Contains(annotation));
        bool primaryChanged = !ReferenceEquals(_selectedAnnotation, nextPrimary);
        bool vertexChanged = _selectedAnnotationVertexIndex != vertexIndex;

        if (next.Count > 0 &&
            (_selectedMeasurements.Count > 0 || _selectedMeasurement != null))
        {
            _selectedMeasurements.Clear();
            _selectedMeasurement = null;
            _selectedVertexIndex = -1;
            ClearMeasurementVertexSelection();
            MeasurementSelectionChanged?.Invoke(null);
            MeasurementsSelectionChanged?.Invoke(Array.Empty<Measurement>());
        }

        _selectedAnnotations.Clear();
        foreach (PageAnnotation annotation in next)
            _selectedAnnotations.Add(annotation);

        _selectedAnnotation = nextPrimary;
        _selectedAnnotationVertexIndex = nextPrimary == null ? -1 : vertexIndex;
        if (primaryChanged || setChanged || vertexChanged)
            RequestRepaint();
        PublishTransformSelectionChanged();
    }

    private void ToggleAnnotationSelection(PageAnnotation annotation)
    {
        if (!_annotations.Contains(annotation) || !IsAnnotationVisibleOnActivePage(annotation))
            return;

        var selected = GetSelectedAnnotations().ToList();
        if (selected.Contains(annotation))
            selected.Remove(annotation);
        else
            selected.Add(annotation);

        SetSelectedAnnotations(selected, selected.Contains(annotation) ? annotation : selected.LastOrDefault(), -1);
        PostStatus(selected.Count == 0
            ? "Selection cleared."
            : $"Selected {selected.Count} markup(s). Delete removes them.");
    }

    private void CenterOnMeasurement(Measurement measurement)
    {
        if (measurement.Points.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0 || _zoom <= 0)
            return;

        SKRect bounds = MeasurementBounds(measurement);
        float centerX = (bounds.Left + bounds.Right) / 2f;
        float centerY = (bounds.Top + bounds.Bottom) / 2f;
        float visibleW = ScreenToPdfDistance((float)ActualWidth);
        float visibleH = ScreenToPdfDistance((float)ActualHeight);

        _panX = centerX - visibleW / 2f;
        _panY = centerY - visibleH / 2f;
        ClampPanToPage();
        ScheduleRerenderForZoom(force: false);
    }

    private void ClampPanToPage()
    {
        if (_pdfW <= 0 || _pdfH <= 0 || ActualWidth <= 0 || ActualHeight <= 0 || _zoom <= 0)
            return;

        float visibleW = ScreenToPdfDistance((float)ActualWidth);
        float visibleH = ScreenToPdfDistance((float)ActualHeight);
        _panX = Math.Clamp(_panX, 0, Math.Max(0, _pdfW - visibleW));
        _panY = Math.Clamp(_panY, 0, Math.Max(0, _pdfH - visibleH));
    }

    private void ClearSelection()
    {
        bool changed = _selectedMeasurement != null ||
                       _selectedMeasurements.Count > 0 ||
                       _selectedAnnotation != null ||
                       _selectedAnnotations.Count > 0;
        _selectedMeasurement = null;
        _selectedMeasurements.Clear();
        _selectedVertexIndex = -1;
        ClearMeasurementVertexSelection();
        ClearAnnotationSelection();
        _draggingVertex = false;
        _draggingMeasurement = false;
        _draggingAnnotationVertex = false;
        _draggingAnnotation = false;
        _draggingTransformScale = false;
        _draggingTransformRotate = false;
        _dragMeasurementChanged = false;
        _dragAnnotationChanged = false;
        _dragMeasurementOriginalPoints.Clear();
        _dragMeasurementOriginalHoles.Clear();
        _dragMeasurementVertexOriginalPoints.Clear();
        _dragSelectionOriginalPoints.Clear();
        _dragSelectionOriginalHoles.Clear();
        _dragAnnotationOriginalPoints.Clear();
        _transformMeasurementOriginalPoints.Clear();
        _transformMeasurementOriginalHoles.Clear();
        _transformMeasurementOriginalJoistDirections.Clear();
        _transformAnnotationOriginalPoints.Clear();
        if (changed)
        {
            MeasurementSelectionChanged?.Invoke(null);
            MeasurementsSelectionChanged?.Invoke(Array.Empty<Measurement>());
        }
        PublishTransformSelectionChanged();
    }

    private void ClearAnnotationSelection()
    {
        _selectedAnnotation = null;
        _selectedAnnotations.Clear();
        _selectedAnnotationVertexIndex = -1;
    }

    private void DeleteSelectedOverlay()
    {
        if (TryDeleteSelectedMeasurementVertices())
            return;

        if (GetSelectedMeasurements().Count > 0)
        {
            DeleteSelectedMeasurement();
            return;
        }

        DeleteSelectedAnnotation();
    }

    private void DeleteSelectedMeasurement()
    {
        var selected = GetSelectedMeasurements();
        if (selected.Count == 0)
            return;

        PushRemovedMeasurementsUndo(selected, $"restore {selected.Count} deleted measurement(s)");
        foreach (Measurement removed in selected)
        {
            _measurements.Remove(removed);
            _measurementSet.Remove(removed);
            RemoveMeasurementFromPageIndex(removed);
            ForgetMeasurementState(removed);
        }
        ClearSelection();
        RequestRepaint();
        PostStatus(selected.Count == 1
            ? $"Deleted {selected[0].MType}."
            : $"Deleted {selected.Count} selected measurements.");
        NotifyMeasurementsRemoved(selected);
    }

    private void DeleteSelectedAnnotation()
    {
        var selected = GetSelectedAnnotations();
        if (selected.Count == 0 && _selectedAnnotation != null)
            selected = [_selectedAnnotation];
        if (selected.Count == 0)
            return;

        DeletePageAnnotations(selected);
    }

    private bool IsMeasurementOnActivePage(Measurement measurement) =>
        IsSamePageFolder(measurement.PageFolder, _pageFolder) &&
        IsMeasurementTakeoffVisible(measurement);

    private bool IsMeasurementTakeoffVisible(Measurement measurement)
    {
        if (string.IsNullOrWhiteSpace(measurement.TakeoffFolder))
            return true;

        return !_hiddenTakeoffFolders.Contains(NormalizePageFolderForCompare(measurement.TakeoffFolder));
    }

    private bool IsAnnotationOnActivePage(PageAnnotation annotation) =>
        IsSamePageFolder(annotation.PageFolder, _pageFolder);

    private bool IsAnnotationVisibleOnActivePage(PageAnnotation annotation) =>
        IsAnnotationOnActivePage(annotation) &&
        (!_hideRulerAnnotations || !IsRulerAnnotation(annotation) || !annotation.Hidden);

    private static bool IsRulerAnnotation(PageAnnotation annotation) =>
        string.Equals(
            OurPlaneCoreJobStore.NormalizePageAnnotationKind(annotation.Kind),
            "dimension",
            StringComparison.OrdinalIgnoreCase);

    private bool IsMeasurementSelected(Measurement measurement) =>
        _selectedMeasurements.Contains(measurement);

    private bool IsAnnotationSelected(PageAnnotation annotation) =>
        _selectedAnnotations.Contains(annotation);

    private void PruneHiddenAnnotationSelection()
    {
        if (_selectedAnnotations.Count == 0 && _selectedAnnotation == null)
            return;

        var visible = GetSelectedAnnotations();
        if (visible.Count == _selectedAnnotations.Count &&
            (_selectedAnnotation == null || visible.Contains(_selectedAnnotation)))
        {
            return;
        }

        SetSelectedAnnotations(visible, visible.Contains(_selectedAnnotation) ? _selectedAnnotation : visible.LastOrDefault(), -1);
    }

    private IReadOnlyList<Measurement> ActivePageMeasurements()
    {
        if (string.IsNullOrWhiteSpace(_pageFolder))
            return _measurements;

        string key = NormalizePageFolderForCompare(_pageFolder);
        if (!_measurementsByPage.TryGetValue(key, out List<Measurement>? measurements))
            return Array.Empty<Measurement>();

        if (_hiddenTakeoffFolders.Count == 0)
            return measurements;

        int folderHash = TakeoffFolderHash(measurements);
        if (_visibleActivePageMeasurementsIndexVersion == _measurementIndexVersion &&
            _visibleActivePageMeasurementsHiddenVersion == _hiddenTakeoffFoldersVersion &&
            _visibleActivePageMeasurementsFolderHash == folderHash &&
            string.Equals(_visibleActivePageMeasurementsPageKey, key, StringComparison.OrdinalIgnoreCase))
        {
            return _visibleActivePageMeasurements;
        }

        _visibleActivePageMeasurements.Clear();
        foreach (Measurement measurement in measurements)
        {
            if (IsMeasurementTakeoffVisible(measurement))
                _visibleActivePageMeasurements.Add(measurement);
        }

        _visibleActivePageMeasurementsPageKey = key;
        _visibleActivePageMeasurementsIndexVersion = _measurementIndexVersion;
        _visibleActivePageMeasurementsHiddenVersion = _hiddenTakeoffFoldersVersion;
        _visibleActivePageMeasurementsFolderHash = folderHash;
        return _visibleActivePageMeasurements;
    }

    private IReadOnlyList<Measurement> ActivePageMeasurementsNear(SKRect rect) =>
        ActivePageMeasurementSpatialIndex().Query(rect);

    private IReadOnlyList<ViewportMeasurementVertexCandidate> ActivePageMeasurementVerticesNear(SKRect rect) =>
        ActivePageMeasurementSpatialIndex().QueryVertices(rect);

    private IReadOnlyList<ViewportMeasurementSegmentCandidate> ActivePageMeasurementSegmentsNear(SKRect rect) =>
        ActivePageMeasurementSpatialIndex().QuerySegments(rect);

    private ViewportMeasurementSpatialIndex ActivePageMeasurementSpatialIndex()
    {
        IReadOnlyList<Measurement> activeMeasurements = ActivePageMeasurements();
        string key = string.IsNullOrWhiteSpace(_pageFolder)
            ? ""
            : NormalizePageFolderForCompare(_pageFolder);
        int folderHash = TakeoffFolderHash(activeMeasurements);

        if (_activePageMeasurementSpatialIndex != null &&
            _activePageMeasurementSpatialIndexVersion == _measurementIndexVersion &&
            _activePageMeasurementSpatialIndexGeometryVersion == _measurementGeometryVersion &&
            _activePageMeasurementSpatialIndexHiddenVersion == _hiddenTakeoffFoldersVersion &&
            _activePageMeasurementSpatialIndexFolderHash == folderHash &&
            string.Equals(_activePageMeasurementSpatialIndexPageKey, key, StringComparison.OrdinalIgnoreCase))
        {
            return _activePageMeasurementSpatialIndex;
        }

        _activePageMeasurementSpatialIndex = new ViewportMeasurementSpatialIndex(activeMeasurements);
        _activePageMeasurementSpatialIndexPageKey = key;
        _activePageMeasurementSpatialIndexVersion = _measurementIndexVersion;
        _activePageMeasurementSpatialIndexGeometryVersion = _measurementGeometryVersion;
        _activePageMeasurementSpatialIndexHiddenVersion = _hiddenTakeoffFoldersVersion;
        _activePageMeasurementSpatialIndexFolderHash = folderHash;
        return _activePageMeasurementSpatialIndex;
    }

    private void InvalidateActivePageMeasurementSpatialIndex()
    {
        _measurementGeometryVersion++;
        _activePageMeasurementSpatialIndex = null;
        _activePageMeasurementSpatialIndexPageKey = "";
        _activePageMeasurementSpatialIndexVersion = -1;
        _activePageMeasurementSpatialIndexGeometryVersion = -1;
        _activePageMeasurementSpatialIndexHiddenVersion = -1;
        _activePageMeasurementSpatialIndexFolderHash = 0;
    }

    private SKRect MeasurementHitSearchRect(SKPoint pdf, float screenTolerancePx)
    {
        float tol = screenTolerancePx / Math.Max(_zoom, 0.01f);
        return SKRect.Create(pdf.X - tol, pdf.Y - tol, tol * 2f, tol * 2f);
    }

    private static int TakeoffFolderHash(IReadOnlyList<Measurement> measurements)
    {
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < measurements.Count; i++)
            {
                hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(
                    measurements[i].TakeoffFolder ?? string.Empty);
            }

            return hash;
        }
    }

    private void IndexMeasurementByPage(Measurement measurement)
    {
        if (string.IsNullOrWhiteSpace(measurement.PageFolder))
        {
            InvalidateActivePageMeasurementCache();
            return;
        }

        string key = NormalizePageFolderForCompare(measurement.PageFolder);
        if (!_measurementsByPage.TryGetValue(key, out List<Measurement>? measurements))
        {
            measurements = [];
            _measurementsByPage[key] = measurements;
        }

        if (!measurements.Contains(measurement))
            measurements.Add(measurement);
        InvalidateActivePageMeasurementCache();
    }

    private void RemoveMeasurementFromPageIndex(Measurement measurement)
    {
        if (string.IsNullOrWhiteSpace(measurement.PageFolder))
        {
            InvalidateActivePageMeasurementCache();
            return;
        }

        string key = NormalizePageFolderForCompare(measurement.PageFolder);
        if (!_measurementsByPage.TryGetValue(key, out List<Measurement>? measurements))
        {
            InvalidateActivePageMeasurementCache();
            return;
        }

        measurements.Remove(measurement);
        if (measurements.Count == 0)
            _measurementsByPage.Remove(key);
        InvalidateActivePageMeasurementCache();
    }

    private void InvalidateActivePageMeasurementCache()
    {
        _measurementIndexVersion++;
        _visibleActivePageMeasurements.Clear();
        _visibleActivePageMeasurementsPageKey = "";
        _visibleActivePageMeasurementsIndexVersion = -1;
        _visibleActivePageMeasurementsHiddenVersion = -1;
        _visibleActivePageMeasurementsFolderHash = 0;
        InvalidateActivePageMeasurementSpatialIndex();
    }

    private void InvalidateHiddenTakeoffFolderCache()
    {
        _hiddenTakeoffFoldersVersion++;
        _visibleActivePageMeasurements.Clear();
        _visibleActivePageMeasurementsPageKey = "";
        _visibleActivePageMeasurementsIndexVersion = -1;
        _visibleActivePageMeasurementsHiddenVersion = -1;
        _visibleActivePageMeasurementsFolderHash = 0;
        InvalidateActivePageMeasurementSpatialIndex();
    }

    private void ForgetMeasurementState(Measurement measurement)
    {
        _selectedMeasurements.Remove(measurement);
        _selectedMeasurementVertexIndices.Remove(measurement);
        _dragMeasurementVertexOriginalPoints.Remove(measurement);
        _dragSelectionOriginalPoints.Remove(measurement);
        _dragSelectionOriginalHoles.Remove(measurement);
        _transformMeasurementOriginalPoints.Remove(measurement);
        _transformMeasurementOriginalHoles.Remove(measurement);

        if (ReferenceEquals(_selectedMeasurement, measurement))
        {
            _selectedMeasurement = null;
            _dragMeasurementOriginalPoints.Clear();
            _dragMeasurementOriginalHoles.Clear();
        }
    }

    private void PruneHiddenMeasurementSelection()
    {
        var hiddenSelected = _selectedMeasurements
            .Where(measurement => !IsMeasurementOnActivePage(measurement))
            .ToList();
        if (hiddenSelected.Count == 0)
            return;

        foreach (Measurement measurement in hiddenSelected)
            ForgetMeasurementState(measurement);

        if (_selectedMeasurements.Count == 0)
        {
            ClearSelection();
            return;
        }

        if (_selectedMeasurement == null || !_selectedMeasurements.Contains(_selectedMeasurement))
        {
            _selectedMeasurement = _selectedMeasurements.LastOrDefault();
            _selectedVertexIndex = -1;
            ClearMeasurementVertexSelection();
        }

        MeasurementSelectionChanged?.Invoke(_selectedMeasurement);
        MeasurementsSelectionChanged?.Invoke(_selectedMeasurements.ToList());
        PublishTransformSelectionChanged();
    }

    private static bool IsSamePageFolder(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(NormalizePageFolderForCompare(left), NormalizePageFolderForCompare(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePageFolderForCompare(string path)
    {
        string trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0)
            return "";

        try
        {
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return trimmed;
        }
    }

}
