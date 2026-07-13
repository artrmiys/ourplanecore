using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OurPlanCore;

public partial class MainWindow
{
    private const string GuideScreenshotCaptureEnv = "OURPLANCORE_GUIDE_SCREENSHOT_CAPTURE";
    private const string GuideScreenshotDirEnv = "OURPLANCORE_GUIDE_SCREENSHOT_DIR";
    private const string GuideScreenshotManifestEnv = "OURPLANCORE_GUIDE_SCREENSHOT_MANIFEST";
    private const string GuideScreenshotTimeoutEnv = "OURPLANCORE_GUIDE_SCREENSHOT_TIMEOUT_MS";

    /// <summary>
    /// Headless-style capture driver: when the capture env var is set, the app opens the
    /// sample job (via persisted settings), walks every workspace surface, renders each one
    /// to a real PNG with <see cref="RenderTargetBitmap"/>, writes a manifest, then shuts down.
    /// These PNGs are the source assets embedded into the guide sample project.
    /// </summary>
    private async Task TryRunGuideScreenshotCaptureAsync()
    {
        if (!IsTruthyEnvironment(GuideScreenshotCaptureEnv))
            return;

        string outputDir = AppIdentity.GetEnvironmentVariable(GuideScreenshotDirEnv) ?? "";
        if (string.IsNullOrWhiteSpace(outputDir))
            outputDir = Path.Combine(Path.GetTempPath(), "onc_guide_screenshots");
        Directory.CreateDirectory(outputDir);

        var manifest = new GuideScreenshotManifest
        {
            StartedUtc = DateTime.UtcNow,
            OutputDir = outputDir,
        };
        int exitCode = 0;
        try
        {
            int timeoutMs = ReadEnvironmentInt(GuideScreenshotTimeoutEnv, 12000, 1000, 60000);
            await CaptureGuideScreenshotsAsync(outputDir, timeoutMs, manifest);
            manifest.Passed = manifest.Failures.Count == 0;
            exitCode = manifest.Passed ? 0 : 2;
        }
        catch (Exception ex)
        {
            manifest.Failures.Add(ex.ToString());
            manifest.Passed = false;
            exitCode = 2;
        }
        finally
        {
            manifest.FinishedUtc = DateTime.UtcNow;
            WriteGuideScreenshotManifest(manifest);
            Application.Current.Shutdown(exitCode);
        }
    }

