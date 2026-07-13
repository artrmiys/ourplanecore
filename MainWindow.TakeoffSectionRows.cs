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
    // Takeoff section row deletion, labels, and tooltips.

    private void DeleteSection(TakeoffItem item, Measurement measurement)
    {
        DeleteTakeoffSections(new TakeoffMeasurementNode(item, measurement));
    }

    private void DeleteTakeoffSections(TakeoffMeasurementNode anchor)
    {
        var selectedNodes = SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true);
        if (selectedNodes.Count == 0)
            return;

        string entryTitle = selectedNodes.Count == 1
            ? MeasurementEntryTitle(anchor.Item)
            : MeasurementEntryTitlePlural(selectedNodes);
        if (MessageBox.Show(
                selectedNodes.Count == 1
                    ? $"Delete this {entryTitle.ToLowerInvariant()} from {anchor.Item.Name}?"
                    : $"Delete {selectedNodes.Count} selected {entryTitle}?",
                selectedNodes.Count == 1 ? $"Delete {entryTitle}" : "Delete Takeoff Rows",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var removedMeasurements = selectedNodes
            .Select(node => node.Measurement)
            .Distinct()
            .ToList();
        foreach (var group in selectedNodes.GroupBy(node => node.Item))
        {
            foreach (Measurement measurement in group.Select(node => node.Measurement).Distinct())
                group.Key.Measurements.Remove(measurement);

            QueueTakeoffAutosave(group.Key);
            RefreshTreeItem(group.Key);
        }

        _takeoffSectionMultiSelection.Clear();
        _takeoffSectionRangeAnchorKey = null;
        _viewport.DeleteMeasurements(removedMeasurements);
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        RefreshEstimateTable();
        UpdateTotalDisplay();
        TxtStatus.Text = removedMeasurements.Count == 1
            ? $"Deleted {entryTitle.ToLowerInvariant()} from {anchor.Item.Name}."
            : $"Deleted {removedMeasurements.Count} selected {entryTitle}.";
    }

    private static string SectionDisplayName(TakeoffItem item, Measurement measurement, int index) =>
        string.IsNullOrWhiteSpace(measurement.Name)
            ? DefaultSectionName(item, measurement, index)
            : measurement.Name;

    private static string DefaultSectionName(TakeoffItem item, Measurement measurement) =>
        DefaultSectionName(item, measurement, Math.Max(0, item.Measurements.IndexOf(measurement)));

    private static string DefaultSectionName(TakeoffItem item, Measurement measurement, int index)
    {
        string page = SectionPageName(measurement);
        string entry = OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point" ? "Count" : "Section";
        return string.IsNullOrWhiteSpace(page)
            ? $"{entry} {index + 1}"
            : $"{entry} {index + 1} - {page}";
    }

    private static string SectionPageName(Measurement measurement) =>
        string.IsNullOrWhiteSpace(measurement.PageFolder)
            ? ""
            : OurPlanCoreJobStore.DisplayName(measurement.PageFolder);

    private static string SectionCountLabel(TakeoffItem item) =>
        OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point"
            ? item.Measurements.Count == 1 ? "1 count" : $"{item.Measurements.Count} counts"
            : item.Measurements.Count == 1 ? "1 section" : $"{item.Measurements.Count} sections";

    private static string MeasurementEntryTitle(TakeoffItem item) =>
        OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point" ? "Count" : "Section";

    private static string MeasurementEntryTitlePlural(IEnumerable<TakeoffMeasurementNode> nodes)
    {
        var types = nodes
            .Select(node => OurPlanCoreJobStore.NormalizeMeasurementType(node.Item.MeasurementType))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (types.Count == 1)
            return types[0] == "point" ? "counts" : "sections";
        return "section/count rows";
    }

    private static bool CanRemoveMeasurementVertex(Measurement measurement) =>
        measurement.MType switch
        {
            "line" => measurement.Points.Count > 2,
            "area" => measurement.Points.Count > 3,
            _ => false,
        };

    private static string SectionTooltip(TakeoffItem item)
    {
        var lines = new List<string> { SectionCountLabel(item) };
        for (int i = 0; i < item.Measurements.Count; i++)
        {
            Measurement m = item.Measurements[i];
            string page = string.IsNullOrWhiteSpace(m.PageFolder)
                ? "unknown page"
                : OurPlanCoreJobStore.DisplayName(m.PageFolder);
            string name = string.IsNullOrWhiteSpace(m.Name)
                ? (OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point" ? $"Count {i + 1}" : $"Section {i + 1}")
                : m.Name;
            string detail = OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point"
                ? "1 count"
                : $"{m.Points.Count} vertices";
            lines.Add($"{name}: {page}, {detail}");
            if (!string.IsNullOrWhiteSpace(m.Notes))
                lines.Add($"  Notes: {m.Notes}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string? TakeoffItemTooltip(TakeoffItem item, bool isActive)
    {
        var lines = new List<string>();
        if (isActive)
            lines.Add("Active takeoff target");
        if (!string.IsNullOrWhiteSpace(item.Notes))
            lines.Add($"Notes: {item.Notes}");
        if (item.Measurements.Count > 0)
            lines.Add(SectionTooltip(item));

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }
}
