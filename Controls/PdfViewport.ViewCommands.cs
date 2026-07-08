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
    public void SetTool(string name)
    {
        _tool = name.ToLower() switch
        {
            "select" => ViewerTool.Select,
            "scale" => ViewerTool.Scale,
            "ruler" => ViewerTool.Ruler,
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
        CancelDrawing(clearSelection: _tool != ViewerTool.AreaCut);
        SetSnapPreview(null);
        UpdateCursor();
        PostRecordPrompt();
    }

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

    public bool BeginJoistDirectionCapture(Measurement areaMeasurement)
    {
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(areaMeasurement.MType) != "area")
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

    public void ZoomIn()  => ApplyZoom(1.25f, ViewportCanvasWidth / 2f, ViewportCanvasHeight / 2f);
    public void ZoomOut() => ApplyZoom(0.80f, ViewportCanvasWidth / 2f, ViewportCanvasHeight / 2f);
}
