using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlanCore;

public partial class MainWindow
{
    // Page takeoff node rebuilding, headers, ordering, and measurement lookup.

    private const string PageOverlayVisibilityToggleTag = "PageOverlayVisibilityToggle";
    private const string PageTakeoffVisibilityToggleTag = "PageTakeoffVisibilityToggle";
    private const double PageTakeoffGlyphSize = 14;
    private const double PageTakeoffActiveGlyphSize = PageTakeoffGlyphSize;
    private const double PageTakeoffGlyphHostSize = PageTakeoffGlyphSize;
    private const double PageTakeoffActiveGlyphHostSize = PageTakeoffActiveGlyphSize;

    private void RebuildPageTakeoffNodes(TreeViewItem pageItem, PageInfo page)
    {
        foreach (TreeViewItem child in pageItem.Items.OfType<TreeViewItem>())
            UnregisterPageTreeItemSubtree(child);

        pageItem.Items.Clear();
        IReadOnlyList<TakeoffItem> orderedTakeoffs = OrderedTakeoffsForPage(page);
        var addedTakeoffs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TakeoffItem takeoff in orderedTakeoffs)
        {
            if (!addedTakeoffs.Add(TakeoffLegendOrderKey(takeoff)))
                continue;

            var node = new PageTakeoffNode(page, takeoff);
            var child = new TreeViewItem
            {
                Header = BuildPageTakeoffHeader(page, takeoff),
                Tag = node,
                MinHeight = 0,
                Padding = new Thickness(0),
            };
            AttachLazyPageTakeoffContextMenu(child, node);
            pageItem.Items.Add(child);
            RegisterPageTreeItemSubtree(child);
        }