    private async Task CaptureGuideScreenshotsAsync(string outputDir, int timeoutMs, GuideScreenshotManifest manifest)
    {
        // Make sure the window is laid out before any capture.
        await SettleFramesAsync(6);

        Activate();
        await SettleFramesAsync(2);

        // Capture is self-contained: build a fresh sample job and open it so the screenshots
        // always reflect the same deterministic content the guide ships with.
        string sampleRoot = Path.Combine(Path.GetTempPath(), "onc_guide_capture_" + Guid.NewGuid().ToString("N"));
        OurPlanCoreJob sample = SampleJobService.CreateSampleJob(sampleRoot);
        manifest.JobPath = sample.RootPath;
        OpenJob(sample.RootPath);
        await SettleFramesAsync(4);

        PageInfo? planPage = CollectPagesUnder(_currentJob?.PagesRoot ?? "")
            .FirstOrDefault(page => page.Name.Contains("A101", StringComparison.OrdinalIgnoreCase));
        planPage ??= CollectPagesUnder(_currentJob?.PagesRoot ?? "").FirstOrDefault();

        SelectWorkspaceTab("MainView");
        await SettleFramesAsync(3);
        if (planPage != null)
        {
            OpenPageInActiveTab(planPage);
            try
            {
                await WaitForViewportPageRenderAsync(planPage, timeoutMs);
            }
            catch
            {
                // Capture whatever rendered; record nothing fatal so the rest of the surfaces still run.
            }
            await SettleFramesAsync(3);
        }

        // Top ribbons (PDF Output is installed programmatically between Page and Viewport).
        await CaptureTopRibbonAsync(outputDir, manifest, "Main", "10-ribbon-main");
        await CaptureTopRibbonAsync(outputDir, manifest, "Page", "11-ribbon-page");
        await CaptureTopRibbonAsync(outputDir, manifest, "PDF Output", "12-ribbon-pdf-output");
        await CaptureTopRibbonAsync(outputDir, manifest, "Viewport", "13-ribbon-viewport");

        // Main workspace with each side panel surfaced.
        SelectWorkspaceTab("MainView");
        await SettleFramesAsync(3);
        SelectPagesSideTab("Pages");
        await SettleFramesAsync(3);
        await CaptureVisualAsync(outputDir, manifest, RootFrame, "01-main-workspace");

        SelectPagesSideTab("PDF Layers");
        await SettleFramesAsync(3);
        await CaptureVisualAsync(outputDir, manifest, RootFrame, "02-pages-and-layers");
        SelectPagesSideTab("Pages");
        await SettleFramesAsync(2);

        // Focused crops so the guide can call out buttons clearly. Rendering a deep child visual
        // in isolation comes out blank in WPF, so we render the root and crop the child's bounds.
        await CaptureCropAsync(outputDir, manifest, TakeoffsPanel, "03-takeoffs-panel");
        await CaptureCropAsync(outputDir, manifest, BottomToolStrip, "04-viewport-toolbar");

        // Manager / settings surfaces.
        await CaptureWorkspaceTabAsync(outputDir, manifest, "SheetManager", "20-sheet-manager", timeoutMs);
        await CaptureWorkspaceTabAsync(outputDir, manifest, "TakeoffManager", "21-takeoff-manager", timeoutMs);
        await CaptureWorkspaceTabAsync(outputDir, manifest, "ReportBuilder", "22-report-builder", timeoutMs);
        await CaptureWorkspaceTabAsync(outputDir, manifest, "MaterialsManager", "23-materials", timeoutMs);
        await CaptureWorkspaceTabAsync(outputDir, manifest, "AiManager", "24-ai-manager", timeoutMs);
        await CaptureThreeDAsync(outputDir, manifest, "25-3d");
        await CaptureWorkspaceTabAsync(outputDir, manifest, "SettingsManager", "26-settings", timeoutMs);

        SelectWorkspaceTab("MainView");
        await SettleFramesAsync(2);
    }

    private async Task CaptureTopRibbonAsync(string outputDir, GuideScreenshotManifest manifest, string header, string name)
    {
        try
        {
            SelectTopTab(header);
            await SettleFramesAsync(3);
            await CaptureVisualAsync(outputDir, manifest, TopMainTabs, name);
        }
        catch (Exception ex)
        {
            manifest.Failures.Add($"{name}: {ex.Message}");
        }
    }

    private async Task CaptureWorkspaceTabAsync(
        string outputDir,
        GuideScreenshotManifest manifest,
        string tabKey,
        string name,
        int timeoutMs)
    {
        try
        {
            SelectWorkspaceTab(tabKey);
            await SettleFramesAsync(5);
            await CaptureVisualAsync(outputDir, manifest, RootFrame, name);
        }
        catch (Exception ex)
        {
            manifest.Failures.Add($"{name}: {ex.Message}");
        }
    }

    private async Task CaptureThreeDAsync(string outputDir, GuideScreenshotManifest manifest, string name)
    {
        try
        {
            // The sample job already ships a finished 3D model (walls + gabled roof + dormers),
            // built and saved by SampleThreeDModelBuilder and loaded on job open. Just show it.
            SelectWorkspaceTab("3DManager");
            await SettleFramesAsync(3);
            try
            {
                SetThreeDRoofEdgeSelectMode(enabled: false);
                _selectedThreeDRoofGuideIds.Clear();
                _selectedThreeDRoofGuideId = "";
                SetActiveThreeDRoofGroup("", render: false);
                RenderThreeDWallModel(fitCamera: true);
            }
            catch { }
            await SettleFramesAsync(2);
            try { Btn3dViewerIso_Click(this, new RoutedEventArgs()); } catch { }
            await SettleFramesAsync(4);
            await CaptureVisualAsync(outputDir, manifest, RootFrame, name);
        }
        catch (Exception ex)
        {
            manifest.Failures.Add($"{name}: {ex.Message}");
        }
    }

