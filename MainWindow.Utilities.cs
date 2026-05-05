using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;

namespace SmartTakeoffs;

public partial class MainWindow
{
    // ── Utilities ─────────────────────────────────────────────────────────────

    private void ApplyPersistedSettings()
    {
        _isApplyingSettings = true;
        try
        {
            _viewport.UnitMode = _settings.UnitMode == UnitMode.Metric.ToString()
                ? UnitMode.Metric
                : UnitMode.Imperial;
            ComboFolderTemplateMode.SelectedIndex = NormalizeFolderTemplateMode(_settings.FolderTemplateMode) switch
            {
                "COM" => 1,
                "EWP" => 2,
                _ => 0,
            };
            ApplyViewportBackground(_settings.ViewportBackground, persist: false);
            ApplyTheme(string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase), persist: false);
            ApplyDisplaySettingsToViewport();
            ApplySheetOverlaySettings();
            ApplySidePanelWidths();
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void TryOpenLastJobFromSettings()
    {
        if (string.IsNullOrWhiteSpace(_settings.LastJobPath) || !Directory.Exists(_settings.LastJobPath))
        {
            ShowStartupJobPickerIfUseful();
            return;
        }

        try
        {
            OpenJob(_settings.LastJobPath, initialPageFolder: _settings.LastPageFolder);
            TxtStatus.Text = $"Loaded last job: {_currentJob?.Name}. Select a page to render it.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Last job could not be opened: {ex.Message}";
            ShowStartupJobPickerIfUseful();
        }
    }

    private void SaveCurrentPageScale()
    {
        if (_currentPage == null)
            return;

        _currentPage.ScaleMetersPerPt = _viewport.ScaleMetersPerPt;
        ApplyScaleToCurrentPageMeasurements(_viewport.ScaleMetersPerPt);
        SmartTakeoffsJobStore.SavePageScale(_currentPage.FolderPath, _viewport.ScaleMetersPerPt);
    }

    private void ApplyViewportBackground(string color, bool persist)
    {
        string cleanColor = string.IsNullOrWhiteSpace(color) ? "#FFFFFF" : color;
        _viewport.ViewBackgroundColor = cleanColor;
        ViewportHost.Background = new SolidColorBrush(ParseWpfColor(cleanColor, Colors.White));
        _viewport.InvalidateVisual();

        if (persist)
        {
            _settings.ViewportBackground = cleanColor;
            SaveAppSettings();
        }
    }

    private void ApplyTheme(bool dark, bool persist)
    {
        bool wasApplying = _isApplyingSettings;
        _isApplyingSettings = true;
        try
        {
            BtnDisplayDarkTheme.IsChecked = dark;
            BtnDisplayDarkTheme.Content = dark ? "Light" : "Dark";
        }
        finally
        {
            _isApplyingSettings = wasApplying;
        }

        Color window = dark ? Color.FromRgb(30, 32, 35) : Color.FromRgb(240, 240, 240);
        Color toolbar = dark ? Color.FromRgb(43, 45, 49) : Color.FromRgb(240, 240, 240);
        Color panel = dark ? Color.FromRgb(37, 39, 42) : Color.FromRgb(245, 245, 245);
        Color status = dark ? Color.FromRgb(43, 45, 49) : Color.FromRgb(232, 232, 232);
        Color tree = dark ? Color.FromRgb(31, 33, 36) : Colors.White;
        Color splitter = dark ? Color.FromRgb(68, 72, 78) : Color.FromRgb(204, 204, 204);
        Brush foreground = new SolidColorBrush(dark ? Color.FromRgb(230, 230, 230) : Color.FromRgb(30, 30, 30));
        UpdateAppBrush("WindowBackgroundBrush", window);
        UpdateAppBrush("PanelBackgroundBrush", panel);
        UpdateAppBrush("SurfaceBackgroundBrush", tree);
        UpdateAppBrush("SplitterBrush", splitter);
        UpdateAppBrush("SecondaryForegroundBrush", dark ? Color.FromRgb(176, 179, 184) : Color.FromRgb(102, 102, 102));
        UpdateAppBrush("ScrollBarTrackBrush", dark ? Color.FromRgb(45, 47, 52)  : Color.FromRgb(220, 220, 220));
        UpdateAppBrush("ScrollBarThumbBrush", dark ? Color.FromRgb(90, 93, 100) : Color.FromRgb(160, 160, 160));
        UpdateAppBrush("ControlForegroundBrush", dark ? Color.FromRgb(238, 238, 238) : Color.FromRgb(32, 32, 32));
        UpdateAppBrush("ControlBackgroundBrush", dark ? Color.FromRgb(58, 61, 66) : Color.FromRgb(248, 248, 248));
        UpdateAppBrush("ControlBorderBrush", dark ? Color.FromRgb(118, 122, 130) : Color.FromRgb(160, 160, 160));
        UpdateAppBrush("ControlHoverBackgroundBrush", dark ? Color.FromRgb(72, 76, 82) : Color.FromRgb(232, 232, 232));
        UpdateAppBrush("ControlPressedBackgroundBrush", dark ? Color.FromRgb(86, 91, 98) : Color.FromRgb(208, 208, 208));
        UpdateAppBrush("ControlActiveBackgroundBrush", dark ? Color.FromRgb(37, 99, 160) : Color.FromRgb(204, 229, 255));
        UpdateAppBrush("ControlActiveForegroundBrush", dark ? Colors.White : Color.FromRgb(17, 17, 17));
        UpdateAppBrush("AccentBrush", dark ? Color.FromRgb(90, 160, 235) : Color.FromRgb(37, 99, 166));
        UpdateAppBrush("AccentHoverBrush", dark ? Color.FromRgb(112, 178, 245) : Color.FromRgb(31, 85, 145));
        UpdateAppBrush("AccentPressedBrush", dark ? Color.FromRgb(70, 135, 210) : Color.FromRgb(24, 68, 111));
        UpdateAppBrush("AccentForegroundBrush", Colors.White);
        UpdateAppBrush("ToolbarBandBrush", dark ? Color.FromRgb(45, 48, 54) : Color.FromRgb(236, 239, 243));
        UpdateAppBrush("ManagerHeaderBrush", dark ? Color.FromRgb(50, 56, 66) : Color.FromRgb(232, 238, 246));
        UpdateAppBrush("SubtleButtonBackgroundBrush", dark ? Color.FromRgb(50, 53, 58) : Color.FromRgb(243, 244, 246));
        UpdateAppBrush("DataGridAltRowBrush", dark ? Color.FromRgb(34, 37, 42) : Color.FromRgb(247, 249, 252));
        UpdateAppBrush("CommitBrush", dark ? Color.FromRgb(70, 150, 82) : Color.FromRgb(46, 125, 50));
        UpdateAppBrush("CommitHoverBrush", dark ? Color.FromRgb(84, 168, 96) : Color.FromRgb(39, 109, 44));
        UpdateAppBrush("CommitPressedBrush", dark ? Color.FromRgb(52, 122, 63) : Color.FromRgb(29, 84, 33));

        // Tree row state — theme-aware (paired light/dark variants)
        UpdateAppBrush("RowOnPageBrush",        dark ? Color.FromRgb(34, 64, 46)   : Color.FromRgb(214, 245, 222));
        UpdateAppBrush("RowActiveBrush",        dark ? Color.FromRgb(82, 64, 24)   : Color.FromRgb(255, 236, 190));
        UpdateAppBrush("RowMultiSelectBrush",   dark ? Color.FromRgb(38, 70, 110)  : Color.FromRgb(205, 226, 255));
        UpdateAppBrush("RowDropOkBrush",        dark ? Color.FromRgb(40, 86, 58)   : Color.FromRgb(204, 245, 218));
        UpdateAppBrush("RowDropBadBrush",       dark ? Color.FromRgb(110, 48, 48)  : Color.FromRgb(255, 214, 214));
        UpdateAppBrush("RowFlagForegroundBrush",dark ? Colors.White                : Color.FromRgb(17, 17, 17));
        UpdateAppBrush("RowActiveAccentBrush",  dark ? Color.FromRgb(120, 170, 255): Color.FromRgb(31, 82, 166));
        SetupToolButtonContent();

        Background = new SolidColorBrush(window);
        RootDock.Background = new SolidColorBrush(window);
        MainToolBar.Background = new SolidColorBrush(toolbar);
        MainStatusBar.Background = new SolidColorBrush(status);
        PagesPanel.Background = new SolidColorBrush(panel);
        TakeoffsPanel.Background = new SolidColorBrush(panel);
        PagesTree.Background = new SolidColorBrush(tree);
        TakeoffsTree.Background = new SolidColorBrush(tree);
        PagesTree.Foreground = foreground;
        TakeoffsTree.Foreground = foreground;
        TxtStatus.Foreground = foreground;
        TxtScaleInfo.Foreground = new SolidColorBrush(dark ? Color.FromRgb(138, 180, 248) : Color.FromRgb(0, 85, 204));
        ObservationsListView.Background  = new SolidColorBrush(tree);
        ObservationsListView.Foreground  = foreground;
        if (_estimateList != null)
        {
            _estimateList.Background = new SolidColorBrush(tree);
            _estimateList.Foreground = foreground;
        }
        if (_massingDraftTextBox != null)
        {
            _massingDraftTextBox.Background = new SolidColorBrush(tree);
            _massingDraftTextBox.Foreground = foreground;
        }
        if (_massingMarkerList != null)
        {
            _massingMarkerList.Background = new SolidColorBrush(tree);
            _massingMarkerList.Foreground = foreground;
        }
        if (_massingMarkerDetailsTextBox != null)
        {
            _massingMarkerDetailsTextBox.Background = new SolidColorBrush(tree);
            _massingMarkerDetailsTextBox.Foreground = foreground;
        }
        InboxPanel.Background           = new SolidColorBrush(tree);
        InboxHeaderBorder.Background    = new SolidColorBrush(toolbar);
        InboxSplitter.Background        = new SolidColorBrush(splitter);
        UpdateRecordButton();

        if (persist)
        {
            _settings.Theme = dark ? "Dark" : "Light";
            SaveAppSettings();
        }
    }

    private static void UpdateAppBrush(string key, Color color)
    {
        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private void SaveAppSettings()
    {
        if (_isApplyingSettings)
            return;

        AppSettingsStore.Save(_settings);
    }

    private void ApplySidePanelWidths()
    {
        PagesColumn.Width = new GridLength(NormalizePanelWidth(_settings.LeftPanelWidth, 200.0));
        TakeoffsColumn.Width = new GridLength(NormalizePanelWidth(_settings.RightPanelWidth, 220.0));
    }

    private void SidePanelSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        SaveSidePanelWidths();
    }

    private void SidePanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isApplyingSettings || !IsLoaded || !e.WidthChanged)
            return;

        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) >= 1.0)
            SaveSidePanelWidths();
    }

    private void SaveSidePanelWidths()
    {
        double left = NormalizePanelWidth(PagesColumn.ActualWidth, 200.0);
        double right = NormalizePanelWidth(TakeoffsColumn.ActualWidth, 220.0);
        if (Math.Abs(_settings.LeftPanelWidth - left) < 0.5 &&
            Math.Abs(_settings.RightPanelWidth - right) < 0.5)
        {
            return;
        }

        _settings.LeftPanelWidth = left;
        _settings.RightPanelWidth = right;
        SaveAppSettings();
    }

    private static double NormalizePanelWidth(double width, double fallback)
    {
        if (!double.IsFinite(width) || width < 120.0)
            return fallback;
        return Math.Clamp(width, 120.0, 640.0);
    }

    private static Color ParseWpfColor(string color, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(color);
        }
        catch
        {
            return fallback;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
                yield return typed;

            foreach (T nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private static string? SelectFolder(string title, string? initialFolder = null)
    {
        var dlg = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
            dlg.FolderName = initialFolder;

        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    private void UpdateScaleUi(double scale)
    {
        const double PT_M = 25.4 / 72.0 / 1000.0;
        double ratio = scale > 0 ? scale / PT_M : 0;
        TxtScaleInfo.Text = ratio > 0 ? $"≈1:{ratio:F0}" : "";
        if (ratio > 0) TxtScaleRatio.Text = $"{ratio:F0}";
    }

    private string? ShowInputDialog(string prompt, string initial, string title)
    {
        var win = new Window
        {
            Title                 = title,
            Width                 = 300,
            SizeToContent         = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode            = ResizeMode.NoResize,
            Owner                 = this,
        };
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 6) });
        var tb = new TextBox { Text = initial };
        panel.Children.Add(tb);
        var btns = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(0, 8, 0, 0),
        };
        var ok     = new Button { Content = "OK",     Width = 70, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 70, IsCancel  = true };
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        panel.Children.Add(btns);
        win.Content = panel;

        string? result = null;
        ok.Click += (_, _) => { result = tb.Text.Trim(); win.DialogResult = true; };
        win.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return win.ShowDialog() == true ? result : null;
    }

    private string? ShowMultilineInputDialog(string prompt, string initial, string title)
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Owner = this,
        };
        var panel = new DockPanel { Margin = new Thickness(10) };
        var promptBlock = new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        DockPanel.SetDock(promptBlock, Dock.Top);
        panel.Children.Add(promptBlock);

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var ok = new Button { Content = "OK", Width = 70, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 70, IsCancel = true };
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        DockPanel.SetDock(btns, Dock.Bottom);
        panel.Children.Add(btns);

        var tb = new TextBox
        {
            Text = initial,
            AcceptsReturn = true,
            AcceptsTab = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
        };
        panel.Children.Add(tb);
        win.Content = panel;

        string? result = null;
        ok.Click += (_, _) => { result = tb.Text; win.DialogResult = true; };
        win.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return win.ShowDialog() == true ? result : null;
    }

    private static bool IsJobFolder(string folder) =>
        File.Exists(Path.Combine(folder, "Data.xml")) &&
        Directory.Exists(Path.Combine(folder, "Pages")) &&
        Directory.Exists(Path.Combine(folder, "Takeoffs"));
}
