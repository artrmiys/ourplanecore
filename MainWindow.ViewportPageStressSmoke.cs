using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private const string ViewportPageStressSmokeEnv = "OURPLANECORE_VIEWPORT_PAGE_STRESS_SMOKE";
    private const string ViewportPageStressSmokeReportEnv = "OURPLANECORE_VIEWPORT_PAGE_STRESS_REPORT";
    private const string ViewportPageStressSmokeTimeoutEnv = "OURPLANECORE_VIEWPORT_PAGE_STRESS_TIMEOUT_MS";
    private const string ViewportPageStressSmokeOpenCountEnv = "OURPLANECORE_VIEWPORT_PAGE_STRESS_OPEN_COUNT";
    private const string ViewportPageStressSmokeReturnCountEnv = "OURPLANECORE_VIEWPORT_PAGE_STRESS_RETURN_COUNT";
    private const string ViewportPageStressSmokeTabCountEnv = "OURPLANECORE_VIEWPORT_PAGE_STRESS_TAB_COUNT";
    private const string ViewportPageStressSmokeTargetZoomEnv = "OURPLANECORE_VIEWPORT_PAGE_STRESS_TARGET_ZOOM";
    private const string ViewportPageStressSmokePanStepsEnv = "OURPLANECORE_VIEWPORT_PAGE_STRESS_PAN_STEPS";

    private async Task TryRunViewportPageStressSmokeAsync()
    {
        if (!IsTruthyEnvironment(ViewportPageStressSmokeEnv))
            return;

        var report = new ViewportPageStressSmokeReport
        {
            StartedUtc = DateTime.UtcNow,
        };
        int exitCode = 0;
        try
        {
            await RunViewportPageStressSmokeAsync(report);
            report.Passed = report.Failures.Count == 0;
            exitCode = report.Passed ? 0 : 2;
        }
        catch (Exception ex)
        {
            report.Failures.Add(ex.ToString());
            report.Passed = false;
            exitCode = 2;
        }
        finally
        {
            if (ViewportPerformanceRecorder.IsActive)
                report.Performance = ViewportPerformanceRecorder.EndRun();
            report.FinishedUtc = DateTime.UtcNow;
            WriteViewportPageStressSmokeReport(report);
            Application.Current.Shutdown(exitCode);
        }
    }

    private async Task RunViewportPageStressSmokeAsync(ViewportPageStressSmokeReport report)
    {
        if (_currentJob == null)
            throw new InvalidOperationException("No current job was opened before viewport page stress smoke.");

        ViewportPerformanceRecorder.BeginRun(_currentJob.RootPath, "viewport-page-stress-smoke");
        int timeoutMs = ReadEnvironmentInt(ViewportPageStressSmokeTimeoutEnv, 8000, 1000, 60000);
        int returnCount = ReadEnvironmentInt(ViewportPageStressSmokeReturnCountEnv, 6, 1, 50);
        int tabCount = ReadEnvironmentInt(ViewportPageStressSmokeTabCountEnv, 5, 1, 20);
        List<PageInfo> pages = CollectPagesUnder(_currentJob.PagesRoot).ToList();
        if (pages.Count == 0)
            throw new InvalidOperationException("No pages were found for viewport page stress smoke.");
        int openCount = ReadEnvironmentInt(ViewportPageStressSmokeOpenCountEnv, pages.Count, 1, pages.Count);
        IReadOnlyList<PageInfo> openPages = SelectSmokeSamplePages(pages, openCount);

        report.JobPath = _currentJob.RootPath;
        report.PageCount = pages.Count;
        report.OpenSampleCount = openPages.Count;
        foreach (PageInfo page in openPages)
        {
            PageSmokeResult result = await OpenAndProbePageAsync(page, timeoutMs, "open");
            report.OpenResults.Add(result);
            if (!result.Passed)
                report.Failures.Add($"{result.Stage} failed for {result.PageName}: {result.Error}");
        }

        foreach (PageInfo page in SelectSmokeSamplePages(pages, returnCount))
        {
            PageSmokeResult result = await OpenAndProbePageAsync(page, timeoutMs, "return");
            report.ReturnResults.Add(result);
            if (!result.Passed)
                report.Failures.Add($"{result.Stage} failed for {result.PageName}: {result.Error}");
        }

        foreach (PageInfo page in SelectSmokeSamplePages(pages, tabCount))
        {
            PageSmokeResult result = await OpenAndProbePageInNewTabAsync(page, timeoutMs);
            report.TabResults.Add(result);
            if (!result.Passed)
                report.Failures.Add($"{result.Stage} failed for {result.PageName}: {result.Error}");
        }

        foreach (PageTabState tab in _pageTabs.ToList())
        {
            PageInfo? page = OurPlaneCoreJobStore.TryReadPage(tab.PageFolder);
            if (page == null)
                continue;

            PageSmokeResult result = await ActivateAndProbeTabAsync(tab, page, timeoutMs);
            report.TabReturnResults.Add(result);
            if (!result.Passed)
                report.Failures.Add($"{result.Stage} failed for {result.PageName}: {result.Error}");
        }
    }

    private async Task<PageSmokeResult> OpenAndProbePageAsync(PageInfo page, int timeoutMs, string stage)
    {
        var result = CreatePageSmokeResult(page, stage);
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            OpenPageInActiveTab(page);
            await WaitForViewportPageRenderAsync(page, timeoutMs);
            result.RenderReadyMs = watch.ElapsedMilliseconds;
            await ExerciseViewportZoomAsync();
            await WaitForViewportPageRenderAsync(page, timeoutMs);
            result.VisualProbe = ProbeViewportSurfaceOpacity();
            result.Passed = result.VisualProbe.Passed;
            result.Error = result.VisualProbe.Error;
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.Error = ex.Message;
        }
        finally
        {
            watch.Stop();
            result.ElapsedMs = watch.ElapsedMilliseconds;
        }

        return result;
    }

    private async Task<PageSmokeResult> OpenAndProbePageInNewTabAsync(PageInfo page, int timeoutMs)
    {
        var result = CreatePageSmokeResult(page, "new_tab");
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            OpenPageInNewTab(page);
            await WaitForViewportPageRenderAsync(page, timeoutMs);
            result.RenderReadyMs = watch.ElapsedMilliseconds;
            await ExerciseViewportZoomAsync();
            result.VisualProbe = ProbeViewportSurfaceOpacity();
            result.Passed = result.VisualProbe.Passed;
            result.Error = result.VisualProbe.Error;
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.Error = ex.Message;
        }
        finally
        {
            watch.Stop();
            result.ElapsedMs = watch.ElapsedMilliseconds;
        }

        return result;
    }

    private async Task<PageSmokeResult> ActivateAndProbeTabAsync(PageTabState tab, PageInfo page, int timeoutMs)
    {
        var result = CreatePageSmokeResult(page, "tab_return");
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            ActivatePageTab(tab, page);
            await WaitForViewportPageRenderAsync(page, timeoutMs);
            result.RenderReadyMs = watch.ElapsedMilliseconds;
            result.VisualProbe = ProbeViewportSurfaceOpacity();
            result.Passed = result.VisualProbe.Passed;
            result.Error = result.VisualProbe.Error;
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.Error = ex.Message;
        }
        finally
        {
            watch.Stop();
            result.ElapsedMs = watch.ElapsedMilliseconds;
        }

        return result;
    }

    private async Task WaitForViewportPageRenderAsync(PageInfo page, int timeoutMs)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (_viewport.IsPageRenderReady(page.FolderPath))
                return;

            await Task.Delay(50);
        }

        throw new TimeoutException($"Viewport did not render '{page.Name}' within {timeoutMs} ms.");
    }

    private async Task ExerciseViewportZoomAsync()
    {
        var start = _viewport.CaptureViewState();
        float targetZoom = ReadEnvironmentFloat(ViewportPageStressSmokeTargetZoomEnv, 0, 0, 20);
        if (targetZoom > 0)
        {
            int panSteps = ReadEnvironmentInt(ViewportPageStressSmokePanStepsEnv, 4, 0, 20);
            _viewport.RestoreViewState(new PdfViewport.ViewState(targetZoom, start.PanX, start.PanY));
            await YieldViewportSmokeFrameAsync();
            for (int i = 0; i < panSteps; i++)
            {
                var current = _viewport.CaptureViewState();
                _viewport.RestoreViewState(new PdfViewport.ViewState(
                    targetZoom,
                    current.PanX + 36,
                    current.PanY + 24));
                await YieldViewportSmokeFrameAsync();
            }

            _viewport.RestoreViewState(start);
            await YieldViewportSmokeFrameAsync();
            return;
        }

        _viewport.ZoomIn();
        await YieldViewportSmokeFrameAsync();
        _viewport.ZoomIn();
        await YieldViewportSmokeFrameAsync();
        var zoomed = _viewport.CaptureViewState();
        _viewport.RestoreViewState(new PdfViewport.ViewState(zoomed.Zoom, zoomed.PanX + 48, zoomed.PanY + 36));
        await YieldViewportSmokeFrameAsync();
        _viewport.ZoomOut();
        await YieldViewportSmokeFrameAsync();
        _viewport.RestoreViewState(start);
        await YieldViewportSmokeFrameAsync();
    }

    private async Task YieldViewportSmokeFrameAsync()
    {
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        await Task.Delay(35);
    }

    private ViewportVisualProbe ProbeViewportSurfaceOpacity()
    {
        if (ViewportSurfaceHost.ActualWidth < 2 || ViewportSurfaceHost.ActualHeight < 2)
            return new ViewportVisualProbe(false, "ViewportSurfaceHost has empty bounds.", []);

        int width = Math.Max(1, (int)Math.Round(ViewportSurfaceHost.ActualWidth));
        int height = Math.Max(1, (int)Math.Round(ViewportSurfaceHost.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(ViewportSurfaceHost);

        (int X, int Y)[] points =
        {
            (width / 2, height / 2),
            (width / 4, height / 4),
            ((width * 3) / 4, height / 4),
            (width / 4, (height * 3) / 4),
            ((width * 3) / 4, (height * 3) / 4),
        };

        var samples = new List<ViewportPixelSample>();
        foreach ((int x, int y) in points)
        {
            byte[] pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            samples.Add(new ViewportPixelSample(x, y, pixel[2], pixel[1], pixel[0], pixel[3]));
        }

        ViewportPixelSample? transparent = samples.FirstOrDefault(sample => sample.Alpha < 250);
        if (transparent != null)
            return new ViewportVisualProbe(false, $"Viewport sample alpha was {transparent.Alpha} at {transparent.X},{transparent.Y}.", samples);

        return new ViewportVisualProbe(true, "", samples);
    }

    private static IReadOnlyList<PageInfo> SelectSmokeSamplePages(IReadOnlyList<PageInfo> pages, int count)
    {
        if (pages.Count <= count)
            return pages.ToList();

        var indexes = new SortedSet<int> { 0, pages.Count / 2, pages.Count - 1 };
        double step = (double)(pages.Count - 1) / Math.Max(1, count - 1);
        for (int i = 0; indexes.Count < count && i < count; i++)
            indexes.Add((int)Math.Round(i * step));

        for (int i = 0; indexes.Count < count && i < pages.Count; i++)
            indexes.Add(i);

        return indexes.Take(count).Select(index => pages[index]).ToList();
    }

    private static PageSmokeResult CreatePageSmokeResult(PageInfo page, string stage) =>
        new()
        {
            Stage = stage,
            PageName = page.Name,
            PageFolder = page.FolderPath,
        };

    private static bool IsTruthyEnvironment(string name)
    {
        string value = Environment.GetEnvironmentVariable(name) ?? "";
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadEnvironmentInt(string name, int fallback, int min, int max)
    {
        string value = Environment.GetEnvironmentVariable(name) ?? "";
        return int.TryParse(value, out int parsed) ? Math.Clamp(parsed, min, max) : fallback;
    }

    private static float ReadEnvironmentFloat(string name, float fallback, float min, float max)
    {
        string value = Environment.GetEnvironmentVariable(name) ?? "";
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }

    private static void WriteViewportPageStressSmokeReport(ViewportPageStressSmokeReport report)
    {
        string path = Environment.GetEnvironmentVariable(ViewportPageStressSmokeReportEnv) ?? "";
        if (string.IsNullOrWhiteSpace(path))
            path = DefaultViewportPageStressSmokeReportPath(report);
        if (string.IsNullOrWhiteSpace(path))
            return;

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(report, options));
        AppLog.Info($"Viewport page stress smoke report written: {path}");
    }

    private static string DefaultViewportPageStressSmokeReportPath(ViewportPageStressSmokeReport report)
    {
        if (string.IsNullOrWhiteSpace(report.JobPath))
            return "";

        string stamp = report.StartedUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(report.JobPath, "AI_Context", "perf_runs", $"viewport_page_stress_{stamp}.json");
    }

    private sealed class ViewportPageStressSmokeReport
    {
        public bool Passed { get; set; }
        public string JobPath { get; set; } = "";
        public int PageCount { get; set; }
        public int OpenSampleCount { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }
        public List<PageSmokeResult> OpenResults { get; } = [];
        public List<PageSmokeResult> ReturnResults { get; } = [];
        public List<PageSmokeResult> TabResults { get; } = [];
        public List<PageSmokeResult> TabReturnResults { get; } = [];
        public List<string> Failures { get; } = [];
        public ViewportPerformanceRun? Performance { get; set; }
    }

    private sealed class PageSmokeResult
    {
        public string Stage { get; set; } = "";
        public string PageName { get; set; } = "";
        public string PageFolder { get; set; } = "";
        public long RenderReadyMs { get; set; }
        public long ElapsedMs { get; set; }
        public bool Passed { get; set; }
        public string Error { get; set; } = "";
        public ViewportVisualProbe VisualProbe { get; set; } = ViewportVisualProbe.Empty;
    }

    private sealed record ViewportVisualProbe(
        bool Passed,
        string Error,
        IReadOnlyList<ViewportPixelSample> Samples)
    {
        public static ViewportVisualProbe Empty { get; } = new(true, "", []);
    }

    private sealed record ViewportPixelSample(int X, int Y, byte Red, byte Green, byte Blue, byte Alpha);
}
