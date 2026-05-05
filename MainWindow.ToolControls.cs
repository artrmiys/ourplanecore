using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SmartTakeoffs.Controls;

namespace SmartTakeoffs;

public partial class MainWindow
{
    // Tool controls

    private void BtnTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement btn && btn.Tag is string tool)
        {
            bool forceNewTakeoff = tool is "point" or "line" or "area" &&
                                   (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            SetTool(tool, forceNewTakeoff);
        }
    }

    private void SetupRecordButton()
    {
        _recordButton = new ToggleButton
        {
            Content = "Record",
            ToolTip = "Start recording into the active takeoff target",
            Padding = new Thickness(8, 2, 8, 2),
            MinWidth = 68,
            Margin = new Thickness(4, 0, 1, 0),
            FontWeight = FontWeights.Normal,
        };
        _recordButton.Checked += (_, _) => OnRecordToggled(on: true);
        _recordButton.Unchecked += (_, _) => OnRecordToggled(on: false);

        int areaIndex = MainToolBar.Items.IndexOf(BtnArea);
        MainToolBar.Items.Insert(areaIndex >= 0 ? areaIndex + 1 : MainToolBar.Items.Count, _recordButton);
    }

    // Estimating setup moved to MainWindow.Estimating.cs

    // Massing workspace panel setup moved to MainWindow.MassingPanel.cs

    // Workspace manager callbacks moved to MainWindow.WorkspaceManagers.cs

    // Estimate selection and section properties moved to MainWindow.Estimating.cs

    private void OnRecordToggled(bool on)
    {
        if (_updatingRecordButton)
            return;

        if (on)
        {
            string tool = _activeTool is "point" or "line" or "area"
                ? _activeTool
                : _lastDrawingTool;
            SetTool(tool);
            if (_activeTool is not ("point" or "line" or "area"))
                UpdateRecordButton();
            return;
        }

        if (_activeTool is "point" or "line" or "area")
            SetTool("select");
    }

    private void SetTool(string tool, bool forceNewTakeoff = false)
    {
        if (tool is "point" or "line" or "area" && !EnsureDrawingTakeoff(tool, forceNewTakeoff))
        {
            SyncToolButtonsToActiveTool();
            return;
        }

        ApplyToolSelection(tool);
    }

    private void ApplyToolSelection(string tool)
    {
        _activeTool = tool;
        if (tool is "point" or "line" or "area")
            _lastDrawingTool = tool;
        _viewport.SetTool(tool);
        foreach (var (t, btn) in _toolBtns)
            btn.IsChecked = t == tool;
        UpdateRecordButton();
        UpdateToolStatus();
    }

    private void SyncToolButtonsToActiveTool()
    {
        foreach (var (t, btn) in _toolBtns)
            btn.IsChecked = t == _activeTool;
        UpdateRecordButton();
        UpdateToolStatus();
    }

    private void UpdateRecordButton()
    {
        if (_recordButton == null)
            return;

        bool recording = _activeTool is "point" or "line" or "area";
        string recordType = recording ? MeasurementTypeTitle(_activeTool) : "";
        _updatingRecordButton = true;
        _recordButton.IsChecked = recording;
        _recordButton.Content = recording ? $"Rec {recordType}" : "Record";
        _recordButton.ToolTip = recording
            ? _activeItem == null
                ? $"Recording {recordType}; no active takeoff target is selected."
                : $"Recording {recordType} into {_activeItem.Name}. Click to stop."
            : "Start recording into the active takeoff target.";
        _recordButton.Background = recording
            ? new SolidColorBrush(Color.FromRgb(196, 32, 32))
            : (Brush)FindResource("ControlBackgroundBrush");
        _recordButton.Foreground = recording
            ? Brushes.White
            : (Brush)FindResource("ControlForegroundBrush");
        _recordButton.BorderBrush = recording
            ? new SolidColorBrush(Color.FromRgb(120, 0, 0))
            : (Brush)FindResource("ControlBorderBrush");
        _updatingRecordButton = false;
    }

    private void BtnSnap_Checked(object sender, RoutedEventArgs e) =>
        SetSnapMode(enabled: true);

    private void BtnSnap_Unchecked(object sender, RoutedEventArgs e) =>
        SetSnapMode(enabled: false);

    private void BtnOrtho_Checked(object sender, RoutedEventArgs e) =>
        SetOrthoMode(enabled: true);

    private void BtnOrtho_Unchecked(object sender, RoutedEventArgs e) =>
        SetOrthoMode(enabled: false);

    private void SetSnapMode(bool enabled)
    {
        if (_updatingConstraintButtons)
            return;

        _viewport.SnapEnabled = enabled;
        UpdateConstraintButtons();
        UpdateToolStatus();
    }

    private void SetOrthoMode(bool enabled)
    {
        if (_updatingConstraintButtons)
            return;

        _viewport.OrthoEnabled = enabled;
        UpdateConstraintButtons();
        UpdateToolStatus();
    }

    private void OnViewportSnapChanged(bool enabled)
    {
        UpdateConstraintButtons();
        UpdateToolStatus();
    }

    private void OnViewportOrthoChanged(bool enabled)
    {
        UpdateConstraintButtons();
        UpdateToolStatus();
    }

    private void UpdateConstraintButtons()
    {
        _updatingConstraintButtons = true;
        try
        {
            BtnSnap.IsChecked = _viewport.SnapEnabled;
            BtnSnap.Content = _viewport.SnapEnabled ? "Snap On" : "Snap";
            BtnOrtho.IsChecked = _viewport.OrthoEnabled;
            BtnOrtho.Content = _viewport.OrthoEnabled ? "Ortho On" : "Ortho";
        }
        finally
        {
            _updatingConstraintButtons = false;
        }
    }

    private bool EnsureDrawingTakeoff(string tool, bool forceNewTakeoff = false)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Takeoff Item",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (_currentPage == null)
        {
            MessageBox.Show("Select a page before drawing measurements.", "Takeoff Item",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        string mtype = SmartTakeoffsJobStore.NormalizeMeasurementType(tool);
        if (mtype is "line" or "area" && _currentPage.ScaleMetersPerPt <= 0)
        {
            MessageBox.Show(
                "Set the page scale before drawing Line or Area measurements.",
                "Scale Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (!forceNewTakeoff &&
            _activeItem != null &&
            SmartTakeoffsJobStore.NormalizeMeasurementType(_activeItem.MeasurementType) == mtype)
        {
            _activeItem.MeasurementType = mtype;
            _viewport.ActiveColor = _activeItem.Color;
            _viewport.ActiveTakeoffFolder = _activeItem.FolderPath;
            return true;
        }

        if (!forceNewTakeoff && !ConfirmCreateDrawingTakeoffTarget(mtype))
            return false;

        string parentFolder = NewTakeoffItemParentFolder();
        string defaultColor = ResolveTakeoffFolderDefaultColor(
            parentFolder,
            _activeItem?.Color ?? _viewport.ActiveColor);
        var dlg = new NewItemDialog(
            mtype,
            DefaultTakeoffNameForFolder(mtype, parentFolder),
            lockType: true,
            defaultColor: defaultColor)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true)
            return false;

        var newItem = CreateUniqueTakeoffItem(dlg.ItemName, dlg.ItemColor, mtype, parentFolder);
        ApplyTakeoffFolderDefaultsToNewItem(newItem, parentFolder);
        _takeoffItems.Add(newItem);
        var treeParent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
        var tvi = AddTakeoffTreeItem(newItem, treeParent);
        if (treeParent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;

        _activeItem = newItem;
        _activeTakeoffParentFolder = parentFolder;
        _viewport.ActiveColor = newItem.Color;
        _viewport.ActiveTakeoffFolder = newItem.FolderPath;
        tvi.IsSelected = true;
        UpdateToolStatus();
        RefreshActiveTakeoffVisuals();
        UpdateTotalDisplay();
        return true;
    }

    private bool ConfirmCreateDrawingTakeoffTarget(string measurementType)
    {
        string targetType = MeasurementTypeTitle(measurementType);
        string message = _activeItem == null
            ? $"No active takeoff target is selected.\n\nCreate a {targetType} takeoff item before recording?"
            : $"Active target is {_activeItem.Name} ({MeasurementTypeTitle(_activeItem.MeasurementType)}).\n\n{targetType} recording needs a {targetType} takeoff item. Create a separate target?";

        return MessageBox.Show(
            message,
            "Create Takeoff Target",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void BtnFit_Click(object sender, RoutedEventArgs e)    => _viewport.ZoomFit();
    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)  => _viewport.ZoomIn();
    private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => _viewport.ZoomOut();

    private void BtnSetScale_Click(object sender, RoutedEventArgs e) => ApplyScaleFromEntry();

    private void TxtScaleRatio_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return || e.Key == Key.Enter)
            ApplyScaleFromEntry();
    }

    private void ApplyScaleFromEntry()
    {
        const double PT_M = 25.4 / 72.0 / 1000.0;
        if (!double.TryParse(TxtScaleRatio.Text.Replace(",", "."),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double ratio)
            || ratio <= 0)
        {
            MessageBox.Show("Enter a valid number, e.g. 100 for 1:100",
                            "Scale", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _viewport.ScaleMetersPerPt = PT_M * ratio;
        if (_currentPage != null)
            _currentPage.ScaleMetersPerPt = _viewport.ScaleMetersPerPt;
        ApplyScaleToCurrentPageMeasurements(_viewport.ScaleMetersPerPt);
        SaveCurrentPageScale();
        UpdateScaleUi(_viewport.ScaleMetersPerPt);
        TxtStatus.Text = $"Scale set: 1:{ratio:F0}  (1pt = {_viewport.ScaleMetersPerPt:F6} m)";
        RefreshAllTotals();
    }

    private void BtnScalePresets_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };

        menu.Items.Add(new MenuItem { Header = "── Metric ──", IsEnabled = false });
        foreach (var (label, ratio) in MetricPresets)
            AddPresetItem(menu, label, ratio);

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "── Imperial ──", IsEnabled = false });
        foreach (var (label, ratio) in ImperialPresets)
            AddPresetItem(menu, label, ratio);

        menu.IsOpen = true;
    }

    private void AddPresetItem(ContextMenu menu, string label, double ratio)
    {
        var mi = new MenuItem { Header = label };
        mi.Click += (_, _) =>
        {
            TxtScaleRatio.Text = $"{ratio:F0}";
            ApplyScaleFromEntry();
        };
        menu.Items.Add(mi);
    }
}
