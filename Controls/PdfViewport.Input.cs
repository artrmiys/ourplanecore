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
    // ═════════════════════════════════════════════════════════════════════════
    // Mouse / keyboard events
    // ═════════════════════════════════════════════════════════════════════════

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Focus();
        var pos    = ViewPointToCanvas(e.GetPosition(this));
        float step = (float)Math.Clamp(ZoomWheelFactor, 1.01, 4.0);
        float fac  = e.Delta > 0 ? step : 1f / step;
        ApplyZoom(fac, (float)pos.X, (float)pos.Y);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        var pos = ViewPointToCanvas(e.GetPosition(this));

        if (e.RightButton == MouseButtonState.Pressed && _pageBitmap != null)
        {
            var pdf = ScreenToPdf((float)pos.X, (float)pos.Y);
            _lastPointerPdf = pdf;
            _rightClickStart = pos;
            _rightClickPdf = pdf;
            _rightClickMoved = false;
            _rightClickMeasurement = null;
            _rightClickAnnotation = null;
            _rightClickOverlayHit = HitSheetOverlay(pos);
            if (_rightClickOverlayHit != ViewportOverlayHitKind.None)
            {
                ClearSelection();
                ClearAnnotationSelection();
            }
            else
            {
                PageAnnotation? preferredAnnotation = null;
                if (_annotationSelectionDomain)
                    TryHitAnnotation(pdf, out preferredAnnotation);

                if (preferredAnnotation != null)
                {
                    _rightClickAnnotation = preferredAnnotation;
                    if (_selectedAnnotations.Contains(preferredAnnotation))
                        SetSelectedAnnotations(GetSelectedAnnotations(), preferredAnnotation, -1);
                    else
                        SelectAnnotation(preferredAnnotation, -1);
                }
                else if (TryHitMeasurement(pdf, out Measurement measurement))
                {
                    _rightClickMeasurement = measurement;
                    if (_selectedMeasurements.Contains(measurement))
                        SetSelectedMeasurements(GetSelectedMeasurements(), measurement, -1);
                    else
                        SelectMeasurement(measurement, -1);
                }
                else if (TryHitAnnotation(pdf, out PageAnnotation annotation))
                {
                    _rightClickAnnotation = annotation;
                    if (_selectedAnnotations.Contains(annotation))
                        SetSelectedAnnotations(GetSelectedAnnotations(), annotation, -1);
                    else
                        SelectAnnotation(annotation, -1);
                }
            }
        }

        if (e.LeftButton == MouseButtonState.Pressed &&
            _pageBitmap != null)
        {
            var pdf = ScreenToPdf((float)pos.X, (float)pos.Y);
            _lastPointerPdf = pdf;
            if (HandleReadOnlyLeftMouseDown(pdf))
            {
                e.Handled = true;
                return;
            }

            if (HandleAiCropNoteMouseDown(pdf))
            {
                e.Handled = true;
                return;
            }

            if (HandleSimilarCountMouseDown(pdf))
            {
                e.Handled = true;
                return;
            }

            if (HandleSheetOverlayPointEditClick(pdf))
            {
                e.Handled = true;
                return;
            }

            if (TryBeginSheetOverlayDrag(pdf))
            {
                e.Handled = true;
                return;
            }

            if (HandleThreeDRoofEdgeSelectionClick(pdf))
            {
                e.Handled = true;
                return;
            }

            if (HandleThreeDRoofClick(pdf))
            {
                e.Handled = true;
                return;
            }

            if (_joistDirectionMeasurement != null)
            {
                HandleJoistDirectionClick(ResolveDigitizerPoint(pdf, updatePreview: true));
                e.Handled = true;
                return;
            }

            if (_pdfLayerTraceEnabled)
            {
                _ = AdvancePdfLayerTraceAsync(pdf);
                e.Handled = true;
                return;
            }

            if (_tool == ViewerTool.Select)
            {
                bool hasInProgressInput = _drawPts.Count > 0 || _scalePts.Count > 0 || _rubberEnd.HasValue;

                // Alt operates on the selected Line/Area object's vertices.
                // Click or box toggles handles; empty Alt-click clears handles.
                if (IsVertexModifierActive() && HasEditableMeasurementSelection())
                {
                    if (TryBeginVertexEdit(pdf, pos))
                    {
                        if (_draggingVertex)
                            CaptureMouse();
                        e.Handled = true;
                        return;
                    }

                    BeginVertexBoxSelection(pdf);
                    e.Handled = true;
                    return;
                }

                bool selectionModifierActive = IsSelectionModifierActive();
                bool deselectModifierActive = IsDeselectModifierActive();
                if (selectionModifierActive)
                {
                    if (TrySelectCutRegionAt(pdf, deselectModifierActive ? HoleSelectMode.Remove : HoleSelectMode.Add))
                    {
                        e.Handled = true;
                        return;
                    }

                    if (HasEditableMeasurementSelection())
                    {
                        BeginBoxSelection(pdf, additive: true, removeMode: deselectModifierActive);
                        e.Handled = true;
                        return;
                    }

                    if (_annotationSelectionDomain &&
                        TryHitAnnotation(pdf, out PageAnnotation preferredAnnotation))
                    {
                        ApplyAnnotationClickGesture(preferredAnnotation, deselectModifierActive);
                        e.Handled = true;
                        return;
                    }

                    if (TryHitMeasurement(pdf, out Measurement toggled))
                    {
                        ToggleMeasurementSelection(toggled);
                        e.Handled = true;
                        return;
                    }

                    if (TryHitAnnotation(pdf, out PageAnnotation toggledAnnotation))
                    {
                        ApplyAnnotationClickGesture(toggledAnnotation, deselectModifierActive);
                        e.Handled = true;
                        return;
                    }
                }

                bool preserveSelectionForAdd = selectionModifierActive;
                if (TryBeginTransformHandleEdit(pdf, pos))
                {
                    e.Handled = true;
                    return;
                }

                if (!TryHitVertex(pdf, out _, out _) &&
                    TrySelectCutRegionAt(pdf, HoleSelectMode.Replace))
                {
                    e.Handled = true;
                    return;
                }

                if (e.ClickCount == 2 && TryEditNoteAnnotationAt(pdf))
                {
                    e.Handled = true;
                    return;
                }

                if (e.ClickCount == 2 && TryRequestTakeoffRenameAt(pdf))
                {
                    e.Handled = true;
                    return;
                }

                bool clearSelectionOnMiss = !hasInProgressInput && !preserveSelectionForAdd;
                bool beganEdit = _annotationSelectionDomain
                    ? TryBeginAnnotationEdit(pdf, pos, clearSelectionOnMiss: false) ||
                      TryBeginMeasurementEdit(pdf, pos, clearSelectionOnMiss)
                    : TryBeginMeasurementEdit(pdf, pos, clearSelectionOnMiss: false) ||
                      TryBeginAnnotationEdit(pdf, pos, clearSelectionOnMiss);
                if (beganEdit)
                {
                    if (_draggingVertex || _draggingMeasurement ||
                        _draggingAnnotationVertex || _draggingAnnotation)
                        CaptureMouse();
                    e.Handled = true;
                    return;
                }

                BeginBoxSelection(pdf, additive: IsSelectionModifierActive(), removeMode: deselectModifierActive);
                e.Handled = true;
                return;
            }
        }

        bool isPanButton = e.MiddleButton == MouseButtonState.Pressed
                        || e.RightButton  == MouseButtonState.Pressed
                        || (_tool == ViewerTool.Pan && e.LeftButton == MouseButtonState.Pressed);

        if (isPanButton)
        {
            _dragStart  = pos;
            _dragPanX0  = _panX;
            _dragPanY0  = _panY;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed && _pageBitmap != null)
        {
            if (!EnsureScaleForLinearArea())
            {
                e.Handled = true;
                return;
            }

            SKPoint rawPdf = ScreenToPdf((float)pos.X, (float)pos.Y);
            if (!HasActiveOrthoConstraint() && TryCommitEdgeSnapPreview(rawPdf))
            {
                e.Handled = true;
                return;
            }

            var pdf = ResolveDigitizerPoint(rawPdf, updatePreview: true);
            if (_tool != ViewerTool.AreaCut)
                ClearSelection();

            // Double-click (ClickCount==2) finishes a line/area without adding an extra point.
            // Single-click (ClickCount==1) adds a vertex as usual.
            if (e.ClickCount == 2 &&
                (_tool is ViewerTool.Line or ViewerTool.Area or ViewerTool.DrawLine or ViewerTool.DrawArea ||
                 _tool == ViewerTool.AreaCut && !BoxModeEnabled))
            {
                if (_tool == ViewerTool.AreaCut)
                    FinalizeAreaCutPolygon();
                else if (_tool == ViewerTool.DrawLine)
                    FinalizeAnnotation("line", useAllPoints: true);
                else if (_tool == ViewerTool.DrawArea)
                    FinalizeAreaAnnotation();
                else
                    FinalizeDrawing();
                e.Handled = true;
                return;
            }

            HandleLeftClick(pdf);
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = ViewPointToCanvas(e.GetPosition(this));
        if (_pageBitmap != null)
        {
            _cursorGuideVisible = true;
            SetLastPointerPdf(ScreenToPdf((float)pos.X, (float)pos.Y));
        }

        if (_rightClickStart.HasValue &&
            DistanceSquared(_rightClickStart.Value, pos) > 16)
        {
            _rightClickMoved = true;
        }

        if (HandleAiCropNoteMouseMove(pos))
        {
            e.Handled = true;
            return;
        }

        if (HandleSimilarCountMouseMove(pos))
        {
            e.Handled = true;
            return;
        }

        if (TryUpdateSheetOverlayDrag(ScreenToPdf((float)pos.X, (float)pos.Y)))
        {
            e.Handled = true;
            return;
        }

        if (_draggingTransformScale || _draggingTransformRotate)
        {
            UpdateTransformDrag(ScreenToPdf((float)pos.X, (float)pos.Y));
            e.Handled = true;
            return;
        }

        if (_draggingVertex &&
            _selectedMeasurement != null &&
            _selectedVertexIndex >= 0)
        {
            SKPoint delta = ResolveVertexDragDelta(pos);
            if (_dragMeasurementVertexOriginalPoints.Count > 0)
            {
                foreach (var (measurement, originalPoints) in _dragMeasurementVertexOriginalPoints)
                {
                    foreach (var (index, original) in originalPoints)
                    {
                        if (!IsValidMeasurementVertexIndex(measurement, index))
                            continue;

                        SetMeasurementVertex(measurement, index, new SKPoint(
                            original.X + delta.X,
                            original.Y + delta.Y));
                    }
                }
            }
            else
            {
                SetMeasurementVertex(_selectedMeasurement, _selectedVertexIndex, new SKPoint(
                    _dragVertexOriginalPoint.X + delta.X,
                    _dragVertexOriginalPoint.Y + delta.Y));
            }

            _dragMeasurementChanged = true;
            PostDragStatus(DraggedVertexCount() > 1 ? "Dragging vertices" : "Dragging vertex", delta);
            RequestRepaint();
            e.Handled = true;
            return;
        }

        if (_draggingMeasurement &&
            _selectedMeasurement != null)
        {
            SKPoint delta = ConstrainDragDeltaOrtho(ScreenDragDeltaToPdf(pos));
            if (_dragSelectionOriginalPoints.Count > 0)
            {
                foreach (var (measurement, originalPoints) in _dragSelectionOriginalPoints)
                {
                    for (int i = 0; i < measurement.Points.Count && i < originalPoints.Count; i++)
                    {
                        SKPoint original = originalPoints[i];
                        measurement.Points[i] = new SKPoint(original.X + delta.X, original.Y + delta.Y);
                    }

                    if (_dragSelectionOriginalHoles.TryGetValue(measurement, out var originalHoles))
                    {
                        RestoreTransformedHoles(
                            measurement.Holes,
                            originalHoles,
                            point => new SKPoint(point.X + delta.X, point.Y + delta.Y));
                    }
                }
            }
            else
            {
                for (int i = 0; i < _selectedMeasurement.Points.Count && i < _dragMeasurementOriginalPoints.Count; i++)
                {
                    SKPoint original = _dragMeasurementOriginalPoints[i];
                    _selectedMeasurement.Points[i] = new SKPoint(original.X + delta.X, original.Y + delta.Y);
                }

                RestoreTransformedHoles(
                    _selectedMeasurement.Holes,
                    _dragMeasurementOriginalHoles,
                    point => new SKPoint(point.X + delta.X, point.Y + delta.Y));
            }

            _dragMeasurementChanged = true;
            PostDragStatus("Dragging measurement", delta);
            RequestRepaint();
            e.Handled = true;
            return;
        }

        if (_draggingAnnotationVertex &&
            _selectedAnnotation != null &&
            _selectedAnnotationVertexIndex >= 0)
        {
            SKPoint rawDelta = ScreenDragDeltaToPdf(pos);
            SKPoint rawTarget = new(
                _dragAnnotationVertexOriginalPoint.X + rawDelta.X,
                _dragAnnotationVertexOriginalPoint.Y + rawDelta.Y);
            SKPoint resolved = ResolveConstrainedPoint(
                rawTarget,
                _dragAnnotationVertexOriginalPoint,
                updatePreview: true,
                IsAnnotationSelfVertexSnap);
            SKPoint delta = new(
                resolved.X - _dragAnnotationVertexOriginalPoint.X,
                resolved.Y - _dragAnnotationVertexOriginalPoint.Y);
            _selectedAnnotation.Points[_selectedAnnotationVertexIndex] = resolved;
            _dragAnnotationChanged = true;
            PostDragStatus("Dragging markup point", delta);
            RequestRepaint();
            e.Handled = true;
            return;
        }

        if (_draggingAnnotation &&
            _selectedAnnotation != null)
        {
            SKPoint delta = ConstrainDragDeltaOrtho(ScreenDragDeltaToPdf(pos));
            if (_dragAnnotationSelectionOriginalPoints.Count > 0)
            {
                foreach (var (annotation, originalPoints) in _dragAnnotationSelectionOriginalPoints)
                {
                    for (int i = 0; i < annotation.Points.Count && i < originalPoints.Count; i++)
                    {
                        SKPoint original = originalPoints[i];
                        annotation.Points[i] = new SKPoint(original.X + delta.X, original.Y + delta.Y);
                    }
                }
            }
            else
            {
                for (int i = 0; i < _selectedAnnotation.Points.Count && i < _dragAnnotationOriginalPoints.Count; i++)
                {
                    SKPoint original = _dragAnnotationOriginalPoints[i];
                    _selectedAnnotation.Points[i] = new SKPoint(original.X + delta.X, original.Y + delta.Y);
                }
            }

            _dragAnnotationChanged = true;
            PostDragStatus(
                _dragAnnotationSelectionOriginalPoints.Count > 0 ? "Dragging markups" : "Dragging markup",
                delta);
            RequestRepaint();
            e.Handled = true;
            return;
        }

        if (_boxSelecting)
        {
            _boxSelectEndPdf = ScreenToPdf((float)pos.X, (float)pos.Y);
            _lastPointerPdf = _boxSelectEndPdf;
            PostBoxSelectionStatus();
            RequestRepaint();
            e.Handled = true;
            return;
        }

        if (_dragStart.HasValue && (
            e.MiddleButton == MouseButtonState.Pressed ||
            e.RightButton  == MouseButtonState.Pressed ||
            (_tool == ViewerTool.Pan && e.LeftButton == MouseButtonState.Pressed)))
        {
            BeginFastNavigation();
            _panX = _dragPanX0 - ScreenToPdfDistance((float)(pos.X - _dragStart.Value.X));
            _panY = _dragPanY0 - ScreenToPdfDistance((float)(pos.Y - _dragStart.Value.Y));
            RequestRepaint();
            e.Handled = true;
            return;
        }

        var pointerPdf = ScreenToPdf((float)pos.X, (float)pos.Y);
        _lastPointerPdf = pointerPdf;
        if (IsSheetOverlayPointEditing)
        {
            UpdateSheetOverlayPointEditPreview(pointerPdf);
            RequestRepaint();
            e.Handled = true;
            return;
        }

        if (_joistDirectionMeasurement != null)
        {
            pointerPdf = ResolveDigitizerPoint(pointerPdf, updatePreview: true);
            _lastPointerPdf = pointerPdf;
            if (_joistDirectionPts.Count > 0)
            {
                _joistDirectionRubberEnd = pointerPdf;
                RequestRepaint();
            }
            PostStatus(_joistDirectionPts.Count == 0
                ? "Joist direction: click the first point."
                : "Joist direction: click the second point.");
            e.Handled = true;
            return;
        }

        if (_pdfLayerTraceEnabled)
        {
            SetSnapPreview(null);
            UpdatePdfLayerTraceHover(pointerPdf);
            e.Handled = true;
            return;
        }

        if (UpdateThreeDRoofPointer(pointerPdf))
        {
            e.Handled = true;
            return;
        }

        SKPoint rawPointerPdf = pointerPdf;
        if (_pageBitmap != null &&
            _tool is ViewerTool.Scale or ViewerTool.Ruler or ViewerTool.Beam or ViewerTool.Openings or ViewerTool.DrawHighlight or ViewerTool.DrawLine or ViewerTool.DrawArrow or ViewerTool.DrawRect or ViewerTool.DrawCloud or ViewerTool.DrawArea or ViewerTool.Point or ViewerTool.Line or ViewerTool.Area or ViewerTool.AreaCut &&
            !IsMissingScaleForLinearArea())
        {
            pointerPdf = ResolveDigitizerPoint(pointerPdf, updatePreview: true);
            _lastPointerPdf = pointerPdf;
            if (HasActiveOrthoConstraint())
                ClearEdgeSnapPreview();
            else
                UpdateEdgeSnapPreview(rawPointerPdf);
        }
        else
        {
            SetSnapPreview(null);
            ClearEdgeSnapPreview();
        }

        // Rubber-band
        if (_drawPts.Count > 0 && _tool is ViewerTool.Line or ViewerTool.Area or ViewerTool.Ruler or ViewerTool.Beam or ViewerTool.Openings or ViewerTool.DrawHighlight or ViewerTool.DrawLine or ViewerTool.DrawArrow or ViewerTool.DrawRect or ViewerTool.DrawCloud or ViewerTool.DrawArea or ViewerTool.AreaCut)
        {
            _rubberEnd = pointerPdf;
            RequestRepaint();
        }

        if (IsMissingScaleForLinearArea())
            PostScaleRequiredStatus();

        UpdateCursor(pointerPdf);
        RequestPointerMoveRepaint();
        e.Handled = true;
    }

    private void RequestPointerMoveRepaint()
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastPointerRepaintAt).TotalMilliseconds < ViewportRenderPolicy.PointerMoveRepaintMinIntervalMs)
            return;

        _lastPointerRepaintAt = now;
        RequestRepaint();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _cursorGuideVisible = false;
        ClearSheetOverlayPointEditSnapPreview();
        ClearEdgeSnapPreview();
        RequestRepaint();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        bool showContextMenu = e.ChangedButton == MouseButton.Right &&
                               _rightClickStart.HasValue &&
                               !_rightClickMoved &&
                               _rightClickPdf.HasValue &&
                               _pageBitmap != null &&
                               !_aiCropNoteSelecting;
        Point contextScreen = _rightClickStart ?? ViewPointToCanvas(e.GetPosition(this));
        SKPoint contextPdf = _rightClickPdf ?? ScreenToPdf((float)contextScreen.X, (float)contextScreen.Y);
        Measurement? contextMeasurement = _rightClickMeasurement;
        PageAnnotation? contextAnnotation = _rightClickAnnotation;
        ViewportOverlayHitKind contextOverlayHit = _rightClickOverlayHit;

        if (e.ChangedButton == MouseButton.Left && FinishAiCropNoteSelection())
        {
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && FinishSimilarCountSelection())
        {
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && FinishSheetOverlayDrag())
        {
            e.Handled = true;
            return;
        }

        if (_draggingTransformScale || _draggingTransformRotate)
        {
            FinishTransformDrag();
            e.Handled = true;
            return;
        }

        if (_draggingVertex || _draggingMeasurement)
        {
            FinishMeasurementDrag();
            e.Handled = true;
            return;
        }

        if (_draggingAnnotationVertex || _draggingAnnotation)
        {
            FinishAnnotationDrag();
            e.Handled = true;
            return;
        }

        if (_boxSelecting && e.ChangedButton == MouseButton.Left)
        {
            FinishBoxSelection();
            e.Handled = true;
            return;
        }

        if (_dragStart.HasValue &&
            e.MiddleButton != MouseButtonState.Pressed &&
            e.RightButton  != MouseButtonState.Pressed &&
            e.LeftButton   != MouseButtonState.Pressed)
        {
            _dragStart = null;
            ReleaseMouseCapture();
            EndFastNavigation();
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            _rightClickStart = null;
            _rightClickPdf = null;
            _rightClickMeasurement = null;
            _rightClickAnnotation = null;
            _rightClickOverlayHit = ViewportOverlayHitKind.None;
            _rightClickMoved = false;
        }

        if (showContextMenu)
        {
            ContextRequested?.Invoke(new ViewportContextRequest(
                contextScreen.X,
                contextScreen.Y,
                contextPdf.X,
                contextPdf.Y,
                _pageFolder,
                contextMeasurement,
                contextAnnotation,
                contextOverlayHit));
        }
        e.Handled = true;
    }

    private void CancelJoistDirectionCapture()
    {
        if (_joistDirectionMeasurement == null && _joistDirectionPts.Count == 0)
            return;

        _joistDirectionMeasurement = null;
        _joistDirectionPts.Clear();
        _joistDirectionRubberEnd = null;
        SetSnapPreview(null);
        RequestRepaint();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        FinishTransformDrag();
        FinishMeasurementDrag();
        FinishAnnotationDrag();
        if (Mouse.LeftButton != MouseButtonState.Pressed)
            FinishSheetOverlayDrag();
        else
            CancelSheetOverlayDrag(silent: true);
        if (_aiCropNoteSelecting && Mouse.LeftButton != MouseButtonState.Pressed)
            CancelAiCropNoteSelection(postStatus: false);
        if (_similarCountSelecting && _similarCountDragging && Mouse.LeftButton != MouseButtonState.Pressed)
            CancelSimilarCountSelection(postStatus: false);
        if (_boxSelecting && Mouse.LeftButton != MouseButtonState.Pressed)
            CancelBoxSelection();
        if (_dragStart.HasValue && Mouse.MiddleButton != MouseButtonState.Pressed &&
            Mouse.RightButton != MouseButtonState.Pressed &&
            Mouse.LeftButton != MouseButtonState.Pressed)
        {
            _dragStart = null;
            EndFastNavigation();
        }
        base.OnLostMouseCapture(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        Key key = KeyboardShortcutKeys.EffectiveKey(e);
        if (key == Key.Tab && TryCycleEdgeSnapPreview())
        {
            e.Handled = true;
            return;
        }

        if (_pdfLayerTraceEnabled && key == Key.Tab)
        {
            if (_pdfLayerTraceChoosingLayer)
                CyclePdfLayerTraceCandidate();
            else
                CyclePdfLayerTraceMode();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (HandleReadOnlyKeyDown(e))
            return;

        if (HandleThreeDRoofKey(e))
            return;

        if (TryHandleSheetOverlayTransformShortcut(e))
            return;

        Key key = KeyboardShortcutKeys.EffectiveKey(e);
        switch (key)
        {
            case Key.Escape:
                if (CancelSheetOverlayDrag())
                {
                    e.Handled = true;
                    break;
                }

                if (IsSheetOverlayPointEditing)
                {
                    CancelSheetOverlayPointEdit();
                    e.Handled = true;
                    break;
                }

                if (CancelAiCropNoteSelection())
                {
                    e.Handled = true;
                    break;
                }

                if (CancelSimilarCountSelection())
                {
                    e.Handled = true;
                    break;
                }

                if (_pdfLayerTraceEnabled)
                {
                    if (_pdfLayerTraceChoosingLayer || _pdfLayerTraceReadyToApply)
                    {
                        ClearPdfLayerTraceSession();
                        PublishPdfLayerTraceState();
                        PostPdfLayerTraceStatus();
                    }
                    else
                    {
                        SetPdfLayerTraceEnabled(false);
                    }
                    e.Handled = true;
                    break;
                }

                if (_joistDirectionMeasurement != null)
                {
                    CancelJoistDirectionCapture();
                    PostStatus("Joist direction cancelled.");
                    e.Handled = true;
                    break;
                }

                CompleteOrCancelDrawing();
                e.Handled = true;
                break;
            case Key.Enter:
                if (_pdfLayerTraceEnabled)
                {
                    _ = AdvancePdfLayerTraceAsync(_lastPointerPdf);
                    e.Handled = true;
                }
                break;
            case Key.T when Keyboard.Modifiers == ModifierKeys.None:
                TogglePdfLayerTraceEnabled();
                e.Handled = true;
                break;
            case Key.C:
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    if (!CopySelectedCutRegions())
                    {
                        _holeClipboard.Clear();
                        if (!CopySelectedPageAnnotations())
                        {
                            IReadOnlyList<Measurement> selectedMeasurements = GetSelectedMeasurements();
                            if (selectedMeasurements.Count > 0)
                            {
                                MarkMeasurementClipboardCurrent();
                                CopyMeasurementsRequested?.Invoke(selectedMeasurements);
                            }
                            else
                            {
                                PostStatus("Select measurements or markups before copying.");
                            }
                        }
                    }
                    e.Handled = true;
                }
                else if (_drawPts.Count > 0 &&
                         (_tool is ViewerTool.Line or ViewerTool.Area or ViewerTool.DrawLine or ViewerTool.DrawArea ||
                          _tool == ViewerTool.AreaCut))
                {
                    CompleteOrCancelDrawing();
                    e.Handled = true;
                }
                break;
            case Key.V when Keyboard.Modifiers == ModifierKeys.Control:
                if (IsCutRegionClipboardCurrent)
                    PasteCutRegions(_lastPointerPdf);
                else if (IsAnnotationClipboardCurrent)
                    PasteCopiedPageAnnotations(_lastPointerPdf);
                else
                    PasteMeasurementsRequested?.Invoke(_lastPointerPdf);
                e.Handled = true;
                break;
            case Key.Delete:
                DeleteSelectedOverlay();
                e.Handled = true;
                break;
            case Key.F2 when Keyboard.Modifiers == ModifierKeys.None:
                RequestSelectedTakeoffRename();
                e.Handled = true;
                break;
            case Key.F:
                ZoomFit();
                e.Handled = true;
                break;
            case Key.F3 when Keyboard.Modifiers == ModifierKeys.Control:
                PdfSnapEnabled = !PdfSnapEnabled;
                e.Handled = true;
                break;
            case Key.F3:
                SnapEnabled = !SnapEnabled;
                e.Handled = true;
                break;
            case Key.F8:
                OrthoEnabled = !OrthoEnabled;
                e.Handled = true;
                break;
            case Key.F9:
                BoxModeEnabled = !BoxModeEnabled;
                e.Handled = true;
                break;
            case Key.Add when Keyboard.Modifiers == ModifierKeys.Control:
            case Key.OemPlus when Keyboard.Modifiers == ModifierKeys.Control:
                ZoomIn(); e.Handled = true;
                break;
            case Key.Subtract when Keyboard.Modifiers == ModifierKeys.Control:
            case Key.OemMinus  when Keyboard.Modifiers == ModifierKeys.Control:
                ZoomOut(); e.Handled = true;
                break;
            case Key.Z when Keyboard.Modifiers == ModifierKeys.Control:
                UndoLast(); e.Handled = true;
                break;
            case Key.A when Keyboard.Modifiers == ModifierKeys.Control:
                SelectAllActivePageObjects();
                e.Handled = true;
                break;
            case Key.Back:
                UndoLast(); e.Handled = true;
                break;
            // Tool hotkeys
            case Key.V: ToolChanged?.Invoke("pan");   e.Handled = true; break;
            case Key.E: ToolChanged?.Invoke("select"); e.Handled = true; break;
            case Key.S: ToolChanged?.Invoke("scale"); e.Handled = true; break;
            case Key.R: ToolChanged?.Invoke("ruler"); e.Handled = true; break;
            case Key.H: ToolChanged?.Invoke("drawhighlight"); e.Handled = true; break;
            case Key.D: ToolChanged?.Invoke("drawline"); e.Handled = true; break;
            case Key.B: ToolChanged?.Invoke("beam"); e.Handled = true; break;
            case Key.O: ToolChanged?.Invoke("openings"); e.Handled = true; break;
            case Key.N: ToolChanged?.Invoke("note"); e.Handled = true; break;
            case Key.P: ToolChanged?.Invoke("point"); e.Handled = true; break;
            case Key.L: ToolChanged?.Invoke("line");  e.Handled = true; break;
            case Key.A: ToolChanged?.Invoke("area");  e.Handled = true; break;
            case Key.J: ToolChanged?.Invoke("joistarea"); e.Handled = true; break;
            case Key.X: ToolChanged?.Invoke("areacut"); e.Handled = true; break;
        }
    }

    private bool TryRequestTakeoffRenameAt(SKPoint pdf)
    {
        if (TryHitSelectedMeasurement(pdf, out Measurement selectedMeasurement) ||
            TryHitMeasurement(pdf, out selectedMeasurement))
        {
            SelectMeasurement(selectedMeasurement, -1);
            TakeoffRenameRequested?.Invoke(selectedMeasurement);
            return true;
        }

        return false;
    }

    private void RequestSelectedTakeoffRename()
    {
        Measurement? measurement = GetSelectedMeasurements().LastOrDefault();
        TakeoffRenameRequested?.Invoke(measurement);
    }


    private void UpdateCursor(SKPoint? pdf = null)
    {
        if (_tool == ViewerTool.Select &&
            pdf.HasValue &&
            (TryHitVertexOnSelectedMeasurement(pdf.Value, out _, out _) ||
             TryHitEditableVertex(pdf.Value, out Measurement pointVertexMeasurement, out _) &&
             pointVertexMeasurement.MType == "point"))
        {
            Cursor = Cursors.Cross;
            return;
        }

        Cursor = _tool switch
        {
            ViewerTool.Pan => Cursors.Hand,
            ViewerTool.Select => Cursors.Arrow,
            _              => Cursors.Cross,
        };
    }

}
