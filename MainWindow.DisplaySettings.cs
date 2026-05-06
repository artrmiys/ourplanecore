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
    private void BtnViewportBg_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        AddViewportBgItem(menu, "White", "#FFFFFF");
        AddViewportBgItem(menu, "Light gray", "#F2F2F2");
        AddViewportBgItem(menu, "Warm paper", "#FFF8E8");
        AddViewportBgItem(menu, "Dark gray", "#2B2B2B");
        menu.IsOpen = true;
    }

    private void AddViewportBgItem(ContextMenu menu, string label, string color)
    {
        var mi = new MenuItem { Header = label, IsCheckable = true, IsChecked = _settings.ViewportBackground == color };
        mi.Click += (_, _) => ApplyViewportBackground(color, persist: true);
        menu.Items.Add(mi);
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

    private void BtnLegendSizeMenu_Click(object sender, RoutedEventArgs e)
    {
        ShowOverlaySizePopup(sender, "Legend Size", _settings.SheetLegendScale, SetSheetLegendScale);
    }

    private void BtnScaleHeaderSizeMenu_Click(object sender, RoutedEventArgs e) =>
        ShowOverlaySizePopup(sender, "Header Size", _settings.SheetHeaderScale, SetSheetHeaderScale);

    private void BtnLabelSizePresets_Click(object sender, RoutedEventArgs e) =>
        ShowOverlaySizePopup(sender, "Label Size", _settings.MeasurementLabelScale, SetMeasurementLabelScale);

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
        _settings.MeasurementLabelScale = NormalizeOverlayScale(_settings.MeasurementLabelScale);
        _viewport.ShowMeasurementLabels = _settings.ShowMeasurementLabels;
        _viewport.ShowLineLabels = _settings.ShowLineLabels;
        _viewport.ShowAreaLabels = _settings.ShowAreaLabels;
        _viewport.ShowCountLabels = _settings.ShowCountLabels;
        _viewport.MeasurementLabelScale = _settings.MeasurementLabelScale;
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
            ChkDisplayCountLabels.IsChecked = _settings.ShowCountLabels;
            ChkDisplayLegend.IsChecked = _settings.ShowSheetLegend;
            ChkDisplayLegendScaleWithPage.IsChecked = _settings.ScaleSheetOverlaysWithPage;
            ChkDisplayLabelsScaleWithPage.IsChecked = _settings.ScaleMeasurementLabelsWithPage;
            ChkDisplayHeaderScaleWithPage.IsChecked = _settings.ScaleSheetHeaderWithPage;
            ChkDisplayImperial.IsChecked = _viewport.UnitMode == UnitMode.Imperial;
            ChkDisplaySimplifyNavigation.IsChecked = _settings.SimplifyViewportNavigation;
            TxtMeasurementLabelScale.Text = _settings.MeasurementLabelScale.ToString("0.##", CultureInfo.InvariantCulture);
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
        _viewport.ShowCountLabels = _settings.ShowCountLabels;
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

    private static string NormalizeSheetLegendAnchor(string? anchor)
    {
        string clean = (anchor ?? "").Trim();
        return LegendAnchorOptions().Any(option => string.Equals(option.Anchor, clean, StringComparison.OrdinalIgnoreCase))
            ? LegendAnchorOptions().First(option => string.Equals(option.Anchor, clean, StringComparison.OrdinalIgnoreCase)).Anchor
            : "BottomLeft";
    }

    private static string LegendAnchorLabel(string anchor) =>
        LegendAnchorOptions().FirstOrDefault(option => string.Equals(option.Anchor, anchor, StringComparison.OrdinalIgnoreCase)).Label ?? "Bottom Left";
}
