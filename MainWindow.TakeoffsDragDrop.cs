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
    private TreeViewItem? _takeoffFolderDropTarget;
    private bool _takeoffFolderDropAllowed;
    private string _takeoffFolderDropStatus = "";

    private void TakeoffsTree_MouseMove(object sender, MouseEventArgs e)
    {
        if (_takeoffsDragStart == null)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetTakeoffsDragState();
            return;
        }

        if (!_takeoffsDragArmed)
        {
            ResetTakeoffsDragState();
            return;
        }

        Point pos = e.GetPosition(TakeoffsTree);
        if (Math.Abs(pos.X - _takeoffsDragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _takeoffsDragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if ((_takeoffsDragItem ?? TakeoffsTree.SelectedItem) is not TreeViewItem item)
            return;

        if (TryRefreshStaleTakeoffTreeNode(item))
        {
            ResetTakeoffsDragState();
            return;
        }

        if (item.Tag is TakeoffMeasurementNode sectionNode)
        {
            var nodes = SelectedTakeoffSectionNodes(sectionNode, fallbackToAnchor: true);
            if (nodes.Count == 0)
            {
                ResetTakeoffsDragState();
                return;
            }

            var sectionPayload = new TakeoffSectionDrag(nodes);
            DoTakeoffsDragDrop(sectionPayload, DragDropEffects.Move | DragDropEffects.Copy);
            return;
        }

        var entries = GetSelectedTakeoffEntries(item);
        if (entries.Count == 0)
        {
            ResetTakeoffsDragState();
            return;
        }

        var payload = new TakeoffsClipboard(entries, TakeoffsClipboardMode.Cut);
        DoTakeoffsDragDrop(payload, DragDropEffects.Move | DragDropEffects.Copy);
    }

    private void DoTakeoffsDragDrop(object payload, DragDropEffects effects)
    {
        try
        {
            DragDrop.DoDragDrop(TakeoffsTree, payload, effects);
        }
        finally
        {
            ClearTakeoffSectionDropCue();
            ClearTakeoffPositionDropCue();
            ClearTakeoffFolderDropCue();
            ResetTakeoffsDragState();
        }
    }

    private void ResetTakeoffsDragState()
    {
        _takeoffsDragStart = null;
        _takeoffsDragItem = null;
        _takeoffsDragArmed = false;
    }

    private void TakeoffsTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        TreeViewItem? rawTargetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        if (e.Data.GetData(typeof(TakeoffSectionDrag)) is TakeoffSectionDrag sectionDrag)
        {
            ClearTakeoffPositionDropCue();
            ClearTakeoffFolderDropCue();
            bool canDropSection = CanDropTakeoffSections(sectionDrag, rawTargetItem, copy);
            UpdateTakeoffSectionDropCue(sectionDrag, rawTargetItem, copy, canDropSection);
            if (canDropSection)
                e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        ClearTakeoffSectionDropCue();
        if (e.Data.GetData(typeof(TakeoffsClipboard)) is not TakeoffsClipboard payload)
            return;

        TreeViewItem? targetItem = ResolveTakeoffsClipboardDropTarget(rawTargetItem);
        if (targetItem == null && !copy && CanDropTakeoffsToRootBottom(payload))
        {
            ClearTakeoffFolderDropCue();
            UpdateTakeoffPositionDropCue(null, after: true, canDrop: true, TakeoffsRootBottomDropStatus(payload));
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        if (TryGetTakeoffPositionDropCue(payload, targetItem, copy, e, out bool after, out bool canDropPosition, out string positionStatus))
        {
            ClearTakeoffFolderDropCue();
            UpdateTakeoffPositionDropCue(targetItem, after, canDropPosition, positionStatus);
            if (canDropPosition)
                e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        ClearTakeoffPositionDropCue();
        string? targetFolder = targetItem == null ? _currentJob?.TakeoffsRoot : GetTakeoffPasteTargetFolder(targetItem);
        TakeoffsClipboardMode dragMode = copy ? TakeoffsClipboardMode.Copy : TakeoffsClipboardMode.Cut;
        bool canDropInto = CanDropTakeoffsInto(payload, targetFolder, dragMode);
        if (targetItem?.Tag is TakeoffFolderNode && targetFolder != null)
            UpdateTakeoffFolderDropCue(payload, targetItem, copy, canDropInto);
        else
            ClearTakeoffFolderDropCue();
        if (canDropInto)
            e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
    }

    private void TakeoffsTree_Drop(object sender, DragEventArgs e)
    {
        TreeViewItem? rawTargetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        if (e.Data.GetData(typeof(TakeoffSectionDrag)) is TakeoffSectionDrag sectionDrag)
        {
            ClearTakeoffPositionDropCue();
            ClearTakeoffFolderDropCue();
            if (CanDropTakeoffSections(sectionDrag, rawTargetItem, copy))
                DropTakeoffSections(sectionDrag, rawTargetItem!, copy);
            ClearTakeoffSectionDropCue();
            e.Handled = true;
            return;
        }

        ClearTakeoffSectionDropCue();
        if (e.Data.GetData(typeof(TakeoffsClipboard)) is not TakeoffsClipboard payload)
            return;

        TreeViewItem? targetItem = ResolveTakeoffsClipboardDropTarget(rawTargetItem);
        if (TryGetTakeoffPositionDropCue(payload, targetItem, copy, e, out bool after, out bool canDropPosition, out _) &&
            canDropPosition &&
            targetItem != null)
        {
            ClearTakeoffFolderDropCue();
            DropTakeoffPosition(payload, targetItem, after);
            ClearTakeoffPositionDropCue();
            e.Handled = true;
            return;
        }

        ClearTakeoffPositionDropCue();
        string? targetFolder = targetItem == null ? _currentJob?.TakeoffsRoot : GetTakeoffPasteTargetFolder(targetItem);
        TakeoffsClipboardMode mode = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey
            ? TakeoffsClipboardMode.Copy
            : TakeoffsClipboardMode.Cut;
        if (targetItem == null && mode == TakeoffsClipboardMode.Cut && CanDropTakeoffsToRootBottom(payload))
        {
            ClearTakeoffFolderDropCue();
            DropTakeoffsToRootBottom(payload);
            ClearTakeoffPositionDropCue();
            e.Handled = true;
            return;
        }

        if (CanDropTakeoffsInto(payload, targetFolder, mode))
            RunTakeoffDrop(payload, targetFolder!, mode);
        ClearTakeoffFolderDropCue();
        e.Handled = true;
    }

    private void TakeoffsTree_DragLeave(object sender, DragEventArgs e)
    {
        if (!TakeoffsTree.IsMouseOver)
        {
            ClearTakeoffSectionDropCue();
            ClearTakeoffPositionDropCue();
            ClearTakeoffFolderDropCue();
        }
    }

    private TreeViewItem? ResolveTakeoffsClipboardDropTarget(TreeViewItem? targetItem)
    {
        return targetItem?.Tag switch
        {
            TakeoffMeasurementNode node => FindTakeoffTreeItemByFolder(node.Item.FolderPath) ?? targetItem,
            _ => targetItem,
        };
    }

    private bool CanDropTakeoffsToRootBottom(TakeoffsClipboard payload)
    {
        if (_currentJob == null || payload.Entries.Count == 0 || !Directory.Exists(_currentJob.TakeoffsRoot))
            return false;

        foreach (TakeoffsClipboardEntry entry in payload.Entries)
        {
            if (!Directory.Exists(entry.SourcePath) ||
                !OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, entry.SourcePath) ||
                string.Equals(entry.SourcePath, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string sourceParent = Path.GetDirectoryName(entry.SourcePath) ?? "";
            if (string.Equals(sourceParent, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!CanDropTakeoffsInto(new TakeoffsClipboard([entry], TakeoffsClipboardMode.Cut), _currentJob.TakeoffsRoot, TakeoffsClipboardMode.Cut))
                return false;
        }

        return true;
    }

    private static string TakeoffsRootBottomDropStatus(TakeoffsClipboard payload) =>
        payload.Entries.Count == 1
            ? "Move 1 takeoff node to Takeoffs root bottom."
            : $"Move {payload.Entries.Count} takeoff nodes to Takeoffs root bottom.";

    private void DropTakeoffsToRootBottom(TakeoffsClipboard payload)
    {
        if (_currentJob == null)
            return;

        string root = _currentJob.TakeoffsRoot;
        try
        {
            FlushTakeoffAutosaves();
            var changed = new List<string>();
            var entriesToMove = new List<TakeoffsClipboardEntry>();
            var rebasedLegendPaths = new List<(string OldPath, string NewPath)>();

            foreach (TakeoffsClipboardEntry entry in payload.Entries)
            {
                string sourceParent = Path.GetDirectoryName(entry.SourcePath) ?? "";
                if (string.Equals(sourceParent, root, StringComparison.OrdinalIgnoreCase))
                {
                    changed.Add(entry.SourcePath);
                    continue;
                }

                if (CanDropTakeoffsInto(new TakeoffsClipboard([entry], TakeoffsClipboardMode.Cut), root, TakeoffsClipboardMode.Cut))
                    entriesToMove.Add(entry);
            }

            foreach (var moved in OurPlaneCoreJobStore.MoveNodes(entriesToMove.Select(entry => entry.SourcePath), root))
            {
                changed.Add(moved.MovedPath);
                rebasedLegendPaths.Add((moved.SourcePath, moved.MovedPath));
            }

            if (changed.Count == 0)
                return;

            bool reordered = OurPlaneCoreJobStore.MoveSiblingsToEnd(changed, root);
            _takeoffsClipboard = null;
            RebasePageLegendTakeoffOrderReferences(rebasedLegendPaths);

            if (rebasedLegendPaths.Count > 0)
            {
                if (!TryApplyTakeoffStructureMoveFast(rebasedLegendPaths, root, changed))
                {
                    LoadTakeoffsForJob();
                    SetTakeoffMultiSelection(changed);
                    SelectFirstTakeoffPath(changed);
                }
            }
            else if (!TryRefreshTakeoffTreeParentOrderFast(root, changed))
            {
                LoadTakeoffsForJob();
                SetTakeoffMultiSelection(changed);
                SelectFirstTakeoffPath(changed);
            }

            TxtStatus.Text = reordered || entriesToMove.Count > 0
                ? TakeoffsRootBottomDroppedStatus(changed.Count)
                : "Selected takeoff node(s) are already at Takeoffs root bottom.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Move Takeoffs to Root Bottom", ex);
        }
    }

    private static string TakeoffsRootBottomDroppedStatus(int count) =>
        count == 1
            ? "Moved 1 takeoff node to Takeoffs root bottom."
            : $"Moved {count} takeoff nodes to Takeoffs root bottom.";

    private bool TryGetTakeoffPositionDropCue(
        TakeoffsClipboard payload,
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

        string? targetPath = GetTakeoffNodePath(targetItem);
        if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
            return false;

        Point targetPoint = e.GetPosition(targetItem);
        bool dropOutOfTargetFolder = targetItem.Tag is TakeoffFolderNode && AreDirectTakeoffChildren(payload, targetPath);
        if (targetItem.Tag is TakeoffFolderNode && !dropOutOfTargetFolder && !IsTakeoffPositionEdgeDrop(targetItem, targetPoint))
            return false;

        after = dropOutOfTargetFolder || IsTakeoffPositionDropAfter(targetItem, targetPoint);
        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        canDrop = CanDropTakeoffsToPosition(payload, targetPath, after);
        string targetName = OurPlaneCoreJobStore.DisplayName(targetPath);
        string position = after ? "after" : "before";
        status = canDrop
            ? dropOutOfTargetFolder
                ? $"Move {paths.Count} takeoff node(s) out after {targetName}."
                : $"Move {paths.Count} takeoff node(s) {position} {targetName}."
            : $"Cannot reorder here. Drag onto another sibling position in the same folder.";
        return true;
    }

    private static bool AreDirectTakeoffChildren(TakeoffsClipboard payload, string folderPath)
    {
        if (payload.Entries.Count == 0)
            return false;

        return payload.Entries.All(entry =>
            string.Equals(
                Path.GetDirectoryName(entry.SourcePath) ?? "",
                folderPath,
                StringComparison.OrdinalIgnoreCase));
    }

    private bool CanDropTakeoffsToPosition(TakeoffsClipboard payload, string targetPath, bool after)
    {
        if (_currentJob == null || payload.Entries.Count == 0 || string.IsNullOrWhiteSpace(targetPath))
            return false;

        string targetParent = Path.GetDirectoryName(targetPath) ?? "";
        if (string.IsNullOrWhiteSpace(targetParent) ||
            !OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, targetParent) ||
            !Directory.Exists(targetParent))
        {
            return false;
        }

        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Any(path => string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (paths.All(path => string.Equals(Path.GetDirectoryName(path) ?? "", targetParent, StringComparison.OrdinalIgnoreCase)))
            return OurPlaneCoreJobStore.CanMoveSiblingsToPosition(paths, targetPath, after);

        return CanDropTakeoffsInto(payload, targetParent, TakeoffsClipboardMode.Cut);
    }

    private static bool IsTakeoffPositionDropAfter(TreeViewItem item, Point targetPoint) =>
        targetPoint.Y >= TakeoffNodeHeaderDropHeight(item) / 2.0;

    private static bool IsTakeoffPositionEdgeDrop(TreeViewItem item, Point targetPoint)
    {
        double height = TakeoffNodeHeaderDropHeight(item);
        if (targetPoint.Y < 0 || targetPoint.Y > height)
            return false;

        double edge = Math.Min(5.0, Math.Max(3.0, height * 0.18));
        return targetPoint.Y <= edge || targetPoint.Y >= height - edge;
    }

    private static double TakeoffNodeHeaderDropHeight(TreeViewItem item)
    {
        double itemHeight = Math.Max(1.0, item.ActualHeight);
        if (item.Header is FrameworkElement header && header.ActualHeight > 0)
            return Math.Min(itemHeight, Math.Max(18.0, header.ActualHeight + 6.0));

        return Math.Min(itemHeight, 28.0);
    }

    private void UpdateTakeoffPositionDropCue(TreeViewItem? targetItem, bool after, bool canDrop, string status)
    {
        if (ReferenceEquals(_takeoffPositionDropTarget, targetItem) &&
            _takeoffPositionDropAfter == after &&
            _takeoffPositionDropAllowed == canDrop &&
            string.Equals(_takeoffPositionDropStatus, status, StringComparison.Ordinal))
        {
            return;
        }

        TreeViewItem? oldTarget = _takeoffPositionDropTarget;
        _takeoffPositionDropTarget = targetItem;
        _takeoffPositionDropAfter = after;
        _takeoffPositionDropAllowed = canDrop;
        _takeoffPositionDropStatus = status;
        RefreshTakeoffDropCueRows(oldTarget, targetItem);
        if (!string.IsNullOrWhiteSpace(status))
            TxtStatus.Text = status;
    }

    private void ClearTakeoffPositionDropCue()
    {
        if (_takeoffPositionDropTarget == null && string.IsNullOrEmpty(_takeoffPositionDropStatus))
            return;

        TreeViewItem? oldTarget = _takeoffPositionDropTarget;
        _takeoffPositionDropTarget = null;
        _takeoffPositionDropAfter = false;
        _takeoffPositionDropAllowed = false;
        _takeoffPositionDropStatus = "";
        RefreshTakeoffDropCueRows(oldTarget);
    }

    private string TakeoffFolderDropStatus(TakeoffsClipboard payload, TreeViewItem targetItem, bool copy, bool canDrop)
    {
        if (payload.Entries.Count == 0)
            return "Select takeoff node(s) before dragging.";
        if (targetItem.Tag is not TakeoffFolderNode)
            return "Drop on a Takeoffs folder, or on row edges to reorder.";

        string? targetFolder = GetTakeoffPasteTargetFolder(targetItem);
        string targetName = string.IsNullOrWhiteSpace(targetFolder)
            ? "Takeoffs root"
            : OurPlaneCoreJobStore.DisplayName(targetFolder);
        string action = copy ? "Copy" : "Move";
        if (canDrop)
            return $"{action} {payload.Entries.Count} takeoff node(s) into {targetName}.";

        if (!copy &&
            !string.IsNullOrWhiteSpace(targetFolder) &&
            payload.Entries.Any(entry => OurPlaneCoreJobStore.IsSameOrDescendant(entry.SourcePath, targetFolder)))
        {
            return "Cannot move a takeoff folder into itself or its child.";
        }

        return $"Cannot {action.ToLowerInvariant()} selected takeoff node(s) into {targetName}.";
    }

    private void UpdateTakeoffFolderDropCue(TakeoffsClipboard payload, TreeViewItem targetItem, bool copy, bool canDrop)
    {
        string status = TakeoffFolderDropStatus(payload, targetItem, copy, canDrop);
        if (ReferenceEquals(_takeoffFolderDropTarget, targetItem) &&
            _takeoffFolderDropAllowed == canDrop &&
            string.Equals(_takeoffFolderDropStatus, status, StringComparison.Ordinal))
        {
            return;
        }

        TreeViewItem? oldTarget = _takeoffFolderDropTarget;
        _takeoffFolderDropTarget = targetItem;
        _takeoffFolderDropAllowed = canDrop;
        _takeoffFolderDropStatus = status;
        RefreshTakeoffDropCueRows(oldTarget, targetItem);
        if (!string.IsNullOrWhiteSpace(status))
            TxtStatus.Text = status;
    }

    private void ClearTakeoffFolderDropCue()
    {
        if (_takeoffFolderDropTarget == null && string.IsNullOrEmpty(_takeoffFolderDropStatus))
            return;

        TreeViewItem? oldTarget = _takeoffFolderDropTarget;
        _takeoffFolderDropTarget = null;
        _takeoffFolderDropAllowed = false;
        _takeoffFolderDropStatus = "";
        RefreshTakeoffDropCueRows(oldTarget);
    }

    private void DropTakeoffPosition(TakeoffsClipboard payload, TreeViewItem targetItem, bool after)
    {
        string? targetPath = GetTakeoffNodePath(targetItem);
        if (string.IsNullOrWhiteSpace(targetPath))
            return;

        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Count == 0)
            return;

        try
        {
            FlushTakeoffAutosaves();
            string targetParent = Path.GetDirectoryName(targetPath) ?? "";
            if (string.IsNullOrWhiteSpace(targetParent))
                return;

            var changed = new List<string>();
            var rebasedLegendPaths = new List<(string OldPath, string NewPath)>();
            if (paths.All(path => string.Equals(Path.GetDirectoryName(path) ?? "", targetParent, StringComparison.OrdinalIgnoreCase)))
            {
                if (!OurPlaneCoreJobStore.MoveSiblingsToPosition(paths, targetPath, after))
                    return;
                changed.AddRange(paths);
            }
            else
            {
                var entriesToMove = new List<TakeoffsClipboardEntry>();
                foreach (var entry in payload.Entries)
                {
                    if (string.Equals(Path.GetDirectoryName(entry.SourcePath) ?? "", targetParent, StringComparison.OrdinalIgnoreCase))
                    {
                        changed.Add(entry.SourcePath);
                        continue;
                    }

                    if (!CanDropTakeoffsInto(new TakeoffsClipboard([entry], TakeoffsClipboardMode.Cut), targetParent, TakeoffsClipboardMode.Cut))
                        continue;

                    entriesToMove.Add(entry);
                }

                foreach (var moved in OurPlaneCoreJobStore.MoveNodes(entriesToMove.Select(entry => entry.SourcePath), targetParent))
                {
                    changed.Add(moved.MovedPath);
                    rebasedLegendPaths.Add((moved.SourcePath, moved.MovedPath));
                }

                if (changed.Count == 0 ||
                    !OurPlaneCoreJobStore.MoveSiblingsToPosition(changed, targetPath, after))
                {
                    return;
                }

                _takeoffsClipboard = null;
                RebasePageLegendTakeoffOrderReferences(rebasedLegendPaths);
            }

            bool fastRefreshed = rebasedLegendPaths.Count > 0
                ? TryApplyTakeoffStructureMoveFast(rebasedLegendPaths, targetParent, changed)
                : TryRefreshTakeoffTreeParentOrderFast(targetParent, changed);
            if (!fastRefreshed)
            {
                LoadTakeoffsForJob();
                SetTakeoffMultiSelection(changed);
                SelectFirstTakeoffPath(changed);
            }

            TxtStatus.Text = changed.Count == 1
                ? $"Moved takeoff node {(after ? "after" : "before")} {OurPlaneCoreJobStore.DisplayName(targetPath)}."
                : $"Moved {changed.Count} takeoff nodes {(after ? "after" : "before")} {OurPlaneCoreJobStore.DisplayName(targetPath)}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Reorder Takeoffs", ex);
        }
    }

    private bool CanDropTakeoffSections(TakeoffSectionDrag payload, TreeViewItem? targetItem, bool copy)
    {
        if (payload.Nodes.Count == 0 || GetTakeoffSectionDropTarget(targetItem) is not { } target)
            return false;

        string targetType = OurPlaneCoreJobStore.NormalizeMeasurementType(target.MeasurementType);
        bool hasMovableNode = false;
        foreach (TakeoffMeasurementNode node in payload.Nodes)
        {
            if (!node.Item.Measurements.Contains(node.Measurement))
                return false;
            if (OurPlaneCoreJobStore.NormalizeMeasurementType(node.Measurement.MType) != targetType ||
                OurPlaneCoreJobStore.NormalizeMeasurementType(node.Item.MeasurementType) != targetType)
                return false;
            if (!ReferenceEquals(node.Item, target))
                hasMovableNode = true;
        }

        return copy || hasMovableNode;
    }

    private string TakeoffSectionDropStatus(TakeoffSectionDrag payload, TreeViewItem? targetItem, bool copy, bool canDrop)
    {
        if (payload.Nodes.Count == 0)
            return "Select section/count rows before dragging.";
        if (targetItem == null)
            return "Drop section/count rows on a takeoff item.";
        if (targetItem.Tag is TakeoffFolderNode)
            return "Drop section/count rows on a takeoff item, not a folder.";
        if (GetTakeoffSectionDropTarget(targetItem) is not { } target)
            return "Drop section/count rows on a takeoff item.";

        string action = copy ? "Copy" : "Move";
        string targetType = OurPlaneCoreJobStore.NormalizeMeasurementType(target.MeasurementType);
        TakeoffMeasurementNode? stale = payload.Nodes.FirstOrDefault(node => !node.Item.Measurements.Contains(node.Measurement));
        if (stale != null)
            return "Selected section/count row no longer exists.";

        TakeoffMeasurementNode? mismatch = payload.Nodes.FirstOrDefault(node =>
            OurPlaneCoreJobStore.NormalizeMeasurementType(node.Measurement.MType) != targetType ||
            OurPlaneCoreJobStore.NormalizeMeasurementType(node.Item.MeasurementType) != targetType);
        if (mismatch != null)
        {
            string sourceType = MeasurementTypeTitle(OurPlaneCoreJobStore.NormalizeMeasurementType(mismatch.Measurement.MType));
            string destinationType = MeasurementTypeTitle(targetType);
            return $"{action} blocked: {sourceType} rows can only drop on {sourceType} takeoff items, not {destinationType}.";
        }

        if (!copy && payload.Nodes.All(node => ReferenceEquals(node.Item, target)))
            return $"Already in {target.Name}. Hold Ctrl while dropping to copy.";

        return canDrop
            ? $"{action} {payload.Nodes.Count} section/count row(s) to {target.Name}."
            : $"Cannot {(copy ? "copy" : "move")} selected section/count rows to {target.Name}.";
    }

    private void UpdateTakeoffSectionDropCue(TakeoffSectionDrag payload, TreeViewItem? targetItem, bool copy, bool canDrop)
    {
        string status = TakeoffSectionDropStatus(payload, targetItem, copy, canDrop);
        if (ReferenceEquals(_takeoffSectionDropTarget, targetItem) &&
            _takeoffSectionDropAllowed == canDrop &&
            string.Equals(_takeoffSectionDropStatus, status, StringComparison.Ordinal))
        {
            return;
        }

        TreeViewItem? oldTarget = _takeoffSectionDropTarget;
        _takeoffSectionDropTarget = targetItem;
        _takeoffSectionDropAllowed = canDrop;
        _takeoffSectionDropStatus = status;
        RefreshTakeoffDropCueRows(oldTarget, targetItem);
        if (!string.IsNullOrWhiteSpace(status))
            TxtStatus.Text = status;
    }

    private void ClearTakeoffSectionDropCue()
    {
        if (_takeoffSectionDropTarget == null && string.IsNullOrEmpty(_takeoffSectionDropStatus))
            return;

        TreeViewItem? oldTarget = _takeoffSectionDropTarget;
        _takeoffSectionDropTarget = null;
        _takeoffSectionDropAllowed = false;
        _takeoffSectionDropStatus = "";
        RefreshTakeoffDropCueRows(oldTarget);
    }

    private void DropTakeoffSections(TakeoffSectionDrag payload, TreeViewItem targetItem, bool copy)
    {
        if (!CanDropTakeoffSections(payload, targetItem, copy) ||
            GetTakeoffSectionDropTarget(targetItem) is not { } target)
        {
            return;
        }

        FlushTakeoffAutosaves();
        string targetType = OurPlaneCoreJobStore.NormalizeMeasurementType(target.MeasurementType);
        var changedItems = new HashSet<TakeoffItem>();
        var resultingNodes = new List<TakeoffMeasurementNode>();

        foreach (TakeoffMeasurementNode node in payload.Nodes
                     .GroupBy(node => node.Measurement.Id, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            if (copy)
            {
                Measurement copied = CloneMeasurementForTakeoff(node.Measurement, target, targetType);
                target.Measurements.Add(copied);
                changedItems.Add(target);
                resultingNodes.Add(new TakeoffMeasurementNode(target, copied));
                continue;
            }

            if (ReferenceEquals(node.Item, target))
                continue;

            if (!node.Item.Measurements.Remove(node.Measurement))
                continue;

            node.Measurement.TakeoffFolder = target.FolderPath;
            node.Measurement.MType = targetType;
            node.Measurement.Color = target.Color;
            target.Measurements.Add(node.Measurement);
            changedItems.Add(node.Item);
            changedItems.Add(target);
            resultingNodes.Add(new TakeoffMeasurementNode(target, node.Measurement));
        }

        if (resultingNodes.Count == 0)
            return;

        foreach (TakeoffItem changed in changedItems)
        {
            OurPlaneCoreJobStore.SaveTakeoffItem(changed);
            RefreshTreeItem(changed);
        }

        _viewport.LoadMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
        CancelPendingTakeoffSelectionSync();
        SelectTakeoffSectionNodesSilently(resultingNodes);
        SelectTakeoffSectionMeasurementsOnCanvas(resultingNodes);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        RefreshEstimateTable();
        UpdateTotalDisplay();
        TxtStatus.Text = copy
            ? $"Copied {resultingNodes.Count} section/count row(s) to {target.Name}."
            : $"Moved {resultingNodes.Count} section/count row(s) to {target.Name}.";
    }

    private static Measurement CloneMeasurementForTakeoff(Measurement source, TakeoffItem target, string targetType) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = source.Name,
            Notes = source.Notes,
            MType = targetType,
            Points = source.Points.ToList(),
            Color = target.Color,
            PageFolder = source.PageFolder,
            TakeoffFolder = target.FolderPath,
            ScaleMetersPerPt = source.ScaleMetersPerPt,
        };

    private static TakeoffItem? GetTakeoffSectionDropTarget(TreeViewItem? item) =>
        item?.Tag switch
        {
            TakeoffItem target => target,
            TakeoffMeasurementNode node => node.Item,
            _ => null,
        };
}
