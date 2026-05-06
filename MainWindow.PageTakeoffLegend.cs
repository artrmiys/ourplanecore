using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void RebuildPageTakeoffNodes(TreeViewItem pageItem, PageInfo page)
    {
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
            child.ContextMenu = BuildPageTakeoffContextMenu(node);
            pageItem.Items.Add(child);
            legendIndex++;
        }

        if (!string.IsNullOrWhiteSpace(page.OverlayPageFolder))
            pageItem.Items.Add(CreatePageOverlayTreeItem(page));
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

        var dock = new DockPanel { LastChildFill = true };
        var transform = new TextBlock
        {
            Text = $"{page.OverlayScale:0.###}x  {page.OverlayOffsetXPt:0.#},{page.OverlayOffsetYPt:0.#}",
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
            Background = swatchBrush,
            BorderBrush = secondaryBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(28, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var nameRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameRow.Children.Add(colorBox);
        nameRow.Children.Add(new TextBlock
        {
            Text = $"Overlay: {overlayName}",
            FontWeight = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        dock.Children.Add(nameRow);
        dock.ToolTip = "Sheet overlay. Right-click to move, scale, recolor, or clear.";
        return dock;
    }

    private FrameworkElement BuildPageTakeoffHeader(PageInfo page, TakeoffItem takeoff, int legendIndex)
    {
        bool isActive = IsActivePageTakeoff(page, takeoff);
        var secondaryBrush = (Brush)Application.Current.Resources["SecondaryForegroundBrush"]
            ?? new SolidColorBrush(Color.FromRgb(128, 128, 128));
        Brush swatchBrush = BrushFromHex(takeoff.Color, Brushes.Gray);

        var dock = new DockPanel { LastChildFill = true };

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
            $"Legend position: {legendIndex + 1}" + Environment.NewLine +
            "Linked to the real Takeoffs item. Use Move Up/Down here only to change this sheet's legend order.";
        return dock;
    }

    private IEnumerable<TakeoffItem> TakeoffsForPage(string pageFolder) =>
        _takeoffItems
            .Where(item => item.Measurements.Any(m =>
                IsSamePageFolder(m.PageFolder, pageFolder)))
            .GroupBy(TakeoffLegendOrderKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());

    private IEnumerable<Measurement> MeasurementsForTakeoffOnPage(TakeoffItem item, string pageFolder) =>
        item.Measurements.Where(measurement => IsSamePageFolder(measurement.PageFolder, pageFolder));

    private IReadOnlyList<TakeoffItem> OrderedTakeoffsForPage(PageInfo page)
    {
        var takeoffs = TakeoffsForPage(page.FolderPath).ToList();
        if (takeoffs.Count <= 1)
            return takeoffs;

        var byKey = takeoffs
            .GroupBy(TakeoffLegendOrderKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<TakeoffItem>();

        foreach (string storedKey in page.LegendTakeoffOrder.Select(NormalizeTakeoffLegendOrderKey))
        {
            if (string.IsNullOrWhiteSpace(storedKey) || !byKey.TryGetValue(storedKey, out TakeoffItem? takeoff))
                continue;
            if (!used.Add(storedKey))
                continue;

            ordered.Add(takeoff);
        }

        ordered.AddRange(takeoffs
            .Where(takeoff => !used.Contains(TakeoffLegendOrderKey(takeoff)))
            .OrderBy(takeoff => MeasurementTypeTitle(takeoff.MeasurementType), StringComparer.OrdinalIgnoreCase)
            .ThenBy(takeoff => takeoff.Name, StringComparer.OrdinalIgnoreCase));

        return ordered;
    }

    private string TakeoffLegendOrderKey(TakeoffItem item) =>
        NormalizeTakeoffLegendOrderKey(item.FolderPath);

    private string NormalizeTakeoffLegendOrderKey(string value)
    {
        string clean = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return "";

        if (_currentJob != null && Path.IsPathFullyQualified(clean))
        {
            string full = NormalizePath(clean);
            if (OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, full))
                clean = Path.GetRelativePath(_currentJob.TakeoffsRoot, full);
        }

        return clean.Replace('\\', '/').Trim('/');
    }

    private void SavePageLegendOrder(PageInfo page, IReadOnlyList<TakeoffItem> orderedTakeoffs)
    {
        var order = orderedTakeoffs
            .Select(TakeoffLegendOrderKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        page.LegendTakeoffOrder = order;
        if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
            _currentPage.LegendTakeoffOrder = order.ToList();
        OurPlaneCoreJobStore.SavePageLegendTakeoffOrder(page.FolderPath, order);
    }

    private void RebasePageLegendTakeoffOrderReferences(string oldPath, string newPath)
    {
        RebaseExpandedTreePaths(_expandedTakeoffTreePaths, oldPath, newPath);

        if (_currentJob == null)
            return;

        string oldKey = NormalizeTakeoffLegendOrderKey(oldPath);
        string newKey = NormalizeTakeoffLegendOrderKey(newPath);
        if (string.IsNullOrWhiteSpace(oldKey) ||
            string.IsNullOrWhiteSpace(newKey) ||
            string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (PageInfo page in CollectPagesUnder(_currentJob.PagesRoot))
        {
            if (page.LegendTakeoffOrder.Count == 0)
                continue;

            var updated = page.LegendTakeoffOrder
                .Select(key => RebaseTakeoffLegendOrderKey(key, oldKey, newKey))
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool changed = updated.Count != page.LegendTakeoffOrder.Count ||
                           updated.Where((key, index) => !string.Equals(
                               key,
                               NormalizeTakeoffLegendOrderKey(page.LegendTakeoffOrder[index]),
                               StringComparison.OrdinalIgnoreCase)).Any();
            if (!changed)
                continue;

            page.LegendTakeoffOrder = updated;
            OurPlaneCoreJobStore.SavePageLegendTakeoffOrder(page.FolderPath, updated);
            if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
                _currentPage.LegendTakeoffOrder = updated.ToList();
        }
    }

    private string RebaseTakeoffLegendOrderKey(string key, string oldPrefix, string newPrefix)
    {
        string clean = NormalizeTakeoffLegendOrderKey(key);
        if (string.Equals(clean, oldPrefix, StringComparison.OrdinalIgnoreCase))
            return newPrefix;

        string prefix = oldPrefix.TrimEnd('/') + "/";
        if (clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return newPrefix.TrimEnd('/') + clean[(prefix.Length - 1)..];

        return clean;
    }
}
