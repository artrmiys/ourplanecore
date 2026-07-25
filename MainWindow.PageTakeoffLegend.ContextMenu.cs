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
    // Linked page takeoff context menu, rename, visibility, and canvas selection.

    private void AttachLazyPageTakeoffContextMenu(TreeViewItem child, PageTakeoffNode node)
    {
        child.ContextMenu = new ContextMenu();
        child.ContextMenuOpening += (_, _) =>
            child.ContextMenu = BuildPageTakeoffContextMenu(node);
    }

    private ContextMenu BuildPageTakeoffContextMenu(PageTakeoffNode node)
    {
        var menu = new ContextMenu();
        int selectedCount = SelectedPageTakeoffContextCount(node);
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Select {selectedCount} Linked Takeoffs" : "Select Linked Takeoff",
            true,
            () => SelectLinkedPageTakeoff(node)));
        if (IsCurrentJobReadOnly)
        {
            menu.Items.Add(MakeMenuItem("Read-only: editing is disabled", false, () => { }));
            return menu;
        }

        menu.Items.Add(MakeMenuItem(
            "Rename Linked Takeoff...",
            selectedCount <= 1,
            () => RenameLinkedPageTakeoff(node)));
        menu.Items.Add(MakeMenuItem(
            IsPageTakeoffVisible(node.Page, node.Takeoff) ? "Hide on This Sheet" : "Show on This Sheet",
            true,
            () => TogglePageTakeoffVisibility(node.Page, node.Takeoff)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Hide {selectedCount} Selected on This Sheet" : "Hide Selected on This Sheet",
            selectedCount > 0,
            () => SetSelectedPageTakeoffVisibility(node, visible: false)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Show {selectedCount} Selected on This Sheet" : "Show Selected on This Sheet",
            selectedCount > 0,
            () => SetSelectedPageTakeoffVisibility(node, visible: true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Move {selectedCount} Backward" : "Move Backward",
            CanMovePageTakeoffLayerNodes(node, -1),
            () => MovePageTakeoffLayerNodes(node, -1)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Move {selectedCount} Forward" : "Move Forward",
            CanMovePageTakeoffLayerNodes(node, 1),
            () => MovePageTakeoffLayerNodes(node, 1)));
        menu.Items.Add(BuildPageTakeoffCountDisplayMenu(node));
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
        menu.Items.Add(MakeMenuItem("Sort Sheet Legend Auto", CanSortPageLegend(node.Page), () => SortPageLegendAuto(node.Page, node.Takeoff.FolderPath)));
        menu.Items.Add(MakeMenuItem("Sort Sheet Legend A-Z", CanSortPageLegend(node.Page), () => SortPageLegendByName(node.Page, node.Takeoff.FolderPath)));
        menu.Items.Add(MakeMenuItem("Reset Sheet Legend Order", HasCustomPageLegendOrder(node.Page), () => ResetPageLegendOrder(node.Page, node.Takeoff.FolderPath)));
        return menu;
    }

    private int SelectedPageTakeoffContextCount(PageTakeoffNode anchor) =>
        SelectedPageTakeoffNodes(anchor, fallbackToAnchor: true).Count;

    private void RenameLinkedPageTakeoff(PageTakeoffNode node)
    {
        if (FindTakeoffTreeItem(node.Takeoff) is not { } item)
        {
            TxtStatus.Text = "Linked takeoff is not visible in the Takeoffs tree.";
            return;
        }

        RenameItem(item, node.Takeoff);
    }

    private void SetSelectedPageTakeoffVisibility(PageTakeoffNode anchor, bool visible)
    {
        var selectedNodes = SelectedPageTakeoffNodes(anchor, fallbackToAnchor: true);
        if (selectedNodes.Count == 0)
            return;

        var hidden = anchor.Page.HiddenTakeoffs
            .Select(NormalizeTakeoffLegendOrderKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hiddenMeasurements = anchor.Page.HiddenMeasurements
            .Select(NormalizeMeasurementVisibilityId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (PageTakeoffNode node in selectedNodes)
        {
            string key = TakeoffLegendOrderKey(node.Takeoff);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (visible)
                hidden.RemoveAll(value => string.Equals(value, key, StringComparison.OrdinalIgnoreCase));
            else if (!hidden.Contains(key, StringComparer.OrdinalIgnoreCase))
                hidden.Add(key);
        }
        if (visible)
            hiddenMeasurements = RemoveHiddenMeasurementsForTakeoffs(
                anchor.Page,
                selectedNodes.Select(node => node.Takeoff),
                hiddenMeasurements);

        anchor.Page.HiddenTakeoffs = hidden;
        anchor.Page.HiddenMeasurements = hiddenMeasurements;
        if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, anchor.Page.FolderPath))
        {
            _currentPage.HiddenTakeoffs = hidden.ToList();
            _currentPage.HiddenMeasurements = hiddenMeasurements.ToList();
        }
        OurPlanCoreJobStore.SavePageHiddenTakeoffs(anchor.Page.FolderPath, hidden);
        OurPlanCoreJobStore.SavePageHiddenMeasurements(anchor.Page.FolderPath, hiddenMeasurements);
        RefreshPageOverlayTreeNode(anchor.Page);
        ApplyViewportPageTakeoffVisibility(anchor.Page);
        RefreshSheetLegend();
        RefreshDetachedSheetsForPage(anchor.Page.FolderPath);
        TxtStatus.Text = visible
            ? $"Shown {selectedNodes.Count} selected takeoff(s) on {anchor.Page.Name}."
            : $"Hidden {selectedNodes.Count} selected takeoff(s) on {anchor.Page.Name}.";
    }

    private void SelectLinkedPageTakeoff(PageTakeoffNode node)
    {
        if ((_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, node.Page.FolderPath)) &&
            !HasDetachedSheetForPage(node.Page.FolderPath))
        {
            OpenPageInActiveTab(node.Page);
        }

        var selectedNodes = SyncTakeoffsTreeSelectionFromPageTakeoffs(node, fallbackToAnchor: true);
        if (selectedNodes.Count == 0)
            return;

        Dispatcher.InvokeAsync(() => SelectPageTakeoffMeasurementsOnCanvas(selectedNodes, node.Page));
        if (selectedNodes.Count <= 1)
            TxtStatus.Text = $"Linked takeoff selected for {node.Page.Name}: {node.Takeoff.Name}.";
    }

    private IReadOnlyList<PageTakeoffNode> SyncTakeoffsTreeSelectionFromPageTakeoffs(
        PageTakeoffNode anchor,
        bool fallbackToAnchor)
    {
        if (TryBlockTakeoffSwitchDuringRecord(anchor.Takeoff))
            return [];

        var selectedNodes = SelectedPageTakeoffNodes(anchor, fallbackToAnchor);
        if (selectedNodes.Count == 0 && fallbackToAnchor)
            selectedNodes = [anchor];

        var selectedTakeoffs = selectedNodes
            .Select(selectedNode => selectedNode.Takeoff)
            .Where(takeoff => !string.IsNullOrWhiteSpace(takeoff.FolderPath))
            .GroupBy(takeoff => NormalizePath(takeoff.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (selectedTakeoffs.Count == 0)
            selectedTakeoffs = [anchor.Takeoff];

        ActivateTakeoffItem(anchor.Takeoff);
        SelectTakeoffItemsSilently(selectedTakeoffs, anchor.Takeoff);
        RefreshActiveTakeoffVisuals();
        UpdateTotalDisplay(refreshEstimate: false);
        return selectedNodes;
    }

    private void SelectPageTakeoffMeasurementsOnCanvas(PageTakeoffNode node)
    {
        bool onCurrentPage = _currentPage != null && IsSamePageFolder(_currentPage.FolderPath, node.Page.FolderPath);
        if (!onCurrentPage && !HasDetachedSheetForPage(node.Page.FolderPath))
            return;

        SelectTakeoffMeasurementsOnCanvas(node.Takeoff, node.Page.FolderPath, node.Page.Name);
    }

    private void SelectSelectedPageTakeoffMeasurementsOnCanvas(PageTakeoffNode anchor)
    {
        if ((_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, anchor.Page.FolderPath)) &&
            !HasDetachedSheetForPage(anchor.Page.FolderPath))
        {
            OpenPageInActiveTab(anchor.Page);
        }

        var selectedNodes = SelectedPageTakeoffNodes(anchor, fallbackToAnchor: false);
        SelectPageTakeoffMeasurementsOnCanvas(selectedNodes, anchor.Page);
    }

    private List<PageTakeoffNode> SelectedPageTakeoffNodes(PageTakeoffNode anchor, bool fallbackToAnchor)
    {
        if (string.IsNullOrWhiteSpace(anchor.Page.FolderPath) ||
            string.IsNullOrWhiteSpace(anchor.Takeoff.FolderPath))
        {
            return [];
        }

        string anchorKey = PageTakeoffSelectionKey(anchor);
        if (string.IsNullOrWhiteSpace(anchorKey))
            return [];

        IEnumerable<string> keys = _pageTakeoffMultiSelection.Contains(anchorKey)
            ? _pageTakeoffMultiSelection
            : fallbackToAnchor
                ? [anchorKey]
                : Enumerable.Empty<string>();

        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        if (keySet.Count == 0)
            return [];

        if (FindPageTreeItemByFolder(anchor.Page.FolderPath) is not { } pageItem)
            return [];

        return pageItem.Items
            .OfType<TreeViewItem>()
            .Select(item => item.Tag as PageTakeoffNode)
            .Where(node => node != null &&
                           !string.IsNullOrWhiteSpace(node.Page.FolderPath) &&
                           !string.IsNullOrWhiteSpace(node.Takeoff.FolderPath) &&
                           keySet.Contains(PageTakeoffSelectionKey(node)))
            .Select(node => node!)
            .ToList();
    }

    private void SelectPageTakeoffMeasurementsOnCanvas(IReadOnlyList<PageTakeoffNode> nodes, PageInfo page)
    {
        bool onCurrentPage = _currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath);
        if (!onCurrentPage && !HasDetachedSheetForPage(page.FolderPath))
            return;

        var measurements = nodes
            .SelectMany(node => MeasurementsForTakeoffOnPage(node.Takeoff, page.FolderPath))
            .Distinct()
            .ToList();

        _syncingViewportSelectionFromTakeoffItem = true;
        try
        {
            if (onCurrentPage)
                _viewport.SelectMeasurements(measurements);
            SelectMeasurementsInDetachedSheets(page.FolderPath, measurements);
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

        bool onCurrentPage = _currentPage != null && IsSamePageFolder(_currentPage.FolderPath, pageFolder);
        _syncingViewportSelectionFromTakeoffItem = true;
        try
        {
            if (onCurrentPage)
                _viewport.SelectMeasurements(pageMeasurements);
            SelectMeasurementsInDetachedSheets(pageFolder, pageMeasurements);
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
}
