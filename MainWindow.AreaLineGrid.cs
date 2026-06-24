using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void CreateLineGridFromSelectedArea(TreeViewItem tvi, TakeoffItem item)
    {
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) != "area")
        {
            TxtStatus.Text = "Line grid can only be created from Area takeoff items.";
            return;
        }

        Measurement? area = SelectedJoistAreaMeasurement(item);
        if (area == null)
        {
            TxtStatus.Text = "Select one Area on the sheet, or right-click an Area row and choose Create Line Grid.";
            return;
        }

        CreateLineGridFromArea(item, area);
    }

    private void CreateLineGridFromAreaSection(TakeoffItem item, Measurement measurement)
    {
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) != "area" ||
            OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) != "area")
        {
            TxtStatus.Text = "Line grid can only be created from Area sections.";
            return;
        }

        CreateLineGridFromArea(item, measurement);
    }

    private void CreateLineGridFromArea(TakeoffItem sourceItem, Measurement area)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Create Line Grid",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        double defaultSpacing = ResolveAreaLineGridSpacingInches(sourceItem, area);
        var dialog = new AreaLineGridDialog(
            BuildAreaLineGridDefaultName(sourceItem, area),
            defaultSpacing,
            defaultSpacing)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        var options = new AreaLineGridOptions(
            dialog.IncludeHorizontal,
            dialog.HorizontalSpacingInches,
            dialog.IncludeVertical,
            dialog.VerticalSpacingInches);

        AreaLineGridResult result;
        double effectiveScale;
        try
        {
            double fallbackScale = _currentPage?.ScaleMetersPerPt > 0
                ? _currentPage.ScaleMetersPerPt
                : _viewport.ScaleMetersPerPt;
            effectiveScale = area.ScaleMetersPerPt > 0 ? area.ScaleMetersPerPt : fallbackScale;
            result = AreaLineGridService.Generate(area, fallbackScale, options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(ex.Message, "Create Line Grid",
                MessageBoxButton.OK, MessageBoxImage.Information);
            TxtStatus.Text = ex.Message;
            return;
        }

        if (result.Count == 0)
        {
            TxtStatus.Text = "Line grid created no segments. Increase the area size or check the spacing.";
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

        List<Measurement> generated = CreateAreaLineGridMeasurements(lineItem, area, result, effectiveScale);
        lineItem.Measurements.AddRange(generated);
        OurPlaneCoreJobStore.SaveTakeoffItem(lineItem);
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
        string status = BuildAreaLineGridStatus(lineItem, result, options);
        ShowAreaLineGridOnSheet(area, generated, status);
    }

    private static double ResolveAreaLineGridSpacingInches(TakeoffItem item, Measurement area)
    {
        if (area.JoistSpacingInches > 0)
            return area.JoistSpacingInches;
        if (item.JoistSpacingInches > 0)
            return item.JoistSpacingInches;
        return 16;
    }

    private string ResolveAreaLineGridParentFolder(TakeoffItem sourceItem)
    {
        string? parent = Path.GetDirectoryName(sourceItem.FolderPath);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            return parent;

        return _currentJob?.TakeoffsRoot ?? NewTakeoffItemParentFolderForUserCreate();
    }

    private static string BuildAreaLineGridDefaultName(TakeoffItem item, Measurement area)
    {
        string areaName = string.IsNullOrWhiteSpace(area.Name) ? "" : $" {area.Name.Trim()}";
        return $"{item.Name}{areaName} Grid Lines";
    }

    private static List<Measurement> CreateAreaLineGridMeasurements(
        TakeoffItem lineItem,
        Measurement sourceArea,
        AreaLineGridResult result,
        double scaleMetersPerPt)
    {
        var generated = new List<Measurement>(result.Count);
        int horizontalIndex = 0;
        int verticalIndex = 0;
        foreach (AreaLineGridSegment segment in result.Segments)
        {
            string prefix = segment.Axis == AreaLineGridAxis.Horizontal
                ? $"H {++horizontalIndex}"
                : $"V {++verticalIndex}";
            generated.Add(new Measurement
            {
                Name = prefix,
                MType = "line",
                Points =
                [
                    new SKPoint(segment.Start.X, segment.Start.Y),
                    new SKPoint(segment.End.X, segment.End.Y),
                ],
                Color = lineItem.Color,
                PageFolder = sourceArea.PageFolder,
                TakeoffFolder = lineItem.FolderPath,
                ScaleMetersPerPt = scaleMetersPerPt,
            });
        }

        return generated;
    }

    private void RefreshAreaLineGridUi(TakeoffItem lineItem, string pageFolder)
    {
        RefreshTreeItem(lineItem);
        using (UsePageMeasurementLookup())
        {
            RefreshTakeoffRowVisualsForItems([lineItem]);
            RefreshPageTakeoffIndicatorsForFolder(pageFolder);
            ApplyTakeoffPageHighlights();
            RefreshSheetLegend();
        }

        RefreshEstimateTable();
        UpdateTotalDisplay();
    }

    private void ShowAreaLineGridOnSheet(
        Measurement area,
        IReadOnlyList<Measurement> generated,
        string status)
    {
        void RefreshViewportSelection()
        {
            _viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements), clearUndoStack: false);
            _viewport.SelectMeasurements(generated);
            _viewport.RefreshMeasurementDisplay();
            TxtStatus.Text = status;
        }

        if (!string.IsNullOrWhiteSpace(area.PageFolder) &&
            (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, area.PageFolder)) &&
            OurPlaneCoreJobStore.TryReadPage(area.PageFolder) is { } page)
        {
            OpenPageInActiveTab(page);
            Dispatcher.InvokeAsync(RefreshViewportSelection);
            return;
        }

        RefreshViewportSelection();
    }

    private string BuildAreaLineGridStatus(
        TakeoffItem item,
        AreaLineGridResult result,
        AreaLineGridOptions options)
    {
        var parts = new List<string>();
        if (options.IncludeHorizontal)
            parts.Add($"H {result.HorizontalCount} @ {options.HorizontalSpacingInches:0.##} in");
        if (options.IncludeVertical)
            parts.Add($"V {result.VerticalCount} @ {options.VerticalSpacingInches:0.##} in");

        string total = Units.FormatLength(result.TotalLengthMeters, _viewport.UnitMode);
        return $"Line grid created: {item.Name}, {result.Count} editable line segments, {total} ({string.Join(", ", parts)}).";
    }
}
