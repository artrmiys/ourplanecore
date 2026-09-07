using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using OurPlanCore.Controls;

namespace OurPlanCore;

public sealed class DetachedSheetWindow : Window
{
    private readonly PdfViewport _viewport = new();
    public PdfViewport Viewport => _viewport;
    public PageInfo Page { get; private set; }

    public DetachedSheetWindow(
        OurPlanCoreJob job,
        PageInfo page,
        IReadOnlyList<TakeoffItem> takeoffItems,
        AppSettings settings,
        UnitMode unitMode)
    {
        Page = page;
        Title = page.Name;
        MinWidth = 320;
        MinHeight = 240;
        Width = 960;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _viewport;

        ConfigureViewport(job, page, takeoffItems, settings, unitMode);
    }

    public void RefreshTakeoffDisplay(
        OurPlanCoreJob job,
        IReadOnlyList<TakeoffItem> takeoffItems,
        AppSettings settings,
        UnitMode unitMode,
        bool clearUndoStack = true)
    {
        PageInfo page = OurPlanCoreJobStore.TryReadPage(Page.FolderPath) ?? Page;
        ApplyViewportDisplaySettings(settings, unitMode);
        _viewport.SetMeasurements(takeoffItems.SelectMany(takeoff => takeoff.Measurements), clearUndoStack);
        _viewport.SetHiddenTakeoffFolders(HiddenTakeoffFolders(job, page, takeoffItems));
        _viewport.SetHiddenMeasurementIds(page.HiddenMeasurements);
        _viewport.SetSheetLegend(settings.ShowSheetLegend
            ? SheetLegendBuilder.Build(job, page, takeoffItems, unitMode)
            : []);
        _viewport.InvalidateVisual();
    }

    public void RefreshPageScale(double scaleMetersPerPt)
    {
        Page.ScaleMetersPerPt = scaleMetersPerPt;
        _viewport.ScaleMetersPerPt = scaleMetersPerPt;
    }

    // Swaps the sheet shown in this window in place (Pages-tree navigation
    // targeting a focused detached window). Keeps the current tool and all
    // display settings; only the page-bound viewport state is reloaded.
    public void ShowPage(
        OurPlanCoreJob job,
        PageInfo page,
        IReadOnlyList<TakeoffItem> takeoffItems,
        AppSettings settings,
        UnitMode unitMode)
    {
        Page = page;
        Title = page.Name;
        _viewport.ScaleMetersPerPt = page.ScaleMetersPerPt;
        _viewport.SetMeasurements(takeoffItems.SelectMany(takeoff => takeoff.Measurements));
        _viewport.SetHiddenTakeoffFolders(HiddenTakeoffFolders(job, page, takeoffItems));
        _viewport.SetHiddenMeasurementIds(page.HiddenMeasurements);

        _viewport.LoadPage(
            page.PdfPath,
            page.PdfPage,
            page.FolderPath,
            page.PdfLayersCached ? page.PdfLayers : null,
            rasterSheet: page.RasterSheet);
        _viewport.SetPageAnnotations(OurPlanCoreJobStore.LoadPageAnnotations(page.FolderPath));
        _viewport.SetSheetLegend(settings.ShowSheetLegend
            ? SheetLegendBuilder.Build(job, page, takeoffItems, unitMode)
            : []);
        _viewport.InvalidateVisual();
    }

    private void ConfigureViewport(
        OurPlanCoreJob job,
        PageInfo page,
        IReadOnlyList<TakeoffItem> takeoffItems,
        AppSettings settings,
        UnitMode unitMode)
    {
        ApplyViewportDisplaySettings(settings, unitMode);
        _viewport.SetTool("pan");
        ShowPage(job, page, takeoffItems, settings, unitMode);
    }

