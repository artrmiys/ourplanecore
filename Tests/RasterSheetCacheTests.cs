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
                Math.Abs(RasterSheetCacheService.DefaultRenderScale - 200f / 72f) < 0.0001f,
                "default raster cache should use a PlanSwift-like 200 DPI display render scale");

            RasterSheetBuildResult build = RasterSheetCacheService.BuildAndEnable(page, 0.5f);
            AssertTrue(build.Ok, build.Error);

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
                RasterSheetCacheService.TryReadReady(
                    refreshed.FolderPath,
                    refreshed.PdfPath,
                    refreshed.RasterSheet,
                    out RasterSheetBitmapResult bitmap,
                    out string reason),
                reason);
            bitmap.Bitmap.Dispose();

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
