using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OurPlanCore;

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

    private void ComboViewportRenderQuality_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings)
            return;

        string mode = ComboViewportRenderQuality.SelectedIndex switch
        {
            0 => ViewportRenderPolicy.BalancedQualityMode,
            2 => ViewportRenderPolicy.MaxQualityMode,
            _ => ViewportRenderPolicy.HighQualityMode,
        };

        _settings.ViewportRenderQuality = mode;
        ViewportRenderPolicy.ApplyQualityMode(mode);
        _viewport.RefreshRenderQuality();
        RefreshDetachedSheetRenderQuality();
        SaveAppSettings();
        SyncDisplaySettingsControls();
        TxtStatus.Text = $"Viewport render quality: {mode}.";
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

    private void DisplaySetting_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
            return;

        SyncDisplayLabelGroupToggle(sender);
        _settings.ShowMeasurementLabels = ChkDisplayMeasurementLabels.IsChecked == true;
        _settings.ShowLineLabels = ChkDisplayLineLabels.IsChecked == true;
        _settings.ShowAreaLabels = ChkDisplayAreaLabels.IsChecked == true;
        _settings.ShowJoistLabels = ChkDisplayJoistLabels.IsChecked == true;
        _settings.ShowCountLabels = ChkDisplayCountLabels.IsChecked == true;
        NormalizeDisplayLabelGroupSettings();
        _settings.ShowSheetLegend = ChkDisplayLegend.IsChecked == true;
        _settings.ScaleSheetOverlaysWithPage = ChkDisplayLegendScaleWithPage.IsChecked == true;
        _settings.ScaleMeasurementLabelsWithPage = ChkDisplayLabelsScaleWithPage.IsChecked == true;
        _settings.ScaleSheetHeaderWithPage = ChkDisplayHeaderScaleWithPage.IsChecked == true;
        _settings.SimplifyViewportNavigation = ChkDisplaySimplifyNavigation.IsChecked == true;
        _settings.PdfLayersEnabled = ChkDisplayPdfLayers.IsChecked == true;
        bool staticRenderModeChanged =
            _settings.StaticPageRenderEnabled != (ChkDisplayStaticRaster.IsChecked == true);
        _settings.StaticPageRenderEnabled = ChkDisplayStaticRaster.IsChecked == true;
        _settings.BlackVectorOverlayEnabled = ChkDisplayBlackVector.IsChecked == true;
        _settings.UnitMode = ChkDisplayImperial.IsChecked == true
            ? UnitMode.Imperial.ToString()
            : UnitMode.Metric.ToString();

        ApplyDisplaySettingsToViewport();
        // Switching the render mode must re-arm (or tear down) the live sharpening
        // machinery for the page already on screen; a repaint alone won't do it.
        if (staticRenderModeChanged)
        {
            _viewport.RefreshRenderQuality();
            _viewport.RefreshStaticRasterDpi();
            RefreshDetachedSheetRenderQuality();
            RefreshDetachedSheetStaticRasterDpi();
        }
        ApplySheetOverlaySettings();
        RefreshDetachedSheetDisplaySettings();
        SaveAppSettings();
        RefreshAllTotals();
        TxtStatus.Text = "Display settings saved.";
    }

    private void SyncDisplayLabelGroupToggle(object sender)
    {
        if (!ReferenceEquals(sender, ChkDisplayMeasurementLabels) && !IsDisplayLabelTypeToggle(sender))
            return;

        bool wasApplying = _isApplyingSettings;
        _isApplyingSettings = true;
        try
        {
            if (ReferenceEquals(sender, ChkDisplayMeasurementLabels))
            {
                bool showAll = ChkDisplayMeasurementLabels.IsChecked == true;
                ChkDisplayLineLabels.IsChecked = showAll;
                ChkDisplayAreaLabels.IsChecked = showAll;
                ChkDisplayJoistLabels.IsChecked = showAll;
                ChkDisplayCountLabels.IsChecked = showAll;
                return;
            }

            ChkDisplayMeasurementLabels.IsChecked =
                ChkDisplayLineLabels.IsChecked == true &&
                ChkDisplayAreaLabels.IsChecked == true &&
                ChkDisplayJoistLabels.IsChecked == true &&
                ChkDisplayCountLabels.IsChecked == true;
        }
        finally
        {
            _isApplyingSettings = wasApplying;
        }
    }

    private bool IsDisplayLabelTypeToggle(object sender) =>
        ReferenceEquals(sender, ChkDisplayLineLabels) ||
        ReferenceEquals(sender, ChkDisplayAreaLabels) ||
        ReferenceEquals(sender, ChkDisplayJoistLabels) ||
        ReferenceEquals(sender, ChkDisplayCountLabels);

    private void NormalizeDisplayLabelGroupSettings()
    {
        if (_settings.ShowMeasurementLabels)
        {
            _settings.ShowLineLabels = true;
            _settings.ShowAreaLabels = true;
            _settings.ShowJoistLabels = true;
            _settings.ShowCountLabels = true;
            return;
        }

        _settings.ShowMeasurementLabels =
            _settings.ShowLineLabels &&
            _settings.ShowAreaLabels &&
            _settings.ShowJoistLabels &&
            _settings.ShowCountLabels;
    }

    private void RefreshDetachedSheetDisplaySettings()
    {
        if (_currentJob == null || _detachedSheetWindows.Count == 0)
            return;

        UnitMode unitMode = _settings.UnitMode == UnitMode.Metric.ToString()
            ? UnitMode.Metric
            : UnitMode.Imperial;
        foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
            RefreshDetachedTakeoffDisplay(window, unitMode);
    }

    private void ApplyDisplaySettingsToViewport()
    {
        AppSettingsStore.NormalizeOutputSettings(_settings);
        NormalizeDisplayLabelGroupSettings();
        ViewportRenderPolicy.ApplyQualityMode(_settings.ViewportRenderQuality);
        PdfLayerRenderService.PdfLayersEnabled =
            IsModuleEnabled(ModuleId.PdfLayers) && _settings.PdfLayersEnabled;
        ViewportRenderPolicy.StaticRasterModeEnabled = _settings.StaticPageRenderEnabled;
        ViewportRenderPolicy.StaticRasterTargetDpi = _settings.StaticPageRenderDpi;
        _viewport.ShowBlackVectorOverlay = _settings.BlackVectorOverlayEnabled;
        _settings.MeasurementLabelScale = NormalizeOverlayScale(_settings.MeasurementLabelScale);
        _settings.ViewportMeasurementStrokeScale = NormalizeStrokeScale(_settings.ViewportMeasurementStrokeScale);
        _settings.ViewportRulerStrokeWidth = NormalizeRulerStrokeWidth(_settings.ViewportRulerStrokeWidth);
        _settings.ViewportPdfSnapBridgeTolerancePx = NormalizePdfSnapBridgeTolerance(_settings.ViewportPdfSnapBridgeTolerancePx);
        _settings.ViewportPointSizeScale = NormalizePointScale(_settings.ViewportPointSizeScale);
        _settings.ViewportZoomWheelFactor = NormalizeZoomWheelFactor(_settings.ViewportZoomWheelFactor);
        _viewport.ShowMeasurementLabels = _settings.ShowMeasurementLabels;
        _viewport.ShowLineLabels = _settings.ShowLineLabels;
        _viewport.ShowAreaLabels = _settings.ShowAreaLabels;
        _viewport.ShowJoistLabels = _settings.ShowJoistLabels;
        _viewport.ShowCountLabels = _settings.ShowCountLabels;
        _viewport.MeasurementLabelScale = _settings.MeasurementLabelScale;
        _viewport.MeasurementStrokeScale = _settings.ViewportMeasurementStrokeScale;
        _viewport.RulerStrokeWidth = _settings.ViewportRulerStrokeWidth;
        _viewport.LiveInputLabelSizePx = _settings.ViewportLiveInputLabelSizePx;
        _viewport.LiveInputLabelOpacity = _settings.ViewportLiveInputLabelOpacity;
        _viewport.PdfSnapBridgeToleranceScreenPx = _settings.ViewportPdfSnapBridgeTolerancePx;
        _viewport.PointSizeScale = _settings.ViewportPointSizeScale;
        _viewport.ZoomWheelFactor = _settings.ViewportZoomWheelFactor;
        _viewport.AreaEdgeScale = _settings.ViewportAreaEdgeScale;
        _viewport.AreaFillOpacity = _settings.ViewportAreaFillOpacity;
        _viewport.ExtraJoistGlowIntensity = _settings.ExtraJoistGlowIntensity;
        _viewport.ScaleMeasurementLabelsWithPage = _settings.ScaleMeasurementLabelsWithPage;
        _viewport.ScaleSheetHeaderWithPage = _settings.ScaleSheetHeaderWithPage;
        _viewport.SimplifyNavigationRendering = _settings.SimplifyViewportNavigation;
        _viewport.UnitMode = _settings.UnitMode == UnitMode.Metric.ToString()
            ? UnitMode.Metric
            : UnitMode.Imperial;
        SyncDisplaySettingsControls();
        _viewport.InvalidateVisual();
        RefreshDetachedSheetDisplaySettings();
    }

    private void SyncDisplaySettingsControls()
    {
        bool wasApplying = _isApplyingSettings;
        _isApplyingSettings = true;
        try
        {
            NormalizeDisplayLabelGroupSettings();
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
            ChkDisplayPdfLayers.IsChecked = _settings.PdfLayersEnabled;
            ChkDisplayStaticRaster.IsChecked = _settings.StaticPageRenderEnabled;
            ChkDisplayBlackVector.IsChecked = _settings.BlackVectorOverlayEnabled;
            TxtStaticRasterDpi.Text = _settings.StaticPageRenderDpi.ToString(CultureInfo.InvariantCulture);
            ComboViewportRenderQuality.SelectedIndex = ViewportRenderQualitySelectedIndex(_settings.ViewportRenderQuality);
            ComboDisplayViewportBackground.SelectedIndex = ViewportBackgroundSelectedIndex(_settings.ViewportBackground);
            ComboDisplayPageBackground.SelectedIndex = PageBackgroundSelectedIndex(_settings.PageBackground);
            TxtMeasurementLabelScale.Text = _settings.MeasurementLabelScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtMeasurementStrokeScale.Text = _settings.ViewportMeasurementStrokeScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtRulerStrokeWidth.Text = _settings.ViewportRulerStrokeWidth.ToString("0.##", CultureInfo.InvariantCulture);
            ViewportLiveInputSettings.SizeText = _settings.ViewportLiveInputLabelSizePx.ToString("0.#", CultureInfo.InvariantCulture);
            ViewportLiveInputSettings.OpacityText = (_settings.ViewportLiveInputLabelOpacity * 100.0).ToString("0", CultureInfo.InvariantCulture);
            TxtPdfSnapBridgeTolerance.Text = _settings.ViewportPdfSnapBridgeTolerancePx.ToString("0.#", CultureInfo.InvariantCulture);
            TxtMeasurementPointScale.Text = _settings.ViewportPointSizeScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtZoomWheelFactor.Text = _settings.ViewportZoomWheelFactor.ToString("0.##", CultureInfo.InvariantCulture);
            TxtAreaEdgeScale.Text = _settings.ViewportAreaEdgeScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtAreaFillOpacity.Text = Math.Round(_settings.ViewportAreaFillOpacity * 100.0).ToString("0", CultureInfo.InvariantCulture);
            SyncExtraJoistGlowControls();
            SldLabelScale.Value = _settings.MeasurementLabelScale;
            SldLineThickness.Value = _settings.ViewportMeasurementStrokeScale;
            SldRulerThickness.Value = _settings.ViewportRulerStrokeWidth;
            ViewportLiveInputSettings.SizeValue = _settings.ViewportLiveInputLabelSizePx;
            ViewportLiveInputSettings.OpacityValue = _settings.ViewportLiveInputLabelOpacity * 100.0;
            SldPdfSnapBridgeTolerance.Value = _settings.ViewportPdfSnapBridgeTolerancePx;
            SldPointSize.Value = _settings.ViewportPointSizeScale;
            SldZoomWheelFactor.Value = _settings.ViewportZoomWheelFactor;
            SldAreaEdge.Value = _settings.ViewportAreaEdgeScale;
            SldAreaFill.Value = Math.Round(_settings.ViewportAreaFillOpacity * 100.0);
            SyncOutputSettingsControls();
            SyncDefaultsZoomControl();
        }
        finally
        {
            _isApplyingSettings = wasApplying;
        }
    }

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

    private static int ViewportRenderQualitySelectedIndex(string mode) =>
        ViewportRenderPolicy.NormalizeQualityMode(mode) switch
        {
            ViewportRenderPolicy.BalancedQualityMode => 0,
            ViewportRenderPolicy.MaxQualityMode => 2,
            _ => 1,
        };

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
