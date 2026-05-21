using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    // Tool controls

    private void BtnTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement btn && btn.Tag is string tool)
        {
            bool forceNewTakeoff = IsRecordTool(tool);
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

        int areaIndex = MainToolBar.Items.IndexOf(BtnAreaCut);
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
            string tool = IsRecordTool(_activeTool)
                ? _activeTool
                : RecordToolForActiveTakeoff();
            SetTool(tool);
            if (!IsRecordTool(_activeTool))
                UpdateRecordButton();
            return;
        }

        if (IsRecordTool(_activeTool))
            SetTool("select");
    }

    private void SetTool(string tool, bool forceNewTakeoff = false)
    {
        if (IsRecordTool(tool) && !EnsureDrawingTakeoff(tool, forceNewTakeoff))
        {
            SyncToolButtonsToActiveTool();
            return;
        }

        ApplyToolSelection(tool);
    }

    private void ApplyToolSelection(string tool)
    {
        _activeTool = tool;
        if (IsRecordTool(tool))
            _lastDrawingTool = tool;
        _viewport.SetTool(ViewportToolName(tool));
        foreach (var (t, btn) in _toolBtns)
            btn.IsChecked = t == tool;
        UpdateAnnotationMenuButton();
        UpdateRecordButton();
        UpdateToolStatus();
    }

    private void SyncToolButtonsToActiveTool()
    {
        foreach (var (t, btn) in _toolBtns)
            btn.IsChecked = t == _activeTool;
        UpdateAnnotationMenuButton();
        UpdateRecordButton();
        UpdateToolStatus();
    }

    private void BtnAnnotationMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = BtnAnnotationMenu,
            Placement = PlacementMode.Bottom,
        };

        AddAnnotationToolItem(menu, "Draw line", "drawline");
        AddAnnotationToolItem(menu, "Arrow", "drawarrow");
        AddAnnotationToolItem(menu, "Box", "drawrect");
        AddAnnotationToolItem(menu, "Cloud", "drawcloud");
        AddAnnotationToolItem(menu, "Area fill", "drawarea");
        AddAnnotationToolItem(menu, "Note", "note");
        menu.Items.Add(new Separator());
        AddAnnotationThicknessMenu(menu);
        AddAnnotationColorMenu(menu);

        menu.IsOpen = true;
    }

    private void AddAnnotationToolItem(ContextMenu menu, string label, string tool)
    {
        var item = new MenuItem
        {
            Header = label,
            IsCheckable = true,
            IsChecked = string.Equals(_activeTool, tool, StringComparison.OrdinalIgnoreCase),
        };
        item.Click += (_, _) => SetTool(tool);
        menu.Items.Add(item);
    }

    private void AddAnnotationThicknessMenu(ContextMenu menu)
    {
        var parent = new MenuItem { Header = "Thickness" };
        foreach (double width in new[] { 1.0, 1.8, 3.0, 5.0, 8.0 })
        {
            double selectedWidth = width;
            var child = new MenuItem
            {
                Header = $"{selectedWidth:0.#} px",
                IsCheckable = true,
                IsChecked = Math.Abs(_annotationStrokeWidth - selectedWidth) < 0.01,
            };
            child.Click += (_, _) => SetAnnotationStrokeWidth(selectedWidth);
            parent.Items.Add(child);
        }

        menu.Items.Add(parent);
    }

    private void AddAnnotationColorMenu(ContextMenu menu)
    {
        var parent = new MenuItem { Header = "Color" };
        foreach (var (label, hex) in AnnotationColorPresets())
        {
            string selectedHex = hex;
            var child = new MenuItem
            {
                Header = CreateAnnotationColorHeader(label, hex),
                IsCheckable = true,
                IsChecked = string.Equals(_annotationColor, hex, StringComparison.OrdinalIgnoreCase),
            };
            child.Click += (_, _) => SetAnnotationColor(selectedHex);
            parent.Items.Add(child);
        }

        menu.Items.Add(parent);
    }

    private void SetAnnotationStrokeWidth(double width)
    {
        _annotationStrokeWidth = Math.Clamp(width, 0.75, 12.0);
        _viewport.ActiveAnnotationStrokeWidth = _annotationStrokeWidth;
        UpdateAnnotationMenuButton();
        TxtStatus.Text = $"Annotation thickness: {_annotationStrokeWidth:0.#} px.";
    }

    private void SetAnnotationColor(string color)
    {
        _annotationColor = color;
        _viewport.ActiveAnnotationColor = _annotationColor;
        UpdateAnnotationMenuButton();
        TxtStatus.Text = $"Annotation color: {color}.";
    }

    private void UpdateAnnotationMenuButton()
    {
        string tool = _activeTool switch
        {
            "drawline" => "Draw",
            "drawarrow" => "Arrow",
            "drawrect" => "Box",
            "drawcloud" => "Cloud",
            "drawarea" => "Area",
            "note" => "Note",
            _ => "Annotation",
        };
        BtnAnnotationMenu.Content = tool == "Annotation" ? "Annotation" : $"Annot: {tool}";
        BtnAnnotationMenu.ToolTip = $"Annotation tools. Color {_annotationColor}, thickness {_annotationStrokeWidth:0.#} px.";
    }

    private static IEnumerable<(string Label, string Hex)> AnnotationColorPresets()
    {
        yield return ("Red", "#FF4444");
        yield return ("Blue", "#2196F3");
        yield return ("Green", "#4CAF50");
        yield return ("Orange", "#FF9800");
        yield return ("Purple", "#9C27B0");
        yield return ("Cyan", "#00BCD4");
        yield return ("Yellow", "#FFC107");
        yield return ("Black", "#212121");
    }

    private static FrameworkElement CreateAnnotationColorHeader(string label, string hex)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var swatch = new Border
        {
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 0, 7, 0),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
        };
        panel.Children.Add(swatch);
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private void SyncToolTypeForTakeoffItem(TakeoffItem item)
    {
        string tool = ToolForTakeoffItem(item);
        _lastDrawingTool = tool;

        if (IsRecordTool(_activeTool))
        {
            if (!string.Equals(_activeTool, tool, StringComparison.OrdinalIgnoreCase))
                ApplyToolSelection(tool);
            else
                SyncToolButtonsToActiveTool();
            return;
        }

        UpdateRecordButton();
        UpdateToolStatus();
    }

    private void UpdateRecordButton()
    {
        if (_recordButton == null)
            return;

        bool recording = IsRecordTool(_activeTool);
        string recordType = recording ? RecordToolTitle(_activeTool) : "";
        _updatingRecordButton = true;
        _recordButton.IsChecked = recording;
        _recordButton.Content = recording ? $"Rec {recordType}" : "Record";
        _recordButton.ToolTip = recording
            ? _activeItem == null
                ? $"Recording {recordType}; no active takeoff target is selected."
                : $"Recording {recordType} into {_activeItem.Name}. Click or press Space to stop."
            : "Start recording into the active takeoff target (Space).";
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

    private void BtnPdfSnap_Checked(object sender, RoutedEventArgs e) =>
        SetPdfSnapMode(enabled: true);

    private void BtnPdfSnap_Unchecked(object sender, RoutedEventArgs e) =>
        SetPdfSnapMode(enabled: false);

    private void BtnRulerSheetVisibility_Changed(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_updatingRulerVisibilityButton)
            return;

        if (_currentPage == null)
        {
            ApplyRulerVisibilityToViewport();
            TxtStatus.Text = "Open a sheet before hiding ruler markups.";
            return;
        }

        string pageKey = NormalizePathForCompare(_currentPage.FolderPath);
        bool hidden = BtnRulerSheetVisibility.IsChecked == true;
        if (hidden)
        {
            _pagesWithHiddenRulers.Add(pageKey);
            if (_viewport.HideVisibleRulerAnnotationsOnActivePage())
                SaveCurrentPageAnnotations();
        }
        else
        {
            _pagesWithHiddenRulers.Remove(pageKey);
            if (_viewport.ShowAllRulerAnnotationsOnActivePage())
                SaveCurrentPageAnnotations();
        }

        ApplyRulerVisibilityToViewport();
        TxtStatus.Text = hidden
            ? $"Current ruler markups hidden on {_currentPage.Name}. New ruler markups stay visible until you hide again."
            : $"Ruler markups visible on {_currentPage.Name}.";
    }

    private void ApplyRulerVisibilityToViewport()
    {
        bool hidden = CurrentPageRulersHidden();
        _viewport.HideRulerAnnotations = hidden;
        UpdateRulerVisibilityButton(hidden);
    }

    private bool CurrentPageRulersHidden() =>
        _currentPage != null &&
        _pagesWithHiddenRulers.Contains(NormalizePathForCompare(_currentPage.FolderPath));

    private void UpdateRulerVisibilityButton(bool hidden)
    {
        _updatingRulerVisibilityButton = true;
        try
        {
            BtnRulerSheetVisibility.IsEnabled = _currentPage != null;
            BtnRulerSheetVisibility.IsChecked = hidden;
            BtnRulerSheetVisibility.ToolTip = hidden
                ? "Show all ruler markups on this sheet"
                : "Hide all ruler markups on this sheet";
        }
        finally
        {
            _updatingRulerVisibilityButton = false;
        }
    }

    private void BtnOrtho_Checked(object sender, RoutedEventArgs e) =>
        SetOrthoMode(enabled: true);

    private void BtnOrtho_Unchecked(object sender, RoutedEventArgs e) =>
        SetOrthoMode(enabled: false);

    private void BtnBoxMode_Checked(object sender, RoutedEventArgs e) =>
        SetBoxMode(enabled: true);

    private void BtnBoxMode_Unchecked(object sender, RoutedEventArgs e) =>
        SetBoxMode(enabled: false);

    private void SetSnapMode(bool enabled)
    {
        if (_updatingConstraintButtons)
            return;

        _viewport.SnapEnabled = enabled;
        UpdateConstraintButtons();
        UpdateToolStatus();
    }

    private void SetPdfSnapMode(bool enabled)
    {
        if (_updatingConstraintButtons)
            return;

        _viewport.PdfSnapEnabled = enabled;
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

    private void SetBoxMode(bool enabled)
    {
        if (_updatingConstraintButtons)
            return;

        _viewport.BoxModeEnabled = enabled;
        UpdateConstraintButtons();
        UpdateToolStatus();
    }

    private void OnViewportSnapChanged(bool enabled)
    {
        UpdateConstraintButtons();
        UpdateToolStatus();
    }

    private void OnViewportPdfSnapChanged(bool enabled)
    {
        UpdateConstraintButtons();
        UpdateToolStatus();
    }

    private void OnViewportOrthoChanged(bool enabled)
    {
        UpdateConstraintButtons();
        UpdateToolStatus();
    }

    private void OnViewportBoxModeChanged(bool enabled)
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
            BtnPdfSnap.IsChecked = _viewport.PdfSnapEnabled;
            BtnPdfSnap.Content = _viewport.PdfSnapEnabled ? "PDF Snap On" : "PDF Snap";
            BtnOrtho.IsChecked = _viewport.OrthoEnabled;
            BtnOrtho.Content = _viewport.OrthoEnabled ? "Ortho On" : "Ortho";
            BtnBoxMode.IsChecked = _viewport.BoxModeEnabled;
            BtnBoxMode.Content = _viewport.BoxModeEnabled ? "Box On" : "Box";
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

        string mtype = RecordMeasurementType(tool);
        bool joistArea = IsJoistAreaTool(tool);
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
            CanRecordIntoActiveTakeoff(_activeItem, mtype, joistArea))
        {
            _activeItem.MeasurementType = mtype;
            _viewport.ActiveColor = _activeItem.Color;
            _viewport.ActiveTakeoffFolder = _activeItem.FolderPath;
            if (mtype == "point")
                _viewport.ActiveCountSymbol = _activeItem.CountSymbol;
            return true;
        }

        if (!forceNewTakeoff && !ConfirmCreateDrawingTakeoffTarget(mtype, joistArea))
            return false;

        string parentFolder = NewTakeoffItemParentFolder();
        // Every new takeoff gets its own random color not already on the
        // sheet; still editable in the dialog and later via properties.
        string defaultColor = RandomTakeoffColor(_activeItem?.Color ?? _viewport.ActiveColor);
        var dlg = new NewItemDialog(
            mtype,
            joistArea ? DefaultJoistAreaTakeoffNameForFolder(parentFolder) : DefaultTakeoffNameForFolder(mtype, parentFolder),
            lockType: true,
            defaultColor: defaultColor,
            defaultCountSymbol: _newCountSymbol)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true)
            return false;

        if (mtype == "point")
        {
            _newCountSymbol = CountDisplaySymbol.Normalize(dlg.ItemCountSymbol);
            _viewport.ActiveCountSymbol = _newCountSymbol;
            UpdateDefaultCountSymbolButton();
        }

        var newItem = CreateUniqueTakeoffItem(dlg.ItemName, dlg.ItemColor, mtype, parentFolder);
        ApplyTakeoffFolderDefaultsToNewItem(newItem, parentFolder);
        ApplyNewCountSymbolToItemIfNeeded(newItem, mtype);
        if (joistArea)
            ApplyDefaultJoistAreaSettings(newItem);
        _takeoffItems.Add(newItem);
        var treeParent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
        var tvi = AddTakeoffTreeItem(newItem, treeParent);
        if (treeParent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;

        _activeItem = newItem;
        _activeTakeoffParentFolder = parentFolder;
        _viewport.ActiveColor = newItem.Color;
        _viewport.ActiveTakeoffFolder = newItem.FolderPath;
        _viewport.ActiveCountSymbol = newItem.CountSymbol;
        tvi.IsSelected = true;
        UpdateToolStatus();
        RefreshActiveTakeoffVisuals();
        UpdateTotalDisplay();
        return true;
    }

    private static bool IsRecordTool(string tool) =>
        tool is "point" or "line" or "area" or "joistarea";

    private static bool IsJoistAreaTool(string tool) =>
        string.Equals(tool, "joistarea", StringComparison.OrdinalIgnoreCase);

    private static string RecordMeasurementType(string tool) =>
        IsJoistAreaTool(tool)
            ? "area"
            : OurPlaneCoreJobStore.NormalizeMeasurementType(tool);

    private static string ViewportToolName(string tool) =>
        IsJoistAreaTool(tool) ? "area" : tool;

    private static string RecordToolTitle(string tool) =>
        IsJoistAreaTool(tool) ? "J Area" : MeasurementTypeTitle(tool);

    private static string ToolForTakeoffItem(TakeoffItem item) =>
        item.IsJoistArea
            ? "joistarea"
            : OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);

    private string RecordToolForActiveTakeoff()
    {
        if (_activeItem != null)
            return ToolForTakeoffItem(_activeItem);

        return IsRecordTool(_lastDrawingTool) ? _lastDrawingTool : "point";
    }

    private static bool CanRecordIntoActiveTakeoff(TakeoffItem item, string measurementType, bool requiresJoistArea)
    {
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) != measurementType)
            return false;

        return !requiresJoistArea || item.IsJoistArea;
    }

    private void ApplyDefaultJoistAreaSettings(TakeoffItem item)
    {
        JoistTakeoffDefaults.ApplyToNewJoistArea(item);
        OurPlaneCoreJobStore.SaveTakeoffItem(item);
    }

    private string DefaultJoistAreaTakeoffNameForFolder(string parentFolder)
    {
        string baseName = _currentPage != null ? $"{_currentPage.Name} J Area" : "J Area Item";
        string prefix = ResolveTakeoffFolderDefaultNamePrefix(parentFolder);
        if (string.IsNullOrWhiteSpace(prefix) ||
            baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return baseName;
        }

        return prefix.EndsWith(" ", StringComparison.Ordinal) ||
               prefix.EndsWith("-", StringComparison.Ordinal) ||
               prefix.EndsWith("_", StringComparison.Ordinal)
            ? prefix + baseName
            : $"{prefix} {baseName}";
    }

    private bool ConfirmCreateDrawingTakeoffTarget(string measurementType, bool joistArea)
    {
        string targetType = joistArea ? "J Area" : MeasurementTypeTitle(measurementType);
        string message = _activeItem == null
            ? $"No active takeoff target is selected.\n\nCreate a {targetType} takeoff item before recording?"
            : $"Active target is {_activeItem.Name} ({TakeoffTypeTitle(_activeItem)}).\n\n{targetType} recording needs a {targetType} takeoff item. Create a separate target?";

        return MessageBox.Show(
            message,
            "Create Takeoff Target",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void BtnMirrorHorizontal_Click(object sender, RoutedEventArgs e)
    {
        if (_viewport == null)
            return;

        if (_viewport.MirrorSelectedHorizontal())
            ResetTransformEditSliders();
        UpdateTransformEditControls();
    }

    private void BtnMirrorVertical_Click(object sender, RoutedEventArgs e)
    {
        if (_viewport == null)
            return;

        if (_viewport.MirrorSelectedVertical())
            ResetTransformEditSliders();
        UpdateTransformEditControls();
    }

    private void SliderRotateSelection_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingTransformSliders || _viewport == null)
            return;

        double delta = e.NewValue - _lastTransformRotateSliderValue;
        if (Math.Abs(delta) < 0.001)
            return;

        if (_viewport.RotateSelectedBy(delta))
        {
            _lastTransformRotateSliderValue = e.NewValue;
        }
        else
        {
            ResetTransformEditSliders();
        }

        UpdateTransformEditControls();
    }

    private void SliderScaleSelection_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingTransformSliders || _viewport == null)
            return;

        double previous = Math.Max(0.05, _lastTransformScaleSliderValue);
        double factor = e.NewValue / previous;
        if (Math.Abs(factor - 1.0) < 0.0001)
            return;

        if (_viewport.ScaleSelectedBy(factor))
        {
            _lastTransformScaleSliderValue = e.NewValue;
        }
        else
        {
            ResetTransformEditSliders();
        }

        UpdateTransformEditControls();
    }

    private void BtnResetRotateSelection_Click(object sender, RoutedEventArgs e)
    {
        SetTransformRotateSlider(0);
        UpdateTransformEditControls();
    }

    private void BtnResetScaleSelection_Click(object sender, RoutedEventArgs e)
    {
        SetTransformScaleSlider(1);
        UpdateTransformEditControls();
    }

    private void OnViewportTransformSelectionChanged(bool hasSelection) =>
        UpdateTransformEditControls(hasSelection);

    private void UpdateTransformEditControls(bool? hasSelection = null)
    {
        if (_viewport == null)
            return;

        bool enabled = hasSelection ?? _viewport.HasTransformSelection;
        BtnMirrorHorizontal.IsEnabled = enabled;
        BtnMirrorVertical.IsEnabled = enabled;
        SliderRotateSelection.IsEnabled = enabled;
        SliderScaleSelection.IsEnabled = enabled;
        BtnResetRotateSelection.IsEnabled = enabled;
        BtnResetScaleSelection.IsEnabled = enabled;

        if (!enabled)
            ResetTransformEditSliders();
    }

    private void ResetTransformEditSliders()
    {
        _updatingTransformSliders = true;
        try
        {
            SetTransformRotateSliderCore(0);
            SetTransformScaleSliderCore(1);
        }
        finally
        {
            _updatingTransformSliders = false;
        }
    }

    private void SetTransformRotateSlider(double value)
    {
        _updatingTransformSliders = true;
        try
        {
            SetTransformRotateSliderCore(value);
        }
        finally
        {
            _updatingTransformSliders = false;
        }
    }

    private void SetTransformScaleSlider(double value)
    {
        _updatingTransformSliders = true;
        try
        {
            SetTransformScaleSliderCore(value);
        }
        finally
        {
            _updatingTransformSliders = false;
        }
    }

    private void SetTransformRotateSliderCore(double value)
    {
        SliderRotateSelection.Value = value;
        _lastTransformRotateSliderValue = value;
    }

    private void SetTransformScaleSliderCore(double value)
    {
        SliderScaleSelection.Value = value;
        _lastTransformScaleSliderValue = value;
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
        if (!PdfSheetMetadataService.TryParseScaleMetersPerPt(TxtScaleRatio.Text, out double scaleMetersPerPt))
        {
            MessageBox.Show("Enter an imperial scale, e.g. 1/8\" = 1'0\".",
                            "Scale", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _viewport.ScaleMetersPerPt = scaleMetersPerPt;
        if (_currentPage != null)
            _currentPage.ScaleMetersPerPt = _viewport.ScaleMetersPerPt;
        ApplyScaleToCurrentPageMeasurements(_viewport.ScaleMetersPerPt);
        SaveCurrentPageScale();
        UpdateScaleUi(_viewport.ScaleMetersPerPt);
        RefreshFloatingPageSetup(_currentPage?.FolderPath);
        TxtStatus.Text = $"Scale set: {PdfSheetMetadataService.FormatImperialScale(_viewport.ScaleMetersPerPt)}";
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
            TxtScaleRatio.Text = label;
            ApplyScaleFromEntry();
        };
        menu.Items.Add(mi);
    }
}
