using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OurPlanCore;

public partial class MainWindow
{
    private RasterDpiPresetConfig _rasterDpiPresetConfig = RasterDpiPresetConfig.BuildDefault();
    private TextBox? _defaultsRasterDpiPresetsBox;
    private TextBlock? _defaultsRasterDpiPresetsStatus;

    private void AppendRasterDpiPresetSettings(Panel root)
    {
        root.Children.Add(Header("Sheet Manager raster DPI presets"));
        root.Children.Add(new TextBlock
        {
            Text = "Edit the numeric DPI buttons shown in Sheet Manager. Use 72-400 DPI, "
                 + "separated by commas or spaces. PDF and Auto are always available.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
        });

        _defaultsRasterDpiPresetsBox = new TextBox
        {
            MinWidth = 330,
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
            ToolTip = "Example: 72, 100, 150, 200, 300, 400",
        };
        root.Children.Add(_defaultsRasterDpiPresetsBox);

        var actions = HBar();
        actions.Children.Add(MgrButton("Reset", (_, _) =>
        {
            _rasterDpiPresetConfig = RasterDpiPresetConfig.BuildDefault();
            BindRasterDpiPresetSettings();
        }));
        actions.Children.Add(MgrButton("Save global default", (_, _) =>
            SaveRasterDpiPresetSettings(saveForJob: false)));
        actions.Children.Add(MgrButton("Save as this job", (_, _) =>
            SaveRasterDpiPresetSettings(saveForJob: true)));
        actions.Children.Add(MgrButton("Apply", (_, _) =>
            ApplyRasterDpiPresetSettings(), primary: true));
        root.Children.Add(actions);

        _defaultsRasterDpiPresetsStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
        };
        root.Children.Add(_defaultsRasterDpiPresetsStatus);
        BindRasterDpiPresetSettings();
    }

    private void BindRasterDpiPresetSettings()
    {
        if (_defaultsRasterDpiPresetsBox == null)
            return;

        _defaultsRasterDpiPresetsBox.Text = string.Join(
            ", ",
            _rasterDpiPresetConfig.Presets.Select(
                dpi => dpi.ToString(CultureInfo.InvariantCulture)));
    }

    private bool TryReadRasterDpiPresetSettings(out RasterDpiPresetConfig config)
    {
        config = RasterDpiPresetConfig.BuildDefault();
        string text = _defaultsRasterDpiPresetsBox?.Text ?? "";
        string[] tokens = text.Split(
            [',', ';', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            SetRasterDpiPresetStatus("Enter at least one DPI preset.");
            return false;
        }

        var values = new List<int>();
        foreach (string token in tokens)
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dpi) ||
                dpi is < 72 or > RasterSheetCacheService.MaxRasterDpi)
            {
                SetRasterDpiPresetStatus(
                    $"'{token}' is not a valid DPI. Use whole numbers from 72 to {RasterSheetCacheService.MaxRasterDpi}.");
                return false;
            }

            if (!values.Contains(dpi))
                values.Add(dpi);
        }

        if (values.Count > 12)
        {
            SetRasterDpiPresetStatus("Use no more than 12 DPI presets.");
            return false;
        }

        config = new RasterDpiPresetConfig { Presets = values };
        return true;
    }

    private void ApplyRasterDpiPresetSettings()
    {
        if (!TryReadRasterDpiPresetSettings(out RasterDpiPresetConfig config))
            return;

        _rasterDpiPresetConfig = config.Clone();
        RasterDpiPresetService.Install(config);
        RefreshSheetManagerRasterPresetButtons();
        BindRasterDpiPresetSettings();
        SetRasterDpiPresetStatus("Applied to the current Sheet Manager.");
    }

    private void SaveRasterDpiPresetSettings(bool saveForJob)
    {
        if (!TryReadRasterDpiPresetSettings(out RasterDpiPresetConfig config))
            return;
        if (saveForJob && _currentJob == null)
        {
            SetRasterDpiPresetStatus("Open a job to save a per-job override.");
            return;
        }

        try
        {
            if (saveForJob)
                SettingsPresetStore.SaveJobRasterDpiPresetOverride(_currentJob!, config);
            else
                SettingsPresetStore.SaveGlobalRasterDpiPresets(config);

            RasterDpiPresetConfig resolved =
                SettingsPresetStore.ResolveRasterDpiPresets(_currentJob);
            _rasterDpiPresetConfig = resolved.Clone();
            RasterDpiPresetService.Install(resolved);
            RefreshSheetManagerRasterPresetButtons();
            BindRasterDpiPresetSettings();
            SetRasterDpiPresetStatus(
                saveForJob
                    ? "Saved as this job's DPI presets and applied."
                    : _currentJob != null &&
                      SettingsPresetStore.LoadJobRasterDpiPresetOverride(_currentJob) != null
                        ? "Saved global DPI presets; this job's override remains applied."
                        : "Saved as global DPI presets and applied.");
        }
        catch (Exception ex)
        {
            ShowOperationError("Raster DPI Presets", ex);
        }
    }

    private void SetRasterDpiPresetStatus(string text)
    {
        if (_defaultsRasterDpiPresetsStatus != null)
            _defaultsRasterDpiPresetsStatus.Text = text;
        TxtStatus.Text = text;
    }
}
