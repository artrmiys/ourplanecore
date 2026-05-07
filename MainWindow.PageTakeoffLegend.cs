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

    private IReadOnlyList<TakeoffItem> VisibleOrderedTakeoffsForPage(PageInfo page) =>
        OrderedTakeoffsForPage(page)
            .Where(takeoff => IsPageTakeoffVisible(page, takeoff))
            .ToList();

    private bool IsPageTakeoffVisible(PageInfo page, TakeoffItem takeoff)
    {
        string key = TakeoffLegendOrderKey(takeoff);
        if (string.IsNullOrWhiteSpace(key))
            return true;

        return !page.HiddenTakeoffs
            .Select(NormalizeTakeoffLegendOrderKey)
            .Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    private void TogglePageTakeoffVisibility(PageInfo page, TakeoffItem takeoff)
    {
        string key = TakeoffLegendOrderKey(takeoff);
        if (string.IsNullOrWhiteSpace(key))
            return;

        var hidden = page.HiddenTakeoffs
            .Select(NormalizeTakeoffLegendOrderKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool nowHidden;
        if (hidden.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            hidden.RemoveAll(value => string.Equals(value, key, StringComparison.OrdinalIgnoreCase));
            nowHidden = false;
        }
        else
        {
            hidden.Add(key);
            nowHidden = true;
        }

        page.HiddenTakeoffs = hidden;
        if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
            _currentPage.HiddenTakeoffs = hidden.ToList();
        OurPlaneCoreJobStore.SavePageHiddenTakeoffs(page.FolderPath, hidden);
        RefreshPageOverlayTreeNode(page);
        ApplyViewportPageTakeoffVisibility(page);
        RefreshSheetLegend();
        TxtStatus.Text = nowHidden
            ? $"Hidden on {page.Name}: {takeoff.Name}."
            : $"Visible on {page.Name}: {takeoff.Name}.";
    }

    private void ApplyViewportPageTakeoffVisibility(PageInfo page)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
            return;

        var hiddenKeys = page.HiddenTakeoffs
            .Select(NormalizeTakeoffLegendOrderKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hiddenFolders = _takeoffItems
            .Where(item => hiddenKeys.Contains(TakeoffLegendOrderKey(item)))
            .Select(item => item.FolderPath)
            .ToList();
        _viewport.SetHiddenTakeoffFolders(hiddenFolders);
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

    private ContextMenu BuildPageTakeoffContextMenu(PageTakeoffNode node)
    {
        var menu = new ContextMenu();
        int selectedCount = SelectedPageTakeoffContextCount(node);
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Select {selectedCount} Linked Takeoffs" : "Select Linked Takeoff",
            true,
            () => SelectLinkedPageTakeoff(node)));
        menu.Items.Add(MakeMenuItem(
            IsPageTakeoffVisible(node.Page, node.Takeoff) ? "Hide on This Sheet" : "Show on This Sheet",
            true,
            () => TogglePageTakeoffVisibility(node.Page, node.Takeoff)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Move {selectedCount} Up in Legend" : "Move Up in Legend",
            CanMovePageTakeoffLegendNodes(node, -1),
            () => MovePageTakeoffLegendNodes(node, -1)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Move {selectedCount} Down in Legend" : "Move Down in Legend",
            CanMovePageTakeoffLegendNodes(node, 1),
            () => MovePageTakeoffLegendNodes(node, 1)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Sort Sheet Legend A-Z", CanSortPageLegend(node.Page), () => SortPageLegendByName(node.Page, node.Takeoff.FolderPath)));
        menu.Items.Add(MakeMenuItem("Reset Sheet Legend Order", HasCustomPageLegendOrder(node.Page), () => ResetPageLegendOrder(node.Page, node.Takeoff.FolderPath)));
        return menu;
    }

    private int SelectedPageTakeoffContextCount(PageTakeoffNode anchor) =>
        SelectedPageTakeoffNodes(anchor, fallbackToAnchor: true).Count;

    private void SelectLinkedPageTakeoff(PageTakeoffNode node)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, node.Page.FolderPath))
            OpenPageInActiveTab(node.Page);

        var selectedNodes = SelectedPageTakeoffNodes(node, fallbackToAnchor: true);
        ActivateTakeoffItem(node.Takeoff);
        SelectTakeoffItemSilently(node.Takeoff);
        RefreshActiveTakeoffVisuals();
        UpdateTotalDisplay();
        Dispatcher.InvokeAsync(() => SelectPageTakeoffMeasurementsOnCanvas(selectedNodes, node.Page));
        if (selectedNodes.Count <= 1)
            TxtStatus.Text = $"Linked takeoff selected for {node.Page.Name}: {node.Takeoff.Name}.";
    }

    private void SelectPageTakeoffMeasurementsOnCanvas(PageTakeoffNode node)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, node.Page.FolderPath))
            return;

        SelectTakeoffMeasurementsOnCanvas(node.Takeoff, node.Page.FolderPath, node.Page.Name);
    }

    private void SelectSelectedPageTakeoffMeasurementsOnCanvas(PageTakeoffNode anchor)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, anchor.Page.FolderPath))
            OpenPageInActiveTab(anchor.Page);

        var selectedNodes = SelectedPageTakeoffNodes(anchor, fallbackToAnchor: false);
        SelectPageTakeoffMeasurementsOnCanvas(selectedNodes, anchor.Page);
    }

    private List<PageTakeoffNode> SelectedPageTakeoffNodes(PageTakeoffNode anchor, bool fallbackToAnchor)
    {
        string anchorKey = PageTakeoffSelectionKey(anchor);
        IEnumerable<string> keys = _pageTakeoffMultiSelection.Contains(anchorKey)
            ? _pageTakeoffMultiSelection
            : fallbackToAnchor
                ? [anchorKey]
                : Enumerable.Empty<string>();

        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        if (keySet.Count == 0)
            return [];

        return EnumeratePageTreeItems()
            .Select(item => item.Tag as PageTakeoffNode)
            .Where(node => node != null &&
                           IsSamePageFolder(node.Page.FolderPath, anchor.Page.FolderPath) &&
                           keySet.Contains(PageTakeoffSelectionKey(node)))
            .Select(node => node!)
            .ToList();
    }

    private void SelectPageTakeoffMeasurementsOnCanvas(IReadOnlyList<PageTakeoffNode> nodes, PageInfo page)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
            return;

        var measurements = nodes
            .SelectMany(node => MeasurementsForTakeoffOnPage(node.Takeoff, page.FolderPath))
            .Distinct()
            .ToList();

        _syncingViewportSelectionFromTakeoffItem = true;
        try
        {
            _viewport.SelectMeasurements(measurements);
        }
        finally
        {
            _syncingViewportSelectionFromTakeoffItem = false;
        }

        if (measurements.Count == 0)
            TxtStatus.Text = $"No selected takeoff measurements on {page.Name}.";
        else if (nodes.Count <= 1)
            TxtStatus.Text = measurements.Count == 1
                ? $"Selected {nodes[0].Takeoff.Name} measurement on {page.Name}."
                : $"Selected {measurements.Count} {nodes[0].Takeoff.Name} measurements on {page.Name}.";
        else
            TxtStatus.Text = $"Selected {measurements.Count} measurements from {nodes.Count} linked takeoffs on {page.Name}.";
    }

    private void SelectTakeoffMeasurementsOnCanvas(TakeoffItem item, string pageFolder, string pageName)
    {
        var pageMeasurements = MeasurementsForTakeoffOnPage(item, pageFolder).ToList();
        if (pageMeasurements.Count == 0)
            return;

        _syncingViewportSelectionFromTakeoffItem = true;
        try
        {
            _viewport.SelectMeasurements(pageMeasurements);
        }
        finally
        {
            _syncingViewportSelectionFromTakeoffItem = false;
        }

        TxtStatus.Text = pageMeasurements.Count == 1
            ? $"Selected {item.Name} measurement on {pageName}."
            : $"Selected {pageMeasurements.Count} {item.Name} measurements on {pageName}.";
    }

    private void SelectTakeoffSelectionMeasurementsOnCurrentPage(TreeViewItem? anchor)
    {
        if (_currentPage == null || anchor == null || anchor.Tag is TakeoffMeasurementNode)
            return;

        var selectedItems = TakeoffItemsForSelection(anchor);
        if (selectedItems.Count == 0)
            return;

        var pageMeasurements = selectedItems
            .SelectMany(item => MeasurementsForTakeoffOnPage(item, _currentPage.FolderPath))
            .Distinct()
            .ToList();

        _syncingViewportSelectionFromTakeoffItem = true;
        try
        {
            _viewport.SelectMeasurements(pageMeasurements);
        }
        finally
        {
            _syncingViewportSelectionFromTakeoffItem = false;
        }

        if (pageMeasurements.Count == 0)
        {
            TxtStatus.Text = $"No selected takeoff measurements on {_currentPage.Name}.";
        }
        else if (selectedItems.Count == 1)
        {
            TxtStatus.Text = pageMeasurements.Count == 1
                ? $"Selected {selectedItems[0].Name} measurement on {_currentPage.Name}."
                : $"Selected {pageMeasurements.Count} {selectedItems[0].Name} measurements on {_currentPage.Name}.";
        }
        else
        {
            TxtStatus.Text = $"Selected {pageMeasurements.Count} measurements from {selectedItems.Count} takeoffs on {_currentPage.Name}.";
        }
    }

    private bool CanMovePageTakeoffLegendNode(PageTakeoffNode node, int offset)
    {
        IReadOnlyList<TakeoffItem> ordered = OrderedTakeoffsForPage(node.Page);
        int index = IndexOfTakeoff(ordered, node.Takeoff);
        int target = index + offset;
        return index >= 0 && target >= 0 && target < ordered.Count;
    }

    private bool CanMovePageTakeoffLegendNodes(PageTakeoffNode anchor, int offset)
    {
        var selectedNodes = SelectedPageTakeoffNodes(anchor, fallbackToAnchor: true);
        return CanMovePageTakeoffLegendNodes(selectedNodes, anchor.Page, offset);
    }

    private bool CanMovePageTakeoffLegendNodes(IReadOnlyList<PageTakeoffNode> selectedNodes, PageInfo page, int offset)
    {
        if (offset == 0 || selectedNodes.Count == 0)
            return false;

        var ordered = OrderedTakeoffsForPage(page).ToList();
        if (ordered.Count <= 1)
            return false;

        var selectedKeys = selectedNodes
            .Select(node => TakeoffLegendOrderKey(node.Takeoff))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedKeys.Count == 0 || selectedKeys.Count >= ordered.Count)
            return false;

        if (offset < 0)
        {
            for (int i = 1; i < ordered.Count; i++)
            {
                if (selectedKeys.Contains(TakeoffLegendOrderKey(ordered[i])) &&
                    !selectedKeys.Contains(TakeoffLegendOrderKey(ordered[i - 1])))
                    return true;
            }
        }
        else
        {
            for (int i = 0; i < ordered.Count - 1; i++)
            {
                if (selectedKeys.Contains(TakeoffLegendOrderKey(ordered[i])) &&
                    !selectedKeys.Contains(TakeoffLegendOrderKey(ordered[i + 1])))
                    return true;
            }
        }

        return false;
    }

    private void MovePageTakeoffLegendNode(PageTakeoffNode node, int offset)
    {
        var ordered = OrderedTakeoffsForPage(node.Page).ToList();
        int index = IndexOfTakeoff(ordered, node.Takeoff);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= ordered.Count)
            return;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        SavePageLegendOrder(node.Page, ordered);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectPageTakeoffNodeSilently(node.Page.FolderPath, node.Takeoff.FolderPath);
        TxtStatus.Text = offset < 0
            ? $"Moved {node.Takeoff.Name} up in {node.Page.Name} legend."
            : $"Moved {node.Takeoff.Name} down in {node.Page.Name} legend.";
    }

    private void MovePageTakeoffLegendNodes(PageTakeoffNode anchor, int offset)
    {
        var selectedNodes = SelectedPageTakeoffNodes(anchor, fallbackToAnchor: true);
        if (selectedNodes.Count <= 1)
        {
            MovePageTakeoffLegendNode(anchor, offset);
            return;
        }

        if (!CanMovePageTakeoffLegendNodes(selectedNodes, anchor.Page, offset))
            return;

        var ordered = OrderedTakeoffsForPage(anchor.Page).ToList();
        var selectedKeys = selectedNodes
            .Select(node => TakeoffLegendOrderKey(node.Takeoff))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (offset < 0)
        {
            for (int i = 1; i < ordered.Count; i++)
            {
                string currentKey = TakeoffLegendOrderKey(ordered[i]);
                string previousKey = TakeoffLegendOrderKey(ordered[i - 1]);
                if (selectedKeys.Contains(currentKey) && !selectedKeys.Contains(previousKey))
                    (ordered[i - 1], ordered[i]) = (ordered[i], ordered[i - 1]);
            }
        }
        else
        {
            for (int i = ordered.Count - 2; i >= 0; i--)
            {
                string currentKey = TakeoffLegendOrderKey(ordered[i]);
                string nextKey = TakeoffLegendOrderKey(ordered[i + 1]);
                if (selectedKeys.Contains(currentKey) && !selectedKeys.Contains(nextKey))
                    (ordered[i], ordered[i + 1]) = (ordered[i + 1], ordered[i]);
            }
        }

        SavePageLegendOrder(anchor.Page, ordered);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectPageTakeoffNodeSilently(anchor.Page.FolderPath, anchor.Takeoff.FolderPath);
        ApplyPagesMultiSelectionVisuals();
        TxtStatus.Text = offset < 0
            ? $"Moved {selectedNodes.Count} linked takeoffs up in {anchor.Page.Name} legend."
            : $"Moved {selectedNodes.Count} linked takeoffs down in {anchor.Page.Name} legend.";
    }

    private bool CanSortPageLegend(PageInfo page) =>
        TakeoffsForPage(page.FolderPath).Skip(1).Any();

    private static bool HasCustomPageLegendOrder(PageInfo page) =>
        page.LegendTakeoffOrder.Count > 0;

    private void SortPageLegendByName(PageInfo page, string? selectTakeoffFolder = null)
    {
        var ordered = TakeoffsForPage(page.FolderPath)
            .OrderBy(takeoff => takeoff.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(takeoff => MeasurementTypeTitle(takeoff.MeasurementType), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ordered.Count <= 1)
            return;

        SavePageLegendOrder(page, ordered);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectLegendOrderResult(page, selectTakeoffFolder);
        TxtStatus.Text = $"Sorted {page.Name} legend A-Z.";
    }

    private void ResetPageLegendOrder(PageInfo page, string? selectTakeoffFolder = null)
    {
        page.LegendTakeoffOrder = [];
        if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
            _currentPage.LegendTakeoffOrder = [];
        OurPlaneCoreJobStore.SavePageLegendTakeoffOrder(page.FolderPath, []);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectLegendOrderResult(page, selectTakeoffFolder);
        TxtStatus.Text = $"Reset {page.Name} legend order.";
    }

    private void SelectLegendOrderResult(PageInfo page, string? selectTakeoffFolder)
    {
        if (string.IsNullOrWhiteSpace(selectTakeoffFolder))
            return;

        SelectPageTakeoffNodeSilently(page.FolderPath, selectTakeoffFolder);
    }

    private bool CanDropPageTakeoffLegend(PageTakeoffLegendDrag drag, TreeViewItem? targetItem)
    {
        if (targetItem?.Tag is not PageTakeoffNode targetNode)
            return false;
        if (!IsSamePageFolder(drag.PageFolder, targetNode.Page.FolderPath))
            return false;

        var draggedFolders = drag.TakeoffFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (draggedFolders.Count == 0)
            return false;
        if (draggedFolders.Contains(targetNode.Takeoff.FolderPath, StringComparer.OrdinalIgnoreCase))
            return false;

        IReadOnlyList<TakeoffItem> ordered = OrderedTakeoffsForPage(targetNode.Page);
        return draggedFolders.All(folder => IndexOfTakeoffByFolder(ordered, folder) >= 0) &&
               IndexOfTakeoff(ordered, targetNode.Takeoff) >= 0;
    }

    private void UpdatePageTakeoffLegendDropCue(PageTakeoffLegendDrag drag, TreeViewItem targetItem, Point targetPosition)
    {
        bool dropAfter = IsPageTakeoffLegendDropAfter(targetItem, targetPosition);
        if (ReferenceEquals(_pageTakeoffLegendDropTarget, targetItem) &&
            _pageTakeoffLegendDropAfter == dropAfter)
        {
            return;
        }

        _pageTakeoffLegendDropTarget = targetItem;
        _pageTakeoffLegendDropAfter = dropAfter;
        ApplyPagesMultiSelectionVisuals();
        if (targetItem.Tag is PageTakeoffNode node)
        {
            IReadOnlyList<TakeoffItem> ordered = OrderedTakeoffsForPage(node.Page);
            int targetIndex = IndexOfTakeoff(ordered, node.Takeoff);
            int insertPosition = Math.Clamp(targetIndex + (dropAfter ? 2 : 1), 1, Math.Max(1, ordered.Count));
            string countText = drag.TakeoffFolders.Count == 1 ? "1 linked takeoff" : $"{drag.TakeoffFolders.Count} linked takeoffs";
            TxtStatus.Text = $"Drop {countText} {(dropAfter ? "below" : "above")} {node.Takeoff.Name} | {node.Page.Name} legend position {insertPosition}.";
        }
    }

    private void ClearPageTakeoffLegendDropCue()
    {
        if (_pageTakeoffLegendDropTarget == null)
            return;

        _pageTakeoffLegendDropTarget = null;
        ApplyPagesMultiSelectionVisuals();
    }

    private static bool IsPageTakeoffLegendDropAfter(TreeViewItem targetItem, Point targetPosition) =>
        targetPosition.Y > Math.Max(1.0, targetItem.ActualHeight) / 2.0;

    private void DropPageTakeoffLegend(PageTakeoffLegendDrag drag, TreeViewItem targetItem, Point targetPosition)
    {
        if (!CanDropPageTakeoffLegend(drag, targetItem) ||
            targetItem.Tag is not PageTakeoffNode targetNode)
        {
            return;
        }

        var draggedKeys = drag.TakeoffFolders
            .Select(NormalizeTakeoffLegendOrderKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (draggedKeys.Count == 0)
            return;

        var ordered = OrderedTakeoffsForPage(targetNode.Page).ToList();
        var moved = ordered
            .Where(takeoff => draggedKeys.Contains(TakeoffLegendOrderKey(takeoff)))
            .ToList();
        if (moved.Count == 0)
            return;

        ordered.RemoveAll(takeoff => draggedKeys.Contains(TakeoffLegendOrderKey(takeoff)));
        int targetIndex = IndexOfTakeoff(ordered, targetNode.Takeoff);
        if (targetIndex < 0)
            return;

        bool insertAfter = IsPageTakeoffLegendDropAfter(targetItem, targetPosition);
        int insertIndex = targetIndex + (insertAfter ? 1 : 0);
        insertIndex = Math.Clamp(insertIndex, 0, ordered.Count);
        ordered.InsertRange(insertIndex, moved);

        SavePageLegendOrder(targetNode.Page, ordered);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectPageTakeoffNodeSilently(targetNode.Page.FolderPath, moved[0].FolderPath);
        ApplyPagesMultiSelectionVisuals();
        int firstPosition = insertIndex + 1;
        TxtStatus.Text = moved.Count == 1
            ? $"Moved {moved[0].Name} to {targetNode.Page.Name} legend position {firstPosition}."
            : $"Moved {moved.Count} linked takeoffs to {targetNode.Page.Name} legend positions {firstPosition}-{firstPosition + moved.Count - 1}.";
    }

    private static int IndexOfTakeoff(IReadOnlyList<TakeoffItem> takeoffs, TakeoffItem target)
    {
        for (int i = 0; i < takeoffs.Count; i++)
        {
            if (string.Equals(takeoffs[i].FolderPath, target.FolderPath, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static int IndexOfTakeoffByFolder(IReadOnlyList<TakeoffItem> takeoffs, string folderPath)
    {
        for (int i = 0; i < takeoffs.Count; i++)
        {
            if (string.Equals(takeoffs[i].FolderPath, folderPath, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void SelectPageTakeoffNodeSilently(string pageFolder, string takeoffFolder)
    {
        if (FindPageTakeoffTreeItem(pageFolder, takeoffFolder) is not { } item)
            return;

        _syncingPageTreeSelection = true;
        try
        {
            ExpandTreeItemAndAncestorsWithoutTracking(item);
            item.IsSelected = true;
            ApplyPagesMultiSelectionVisuals();
            BringPageTreeItemIntoCenteredView(item);
        }
        finally
        {
            _syncingPageTreeSelection = false;
        }
    }

    private TreeViewItem? FindPageTakeoffTreeItem(string pageFolder, string takeoffFolder)
    {
        foreach (TreeViewItem item in EnumeratePageTreeItems())
        {
            if (item.Tag is not PageTakeoffNode node)
                continue;

            if (IsSamePageFolder(node.Page.FolderPath, pageFolder) &&
                string.Equals(node.Takeoff.FolderPath, takeoffFolder, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }
}
