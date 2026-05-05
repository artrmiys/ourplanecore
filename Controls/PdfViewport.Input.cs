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
using SmartTakeoffs;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace SmartTakeoffs.Controls;

public sealed partial class PdfViewport
{
    // ═════════════════════════════════════════════════════════════════════════
    // Mouse / keyboard events
    // ═════════════════════════════════════════════════════════════════════════

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Focus();
        var pos    = e.GetPosition(this);
        float fac  = e.Delta > 0 ? 1.12f : 1f / 1.12f;
        ApplyZoom(fac, (float)pos.X, (float)pos.Y);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        var pos = e.GetPosition(this);

        if (e.RightButton == MouseButtonState.Pressed && _pageBitmap != null)
        {
            var pdf = ScreenToPdf((float)pos.X, (float)pos.Y);
            _lastPointerPdf = pdf;
            _rightClickStart = pos;
            _rightClickPdf = pdf;
            _rightClickMoved = false;
            _rightClickMeasurement = null;
            if (TryHitMeasurement(pdf, out Measurement measurement))
            {
                _rightClickMeasurement = measurement;
                if (_selectedMeasurements.Contains(measurement))
                    SetSelectedMeasurements(GetSelectedMeasurements(), measurement, -1);
                else
                    SelectMeasurement(measurement, -1);
            }
        }

