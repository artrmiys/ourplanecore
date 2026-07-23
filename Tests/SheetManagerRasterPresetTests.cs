using OurPlanCore;

internal static class SheetManagerRasterPresetTests
{
    public static void DefaultsMatchSheetManagerContractAndCloneIndependently()
    {
        RasterDpiPresetConfig defaults = RasterDpiPresetConfig.BuildDefault();
        AssertSequence(
            [72, 100, 150, 200, 300, 400],
            defaults.Presets,
            "built-in Sheet Manager DPI presets");

        RasterDpiPresetConfig clone = defaults.Clone();
        clone.Presets[0] = 96;
        AssertTrue(defaults.Presets[0] == 72, "Clone must not share the editable preset list");
    }

    public static void ExactDpiPinExcludesSourceImagesAndInvalidValues()
    {
        var readable = new RasterSheetSource
        {
            RenderProfile = RasterSheetCacheService.ReadableRasterProfile,
            PinnedDpi = 150,
        };
        AssertTrue(RasterSheetCacheService.IsRasterDpiPinned(readable), "150 DPI readable raster must be pinned");
        AssertTrue(RasterSheetCacheService.PinnedRasterDpi(readable) == 150, "the exact pin must be retained");

        readable.PinnedDpi = 71;
        AssertTrue(!RasterSheetCacheService.IsRasterDpiPinned(readable), "out-of-range values must restore adaptive mode");

        readable.PinnedDpi = 200;
        readable.RenderProfile = RasterSheetCacheService.SourceImageRasterProfile;
        AssertTrue(!RasterSheetCacheService.IsRasterDpiPinned(readable), "source-image rasters must keep their original semantics");
    }

    public static void PersistedRasterMetadataWinsOverStalePageSnapshots()
    {
        WithBlankPage("stale raster metadata", page =>
        {
            var persistedSource = new RasterSheetSource
            {
                Enabled = false,
                UseAsPageOpenRaster = false,
                Image = Path.Combine(RasterSheetCacheService.CacheFolderName, "newer.webp"),
                Format = RasterSheetCacheService.WebpRasterFormat,
                RenderProfile = RasterSheetCacheService.ReadableRasterProfile,
                RenderScale = RasterSheetCacheService.RasterDpiToRenderScale(100),
                PinnedDpi = 100,
                WidthPt = 612,
                HeightPt = 792,
            };
            OurPlanCoreJobStore.SavePageRasterSheet(page.FolderPath, persistedSource);

            RasterSheetSource staleSource = persistedSource.Clone();
            staleSource.Enabled = true;
            staleSource.UseAsPageOpenRaster = true;
            staleSource.Image = Path.Combine(RasterSheetCacheService.CacheFolderName, "stale.webp");
            staleSource.PinnedDpi = 72;
            PageInfo stalePage = SnapshotWithRaster(page, staleSource);

            AssertTrue(
                RasterSheetCacheService.TrySetEnabled(
                    stalePage,
                    enabled: true,
                    out string enabledError,
                    out bool enabledChanged),
                enabledError);
            AssertTrue(enabledChanged, "persisted disabled state should be enabled");
            PageInfo enabledPage = ReadPage(page);
            AssertTrue(enabledPage.RasterSheet?.Enabled == true, "enabled state should be persisted");
            AssertTrue(enabledPage.RasterSheet?.UseAsPageOpenRaster == false, "stale Raster First must not be restored");
            AssertTrue(enabledPage.RasterSheet?.PinnedDpi == 100, "newer persisted pin must survive enable");
            AssertTrue(
                enabledPage.RasterSheet?.Image.EndsWith("newer.webp", StringComparison.OrdinalIgnoreCase) == true,
                "newer persisted raster image must survive enable");

            RasterSheetSource newerSource = enabledPage.RasterSheet!.Clone();
            newerSource.Image = Path.Combine(RasterSheetCacheService.CacheFolderName, "newest.webp");
            newerSource.PinnedDpi = 150;
            OurPlanCoreJobStore.SavePageRasterSheet(page.FolderPath, newerSource);
            AssertTrue(
                RasterSheetCacheService.TrySetUseAsPageOpenRaster(
                    stalePage,
                    useAsPageOpenRaster: true,
                    out string firstError,
                    out bool firstChanged),
                firstError);
            AssertTrue(firstChanged, "Raster First should update the persisted source");
            PageInfo firstPage = ReadPage(page);
            AssertTrue(firstPage.RasterSheet?.UseAsPageOpenRaster == true, "Raster First should be enabled");
            AssertTrue(firstPage.RasterSheet?.PinnedDpi == 150, "newest persisted pin must survive Raster First");
            AssertTrue(
                firstPage.RasterSheet?.Image.EndsWith("newest.webp", StringComparison.OrdinalIgnoreCase) == true,
                "newest persisted raster image must survive Raster First");

            OurPlanCoreJobStore.SavePageRasterSheet(page.FolderPath, null);
            AssertTrue(
                RasterSheetCacheService.TrySetEnabled(
                    stalePage,
                    enabled: false,
                    out string clearedEnabledError,
                    out bool clearedEnabledChanged),
                clearedEnabledError);
            AssertTrue(!clearedEnabledChanged, "PDF on a persisted-null raster should be a no-op");
            AssertTrue(
                RasterSheetCacheService.TrySetUseAsPageOpenRaster(
                    stalePage,
                    useAsPageOpenRaster: false,
                    out string clearedFirstError,
                    out bool clearedFirstChanged),
                clearedFirstError);
            AssertTrue(!clearedFirstChanged, "Raster First off on a persisted-null raster should be a no-op");
            AssertTrue(ReadPage(page).RasterSheet == null, "stale metadata must never resurrect a cleared raster");
        });
    }

