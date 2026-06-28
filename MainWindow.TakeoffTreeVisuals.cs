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
    // Takeoff tree row visuals, headers, and sheet legend.

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
                    var pageMeasurements = MeasurementsForTakeoffOnPage(item, _currentPage.FolderPath)
                        .Where(measurement => !IsMeasurementHiddenByPageSnapshot(_currentPage, measurement))
                        .ToList();
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

    // Single-stroke folder in the theme text color, per
    // docs/60-ux-ui/BLUEBEAM_DESIGN_SYSTEM.md §7. Filled when the folder holds
    // any real content (page/takeoff item anywhere in its subtree); a hollow
    // outline when it is empty or only contains other empty folders.
    private static FrameworkElement CreateFolderTreeIcon(bool filled)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Width = 7.5,
            Height = 6.5,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.1,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            SnapsToDevicePixels = true,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Data = Geometry.Parse("M2.5,12.5 L2.5,4.8 L6,4.8 L7.5,6.3 L13.5,6.3 L13.5,12.5 Z M2.5,6.3 L7.5,6.3"),
        };
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ControlForegroundBrush");
        if (filled)
            icon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ControlForegroundBrush");
        return icon;
    }

    // True when the takeoff folder subtree contains at least one takeoff item
    // (a folder with measurements.json). Empty nested folders do not count.
    private static bool TakeoffFolderHasContent(string folderPath)
    {
        try
        {
            return !string.IsNullOrEmpty(folderPath)
                && Directory.Exists(folderPath)
                && Directory.EnumerateFiles(folderPath, "measurements.json", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    private void SetFolderTreeItemHeader(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        TakeoffFolderProperties properties = TakeoffFolderPropertiesStore.Load(folder.FolderPath);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(CreateFolderTreeIcon(TakeoffFolderHasContent(folder.FolderPath)));
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
}
