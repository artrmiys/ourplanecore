using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void AttachContextMenu(TreeViewItem tvi, TakeoffItem item)
    {
        var menu = new ContextMenu();
        int selectedCount = TakeoffSelectionCount(tvi);
        bool singleSelection = selectedCount <= 1;

        var activeTarget = new MenuItem { Header = IsActiveTakeoffItem(item) ? "Active Target" : "Set Active Target" };
        activeTarget.Click += (_, _) => SetActiveTakeoffTarget(tvi, item);
        activeTarget.IsEnabled = singleSelection;
        menu.Items.Add(activeTarget);
        menu.Items.Add(new Separator());

        var properties = new MenuItem { Header = "Properties..." };
        properties.Click += (_, _) => EditTakeoffItemProperties(tvi, item);
        properties.IsEnabled = singleSelection;
        menu.Items.Add(properties);
        menu.Items.Add(MakeMenuItem(
            item.IsJoistArea ? "Joist Properties..." : "Use Area As Joists...",
            singleSelection && OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "area",
            () => EditTakeoffItemProperties(tvi, item)));
        menu.Items.Add(MakeMenuItem(
            "Generate Joists / Draw Direction",
            singleSelection && OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "area",
            () => SetJoistDirectionFromSelectedLine(tvi, item)));

        int selectedItemsCount = TakeoffItemsForSelection(tvi).Count;
        menu.Items.Add(MakeMenuItem(
            selectedItemsCount > 1 ? $"Bulk Properties ({selectedItemsCount} Items)..." : "Bulk Properties...",
            selectedItemsCount > 1,
            () => EditSelectedTakeoffProperties(tvi)));

        var rename = new MenuItem { Header = "Rename..." };
        rename.Click += (_, _) => RenameItem(tvi, item);
        rename.IsEnabled = singleSelection;
        menu.Items.Add(rename);

        var newSection = new MenuItem { Header = item.MeasurementType == "point" ? "Add Count" : "New Section" };
        newSection.Click += (_, _) => StartNewSection(tvi, item);
        newSection.IsEnabled = singleSelection;
        menu.Items.Add(newSection);

        var unitPrice = new MenuItem { Header = "Set Unit Price" };
        unitPrice.Click += (_, _) => SetUnitPrice(item);
        unitPrice.IsEnabled = singleSelection;
        menu.Items.Add(unitPrice);

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Item", true, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Copy)));
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Item", true, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Cut)));
        menu.Items.Add(MakeMenuItem(
            "Paste Into Parent Folder",
            CanPasteTakeoffsInto(Path.GetDirectoryName(item.FolderPath)),
            () => PasteIntoSelectedTakeoffTarget(tvi)));
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Duplicate Selected" : "Duplicate Item", true, () => DuplicateTakeoffNode(tvi)));

        menu.Items.Add(new Separator());

        var moveUp = new MenuItem
        {
            Header = selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up",
            IsEnabled = CanMoveTakeoffNodes(tvi, -1),
        };
        moveUp.Click += (_, _) => MoveTakeoffNodes(tvi, -1);
        menu.Items.Add(moveUp);

        var moveDown = new MenuItem
        {
            Header = selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down",
            IsEnabled = CanMoveTakeoffNodes(tvi, 1),
        };
        moveDown.Click += (_, _) => MoveTakeoffNodes(tvi, 1);
        menu.Items.Add(moveDown);

        menu.Items.Add(new Separator());

        var delete = new MenuItem { Header = selectedCount > 1 ? "Delete selected takeoffs" : "Delete item + measurements" };
        delete.Click += (_, _) => DeleteTakeoffNodes(tvi);
        menu.Items.Add(delete);

        tvi.ContextMenu = menu;
    }

    private void AttachFolderContextMenu(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        var menu = new ContextMenu();
        int selectedCount = TakeoffSelectionCount(tvi);
        bool singleSelection = selectedCount <= 1;
        bool canEditFolder = !folder.IsRoot && singleSelection;

        var newFolder = new MenuItem { Header = "New Folder" };
        newFolder.Click += (_, _) =>
        {
            tvi.IsSelected = true;
            BtnNewTakeoffFolder_Click(tvi, new RoutedEventArgs());
        };
        menu.Items.Add(newFolder);

        var newItem = new MenuItem { Header = "New Item" };
        newItem.Click += (_, _) =>
        {
            tvi.IsSelected = true;
            BtnNewItem_Click(tvi, new RoutedEventArgs());
        };
        menu.Items.Add(newItem);

        menu.Items.Add(MakeMenuItem("Auto Create Tree", true, () => AutoCreateTakeoffTree(folder.FolderPath)));
        menu.Items.Add(MakeMenuItem("Create Folders From Pages", true, () => AutoCreateTakeoffFoldersFromPages(folder.FolderPath)));

        menu.Items.Add(new Separator());

        var rename = new MenuItem { Header = "Rename Folder…" };
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Folder", !folder.IsRoot, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Copy)));
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Folder", !folder.IsRoot, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Cut)));
        menu.Items.Add(MakeMenuItem("Paste Into Folder", CanPasteTakeoffsInto(folder.FolderPath), () => PasteIntoSelectedTakeoffTarget(tvi)));
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Duplicate Selected" : "Duplicate Folder", !folder.IsRoot, () => DuplicateTakeoffNode(tvi)));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem("Folder Properties...", canEditFolder, () => EditTakeoffFolderProperties(tvi, folder)));
        int nestedTakeoffCount = TakeoffItemsForSelection(tvi).Count;
        menu.Items.Add(MakeMenuItem(
            nestedTakeoffCount > 1 ? $"Bulk Item Properties ({nestedTakeoffCount})..." : "Bulk Item Properties...",
            nestedTakeoffCount > 0,
            () => EditSelectedTakeoffProperties(tvi)));

        rename.Click += (_, _) => RenameTakeoffFolder(tvi, folder);
        rename.IsEnabled = canEditFolder;
        menu.Items.Add(rename);

        var moveUp = new MenuItem
        {
            Header = selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up",
            IsEnabled = CanMoveTakeoffNodes(tvi, -1),
        };
        moveUp.Click += (_, _) => MoveTakeoffNodes(tvi, -1);
        menu.Items.Add(moveUp);

        var moveDown = new MenuItem
        {
            Header = selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down",
            IsEnabled = CanMoveTakeoffNodes(tvi, 1),
        };
        moveDown.Click += (_, _) => MoveTakeoffNodes(tvi, 1);
        menu.Items.Add(moveDown);

        menu.Items.Add(new Separator());

        var sortAz = new MenuItem { Header = "Sort Children A-Z" };
        sortAz.Click += (_, _) => SortTakeoffChildren(folder.FolderPath, descending: false);
        menu.Items.Add(sortAz);

        var sortZa = new MenuItem { Header = "Sort Children Z-A" };
        sortZa.Click += (_, _) => SortTakeoffChildren(folder.FolderPath, descending: true);
        menu.Items.Add(sortZa);

        menu.Items.Add(new Separator());

        var open = new MenuItem { Header = "Open in Explorer" };
        open.Click += (_, _) => OpenFolderInExplorer(folder.FolderPath);
        menu.Items.Add(open);

        var delete = new MenuItem
        {
            Header = selectedCount > 1 ? "Delete selected takeoffs" : "Delete folder + children",
            IsEnabled = !folder.IsRoot,
        };
        delete.Click += (_, _) => DeleteTakeoffNodes(tvi);
        menu.Items.Add(delete);

        tvi.ContextMenu = menu;
    }

    private ContextMenu BuildTakeoffsRootContextMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(MakeMenuItem(
            "Auto Create Tree",
            _currentJob != null,
            () =>
            {
                if (_currentJob != null)
                    AutoCreateTakeoffTree(_currentJob.TakeoffsRoot);
            }));
        menu.Items.Add(MakeMenuItem(
            "Create Folders From Pages",
            _currentJob != null,
            () =>
            {
                if (_currentJob != null)
                    AutoCreateTakeoffFoldersFromPages(_currentJob.TakeoffsRoot);
            }));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem(
            "Paste Into Root",
            _currentJob != null && CanPasteTakeoffsInto(_currentJob.TakeoffsRoot),
            () =>
            {
                if (_currentJob != null)
                    PasteTakeoffsIntoFolder(_currentJob.TakeoffsRoot);
            }));

        menu.Items.Add(new Separator());

        var sortAz = new MenuItem { Header = "Sort Root A-Z" };
        sortAz.Click += (_, _) =>
        {
            if (_currentJob != null)
                SortTakeoffChildren(_currentJob.TakeoffsRoot, descending: false);
        };
        menu.Items.Add(sortAz);

        var sortZa = new MenuItem { Header = "Sort Root Z-A" };
        sortZa.Click += (_, _) =>
        {
            if (_currentJob != null)
                SortTakeoffChildren(_currentJob.TakeoffsRoot, descending: true);
        };
        menu.Items.Add(sortZa);

        menu.Items.Add(new Separator());

        var open = new MenuItem { Header = "Open Takeoffs in Explorer" };
        open.Click += (_, _) =>
        {
            if (_currentJob != null)
                OpenFolderInExplorer(_currentJob.TakeoffsRoot);
        };
        menu.Items.Add(open);

        return menu;
    }

    private void RefreshTakeoffNodeContextMenu(TreeViewItem item)
    {
        switch (item.Tag)
        {
            case TakeoffItem takeoff:
                AttachContextMenu(item, takeoff);
                break;
            case TakeoffFolderNode folder:
                AttachFolderContextMenu(item, folder);
                break;
        }
    }
}