        if (IsModuleEnabled(ModuleId.SheetOverlay))
        {
            foreach (SheetOverlayLayerInfo layer in page.OverlayLayers)
            {
                TreeViewItem overlayItem = CreatePageOverlayTreeItem(page, layer);
                pageItem.Items.Add(overlayItem);
                RegisterPageTreeItemSubtree(overlayItem);
            }
        }
    }

    private TreeViewItem CreatePageOverlayTreeItem(
        PageInfo page,
        SheetOverlayLayerInfo layer)
    {
        string overlayName = OverlayPageName(layer.SourcePageFolder);
        var node = new PageOverlayNode(page, layer, overlayName);
        return new TreeViewItem
        {
            Header = BuildPageOverlayHeader(page, layer, overlayName),
            Tag = node,
            ContextMenu = BuildPageOverlayContextMenu(node),
        };
    }

    private FrameworkElement BuildPageOverlayHeader(
        PageInfo page,
        SheetOverlayLayerInfo layer,
        string overlayName)
    {
        var secondaryBrush = (Brush)Application.Current.Resources["SecondaryForegroundBrush"]
            ?? new SolidColorBrush(Color.FromRgb(128, 128, 128));
        Brush swatchBrush = BrushFromHex(layer.Color, Brushes.Gray);
        bool isActive = string.Equals(
            page.ActiveOverlayId,
            layer.Id,
            StringComparison.OrdinalIgnoreCase);

        var dock = new DockPanel
        {
            LastChildFill = true,
            Opacity = layer.IsVisible ? 1.0 : 0.58,
        };
        var transform = new TextBlock
        {
            Text = layer.IsVisible
                ? $"{layer.Scale:0.###}x  {layer.Opacity * 100:0}%"
                : "hidden",
            Foreground = secondaryBrush,
            FontSize = 10,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
            Margin = new Thickness(8, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            MinWidth = 76,
        };
        DockPanel.SetDock(transform, Dock.Right);
        dock.Children.Add(transform);

        var colorBox = new Border
        {
            Width = 12,
            Height = 12,
            Background = layer.IsVisible ? swatchBrush : Brushes.Transparent,
            BorderBrush = isActive
                ? new SolidColorBrush(Color.FromRgb(0xF4, 0x9B, 0x24))
                : secondaryBrush,
            BorderThickness = new Thickness(isActive ? 2 : 1),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var nameRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameRow.Children.Add(BuildPageOverlayVisibilityDot(page, layer, swatchBrush));
        nameRow.Children.Add(colorBox);
        nameRow.Children.Add(new TextBlock
        {
            Text = $"{(isActive ? "● " : "")}Overlay: {overlayName}",
            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        dock.Children.Add(nameRow);
        dock.ToolTip = layer.IsVisible
            ? "Overlay layer. Click to activate; right-click for layer actions."
            : "Overlay layer is hidden. Click the dot to show it.";
        return dock;
    }

    private FrameworkElement BuildPageOverlayVisibilityDot(
        PageInfo page,
        SheetOverlayLayerInfo layer,
        Brush swatchBrush)
    {
        var dot = new Border
        {
            Width = 11,
            Height = 11,
            CornerRadius = new CornerRadius(6),
            Background = layer.IsVisible ? swatchBrush : Brushes.Transparent,
            BorderBrush = swatchBrush,
            BorderThickness = new Thickness(1.5),
            Margin = new Thickness(2, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Tag = PageOverlayVisibilityToggleTag,
            ToolTip = layer.IsVisible
                ? $"Hide overlay on {page.Name}"
                : $"Show overlay on {page.Name}",
        };
        dot.PreviewMouseLeftButtonDown += (_, e) =>
        {
            TogglePageOverlayVisibility(page, layer.Id);
            e.Handled = true;
        };
        return dot;
    }

    private FrameworkElement BuildPageTakeoffHeader(PageInfo page, TakeoffItem takeoff)
    {
        bool isActive = IsActivePageTakeoff(page, takeoff);
        var secondaryBrush = (Brush)Application.Current.Resources["SecondaryForegroundBrush"]
            ?? new SolidColorBrush(Color.FromRgb(128, 128, 128));
        Brush swatchBrush = BrushFromHex(takeoff.Color, Brushes.Gray);

        bool isVisible = IsPageTakeoffVisible(page, takeoff);
        var dock = new DockPanel { LastChildFill = true, Opacity = isVisible ? 1.0 : 0.58 };

        var pageMeasurements = MeasurementsForTakeoffOnPage(takeoff, page.FolderPath).ToList();
        if (pageMeasurements.Count > 0)
        {
            var qty = new TextBlock
            {
                Text              = SheetLegendQuantityText(takeoff, pageMeasurements),
                Foreground        = secondaryBrush,
                FontSize          = 9,
                FontFamily        = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
                Margin            = new Thickness(6, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment     = TextAlignment.Right,
                MinWidth          = 52,
            };
            DockPanel.SetDock(qty, Dock.Right);
            dock.Children.Add(qty);
        }

        var nameRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameRow.Children.Add(BuildPageTakeoffVisibilityGlyph(page, takeoff, swatchBrush, secondaryBrush, isVisible, isActive));
        nameRow.Children.Add(new TextBlock
        {
            Text              = takeoff.Name,
            FontWeight        = FontWeights.Normal,
            FontSize          = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
        });
        dock.Children.Add(nameRow);

        dock.ToolTip =
            $"{(isVisible ? "Visible" : "Hidden")} on this sheet. Click the symbol to toggle." + Environment.NewLine +
            "Linked to the real Takeoffs item. Right-click to change this sheet's legend order.";
        return dock;
    }

    private FrameworkElement BuildPageTakeoffVisibilityGlyph(
        PageInfo page,
        TakeoffItem takeoff,
        Brush swatchBrush,
        Brush secondaryBrush,
        bool isVisible,
        bool isActive)
    {
        FrameworkElement glyph = BuildTakeoffSwatchGlyph(
            takeoff,
            swatchBrush,
            isActive ? PageTakeoffActiveGlyphSize : PageTakeoffGlyphSize);
        glyph.Opacity = isVisible ? 1.0 : 0.34;
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;

        var host = new Border
        {
            Width = isActive ? PageTakeoffActiveGlyphHostSize : PageTakeoffGlyphHostSize,
            Height = isActive ? PageTakeoffActiveGlyphHostSize : PageTakeoffGlyphHostSize,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 6, 0),
            Child = glyph,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            Tag = PageTakeoffVisibilityToggleTag,
            ToolTip = isVisible
                ? $"Hide {takeoff.Name} on {page.Name}"
                : $"Show {takeoff.Name} on {page.Name}",
        };
        return host;
    }

    private IEnumerable<TakeoffItem> TakeoffsForPage(string pageFolder)
    {
        if (TryGetIndexedTakeoffsForPage(pageFolder, out IReadOnlyList<TakeoffItem> indexedTakeoffs))
            return indexedTakeoffs;

        return _takeoffItems
            .Where(item => !string.IsNullOrWhiteSpace(item.FolderPath))
            .Where(item => item.Measurements.Any(m =>
                IsSamePageFolder(m.PageFolder, pageFolder)))
            .GroupBy(TakeoffLegendOrderKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
    }

    private IEnumerable<Measurement> MeasurementsForTakeoffOnPage(TakeoffItem item, string pageFolder)
    {
        if (TryGetIndexedMeasurementsForTakeoffOnPage(item, pageFolder, out IReadOnlyList<Measurement> indexedMeasurements))
            return indexedMeasurements;

        return item.Measurements.Where(measurement => IsSamePageFolder(measurement.PageFolder, pageFolder));
    }

    private IReadOnlyList<TakeoffItem> OrderedTakeoffsForPage(PageInfo page)
    {
        var takeoffs = TakeoffsForPage(page.FolderPath).ToList();
        if (takeoffs.Count <= 1)
            return takeoffs;

        if (IsPageLegendManual(page) && page.LegendTakeoffOrder.Count > 0)
            return ManualOrderedTakeoffsForPage(page, takeoffs);

        return AutoOrderTakeoffs(takeoffs);
    }

    private IReadOnlyList<TakeoffItem> ManualOrderedTakeoffsForPage(PageInfo page, IReadOnlyList<TakeoffItem> takeoffs)
    {
        var byKey = takeoffs
            .GroupBy(TakeoffLegendOrderKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var ordered = new List<TakeoffItem>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string stored in page.LegendTakeoffOrder.Select(NormalizeTakeoffLegendOrderKey))
        {
            if (string.IsNullOrWhiteSpace(stored) ||
                !used.Add(stored) ||
                !byKey.TryGetValue(stored, out TakeoffItem? takeoff))
            {
                continue;
            }

            ordered.Add(takeoff);
        }

        IEnumerable<TakeoffItem> remaining = takeoffs
            .Where(takeoff => !used.Contains(TakeoffLegendOrderKey(takeoff)));
        ordered.AddRange(AutoOrderTakeoffs(remaining));
        return ordered;
    }

    private static List<TakeoffItem> AutoOrderTakeoffs(IEnumerable<TakeoffItem> takeoffs) =>
        TakeoffAutoRoutingService.SortPageLegendItems(takeoffs).ToList();

    private IReadOnlyList<TakeoffItem> VisibleOrderedTakeoffsForPage(PageInfo page) =>
        OrderedTakeoffsForPage(page)
            .Where(takeoff => IsPageTakeoffVisible(page, takeoff))
            .ToList();

    private IReadOnlyList<TakeoffItem> LayerOrderedTakeoffsForPage(PageInfo page)
    {
        var defaultOrder = DefaultLayerOrderedTakeoffsForPage(page).ToList();
        if (defaultOrder.Count <= 1 || page.TakeoffLayerOrder.Count == 0)
            return defaultOrder;

        var byKey = defaultOrder
            .GroupBy(TakeoffLegendOrderKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var result = new List<TakeoffItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string stored in page.TakeoffLayerOrder.Select(NormalizeTakeoffLegendOrderKey))
        {
            if (!string.IsNullOrWhiteSpace(stored) &&
                seen.Add(stored) &&
                byKey.TryGetValue(stored, out TakeoffItem? takeoff))
            {
                result.Add(takeoff);
            }
        }

        foreach (TakeoffItem takeoff in defaultOrder)
        {
            string key = TakeoffLegendOrderKey(takeoff);
            if (seen.Add(key))
                result.Add(takeoff);
        }

        return result;
    }

    private IEnumerable<TakeoffItem> DefaultLayerOrderedTakeoffsForPage(PageInfo page) =>
        TakeoffsForPage(page.FolderPath)
            .Select((takeoff, index) => (Takeoff: takeoff, Index: index))
            .OrderBy(entry => DefaultTakeoffLayerRank(entry.Takeoff))
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Takeoff);

    private static int DefaultTakeoffLayerRank(TakeoffItem takeoff) =>
        OurPlanCoreJobStore.NormalizeMeasurementType(takeoff.MeasurementType) switch
        {
            "area" => 0,
            "line" => 1,
            "point" => 2,
            _ => 1,
        };
}
