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
    private void CreateTakeoffFromTemplateSelection(TreeView? tree)
    {
        TakeoffTemplateNode? node = SelectedTemplateNode(tree);
        if (node == null || node.IsFolder)
        {
            TxtStatus.Text = "Select a template preset item first.";
            return;
        }

        CreateTakeoffFromTemplateNode(node);
    }

    private void CreateTakeoffFromTemplateNode(TakeoffTemplateNode node)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Template Takeoff",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (node.IsFolder)
        {
            TxtStatus.Text = "Select a template preset item, not a folder.";
            return;
        }

        string templateName = string.IsNullOrWhiteSpace(node.Name) ? "New Item" : node.Name;
        var dialog = new NewItemDialog(
            node.MeasurementType,
            templateName,
            lockType: false,
            defaultColor: node.Color,
            defaultCountSymbol: node.CountSymbol,
            initialNameCaretIndex: templateName.Length)
        {
            Owner = this,
            Title = "Create Takeoff From Template",
        };
        if (dialog.ShowDialog() != true)
            return;

        string measurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(dialog.ItemType);
        if (measurementType == "point")
            RememberNewCountSymbol(dialog.ItemCountSymbol);

        IReadOnlyList<string> templateFolderPath = TemplateFolderPathForNode(node);
        string parentFolder = TakeoffTemplateRouting.ResolveDestinationFolder(_currentJob, templateFolderPath);
        bool routedToExistingTemplateFolder = templateFolderPath.Count == 0 ||
            !string.Equals(parentFolder, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase);

        try
        {
            TakeoffItem item = CreateUniqueTakeoffItem(dialog.ItemName, dialog.ItemColor, measurementType, parentFolder);
            ApplyTakeoffFolderDefaultsToNewItem(item, parentFolder);
            ApplyTemplateNodeSettingsToItem(node, item, dialog, measurementType);

            _takeoffItems.Add(item);
            ItemsControl treeParent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
            TreeViewItem tvi = AddTakeoffTreeItem(item, treeParent);
            if (treeParent is TreeViewItem parentTvi)
                parentTvi.IsExpanded = true;

            ActivateTemplateCreatedTakeoff(item, tvi, measurementType);
            string targetName = routedToExistingTemplateFolder
                ? OurPlaneCoreJobStore.DisplayName(parentFolder)
                : "Takeoffs root";
            TxtStatus.Text = $"Template created: {item.Name}. Target: {targetName}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Template Takeoff", ex);
        }
    }

    private void ApplyTemplateNodeSettingsToItem(
        TakeoffTemplateNode node,
        TakeoffItem item,
        NewItemDialog dialog,
        string measurementType)
    {
        item.Name = dialog.ItemName;
        item.Color = dialog.ItemColor;
        item.MeasurementType = measurementType;
        item.CountSymbol = measurementType == "point"
            ? CountDisplaySymbol.Normalize(dialog.ItemCountSymbol)
            : CountDisplaySymbol.Circle;
        if (node.UnitPrice > 0)
            item.UnitPrice = node.UnitPrice;
        if (!string.IsNullOrWhiteSpace(node.Notes))
            item.Notes = node.Notes;

        OurPlaneCoreJobStore.SaveTakeoffItem(item);
    }

    private void ActivateTemplateCreatedTakeoff(TakeoffItem item, TreeViewItem tvi, string measurementType)
    {
        _activeItem = item;
        _activeTakeoffParentFolder = Path.GetDirectoryName(item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;
        _viewport.ActiveCountSymbol = item.CountSymbol;
        tvi.IsSelected = true;
        tvi.BringIntoView();
        UpdateToolStatus();
        RefreshActiveTakeoffVisuals();
        UpdateTotalDisplay();

        string tool = ToolForTemplateMeasurementType(measurementType);
        if (CanStartTemplateDrawingTool(measurementType))
            ApplyToolSelection(tool);
    }

    private bool CanStartTemplateDrawingTool(string measurementType)
    {
        if (_currentPage == null)
            return false;

        string normalized = OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType);
        return normalized == "point" || _currentPage.ScaleMetersPerPt > 0;
    }

    private static string ToolForTemplateMeasurementType(string measurementType) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "area" => "area",
            "point" => "point",
            _ => "line",
        };

    private IReadOnlyList<string> TemplateFolderPathForNode(TakeoffTemplateNode node)
    {
        return TryFindTemplateNodePath(TemplateRoots(), node, [], out List<string> path)
            ? path
            : [];
    }

    private static bool TryFindTemplateNodePath(
        IEnumerable<TakeoffTemplateNode> nodes,
        TakeoffTemplateNode target,
        List<string> currentPath,
        out List<string> path)
    {
        foreach (TakeoffTemplateNode node in nodes)
        {
            if (ReferenceEquals(node, target))
            {
                path = [.. currentPath];
                return true;
            }

            if (!node.IsFolder)
                continue;

            currentPath.Add(node.Name);
            if (TryFindTemplateNodePath(node.Children, target, currentPath, out path))
            {
                currentPath.RemoveAt(currentPath.Count - 1);
                return true;
            }
            currentPath.RemoveAt(currentPath.Count - 1);
        }

        path = [];
        return false;
    }
}
