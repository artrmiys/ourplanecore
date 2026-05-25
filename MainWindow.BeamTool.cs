using System;
using System.Windows;
using System.Windows.Controls;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void OnBeamMeasurementCompleted(BeamMeasurementRequest request)
    {
        if (_currentJob == null || _currentPage == null)
        {
            TxtStatus.Text = "Beam ruler was placed, but no job/page is open for the Count item.";
            return;
        }

        if (!IsSamePageFolder(request.PageFolder, _currentPage.FolderPath))
        {
            TxtStatus.Text = "Beam ruler was placed, but the active sheet changed before the Count item was created.";
            return;
        }

        string parentFolder = NewTakeoffItemParentFolderForUserCreate();
        string defaultColor = RandomTakeoffColor(_activeItem?.Color ?? _viewport.ActiveColor);
        string defaultName = BeamTakeoffService.BuildDefaultCountName(
            ResolveTakeoffFolderDefaultNamePrefix(parentFolder),
            request.OrderLengthText,
            out int editablePrefixLength);

        var dialog = new NewItemDialog(
            "point",
            defaultName,
            lockType: true,
            defaultColor: defaultColor,
            defaultCountSymbol: _newCountSymbol,
            initialNameSelectionLength: editablePrefixLength)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            TxtStatus.Text = $"Beam ruler kept: {request.LengthFeet:0.##} ft. Count item cancelled.";
            return;
        }

        RememberNewCountSymbol(dialog.ItemCountSymbol);

        TakeoffItem item = CreateUniqueTakeoffItem(dialog.ItemName, dialog.ItemColor, "point", parentFolder);
        ApplyTakeoffFolderDefaultsToNewItem(item, parentFolder);
        ApplyNewCountSymbolToItemIfNeeded(item, "point");
        _takeoffItems.Add(item);

        var treeParent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
        TreeViewItem tvi = AddTakeoffTreeItem(item, treeParent);
        if (treeParent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;

        _activeItem = item;
        _activeTakeoffParentFolder = parentFolder;
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;
        _viewport.ActiveCountSymbol = item.CountSymbol;
        tvi.IsSelected = true;

        SetTool("point");
        _viewport.AddCountMeasurementAt(request.CountPointPdf);
        UpdateToolStatus();
        RefreshActiveTakeoffVisuals();
        UpdateTotalDisplay();
        TxtStatus.Text = $"Beam Count created: {item.Name}. Ruler {request.LengthFeet:0.##} ft, order size {request.OrderLengthText}.";
    }

    private void OnOpeningMeasurementCompleted(OpeningMeasurementRequest request)
    {
        if (_currentJob == null || _currentPage == null)
        {
            TxtStatus.Text = "Opening dimensions were placed, but no job/page is open for the Count item.";
            return;
        }

        if (!IsSamePageFolder(request.PageFolder, _currentPage.FolderPath))
        {
            TxtStatus.Text = "Opening dimensions were placed, but the active sheet changed before the Count item was created.";
            return;
        }

        string parentFolder = NewTakeoffItemParentFolderForUserCreate();
        string defaultColor = RandomTakeoffColor(_activeItem?.Color ?? _viewport.ActiveColor);
        string defaultName = OpeningTakeoffService.BuildDefaultCountName(request.SizeText);

        var dialog = new NewItemDialog(
            "point",
            defaultName,
            lockType: true,
            defaultColor: defaultColor,
            defaultCountSymbol: _newCountSymbol,
            initialNameCaretIndex: defaultName.Length)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            TxtStatus.Text = $"Opening dimensions kept: {request.SizeText}. Count item cancelled.";
            return;
        }

        RememberNewCountSymbol(dialog.ItemCountSymbol);

        TakeoffItem item = CreateUniqueTakeoffItem(dialog.ItemName, dialog.ItemColor, "point", parentFolder);
        ApplyTakeoffFolderDefaultsToNewItem(item, parentFolder);
        ApplyNewCountSymbolToItemIfNeeded(item, "point");
        _takeoffItems.Add(item);

        var treeParent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
        TreeViewItem tvi = AddTakeoffTreeItem(item, treeParent);
        if (treeParent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;

        _activeItem = item;
        _activeTakeoffParentFolder = parentFolder;
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;
        _viewport.ActiveCountSymbol = item.CountSymbol;
        tvi.IsSelected = true;

        SetTool("point");
        _viewport.AddCountMeasurementAt(request.CountPointPdf);
        UpdateToolStatus();
        RefreshActiveTakeoffVisuals();
        UpdateTotalDisplay();
        TxtStatus.Text = $"Opening Count created: {item.Name}. Dimensions {request.SizeText}.";
    }
}
