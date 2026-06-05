using OurPlaneCore;
using SkiaSharp;

internal static class RasterSheetCacheTests
{
    public static void BuildsWorkingImageAndStrictSnapManifest()
    {
        string repoRoot = FindRepoRoot();
        string pdfPath = Path.Combine(
            repoRoot,
            "reference",
            "window_detector_poc",
            "outputs",
            "wind_window_points_marked.pdf");
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("Raster sheet test PDF is missing.", pdfPath);

        string tempRoot = Path.Combine(Path.GetTempPath(), "opc_raster_sheet_tests", Guid.NewGuid().ToString("N"));
        try
        {
            OurPlaneCoreJob job = OurPlaneCoreJobStore.CreateJob(tempRoot, "Raster Test Job");
            string importFolder = OurPlaneCoreJobStore.DefaultImportFolder(job);
            PageInfo page = OurPlaneCoreJobStore.ImportPdf(job, pdfPath, ["Raster Test"], importFolder).Single();

            AssertTrue(
                RasterSheetCacheService.TrySetEnabled(page, enabled: false, out string pdfError, out bool pdfChanged),
                pdfError);
            AssertFalse(pdfChanged, "disabling raster on a sheet without a cache should be a no-op PDF state, not a failure");

            AssertTrue(
                Math.Abs(RasterSheetCacheService.DefaultRenderScale - 200f / 72f) < 0.0001f,
                "default raster cache should use a PlanSwift-like 200 DPI display render scale");

            RasterSheetBuildResult build = RasterSheetCacheService.BuildAndEnable(page, 0.5f);
            AssertTrue(build.Ok, build.Error);
            AssertFalse(build.Reused, "first raster build should render a new working raster image");

            PageInfo refreshed = OurPlaneCoreJobStore.TryReadPage(page.FolderPath)
                ?? throw new InvalidOperationException("Raster page source was not readable after build.");
            AssertTrue(refreshed.RasterSheet != null, "source.json should persist raster sheet metadata");
            AssertTrue(refreshed.RasterSheet!.Enabled, "raster sheet should be enabled after build");
            AssertTrue(
                string.Equals(refreshed.RasterSheet.RenderProfile, RasterSheetCacheService.ReadableRasterProfile, StringComparison.Ordinal),
                "working raster should be generated with the readable antialiased raster profile");
            AssertTrue(refreshed.RasterSheet.SnapBlackOnly, "raster snap manifest should stay strict black-line only");
            AssertTrue(refreshed.RasterSheet.SnapIndex.EndsWith("snap.json", StringComparison.OrdinalIgnoreCase), "snap manifest path should be saved");

            string imagePath = Path.GetFullPath(Path.Combine(refreshed.FolderPath, refreshed.RasterSheet.Image));
            string snapPath = Path.GetFullPath(Path.Combine(refreshed.FolderPath, refreshed.RasterSheet.SnapIndex));
            AssertTrue(File.Exists(imagePath), "working raster image should be written beside the page");
            AssertTrue(
                string.Equals(refreshed.RasterSheet.Format, RasterSheetCacheService.WebpRasterFormat, StringComparison.OrdinalIgnoreCase) &&
                imagePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase),
                "new readable raster cache images should be stored as compact lossless WebP");
            AssertTrue(File.Exists(snapPath), "strict snap manifest should be written beside the page");
            AssertTrue(
                RasterSheetCacheService.DisplayStatus(refreshed).Contains("+readable", StringComparison.Ordinal),
                "Sheet Manager raster status should report the readable raster profile");
            AssertTrue(
                RasterSheetCacheService.DisplayStatus(refreshed).Contains("ready", StringComparison.Ordinal),
                "Sheet Manager raster status should show ready DPI cache variants");

            AssertTrue(
                RasterSheetCacheService.TryReadReady(
                    refreshed.FolderPath,
                    refreshed.PdfPath,
                    refreshed.RasterSheet,
                    out RasterSheetBitmapResult bitmap,
                    out string reason),
                reason);
            bitmap.Bitmap.Dispose();

            string legacyPngPath = Path.ChangeExtension(imagePath, ".png");
            using (SKBitmap? webpBitmap = SKBitmap.Decode(imagePath))
            {
                AssertTrue(webpBitmap != null, "test WebP raster should decode before legacy PNG migration setup");
                using SKImage image = SKImage.FromBitmap(webpBitmap!);
                using SKData? pngData = image.Encode(SKEncodedImageFormat.Png, 100);
                AssertTrue(pngData != null && pngData.Size > 0, "test raster should encode as a legacy PNG");
                File.WriteAllBytes(legacyPngPath, pngData!.ToArray());
            }

            File.Delete(imagePath);
            RasterSheetSource legacyPngActive = refreshed.RasterSheet.Clone();
            legacyPngActive.Image = Path.GetRelativePath(refreshed.FolderPath, legacyPngPath);
            legacyPngActive.Format = RasterSheetCacheService.PngRasterFormat;
            OurPlaneCoreJobStore.SavePageRasterSheet(refreshed.FolderPath, legacyPngActive);
            PageInfo legacyPngPage = OurPlaneCoreJobStore.TryReadPage(refreshed.FolderPath)
                ?? throw new InvalidOperationException("Raster page source was not readable after legacy PNG setup.");
            RasterSheetCacheCompactResult migration = RasterSheetCacheService.CompactCache(legacyPngPage);
            AssertTrue(migration.Ok, migration.Error);
            AssertTrue(migration.DeletedFiles >= 1, "raster cache compact should delete the migrated active PNG after writing WebP");
            PageInfo migratedPage = OurPlaneCoreJobStore.TryReadPage(refreshed.FolderPath)
                ?? throw new InvalidOperationException("Raster page source was not readable after legacy PNG migration.");
            AssertTrue(
                migratedPage.RasterSheet != null &&
                string.Equals(migratedPage.RasterSheet.Format, RasterSheetCacheService.WebpRasterFormat, StringComparison.OrdinalIgnoreCase) &&
                migratedPage.RasterSheet.Image.EndsWith(".webp", StringComparison.OrdinalIgnoreCase),
                "raster cache compact should update source.json from legacy PNG to compact WebP");
            imagePath = Path.GetFullPath(Path.Combine(migratedPage.FolderPath, migratedPage.RasterSheet!.Image));
            AssertTrue(File.Exists(imagePath), "migrated active WebP raster should exist");
            AssertFalse(File.Exists(legacyPngPath), "migrated active PNG raster should be removed after source.json points to WebP");
            AssertTrue(
                RasterSheetCacheService.TryReadReady(
                    migratedPage.FolderPath,
                    migratedPage.PdfPath,
                    migratedPage.RasterSheet,
                    out RasterSheetBitmapResult migratedBitmap,
                    out string migratedReason),
                migratedReason);
            migratedBitmap.Bitmap.Dispose();
            refreshed = migratedPage;

            DateTime reusedMarkerUtc = DateTime.UtcNow.AddMinutes(-5);
            File.SetLastWriteTimeUtc(imagePath, reusedMarkerUtc);
            reusedMarkerUtc = File.GetLastWriteTimeUtc(imagePath);
            RasterSheetBuildResult reused = RasterSheetCacheService.BuildAndEnable(refreshed, 0.5f);
            AssertTrue(reused.Ok, reused.Error);
            AssertTrue(reused.Reused, "matching active raster cache should be reported as reused");
            AssertTrue(
                File.GetLastWriteTimeUtc(imagePath) == reusedMarkerUtc,
                "matching raster cache should be enabled from disk without re-rendering the working image");

            RasterSheetBuildResult higherDpi = RasterSheetCacheService.BuildAndEnable(refreshed, 1.0f);
            AssertTrue(higherDpi.Ok, higherDpi.Error);
            AssertFalse(higherDpi.Reused, "different raster DPI should render a separate working image the first time");
            PageInfo higherRefreshed = OurPlaneCoreJobStore.TryReadPage(page.FolderPath)
                ?? throw new InvalidOperationException("Raster page source was not readable after higher DPI build.");
            AssertTrue(
                RasterSheetCacheService.BestReadyReadableRasterDpi(higherRefreshed) == 72,
                "Auto raster quality should prefer the highest ready DPI cache instead of forcing a new render");
            AssertTrue(
                higherRefreshed.RasterSheet != null &&
                higherRefreshed.RasterSheet.Image.Contains("72dpi", StringComparison.OrdinalIgnoreCase),
                "higher DPI build should point source.json at a DPI-specific working image");

            DateTime variantMarkerUtc = DateTime.UtcNow.AddMinutes(-6);
            File.SetLastWriteTimeUtc(imagePath, variantMarkerUtc);
            variantMarkerUtc = File.GetLastWriteTimeUtc(imagePath);
            RasterSheetBuildResult switchedBack = RasterSheetCacheService.BuildAndEnable(higherRefreshed, 0.5f);
            AssertTrue(switchedBack.Ok, switchedBack.Error);
            AssertTrue(switchedBack.Reused, "previously built raster DPI variant should be reused when switching back");
            AssertTrue(
                File.GetLastWriteTimeUtc(imagePath) == variantMarkerUtc,
                "switching back to a ready raster DPI variant should not re-render its image");

            PageInfo switchedBackPage = OurPlaneCoreJobStore.TryReadPage(page.FolderPath)
                ?? throw new InvalidOperationException("Raster page source was not readable after switching back.");
            RasterSheetSource disabledForPrepare = switchedBackPage.RasterSheet!.Clone();
            disabledForPrepare.Enabled = false;
            OurPlaneCoreJobStore.SavePageRasterSheet(switchedBackPage.FolderPath, disabledForPrepare);
            PageInfo disabledPage = OurPlaneCoreJobStore.TryReadPage(page.FolderPath)
                ?? throw new InvalidOperationException("Raster page source was not readable before cache-only prepare.");
            RasterSheetBuildResult preparedOnly = RasterSheetCacheService.BuildCachePreservingEnabled(disabledPage, 1.25f);
            AssertTrue(preparedOnly.Ok, preparedOnly.Error);
            AssertFalse(preparedOnly.Reused, "cache-only prepare should render a missing DPI variant once");
            PageInfo preparedOnlyPage = OurPlaneCoreJobStore.TryReadPage(page.FolderPath)
                ?? throw new InvalidOperationException("Raster page source was not readable after cache-only prepare.");
            AssertTrue(
                preparedOnlyPage.RasterSheet != null && !preparedOnlyPage.RasterSheet.Enabled,
                "background raster prepare should not convert a PDF/off sheet to raster mode");
            AssertTrue(
                RasterSheetCacheService.BestReadyReadableRasterDpi(preparedOnlyPage) == 90,
                "Auto raster quality should see cache-only prepared DPI variants from disk");
            IReadOnlyDictionary<string, IReadOnlyList<int>> readySnapshot =
                RasterSheetCacheService.ReadyReadableRasterDpisByPageFolder([preparedOnlyPage]);
            AssertTrue(
                readySnapshot.TryGetValue(Path.GetFullPath(preparedOnlyPage.FolderPath), out IReadOnlyList<int>? readyDpis) &&
                readyDpis.SequenceEqual([36, 72, 90]),
                "Sheet Manager raster status refresh should build one ready-DPI snapshot with every cached raster image variant");
            AssertTrue(
                RasterSheetCacheService.DisplayStatus(preparedOnlyPage, readySnapshot).Contains("ready 36/72/90", StringComparison.Ordinal),
                "Sheet Manager raster status should format ready DPI variants from its bulk snapshot");
            AssertTrue(
                RasterSheetCacheService.HasReadyReadableRaster(preparedOnlyPage, 1.25f),
                "Raster On fast path should detect a prepared readable DPI cache without rendering");
            AssertFalse(
                RasterSheetCacheService.HasReadyReadableRaster(preparedOnlyPage, 2.0f),
                "Raster On fast path should fall back to rendering when the requested readable DPI cache is missing");
            AssertTrue(
                RasterSheetCacheService.TryEnableReadyReadableRaster(
                    preparedOnlyPage,
                    1.25f,
                    out RasterSheetBuildResult readyOnly),
                readyOnly.Error);
            AssertTrue(readyOnly.Reused, "ready-only raster enable should report reuse instead of a new render");
            PageInfo readyOnlyPage = OurPlaneCoreJobStore.TryReadPage(page.FolderPath)
                ?? throw new InvalidOperationException("Raster page source was not readable after ready-only enable.");
            AssertTrue(
                readyOnlyPage.RasterSheet is { Enabled: true } &&
                readyOnlyPage.RasterSheet.Image.Contains("90dpi", StringComparison.OrdinalIgnoreCase),
                "ready-only raster enable should switch source.json to the prepared DPI image");
            RasterSheetCacheCompactResult compact = RasterSheetCacheService.CompactCache(readyOnlyPage);
            AssertTrue(compact.Ok, compact.Error);
            AssertTrue(compact.DeletedFiles >= 2, "raster cache compact should delete unused DPI raster image variants");
            AssertTrue(compact.DeletedBytes > 0, "raster cache compact should report freed disk space");
            PageInfo compactedPage = OurPlaneCoreJobStore.TryReadPage(page.FolderPath)
                ?? throw new InvalidOperationException("Raster page source was not readable after compact.");
            IReadOnlyDictionary<string, IReadOnlyList<int>> compactedReadySnapshot =
                RasterSheetCacheService.ReadyReadableRasterDpisByPageFolder([compactedPage]);
            AssertTrue(
                compactedReadySnapshot.TryGetValue(Path.GetFullPath(compactedPage.FolderPath), out IReadOnlyList<int>? compactedReadyDpis) &&
                compactedReadyDpis.SequenceEqual([90]),
                "raster cache compact should keep only the active readable DPI variant");
            AssertTrue(
                RasterSheetCacheService.TryReadReady(
                    compactedPage.FolderPath,
                    compactedPage.PdfPath,
                    compactedPage.RasterSheet,
                    out RasterSheetBitmapResult compactedBitmap,
                    out string compactedReason),
                compactedReason);
            compactedBitmap.Bitmap.Dispose();

            RasterSheetSource legacy = refreshed.RasterSheet.Clone();
            legacy.RenderProfile = RasterSheetCacheService.ReadableLineBoostProfile;
            AssertFalse(
                RasterSheetCacheService.TryReadReady(
                    refreshed.FolderPath,
                    refreshed.PdfPath,
                    legacy,
                    out RasterSheetBitmapResult legacyBitmap,
                    out string legacyReason),
                "legacy lineboost raster cache should be rejected so v4 cannot show blocky boosted raster images");
            legacyBitmap.Bitmap.Dispose();
            AssertTrue(
                legacyReason.Contains("legacy lineboost", StringComparison.OrdinalIgnoreCase),
                "legacy raster rejection reason should be visible in the log");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch { }
        }
    }

    private static string FindRepoRoot()
    {
        string dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "ourplanecore.csproj")))
                return dir;

            string? parent = Directory.GetParent(dir)?.FullName;
            if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                break;
            dir = parent ?? "";
        }

        throw new DirectoryNotFoundException("Could not locate ourplanecore repo root.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition)
            throw new InvalidOperationException(message);
    }
}
