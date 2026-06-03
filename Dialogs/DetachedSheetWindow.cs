using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public sealed class DetachedSheetWindow : Window
{
    private readonly PdfViewport _viewport = new();
    public PdfViewport Viewport => _viewport;
    public PageInfo Page { get; }

    public DetachedSheetWindow(
        OurPlaneCoreJob job,
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
        OurPlaneCoreJob job,
        IReadOnlyList<TakeoffItem> takeoffItems,
        AppSettings settings,
        UnitMode unitMode)
    {
        _viewport.SetMeasurements(takeoffItems.SelectMany(takeoff => takeoff.Measurements));
        _viewport.SetHiddenTakeoffFolders(HiddenTakeoffFolders(job, Page, takeoffItems));
        _viewport.SetSheetLegend(settings.ShowSheetLegend
            ? SheetLegendBuilder.Build(job, Page, takeoffItems, unitMode)
            : []);
    }

    private void ConfigureViewport(
        OurPlaneCoreJob job,
        PageInfo page,
        IReadOnlyList<TakeoffItem> takeoffItems,
        AppSettings settings,
        UnitMode unitMode)
    {
        _viewport.ViewBackgroundColor = settings.ViewportBackground;
        _viewport.PageBackgroundColor = settings.PageBackground;
        _viewport.ScaleMetersPerPt = page.ScaleMetersPerPt;
        _viewport.UnitMode = unitMode;
        _viewport.ShowMeasurementLabels = settings.ShowMeasurementLabels;
        _viewport.ShowLineLabels = settings.ShowLineLabels;
        _viewport.ShowAreaLabels = settings.ShowAreaLabels;
        _viewport.ShowCountLabels = settings.ShowCountLabels;
        _viewport.MeasurementLabelScale = ClampScale(settings.MeasurementLabelScale);
        _viewport.MeasurementStrokeScale = ClampScale(settings.ViewportMeasurementStrokeScale);
        _viewport.RulerStrokeWidth = Math.Clamp(settings.ViewportRulerStrokeWidth, 0.5, 6.0);
        _viewport.PointSizeScale = ClampScale(settings.ViewportPointSizeScale);
        _viewport.SheetLegendAnchor = settings.SheetLegendAnchor;
        _viewport.SheetLegendScale = ClampScale(settings.SheetLegendScale);
        _viewport.SheetHeaderScale = ClampScale(settings.SheetHeaderScale);
        _viewport.ScaleSheetOverlaysWithPage = settings.ScaleSheetOverlaysWithPage;
        _viewport.ScaleMeasurementLabelsWithPage = settings.ScaleMeasurementLabelsWithPage;
        _viewport.ScaleSheetHeaderWithPage = settings.ScaleSheetHeaderWithPage;
        _viewport.SimplifyNavigationRendering = settings.SimplifyViewportNavigation;
        _viewport.SetTool("pan");
        _viewport.SetMeasurements(takeoffItems.SelectMany(takeoff => takeoff.Measurements));
        _viewport.SetHiddenTakeoffFolders(HiddenTakeoffFolders(job, page, takeoffItems));

        _viewport.LoadPage(
            page.PdfPath,
            page.PdfPage,
            page.FolderPath,
            page.PdfLayersCached ? page.PdfLayers : null,
            rasterSheet: page.RasterSheet);
        _viewport.SetPageAnnotations(OurPlaneCoreJobStore.LoadPageAnnotations(page.FolderPath));
        _viewport.SetSheetLegend(settings.ShowSheetLegend
            ? SheetLegendBuilder.Build(job, page, takeoffItems, unitMode)
            : []);
    }

    private static IReadOnlyList<string> HiddenTakeoffFolders(
        OurPlaneCoreJob job,
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

    private static string NormalizeLegendOrderKey(OurPlaneCoreJob job, string value)
    {
        string clean = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return "";

        if (Path.IsPathFullyQualified(clean))
        {
            string full = NormalizePath(clean);
            if (OurPlaneCoreJobStore.IsSameOrDescendant(job.TakeoffsRoot, full))
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
            : Math.Clamp(scale, 0.5, 3.0);
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
        TileIntoBounds(windows, monitor.Left, monitor.Top, monitor.Width, monitor.Height, verticalStack);
        return monitor.IsPrimary ? "primary monitor" : "monitor 2";
    }

    private static void TileIntoBounds(
        IReadOnlyList<Window> windows,
        double left,
        double top,
        double width,
        double height,
        bool verticalStack)
    {
        int count = Math.Min(64, windows.Count);
        int columns = verticalStack
            ? 1
            : Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
        int rows = verticalStack
            ? count
            : Math.Max(1, (int)Math.Ceiling(count / (double)columns));
        double cellWidth = Math.Max(1, width / columns);
        double cellHeight = Math.Max(1, height / rows);

        for (int i = 0; i < count; i++)
        {
            int row = i / columns;
            int column = i % columns;
            Window window = windows[i];
            window.WindowState = WindowState.Normal;
            window.MinWidth = Math.Min(window.MinWidth, cellWidth);
            window.MinHeight = Math.Min(window.MinHeight, cellHeight);
            window.Left = left + column * cellWidth;
            window.Top = top + row * cellHeight;
            window.Width = cellWidth;
            window.Height = cellHeight;
        }
    }

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
                    (info.Flags & MonitorInfoPrimary) != 0));
            }

            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    private static MonitorBounds PrimaryWorkAreaFallback() =>
        new(
            SystemParameters.WorkArea.Left,
            SystemParameters.WorkArea.Top,
            Math.Max(1, SystemParameters.WorkArea.Width),
            Math.Max(1, SystemParameters.WorkArea.Height),
            true);

    private const int MonitorInfoPrimary = 1;

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clip,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

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

    private sealed record MonitorBounds(double Left, double Top, double Width, double Height, bool IsPrimary);
}
