using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OurPlaneCore;

public partial class MainWindow
{
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
}
