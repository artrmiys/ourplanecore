using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void SetupEstimateTable()
    {
        TakeoffsPanel.Children.Remove(TakeoffsTreeHost);

        _estimateList = new ListView
        {
            Margin = new Thickness(0),
            MinHeight = 80,
            SelectionMode = SelectionMode.Extended,
            View = new GridView
            {
                Columns =
                {
                    new GridViewColumn { Header = "Item / Section", Width = 110, DisplayMemberBinding = new Binding(nameof(EstimateDisplayRow.Item)) },
                    new GridViewColumn { Header = "Type/Page", Width = 82, DisplayMemberBinding = new Binding(nameof(EstimateDisplayRow.Page)) },
                    new GridViewColumn { Header = "Sec", Width = 34, DisplayMemberBinding = new Binding(nameof(EstimateDisplayRow.Sections)) },
                    new GridViewColumn { Header = "Qty", Width = 62, DisplayMemberBinding = new Binding(nameof(EstimateDisplayRow.Quantity)) },
                    new GridViewColumn { Header = "Unit", Width = 32, DisplayMemberBinding = new Binding(nameof(EstimateDisplayRow.Unit)) },
                    new GridViewColumn { Header = "Price", Width = 48, DisplayMemberBinding = new Binding(nameof(EstimateDisplayRow.UnitPrice)) },
                    new GridViewColumn { Header = "Cost", Width = 54, DisplayMemberBinding = new Binding(nameof(EstimateDisplayRow.Cost)) },
                    new GridViewColumn { Header = "Notes", Width = 140, DisplayMemberBinding = new Binding(nameof(EstimateDisplayRow.Notes)) },
                },
            },
        };
        VirtualizingPanel.SetIsVirtualizing(_estimateList, true);
        VirtualizingPanel.SetVirtualizationMode(_estimateList, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_estimateList, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(_estimateList, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_estimateList, ScrollBarVisibility.Auto);
        _estimateList.SelectionChanged += OnEstimateSelectionChanged;
        _estimateList.SelectionChanged += (_, _) => UpdateEstimateActionButtons();
        _estimateList.ContextMenu = BuildEstimateContextMenu();

        _estimateFilterBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 4),
            MinHeight = 24,
            ToolTip = "Filter estimating rows by item, section, page, or notes",
        };
        _estimateFilterBox.TextChanged += (_, _) => RefreshEstimateTable();

        _estimateCurrentSheetOnlyBox = new CheckBox
        {
            Content = "Sheet",
            Margin = new Thickness(0, 0, 6, 4),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Show only measurements on the active sheet",
        };
        _estimateCurrentSheetOnlyBox.Checked += (_, _) => RefreshEstimateTable();
        _estimateCurrentSheetOnlyBox.Unchecked += (_, _) => RefreshEstimateTable();

        _estimateSelectButton = EstimateActionButton("Select", "Select estimate rows on the canvas", EstimateSelectSelectedOnCanvas);
        _estimateGoToPageButton = EstimateActionButton("Page", "Open the first selected estimate row's sheet", EstimateGoToSelectedPage);
        _estimatePropertiesButton = EstimateActionButton("Props", "Edit the selected estimate row", EstimateOpenSelectedProperties);
        _estimateOpenWindowButton = EstimateActionButton("Open", "Open Estimating in a full window", OpenEstimatingWindow);

        var estimateToolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var estimateActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        estimateActions.Children.Add(_estimateCurrentSheetOnlyBox);
        estimateActions.Children.Add(_estimateOpenWindowButton);
        estimateActions.Children.Add(_estimateSelectButton);
        estimateActions.Children.Add(_estimateGoToPageButton);
        estimateActions.Children.Add(_estimatePropertiesButton);
        DockPanel.SetDock(estimateActions, Dock.Right);
        estimateToolbar.Children.Add(estimateActions);
        estimateToolbar.Children.Add(_estimateFilterBox);

        _estimateSummaryText = new TextBlock
        {
            Text = "Estimate: no measured rows",
            Margin = new Thickness(2, 4, 2, 0),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _estimateSummaryText.SetResourceReference(Control.ForegroundProperty, "SecondaryForegroundBrush");

        var estimatePanel = new DockPanel { Margin = new Thickness(2) };
        estimatePanel.SetResourceReference(Panel.BackgroundProperty, "PanelBackgroundBrush");
        DockPanel.SetDock(estimateToolbar, Dock.Top);
        DockPanel.SetDock(_estimateSummaryText, Dock.Bottom);
        estimatePanel.Children.Add(estimateToolbar);
        estimatePanel.Children.Add(_estimateSummaryText);
        estimatePanel.Children.Add(_estimateList);

        var tabs = new TabControl
        {
            Margin = new Thickness(2),
        };
        _rightWorkspaceTabs = tabs;
        tabs.ItemContainerStyle = (Style)FindResource("WorkspaceTabItem");
        tabs.SetResourceReference(Control.BackgroundProperty, "PanelBackgroundBrush");
        tabs.SetResourceReference(Control.ForegroundProperty, "ControlForegroundBrush");
        tabs.SetResourceReference(Control.BorderBrushProperty, "ControlBorderBrush");
        tabs.Items.Add(new TabItem
        {
            Header = "Takeoffs",
            Content = TakeoffsTreeHost,
        });
        _estimateTab = new TabItem
        {
            Header = "Estimating",
            Content = estimatePanel,
        };
        tabs.Items.Add(_estimateTab);
        _threeDTab = new TabItem
        {
            Header = "3D",
            Content = BuildThreeDSidePanel(),
        };
        tabs.Items.Add(_threeDTab);
        tabs.SelectedIndex = 0;
        tabs.SelectionChanged += (sender, e) =>
        {
            // Ignore SelectionChanged bubbled up from nested TabControls.
            if (sender is TabControl tc && !ReferenceEquals(e.OriginalSource, tc))
                return;
            if (_estimateTableDirty && ReferenceEquals(tabs.SelectedItem, _estimateTab))
                RefreshEstimateTable();
        };

        TakeoffsPanel.Children.Add(tabs);
        RefreshEstimateTable();
        UpdateEstimateActionButtons();
    }

    private static Button EstimateActionButton(string text, string tooltip, Action action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 4, 0),
            MinWidth = 44,
            FontSize = 11,
            ToolTip = tooltip,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void UpdateEstimateSummaryText(int itemRows, int detailRows, double visibleCost, bool currentSheetOnly, string filter)
    {
        if (_estimateSummaryText == null)
            return;

        string scope = currentSheetOnly ? "Sheet estimate" : "Estimate";
        string filterText = string.IsNullOrWhiteSpace(filter) ? "" : " filtered";
        string costText = visibleCost > 0
            ? $", cost {visibleCost.ToString("F2", CultureInfo.InvariantCulture)}"
            : "";
        _estimateSummaryText.Text = itemRows == 0
            ? $"{scope}: no measured rows{filterText}"
            : $"{scope}: {itemRows} item(s), {detailRows} section/count row(s){costText}{filterText}";
    }

    private void OpenEstimatingWindow()
    {
        if (_estimatingWindow != null)
        {
            _estimatingWindow.RefreshRows();
            _estimatingWindow.Activate();
            return;
        }

        _estimatingWindow = new EstimatingWindow(
            BuildEstimatingWindowRows,
            SelectEstimatingWindowRowsOnCanvas,
            GoToEstimatingWindowRowPage,
            OpenEstimatingWindowRowProperties)
        {
            Owner = this,
        };
        _estimatingWindow.Closed += (_, _) => _estimatingWindow = null;
        _estimatingWindow.Show();
    }

    private IReadOnlyList<EstimatingWindowRow> BuildEstimatingWindowRows(string filter, bool currentSheetOnly)
    {
        bool sheetOnly = currentSheetOnly && _currentPage != null;
        return BuildEstimateDisplayRows(filter, sheetOnly)
            .Select(row => new EstimatingWindowRow
            {
                Item = row.Item,
                Page = row.Page,
                Sections = row.Sections,
                Quantity = row.Quantity,
                Unit = row.Unit,
                UnitPrice = row.UnitPrice,
                Cost = row.Cost,
                Notes = row.Notes,
                Takeoff = row.Takeoff,
                Measurement = row.Measurement,
            })
            .ToList();
    }

    private List<TakeoffMeasurementNode> EstimatingWindowMeasurementNodes(IEnumerable<EstimatingWindowRow> rows)
    {
        var nodes = new List<TakeoffMeasurementNode>();
        foreach (EstimatingWindowRow row in rows)
        {
            if (row.Takeoff == null)
                continue;

            if (row.Measurement != null)
            {
                nodes.Add(new TakeoffMeasurementNode(row.Takeoff, row.Measurement));
                continue;
            }

            nodes.AddRange(row.Takeoff.Measurements.Select(measurement => new TakeoffMeasurementNode(row.Takeoff, measurement)));
        }

        return nodes
            .GroupBy(node => node.Measurement.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private void SelectEstimatingWindowRowsOnCanvas(IReadOnlyList<EstimatingWindowRow> rows)
    {
        List<TakeoffMeasurementNode> nodes = EstimatingWindowMeasurementNodes(rows);
        if (nodes.Count == 0)
        {
            TxtStatus.Text = "Selected estimate rows do not have measurements.";
            return;
        }

        SelectTakeoffSectionNodesSilently(nodes);
        SelectTakeoffSectionMeasurementsOnCanvas(nodes);
    }

    private void GoToEstimatingWindowRowPage(EstimatingWindowRow row)
    {
        List<TakeoffMeasurementNode> nodes = EstimatingWindowMeasurementNodes([row]);
        TakeoffMeasurementNode? target = nodes.FirstOrDefault(node => !string.IsNullOrWhiteSpace(node.Measurement.PageFolder));
        if (target == null)
        {
            TxtStatus.Text = "Selected estimate row does not have a source page.";
            return;
        }

        GoToMeasurementPage(target.Measurement);
        SelectTakeoffSectionNodesSilently(nodes);
        SelectTakeoffSectionMeasurementsOnCanvas(nodes);
    }

    private void OpenEstimatingWindowRowProperties(EstimatingWindowRow row)
    {
        if (row.Takeoff == null)
            return;

        if (row.Measurement != null)
        {
            EditSectionProperties(row.Takeoff, row.Measurement);
            return;
        }

        if (FindTakeoffTreeItem(row.Takeoff) is { } tvi)
            EditTakeoffItemProperties(tvi, row.Takeoff);
    }

    private List<EstimateDisplayRow> SelectedEstimateRows() =>
        _estimateList?.SelectedItems
            .OfType<EstimateDisplayRow>()
            .ToList() ?? [];

    private List<TakeoffMeasurementNode> SelectedEstimateMeasurementNodes()
    {
        var nodes = new List<TakeoffMeasurementNode>();
        foreach (EstimateDisplayRow row in SelectedEstimateRows())
        {
            if (row.Takeoff == null)
                continue;

            if (row.Measurement != null)
            {
                nodes.Add(new TakeoffMeasurementNode(row.Takeoff, row.Measurement));
                continue;
            }

            nodes.AddRange(row.Takeoff.Measurements.Select(measurement => new TakeoffMeasurementNode(row.Takeoff, measurement)));
        }

        return nodes
            .GroupBy(node => node.Measurement.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private void UpdateEstimateActionButtons()
    {
        var selectedRows = SelectedEstimateRows();
        var selectedNodes = SelectedEstimateMeasurementNodes();
        bool hasMeasurements = selectedNodes.Count > 0;
        if (_estimateSelectButton != null)
            _estimateSelectButton.IsEnabled = hasMeasurements;
        if (_estimateGoToPageButton != null)
            _estimateGoToPageButton.IsEnabled = selectedNodes.Any(node => !string.IsNullOrWhiteSpace(node.Measurement.PageFolder));
        if (_estimatePropertiesButton != null)
            _estimatePropertiesButton.IsEnabled = selectedRows.Count == 1 && selectedRows[0].Takeoff != null;
    }

    private void EstimateSelectSelectedOnCanvas()
    {
        var nodes = SelectedEstimateMeasurementNodes();
        if (nodes.Count == 0)
        {
            TxtStatus.Text = "Select estimate measurement rows first.";
            return;
        }

        if (TryBlockMeasurementSelectionDuringRecord(nodes.Select(node => node.Measurement)))
            return;

        SelectTakeoffSectionNodesSilently(nodes);
        SelectTakeoffSectionMeasurementsOnCanvas(nodes);
    }

    private void EstimateGoToSelectedPage()
    {
        var nodes = SelectedEstimateMeasurementNodes();
        TakeoffMeasurementNode? target = nodes.FirstOrDefault(node => !string.IsNullOrWhiteSpace(node.Measurement.PageFolder));
        if (target == null)
        {
            TxtStatus.Text = "Selected estimate rows do not have a source page.";
            return;
        }

        if (TryBlockMeasurementSelectionDuringRecord(nodes.Select(node => node.Measurement)))
            return;

        GoToMeasurementPage(target.Measurement);
        SelectTakeoffSectionNodesSilently(nodes);
        SelectTakeoffSectionMeasurementsOnCanvas(nodes);
    }

    private void EstimateOpenSelectedProperties()
    {
        if (SelectedEstimateRows() is not [EstimateDisplayRow row] || row.Takeoff == null)
            return;

        if (row.Measurement != null)
        {
            EditSectionProperties(row.Takeoff, row.Measurement);
            return;
        }

        if (FindTakeoffTreeItem(row.Takeoff) is { } tvi)
            EditTakeoffItemProperties(tvi, row.Takeoff);
    }

    private ContextMenu BuildEstimateContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();
            var selectedRows = SelectedEstimateRows();
            var selectedNodes = SelectedEstimateMeasurementNodes();
            if (selectedRows.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "No estimate row selected", IsEnabled = false });
                return;
            }

            if (selectedNodes.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "Selected rows have no measurements", IsEnabled = false });
                return;
            }

            EstimateDisplayRow? row = selectedRows.Count == 1 ? selectedRows[0] : null;
            string entryTitle = row?.Takeoff != null ? MeasurementEntryTitle(row.Takeoff) : "Measurement";

            var goToPage = new MenuItem { Header = selectedNodes.Count > 1 ? "Go to First Page" : "Go to Page" };
            goToPage.Click += (_, _) => EstimateGoToSelectedPage();
            menu.Items.Add(goToPage);

            var selectOnCanvas = new MenuItem { Header = selectedNodes.Count > 1 ? $"Select {selectedNodes.Count} on Canvas" : "Select on Canvas" };
            selectOnCanvas.Click += (_, _) => EstimateSelectSelectedOnCanvas();
            menu.Items.Add(selectOnCanvas);

            var properties = new MenuItem { Header = $"{entryTitle} Properties..." };
            properties.IsEnabled = row is { Takeoff: not null };
            properties.Click += (_, _) => EstimateOpenSelectedProperties();
            menu.Items.Add(properties);

            var rename = new MenuItem { Header = $"Rename {entryTitle}" };
            rename.IsEnabled = row is { Takeoff: not null, Measurement: not null };
            rename.Click += (_, _) =>
            {
                if (row is { Takeoff: not null, Measurement: not null })
                    RenameSection(row.Takeoff, row.Measurement);
            };
            menu.Items.Add(rename);

            var delete = new MenuItem { Header = $"Delete {entryTitle}" };
            delete.IsEnabled = row is { Takeoff: not null, Measurement: not null };
            delete.Click += (_, _) =>
            {
                if (row is { Takeoff: not null, Measurement: not null })
                    DeleteSection(row.Takeoff, row.Measurement);
            };
            menu.Items.Add(delete);
        };
        return menu;
    }

    private void SelectSectionOnCanvas(Measurement measurement, bool suppressTakeoffSync = false)
    {
        if (TryBlockMeasurementTakeoffSwitchDuringRecord(measurement))
            return;

        if (!string.IsNullOrWhiteSpace(measurement.PageFolder) &&
            (_currentPage == null ||
             !IsSamePageFolder(_currentPage.FolderPath, measurement.PageFolder)))
        {
            SelectPageByFolder(measurement.PageFolder);
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (suppressTakeoffSync)
                _suppressTakeoffSelectionFromViewport = true;
            try
            {
                _viewport.FocusMeasurement(measurement);
            }
            finally
            {
                if (suppressTakeoffSync)
                    _suppressTakeoffSelectionFromViewport = false;
            }
        });
    }

    private void SelectTakeoffSectionMeasurementsOnCanvas(IReadOnlyList<TakeoffMeasurementNode> nodes)
    {
        var measurements = nodes
            .Select(node => node.Measurement)
            .Distinct()
            .ToList();
        if (measurements.Count == 0)
            return;

        if (TryBlockMeasurementSelectionDuringRecord(measurements))
            return;

        Measurement target = measurements.FirstOrDefault(measurement =>
            _currentPage != null && IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath)) ?? measurements[0];

        if (!string.IsNullOrWhiteSpace(target.PageFolder) &&
            (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, target.PageFolder)))
        {
            SelectPageByFolder(target.PageFolder);
        }

        Dispatcher.InvokeAsync(() =>
        {
            _syncingViewportSelectionFromTakeoffItem = true;
            try
            {
                _viewport.SelectMeasurements(measurements);
            }
            finally
            {
                _syncingViewportSelectionFromTakeoffItem = false;
            }
        });

        TxtStatus.Text = measurements.Count == 1
            ? $"Selected {MeasurementEntryTitle(nodes[0].Item).ToLowerInvariant()} on canvas."
            : $"Selected {measurements.Count} {MeasurementEntryTitlePlural(nodes)} on canvas.";
    }

    private void GoToMeasurementPage(Measurement measurement)
    {
        if (string.IsNullOrWhiteSpace(measurement.PageFolder))
            return;

        SelectPageByFolder(measurement.PageFolder);
    }

    private void OnEstimateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingEstimateSelection || _estimateList == null)
            return;

        var selectedRows = SelectedEstimateRows();
        if (selectedRows.Count == 0)
            return;

        _syncingEstimateSelection = true;
        try
        {
            if (selectedRows.Count > 1)
            {
                EstimateSelectSelectedOnCanvas();
                return;
            }

            EstimateDisplayRow row = selectedRows[0];
            if (row.Measurement != null)
            {
                SelectSectionOnCanvas(row.Measurement);
                return;
            }

            if (row.Takeoff != null)
            {
                if (TryBlockTakeoffSwitchDuringRecord(row.Takeoff))
                    return;

                SelectTakeoffItem(row.Takeoff);
                SelectCurrentPageTakeoffMeasurementsOnCanvas(row.Takeoff);
            }
        }
        finally
        {
            _syncingEstimateSelection = false;
            UpdateEstimateActionButtons();
        }
    }

    private void OnViewportMeasurementSelectionChanged(Measurement? measurement)
    {
        if (_syncingEstimateSelection || _syncingViewportSelectionFromTakeoffItem || _estimateList == null)
            return;

        if (Mouse.LeftButton == MouseButtonState.Pressed)
            return;

        _syncingEstimateSelection = true;
        try
        {
            if (measurement == null)
            {
                _estimateList.SelectedItem = null;
                return;
            }

            if (TryBlockMeasurementTakeoffSwitchDuringRecord(measurement))
                return;

            EstimateDisplayRow? row = _estimateList.Items
                .OfType<EstimateDisplayRow>()
                .FirstOrDefault(r => ReferenceEquals(r.Measurement, measurement));
            if (row == null)
                return;

            _estimateList.SelectedItem = row;
            _estimateList.ScrollIntoView(row);
            ActivateTakeoffForMeasurement(measurement);
            SelectPageTakeoffNodeForMeasurement(measurement);
        }
        finally
        {
            _syncingEstimateSelection = false;
        }
        UpdateEstimateActionButtons();
        _estimatingWindow?.RefreshRows();
    }

    private void OnViewportMeasurementsSelectionChanged(IReadOnlyList<Measurement> measurements)
    {
        if (_syncingViewportSelectionFromTakeoffItem || _suppressTakeoffSelectionFromViewport)
            return;

        SelectTakeoffItemsForViewportMeasurements(measurements);
    }

    private void SelectTakeoffItemsForViewportMeasurements(IReadOnlyList<Measurement> measurements)
    {
        if (TryBlockMeasurementSelectionDuringRecord(measurements))
            return;

        var selectedItems = measurements
            .Select(FindTakeoffItemForMeasurement)
            .Where(item => item != null)
            .Select(item => item!)
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (selectedItems.Count == 0)
        {
            if (IsTakeoffRecordActive())
            {
                RestoreActiveTakeoffTargetAfterBlockedRecordSwitch();
                return;
            }

            _takeoffsMultiSelection.Clear();
            _takeoffSectionMultiSelection.Clear();
            ApplyTakeoffPageHighlights();
            RevealPagesForTakeoffItems([]);
            return;
        }

        _takeoffsMultiSelection.Clear();
        _takeoffSectionMultiSelection.Clear();
        foreach (TakeoffItem item in selectedItems)
            _takeoffsMultiSelection.Add(item.FolderPath);

        Measurement? activeMeasurement = measurements.FirstOrDefault(measurement => FindTakeoffItemForMeasurement(measurement) != null);
        if ((_activeItem == null || !selectedItems.Any(IsActiveTakeoffItem)) &&
            activeMeasurement != null)
        {
            ActivateTakeoffForMeasurement(activeMeasurement);
        }
        else
        {
            ApplyTakeoffPageHighlights();
            RefreshActiveTakeoffVisuals();
        }

        SelectFirstTakeoffItemSilently(selectedItems);
        RevealPagesForTakeoffItems(selectedItems, _currentPage?.FolderPath);

        string? joistStatus = JoistSelectionStatus(measurements);
        if (joistStatus != null)
        {
            TxtStatus.Text = joistStatus;
            return;
        }

        TxtStatus.Text = selectedItems.Count == 1
            ? $"Selected takeoff from sheet: {selectedItems[0].Name}."
            : $"Selected {selectedItems.Count} takeoffs from sheet selection.";
    }

    private string? JoistSelectionStatus(IReadOnlyList<Measurement> measurements)
    {
        var joistAreas = measurements
            .Where(measurement =>
                measurement.JoistEnabled &&
                OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area")
            .ToList();
        if (joistAreas.Count == 0)
            return null;

        if (joistAreas.Count == 1)
        {
            Measurement area = joistAreas[0];
            TakeoffItem? item = FindTakeoffItemForMeasurement(area);
            string name = item?.Name ?? "selected area";
            if (!area.JoistDirectionLocked)
                return $"Selected joist area {name}: set joist direction.";

            JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(area, _viewport.ScaleMetersPerPt);
            return $"Selected joist area {name}: {JoistTakeoffCalculator.FormatDiagnostics(layout, _viewport.UnitMode)}{FormatJoistScaleSuffix(area)}.";
        }

        var layouts = joistAreas
            .Where(area => area.JoistDirectionLocked)
            .Select(area => JoistTakeoffCalculator.Calculate(area, _viewport.ScaleMetersPerPt))
            .Where(layout => layout.HasScale)
            .ToList();
        int pending = joistAreas.Count(area => !area.JoistDirectionLocked);
        int joistCount = layouts.Sum(layout => layout.Count);
        int candidateCount = layouts.Sum(layout => layout.CandidateLineCount);
        string pendingText = pending > 0 ? $", {pending} need direction" : "";
        return $"Selected {joistAreas.Count} joist areas: joists {joistCount} pcs, candidate lines {candidateCount}{pendingText}.";
    }

    private string FormatJoistScaleSuffix(Measurement area)
    {
        double scale = area.ScaleMetersPerPt > 0 ? area.ScaleMetersPerPt : _viewport.ScaleMetersPerPt;
        if (scale <= 0)
            return "";

        double feetPerPt = scale / 0.3048;
        return $", scale {feetPerPt.ToString("0.####", CultureInfo.InvariantCulture)} ft/pt";
    }

    private TakeoffItem? ActivateTakeoffForMeasurement(Measurement measurement)
    {
        TakeoffItem? item = FindTakeoffItemForMeasurement(measurement);
        if (item == null)
            return null;

        if (TryBlockTakeoffSwitchDuringRecord(item))
            return null;

        item.MeasurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        _activeItem = item;
        _activeTakeoffParentFolder = Path.GetDirectoryName(item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;
        _viewport.ActiveCountSymbol = item.CountSymbol;
        SyncToolTypeForTakeoffItem(item);
        RefreshPagesTakeoffIndicators();
        RefreshActiveTakeoffVisuals();
        UpdateTotalDisplay();
        return item;
    }

    private void SelectPageTakeoffNodeForMeasurement(Measurement measurement)
    {
        if (FindTakeoffItemForMeasurement(measurement) is not { } item)
            return;

        string pageFolder = !string.IsNullOrWhiteSpace(measurement.PageFolder)
            ? measurement.PageFolder
            : _currentPage?.FolderPath ?? "";
        if (string.IsNullOrWhiteSpace(pageFolder))
            return;

        SelectPageTakeoffNodeSilently(pageFolder, item.FolderPath);
    }

    private void SelectCurrentPageTakeoffNodeSilently(TakeoffItem item)
    {
        if (_currentPage == null ||
            !item.Measurements.Any(measurement => IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath)))
        {
            return;
        }

        SelectPageTakeoffNodeSilently(_currentPage.FolderPath, item.FolderPath);
    }

    private void SelectCurrentPageTakeoffMeasurementsOnCanvas(TakeoffItem item)
    {
        if (_currentPage == null)
            return;

        SelectTakeoffMeasurementsOnCanvas(item, _currentPage.FolderPath, _currentPage.Name);
    }

    private void RenameSection(TakeoffItem item, Measurement measurement)
    {
        string current = string.IsNullOrWhiteSpace(measurement.Name)
            ? DefaultSectionName(item, measurement)
            : measurement.Name;
        string? name = ShowInputDialog("Section name:", current, "Rename Section");
        if (name == null)
            return;

        measurement.Name = name.Trim();
        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        RefreshTreeItem(item);
        RefreshEstimateTable();
        RefreshSheetLegend();
        TxtStatus.Text = $"Renamed {MeasurementEntryTitle(item).ToLowerInvariant()}: {measurement.Name}";
    }

    private void EditSectionProperties(TakeoffItem item, Measurement measurement)
    {
        string currentName = string.IsNullOrWhiteSpace(measurement.Name)
            ? DefaultSectionName(item, measurement)
            : measurement.Name;

        if (!ShowSectionPropertiesDialog(item, currentName, measurement.Notes, out string name, out string notes))
            return;

        measurement.Name = name.Trim();
        measurement.Notes = notes.Trim();
        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        RefreshTreeItem(item);
        RefreshEstimateTable();
        RefreshSheetLegend();
        TxtStatus.Text = $"Updated {MeasurementEntryTitle(item).ToLowerInvariant()} properties.";
    }

    private void EditTakeoffSectionNotes(TakeoffMeasurementNode anchor)
    {
        var selectedNodes = SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true);
        if (selectedNodes.Count == 0)
            return;

        var selectedMeasurements = selectedNodes
            .Select(node => node.Measurement)
            .Distinct()
            .ToList();
        string firstNotes = selectedMeasurements[0].Notes;
        string initialNotes = selectedMeasurements.All(measurement =>
            string.Equals(measurement.Notes, firstNotes, StringComparison.Ordinal))
            ? firstNotes
            : "";

        string? notes = ShowMultilineInputDialog(
            selectedMeasurements.Count == 1 ? "Notes:" : $"Notes for {selectedMeasurements.Count} selected section/count rows:",
            initialNotes,
            selectedMeasurements.Count == 1 ? "Set Notes" : "Set Section/Count Notes");
        if (notes == null)
            return;

        foreach (var group in selectedNodes.GroupBy(node => node.Item))
        {
            foreach (Measurement measurement in group.Select(node => node.Measurement).Distinct())
                measurement.Notes = notes.Trim();

            OurPlaneCoreJobStore.SaveTakeoffItem(group.Key);
            RefreshTreeItem(group.Key);
        }

        SelectTakeoffSectionNodesSilently(selectedNodes);
        RefreshEstimateTable();
        RefreshSheetLegend();
        TxtStatus.Text = selectedMeasurements.Count == 1
            ? $"Updated notes for {MeasurementEntryTitle(anchor.Item).ToLowerInvariant()}."
            : $"Updated notes for {selectedMeasurements.Count} selected {MeasurementEntryTitlePlural(selectedNodes)}.";
    }

    private void GoToTakeoffSectionsPage(TakeoffMeasurementNode anchor)
    {
        var selectedNodes = SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true);
        TakeoffMeasurementNode? targetNode = selectedNodes.FirstOrDefault(node =>
            !string.IsNullOrWhiteSpace(node.Measurement.PageFolder));
        if (targetNode == null)
            return;

        GoToMeasurementPage(targetNode.Measurement);
        SelectTakeoffSectionNodesSilently(selectedNodes);
        SelectTakeoffSectionMeasurementsOnCanvas(selectedNodes);
    }

    private bool ShowSectionPropertiesDialog(
        TakeoffItem item,
        string currentName,
        string currentNotes,
        out string name,
        out string notes)
    {
        name = currentName;
        notes = currentNotes;

        var dialog = new Window
        {
            Title = $"{MeasurementEntryTitle(item)} Properties",
            Owner = this,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Name:", Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new TextBox { Text = currentName };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock { Text = "Notes:", Margin = new Thickness(0, 10, 0, 4) });
        var notesBox = new TextBox
        {
            Text = currentNotes,
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
            Margin = new Thickness(0, 10, 0, 0),
        };
        var ok = new Button { Content = "OK", Width = 70, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 70, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };

        if (dialog.ShowDialog() != true)
            return false;

        name = string.IsNullOrWhiteSpace(nameBox.Text) ? currentName : nameBox.Text.Trim();
        notes = notesBox.Text.Trim();
        return true;
    }
}
