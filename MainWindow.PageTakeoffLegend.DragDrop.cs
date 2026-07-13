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
    // Page takeoff legend drag/drop cues and tree item lookup.

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
        => FindPageTakeoffTreeItemIndexed(pageFolder, takeoffFolder);

    private static TreeViewItem? FindPageTakeoffTreeItem(TreeViewItem pageItem, string takeoffFolder)
    {
        foreach (TreeViewItem item in pageItem.Items.OfType<TreeViewItem>())
        {
            if (item.Tag is PageTakeoffNode node &&
                string.Equals(node.Takeoff.FolderPath, takeoffFolder, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }
}
