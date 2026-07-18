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
using OurPlanCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private bool TryBeginVertexEdit(SKPoint pdf, Point screen)
    {
        if (SelectedVertexCount() > 0 &&
            TryHitSelectedMeasurementSelectedVertex(pdf, out Measurement selectedSetMeasurement, out int selectedSetVertexIndex))
        {
            BeginVertexEdit(selectedSetMeasurement, selectedSetVertexIndex, screen);
            return true;
        }

        if (TryHitVertexOnSelectedMeasurement(pdf, out Measurement selectedObjectMeasurement, out int selectedObjectVertexIndex))
        {
            BeginVertexEdit(selectedObjectMeasurement, selectedObjectVertexIndex, screen);
            return true;
        }

        if (TryHitEditableVertex(pdf, out Measurement pointVertexMeasurement, out int pointVertexIndex) &&
            pointVertexMeasurement.MType == "point")
        {
            BeginVertexEdit(pointVertexMeasurement, pointVertexIndex, screen);
            return true;
        }

        if (IsVertexModifierActive() &&
            (TryHitSelectedVertex(pdf, out Measurement selectedVertexMeasurement, out int selectedVertexIndex) ||
             TryHitEditableVertex(pdf, out selectedVertexMeasurement, out selectedVertexIndex)))
        {
            BeginVertexEdit(selectedVertexMeasurement, selectedVertexIndex, screen);
            return true;
        }

        if (IsVertexModifierActive() &&
            SelectedVertexCount() > 0 &&
            TryHitSelectedMeasurement(pdf, out Measurement vertexModeMeasurement) &&
            TryGetSelectedVertexDragAnchor(vertexModeMeasurement, out Measurement anchorMeasurement, out int anchorVertexIndex))
        {
            BeginVertexEdit(anchorMeasurement, anchorVertexIndex, screen);
            return true;
        }

        return false;
    }

    private bool TryBeginMeasurementEdit(SKPoint pdf, Point screen, bool clearSelectionOnMiss)
    {
        if (TryBeginVertexEdit(pdf, screen))
            return true;

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
        CaptureMouse();
        if (_selectedAnnotations.Contains(annotation))
            SetSelectedAnnotations(GetSelectedAnnotations(), annotation, -1);
        else
            SelectAnnotation(annotation, -1);

        _dragAnnotationOriginalPoints = annotation.Points.ToList();
        _dragAnnotationSelectionOriginalPoints.Clear();
        var selected = GetSelectedAnnotations();
        if (selected.Count > 1 && selected.Contains(annotation))
        {
            foreach (PageAnnotation selectedAnnotation in selected)
                _dragAnnotationSelectionOriginalPoints[selectedAnnotation] = selectedAnnotation.Points.ToList();
        }

        PostStatus(selected.Count > 1
            ? $"Moving {selected.Count} selected markups."
            : $"Moving {ToolTitle(annotation.Kind)} markup. Drag body to move; blue handles reshape; orange handle rotates/scales.");
    }

    private void FinishAnnotationDrag()
    {
        if (!_draggingAnnotationVertex && !_draggingAnnotation)
            return;

        bool wasChanged = _dragAnnotationChanged;
        PageAnnotation? changed = wasChanged ? _selectedAnnotation : null;
        List<SKPoint> beforePoints = _dragAnnotationOriginalPoints.ToList();
        Dictionary<PageAnnotation, List<SKPoint>> beforeSelection = _dragAnnotationSelectionOriginalPoints
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
        _draggingAnnotationVertex = false;
        _draggingAnnotation = false;
        _dragAnnotationChanged = false;
        _dragAnnotationOriginalPoints.Clear();
        _dragAnnotationSelectionOriginalPoints.Clear();
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        if (wasChanged && beforeSelection.Count > 0)
        {
            PushGeometryUndoSnapshotFromOriginals(
                new Dictionary<Measurement, List<SKPoint>>(),
                beforeSelection,
                "move selected markups",
                "annotation-drag");
            foreach (PageAnnotation annotation in beforeSelection.Keys)
                PageAnnotationChanged?.Invoke(annotation);
        }
        else if (changed != null)
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

    private SKPoint ResolveVertexDragDelta(Point screen)
    {
        SKPoint rawDelta = ScreenDragDeltaToPdf(screen);
        SKPoint rawTarget = new(
            _dragVertexOriginalPoint.X + rawDelta.X,
            _dragVertexOriginalPoint.Y + rawDelta.Y);
        SKPoint resolved = ResolveConstrainedPoint(
            rawTarget,
            _dragVertexOriginalPoint,
            updatePreview: true,
            IsSelfVertexSnap);
        return new SKPoint(
            resolved.X - _dragVertexOriginalPoint.X,
            resolved.Y - _dragVertexOriginalPoint.Y);
    }

    private bool IsSelfVertexSnap(SKPoint snapped)
    {
        float tolerance = Math.Max(ViewportConstants.ZeroLengthEpsilon, ScreenToPdfDistance(2f));
        return DistanceSquared(snapped, _dragVertexOriginalPoint) <= tolerance * tolerance;
    }

    private bool IsAnnotationSelfVertexSnap(SKPoint snapped)
    {
        float tolerance = Math.Max(ViewportConstants.ZeroLengthEpsilon, ScreenToPdfDistance(2f));
        return DistanceSquared(snapped, _dragAnnotationVertexOriginalPoint) <= tolerance * tolerance;
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

}
