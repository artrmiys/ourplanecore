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
        if (string.IsNullOrWhiteSpace(area.PageFolder))
        {
            PostStatusInfo("This Area is not linked to a sheet, so walls cannot be traced from it.");
            return;
        }

        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, area.PageFolder))
        {
            PageInfo? areaPage = OurPlaneCoreJobStore.TryReadPage(area.PageFolder);
            if (areaPage == null)
            {
                PostStatusInfo("Cannot open the sheet for this Area, so wall tracing was not started.");
                return;
            }

            OpenPageInActiveTab(areaPage);
            await Dispatcher.InvokeAsync(() => { });
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

        TxtStatus.Text = "Tracing walls: reading sheet vector/raster lines...";
        (IReadOnlyList<PdfGeometrySnapSegment> segments, string error, string source) =
            await _viewport.ReadWallTraceSegmentsForCurrentPageAsync();
        if (segments.Count == 0)
        {
            PostStatusInfo(string.IsNullOrWhiteSpace(error)
                ? "No vector or raster lines found on this sheet. Wall tracing needs visible wall linework."
                : $"Wall tracing failed to read sheet lines: {error}");
            return;
        }

        IReadOnlyList<SKRect> textRects = await _viewport.ReadPdfTextRectsForCurrentPageAsync();
        IReadOnlyList<WallCenterlineTracer.FillZone> wallFillZones =
            await _viewport.ReadPdfWallFillZonesForCurrentPageAsync();

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
            WallFillZones = wallFillZones.Count > 0 ? wallFillZones : null,
            DarkFillOnly = dialog.DarkFillOnly,
            DarkLuminanceMax = (float?)dialog.DarkFillCutoff,
            BoundaryExclusionPt = dialog.IncludePerimeterWalls
                ? 0f
                : (float)(dialog.PerimeterOffsetFeet * 12 * MetersPerInch / effectiveScale),
        };

        IReadOnlyList<SKPoint> polygon = area.Points;
        IReadOnlyList<IReadOnlyList<SKPoint>>? holes = area.Holes?.Count > 0
            ? area.Holes.Select(h => (IReadOnlyList<SKPoint>)h).ToList()
            : null;

        var traceResult = await TraceWallCenterlinesWithRasterFallbackAsync(segments, source, polygon, options, holes);
        List<SKPoint[]> polylines = traceResult.Polylines;
        source = traceResult.Source;
        string rasterFallbackError = traceResult.RasterFallbackError;
        bool rasterFallbackTried = traceResult.RasterFallbackTried;

        if (polylines.Count == 0)
        {
            PostWallTraceNoPairsStatus(source, rasterFallbackTried, rasterFallbackError);
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
        string sourceLabel = string.Equals(source, "raster-image", StringComparison.OrdinalIgnoreCase)
            ? "raster"
            : "vector";
        string status =
            $"Wall trace ({sourceLabel}): {generated.Count} centerline(s), {total} " +
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

    private async Task<(List<SKPoint[]> Polylines, string Source, string RasterFallbackError, bool RasterFallbackTried)>
        TraceWallCenterlinesWithRasterFallbackAsync(
            IReadOnlyList<PdfGeometrySnapSegment> segments,
            string source,
            IReadOnlyList<SKPoint> polygon,
            WallCenterlineTracer.Options options,
            IReadOnlyList<IReadOnlyList<SKPoint>>? holes)
    {
        List<SKPoint[]> polylines = await TraceWallCenterlinesAsync(segments, polygon, options, holes);
        if (polylines.Count > 0 ||
            string.Equals(source, "raster-image", StringComparison.OrdinalIgnoreCase))
        {
            return (polylines, source, "", false);
        }

        TxtStatus.Text = "Tracing walls: PDF lines did not match; trying raster image...";
        (IReadOnlyList<PdfGeometrySnapSegment> rasterSegments, string rasterError) =
            await _viewport.ReadWallTraceRasterImageSegmentsForCurrentPageAsync();
        if (rasterSegments.Count == 0)
            return (polylines, source, rasterError, true);

        polylines = await TraceWallCenterlinesAsync(rasterSegments, polygon, options, holes);
        return (polylines, "raster-image", rasterError, true);
    }

    private void PostWallTraceNoPairsStatus(string source, bool rasterFallbackTried, string rasterFallbackError)
    {
        if (string.Equals(source, "raster-image", StringComparison.OrdinalIgnoreCase))
        {
            TxtStatus.Text = "Raster wall trace found line pixels, but no wall pairs matched inside the area. Try widening the thickness range or check the sheet scale.";
            return;
        }

        TxtStatus.Text = rasterFallbackTried && !string.IsNullOrWhiteSpace(rasterFallbackError)
            ? $"No wall pairs found from PDF lines, and raster fallback failed: {rasterFallbackError}"
            : "No wall pairs found inside the area. Try widening the thickness range or check the sheet scale.";
    }

    private static Task<List<SKPoint[]>> TraceWallCenterlinesAsync(
        IReadOnlyList<PdfGeometrySnapSegment> segments,
        IReadOnlyList<SKPoint> polygon,
        WallCenterlineTracer.Options options,
        IReadOnlyList<IReadOnlyList<SKPoint>>? holes)
    {
        return Task.Run(() =>
        {
            List<WallCenterlineTracer.Segment> traceInput = segments
                .Where(s => !string.Equals(s.Kind, "pdf-curve", StringComparison.OrdinalIgnoreCase))
                .Select(s => new WallCenterlineTracer.Segment(s.Start, s.End))
                .ToList();

            return WallCenterlineTracer.Trace(traceInput, polygon, options, holes);
        });
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
