using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private void BtnNewPageFolder_Click(object sender, RoutedEventArgs e)
    {
        if (IsCurrentJobReadOnly && !EnsureCurrentJobWritable("add a page folder"))
            return;

        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before adding a page folder.");
            return;
        }

        string targetFolder = PageFolderCreationTarget(PagesTree.SelectedItem as TreeViewItem) ??
                              _currentJob.PagesRoot;
        CreatePageFolder(targetFolder);
    }

    private void BtnNewBlankPage_Click(object sender, RoutedEventArgs e)
    {
        if (IsCurrentJobReadOnly && !EnsureCurrentJobWritable("add a blank sheet"))
            return;

        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before adding a blank sheet.");
            return;
        }

        string targetFolder = PageFolderCreationTarget(PagesTree.SelectedItem as TreeViewItem) ??
                              _currentJob.PagesRoot;
        CreateBlankPage(targetFolder);
    }

    private void NewPageFolder(TreeViewItem item)
    {
        if (!EnsureCurrentJobWritable("add a page folder"))
            return;

        string? targetFolder = PageFolderCreationTarget(item);
        if (targetFolder == null)
            return;

        CreatePageFolder(targetFolder);
    }

    private void NewBlankPage(TreeViewItem item)
    {
        if (!EnsureCurrentJobWritable("add a blank sheet"))
            return;

        if (_currentJob == null)
            return;

        string? targetFolder = PageFolderCreationTarget(item) ?? _currentJob.PagesRoot;
        if (targetFolder == null)
            return;

        CreateBlankPage(targetFolder);
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
        if (!EnsureCurrentJobWritable("add a page folder"))
            return;

        if (!IsPathInsidePagesRoot(parentFolder))
            return;

        string? name = ShowInputDialog("Folder name:", "New Folder", "New Folder");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            string created = OurPlanCoreJobStore.CreateFolder(parentFolder, name);
            ReloadPagesTree(created);
            TxtStatus.Text = $"Created folder: {OurPlanCoreJobStore.DisplayName(created)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("New Folder", ex);
        }
    }

    private void CreateBlankPage(string parentFolder)
    {
        if (!EnsureCurrentJobWritable("add a blank sheet"))
            return;

        if (_currentJob == null || !IsPathInsidePagesRoot(parentFolder))
            return;

        string? name = ShowInputDialog("Sheet name:", "Blank Sheet", "Blank Sheet");
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            PageInfo page = OurPlanCoreJobStore.CreateBlankPage(_currentJob, name, parentFolder);
            ReloadPagesTree(page.FolderPath);
            OpenPageInActiveTab(page);
            RefreshFloatingPageSetup(page.FolderPath);
            TxtStatus.Text = $"Blank sheet created: {page.Name}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Blank Sheet", ex);
        }
    }

    private void RenamePagesNode(TreeViewItem item)
    {
        if (!EnsureCurrentJobWritable("rename a page or folder"))
            return;
        if (_currentJob is not { } originJob)
            return;

        if (IsRootPagesNode(item)) return;
        string? path = GetPagesNodePath(item);
        if (path == null || !IsPathInsidePagesRoot(path, allowRoot: false)) return;

        string currentName = OurPlanCoreJobStore.DisplayName(path);
        string? name = ShowInputDialog("New name:", currentName, item.Tag is PageInfo ? "Rename Page" : "Rename Folder");
        if (string.IsNullOrWhiteSpace(name) || name == currentName) return;
        if (!EnsureExpectedJobWritable(originJob, "rename a page or folder") ||
            !EnsureCurrentJobWritable("rename a page or folder"))
            return;

        try
        {
            string renamed = item.Tag is PageInfo
                ? OurPlanCoreJobStore.RenamePageAllowDuplicateName(path, name)
                : OurPlanCoreJobStore.RenameNode(path, name);
            bool reloadActiveTab = UpdatePageReferencesForMovedPath(path, renamed);
            ReloadPagesTree(renamed);
            ReloadActivePageTabAfterPathChange(reloadActiveTab);
            TxtStatus.Text = $"Renamed to: {OurPlanCoreJobStore.DisplayName(renamed)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Rename", ex);
        }
    }

    private void DeletePagesNode(TreeViewItem item)
    {
        if (!EnsureCurrentJobWritable("delete pages or folders"))
            return;

        var entries = GetSelectedPageEntries(item);
        if (entries.Count == 0) return;

        string message;
        if (entries.Count == 1)
        {
            string path = entries[0].SourcePath;
            bool isPage = entries[0].IsPage;
            bool hasChildren = Directory.EnumerateFileSystemEntries(path).Any();
            string name = OurPlanCoreJobStore.DisplayName(path);
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

        var parents = entries
            .Select(e => Path.GetDirectoryName(e.SourcePath) ?? _currentJob?.PagesRoot ?? "")
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        string? selectAfter = parents.FirstOrDefault() ?? _currentJob?.PagesRoot;
        try
        {
            PageDeleteUndoBatch undoBatch = MovePageEntriesToUndoTrash(entries);
            RememberLastPageDelete(undoBatch);

            foreach (var entry in entries)
                ClearCurrentPageIfAffected(entry.SourcePath);
            CloseDetachedSheetsForDeletedPages(entries);

            if (_pagesClipboard != null && entries.Any(e =>
                    _pagesClipboard.Entries.Any(c =>
                        OurPlanCoreJobStore.IsSameOrDescendant(e.SourcePath, c.SourcePath))))
                _pagesClipboard = null;

            foreach (string parent in parents.Where(Directory.Exists))
                OurPlanCoreJobStore.NormalizeOrder(parent);
            _pagesMultiSelection.Clear();
            ReloadPagesTree(selectAfter);
            TxtStatus.Text = entries.Count == 1
                ? $"Deleted: {undoBatch.StatusName}. Press Ctrl+Z in Pages to restore."
                : $"Deleted {entries.Count} items. Press Ctrl+Z in Pages to restore.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Delete", ex);
        }
    }

    private void CopyCutPagesNode(TreeViewItem item, PagesClipboardMode mode)
    {
        if (mode == PagesClipboardMode.Cut && !EnsureCurrentJobWritable("cut pages or folders"))
            return;

        var entries = GetSelectedPageEntries(item);
        if (entries.Count == 0) return;

        _pagesClipboard = new PagesClipboard(entries, mode);
        string verb = mode == PagesClipboardMode.Copy ? "Copied" : "Cut";
        TxtStatus.Text = entries.Count == 1
            ? $"{verb}: {OurPlanCoreJobStore.DisplayName(entries[0].SourcePath)}"
            : $"{verb} {entries.Count} items.";
    }

    private void PasteIntoSelectedTarget(TreeViewItem item)
    {
        if (!EnsureCurrentJobWritable("paste pages or folders"))
            return;

        string? targetFolder = GetPasteTargetFolder(item);
        if (targetFolder == null) return;
        PasteIntoFolder(targetFolder);
    }

    private void PasteIntoFolder(string targetFolder)
    {
        if (!EnsureCurrentJobWritable("paste pages or folders"))
            return;

        if (_pagesClipboard == null || !CanPasteInto(targetFolder)) return;

        RunDrop(_pagesClipboard, targetFolder, _pagesClipboard.Mode);
    }

    private void RunDrop(PagesClipboard payload, string targetFolder, PagesClipboardMode mode)
    {
        if (!EnsureCurrentJobWritable(mode == PagesClipboardMode.Cut
                ? "move pages or folders"
                : "paste pages or folders"))
        {
            return;
        }

        bool wasCut = mode == PagesClipboardMode.Cut;

        try
        {
            using var operation = BeginPageOperation("Paste or move pages");
            var pastedItems = new List<string>();
            bool reloadActiveTab = false;
            var validEntries = payload.Entries
                .Where(entry => Directory.Exists(entry.SourcePath))
                .Where(entry => CanDropInto(new PagesClipboard([entry], mode), targetFolder, mode))
                .ToList();

            if (wasCut)
            {
                var moved = OurPlanCoreJobStore.MoveNodes(validEntries.Select(entry => entry.SourcePath), targetFolder);
                foreach (var move in moved)
                {
                    pastedItems.Add(move.MovedPath);
                }

                reloadActiveTab = UpdatePageReferencesForMovedPaths(
                    moved.Select(move => (move.SourcePath, move.MovedPath)).ToList());
            }
            else
            {
                pastedItems.AddRange(OurPlanCoreJobStore.CopyNodesPreserveDisplayName(
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
                ? $"{(wasCut ? "Moved" : "Pasted")}: {OurPlanCoreJobStore.DisplayName(pastedItems[0])}"
                : $"{(wasCut ? "Moved" : "Pasted")} {pastedItems.Count} items.";
            operation.Commit();
        }
        catch (Exception ex)
        {
            ShowOperationError("Paste", ex);
        }
    }

    private void DuplicatePageNode(TreeViewItem item)
    {
        if (!EnsureCurrentJobWritable("duplicate a page"))
            return;

        if (item.Tag is not PageInfo page || !IsPathInsidePagesRoot(page.FolderPath, allowRoot: false))
            return;

        try
        {
            string duplicated = OurPlanCoreJobStore.DuplicatePage(page.FolderPath);
            ReloadPagesTree(duplicated);
            TxtStatus.Text = $"Duplicated page: {OurPlanCoreJobStore.DisplayName(duplicated)}";
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
        return OurPlanCoreJobStore.CanMoveSiblings(paths, offset);
    }

    private void MovePagesNodes(TreeViewItem item, int offset)
    {
        if (!EnsureCurrentJobWritable("reorder pages or folders"))
            return;

        if (IsRootPagesNode(item)) return;
        string? path = GetPagesNodePath(item);
        if (path == null || !IsPathInsidePagesRoot(path, allowRoot: false)) return;

        var entries = GetSelectedPageEntries(item);
        var paths = entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Count == 0)
            return;

        try
        {
            if (OurPlanCoreJobStore.MoveSiblings(paths, offset))
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
        if (!EnsureCurrentJobWritable("sort pages or folders"))
            return;

        if (item.Tag is not PageFolderNode folder || !IsPathInsidePagesRoot(folder.FolderPath))
            return;

        try
        {
            OurPlanCoreJobStore.SortChildren(folder.FolderPath, descending);
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
        if (!EnsureCurrentJobWritable("move a page to another folder"))
            return;

        if (item.Tag is not PageInfo page || _currentJob == null)
            return;

        string? target = SelectFolder("Select destination folder inside Pages", _currentJob.PagesRoot);
        if (target == null) return;
        target = Path.GetFullPath(target);

        if (!IsPathInsidePagesRoot(target) || OurPlanCoreJobStore.IsPageFolder(target))
        {
            PostStatusWarning("Choose a folder inside the current job's Pages tree.");
            return;
        }

        if (string.Equals(Path.GetDirectoryName(page.FolderPath), target, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            using var operation = BeginPageOperation("Move page to folder");
            string moved = OurPlanCoreJobStore.MoveNode(page.FolderPath, target);
            bool reloadActiveTab = UpdatePageReferencesForMovedPath(page.FolderPath, moved);
            ReloadPagesTree(moved);
            ReloadActivePageTabAfterPathChange(reloadActiveTab);
            TxtStatus.Text = $"Moved page to: {OurPlanCoreJobStore.DisplayName(target)}";
            operation.Commit();
        }
        catch (Exception ex)
        {
            ShowOperationError("Move to Folder", ex);
        }
    }

    private bool CanPasteInto(string? targetFolder)
    {
        return !IsCurrentJobReadOnly &&
               _pagesClipboard != null &&
               CanDropInto(_pagesClipboard, targetFolder, _pagesClipboard.Mode);
    }

    private bool CanDropInto(PagesClipboard payload, string? targetFolder, PagesClipboardMode mode)
    {
        if (IsCurrentJobReadOnly || _currentJob == null || string.IsNullOrWhiteSpace(targetFolder))
            return false;
        if (payload.Entries.Count == 0 || !Directory.Exists(targetFolder))
            return false;
        if (!IsPathInsidePagesRoot(targetFolder) || OurPlanCoreJobStore.IsPageFolder(targetFolder))
            return false;

        bool hasMovableEntry = false;
        foreach (var entry in payload.Entries)
        {
            if (!Directory.Exists(entry.SourcePath))
                return false;
            if (!IsPathInsidePagesRoot(entry.SourcePath, allowRoot: false))
                return false;
            if (OurPlanCoreJobStore.IsSameOrDescendant(entry.SourcePath, targetFolder))
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
