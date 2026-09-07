using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private const int ImportedPdfRasterDpi = 150;

    private sealed record PdfImportPlan(
        string PdfPath,
        int PageCount,
        IReadOnlyList<string> DefaultPageNames)
    {
        public IReadOnlyList<string> PageNames { get; set; } = DefaultPageNames;
    }

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            () => ImportPdfAsync(sender),
            "Import PDF failed.",
            "Import PDF");
    }

    private async void BtnImportPdfFolder_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            () => ImportPdfFolderToCurrentJobAsync(sender),
            "Import PDF folder failed.",
            "Import PDF Folder");
    }

    private async Task ImportPdfAsync(object sender)
    {
        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before importing PDFs.");
            return;
        }
        OurPlanCoreJob importJob = _currentJob;
        if (!EnsureExpectedJobWritable(importJob, "import PDF pages"))
            return;

        var dlg = new OpenFileDialog
        {
            Title = "Import PDF(s)",
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;
        if (!EnsureExpectedJobWritable(importJob, "import PDF pages"))
            return;

        IReadOnlyList<string> pdfPaths = SelectedPdfPaths(dlg);
        if (pdfPaths.Count == 0)
            return;

        await ImportPdfPathsAsync(pdfPaths, sender, expectedJob: importJob);
    }

    private async Task ImportPdfFolderToCurrentJobAsync(object sender)
    {
        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before importing a PDF folder.");
            return;
        }
        OurPlanCoreJob importJob = _currentJob;
        if (!EnsureExpectedJobWritable(importJob, "import a PDF folder"))
            return;

        string? folder = SelectFolder("Select folder with PDF files", NewPdfImportInitialFolder());
        if (folder == null)
            return;
        if (!EnsureExpectedJobWritable(importJob, "import a PDF folder"))
            return;

        IReadOnlyList<string> pdfPaths = PdfImportSourceFinder.FindPdfFilesRecursive(folder);
        if (pdfPaths.Count == 0)
        {
            PostStatusInfo("No PDF files were found in the selected folder or its subfolders.");
            return;
        }

        await ImportPdfPathsAsync(pdfPaths, sender, confirmPageNames: false, expectedJob: importJob);
    }

    private async Task ImportPdfPathsAsync(
        IReadOnlyList<string> pdfPaths,
        object sender,
        bool confirmPageNames = true,
        OurPlanCoreJob? expectedJob = null)
    {
        expectedJob ??= _currentJob;
        if (expectedJob == null || !EnsureExpectedJobWritable(expectedJob, "import PDF pages"))
            return;

        IReadOnlyList<PdfImportPlan> plans = BuildPdfImportPlans(pdfPaths, out IReadOnlyList<string> skipped);
        if (plans.Count == 0)
        {
            PostStatusWarning("Could not read any pages from the selected PDF file(s).");
            return;
        }

        if (confirmPageNames && !ConfirmPdfImportPageNames(plans, skipped))
            return;
        if (!EnsureExpectedJobWritable(expectedJob, "import PDF pages"))
            return;

        bool? buildRasterCache = ConfirmPdfImportRasterOption(plans.Sum(plan => plan.PageCount));
        if (!buildRasterCache.HasValue)
            return;
        if (!EnsureExpectedJobWritable(expectedJob, "import PDF pages"))
            return;

        await ImportPdfPlansAsync(plans, skipped, sender, buildRasterCache.Value, expectedJob);
    }

    private string? NewPdfImportInitialFolder()
    {
        if (HasCurrentPackageSession)
        {
            string? artifactParent = Path.GetDirectoryName(_currentPackageSession!.PackagePath);
            if (!string.IsNullOrWhiteSpace(artifactParent) && Directory.Exists(artifactParent))
                return artifactParent;
        }

        if (_currentJob?.RootPath is { } root && Directory.Exists(root))
            return root;

        if (!string.IsNullOrWhiteSpace(_settings.JobsRootPath) && Directory.Exists(_settings.JobsRootPath))
            return _settings.JobsRootPath;

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Directory.Exists(desktop) ? desktop : null;
    }

    private IReadOnlyList<PdfImportPlan> BuildPdfImportPlans(
        IReadOnlyList<string> pdfPaths,
        out IReadOnlyList<string> skipped)
    {
        bool multiPdf = pdfPaths.Count > 1;
        var plans = new List<PdfImportPlan>();
        var skippedList = new List<string>();

        foreach (string pdfPath in pdfPaths)
        {
            int pageCount = _viewport.GetPageCount(pdfPath);
            if (pageCount <= 0)
            {
                skippedList.Add(Path.GetFileName(pdfPath));
                continue;
            }

            plans.Add(new PdfImportPlan(
                pdfPath,
                pageCount,
                DefaultPageNames(pdfPath, pageCount, multiPdf)));
        }

        skipped = skippedList;
        return plans;
    }

    private bool ConfirmPdfImportPageNames(
        IReadOnlyList<PdfImportPlan> plans,
        IReadOnlyList<string> skipped)
    {
        int totalPages = plans.Sum(plan => plan.PageCount);
        string defaultNames = string.Join(Environment.NewLine, plans.SelectMany(plan => plan.DefaultPageNames));
        string prompt = plans.Count == 1
            ? $"PDF has {totalPages} page(s). Edit page names, one per line:"
            : $"Selected {plans.Count} PDF file(s), {totalPages} total page(s). Edit page names, one per line:";

        if (skipped.Count > 0)
            prompt += $"{Environment.NewLine}{Environment.NewLine}Skipped unreadable PDF(s): {string.Join(", ", skipped)}";

        string? rawNames = ShowMultilineInputDialog(prompt, defaultNames, "Page Names");
        if (rawNames == null)
            return false;

        string[] names = rawNames
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();

        if (names.Length != totalPages)
        {
            PostStatusWarning($"Expected {totalPages} page name(s), got {names.Length}.");
            return false;
        }

        int offset = 0;
        foreach (PdfImportPlan plan in plans)
        {
            plan.PageNames = names.Skip(offset).Take(plan.PageCount).ToArray();
            offset += plan.PageCount;
        }

        return true;
    }

    private async Task ImportPdfPlansAsync(
        IReadOnlyList<PdfImportPlan> plans,
        IReadOnlyList<string> skipped,
        object sender,
        bool buildRasterCache,
        OurPlanCoreJob importJob)
    {
        if (!EnsureExpectedJobWritable(importJob, "import PDF pages"))
            return;
        string destFolder = GetSelectedImportFolder();
        if (!OurPlanCoreJobStore.IsSameOrDescendant(importJob.PagesRoot, destFolder))
        {
            PostStatusWarning("PDF import cancelled because the destination no longer belongs to the originating job.");
            return;
        }
        Button? importButton = sender as Button;
        int totalPages = plans.Sum(plan => plan.PageCount);
        var createdPages = new List<PageInfo>();
        int rasterOk = 0;
        int rasterFailed = 0;

        try
        {
            if (importButton != null)
                importButton.IsEnabled = false;

            using (ShowBusyOverlay($"Importing {plans.Count} PDF file(s), {totalPages} page(s)..."))
            {
                await WaitForBusyOverlayRenderAsync();
                if (!EnsureExpectedJobWritable(importJob, "import PDF pages"))
                    return;
                bool hadUserPageExpansion = _expandedPageTreePaths.Count > 0;

                for (int index = 0; index < plans.Count; index++)
                {
                    if (!EnsureExpectedJobWritable(importJob, "continue PDF import"))
                        return;
                    PdfImportPlan plan = plans[index];
                    string pdfName = Path.GetFileName(plan.PdfPath);
                    var progress = new Progress<string>(msg =>
                    {
                        string text = $"PDF {index + 1}/{plans.Count}: {msg}";
                        TxtStatus.Text = text;
                        BusyOverlayText.Text = text;
                    });

                    ((IProgress<string>)progress).Report("copying pages...");
                    Dictionary<int, IReadOnlyList<PdfLayerInfo>> pdfLayerCache = [];

                    IReadOnlyList<PageInfo> created = OurPlanCoreJobStore.ImportPdf(
                        importJob,
                        plan.PdfPath,
                        plan.PageNames,
                        destFolder,
                        pdfLayerCache);
                    createdPages.AddRange(created);
                    if (buildRasterCache)
                    {
                        for (int pageIndex = 0; pageIndex < created.Count; pageIndex++)
                        {
                            PageInfo page = created[pageIndex];
                            ((IProgress<string>)progress).Report($"building {ImportedPdfRasterDpi}dpi raster {pageIndex + 1}/{created.Count}...");
                            RasterSheetBuildResult raster = await Task.Run(() => BuildAndWarmImportedRaster(page));
                            if (!EnsureExpectedJobWritable(importJob, "continue PDF raster import"))
                                return;
                            if (raster.Ok)
                                rasterOk++;
                            else
                            {
                                rasterFailed++;
                                AppLog.Warn($"Raster cache build failed during import for '{page.Name}': {raster.Error}");
                            }
                        }
                    }
                    BusyOverlayText.Text = $"Imported {pdfName} ({created.Count} page(s)).";
                }

                if (!EnsureExpectedJobWritable(importJob, "finish PDF import"))
                    return;
                ReloadPagesTree();
                if (createdPages.Count > 0)
                    SelectPageByFolder(createdPages[0].FolderPath);
                if (!hadUserPageExpansion)
                    CollapseTreeAndExpansionState(PagesTree, _expandedPageTreePaths);
            }

            string skippedText = skipped.Count > 0 ? $" Skipped {skipped.Count} unreadable PDF(s)." : "";
            TxtStatus.Text =
                $"Imported {createdPages.Count} page(s) from {plans.Count} PDF file(s). " +
                $"PDF layers load on demand from the PDF Layers tab." +
                (buildRasterCache ? $" Raster {ImportedPdfRasterDpi}dpi + First built: {rasterOk}, failed: {rasterFailed}." : "") +
                skippedText;

            if (createdPages.Count > 0)
            {
                if (!EnsureExpectedJobWritable(importJob, "apply imported PDF metadata"))
                    return;
                await TryApplySheetMetadataAfterPdfImportAsync(createdPages);
                if (!EnsureExpectedJobWritable(importJob, "finish imported PDF metadata"))
                    return;
                // Re-arm the job warmup so freshly imported pages get preview +
                // raster warmup without reopening the job.
                InvalidatePagePreviewPrefetchCache();
                QueueImportedPagePreviewWarmup(createdPages);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Import PDF failed.");
            MessageBox.Show($"Import failed:\n{ex.Message}", "Import PDF",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (importButton != null)
                importButton.IsEnabled = true;
        }
    }

    // Import builds no preview bitmaps, so the first open of each new page used
    // to pay a full cold render. Pre-render previews for the created pages on
    // the prefetch pool: the cold-open 0.20 scale for every page, plus the
    // readable 0.75 base for the leading pages the user opens next.
    private static void QueueImportedPagePreviewWarmup(IReadOnlyList<PageInfo> createdPages)
    {
        foreach (PageInfo page in createdPages)
        {
            PdfViewport.PrefetchPagePreview(
                page.PdfPath,
                page.PdfPage,
                ViewportRenderPolicy.ColdPageSwitchPreviewRenderScale);
        }

        foreach (PageInfo page in createdPages.Take(ViewportRenderPolicy.ImportedPageReadablePreviewWarmupCount))
        {
            PdfViewport.PrefetchPagePreview(
                page.PdfPath,
                page.PdfPage,
                ViewportRenderPolicy.InitialPagePreviewRenderScale);
        }

        if (createdPages.Count > ViewportRenderPolicy.ImportedPageReadablePreviewWarmupCount)
        {
            AppLog.Info(
                $"Imported readable preview warmup capped; created={createdPages.Count}; " +
                $"warmed={ViewportRenderPolicy.ImportedPageReadablePreviewWarmupCount}");
        }
    }

    private static IReadOnlyList<string> SelectedPdfPaths(OpenFileDialog dialog)
    {
        IEnumerable<string> paths = dialog.FileNames.Length > 0
            ? dialog.FileNames
            : [dialog.FileName];

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> DefaultPageNames(string pdfPath, int pageCount, bool includePdfName)
    {
        string pdfName = Path.GetFileNameWithoutExtension(pdfPath);
        if (!includePdfName)
            return Enumerable.Range(1, pageCount).Select(i => $"Page {i}").ToList();

        if (pageCount == 1)
            return [string.IsNullOrWhiteSpace(pdfName) ? "Page 1" : pdfName];

        string prefix = string.IsNullOrWhiteSpace(pdfName) ? "PDF" : pdfName.Trim();
        return Enumerable.Range(1, pageCount)
            .Select(i => $"{prefix} - Page {i}")
            .ToList();
    }

    private bool? ConfirmPdfImportRasterOption(int totalPages)
    {
        var win = new Window
        {
            Title = "Import PDF Options",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Import {totalPages} PDF page(s).",
            Margin = new Thickness(0, 0, 0, 8),
        });
        var rasterCheck = new CheckBox
        {
            Content = "Build readable raster cache and strict black-line snap index",
            IsChecked = _settings.BuildRasterCacheOnPdfImport,
            Margin = new Thickness(0, 0, 0, 4),
        };
        panel.Children.Add(rasterCheck);
        panel.Children.Add(new TextBlock
        {
            Text = $"Original PDFs stay as the source. Raster First uses {ImportedPdfRasterDpi} dpi for fast sheet open.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78,
            Margin = new Thickness(22, 0, 0, 8),
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var ok = new Button { Content = "Import", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 76, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        win.Content = panel;

        bool? result = null;
        ok.Click += (_, _) =>
        {
            result = rasterCheck.IsChecked == true;
            _settings.BuildRasterCacheOnPdfImport = result.Value;
            SaveAppSettings();
            win.DialogResult = true;
        };

        return win.ShowDialog() == true ? result : null;
    }

    private static RasterSheetBuildResult BuildAndWarmImportedRaster(PageInfo page)
    {
        float renderScale = RasterSheetCacheService.RasterDpiToRenderScale(ImportedPdfRasterDpi);
        RasterSheetBuildResult result = RasterSheetCacheService.BuildAndEnable(page, renderScale);
        if (!result.Ok)
            return result;

        PageInfo refreshed = OurPlanCoreJobStore.TryReadPage(page.FolderPath) ?? page;
        if (!RasterSheetCacheService.TrySetUseAsPageOpenRaster(refreshed, true, out string firstError, out _))
            AppLog.Warn($"Raster First enable failed during import for '{page.Name}': {firstError}");

        refreshed = OurPlanCoreJobStore.TryReadPage(page.FolderPath) ?? refreshed;
        RasterSheetSource? warmSource = refreshed.RasterSheet ?? result.Source;
        if (warmSource != null)
            PdfViewport.WarmRasterSheetBitmapCache(refreshed, warmSource);

        return result with { Source = warmSource ?? result.Source };
    }

    private static Dictionary<int, IReadOnlyList<PdfLayerInfo>> BuildPdfLayerCache(
        string pdfPath,
        int pageCount,
        IProgress<string>? progress)
    {
        var cache = new Dictionary<int, IReadOnlyList<PdfLayerInfo>>();
        for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            progress?.Report($"scanning PDF layers {pageIndex + 1}/{pageCount}...");
            if (PdfLayerRenderService.TryReadVisibleLayers(pdfPath, pageIndex, out var layers, out _) &&
                layers.Count > 0)
            {
                cache[pageIndex] = layers;
            }
        }
        return cache;
    }
}
