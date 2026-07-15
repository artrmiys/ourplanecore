using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OurPlanCore;

public partial class MainWindow
{
    private void BtnRefreshPagesTree_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before refreshing Pages.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_pendingPagesTreeDropRefreshPath))
        {
            FlushPendingPagesTreeDropRefresh();
            TxtStatus.Text = "Pages tree refreshed after pending move.";
            return;
        }

        string? selectedPath = PagesTree.SelectedItem is TreeViewItem selected
            ? GetPagesNodePath(selected)
            : _currentPage?.FolderPath;

        ClearPagesPositionDropCue();
        ReloadPagesTree(selectedPath, selectSilently: true);
        RefreshPageTabs(_activePageTab);
        RefreshSheetLegend();
        TxtStatus.Text = "Pages tree refreshed.";
    }

    private void PagesTree_KeyDown(object sender, KeyEventArgs e)
    {
        Key key = KeyboardShortcutKeys.EffectiveKey(e);
        if (Keyboard.Modifiers == ModifierKeys.None && key == Key.Escape)
        {
            ClearPagesTreeSelectionFromEscape();
            e.Handled = true;
            return;
        }

        if (PagesTree.SelectedItem is not TreeViewItem item) return;

        if (item.Tag is PageTakeoffNode node)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Up)
            {
                if (EnsureCurrentJobWritable("reorder the sheet legend"))
                    MovePageTakeoffLegendNodes(node, -1);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Down)
            {
                if (EnsureCurrentJobWritable("reorder the sheet legend"))
                    MovePageTakeoffLegendNodes(node, 1);
                e.Handled = true;
            }
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.C)
        {
            CopyCutPagesNode(item, PagesClipboardMode.Copy);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.X)
        {
            CopyCutPagesNode(item, PagesClipboardMode.Cut);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.V)
        {
            PasteIntoSelectedTarget(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.D)
        {
            DuplicatePageNode(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Up)
        {
            MovePagesNodes(item, -1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Down)
        {
            MovePagesNodes(item, 1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && key == Key.Delete)
        {
            DeletePagesNode(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && key == Key.F2)
        {
            RenamePagesNode(item);
            e.Handled = true;
        }
    }

    private void ClearPagesTreeSelectionFromEscape()
    {
        _pagesMultiSelection.Clear();
        _pageTakeoffMultiSelection.Clear();
        _pagesRangeAnchorPath = null;
        _pageTakeoffRangeAnchorKey = null;

        if (PagesTree.SelectedItem is TreeViewItem selected)
        {
            _syncingPageTreeSelection = true;
            try
            {
                selected.IsSelected = false;
            }
            finally
            {
                _syncingPageTreeSelection = false;
            }
        }

        ApplyPagesMultiSelectionVisuals();
        TxtStatus.Text = "Pages selection cleared.";
    }

    private ContextMenu BuildPagesContextMenu(TreeViewItem item)
    {
        var menu = new ContextMenu();

        if (item.Tag is PageFolderNode folder)
        {
            int selectedCount = PageSelectionCount(item);
            bool isRoot = folder.IsRoot;
            bool canWrite = !IsCurrentJobReadOnly;
            bool canPaste = CanPasteInto(folder.FolderPath);
            bool hasChildren = Directory.Exists(folder.FolderPath) &&
                               Directory.EnumerateDirectories(folder.FolderPath).Any();
            IReadOnlyList<PageInfo> folderLegendPages = LegendPagesInFolder(folder.FolderPath);

            menu.Items.Add(MakeMenuItem("New Blank Sheet", canWrite, () => NewBlankPage(item)));
            menu.Items.Add(MakeMenuItem("New Folder", canWrite, () => NewPageFolder(item)));
            menu.Items.Add(MakeMenuItem("Rename Folder", canWrite && !isRoot && selectedCount <= 1, () => RenamePagesNode(item)));
            menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Delete Selected" : "Delete Folder", canWrite && (!isRoot || selectedCount > 1), () => DeletePagesNode(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeSubmenu(
                "Clipboard",
                MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Folder", !isRoot || selectedCount > 1, () => CopyCutPagesNode(item, PagesClipboardMode.Copy)),
                MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Folder", canWrite && (!isRoot || selectedCount > 1), () => CopyCutPagesNode(item, PagesClipboardMode.Cut)),
                MakeMenuItem("Paste Into Folder", canPaste, () => PasteIntoSelectedTarget(item))));
            menu.Items.Add(MakeSubmenu(
                "Organize",
                MakeMenuItem("Auto Create Folders", canWrite, () => RunPagesMutation("create page folders", () => AutoCreatePageFolders(folder.FolderPath))),
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up", canWrite && CanMovePagesNodes(item, -1), () => MovePagesNodes(item, -1)),
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down", canWrite && CanMovePagesNodes(item, 1), () => MovePagesNodes(item, 1)),
                MakeMenuItem("Sort Children A-Z", canWrite && hasChildren, () => SortFolderChildren(item, descending: false)),
                MakeMenuItem("Sort Children Z-A", canWrite && hasChildren, () => SortFolderChildren(item, descending: true)),
                MakeMenuItem("Sort Sheet Legends Auto in Folder", canWrite && CanSortPageLegends(folderLegendPages), () => RunPagesMutation("sort sheet legends", () => SortPageLegendsAuto(folderLegendPages))),
                MakeMenuItem("Sort Sheet Legends A-Z in Folder", canWrite && CanSortPageLegends(folderLegendPages), () => RunPagesMutation("sort sheet legends", () => SortPageLegendsByName(folderLegendPages))),
                MakeMenuItem("Reset Sheet Legend Orders in Folder", canWrite && HasCustomPageLegendOrders(folderLegendPages), () => RunPagesMutation("reset sheet legend order", () => ResetPageLegendOrders(folderLegendPages))),
                MakeMenuItem("Sort A/S in This Folder", canWrite, () => RunPagesMutation("sort pages into architecture and structure", () => SortPagesIntoArchStruct(folder.FolderPath))),
                MakeMenuItem("Sort D/Sec/WT in This Folder", canWrite, () => RunPagesMutation("sort pages by suffix", () => SortPagesBySuffix(folder.FolderPath))),
                MakeMenuItem("Repair Measurement Links", canWrite, () => RunPagesMutation("repair measurement links", RepairMeasurementPageLinks))));
            if (IsModuleEnabled(ModuleId.SheetManager))
            {
                var folderMetadataMenu = MakeSubmenu(
                    "PDF Metadata",
                    MakeMenuItem("Analyze PDF Metadata", canWrite, async () => await RunPagesMutationAsync("analyze PDF metadata", () => AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: false))),
                    MakeMenuItem("Auto Rename from PDF...", canWrite, async () => await RunPagesMutationAsync("auto-rename pages", () => AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: false))),
                    MakeMenuItem("Auto Scale from PDF...", canWrite, async () => await RunPagesMutationAsync("auto-scale pages", () => AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: true))),
                    MakeMenuItem("Auto Rename + Scale from PDF...", canWrite, async () => await RunPagesMutationAsync("auto-rename and scale pages", () => AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: true))));
                if (IsModuleEnabled(ModuleId.Ai))
                    folderMetadataMenu.Items.Add(MakeMenuItem("Queue GPT Metadata Fallback", canWrite, () => RunPagesMutation("queue PDF metadata analysis", () => QueuePdfMetadataFallback(item))));
                menu.Items.Add(folderMetadataMenu);
                menu.Items.Add(MakeSubmenu(
                    "Learning",
                    MakeMenuItem("Capture Final Learning Snapshot", canWrite, () => RunPagesMutation("capture a learning snapshot", () => CaptureFinalLearningSnapshot(item))),
                    MakeMenuItem("Review Project Learned Rules...", canWrite, () => RunPagesMutation("edit project learned rules", ReviewProjectLearnedRules)),
                    MakeMenuItem("Review Global Learned Rules...", canWrite, () => RunPagesMutation("edit global learned rules", ReviewLearnedRules))));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Open in Explorer", true, () => OpenFolderInExplorer(folder.FolderPath)));
        }
        else if (item.Tag is PageInfo page)
        {
            int selectedCount = PageSelectionCount(item);
            int selectedPageCount = GetSelectedPageEntries(item).Count(entry => entry.IsPage);
            bool canWrite = !IsCurrentJobReadOnly;
            IReadOnlyList<PageInfo> selectedLegendPages = SelectedLegendPages(item);
            int selectedLegendPageCount = selectedLegendPages.Count;
            bool multiLegendPages = selectedLegendPageCount > 1;
            string parent = Path.GetDirectoryName(page.FolderPath) ?? "";
            menu.Items.Add(MakeMenuItem("Open in New Tab", true, () => OpenPageInNewTab(page)));
            menu.Items.Add(MakeMenuItem(
                selectedPageCount > 1 ? $"Open {selectedPageCount} Selected in New Tabs" : "Open Selected in New Tabs",
                selectedPageCount > 1,
                () => OpenSelectedPagesInNewTabs(item)));
            if (IsModuleEnabled(ModuleId.DetachedSheets))
            {
                menu.Items.Add(MakeMenuItem(
                    selectedPageCount > 1 ? $"Detach {selectedPageCount} Selected to Windows" : "Detach Sheet to Window",
                    selectedPageCount >= 1,
                    () => OpenSelectedPagesInDetachedWindows(item, tileOnSecondMonitor: false)));
                menu.Items.Add(MakeMenuItem(
                    selectedPageCount > 1 ? $"Tile {Math.Min(64, selectedPageCount)} Selected on Monitor 2" : "Tile Sheet on Monitor 2",
                    selectedPageCount >= 1,
                    () => OpenSelectedPagesInDetachedWindows(item, tileOnSecondMonitor: true)));
            }
            if (IsModuleEnabled(ModuleId.SheetOverlay))
            {
                MenuItem overlayMenu = BuildSheetOverlayMenu(page);
                overlayMenu.IsEnabled = canWrite;
                menu.Items.Add(overlayMenu);
            }
            menu.Items.Add(MakeMenuItem(
                selectedPageCount > 1 ? $"Set Scale for {selectedPageCount} Selected..." : "Set Scale...",
                canWrite && selectedPageCount >= 1,
                () => RunPagesMutation("set sheet scale", () => SetSelectedPagesScaleFromContext(item))));
            menu.Items.Add(MakeMenuItem(
                selectedPageCount > 1 ? $"Apply Current Sheet Scale to {selectedPageCount} Selected" : "Apply Current Sheet Scale",
                canWrite && selectedPageCount >= 1 && CurrentPageScaleMetersPerPt() > 0,
                () => RunPagesMutation("apply the current sheet scale", () => ApplyCurrentScaleToSelectedPagesFromContext(item))));
            if (IsModuleEnabled(ModuleId.PdfOutput))
            {
                menu.Items.Add(MakeMenuItem(
                    selectedPageCount > 1 ? $"Export {selectedPageCount} Selected to PDF..." : "Export Sheet to PDF...",
                    selectedPageCount >= 1,
                    async () => await RunAsyncUiHandler(
                        () => ExportSheetsFromPagesTreeAsync(item),
                        "PDF export failed.",
                        "Export PDF")));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Rename Page", canWrite && selectedCount <= 1, () => RenamePagesNode(item)));
            menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Delete Selected" : "Delete Page", canWrite, () => DeletePagesNode(item)));
            menu.Items.Add(MakeMenuItem("Duplicate Page", canWrite && selectedCount <= 1, () => DuplicatePageNode(item)));
            menu.Items.Add(MakeMenuItem("New Blank Sheet in Parent", canWrite && selectedCount <= 1, () => NewBlankPage(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeSubmenu(
                "Clipboard",
                MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Page", true, () => CopyCutPagesNode(item, PagesClipboardMode.Copy)),
                MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Page", canWrite, () => CopyCutPagesNode(item, PagesClipboardMode.Cut)),
                MakeMenuItem("Paste Into Parent Folder", CanPasteInto(parent), () => PasteIntoSelectedTarget(item))));
            menu.Items.Add(MakeSubmenu(
                "Organize",
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up", canWrite && CanMovePagesNodes(item, -1), () => MovePagesNodes(item, -1)),
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down", canWrite && CanMovePagesNodes(item, 1), () => MovePagesNodes(item, 1)),
                MakeMenuItem("Move to Folder...", canWrite && selectedCount <= 1, () => MovePageToFolder(item)),
                MakeMenuItem(
                    multiLegendPages ? $"Sort {selectedLegendPageCount} Selected Sheet Legends Auto" : "Sort Sheet Legend Auto",
                    canWrite && (multiLegendPages ? CanSortPageLegends(selectedLegendPages) : CanSortPageLegend(page)),
                    () => RunPagesMutation("sort sheet legends", () => { if (multiLegendPages) SortPageLegendsAuto(selectedLegendPages); else SortPageLegendAuto(page); })),
                MakeMenuItem(
                    multiLegendPages ? $"Sort {selectedLegendPageCount} Selected Sheet Legends A-Z" : "Sort Sheet Legend A-Z",
                    canWrite && (multiLegendPages ? CanSortPageLegends(selectedLegendPages) : CanSortPageLegend(page)),
                    () => RunPagesMutation("sort sheet legends", () => { if (multiLegendPages) SortPageLegendsByName(selectedLegendPages); else SortPageLegendByName(page); })),
                MakeMenuItem(
                    multiLegendPages ? $"Reset {selectedLegendPageCount} Sheet Legend Orders" : "Reset Sheet Legend Order",
                    canWrite && (multiLegendPages ? HasCustomPageLegendOrders(selectedLegendPages) : HasCustomPageLegendOrder(page)),
                    () => RunPagesMutation("reset sheet legend order", () => { if (multiLegendPages) ResetPageLegendOrders(selectedLegendPages); else ResetPageLegendOrder(page); })),
                MakeMenuItem("Sort A/S into Arch/Struct", canWrite, () => RunPagesMutation("sort pages into architecture and structure", SortPagesIntoArchStruct)),
                MakeMenuItem("Sort D/Sec/WT by Suffix", canWrite, () => RunPagesMutation("sort pages by suffix", SortPagesBySuffix)),
                MakeMenuItem("Repair Measurement Links", canWrite, () => RunPagesMutation("repair measurement links", RepairMeasurementPageLinks))));
            if (IsModuleEnabled(ModuleId.SheetManager))
            {
                var pageMetadataMenu = MakeSubmenu(
                    "PDF Metadata",
                    MakeMenuItem("Analyze PDF Metadata", canWrite, async () => await RunPagesMutationAsync("analyze PDF metadata", () => AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: false))),
                    MakeMenuItem("Auto Rename from PDF...", canWrite, async () => await RunPagesMutationAsync("auto-rename pages", () => AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: false))),
                    MakeMenuItem("Auto Scale from PDF...", canWrite, async () => await RunPagesMutationAsync("auto-scale pages", () => AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: true))),
                    MakeMenuItem("Auto Rename + Scale from PDF...", canWrite, async () => await RunPagesMutationAsync("auto-rename and scale pages", () => AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: true))));
                if (IsModuleEnabled(ModuleId.Ai))
                    pageMetadataMenu.Items.Add(MakeMenuItem("Queue GPT Metadata Fallback", canWrite, () => RunPagesMutation("queue PDF metadata analysis", () => QueuePdfMetadataFallback(item))));
                pageMetadataMenu.Items.Add(MakeMenuItem("Open source_pdf.json", File.Exists(OurPlanCoreJobStore.SourcePdfMetadataPath(page.FolderPath)), () => OpenSourcePdfMetadata(page.FolderPath)));
                menu.Items.Add(pageMetadataMenu);
                menu.Items.Add(MakeSubmenu(
                    "Learning",
                    MakeMenuItem("Capture Final Learning Snapshot", canWrite, () => RunPagesMutation("capture a learning snapshot", () => CaptureFinalLearningSnapshot(item))),
                    MakeMenuItem("Review Project Learned Rules...", canWrite, () => RunPagesMutation("edit project learned rules", ReviewProjectLearnedRules)),
                    MakeMenuItem("Review Global Learned Rules...", canWrite, () => RunPagesMutation("edit global learned rules", ReviewLearnedRules))));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Open Page Folder in Explorer", true, () => OpenFolderInExplorer(page.FolderPath)));
        }
        else if (item.Tag is PageTakeoffNode node)
        {
            menu = BuildPageTakeoffContextMenu(node);
        }
        else if (item.Tag is PageOverlayNode overlay && IsModuleEnabled(ModuleId.SheetOverlay))
        {
            menu = BuildPageOverlayContextMenu(overlay);
        }

        ApplyModuleAvailabilityToMenu(menu);
        return menu;
    }
}
