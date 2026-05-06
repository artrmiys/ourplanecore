using System;
using System.Linq;
using System.Windows.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void SetJoistDirectionFromSelectedLine(TreeViewItem tvi, TakeoffItem item)
    {
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) != "area")
        {
            TxtStatus.Text = "Joist direction can only be set on Area takeoff items.";
            return;
        }

        Measurement? area = SelectedJoistAreaMeasurement(item);
        if (area == null)
        {
            string message = "Select one Area measurement on the sheet first, then run this joist direction command.";
            TxtStatus.Text = message;
            return;
        }

        item.IsJoistTakeoff = true;
        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        _viewport.SetMeasurements(_takeoffItems.SelectMany(takeoff => takeoff.Measurements));
        BeginJoistDirectionCapture(item, area);
    }

    private Measurement? SelectedJoistAreaMeasurement(TakeoffItem item)
    {
        var selected = _viewport.GetSelectedMeasurements()
            .Where(measurement =>
                item.Measurements.Contains(measurement) &&
                OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area")
            .ToList();
        if (selected.Count == 1)
            return selected[0];

        if (_currentPage != null)
        {
            var pageAreas = item.Measurements
                .Where(measurement =>
                    OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
                    IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath))
                .ToList();
            if (pageAreas.Count == 1)
                return pageAreas[0];
        }

        return null;
    }

    private void BeginJoistDirectionCapture(TakeoffItem item, Measurement area)
    {
        item.IsJoistTakeoff = true;
        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        area.JoistDirectionLocked = false;
        _viewport.BeginJoistDirectionCapture(area);
        TxtStatus.Text = $"Joist direction for {item.Name}: draw a two-point line parallel to the joists on the selected area.";
    }

    private void OnJoistDirectionCaptured(Measurement area, SKPoint start, SKPoint end)
    {
        TakeoffItem? item = FindTakeoffItemForMeasurement(area);
        if (item == null)
            return;

        if (!TryDirectionFromPoints(start, end, out double directionDegrees))
        {
            TxtStatus.Text = "Joist direction line is too short.";
            return;
        }

        item.IsJoistTakeoff = true;
        area.JoistDirectionDegrees = directionDegrees;
        area.JoistDirectionLocked = true;
        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        _viewport.SelectMeasurements([area]);
        RefreshTreeItem(item);
        RefreshActiveTakeoffVisuals();
        RefreshEstimateTable();
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        UpdateTotalDisplay();
        if (BeginNextPendingJoistDirectionCapture(item, area))
            return;

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(area, _viewport.ScaleMetersPerPt);
        TxtStatus.Text = $"Joists generated for {item.Name}: direction {directionDegrees:0.#} deg, {JoistTakeoffCalculator.FormatDiagnostics(layout, _viewport.UnitMode)}{FormatJoistScaleSuffix(area)}.";
    }

    private bool BeginNextPendingJoistDirectionCapture(TakeoffItem item, Measurement? skip = null)
    {
        if (_currentPage == null || !item.IsJoistArea)
            return false;

        Measurement? next = item.Measurements.FirstOrDefault(measurement =>
            !ReferenceEquals(measurement, skip) &&
            OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
            IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath) &&
            !measurement.JoistDirectionLocked);
        if (next == null)
            return false;

        _viewport.BeginJoistDirectionCapture(next);
        TxtStatus.Text = $"Set joist direction for next area in {item.Name}: click two points parallel to the joists.";
        return true;
    }

    private bool TryGetSelectedLineDirection(out double directionDegrees, out string message)
    {
        directionDegrees = 0;
        Measurement? line = _viewport.GetSelectedMeasurements()
            .FirstOrDefault(measurement =>
                OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "line" &&
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
