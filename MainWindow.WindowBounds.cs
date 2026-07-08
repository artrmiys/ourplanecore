using System;
using System.ComponentModel;
using System.Windows;

namespace OurPlaneCore;

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
        SaveWindowBounds();
        base.OnClosing(e);
    }
}
