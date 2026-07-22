using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    // Section model (mirrors the Pages tree menu so both trees feel identical):
    //   1. Primary action + item-specific tool submenus
    //   2. Rename / Delete / Duplicate
    //   3. Properties
    //   4. Clipboard ▸ / Organize ▸
    private void AttachContextMenu(TreeViewItem tvi, TakeoffItem item)
    {
        var menu = new ContextMenu();
        int selectedCount = TakeoffSelectionCount(tvi);
        bool singleSelection = selectedCount <= 1;
        bool isArea = OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "area";
        bool isLine = OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "line";
        int selectedItemsCount = TakeoffItemsForSelection(tvi).Count;
        string parentFolder = Path.GetDirectoryName(item.FolderPath) ?? "";

        // 1 — primary action + tools
        menu.Items.Add(MakeMenuItem(
            IsActiveTakeoffItem(item) ? "Active Target" : "Set Active Target",
            singleSelection && CanChangeActiveTakeoffTarget(item),
            () => SetActiveTakeoffTarget(tvi, item)));
        menu.Items.Add(MakeMenuItem(
            item.MeasurementType == "point" ? "Add Count" : "New Section",
            singleSelection,
            () => StartNewSection(tvi, item)));
        if (isArea && IsModuleEnabled(ModuleId.AdvancedTakeoffTools))
        {
            Measurement? selectedJoistArea = item.IsJoistArea
                ? SelectedJoistAreaMeasurement(item)
                : null;
            menu.Items.Add(MakeMenuItem("Create Line Grid...", singleSelection, () => CreateLineGridFromSelectedArea(tvi, item)));
            menu.Items.Add(MakeMenuItem(
                "Trace Walls Inside Area...",
                singleSelection && _currentJob != null && _currentPage != null,
                () => TraceWallsFromSelectedArea(tvi, item)));
            menu.Items.Add(MakeSubmenu(
                "Joist",
                MakeMenuItem(
                    item.IsJoistArea ? "Joist Properties..." : "Use Area As Joists...",
                    singleSelection,
                    () => EditTakeoffItemProperties(tvi, item)),
                MakeMenuItem(
                    "Add Joists",
                    singleSelection && item.IsJoistArea && IsCurrentJobWritable,
                    () => AddJoistsToAllAreas(item)),
                MakeMenuItem(
                    "Add Extra Joist",
                    singleSelection && selectedJoistArea?.JoistDirectionLocked == true && IsCurrentJobWritable,
                    () => StartExtraJoistPlacement(item, selectedJoistArea!)),
                MakeMenuItem("Set / Reset Joist Direction", singleSelection, () => SetJoistDirectionFromSelectedLine(tvi, item)),
                MakeMenuItem("Set Direction for All Areas", singleSelection, () => SetJoistDirectionForAllAreasFromSelectedLine(tvi, item))));
        }
        if (isLine && IsModuleEnabled(ModuleId.AdvancedTakeoffTools))
        {
            menu.Items.Add(MakeMenuItem(
                "Create Count Points Along Lines...",
                singleSelection,
                () => CreatePointsAlongLineTakeoffItem(item)));
        }
        if (item.MeasurementType == "point" && IsModuleEnabled(ModuleId.AdvancedTakeoffTools))
        {
            menu.Items.Add(MakeMenuItem(
                "Find Similar...",
                singleSelection && _currentJob != null && _currentPage != null,
                () => StartFindSimilarForItem(tvi, item)));
        }
        menu.Items.Add(BuildTakeoffCountDisplayMenu(tvi));
        if (IsModuleEnabled(ModuleId.ThreeD))
            menu.Items.Add(BuildTakeoff3DMenu(tvi));

        menu.Items.Add(new Separator());

        // 2 — rename / delete / duplicate
        menu.Items.Add(MakeMenuItem("Rename...", singleSelection, () => RenameItem(tvi, item)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? "Delete selected takeoffs" : "Delete item + measurements",
            true,
            () => DeleteTakeoffNodes(tvi)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? "Duplicate Selected" : "Duplicate Item",
            true,
            () => DuplicateTakeoffNode(tvi)));

        menu.Items.Add(new Separator());

        // 3 — properties
        menu.Items.Add(MakeMenuItem("Properties...", singleSelection, () => EditTakeoffItemProperties(tvi, item)));
        menu.Items.Add(MakeMenuItem(
            selectedItemsCount > 1 ? $"Bulk Properties ({selectedItemsCount} Items)..." : "Bulk Properties...",
            selectedItemsCount > 1,
            () => EditSelectedTakeoffProperties(tvi)));
        if (IsModuleEnabled(ModuleId.Estimating))
            menu.Items.Add(MakeMenuItem("Set Unit Price", singleSelection, () => SetUnitPrice(item)));

        menu.Items.Add(new Separator());

        // 4 — clipboard / organize
        menu.Items.Add(MakeSubmenu(
            "Clipboard",
            MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Item", true, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Copy)),
            MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Item", true, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Cut)),
            MakeMenuItem("Paste Into Parent Folder", CanPasteTakeoffsInto(parentFolder), () => PasteIntoSelectedTakeoffTarget(tvi))));
        menu.Items.Add(MakeSubmenu(
            "Organize",
            MakeMenuItem(
                selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up",
                CanMoveTakeoffNodes(tvi, -1),
                () => MoveTakeoffNodes(tvi, -1)),
            MakeMenuItem(
                selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down",
                CanMoveTakeoffNodes(tvi, 1),
                () => MoveTakeoffNodes(tvi, 1))));

        tvi.ContextMenu = menu;
    }

    private void AttachFolderContextMenu(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        var menu = new ContextMenu();
        int selectedCount = TakeoffSelectionCount(tvi);
        bool singleSelection = selectedCount <= 1;
        bool canEditFolder = !folder.IsRoot && singleSelection;

        int nestedTakeoffCount = TakeoffItemsForSelection(tvi).Count;

        // 1 — create
        menu.Items.Add(MakeMenuItem("New Folder", true, () =>
        {
            tvi.IsSelected = true;
            BtnNewTakeoffFolder_Click(tvi, new RoutedEventArgs());
        }));
        menu.Items.Add(MakeMenuItem("New Item", true, () =>
        {
            tvi.IsSelected = true;
            BtnNewItem_Click(tvi, new RoutedEventArgs());
        }));

        menu.Items.Add(new Separator());

        // 2 — rename / delete
        menu.Items.Add(MakeMenuItem("Rename Folder…", canEditFolder, () => RenameTakeoffFolder(tvi, folder)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? "Delete selected takeoffs" : "Delete folder + children",
            !folder.IsRoot,
            () => DeleteTakeoffNodes(tvi)));

        menu.Items.Add(new Separator());

        // 3 — properties
        menu.Items.Add(MakeMenuItem("Folder Properties...", canEditFolder, () => EditTakeoffFolderProperties(tvi, folder)));
        menu.Items.Add(MakeMenuItem(
            nestedTakeoffCount > 1 ? $"Bulk Item Properties ({nestedTakeoffCount})..." : "Bulk Item Properties...",
            nestedTakeoffCount > 0,
            () => EditSelectedTakeoffProperties(tvi)));
        menu.Items.Add(MakeMenuItem(
            nestedTakeoffCount > 1 ? $"Random Colors for Items ({nestedTakeoffCount})" : "Random Colors for Items",
            nestedTakeoffCount > 0,
            () => RandomizeTakeoffItemColors(tvi)));
        menu.Items.Add(BuildTakeoffCountDisplayMenu(tvi));
        if (IsModuleEnabled(ModuleId.ThreeD))
            menu.Items.Add(BuildTakeoff3DMenu(tvi));

        menu.Items.Add(new Separator());

        // 4 — clipboard / organize
        menu.Items.Add(MakeSubmenu(
            "Clipboard",
            MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Folder", !folder.IsRoot, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Copy)),
            MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Folder", !folder.IsRoot, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Cut)),
            MakeMenuItem("Paste Into Folder", CanPasteTakeoffsInto(folder.FolderPath), () => PasteIntoSelectedTakeoffTarget(tvi)),
            MakeMenuItem(selectedCount > 1 ? "Duplicate Selected" : "Duplicate Folder", !folder.IsRoot, () => DuplicateTakeoffNode(tvi))));
        var organize = MakeSubmenu("Organize");
        if (IsModuleEnabled(ModuleId.TakeoffAutomation))
        {
            organize.Items.Add(MakeMenuItem("Auto Create Tree", true, () => AutoCreateTakeoffTree(folder.FolderPath)));
            organize.Items.Add(MakeMenuItem("Create Folders From Pages", true, () => AutoCreateTakeoffFoldersFromPages(folder.FolderPath)));
        }
        organize.Items.Add(MakeMenuItem(
                selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up",
                CanMoveTakeoffNodes(tvi, -1),
                () => MoveTakeoffNodes(tvi, -1)));
        organize.Items.Add(MakeMenuItem(
                selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down",
                CanMoveTakeoffNodes(tvi, 1),
                () => MoveTakeoffNodes(tvi, 1)));
        organize.Items.Add(MakeMenuItem("Sort Children A-Z", true, () => SortTakeoffChildren(folder.FolderPath, descending: false)));
        organize.Items.Add(MakeMenuItem("Sort Children Z-A", true, () => SortTakeoffChildren(folder.FolderPath, descending: true)));
        menu.Items.Add(organize);

        menu.Items.Add(new Separator());

        // 5 — explorer
        menu.Items.Add(MakeMenuItem("Open in Explorer", true, () => OpenFolderInExplorer(folder.FolderPath)));

        tvi.ContextMenu = menu;
    }

    private ContextMenu BuildTakeoffsRootContextMenu()
    {
        var menu = new ContextMenu();

        if (IsModuleEnabled(ModuleId.TakeoffAutomation))
        {
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
        }

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

    private MenuItem BuildTakeoff3DMenu(TreeViewItem anchor)
    {
        int selectedItems = TakeoffItemsForSelection(anchor).Count;
        var menu = new MenuItem
        {
            Header = "3D",
            IsEnabled = selectedItems > 0,
        };

        var wall = new MenuItem
        {
            Header = selectedItems > 1 ? $"Wall from Selected ({selectedItems})" : "Wall",
            IsEnabled = CanBuild3DWallsFromTakeoffSelection(anchor),
        };
        wall.Click += (_, _) => Build3DWallsFromTakeoffSelection(anchor, switchTo3DTab: true);
        menu.Items.Add(wall);
        var roof = new MenuItem
        {
            Header = selectedItems > 1 ? $"Roof Base from Selected Areas ({selectedItems})" : "Roof Base from Area",
            IsEnabled = CanBuildRoofBaseFromTakeoffSelection(anchor),
        };
        roof.Click += (_, _) => BuildRoofFromRfAreas(anchor, switchTo3DTab: true);
        menu.Items.Add(roof);
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
