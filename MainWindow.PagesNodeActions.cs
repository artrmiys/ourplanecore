using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void BtnNewPageFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "New Page Folder",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string targetFolder = PageFolderCreationTarget(PagesTree.SelectedItem as TreeViewItem) ??
                              _currentJob.PagesRoot;
        CreatePageFolder(targetFolder);
    }

    private void NewPageFolder(TreeViewItem item)
    {
        string? targetFolder = PageFolderCreationTarget(item);
        if (targetFolder == null)
            return;

        CreatePageFolder(targetFolder);
    }

    private string? PageFolderCreationTarget(TreeViewItem? item)
    {
        if (_currentJob == null || item == null)
            return null;

        string? folderPath = item.Tag switch
        {
            PageFolderNode folder => folder.FolderPath,
            PageInfo page => Path.GetDirectoryName(page.FolderPath),
            PageTakeoffNode node => Path.GetDirectoryName(node.Page.FolderPath),
            PageOverlayNode overlay => Path.GetDirectoryName(overlay.Page.FolderPath),
            _ => null,
        };

        return !string.IsNullOrWhiteSpace(folderPath) &&
               IsPathInsidePagesRoot(folderPath)
            ? folderPath
            : null;
    }

    private void CreatePageFolder(string parentFolder)
    {
        if (!IsPathInsidePagesRoot(parentFolder))
            return;

        string? name = ShowInputDialog("Folder name:", "New Folder", "New Folder");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            string created = OurPlaneCoreJobStore.CreateFolder(parentFolder, name);
            ReloadPagesTree(created);
            TxtStatus.Text = $"Created folder: {OurPlaneCoreJobStore.DisplayName(created)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("New Folder", ex);
        }
    }

    private void RenamePagesNode(TreeViewItem item)
    {
        if (IsRootPagesNode(item)) return;
        string? path = GetPagesNodePath(item);
        if (path == null || !IsPathInsidePagesRoot(path, allowRoot: false)) return;

        string currentName = OurPlaneCoreJobStore.DisplayName(path);
        string? name = ShowInputDialog("New name:", currentName, item.Tag is PageInfo ? "Rename Page" : "Rename Folder");
        if (string.IsNullOrWhiteSpace(name) || name == currentName) return;

        try
        {
            string renamed = item.Tag is PageInfo
                ? OurPlaneCoreJobStore.RenamePageAllowDuplicateName(path, name)
                : OurPlaneCoreJobStore.RenameNode(path, name);
            bool reloadActiveTab = UpdatePageReferencesForMovedPath(path, renamed);
            ReloadPagesTree(renamed);
            ReloadActivePageTabAfterPathChange(reloadActiveTab);
            TxtStatus.Text = $"Renamed to: {OurPlaneCoreJobStore.DisplayName(renamed)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Rename", ex);
        }
    }

    private void DeletePagesNode(TreeViewItem item)
    {
        var entries = GetSelectedPageEntries(item);
        if (entries.Count == 0) return;

        string message;
        if (entries.Count == 1)
        {
            string path = entries[0].SourcePath;
            bool isPage = entries[0].IsPage;
            bool hasChildren = Directory.EnumerateFileSystemEntries(path).Any();
            string name = OurPlaneCoreJobStore.DisplayName(path);
            message = isPage
                ? $"Delete page '{name}'?"
                : hasChildren
                    ? $"Delete folder '{name}' and everything inside it?"
                    : $"Delete empty folder '{name}'?";
        }
        else
        {
            message = $"Delete {entries.Count} selected page/folder item(s)?";
        }

        var result = MessageBox.Show(message, "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var deletedNames = entries.Select(e => OurPlaneCoreJobStore.DisplayName(e.SourcePath)).ToList();
        var parents = entries
            .Select(e => Path.GetDirectoryName(e.SourcePath) ?? _currentJob?.PagesRoot ?? "")
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        string? selectAfter = parents.FirstOrDefault() ?? _currentJob?.PagesRoot;
        try
        {
            foreach (var entry in entries)
                ClearCurrentPageIfAffected(entry.SourcePath);

            foreach (var entry in entries)
                DeleteDirectoryToRecycle(entry.SourcePath);

            if (_pagesClipboard != null && entries.Any(e =>
                    _pagesClipboard.Entries.Any(c =>
                        OurPlaneCoreJobStore.IsSameOrDescendant(e.SourcePath, c.SourcePath))))
                _pagesClipboard = null;

            foreach (string parent in parents.Where(Directory.Exists))
                OurPlaneCoreJobStore.NormalizeOrder(parent);
            _pagesMultiSelection.Clear();
            ReloadPagesTree(selectAfter);
            TxtStatus.Text = entries.Count == 1
                ? $"Deleted: {deletedNames[0]}"
                : $"Deleted {entries.Count} items.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Delete", ex);
        }
    }

    private void CopyCutPagesNode(TreeViewItem item, PagesClipboardMode mode)
    {
        var entries = GetSelectedPageEntries(item);
        if (entries.Count == 0) return;

        _pagesClipboard = new PagesClipboard(entries, mode);
        string verb = mode == PagesClipboardMode.Copy ? "Copied" : "Cut";
        TxtStatus.Text = entries.Count == 1
            ? $"{verb}: {OurPlaneCoreJobStore.DisplayName(entries[0].SourcePath)}"
            : $"{verb} {entries.Count} items.";
    }

    private void PasteIntoSelectedTarget(TreeViewItem item)
    {
        string? targetFolder = GetPasteTargetFolder(item);
        if (targetFolder == null) return;
        PasteIntoFolder(targetFolder);
    }

    private void PasteIntoFolder(string targetFolder)
    {
        if (_pagesClipboard == null || !CanPasteInto(targetFolder)) return;

        RunDrop(_pagesClipboard, targetFolder, _pagesClipboard.Mode);
    }

    private void RunDrop(PagesClipboard payload, string targetFolder, PagesClipboardMode mode)
    {
        bool wasCut = mode == PagesClipboardMode.Cut;

        try
        {
            var pastedItems = new List<string>();
            bool reloadActiveTab = false;
            var validEntries = payload.Entries
                .Where(entry => Directory.Exists(entry.SourcePath))
                .Where(entry => CanDropInto(new PagesClipboard([entry], mode), targetFolder, mode))
                .ToList();

            if (wasCut)
            {
                var moved = OurPlaneCoreJobStore.MoveNodes(validEntries.Select(entry => entry.SourcePath), targetFolder);
                foreach (var move in moved)
                {
                    pastedItems.Add(move.MovedPath);
                }

                reloadActiveTab = UpdatePageReferencesForMovedPaths(
                    moved.Select(move => (move.SourcePath, move.MovedPath)).ToList());
            }
            else
            {
                pastedItems.AddRange(OurPlaneCoreJobStore.CopyNodesPreserveDisplayName(
                    validEntries.Select(entry => entry.SourcePath),
                    targetFolder));
            }

            if (wasCut)
                _pagesClipboard = null;
            if (pastedItems.Count == 0)
                return;

            _pagesMultiSelection.Clear();
            foreach (string pasted in pastedItems)
                _pagesMultiSelection.Add(pasted);
            QueuePagesTreeDropRefresh(pastedItems[0], reloadActiveTab);
            TxtStatus.Text = pastedItems.Count == 1
                ? $"{(wasCut ? "Moved" : "Pasted")}: {OurPlaneCoreJobStore.DisplayName(pastedItems[0])}"
                : $"{(wasCut ? "Moved" : "Pasted")} {pastedItems.Count} items.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Paste", ex);
        }
    }

    private void DuplicatePageNode(TreeViewItem item)
    {
        if (item.Tag is not PageInfo page || !IsPathInsidePagesRoot(page.FolderPath, allowRoot: false))
            return;

        try
        {
            string duplicated = OurPlaneCoreJobStore.DuplicatePage(page.FolderPath);
            ReloadPagesTree(duplicated);
            TxtStatus.Text = $"Duplicated page: {OurPlaneCoreJobStore.DisplayName(duplicated)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Duplicate Page", ex);
        }
    }

    private void MovePagesNode(TreeViewItem item, int offset)
    {
        MovePagesNodes(item, offset);
    }

    private bool CanMovePagesNodes(TreeViewItem item, int offset)
    {
        var paths = GetSelectedPageEntries(item)
            .Select(entry => entry.SourcePath)
            .ToList();
        return OurPlaneCoreJobStore.CanMoveSiblings(paths, offset);
    }

    private void MovePagesNodes(TreeViewItem item, int offset)
    {
        if (IsRootPagesNode(item)) return;
        string? path = GetPagesNodePath(item);
        if (path == null || !IsPathInsidePagesRoot(path, allowRoot: false)) return;

        var entries = GetSelectedPageEntries(item);
        var paths = entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Count == 0)
            return;

        try
        {
            if (OurPlaneCoreJobStore.MoveSiblings(paths, offset))
            {
                _pagesMultiSelection.Clear();
                foreach (string selectedPath in paths)
                    _pagesMultiSelection.Add(selectedPath);

                ReloadPagesTree(paths[0]);
                TxtStatus.Text = paths.Count == 1
                    ? (offset < 0 ? "Moved up." : "Moved down.")
                    : (offset < 0 ? $"Moved {paths.Count} page/folder items up." : $"Moved {paths.Count} page/folder items down.");
            }
        }
        catch (Exception ex)
        {
            ShowOperationError(offset < 0 ? "Move Up" : "Move Down", ex);
        }
    }

    private void SortFolderChildren(TreeViewItem item, bool descending)
    {
        if (item.Tag is not PageFolderNode folder || !IsPathInsidePagesRoot(folder.FolderPath))
            return;

        try
        {
            OurPlaneCoreJobStore.SortChildren(folder.FolderPath, descending);
            ReloadPagesTree(folder.FolderPath);
            TxtStatus.Text = descending ? "Sorted children Z-A." : "Sorted children A-Z.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort Children", ex);
        }
    }

    private void MovePageToFolder(TreeViewItem item)
    {
        if (item.Tag is not PageInfo page || _currentJob == null)
            return;

        string? target = SelectFolder("Select destination folder inside Pages", _currentJob.PagesRoot);
        if (target == null) return;
        target = Path.GetFullPath(target);

        if (!IsPathInsidePagesRoot(target) || OurPlaneCoreJobStore.IsPageFolder(target))
        {
            MessageBox.Show("Choose a folder inside the current job's Pages tree.",
                            "Move to Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.Equals(Path.GetDirectoryName(page.FolderPath), target, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            string moved = OurPlaneCoreJobStore.MoveNode(page.FolderPath, target);
            bool reloadActiveTab = UpdatePageReferencesForMovedPath(page.FolderPath, moved);
            ReloadPagesTree(moved);
            ReloadActivePageTabAfterPathChange(reloadActiveTab);
            TxtStatus.Text = $"Moved page to: {OurPlaneCoreJobStore.DisplayName(target)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Move to Folder", ex);
        }
    }

    private bool CanPasteInto(string? targetFolder)
    {
        return _pagesClipboard != null && CanDropInto(_pagesClipboard, targetFolder, _pagesClipboard.Mode);
    }

    private bool CanDropInto(PagesClipboard payload, string? targetFolder, PagesClipboardMode mode)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(targetFolder))
            return false;
        if (payload.Entries.Count == 0 || !Directory.Exists(targetFolder))
            return false;
        if (!IsPathInsidePagesRoot(targetFolder) || OurPlaneCoreJobStore.IsPageFolder(targetFolder))
            return false;

        bool hasMovableEntry = false;
        foreach (var entry in payload.Entries)
        {
            if (!Directory.Exists(entry.SourcePath))
                return false;
            if (!IsPathInsidePagesRoot(entry.SourcePath, allowRoot: false))
                return false;
            if (OurPlaneCoreJobStore.IsSameOrDescendant(entry.SourcePath, targetFolder))
                return false;

            if (mode == PagesClipboardMode.Cut)
            {
                string sourceParent = Path.GetDirectoryName(entry.SourcePath) ?? "";
                if (!string.Equals(sourceParent, targetFolder, StringComparison.OrdinalIgnoreCase))
                    hasMovableEntry = true;
            }
        }

        if (mode == PagesClipboardMode.Cut && !hasMovableEntry)
            return false;

        return true;
    }

    private string? GetPasteTargetFolder(TreeViewItem item)
    {
        return item.Tag switch
        {
            PageFolderNode folder => folder.FolderPath,
            PageInfo page => Path.GetDirectoryName(page.FolderPath),
            PageTakeoffNode node => Path.GetDirectoryName(node.Page.FolderPath),
            PageOverlayNode overlay => Path.GetDirectoryName(overlay.Page.FolderPath),
            _ => null,
        };
    }
}
