using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OurPlanCore;

public partial class MainWindow
{
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
            PostStatusWarning("Enter an overlay size value from 0.5 to 3.0.");
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

    private void ApplySheetOverlaySettings()
    {
        _settings.SheetLegendAnchor = NormalizeSheetLegendAnchor(_settings.SheetLegendAnchor);
        _settings.SheetLegendScale = NormalizeOverlayScale(_settings.SheetLegendScale);
        _settings.SheetHeaderScale = NormalizeOverlayScale(_settings.SheetHeaderScale);
        _settings.MeasurementLabelScale = NormalizeOverlayScale(_settings.MeasurementLabelScale);
        AppSettingsStore.NormalizeOutputSettings(_settings);
        ViewportRenderPolicy.ApplyQualityMode(_settings.ViewportRenderQuality);
        PdfLayerRenderService.PdfLayersEnabled =
            IsModuleEnabled(ModuleId.PdfLayers) && _settings.PdfLayersEnabled;
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
