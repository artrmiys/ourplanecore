using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    // Takeoff target bar, tool status, and type labels.

    private string CurrentToolMeasurementType() =>
        IsRecordTool(_activeTool) ? RecordMeasurementType(_activeTool) :
        IsRecordTool(_lastDrawingTool) ? RecordMeasurementType(_lastDrawingTool) :
        "line";

    private void UpdateToolStatus()
    {
        string title = _activeTool switch
        {
            "point" => MeasurementTypeDisplay("point"),
            "line" => MeasurementTypeDisplay("line"),
            "area" => MeasurementTypeDisplay("area"),
            "joistarea" => "J Area",
            "beam" => "Beam",
            "openings" => "Openings",
            "select" => "Select",
            "scale" => "Scale",
            "ruler" => "Ruler",
            "pitch" => "Pitch",
            "drawhighlight" => "Highlighter",
            "drawline" => "Draw Line",
            "drawarrow" => "Arrow",
            "drawrect" => "Box",
            "drawcloud" => "Cloud",
            "drawarea" => "Area Annotation",
            "note" => "Note",
            "areacut" => "Area Cut",
            _ => "Pan",
        };
        bool recording = IsRecordTool(_activeTool);
        string item = recording && _activeItem != null
            ? $"  |  Item: {_activeItem.Name}"
            : "";
        TxtTool.Text =
            $"  Tool: {title}  |  Record: {(recording ? "On" : "Off")}" +
            $"  |  Snap: {(_viewport.SnapEnabled ? "On" : "Off")}" +
            $"  |  PDF Snap: {(_viewport.PdfSnapEnabled ? "On" : "Off")}" +
            $"  |  Ortho: {(_viewport.OrthoEnabled ? "On" : "Off")}" +
            $"  |  Box: {(_viewport.BoxModeEnabled ? "On" : "Off")}{item}";
        UpdateActiveTakeoffTargetBar();
        UpdateStatusBarSegments();
    }

    private void UpdateActiveTakeoffTargetBar()
    {
        if (ActiveTakeoffTargetBar == null)
            return;

        ActiveTakeoffTargetBar.Visibility = Visibility.Visible;
        if (_activeItem == null)
        {
            TxtActiveTakeoffTarget.Text = "";
            TxtActiveTakeoffTargetMeta.Text = "";
            ActiveTakeoffTargetGlyphHost.Child = null;
            BtnActiveTakeoffRecord.Content = "Record";
            BtnActiveTakeoffRecord.ToolTip = "Select a takeoff item before recording (Space)";
            BtnActiveTakeoffMore.ToolTip = "Select a takeoff item for actions";
            BtnActiveTakeoffSheetNext.ToolTip = "No active takeoff item";
            BtnActiveTakeoffRecord.IsEnabled = false;
            BtnActiveTakeoffMore.IsEnabled = false;
            BtnActiveTakeoffFind.IsEnabled = false;
            BtnActiveTakeoffProperties.IsEnabled = false;
            BtnActiveTakeoffPrevious.IsEnabled = false;
            BtnActiveTakeoffNext.IsEnabled = false;
            BtnActiveTakeoffSheetNext.IsEnabled = false;
            UpdateStatusBarSegments();
            return;
        }

        string measurementType = OurPlanCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType);
        string typeTitle = TakeoffTypeDisplay(_activeItem);
        string total = _activeItem.Measurements.Count == 0
            ? "no measurements"
            : _activeItem.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode);
        string sheetTotal = ActiveTakeoffSheetTotalText(_activeItem);
        bool recordingThis = IsRecordingTakeoffItem(_activeItem);

        TxtActiveTakeoffTarget.Text = _activeItem.Name;
        TxtActiveTakeoffTargetMeta.Text = $"{TakeoffTypeTitle(_activeItem)} | total: {total}{sheetTotal}";
        ActiveTakeoffTargetGlyphHost.Child = BuildTakeoffSwatchGlyph(
            _activeItem, BrushFromHex(_activeItem.Color, Brushes.Gray), 18);
        BtnActiveTakeoffRecord.Content = recordingThis ? $"Recording {typeTitle}" : $"Record {typeTitle}";
        BtnActiveTakeoffRecord.IsEnabled = _currentPage != null;
        BtnActiveTakeoffMore.IsEnabled = true;
        BtnActiveTakeoffRecord.ToolTip = _currentPage == null
            ? "Select a sheet before recording"
            : recordingThis
                ? $"Recording {typeTitle} into {_activeItem.Name}. Click or press Space to stop."
                : $"Start recording {typeTitle} into {_activeItem.Name} (Space)";
        bool hasTreeItem = FindTakeoffTreeItem(_activeItem) != null;
        BtnActiveTakeoffFind.IsEnabled = hasTreeItem;
        BtnActiveTakeoffProperties.IsEnabled = hasTreeItem;
        bool canCycle = ActiveTakeoffTargetCycleItems().Count > 1;
        BtnActiveTakeoffPrevious.IsEnabled = canCycle;
        BtnActiveTakeoffNext.IsEnabled = canCycle;
        int sheetTargetCount = ActiveSheetTakeoffTargetCycleItems().Count;
        BtnActiveTakeoffSheetNext.IsEnabled = sheetTargetCount > 0;
        BtnActiveTakeoffSheetNext.ToolTip = sheetTargetCount > 0
            ? $"Switch through {sheetTargetCount} takeoff item(s) measured on this sheet"
            : "No takeoff items are measured on this sheet yet";
        UpdateStatusBarSegments();
    }

    private string ActiveTakeoffSheetTotalText(TakeoffItem item)
    {
        if (_currentPage == null)
            return "";

        var pageMeasurements = MeasurementsForTakeoffOnPage(item, _currentPage.FolderPath).ToList();
        string pageQuantity = pageMeasurements.Count == 0
            ? "none on sheet"
            : SheetLegendQuantityText(item, pageMeasurements);
        return $" | sheet: {pageQuantity}";
    }

    private string DefaultTakeoffName(string measurementType)
    {
        string title = MeasurementTypeTitle(measurementType);
        if (_activeItem != null &&
            OurPlanCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType) != measurementType)
            return $"{_activeItem.Name} - {title}";
        if (_currentPage != null)
            return $"{_currentPage.Name} {title}";
        return $"{title} Item";
    }

    private string DefaultTakeoffNameForFolder(string measurementType, string parentFolder)
    {
        string baseName = DefaultTakeoffName(measurementType);
        string prefix = ResolveTakeoffFolderDefaultNamePrefix(parentFolder);
        if (string.IsNullOrWhiteSpace(prefix) ||
            baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return baseName;
        }

        return prefix.EndsWith(" ", StringComparison.Ordinal) ||
               prefix.EndsWith("-", StringComparison.Ordinal) ||
               prefix.EndsWith("_", StringComparison.Ordinal)
            ? prefix + baseName
            : $"{prefix} {baseName}";
    }

    private static string MeasurementTypeTitle(string measurementType) =>
        OurPlanCoreJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "point" => "Count",
            "area" => "Area",
            _ => "Line",
        };

    private static string TakeoffTypeTitle(TakeoffItem item) =>
        item.IsJoistArea ? "Joist" : MeasurementTypeTitle(item.MeasurementType);

    private static string MeasurementTypeSign(string measurementType) =>
        OurPlanCoreJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "point" => "○",
            "area" => "□",
            _ => "╱",
        };

    private static string TakeoffTypeSign(TakeoffItem item) =>
        item.IsJoistArea ? "□╱" : MeasurementTypeSign(item.MeasurementType);

    private static string MeasurementTypeDisplay(string measurementType) =>
        MeasurementTypeTitle(measurementType);

    private static string TakeoffTypeDisplay(TakeoffItem item) =>
        TakeoffTypeTitle(item);

    private bool IsRecordingTakeoffItem(TakeoffItem item)
    {
        if (!IsRecordTool(_activeTool))
            return false;

        if (RecordMeasurementType(_activeTool) != OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType))
            return false;

        return !item.IsJoistArea || IsJoistAreaTool(_activeTool);
    }

    private static string SheetLegendTypeTitle(TakeoffItem item) =>
        item.IsJoistArea ? "Area" : TakeoffTypeTitle(item);

    private static string SheetLegendTypeSign(TakeoffItem item) =>
        item.IsJoistArea ? MeasurementTypeSign("area") : TakeoffTypeSign(item);

    private static FrameworkElement CreateTakeoffTypeIcon(TakeoffItem item, double size, Thickness margin) =>
        Controls.MeasurementGlyph.CreateWpf(
            Controls.MeasurementGlyph.Parse(
                OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType),
                joist: item.IsJoistArea,
                countSymbol: item.CountSymbol),
            BrushFromHex(item.Color, Brushes.Gray),
            size,
            margin);

    private static FrameworkElement CreateMeasurementTypeIcon(string kind, Brush brush, double size, Thickness margin) =>
        Controls.MeasurementGlyph.CreateWpf(
            Controls.MeasurementGlyph.Parse(OurPlanCoreJobStore.NormalizeMeasurementType(kind),
                joist: kind.Equals("joist", StringComparison.OrdinalIgnoreCase)),
            brush,
            size,
            margin);

    private static FrameworkElement CreateMeasurementTypeIcon(Measurement measurement, Brush brush, double size, Thickness margin) =>
        Controls.MeasurementGlyph.CreateWpf(
            Controls.MeasurementGlyph.Parse(
                OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType),
                joist: measurement.JoistEnabled,
                countSymbol: measurement.CountSymbol),
            brush,
            size,
            margin);

    private string TakeoffUnitText(TakeoffItem item) =>
        item.IsJoistArea ? UnitText("line") : UnitText(item.MeasurementType);

    private string MeasurementUnitText(Measurement measurement) =>
        measurement.JoistEnabled ? UnitText("line") : UnitText(measurement.MType);

    private static string CsvMeasurementType(TakeoffItem item) =>
        item.IsJoistArea ? "joist" : OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
}