        if (e.LeftButton == MouseButtonState.Pressed &&
            _pageBitmap != null)
        {
            var pdf = ScreenToPdf((float)pos.X, (float)pos.Y);
            _lastPointerPdf = pdf;
            if (_joistDirectionMeasurement != null)
            {
                HandleJoistDirectionClick(ResolveDigitizerPoint(pdf, updatePreview: true));
                e.Handled = true;
                return;
            }

            if (_pdfLayerTraceEnabled)
            {
                AdvancePdfLayerTrace(pdf);
                e.Handled = true;
                return;
            }

            if (_tool == ViewerTool.Select)
            {
                bool hasInProgressInput = _drawPts.Count > 0 || _scalePts.Count > 0 || _rubberEnd.HasValue;
                if (IsSelectionModifierActive() && TryHitMeasurement(pdf, out Measurement toggled))
                {
                    ToggleMeasurementSelection(toggled);
                    e.Handled = true;
                    return;
                }

                bool preserveSelectionForAdd = IsSelectionModifierActive();
                if (TryBeginMeasurementEdit(pdf, pos, clearSelectionOnMiss: !hasInProgressInput && !preserveSelectionForAdd))
                {
                    if (_draggingVertex || _draggingMeasurement)
                        CaptureMouse();
                    e.Handled = true;
                    return;
                }

                BeginBoxSelection(pdf, additive: IsSelectionModifierActive());
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
            _isViewDragging = true;
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

            var pdf = ResolveDigitizerPoint(ScreenToPdf((float)pos.X, (float)pos.Y), updatePreview: true);
            ClearSelection();

            // Double-click (ClickCount==2) finishes a line/area without adding an extra point.
            // Single-click (ClickCount==1) adds a vertex as usual.
            if (e.ClickCount == 2 && _tool is ViewerTool.Line or ViewerTool.Area)
            {
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
        var pos = e.GetPosition(this);
        if (_rightClickStart.HasValue &&
            DistanceSquared(_rightClickStart.Value, pos) > 16)
        {
            _rightClickMoved = true;
        }

        if (_draggingVertex &&
            _selectedMeasurement != null &&
            _selectedVertexIndex >= 0)
        {
            SKPoint delta = ScreenDragDeltaToPdf(pos);
            _selectedMeasurement.Points[_selectedVertexIndex] = new SKPoint(
                _dragVertexOriginalPoint.X + delta.X,
                _dragVertexOriginalPoint.Y + delta.Y);
            _dragMeasurementChanged = true;
            PostDragStatus("Dragging vertex", delta);
            RequestRepaint();
            e.Handled = true;
            return;
        }

        if (_draggingMeasurement &&
            _selectedMeasurement != null)
        {
            SKPoint delta = ScreenDragDeltaToPdf(pos);
            if (_dragSelectionOriginalPoints.Count > 0)
            {
                foreach (var (measurement, originalPoints) in _dragSelectionOriginalPoints)
                {
                    for (int i = 0; i < measurement.Points.Count && i < originalPoints.Count; i++)
                    {
                        SKPoint original = originalPoints[i];
                        measurement.Points[i] = new SKPoint(original.X + delta.X, original.Y + delta.Y);
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
            }

            _dragMeasurementChanged = true;
            PostDragStatus("Dragging measurement", delta);
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
            _panX = _dragPanX0 - (float)((pos.X - _dragStart.Value.X) / _zoom);
            _panY = _dragPanY0 - (float)((pos.Y - _dragStart.Value.Y) / _zoom);
            RequestRepaint();
            e.Handled = true;
            return;
        }

        var pointerPdf = ScreenToPdf((float)pos.X, (float)pos.Y);
        _lastPointerPdf = pointerPdf;
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
            PostPdfLayerTraceStatus();
            e.Handled = true;
            return;
        }

        if (_pageBitmap != null &&
            _tool is ViewerTool.Scale or ViewerTool.Ruler or ViewerTool.DrawLine or ViewerTool.DrawArrow or ViewerTool.DrawRect or ViewerTool.Point or ViewerTool.Line or ViewerTool.Area &&
            !IsMissingScaleForLinearArea())
        {
            pointerPdf = ResolveDigitizerPoint(pointerPdf, updatePreview: true);
            _lastPointerPdf = pointerPdf;
        }
        else
        {
            SetSnapPreview(null);
        }

        // Rubber-band
        if (_drawPts.Count > 0 && _tool is ViewerTool.Line or ViewerTool.Area or ViewerTool.Ruler or ViewerTool.DrawLine or ViewerTool.DrawArrow or ViewerTool.DrawRect)
        {
            _rubberEnd = pointerPdf;
            RequestRepaint();
        }

        if (IsMissingScaleForLinearArea())
            PostScaleRequiredStatus();
        else
            PostPointerStatus(pointerPdf);

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        bool showContextMenu = e.ChangedButton == MouseButton.Right &&
                               _rightClickStart.HasValue &&
                               !_rightClickMoved &&
                               _rightClickPdf.HasValue &&
                               _pageBitmap != null;
        Point contextScreen = _rightClickStart ?? e.GetPosition(this);
        SKPoint contextPdf = _rightClickPdf ?? ScreenToPdf((float)contextScreen.X, (float)contextScreen.Y);
        Measurement? contextMeasurement = _rightClickMeasurement;

        if (_draggingVertex || _draggingMeasurement)
        {
            FinishMeasurementDrag();
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
            _isViewDragging = false;
            ReleaseMouseCapture();
            RequestRepaint();
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            _rightClickStart = null;
            _rightClickPdf = null;
            _rightClickMeasurement = null;
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
                contextMeasurement));
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
        FinishMeasurementDrag();
        if (_boxSelecting && Mouse.LeftButton != MouseButtonState.Pressed)
            CancelBoxSelection();
        base.OnLostMouseCapture(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_pdfLayerTraceEnabled && e.Key == Key.Tab)
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
        switch (e.Key)
        {
            case Key.Escape:
                if (_pdfLayerTraceEnabled)
                {
                    if (_pdfLayerTraceChoosingLayer || _pdfLayerTraceReadyToApply)
                    {
                        ClearPdfLayerTraceSession(keepCandidateLayer: true);
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
                    AdvancePdfLayerTrace(_lastPointerPdf);
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
                    CopyMeasurementsRequested?.Invoke(GetSelectedMeasurements());
                    e.Handled = true;
                }
                else if (_drawPts.Count > 0 && _tool is ViewerTool.Line or ViewerTool.Area)
                {
                    CompleteOrCancelDrawing();
                    e.Handled = true;
                }
                break;
            case Key.V when Keyboard.Modifiers == ModifierKeys.Control:
                PasteMeasurementsRequested?.Invoke(_lastPointerPdf);
                e.Handled = true;
                break;
            case Key.Delete:
                DeleteSelectedMeasurement();
                e.Handled = true;
                break;
            case Key.F:
                ZoomFit();
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
                SelectAllActivePageMeasurements();
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
            case Key.D: ToolChanged?.Invoke("drawline"); e.Handled = true; break;
            case Key.B: ToolChanged?.Invoke("drawrect"); e.Handled = true; break;
            case Key.P: ToolChanged?.Invoke("point"); e.Handled = true; break;
            case Key.L: ToolChanged?.Invoke("line");  e.Handled = true; break;
            case Key.A: ToolChanged?.Invoke("area");  e.Handled = true; break;
        }
    }


    private void UpdateCursor()
    {
        Cursor = _tool switch
        {
            ViewerTool.Pan => Cursors.Hand,
            ViewerTool.Select => Cursors.Arrow,
            _              => Cursors.Cross,
        };
    }

}
