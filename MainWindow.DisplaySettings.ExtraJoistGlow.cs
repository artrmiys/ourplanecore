using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OurPlanCore;

public partial class MainWindow
{
    private Slider? _sldExtraJoistGlow;
    private TextBox? _txtExtraJoistGlowIntensity;

    private void InstallExtraJoistGlowSettings()
    {
        if (_sldExtraJoistGlow != null || _txtExtraJoistGlowIntensity != null)
            return;

        _sldExtraJoistGlow = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            SmallChange = 5,
            LargeChange = 10,
            Width = 76,
            Style = TryFindResource("RibbonSlider") as Style,
            ToolTip = "Extra Joist glow intensity 0 - 100 %",
        };
        _sldExtraJoistGlow.ValueChanged += SldExtraJoistGlow_ValueChanged;
        _sldExtraJoistGlow.PreviewMouseUp += Slider_CommitSave;
        _sldExtraJoistGlow.KeyUp += Slider_CommitSave;

        _txtExtraJoistGlowIntensity = new TextBox
        {
            Style = TryFindResource("RibbonNumericValue") as Style,
            ToolTip = "Type Extra Joist glow intensity 0 - 100 % and press Enter",
        };
        _txtExtraJoistGlowIntensity.KeyDown += TxtExtraJoistGlowIntensity_KeyDown;
        _txtExtraJoistGlowIntensity.LostFocus += TxtExtraJoistGlowIntensity_LostFocus;

        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock
        {
            Text = "Extra",
            Style = TryFindResource("RibbonRowLabel") as Style,
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(_sldExtraJoistGlow, 1);
        Grid.SetColumn(_txtExtraJoistGlowIntensity, 2);
        row.Children.Add(label);
        row.Children.Add(_sldExtraJoistGlow);
        row.Children.Add(_txtExtraJoistGlowIntensity);
        ViewportExtraJoistSettingsHost.Children.Add(row);

        SyncExtraJoistGlowControls();
    }

    private void TxtExtraJoistGlowIntensity_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyExtraJoistGlowIntensityFromText();
        e.Handled = true;
    }

    private void TxtExtraJoistGlowIntensity_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyExtraJoistGlowIntensityFromText();

    private void ApplyExtraJoistGlowIntensityFromText()
    {
        if (_isApplyingSettings || _txtExtraJoistGlowIntensity == null)
            return;

        string raw = _txtExtraJoistGlowIntensity.Text
            .Trim()
            .TrimEnd('%')
            .Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double percent) ||
            percent < 0 ||
            percent > 100)
        {
            SyncExtraJoistGlowControls();
            TxtStatus.Text = "Extra Joist glow intensity must be 0 - 100 (%).";
            return;
        }

        SetExtraJoistGlowIntensity(percent / 100.0);
    }

    private void SetExtraJoistGlowIntensity(double intensity)
    {
        _settings.ExtraJoistGlowIntensity =
            AppSettingsStore.NormalizeExtraJoistGlowIntensity(intensity);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
        TxtStatus.Text =
            $"Extra Joist glow intensity: {Math.Round(_settings.ExtraJoistGlowIntensity * 100.0):0}%.";
    }

    private void SldExtraJoistGlow_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings || _txtExtraJoistGlowIntensity == null)
            return;

        _settings.ExtraJoistGlowIntensity =
            AppSettingsStore.NormalizeExtraJoistGlowIntensity(e.NewValue / 100.0);
        _viewport.ExtraJoistGlowIntensity = _settings.ExtraJoistGlowIntensity;
        _txtExtraJoistGlowIntensity.Text =
            Math.Round(_settings.ExtraJoistGlowIntensity * 100.0)
                .ToString("0", CultureInfo.InvariantCulture);
        _viewport.InvalidateVisual();
        ApplyLiveDisplayScalesToDetachedSheets();
        _viewportScaleDirty = true;
        TxtStatus.Text =
            $"Extra Joist glow intensity: {Math.Round(_settings.ExtraJoistGlowIntensity * 100.0):0}%.";
    }

    private void SyncExtraJoistGlowControls()
    {
        if (_sldExtraJoistGlow == null || _txtExtraJoistGlowIntensity == null)
            return;

        bool wasApplyingSettings = _isApplyingSettings;
        _isApplyingSettings = true;
        try
        {
            double percent = Math.Round(
                AppSettingsStore.NormalizeExtraJoistGlowIntensity(
                    _settings.ExtraJoistGlowIntensity) * 100.0);
            _sldExtraJoistGlow.Value = percent;
            _txtExtraJoistGlowIntensity.Text =
                percent.ToString("0", CultureInfo.InvariantCulture);
        }
        finally
        {
            _isApplyingSettings = wasApplyingSettings;
        }
    }
}
