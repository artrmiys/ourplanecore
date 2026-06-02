using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void TakeoffsTree_KeyDown(object sender, KeyEventArgs e)
    {
        Key key = KeyboardShortcutKeys.EffectiveKey(e);
        if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Z)
        {
            TryUndoLastTakeoffDelete();
            e.Handled = true;
            return;
        }

        if (TakeoffsTree.SelectedItem is not TreeViewItem item)
        {
            if (Keyboard.Modifiers == ModifierKeys.None &&
                key == Key.Delete &&
                FirstSelectedTakeoffTreeItem() is { } selected)
            {
                DeleteTakeoffNodes(selected);
                e.Handled = true;
            }
            return;
        }
        if (item.Tag is TakeoffMeasurementNode sectionNode)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Up)
            {
                MoveTakeoffSections(sectionNode, -1);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Down)
            {
                MoveTakeoffSections(sectionNode, 1);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.None && key == Key.Delete)
            {
                DeleteTakeoffSections(sectionNode);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.None && key == Key.F2 &&
                     SelectedTakeoffSectionNodes(sectionNode, fallbackToAnchor: true).Count <= 1)
            {
                RenameSection(sectionNode.Item, sectionNode.Measurement);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Enter)
            {
                SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(sectionNode, fallbackToAnchor: true), sectionNode);
                e.Handled = true;
            }
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && key == Key.C)
        {
            CopyCutTakeoffNode(item, TakeoffsClipboardMode.Copy);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && key == Key.X)
        {
            CopyCutTakeoffNode(item, TakeoffsClipboardMode.Cut);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && key == Key.V)
        {
            PasteIntoSelectedTakeoffTarget(item);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && key == Key.D)
        {
            DuplicateTakeoffNode(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Up)
        {
            MoveTakeoffNodes(item, -1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Down)
        {
            MoveTakeoffNodes(item, 1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && key == Key.Delete)
        {
            DeleteTakeoffNodes(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && key == Key.F2 && TakeoffSelectionCount(item) <= 1)
        {
            if (item.Tag is TakeoffItem takeoff)
                RenameItem(item, takeoff);
            else if (item.Tag is TakeoffFolderNode folder && !folder.IsRoot)
                RenameTakeoffFolder(item, folder);
            e.Handled = true;
        }
    }

    private TreeViewItem? FirstSelectedTakeoffTreeItem()
    {
        foreach (string path in _takeoffsMultiSelection)
        {
            if (FindTakeoffTreeItemByFolder(path) is { } item)
                return item;
        }

        return null;
    }

    private void CopyCutTakeoffNode(TreeViewItem item, TakeoffsClipboardMode mode)
    {
        var entries = GetSelectedTakeoffEntries(item);
        if (entries.Count == 0) return;

        _takeoffsClipboard = new TakeoffsClipboard(entries, mode);
        string verb = mode == TakeoffsClipboardMode.Copy ? "Copied" : "Cut";
        TxtStatus.Text = entries.Count == 1
            ? $"{verb}: {OurPlaneCoreJobStore.DisplayName(entries[0].SourcePath)}"
            : $"{verb} {entries.Count} takeoff nodes.";
    }

    private void PasteIntoSelectedTakeoffTarget(TreeViewItem item)
    {
        string? targetFolder = GetTakeoffPasteTargetFolder(item);
        if (targetFolder != null)
            PasteTakeoffsIntoFolder(targetFolder);
    }

    private void PasteTakeoffsIntoFolder(string targetFolder)
    {
        if (_takeoffsClipboard == null || !CanDropTakeoffsInto(_takeoffsClipboard, targetFolder, _takeoffsClipboard.Mode))
            return;

        RunTakeoffDrop(_takeoffsClipboard, targetFolder, _takeoffsClipboard.Mode);
    }

    private void DuplicateTakeoffNode(TreeViewItem item)
    {
        var entries = GetSelectedTakeoffEntries(item);
        if (entries.Count == 0) return;

        try
        {
            FlushTakeoffAutosaves();
            var changed = new List<string>();
            foreach (var entry in entries)
            {
                string? parent = Path.GetDirectoryName(entry.SourcePath);
                if (string.IsNullOrWhiteSpace(parent) ||
                    !CanDropTakeoffsInto(new TakeoffsClipboard([entry], TakeoffsClipboardMode.Copy), parent, TakeoffsClipboardMode.Copy))
                {
                    continue;
                }

                changed.Add(OurPlaneCoreJobStore.CopyNodePreserveDisplayName(entry.SourcePath, parent));
            }

            if (changed.Count == 0)
                return;

            if (!TryApplyTakeoffStructureCopyFast(changed))
            {
                LoadTakeoffsForJob();
                SetTakeoffMultiSelection(changed);
                SelectFirstTakeoffPath(changed);
            }
            TxtStatus.Text = changed.Count == 1
                ? $"Duplicated: {OurPlaneCoreJobStore.DisplayName(changed[0])}"
                : $"Duplicated {changed.Count} takeoff nodes.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Duplicate Takeoffs", ex);
        }
    }

    private void RunTakeoffDrop(
        TakeoffsClipboard payload,
        string targetFolder,
        TakeoffsClipboardMode mode,
        TakeoffDropTimings? timings = null)
    {
        bool wasCut = mode == TakeoffsClipboardMode.Cut;
        try
        {
            string? previousActivePath = _activeItem?.FolderPath;
            var flushWatch = Stopwatch.StartNew();
            FlushTakeoffAutosaves();
            flushWatch.Stop();
            if (timings != null)
                timings.FlushMilliseconds = flushWatch.ElapsedMilliseconds;

            var changed = new List<string>();
            var rebasedLegendPaths = new List<(string OldPath, string NewPath)>();
            var fileWatch = Stopwatch.StartNew();
            if (wasCut)
            {
                var moveEntries = payload.Entries
                    .Where(entry => CanDropTakeoffsInto(new TakeoffsClipboard([entry], mode), targetFolder, mode))
                    .ToList();
                foreach (var moved in OurPlaneCoreJobStore.MoveNodes(moveEntries.Select(entry => entry.SourcePath), targetFolder))
                {
                    changed.Add(moved.MovedPath);
                    rebasedLegendPaths.Add((moved.SourcePath, moved.MovedPath));
                }
            }
            else
            {
                var copyEntries = payload.Entries
                    .Where(entry => CanDropTakeoffsInto(new TakeoffsClipboard([entry], mode), targetFolder, mode))
                    .Select(entry => entry.SourcePath)
                    .ToList();
                changed.AddRange(OurPlaneCoreJobStore.CopyNodesPreserveDisplayName(copyEntries, targetFolder));
            }
            fileWatch.Stop();
            if (timings != null)
                timings.FileOperationMilliseconds = fileWatch.ElapsedMilliseconds;

            if (changed.Count == 0)
                return;

            if (wasCut)
                _takeoffsClipboard = null;

            RebasePageLegendTakeoffOrderReferences(rebasedLegendPaths);

            var refreshWatch = Stopwatch.StartNew();
            if (wasCut)
            {
                if (!TryApplyTakeoffStructureMoveFast(rebasedLegendPaths, targetFolder, changed))
                {
                    ReloadTakeoffsForMoveSelection(changed, previousActivePath);
                }
            }
            else
            {
                if (!TryApplyTakeoffStructureCopyFast(changed, timings))
                {
                    LoadTakeoffsForJob();
                    SetTakeoffMultiSelection(changed);
                    SelectFirstTakeoffPath(changed);
                }
            }
            refreshWatch.Stop();
            if (timings != null)
                timings.UiRefreshMilliseconds = refreshWatch.ElapsedMilliseconds;

            TxtStatus.Text = changed.Count == 1
                ? $"{(wasCut ? "Moved" : "Pasted")}: {OurPlaneCoreJobStore.DisplayName(changed[0])}"
                : $"{(wasCut ? "Moved" : "Pasted")} {changed.Count} takeoff nodes.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Takeoff Paste", ex);
        }
    }

    private bool CanPasteTakeoffsInto(string? targetFolder) =>
        _takeoffsClipboard != null && CanDropTakeoffsInto(_takeoffsClipboard, targetFolder, _takeoffsClipboard.Mode);

    private bool CanDropTakeoffsInto(TakeoffsClipboard payload, string? targetFolder, TakeoffsClipboardMode mode)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(targetFolder) || payload.Entries.Count == 0)
            return false;
        if (!Directory.Exists(targetFolder))
            return false;

        if (!OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, targetFolder) ||
            OurPlaneCoreJobStore.IsTakeoffItemFolder(targetFolder))
            return false;

        bool hasMovableEntry = false;
        foreach (var entry in payload.Entries)
        {
            if (!Directory.Exists(entry.SourcePath))
                return false;
            if (!OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, entry.SourcePath) ||
                string.Equals(entry.SourcePath, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
                return false;

            if (OurPlaneCoreJobStore.IsSameOrDescendant(entry.SourcePath, targetFolder))
                return false;

            if (mode == TakeoffsClipboardMode.Cut)
            {
                string parent = Path.GetDirectoryName(entry.SourcePath) ?? "";
                if (string.Equals(parent, targetFolder, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            hasMovableEntry = true;
        }

        return mode == TakeoffsClipboardMode.Copy || hasMovableEntry;
    }

    private string? GetTakeoffPasteTargetFolder(TreeViewItem item)
    {
        return item.Tag switch
        {
            TakeoffFolderNode folder => folder.FolderPath,
            TakeoffItem takeoff => Path.GetDirectoryName(takeoff.FolderPath),
            TakeoffMeasurementNode node => Path.GetDirectoryName(node.Item.FolderPath),
            _ => _currentJob?.TakeoffsRoot,
        };
    }

    private sealed class TakeoffDropTimings
    {
        public long FlushMilliseconds { get; set; }
        public long FileOperationMilliseconds { get; set; }
        public long UiRefreshMilliseconds { get; set; }
        public long CopyLoadMilliseconds { get; set; }
        public long CopyAppendMilliseconds { get; set; }
        public long CopyViewportMilliseconds { get; set; }
        public long CopySelectionMilliseconds { get; set; }
        public long CopyPageIndicatorsMilliseconds { get; set; }
        public long CopyLegendMilliseconds { get; set; }
        public long CopyEstimateMilliseconds { get; set; }
        public long CopyTotalMilliseconds { get; set; }
    }
}
