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
    // Page takeoff layer and legend move/sort commands.

    private bool CanMovePageTakeoffLayerNodes(PageTakeoffNode anchor, int offset)
    {
        var selectedNodes = SelectedPageTakeoffNodes(anchor, fallbackToAnchor: true);
        return CanMovePageTakeoffLayerNodes(selectedNodes, anchor.Page, offset);
    }

    private bool CanMovePageTakeoffLayerNodes(IReadOnlyList<PageTakeoffNode> selectedNodes, PageInfo page, int offset)
    {
        if (offset == 0 || selectedNodes.Count == 0)
            return false;

        var ordered = LayerOrderedTakeoffsForPage(page).ToList();
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

    private void MovePageTakeoffLayerNodes(PageTakeoffNode anchor, int offset)
    {
        var selectedNodes = SelectedPageTakeoffNodes(anchor, fallbackToAnchor: true);
        if (!CanMovePageTakeoffLayerNodes(selectedNodes, anchor.Page, offset))
            return;

        var ordered = LayerOrderedTakeoffsForPage(anchor.Page).ToList();
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

        SavePageTakeoffLayerOrder(anchor.Page, ordered);
        ApplyViewportPageTakeoffVisibility(anchor.Page);
        ApplyPagesMultiSelectionVisuals();
        TxtStatus.Text = offset < 0
            ? $"Moved {selectedNodes.Count} linked takeoff layer(s) backward on {anchor.Page.Name}."
            : $"Moved {selectedNodes.Count} linked takeoff layer(s) forward on {anchor.Page.Name}.";
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
        IsPageLegendManual(page) && page.LegendTakeoffOrder.Count > 0;

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

    private void SortPageLegendAuto(PageInfo page, string? selectTakeoffFolder = null)
    {
        var ordered = AutoOrderTakeoffs(TakeoffsForPage(page.FolderPath));
        if (ordered.Count <= 1)
            return;

        ClearPageLegendOrder(page);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectLegendOrderResult(page, selectTakeoffFolder);
        TxtStatus.Text = $"Set {page.Name} legend to live auto sorting.";
    }

    private void ResetPageLegendOrder(PageInfo page, string? selectTakeoffFolder = null)
    {
        ClearPageLegendOrder(page);
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
}
