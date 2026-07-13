using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private sealed record MeasurementMergeTargetOption(TakeoffItem Item, string Label);

    private void BtnMergeSelectedMeasurements_Click(object sender, RoutedEventArgs e) =>
        MergeSelectedMeasurementsToPromptedTakeoff();

    private void BtnSplitSelectedMeasurements_Click(object sender, RoutedEventArgs e) =>
        SplitSelectedMeasurementsToNewTakeoff();

    private void BtnCombineUnion_Click(object sender, RoutedEventArgs e) =>
        CombineSelectedAreasIfEnabled(Controls.AreaCombineMode.Union);

    private void BtnCombineSubtract_Click(object sender, RoutedEventArgs e) =>
        CombineSelectedAreasIfEnabled(Controls.AreaCombineMode.Subtract);

    private void BtnCombineIntersect_Click(object sender, RoutedEventArgs e) =>
        CombineSelectedAreasIfEnabled(Controls.AreaCombineMode.Intersect);

    private void BtnCombineRemoveOverlap_Click(object sender, RoutedEventArgs e) =>
        CombineSelectedAreasIfEnabled(Controls.AreaCombineMode.RemoveOverlap);

    private void BtnCombineDivide_Click(object sender, RoutedEventArgs e) =>
        CombineSelectedAreasIfEnabled(Controls.AreaCombineMode.Divide);

    private void CombineSelectedAreasIfEnabled(Controls.AreaCombineMode mode)
    {
        if (RequireModule(ModuleId.AdvancedTakeoffTools, "Combine Areas"))
            _viewport.CombineSelectedAreas(mode);
    }

    private void MergeSelectedMeasurementsToPromptedTakeoff(
        IReadOnlyList<Measurement>? explicitSelection = null,
        TakeoffMeasurementNode? sectionAnchor = null)
    {
        if (!RequireModule(ModuleId.AdvancedTakeoffTools, "Merge Measurements"))
            return;

        IReadOnlyList<Measurement> selected = SelectedMeasurementsForMergeSplit(explicitSelection, sectionAnchor);
        if (!ValidateMergeSplitSelection(selected, out string measurementType))
            return;

        TakeoffItem? target = PromptMergeTargetTakeoff(selected, measurementType);
        if (target == null)
            return;

        MoveSelectedMeasurementsToTakeoff(
            selected,
            target,
            moved => $"Merged {moved} segment(s) into {target.Name}.");
    }

    private void SplitSelectedMeasurementsToNewTakeoff(
        IReadOnlyList<Measurement>? explicitSelection = null,
        TakeoffMeasurementNode? sectionAnchor = null)
    {
        if (!RequireModule(ModuleId.AdvancedTakeoffTools, "Split Measurements"))
            return;

        IReadOnlyList<Measurement> selected = SelectedMeasurementsForMergeSplit(explicitSelection, sectionAnchor);
        if (!ValidateMergeSplitSelection(selected, out string measurementType))
            return;

        IReadOnlyList<TakeoffItem> sourceItems = SourceItemsForMeasurements(selected);
        string initialName = DefaultSplitTakeoffName(sourceItems, selected, measurementType);
        string? name = ShowInputDialog("New takeoff name:", initialName, "Split Selected Measurements");
        if (name == null)
            return;

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            TxtStatus.Text = "Split skipped: enter a takeoff name.";
            return;
        }

        TakeoffItem target = CreateSplitTargetTakeoff(name, measurementType, sourceItems, selected);
        MoveSelectedMeasurementsToTakeoff(
            selected,
            target,
            moved => $"Split {moved} segment(s) into new takeoff {target.Name}.");
    }

    private IReadOnlyList<Measurement> SelectedMeasurementsForMergeSplit(
        IReadOnlyList<Measurement>? explicitSelection,
        TakeoffMeasurementNode? sectionAnchor)
    {
        if (explicitSelection is { Count: > 0 })
            return explicitSelection.Distinct().ToList();

        if (sectionAnchor != null)
        {
            return SelectedTakeoffSectionNodes(sectionAnchor, fallbackToAnchor: true)
                .Select(node => node.Measurement)
                .Distinct()
                .ToList();
        }

        var viewportSelection = _viewport.GetSelectedMeasurements()
            .Distinct()
            .ToList();
        if (viewportSelection.Count > 0)
            return viewportSelection;

        if (TakeoffsTree.SelectedItem is TreeViewItem { Tag: TakeoffMeasurementNode node })
        {
            return SelectedTakeoffSectionNodes(node, fallbackToAnchor: true)
                .Select(selectedNode => selectedNode.Measurement)
                .Distinct()
                .ToList();
        }

        return [];
    }

    private bool ValidateMergeSplitSelection(IReadOnlyList<Measurement> selected, out string measurementType)
    {
        measurementType = "";
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before moving measurement segments.";
            return false;
        }

        if (selected.Count == 0)
        {
            TxtStatus.Text = "Select one or more measurement segments first.";
            return false;
        }

        var types = selected
            .Select(measurement => OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (types.Count != 1)
        {
            TxtStatus.Text = "Merge/Split needs one measurement type at a time.";
            return false;
        }

        measurementType = types[0];
        return true;
    }

    private TakeoffItem? PromptMergeTargetTakeoff(IReadOnlyList<Measurement> selected, string measurementType)
    {
        var options = _takeoffItems
            .Where(item => OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == measurementType)
            .Where(item => !selected.All(item.Measurements.Contains))
            .Select(item => new MeasurementMergeTargetOption(item, MergeTargetLabel(item)))
            .ToList();

        if (options.Count == 0)
        {
            TxtStatus.Text = "No compatible target takeoff was found.";
            return null;
        }

        if (options.Count == 1)
            return options[0].Item;

        var win = new Window
        {
            Title = "Merge Into Takeoff",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
        };
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Move {selected.Count} selected segment(s) into:",
            Margin = new Thickness(0, 0, 0, 6),
        });
        var combo = new ComboBox
        {
            ItemsSource = options,
            DisplayMemberPath = nameof(MeasurementMergeTargetOption.Label),
            MinWidth = 360,
        };
        combo.SelectedItem = options.FirstOrDefault(option => ReferenceEquals(option.Item, _activeItem)) ?? options[0];
        panel.Children.Add(combo);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var ok = new Button { Content = "Merge", Width = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 78, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        win.Content = panel;

        TakeoffItem? result = null;
        ok.Click += (_, _) =>
        {
            result = (combo.SelectedItem as MeasurementMergeTargetOption)?.Item;
            win.DialogResult = result != null;
        };
        win.Loaded += (_, _) => combo.Focus();
        return win.ShowDialog() == true ? result : null;
    }

    private string MergeTargetLabel(TakeoffItem item)
    {
        string parent = Path.GetDirectoryName(item.FolderPath) ?? "";
        string parentName = string.IsNullOrWhiteSpace(parent) || _currentJob == null ||
                            string.Equals(parent, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase)
            ? "Takeoffs"
            : OurPlaneCoreJobStore.DisplayName(parent);
        return $"{item.Name}  |  {TakeoffTypeTitle(item)}  |  {parentName}";
    }

    private TakeoffItem CreateSplitTargetTakeoff(
        string name,
        string measurementType,
        IReadOnlyList<TakeoffItem> sourceItems,
        IReadOnlyList<Measurement> selected)
    {
        TakeoffItem? source = sourceItems.FirstOrDefault();
        string parentFolder = SplitTargetParentFolder(sourceItems);
        string color = source?.Color ?? "";
        if (string.IsNullOrWhiteSpace(color))
            color = selected.FirstOrDefault()?.Color ?? "";
        if (string.IsNullOrWhiteSpace(color))
            color = RandomTakeoffColor();

        TakeoffItem target = CreateUniqueTakeoffItem(name, color, measurementType, parentFolder);
        CopySplitTakeoffProperties(target, source, selected, measurementType);
        _takeoffItems.Add(target);

        ItemsControl parent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
        AddTakeoffTreeItem(target, parent);
        if (parent is TreeViewItem parentItem)
            parentItem.IsExpanded = true;

        return target;
    }

    private string SplitTargetParentFolder(IReadOnlyList<TakeoffItem> sourceItems)
    {
        var parentFolders = sourceItems
            .Select(item => Path.GetDirectoryName(item.FolderPath) ?? "")
            .Where(folder => !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return parentFolders.Count == 1
            ? parentFolders[0]
            : NewTakeoffItemParentFolder();
    }

    private static void CopySplitTakeoffProperties(
        TakeoffItem target,
        TakeoffItem? source,
        IReadOnlyList<Measurement> selected,
        string measurementType)
    {
        if (source != null)
        {
            target.UnitPrice = source.UnitPrice;
            target.Notes = source.Notes;
            target.CountSymbol = CountDisplaySymbol.Normalize(source.CountSymbol);
            target.IsJoistTakeoff = source.IsJoistArea;
            target.JoistType = source.JoistType;
            target.JoistSpacingInches = source.JoistSpacingInches;
            target.JoistDirectionDegrees = source.JoistDirectionDegrees;
            target.JoistDirectionFollowsAreaRotation = source.JoistDirectionFollowsAreaRotation;
            target.JoistAddEndJoist = source.JoistAddEndJoist;
            target.JoistPitch = JoistTakeoffCalculator.NormalizePitch(source.JoistPitch);
            target.JoistLengthRounding = JoistTakeoffCalculator.NormalizeLengthRounding(source.JoistLengthRounding);
            target.JoistShowLabels = source.JoistShowLabels;
            target.JoistShowLabelsUserSet = source.JoistShowLabelsUserSet;
            target.JoistDetailedLabels = source.JoistDetailedLabels;
            return;
        }

        if (measurementType != "area")
            return;

        Measurement? joistMeasurement = selected.FirstOrDefault(measurement => measurement.JoistEnabled);
        if (joistMeasurement == null)
            return;

        target.IsJoistTakeoff = true;
        target.JoistType = joistMeasurement.JoistType;
        target.JoistSpacingInches = joistMeasurement.JoistSpacingInches;
        target.JoistDirectionDegrees = joistMeasurement.JoistDirectionDegrees;
        target.JoistDirectionFollowsAreaRotation = joistMeasurement.JoistDirectionFollowsAreaRotation;
        target.JoistAddEndJoist = joistMeasurement.JoistAddEndJoist;
        target.JoistPitch = JoistTakeoffCalculator.NormalizePitch(joistMeasurement.JoistPitch);
        target.JoistLengthRounding = JoistTakeoffCalculator.NormalizeLengthRounding(joistMeasurement.JoistLengthRounding);
        target.JoistShowLabels = joistMeasurement.JoistShowLabels;
        target.JoistDetailedLabels = joistMeasurement.JoistDetailedLabels;
    }

    private void MoveSelectedMeasurementsToTakeoff(
        IReadOnlyList<Measurement> selected,
        TakeoffItem target,
        Func<int, string> statusText)
    {
        try
        {
            MeasurementMoveResult result = MeasurementMergeSplitService.MoveMeasurementsToTakeoff(
                _takeoffItems,
                selected,
                target);
            string status = statusText(result.MovedMeasurements.Count);
            if (result.CoalescedLineCount > 0)
                status = $"{status.TrimEnd('.')}. Coalesced {result.CoalescedLineCount} line section(s).";
            if (result.CoalescedAreaCount > 0)
                status = $"{status.TrimEnd('.')}. Spliced {result.CoalescedAreaCount} area section(s).";
            PersistAndRefreshMeasurementMove(result, status);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            TxtStatus.Text = ex.Message;
        }
    }

    private void PersistAndRefreshMeasurementMove(MeasurementMoveResult result, string status)
    {
        FlushTakeoffAutosaves();
        foreach (TakeoffItem item in result.ChangedItems)
        {
            QueueTakeoffAutosave(item);
            RefreshTreeItem(item);
        }

        ActivateTakeoffItem(result.TargetItem);
        _viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements), clearUndoStack: false);

        var movedNodes = result.SelectedMeasurements
            .Select(measurement => new TakeoffMeasurementNode(result.TargetItem, measurement))
            .ToList();
        SelectTakeoffSectionNodesSilently(movedNodes);
        SelectTakeoffSectionMeasurementsOnCanvas(movedNodes);

        using (UsePageMeasurementLookup())
        {
            RefreshPageTakeoffIndicatorsForFolders(result.PageFolders);
            RefreshTakeoffRowVisualsForItems(result.ChangedItems);
            ApplyTakeoffPageHighlights();
            RefreshSheetLegend();
        }

        RefreshEstimateTable();
        UpdateTotalDisplay();
        TxtStatus.Text = status;
    }

    private IReadOnlyList<TakeoffItem> SourceItemsForMeasurements(IReadOnlyList<Measurement> measurements)
    {
        var itemByMeasurement = BuildTakeoffItemByMeasurementLookup();
        return measurements
            .Select(measurement => itemByMeasurement.TryGetValue(measurement, out TakeoffItem? item) ? item : null)
            .Where(item => item != null)
            .Select(item => item!)
            .Distinct()
            .ToList();
    }

    private static string DefaultSplitTakeoffName(
        IReadOnlyList<TakeoffItem> sourceItems,
        IReadOnlyList<Measurement> selected,
        string measurementType)
    {
        if (selected.Count == 1 && !string.IsNullOrWhiteSpace(selected[0].Name))
            return selected[0].Name.Trim();

        if (sourceItems.Count == 1 && !string.IsNullOrWhiteSpace(sourceItems[0].Name))
            return sourceItems[0].Name.Trim();

        return $"{MeasurementTypeTitle(measurementType)} Split";
    }
}
