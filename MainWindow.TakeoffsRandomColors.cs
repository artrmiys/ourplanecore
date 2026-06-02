using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private static readonly string[] RandomTakeoffColorPalette =
    [
        "#E53935",
        "#1E88E5",
        "#43A047",
        "#FB8C00",
        "#8E24AA",
        "#00ACC1",
        "#FDD835",
        "#3949AB",
        "#D81B60",
        "#7CB342",
        "#6D4C41",
        "#039BE5",
        "#C0CA33",
        "#5E35B1",
        "#F4511E",
        "#00897B",
        "#546E7A",
        "#C2185B",
        "#2E7D32",
        "#EF6C00",
        "#1565C0",
        "#AD1457",
        "#558B2F",
        "#00695C",
    ];

    private void RandomizeTakeoffItemColors(TreeViewItem anchor)
    {
        IReadOnlyList<TakeoffItem> selectedItems = DistinctColorableTakeoffItems(TakeoffItemsForSelection(anchor));
        if (selectedItems.Count == 0)
        {
            TxtStatus.Text = "No takeoff items found for random colors.";
            return;
        }

        try
        {
            ApplyRandomTakeoffItemColors(selectedItems, anchor);
        }
        catch (Exception ex)
        {
            ShowOperationError("Random Takeoff Colors", ex);
        }
    }

    private void ApplyRandomTakeoffItemColors(IReadOnlyList<TakeoffItem> items, TreeViewItem? anchor)
    {
        int paletteOffset = Random.Shared.Next(RandomTakeoffColorPalette.Length);
        double hueOffset = Random.Shared.NextDouble() * 360.0;

        for (int index = 0; index < items.Count; index++)
        {
            TakeoffItem item = items[index];
            string color = RandomTakeoffColor(index, paletteOffset, hueOffset);
            item.Color = color;
            foreach (Measurement measurement in item.Measurements)
                measurement.Color = color;

            OurPlaneCoreJobStore.SaveTakeoffItem(item);
            RefreshTreeItem(item);
        }

        if (_activeItem != null &&
            items.Any(item => string.Equals(item.FolderPath, _activeItem.FolderPath, StringComparison.OrdinalIgnoreCase)))
        {
            _viewport.ActiveColor = _activeItem.Color;
        }

        RefreshEstimateTable();
        RefreshPagesTakeoffIndicators();
        RefreshActiveTakeoffVisuals();
        RefreshSheetLegend();
        UpdateTotalDisplay();
        _viewport.RefreshMeasurementDisplay();
        if (anchor != null)
            SelectTakeoffSelectionMeasurementsOnCurrentPage(anchor);
        TxtStatus.Text = $"Randomized colors for {items.Count} takeoff item(s).";
    }

    private static IReadOnlyList<TakeoffItem> DistinctColorableTakeoffItems(IEnumerable<TakeoffItem> items) =>
        items
            .Where(item => !string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath))
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

    private static string RandomTakeoffColor(int index, int paletteOffset, double hueOffset)
    {
        if (index < RandomTakeoffColorPalette.Length)
            return RandomTakeoffColorPalette[(index + paletteOffset) % RandomTakeoffColorPalette.Length];

        double hue = (hueOffset + (index * 137.508)) % 360.0;
        return ColorFromHsv(hue, saturation: 0.78, value: 0.88);
    }

    private static string ColorFromHsv(double hue, double saturation, double value)
    {
        double chroma = value * saturation;
        double h = hue / 60.0;
        double x = chroma * (1.0 - Math.Abs((h % 2.0) - 1.0));
        double r1 = 0;
        double g1 = 0;
        double b1 = 0;

        if (h < 1)
        {
            r1 = chroma;
            g1 = x;
        }
        else if (h < 2)
        {
            r1 = x;
            g1 = chroma;
        }
        else if (h < 3)
        {
            g1 = chroma;
            b1 = x;
        }
        else if (h < 4)
        {
            g1 = x;
            b1 = chroma;
        }
        else if (h < 5)
        {
            r1 = x;
            b1 = chroma;
        }
        else
        {
            r1 = chroma;
            b1 = x;
        }

        double m = value - chroma;
        int r = (int)Math.Round((r1 + m) * 255.0);
        int g = (int)Math.Round((g1 + m) * 255.0);
        int b = (int)Math.Round((b1 + m) * 255.0);
        return $"#{ClampColorByte(r):X2}{ClampColorByte(g):X2}{ClampColorByte(b):X2}";
    }

    private static int ClampColorByte(int value) =>
        Math.Clamp(value, 0, 255);
}
