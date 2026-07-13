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
    // Estimate table row building, total display, quantities, and costs.

    private void UpdateTotalDisplay(bool refreshEstimate = true)
    {
        if (refreshEstimate)
            RefreshEstimateTable();

        UpdateActiveTakeoffTargetBar();
        if (_activeItem == null || _activeItem.Measurements.Count == 0)
        {
            TxtTotal.Text = "Total: —";
            return;
        }
        TxtTotal.Text = $"Total: {_activeItem.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode)}";
    }

    private List<EstimateDisplayRow> BuildEstimateDisplayRows(string filter, bool currentSheetOnly)
    {
        var rows = new List<EstimateDisplayRow>();
        foreach (var item in _takeoffItems.Where(i => i.Measurements.Count > 0))
        {
            var scopedMeasurements = currentSheetOnly
                ? item.Measurements
                    .Where(measurement => _currentPage != null && IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath))
                    .ToList()
                : item.Measurements.ToList();
            if (scopedMeasurements.Count == 0)
                continue;

            bool itemMatches = EstimateItemMatchesFilter(item, filter);
            var scopedSet = scopedMeasurements.ToHashSet();
            var visibleSet = new HashSet<Measurement>();
            for (int i = 0; i < item.Measurements.Count; i++)
            {
                Measurement measurement = item.Measurements[i];
                if (!scopedSet.Contains(measurement))
                    continue;
                if (itemMatches || EstimateMeasurementMatchesFilter(item, measurement, i, filter))
                    visibleSet.Add(measurement);
            }
            if (!itemMatches && visibleSet.Count == 0)
                continue;

            rows.Add(new EstimateDisplayRow(
                item.Name,
                currentSheetOnly ? $"{TakeoffTypeDisplay(item)} / {_currentPage?.Name}" : TakeoffTypeDisplay(item),
                scopedMeasurements.Count.ToString(CultureInfo.InvariantCulture),
                currentSheetOnly ? SheetLegendQuantityText(item, scopedMeasurements) : QuantityText(item),
                TakeoffUnitText(item),
                UnitPriceText(item),
                currentSheetOnly ? CostText(item, scopedMeasurements) : CostText(item),
                "",
                item,
                null));
            for (int i = 0; i < item.Measurements.Count; i++)
            {
                Measurement measurement = item.Measurements[i];
                if (!visibleSet.Contains(measurement))
                    continue;

                rows.Add(new EstimateDisplayRow(
                    $"  {SectionDisplayName(item, measurement, i)}",
                    $"{(measurement.JoistEnabled ? "Joist" : MeasurementTypeTitle(measurement.MType))} {SectionPageName(measurement)}".Trim(),
                    "",
                    QuantityText(measurement),
                    MeasurementUnitText(measurement),
                    "",
                    "",
                    measurement.Notes,
                    item,
                    measurement));
            }
        }

        return rows;
    }

    private void RefreshEstimateTable()
    {
        if (_estimateList == null)
            return;

        // The estimate rebuild is O(all takeoffs × all measurements) plus a
        // full DataGrid ItemsSource reset. It runs on every measurement edit,
        // but the Estimating tab is hidden during normal takeoff work (the
        // right panel defaults to the Takeoffs tab). Defer the rebuild while
        // it's not visible and do it when the tab is activated instead.
        if (_rightWorkspaceTabs != null && _estimateTab != null &&
            !ReferenceEquals(_rightWorkspaceTabs.SelectedItem, _estimateTab))
        {
            _estimateTableDirty = true;
            return;
        }

        _estimateTableDirty = false;
        RebuildEstimateTableNow();
    }

    private void RebuildEstimateTableNow()
    {
        if (_estimateList == null)
            return;

        Measurement? selectedMeasurement = (_estimateList.SelectedItem as EstimateDisplayRow)?.Measurement;
        _syncingEstimateSelection = true;
        try
        {
            string filter = _estimateFilterBox?.Text.Trim() ?? "";
            bool currentSheetOnly = _estimateCurrentSheetOnlyBox?.IsChecked == true && _currentPage != null;
            var rows = BuildEstimateDisplayRows(filter, currentSheetOnly);
            EstimateDisplayRow? selectedRow = selectedMeasurement == null
                ? null
                : rows.FirstOrDefault(row => ReferenceEquals(row.Measurement, selectedMeasurement));
            int itemRows = rows.Count(row => row.Takeoff != null && row.Measurement == null);
            int detailRows = rows.Count(row => row.Measurement != null);
            double visibleCost = rows
                .Where(row => row.Takeoff != null && row.Measurement == null)
                .Select(row => double.TryParse(row.Cost, NumberStyles.Float, CultureInfo.InvariantCulture, out double cost)
                    ? cost
                    : 0)
                .Sum();

            _estimateList.ItemsSource = null;
            _estimateList.ItemsSource = rows;

            UpdateEstimateSummaryText(itemRows, detailRows, visibleCost, currentSheetOnly, filter);

            if (selectedRow != null)
            {
                _estimateList.SelectedItem = selectedRow;
                _estimateList.ScrollIntoView(selectedRow);
            }
        }
        finally
        {
            _syncingEstimateSelection = false;
        }
    }

    private static bool EstimateItemMatchesFilter(TakeoffItem item, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return TextContains(item.Name, filter) ||
               TextContains(TakeoffTypeTitle(item), filter);
    }

    private static bool EstimateMeasurementMatchesFilter(TakeoffItem item, Measurement measurement, int measurementIndex, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return TextContains(item.Name, filter) ||
               TextContains(MeasurementTypeTitle(measurement.MType), filter) ||
               TextContains(SectionDisplayName(item, measurement, measurementIndex), filter) ||
               TextContains(SectionPageName(measurement), filter) ||
               TextContains(measurement.Notes, filter);
    }

    private static bool TextContains(string value, string filter) =>
        value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

    private string QuantityText(TakeoffItem item)
    {
        string mt = OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double value = item.Total(_viewport.ScaleMetersPerPt);
        if (item.IsJoistArea)
            return QuantityText("line", value);
        return QuantityText(mt, value);
    }

    private string QuantityText(Measurement measurement)
    {
        string mt = OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType);
        double value = measurement.Value(_viewport.ScaleMetersPerPt);
        if (measurement.JoistEnabled)
            return QuantityText("line", value);
        return QuantityText(mt, value);
    }

    private string QuantityText(string mt, double value)
    {
        return mt switch
        {
            "line" => _viewport.UnitMode == UnitMode.Imperial
                ? (value / 0.3048).ToString("F2", CultureInfo.InvariantCulture)
                : value.ToString("F2", CultureInfo.InvariantCulture),
            "area" => _viewport.UnitMode == UnitMode.Imperial
                ? (value / 0.0929030).ToString("F2", CultureInfo.InvariantCulture)
                : value.ToString("F2", CultureInfo.InvariantCulture),
            "point" => value.ToString("F0", CultureInfo.InvariantCulture),
            _ => value.ToString("F2", CultureInfo.InvariantCulture),
        };
    }

    private string UnitText(string measurementType)
    {
        string mt = OurPlanCoreJobStore.NormalizeMeasurementType(measurementType);
        return mt switch
        {
            "line" => _viewport.UnitMode == UnitMode.Imperial ? "ft" : "m",
            "area" => _viewport.UnitMode == UnitMode.Imperial ? "sf" : "m2",
            "point" => "ea",
            _ => "",
        };
    }

    private static string UnitPriceText(TakeoffItem item) =>
        item.UnitPrice > 0 ? item.UnitPrice.ToString("F2", CultureInfo.InvariantCulture) : "";

    private string CostText(TakeoffItem item)
    {
        if (item.UnitPrice <= 0 || item.Measurements.Count == 0)
            return "";

        double quantity = EstimateQuantity(item);
        return (quantity * item.UnitPrice).ToString("F2", CultureInfo.InvariantCulture);
    }

    private string CostText(TakeoffItem item, IReadOnlyList<Measurement> measurements)
    {
        if (item.UnitPrice <= 0 || measurements.Count == 0)
            return "";

        double quantity = EstimateQuantity(item, measurements);
        return (quantity * item.UnitPrice).ToString("F2", CultureInfo.InvariantCulture);
    }

    private double EstimateQuantity(TakeoffItem item)
    {
        string mt = OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double value = item.Total(_viewport.ScaleMetersPerPt);
        return mt switch
        {
            _ when item.IsJoistArea && _viewport.UnitMode == UnitMode.Imperial => value / 0.3048,
            "line" when _viewport.UnitMode == UnitMode.Imperial => value / 0.3048,
            "area" when _viewport.UnitMode == UnitMode.Imperial => value / 0.0929030,
            _ => value,
        };
    }

    private double EstimateQuantity(TakeoffItem item, IReadOnlyList<Measurement> measurements)
    {
        string mt = OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double fallbackScale = _currentPage?.ScaleMetersPerPt > 0
            ? _currentPage.ScaleMetersPerPt
            : _viewport.ScaleMetersPerPt;
        double value = measurements.Sum(measurement => measurement.Value(fallbackScale));
        return mt switch
        {
            _ when measurements.Any(measurement => measurement.JoistEnabled) && _viewport.UnitMode == UnitMode.Imperial => value / 0.3048,
            "line" when _viewport.UnitMode == UnitMode.Imperial => value / 0.3048,
            "area" when _viewport.UnitMode == UnitMode.Imperial => value / 0.0929030,
            _ => value,
        };
    }
}
