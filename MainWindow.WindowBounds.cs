using System;
using System.ComponentModel;
using System.Windows;

namespace OurPlanCore;

public partial class MainWindow
{
    // ── Main window placement persistence ─────────────────────────────────────
    //
    // Two jobs, both invisible on a screen where the default 1280x780 window
    // already fits:
    //   1. Restore the last saved size/position/maximized state.
    //   2. On a fresh install, shrink the default window ONLY when it wouldn't
    //      fit the work area (a small laptop), so the bottom command strip can't
    //      open below the taskbar.
    // If nothing is saved and the default window fits, this makes NO changes.

    private void RestoreWindowBounds()
    {
        if (_settings.WindowWidth > 0 && _settings.WindowHeight > 0)
        {
            RestoreSavedWindowBounds();
            return;
        }

        // Fresh install: leave big screens exactly as before; only rescue laptops
        // whose work area is smaller than the default window.
        Rect work = SystemParameters.WorkArea;
        if (Width <= work.Width && Height <= work.Height)
            return;

        double fitW = Math.Min(Width, work.Width);
        double fitH = Math.Min(Height, work.Height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = fitW;
        Height = fitH;
        Left = work.Left + Math.Max(0, (work.Width - fitW) / 2);
        Top = work.Top + Math.Max(0, (work.Height - fitH) / 2);
    }

    private void RestoreSavedWindowBounds()
    {
        double vWidth = Math.Max(MinWidth, SystemParameters.VirtualScreenWidth);
        double vHeight = Math.Max(MinHeight, SystemParameters.VirtualScreenHeight);
        double w = Math.Clamp(_settings.WindowWidth, MinWidth, vWidth);
        double h = Math.Clamp(_settings.WindowHeight, MinHeight, vHeight);

        double vLeft = SystemParameters.VirtualScreenLeft;
        double vTop = SystemParameters.VirtualScreenTop;
        double vRight = vLeft + SystemParameters.VirtualScreenWidth;
        double vBottom = vTop + SystemParameters.VirtualScreenHeight;

        double left = double.IsFinite(_settings.WindowLeft) ? _settings.WindowLeft : vLeft;
        double top = double.IsFinite(_settings.WindowTop) ? _settings.WindowTop : vTop;
        left = Math.Clamp(left, vLeft, Math.Max(vLeft, vRight - w));
        top = Math.Clamp(top, vTop, Math.Max(vTop, vBottom - h));

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = w;
        Height = h;
        Left = left;
        Top = top;
        if (_settings.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private void SaveWindowBounds()
    {
        if (_isApplyingSettings)
            return;

        bool maximized = WindowState == WindowState.Maximized;

        // RestoreBounds is the normal-state rectangle even while maximized or
        // minimized, so we always persist the size the window returns to.
        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;

        if (double.IsFinite(bounds.Left) && double.IsFinite(bounds.Top) &&
            bounds.Width >= MinWidth && bounds.Height >= MinHeight)
        {
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
        }

        _settings.WindowMaximized = maximized;
        SaveAppSettings();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!FlushTakeoffAutosavesBeforeClose())
        {
            e.Cancel = true;
            return;
        }

        if (!SaveCurrentPageStateBeforeClose())
        {
            e.Cancel = true;
            return;
        }

        SaveWindowBounds();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _sheetLegendAutoSortTimer.Stop();
        _takeoffSaveService.Stop();
        RunCloseCleanup(SaveSidePanelWidths, "save side panel widths");
        RunCloseCleanup(RunRasterCacheCleanupOnClose, "clean raster cache");
        RunCloseCleanup(ClearJobRecoveryLock, "clear job recovery lock");
        RunCloseCleanup(PdfLayerRenderService.StopWorker, "stop PDF layer worker");
        base.OnClosed(e);
    }

    private bool FlushTakeoffAutosavesBeforeClose()
    {
        if (TryFlushTakeoffAutosaves("close OurPlanCore", showDialog: false))
            return true;

        MessageBoxResult choice = MessageBox.Show(
            $"Some takeoff changes could not be saved and the app will stay open.\n\n" +
            $"{_takeoffSaveService.LastError}\n\n" +
            "Yes = retry now.\n" +
            "No = discard only entries whose takeoff folders are still unavailable, then close.\n" +
            "Cancel = keep the app open.",
            "Unsaved Takeoff Changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Error);
        if (choice == MessageBoxResult.Yes &&
            TryFlushTakeoffAutosaves("close OurPlanCore", showDialog: false))
        {
            return true;
        }

        if (choice == MessageBoxResult.No)
        {
            int discarded = _takeoffSaveService.DiscardUnavailableItems();
            if (discarded > 0 &&
                TryFlushTakeoffAutosaves("close OurPlanCore", showDialog: false))
            {
                AppLog.Warn($"Closing after the user discarded {discarded} unavailable pending takeoff item(s).");
                return true;
            }
        }

        TxtStatus.Text = "Close canceled: takeoff changes are still pending.";
        return false;
    }

    private bool SaveCurrentPageStateBeforeClose()
    {
        try
        {
            SaveCurrentPageScale();
            SaveCurrentPageAnnotations();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Close canceled because current page state could not be saved.");
            TxtStatus.Text = $"Close canceled: current page could not be saved. {ex.Message}";
            MessageBox.Show(
                $"The current page scale or annotations could not be saved. The app will stay open.\n\n{ex.Message}",
                "Page Save Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private static void RunCloseCleanup(Action action, string description)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Failed to {description} while closing.");
        }
    }
}
