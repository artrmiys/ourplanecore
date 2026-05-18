using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void ComboDisplayViewportBackground_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings)
            return;

        if (ComboDisplayViewportBackground.SelectedItem is not ComboBoxItem { Tag: string color })
            return;

        ApplyViewportBackground(color, persist: true);
        SyncDisplaySettingsControls();
        TxtStatus.Text = $"Viewport edge background: {ViewportBackgroundLabel(_settings.ViewportBackground)}.";
    }

    private void ComboFolderTemplateMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings)
            return;

        string mode = ComboFolderTemplateMode.SelectedIndex switch
        {
            1 => "COM",
            2 => "EWP",
            _ => "AUTO",
        };
        _settings.FolderTemplateMode = mode;
        SaveAppSettings();
        TxtStatus.Text = $"Folder template mode: {mode}.";
    }

    private void ComboDisplayPageBackground_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings)
            return;

        if (ComboDisplayPageBackground.SelectedItem is not ComboBoxItem { Tag: string color })
            return;

        ApplyPageBackground(color, persist: true);
        SyncDisplaySettingsControls();
        TxtStatus.Text = $"Page paper background: {PageBackgroundLabel(_settings.PageBackground)}.";
    }

    private void BtnDarkTheme_Checked(object sender, RoutedEventArgs e) =>
        ApplyThemeFromToggle(dark: true);

    private void BtnDarkTheme_Unchecked(object sender, RoutedEventArgs e) =>
        ApplyThemeFromToggle(dark: false);

    private void ApplyThemeFromToggle(bool dark)
    {
        if (_isApplyingSettings)
            return;

        ApplyTheme(dark, persist: true);
    }

    private void AddSheetOverlayMenuItems(ContextMenu menu)
    {
        menu.Items.Add(BuildLegendMenuItem());
        menu.Items.Add(BuildLegendPositionMenu());
        menu.Items.Add(BuildOverlaySizeMenu("Legend Size", _settings.SheetLegendScale, SetSheetLegendScale));
        menu.Items.Add(BuildOverlaySizeMenu("Scale / Sheet Size Label Size", _settings.SheetHeaderScale, SetSheetHeaderScale));

        var scaleLegendWithSheet = new MenuItem
        {
            Header = "Scale Legend With Sheet",
            IsCheckable = true,
            IsChecked = _settings.ScaleSheetOverlaysWithPage,
            ToolTip = "When enabled, the sheet legend grows and shrinks with page zoom.",
        };
        scaleLegendWithSheet.Click += (_, _) => SetSheetOverlaysScaleWithPage(scaleLegendWithSheet.IsChecked);
        menu.Items.Add(scaleLegendWithSheet);

        var scaleLabelsWithSheet = new MenuItem
        {
            Header = "Scale Measurement Labels With Sheet",
            IsCheckable = true,
            IsChecked = _settings.ScaleMeasurementLabelsWithPage,
            ToolTip = "When enabled, measurement value labels grow and shrink with page zoom. Off by default - labels stay screen-sized.",
        };
        scaleLabelsWithSheet.Click += (_, _) => SetMeasurementLabelsScaleWithPage(scaleLabelsWithSheet.IsChecked);
        menu.Items.Add(scaleLabelsWithSheet);

        var scaleHeaderWithSheet = new MenuItem
        {
            Header = "Scale Sheet Header With Sheet",
            IsCheckable = true,
            IsChecked = _settings.ScaleSheetHeaderWithPage,
            ToolTip = "When enabled, the top sheet scale/size header grows and shrinks with page zoom. Off by default - header stays screen-sized.",
        };
        scaleHeaderWithSheet.Click += (_, _) => SetSheetHeaderScaleWithPage(scaleHeaderWithSheet.IsChecked);
        menu.Items.Add(scaleHeaderWithSheet);
    }

    private MenuItem BuildLegendMenuItem()
    {
        var item = new MenuItem
        {
            Header = "Legend",
            IsCheckable = true,
            IsChecked = _settings.ShowSheetLegend,
            ToolTip = "Show or hide the active sheet legend overlay",
        };
        item.Click += (_, _) => SetSheetLegendVisible(item.IsChecked);
        return item;
    }

    private MenuItem BuildLegendPositionMenu()
    {
        var menu = new MenuItem
        {
            Header = "Legend Position",
            IsEnabled = _settings.ShowSheetLegend,
        };

        foreach (var (label, anchor) in LegendAnchorOptions())
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = string.Equals(NormalizeSheetLegendAnchor(_settings.SheetLegendAnchor), anchor, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => SetSheetLegendAnchor(anchor);
            menu.Items.Add(item);
        }

        return menu;
    }

    private static IReadOnlyList<(string Label, string Anchor)> LegendAnchorOptions() =>
    [
        ("Top Left", "TopLeft"),
        ("Top Center", "TopCenter"),
        ("Top Right", "TopRight"),
        ("Middle Left", "MiddleLeft"),
        ("Middle Right", "MiddleRight"),
        ("Bottom Left", "BottomLeft"),
        ("Bottom Center", "BottomCenter"),
        ("Bottom Right", "BottomRight"),
    ];

    private MenuItem BuildOverlaySizeMenu(string header, double currentScale, Action<double> apply)
    {
        var menu = new MenuItem { Header = header };
        foreach (var (label, scale) in OverlaySizeOptions())
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = Math.Abs(NormalizeOverlayScale(currentScale) - scale) < 0.001,
            };
            item.Click += (_, _) => apply(scale);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Custom...", true, () =>
            PromptOverlaySize(header, currentScale, apply)));

        return menu;
    }

    private void PromptOverlaySize(string title, double currentScale, Action<double> apply)
    {
        string? raw = ShowInputDialog(
            "Scale multiplier (0.5 - 3.0):",
            NormalizeOverlayScale(currentScale).ToString("0.##", CultureInfo.InvariantCulture),
            title);
        if (raw == null)
            return;

        if (!double.TryParse(raw.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < 0.5 ||
            scale > 3.0)
        {
            MessageBox.Show("Enter a value from 0.5 to 3.0.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        apply(scale);
    }

    private static IReadOnlyList<(string Label, double Scale)> OverlaySizeOptions() =>
    [
        ("Small", 0.75),
        ("Normal", 1.00),
        ("Large", 1.35),
        ("XL", 1.75),
        ("XXL", 2.25),
    ];

    private void DisplaySetting_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
            return;

        _settings.ShowMeasurementLabels = ChkDisplayMeasurementLabels.IsChecked == true;
        _settings.ShowLineLabels = ChkDisplayLineLabels.IsChecked == true;
        _settings.ShowAreaLabels = ChkDisplayAreaLabels.IsChecked == true;
        _settings.ShowJoistLabels = ChkDisplayJoistLabels.IsChecked == true;
        _settings.ShowCountLabels = ChkDisplayCountLabels.IsChecked == true;
        _settings.ShowSheetLegend = ChkDisplayLegend.IsChecked == true;
        _settings.ScaleSheetOverlaysWithPage = ChkDisplayLegendScaleWithPage.IsChecked == true;
        _settings.ScaleMeasurementLabelsWithPage = ChkDisplayLabelsScaleWithPage.IsChecked == true;
        _settings.ScaleSheetHeaderWithPage = ChkDisplayHeaderScaleWithPage.IsChecked == true;
        _settings.SimplifyViewportNavigation = ChkDisplaySimplifyNavigation.IsChecked == true;
        _settings.UnitMode = ChkDisplayImperial.IsChecked == true
            ? UnitMode.Imperial.ToString()
            : UnitMode.Metric.ToString();

        ApplyDisplaySettingsToViewport();
        ApplySheetOverlaySettings();
        SaveAppSettings();
        RefreshAllTotals();
        TxtStatus.Text = "Display settings saved.";
    }

    private void BtnMeasurementLabelSmall_Click(object sender, RoutedEventArgs e) =>
        SetMeasurementLabelScale(0.75);

    private void BtnMeasurementLabelNormal_Click(object sender, RoutedEventArgs e) =>
        SetMeasurementLabelScale(1.00);

    private void BtnMeasurementLabelLarge_Click(object sender, RoutedEventArgs e) =>
        SetMeasurementLabelScale(1.35);

    private void BtnMeasurementLabelApply_Click(object sender, RoutedEventArgs e) =>
        ApplyMeasurementLabelScaleFromText();

    private void TxtMeasurementLabelScale_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyMeasurementLabelScaleFromText();
        e.Handled = true;
    }

    private void TxtMeasurementLabelScale_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyMeasurementLabelScaleFromText();

    private void ApplyMeasurementLabelScaleFromText()
    {
        string raw = TxtMeasurementLabelScale.Text.Trim().Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < 0.50 ||
            scale > 3.00)
        {
            TxtMeasurementLabelScale.Text = _settings.MeasurementLabelScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtStatus.Text = "Value label size must be 0.5 - 3.0.";
            return;
        }

        SetMeasurementLabelScale(scale);
    }

    private void SetMeasurementLabelScale(double scale)
    {
        _settings.MeasurementLabelScale = NormalizeOverlayScale(scale);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
        _viewport.InvalidateVisual();
        TxtStatus.Text = $"Viewport value label size: {_settings.MeasurementLabelScale:0.##}x.";
    }

    private void BtnMeasurementStrokeApply_Click(object sender, RoutedEventArgs e) =>
        ApplyMeasurementStrokeScaleFromText();

    private void TxtMeasurementStrokeScale_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyMeasurementStrokeScaleFromText();
        e.Handled = true;
    }

    private void TxtMeasurementStrokeScale_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyMeasurementStrokeScaleFromText();

    private void ApplyMeasurementStrokeScaleFromText()
    {
        string raw = TxtMeasurementStrokeScale.Text.Trim().Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < 0.25 ||
            scale > 4.00)
        {
            TxtMeasurementStrokeScale.Text = _settings.ViewportMeasurementStrokeScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtStatus.Text = "Viewport line thickness must be 0.25 - 4.0.";
            return;
        }

        SetMeasurementStrokeScale(scale);
    }

    private void SetMeasurementStrokeScale(double scale)
    {
        _settings.ViewportMeasurementStrokeScale = NormalizeStrokeScale(scale);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
        _viewport.InvalidateVisual();
        TxtStatus.Text = $"Viewport line thickness: {_settings.ViewportMeasurementStrokeScale:0.##}x.";
    }

    private void BtnMeasurementPointApply_Click(object sender, RoutedEventArgs e) =>
        ApplyMeasurementPointScaleFromText();

    private void TxtMeasurementPointScale_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyMeasurementPointScaleFromText();
        e.Handled = true;
    }

    private void TxtMeasurementPointScale_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyMeasurementPointScaleFromText();

    private void ApplyMeasurementPointScaleFromText()
    {
        string raw = TxtMeasurementPointScale.Text.Trim().Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < 0.25 ||
            scale > 4.00)
        {
            TxtMeasurementPointScale.Text = _settings.ViewportPointSizeScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtStatus.Text = "Viewport point size must be 0.25 - 4.0.";
            return;
        }

        SetMeasurementPointScale(scale);
    }

    private void SetMeasurementPointScale(double scale)
    {
        _settings.ViewportPointSizeScale = NormalizePointScale(scale);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
        _viewport.InvalidateVisual();
        TxtStatus.Text = $"Viewport point size: {_settings.ViewportPointSizeScale:0.##}x.";
    }

    private void BtnAreaEdgeApply_Click(object sender, RoutedEventArgs e) =>
        ApplyAreaEdgeScaleFromText();

    private void TxtAreaEdgeScale_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyAreaEdgeScaleFromText();
        e.Handled = true;
    }

    private void TxtAreaEdgeScale_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyAreaEdgeScaleFromText();

    private void ApplyAreaEdgeScaleFromText()
    {
        string raw = TxtAreaEdgeScale.Text.Trim().Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < 0.25 ||
            scale > 4.00)
        {
            TxtAreaEdgeScale.Text = _settings.ViewportAreaEdgeScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtStatus.Text = "Area edge thickness must be 0.25 - 4.0.";
            return;
        }

        _settings.ViewportAreaEdgeScale = Math.Clamp(scale, 0.25, 4.0);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
        _viewport.InvalidateVisual();
        TxtStatus.Text = $"Area edge thickness: {_settings.ViewportAreaEdgeScale:0.##}x.";
    }

    private void BtnAreaFillOpacityApply_Click(object sender, RoutedEventArgs e) =>
        ApplyAreaFillOpacityFromText();

    private void TxtAreaFillOpacity_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyAreaFillOpacityFromText();
        e.Handled = true;
    }

    private void TxtAreaFillOpacity_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyAreaFillOpacityFromText();

    private void ApplyAreaFillOpacityFromText()
    {
        string raw = TxtAreaFillOpacity.Text.Trim().TrimEnd('%').Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double percent) ||
            percent < 0.0 ||
            percent > 100.0)
        {
            TxtAreaFillOpacity.Text = Math.Round(_settings.ViewportAreaFillOpacity * 100.0).ToString("0", CultureInfo.InvariantCulture);
            TxtStatus.Text = "Area fill opacity must be 0 - 100 (%).";
            return;
        }

        _settings.ViewportAreaFillOpacity = Math.Clamp(percent / 100.0, 0.0, 1.0);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
        _viewport.InvalidateVisual();
        TxtStatus.Text = $"Area fill opacity: {Math.Round(_settings.ViewportAreaFillOpacity * 100.0):0}%.";
    }

    // ── Ribbon sliders ───────────────────────────────────────────────────
    // ValueChanged applies a live preview without writing settings.json on
    // every tick; the value is persisted once on Slider_CommitSave (mouse-up
    // or key-up).

    private bool _viewportScaleDirty;

    private void SldLineThickness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.ViewportMeasurementStrokeScale = NormalizeStrokeScale(e.NewValue);
        _viewport.MeasurementStrokeScale = _settings.ViewportMeasurementStrokeScale;
        TxtMeasurementStrokeScale.Text = _settings.ViewportMeasurementStrokeScale.ToString("0.##", CultureInfo.InvariantCulture);
        _viewport.InvalidateVisual();
        _viewportScaleDirty = true;
        TxtStatus.Text = $"Viewport line thickness: {_settings.ViewportMeasurementStrokeScale:0.##}x.";
    }

    private void SldPointSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.ViewportPointSizeScale = NormalizePointScale(e.NewValue);
        _viewport.PointSizeScale = _settings.ViewportPointSizeScale;
        TxtMeasurementPointScale.Text = _settings.ViewportPointSizeScale.ToString("0.##", CultureInfo.InvariantCulture);
        _viewport.InvalidateVisual();
        _viewportScaleDirty = true;
        TxtStatus.Text = $"Viewport point size: {_settings.ViewportPointSizeScale:0.##}x.";
    }

    private void SldAreaEdge_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.ViewportAreaEdgeScale = Math.Clamp(e.NewValue, 0.25, 4.0);
        _viewport.AreaEdgeScale = _settings.ViewportAreaEdgeScale;
        TxtAreaEdgeScale.Text = _settings.ViewportAreaEdgeScale.ToString("0.##", CultureInfo.InvariantCulture);
        _viewport.InvalidateVisual();
        _viewportScaleDirty = true;
        TxtStatus.Text = $"Area edge thickness: {_settings.ViewportAreaEdgeScale:0.##}x.";
    }

    private void SldAreaFill_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.ViewportAreaFillOpacity = Math.Clamp(e.NewValue / 100.0, 0.0, 1.0);
        _viewport.AreaFillOpacity = _settings.ViewportAreaFillOpacity;
        TxtAreaFillOpacity.Text = Math.Round(_settings.ViewportAreaFillOpacity * 100.0).ToString("0", CultureInfo.InvariantCulture);
        _viewport.InvalidateVisual();
        _viewportScaleDirty = true;
        TxtStatus.Text = $"Area fill opacity: {Math.Round(_settings.ViewportAreaFillOpacity * 100.0):0}%.";
    }

    private void SldLabelScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.MeasurementLabelScale = NormalizeOverlayScale(e.NewValue);
        _viewport.MeasurementLabelScale = _settings.MeasurementLabelScale;
        TxtMeasurementLabelScale.Text = _settings.MeasurementLabelScale.ToString("0.##", CultureInfo.InvariantCulture);
        _viewport.InvalidateVisual();
        _viewportScaleDirty = true;
        TxtStatus.Text = $"Viewport value label size: {_settings.MeasurementLabelScale:0.##}x.";
    }

    private void Slider_CommitSave(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || !_viewportScaleDirty)
            return;

        _viewportScaleDirty = false;
        AppSettingsStore.NormalizeOutputSettings(_settings);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
    }

    private void BtnLegendSizeMenu_Click(object sender, RoutedEventArgs e)
    {
        ShowOverlaySizePopup(sender, "Legend Size", _settings.SheetLegendScale, SetSheetLegendScale);
    }

    private void BtnScaleHeaderSizeMenu_Click(object sender, RoutedEventArgs e) =>
        ShowOverlaySizePopup(sender, "Header Size", _settings.SheetHeaderScale, SetSheetHeaderScale);

    private void BtnLabelSizePresets_Click(object sender, RoutedEventArgs e) =>
        ShowOverlaySizePopup(sender, "Label Size", _settings.MeasurementLabelScale, SetMeasurementLabelScale);

    private void BtnStrokeSizePresets_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement target)
            return;

        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        foreach (var (label, scale) in StrokeSizeOptions())
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = Math.Abs(NormalizeStrokeScale(_settings.ViewportMeasurementStrokeScale) - scale) < 0.001,
            };
            item.Click += (_, _) => SetMeasurementStrokeScale(scale);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Custom...", true, PromptMeasurementStrokeScale));
        menu.IsOpen = true;
    }

    private void BtnPointSizePresets_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement target)
            return;

        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        foreach (var (label, scale) in PointSizeOptions())
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = Math.Abs(NormalizePointScale(_settings.ViewportPointSizeScale) - scale) < 0.001,
            };
            item.Click += (_, _) => SetMeasurementPointScale(scale);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Custom...", true, PromptMeasurementPointScale));
        menu.IsOpen = true;
    }

    private void PromptMeasurementStrokeScale()
    {
        string? raw = ShowInputDialog(
            "Scale multiplier (0.25 - 4.0):",
            NormalizeStrokeScale(_settings.ViewportMeasurementStrokeScale).ToString("0.##", CultureInfo.InvariantCulture),
            "Line Thickness");
        if (raw == null)
            return;

        if (!double.TryParse(raw.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < 0.25 ||
            scale > 4.0)
        {
            MessageBox.Show("Enter a value from 0.25 to 4.0.", "Line Thickness", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetMeasurementStrokeScale(scale);
    }

    private void PromptMeasurementPointScale()
    {
        string? raw = ShowInputDialog(
            "Scale multiplier (0.25 - 4.0):",
            NormalizePointScale(_settings.ViewportPointSizeScale).ToString("0.##", CultureInfo.InvariantCulture),
            "Point Size");
        if (raw == null)
            return;

        if (!double.TryParse(raw.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < 0.25 ||
            scale > 4.0)
        {
            MessageBox.Show("Enter a value from 0.25 to 4.0.", "Point Size", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetMeasurementPointScale(scale);
    }

    private void ShowOverlaySizePopup(object sender, string title, double currentScale, Action<double> apply)
    {
        if (sender is not UIElement target)
            return;

        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        foreach (var (label, scale) in OverlaySizeOptions())
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = Math.Abs(NormalizeOverlayScale(currentScale) - scale) < 0.001,
            };
            item.Click += (_, _) => apply(scale);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Custom...", true, () =>
            PromptOverlaySize(title, currentScale, apply)));
        menu.IsOpen = true;
    }

    private void BtnLegendPositionMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement target)
            return;

        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        foreach (var (label, anchor) in LegendAnchorOptions())
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = string.Equals(NormalizeSheetLegendAnchor(_settings.SheetLegendAnchor), anchor, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => SetSheetLegendAnchor(anchor);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void SetSheetLegendVisible(bool visible)
    {
        _settings.ShowSheetLegend = visible;
        ApplySheetOverlaySettings();
        SyncDisplaySettingsControls();
        SaveAppSettings();
        TxtStatus.Text = visible ? "Sheet legend shown." : "Sheet legend hidden.";
    }

    private void SetSheetLegendAnchor(string anchor)
    {
        _settings.SheetLegendAnchor = NormalizeSheetLegendAnchor(anchor);
        ApplySheetOverlaySettings();
        SyncDisplaySettingsControls();
        SaveAppSettings();
        TxtStatus.Text = $"Sheet legend position: {LegendAnchorLabel(_settings.SheetLegendAnchor)}.";
    }

    private void SetSheetLegendScale(double scale)
    {
        _settings.SheetLegendScale = NormalizeOverlayScale(scale);
        ApplySheetOverlaySettings();
        SyncDisplaySettingsControls();
        SaveAppSettings();
        TxtStatus.Text = $"Sheet legend size: {_settings.SheetLegendScale:0.##}x.";
    }

    private void SetSheetHeaderScale(double scale)
    {
        _settings.SheetHeaderScale = NormalizeOverlayScale(scale);
        ApplySheetOverlaySettings();
        SyncDisplaySettingsControls();
        SaveAppSettings();
        TxtStatus.Text = $"Scale label size: {_settings.SheetHeaderScale:0.##}x.";
    }

    private void SetSheetOverlaysScaleWithPage(bool enabled)
    {
        _settings.ScaleSheetOverlaysWithPage = enabled;
        ApplySheetOverlaySettings();
        SyncDisplaySettingsControls();
        SaveAppSettings();
        TxtStatus.Text = enabled
            ? "Sheet legend now scales with page zoom."
            : "Sheet legend now stays screen-sized.";
    }

    private void SetMeasurementLabelsScaleWithPage(bool enabled)
    {
        _settings.ScaleMeasurementLabelsWithPage = enabled;
        ApplySheetOverlaySettings();
        SyncDisplaySettingsControls();
        SaveAppSettings();
        TxtStatus.Text = enabled
            ? "Measurement value labels now scale with page zoom."
            : "Measurement value labels now stay screen-sized.";
    }

    private void SetSheetHeaderScaleWithPage(bool enabled)
    {
        _settings.ScaleSheetHeaderWithPage = enabled;
        ApplySheetOverlaySettings();
        SyncDisplaySettingsControls();
        SaveAppSettings();
        TxtStatus.Text = enabled
            ? "Sheet scale/size header now scales with page zoom."
            : "Sheet scale/size header now stays screen-sized.";
    }

    private void ApplyDisplaySettingsToViewport()
    {
        AppSettingsStore.NormalizeOutputSettings(_settings);
        _settings.MeasurementLabelScale = NormalizeOverlayScale(_settings.MeasurementLabelScale);
        _settings.ViewportMeasurementStrokeScale = NormalizeStrokeScale(_settings.ViewportMeasurementStrokeScale);
        _settings.ViewportPointSizeScale = NormalizePointScale(_settings.ViewportPointSizeScale);
        _viewport.ShowMeasurementLabels = _settings.ShowMeasurementLabels;
        _viewport.ShowLineLabels = _settings.ShowLineLabels;
        _viewport.ShowAreaLabels = _settings.ShowAreaLabels;
        _viewport.ShowJoistLabels = _settings.ShowJoistLabels;
        _viewport.ShowCountLabels = _settings.ShowCountLabels;
        _viewport.MeasurementLabelScale = _settings.MeasurementLabelScale;
        _viewport.MeasurementStrokeScale = _settings.ViewportMeasurementStrokeScale;
        _viewport.PointSizeScale = _settings.ViewportPointSizeScale;
        _viewport.AreaEdgeScale = _settings.ViewportAreaEdgeScale;
        _viewport.AreaFillOpacity = _settings.ViewportAreaFillOpacity;
        _viewport.ScaleMeasurementLabelsWithPage = _settings.ScaleMeasurementLabelsWithPage;
        _viewport.ScaleSheetHeaderWithPage = _settings.ScaleSheetHeaderWithPage;
        _viewport.SimplifyNavigationRendering = _settings.SimplifyViewportNavigation;
        _viewport.UnitMode = _settings.UnitMode == UnitMode.Metric.ToString()
            ? UnitMode.Metric
            : UnitMode.Imperial;
        SyncDisplaySettingsControls();
        _viewport.InvalidateVisual();
    }

    private void SyncDisplaySettingsControls()
    {
        bool wasApplying = _isApplyingSettings;
        _isApplyingSettings = true;
        try
        {
            ChkDisplayMeasurementLabels.IsChecked = _settings.ShowMeasurementLabels;
            ChkDisplayLineLabels.IsChecked = _settings.ShowLineLabels;
            ChkDisplayAreaLabels.IsChecked = _settings.ShowAreaLabels;
            ChkDisplayJoistLabels.IsChecked = _settings.ShowJoistLabels;
            ChkDisplayCountLabels.IsChecked = _settings.ShowCountLabels;
            ChkDisplayLegend.IsChecked = _settings.ShowSheetLegend;
            ChkDisplayLegendScaleWithPage.IsChecked = _settings.ScaleSheetOverlaysWithPage;
            ChkDisplayLabelsScaleWithPage.IsChecked = _settings.ScaleMeasurementLabelsWithPage;
            ChkDisplayHeaderScaleWithPage.IsChecked = _settings.ScaleSheetHeaderWithPage;
            ChkDisplayImperial.IsChecked = _viewport.UnitMode == UnitMode.Imperial;
            ChkDisplaySimplifyNavigation.IsChecked = _settings.SimplifyViewportNavigation;
            ComboDisplayViewportBackground.SelectedIndex = ViewportBackgroundSelectedIndex(_settings.ViewportBackground);
            ComboDisplayPageBackground.SelectedIndex = PageBackgroundSelectedIndex(_settings.PageBackground);
            TxtMeasurementLabelScale.Text = _settings.MeasurementLabelScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtMeasurementStrokeScale.Text = _settings.ViewportMeasurementStrokeScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtMeasurementPointScale.Text = _settings.ViewportPointSizeScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtAreaEdgeScale.Text = _settings.ViewportAreaEdgeScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtAreaFillOpacity.Text = Math.Round(_settings.ViewportAreaFillOpacity * 100.0).ToString("0", CultureInfo.InvariantCulture);
            SldLabelScale.Value = _settings.MeasurementLabelScale;
            SldLineThickness.Value = _settings.ViewportMeasurementStrokeScale;
            SldPointSize.Value = _settings.ViewportPointSizeScale;
            SldAreaEdge.Value = _settings.ViewportAreaEdgeScale;
            SldAreaFill.Value = Math.Round(_settings.ViewportAreaFillOpacity * 100.0);
            SyncOutputSettingsControls();
        }
        finally
        {
            _isApplyingSettings = wasApplying;
        }
    }

    private void ApplySheetOverlaySettings()
    {
        _settings.SheetLegendAnchor = NormalizeSheetLegendAnchor(_settings.SheetLegendAnchor);
        _settings.SheetLegendScale = NormalizeOverlayScale(_settings.SheetLegendScale);
        _settings.SheetHeaderScale = NormalizeOverlayScale(_settings.SheetHeaderScale);
        _settings.MeasurementLabelScale = NormalizeOverlayScale(_settings.MeasurementLabelScale);
        AppSettingsStore.NormalizeOutputSettings(_settings);
        _viewport.SheetLegendAnchor = _settings.SheetLegendAnchor;
        _viewport.SheetLegendScale = _settings.SheetLegendScale;
        _viewport.SheetHeaderScale = _settings.SheetHeaderScale;
        _viewport.ScaleSheetOverlaysWithPage = _settings.ScaleSheetOverlaysWithPage;
        _viewport.ScaleMeasurementLabelsWithPage = _settings.ScaleMeasurementLabelsWithPage;
        _viewport.ScaleSheetHeaderWithPage = _settings.ScaleSheetHeaderWithPage;
        _viewport.SimplifyNavigationRendering = _settings.SimplifyViewportNavigation;
        _viewport.MeasurementLabelScale = _settings.MeasurementLabelScale;
        _viewport.ShowMeasurementLabels = _settings.ShowMeasurementLabels;
        _viewport.ShowLineLabels = _settings.ShowLineLabels;
        _viewport.ShowAreaLabels = _settings.ShowAreaLabels;
        _viewport.ShowJoistLabels = _settings.ShowJoistLabels;
        _viewport.ShowCountLabels = _settings.ShowCountLabels;
        _viewport.MeasurementStrokeScale = _settings.ViewportMeasurementStrokeScale;
        _viewport.PointSizeScale = _settings.ViewportPointSizeScale;
        _viewport.AreaEdgeScale = _settings.ViewportAreaEdgeScale;
        _viewport.AreaFillOpacity = _settings.ViewportAreaFillOpacity;
        SyncDisplaySettingsControls();
        RefreshSheetLegend();
        _viewport.InvalidateVisual();
    }

    private static double NormalizeOverlayScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return 1.0;

        return Math.Clamp(scale, 0.50, 3.00);
    }

    private static double NormalizeStrokeScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return 1.0;

        return Math.Clamp(scale, 0.25, 4.00);
    }

    private static double NormalizePointScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return 1.0;

        return Math.Clamp(scale, 0.25, 4.00);
    }

    private static IReadOnlyList<(string Label, double Scale)> StrokeSizeOptions() =>
    [
        ("Thin", 0.75),
        ("Normal", 1.00),
        ("Thick", 1.50),
        ("Heavy", 2.00),
        ("XL", 3.00),
    ];

    private static IReadOnlyList<(string Label, double Scale)> PointSizeOptions() =>
    [
        ("Small", 0.65),
        ("Normal", 1.00),
        ("Large", 1.40),
        ("XL", 2.00),
        ("XXL", 3.00),
    ];

    private static string NormalizeSheetLegendAnchor(string? anchor)
    {
        string clean = (anchor ?? "").Trim();
        return LegendAnchorOptions().Any(option => string.Equals(option.Anchor, clean, StringComparison.OrdinalIgnoreCase))
            ? LegendAnchorOptions().First(option => string.Equals(option.Anchor, clean, StringComparison.OrdinalIgnoreCase)).Anchor
            : "BottomLeft";
    }

    private static string LegendAnchorLabel(string anchor) =>
        LegendAnchorOptions().FirstOrDefault(option => string.Equals(option.Anchor, anchor, StringComparison.OrdinalIgnoreCase)).Label ?? "Bottom Left";

    private static int ViewportBackgroundSelectedIndex(string color)
    {
        string clean = ViewportBackgroundPolicy.NormalizeColor(color);
        for (int i = 0; i < ViewportBackgroundOptions().Count; i++)
        {
            if (string.Equals(ViewportBackgroundOptions()[i].Color, clean, StringComparison.Ordinal))
                return i;
        }

        return 0;
    }

    private static string ViewportBackgroundLabel(string color)
    {
        string clean = ViewportBackgroundPolicy.NormalizeColor(color);
        return ViewportBackgroundOptions()
            .FirstOrDefault(option => string.Equals(option.Color, clean, StringComparison.Ordinal))
            .Label ?? "White";
    }

    private static IReadOnlyList<(string Label, string Color)> ViewportBackgroundOptions() =>
    [
        ("White", "#FFFFFF"),
        ("Gray", "#F2F2F2"),
        ("Dark", "#2B2B2B"),
    ];

    private static int PageBackgroundSelectedIndex(string color)
    {
        string clean = ViewportBackgroundPolicy.NormalizeColor(color);
        for (int i = 0; i < PageBackgroundOptions().Count; i++)
        {
            if (string.Equals(PageBackgroundOptions()[i].Color, clean, StringComparison.Ordinal))
                return i;
        }

        return 0;
    }

    private static string PageBackgroundLabel(string color)
    {
        string clean = ViewportBackgroundPolicy.NormalizeColor(color);
        return PageBackgroundOptions()
            .FirstOrDefault(option => string.Equals(option.Color, clean, StringComparison.Ordinal))
            .Label ?? "White";
    }

    private static IReadOnlyList<(string Label, string Color)> PageBackgroundOptions() =>
    [
        ("White", "#FFFFFF"),
        ("Soft gray", "#F2F2F2"),
        ("Medium gray", "#D8D8D8"),
        ("Dark gray", "#B8B8B8"),
        ("Warm", "#FFF8E8"),
        ("Soft green", "#EFF7ED"),
        ("Black", "#000000"),
    ];
}
