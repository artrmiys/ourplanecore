using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using OurPlaneCore.Controls;
using OurPlaneCore.Models;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    private const double MetersPerInch = 0.0254;

    private void TraceWallsFromSelectedArea(TreeViewItem tvi, TakeoffItem item)
    {
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) != "area")
        {
            TxtStatus.Text = "Walls can only be traced inside Area takeoff items.";
            return;
        }

        Measurement? area = SelectedJoistAreaMeasurement(item);
        if (area == null)
        {
            TxtStatus.Text = "Select one Area on the sheet, or right-click an Area row and choose Trace Walls.";
            return;
        }

        _ = TraceWallsFromAreaAsync(item, area);
    }

    private void TraceWallsFromAreaSection(TakeoffItem item, Measurement measurement)
    {
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) != "area" ||
            OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) != "area")
        {
            TxtStatus.Text = "Walls can only be traced inside Area sections.";
            return;
        }

        _ = TraceWallsFromAreaAsync(item, measurement);
    }

    private async Task TraceWallsFromAreaAsync(TakeoffItem sourceItem, Measurement area)
    {
        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before tracing walls.");
            return;
        }

        if (area.Points.Count < 3)
        {
            PostStatusInfo("The area needs at least 3 points to trace walls inside it.");
            return;
        }

        double fallbackScale = _currentPage?.ScaleMetersPerPt > 0
            ? _currentPage.ScaleMetersPerPt
            : _viewport.ScaleMetersPerPt;
        double effectiveScale = area.ScaleMetersPerPt > 0 ? area.ScaleMetersPerPt : fallbackScale;
        if (effectiveScale <= 0)
        {
            PostStatusInfo("Set the sheet scale first: wall thickness is measured in real inches.");
            return;
        }

        var dialog = new WallTraceDialog(BuildWallTraceDefaultName(sourceItem, area)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        TxtStatus.Text = "Tracing walls: reading sheet vector lines...";
        (IReadOnlyList<PdfGeometrySnapSegment> segments, string error) =
            await _viewport.ReadPdfVectorSegmentsForCurrentPageAsync();
        if (segments.Count == 0)
        {
            PostStatusInfo(string.IsNullOrWhiteSpace(error)
                ? "No vector lines found on this sheet. Wall tracing needs a vector PDF (not a scan)."
                : $"Wall tracing failed to read sheet lines: {error}");
            return;
        }

        IReadOnlyList<SKRect> textRects = await _viewport.ReadPdfTextRectsForCurrentPageAsync();
        IReadOnlyList<SKRect> wallFillRects = await _viewport.ReadPdfWallFillRectsForCurrentPageAsync();

        float minThicknessPt = (float)(dialog.MinThicknessInches * MetersPerInch / effectiveScale);
        float maxThicknessPt = (float)(dialog.MaxThicknessInches * MetersPerInch / effectiveScale);
        float minWallLengthPt = (float)(dialog.MinWallLengthFeet * 12 * MetersPerInch / effectiveScale);
        var options = new WallCenterlineTracer.Options
        {
            MinThicknessPt = minThicknessPt,
            MaxThicknessPt = maxThicknessPt,
            MinFaceLengthPt = minWallLengthPt * 0.5f,
            MinWallLengthPt = minWallLengthPt,
            ExcludedZones = InflateTextZones(textRects),
            WallFillZones = wallFillRects.Count > 0 ? wallFillRects : null,
        };

        List<WallCenterlineTracer.Segment> traceInput = segments
            .Where(s => !string.Equals(s.Kind, "pdf-curve", StringComparison.OrdinalIgnoreCase))
            .Select(s => new WallCenterlineTracer.Segment(s.Start, s.End))
            .ToList();
        IReadOnlyList<SKPoint> polygon = area.Points;
        IReadOnlyList<IReadOnlyList<SKPoint>>? holes = area.Holes?.Count > 0
            ? area.Holes.Select(h => (IReadOnlyList<SKPoint>)h).ToList()
            : null;

        List<SKPoint[]> polylines = await Task.Run(() =>
            WallCenterlineTracer.Trace(traceInput, polygon, options, holes));
        if (polylines.Count == 0)
        {
            TxtStatus.Text =
                "No wall pairs found inside the area. Try widening the thickness range or check the sheet scale.";
            return;
        }

        string parentFolder = ResolveAreaLineGridParentFolder(sourceItem);
        TakeoffItem lineItem = CreateUniqueTakeoffItem(
            dialog.TakeoffName,
            RandomTakeoffColor(sourceItem.Color),
            "line",
            parentFolder);
        ApplyTakeoffFolderDefaultsToNewItem(lineItem, parentFolder);
        lineItem.IsJoistTakeoff = false;

        List<Measurement> generated = CreateWallTraceMeasurements(lineItem, area, polylines, effectiveScale);
        lineItem.Measurements.AddRange(generated);
        QueueTakeoffAutosave(lineItem);
        _takeoffItems.Add(lineItem);

        ItemsControl treeParent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
        TreeViewItem tvi = AddTakeoffTreeItem(lineItem, treeParent);
        if (treeParent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;

        _activeItem = lineItem;
        _activeTakeoffParentFolder = parentFolder;
        _viewport.ActiveColor = lineItem.Color;
        _viewport.ActiveTakeoffFolder = lineItem.FolderPath;
        tvi.IsSelected = true;
        ApplyToolSelection("line");

        RefreshAreaLineGridUi(lineItem, area.PageFolder);
        double totalMeters = generated.Sum(m => m.Value(effectiveScale));
        string total = Units.FormatLength(totalMeters, _viewport.UnitMode);
        string status =
            $"Wall trace: {generated.Count} centerline(s), {total} " +
            $"({dialog.MinThicknessInches:0.##}-{dialog.MaxThicknessInches:0.##} in walls). " +
            "Lines stay editable: drag vertices or delete extras.";
        ShowAreaLineGridOnSheet(area, generated, status);
    }

    /// <summary>
    /// Grows word boxes so lines hugging the text (label underlines, the
    /// frame of a room-number tag) fall inside the exclusion zone too.
    /// </summary>
    private static IReadOnlyList<SKRect>? InflateTextZones(IReadOnlyList<SKRect> textRects)
    {
        if (textRects.Count == 0)
            return null;

        var zones = new List<SKRect>(textRects.Count);
        foreach (SKRect rect in textRects)
        {
            SKRect zone = rect;
            float grow = Math.Clamp(rect.Height * 0.6f, 2f, 8f);
            zone.Inflate(2f, grow);
            zones.Add(zone);
        }

        return zones;
    }

    private static string BuildWallTraceDefaultName(TakeoffItem item, Measurement area)
    {
        string areaName = string.IsNullOrWhiteSpace(area.Name) ? "" : $" {area.Name.Trim()}";
        return $"{item.Name}{areaName} Walls";
    }

    private static List<Measurement> CreateWallTraceMeasurements(
        TakeoffItem lineItem,
        Measurement sourceArea,
        List<SKPoint[]> polylines,
        double scaleMetersPerPt)
    {
        var generated = new List<Measurement>(polylines.Count);
        int index = 0;
        foreach (SKPoint[] polyline in polylines)
        {
            if (polyline.Length < 2)
                continue;

            generated.Add(new Measurement
            {
                Name = $"Wall {++index}",
                MType = "line",
                Points = [.. polyline],
                Color = lineItem.Color,
                PageFolder = sourceArea.PageFolder,
                TakeoffFolder = lineItem.FolderPath,
                ScaleMetersPerPt = scaleMetersPerPt,
            });
        }

        return generated;
    }
}
