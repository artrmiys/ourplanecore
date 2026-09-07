using System;
using System.Linq;
using System.Windows.Controls;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    private List<Measurement>? _pendingJoistDirectionApplyTargets;

    private void SetJoistDirectionFromSelectedLine(TreeViewItem tvi, TakeoffItem item)
    {
        if (OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) != "area")
        {
            TxtStatus.Text = "Joist direction can only be set on Area takeoff items.";
            return;
        }

        Measurement? area = SelectedJoistAreaMeasurement(item);
        if (area == null)
        {
            string message = "Select one Area on the sheet, or right-click an Area row and choose Set / Reset Joist Direction.";
            TxtStatus.Text = message;
            return;
        }

        SetJoistDirectionForSection(item, area);
    }

    private void SetJoistDirectionForSection(TakeoffItem item, Measurement area)
    {
        StartJoistDirectionCapture(item, area, applyTargets: null);
    }

    private void SetJoistDirectionForAllAreasFromSelectedLine(TreeViewItem tvi, TakeoffItem item)
    {
        if (OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) != "area")
        {
            TxtStatus.Text = "Joist direction can only be set on Area takeoff items.";
            return;
        }

        Measurement? area = SelectedJoistAreaMeasurement(item);
        if (area == null)
        {
            TxtStatus.Text = "Select or right-click one Area as the direction guide, then run Set Direction for All Areas.";
            return;
        }

        SetJoistDirectionForAllAreas(item, area);
    }

    private void SetJoistDirectionForAllAreas(TakeoffItem item, Measurement guideArea)
    {
        var targets = item.Measurements
            .Where(measurement => OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area")
            .Distinct()
            .ToList();
        if (targets.Count == 0)
        {
            TxtStatus.Text = "This takeoff item has no Area measurements to update.";
            return;
        }

        StartJoistDirectionCapture(item, guideArea, targets);
    }

    private void StartJoistDirectionCapture(TakeoffItem item, Measurement area, IReadOnlyList<Measurement>? applyTargets)
    {
        if (OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) != "area" ||
            OurPlanCoreJobStore.NormalizeMeasurementType(area.MType) != "area")
        {
            TxtStatus.Text = "Joist direction can only be set on Area measurements.";
            return;
        }

        if (string.IsNullOrWhiteSpace(area.PageFolder))
        {
            TxtStatus.Text = "This Area is not linked to a sheet, so joist direction cannot be set from the tree.";
            return;
        }

        item.IsJoistTakeoff = true;
        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        QueueTakeoffAutosave(item);

        void StartCapture()
        {
            _viewport.SetMeasurements(_takeoffItems.SelectMany(takeoff => takeoff.Measurements));
            _viewport.SelectMeasurements([area]);
            _pendingJoistDirectionApplyTargets = NormalizeJoistDirectionApplyTargets(item, applyTargets);
            bool started = _pendingJoistDirectionApplyTargets == null
                ? BeginJoistDirectionCapture(item, area)
                : BeginJoistDirectionCapture(item, area, preservePendingApplyTargets: true);
            if (!started)
                _pendingJoistDirectionApplyTargets = null;
        }

        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, area.PageFolder))
        {
            PageInfo? page = OurPlanCoreJobStore.TryReadPage(area.PageFolder);
            if (page == null)
            {
                TxtStatus.Text = "Cannot open the sheet for this Area, so joist direction was not started.";
                return;
            }

            OpenPageInActiveTab(page);
            Dispatcher.InvokeAsync(StartCapture);
            return;
        }

        StartCapture();
    }

    private static List<Measurement>? NormalizeJoistDirectionApplyTargets(
        TakeoffItem item,
        IReadOnlyList<Measurement>? applyTargets)
    {
        if (applyTargets == null)
            return null;

        var targets = applyTargets
            .Where(measurement =>
                item.Measurements.Contains(measurement) &&
                OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area")
            .Distinct()
            .ToList();
        return targets.Count == 0 ? null : targets;
    }

    private Measurement? SelectedJoistAreaMeasurement(TakeoffItem item)
    {
        var selected = _viewport.GetSelectedMeasurements()
            .Where(measurement =>
                item.Measurements.Contains(measurement) &&
                OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area")
            .ToList();
        if (selected.Count == 1)
            return selected[0];

        var selectedTreeAreas = EnumerateTakeoffTreeItems(TakeoffsTree)
            .Select(treeItem => treeItem.Tag as TakeoffMeasurementNode)
            .Where(node =>
                node != null &&
                ReferenceEquals(node.Item, item) &&
                _takeoffSectionMultiSelection.Contains(TakeoffSectionSelectionKey(node)) &&
                OurPlanCoreJobStore.NormalizeMeasurementType(node.Measurement.MType) == "area")
            .Select(node => node!.Measurement)
            .Distinct()
            .ToList();
        if (selectedTreeAreas.Count == 1)
            return selectedTreeAreas[0];

        if (_currentPage != null)
        {
            var pageAreas = item.Measurements
                .Where(measurement =>
                    OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
                    IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath))
                .ToList();
            if (pageAreas.Count == 1)
                return pageAreas[0];
        }

        return null;
    }

    private bool BeginJoistDirectionCapture(
        TakeoffItem item,
        Measurement area,
        bool preservePendingApplyTargets = false)
    {
        if (!preservePendingApplyTargets)
            _pendingJoistDirectionApplyTargets = null;
        item.IsJoistTakeoff = true;
        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        bool started = _viewport.BeginJoistDirectionCapture(area);
        if (started)
        {
            string scope = _pendingJoistDirectionApplyTargets is { Count: > 1 } targets
                ? $" for {targets.Count} areas"
                : "";
            TxtStatus.Text = $"Joist direction{scope} in {item.Name}: draw a two-point line parallel to the joists on the selected area.";
        }

        return started;
    }

    private void OnJoistDirectionCaptured(Measurement area, SKPoint start, SKPoint end)
    {
        // Always consume this state first. A rejected/too-short guide must not
        // leave a previous "apply to all" target list armed for a later Area.
        List<Measurement>? applyTargets = _pendingJoistDirectionApplyTargets;
        _pendingJoistDirectionApplyTargets = null;

        if (!EnsureCurrentJobWritable("change joist direction"))
            return;

        TakeoffItem? item = FindTakeoffItemForMeasurement(area);
        if (item == null)
            return;

        if (!TryDirectionFromPoints(start, end, out double directionDegrees))
        {
            TxtStatus.Text = "Joist direction line is too short.";
            return;
        }

        item.IsJoistTakeoff = true;
        item.JoistDirectionDegrees = directionDegrees;
        List<Measurement> updatedAreas = applyTargets is { Count: > 0 }
            ? applyTargets
            : [area];
        foreach (Measurement target in updatedAreas)
        {
            target.JoistDirectionDegrees = directionDegrees;
            target.JoistDirectionLocked = true;
        }
        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        QueueTakeoffAutosave(item);
        _viewport.SelectMeasurements(updatedAreas);
        RefreshTreeItem(item);
        RefreshActiveTakeoffVisuals();
        RefreshEstimateTable();
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        UpdateTotalDisplay();
        if (applyTargets == null && BeginNextPendingJoistDirectionCapture(item, area))
            return;

        if (updatedAreas.Count > 1)
        {
            JoistLayoutSummary summary = JoistTakeoffCalculator.Summarize(updatedAreas, _viewport.ScaleMetersPerPt);
            TxtStatus.Text = $"Joist direction {directionDegrees:0.#} deg applied to {updatedAreas.Count} areas in {item.Name}: {JoistTakeoffCalculator.FormatSummary(summary, _viewport.UnitMode)}.";
            return;
        }

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(updatedAreas[0], _viewport.ScaleMetersPerPt);
        TxtStatus.Text = $"Joists generated for {item.Name}: direction {directionDegrees:0.#} deg, {JoistTakeoffCalculator.FormatDiagnostics(layout, _viewport.UnitMode)}{FormatJoistScaleSuffix(updatedAreas[0])}.";
    }

    private bool BeginNextPendingJoistDirectionCapture(TakeoffItem item, Measurement? skip = null)
    {
        // This is always a one-Area capture. Clear any abandoned target list
        // left by a cancelled Set Direction for All Areas operation.
        _pendingJoistDirectionApplyTargets = null;
        if (!item.IsJoistArea)
            return false;

        List<Measurement> pending = item.Measurements
            .Where(measurement =>
                !ReferenceEquals(measurement, skip) &&
                OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
                !measurement.JoistDirectionLocked)
            .ToList();
        Measurement? next = pending.FirstOrDefault(measurement =>
            _currentPage != null && IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath))
            ?? pending.FirstOrDefault();
        if (next == null)
            return false;

        void StartCapture()
        {
            _viewport.SetMeasurements(_takeoffItems.SelectMany(takeoff => takeoff.Measurements));
            _viewport.SelectMeasurements([next]);
            if (!_viewport.BeginJoistDirectionCapture(next))
                return;
            int remaining = item.Measurements.Count(measurement =>
                OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
                !measurement.JoistDirectionLocked);
            TxtStatus.Text = $"Set this Area segment's own joist direction in {item.Name} " +
                             $"({remaining} remaining): click two points parallel to its joists.";
        }

        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, next.PageFolder))
        {
            PageInfo? page = OurPlanCoreJobStore.TryReadPage(next.PageFolder);
            if (page == null)
            {
                TxtStatus.Text = "Cannot open the sheet for the next Joist Area segment.";
                return false;
            }

            OpenPageInActiveTab(page);
            Dispatcher.InvokeAsync(StartCapture);
            return true;
        }

        StartCapture();
        return true;
    }

    private bool TryGetSelectedLineDirection(out double directionDegrees, out string message)
    {
        directionDegrees = 0;
        Measurement? line = _viewport.GetSelectedMeasurements()
            .FirstOrDefault(measurement =>
                OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "line" &&
                measurement.Points.Count >= 2);
        if (line == null)
        {
            message = "Select a Line measurement on the sheet first, then run this joist direction command.";
            return false;
        }

        SKPoint start = line.Points[0];
        SKPoint end = line.Points[^1];
        if (!TryDirectionFromPoints(start, end, out directionDegrees))
        {
            message = "Selected line is too short to define joist direction.";
            return false;
        }

        message = "";
        return true;
    }

    private static bool TryDirectionFromPoints(SKPoint start, SKPoint end, out double directionDegrees)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        if (Math.Sqrt(dx * dx + dy * dy) < 0.001)
        {
            directionDegrees = 0;
            return false;
        }

        directionDegrees = NormalizeJoistDirectionDegrees(Math.Atan2(dy, dx) * 180.0 / Math.PI);
        return true;
    }

    private static double NormalizeJoistDirectionDegrees(double degrees)
    {
        double normalized = degrees % 180.0;
        if (normalized < 0)
            normalized += 180.0;
        return Math.Abs(normalized - 180.0) < 0.0001 ? 0 : normalized;
    }
}