    public static void AtomicPresetTransitionsKeepActiveRasterAndPinTogether()
    {
        WithBlankPage("atomic raster preset", page =>
        {
            float dpi100Scale = RasterSheetCacheService.RasterDpiToRenderScale(100);
            RasterSheetBuildResult built100 = RasterSheetCacheService.BuildAndEnable(
                page,
                dpi100Scale,
                allowPinnedDpiChange: true,
                pinnedDpiOverride: 100);
            AssertTrue(built100.Ok, built100.Error);
            AssertRasterState(ReadPage(page), enabled: true, pinnedDpi: 100, activeDpi: 100);

            PageInfo stalePage = SnapshotWithRaster(page, page.RasterSheet);
            AssertTrue(
                RasterSheetCacheService.TrySetEnabledAndPinnedDpi(
                    stalePage,
                    enabled: false,
                    pinnedDpi: 0,
                    out string pdfError,
                    out bool pdfChanged),
                pdfError);
            AssertTrue(pdfChanged, "PDF must atomically disable the raster and clear its pin");
            AssertRasterState(ReadPage(page), enabled: false, pinnedDpi: 0, activeDpi: 100);

            AssertTrue(
                RasterSheetCacheService.TrySetEnabledAndPinnedDpi(
                    stalePage,
                    enabled: true,
                    pinnedDpi: 100,
                    out string onError,
                    out bool onChanged),
                onError);
            AssertTrue(onChanged, "numeric preset must atomically enable and pin the active raster");
            AssertRasterState(ReadPage(page), enabled: true, pinnedDpi: 100, activeDpi: 100);

            float dpi72Scale = RasterSheetCacheService.RasterDpiToRenderScale(72);
            RasterSheetBuildResult builtAuto = RasterSheetCacheService.BuildAndEnable(
                ReadPage(page),
                dpi72Scale,
                allowPinnedDpiChange: true,
                pinnedDpiOverride: 0);
            AssertTrue(builtAuto.Ok, builtAuto.Error);
            AssertRasterState(ReadPage(page), enabled: true, pinnedDpi: 0, activeDpi: 72);

            AssertTrue(
                RasterSheetCacheService.TryEnableReadyReadableRaster(
                    ReadPage(page),
                    dpi100Scale,
                    out RasterSheetBuildResult readyPinned,
                    allowPinnedDpiChange: true,
                    pinnedDpiOverride: 100),
                readyPinned.Error);
            AssertTrue(readyPinned.Reused, "numeric preset should reuse its ready raster variant");
            AssertRasterState(ReadPage(page), enabled: true, pinnedDpi: 100, activeDpi: 100);

            AssertTrue(
                RasterSheetCacheService.TryEnableReadyReadableRaster(
                    ReadPage(page),
                    dpi72Scale,
                    out RasterSheetBuildResult readyAuto,
                    allowPinnedDpiChange: true,
                    pinnedDpiOverride: 0),
                readyAuto.Error);
            AssertTrue(readyAuto.Reused, "Auto should reuse its ready raster variant");
            AssertRasterState(ReadPage(page), enabled: true, pinnedDpi: 0, activeDpi: 72);
        });
    }

