using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlaneCore;

public partial class MainWindow
{
    private const string PageMeasurementVisibilityToggleTag = "PageMeasurementVisibilityToggle";

    private FrameworkElement BuildPageMeasurementVisibilityDot(PageInfo page)
    {
        bool allVisible = AreAllCurrentPageMeasurementsVisible(page);
        Brush stroke = (Brush?)Application.Current.Resources["ControlForegroundBrush"] ?? Brushes.DimGray;
        var dot = new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = allVisible ? stroke : Brushes.Transparent,
            BorderBrush = stroke,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(6, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Tag = PageMeasurementVisibilityToggleTag,
            ToolTip = allVisible
                ? $"Hide all current takeoffs on {page.Name}. New measurements stay visible."
                : $"Show all takeoffs on {page.Name}.",
        };
        dot.PreviewMouseLeftButtonDown += (_, e) =>
        {
            TogglePageMeasurementVisibilitySnapshot(page);
            e.Handled = true;
        };
        return dot;
    }

    private bool HasCurrentPageMeasurements(PageInfo page) =>
        CurrentMeasurementsForPage(page).Count > 0;

    private bool AreAllCurrentPageMeasurementsVisible(PageInfo page)
    {
        List<Measurement> measurements = CurrentMeasurementsForPage(page);
        var hiddenMeasurements = page.HiddenMeasurements
            .Select(NormalizeMeasurementVisibilityId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (measurements.Any(measurement =>
                hiddenMeasurements.Contains(NormalizeMeasurementVisibilityId(measurement.Id))))
        {
            return false;
        }

        var hiddenTakeoffs = page.HiddenTakeoffs
            .Select(NormalizeTakeoffLegendOrderKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (hiddenTakeoffs.Count == 0)
            return measurements.Count > 0 || hiddenMeasurements.Count == 0;

        if (CurrentTakeoffKeysForPage(page).Any(hiddenTakeoffs.Contains))
            return false;

        return measurements.Count > 0 || hiddenMeasurements.Count == 0;
    }

    private void TogglePageMeasurementVisibilitySnapshot(PageInfo page)
    {
        if (AreAllCurrentPageMeasurementsVisible(page))
            HideCurrentPageMeasurements(page);
        else
            ShowAllPageMeasurements(page);
    }

    private void HideCurrentPageMeasurements(PageInfo page)
    {
        var hiddenMeasurements = CurrentMeasurementsForPage(page)
            .Select(measurement => NormalizeMeasurementVisibilityId(measurement.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (hiddenMeasurements.Count == 0)
        {
            TxtStatus.Text = $"No current takeoffs to hide on {page.Name}.";
            return;
        }

        SavePageMeasurementVisibility(page, [], hiddenMeasurements);
        TxtStatus.Text = $"Hidden {hiddenMeasurements.Count} current measurement(s) on {page.Name}. New measurements stay visible.";
    }

    private void ShowAllPageMeasurements(PageInfo page)
    {
        SavePageMeasurementVisibility(page, [], []);
        TxtStatus.Text = $"Shown all takeoffs on {page.Name}.";
    }

    private void SavePageMeasurementVisibility(
        PageInfo page,
        IReadOnlyList<string> hiddenTakeoffs,
        IReadOnlyList<string> hiddenMeasurements)
    {
        var takeoffs = hiddenTakeoffs
            .Select(NormalizeTakeoffLegendOrderKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var measurements = hiddenMeasurements
            .Select(NormalizeMeasurementVisibilityId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        page.HiddenTakeoffs = takeoffs;
        page.HiddenMeasurements = measurements;
        if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
        {
            _currentPage.HiddenTakeoffs = takeoffs.ToList();
            _currentPage.HiddenMeasurements = measurements.ToList();
        }

        ApplyViewportPageTakeoffVisibility(page);
        OurPlaneCoreJobStore.SavePageHiddenTakeoffs(page.FolderPath, takeoffs);
        OurPlaneCoreJobStore.SavePageHiddenMeasurements(page.FolderPath, measurements);
        RefreshPageTakeoffIndicatorsForFolder(page.FolderPath);
        RefreshSheetLegend();
        RefreshDetachedSheetDisplaySettings();
    }

    private List<Measurement> CurrentMeasurementsForPage(PageInfo page)
    {
        var result = new List<Measurement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TakeoffItem takeoff in TakeoffsForPage(page.FolderPath))
        {
            foreach (Measurement measurement in MeasurementsForTakeoffOnPage(takeoff, page.FolderPath))
            {
                string id = NormalizeMeasurementVisibilityId(measurement.Id);
                if (string.IsNullOrWhiteSpace(id) || seen.Add(id))
                    result.Add(measurement);
            }
        }

        return result;
    }

    private IEnumerable<string> CurrentTakeoffKeysForPage(PageInfo page) =>
        TakeoffsForPage(page.FolderPath)
            .Select(TakeoffLegendOrderKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private List<string> RemoveHiddenMeasurementsForTakeoffs(
        PageInfo page,
        IEnumerable<TakeoffItem> takeoffs,
        IEnumerable<string> hiddenMeasurements)
    {
        var remove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TakeoffItem takeoff in takeoffs)
        {
            foreach (Measurement measurement in MeasurementsForTakeoffOnPage(takeoff, page.FolderPath))
            {
                string id = NormalizeMeasurementVisibilityId(measurement.Id);
                if (!string.IsNullOrWhiteSpace(id))
                    remove.Add(id);
            }
        }

        return hiddenMeasurements
            .Select(NormalizeMeasurementVisibilityId)
            .Where(id => !string.IsNullOrWhiteSpace(id) && !remove.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsMeasurementHiddenByPageSnapshot(PageInfo page, Measurement measurement)
    {
        string id = NormalizeMeasurementVisibilityId(measurement.Id);
        return !string.IsNullOrWhiteSpace(id) &&
               page.HiddenMeasurements
                   .Select(NormalizeMeasurementVisibilityId)
                   .Contains(id, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeMeasurementVisibilityId(string? value) =>
        (value ?? "").Trim();

    private static bool IsPageMeasurementVisibilityToggleSource(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is FrameworkElement { Tag: PageMeasurementVisibilityToggleTag })
                return true;
            if (source is TreeViewItem)
                return false;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