    private void ApplyViewportDisplaySettings(AppSettings settings, UnitMode unitMode)
    {
        _viewport.ViewBackgroundColor = settings.ViewportBackground;
        _viewport.PageBackgroundColor = settings.PageBackground;
        _viewport.UnitMode = unitMode;
        _viewport.ShowMeasurementLabels = settings.ShowMeasurementLabels;
        _viewport.ShowLineLabels = settings.ShowLineLabels;
        _viewport.ShowAreaLabels = settings.ShowAreaLabels;
        _viewport.ShowJoistLabels = settings.ShowJoistLabels;
        _viewport.ShowCountLabels = settings.ShowCountLabels;
        _viewport.ShowBlackVectorOverlay = settings.BlackVectorOverlayEnabled;
        _viewport.MeasurementLabelScale = ClampScale(settings.MeasurementLabelScale);
        _viewport.MeasurementStrokeScale = ClampScale(settings.ViewportMeasurementStrokeScale);
        _viewport.RulerStrokeWidth = Math.Clamp(settings.ViewportRulerStrokeWidth, 0.5, 6.0);
        _viewport.LiveInputLabelSizePx = AppSettingsStore.NormalizeLiveInputLabelSize(settings.ViewportLiveInputLabelSizePx);
        _viewport.LiveInputLabelOpacity = AppSettingsStore.NormalizeLiveInputLabelOpacity(settings.ViewportLiveInputLabelOpacity);
        _viewport.PdfSnapBridgeToleranceScreenPx =
            AppSettingsStore.NormalizePdfSnapBridgeTolerancePx(settings.ViewportPdfSnapBridgeTolerancePx);
        _viewport.PointSizeScale = ClampScale(settings.ViewportPointSizeScale);
        _viewport.AreaEdgeScale = settings.ViewportAreaEdgeScale;
        _viewport.AreaFillOpacity = settings.ViewportAreaFillOpacity;
        _viewport.ExtraJoistGlowIntensity = settings.ExtraJoistGlowIntensity;
        _viewport.ZoomWheelFactor = settings.ViewportZoomWheelFactor;
        _viewport.SheetLegendAnchor = settings.SheetLegendAnchor;
        _viewport.SheetLegendScale = ClampScale(settings.SheetLegendScale);
        _viewport.SheetHeaderScale = ClampScale(settings.SheetHeaderScale);
        _viewport.ScaleSheetOverlaysWithPage = settings.ScaleSheetOverlaysWithPage;
        _viewport.ScaleMeasurementLabelsWithPage = settings.ScaleMeasurementLabelsWithPage;
        _viewport.ScaleSheetHeaderWithPage = settings.ScaleSheetHeaderWithPage;
        _viewport.SimplifyNavigationRendering = settings.SimplifyViewportNavigation;
    }

    private static IReadOnlyList<string> HiddenTakeoffFolders(
        OurPlanCoreJob job,
        PageInfo page,
        IReadOnlyList<TakeoffItem> takeoffItems)
    {
        var hiddenKeys = page.HiddenTakeoffs
            .Select(key => NormalizeLegendOrderKey(job, key))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (hiddenKeys.Count == 0)
            return [];

        return takeoffItems
            .Where(item => hiddenKeys.Contains(NormalizeLegendOrderKey(job, item.FolderPath)))
            .Select(item => item.FolderPath)
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .ToList();
    }

    private static string NormalizeLegendOrderKey(OurPlanCoreJob job, string value)
    {
        string clean = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return "";

        if (Path.IsPathFullyQualified(clean))
        {
            string full = NormalizePath(clean);
            if (OurPlanCoreJobStore.IsSameOrDescendant(job.TakeoffsRoot, full))
                clean = Path.GetRelativePath(job.TakeoffsRoot, full);
        }

        return clean.Replace('\\', '/').Trim('/');
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static double ClampScale(double scale) =>
        double.IsNaN(scale) || double.IsInfinity(scale)
            ? 1.0
            : Math.Clamp(scale, 0.25, 3.0);
}

public static class DetachedSheetWindowLayout
{
    public static string TileOnSecondMonitorOrPrimary(IReadOnlyList<Window> windows, bool verticalStack = false)
    {
        if (windows.Count == 0)
            return "monitor";

        IReadOnlyList<MonitorBounds> monitors = EnumerateMonitors();
        MonitorBounds monitor = monitors
                                    .Where(item => !item.IsPrimary)
                                    .OrderByDescending(item => item.Width * item.Height)
                                    .FirstOrDefault()
                                ?? monitors.FirstOrDefault(item => item.IsPrimary)
                                ?? PrimaryWorkAreaFallback();
        TileIntoBounds(windows, monitor, verticalStack);
        return monitor.IsPrimary ? "primary monitor" : "monitor 2";
    }