    public static void OperationGateAndPinnedViewportPathsAreWired()
    {
        string manager = File.ReadAllText(RepoFile("MainWindow.WorkspaceManagers.cs"));
        string service = File.ReadAllText(RepoFile("Models/RasterSheetCacheService.cs"));
        string renderCache = File.ReadAllText(RepoFile("Controls/PdfViewport.RenderCache.cs"));
        string rasterSheet = File.ReadAllText(RepoFile("Controls/PdfViewport.RasterSheet.cs"));
        string pdfSnap = File.ReadAllText(RepoFile("Controls/PdfViewport.PdfSnap.cs"));
        string staticPolicy = File.ReadAllText(RepoFile("Models/StaticRasterPrefetchPolicy.cs"));
        string settings = File.ReadAllText(RepoFile("MainWindow.SettingsManager.RasterDpiPresets.cs"));

        int setMethod = manager.IndexOf("private async Task SetSheetManagerRasterEnabledAsync(", StringComparison.Ordinal);
        int gate = manager.IndexOf("TryBeginSheetManagerRasterOperation(", setMethod, StringComparison.Ordinal);
        int firstAwait = manager.IndexOf("await ", setMethod, StringComparison.Ordinal);
        AssertTrue(setMethod >= 0 && gate > setMethod && gate < firstAwait, "the full raster action must acquire its gate before the first await");
        AssertTrue(
            manager.Contains("PageInfo currentPage = OurPlanCoreJobStore.TryReadPage(page.FolderPath) ?? page;", StringComparison.Ordinal),
            "ready planning must use the whole persisted page snapshot");
        AssertTrue(
            manager.Contains("TrySetEnabledAndPinnedDpi(", StringComparison.Ordinal) &&
            manager.Contains("pinnedDpiOverride: pinRequestedDpi ? effectiveDpi : 0", StringComparison.Ordinal),
            "ready, build, Auto, and PDF transitions must use atomic raster-plus-pin writes");
        AssertTrue(
            service.Contains("return persistedPage.RasterSheet?.Clone();", StringComparison.Ordinal),
            "persisted null raster metadata must be authoritative");
        AssertTrue(
            renderCache.Contains("RasterSheetCacheService.IsRasterDpiPinned(page.RasterSheet)", StringComparison.Ordinal) &&
            renderCache.Contains("BuildReadableRasterSheetPreservingPinnedDpi(page)", StringComparison.Ordinal),
            "work-zoom prefetch and refresh self-heal must honor pinned DPI");
        AssertTrue(
            rasterSheet.Contains("pinnedDpiOverride: pinnedDpi", StringComparison.Ordinal),
            "current-page self-heal must rebuild at the exact persisted pin");
        AssertTrue(
            pdfSnap.Contains("RasterSheetCacheService.PinnedRasterDpi(page.RasterSheet)", StringComparison.Ordinal),
            "snap cache fallback must render the pinned DPI instead of the default DPI");
        AssertTrue(
            staticPolicy.Contains("int targetDpi = RasterSheetCacheService.PinnedRasterDpi(source);", StringComparison.Ordinal),
            "static page-open policy must prefer a per-sheet exact pin");
        AssertTrue(
            settings.Contains("SettingsPresetStore.ResolveRasterDpiPresets(_currentJob)", StringComparison.Ordinal) &&
            settings.Contains("this job's override remains applied", StringComparison.Ordinal),
            "saving global presets must keep the current job override installed");
    }

