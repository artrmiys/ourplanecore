using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OurPlaneCore.Controls;
using OurPlaneCore.Dialogs;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void BtnPointAlongLine_Click(object sender, RoutedEventArgs e) =>
        CreatePointsAlongSelectedLine();

    private void CreatePointsAlongSelectedLine()
    {
        IReadOnlyList<Measurement> selectedLines = SelectedLineMeasurementsForPointAlongLine();
        if (selectedLines.Count != 1)
        {
            TxtStatus.Text = selectedLines.Count == 0
                ? "Select one Line measurement first."
                : "Select only one Line measurement before creating Count points along it.";
            return;
        }

        CreatePointsAlongLine(selectedLines[0]);
    }

    private IReadOnlyList<Measurement> SelectedLineMeasurementsForPointAlongLine() =>
        _viewport.GetSelectedMeasurements()
            .Where(IsPointAlongLineSource)
            .ToList();

    private static bool IsPointAlongLineSource(Measurement measurement) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "line" &&
        measurement.Points.Count >= 2;

    private void CreatePointsAlongLine(Measurement line, TakeoffItem? sourceItem = null)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Create Count Points",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!IsPointAlongLineSource(line))
        {
            TxtStatus.Text = "Count points can only be created from a Line measurement.";
            return;
        }

        if (sourceItem == null)
            TryResolveTakeoffItemForMeasurement(line, out sourceItem);

        double defaultSpacing = ResolvePointAlongLineSpacingInches(sourceItem, line);
        var dialog = new PointAlongLineDialog(
            BuildPointAlongLineDefaultName(sourceItem, line),
            defaultSpacing)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        double fallbackScale = ResolvePointAlongLineFallbackScale(line);
        PointAlongLineResult result;
        try
        {
            result = PointAlongLineService.Generate(
                line,
                fallbackScale,
                new PointAlongLineOptions(dialog.SpacingInches, dialog.IncludeEndPoint));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(ex.Message, "Create Count Points",
                MessageBoxButton.OK, MessageBoxImage.Information);
            TxtStatus.Text = ex.Message;
            return;
        }

        if (result.Points.Count == 0)
        {
            TxtStatus.Text = "No Count points were created. Check the line length and spacing.";
            return;
        }

        string parentFolder = ResolvePointAlongLineParentFolder(sourceItem);
        TakeoffItem pointItem = CreateUniqueTakeoffItem(
            dialog.TakeoffName,
            RandomTakeoffColor(sourceItem?.Color ?? _viewport.ActiveColor),
            "point",
            parentFolder);
        ApplyTakeoffFolderDefaultsToNewItem(pointItem, parentFolder);
        ApplyNewCountSymbolToItemIfNeeded(pointItem, "point");

        double effectiveScale = line.ScaleMetersPerPt > 0 ? line.ScaleMetersPerPt : fallbackScale;
        List<Measurement> generated = CreatePointAlongLineMeasurements(pointItem, line, result.Points, effectiveScale);
        pointItem.Measurements.AddRange(generated);
        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(pointItem);
        OurPlaneCoreJobStore.SaveTakeoffItem(pointItem);
        _takeoffItems.Add(pointItem);

        ItemsControl treeParent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
        TreeViewItem tvi = AddTakeoffTreeItem(pointItem, treeParent);
        if (treeParent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;

        _activeItem = pointItem;
        _activeTakeoffParentFolder = parentFolder;
        _viewport.ActiveColor = pointItem.Color;
        _viewport.ActiveTakeoffFolder = pointItem.FolderPath;
        _viewport.ActiveCountSymbol = pointItem.CountSymbol;
        tvi.IsSelected = true;
        ApplyToolSelection("point");

        RefreshPointAlongLineUi(pointItem, line.PageFolder);
        ShowPointAlongLineOnSheet(
            line,
            generated,
            BuildPointAlongLineStatus(pointItem, result, dialog.SpacingInches));
    }

    private static double ResolvePointAlongLineSpacingInches(TakeoffItem? sourceItem, Measurement line)
    {
        if (line.JoistSpacingInches > 0)
            return line.JoistSpacingInches;
        if (sourceItem?.JoistSpacingInches > 0)
            return sourceItem.JoistSpacingInches;
        return 16;
    }

    private double ResolvePointAlongLineFallbackScale(Measurement line)
    {
        if (line.ScaleMetersPerPt > 0)
            return line.ScaleMetersPerPt;
        if (_currentPage?.ScaleMetersPerPt > 0)
            return _currentPage.ScaleMetersPerPt;
        return _viewport.ScaleMetersPerPt;
    }

    private string ResolvePointAlongLineParentFolder(TakeoffItem? sourceItem)
    {
        string? parent = sourceItem == null ? null : Path.GetDirectoryName(sourceItem.FolderPath);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            return parent;

        return _currentJob?.TakeoffsRoot ?? NewTakeoffItemParentFolderForUserCreate();
    }

    private static string BuildPointAlongLineDefaultName(TakeoffItem? sourceItem, Measurement line)
    {
        string baseName = sourceItem == null || string.IsNullOrWhiteSpace(sourceItem.Name)
            ? string.IsNullOrWhiteSpace(line.Name) ? "Line" : line.Name.Trim()
            : sourceItem.Name.Trim();
        return $"{baseName} Count Points";
    }

    private static List<Measurement> CreatePointAlongLineMeasurements(
        TakeoffItem pointItem,
        Measurement sourceLine,
        IReadOnlyList<SKPoint> points,
        double scaleMetersPerPt)
    {
        var generated = new List<Measurement>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            SKPoint point = points[i];
            generated.Add(new Measurement
            {
                Name = $"P {i + 1}",
                MType = "point",
                Points = [new SKPoint(point.X, point.Y)],
                Color = pointItem.Color,
                CountSymbol = pointItem.CountSymbol,
                PageFolder = sourceLine.PageFolder,
                TakeoffFolder = pointItem.FolderPath,
                ScaleMetersPerPt = scaleMetersPerPt,
            });
        }

        return generated;
    }

    private void RefreshPointAlongLineUi(TakeoffItem pointItem, string pageFolder)
    {
        RefreshTreeItem(pointItem);
        using (UsePageMeasurementLookup())
        {
            RefreshTakeoffRowVisualsForItems([pointItem]);
            RefreshPageTakeoffIndicatorsForFolder(pageFolder);
            ApplyTakeoffPageHighlights();
            RefreshSheetLegend();
        }

        RefreshEstimateTable();
        UpdateTotalDisplay();
    }

    private void ShowPointAlongLineOnSheet(
        Measurement sourceLine,
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

        if (!string.IsNullOrWhiteSpace(sourceLine.PageFolder) &&
            (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, sourceLine.PageFolder)) &&
            OurPlaneCoreJobStore.TryReadPage(sourceLine.PageFolder) is { } page)
        {
            OpenPageInActiveTab(page);
            Dispatcher.InvokeAsync(RefreshViewportSelection);
            return;
        }

        RefreshViewportSelection();
    }

    private string BuildPointAlongLineStatus(
        TakeoffItem item,
        PointAlongLineResult result,
        double spacingInches)
    {
        string total = Units.FormatLength(result.TotalLengthMeters, _viewport.UnitMode);
        return $"Count points created: {item.Name}, {result.Points.Count} @ {spacingInches:0.##} in along {total}.";
    }
}
