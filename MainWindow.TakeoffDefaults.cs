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
    // Takeoff creation defaults and color helpers.

    private string CurrentTakeoffParentFolder()
    {
        string? selectedFolder = TakeoffsTree.SelectedItem is TreeViewItem { Tag: TakeoffFolderNode folder }
            ? folder.FolderPath
            : null;
        string? selectedItemParentFolder = TakeoffsTree.SelectedItem is TreeViewItem { Tag: TakeoffItem item } &&
            !string.IsNullOrWhiteSpace(item.FolderPath)
                ? Path.GetDirectoryName(item.FolderPath) ?? _currentJob?.TakeoffsRoot
                : null;
        return TakeoffCreationPolicy.NewFolderParentFolder(
            _currentJob,
            selectedFolder,
            selectedItemParentFolder,
            _activeTakeoffParentFolder,
            Directory.Exists);
    }

    private string NewTakeoffItemParentFolder() =>
        TakeoffCreationPolicy.NewItemParentFolder(_currentJob);

    private string NewTakeoffItemParentFolderForUserCreate()
    {
        if (_currentJob == null)
            return "";

        if (TakeoffsTree.SelectedItem is TreeViewItem { Tag: TakeoffFolderNode folder } &&
            !string.IsNullOrWhiteSpace(folder.FolderPath) &&
            Directory.Exists(folder.FolderPath))
        {
            return folder.FolderPath;
        }

        return NewTakeoffItemParentFolder();
    }

    private string ResolveTakeoffFolderDefaultMeasurementType(string folderPath, string fallback)
    {
        string fallbackType = OurPlanCoreJobStore.NormalizeMeasurementType(fallback);
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (!string.IsNullOrWhiteSpace(properties.DefaultMeasurementType))
                return OurPlanCoreJobStore.NormalizeMeasurementType(properties.DefaultMeasurementType);
        }

        return fallbackType;
    }

    private string RandomTakeoffColor(string? avoidColor = null)
    {
        List<Color> colorsToAvoid = CurrentSheetTakeoffColors();
        if (TryParseTakeoffColor(avoidColor, out Color avoided))
        {
            colorsToAvoid.Add(avoided);
            if (TryCreateContrastingTakeoffColor(avoided, colorsToAvoid, out string contrasting))
                return contrasting;
        }
        else if (colorsToAvoid.Count > 0 &&
                 TryCreateContrastingTakeoffColor(colorsToAvoid[^1], colorsToAvoid, out string contrasting))
        {
            return contrasting;
        }

        for (int attempt = 0; attempt < 96; attempt++)
        {
            string candidate = RandomVividTakeoffColor();
            if (!TryParseTakeoffColor(candidate, out Color color))
                continue;

            if (IsDistinctTakeoffColor(color, colorsToAvoid))
                return candidate;
        }

        return RandomVividTakeoffColor();
    }

    private static bool TryCreateContrastingTakeoffColor(
        Color baseColor,
        IReadOnlyList<Color> colorsToAvoid,
        out string colorText)
    {
        RgbToHsl(baseColor, out double baseHue, out double baseSaturation, out double baseLightness);
        double saturation = Math.Clamp(Math.Max(baseSaturation, 0.68), 0.58, 0.92);
        double lightness = Math.Clamp(baseLightness, 0.40, 0.58);
        double[] hueOffsets = [120.0, 240.0, 180.0, 90.0, 270.0, 150.0, 210.0, 60.0, 300.0];
        double[] lightnessOffsets = [0.0, -0.08, 0.08, -0.14, 0.14];

        foreach (double hueOffset in hueOffsets)
        {
            foreach (double lightnessOffset in lightnessOffsets)
            {
                double hue = NormalizeHue(baseHue + hueOffset);
                HslToRgb(
                    hue,
                    saturation,
                    Math.Clamp(lightness + lightnessOffset, 0.34, 0.66),
                    out byte r,
                    out byte g,
                    out byte b);
                var color = Color.FromRgb(r, g, b);
                if (IsDistinctTakeoffColor(color, colorsToAvoid))
                {
                    colorText = $"#{r:X2}{g:X2}{b:X2}";
                    return true;
                }
            }
        }

        colorText = "";
        return false;
    }

    private List<Color> CurrentSheetTakeoffColors()
    {
        string pageFolder = _currentPage?.FolderPath ?? "";
        if (string.IsNullOrWhiteSpace(pageFolder))
            return [];

        var colors = new List<Color>();
        foreach (TakeoffItem item in _takeoffItems)
        {
            if (!item.Measurements.Any(measurement => IsSamePageFolder(measurement.PageFolder, pageFolder)))
                continue;

            if (TryParseTakeoffColor(item.Color, out Color color))
                colors.Add(color);
        }

        return colors;
    }

    private static bool TryParseTakeoffColor(string? value, out Color color)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            color = default;
            return false;
        }

        try
        {
            color = (Color)ColorConverter.ConvertFromString(NormalizeTakeoffColor(value));
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    private static bool IsDistinctTakeoffColor(Color color, IEnumerable<Color> existingColors)
    {
        foreach (Color existing in existingColors)
        {
            int dr = color.R - existing.R;
            int dg = color.G - existing.G;
            int db = color.B - existing.B;
            if (dr * dr + dg * dg + db * db < 48 * 48)
                return false;
        }

        return true;
    }

    private static string RandomVividTakeoffColor()
    {
        double hue = Random.Shared.NextDouble() * 360.0;
        double saturation = 0.58 + Random.Shared.NextDouble() * 0.34;
        double lightness = 0.38 + Random.Shared.NextDouble() * 0.24;
        HslToRgb(hue, saturation, lightness, out byte r, out byte g, out byte b);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static void RgbToHsl(Color color, out double hue, out double saturation, out double lightness)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        lightness = (max + min) / 2.0;
        if (delta <= 0.000001)
        {
            hue = 0.0;
            saturation = 0.0;
            return;
        }

        saturation = delta / (1.0 - Math.Abs(2.0 * lightness - 1.0));
        if (Math.Abs(max - r) <= 0.000001)
            hue = 60.0 * (((g - b) / delta) % 6.0);
        else if (Math.Abs(max - g) <= 0.000001)
            hue = 60.0 * (((b - r) / delta) + 2.0);
        else
            hue = 60.0 * (((r - g) / delta) + 4.0);

        hue = NormalizeHue(hue);
    }

    private static double NormalizeHue(double hue)
    {
        double normalized = hue % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }

    private static void HslToRgb(double hue, double saturation, double lightness, out byte r, out byte g, out byte b)
    {
        double c = (1.0 - Math.Abs(2.0 * lightness - 1.0)) * saturation;
        double x = c * (1.0 - Math.Abs(hue / 60.0 % 2.0 - 1.0));
        double m = lightness - c / 2.0;
        double r1;
        double g1;
        double b1;
        if (hue < 60.0)
            (r1, g1, b1) = (c, x, 0.0);
        else if (hue < 120.0)
            (r1, g1, b1) = (x, c, 0.0);
        else if (hue < 180.0)
            (r1, g1, b1) = (0.0, c, x);
        else if (hue < 240.0)
            (r1, g1, b1) = (0.0, x, c);
        else if (hue < 300.0)
            (r1, g1, b1) = (x, 0.0, c);
        else
            (r1, g1, b1) = (c, 0.0, x);

        r = (byte)Math.Round(Math.Clamp((r1 + m) * 255.0, 0.0, 255.0));
        g = (byte)Math.Round(Math.Clamp((g1 + m) * 255.0, 0.0, 255.0));
        b = (byte)Math.Round(Math.Clamp((b1 + m) * 255.0, 0.0, 255.0));
    }

    private double? ResolveTakeoffFolderDefaultUnitPrice(string folderPath)
    {
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (properties.DefaultUnitPrice is >= 0)
                return properties.DefaultUnitPrice.Value;
        }

        return null;
    }

    private string ResolveTakeoffFolderDefaultItemNotes(string folderPath)
    {
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (!string.IsNullOrWhiteSpace(properties.DefaultItemNotes))
                return properties.DefaultItemNotes;
        }

        return "";
    }

    private string ResolveTakeoffFolderDefaultNamePrefix(string folderPath)
    {
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (!string.IsNullOrWhiteSpace(properties.DefaultNamePrefix))
                return properties.DefaultNamePrefix;
        }

        return "";
    }

    private void ApplyTakeoffFolderDefaultsToNewItem(TakeoffItem item, string parentFolder)
    {
        bool changed = false;
        if (ResolveTakeoffFolderDefaultUnitPrice(parentFolder) is { } unitPrice)
        {
            item.UnitPrice = unitPrice;
            changed = true;
        }

        string defaultNotes = ResolveTakeoffFolderDefaultItemNotes(parentFolder);
        if (!string.IsNullOrWhiteSpace(defaultNotes))
        {
            item.Notes = defaultNotes;
            changed = true;
        }

        if (changed)
            QueueTakeoffAutosave(item);
    }

    private IEnumerable<TakeoffFolderProperties> EnumerateTakeoffFolderProperties(string folderPath)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(folderPath))
            yield break;

        string? current = folderPath;
        while (!string.IsNullOrWhiteSpace(current) &&
               Directory.Exists(current) &&
               OurPlanCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, current))
        {
            if (TakeoffFolderPropertiesStore.TryLoad(current) != null)
                yield return TakeoffFolderPropertiesStore.Load(current);

            if (string.Equals(current, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
                yield break;
            current = Path.GetDirectoryName(current);
        }
    }
}