    public static void ToolbarUsesStrictSelectionAndManagersScrollHorizontally()
    {
        string xaml = File.ReadAllText(RepoFile("MainWindow.xaml"));
        string presetUi = File.ReadAllText(RepoFile("MainWindow.SheetManagerRasterPresets.cs"));
        string manager = File.ReadAllText(RepoFile("MainWindow.WorkspaceManagers.cs"));
        string resources = File.ReadAllText(RepoFile("Resources/AppNavigationResources.xaml"));
        string viewport = File.ReadAllText(RepoFile("Controls/PdfViewport.RasterSheetDpiUpgrade.cs"));

        AssertTrue(xaml.Contains("Text=\"Selected: 0\"", StringComparison.Ordinal), "toolbar must expose selected-row count");
        AssertTrue(xaml.Contains("x:Name=\"SheetManagerRasterPresetItems\"", StringComparison.Ordinal), "numeric presets must be config-driven");
        AssertTrue(xaml.Contains("x:Name=\"SheetManagerRasterOptionsPopup\"", StringComparison.Ordinal), "advanced raster actions must live under Options");
        AssertTrue(!xaml.Contains("Header=\"Raster Action\"", StringComparison.Ordinal), "duplicate row action column must be removed");
        AssertTrue(
            presetUi.Contains("var pages = SelectedSheetManagerPages();", StringComparison.Ordinal) &&
            !presetUi.Contains("SelectedSheetManagerPagesForRaster()", StringComparison.Ordinal),
            "direct preset buttons must never fall back to all sheets");
        AssertTrue(
            manager.Contains("pinRequestedDpi ? effectiveDpi : 0", StringComparison.Ordinal),
            "numeric presets must persist an exact pin while Auto clears it");
        AssertTrue(
            viewport.Contains("RasterSheetCacheService.IsRasterDpiPinned", StringComparison.Ordinal),
            "responsive viewport upgrades must honor exact DPI pins");
        AssertTrue(
            resources.Contains(
                "<Setter Property=\"ScrollViewer.HorizontalScrollBarVisibility\" Value=\"Auto\"/>",
                StringComparison.Ordinal),
            "shared DataGrid style must expose horizontal scrolling only when needed");
    }

    private static string RepoFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate) && File.Exists(Path.Combine(current.FullName, "ourplancore.csproj")))
                return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException($"Repository file not found: {relativePath}");
    }

    private static void WithBlankPage(string name, Action<PageInfo> action)
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "onc_sheet_manager_raster_tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(tempRoot, name);
            PageInfo page = OurPlanCoreJobStore.CreateBlankPage(
                job,
                "A100",
                OurPlanCoreJobStore.DefaultImportFolder(job));
            action(page);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static PageInfo SnapshotWithRaster(PageInfo page, RasterSheetSource? rasterSheet) =>
        new()
        {
            Name = page.Name,
            FolderPath = page.FolderPath,
            PdfPath = page.PdfPath,
            PdfPage = page.PdfPage,
            RasterSheet = rasterSheet?.Clone(),
        };

    private static PageInfo ReadPage(PageInfo page) =>
        OurPlanCoreJobStore.TryReadPage(page.FolderPath) ??
        throw new InvalidOperationException("Raster test page source was not readable.");

    private static void AssertRasterState(
        PageInfo page,
        bool enabled,
        int pinnedDpi,
        int activeDpi)
    {
        RasterSheetSource source = page.RasterSheet ??
            throw new InvalidOperationException("Expected persisted raster metadata.");
        AssertTrue(source.Enabled == enabled, $"expected Enabled={enabled}");
        AssertTrue(source.PinnedDpi == pinnedDpi, $"expected pin {pinnedDpi}, got {source.PinnedDpi}");
        AssertTrue(
            RasterSheetCacheService.RenderScaleToDpi(source.RenderScale) == activeDpi,
            $"expected active DPI {activeDpi}, got {RasterSheetCacheService.RenderScaleToDpi(source.RenderScale)}");
    }

    private static void AssertSequence(
        IReadOnlyList<int> expected,
        IReadOnlyList<int> actual,
        string message)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"{message}: expected {string.Join(",", expected)}, got {string.Join(",", actual)}");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
