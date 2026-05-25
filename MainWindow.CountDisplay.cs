using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private MenuItem BuildTakeoffCountDisplayMenu(TreeViewItem anchor)
    {
        var countItems = TakeoffItemsForSelection(anchor)
            .Where(IsCountTakeoffItem)
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        return BuildCountDisplayMenu(
            countItems.Count > 1 ? $"Count Display ({countItems.Count})" : "Count Display",
            countItems.Count > 0,
            CommonCountSymbol(countItems.Select(item => item.CountSymbol)),
            symbol => ApplyCountDisplayToTakeoffItems(countItems, symbol));
    }

    private MenuItem BuildTakeoffSectionCountDisplayMenu(TakeoffMeasurementNode anchor)
    {
        var countNodes = SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true)
            .Where(node => IsCountMeasurement(node.Measurement))
            .GroupBy(node => node.Measurement)
            .Select(group => group.First())
            .ToList();

        return BuildCountDisplayMenu(
            countNodes.Count > 1 ? $"Count Display ({countNodes.Count})" : "Count Display",
            countNodes.Count > 0,
            CommonCountSymbol(countNodes.Select(node => node.Measurement.CountSymbol)),
            symbol => ApplyCountDisplayToTakeoffSections(countNodes, symbol));
    }

    private MenuItem BuildPageTakeoffCountDisplayMenu(PageTakeoffNode anchor)
    {
        var countItems = SelectedPageTakeoffNodes(anchor, fallbackToAnchor: true)
            .Select(node => node.Takeoff)
            .Where(IsCountTakeoffItem)
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        return BuildCountDisplayMenu(
            countItems.Count > 1 ? $"Count Display ({countItems.Count})" : "Count Display",
            countItems.Count > 0,
            CommonCountSymbol(countItems.Select(item => item.CountSymbol)),
            symbol => ApplyCountDisplayToTakeoffItems(countItems, symbol));
    }

    private void AddViewportCountDisplayMenuItem(ContextMenu menu, ViewportContextRequest request)
    {
        IReadOnlyList<Measurement> selected = _viewport.GetSelectedMeasurements();
        var measurements = selected.Any(measurement => ReferenceEquals(measurement, request.Measurement))
            ? selected
            : request.Measurement == null
                ? []
                : [request.Measurement];
        var countMeasurements = measurements
            .Where(IsCountMeasurement)
            .Distinct()
            .ToList();
        if (countMeasurements.Count == 0)
            return;

        menu.Items.Add(BuildCountDisplayMenu(
            countMeasurements.Count > 1 ? $"Count Display ({countMeasurements.Count})" : "Count Display",
            enabled: true,
            CommonCountSymbol(countMeasurements.Select(measurement => measurement.CountSymbol)),
            symbol => ApplyCountDisplayToMeasurements(countMeasurements, symbol)));
    }

    private MenuItem BuildCountDisplayMenu(string header, bool enabled, string currentSymbol, Action<string> apply)
    {
        var menu = new MenuItem
        {
            Header = header,
            IsEnabled = enabled,
        };
        foreach (string symbol in CountDisplaySymbol.All)
        {
            string option = symbol;
            var child = new MenuItem
            {
                Header = CountDisplaySymbol.Title(option),
                IsCheckable = true,
                IsChecked = enabled && string.Equals(currentSymbol, option, StringComparison.OrdinalIgnoreCase),
            };
            child.Click += (_, _) => apply(option);
            menu.Items.Add(child);
        }

        return menu;
    }

    private void ApplyCountDisplayToTakeoffItems(IReadOnlyList<TakeoffItem> items, string symbol)
    {
        string normalized = CountDisplaySymbol.Normalize(symbol);
        var countItems = items
            .Where(IsCountTakeoffItem)
            .Where(item => !string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath))
            .ToList();
        if (countItems.Count == 0)
        {
            TxtStatus.Text = "No Count takeoff items selected.";
            return;
        }

        var changedMeasurements = new List<Measurement>();
        foreach (TakeoffItem item in countItems)
        {
            item.CountSymbol = normalized;
            foreach (Measurement measurement in item.Measurements.Where(IsCountMeasurement))
            {
                measurement.CountSymbol = normalized;
                changedMeasurements.Add(measurement);
            }
        }

        FinishCountDisplayChange(countItems, changedMeasurements, normalized);
    }

    private void ApplyCountDisplayToTakeoffSections(IReadOnlyList<TakeoffMeasurementNode> nodes, string symbol)
    {
        string normalized = CountDisplaySymbol.Normalize(symbol);
        var countNodes = nodes
            .Where(node => IsCountMeasurement(node.Measurement))
            .ToList();
        if (countNodes.Count == 0)
        {
            TxtStatus.Text = "No Count rows selected.";
            return;
        }

        var changedItems = new List<TakeoffItem>();
        var changedMeasurements = new List<Measurement>();
        foreach (var group in countNodes.GroupBy(node => node.Item))
        {
            TakeoffItem item = group.Key;
            item.CountSymbol = normalized;
            foreach (Measurement measurement in group.Select(node => node.Measurement).Distinct())
            {
                measurement.CountSymbol = normalized;
                changedMeasurements.Add(measurement);
            }
            changedItems.Add(item);
        }

        FinishCountDisplayChange(changedItems, changedMeasurements, normalized);
    }

    private void ApplyCountDisplayToMeasurements(IReadOnlyList<Measurement> measurements, string symbol)
    {
        string normalized = CountDisplaySymbol.Normalize(symbol);
        var countMeasurements = measurements
            .Where(IsCountMeasurement)
            .Distinct()
            .ToList();
        if (countMeasurements.Count == 0)
        {
            TxtStatus.Text = "No Count points selected.";
            return;
        }

        var changedItems = new List<TakeoffItem>();
        foreach (var group in countMeasurements.GroupBy(ResolveCountMeasurementItem))
        {
            if (group.Key == null)
                continue;

            TakeoffItem item = group.Key;
            item.CountSymbol = normalized;
            foreach (Measurement measurement in group)
                measurement.CountSymbol = normalized;
            changedItems.Add(item);
        }

        FinishCountDisplayChange(changedItems, countMeasurements, normalized);
        _viewport.SelectMeasurements(countMeasurements);
    }

    private void FinishCountDisplayChange(
        IReadOnlyList<TakeoffItem> changedItems,
        IReadOnlyList<Measurement> changedMeasurements,
        string symbol)
    {
        foreach (TakeoffItem item in changedItems
                     .Where(item => !string.IsNullOrWhiteSpace(item.FolderPath))
                     .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            OurPlaneCoreJobStore.SaveTakeoffItem(item);
            RefreshTreeItem(item);
        }

        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshActiveTakeoffVisuals();
        RefreshSheetLegend();
        RefreshEstimateTable();
        UpdateTotalDisplay();
        _viewport.RefreshMeasurementDisplay();

        string target = changedMeasurements.Count == 1 ? "Count point" : $"{changedMeasurements.Count} Count points";
        TxtStatus.Text = $"{target}: display set to {CountDisplaySymbol.Title(symbol)}.";
    }

    private TakeoffItem? ResolveCountMeasurementItem(Measurement measurement) =>
        _takeoffItems.FirstOrDefault(item =>
            IsCountTakeoffItem(item) &&
            item.Measurements.Contains(measurement));

    private static bool IsCountTakeoffItem(TakeoffItem item) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point";

    private static bool IsCountMeasurement(Measurement measurement) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "point";

    private static string CommonCountSymbol(IEnumerable<string> symbols)
    {
        var normalized = symbols
            .Select(CountDisplaySymbol.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized.Count == 1 ? normalized[0] : "";
    }

    private void BtnCountSymbolDefault_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (string symbol in CountDisplaySymbol.All)
        {
            string option = symbol;
            var child = new MenuItem
            {
                Header = CountDisplaySymbol.Title(option),
                IsCheckable = true,
                IsChecked = string.Equals(_newCountSymbol, option, StringComparison.OrdinalIgnoreCase),
            };
            child.Click += (_, _) => SetNewCountSymbol(option);
            menu.Items.Add(child);
        }

        menu.PlacementTarget = BtnCountSymbolDefault;
        menu.IsOpen = true;
    }

    private void SetNewCountSymbol(string symbol)
    {
        RememberNewCountSymbol(symbol);
        TxtStatus.Text = $"New Count display: {CountDisplaySymbol.Title(_newCountSymbol)}.";
    }

    private void RememberNewCountSymbol(string symbol)
    {
        _newCountSymbol = CountDisplaySymbol.Normalize(symbol);
        _settings.DefaultCountSymbol = _newCountSymbol;
        _viewport.ActiveCountSymbol = _newCountSymbol;
        UpdateDefaultCountSymbolButton();
        SaveAppSettings();
    }

    private void ApplyNewCountSymbolToItemIfNeeded(TakeoffItem item, string measurementType)
    {
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType) != "point")
            return;

        item.CountSymbol = CountDisplaySymbol.Normalize(_newCountSymbol);
        OurPlaneCoreJobStore.SaveTakeoffItem(item);
    }

    private void UpdateDefaultCountSymbolButton()
    {
        BtnCountSymbolDefault.Content = CountSymbolButtonText(_newCountSymbol);
        BtnCountSymbolDefault.ToolTip =
            $"Default display for newly created Count takeoffs: {CountDisplaySymbol.Title(_newCountSymbol)}";
    }

    private static string CountSymbolButtonText(string symbol) =>
        CountDisplaySymbol.Normalize(symbol) switch
        {
            CountDisplaySymbol.Cross => "X",
            CountDisplaySymbol.Square => "[]",
            _ => "O",
        };
}
