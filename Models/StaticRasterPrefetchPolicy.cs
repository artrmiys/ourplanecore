using System;
using System.IO;

namespace OurPlanCore;

public static class StaticRasterPrefetchPolicy
{
    public static bool RequiresPinnedDpiMigration(int currentDpi, int targetDpi) =>
        targetDpi > 0 && currentDpi != targetDpi;

    public static bool HasReadyPageOpenRaster(PageInfo page)
    {
        if (!ViewportRenderPolicy.StaticRasterModeEnabled ||
            PdfLayerRenderService.PdfLayersEnabled ||
            page.RasterSheet?.Enabled != true ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            string.IsNullOrWhiteSpace(page.PdfPath) ||
            !File.Exists(page.PdfPath))
        {
            return false;
        }

        RasterSheetSource source = page.RasterSheet;
        if (RasterSheetCacheService.ShouldRebuildForReadableDisplay(
                page.FolderPath,
                page.PdfPath,
                source,
                out _))
        {
            return false;
        }

        if (RasterSheetCacheService.IsSourceImageRaster(source))
        {
            return RasterSheetCacheService.ShouldUseSourceImageRasterForFastOpen(source) ||
                   HasExistingSourceImageOverview(page.FolderPath, source);
        }

        if (!RasterSheetCacheService.UseAsPageOpenRaster(source))
            return false;

        int targetDpi = RasterSheetCacheService.PinnedRasterDpi(source);
        if (targetDpi <= 0)
            targetDpi = ResolveEffectiveTargetDpi(source.WidthPt, source.HeightPt);
        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(source.RenderScale);
        if (!RequiresPinnedDpiMigration(currentDpi, targetDpi))
            return true;

        return RasterSheetCacheService.HasReadyReadableRaster(
            page,
            RasterSheetCacheService.RasterDpiToRenderScale(targetDpi));
    }

    public static int ResolveEffectiveTargetDpi(double pageWidthPt, double pageHeightPt)
    {
        int chosen = AppSettingsStore.NormalizeStaticPageRenderDpi(
            ViewportRenderPolicy.StaticRasterTargetDpi);
        if (pageWidthPt <= 0 || pageHeightPt <= 0)
            return chosen;

        double pagePoints = pageWidthPt * pageHeightPt;
        if (double.IsNaN(pagePoints) || double.IsInfinity(pagePoints) || pagePoints <= 0)
            return chosen;

        double budgetScale = Math.Sqrt(ViewportRenderPolicy.ResponsiveMaxRenderPixels / pagePoints);
        int budgetDpi = RasterSheetCacheService.RenderScaleToDpi(budgetScale);
        return budgetDpi <= 0 ? chosen : Math.Max(1, Math.Min(chosen, budgetDpi));
    }

    private static bool HasExistingSourceImageOverview(string pageFolder, RasterSheetSource source)
    {
        if (!RasterSheetCacheService.HasSourceImageOverview(source))
            return false;

        try
        {
            return File.Exists(Path.GetFullPath(Path.Combine(pageFolder, source.OverviewImage)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
