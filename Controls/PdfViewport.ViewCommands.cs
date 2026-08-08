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
    public void SetTool(string name)
    {
        ViewerTool requestedTool = name.ToLower() switch
        {
            "select" => ViewerTool.Select,
            "scale" => ViewerTool.Scale,
            "ruler" => ViewerTool.Ruler,
            "pitch" => ViewerTool.Pitch,
            "beam" => ViewerTool.Beam,
            "openings" => ViewerTool.Openings,
            "drawhighlight" => ViewerTool.DrawHighlight,
            "drawline" => ViewerTool.DrawLine,
            "drawarrow" => ViewerTool.DrawArrow,
            "drawrect" => ViewerTool.DrawRect,
            "drawcloud" => ViewerTool.DrawCloud,
            "drawarea" => ViewerTool.DrawArea,
            "note" => ViewerTool.Note,
            "point" => ViewerTool.Point,
            "line"  => ViewerTool.Line,
            "area"  => ViewerTool.Area,
            "joistarea" => ViewerTool.Area,
            "areacut" => ViewerTool.AreaCut,
            _       => ViewerTool.Pan,
        };
        if (IsReadOnlyMode && requestedTool is not (ViewerTool.Pan or ViewerTool.Select))
        {
            PostStatus("Read-only: choose Pan or Select to inspect this job.");
            return;
        }

        CancelExtraJoistPlacement(postStatus: false);
        _tool = requestedTool;
        if (requestedTool is ViewerTool.Ruler or ViewerTool.Pitch or ViewerTool.DrawHighlight or ViewerTool.DrawLine or
            ViewerTool.DrawArrow or ViewerTool.DrawRect or ViewerTool.DrawCloud or ViewerTool.DrawArea or
            ViewerTool.Note)
        {
            _annotationSelectionDomain = true;
        }
        else if (requestedTool is ViewerTool.Beam or ViewerTool.Openings or ViewerTool.Point or
                 ViewerTool.Line or ViewerTool.Area or ViewerTool.AreaCut)
        {
            _annotationSelectionDomain = false;
        }
        CancelDrawing(clearSelection: _tool != ViewerTool.AreaCut);
        SetSnapPreview(null);
        UpdateCursor();
        PostRecordPrompt();
    }

    // True while a takeoff drawing tool (count/line/area) is armed — the
    // detached-sheet Space shortcut uses this to toggle record per window.
    public bool IsTakeoffRecordToolActive =>
        _tool is ViewerTool.Point or ViewerTool.Line or ViewerTool.Area;

    public SKRect GetVisiblePdfRect()
    {
        if (_pdfW <= 0 || _pdfH <= 0 || _zoom <= 0 || !HasViewportCanvasSize)
            return SKRect.Empty;

        var rect = new SKRect(
            _panX,
            _panY,
            _panX + ViewportCanvasWidth / _zoom,
            _panY + ViewportCanvasHeight / _zoom);
        float left = Math.Clamp(rect.Left, 0, Math.Max(0, _pdfW - 1));
        float top = Math.Clamp(rect.Top, 0, Math.Max(0, _pdfH - 1));
        float right = Math.Clamp(rect.Right, left + 1, _pdfW);
        float bottom = Math.Clamp(rect.Bottom, top + 1, _pdfH);
        return new SKRect(left, top, right, bottom);
    }

    public IReadOnlyList<Measurement> GetSelectedMeasurements() =>
        _selectedMeasurements
            .Where(m => _measurementSet.Contains(m) && IsMeasurementOnActivePage(m))
            .ToList();

    public IReadOnlyList<PointVertexSelection> GetSelectedPointVertexSelections()
    {
        var selections = GetSelectedMeasurements()
            .Where(measurement =>
                OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "point")
            .Select(measurement => new PointVertexSelection(
                measurement,
                Array.AsReadOnly(ActiveMeasurementVertexIndices(measurement)
                    .Where(index => index >= 0 && index < measurement.Points.Count)
                    .Distinct()
                    .OrderBy(index => index)
                    .ToArray())))
            .Where(selection => selection.PointIndices.Count > 0)
            .ToArray();

        return Array.AsReadOnly(selections);
    }

    public bool BeginJoistDirectionCapture(Measurement areaMeasurement)
    {
        CancelExtraJoistPlacement(postStatus: false);
        if (OurPlanCoreJobStore.NormalizeMeasurementType(areaMeasurement.MType) != "area")
        {
            PostStatus("Joist direction can only be set on Area measurements.");
            return false;
        }

        if (!_measurementSet.Contains(areaMeasurement))
        {
            PostStatus("Joist direction was not started: Area is not loaded in the viewport.");
            return false;
        }

        if (!IsSamePageFolder(areaMeasurement.PageFolder, _pageFolder))
        {
            PostStatus("Joist direction was not started: open the Area sheet and try again.");
            return false;
        }

        if (!IsMeasurementTakeoffVisible(areaMeasurement))
        {
            PostStatus("Joist direction was not started: this takeoff is hidden on the active sheet.");
            return false;
        }

        _joistDirectionMeasurement = areaMeasurement;
        _joistDirectionPts.Clear();
        _joistDirectionRubberEnd = null;
        CancelDrawing();
        SelectMeasurements([areaMeasurement]);
        PostStatus("Joist direction: click two points parallel to the joists. Esc cancels.");
        RequestRepaint();
        return true;
    }

    public void ZoomFit()
    {
        if (_pdfW <= 0 || ViewportCanvasWidth < 2 || ViewportCanvasHeight < 2) return;
        _zoom = Math.Min(ViewportCanvasWidth / _pdfW, ViewportCanvasHeight / _pdfH) * 0.95f;
        _panX = _panY = 0;
        ScheduleRerenderForZoom(force: true);
        MaybeRequestSheetOverlayRenderScaleRefresh();
        RequestRepaint();
        NotifyZoomChanged();
    }

    public void RefreshRenderQuality()
    {
        ClearDetailRender();
        ScheduleRerenderForZoom(force: true);
        RequestRepaint();
    }

    // Re-render the current page at the newly chosen static DPI (if higher than
    // what is displayed). Cheap no-op when the page is already at or above target.
    public void RefreshStaticRasterDpi() =>
        QueueStaticRasterDpiApplyIfNeeded();

    public void ZoomIn()  => ApplyZoom(1.25f, ViewportCanvasWidth / 2f, ViewportCanvasHeight / 2f);
    public void ZoomOut() => ApplyZoom(0.80f, ViewportCanvasWidth / 2f, ViewportCanvasHeight / 2f);
}
