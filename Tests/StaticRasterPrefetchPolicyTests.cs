using OurPlanCore;
using SkiaSharp;

internal static class StaticRasterPrefetchPolicyTests
{
    public static void ActiveNearTargetRasterSuppressesOnlySafePrefetch()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "onc_static_prefetch_tests", Guid.NewGuid().ToString("N"));
        bool previousEnabled = ViewportRenderPolicy.StaticRasterModeEnabled;
        int previousTargetDpi = ViewportRenderPolicy.StaticRasterTargetDpi;
        bool previousPdfLayersEnabled = PdfLayerRenderService.PdfLayersEnabled;
        try
        {
            string pageFolder = Path.Combine(tempRoot, "Pages", "A100");
            string rasterFolder = Path.Combine(pageFolder, RasterSheetCacheService.CacheFolderName);
            Directory.CreateDirectory(rasterFolder);
            string pdfPath = Path.Combine(tempRoot, "source.pdf");
            File.WriteAllBytes(pdfPath, [1, 2, 3, 4]);

            WriteRaster(Path.Combine(rasterFolder, "working-144dpi.webp"), 144);
            var pdfInfo = new FileInfo(pdfPath);
            var source = new RasterSheetSource
            {
                Enabled = true,
                UseAsPageOpenRaster = true,
                Image = Path.Combine(RasterSheetCacheService.CacheFolderName, "working-144dpi.webp"),
                Format = RasterSheetCacheService.WebpRasterFormat,
                RenderProfile = RasterSheetCacheService.ReadableRasterProfile,
                RenderScale = 2,
                WidthPt = 72,
                HeightPt = 72,
                PdfLength = pdfInfo.Length,
                PdfLastWriteUtcTicks = pdfInfo.LastWriteTimeUtc.Ticks,
            };
            var page = new PageInfo
            {
                Name = "A100",
                FolderPath = pageFolder,
                PdfPath = pdfPath,
                PdfPage = 0,
                RasterSheet = source,
            };

            ViewportRenderPolicy.StaticRasterModeEnabled = true;
            ViewportRenderPolicy.StaticRasterTargetDpi = 150;
            PdfLayerRenderService.PdfLayersEnabled = false;
            AssertTrue(
                StaticRasterPrefetchPolicy.HasReadyPageOpenRaster(page),
                "the active 144 DPI raster should satisfy the 95% tolerance for a 150 DPI static target");

            WriteRaster(Path.Combine(rasterFolder, "working-72dpi.webp"), 72);
            source.Image = Path.Combine(RasterSheetCacheService.CacheFolderName, "working-72dpi.webp");
            source.RenderScale = 1;
            AssertFalse(
                StaticRasterPrefetchPolicy.HasReadyPageOpenRaster(page),
                "a non-active near-target side raster must not hide the live fallback");

            WriteRaster(Path.Combine(rasterFolder, "working-150dpi.webp"), 150);
            AssertTrue(
                StaticRasterPrefetchPolicy.HasReadyPageOpenRaster(page),
                "an exact side raster may suppress live prefetch because the static build path reuses it");

            File.Delete(Path.Combine(rasterFolder, "working-150dpi.webp"));
            source.RenderProfile = RasterSheetCacheService.SourceImageRasterProfile;
            source.WidthPt = 36 * 72;
            source.HeightPt = 48 * 72;
            source.RenderScale = 4;
            source.OverviewImage = Path.Combine(RasterSheetCacheService.CacheFolderName, "overview.webp");
            source.OverviewRenderScale = 1;
            AssertFalse(
                StaticRasterPrefetchPolicy.HasReadyPageOpenRaster(page),
                "missing source-image overview data must keep the live preview fallback");

            WriteRaster(Path.Combine(rasterFolder, "overview.webp"), 72);
            AssertTrue(
                StaticRasterPrefetchPolicy.HasReadyPageOpenRaster(page),
                "an existing overview should make an oversized source-image raster safe for page open");

            source.RenderProfile = RasterSheetCacheService.ReadableRasterProfile;
            source.WidthPt = 72;
            source.HeightPt = 72;
            source.RenderScale = 2;
            source.Image = Path.Combine(RasterSheetCacheService.CacheFolderName, "working-144dpi.webp");
            source.OverviewImage = "";
            source.OverviewRenderScale = 0;

            ViewportRenderPolicy.StaticRasterTargetDpi = 300;
            AssertFalse(
                StaticRasterPrefetchPolicy.HasReadyPageOpenRaster(page),
                "a low-resolution variant must not suppress the live PDF fallback for a 300 DPI target");

            ViewportRenderPolicy.StaticRasterTargetDpi = 150;
            source.UseAsPageOpenRaster = false;
            AssertFalse(
                StaticRasterPrefetchPolicy.HasReadyPageOpenRaster(page),
                "an opt-in readable raster that is not used for page open must retain preview prefetch");

            source.UseAsPageOpenRaster = true;
            ViewportRenderPolicy.StaticRasterModeEnabled = false;
            AssertFalse(
                StaticRasterPrefetchPolicy.HasReadyPageOpenRaster(page),
                "disabling static raster mode must retain the live PDF prefetch pipeline");

            ViewportRenderPolicy.StaticRasterModeEnabled = true;
            PdfLayerRenderService.PdfLayersEnabled = true;
            AssertFalse(
                StaticRasterPrefetchPolicy.HasReadyPageOpenRaster(page),
                "the live PDF layer renderer must override static-raster prefetch suppression");

            PdfLayerRenderService.PdfLayersEnabled = false;
            ViewportRenderPolicy.StaticRasterTargetDpi = 300;
            int eSizeTarget = StaticRasterPrefetchPolicy.ResolveEffectiveTargetDpi(36 * 72, 48 * 72);
            AssertTrue(
                eSizeTarget is >= 235 and <= 237,
                "the shared static target should honor the 96 MP page bitmap budget for E-size sheets");
        }
        finally
        {
            ViewportRenderPolicy.StaticRasterModeEnabled = previousEnabled;
            ViewportRenderPolicy.StaticRasterTargetDpi = previousTargetDpi;
            PdfLayerRenderService.PdfLayersEnabled = previousPdfLayersEnabled;
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

    private static void WriteRaster(string path, int pixels)
    {
        using var bitmap = new SKBitmap(pixels, pixels);
        bitmap.Erase(SKColors.White);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Webp, 100)
            ?? throw new InvalidOperationException("Test raster encoding failed.");
        File.WriteAllBytes(path, data.ToArray());
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);
}
