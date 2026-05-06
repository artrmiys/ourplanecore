using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    // ── Takeoffs tree ─────────────────────────────────────────────────────────

    private void TakeoffsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingTakeoffTreeSelection)
            return;

        CancelPendingTakeoffSelectionSync();

        if (e.NewValue is TreeViewItem selectedNode &&
            GetTakeoffNodePath(selectedNode) is { } selectedPath &&
            (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control &&
            !_takeoffsMultiSelection.Contains(selectedPath))
        {
            _takeoffsMultiSelection.Clear();
            _takeoffSectionMultiSelection.Clear();
            _takeoffsMultiSelection.Add(selectedPath);
            ApplyTakeoffPageHighlights();
        }

        if (e.NewValue is TreeViewItem { Tag: TakeoffItem item })
        {
            _takeoffSectionMultiSelection.Clear();
            item.MeasurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
            _activeItem           = item;
            _activeTakeoffParentFolder = Path.GetDirectoryName(item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
            _viewport.ActiveColor = item.Color;
            _viewport.ActiveTakeoffFolder = item.FolderPath;
            if (_activeTool is "point" or "line" or "area" && _activeTool != item.MeasurementType)
                ApplyToolSelection(item.MeasurementType);
            else
            UpdateToolStatus();
            RefreshPagesTakeoffIndicators();
            RefreshActiveTakeoffVisuals();
            RevealPagesForTakeoffSelection(e.NewValue as TreeViewItem);
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(e.NewValue as TreeViewItem));
            UpdateTotalDisplay();
        }
        else if (e.NewValue is TreeViewItem { Tag: TakeoffFolderNode folder })
        {
            _takeoffSectionMultiSelection.Clear();
            _activeItem = null;
            _activeTakeoffParentFolder = folder.FolderPath;
            _viewport.ActiveTakeoffFolder = "";
            TxtStatus.Text = TakeoffFolderStatusText(folder);
            UpdateToolStatus();
            RefreshPagesTakeoffIndicators();
            RefreshActiveTakeoffVisuals();
            RevealPagesForTakeoffSelection(e.NewValue as TreeViewItem);
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(e.NewValue as TreeViewItem));
            UpdateTotalDisplay();
        }
        else if (e.NewValue is TreeViewItem { Tag: TakeoffMeasurementNode node })
        {
            string sectionKey = TakeoffSectionSelectionKey(node);
            _takeoffsMultiSelection.Clear();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control &&
                !_takeoffSectionMultiSelection.Contains(sectionKey))
            {
                _takeoffSectionMultiSelection.Clear();
                _takeoffSectionMultiSelection.Add(sectionKey);
                _takeoffSectionRangeAnchorKey = sectionKey;
                ApplyTakeoffPageHighlights();
            }

            _activeItem = node.Item;
            _activeTakeoffParentFolder = Path.GetDirectoryName(node.Item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
            _viewport.ActiveColor = node.Item.Color;
            _viewport.ActiveTakeoffFolder = node.Item.FolderPath;
            if (_suppressCanvasFocusFromTakeoffSelection || Mouse.LeftButton == MouseButtonState.Pressed)
            {
                if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, node.Measurement.PageFolder))
                    _viewport.SelectMeasurements([node.Measurement]);
            }
            else
            {
                SelectSectionOnCanvas(node.Measurement, suppressTakeoffSync: true);
            }
            UpdateToolStatus();
            RefreshPagesTakeoffIndicators();
            RefreshActiveTakeoffVisuals();
            RevealPagesForTakeoffItems([node.Item], node.Measurement.PageFolder);
            UpdateTotalDisplay();
        }
    }

    private void CancelPendingTakeoffSelectionSync()
    {
        unchecked
        {
            _takeoffSelectionSyncVersion++;
        }
    }

    private void ScheduleTakeoffSelectionSync(Action action)
    {
        int version;
        unchecked
        {
            _takeoffSelectionSyncVersion++;
            version = _takeoffSelectionSyncVersion;
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (version != _takeoffSelectionSyncVersion)
                return;

            action();
        });
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "No job open - nothing to save.";
            return;
        }
        try
        {
            FlushTakeoffAutosaves();
            SaveCurrentPageScale();
            SaveCurrentPageAnnotations();
            foreach (var item in _takeoffItems)
            {
                EnsureTakeoffItemFolder(item);
                OurPlaneCoreJobStore.SaveTakeoffItem(item);
            }

            if (!string.IsNullOrEmpty(_currentPdfPath))
                ProjectFile.Save(_currentPdfPath, _viewport.ScaleMetersPerPt, _viewport.UnitMode, _takeoffItems);

            string? snapshotPath = SaveJobRecoverySnapshot("manual_save");
            string snapshotText = string.IsNullOrWhiteSpace(snapshotPath)
                ? ""
                : $" Snapshot: {Path.GetRelativePath(_currentJob.RootPath, snapshotPath)}";
            TxtStatus.Text = $"Saved takeoffs -> {_currentJob.TakeoffsRoot}.{snapshotText}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed:\n{ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnAddObservation_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before adding observations.";
            return;
        }

        string defaultText = _currentPage == null
            ? ""
            : $"Page {_currentPage.Name}: ";
        string? text = ShowInputDialog("Observation:", "Add Observation", defaultText);
        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            var observation = SmartContextStore.AddManualObservation(_currentJob, _currentPage, text);
            TxtStatus.Text = $"Saved observation {observation.Id} -> {_currentJob.AIContextRoot}";
            LoadObservationsInbox();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot save observation:\n{ex.Message}", "Add Observation",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Takeoff item context menu ─────────────────────────────────────────────

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

    private void RefreshTakeoffSectionNodes(TreeViewItem itemNode, TakeoffItem item)
    {
        bool wasExpanded = itemNode.IsExpanded;
        Measurement? selectedMeasurement = (TakeoffsTree.SelectedItem as TreeViewItem)?.Tag is TakeoffMeasurementNode selectedNode
            ? selectedNode.Measurement
            : null;

        itemNode.Items.Clear();
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
        tvi.ContextMenu = BuildTakeoffSectionContextMenu(new TakeoffMeasurementNode(item, measurement));
    }

    private ContextMenu BuildTakeoffSectionContextMenu(TakeoffMeasurementNode anchor)
    {
        TakeoffItem item = anchor.Item;
        Measurement measurement = anchor.Measurement;
        string title = MeasurementEntryTitle(item);
        int selectedCount = SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true).Count;
        bool singleSelection = selectedCount <= 1;
        var menu = new ContextMenu();
        menu.Items.Add(MakeMenuItem($"{title} Properties...", singleSelection, () => EditSectionProperties(item, measurement)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Set Notes for {selectedCount} Rows..." : "Set Notes...",
            true,
            () => EditTakeoffSectionNotes(anchor)));
        menu.Items.Add(MakeMenuItem($"Rename {title}", singleSelection, () => RenameSection(item, measurement)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? "Go to First Page" : "Go to Page",
            SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true).Any(node => !string.IsNullOrWhiteSpace(node.Measurement.PageFolder)),
            () => GoToTakeoffSectionsPage(anchor)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Select {selectedCount} on Canvas" : "Select on Canvas",
            true,
            () => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true))));
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
            measurement.JoistEnabled ? "joist" : OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType),
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

        OurPlaneCoreJobStore.SaveTakeoffItem(anchor.Item);
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
            MessageBox.Show(
                item.MeasurementType == "point"
                    ? "Select a page before adding a count."
                    : "Select a page before starting a new section.",
                item.MeasurementType == "point" ? "Add Count" : "New Section",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        tvi.IsSelected = true;
        _activeItem = item;
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;
        SetTool(item.MeasurementType);
        RefreshActiveTakeoffVisuals();
        if (_activeTool == item.MeasurementType)
            TxtStatus.Text = item.MeasurementType == "point"
                ? $"Add counts for {item.Name}."
                : $"New {MeasurementTypeTitle(item.MeasurementType)} section for {item.Name}.";
    }

    private void SetActiveTakeoffTarget(TreeViewItem? tvi, TakeoffItem item, bool selectCanvasMeasurements = true)
    {
        CancelPendingTakeoffSelectionSync();
        item.MeasurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        _activeItem = item;
        _activeTakeoffParentFolder = Path.GetDirectoryName(item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;

        if (tvi != null)
        {
            _takeoffsMultiSelection.Clear();
            if (!string.IsNullOrWhiteSpace(item.FolderPath))
                _takeoffsMultiSelection.Add(item.FolderPath);
            tvi.IsSelected = true;
            tvi.BringIntoView();
        }

        if (_activeTool is "point" or "line" or "area" && _activeTool != item.MeasurementType)
            ApplyToolSelection(item.MeasurementType);
        else
            UpdateToolStatus();

        RefreshPagesTakeoffIndicators();
        RefreshActiveTakeoffVisuals();
        RevealPagesForTakeoffItems([item], _currentPage?.FolderPath);
        if (selectCanvasMeasurements)
            ScheduleTakeoffSelectionSync(() => SelectCurrentPageTakeoffMeasurementsOnCanvas(item));
        UpdateTotalDisplay();
        TxtStatus.Text = $"Active takeoff target: {item.Name}.";
    }

    private void BtnActiveTakeoffRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTool is "point" or "line" or "area")
        {
            string recordType = MeasurementTypeTitle(_activeTool);
            SetTool("select");
            TxtStatus.Text = $"Record stopped: {recordType}.";
            return;
        }

        if (_activeItem == null)
        {
            TxtStatus.Text = "Select a takeoff item before recording.";
            return;
        }

        if (FindTakeoffTreeItem(_activeItem) is { } tvi)
        {
            StartNewSection(tvi, _activeItem);
            return;
        }

        if (_currentPage == null)
        {
            string measurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType);
            MessageBox.Show(
                measurementType == "point"
                    ? "Select a page before adding a count."
                    : "Select a page before starting a new section.",
                measurementType == "point" ? "Add Count" : "New Section",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SetActiveTakeoffTarget(null, _activeItem, selectCanvasMeasurements: false);
        SetTool(OurPlaneCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType));
    }

    private void BtnActiveTakeoffMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement target)
            return;

        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
        };

        menu.Items.Add(MakeMenuItem("Properties...", BtnActiveTakeoffProperties.IsEnabled, () => BtnActiveTakeoffProperties_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Find in Tree", BtnActiveTakeoffFind.IsEnabled, () => BtnActiveTakeoffFind_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Sheet Targets...", BtnActiveTakeoffSheetNext.IsEnabled, () => ShowActiveSheetTakeoffTargetMenu(target)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Previous Target", BtnActiveTakeoffPrevious.IsEnabled, () => BtnActiveTakeoffPrevious_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Next Target", BtnActiveTakeoffNext.IsEnabled, () => BtnActiveTakeoffNext_Click(sender, new RoutedEventArgs())));

        menu.IsOpen = true;
    }

    private void BtnActiveTakeoffFind_Click(object sender, RoutedEventArgs e)
    {
        if (_activeItem == null)
        {
            TxtStatus.Text = "Select a takeoff item first.";
            return;
        }

        if (FindTakeoffTreeItem(_activeItem) is not { } tvi)
        {
            TxtStatus.Text = "Active takeoff item is not visible in the Takeoffs tree.";
            return;
        }

        SelectTakeoffItem(_activeItem);
        SetActiveTakeoffTarget(tvi, _activeItem, selectCanvasMeasurements: false);
    }

    private void BtnActiveTakeoffProperties_Click(object sender, RoutedEventArgs e)
    {
        if (_activeItem == null)
        {
            TxtStatus.Text = "Select a takeoff item first.";
            return;
        }

        if (FindTakeoffTreeItem(_activeItem) is not { } tvi)
        {
            TxtStatus.Text = "Active takeoff item is not visible in the Takeoffs tree.";
            return;
        }

        SetActiveTakeoffTarget(tvi, _activeItem, selectCanvasMeasurements: false);
        EditTakeoffItemProperties(tvi, _activeItem);
    }

    private void BtnActiveTakeoffPrevious_Click(object sender, RoutedEventArgs e) =>
        MoveActiveTakeoffTarget(-1);

    private void BtnActiveTakeoffNext_Click(object sender, RoutedEventArgs e) =>
        MoveActiveTakeoffTarget(1);

    private void BtnActiveTakeoffSheetNext_Click(object sender, RoutedEventArgs e) =>
        ShowActiveSheetTakeoffTargetMenu(BtnActiveTakeoffSheetNext);

    private void MoveActiveTakeoffTarget(int offset)
    {
        var targets = ActiveTakeoffTargetCycleItems();
        if (targets.Count == 0)
        {
            TxtStatus.Text = "No takeoff items are available.";
            return;
        }

        int currentIndex = _activeItem == null
            ? -1
            : targets.FindIndex(IsActiveTakeoffItem);
        int nextIndex = currentIndex < 0
            ? (offset < 0 ? targets.Count - 1 : 0)
            : (currentIndex + offset + targets.Count) % targets.Count;
        TakeoffItem next = targets[nextIndex];
        SetActiveTakeoffTarget(FindTakeoffTreeItem(next), next);
        TxtStatus.Text = $"Active takeoff target {nextIndex + 1}/{targets.Count}: {next.Name}.";
    }

    private List<TakeoffItem> ActiveTakeoffTargetCycleItems() =>
        _takeoffItems
            .Where(item => !string.IsNullOrWhiteSpace(item.FolderPath))
            .ToList();

    private void MoveActiveSheetTakeoffTarget(int offset)
    {
        var targets = ActiveSheetTakeoffTargetCycleItems();
        if (targets.Count == 0)
        {
            TxtStatus.Text = _currentPage == null
                ? "Select a sheet before cycling sheet takeoffs."
                : $"No takeoff items are measured on {_currentPage.Name}.";
            return;
        }

        int currentIndex = _activeItem == null
            ? -1
            : targets.FindIndex(IsActiveTakeoffItem);
        int nextIndex = currentIndex < 0
            ? (offset < 0 ? targets.Count - 1 : 0)
            : (currentIndex + offset + targets.Count) % targets.Count;
        TakeoffItem next = targets[nextIndex];
        SetActiveTakeoffTarget(FindTakeoffTreeItem(next), next);
        TxtStatus.Text = $"Sheet takeoff target {nextIndex + 1}/{targets.Count}: {next.Name}.";
    }

    private List<TakeoffItem> ActiveSheetTakeoffTargetCycleItems() =>
        _currentPage == null
            ? []
            : OrderedTakeoffsForPage(_currentPage).ToList();

    private void ShowActiveSheetTakeoffTargetMenu(UIElement? placementTarget = null)
    {
        var targets = ActiveSheetTakeoffTargetCycleItems();
        if (targets.Count == 0)
        {
            TxtStatus.Text = _currentPage == null
                ? "Select a sheet before choosing sheet takeoffs."
                : $"No takeoff items are measured on {_currentPage.Name}.";
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(MakeMenuItem("Next Sheet Target", targets.Count > 1, () => MoveActiveSheetTakeoffTarget(1)));
        menu.Items.Add(new Separator());

        for (int i = 0; i < targets.Count; i++)
        {
            TakeoffItem target = targets[i];
            int index = i;
            string activePrefix = IsActiveTakeoffItem(target) ? "* " : "";
            string quantity = ActiveSheetTakeoffTargetQuantity(target);
            menu.Items.Add(MakeMenuItem(
                $"{activePrefix}{index + 1}. {target.Name} - {quantity}",
                true,
                () => SelectActiveSheetTakeoffTarget(target, index, targets.Count)));
        }

        menu.PlacementTarget = placementTarget ?? BtnActiveTakeoffSheetNext;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void SelectActiveSheetTakeoffTarget(TakeoffItem target, int index, int count)
    {
        SetActiveTakeoffTarget(FindTakeoffTreeItem(target), target);
        TxtStatus.Text = $"Sheet takeoff target {index + 1}/{count}: {target.Name}.";
    }

    private string ActiveSheetTakeoffTargetQuantity(TakeoffItem item)
    {
        if (_currentPage == null)
            return "";

        var measurements = MeasurementsForTakeoffOnPage(item, _currentPage.FolderPath).ToList();
        return measurements.Count == 0
            ? "none on sheet"
            : SheetLegendQuantityText(item, measurements);
    }

    private void SetUnitPrice(TakeoffItem item)
    {
        string? raw = ShowInputDialog(
            $"Unit price per {TakeoffUnitText(item)}:",
            item.UnitPrice > 0 ? item.UnitPrice.ToString("G", CultureInfo.InvariantCulture) : "0",
            "Set Unit Price");
        if (raw == null)
            return;

        if (!double.TryParse(raw.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double price) ||
            price < 0)
        {
            MessageBox.Show("Enter a valid non-negative unit price.", "Set Unit Price",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        item.UnitPrice = price;
        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        RefreshTreeItem(item);
        RefreshEstimateTable();
        RefreshSheetLegend();
        TxtStatus.Text = $"Unit price set for {item.Name}: {price:G}";
    }

    private void EditTakeoffItemProperties(TreeViewItem tvi, TakeoffItem item)
    {
        if (!ShowTakeoffItemPropertiesDialog(
                item,
                out string name,
                out string color,
                out double unitPrice,
                out string notes,
                out JoistTakeoffEdit joistEdit))
        {
            return;
        }

        try
        {
            bool colorChanged = !string.Equals(item.Color, color, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath) &&
                !string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                string oldPath = item.FolderPath;
                item.FolderPath = OurPlaneCoreJobStore.RenameNode(item.FolderPath, name);
                RebasePageLegendTakeoffOrderReferences(oldPath, item.FolderPath);
                item.Name = OurPlaneCoreJobStore.DisplayName(item.FolderPath);
                foreach (var measurement in item.Measurements)
                    measurement.TakeoffFolder = item.FolderPath;
            }
            else
            {
                item.Name = OurPlaneCoreJobStore.SanitizeName(name, 120);
            }

            item.Color = color;
            item.UnitPrice = unitPrice;
            item.Notes = notes.Trim();
            bool joistChanged =
                item.IsJoistTakeoff != joistEdit.Enabled ||
                !string.Equals(item.JoistType, joistEdit.JoistType, StringComparison.Ordinal) ||
                Math.Abs(item.JoistSpacingInches - joistEdit.SpacingInches) > 0.0001 ||
                Math.Abs(item.JoistDirectionDegrees - joistEdit.DirectionDegrees) > 0.0001 ||
                !string.Equals(
                    JoistTakeoffCalculator.NormalizePitch(item.JoistPitch),
                    JoistTakeoffCalculator.NormalizePitch(joistEdit.Pitch),
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding),
                    JoistTakeoffCalculator.NormalizeLengthRounding(joistEdit.LengthRounding),
                    StringComparison.OrdinalIgnoreCase) ||
                item.JoistShowLabels != joistEdit.ShowLabels ||
                item.JoistDetailedLabels != joistEdit.DetailedLabels;
            bool wasJoistArea = item.IsJoistArea;
            item.IsJoistTakeoff = joistEdit.Enabled && OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "area";
            item.JoistType = joistEdit.JoistType.Trim();
            item.JoistSpacingInches = joistEdit.SpacingInches > 0 ? joistEdit.SpacingInches : 16;
            item.JoistDirectionDegrees = joistEdit.DirectionDegrees;
            item.JoistPitch = JoistTakeoffCalculator.NormalizePitch(joistEdit.Pitch);
            item.JoistLengthRounding = JoistTakeoffCalculator.NormalizeLengthRounding(joistEdit.LengthRounding);
            item.JoistShowLabels = joistEdit.ShowLabels;
            item.JoistDetailedLabels = joistEdit.DetailedLabels;
            OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
            if (colorChanged)
            {
                foreach (Measurement measurement in item.Measurements)
                    measurement.Color = color;
            }
            if (colorChanged || joistChanged)
                _viewport.SetMeasurements(_takeoffItems.SelectMany(takeoff => takeoff.Measurements));

            OurPlaneCoreJobStore.SaveTakeoffItem(item);
            SetTreeItemHeader(tvi, item);
            RefreshTakeoffSectionNodes(tvi, item);
            RefreshEstimateTable();
            RefreshPagesTakeoffIndicators();
            ApplyTakeoffPageHighlights();
            RefreshSheetLegend();
            UpdateTotalDisplay();
            if (item.IsJoistArea && (!wasJoistArea || item.HasPendingJoistDirections))
                BeginNextPendingJoistDirectionCapture(item);
            TxtStatus.Text = $"Updated takeoff item properties: {item.Name}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Takeoff Item Properties", ex);
        }
    }

    private void SetJoistDirectionFromSelectedLine(TreeViewItem tvi, TakeoffItem item)
    {
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) != "area")
        {
            TxtStatus.Text = "Joist direction can only be set on Area takeoff items.";
            return;
        }

        Measurement? area = SelectedJoistAreaMeasurement(item);
        if (area == null)
        {
            string message = "Select one Area measurement on the sheet first, then run this joist direction command.";
            TxtStatus.Text = message;
            return;
        }

        item.IsJoistTakeoff = true;
        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        _viewport.SetMeasurements(_takeoffItems.SelectMany(takeoff => takeoff.Measurements));
        BeginJoistDirectionCapture(item, area);
    }

    private Measurement? SelectedJoistAreaMeasurement(TakeoffItem item)
    {
        var selected = _viewport.GetSelectedMeasurements()
            .Where(measurement =>
                item.Measurements.Contains(measurement) &&
                OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area")
            .ToList();
        if (selected.Count == 1)
            return selected[0];

        if (_currentPage != null)
        {
            var pageAreas = item.Measurements
                .Where(measurement =>
                    OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
                    IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath))
                .ToList();
            if (pageAreas.Count == 1)
                return pageAreas[0];
        }

        return null;
    }

    private void BeginJoistDirectionCapture(TakeoffItem item, Measurement area)
    {
        item.IsJoistTakeoff = true;
        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        area.JoistDirectionLocked = false;
        _viewport.BeginJoistDirectionCapture(area);
        TxtStatus.Text = $"Joist direction for {item.Name}: draw a two-point line parallel to the joists on the selected area.";
    }

    private void OnJoistDirectionCaptured(Measurement area, SKPoint start, SKPoint end)
    {
        TakeoffItem? item = FindTakeoffItemForMeasurement(area);
        if (item == null)
            return;

        if (!TryDirectionFromPoints(start, end, out double directionDegrees))
        {
            TxtStatus.Text = "Joist direction line is too short.";
            return;
        }

        item.IsJoistTakeoff = true;
        area.JoistDirectionDegrees = directionDegrees;
        area.JoistDirectionLocked = true;
        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        _viewport.SelectMeasurements([area]);
        RefreshTreeItem(item);
        RefreshActiveTakeoffVisuals();
        RefreshEstimateTable();
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        UpdateTotalDisplay();
        if (BeginNextPendingJoistDirectionCapture(item, area))
            return;

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(area, _viewport.ScaleMetersPerPt);
        TxtStatus.Text = $"Joists generated for {item.Name}: direction {directionDegrees:0.#} deg, {JoistTakeoffCalculator.FormatDiagnostics(layout, _viewport.UnitMode)}{FormatJoistScaleSuffix(area)}.";
    }

    private bool BeginNextPendingJoistDirectionCapture(TakeoffItem item, Measurement? skip = null)
    {
        if (_currentPage == null || !item.IsJoistArea)
            return false;

        Measurement? next = item.Measurements.FirstOrDefault(measurement =>
            !ReferenceEquals(measurement, skip) &&
            OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
            IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath) &&
            !measurement.JoistDirectionLocked);
        if (next == null)
            return false;

        _viewport.BeginJoistDirectionCapture(next);
        TxtStatus.Text = $"Set joist direction for next area in {item.Name}: click two points parallel to the joists.";
        return true;
    }

    private bool TryGetSelectedLineDirection(out double directionDegrees, out string message)
    {
        directionDegrees = 0;
        Measurement? line = _viewport.GetSelectedMeasurements()
            .FirstOrDefault(measurement =>
                OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "line" &&
                measurement.Points.Count >= 2);
        if (line == null)
        {
            message = "Select a Line measurement on the sheet first, then run this joist direction command.";
            return false;
        }

        SKPoint start = line.Points[0];
        SKPoint end = line.Points[^1];
        if (!TryDirectionFromPoints(start, end, out directionDegrees))
        {
            message = "Selected line is too short to define joist direction.";
            return false;
        }

        message = "";
        return true;
    }

    private static bool TryDirectionFromPoints(SKPoint start, SKPoint end, out double directionDegrees)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        if (Math.Sqrt(dx * dx + dy * dy) < 0.001)
        {
            directionDegrees = 0;
            return false;
        }

        directionDegrees = NormalizeJoistDirectionDegrees(Math.Atan2(dy, dx) * 180.0 / Math.PI);
        return true;
    }

    private static double NormalizeJoistDirectionDegrees(double degrees)
    {
        double normalized = degrees % 180.0;
        if (normalized < 0)
            normalized += 180.0;
        return Math.Abs(normalized - 180.0) < 0.0001 ? 0 : normalized;
    }

    private void EditSelectedTakeoffProperties(TreeViewItem anchor)
    {
        var selectedItems = TakeoffItemsForSelection(anchor)
            .Where(item => !string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath))
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (selectedItems.Count == 0)
        {
            TxtStatus.Text = "No takeoff items selected for bulk properties.";
            return;
        }

        if (!ShowBulkTakeoffPropertiesDialog(selectedItems, out BulkTakeoffPropertiesEdit edit))
            return;

        try
        {
            foreach (TakeoffItem selectedItem in selectedItems)
            {
                if (edit.ApplyColor)
                {
                    selectedItem.Color = edit.Color;
                    foreach (Measurement measurement in selectedItem.Measurements)
                        measurement.Color = edit.Color;
                }

                if (edit.ApplyUnitPrice)
                    selectedItem.UnitPrice = edit.UnitPrice;

                if (edit.ApplyNotes)
                    selectedItem.Notes = edit.Notes.Trim();

                OurPlaneCoreJobStore.SaveTakeoffItem(selectedItem);
                RefreshTreeItem(selectedItem);
            }

            if (edit.ApplyColor && _activeItem != null &&
                selectedItems.Any(item => string.Equals(item.FolderPath, _activeItem.FolderPath, StringComparison.OrdinalIgnoreCase)))
            {
                _viewport.ActiveColor = _activeItem.Color;
            }

            RefreshEstimateTable();
            RefreshPagesTakeoffIndicators();
            RefreshActiveTakeoffVisuals();
            RefreshSheetLegend();
            UpdateTotalDisplay();
            SelectTakeoffSelectionMeasurementsOnCurrentPage(anchor);
            TxtStatus.Text = $"Updated bulk properties for {selectedItems.Count} takeoff item(s).";
        }
        catch (Exception ex)
        {
            ShowOperationError("Bulk Takeoff Properties", ex);
        }
    }

    private bool ShowBulkTakeoffPropertiesDialog(
        IReadOnlyList<TakeoffItem> items,
        out BulkTakeoffPropertiesEdit edit)
    {
        string firstColor = NormalizeTakeoffColor(items[0].Color);
        bool sameColor = items.All(item =>
            string.Equals(NormalizeTakeoffColor(item.Color), firstColor, StringComparison.OrdinalIgnoreCase));
        double firstPrice = items[0].UnitPrice;
        bool samePrice = items.All(item => Math.Abs(item.UnitPrice - firstPrice) < 0.0000001);
        string firstNotes = items[0].Notes;
        bool sameNotes = items.All(item => string.Equals(item.Notes, firstNotes, StringComparison.Ordinal));
        var selectedTypes = items
            .Select(item => OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        bool sameType = selectedTypes.Count == 1;
        string typeText = sameType ? MeasurementTypeTitle(selectedTypes[0]) : "mixed Line/Area/Count";

        edit = new BulkTakeoffPropertiesEdit(false, firstColor, false, firstPrice, false, firstNotes);

        var dialog = new Window
        {
            Title = $"Bulk Takeoff Properties ({items.Count})",
            Owner = this,
            Width = 430,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Selected items: {items.Count} | Type: {typeText}",
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var applyColorBox = new CheckBox
        {
            Content = sameColor ? "Apply color" : "Apply color (currently mixed)",
            Margin = new Thickness(0, 0, 0, 4),
        };
        panel.Children.Add(applyColorBox);

        string selectedColor = firstColor;
        var colorBox = new TextBox
        {
            Text = selectedColor,
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = false,
        };
        var swatches = new List<Border>();
        var colorPanel = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 4),
            IsEnabled = false,
        };
        foreach (var preset in TakeoffColorPresets)
        {
            var swatch = new Border
            {
                Width = 32,
                Height = 22,
                Margin = new Thickness(2),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset.Hex)),
                BorderBrush = string.Equals(preset.Hex, selectedColor, StringComparison.OrdinalIgnoreCase)
                    ? Brushes.White
                    : Brushes.Transparent,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                ToolTip = preset.Label,
                Cursor = Cursors.Hand,
            };
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                selectedColor = preset.Hex;
                colorBox.Text = selectedColor;
                applyColorBox.IsChecked = true;
                foreach (Border border in swatches)
                    border.BorderBrush = Brushes.Transparent;
                swatch.BorderBrush = Brushes.White;
            };
            swatches.Add(swatch);
            colorPanel.Children.Add(swatch);
        }
        panel.Children.Add(colorPanel);
        colorBox.TextChanged += (_, _) => applyColorBox.IsChecked = true;
        panel.Children.Add(colorBox);

        var applyPriceBox = new CheckBox
        {
            Content = sameType
                ? $"Apply unit price per {UnitText(selectedTypes[0])}"
                : "Unit price disabled for mixed Line/Area/Count selection",
            IsEnabled = sameType,
            Margin = new Thickness(0, 12, 0, 4),
        };
        panel.Children.Add(applyPriceBox);
        var priceBox = new TextBox
        {
            Text = samePrice && firstPrice > 0 ? firstPrice.ToString("G", CultureInfo.InvariantCulture) : "0",
            IsEnabled = false,
        };
        if (sameType)
            priceBox.TextChanged += (_, _) => applyPriceBox.IsChecked = true;
        panel.Children.Add(priceBox);

        var applyNotesBox = new CheckBox
        {
            Content = sameNotes ? "Replace notes" : "Replace notes (currently mixed)",
            Margin = new Thickness(0, 12, 0, 4),
        };
        panel.Children.Add(applyNotesBox);
        var notesBox = new TextBox
        {
            Text = sameNotes ? firstNotes : "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 90,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsEnabled = false,
        };
        notesBox.TextChanged += (_, _) => applyNotesBox.IsChecked = true;
        panel.Children.Add(notesBox);

        void RefreshEnabledFields()
        {
            bool applyColor = applyColorBox.IsChecked == true;
            colorPanel.IsEnabled = applyColor;
            colorBox.IsEnabled = applyColor;
            priceBox.IsEnabled = sameType && applyPriceBox.IsChecked == true;
            notesBox.IsEnabled = applyNotesBox.IsChecked == true;
        }

        applyColorBox.Checked += (_, _) => RefreshEnabledFields();
        applyColorBox.Unchecked += (_, _) => RefreshEnabledFields();
        applyPriceBox.Checked += (_, _) => RefreshEnabledFields();
        applyPriceBox.Unchecked += (_, _) => RefreshEnabledFields();
        applyNotesBox.Checked += (_, _) => RefreshEnabledFields();
        applyNotesBox.Unchecked += (_, _) => RefreshEnabledFields();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "OK", Width = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 78, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        BulkTakeoffPropertiesEdit result = edit;
        ok.Click += (_, _) =>
        {
            bool applyColor = applyColorBox.IsChecked == true;
            bool applyPrice = sameType && applyPriceBox.IsChecked == true;
            bool applyNotes = applyNotesBox.IsChecked == true;
            if (!applyColor && !applyPrice && !applyNotes)
            {
                MessageBox.Show("Choose at least one property to apply.", "Bulk Takeoff Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string cleanColor = NormalizeTakeoffColor(colorBox.Text);
            if (applyColor && !IsValidWpfColor(cleanColor))
            {
                MessageBox.Show("Enter a valid color like #FF4444.", "Bulk Takeoff Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            double parsedPrice = firstPrice;
            if (applyPrice &&
                (!double.TryParse(priceBox.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out parsedPrice) ||
                 parsedPrice < 0))
            {
                MessageBox.Show("Enter a valid non-negative unit price.", "Bulk Takeoff Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            result = new BulkTakeoffPropertiesEdit(
                applyColor,
                cleanColor,
                applyPrice,
                parsedPrice,
                applyNotes,
                notesBox.Text.Trim());
            dialog.DialogResult = true;
        };

        bool accepted = dialog.ShowDialog() == true;
        if (accepted)
            edit = result;

        return accepted;
    }

    private bool ShowTakeoffItemPropertiesDialog(
        TakeoffItem item,
        out string name,
        out string color,
        out double unitPrice,
        out string notes,
        out JoistTakeoffEdit joistEdit)
    {
        name = item.Name;
        color = NormalizeTakeoffColor(item.Color);
        unitPrice = item.UnitPrice;
        notes = item.Notes;
        joistEdit = new JoistTakeoffEdit(
            item.IsJoistArea,
            item.JoistType,
            item.JoistSpacingInches > 0 ? item.JoistSpacingInches : 16,
            item.JoistDirectionDegrees,
            JoistTakeoffCalculator.NormalizePitch(item.JoistPitch),
            JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding),
            item.JoistShowLabels,
            item.JoistDetailedLabels);

        var dialog = new Window
        {
            Title = "Takeoff Item Properties",
            Owner = this,
            Width = 500,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Name:", Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new TextBox { Text = item.Name };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock
        {
            Text = $"Type: {MeasurementTypeTitle(item.MeasurementType)}",
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 0),
        });
        bool isAreaTakeoff = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "area";
        var joistEnabledBox = new CheckBox
        {
            Content = "Joist layout",
            IsChecked = item.IsJoistArea,
            IsEnabled = isAreaTakeoff,
            Margin = new Thickness(0, 10, 0, 2),
        };
        panel.Children.Add(joistEnabledBox);

        var joistPanel = new Grid
        {
            Margin = new Thickness(18, 0, 0, 6),
            IsEnabled = isAreaTakeoff && joistEnabledBox.IsChecked == true,
        };
        joistPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        joistPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 7; i++)
            joistPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabeledTextBox(joistPanel, 0, "Joist type:", out TextBox joistTypeBox, item.JoistType);
        AddLabeledTextBox(
            joistPanel,
            1,
            "O.C. spacing (in):",
            out TextBox joistSpacingBox,
            (item.JoistSpacingInches > 0 ? item.JoistSpacingInches : 16).ToString("G", CultureInfo.InvariantCulture));
        AddLabeledTextBox(
            joistPanel,
            2,
            "Pitch (rise:run):",
            out TextBox joistPitchBox,
            JoistTakeoffCalculator.NormalizePitch(item.JoistPitch));
        joistPitchBox.ToolTip = "Roof pitch as rise:run, e.g. 3:12. Blank or 0:12 is flat.";
        var directionLabel = new TextBlock
        {
            Text = "Joist direction:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 8, 3),
        };
        Grid.SetRow(directionLabel, 3);
        Grid.SetColumn(directionLabel, 0);
        joistPanel.Children.Add(directionLabel);
        var joistDirectionBox = new TextBox
        {
            Text = item.JoistDirectionDegrees.ToString("G", CultureInfo.InvariantCulture),
            IsReadOnly = true,
            Width = 78,
            ToolTip = "Direction is set by drawing a two-point line parallel to the joists after selecting or drawing an Area.",
        };
        Grid.SetRow(joistDirectionBox, 3);
        Grid.SetColumn(joistDirectionBox, 1);
        joistPanel.Children.Add(joistDirectionBox);

        var roundingLabel = new TextBlock
        {
            Text = "Length calc:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 8, 3),
        };
        Grid.SetRow(roundingLabel, 4);
        Grid.SetColumn(roundingLabel, 0);
        joistPanel.Children.Add(roundingLabel);
        var roundingBox = new ComboBox
        {
            MinWidth = 190,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 3, 0, 3),
        };
        foreach (string rounding in new[]
                 {
                     JoistTakeoffCalculator.RoundingNone,
                     JoistTakeoffCalculator.RoundingNearestFoot,
                     JoistTakeoffCalculator.RoundingNearestEvenFoot,
                     JoistTakeoffCalculator.RoundingNearestTwoFeet,
                 })
        {
            roundingBox.Items.Add(new ComboBoxItem
            {
                Content = JoistTakeoffCalculator.LengthRoundingTitle(rounding),
                Tag = rounding,
            });
        }
        string selectedRounding = JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding);
        for (int i = 0; i < roundingBox.Items.Count; i++)
        {
            if (roundingBox.Items[i] is ComboBoxItem option &&
                string.Equals((string?)option.Tag, selectedRounding, StringComparison.OrdinalIgnoreCase))
            {
                roundingBox.SelectedIndex = i;
                break;
            }
        }
        if (roundingBox.SelectedIndex < 0)
            roundingBox.SelectedIndex = 0;
        Grid.SetRow(roundingBox, 4);
        Grid.SetColumn(roundingBox, 1);
        joistPanel.Children.Add(roundingBox);

        var joistLabelsBox = new CheckBox
        {
            Content = "Label each joist",
            IsChecked = item.JoistShowLabels,
            Margin = new Thickness(0, 3, 0, 3),
            ToolTip = "When off, the area label still shows count and order length.",
        };
        Grid.SetRow(joistLabelsBox, 5);
        Grid.SetColumn(joistLabelsBox, 1);
        joistPanel.Children.Add(joistLabelsBox);

        var joistDetailedLabelsBox = new CheckBox
        {
            Content = "Detailed area label",
            IsChecked = item.JoistDetailedLabels,
            Margin = new Thickness(0, 3, 0, 3),
            ToolTip = "On: show order/raw/flat lengths. Off: use the old compact count / length format.",
        };
        Grid.SetRow(joistDetailedLabelsBox, 6);
        Grid.SetColumn(joistDetailedLabelsBox, 1);
        joistPanel.Children.Add(joistDetailedLabelsBox);

        joistEnabledBox.Checked += (_, _) => joistPanel.IsEnabled = isAreaTakeoff;
        joistEnabledBox.Unchecked += (_, _) => joistPanel.IsEnabled = false;
        if (!isAreaTakeoff)
            joistEnabledBox.ToolTip = "Joist layout is available for Area takeoff items.";
        panel.Children.Add(joistPanel);

        panel.Children.Add(new TextBlock { Text = "Color:", Margin = new Thickness(0, 10, 0, 4) });
        string selectedColor = NormalizeTakeoffColor(item.Color);
        var colorBox = new TextBox { Text = selectedColor, Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
        var swatches = new List<Border>();
        var colorPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var preset in TakeoffColorPresets)
        {
            var swatch = new Border
            {
                Width = 32,
                Height = 22,
                Margin = new Thickness(2),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset.Hex)),
                BorderBrush = string.Equals(preset.Hex, selectedColor, StringComparison.OrdinalIgnoreCase)
                    ? Brushes.White
                    : Brushes.Transparent,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                ToolTip = preset.Label,
                Cursor = Cursors.Hand,
            };
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                selectedColor = preset.Hex;
                colorBox.Text = selectedColor;
                foreach (Border border in swatches)
                    border.BorderBrush = Brushes.Transparent;
                swatch.BorderBrush = Brushes.White;
            };
            swatches.Add(swatch);
            colorPanel.Children.Add(swatch);
        }
        panel.Children.Add(colorPanel);
        panel.Children.Add(colorBox);

        var unitPriceLabel = new TextBlock
        {
            Text = $"Unit price per {TakeoffUnitText(item)}:",
            Margin = new Thickness(0, 10, 0, 4),
        };
        panel.Children.Add(unitPriceLabel);
        joistEnabledBox.Checked += (_, _) => unitPriceLabel.Text = $"Unit price per {UnitText("line")}:";
        joistEnabledBox.Unchecked += (_, _) => unitPriceLabel.Text = $"Unit price per {UnitText(item.MeasurementType)}:";
        var priceBox = new TextBox
        {
            Text = item.UnitPrice > 0 ? item.UnitPrice.ToString("G", CultureInfo.InvariantCulture) : "0",
        };
        panel.Children.Add(priceBox);

        panel.Children.Add(new TextBlock { Text = "Notes:", Margin = new Thickness(0, 10, 0, 4) });
        var notesBox = new TextBox
        {
            Text = item.Notes,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 90,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        panel.Children.Add(notesBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "OK", Width = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 78, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        string resultName = item.Name;
        string resultColor = selectedColor;
        double resultPrice = item.UnitPrice;
        string resultNotes = item.Notes;
        JoistTakeoffEdit resultJoist = joistEdit;

        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                MessageBox.Show("Name is required.", "Takeoff Item Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string cleanColor = NormalizeTakeoffColor(colorBox.Text);
            if (!IsValidWpfColor(cleanColor))
            {
                MessageBox.Show("Enter a valid color like #FF4444.", "Takeoff Item Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!double.TryParse(priceBox.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedPrice) ||
                parsedPrice < 0)
            {
                MessageBox.Show("Enter a valid non-negative unit price.", "Takeoff Item Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool joistEnabled = isAreaTakeoff && joistEnabledBox.IsChecked == true;
            double joistSpacing = item.JoistSpacingInches > 0 ? item.JoistSpacingInches : 16;
            double joistDirection = item.JoistDirectionDegrees;
            string joistPitch = JoistTakeoffCalculator.NormalizePitch(item.JoistPitch);
            string joistRounding = JoistTakeoffCalculator.RoundingNone;
            if (roundingBox.SelectedItem is ComboBoxItem selectedRoundingItem &&
                selectedRoundingItem.Tag is string selectedRoundingValue)
            {
                joistRounding = JoistTakeoffCalculator.NormalizeLengthRounding(selectedRoundingValue);
            }

            if (joistEnabled)
            {
                if (!double.TryParse(joistSpacingBox.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out joistSpacing) ||
                    joistSpacing <= 0)
                {
                    MessageBox.Show("Enter a valid positive joist spacing.", "Takeoff Item Properties",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (!double.TryParse(joistDirectionBox.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out joistDirection))
                {
                    MessageBox.Show("Enter a valid joist direction angle.", "Takeoff Item Properties",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (!JoistTakeoffCalculator.TryNormalizePitch(joistPitchBox.Text, out joistPitch))
                {
                    MessageBox.Show("Enter roof pitch as rise:run, e.g. 3:12. Leave blank for flat.",
                                    "Takeoff Item Properties",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            resultName = nameBox.Text.Trim();
            resultColor = cleanColor;
            resultPrice = parsedPrice;
            resultNotes = notesBox.Text.Trim();
            resultJoist = new JoistTakeoffEdit(
                joistEnabled,
                joistTypeBox.Text.Trim(),
                joistSpacing,
                joistDirection,
                joistPitch,
                joistRounding,
                joistLabelsBox.IsChecked == true,
                joistDetailedLabelsBox.IsChecked == true);
            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };

        bool accepted = dialog.ShowDialog() == true;
        if (accepted)
        {
            name = resultName;
            color = resultColor;
            unitPrice = resultPrice;
            notes = resultNotes;
            joistEdit = resultJoist;
        }

        return accepted;
    }

    private static void AddLabeledTextBox(Grid grid, int row, string label, out TextBox textBox, string value)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 8, 3),
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        textBox = new TextBox
        {
            Text = value,
            MinWidth = 190,
            Margin = new Thickness(0, 3, 0, 3),
        };
        Grid.SetRow(textBox, row);
        Grid.SetColumn(textBox, 1);
        grid.Children.Add(textBox);
    }

    private static string NormalizeTakeoffColor(string value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? "#FF4444" : value.Trim();
        if (!trimmed.StartsWith('#'))
            trimmed = "#" + trimmed;
        return trimmed;
    }

    private static bool IsValidWpfColor(string value)
    {
        try
        {
            _ = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Brush BrushFromHex(string value, Brush fallback)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(NormalizeTakeoffColor(value)));
        }
        catch
        {
            return fallback;
        }
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

    private void RenameItem(TreeViewItem tvi, TakeoffItem item)
    {
        string? name = ShowInputDialog("New name:", item.Name, "Rename Item");
        if (name == null || name == item.Name) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath))
            {
                string oldPath = item.FolderPath;
                item.FolderPath = OurPlaneCoreJobStore.RenameNode(item.FolderPath, name);
                RebasePageLegendTakeoffOrderReferences(oldPath, item.FolderPath);
                item.Name = OurPlaneCoreJobStore.DisplayName(item.FolderPath);
                foreach (var measurement in item.Measurements)
                    measurement.TakeoffFolder = item.FolderPath;
                OurPlaneCoreJobStore.SaveTakeoffItem(item);
            }
            else
            {
                item.Name = OurPlaneCoreJobStore.SanitizeName(name, 120);
            }

            SetTreeItemHeader(tvi, item);
            RefreshPagesTakeoffIndicators();
            RefreshSheetLegend();
            UpdateTotalDisplay();
        }
        catch (Exception ex)
        {
            ShowOperationError("Rename Takeoff Item", ex);
        }
    }

    private void DeleteItem(TreeViewItem tvi, TakeoffItem item)
    {
        var res = MessageBox.Show(
            $"Delete \"{item.Name}\" and all its measurements?",
            "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        try
        {
            if (!string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath))
                DeleteDirectoryToRecycle(item.FolderPath);
        }
        catch (Exception ex)
        {
            ShowOperationError("Delete Takeoff Item", ex);
            return;
        }

        _viewport.DeleteMeasurements(item.Measurements);
        _takeoffItems.Remove(item);
        RemoveTreeItem(tvi);

        if (ReferenceEquals(_activeItem, item))
        {
            _activeItem = null;
            SelectFirstTakeoffItem();
        }
        UpdateTotalDisplay();
    }

    private void EditTakeoffFolderProperties(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        if (_currentJob == null || !Directory.Exists(folder.FolderPath))
            return;

        TakeoffFolderProperties properties = TakeoffFolderPropertiesStore.Load(folder.FolderPath);
        var dialog = new TakeoffFolderPropertiesDialog(folder.Name, properties)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            string oldPath = folder.FolderPath;
            string newPath = folder.FolderPath;
            string requestedName = dialog.FolderName.Trim();
            if (!string.IsNullOrWhiteSpace(requestedName) &&
                !string.Equals(requestedName, folder.Name, StringComparison.Ordinal))
            {
                newPath = OurPlaneCoreJobStore.RenameNode(folder.FolderPath, requestedName);
                RebasePageLegendTakeoffOrderReferences(oldPath, newPath);
                foreach (var item in _takeoffItems)
                {
                    if (!OurPlaneCoreJobStore.IsSameOrDescendant(oldPath, item.FolderPath))
                        continue;

                    item.FolderPath = Path.Combine(newPath, Path.GetRelativePath(oldPath, item.FolderPath));
                    foreach (var measurement in item.Measurements)
                        measurement.TakeoffFolder = item.FolderPath;
                }
            }

            var updatedFolder = new TakeoffFolderNode
            {
                Name = OurPlaneCoreJobStore.DisplayName(newPath),
                FolderPath = newPath,
            };
            var updatedProperties = new TakeoffFolderProperties
            {
                DisplayName = updatedFolder.Name,
                Notes = dialog.Notes,
                DefaultColor = dialog.DefaultColor,
                DefaultMeasurementType = dialog.DefaultMeasurementType,
                DefaultUnitPrice = dialog.DefaultUnitPrice,
                DefaultItemNotes = dialog.DefaultItemNotes,
                DefaultNamePrefix = dialog.DefaultNamePrefix,
            };
            TakeoffFolderPropertiesStore.Save(newPath, updatedProperties);

            tvi.Tag = updatedFolder;
            SetFolderTreeItemHeader(tvi, updatedFolder);
            AttachFolderContextMenu(tvi, updatedFolder);
            _activeTakeoffParentFolder = newPath;
            TxtStatus.Text = TakeoffFolderStatusText(updatedFolder);
        }
        catch (Exception ex)
        {
            ShowOperationError("Takeoff Folder Properties", ex);
        }
    }

    private void RenameTakeoffFolder(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        string? name = ShowInputDialog("New name:", "Rename Folder", folder.Name);
        if (name == null || name == folder.Name) return;

        try
        {
            string oldPath = folder.FolderPath;
            string newPath = OurPlaneCoreJobStore.RenameNode(folder.FolderPath, name);
            RebasePageLegendTakeoffOrderReferences(oldPath, newPath);
            folder = new TakeoffFolderNode
            {
                Name = OurPlaneCoreJobStore.DisplayName(newPath),
                FolderPath = newPath,
            };
            tvi.Tag = folder;
            SetFolderTreeItemHeader(tvi, folder);
            AttachFolderContextMenu(tvi, folder);

            foreach (var item in _takeoffItems)
            {
                if (!OurPlaneCoreJobStore.IsSameOrDescendant(oldPath, item.FolderPath))
                    continue;

                item.FolderPath = Path.Combine(newPath, Path.GetRelativePath(oldPath, item.FolderPath));
                foreach (var measurement in item.Measurements)
                    measurement.TakeoffFolder = item.FolderPath;
            }

            _activeTakeoffParentFolder = newPath;
        }
        catch (Exception ex)
        {
            ShowOperationError("Rename Takeoff Folder", ex);
        }
    }

    private void DeleteTakeoffFolder(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        var res = MessageBox.Show(
            $"Delete folder \"{folder.Name}\" and all child takeoffs?",
            "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        var removedItems = _takeoffItems
            .Where(i => OurPlaneCoreJobStore.IsSameOrDescendant(folder.FolderPath, i.FolderPath))
            .ToList();

        try
        {
            DeleteDirectoryToRecycle(folder.FolderPath);
        }
        catch (Exception ex)
        {
            ShowOperationError("Delete Takeoff Folder", ex);
            return;
        }

        foreach (var item in removedItems)
        {
            _viewport.DeleteMeasurements(item.Measurements);
            _takeoffItems.Remove(item);
        }
        RemoveTreeItem(tvi);

        if (_activeItem != null && removedItems.Contains(_activeItem))
            SelectFirstTakeoffItem();
        else
            UpdateTotalDisplay();
    }

    private void DeleteTakeoffNodes(TreeViewItem anchor)
    {
        if (_currentJob == null)
            return;

        var entries = GetSelectedTakeoffEntries(anchor);
        if (entries.Count == 0)
            return;

        string message = entries.Count == 1
            ? $"Delete \"{OurPlaneCoreJobStore.DisplayName(entries[0].SourcePath)}\" and all contained measurements?"
            : $"Delete {entries.Count} selected takeoff node(s) and all contained measurements?";
        var res = MessageBox.Show(message, "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes)
            return;

        try
        {
            FlushTakeoffAutosaves();
            var removedItems = _takeoffItems
                .Where(item => entries.Any(entry =>
                    OurPlaneCoreJobStore.IsSameOrDescendant(entry.SourcePath, item.FolderPath)))
                .ToList();

            foreach (var entry in entries)
            {
                if (Directory.Exists(entry.SourcePath))
                    DeleteDirectoryToRecycle(entry.SourcePath);
            }

            foreach (var item in removedItems)
                _takeoffItems.Remove(item);

            if (_activeItem != null && removedItems.Contains(_activeItem))
                _activeItem = null;

            if (_takeoffsClipboard != null && entries.Any(entry =>
                    _takeoffsClipboard.Entries.Any(clip =>
                        OurPlaneCoreJobStore.IsSameOrDescendant(entry.SourcePath, clip.SourcePath))))
                _takeoffsClipboard = null;

            _takeoffsMultiSelection.Clear();
            LoadTakeoffsForJob();
            _viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
            RefreshPagesTakeoffIndicators();
            RefreshEstimateTable();
            UpdateTotalDisplay();
            TxtStatus.Text = entries.Count == 1
                ? $"Deleted: {OurPlaneCoreJobStore.DisplayName(entries[0].SourcePath)}"
                : $"Deleted {entries.Count} takeoff nodes.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Delete Takeoffs", ex);
        }
    }

    private void MoveTakeoffNode(string folderPath, int offset)
    {
        if (FindTakeoffTreeItemByFolder(folderPath) is { } item)
        {
            MoveTakeoffNodes(item, offset);
            return;
        }

        try
        {
            if (!OurPlaneCoreJobStore.MoveSibling(folderPath, offset))
                return;
            LoadTakeoffsForJob();
        }
        catch (Exception ex)
        {
            ShowOperationError("Move Takeoff Node", ex);
        }
    }

    private bool CanMoveTakeoffNodes(TreeViewItem anchor, int offset)
    {
        var paths = GetSelectedTakeoffEntries(anchor)
            .Select(entry => entry.SourcePath)
            .ToList();
        return OurPlaneCoreJobStore.CanMoveSiblings(paths, offset);
    }

    private void MoveTakeoffNodes(TreeViewItem anchor, int offset)
    {
        var entries = GetSelectedTakeoffEntries(anchor);
        var paths = entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Count == 0)
            return;

        try
        {
            if (!OurPlaneCoreJobStore.MoveSiblings(paths, offset))
                return;

            LoadTakeoffsForJob();
            SetTakeoffMultiSelection(paths);
            SelectFirstTakeoffPath(paths);
            TxtStatus.Text = paths.Count == 1
                ? (offset < 0 ? "Moved takeoff node up." : "Moved takeoff node down.")
                : (offset < 0 ? $"Moved {paths.Count} takeoff nodes up." : $"Moved {paths.Count} takeoff nodes down.");
        }
        catch (Exception ex)
        {
            ShowOperationError(offset < 0 ? "Move Takeoff Nodes Up" : "Move Takeoff Nodes Down", ex);
        }
    }

    private void SortTakeoffChildren(string folderPath, bool descending)
    {
        try
        {
            OurPlaneCoreJobStore.SortChildren(folderPath, descending);
            LoadTakeoffsForJob();
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort Takeoffs", ex);
        }
    }

    private void TakeoffsTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            if (item.Tag is TakeoffMeasurementNode sectionNode)
            {
                string key = TakeoffSectionSelectionKey(sectionNode);
                _takeoffsMultiSelection.Clear();
                if (!_takeoffSectionMultiSelection.Contains(key))
                {
                    _takeoffSectionMultiSelection.Clear();
                    _takeoffSectionMultiSelection.Add(key);
                    _takeoffSectionRangeAnchorKey = key;
                    _takeoffsMultiSelection.Clear();
                    ApplyTakeoffPageHighlights();
                }

                item.Focus();
                item.IsSelected = true;
                item.ContextMenu = BuildTakeoffSectionContextMenu(sectionNode);
                e.Handled = true;
                return;
            }

            string? path = GetTakeoffNodePath(item);
            if (path != null)
                _takeoffSectionMultiSelection.Clear();
            if (path != null && !_takeoffsMultiSelection.Contains(path))
            {
                _takeoffsMultiSelection.Clear();
                _takeoffSectionMultiSelection.Clear();
                _takeoffsMultiSelection.Add(path);
                _takeoffsRangeAnchorPath = path;
                ApplyTakeoffPageHighlights();
            }
            if (path != null)
                RevealPagesForTakeoffSelection(item);

            item.Focus();
            item.IsSelected = true;
            RefreshTakeoffNodeContextMenu(item);
            e.Handled = true;
        }
    }

    private void TakeoffsTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            RefreshTakeoffNodeContextMenu(item);
            return;
        }

        TakeoffsTree.ContextMenu = BuildTakeoffsRootContextMenu();
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

    private void TakeoffsTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _takeoffsDragStart = e.GetPosition(TakeoffsTree);
        _takeoffsDragItem = null;
        _takeoffsDragArmed = false;
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { } item)
            return;

        _takeoffsDragItem = item;
        _takeoffsDragArmed = CanArmTakeoffsTreeDrag(item, e.OriginalSource as DependencyObject);

        if (item.Tag is TakeoffMeasurementNode sectionNode)
        {
            HandleTakeoffSectionNodeMultiSelect(item, sectionNode, e);
            return;
        }

        string? path = GetTakeoffNodePath(item);
        if (path == null)
            return;

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None &&
            _takeoffsMultiSelection.Count > 1 &&
            _takeoffsMultiSelection.Contains(path))
        {
            _takeoffSectionMultiSelection.Clear();
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            RevealPagesForTakeoffSelection(item);
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(item));
            return;
        }

        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            bool additive = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            SelectTakeoffsRange(_takeoffsRangeAnchorPath, path, additive);
            _takeoffsRangeAnchorPath = path;
            _takeoffSectionMultiSelection.Clear();
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            RevealPagesForTakeoffSelection(item);
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(item));
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (!_takeoffsMultiSelection.Add(path))
                _takeoffsMultiSelection.Remove(path);
            _takeoffsRangeAnchorPath = path;
            _takeoffSectionMultiSelection.Clear();
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            RevealPagesForTakeoffSelection(item);
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(item));
            e.Handled = true;
            return;
        }

        if (!_takeoffsMultiSelection.SetEquals([path]))
        {
            _takeoffsMultiSelection.Clear();
            _takeoffsMultiSelection.Add(path);
            _takeoffSectionMultiSelection.Clear();
            ApplyTakeoffPageHighlights();
        }
        _takeoffsRangeAnchorPath = path;
        item.IsSelected = true;
        RevealPagesForTakeoffSelection(item);
        ScheduleTakeoffSelectionSync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(item));
    }

    private bool CanArmTakeoffsTreeDrag(TreeViewItem item, DependencyObject? source)
    {
        if (FindAncestor<ToggleButton>(source) != null)
            return false;
        if (ReferenceEquals(TakeoffsTree.SelectedItem, item))
            return true;
        if (item.Tag is TakeoffMeasurementNode sectionNode)
            return _takeoffSectionMultiSelection.Contains(TakeoffSectionSelectionKey(sectionNode));

        string? path = GetTakeoffNodePath(item);
        return path != null && _takeoffsMultiSelection.Contains(path);
    }

    private void HandleTakeoffSectionNodeMultiSelect(TreeViewItem item, TakeoffMeasurementNode node, MouseButtonEventArgs e)
    {
        string key = TakeoffSectionSelectionKey(node);
        ModifierKeys modifiers = Keyboard.Modifiers;
        _takeoffsMultiSelection.Clear();

        if (modifiers == ModifierKeys.None &&
            _takeoffSectionMultiSelection.Count > 1 &&
            _takeoffSectionMultiSelection.Contains(key))
        {
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(node, fallbackToAnchor: false)));
            return;
        }

        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            bool additive = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            SelectTakeoffSectionRange(_takeoffSectionRangeAnchorKey, key, node.Item, additive);
            _takeoffSectionRangeAnchorKey = key;
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(node, fallbackToAnchor: false)));
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (!_takeoffSectionMultiSelection.Add(key))
                _takeoffSectionMultiSelection.Remove(key);
            _takeoffSectionRangeAnchorKey = key;
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(node, fallbackToAnchor: false)));
            e.Handled = true;
            return;
        }

        _takeoffSectionMultiSelection.Clear();
        _takeoffSectionMultiSelection.Add(key);
        _takeoffSectionRangeAnchorKey = key;
        ApplyTakeoffPageHighlights();
        ScheduleTakeoffSelectionSync(() => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(node, fallbackToAnchor: true)));
    }

    private void SelectTakeoffsRange(string? anchorPath, string targetPath, bool additive)
    {
        var candidates = EnumerateVisibleTreeItems(TakeoffsTree)
            .Select(item => (Item: item, Key: GetTakeoffNodePath(item)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => (entry.Item, Key: entry.Key!))
            .ToList();

        SelectRangeKeys(candidates, anchorPath, targetPath, _takeoffsMultiSelection, additive);
    }

    private void TakeoffsTree_MouseMove(object sender, MouseEventArgs e)
    {
        if (_takeoffsDragStart == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        if (!_takeoffsDragArmed)
        {
            _takeoffsDragStart = null;
            _takeoffsDragItem = null;
            return;
        }

        Point pos = e.GetPosition(TakeoffsTree);
        if (Math.Abs(pos.X - _takeoffsDragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _takeoffsDragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if ((_takeoffsDragItem ?? TakeoffsTree.SelectedItem) is not TreeViewItem item)
            return;

        if (item.Tag is TakeoffMeasurementNode sectionNode)
        {
            var nodes = SelectedTakeoffSectionNodes(sectionNode, fallbackToAnchor: true);
            if (nodes.Count == 0)
            {
                _takeoffsDragStart = null;
                _takeoffsDragItem = null;
                return;
            }

            var sectionPayload = new TakeoffSectionDrag(nodes);
            DragDrop.DoDragDrop(TakeoffsTree, sectionPayload, DragDropEffects.Move | DragDropEffects.Copy);
            ClearTakeoffSectionDropCue();
            ClearTakeoffPositionDropCue();
            _takeoffsDragStart = null;
            _takeoffsDragItem = null;
            _takeoffsDragArmed = false;
            return;
        }

        var entries = GetSelectedTakeoffEntries(item);
        if (entries.Count == 0)
        {
            _takeoffsDragStart = null;
            _takeoffsDragItem = null;
            _takeoffsDragArmed = false;
            return;
        }

        var payload = new TakeoffsClipboard(entries, TakeoffsClipboardMode.Cut);
        DragDrop.DoDragDrop(TakeoffsTree, payload, DragDropEffects.Move | DragDropEffects.Copy);
        ClearTakeoffPositionDropCue();
        _takeoffsDragStart = null;
        _takeoffsDragItem = null;
        _takeoffsDragArmed = false;
    }

    private void TakeoffsTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        TreeViewItem? targetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        if (e.Data.GetData(typeof(TakeoffSectionDrag)) is TakeoffSectionDrag sectionDrag)
        {
            ClearTakeoffPositionDropCue();
            bool canDropSection = CanDropTakeoffSections(sectionDrag, targetItem, copy);
            UpdateTakeoffSectionDropCue(sectionDrag, targetItem, copy, canDropSection);
            if (canDropSection)
                e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        ClearTakeoffSectionDropCue();
        if (e.Data.GetData(typeof(TakeoffsClipboard)) is not TakeoffsClipboard payload)
            return;

        if (TryGetTakeoffPositionDropCue(payload, targetItem, copy, e, out bool after, out bool canDropPosition, out string positionStatus))
        {
            UpdateTakeoffPositionDropCue(targetItem, after, canDropPosition, positionStatus);
            if (canDropPosition)
                e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        ClearTakeoffPositionDropCue();
        string? targetFolder = targetItem == null ? _currentJob?.TakeoffsRoot : GetTakeoffPasteTargetFolder(targetItem);
        if (CanDropTakeoffsInto(payload, targetFolder, copy ? TakeoffsClipboardMode.Copy : TakeoffsClipboardMode.Cut))
            e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
    }

    private void TakeoffsTree_Drop(object sender, DragEventArgs e)
    {
        TreeViewItem? targetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        if (e.Data.GetData(typeof(TakeoffSectionDrag)) is TakeoffSectionDrag sectionDrag)
        {
            ClearTakeoffPositionDropCue();
            if (CanDropTakeoffSections(sectionDrag, targetItem, copy))
                DropTakeoffSections(sectionDrag, targetItem!, copy);
            ClearTakeoffSectionDropCue();
            e.Handled = true;
            return;
        }

        ClearTakeoffSectionDropCue();
        if (e.Data.GetData(typeof(TakeoffsClipboard)) is not TakeoffsClipboard payload)
            return;

        if (TryGetTakeoffPositionDropCue(payload, targetItem, copy, e, out bool after, out bool canDropPosition, out _) &&
            canDropPosition &&
            targetItem != null)
        {
            DropTakeoffPosition(payload, targetItem, after);
            ClearTakeoffPositionDropCue();
            e.Handled = true;
            return;
        }

        ClearTakeoffPositionDropCue();
        string? targetFolder = targetItem == null ? _currentJob?.TakeoffsRoot : GetTakeoffPasteTargetFolder(targetItem);
        TakeoffsClipboardMode mode = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey
            ? TakeoffsClipboardMode.Copy
            : TakeoffsClipboardMode.Cut;
        if (CanDropTakeoffsInto(payload, targetFolder, mode))
            RunTakeoffDrop(payload, targetFolder!, mode);
        e.Handled = true;
    }

    private void TakeoffsTree_DragLeave(object sender, DragEventArgs e)
    {
        if (!TakeoffsTree.IsMouseOver)
        {
            ClearTakeoffSectionDropCue();
            ClearTakeoffPositionDropCue();
        }
    }

    private void TakeoffsTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (TakeoffsTree.SelectedItem is not TreeViewItem item) return;
        if (item.Tag is TakeoffMeasurementNode sectionNode)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
            {
                MoveTakeoffSections(sectionNode, -1);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
            {
                MoveTakeoffSections(sectionNode, 1);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
            {
                DeleteTakeoffSections(sectionNode);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.F2 &&
                     SelectedTakeoffSectionNodes(sectionNode, fallbackToAnchor: true).Count <= 1)
            {
                RenameSection(sectionNode.Item, sectionNode.Measurement);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
            {
                SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(sectionNode, fallbackToAnchor: true));
                e.Handled = true;
            }
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.C)
        {
            CopyCutTakeoffNode(item, TakeoffsClipboardMode.Copy);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.X)
        {
            CopyCutTakeoffNode(item, TakeoffsClipboardMode.Cut);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.V)
        {
            PasteIntoSelectedTakeoffTarget(item);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.D)
        {
            DuplicateTakeoffNode(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
        {
            MoveTakeoffNodes(item, -1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
        {
            MoveTakeoffNodes(item, 1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
        {
            DeleteTakeoffNodes(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.F2 && TakeoffSelectionCount(item) <= 1)
        {
            if (item.Tag is TakeoffItem takeoff)
                RenameItem(item, takeoff);
            else if (item.Tag is TakeoffFolderNode folder && !folder.IsRoot)
                RenameTakeoffFolder(item, folder);
            e.Handled = true;
        }
    }

    private void CopyCutTakeoffNode(TreeViewItem item, TakeoffsClipboardMode mode)
    {
        var entries = GetSelectedTakeoffEntries(item);
        if (entries.Count == 0) return;

        _takeoffsClipboard = new TakeoffsClipboard(entries, mode);
        string verb = mode == TakeoffsClipboardMode.Copy ? "Copied" : "Cut";
        TxtStatus.Text = entries.Count == 1
            ? $"{verb}: {OurPlaneCoreJobStore.DisplayName(entries[0].SourcePath)}"
            : $"{verb} {entries.Count} takeoff nodes.";
    }

    private void PasteIntoSelectedTakeoffTarget(TreeViewItem item)
    {
        string? targetFolder = GetTakeoffPasteTargetFolder(item);
        if (targetFolder != null)
            PasteTakeoffsIntoFolder(targetFolder);
    }

    private void PasteTakeoffsIntoFolder(string targetFolder)
    {
        if (_takeoffsClipboard == null || !CanDropTakeoffsInto(_takeoffsClipboard, targetFolder, _takeoffsClipboard.Mode))
            return;

        RunTakeoffDrop(_takeoffsClipboard, targetFolder, _takeoffsClipboard.Mode);
    }

    private void DuplicateTakeoffNode(TreeViewItem item)
    {
        var entries = GetSelectedTakeoffEntries(item);
        if (entries.Count == 0) return;

        try
        {
            FlushTakeoffAutosaves();
            var changed = new List<string>();
            foreach (var entry in entries)
            {
                string? parent = Path.GetDirectoryName(entry.SourcePath);
                if (string.IsNullOrWhiteSpace(parent) ||
                    !CanDropTakeoffsInto(new TakeoffsClipboard([entry], TakeoffsClipboardMode.Copy), parent, TakeoffsClipboardMode.Copy))
                {
                    continue;
                }

                changed.Add(OurPlaneCoreJobStore.CopyNode(entry.SourcePath, parent));
            }

            if (changed.Count == 0)
                return;

            LoadTakeoffsForJob();
            SetTakeoffMultiSelection(changed);
            SelectFirstTakeoffPath(changed);
            TxtStatus.Text = changed.Count == 1
                ? $"Duplicated: {OurPlaneCoreJobStore.DisplayName(changed[0])}"
                : $"Duplicated {changed.Count} takeoff nodes.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Duplicate Takeoffs", ex);
        }
    }

    private void RunTakeoffDrop(TakeoffsClipboard payload, string targetFolder, TakeoffsClipboardMode mode)
    {
        bool wasCut = mode == TakeoffsClipboardMode.Cut;
        try
        {
            FlushTakeoffAutosaves();
            var changed = new List<string>();
            var rebasedLegendPaths = new List<(string OldPath, string NewPath)>();
            foreach (var entry in payload.Entries)
            {
                if (!CanDropTakeoffsInto(new TakeoffsClipboard([entry], mode), targetFolder, mode))
                    continue;

                string changedPath = wasCut
                    ? OurPlaneCoreJobStore.MoveNode(entry.SourcePath, targetFolder)
                    : OurPlaneCoreJobStore.CopyNode(entry.SourcePath, targetFolder);
                changed.Add(changedPath);
                if (wasCut)
                    rebasedLegendPaths.Add((entry.SourcePath, changedPath));
            }

            if (changed.Count == 0)
                return;

            if (wasCut)
                _takeoffsClipboard = null;

            foreach (var (oldPath, newPath) in rebasedLegendPaths)
                RebasePageLegendTakeoffOrderReferences(oldPath, newPath);

            LoadTakeoffsForJob();
            SetTakeoffMultiSelection(changed);
            SelectFirstTakeoffPath(changed);
            TxtStatus.Text = changed.Count == 1
                ? $"{(wasCut ? "Moved" : "Pasted")}: {OurPlaneCoreJobStore.DisplayName(changed[0])}"
                : $"{(wasCut ? "Moved" : "Pasted")} {changed.Count} takeoff nodes.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Takeoff Paste", ex);
        }
    }

    private bool TryGetTakeoffPositionDropCue(
        TakeoffsClipboard payload,
        TreeViewItem? targetItem,
        bool copy,
        DragEventArgs e,
        out bool after,
        out bool canDrop,
        out string status)
    {
        after = false;
        canDrop = false;
        status = "";

        if (copy || _currentJob == null || payload.Entries.Count == 0 || targetItem == null)
            return false;

        string? targetPath = GetTakeoffNodePath(targetItem);
        if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
            return false;

        Point targetPoint = e.GetPosition(targetItem);
        if (targetItem.Tag is TakeoffFolderNode && !IsTakeoffPositionEdgeDrop(targetItem, targetPoint))
            return false;

        after = IsTakeoffPositionDropAfter(targetItem, targetPoint);
        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        canDrop = CanDropTakeoffsToPosition(payload, targetPath, after);
        string targetName = OurPlaneCoreJobStore.DisplayName(targetPath);
        string position = after ? "after" : "before";
        status = canDrop
            ? $"Move {paths.Count} takeoff node(s) {position} {targetName}."
            : $"Cannot reorder here. Drag onto another sibling position in the same folder.";
        return true;
    }

    private bool CanDropTakeoffsToPosition(TakeoffsClipboard payload, string targetPath, bool after)
    {
        if (_currentJob == null || payload.Entries.Count == 0 || string.IsNullOrWhiteSpace(targetPath))
            return false;

        string targetParent = Path.GetDirectoryName(targetPath) ?? "";
        if (string.IsNullOrWhiteSpace(targetParent) ||
            !OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, targetParent) ||
            !Directory.Exists(targetParent))
        {
            return false;
        }

        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Any(path => string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (paths.All(path => string.Equals(Path.GetDirectoryName(path) ?? "", targetParent, StringComparison.OrdinalIgnoreCase)))
            return OurPlaneCoreJobStore.CanMoveSiblingsToPosition(paths, targetPath, after);

        return CanDropTakeoffsInto(payload, targetParent, TakeoffsClipboardMode.Cut);
    }

    private static bool IsTakeoffPositionDropAfter(TreeViewItem item, Point targetPoint) =>
        targetPoint.Y >= TakeoffNodeHeaderDropHeight(item) / 2.0;

    private static bool IsTakeoffPositionEdgeDrop(TreeViewItem item, Point targetPoint)
    {
        double height = TakeoffNodeHeaderDropHeight(item);
        if (targetPoint.Y < 0 || targetPoint.Y > height)
            return false;

        double edge = Math.Min(8.0, Math.Max(5.0, height * 0.25));
        return targetPoint.Y <= edge || targetPoint.Y >= height - edge;
    }

    private static double TakeoffNodeHeaderDropHeight(TreeViewItem item)
    {
        double itemHeight = Math.Max(1.0, item.ActualHeight);
        if (item.Header is FrameworkElement header && header.ActualHeight > 0)
            return Math.Min(itemHeight, Math.Max(18.0, header.ActualHeight + 6.0));

        return Math.Min(itemHeight, 28.0);
    }

    private void UpdateTakeoffPositionDropCue(TreeViewItem? targetItem, bool after, bool canDrop, string status)
    {
        if (ReferenceEquals(_takeoffPositionDropTarget, targetItem) &&
            _takeoffPositionDropAfter == after &&
            _takeoffPositionDropAllowed == canDrop &&
            string.Equals(_takeoffPositionDropStatus, status, StringComparison.Ordinal))
        {
            return;
        }

        _takeoffPositionDropTarget = targetItem;
        _takeoffPositionDropAfter = after;
        _takeoffPositionDropAllowed = canDrop;
        _takeoffPositionDropStatus = status;
        ApplyTakeoffPageHighlights();
        if (!string.IsNullOrWhiteSpace(status))
            TxtStatus.Text = status;
    }

    private void ClearTakeoffPositionDropCue()
    {
        if (_takeoffPositionDropTarget == null && string.IsNullOrEmpty(_takeoffPositionDropStatus))
            return;

        _takeoffPositionDropTarget = null;
        _takeoffPositionDropAfter = false;
        _takeoffPositionDropAllowed = false;
        _takeoffPositionDropStatus = "";
        ApplyTakeoffPageHighlights();
    }

    private void DropTakeoffPosition(TakeoffsClipboard payload, TreeViewItem targetItem, bool after)
    {
        string? targetPath = GetTakeoffNodePath(targetItem);
        if (string.IsNullOrWhiteSpace(targetPath))
            return;

        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Count == 0)
            return;

        try
        {
            FlushTakeoffAutosaves();
            string targetParent = Path.GetDirectoryName(targetPath) ?? "";
            if (string.IsNullOrWhiteSpace(targetParent))
                return;

            var changed = new List<string>();
            var rebasedLegendPaths = new List<(string OldPath, string NewPath)>();
            if (paths.All(path => string.Equals(Path.GetDirectoryName(path) ?? "", targetParent, StringComparison.OrdinalIgnoreCase)))
            {
                if (!OurPlaneCoreJobStore.MoveSiblingsToPosition(paths, targetPath, after))
                    return;
                changed.AddRange(paths);
            }
            else
            {
                foreach (var entry in payload.Entries)
                {
                    if (string.Equals(Path.GetDirectoryName(entry.SourcePath) ?? "", targetParent, StringComparison.OrdinalIgnoreCase))
                    {
                        changed.Add(entry.SourcePath);
                        continue;
                    }

                    if (!CanDropTakeoffsInto(new TakeoffsClipboard([entry], TakeoffsClipboardMode.Cut), targetParent, TakeoffsClipboardMode.Cut))
                        continue;

                    string changedPath = OurPlaneCoreJobStore.MoveNode(entry.SourcePath, targetParent);
                    changed.Add(changedPath);
                    rebasedLegendPaths.Add((entry.SourcePath, changedPath));
                }

                if (changed.Count == 0 ||
                    !OurPlaneCoreJobStore.MoveSiblingsToPosition(changed, targetPath, after))
                {
                    return;
                }

                _takeoffsClipboard = null;
                foreach (var (oldPath, newPath) in rebasedLegendPaths)
                    RebasePageLegendTakeoffOrderReferences(oldPath, newPath);
            }

            LoadTakeoffsForJob();
            SetTakeoffMultiSelection(changed);
            SelectFirstTakeoffPath(changed);
            TxtStatus.Text = changed.Count == 1
                ? $"Moved takeoff node {(after ? "after" : "before")} {OurPlaneCoreJobStore.DisplayName(targetPath)}."
                : $"Moved {changed.Count} takeoff nodes {(after ? "after" : "before")} {OurPlaneCoreJobStore.DisplayName(targetPath)}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Reorder Takeoffs", ex);
        }
    }

    private bool CanPasteTakeoffsInto(string? targetFolder) =>
        _takeoffsClipboard != null && CanDropTakeoffsInto(_takeoffsClipboard, targetFolder, _takeoffsClipboard.Mode);

    private bool CanDropTakeoffsInto(TakeoffsClipboard payload, string? targetFolder, TakeoffsClipboardMode mode)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(targetFolder) || payload.Entries.Count == 0)
            return false;
        if (!Directory.Exists(targetFolder))
            return false;

        if (!OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, targetFolder) ||
            OurPlaneCoreJobStore.IsTakeoffItemFolder(targetFolder))
            return false;

        bool hasMovableEntry = false;
        foreach (var entry in payload.Entries)
        {
            if (!Directory.Exists(entry.SourcePath))
                return false;
            if (!OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, entry.SourcePath) ||
                string.Equals(entry.SourcePath, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
                return false;

            if (OurPlaneCoreJobStore.IsSameOrDescendant(entry.SourcePath, targetFolder))
                return false;

            if (mode == TakeoffsClipboardMode.Cut)
            {
                string parent = Path.GetDirectoryName(entry.SourcePath) ?? "";
                if (string.Equals(parent, targetFolder, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            hasMovableEntry = true;
        }

        return mode == TakeoffsClipboardMode.Copy || hasMovableEntry;
    }

    private bool CanDropTakeoffSections(TakeoffSectionDrag payload, TreeViewItem? targetItem, bool copy)
    {
        if (payload.Nodes.Count == 0 || GetTakeoffSectionDropTarget(targetItem) is not { } target)
            return false;

        string targetType = OurPlaneCoreJobStore.NormalizeMeasurementType(target.MeasurementType);
        bool hasMovableNode = false;
        foreach (TakeoffMeasurementNode node in payload.Nodes)
        {
            if (!node.Item.Measurements.Contains(node.Measurement))
                return false;
            if (OurPlaneCoreJobStore.NormalizeMeasurementType(node.Measurement.MType) != targetType ||
                OurPlaneCoreJobStore.NormalizeMeasurementType(node.Item.MeasurementType) != targetType)
                return false;
            if (!ReferenceEquals(node.Item, target))
                hasMovableNode = true;
        }

        return copy || hasMovableNode;
    }

    private string TakeoffSectionDropStatus(TakeoffSectionDrag payload, TreeViewItem? targetItem, bool copy, bool canDrop)
    {
        if (payload.Nodes.Count == 0)
            return "Select section/count rows before dragging.";
        if (targetItem == null)
            return "Drop section/count rows on a takeoff item.";
        if (targetItem.Tag is TakeoffFolderNode)
            return "Drop section/count rows on a takeoff item, not a folder.";
        if (GetTakeoffSectionDropTarget(targetItem) is not { } target)
            return "Drop section/count rows on a takeoff item.";

        string action = copy ? "Copy" : "Move";
        string targetType = OurPlaneCoreJobStore.NormalizeMeasurementType(target.MeasurementType);
        TakeoffMeasurementNode? stale = payload.Nodes.FirstOrDefault(node => !node.Item.Measurements.Contains(node.Measurement));
        if (stale != null)
            return "Selected section/count row no longer exists.";

        TakeoffMeasurementNode? mismatch = payload.Nodes.FirstOrDefault(node =>
            OurPlaneCoreJobStore.NormalizeMeasurementType(node.Measurement.MType) != targetType ||
            OurPlaneCoreJobStore.NormalizeMeasurementType(node.Item.MeasurementType) != targetType);
        if (mismatch != null)
        {
            string sourceType = MeasurementTypeTitle(OurPlaneCoreJobStore.NormalizeMeasurementType(mismatch.Measurement.MType));
            string destinationType = MeasurementTypeTitle(targetType);
            return $"{action} blocked: {sourceType} rows can only drop on {sourceType} takeoff items, not {destinationType}.";
        }

        if (!copy && payload.Nodes.All(node => ReferenceEquals(node.Item, target)))
            return $"Already in {target.Name}. Hold Ctrl while dropping to copy.";

        return canDrop
            ? $"{action} {payload.Nodes.Count} section/count row(s) to {target.Name}."
            : $"Cannot {(copy ? "copy" : "move")} selected section/count rows to {target.Name}.";
    }

    private void UpdateTakeoffSectionDropCue(TakeoffSectionDrag payload, TreeViewItem? targetItem, bool copy, bool canDrop)
    {
        string status = TakeoffSectionDropStatus(payload, targetItem, copy, canDrop);
        if (ReferenceEquals(_takeoffSectionDropTarget, targetItem) &&
            _takeoffSectionDropAllowed == canDrop &&
            string.Equals(_takeoffSectionDropStatus, status, StringComparison.Ordinal))
        {
            return;
        }

        _takeoffSectionDropTarget = targetItem;
        _takeoffSectionDropAllowed = canDrop;
        _takeoffSectionDropStatus = status;
        ApplyTakeoffPageHighlights();
        if (!string.IsNullOrWhiteSpace(status))
            TxtStatus.Text = status;
    }

    private void ClearTakeoffSectionDropCue()
    {
        if (_takeoffSectionDropTarget == null && string.IsNullOrEmpty(_takeoffSectionDropStatus))
            return;

        _takeoffSectionDropTarget = null;
        _takeoffSectionDropAllowed = false;
        _takeoffSectionDropStatus = "";
        ApplyTakeoffPageHighlights();
    }

    private void DropTakeoffSections(TakeoffSectionDrag payload, TreeViewItem targetItem, bool copy)
    {
        if (!CanDropTakeoffSections(payload, targetItem, copy) ||
            GetTakeoffSectionDropTarget(targetItem) is not { } target)
        {
            return;
        }

        FlushTakeoffAutosaves();
        string targetType = OurPlaneCoreJobStore.NormalizeMeasurementType(target.MeasurementType);
        var changedItems = new HashSet<TakeoffItem>();
        var resultingNodes = new List<TakeoffMeasurementNode>();

        foreach (TakeoffMeasurementNode node in payload.Nodes
                     .GroupBy(node => node.Measurement.Id, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            if (copy)
            {
                Measurement copied = CloneMeasurementForTakeoff(node.Measurement, target, targetType);
                target.Measurements.Add(copied);
                changedItems.Add(target);
                resultingNodes.Add(new TakeoffMeasurementNode(target, copied));
                continue;
            }

            if (ReferenceEquals(node.Item, target))
                continue;

            if (!node.Item.Measurements.Remove(node.Measurement))
                continue;

            node.Measurement.TakeoffFolder = target.FolderPath;
            node.Measurement.MType = targetType;
            node.Measurement.Color = target.Color;
            target.Measurements.Add(node.Measurement);
            changedItems.Add(node.Item);
            changedItems.Add(target);
            resultingNodes.Add(new TakeoffMeasurementNode(target, node.Measurement));
        }

        if (resultingNodes.Count == 0)
            return;

        foreach (TakeoffItem changed in changedItems)
        {
            OurPlaneCoreJobStore.SaveTakeoffItem(changed);
            RefreshTreeItem(changed);
        }

        _viewport.LoadMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
        CancelPendingTakeoffSelectionSync();
        SelectTakeoffSectionNodesSilently(resultingNodes);
        SelectTakeoffSectionMeasurementsOnCanvas(resultingNodes);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        RefreshEstimateTable();
        UpdateTotalDisplay();
        TxtStatus.Text = copy
            ? $"Copied {resultingNodes.Count} section/count row(s) to {target.Name}."
            : $"Moved {resultingNodes.Count} section/count row(s) to {target.Name}.";
    }

    private static Measurement CloneMeasurementForTakeoff(Measurement source, TakeoffItem target, string targetType) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = source.Name,
            Notes = source.Notes,
            MType = targetType,
            Points = source.Points.ToList(),
            Color = target.Color,
            PageFolder = source.PageFolder,
            TakeoffFolder = target.FolderPath,
            ScaleMetersPerPt = source.ScaleMetersPerPt,
        };

    private static TakeoffItem? GetTakeoffSectionDropTarget(TreeViewItem? item) =>
        item?.Tag switch
        {
            TakeoffItem target => target,
            TakeoffMeasurementNode node => node.Item,
            _ => null,
        };

    private string? GetTakeoffPasteTargetFolder(TreeViewItem item)
    {
        return item.Tag switch
        {
            TakeoffFolderNode folder => folder.FolderPath,
            TakeoffItem takeoff => Path.GetDirectoryName(takeoff.FolderPath),
            _ => _currentJob?.TakeoffsRoot,
        };
    }

    private IReadOnlyList<TakeoffsClipboardEntry> GetSelectedTakeoffEntries(TreeViewItem anchor)
    {
        string? path = GetTakeoffNodePath(anchor);
        if (path == null || _currentJob == null || string.Equals(path, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
            return [];

        IEnumerable<string> paths = _takeoffsMultiSelection.Contains(path)
            ? _takeoffsMultiSelection
            : [path];

        var entries = paths
            .Where(Directory.Exists)
            .Where(candidate => _currentJob != null &&
                                OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, candidate) &&
                                !string.Equals(candidate, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => new TakeoffsClipboardEntry(
                candidate,
                OurPlaneCoreJobStore.IsTakeoffItemFolder(candidate)))
            .ToList();

        return NormalizeSelectedTakeoffEntries(entries);
    }

    private static string? GetTakeoffNodePath(TreeViewItem item) =>
        item.Tag switch
        {
            TakeoffItem takeoff => takeoff.FolderPath,
            TakeoffFolderNode folder => folder.IsRoot ? null : folder.FolderPath,
            _ => null,
        };

    private static string TakeoffSectionSelectionKey(TakeoffMeasurementNode node) =>
        $"{NormalizePath(node.Item.FolderPath)}|{node.Measurement.Id}";

    private static string? GetTakeoffSectionSelectionKey(TreeViewItem item) =>
        item.Tag is TakeoffMeasurementNode node ? TakeoffSectionSelectionKey(node) : null;

    private List<TakeoffMeasurementNode> SelectedTakeoffSectionNodes(TakeoffMeasurementNode anchor, bool fallbackToAnchor)
    {
        string anchorKey = TakeoffSectionSelectionKey(anchor);
        IEnumerable<string> keys = _takeoffSectionMultiSelection.Contains(anchorKey)
            ? _takeoffSectionMultiSelection
            : fallbackToAnchor
                ? [anchorKey]
                : Enumerable.Empty<string>();

        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        if (keySet.Count == 0)
            return [];

        return EnumerateTakeoffTreeItems(TakeoffsTree)
            .Select(item => item.Tag as TakeoffMeasurementNode)
            .Where(node => node != null && keySet.Contains(TakeoffSectionSelectionKey(node)))
            .Select(node => node!)
            .ToList();
    }

    private void SelectTakeoffSectionRange(string? anchorKey, string targetKey, TakeoffItem item, bool additive)
    {
        var candidates = EnumerateVisibleTreeItems(TakeoffsTree)
            .Where(treeItem => treeItem.Tag is TakeoffMeasurementNode node && ReferenceEquals(node.Item, item))
            .Select(treeItem => (Item: treeItem, Key: GetTakeoffSectionSelectionKey(treeItem)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => (entry.Item, Key: entry.Key!))
            .ToList();

        SelectRangeKeys(candidates, anchorKey, targetKey, _takeoffSectionMultiSelection, additive);
    }

    private void SelectTakeoffSectionNodesSilently(IReadOnlyList<TakeoffMeasurementNode> nodes)
    {
        _takeoffsMultiSelection.Clear();
        _takeoffSectionMultiSelection.Clear();
        foreach (TakeoffMeasurementNode node in nodes)
            _takeoffSectionMultiSelection.Add(TakeoffSectionSelectionKey(node));

        TreeViewItem? first = nodes
            .Select(node => FindTakeoffSectionTreeItem(TakeoffsTree, node.Measurement))
            .FirstOrDefault(item => item != null);
        if (first != null)
        {
            _syncingTakeoffTreeSelection = true;
            try
            {
                TreeViewItem visibleTarget = TakeoffVisibleSelectionTarget(first);
                ExpandTakeoffFolderAncestorsWithoutTracking(visibleTarget);
                visibleTarget.IsSelected = true;
                visibleTarget.BringIntoView();
            }
            finally
            {
                _syncingTakeoffTreeSelection = false;
            }
        }

        ApplyTakeoffPageHighlights();
    }

    private static IReadOnlyList<TakeoffsClipboardEntry> NormalizeSelectedTakeoffEntries(
        IReadOnlyList<TakeoffsClipboardEntry> entries)
    {
        var distinct = entries
            .GroupBy(e => NormalizePath(e.SourcePath), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => NormalizePath(e.SourcePath).Length)
            .ToList();

        var result = new List<TakeoffsClipboardEntry>();
        foreach (var entry in distinct)
        {
            if (result.Any(parent => OurPlaneCoreJobStore.IsSameOrDescendant(parent.SourcePath, entry.SourcePath)))
                continue;
            result.Add(entry);
        }

        return result;
    }

    private int TakeoffSelectionCount(TreeViewItem anchor) =>
        GetSelectedTakeoffEntries(anchor).Count;

    private void SetTakeoffMultiSelection(IEnumerable<string> paths)
    {
        _takeoffsMultiSelection.Clear();
        _takeoffSectionMultiSelection.Clear();
        foreach (string path in paths.Where(Directory.Exists))
            _takeoffsMultiSelection.Add(path);
        ApplyTakeoffPageHighlights();
    }

    private void SelectFirstTakeoffPath(IReadOnlyList<string> paths)
    {
        foreach (string path in paths)
        {
            if (FindTakeoffTreeItemByFolder(path) is { } selected)
            {
                selected.IsSelected = true;
                selected.BringIntoView();
                return;
            }
        }
    }

    private void PruneTakeoffsMultiSelection()
    {
        if (_currentJob == null)
        {
            _takeoffsMultiSelection.Clear();
            return;
        }

        _takeoffsMultiSelection.RemoveWhere(path =>
            !Directory.Exists(path) ||
            !OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, path) ||
            string.Equals(path, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase));
    }

    private void PruneTakeoffSectionMultiSelection()
    {
        var validKeys = _takeoffItems
            .SelectMany(item => item.Measurements.Select(measurement => TakeoffSectionSelectionKey(new TakeoffMeasurementNode(item, measurement))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _takeoffSectionMultiSelection.RemoveWhere(key => !validKeys.Contains(key));
        if (_takeoffSectionRangeAnchorKey != null && !validKeys.Contains(_takeoffSectionRangeAnchorKey))
            _takeoffSectionRangeAnchorKey = null;
    }
}
