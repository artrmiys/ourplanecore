using System.Windows.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private bool ExecuteTreeKeyboardCommand(string id)
    {
        if (id == "pages.clearSelection") { ClearPagesTreeSelectionFromEscape(); return true; }
        if (id == "pages.undoDelete") { TryUndoLastPageDelete(); return true; }
        if (id == "takeoffs.undoDelete") { TryUndoLastTakeoffDelete(); return true; }
        if (id == "takeoffs.delete") { TryDeleteTakeoffsKeyboardSelection(); return true; }
        if (KeyboardShortcutDefaults.ContextFor(id) == KeyboardCommandContext.Pages)
        {
            if (PagesTree.SelectedItem is TreeViewItem pageNode) ExecutePagesKeyboardCommand(id, pageNode);
            return true;
        }
        if (KeyboardShortcutDefaults.ContextFor(id) == KeyboardCommandContext.Takeoffs)
        {
            if (TakeoffsTree.SelectedItem is TreeViewItem takeoffNode) ExecuteTakeoffsKeyboardCommand(id, takeoffNode);
            return true;
        }
        return false;
    }

    private void ExecutePagesKeyboardCommand(string id, TreeViewItem item)
    {
        if (item.Tag is PageTakeoffNode node)
        {
            if (id is "pages.moveUp" or "pages.moveDown" && EnsureCurrentJobWritable("reorder the sheet legend"))
                MovePageTakeoffLegendNodes(node, id == "pages.moveUp" ? -1 : 1);
            return;
        }
        switch (id)
        {
            case "pages.copy": CopyCutPagesNode(item, PagesClipboardMode.Copy); break;
            case "pages.cut": CopyCutPagesNode(item, PagesClipboardMode.Cut); break;
            case "pages.paste": PasteIntoSelectedTarget(item); break;
            case "pages.duplicate": DuplicatePageNode(item); break;
            case "pages.moveUp": MovePagesNodes(item, -1); break;
            case "pages.moveDown": MovePagesNodes(item, 1); break;
            case "pages.delete": DeletePagesNode(item); break;
            case "pages.rename": RenamePagesNode(item); break;
        }
    }

    private void ExecuteTakeoffsKeyboardCommand(string id, TreeViewItem item)
    {
        if (item.Tag is TakeoffMeasurementNode section)
        {
            if (id == "takeoffs.showSections")
                SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(section, fallbackToAnchor: true), section);
            else if (id is "takeoffs.moveUp" or "takeoffs.moveDown" && EnsureCurrentJobWritable("move takeoff sections"))
                MoveTakeoffSections(section, id == "takeoffs.moveUp" ? -1 : 1);
            else if (id == "takeoffs.rename" && SelectedTakeoffSectionNodes(section, fallbackToAnchor: true).Count <= 1 &&
                EnsureCurrentJobWritable("rename a takeoff section"))
                RenameSection(section.Item, section.Measurement);
            return;
        }
        switch (id)
        {
            case "takeoffs.copy": CopyCutTakeoffNode(item, TakeoffsClipboardMode.Copy); break;
            case "takeoffs.cut": CopyCutTakeoffNode(item, TakeoffsClipboardMode.Cut); break;
            case "takeoffs.paste": PasteIntoSelectedTakeoffTarget(item); break;
            case "takeoffs.duplicate": DuplicateTakeoffNode(item); break;
            case "takeoffs.moveUp": MoveTakeoffNodes(item, -1); break;
            case "takeoffs.moveDown": MoveTakeoffNodes(item, 1); break;
            case "takeoffs.rename" when TakeoffSelectionCount(item) <= 1:
                if (item.Tag is TakeoffItem takeoff) RenameItem(item, takeoff);
                else if (item.Tag is TakeoffFolderNode folder && !folder.IsRoot) RenameTakeoffFolder(item, folder);
                break;
        }
    }
}
