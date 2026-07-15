using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;

namespace OurPlanCore;

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
            ApplyPageBackground(_settings.PageBackground, persist: false);
            ApplyTheme(string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase), persist: false);
            ApplyDisplaySettingsToViewport();
            ApplySheetOverlaySettings();
            ApplySidePanelWidths();
            if (string.Equals(TxtScaleRatio.Text, "100", StringComparison.OrdinalIgnoreCase))
                TxtScaleRatio.Text = "1/8\" = 1'0\"";
            TxtScaleRatio.ToolTip = "Imperial sheet scale, e.g. 1/8\" = 1'0\". Ratio values like 1:96 are also accepted.";
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
            if (!OpenJob(_settings.LastJobPath, initialPageFolder: _settings.LastPageFolder))
                return;
            TxtStatus.Text = $"Loaded last job: {_currentJob?.Name}. Select a page to render it.";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Last job open failed.");
            if (_currentJob != null)
            {
                RefreshJobHeaderLabels();
                TxtStatus.Text = $"Last job opened with warning: {ex.Message}";
                return;
            }

            TxtStatus.Text = $"Last job could not be opened: {ex.Message}";
            ShowStartupJobPickerIfUseful();
        }
    }

    private void SaveCurrentPageScale()
    {
        if (_currentPage == null || !IsCurrentJobWritable)
            return;

        _currentPage.ScaleMetersPerPt = _viewport.ScaleMetersPerPt;
        ApplyScaleToCurrentPageMeasurements(_viewport.ScaleMetersPerPt);
        OurPlanCoreJobStore.SavePageScale(_currentPage.FolderPath, _viewport.ScaleMetersPerPt);
    }

    private void ApplyViewportBackground(string color, bool persist)
    {
        string cleanColor = ViewportBackgroundPolicy.NormalizeColor(color);
        var backgroundBrush = new SolidColorBrush(ParseWpfColor(cleanColor, Colors.White));
        _viewport.ViewBackgroundColor = cleanColor;
        ViewportHost.Background = backgroundBrush;
        ViewportSurfaceHost.Background = backgroundBrush;
        _viewport.InvalidateVisual();
        _settings.ViewportBackground = cleanColor;

        if (persist)
        {
            SaveAppSettings();
        }
    }

    private void ApplyPageBackground(string color, bool persist)
    {
        string cleanColor = ViewportBackgroundPolicy.NormalizeColor(color);
        _viewport.PageBackgroundColor = cleanColor;
        _settings.PageBackground = cleanColor;
        _viewport.InvalidateVisual();

        if (persist)
            SaveAppSettings();
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

        // Colors below follow docs/BLUEBEAM_DESIGN_SYSTEM.md §4 role tokens
        // (light / dark pairs). Keep this method the single source of runtime
        // color; App.xaml only holds startup fallbacks.
        // OurCore design code (blue + sage) on a NEUTRAL dark base (user wants
        // the dark theme black/charcoal, not navy). Surfaces are neutral grey
        // (#161616..#262626); the blue #4EA1FF and sage #8FB89A appear ONLY as
        // accents (selected/active) and the brand underline — never as a fill.
        Color window   = dark ? Color.FromRgb(22, 22, 22)    : Color.FromRgb(250, 250, 250); // base
        Color toolbar  = dark ? Color.FromRgb(32, 32, 32)    : Color.FromRgb(221, 227, 234); // ribbon
        Color panel    = dark ? Color.FromRgb(28, 28, 28)    : Color.FromRgb(236, 239, 243); // surface-alt
        Color status   = dark ? Color.FromRgb(32, 32, 32)    : Color.FromRgb(221, 227, 234); // ribbon
        Color tree     = dark ? Color.FromRgb(38, 38, 38)    : Colors.White;                 // elevated (row)
        Color splitter = dark ? Color.FromRgb(56, 56, 56)    : Color.FromRgb(200, 204, 210); // border
        Brush foreground = new SolidColorBrush(dark ? Color.FromRgb(228, 228, 228) : Color.FromRgb(31, 35, 40)); // txt
        // Selected row = the same solid tint the Pages/Takeoffs trees use. Active
        // and inactive are identical so list selection never fades when the list
        // loses focus (matches the always-solid tree highlight the user wants).
        Color rowSelection = dark ? Color.FromRgb(62, 78, 102) : Color.FromRgb(191, 212, 236);
        Color rowSelectionInactive = rowSelection;
        UpdateAppBrush("WindowBackgroundBrush", window);
        UpdateAppBrush("PanelBackgroundBrush", panel);
        UpdateAppBrush("SurfaceBackgroundBrush", tree);
        UpdateAppBrush("SplitterBrush", splitter);
        UpdateAppBrush("SecondaryForegroundBrush", dark ? Color.FromRgb(160, 160, 160) : Color.FromRgb(85, 91, 98)); // txt-2
        UpdateAppBrush("ScrollBarTrackBrush", dark ? Color.FromRgb(42, 42, 42)  : Color.FromRgb(229, 231, 235)); // border-soft
        UpdateAppBrush("ScrollBarThumbBrush", dark ? Color.FromRgb(112, 112, 112) : Color.FromRgb(138, 144, 153)); // txt-3
        UpdateAppBrush("ControlForegroundBrush", dark ? Color.FromRgb(228, 228, 228) : Color.FromRgb(31, 35, 40)); // txt
        UpdateAppBrush("ControlBackgroundBrush", dark ? Color.FromRgb(38, 38, 38) : Color.FromRgb(245, 246, 248)); // row
        UpdateAppBrush("ControlBorderBrush", dark ? Color.FromRgb(64, 64, 64) : Color.FromRgb(156, 163, 172)); // border-strong
        UpdateAppBrush("ControlHoverBackgroundBrush", dark ? Color.FromRgb(44, 44, 44) : Color.FromRgb(232, 236, 241)); // row-hi
        UpdateAppBrush("ControlPressedBackgroundBrush", dark ? Color.FromRgb(54, 54, 54) : Color.FromRgb(221, 227, 234));
        UpdateAppBrush("ControlActiveBackgroundBrush", dark ? Color.FromRgb(36, 67, 100) : Color.FromRgb(220, 233, 245)); // info-soft (selected)
        UpdateAppBrush("ControlActiveForegroundBrush", dark ? Color.FromRgb(228, 228, 228) : Color.FromRgb(31, 35, 40)); // txt
        UpdateAppBrush("RowSelectionBrush", rowSelection);
        UpdateAppBrush(SystemColors.HighlightBrushKey, rowSelection);
        UpdateAppBrush(SystemColors.HighlightTextBrushKey, dark ? Color.FromRgb(228, 228, 228) : Color.FromRgb(31, 35, 40));
        UpdateAppBrush(SystemColors.InactiveSelectionHighlightBrushKey, rowSelectionInactive);
        UpdateAppBrush(SystemColors.InactiveSelectionHighlightTextBrushKey, dark ? Color.FromRgb(228, 228, 228) : Color.FromRgb(31, 35, 40));
        UpdateAppBrush(SystemColors.MenuBrushKey, dark ? tree : Colors.White);
        UpdateAppBrush(SystemColors.MenuTextBrushKey, dark ? Color.FromRgb(228, 228, 228) : Color.FromRgb(31, 35, 40));
        UpdateAppBrush(SystemColors.WindowBrushKey, dark ? tree : Colors.White);
        UpdateAppBrush(SystemColors.WindowTextBrushKey, dark ? Color.FromRgb(228, 228, 228) : Color.FromRgb(31, 35, 40));
        UpdateAppBrush(SystemColors.ControlBrushKey, dark ? Color.FromRgb(38, 38, 38) : Color.FromRgb(245, 246, 248));
        UpdateAppBrush(SystemColors.ControlTextBrushKey, dark ? Color.FromRgb(228, 228, 228) : Color.FromRgb(31, 35, 40));
        UpdateAppBrush(SystemColors.GrayTextBrushKey, dark ? Color.FromRgb(112, 112, 112) : Color.FromRgb(138, 144, 153)); // txt-3
        UpdateAppBrush(SystemColors.InfoBrushKey, dark ? Color.FromRgb(38, 38, 38) : Color.FromRgb(245, 246, 248));
        UpdateAppBrush(SystemColors.InfoTextBrushKey, dark ? Color.FromRgb(228, 228, 228) : Color.FromRgb(31, 35, 40));
        UpdateAppBrush("AccentBrush", dark ? Color.FromRgb(78, 161, 255) : Color.FromRgb(31, 111, 178)); // info (selected)
        UpdateAppBrush("AccentHoverBrush", dark ? Color.FromRgb(111, 180, 255) : Color.FromRgb(26, 94, 151));
        UpdateAppBrush("AccentPressedBrush", dark ? Color.FromRgb(61, 138, 232) : Color.FromRgb(21, 78, 128));
        UpdateAppBrush("AccentForegroundBrush", Colors.White);
        // Sage brand signal — used for the active sub-tab underline + applied badge.
        UpdateAppBrush("BrandAccentBrush", dark ? Color.FromRgb(143, 184, 154) : Color.FromRgb(106, 142, 116)); // sage
        UpdateAppBrush("ToolbarBandBrush", dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(221, 227, 234)); // ribbon
        UpdateAppBrush("ManagerHeaderBrush", dark ? Color.FromRgb(40, 40, 40) : Color.FromRgb(230, 234, 240));
        UpdateAppBrush("SubtleButtonBackgroundBrush", dark ? Color.FromRgb(40, 40, 40) : Color.FromRgb(240, 242, 245));
        UpdateAppBrush("DataGridAltRowBrush", dark ? Color.FromRgb(30, 30, 30) : Color.FromRgb(247, 249, 252)); // zebra
        UpdateAppBrush("CommitBrush", dark ? Color.FromRgb(77, 204, 139) : Color.FromRgb(30, 126, 52)); // success (ok)
        UpdateAppBrush("CommitHoverBrush", dark ? Color.FromRgb(102, 214, 160) : Color.FromRgb(26, 110, 45));
        UpdateAppBrush("CommitPressedBrush", dark ? Color.FromRgb(60, 180, 120) : Color.FromRgb(21, 90, 36));

        // Report Builder row kinds — keep the Excel-style semantic tint but make
        // dark theme readable (light values unchanged so light theme is identical).
        UpdateAppBrush("ReportHeaderRowBrush",      dark ? Color.FromRgb(42, 51, 64)  : Color.FromRgb(233, 238, 247)); // blue
        UpdateAppBrush("ReportTableHeaderRowBrush", dark ? Color.FromRgb(42, 55, 39)  : Color.FromRgb(217, 234, 211)); // green
        UpdateAppBrush("ReportSectionRowBrush",     dark ? Color.FromRgb(58, 46, 34)  : Color.FromRgb(244, 177, 131)); // orange
        UpdateAppBrush("ReportInputRowBrush",       dark ? Color.FromRgb(51, 47, 31)  : Color.FromRgb(255, 242, 204)); // yellow

        // Tree row state — theme-aware (paired light/dark variants).
        // Semantic hues kept (amber=active, green=on-page, etc.); the active
        // takeoff stripe is bound to the sage brand signal (OurCore "чуть шире").
        UpdateAppBrush("RowOnPageBrush",        MuteRowHighlight(dark ? Color.FromRgb(34, 64, 46)   : Color.FromRgb(214, 245, 222), tree));
        UpdateAppBrush("RowActiveBrush",        MuteTreeCrossHighlight(dark ? Color.FromRgb(82, 64, 24)   : Color.FromRgb(255, 236, 190), tree));
        UpdateAppBrush("RowMultiSelectBrush",   MuteTreeCrossHighlight(dark ? Color.FromRgb(36, 67, 100)   : Color.FromRgb(220, 233, 245), tree));
        UpdateAppBrush("RowDropOkBrush",        MuteRowHighlight(dark ? Color.FromRgb(40, 86, 58)   : Color.FromRgb(204, 245, 218), tree));
        UpdateAppBrush("RowDropBadBrush",       MuteRowHighlight(dark ? Color.FromRgb(110, 48, 48)  : Color.FromRgb(255, 214, 214), tree));
        UpdateAppBrush("RowFlagForegroundBrush",dark ? Color.FromRgb(228, 228, 228) : Color.FromRgb(31, 35, 40));
        UpdateAppBrush("RowActiveAccentBrush",  MuteRowHighlight(dark ? Color.FromRgb(143, 184, 154) : Color.FromRgb(106, 142, 116), tree)); // sage
        SetupToolButtonContent();

        Background = new SolidColorBrush(window);
        RootDock.Background = new SolidColorBrush(window);
        BottomToolStrip.Background = new SolidColorBrush(toolbar);
        MainStatusBar.Background = new SolidColorBrush(status);
        PagesPanel.Background = new SolidColorBrush(panel);
        TakeoffsPanel.Background = new SolidColorBrush(panel);
        PagesTree.Background = new SolidColorBrush(tree);
        TakeoffsTree.Background = new SolidColorBrush(tree);
        PagesTree.Foreground = foreground;
        TakeoffsTree.Foreground = foreground;
        TxtStatus.Foreground = foreground;
        TxtScaleInfo.Foreground = new SolidColorBrush(dark ? Color.FromRgb(78, 161, 255) : Color.FromRgb(31, 111, 178)); // info
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
        ApplyThreeDViewportTheme(dark);
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

    private static void UpdateAppBrush(object key, Color color)
    {
        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    // Opens a button's attached ContextMenu on left-click, so a plain Button can
    // act as a compact dropdown (used to declutter the Pages/Takeoffs toolbars).
    private void DropdownButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && b.ContextMenu is ContextMenu menu)
        {
            ApplyModuleAvailabilityToMenu(menu);
            menu.PlacementTarget = b;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private static Color MuteRowHighlight(Color color, Color surface) =>
        BlendColor(color, surface, 0.25);

    private static Color MuteTreeCrossHighlight(Color color, Color surface) =>
        BlendColor(color, surface, 0.45);

    private static Color BlendColor(Color color, Color target, double targetAmount)
    {
        targetAmount = Math.Clamp(targetAmount, 0.0, 1.0);
        double sourceAmount = 1.0 - targetAmount;
        return Color.FromRgb(
            (byte)Math.Round(color.R * sourceAmount + target.R * targetAmount),
            (byte)Math.Round(color.G * sourceAmount + target.G * targetAmount),
            (byte)Math.Round(color.B * sourceAmount + target.B * targetAmount));
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
        string scaleText = PdfSheetMetadataService.FormatImperialScale(scale);
        double ratio = 0;
        TxtScaleInfo.Text = ratio > 0 ? $"≈1:{ratio:F0}" : "";
        TxtScaleInfo.Text = string.IsNullOrWhiteSpace(scaleText) ? "" : "applied";
        if (!string.IsNullOrWhiteSpace(scaleText))
            TxtScaleRatio.Text = scaleText;
        UpdateStatusBarSegments();
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
