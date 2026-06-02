using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlaneCore;

public partial class MainWindow
{
    // Page takeoff node rebuilding, headers, ordering, and measurement lookup.

    private const string PageOverlayVisibilityToggleTag = "PageOverlayVisibilityToggle";

    private void RebuildPageTakeoffNodes(TreeViewItem pageItem, PageInfo page)
    {
        foreach (TreeViewItem child in pageItem.Items.OfType<TreeViewItem>())
            UnregisterPageTreeItemSubtree(child);

        pageItem.Items.Clear();
        IReadOnlyList<TakeoffItem> orderedTakeoffs = OrderedTakeoffsForPage(page);
        var addedTakeoffs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int legendIndex = 0;
        foreach (TakeoffItem takeoff in orderedTakeoffs)
        {
            if (!addedTakeoffs.Add(TakeoffLegendOrderKey(takeoff)))
                continue;

            var node = new PageTakeoffNode(page, takeoff);
            var child = new TreeViewItem
            {
                Header = BuildPageTakeoffHeader(page, takeoff, legendIndex),
                Tag = node,
            };
            AttachLazyPageTakeoffContextMenu(child, node);
            pageItem.Items.Add(child);
            RegisterPageTreeItemSubtree(child);
            legendIndex++;
        }

        if (!string.IsNullOrWhiteSpace(page.OverlayPageFolder))
        {
            TreeViewItem overlayItem = CreatePageOverlayTreeItem(page);
            pageItem.Items.Add(overlayItem);
            RegisterPageTreeItemSubtree(overlayItem);
        }
    }

    private TreeViewItem CreatePageOverlayTreeItem(PageInfo page)
    {
        string overlayName = OverlayPageName(page);
        var node = new PageOverlayNode(page, overlayName);
        return new TreeViewItem
        {
            Header = BuildPageOverlayHeader(page, overlayName),
            Tag = node,
            ContextMenu = BuildPageOverlayContextMenu(node),
        };
    }

    private FrameworkElement BuildPageOverlayHeader(PageInfo page, string overlayName)
    {
        var secondaryBrush = (Brush)Application.Current.Resources["SecondaryForegroundBrush"]
            ?? new SolidColorBrush(Color.FromRgb(128, 128, 128));
        Brush swatchBrush = BrushFromHex(page.OverlayColor, Brushes.Gray);

        var dock = new DockPanel { LastChildFill = true, Opacity = page.OverlayVisible ? 1.0 : 0.58 };
        var transform = new TextBlock
        {
            Text = page.OverlayVisible
                ? $"{page.OverlayScale:0.###}x  {page.OverlayOffsetXPt:0.#},{page.OverlayOffsetYPt:0.#}"
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
            Background = page.OverlayVisible ? swatchBrush : Brushes.Transparent,
            BorderBrush = secondaryBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var nameRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameRow.Children.Add(BuildPageOverlayVisibilityDot(page, swatchBrush));
        nameRow.Children.Add(colorBox);
        nameRow.Children.Add(new TextBlock
        {
            Text = $"Overlay: {overlayName}",
            FontWeight = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        dock.Children.Add(nameRow);
        dock.ToolTip = page.OverlayVisible
            ? "Sheet overlay. Right-click to hide, move, scale, recolor, or clear."
            : "Sheet overlay is hidden. Right-click to show it.";
        return dock;
    }

    private FrameworkElement BuildPageOverlayVisibilityDot(PageInfo page, Brush swatchBrush)
    {
        var dot = new Border
        {
            Width = 11,
            Height = 11,
            CornerRadius = new CornerRadius(6),
            Background = page.OverlayVisible ? swatchBrush : Brushes.Transparent,
            BorderBrush = swatchBrush,
            BorderThickness = new Thickness(1.5),
            Margin = new Thickness(2, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Tag = PageOverlayVisibilityToggleTag,
            ToolTip = page.OverlayVisible
                ? $"Hide overlay on {page.Name}"
                : $"Show overlay on {page.Name}",
        };
        dot.PreviewMouseLeftButtonDown += (_, e) =>
        {
            TogglePageOverlayVisibility(page);
            e.Handled = true;
        };
        return dot;
    }

    private FrameworkElement BuildPageTakeoffHeader(PageInfo page, TakeoffItem takeoff, int legendIndex)
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
                FontSize          = 10,
                FontFamily        = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
                Margin            = new Thickness(8, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment     = TextAlignment.Right,
                MinWidth          = 56,
            };
            DockPanel.SetDock(qty, Dock.Right);
            dock.Children.Add(qty);
        }

        var indexText = new TextBlock
        {
            Text              = $"{legendIndex + 1}.",
            Width             = 22,
            TextAlignment     = TextAlignment.Right,
            Foreground        = secondaryBrush,
            FontSize          = 10,
            Margin            = new Thickness(2, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var swatchHost = BuildTakeoffSwatchGlyph(takeoff, swatchBrush, isActive ? 16 : 14);
        swatchHost.Margin = new Thickness(0, 0, 6, 0);

        var nameRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameRow.Children.Add(BuildPageTakeoffVisibilityDot(page, takeoff, swatchBrush, isVisible));
        nameRow.Children.Add(indexText);
        nameRow.Children.Add(swatchHost);
        nameRow.Children.Add(new TextBlock
        {
            Text              = takeoff.Name,
            FontWeight        = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
        });
        dock.Children.Add(nameRow);

        dock.ToolTip =
            $"{(isVisible ? "Visible" : "Hidden")} on this sheet. Click the dot to toggle." + Environment.NewLine +
            $"Legend position: {legendIndex + 1}" + Environment.NewLine +
            "Linked to the real Takeoffs item. Use Move Up/Down here only to change this sheet's legend order.";
        return dock;
    }

    private FrameworkElement BuildPageTakeoffVisibilityDot(
        PageInfo page,
        TakeoffItem takeoff,
        Brush swatchBrush,
        bool isVisible)
    {
        var dot = new Border
        {
            Width = 11,
            Height = 11,
            CornerRadius = new CornerRadius(6),
            Background = isVisible ? swatchBrush : Brushes.Transparent,
            BorderBrush = swatchBrush,
            BorderThickness = new Thickness(1.5),
            Margin = new Thickness(2, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = isVisible
                ? $"Hide {takeoff.Name} on {page.Name}"
                : $"Show {takeoff.Name} on {page.Name}",
        };
        dot.PreviewMouseLeftButtonDown += (_, e) =>
        {
            TogglePageTakeoffVisibility(page, takeoff);
            e.Handled = true;
        };
        return dot;
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

        return AutoOrderTakeoffs(takeoffs);
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
        OurPlaneCoreJobStore.NormalizeMeasurementType(takeoff.MeasurementType) switch
        {
            "area" => 0,
            "line" => 1,
            "point" => 2,
            _ => 1,
        };
}
