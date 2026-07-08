using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void RefreshTakeoffSectionNodes(TreeViewItem itemNode, TakeoffItem item)
    {
        bool wasExpanded = itemNode.IsExpanded;
        Measurement? selectedMeasurement = (TakeoffsTree.SelectedItem as TreeViewItem)?.Tag is TakeoffMeasurementNode selectedNode
            ? selectedNode.Measurement
            : null;

        itemNode.Items.Clear();
        if (!_settings.ShowTakeoffSectionsInTree)
        {
            _takeoffSectionMultiSelection.Clear();
            _takeoffSectionRangeAnchorKey = null;
            itemNode.IsExpanded = false;
            return;
        }

        for (int i = 0; i < item.Measurements.Count; i++)
        {
            Measurement measurement = item.Measurements[i];
            var node = new TakeoffMeasurementNode(item, measurement);
            var sectionTvi = new TreeViewItem { Tag = node };
            SetTakeoffSectionHeader(sectionTvi, item, measurement, i);
            AttachTakeoffSectionContextMenu(sectionTvi, item, measurement);
            itemNode.Items.Add(sectionTvi);
            if (ReferenceEquals(selectedMeasurement, measurement))
                sectionTvi.IsSelected = true;
        }

        itemNode.IsExpanded = wasExpanded;
    }

    private void AttachTakeoffSectionContextMenu(TreeViewItem tvi, TakeoffItem item, Measurement measurement)
    {
        tvi.ContextMenu = new ContextMenu();
        tvi.ContextMenuOpening += TakeoffSection_ContextMenuOpening;
    }

    private void TakeoffSection_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not TreeViewItem { Tag: TakeoffMeasurementNode node } item)
            return;

        item.ContextMenu = BuildTakeoffSectionContextMenu(node);
    }

    private ContextMenu BuildTakeoffSectionContextMenu(TakeoffMeasurementNode anchor)
    {
        TakeoffItem item = anchor.Item;
        Measurement measurement = anchor.Measurement;
        string title = MeasurementEntryTitle(item);
        IReadOnlyList<TakeoffMeasurementNode> selectedNodes = SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true);
        int selectedCount = selectedNodes.Count;
        bool singleSelection = selectedCount <= 1;
        var menu = new ContextMenu();
        menu.Items.Add(MakeMenuItem($"{title} Properties...", singleSelection, () => EditSectionProperties(item, measurement)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Set Notes for {selectedCount} Rows..." : "Set Notes...",
            true,
            () => EditTakeoffSectionNotes(anchor)));
        menu.Items.Add(BuildTakeoffSectionCountDisplayMenu(anchor));
        menu.Items.Add(MakeMenuItem($"Rename {title}", singleSelection, () => RenameSection(item, measurement)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? "Go to First Page" : "Go to Page",
            SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true).Any(node => !string.IsNullOrWhiteSpace(node.Measurement.PageFolder)),
            () => GoToTakeoffSectionsPage(anchor)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Select {selectedCount} on Canvas" : "Select on Canvas",
            true,
            () => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true), anchor)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Merge {selectedCount} Segments..." : "Merge Segment...",
            true,
            () => MergeSelectedMeasurementsToPromptedTakeoff(sectionAnchor: anchor)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Split {selectedCount} Segments..." : "Split Segment...",
            true,
            () => SplitSelectedMeasurementsToNewTakeoff(sectionAnchor: anchor)));
        bool isAreaSection =
            OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "area" &&
            OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area";
        IReadOnlyList<Measurement> lineSectionMeasurements = selectedNodes
            .Select(node => node.Measurement)
            .Where(IsPointAlongLineSource)
            .ToList();
        menu.Items.Add(MakeMenuItem(
            lineSectionMeasurements.Count <= 1
                ? "Create Count Points Along Line..."
                : $"Create Count Points Along {lineSectionMeasurements.Count} Lines...",
            lineSectionMeasurements.Count > 0,
            () => CreatePointsAlongLines(lineSectionMeasurements, item)));
        menu.Items.Add(MakeMenuItem(
            "Create Line Grid...",
            isAreaSection,
            () => CreateLineGridFromAreaSection(item, measurement)));
        menu.Items.Add(MakeMenuItem(
            "Set / Reset Joist Direction",
            isAreaSection,
            () => SetJoistDirectionForSection(item, measurement)));
        menu.Items.Add(MakeMenuItem(
            "Set Direction for All Areas",
            isAreaSection,
            () => SetJoistDirectionForAllAreas(item, measurement)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up",
            CanMoveTakeoffSections(anchor, -1),
            () => MoveTakeoffSections(anchor, -1)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down",
            CanMoveTakeoffSections(anchor, 1),
            () => MoveTakeoffSections(anchor, 1)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Delete {selectedCount} {title}s" : $"Delete {title}",
            true,
            () => DeleteTakeoffSections(anchor)));
        return menu;
    }

    private void SetTakeoffSectionHeader(TreeViewItem tvi, TakeoffItem item, Measurement measurement, int index)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(CreateMeasurementTypeIcon(
            measurement,
            BrushFromHex(measurement.Color, Brushes.Gray),
            12,
            new Thickness(0, 0, 7, 0)));
        panel.Children.Add(new TextBlock
        {
            Text = SectionDisplayName(item, measurement, index),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"  {QuantityText(measurement)}",
            Foreground = Brushes.Gray,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });

        string page = SectionPageName(measurement);
        if (!string.IsNullOrWhiteSpace(page))
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"  {page}",
                Foreground = Brushes.Gray,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        tvi.Header = panel;
        var tooltip = new StringBuilder();
        tooltip.AppendLine($"{MeasurementEntryTitle(item)} {index + 1}");
        tooltip.AppendLine($"Page: {(string.IsNullOrWhiteSpace(page) ? "unknown" : page)}");
        tooltip.AppendLine($"Quantity: {QuantityText(measurement)}");
        if (!string.IsNullOrWhiteSpace(measurement.Notes))
            tooltip.AppendLine($"Notes: {measurement.Notes}");
        tvi.ToolTip = tooltip.ToString().Trim();
    }

    private void MoveSection(TakeoffItem item, Measurement measurement, int offset)
    {
        MoveTakeoffSections(new TakeoffMeasurementNode(item, measurement), offset);
    }

    private bool CanMoveTakeoffSections(TakeoffMeasurementNode anchor, int offset)
    {
        var selectedNodes = SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true);
        return CanMoveTakeoffSections(selectedNodes, anchor.Item, offset);
    }

    private bool CanMoveTakeoffSections(IReadOnlyList<TakeoffMeasurementNode> selectedNodes, TakeoffItem item, int offset)
    {
        if (selectedNodes.Count == 0)
            return false;

        if (selectedNodes.Any(node => !ReferenceEquals(node.Item, item)))
            return false;

        return TakeoffSectionOrderService.CanMove(
            item.Measurements,
            selectedNodes.Select(node => node.Measurement.Id),
            offset);
    }

    private void MoveTakeoffSections(TakeoffMeasurementNode anchor, int offset)
    {
        var selectedNodes = SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true);
        if (!CanMoveTakeoffSections(selectedNodes, anchor.Item, offset))
            return;

        if (!TakeoffSectionOrderService.Move(
                anchor.Item.Measurements,
                selectedNodes.Select(node => node.Measurement.Id),
                offset))
            return;

        QueueTakeoffAutosave(anchor.Item);
        RefreshTreeItem(anchor.Item);
        RefreshEstimateTable();
        RefreshSheetLegend();
        CancelPendingTakeoffSelectionSync();
        SelectTakeoffSectionNodesSilently(selectedNodes);
        TxtStatus.Text = selectedNodes.Count == 1
            ? (offset < 0 ? $"Moved {MeasurementEntryTitle(anchor.Item).ToLowerInvariant()} up." : $"Moved {MeasurementEntryTitle(anchor.Item).ToLowerInvariant()} down.")
            : (offset < 0 ? $"Moved {selectedNodes.Count} {MeasurementEntryTitlePlural(selectedNodes)} up." : $"Moved {selectedNodes.Count} {MeasurementEntryTitlePlural(selectedNodes)} down.");
    }

    private void StartNewSection(TreeViewItem tvi, TakeoffItem item)
    {
        if (_currentPage == null)
        {
            PostStatusInfo(
                item.MeasurementType == "point"
                    ? "Select a page before adding a count."
                    : "Select a page before starting a new section.");
            return;
        }

        tvi.IsSelected = true;
        _activeItem = item;
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;
        _viewport.ActiveCountSymbol = item.CountSymbol;
        SetTool(ToolForTakeoffItem(item));
        RefreshActiveTakeoffVisuals();
        if (IsRecordingTakeoffItem(item))
            TxtStatus.Text = item.MeasurementType == "point"
                ? $"Add counts for {item.Name}."
                : $"New {TakeoffTypeTitle(item)} section for {item.Name}.";
    }
}
