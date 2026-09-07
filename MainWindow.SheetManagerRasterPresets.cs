using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private int _sheetManagerLastRasterDpi = SheetManagerAutoRasterDpi;

    private void RefreshSheetManagerRasterPresetButtons()
    {
        if (SheetManagerRasterPresetItems == null)
            return;

        SheetManagerRasterPresetItems.ItemsSource = RasterDpiPresetService.Active.Presets;
        UpdateSheetManagerRasterSelectionUi();
    }

    private void SheetManagerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSheetManagerRasterSelectionUi();

    private void UpdateSheetManagerRasterSelectionUi()
    {
        if (SheetManagerGrid == null)
            return;

        int count = SheetManagerGrid.SelectedItems.Count;
        bool hasSelection = count > 0;
        if (TxtSheetManagerRasterSelection != null)
            TxtSheetManagerRasterSelection.Text = $"Selected: {count}";
        if (SheetManagerRasterPresetControls != null)
            SheetManagerRasterPresetControls.IsEnabled =
                hasSelection && _sheetManagerRasterPrepareCts == null;
        if (SheetManagerRasterOptionsButton != null)
            SheetManagerRasterOptionsButton.IsEnabled = hasSelection;
        if (!hasSelection && SheetManagerRasterOptionsPopup != null)
            SheetManagerRasterOptionsPopup.IsOpen = false;
    }

    private void BtnSheetManagerRasterOptions_Click(object sender, RoutedEventArgs e)
    {
        if (SheetManagerGrid.SelectedItems.Count == 0)
        {
            UpdateSheetManagerRasterSelectionUi();
            return;
        }

        SheetManagerRasterOptionsPopup.IsOpen = !SheetManagerRasterOptionsPopup.IsOpen;
    }

    private async void BtnSheetManagerRasterPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;
        if (TryBlockSheetManagerRasterCommandDuringPrepare("changing the raster DPI"))
            return;

        var pages = SelectedSheetManagerPages();
        if (pages.Count == 0)
        {
            UpdateSheetManagerRasterSelectionUi();
            TxtStatus.Text = "Sheet Manager Raster: select one or more sheets.";
            return;
        }

        string value = button.Tag?.ToString()?.Trim() ?? "";
        if (string.Equals(value, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            await RunSheetManagerRasterPresetAsync(
                () => SetSheetManagerRasterEnabledAsync(pages, enabled: false),
                "PDF");
            return;
        }

        string rasterFormat = SelectedSheetManagerRasterFormat();
        if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
        {
            _sheetManagerLastRasterDpi = SheetManagerAutoRasterDpi;
            await RunSheetManagerRasterPresetAsync(
                () => SetSheetManagerRasterEnabledAsync(
                    pages,
                    enabled: true,
                    SheetManagerAutoRasterDpi,
                    rasterFormat,
                    pinRequestedDpi: false),
                "Auto");
            return;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dpi) ||
            RasterSheetCacheService.NormalizePinnedRasterDpi(dpi) == 0)
        {
            TxtStatus.Text = $"Sheet Manager Raster: invalid DPI preset '{value}'.";
            return;
        }

        _sheetManagerLastRasterDpi = dpi;
        await RunSheetManagerRasterPresetAsync(
            () => SetSheetManagerRasterEnabledAsync(
                pages,
                enabled: true,
                dpi,
                rasterFormat,
                pinRequestedDpi: true),
            $"{dpi.ToString(CultureInfo.InvariantCulture)} DPI");
    }

    private async Task RunSheetManagerRasterPresetAsync(Func<Task> action, string label)
    {
        await RunAsyncUiHandler(
            action,
            $"{label} raster action failed.",
            "Sheet Manager Raster");
    }

}
