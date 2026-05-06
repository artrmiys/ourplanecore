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

        if (item.Tag is PageTakeoffNode node)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
            {
                MovePageTakeoffLegendNodes(node, -1);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
            {
                MovePageTakeoffLegendNodes(node, 1);
                e.Handled = true;
            }
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            CopyCutPagesNode(item, PagesClipboardMode.Copy);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.X)
        {
            CopyCutPagesNode(item, PagesClipboardMode.Cut);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            PasteIntoSelectedTarget(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D)
        {
            DuplicatePageNode(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
        {
            MovePagesNodes(item, -1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
        {
            MovePagesNodes(item, 1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
        {
            DeletePagesNode(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.F2)
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
                MakeMenuItem("Sort A/S into Arch/Struct", true, SortPagesIntoArchStruct),
                MakeMenuItem("Sort D/Sec/WT by Suffix", true, SortPagesBySuffix),
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
            string parent = Path.GetDirectoryName(page.FolderPath) ?? "";
            menu.Items.Add(MakeMenuItem("Open in New Tab", true, () => OpenPageInNewTab(page)));
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
                MakeMenuItem("Sort Sheet Legend A-Z", CanSortPageLegend(page), () => SortPageLegendByName(page)),
                MakeMenuItem("Reset Sheet Legend Order", HasCustomPageLegendOrder(page), () => ResetPageLegendOrder(page)),
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
