using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void PagesTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (PagesTree.SelectedItem is not TreeViewItem item) return;
        Key key = KeyboardShortcutKeys.EffectiveKey(e);

        if (item.Tag is PageTakeoffNode node)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Up)
            {
                MovePageTakeoffLegendNodes(node, -1);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Down)
            {
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

    private ContextMenu BuildPagesContextMenu(TreeViewItem item)
    {
        var menu = new ContextMenu();

        if (item.Tag is PageFolderNode folder)
        {
            int selectedCount = PageSelectionCount(item);
            bool isRoot = folder.IsRoot;
            bool canPaste = CanPasteInto(folder.FolderPath);
            bool hasChildren = Directory.Exists(folder.FolderPath) &&
                               Directory.EnumerateDirectories(folder.FolderPath).Any();
            IReadOnlyList<PageInfo> folderLegendPages = LegendPagesInFolder(folder.FolderPath);

            menu.Items.Add(MakeMenuItem("New Folder", true, () => NewPageFolder(item)));
            menu.Items.Add(MakeMenuItem("Rename Folder", !isRoot && selectedCount <= 1, () => RenamePagesNode(item)));
            menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Delete Selected" : "Delete Folder", !isRoot || selectedCount > 1, () => DeletePagesNode(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeSubmenu(
                "Clipboard",
                MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Folder", !isRoot || selectedCount > 1, () => CopyCutPagesNode(item, PagesClipboardMode.Copy)),
                MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Folder", !isRoot || selectedCount > 1, () => CopyCutPagesNode(item, PagesClipboardMode.Cut)),
                MakeMenuItem("Paste Into Folder", canPaste, () => PasteIntoSelectedTarget(item))));
            menu.Items.Add(MakeSubmenu(
                "Organize",
                MakeMenuItem("Auto Create Folders", true, () => AutoCreatePageFolders(folder.FolderPath)),
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up", CanMovePagesNodes(item, -1), () => MovePagesNodes(item, -1)),
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down", CanMovePagesNodes(item, 1), () => MovePagesNodes(item, 1)),
                MakeMenuItem("Sort Children A-Z", hasChildren, () => SortFolderChildren(item, descending: false)),
                MakeMenuItem("Sort Children Z-A", hasChildren, () => SortFolderChildren(item, descending: true)),
                MakeMenuItem("Sort Sheet Legends Auto in Folder", CanSortPageLegends(folderLegendPages), () => SortPageLegendsAuto(folderLegendPages)),
                MakeMenuItem("Sort Sheet Legends A-Z in Folder", CanSortPageLegends(folderLegendPages), () => SortPageLegendsByName(folderLegendPages)),
                MakeMenuItem("Reset Sheet Legend Orders in Folder", HasCustomPageLegendOrders(folderLegendPages), () => ResetPageLegendOrders(folderLegendPages)),
                MakeMenuItem("Sort A/S in This Folder", true, () => SortPagesIntoArchStruct(folder.FolderPath)),
                MakeMenuItem("Sort D/Sec/WT in This Folder", true, () => SortPagesBySuffix(folder.FolderPath)),
                MakeMenuItem("Repair Measurement Links", true, RepairMeasurementPageLinks)));
            menu.Items.Add(MakeSubmenu(
                "PDF Metadata",
                MakeMenuItem("Analyze PDF Metadata", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: false)),
                MakeMenuItem("Auto Rename from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: false)),
                MakeMenuItem("Auto Scale from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: true)),
                MakeMenuItem("Auto Rename + Scale from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: true)),
                MakeMenuItem("Queue GPT Metadata Fallback", true, () => QueuePdfMetadataFallback(item))));
            menu.Items.Add(MakeSubmenu(
                "Learning",
                MakeMenuItem("Capture Final Learning Snapshot", true, () => CaptureFinalLearningSnapshot(item)),
                MakeMenuItem("Review Project Learned Rules...", true, ReviewProjectLearnedRules),
                MakeMenuItem("Review Global Learned Rules...", true, ReviewLearnedRules)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Open in Explorer", true, () => OpenFolderInExplorer(folder.FolderPath)));
        }
        else if (item.Tag is PageInfo page)
        {
            int selectedCount = PageSelectionCount(item);
            int selectedPageCount = GetSelectedPageEntries(item).Count(entry => entry.IsPage);
            IReadOnlyList<PageInfo> selectedLegendPages = SelectedLegendPages(item);
            int selectedLegendPageCount = selectedLegendPages.Count;
            bool multiLegendPages = selectedLegendPageCount > 1;
            string parent = Path.GetDirectoryName(page.FolderPath) ?? "";
            menu.Items.Add(MakeMenuItem("Open in New Tab", true, () => OpenPageInNewTab(page)));
            menu.Items.Add(MakeMenuItem(
                selectedPageCount > 1 ? $"Open {selectedPageCount} Selected in New Tabs" : "Open Selected in New Tabs",
                selectedPageCount > 1,
                () => OpenSelectedPagesInNewTabs(item)));
            menu.Items.Add(MakeMenuItem(
                selectedPageCount > 1 ? $"Detach {selectedPageCount} Selected to Windows" : "Detach Sheet to Window",
                selectedPageCount >= 1,
                () => OpenSelectedPagesInDetachedWindows(item, tileOnSecondMonitor: false)));
            menu.Items.Add(MakeMenuItem(
                selectedPageCount > 1 ? $"Tile {Math.Min(64, selectedPageCount)} Selected on Monitor 2" : "Tile Sheet on Monitor 2",
                selectedPageCount >= 1,
                () => OpenSelectedPagesInDetachedWindows(item, tileOnSecondMonitor: true)));
            menu.Items.Add(BuildSheetOverlayMenu(page));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Rename Page", selectedCount <= 1, () => RenamePagesNode(item)));
            menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Delete Selected" : "Delete Page", true, () => DeletePagesNode(item)));
            menu.Items.Add(MakeMenuItem("Duplicate Page", selectedCount <= 1, () => DuplicatePageNode(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeSubmenu(
                "Clipboard",
                MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Page", true, () => CopyCutPagesNode(item, PagesClipboardMode.Copy)),
                MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Page", true, () => CopyCutPagesNode(item, PagesClipboardMode.Cut)),
                MakeMenuItem("Paste Into Parent Folder", CanPasteInto(parent), () => PasteIntoSelectedTarget(item))));
            menu.Items.Add(MakeSubmenu(
                "Organize",
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up", CanMovePagesNodes(item, -1), () => MovePagesNodes(item, -1)),
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down", CanMovePagesNodes(item, 1), () => MovePagesNodes(item, 1)),
                MakeMenuItem("Move to Folder...", selectedCount <= 1, () => MovePageToFolder(item)),
                MakeMenuItem(
                    multiLegendPages ? $"Sort {selectedLegendPageCount} Selected Sheet Legends Auto" : "Sort Sheet Legend Auto",
                    multiLegendPages ? CanSortPageLegends(selectedLegendPages) : CanSortPageLegend(page),
                    () => { if (multiLegendPages) SortPageLegendsAuto(selectedLegendPages); else SortPageLegendAuto(page); }),
                MakeMenuItem(
                    multiLegendPages ? $"Sort {selectedLegendPageCount} Selected Sheet Legends A-Z" : "Sort Sheet Legend A-Z",
                    multiLegendPages ? CanSortPageLegends(selectedLegendPages) : CanSortPageLegend(page),
                    () => { if (multiLegendPages) SortPageLegendsByName(selectedLegendPages); else SortPageLegendByName(page); }),
                MakeMenuItem(
                    multiLegendPages ? $"Reset {selectedLegendPageCount} Sheet Legend Orders" : "Reset Sheet Legend Order",
                    multiLegendPages ? HasCustomPageLegendOrders(selectedLegendPages) : HasCustomPageLegendOrder(page),
                    () => { if (multiLegendPages) ResetPageLegendOrders(selectedLegendPages); else ResetPageLegendOrder(page); }),
                MakeMenuItem("Sort A/S into Arch/Struct", true, SortPagesIntoArchStruct),
                MakeMenuItem("Sort D/Sec/WT by Suffix", true, SortPagesBySuffix),
                MakeMenuItem("Repair Measurement Links", true, RepairMeasurementPageLinks)));
            menu.Items.Add(MakeSubmenu(
                "PDF Metadata",
                MakeMenuItem("Analyze PDF Metadata", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: false)),
                MakeMenuItem("Auto Rename from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: false)),
                MakeMenuItem("Auto Scale from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: true)),
                MakeMenuItem("Auto Rename + Scale from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: true)),
                MakeMenuItem("Queue GPT Metadata Fallback", true, () => QueuePdfMetadataFallback(item)),
                MakeMenuItem("Open source_pdf.json", File.Exists(OurPlaneCoreJobStore.SourcePdfMetadataPath(page.FolderPath)), () => OpenSourcePdfMetadata(page.FolderPath))));
            menu.Items.Add(MakeSubmenu(
                "Learning",
                MakeMenuItem("Capture Final Learning Snapshot", true, () => CaptureFinalLearningSnapshot(item)),
                MakeMenuItem("Review Project Learned Rules...", true, ReviewProjectLearnedRules),
                MakeMenuItem("Review Global Learned Rules...", true, ReviewLearnedRules)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Open Page Folder in Explorer", true, () => OpenFolderInExplorer(page.FolderPath)));
        }
        else if (item.Tag is PageTakeoffNode node)
        {
            menu = BuildPageTakeoffContextMenu(node);
        }
        else if (item.Tag is PageOverlayNode overlay)
        {
            menu = BuildPageOverlayContextMenu(overlay);
        }

        return menu;
    }
}
