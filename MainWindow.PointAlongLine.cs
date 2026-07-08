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
        CreatePointsAlongSelectedLines();

    private void CreatePointsAlongSelectedLines()
    {
        IReadOnlyList<Measurement> selectedLines = SelectedLineMeasurementsForPointAlongLine();
        if (selectedLines.Count == 0)
        {
            TxtStatus.Text = "Select one or more Line measurements first.";
            return;
        }

        CreatePointsAlongLines(selectedLines, ResolveCommonPointAlongLineSourceItem(selectedLines));
    }

    private void CreatePointsAlongLineTakeoffItem(TakeoffItem item)
    {
        IReadOnlyList<Measurement> lines = item.Measurements
            .Where(IsPointAlongLineSource)
            .ToList();
        if (lines.Count == 0)
        {
            TxtStatus.Text = $"{item.Name} has no Line measurements to convert into Count points.";
            return;
        }

        CreatePointsAlongLines(lines, item);
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
        CreatePointsAlongLines([line], sourceItem);
    }

    private void CreatePointsAlongLines(IReadOnlyList<Measurement> sourceLines, TakeoffItem? sourceItem = null)
    {
        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before creating Count points.");
            return;
        }

        var lines = sourceLines
            .Where(IsPointAlongLineSource)
            .Distinct()
            .ToList();
        if (lines.Count == 0)
        {
            TxtStatus.Text = "Count points can only be created from Line measurements.";
            return;
        }

        sourceItem ??= ResolveCommonPointAlongLineSourceItem(lines);
        double defaultSpacing = ResolvePointAlongLineSpacingInches(sourceItem, lines[0]);
        var dialog = new PointAlongLineDialog(
            BuildPointAlongLineDefaultName(sourceItem, lines),
            defaultSpacing)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        List<PointAlongLineSourceResult> lineResults;
        try
        {
            lineResults = GeneratePointAlongLineResults(
                lines,
                new PointAlongLineOptions(dialog.SpacingInches, dialog.IncludeEndPoint));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            PostStatusInfo(ex.Message);
            return;
        }

        if (lineResults.Sum(result => result.Result.Points.Count) == 0)
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

        List<Measurement> generated = CreatePointAlongLineMeasurements(pointItem, lineResults);
        pointItem.Measurements.AddRange(generated);
        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(pointItem);
        QueueTakeoffAutosave(pointItem);
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

        RefreshPointAlongLineUi(pointItem, generated.Select(measurement => measurement.PageFolder));
        ShowPointAlongLineOnSheet(
            lines,
            generated,
            BuildPointAlongLineStatus(pointItem, lineResults, generated.Count, dialog.SpacingInches));
    }

    private TakeoffItem? ResolveCommonPointAlongLineSourceItem(IReadOnlyList<Measurement> lines)
    {
        TakeoffItem? common = null;
        foreach (Measurement line in lines)
        {
            if (!TryResolveTakeoffItemForMeasurement(line, out TakeoffItem item))
                return null;
            if (common == null)
            {
                common = item;
                continue;
            }

            if (!string.Equals(common.FolderPath, item.FolderPath, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return common;
    }

    private List<PointAlongLineSourceResult> GeneratePointAlongLineResults(
        IReadOnlyList<Measurement> lines,
        PointAlongLineOptions options)
    {
        var results = new List<PointAlongLineSourceResult>(lines.Count);
        foreach (Measurement line in lines)
        {
            double fallbackScale = ResolvePointAlongLineFallbackScale(line);
            PointAlongLineResult result = PointAlongLineService.Generate(line, fallbackScale, options);
            double effectiveScale = line.ScaleMetersPerPt > 0 ? line.ScaleMetersPerPt : fallbackScale;
            results.Add(new PointAlongLineSourceResult(line, result, effectiveScale));
        }

        return results;
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
        if (_currentPage?.ScaleMetersPerPt > 0 &&
            IsSamePageFolder(_currentPage.FolderPath, line.PageFolder))
        {
            return _currentPage.ScaleMetersPerPt;
        }

        return _viewport.ScaleMetersPerPt;
    }

    private string ResolvePointAlongLineParentFolder(TakeoffItem? sourceItem)
    {
        string? parent = sourceItem == null ? null : Path.GetDirectoryName(sourceItem.FolderPath);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            return parent;

        return _currentJob?.TakeoffsRoot ?? NewTakeoffItemParentFolderForUserCreate();
    }

    private static string BuildPointAlongLineDefaultName(TakeoffItem? sourceItem, IReadOnlyList<Measurement> lines)
    {
        if (sourceItem != null && !string.IsNullOrWhiteSpace(sourceItem.Name))
            return $"{sourceItem.Name.Trim()} Count Points";

        if (lines.Count == 1 && !string.IsNullOrWhiteSpace(lines[0].Name))
            return $"{lines[0].Name.Trim()} Count Points";

        return "Line Count Points";
    }

    private static List<Measurement> CreatePointAlongLineMeasurements(
        TakeoffItem pointItem,
        IReadOnlyList<PointAlongLineSourceResult> lineResults)
    {
        int index = 0;
        var generated = new List<Measurement>(lineResults.Sum(result => result.Result.Points.Count));
        foreach (PointAlongLineSourceResult lineResult in lineResults)
        {
            foreach (SKPoint point in lineResult.Result.Points)
            {
                if (HasGeneratedPoint(generated, lineResult.SourceLine.PageFolder, point))
                    continue;

                generated.Add(new Measurement
                {
                    Name = $"P {++index}",
                    MType = "point",
                    Points = [new SKPoint(point.X, point.Y)],
                    Color = pointItem.Color,
                    CountSymbol = pointItem.CountSymbol,
                    PageFolder = lineResult.SourceLine.PageFolder,
                    TakeoffFolder = pointItem.FolderPath,
                    ScaleMetersPerPt = lineResult.EffectiveScaleMetersPerPt,
                });
            }
        }

        return generated;
    }

    private static bool HasGeneratedPoint(IEnumerable<Measurement> generated, string pageFolder, SKPoint point)
    {
        foreach (Measurement measurement in generated)
        {
            if (!IsSamePageFolder(measurement.PageFolder, pageFolder))
                continue;
            if (measurement.Points.Count == 0)
                continue;

            SKPoint existing = measurement.Points[0];
            if (Math.Abs(existing.X - point.X) <= 0.01f &&
                Math.Abs(existing.Y - point.Y) <= 0.01f)
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshPointAlongLineUi(TakeoffItem pointItem, IEnumerable<string> pageFolders)
    {
        RefreshTreeItem(pointItem);
        using (UsePageMeasurementLookup())
        {
            RefreshTakeoffRowVisualsForItems([pointItem]);
            foreach (string pageFolder in pageFolders
                         .Where(page => !string.IsNullOrWhiteSpace(page))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                RefreshPageTakeoffIndicatorsForFolder(pageFolder);
            }
            ApplyTakeoffPageHighlights();
            RefreshSheetLegend();
        }

        RefreshEstimateTable();
        UpdateTotalDisplay();
    }

    private void ShowPointAlongLineOnSheet(
        IReadOnlyList<Measurement> sourceLines,
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

        string targetPageFolder = ResolvePointAlongLineDisplayPage(sourceLines, generated);
        if (!string.IsNullOrWhiteSpace(targetPageFolder) &&
            (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, targetPageFolder)) &&
            OurPlaneCoreJobStore.TryReadPage(targetPageFolder) is { } page)
        {
            OpenPageInActiveTab(page);
            Dispatcher.InvokeAsync(RefreshViewportSelection);
            return;
        }

        RefreshViewportSelection();
    }

    private string ResolvePointAlongLineDisplayPage(
        IReadOnlyList<Measurement> sourceLines,
        IReadOnlyList<Measurement> generated)
    {
        if (_currentPage != null &&
            generated.Any(measurement => IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath)))
        {
            return _currentPage.FolderPath;
        }

        return generated.FirstOrDefault()?.PageFolder ??
               sourceLines.FirstOrDefault()?.PageFolder ??
               "";
    }

    private string BuildPointAlongLineStatus(
        TakeoffItem item,
        IReadOnlyList<PointAlongLineSourceResult> lineResults,
        int pointCount,
        double spacingInches)
    {
        int lineCount = lineResults.Count;
        double totalLengthMeters = lineResults.Sum(result => result.Result.TotalLengthMeters);
        string total = Units.FormatLength(totalLengthMeters, _viewport.UnitMode);
        string source = lineCount == 1 ? "1 line" : $"{lineCount} lines";
        return $"Count points created: {item.Name}, {pointCount} @ {spacingInches:0.##} in from {source}, {total}.";
    }

    private sealed record PointAlongLineSourceResult(
        Measurement SourceLine,
        PointAlongLineResult Result,
        double EffectiveScaleMetersPerPt);
}
