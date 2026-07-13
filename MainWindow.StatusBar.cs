using System;
using System.Globalization;
using System.Windows;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    private void InitializeStatusBarSegments()
    {
        _takeoffSaveService.DirtyStateChanged += UpdateStatusBarSegments;
        UpdateStatusBarSegments();
    }

    private void UpdateStatusBarSegments()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(UpdateStatusBarSegments));
            return;
        }

        if (TxtStatusScale == null)
            return;

        TxtStatusScale.Text = StatusScaleText();
        TxtStatusZoom.Text = $"Zoom: {Math.Round(_viewport.Zoom * 100).ToString(CultureInfo.InvariantCulture)}%";
        TxtStatusCursor.Text = StatusCursorText();
        TxtStatusMode.Text = StatusModeText();
        TxtStatusTakeoff.Text = _activeItem == null ? "Takeoff: --" : $"Takeoff: {_activeItem.Name}";
        TxtStatusSave.Text = StatusSaveText();
    }

    private string StatusScaleText()
    {
        double scale = _currentPage?.ScaleMetersPerPt > 0
            ? _currentPage.ScaleMetersPerPt
            : _viewport.ScaleMetersPerPt;
        string formatted = PdfSheetMetadataService.FormatImperialScale(scale);
        return string.IsNullOrWhiteSpace(formatted) ? "Scale: --" : $"Scale: {formatted}";
    }

    private string StatusCursorText()
    {
        SKPoint? pointer = _viewport.LastPointerPdf;
        if (pointer == null || _currentPage == null)
            return "X --  Y --";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"X {pointer.Value.X:0.#}  Y {pointer.Value.Y:0.#}");
    }

    private string StatusModeText()
    {
        string title = _activeTool switch
        {
            "point" => "Count",
            "line" => "Line",
            "area" => "Area",
            "joistarea" => "J Area",
            "beam" => "Beam",
            "openings" => "Openings",
            "select" => "Select",
            "scale" => "Scale",
            "ruler" => "Ruler",
            "drawhighlight" => "Highlighter",
            "drawline" => "Draw Line",
            "drawarrow" => "Arrow",
            "drawrect" => "Box",
            "drawcloud" => "Cloud",
            "drawarea" => "Area Annot",
            "note" => "Note",
            "areacut" => "Area Cut",
            _ => "Pan",
        };
        return IsRecordTool(_activeTool) ? $"Mode: Record {title}" : $"Mode: {title}";
    }

    private string StatusSaveText()
    {
        if (_currentJob == null)
            return "Save: --";

        if (_takeoffSaveService.HasPending)
            return "Save: Pending";

        DateTime? flushed = _takeoffSaveService.LastFlushUtc;
        return flushed.HasValue
            ? $"Save: Saved {flushed.Value.ToLocalTime():HH:mm:ss}"
            : "Save: Saved";
    }
}