    private static void TileIntoBounds(
        IReadOnlyList<Window> windows,
        MonitorBounds monitor,
        bool verticalStack)
    {
        int count = Math.Min(64, windows.Count);
        int columns = verticalStack
            ? 1
            : Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
        int rows = verticalStack
            ? count
            : Math.Max(1, (int)Math.Ceiling(count / (double)columns));
        // Monitor bounds are physical pixels (the app is per-monitor-DPI aware),
        // but Window.Left/Top/Width/Height are WPF units. Position through
        // SetWindowPos in device pixels so 125-150% displays don't get windows
        // scaled past the monitor edge; WPF units are only a no-handle fallback.
        double cellWidth = Math.Max(1, monitor.Width / columns);
        double cellHeight = Math.Max(1, monitor.Height / rows);
        double dipScale = monitor.DipScale > 0 ? monitor.DipScale : 1.0;

        for (int i = 0; i < count; i++)
        {
            int row = i / columns;
            int column = i % columns;
            Window window = windows[i];
            window.WindowState = WindowState.Normal;
            window.MinWidth = Math.Min(window.MinWidth, cellWidth / dipScale);
            window.MinHeight = Math.Min(window.MinHeight, cellHeight / dipScale);
            int cellLeft = (int)Math.Round(monitor.Left + column * cellWidth);
            int cellTop = (int)Math.Round(monitor.Top + row * cellHeight);
            int cellRight = (int)Math.Round(monitor.Left + (column + 1) * cellWidth);
            int cellBottom = (int)Math.Round(monitor.Top + (row + 1) * cellHeight);
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                // First call moves the window onto the target monitor, which can
                // fire WM_DPICHANGED when the monitors' scales differ; WPF then
                // resizes the window to keep its DIP size and breaks the grid.
                // Re-applying the exact cell (immediately and once more after
                // WPF's deferred layout pass) pins the tile on mixed-DPI setups.
                ApplyDevicePixelBounds(handle, cellLeft, cellTop, cellRight, cellBottom);
                ApplyDevicePixelBounds(handle, cellLeft, cellTop, cellRight, cellBottom);
                window.Dispatcher.BeginInvoke(
                    new Action(() => ApplyDevicePixelBounds(handle, cellLeft, cellTop, cellRight, cellBottom)),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                window.Left = (monitor.Left + column * cellWidth) / dipScale;
                window.Top = (monitor.Top + row * cellHeight) / dipScale;
                window.Width = cellWidth / dipScale;
                window.Height = cellHeight / dipScale;
            }
        }
    }

    public static bool IsOnSecondaryMonitor(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return false;

        IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return false;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info) && (info.Flags & MonitorInfoPrimary) == 0;
    }

    private static void ApplyDevicePixelBounds(IntPtr handle, int left, int top, int right, int bottom) =>
        SetWindowPos(
            handle,
            IntPtr.Zero,
            left,
            top,
            Math.Max(1, right - left),
            Math.Max(1, bottom - top),
            SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);

    private static IReadOnlyList<MonitorBounds> EnumerateMonitors()
    {
        var monitors = new List<MonitorBounds>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                monitors.Add(new MonitorBounds(
                    info.Work.Left,
                    info.Work.Top,
                    Math.Max(1, info.Work.Right - info.Work.Left),
                    Math.Max(1, info.Work.Bottom - info.Work.Top),
                    (info.Flags & MonitorInfoPrimary) != 0,
                    MonitorDipScale(monitor)));
            }

            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    private static double MonitorDipScale(IntPtr monitor)
    {
        try
        {
            return GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0
                ? dpiX / 96.0
                : 1.0;
        }
        catch (DllNotFoundException)
        {
            return 1.0;
        }
        catch (EntryPointNotFoundException)
        {
            return 1.0;
        }
    }

    private static MonitorBounds PrimaryWorkAreaFallback() =>
        new(
            SystemParameters.WorkArea.Left,
            SystemParameters.WorkArea.Top,
            Math.Max(1, SystemParameters.WorkArea.Width),
            Math.Max(1, SystemParameters.WorkArea.Height),
            true,
            1.0);

    private const int MonitorInfoPrimary = 1;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clip,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed record MonitorBounds(
        double Left,
        double Top,
        double Width,
        double Height,
        bool IsPrimary,
        double DipScale);
}
