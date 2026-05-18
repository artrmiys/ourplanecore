using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void BtnNewItem_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "New Takeoff Item",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string parentFolder = NewTakeoffItemParentFolder();
        string measurementType = ResolveTakeoffFolderDefaultMeasurementType(
            parentFolder,
            CurrentToolMeasurementType());
        // Every new takeoff gets its own random color not already on the
        // sheet; still editable in the dialog and later via properties.
        string defaultColor = RandomTakeoffColor(_viewport.ActiveColor);
        var dlg = new NewItemDialog(
            measurementType,
            DefaultTakeoffNameForFolder(measurementType, parentFolder),
            defaultColor: defaultColor)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true) return;

        TakeoffAutoRouteResult route = TakeoffAutoRoutingService.ResolveRoute(
            _currentJob,
            parentFolder,
            dlg.ItemName,
            dlg.ItemType,
            _currentPage?.Name ?? "",
            _currentPage?.FolderPath ?? "");
        parentFolder = route.ParentFolder;

        var item = CreateUniqueTakeoffItem(dlg.ItemName, dlg.ItemColor, dlg.ItemType, parentFolder);
        ApplyTakeoffFolderDefaultsToNewItem(item, parentFolder);
        ApplyNewCountSymbolToItemIfNeeded(item, dlg.ItemType);
        LoadTakeoffsForJob();

        TakeoffItem activeItem = _takeoffItems.FirstOrDefault(t =>
            string.Equals(t.FolderPath, item.FolderPath, StringComparison.OrdinalIgnoreCase)) ?? item;
        _activeItem = activeItem;
        _activeTakeoffParentFolder = Path.GetDirectoryName(activeItem.FolderPath) ?? parentFolder;
        _viewport.ActiveColor = activeItem.Color;
        _viewport.ActiveTakeoffFolder = activeItem.FolderPath;
        _viewport.ActiveCountSymbol = activeItem.CountSymbol;
        SelectTakeoffNodeByFolder(activeItem.FolderPath);
        UpdateToolStatus();
        RefreshActiveTakeoffVisuals();
        UpdateTotalDisplay();
        TxtStatus.Text = route.Routed
            ? $"New item: {activeItem.Name}. {route.Message}"
            : $"New item: {activeItem.Name}.";
    }

    private void BtnNewTakeoffFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "New Takeoff Folder",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string parentFolder = CurrentTakeoffParentFolder();
        string? name = ShowInputDialog("Folder name:", "New Takeoff Folder", "New Folder");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            string folderPath = OurPlaneCoreJobStore.CreateTakeoffFolder(_currentJob, parentFolder, name);
            var node = new TakeoffFolderNode
            {
                Name = OurPlaneCoreJobStore.DisplayName(folderPath),
                FolderPath = folderPath,
            };
            var parent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
            var tvi = AddTakeoffFolderTreeItem(node, parent);
            if (parent is TreeViewItem parentTvi)
                parentTvi.IsExpanded = true;
            tvi.IsSelected = true;
            _activeTakeoffParentFolder = folderPath;
        }
        catch (Exception ex)
        {
            ShowOperationError("New Takeoff Folder", ex);
        }
    }

    private void BtnAutoTakeoffTree_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Auto Takeoff Tree",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AutoCreateTakeoffTree(CurrentTakeoffParentFolder());
    }

    private void BtnAutoTakeoffFromPages_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Takeoff Folders From Pages",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AutoCreateTakeoffFoldersFromPages(CurrentTakeoffParentFolder());
    }

    private void AutoCreateTakeoffTree(string baseFolder)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
            return;

        FlushTakeoffAutosaves();
        string mode = ResolveFolderTemplateMode();
        string modeLabel = FolderTemplateModeLabel(mode);
        string baseName = string.Equals(baseFolder, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase)
            ? "Takeoffs"
            : OurPlaneCoreJobStore.DisplayName(baseFolder);
        var confirm = MessageBox.Show(
            $"Create standard {modeLabel} takeoff tree under '{baseName}'?\n\nExisting folders will be skipped.",
            "Auto Takeoff Tree",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            FolderTemplateResult result = PlanSwiftFolderTemplateService.CreateTakeoffTree(baseFolder, mode);
            LoadTakeoffsForJob();
            SelectTakeoffNodeByFolder(baseFolder);
            TxtStatus.Text = $"Takeoff tree ({modeLabel}): created {result.Created}, skipped {result.Skipped}, errors {result.Errors}.";
            ShowFolderTemplateErrors("Auto Takeoff Tree", result);
        }
        catch (Exception ex)
        {
            ShowOperationError("Auto Takeoff Tree", ex);
        }
    }

    private void AutoCreateTakeoffFoldersFromPages(string baseFolder)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
            return;

        FlushTakeoffAutosaves();
        string mode = ResolveFolderTemplateMode();
        string modeLabel = FolderTemplateModeLabel(mode);
        IReadOnlyList<string> groupNames = PlanSwiftFolderTemplateService.CollectCapsGroupNames(_currentJob);
        if (groupNames.Count == 0)
        {
            MessageBox.Show("No CAPS page/folder names were found in Pages.", "Takeoff Folders From Pages",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string baseName = string.Equals(baseFolder, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase)
            ? "Takeoffs"
            : OurPlaneCoreJobStore.DisplayName(baseFolder);
        string preview = PlanSwiftFolderTemplateService.PreviewNames(groupNames);
        var confirm = MessageBox.Show(
            $"Create {groupNames.Count} top takeoff folder(s) from Pages CAPS names under '{baseName}', then add the standard {modeLabel} tree?\n\n{preview}\n\nExisting folders will be skipped.",
            "Takeoff Folders From Pages",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            CapsTakeoffFolderResult result = PlanSwiftFolderTemplateService.CreateTakeoffFoldersFromCapsPages(_currentJob, baseFolder, mode);
            LoadTakeoffsForJob();
            SelectTakeoffNodeByFolder(baseFolder);
            TxtStatus.Text =
                $"Takeoff folders from Pages ({modeLabel}): top created {result.TopCreated}, top skipped {result.TopSkipped}, " +
                $"sub created {result.SubCreated}, sub skipped {result.SubSkipped}, errors {result.Errors}.";
            ShowFolderTemplateErrors("Takeoff Folders From Pages", result.ErrorMessages);
        }
        catch (Exception ex)
        {
            ShowOperationError("Takeoff Folders From Pages", ex);
        }
    }

    private void EnsureTakeoffItemFolder(TakeoffItem item)
    {
        if (_currentJob == null)
            return;

        if (string.IsNullOrWhiteSpace(item.FolderPath) || !Directory.Exists(item.FolderPath))
        {
            var stored = CreateUniqueTakeoffItem(
                item.Name,
                item.Color,
                item.MeasurementType,
                NewTakeoffItemParentFolder());
            item.FolderPath = stored.FolderPath;
        }

        foreach (var measurement in item.Measurements)
            measurement.TakeoffFolder = item.FolderPath;
        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
    }

    private TakeoffItem CreateUniqueTakeoffItem(string name, string color, string measurementType = "line", string? parentFolder = null)
    {
        if (_currentJob == null)
            throw new InvalidOperationException("No job is open.");

        string parent = string.IsNullOrWhiteSpace(parentFolder) ? _currentJob.TakeoffsRoot : parentFolder;
        string baseName = string.IsNullOrWhiteSpace(name) ? "Item" : name.Trim();
        return OurPlaneCoreJobStore.CreateTakeoffItem(_currentJob, parent, baseName, color, measurementType);
    }
}
