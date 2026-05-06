using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private sealed record TakeoffsTreeSelectionSnapshot(
        IReadOnlyList<string> SelectedPaths,
        IReadOnlyList<string> SelectedSectionKeys,
        string? PrimaryPath,
        string? PrimarySectionKey,
        string? RangeAnchorPath,
        string? SectionRangeAnchorKey);

    private TakeoffsTreeSelectionSnapshot CaptureTakeoffsTreeSelectionState()
    {
        TreeViewItem? selected = TakeoffsTree.SelectedItem as TreeViewItem;
        string? primaryPath = selected == null ? null : GetTakeoffNodePath(selected);
        string? primarySectionKey = selected == null ? null : GetTakeoffSectionSelectionKey(selected);

        var selectedPaths = _takeoffsMultiSelection.ToList();
        if (!string.IsNullOrWhiteSpace(primaryPath) &&
            !selectedPaths.Contains(primaryPath, StringComparer.OrdinalIgnoreCase))
            selectedPaths.Add(primaryPath);

        var selectedSectionKeys = _takeoffSectionMultiSelection.ToList();
        if (!string.IsNullOrWhiteSpace(primarySectionKey) &&
            !selectedSectionKeys.Contains(primarySectionKey, StringComparer.OrdinalIgnoreCase))
            selectedSectionKeys.Add(primarySectionKey);

        return new TakeoffsTreeSelectionSnapshot(
            selectedPaths,
            selectedSectionKeys,
            primaryPath,
            primarySectionKey,
            _takeoffsRangeAnchorPath,
            _takeoffSectionRangeAnchorKey);
    }

    private bool RestoreTakeoffsTreeSelectionState(TakeoffsTreeSelectionSnapshot snapshot)
    {
        _takeoffsMultiSelection.Clear();
        _takeoffSectionMultiSelection.Clear();

        foreach (string path in snapshot.SelectedPaths.Where(Directory.Exists))
            _takeoffsMultiSelection.Add(path);

        foreach (string key in snapshot.SelectedSectionKeys.Where(TakeoffSectionSelectionKeyExists))
            _takeoffSectionMultiSelection.Add(key);

        _takeoffsRangeAnchorPath = !string.IsNullOrWhiteSpace(snapshot.RangeAnchorPath) &&
                                   Directory.Exists(snapshot.RangeAnchorPath)
            ? snapshot.RangeAnchorPath
            : null;
        _takeoffSectionRangeAnchorKey = !string.IsNullOrWhiteSpace(snapshot.SectionRangeAnchorKey) &&
                                        TakeoffSectionSelectionKeyExists(snapshot.SectionRangeAnchorKey)
            ? snapshot.SectionRangeAnchorKey
            : null;

        TreeViewItem? restored = FindTakeoffSelectionTreeItem(snapshot);
        if (restored == null)
            return false;

        TreeViewItem visibleTarget = TakeoffVisibleSelectionTarget(restored);
        ExpandTakeoffFolderAncestorsWithoutTracking(visibleTarget);
        visibleTarget.IsSelected = true;
        return true;
    }

    private TreeViewItem? FindTakeoffSelectionTreeItem(TakeoffsTreeSelectionSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.PrimarySectionKey))
        {
            TreeViewItem? section = FindTakeoffSectionTreeItemByKey(snapshot.PrimarySectionKey);
            if (section != null)
                return section;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.PrimaryPath) &&
            FindTakeoffTreeItemByFolder(snapshot.PrimaryPath) is { } pathItem)
            return pathItem;

        foreach (string key in _takeoffSectionMultiSelection)
        {
            TreeViewItem? section = FindTakeoffSectionTreeItemByKey(key);
            if (section != null)
                return section;
        }

        foreach (string path in _takeoffsMultiSelection)
        {
            if (FindTakeoffTreeItemByFolder(path) is { } item)
                return item;
        }

        return null;
    }

    private bool TakeoffSectionSelectionKeyExists(string key) =>
        FindTakeoffSectionTreeItemByKey(key) != null;

    private TreeViewItem? FindTakeoffSectionTreeItemByKey(string key) =>
        EnumerateTakeoffTreeItems(TakeoffsTree)
            .FirstOrDefault(item => string.Equals(
                GetTakeoffSectionSelectionKey(item),
                key,
                StringComparison.OrdinalIgnoreCase));
}
