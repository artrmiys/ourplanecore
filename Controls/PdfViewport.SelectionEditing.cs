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
        if (_selectedMeasurements.Count > 1 &&
            TryHitSelectedMeasurement(pdf, out Measurement groupMeasurement))
        {
            BeginMeasurementMove(groupMeasurement, screen);
            return true;
        }

        if (TryHitSelectedVertex(pdf, out Measurement selectedVertexMeasurement, out int selectedVertexIndex) ||
            TryHitVertex(pdf, out selectedVertexMeasurement, out selectedVertexIndex))
        {
            BeginVertexEdit(selectedVertexMeasurement, selectedVertexIndex, screen);
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
        _draggingVertex = true;
        _draggingMeasurement = false;
        _dragMeasurementChanged = false;
        _dragScreenStart = screen;
        _dragVertexOriginalPoint = measurement.Points[vertexIndex];
        _dragSelectionOriginalPoints.Clear();
        CaptureMouse();
        SelectMeasurement(measurement, vertexIndex);
        PostStatus($"Editing {ToolTitle(measurement.MType)} vertex {vertexIndex + 1}. Drag to move.");
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
                _dragSelectionOriginalPoints[selectedMeasurement] = selectedMeasurement.Points.ToList();
        }

        PostStatus(selected.Count > 1
            ? $"Moving {selected.Count} selected measurements."
            : $"Moving {EntryTitle(measurement.MType)}. Drag the body to move; drag blue handles to reshape.");
    }

    private void FinishMeasurementDrag()
    {
        if (!_draggingVertex && !_draggingMeasurement)
            return;

        if (_dragMeasurementChanged && _dragSelectionOriginalPoints.Count > 0)
        {
            foreach (Measurement measurement in _dragSelectionOriginalPoints.Keys.ToList())
                MeasurementChanged?.Invoke(measurement);
        }
        else if (_dragMeasurementChanged && _selectedMeasurement != null)
        {
            MeasurementChanged?.Invoke(_selectedMeasurement);
        }

        _draggingVertex = false;
        _draggingMeasurement = false;
        _dragMeasurementChanged = false;
        _dragMeasurementOriginalPoints.Clear();
        _dragSelectionOriginalPoints.Clear();
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
        _dragAnnotationOriginalPoints.Clear();
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
        PostStatus($"Moving {ToolTitle(annotation.Kind)} markup. Drag body to move; drag blue handles to reshape.");
    }

    private void FinishAnnotationDrag()
    {
        if (!_draggingAnnotationVertex && !_draggingAnnotation)
            return;

        PageAnnotation? changed = _dragAnnotationChanged ? _selectedAnnotation : null;
        _draggingAnnotationVertex = false;
        _draggingAnnotation = false;
        _dragAnnotationChanged = false;
        _dragAnnotationOriginalPoints.Clear();
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        if (changed != null)
            PageAnnotationChanged?.Invoke(changed);
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

    private void PostDragStatus(string label, SKPoint delta)
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastPointerStatusAt).TotalMilliseconds < 120)
            return;

        _lastPointerStatusAt = now;
        float screenDx = delta.X * _zoom;
        float screenDy = delta.Y * _zoom;
        PostStatus($"{label}: dx={screenDx:F0}px dy={screenDy:F0}px.");
    }

    private void BeginBoxSelection(SKPoint pdf, bool additive)
    {
        ClearInProgressInputForEdit();
        _boxSelecting = true;
        _boxSelectStartPdf = pdf;
        _boxSelectEndPdf = pdf;
        _boxSelectAdditive = additive;
        CaptureMouse();
        PostStatus(additive
            ? "Select: drag box to add measurements to the current selection."
            : "Select: drag box around measurements.");
        RequestRepaint();
    }

    private void FinishBoxSelection()
    {
        if (!_boxSelecting)
            return;

        SKRect rect = NormalizeRect(_boxSelectStartPdf, _boxSelectEndPdf);
        bool tiny = rect.Width * Math.Max(_zoom, 0.001f) < 4f &&
                    rect.Height * Math.Max(_zoom, 0.001f) < 4f;
        _boxSelecting = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();

        if (tiny)
        {
            if (!_boxSelectAdditive)
                ClearSelection();
            RequestRepaint();
            PostStatus("Select: no box drawn.");
            return;
        }

        var hits = _measurements
            .Where(m => IsMeasurementOnActivePage(m) && MeasurementIntersectsRect(m, rect))
            .ToList();
        PageAnnotation? annotationHit = hits.Count == 0
            ? _annotations.LastOrDefault(annotation =>
                IsAnnotationOnActivePage(annotation) &&
                AnnotationIntersectsRect(annotation, rect))
            : null;

        if (_boxSelectAdditive)
        {
            var combined = GetSelectedMeasurements().ToList();
            foreach (Measurement hit in hits)
            {
                if (!combined.Contains(hit))
                    combined.Add(hit);
            }
            SetSelectedMeasurements(combined, hits.LastOrDefault() ?? combined.LastOrDefault(), -1);
        }
        else
        {
            if (annotationHit != null)
                SelectAnnotation(annotationHit, -1);
            else
                SetSelectedMeasurements(hits, hits.LastOrDefault(), -1);
        }

        RequestRepaint();
        PostStatus(annotationHit != null
            ? $"Selected {ToolTitle(annotationHit.Kind)} markup."
            : hits.Count == 0
            ? "Select: no measurements inside box."
            : $"Selected {GetSelectedMeasurements().Count} measurement(s). Ctrl+C copies, Ctrl+V pastes.");
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
        PostStatus($"Select box: {rect.Width * _zoom:F0}px x {rect.Height * _zoom:F0}px.");
    }

    private bool TryHitSelectedVertex(SKPoint pdf, out Measurement measurement, out int vertexIndex)
    {
        if (_selectedMeasurement != null &&
            IsMeasurementOnActivePage(_selectedMeasurement) &&
            TryHitVertexOnMeasurement(_selectedMeasurement, pdf, SelectedVertexHitToleranceScreenPx, out vertexIndex))
        {
            measurement = _selectedMeasurement;
            return true;
        }

        measurement = null!;
        vertexIndex = -1;
        return false;
    }

    private bool TryHitVertex(SKPoint pdf, out Measurement measurement, out int vertexIndex)
    {
        for (int i = _measurements.Count - 1; i >= 0; i--)
        {
            Measurement m = _measurements[i];
            if (!IsMeasurementOnActivePage(m)) continue;

            if (TryHitVertexOnMeasurement(m, pdf, VertexHitToleranceScreenPx, out vertexIndex))
            {
                measurement = m;
                return true;
            }
        }

        measurement = null!;
        vertexIndex = -1;
        return false;
    }

    private bool TryHitVertexOnMeasurement(Measurement measurement, SKPoint pdf, float screenTolerancePx, out int vertexIndex)
    {
        float tol = screenTolerancePx / Math.Max(_zoom, 0.01f);
        float tolSq = tol * tol;

        for (int p = measurement.Points.Count - 1; p >= 0; p--)
        {
            if (DistanceSquared(pdf, measurement.Points[p]) <= tolSq)
            {
                vertexIndex = p;
                return true;
            }
        }

        vertexIndex = -1;
        return false;
    }

    private bool TryHitSelectedAnnotationVertex(SKPoint pdf, out PageAnnotation annotation, out int vertexIndex)
    {
        if (_selectedAnnotation != null &&
            IsAnnotationOnActivePage(_selectedAnnotation) &&
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
            if (!IsAnnotationOnActivePage(candidate))
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
        for (int i = _measurements.Count - 1; i >= 0; i--)
        {
            Measurement m = _measurements[i];
            if (!_selectedMeasurements.Contains(m) || !IsMeasurementOnActivePage(m))
                continue;

            if (IsMeasurementHit(m, pdf, SelectedMeasurementHitToleranceScreenPx))
            {
                measurement = m;
                return true;
            }
        }

        measurement = null!;
        return false;
    }

    private bool TryHitMeasurement(SKPoint pdf, out Measurement measurement)
    {
        for (int i = _measurements.Count - 1; i >= 0; i--)
        {
            Measurement m = _measurements[i];
            if (!IsMeasurementOnActivePage(m)) continue;

            if (IsMeasurementHit(m, pdf, MeasurementHitToleranceScreenPx))
            {
                measurement = m;
                return true;
            }
        }

        measurement = null!;
        return false;
    }

    private bool TryHitSelectedAnnotation(SKPoint pdf, out PageAnnotation annotation)
    {
        if (_selectedAnnotation != null &&
            IsAnnotationOnActivePage(_selectedAnnotation) &&
            IsAnnotationHit(_selectedAnnotation, pdf, SelectedMeasurementHitToleranceScreenPx))
        {
            annotation = _selectedAnnotation;
            return true;
        }

        annotation = null!;
        return false;
    }

    private bool TryHitAnnotation(SKPoint pdf, out PageAnnotation annotation)
    {
        for (int i = _annotations.Count - 1; i >= 0; i--)
        {
            PageAnnotation candidate = _annotations[i];
            if (!IsAnnotationOnActivePage(candidate))
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

        return measurement.MType == "area" &&
               measurement.Points.Count > 2 &&
               DistanceToSegment(pdf, measurement.Points[^1], measurement.Points[0]) <= tol;
    }

    private bool IsAnnotationHit(PageAnnotation annotation, SKPoint pdf, float screenTolerancePx)
    {
        if (annotation.Points.Count < 2)
            return false;

        float tol = screenTolerancePx / Math.Max(_zoom, 0.01f);
        SKPoint start = annotation.Points[0];
        SKPoint end = annotation.Points[1];
        string kind = OurPlaneCoreJobStore.NormalizePageAnnotationKind(annotation.Kind);
        if (kind == "rectangle")
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

        return DistanceToSegment(pdf, start, end) <= tol;
    }

    private void SelectMeasurement(Measurement measurement, int vertexIndex)
    {
        SetSelectedMeasurements([measurement], measurement, vertexIndex);
    }

    private void SetSelectedMeasurements(IReadOnlyList<Measurement> measurements, Measurement? primary, int vertexIndex)
    {
        var next = measurements
            .Where(m => _measurements.Contains(m) && IsMeasurementOnActivePage(m))
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
        if (!_measurements.Contains(measurement) || !IsMeasurementOnActivePage(measurement))
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
        var selected = _measurements
            .Where(IsMeasurementOnActivePage)
            .ToList();
        SetSelectedMeasurements(selected, selected.LastOrDefault(), -1);
        PostStatus(selected.Count == 0
            ? "No measurements on this sheet."
            : $"Selected all {selected.Count} measurement(s) on this sheet.");
    }

    private void SelectAnnotation(PageAnnotation annotation, int vertexIndex)
    {
        if (!_annotations.Contains(annotation) || !IsAnnotationOnActivePage(annotation))
            return;

        if (_selectedMeasurements.Count > 0 || _selectedMeasurement != null)
        {
            _selectedMeasurements.Clear();
            _selectedMeasurement = null;
            _selectedVertexIndex = -1;
            MeasurementSelectionChanged?.Invoke(null);
            MeasurementsSelectionChanged?.Invoke(Array.Empty<Measurement>());
        }

        bool changed = !ReferenceEquals(_selectedAnnotation, annotation) ||
                       _selectedAnnotationVertexIndex != vertexIndex;
        _selectedAnnotation = annotation;
        _selectedAnnotationVertexIndex = vertexIndex;
        if (changed)
            RequestRepaint();
        PublishTransformSelectionChanged();
    }

    private void CenterOnMeasurement(Measurement measurement)
    {
        if (measurement.Points.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0 || _zoom <= 0)
            return;

        SKRect bounds = MeasurementBounds(measurement);
        float centerX = (bounds.Left + bounds.Right) / 2f;
        float centerY = (bounds.Top + bounds.Bottom) / 2f;
        float visibleW = (float)ActualWidth / _zoom;
        float visibleH = (float)ActualHeight / _zoom;

        _panX = centerX - visibleW / 2f;
        _panY = centerY - visibleH / 2f;
        ClampPanToPage();
        ScheduleRerenderForZoom(force: false);
    }

    private void ClampPanToPage()
    {
        if (_pdfW <= 0 || _pdfH <= 0 || ActualWidth <= 0 || ActualHeight <= 0 || _zoom <= 0)
            return;

        float visibleW = (float)ActualWidth / _zoom;
        float visibleH = (float)ActualHeight / _zoom;
        _panX = Math.Clamp(_panX, 0, Math.Max(0, _pdfW - visibleW));
        _panY = Math.Clamp(_panY, 0, Math.Max(0, _pdfH - visibleH));
    }

    private void ClearSelection()
    {
        bool changed = _selectedMeasurement != null ||
                       _selectedMeasurements.Count > 0 ||
                       _selectedAnnotation != null;
        _selectedMeasurement = null;
        _selectedMeasurements.Clear();
        _selectedVertexIndex = -1;
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
        _dragSelectionOriginalPoints.Clear();
        _dragAnnotationOriginalPoints.Clear();
        _transformMeasurementOriginalPoints.Clear();
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
        _selectedAnnotationVertexIndex = -1;
    }

    private void DeleteSelectedOverlay()
    {
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

        foreach (Measurement removed in selected)
            _measurements.Remove(removed);
        ClearSelection();
        RequestRepaint();
        PostStatus(selected.Count == 1
            ? $"Deleted {selected[0].MType}."
            : $"Deleted {selected.Count} selected measurements.");
        foreach (Measurement removed in selected)
            MeasurementRemoved?.Invoke(removed);
    }

    private void DeleteSelectedAnnotation()
    {
        if (_selectedAnnotation == null)
            return;

        PageAnnotation annotation = _selectedAnnotation;
        DeletePageAnnotation(annotation);
    }

    private bool IsMeasurementOnActivePage(Measurement measurement) =>
        IsSamePageFolder(measurement.PageFolder, _pageFolder);

    private bool IsAnnotationOnActivePage(PageAnnotation annotation) =>
        IsSamePageFolder(annotation.PageFolder, _pageFolder);

    private bool IsMeasurementSelected(Measurement measurement) =>
        _selectedMeasurements.Contains(measurement);

    private bool IsAnnotationSelected(PageAnnotation annotation) =>
        ReferenceEquals(_selectedAnnotation, annotation);

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
