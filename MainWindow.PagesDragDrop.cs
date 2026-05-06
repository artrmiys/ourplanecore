using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void PagesTree_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pagesDragStart == null || e.LeftButton != MouseButtonState.Pressed)
            return;
        if (PagesTree.SelectedItem is not TreeViewItem item)
            return;
        if (IsRootPagesNode(item))
            return;

        Point pos = e.GetPosition(PagesTree);
        if (Math.Abs(pos.X - _pagesDragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _pagesDragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (item.Tag is PageTakeoffNode pageTakeoff)
        {
            var takeoffFolders = SelectedPageTakeoffNodes(pageTakeoff, fallbackToAnchor: true)
                .Select(node => node.Takeoff.FolderPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (takeoffFolders.Count == 0)
                return;

            var legendPayload = new PageTakeoffLegendDrag(pageTakeoff.Page.FolderPath, takeoffFolders);
            DoPagesDragDrop(legendPayload, DragDropEffects.Move);
            return;
        }

        var entries = GetSelectedPageEntries(item);
        if (entries.Count == 0)
            return;

        var payload = new PagesClipboard(entries, PagesClipboardMode.Cut);
        DoPagesDragDrop(payload, DragDropEffects.Move | DragDropEffects.Copy);
    }

    private void DoPagesDragDrop(object payload, DragDropEffects effects)
    {
        try
        {
            DragDrop.DoDragDrop(PagesTree, payload, effects);
        }
        finally
        {
            _pagesDragStart = null;
            FlushPendingPagesTreeDropRefresh();
        }
    }

    private void PagesTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(typeof(PageTakeoffLegendDrag)) is PageTakeoffLegendDrag legendDrag)
        {
            TreeViewItem? legendTargetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (CanDropPageTakeoffLegend(legendDrag, legendTargetItem))
            {
                e.Effects = DragDropEffects.Move;
                UpdatePageTakeoffLegendDropCue(legendDrag, legendTargetItem!, e.GetPosition(legendTargetItem));
            }
            else
            {
                ClearPageTakeoffLegendDropCue();
            }
            ClearPagesPositionDropCue();
            e.Handled = true;
            return;
        }

        ClearPageTakeoffLegendDropCue();
        if (e.Data.GetData(typeof(PagesClipboard)) is not PagesClipboard payload)
        {
            ClearPagesPositionDropCue();
            e.Handled = true;
            return;
        }

        TreeViewItem? targetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        string? targetFolder = targetItem == null ? _currentJob?.PagesRoot : GetPasteTargetFolder(targetItem);
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;

        if (TryGetPagesPositionDropCue(payload, targetItem, copy, e, out bool after, out bool canDropPosition, out string positionStatus))
        {
            UpdatePagesPositionDropCue(targetItem, after, canDropPosition, positionStatus);
            if (canDropPosition)
                e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        ClearPagesPositionDropCue();
        if (CanDropInto(payload, targetFolder, copy ? PagesClipboardMode.Copy : PagesClipboardMode.Cut))
            e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
    }

    private void PagesTree_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PageTakeoffLegendDrag)) is PageTakeoffLegendDrag legendDrag)
        {
            TreeViewItem? legendTargetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (legendTargetItem != null)
                DropPageTakeoffLegend(legendDrag, legendTargetItem, e.GetPosition(legendTargetItem));
            ClearPageTakeoffLegendDropCue();
            ClearPagesPositionDropCue();
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(typeof(PagesClipboard)) is not PagesClipboard payload)
        {
            ClearPagesPositionDropCue();
            return;
        }

        TreeViewItem? targetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        PagesClipboardMode mode = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey
            ? PagesClipboardMode.Copy
            : PagesClipboardMode.Cut;

        if (TryGetPagesPositionDropCue(payload, targetItem, mode == PagesClipboardMode.Copy, e, out bool after, out bool canDropPosition, out _) &&
            canDropPosition &&
            targetItem != null)
        {
            DropPagesPosition(payload, targetItem, after);
            ClearPagesPositionDropCue();
            e.Handled = true;
            return;
        }

        ClearPagesPositionDropCue();
        string? targetFolder = targetItem == null ? _currentJob?.PagesRoot : GetPasteTargetFolder(targetItem);
        if (!CanDropInto(payload, targetFolder, mode))
            return;

        RunDrop(payload, targetFolder!, mode);
        e.Handled = true;
    }

    private void PagesTree_DragLeave(object sender, DragEventArgs e)
    {
        if (!PagesTree.IsMouseOver)
        {
            ClearPageTakeoffLegendDropCue();
            ClearPagesPositionDropCue();
        }
    }

    private bool TryGetPagesPositionDropCue(
        PagesClipboard payload,
        TreeViewItem? targetItem,
        bool copy,
        DragEventArgs e,
        out bool after,
        out bool canDrop,
        out string status)
    {
        after = false;
        canDrop = false;
        status = "";

        if (copy || _currentJob == null || payload.Entries.Count == 0 || targetItem == null)
            return false;

        string? targetPath = GetPagesNodePath(targetItem);
        if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
            return false;

        Point targetPoint = e.GetPosition(targetItem);
        bool dropOutOfTargetFolder = targetItem.Tag is PageFolderNode && AreDirectPageChildren(payload, targetPath);
        if (targetItem.Tag is PageFolderNode && !dropOutOfTargetFolder && !IsPagesPositionEdgeDrop(targetItem, targetPoint))
            return false;

        after = dropOutOfTargetFolder || IsPagesPositionDropAfter(targetItem, targetPoint);
        canDrop = CanDropPagesToPosition(payload, targetPath, after);
        string targetName = OurPlaneCoreJobStore.DisplayName(targetPath);
        string position = after ? "below" : "above";
        status = canDrop
            ? dropOutOfTargetFolder
                ? $"Move {payload.Entries.Count} page/folder item(s) out below {targetName}."
                : $"Move {payload.Entries.Count} page/folder item(s) {position} {targetName}."
            : "Cannot reorder here. Drop on a sibling position in the same Pages folder.";
        return true;
    }

    private static bool AreDirectPageChildren(PagesClipboard payload, string folderPath)
    {
        if (payload.Entries.Count == 0)
            return false;

        return payload.Entries.All(entry =>
            string.Equals(
                Path.GetDirectoryName(entry.SourcePath) ?? "",
                folderPath,
                StringComparison.OrdinalIgnoreCase));
    }

    private bool CanDropPagesToPosition(PagesClipboard payload, string targetPath, bool after)
    {
        if (_currentJob == null || payload.Entries.Count == 0 || string.IsNullOrWhiteSpace(targetPath))
            return false;

        string targetParent = Path.GetDirectoryName(targetPath) ?? "";
        if (string.IsNullOrWhiteSpace(targetParent) ||
            !IsPathInsidePagesRoot(targetParent) ||
            !Directory.Exists(targetParent))
        {
            return false;
        }

        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Any(path => string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (paths.All(path => string.Equals(Path.GetDirectoryName(path) ?? "", targetParent, StringComparison.OrdinalIgnoreCase)))
            return OurPlaneCoreJobStore.CanMoveSiblingsToPosition(paths, targetPath, after);

        return CanDropInto(payload, targetParent, PagesClipboardMode.Cut);
    }

    private static bool IsPagesPositionDropAfter(TreeViewItem item, Point targetPoint) =>
        targetPoint.Y >= PagesNodeHeaderDropHeight(item) / 2.0;

    private static bool IsPagesPositionEdgeDrop(TreeViewItem item, Point targetPoint)
    {
        double height = PagesNodeHeaderDropHeight(item);
        if (targetPoint.Y < 0 || targetPoint.Y > height)
            return false;

        double edge = Math.Min(5.0, Math.Max(3.0, height * 0.18));
        return targetPoint.Y <= edge || targetPoint.Y >= height - edge;
    }

    private static double PagesNodeHeaderDropHeight(TreeViewItem item)
    {
        double itemHeight = Math.Max(1.0, item.ActualHeight);
        if (item.Header is FrameworkElement header && header.ActualHeight > 0)
            return Math.Min(itemHeight, Math.Max(18.0, header.ActualHeight + 6.0));

        return Math.Min(itemHeight, 28.0);
    }

    private void UpdatePagesPositionDropCue(TreeViewItem? targetItem, bool after, bool canDrop, string status)
    {
        if (ReferenceEquals(_pagesPositionDropTarget, targetItem) &&
            _pagesPositionDropAfter == after &&
            _pagesPositionDropAllowed == canDrop &&
            string.Equals(_pagesPositionDropStatus, status, StringComparison.Ordinal))
        {
            return;
        }

        _pagesPositionDropTarget = targetItem;
        _pagesPositionDropAfter = after;
        _pagesPositionDropAllowed = canDrop;
        _pagesPositionDropStatus = status;
        ApplyPagesMultiSelectionVisuals();
        if (!string.IsNullOrWhiteSpace(status))
            TxtStatus.Text = status;
    }

    private void ClearPagesPositionDropCue()
    {
        if (_pagesPositionDropTarget == null && string.IsNullOrEmpty(_pagesPositionDropStatus))
            return;

        _pagesPositionDropTarget = null;
        _pagesPositionDropAfter = false;
        _pagesPositionDropAllowed = false;
        _pagesPositionDropStatus = "";
        ApplyPagesMultiSelectionVisuals();
    }

    private void DropPagesPosition(PagesClipboard payload, TreeViewItem targetItem, bool after)
    {
        string? targetPath = GetPagesNodePath(targetItem);
        if (string.IsNullOrWhiteSpace(targetPath))
            return;

        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Count == 0)
            return;

        try
        {
            string targetParent = Path.GetDirectoryName(targetPath) ?? "";
            if (string.IsNullOrWhiteSpace(targetParent))
                return;

            var changed = new List<string>();
            bool reloadActiveTab = false;
            if (paths.All(path => string.Equals(Path.GetDirectoryName(path) ?? "", targetParent, StringComparison.OrdinalIgnoreCase)))
            {
                if (!OurPlaneCoreJobStore.MoveSiblingsToPosition(paths, targetPath, after))
                    return;
                changed.AddRange(paths);
            }
            else
            {
                foreach (var entry in payload.Entries)
                {
                    if (string.Equals(Path.GetDirectoryName(entry.SourcePath) ?? "", targetParent, StringComparison.OrdinalIgnoreCase))
                    {
                        changed.Add(entry.SourcePath);
                        continue;
                    }

                    if (!CanDropInto(new PagesClipboard([entry], PagesClipboardMode.Cut), targetParent, PagesClipboardMode.Cut))
                        continue;

                    string changedPath = OurPlaneCoreJobStore.MoveNode(entry.SourcePath, targetParent);
                    reloadActiveTab = UpdatePageReferencesForMovedPath(entry.SourcePath, changedPath) || reloadActiveTab;
                    changed.Add(changedPath);
                }

                if (changed.Count == 0 ||
                    !OurPlaneCoreJobStore.MoveSiblingsToPosition(changed, targetPath, after))
                {
                    return;
                }

                _pagesClipboard = null;
            }

            _pagesMultiSelection.Clear();
            foreach (string changedPath in changed)
                _pagesMultiSelection.Add(changedPath);

            string selectPath = changed[0];
            QueuePagesTreeDropRefresh(selectPath, reloadActiveTab);
            TxtStatus.Text = changed.Count == 1
                ? $"Moved page/folder {(after ? "below" : "above")} {OurPlaneCoreJobStore.DisplayName(targetPath)}."
                : $"Moved {changed.Count} page/folder items {(after ? "below" : "above")} {OurPlaneCoreJobStore.DisplayName(targetPath)}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Reorder Pages", ex);
        }
    }

    private void QueuePagesTreeDropRefresh(string selectPath, bool reloadActiveTab)
    {
        _pendingPagesTreeDropRefreshPath = selectPath;
        _pendingPagesTreeDropReloadActiveTab = _pendingPagesTreeDropReloadActiveTab || reloadActiveTab;
        int version = ++_pendingPagesTreeDropRefreshVersion;

        Dispatcher.InvokeAsync(() =>
        {
            if (version == _pendingPagesTreeDropRefreshVersion)
                FlushPendingPagesTreeDropRefresh();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void FlushPendingPagesTreeDropRefresh()
    {
        if (string.IsNullOrWhiteSpace(_pendingPagesTreeDropRefreshPath))
            return;

        string selectPath = _pendingPagesTreeDropRefreshPath;
        bool reloadActiveTab = _pendingPagesTreeDropReloadActiveTab;
        _pendingPagesTreeDropRefreshPath = null;
        _pendingPagesTreeDropReloadActiveTab = false;

        ClearPagesPositionDropCue();
        ReloadPagesTree(selectPath);
        ReloadActivePageTabAfterPathChange(reloadActiveTab);
        PagesTree.Items.Refresh();
        PagesTree.UpdateLayout();
        RevealPageNodeAfterDrop(selectPath);
    }

    private void RevealPageNodeAfterDrop(string folderPath)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!Directory.Exists(folderPath))
                return;

            SelectPageTreeNodeSilently(folderPath);
            if (FindPageTreeItemByFolder(folderPath) is { } item)
                BringPageTreeItemIntoCenteredView(item);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