    private void SelectPagesSideTab(string header)
    {
        foreach (TabItem item in PagesSideTabs.Items.OfType<TabItem>())
        {
            if (string.Equals(item.Header?.ToString(), header, StringComparison.OrdinalIgnoreCase))
            {
                PagesSideTabs.SelectedItem = item;
                return;
            }
        }
    }

    private async Task CaptureVisualAsync(string outputDir, GuideScreenshotManifest manifest, FrameworkElement element, string name)
    {
        try
        {
            await SettleFramesAsync(2);
            int width = Math.Max(1, (int)Math.Round(element.ActualWidth));
            int height = Math.Max(1, (int)Math.Round(element.ActualHeight));
            if (width < 4 || height < 4)
            {
                manifest.Failures.Add($"{name}: element had empty bounds ({width}x{height}).");
                return;
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            string path = Path.Combine(outputDir, name + ".png");
            using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            encoder.Save(stream);

            manifest.Screenshots.Add(new GuideScreenshotEntry
            {
                Name = name,
                Path = path,
                Width = width,
                Height = height,
            });
        }
        catch (Exception ex)
        {
            manifest.Failures.Add($"{name}: {ex.Message}");
        }
    }

    private async Task CaptureCropAsync(string outputDir, GuideScreenshotManifest manifest, FrameworkElement element, string name)
    {
        try
        {
            await SettleFramesAsync(2);
            int rootW = Math.Max(1, (int)Math.Round(RootFrame.ActualWidth));
            int rootH = Math.Max(1, (int)Math.Round(RootFrame.ActualHeight));
            var full = new RenderTargetBitmap(rootW, rootH, 96, 96, PixelFormats.Pbgra32);
            full.Render(RootFrame);

            GeneralTransform transform = element.TransformToVisual(RootFrame);
            Rect bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            var crop = new Int32Rect(
                Math.Clamp((int)Math.Floor(bounds.X), 0, rootW - 1),
                Math.Clamp((int)Math.Floor(bounds.Y), 0, rootH - 1),
                0,
                0);
            crop.Width = Math.Clamp((int)Math.Ceiling(bounds.Width), 1, rootW - crop.X);
            crop.Height = Math.Clamp((int)Math.Ceiling(bounds.Height), 1, rootH - crop.Y);
            if (crop.Width < 4 || crop.Height < 4)
            {
                manifest.Failures.Add($"{name}: crop bounds too small ({crop.Width}x{crop.Height}).");
                return;
            }

            var cropped = new CroppedBitmap(full, crop);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(cropped));

            string path = Path.Combine(outputDir, name + ".png");
            using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            encoder.Save(stream);

            manifest.Screenshots.Add(new GuideScreenshotEntry
            {
                Name = name,
                Path = path,
                Width = crop.Width,
                Height = crop.Height,
            });
        }
        catch (Exception ex)
        {
            manifest.Failures.Add($"{name}: {ex.Message}");
        }
    }

    private async Task SettleFramesAsync(int frames)
    {
        for (int i = 0; i < Math.Max(1, frames); i++)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(40);
        }
    }

    private static void WriteGuideScreenshotManifest(GuideScreenshotManifest manifest)
    {
        string path = AppIdentity.GetEnvironmentVariable(GuideScreenshotManifestEnv) ?? "";
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(manifest.OutputDir, "manifest.json");

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, options));
    }

    private sealed class GuideScreenshotManifest
    {
        public bool Passed { get; set; }
        public string OutputDir { get; set; } = "";
        public string JobPath { get; set; } = "";
        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }
        public List<GuideScreenshotEntry> Screenshots { get; } = [];
        public List<string> Failures { get; } = [];
    }

    private sealed class GuideScreenshotEntry
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
