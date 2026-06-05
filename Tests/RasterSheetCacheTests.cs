using OurPlaneCore;

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
            AssertFalse(build.Reused, "first raster build should render a new working PNG");

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

            DateTime reusedMarkerUtc = DateTime.UtcNow.AddMinutes(-5);
            File.SetLastWriteTimeUtc(imagePath, reusedMarkerUtc);
            reusedMarkerUtc = File.GetLastWriteTimeUtc(imagePath);
            RasterSheetBuildResult reused = RasterSheetCacheService.BuildAndEnable(refreshed, 0.5f);
            AssertTrue(reused.Ok, reused.Error);
            AssertTrue(reused.Reused, "matching active raster cache should be reported as reused");
            AssertTrue(
                File.GetLastWriteTimeUtc(imagePath) == reusedMarkerUtc,
                "matching raster cache should be enabled from disk without re-rendering the working PNG");

            RasterSheetBuildResult higherDpi = RasterSheetCacheService.BuildAndEnable(refreshed, 1.0f);
            AssertTrue(higherDpi.Ok, higherDpi.Error);
            AssertFalse(higherDpi.Reused, "different raster DPI should render a separate working PNG the first time");
            PageInfo higherRefreshed = OurPlaneCoreJobStore.TryReadPage(page.FolderPath)
                ?? throw new InvalidOperationException("Raster page source was not readable after higher DPI build.");
            AssertTrue(
                RasterSheetCacheService.BestReadyReadableRasterDpi(higherRefreshed) == 72,
                "Auto raster quality should prefer the highest ready DPI cache instead of forcing a new render");
            AssertTrue(
                higherRefreshed.RasterSheet != null &&
                higherRefreshed.RasterSheet.Image.Contains("72dpi", StringComparison.OrdinalIgnoreCase),
                "higher DPI build should point source.json at a DPI-specific working PNG");

            DateTime variantMarkerUtc = DateTime.UtcNow.AddMinutes(-6);
            File.SetLastWriteTimeUtc(imagePath, variantMarkerUtc);
            variantMarkerUtc = File.GetLastWriteTimeUtc(imagePath);
            RasterSheetBuildResult switchedBack = RasterSheetCacheService.BuildAndEnable(higherRefreshed, 0.5f);
            AssertTrue(switchedBack.Ok, switchedBack.Error);
            AssertTrue(switchedBack.Reused, "previously built raster DPI variant should be reused when switching back");
            AssertTrue(
                File.GetLastWriteTimeUtc(imagePath) == variantMarkerUtc,
                "switching back to a ready raster DPI variant should not re-render its PNG");

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

            RasterSheetSource legacy = refreshed.RasterSheet.Clone();
            legacy.RenderProfile = RasterSheetCacheService.ReadableLineBoostProfile;
            AssertFalse(
                RasterSheetCacheService.TryReadReady(
                    refreshed.FolderPath,
                    refreshed.PdfPath,
                    legacy,
                    out RasterSheetBitmapResult legacyBitmap,
                    out string legacyReason),
                "legacy lineboost raster cache should be rejected so v4 cannot show blocky boosted PNGs");
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
