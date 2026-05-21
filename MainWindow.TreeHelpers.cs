using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    // ── Tree helpers ──────────────────────────────────────────────────────────

    private void RefreshTreeItem(TakeoffItem item)
    {
        if (FindTakeoffTreeItem(item) is { } tvi)
        {
            SetTreeItemHeader(tvi, item);
            RefreshTakeoffSectionNodes(tvi, item);
        }
    }

    private void RefreshActiveTakeoffVisuals()
    {
        foreach (TreeViewItem tvi in EnumerateTakeoffTreeItems(TakeoffsTree))
        {
            if (tvi.Tag is TakeoffItem item)
                SetTreeItemHeader(tvi, item);
        }

        UpdateActiveTakeoffTargetBar();
        ApplyTakeoffPageHighlights();
    }

    // Targeted equivalent of RefreshActiveTakeoffVisuals for the
    // measurement-add path: only the edited takeoff's header changed (already
    // rebuilt via RefreshTreeItem) and only its row's "measured on this page"
    // highlight can flip. No selection/active change occurs on add, so the
    // other rows are untouched — avoiding an O(all takeoff nodes) walk on
    // every single measurement.
    private void RefreshTakeoffRowVisualsForItems(IReadOnlyCollection<TakeoffItem> items)
    {
        if (items.Count > 0)
        {
            TakeoffTreeVisualBrushes brushes = CreateTakeoffTreeVisualBrushes();
            HashSet<string> measuredOnCurrentPage = CurrentPageMeasuredTakeoffFolders();
            foreach (TakeoffItem item in items)
            {
                if (FindTakeoffTreeItem(item) is { } tvi)
                    ApplyTakeoffTreeItemVisual(tvi, measuredOnCurrentPage, brushes);
            }
        }

        UpdateActiveTakeoffTargetBar();
    }

    private void RefreshAllTotals()
    {
        RefreshTotalsRecursive(TakeoffsTree);
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        UpdateTotalDisplay();
    }

    private void RefreshSheetLegend()
    {
        if (_currentPage == null || !_settings.ShowSheetLegend)
        {
            _viewport.SetSheetLegend([]);
            return;
        }

        using (UsePageMeasurementLookup())
        {
            var entries = VisibleOrderedTakeoffsForPage(_currentPage)
                .Select(item =>
                {
                    var pageMeasurements = MeasurementsForTakeoffOnPage(item, _currentPage.FolderPath).ToList();
                    return pageMeasurements.Count == 0
                        ? null
                        : new SheetLegendEntry(
                            item.Color,
                            item.Name,
                            SheetLegendQuantityText(item, pageMeasurements),
                            SheetLegendTypeTitle(item),
                            SheetLegendTypeSign(item),
                            [],
                            TakeoffGlyphKind(item));
                })
                .Where(entry => entry != null)
                .Cast<SheetLegendEntry>()
                .ToList();

            _viewport.SetSheetLegend(entries);
        }
    }

    private string SheetLegendQuantityText(TakeoffItem item, IReadOnlyList<Measurement> measurements)
    {
        string measurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double fallbackScale = _currentPage?.ScaleMetersPerPt > 0
            ? _currentPage.ScaleMetersPerPt
            : _viewport.ScaleMetersPerPt;

        if (measurementType == "point")
            return Units.FormatCount(measurements.Sum(measurement => measurement.Points.Count));

        bool hasScale = fallbackScale > 0 || measurements.Any(measurement => measurement.ScaleMetersPerPt > 0);
        if (item.IsJoistArea)
        {
            return hasScale
                ? Units.FormatArea(measurements.Sum(measurement => measurement.AreaValue(fallbackScale)), _viewport.UnitMode)
                : $"{measurements.Sum(measurement => measurement.Points.Count)} pts";
        }

        if (!hasScale)
        {
            if (measurementType == "line")
                return $"{measurements.Sum(measurement => Math.Max(0, measurement.Points.Count - 1))} seg";
            if (measurementType == "area")
                return $"{measurements.Sum(measurement => measurement.Points.Count)} pts";
        }

        double total = measurements.Sum(measurement => measurement.Value(fallbackScale));
        return measurementType switch
        {
            "line" => Units.FormatLength(total, _viewport.UnitMode),
            "area" => Units.FormatArea(total, _viewport.UnitMode),
            _ => Units.FormatCount(total),
        };
    }

    private void RefreshTotalsRecursive(ItemsControl parent)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffItem item)
            {
                SetTreeItemHeader(tvi, item);
                RefreshTakeoffSectionNodes(tvi, item);
            }
            else
            {
                RefreshTotalsRecursive(tvi);
            }
        }
    }

    private void ApplyTakeoffPageHighlights()
    {
        TakeoffTreeVisualBrushes brushes = CreateTakeoffTreeVisualBrushes();
        HashSet<string> measuredOnCurrentPage = CurrentPageMeasuredTakeoffFolders();
        foreach (TreeViewItem item in EnumerateTakeoffTreeItems(TakeoffsTree))
            ApplyTakeoffTreeItemVisual(item, measuredOnCurrentPage, brushes);
    }

    private void RefreshTakeoffDropCueRows(params TreeViewItem?[] items)
    {
        TakeoffTreeVisualBrushes brushes = CreateTakeoffTreeVisualBrushes();
        HashSet<string> measuredOnCurrentPage = CurrentPageMeasuredTakeoffFolders();
        foreach (TreeViewItem item in items.Where(item => item != null).Distinct().Cast<TreeViewItem>())
            ApplyTakeoffTreeItemVisual(item, measuredOnCurrentPage, brushes);
    }

    private void ApplyTakeoffTreeItemVisual(
        TreeViewItem item,
        HashSet<string> measuredOnCurrentPage,
        TakeoffTreeVisualBrushes brushes)
    {
        item.ClearValue(Control.BorderBrushProperty);
        item.ClearValue(Control.BorderThicknessProperty);
        item.ClearValue(Control.FontWeightProperty);

        string? path = GetTakeoffNodePath(item);
        string? sectionKey = GetTakeoffSectionSelectionKey(item);
        bool sectionSelected = sectionKey != null && _takeoffSectionMultiSelection.Contains(sectionKey);
        bool takeoffSelected = path != null && _takeoffsMultiSelection.Contains(path);
        bool isActiveTakeoff = item.Tag is TakeoffItem activeTakeoff && IsActiveTakeoffItem(activeTakeoff);
        bool isMeasuredOnPage = item.Tag is TakeoffItem takeoff && IsTakeoffMeasuredOnCurrentPage(takeoff, measuredOnCurrentPage);
        if (ReferenceEquals(item, _takeoffSectionDropTarget))
        {
            item.Background = _takeoffSectionDropAllowed ? brushes.DropOk : brushes.DropBad;
            item.Foreground = brushes.RowForeground;
            item.FontWeight = FontWeights.Normal;
            item.BorderBrush = _takeoffSectionDropAllowed ? Brushes.SeaGreen : Brushes.IndianRed;
            item.BorderThickness = new Thickness(3, 0, 0, 0);
        }
        else if (ReferenceEquals(item, _takeoffPositionDropTarget))
        {
            item.ClearValue(Control.BackgroundProperty);
            item.ClearValue(Control.ForegroundProperty);
            item.FontWeight = FontWeights.Normal;
            item.BorderBrush = _takeoffPositionDropAllowed ? Brushes.SeaGreen : Brushes.IndianRed;
            item.BorderThickness = _takeoffPositionDropAfter
                ? new Thickness(0, 0, 0, 2)
                : new Thickness(0, 2, 0, 0);
        }
        else if (ReferenceEquals(item, _takeoffFolderDropTarget))
        {
            item.Background = _takeoffFolderDropAllowed ? brushes.DropOk : brushes.DropBad;
            item.Foreground = brushes.RowForeground;
            item.FontWeight = FontWeights.Normal;
            item.BorderBrush = _takeoffFolderDropAllowed ? Brushes.SeaGreen : Brushes.IndianRed;
            item.BorderThickness = new Thickness(3, 0, 0, 0);
        }
        else
        {
            if (sectionSelected || takeoffSelected)
            {
                item.Background = brushes.MultiSelect;
                item.Foreground = brushes.RowForeground;
            }
            else if (isMeasuredOnPage)
            {
                item.Background = brushes.OnPageBackground;
                item.Foreground = brushes.RowForeground;
            }
            else
            {
                item.ClearValue(Control.BackgroundProperty);
                item.ClearValue(Control.ForegroundProperty);
            }

            if (isActiveTakeoff)
            {
                item.Foreground = brushes.RowForeground;
                item.FontWeight = FontWeights.Normal;
                item.BorderBrush = brushes.ActiveAccent;
                item.BorderThickness = new Thickness(3, 0, 0, 0);
            }
        }
    }

    private static TakeoffTreeVisualBrushes CreateTakeoffTreeVisualBrushes()
    {
        Brush? brushOrNull(string key) => Application.Current.Resources[key] as Brush;
        return new TakeoffTreeVisualBrushes(
            brushOrNull("RowDropOkBrush") ?? new SolidColorBrush(Color.FromRgb(204, 245, 218)),
            brushOrNull("RowDropBadBrush") ?? new SolidColorBrush(Color.FromRgb(255, 214, 214)),
            brushOrNull("RowMultiSelectBrush") ?? new SolidColorBrush(Color.FromRgb(205, 226, 255)),
            brushOrNull("RowOnPageBrush") ?? new SolidColorBrush(Color.FromRgb(214, 245, 222)),
            brushOrNull("RowFlagForegroundBrush") ?? Brushes.Black,
            brushOrNull("RowActiveAccentBrush") ?? new SolidColorBrush(Color.FromRgb(31, 82, 166)));
    }

    private readonly record struct TakeoffTreeVisualBrushes(
        Brush DropOk,
        Brush DropBad,
        Brush MultiSelect,
        Brush OnPageBackground,
        Brush RowForeground,
        Brush ActiveAccent);

    private HashSet<string> CurrentPageMeasuredTakeoffFolders()
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_currentPage == null)
            return folders;

        if (TryGetIndexedTakeoffsForPage(_currentPage.FolderPath, out IReadOnlyList<TakeoffItem> indexedTakeoffs))
        {
            foreach (TakeoffItem item in indexedTakeoffs)
            {
                if (!string.IsNullOrWhiteSpace(item.FolderPath))
                    folders.Add(NormalizePathForCompare(item.FolderPath));
            }
            return folders;
        }

        string pageKey = NormalizePathForCompare(_currentPage.FolderPath);
        foreach (TakeoffItem item in _takeoffItems)
        {
            if (string.IsNullOrWhiteSpace(item.FolderPath))
                continue;

            if (item.Measurements.Any(measurement =>
                    !string.IsNullOrWhiteSpace(measurement.PageFolder) &&
                    string.Equals(NormalizePathForCompare(measurement.PageFolder), pageKey, StringComparison.OrdinalIgnoreCase)))
            {
                folders.Add(NormalizePathForCompare(item.FolderPath));
            }
        }

        return folders;
    }

    private static bool IsTakeoffMeasuredOnCurrentPage(TakeoffItem takeoff, HashSet<string> measuredTakeoffFolders) =>
        !string.IsNullOrWhiteSpace(takeoff.FolderPath) &&
        measuredTakeoffFolders.Contains(NormalizePathForCompare(takeoff.FolderPath));

    private static IEnumerable<TreeViewItem> EnumerateTakeoffTreeItems(ItemsControl parent)
    {
        foreach (TreeViewItem child in parent.Items.OfType<TreeViewItem>())
        {
            yield return child;
            foreach (TreeViewItem nested in EnumerateTakeoffTreeItems(child))
                yield return nested;
        }
    }

    private bool IsActiveTakeoffItem(TakeoffItem item)
    {
        if (_activeItem == null)
            return false;

        if (ReferenceEquals(_activeItem, item))
            return true;

        return !string.IsNullOrWhiteSpace(_activeItem.FolderPath) &&
               !string.IsNullOrWhiteSpace(item.FolderPath) &&
               string.Equals(_activeItem.FolderPath, item.FolderPath, StringComparison.OrdinalIgnoreCase);
    }

    private void SetTreeItemHeader(TreeViewItem tvi, TakeoffItem item)
    {
        bool isActive = IsActiveTakeoffItem(item);
        Brush swatchBrush = BrushFromHex(item.Color, Brushes.Gray);
        var secondaryBrush = (Brush)Application.Current.Resources["SecondaryForegroundBrush"]
            ?? new SolidColorBrush(Color.FromRgb(128, 128, 128));

        // Filled glyph in the takeoff color (no separate color square).
        var swatchHost = BuildTakeoffSwatchGlyph(item, swatchBrush, isActive ? 18 : 16);

        // Quantity goes to the right via DockPanel for ledger-style alignment.
        var dock = new DockPanel { LastChildFill = true, HorizontalAlignment = HorizontalAlignment.Stretch };

        string total = item.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode);
        var totalText = new TextBlock
        {
            Text              = total,
            Foreground        = secondaryBrush,
            FontSize          = 10,
            FontFamily        = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
            Margin            = new Thickness(8, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment     = TextAlignment.Right,
            MinWidth          = 56,
        };
        DockPanel.SetDock(totalText, Dock.Right);
        dock.Children.Add(totalText);

        if (item.Measurements.Count > 0)
        {
            var sectionsText = new TextBlock
            {
                Text              = SectionCountLabel(item),
                Foreground        = secondaryBrush,
                FontSize          = 10,
                Margin            = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(sectionsText, Dock.Right);
            dock.Children.Add(sectionsText);
        }

        var nameRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        swatchHost.Margin = new Thickness(0, 0, 6, 0);
        nameRow.Children.Add(swatchHost);
        nameRow.Children.Add(new TextBlock
        {
            Text              = item.Name,
            FontWeight        = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
        });
        dock.Children.Add(nameRow);

        tvi.Header = dock;
        tvi.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        tvi.ToolTip = TakeoffItemTooltip(item, isActive);
    }

    private static FrameworkElement BuildTakeoffSwatchGlyph(TakeoffItem item, Brush swatchBrush, double size)
    {
        // Glyph drawn in the takeoff color with a darker stroke — no separate
        // colored square, the glyph itself carries the color identity.
        return Controls.MeasurementGlyph.CreateWpf(
            TakeoffGlyphKind(item),
            swatchBrush,
            size,
            new Thickness(0));
    }

    private static MeasurementGlyphKind TakeoffGlyphKind(TakeoffItem item) =>
        Controls.MeasurementGlyph.Parse(
            OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType),
            joist: item.IsJoistArea,
            countSymbol: item.CountSymbol);

    private void SetFolderTreeItemHeader(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        TakeoffFolderProperties properties = TakeoffFolderPropertiesStore.Load(folder.FolderPath);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = folder.Name,
            FontWeight = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "  folder",
            Foreground = Brushes.Gray,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });
        string defaultSummary = TakeoffFolderDefaultSummary(properties);
        if (!string.IsNullOrWhiteSpace(defaultSummary))
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"  default: {defaultSummary}",
                Foreground = Brushes.Gray,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        tvi.Header = panel;
        tvi.ToolTip = TakeoffFolderTooltip(folder, properties);
    }

    private string TakeoffFolderStatusText(TakeoffFolderNode folder)
    {
        TakeoffFolderProperties properties = TakeoffFolderPropertiesStore.Load(folder.FolderPath);
        var parts = new List<string> { $"Folder: {folder.Name}" };
        string defaultSummary = TakeoffFolderDefaultSummary(properties);
        if (!string.IsNullOrWhiteSpace(defaultSummary))
            parts.Add($"default {defaultSummary}");
        if (!string.IsNullOrWhiteSpace(properties.Notes))
            parts.Add($"notes: {OneLinePreview(properties.Notes, 90)}");
        return string.Join(" | ", parts);
    }

    private static string? TakeoffFolderTooltip(TakeoffFolderNode folder, TakeoffFolderProperties properties)
    {
        var lines = new List<string> { folder.Name };
        string defaultSummary = TakeoffFolderDefaultSummary(properties);
        if (!string.IsNullOrWhiteSpace(defaultSummary))
            lines.Add($"Default: {defaultSummary}");
        if (!string.IsNullOrWhiteSpace(properties.Notes))
            lines.Add($"Notes: {properties.Notes}");
        return lines.Count <= 1 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string TakeoffFolderDefaultSummary(TakeoffFolderProperties properties)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(properties.DefaultMeasurementType))
            parts.Add(MeasurementTypeTitle(properties.DefaultMeasurementType));
        if (!string.IsNullOrWhiteSpace(properties.DefaultColor))
            parts.Add(properties.DefaultColor);
        if (properties.DefaultUnitPrice is >= 0)
            parts.Add($"price {properties.DefaultUnitPrice.Value:G}");
        if (!string.IsNullOrWhiteSpace(properties.DefaultNamePrefix))
            parts.Add($"prefix {properties.DefaultNamePrefix}");
        if (!string.IsNullOrWhiteSpace(properties.DefaultItemNotes))
            parts.Add($"item notes: {OneLinePreview(properties.DefaultItemNotes, 32)}");
        return string.Join(", ", parts);
    }

    private static string OneLinePreview(string value, int maxLength)
    {
        string text = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= maxLength ? text : text[..Math.Max(0, maxLength - 3)] + "...";
    }

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
        string fallbackType = OurPlaneCoreJobStore.NormalizeMeasurementType(fallback);
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (!string.IsNullOrWhiteSpace(properties.DefaultMeasurementType))
                return OurPlaneCoreJobStore.NormalizeMeasurementType(properties.DefaultMeasurementType);
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
            OurPlaneCoreJobStore.SaveTakeoffItem(item);
    }

    private IEnumerable<TakeoffFolderProperties> EnumerateTakeoffFolderProperties(string folderPath)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(folderPath))
            yield break;

        string? current = folderPath;
        while (!string.IsNullOrWhiteSpace(current) &&
               Directory.Exists(current) &&
               OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, current))
        {
            if (TakeoffFolderPropertiesStore.TryLoad(current) != null)
                yield return TakeoffFolderPropertiesStore.Load(current);

            if (string.Equals(current, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
                yield break;
            current = Path.GetDirectoryName(current);
        }
    }

    private string CurrentToolMeasurementType() =>
        IsRecordTool(_activeTool) ? RecordMeasurementType(_activeTool) :
        IsRecordTool(_lastDrawingTool) ? RecordMeasurementType(_lastDrawingTool) :
        "line";

    private void UpdateToolStatus()
    {
        string title = _activeTool switch
        {
            "point" => MeasurementTypeDisplay("point"),
            "line" => MeasurementTypeDisplay("line"),
            "area" => MeasurementTypeDisplay("area"),
            "joistarea" => "J Area",
            "select" => "Select",
            "scale" => "Scale",
            "ruler" => "Ruler",
            "drawline" => "Draw Line",
            "drawarrow" => "Arrow",
            "drawrect" => "Box",
            "drawcloud" => "Cloud",
            "drawarea" => "Area Annotation",
            "note" => "Note",
            "areacut" => "Area Cut",
            _ => "Pan",
        };
        bool recording = IsRecordTool(_activeTool);
        string item = recording && _activeItem != null
            ? $"  |  Item: {_activeItem.Name}"
            : "";
        TxtTool.Text =
            $"  Tool: {title}  |  Record: {(recording ? "On" : "Off")}" +
            $"  |  Snap: {(_viewport.SnapEnabled ? "On" : "Off")}" +
            $"  |  PDF Snap: {(_viewport.PdfSnapEnabled ? "On" : "Off")}" +
            $"  |  Ortho: {(_viewport.OrthoEnabled ? "On" : "Off")}" +
            $"  |  Box: {(_viewport.BoxModeEnabled ? "On" : "Off")}{item}";
        UpdateActiveTakeoffTargetBar();
    }

    private void UpdateActiveTakeoffTargetBar()
    {
        if (ActiveTakeoffTargetBar == null)
            return;

        ActiveTakeoffTargetBar.Visibility = Visibility.Visible;
        if (_activeItem == null)
        {
            TxtActiveTakeoffTarget.Text = "";
            TxtActiveTakeoffTargetMeta.Text = "";
            ActiveTakeoffTargetGlyphHost.Child = null;
            BtnActiveTakeoffRecord.Content = "Record";
            BtnActiveTakeoffRecord.ToolTip = "Select a takeoff item before recording (Space)";
            BtnActiveTakeoffMore.ToolTip = "Select a takeoff item for actions";
            BtnActiveTakeoffSheetNext.ToolTip = "No active takeoff item";
            BtnActiveTakeoffRecord.IsEnabled = false;
            BtnActiveTakeoffMore.IsEnabled = false;
            BtnActiveTakeoffFind.IsEnabled = false;
            BtnActiveTakeoffProperties.IsEnabled = false;
            BtnActiveTakeoffPrevious.IsEnabled = false;
            BtnActiveTakeoffNext.IsEnabled = false;
            BtnActiveTakeoffSheetNext.IsEnabled = false;
            return;
        }

        string measurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType);
        string typeTitle = TakeoffTypeDisplay(_activeItem);
        string total = _activeItem.Measurements.Count == 0
            ? "no measurements"
            : _activeItem.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode);
        string sheetTotal = ActiveTakeoffSheetTotalText(_activeItem);
        bool recordingThis = IsRecordingTakeoffItem(_activeItem);

        TxtActiveTakeoffTarget.Text = _activeItem.Name;
        TxtActiveTakeoffTargetMeta.Text = $"{TakeoffTypeTitle(_activeItem)} | total: {total}{sheetTotal}";
        ActiveTakeoffTargetGlyphHost.Child = BuildTakeoffSwatchGlyph(
            _activeItem, BrushFromHex(_activeItem.Color, Brushes.Gray), 18);
        BtnActiveTakeoffRecord.Content = recordingThis ? $"Recording {typeTitle}" : $"Record {typeTitle}";
        BtnActiveTakeoffRecord.IsEnabled = _currentPage != null;
        BtnActiveTakeoffMore.IsEnabled = true;
        BtnActiveTakeoffRecord.ToolTip = _currentPage == null
            ? "Select a sheet before recording"
            : recordingThis
                ? $"Recording {typeTitle} into {_activeItem.Name}. Click or press Space to stop."
                : $"Start recording {typeTitle} into {_activeItem.Name} (Space)";
        bool hasTreeItem = FindTakeoffTreeItem(_activeItem) != null;
        BtnActiveTakeoffFind.IsEnabled = hasTreeItem;
        BtnActiveTakeoffProperties.IsEnabled = hasTreeItem;
        bool canCycle = ActiveTakeoffTargetCycleItems().Count > 1;
        BtnActiveTakeoffPrevious.IsEnabled = canCycle;
        BtnActiveTakeoffNext.IsEnabled = canCycle;
        int sheetTargetCount = ActiveSheetTakeoffTargetCycleItems().Count;
        BtnActiveTakeoffSheetNext.IsEnabled = sheetTargetCount > 0;
        BtnActiveTakeoffSheetNext.ToolTip = sheetTargetCount > 0
            ? $"Switch through {sheetTargetCount} takeoff item(s) measured on this sheet"
            : "No takeoff items are measured on this sheet yet";
    }

    private string ActiveTakeoffSheetTotalText(TakeoffItem item)
    {
        if (_currentPage == null)
            return "";

        var pageMeasurements = MeasurementsForTakeoffOnPage(item, _currentPage.FolderPath).ToList();
        string pageQuantity = pageMeasurements.Count == 0
            ? "none on sheet"
            : SheetLegendQuantityText(item, pageMeasurements);
        return $" | sheet: {pageQuantity}";
    }

    private string DefaultTakeoffName(string measurementType)
    {
        string title = MeasurementTypeTitle(measurementType);
        if (_activeItem != null &&
            OurPlaneCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType) != measurementType)
            return $"{_activeItem.Name} - {title}";
        if (_currentPage != null)
            return $"{_currentPage.Name} {title}";
        return $"{title} Item";
    }

    private string DefaultTakeoffNameForFolder(string measurementType, string parentFolder)
    {
        string baseName = DefaultTakeoffName(measurementType);
        string prefix = ResolveTakeoffFolderDefaultNamePrefix(parentFolder);
        if (string.IsNullOrWhiteSpace(prefix) ||
            baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return baseName;
        }

        return prefix.EndsWith(" ", StringComparison.Ordinal) ||
               prefix.EndsWith("-", StringComparison.Ordinal) ||
               prefix.EndsWith("_", StringComparison.Ordinal)
            ? prefix + baseName
            : $"{prefix} {baseName}";
    }

    private static string MeasurementTypeTitle(string measurementType) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "point" => "Count",
            "area" => "Area",
            _ => "Line",
        };

    private static string TakeoffTypeTitle(TakeoffItem item) =>
        item.IsJoistArea ? "Joist" : MeasurementTypeTitle(item.MeasurementType);

    private static string MeasurementTypeSign(string measurementType) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "point" => "○",
            "area" => "□",
            _ => "╱",
        };

    private static string TakeoffTypeSign(TakeoffItem item) =>
        item.IsJoistArea ? "□╱" : MeasurementTypeSign(item.MeasurementType);

    private static string MeasurementTypeDisplay(string measurementType) =>
        MeasurementTypeTitle(measurementType);

    private static string TakeoffTypeDisplay(TakeoffItem item) =>
        TakeoffTypeTitle(item);

    private bool IsRecordingTakeoffItem(TakeoffItem item)
    {
        if (!IsRecordTool(_activeTool))
            return false;

        if (RecordMeasurementType(_activeTool) != OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType))
            return false;

        return !item.IsJoistArea || IsJoistAreaTool(_activeTool);
    }

    private static string SheetLegendTypeTitle(TakeoffItem item) =>
        item.IsJoistArea ? "Area" : TakeoffTypeTitle(item);

    private static string SheetLegendTypeSign(TakeoffItem item) =>
        item.IsJoistArea ? MeasurementTypeSign("area") : TakeoffTypeSign(item);

    private static FrameworkElement CreateTakeoffTypeIcon(TakeoffItem item, double size, Thickness margin) =>
        Controls.MeasurementGlyph.CreateWpf(
            Controls.MeasurementGlyph.Parse(
                OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType),
                joist: item.IsJoistArea,
                countSymbol: item.CountSymbol),
            BrushFromHex(item.Color, Brushes.Gray),
            size,
            margin);

    private static FrameworkElement CreateMeasurementTypeIcon(string kind, Brush brush, double size, Thickness margin) =>
        Controls.MeasurementGlyph.CreateWpf(
            Controls.MeasurementGlyph.Parse(OurPlaneCoreJobStore.NormalizeMeasurementType(kind),
                joist: kind.Equals("joist", StringComparison.OrdinalIgnoreCase)),
            brush,
            size,
            margin);

    private static FrameworkElement CreateMeasurementTypeIcon(Measurement measurement, Brush brush, double size, Thickness margin) =>
        Controls.MeasurementGlyph.CreateWpf(
            Controls.MeasurementGlyph.Parse(
                OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType),
                joist: measurement.JoistEnabled,
                countSymbol: measurement.CountSymbol),
            brush,
            size,
            margin);

    private string TakeoffUnitText(TakeoffItem item) =>
        item.IsJoistArea ? UnitText("line") : UnitText(item.MeasurementType);

    private string MeasurementUnitText(Measurement measurement) =>
        measurement.JoistEnabled ? UnitText("line") : UnitText(measurement.MType);

    private static string CsvMeasurementType(TakeoffItem item) =>
        item.IsJoistArea ? "joist" : OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);

    private TreeViewItem? FindFirstTakeoffTreeItem(ItemsControl parent)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffItem)
                return tvi;

            if (FindFirstTakeoffTreeItem(tvi) is { } found)
                return found;
        }

        return null;
    }

    private bool SelectFirstTakeoffItem()
    {
        if (FindFirstTakeoffTreeItem(TakeoffsTree) is not { } first)
        {
            _activeItem = null;
            _viewport.ActiveColor = "#FF4444";
            _viewport.ActiveTakeoffFolder = "";
            RefreshActiveTakeoffVisuals();
            return false;
        }

        first.IsSelected = true;
        first.BringIntoView();
        return true;
    }

    private void SelectTakeoffItem(TakeoffItem item)
    {
        if (FindTakeoffTreeItem(item) is { } tvi)
        {
            _takeoffsMultiSelection.Clear();
            _takeoffSectionMultiSelection.Clear();
            if (!string.IsNullOrWhiteSpace(item.FolderPath))
                _takeoffsMultiSelection.Add(item.FolderPath);
            ApplyTakeoffPageHighlights();
            tvi.IsSelected = true;
            tvi.BringIntoView();
        }
    }

    private void SelectTakeoffItemSilently(TakeoffItem item)
    {
        if (FindTakeoffTreeItem(item) is not { } tvi)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            _takeoffsMultiSelection.Clear();
            _takeoffSectionMultiSelection.Clear();
            if (!string.IsNullOrWhiteSpace(item.FolderPath))
                _takeoffsMultiSelection.Add(item.FolderPath);
            tvi.IsSelected = true;
            tvi.BringIntoView();
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }
    }

    private void SelectTakeoffItemsSilently(IReadOnlyList<TakeoffItem> items, TakeoffItem focusItem)
    {
        var paths = items
            .Select(item => item.FolderPath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0 || FindTakeoffTreeItem(focusItem) is not { } focusNode)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            _takeoffsMultiSelection.Clear();
            _takeoffSectionMultiSelection.Clear();
            foreach (string path in paths)
                _takeoffsMultiSelection.Add(path);
            focusNode.IsSelected = true;
            focusNode.BringIntoView();
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }

        ApplyTakeoffPageHighlights();
    }

    private void ActivateTakeoffItem(TakeoffItem item)
    {
        if (TryBlockTakeoffSwitchDuringRecord(item))
            return;

        item.MeasurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        _activeItem = item;
        _activeTakeoffParentFolder = Path.GetDirectoryName(item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;
        _viewport.ActiveCountSymbol = item.CountSymbol;
        SyncToolTypeForTakeoffItem(item);
    }

    private void SelectFirstTakeoffItemSilently(IReadOnlyList<TakeoffItem> items)
    {
        TreeViewItem? first = items
            .Select(FindTakeoffTreeItem)
            .FirstOrDefault(item => item != null);
        if (first == null)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            ExpandTakeoffFolderAncestorsWithoutTracking(first);
            first.IsSelected = true;
            first.BringIntoView();
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }
    }

    private IReadOnlyList<TakeoffItem> TakeoffItemsForSelection(TreeViewItem? anchor)
    {
        IReadOnlyList<TakeoffsClipboardEntry> entries = anchor == null
            ? []
            : GetSelectedTakeoffEntries(anchor);

        if (entries.Count == 0)
        {
            return anchor?.Tag switch
            {
                TakeoffItem item => [item],
                TakeoffMeasurementNode node => [node.Item],
                TakeoffFolderNode folder => TakeoffItemsInsideFolder(folder.FolderPath),
                _ => [],
            };
        }

        return _takeoffItems
            .Where(item => entries.Any(entry => OurPlaneCoreJobStore.IsSameOrDescendant(entry.SourcePath, item.FolderPath)))
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private IReadOnlyList<TakeoffItem> TakeoffItemsInsideFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return [];

        return _takeoffItems
            .Where(item => OurPlaneCoreJobStore.IsSameOrDescendant(folderPath, item.FolderPath))
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private void RevealPagesForTakeoffSelection(TreeViewItem? anchor, string? preferredPageFolder = null)
    {
        RevealPagesForTakeoffItems(TakeoffItemsForSelection(anchor), preferredPageFolder ?? _currentPage?.FolderPath);
    }

    private void RevealPagesForTakeoffItems(IReadOnlyList<TakeoffItem> items, string? preferredPageFolder = null)
    {
        HashSet<string> affectedPageKeys = PageTreePathKeysFromPageTakeoffSelection(_pageTakeoffMultiSelection);
        affectedPageKeys.UnionWith(_pagesMultiSelection.Select(NormalizePathForCompare));

        _pageTakeoffMultiSelection.Clear();
        _pagesMultiSelection.Clear();

        var selectedFolders = items
            .Select(item => item.FolderPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedFolders.Count == 0)
        {
            RefreshPageTreeRowsByFolderKeys(affectedPageKeys);
            return;
        }

        TreeViewItem? preferredLinked = null;
        TreeViewItem? firstLinked = null;
        foreach (TreeViewItem pageItem in EnumeratePageTreeItems())
        {
            if (pageItem.Tag is not PageInfo page)
                continue;

            var matchedTakeoffs = items
                .Where(item => selectedFolders.Contains(item.FolderPath) &&
                               item.Measurements.Any(measurement => IsSamePageFolder(measurement.PageFolder, page.FolderPath)))
                .ToList();
            if (matchedTakeoffs.Count == 0)
                continue;

            string pageKey = NormalizePathForCompare(page.FolderPath);
            affectedPageKeys.Add(pageKey);
            bool isPreferredPage = !string.IsNullOrWhiteSpace(preferredPageFolder) &&
                                   IsSamePageFolder(page.FolderPath, preferredPageFolder);

            foreach (TakeoffItem takeoff in matchedTakeoffs)
            {
                _pageTakeoffMultiSelection.Add(PageTakeoffSelectionKey(new PageTakeoffNode(page, takeoff)));
                TreeViewItem? linked = FindPageTakeoffTreeItem(pageItem, takeoff.FolderPath);
                firstLinked ??= linked;
                if (isPreferredPage)
                    preferredLinked ??= linked;
            }
        }

        RefreshPageTreeRowsByFolderKeys(affectedPageKeys);
        preferredLinked ??= firstLinked;

        if (preferredLinked == null)
        {
            if (_currentPage != null)
                SelectPageTreeNodeSilently(_currentPage.FolderPath);
            return;
        }

        _syncingPageTreeSelection = true;
        try
        {
            ExpandTreeItemAndAncestorsWithoutTracking(preferredLinked);
            BringPageTreeItemIntoCenteredView(preferredLinked);
        }
        finally
        {
            _syncingPageTreeSelection = false;
        }
    }

    private void SelectTakeoffSectionNode(Measurement measurement)
    {
        if (FindTakeoffSectionTreeItem(TakeoffsTree, measurement) is not { } tvi)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            TreeViewItem visibleTarget = TakeoffVisibleSelectionTarget(tvi);
            ExpandTakeoffFolderAncestorsWithoutTracking(visibleTarget);
            visibleTarget.IsSelected = true;
            visibleTarget.BringIntoView();
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }
    }

    private void ExpandTakeoffFolderAncestorsWithoutTracking(TreeViewItem item)
    {
        WithTreeExpansionTrackingSuppressed(() =>
        {
            ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(item);
            while (parent is TreeViewItem parentItem)
            {
                if (parentItem.Tag is not TakeoffItem)
                    parentItem.IsExpanded = true;
                parent = ItemsControl.ItemsControlFromItemContainer(parentItem);
            }
        });
    }

    private static TreeViewItem TakeoffVisibleSelectionTarget(TreeViewItem item)
    {
        if (item.Tag is TakeoffMeasurementNode &&
            ItemsControl.ItemsControlFromItemContainer(item) is TreeViewItem parentTakeoff &&
            !parentTakeoff.IsExpanded)
        {
            return parentTakeoff;
        }

        return item;
    }

    private TreeViewItem? FindTakeoffTreeItem(TakeoffItem item) =>
        !string.IsNullOrWhiteSpace(item.FolderPath) &&
        FindTakeoffTreeItemByFolder(item.FolderPath) is { } indexedItem
            ? indexedItem
            : FindTakeoffTreeItem(TakeoffsTree, item);

    private TreeViewItem? FindTakeoffTreeItem(ItemsControl parent, TakeoffItem item)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffItem candidate && ReferenceEquals(candidate, item))
                return tvi;

            if (FindTakeoffTreeItem(tvi, item) is { } found)
                return found;
        }

        return null;
    }

    private TreeViewItem? FindTakeoffSectionTreeItem(ItemsControl parent, Measurement measurement)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffMeasurementNode node && ReferenceEquals(node.Measurement, measurement))
                return tvi;

            if (FindTakeoffSectionTreeItem(tvi, measurement) is { } found)
                return found;
        }

        return null;
    }

    private TreeViewItem? FindTakeoffTreeItemByFolder(string folderPath) =>
        FindTakeoffTreeItemByFolderIndexed(folderPath);

    private void SelectTakeoffNodeByFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        if (FindTakeoffTreeItemByFolder(folderPath) is { } item)
        {
            item.IsSelected = true;
            item.IsExpanded = true;
            item.BringIntoView();
        }
    }

    private TreeViewItem? FindTakeoffTreeItemByFolder(ItemsControl parent, string folderPath)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            string? current = tvi.Tag switch
            {
                TakeoffItem item => item.FolderPath,
                TakeoffFolderNode folder => folder.FolderPath,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(current) &&
                string.Equals(current, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return tvi;
            }

            if (FindTakeoffTreeItemByFolder(tvi, folderPath) is { } found)
                return found;
        }

        return null;
    }

    private void RemoveTreeItem(TreeViewItem tvi)
    {
        UnregisterTakeoffTreeItemSubtree(tvi);
        ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(tvi);
        parent?.Items.Remove(tvi);
    }

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

            OurPlaneCoreJobStore.SaveTakeoffItem(group.Key);
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
        string entry = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point" ? "Count" : "Section";
        return string.IsNullOrWhiteSpace(page)
            ? $"{entry} {index + 1}"
            : $"{entry} {index + 1} - {page}";
    }

    private static string SectionPageName(Measurement measurement) =>
        string.IsNullOrWhiteSpace(measurement.PageFolder)
            ? ""
            : OurPlaneCoreJobStore.DisplayName(measurement.PageFolder);

    private static string SectionCountLabel(TakeoffItem item) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point"
            ? item.Measurements.Count == 1 ? "1 count" : $"{item.Measurements.Count} counts"
            : item.Measurements.Count == 1 ? "1 section" : $"{item.Measurements.Count} sections";

    private static string MeasurementEntryTitle(TakeoffItem item) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point" ? "Count" : "Section";

    private static string MeasurementEntryTitlePlural(IEnumerable<TakeoffMeasurementNode> nodes)
    {
        var types = nodes
            .Select(node => OurPlaneCoreJobStore.NormalizeMeasurementType(node.Item.MeasurementType))
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
                : OurPlaneCoreJobStore.DisplayName(m.PageFolder);
            string name = string.IsNullOrWhiteSpace(m.Name)
                ? (OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point" ? $"Count {i + 1}" : $"Section {i + 1}")
                : m.Name;
            string detail = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point"
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

    private string QuantityText(TakeoffItem item)
    {
        string mt = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double value = item.Total(_viewport.ScaleMetersPerPt);
        if (item.IsJoistArea)
            return QuantityText("line", value);
        return QuantityText(mt, value);
    }

    private string QuantityText(Measurement measurement)
    {
        string mt = OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType);
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
        string mt = OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType);
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
        string mt = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
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
        string mt = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
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
