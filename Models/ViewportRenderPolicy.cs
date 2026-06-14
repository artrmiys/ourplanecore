using System;
using System.Collections.Generic;

namespace OurPlaneCore;

public static class ViewportRenderPolicy
{
    public const string BalancedQualityMode = "Balanced";
    public const string HighQualityMode = "High";
    public const string MaxQualityMode = "Max";
    public const float HighZoomFastFrameThreshold = 2.0f;
    public const float FarZoomFastFrameThreshold = 0.85f;
    public const float ResponsiveMinRenderScale = 1.0f;
    public const float ResponsiveMaxRenderScale = 2.25f;
    public const float ResponsiveMaxRenderPixels = 96_000_000f;
    public const float ZoomRefreshMinZoom = 0.55f;
    public const bool DetailRenderEnabled = true;
    public const float DetailRenderMinZoom = 0.75f;
    public const float DetailRenderMinScaleGain = 1.04f;
    public const float DetailRenderMaxScale = 16.0f;
    public const float DetailRenderMaxPixels = 96_000_000f;
    public const float DetailRenderPaddingScreenPx = 1024f;
    public const bool DetailRenderPrefetchEnabled = true;
    public const float DetailRenderPrefetchMinZoom = 2.5f;
    public const int DetailRenderPrefetchTileCount = 4;
    public const int DetailRenderPrefetchConcurrency = 1;
    public const float DetailRenderPrefetchShiftFactor = 0.80f;
    public const int DetailRenderPrefetchDelayMs = 300;
    public const int DetailRenderCoalesceDelayMs = 80;
    public const int DetailRenderNavigationQuietMs = 240;
    public const int DetailRenderMaxPaintTiles = 4;
    public const float DetailRenderStableTileScreenPx = 1536f;
    public const float DetailRenderStableTileMaxExpansionFactor = 3.5f;
    public const int PageSwitchDetailRenderDelayMs = 100;
    public const int DetailRenderDocPrewarmDelayMs = 350;
    public const int FastPageSwitchPreviewCoalesceMs = 16;
    public const int PageSwitchSharpUpgradeDelayMs = 180;
    public const int PageSwitchSharpUpgradeIdleMs = 500;
    public const int PageSwitchSharpUpgradeMaxDeferrals = 5;
    public const float PageSwitchSharpUpgradeMinZoom = ZoomRefreshMinZoom;
    public const int PageOpenDeferredNavigationQuietMs = 1800;
    public const int PreviewPrefetchDelayMs = 100;
    public const int PreviewPrefetchNavigationQuietMs = 1100;
    public const int PreviewPrefetchActiveRenderHoldMs = 3000;
    public const int PreviewPrefetchAfterActiveRenderHoldMs = 750;
    public const int PersistedPreviewLiveFallbackGraceMs = 35;
    public const int NearbyPagePreviewPrefetchRadius = 1;
    public const int NearbyPageReadableBasePrefetchRadius = 1;
    public const int NearbyPageDirectionalPreviewPrefetchRadius = 3;
    public const int NearbyPageDirectionalReadableBasePrefetchRadius = 3;
    public const int NearbyPageCleanRenderPrefetchRadius = 0;
    public const bool JobOpenPreviewWarmupAllPages = true;
    public const bool JobOpenRasterSheetRefreshWarmupAllPages = true;
    public const int JobOpenPreviewWarmupPriorityLocalRadius = 2;
    public const int JobOpenPreviewWarmupLocalRadius = 8;
    public const int JobOpenPreviewWarmupSpreadAnchorCount = 12;
    public const int JobOpenPreviewWarmupCount = 48;
    public const int JobOpenPreviewWarmupLargeJobCount = 64;
    public const int JobOpenPreviewWarmupHugeJobCount = 96;
    public const int JobOpenRasterSheetBitmapWarmupCount = 12;
    public const int JobOpenRasterSheetBitmapWarmupLargeJobCount = 16;
    public const int JobOpenRasterSheetBitmapWarmupHugeJobCount = 24;
    public const int JobOpenRasterSheetRefreshWarmupCount = 12;
    public const int JobOpenRasterSheetRefreshWarmupLargeJobCount = 16;
    public const int JobOpenRasterSheetRefreshWarmupHugeJobCount = 24;
    public const int JobOpenWarmupLargeJobThreshold = 160;
    public const int JobOpenWarmupHugeJobThreshold = 320;
    public const int RasterSheetRefreshPrefetchDelayMs = 1800;
    public const int RasterSheetRefreshPrefetchCadenceMs = 6500;
    public const int RasterSheetWorkZoomWarmupDelayMs = 800;
    public const int RasterSheetCurrentWorkZoomBuildDelayMs = 80;
    public const int RasterSheetMotionWarmupMinIntervalMs = 650;
    public const int RasterSheetMotionQualityRestoreQuietMs = 450;
    public const int RasterSheetPageOpenImmediateWarmMaxDpi = 144;
    public const int PointerMoveRepaintMinIntervalMs = 33;
    public const float InstantPagePreviewRenderScale = 0.35f;
    public const float FastPageSwitchPreviewRenderScale = 0.35f;
    public const float ColdPageSwitchPreviewRenderScale = 0.20f;
    public const float InitialPagePreviewRenderScale = 0.75f;
    public const float LowZoomBitmapDowngradeRatio = 1.38f;
    public const float RasterSheetDisplayMinZoom = ZoomRefreshMinZoom;
    public const float RasterSheetDisplayExitZoom = 0.45f;
    public const float RasterSheetFarZoomFastPaintMaxZoom = 0.30f;
    public const float RasterSheetFarZoomFastPaintMaxScaleRatio = 0.30f;
    public const int RasterSheetNavigationMaxDpi = 144;
    private static readonly int[] RasterSheetDisplayDpiSteps = [72, 100, 144, 200];
    public const float SheetOverlayLowZoomRenderScale = 1.0f;
    public const float SheetOverlayViewportRenderScale = 2.0f;
    public const float SheetOverlayExportRenderScale = 2.0f;
    public const float SheetOverlayMaxRenderPixels = 48_000_000f;
    public const float MeasurementLabelMinZoom = 0.95f;
    public const int DenseMeasurementLabelThreshold = 250;
    public const int DenseMeasurementDetailThreshold = 400;
    public const int DenseNavigationFastFrameThreshold = 300;
    public const int DenseMeasurementGeometryThreshold = 1500;
    public const int SlowFrameLogMs = 45;
    public const int SlowRenderLogMs = 220;
    public const int SlowSnapLogMs = 18;
    public const float VisibleGeometryPaddingScreenPx = 96f;
    public const float PanOverscrollScreenFraction = 1.00f;
    private static string _qualityMode = HighQualityMode;
    private static readonly float[] SheetOverlayRenderScaleSteps = [1.0f, 2.0f, 2.25f, 3.0f, 4.0f];
    private static readonly int[] RasterSheetWorkZoomWarmupDpis = [72, 100, 144];
    private static readonly int[] RasterSheetWorkZoomBuildDpis = [72, 100, 144];

    public static string QualityMode => _qualityMode;

    public static int SelectJobOpenPreviewWarmupCount(int pageCount) =>
        JobOpenPreviewWarmupAllPages
            ? Math.Max(0, pageCount)
            : SelectAdaptiveWarmupCount(
                pageCount,
                JobOpenPreviewWarmupCount,
                JobOpenPreviewWarmupLargeJobCount,
                JobOpenPreviewWarmupHugeJobCount);

    public static int SelectJobOpenRasterSheetRefreshWarmupCount(int pageCount) =>
        JobOpenRasterSheetRefreshWarmupAllPages
            ? Math.Max(0, pageCount)
            : SelectAdaptiveWarmupCount(
                pageCount,
                JobOpenRasterSheetRefreshWarmupCount,
                JobOpenRasterSheetRefreshWarmupLargeJobCount,
                JobOpenRasterSheetRefreshWarmupHugeJobCount);

    public static int SelectJobOpenRasterSheetBitmapWarmupCount(int pageCount) =>
        SelectAdaptiveWarmupCount(
            pageCount,
            JobOpenRasterSheetBitmapWarmupCount,
            JobOpenRasterSheetBitmapWarmupLargeJobCount,
            JobOpenRasterSheetBitmapWarmupHugeJobCount);

    public static int RasterSheetDisplayMaxDpi => RasterSheetDisplayDpiSteps[^1];

    public static IReadOnlyList<int> RasterSheetWorkZoomWarmupDpiSteps => RasterSheetWorkZoomWarmupDpis;

    public static IReadOnlyList<int> RasterSheetWorkZoomBuildDpiSteps => RasterSheetWorkZoomBuildDpis;

    public static float CurrentResponsiveMaxRenderScale => CurrentQuality.ResponsiveMaxScale;
    public static float CurrentDetailRenderPaddingScreenPx => CurrentQuality.DetailPaddingScreenPx;

    private static int SelectAdaptiveWarmupCount(
        int pageCount,
        int normalCount,
        int largeJobCount,
        int hugeJobCount)
    {
        if (pageCount <= 0)
            return 0;

        int selected = pageCount >= JobOpenWarmupHugeJobThreshold
            ? hugeJobCount
            : pageCount >= JobOpenWarmupLargeJobThreshold
                ? largeJobCount
                : normalCount;
        return Math.Clamp(selected, 0, pageCount);
    }

    public static float DetailRenderPaddingScreenPxForZoom(float zoom)
    {
        float configured = CurrentQuality.DetailPaddingScreenPx;
        if (zoom < 2.0f)
            return Math.Min(configured, 256f);
        if (zoom < 4.0f)
            return Math.Min(configured, 384f);

        return Math.Min(configured, 640f);
    }

    public static string NormalizeQualityMode(string? mode)
    {
        string clean = (mode ?? "").Trim();
        if (string.Equals(clean, BalancedQualityMode, StringComparison.OrdinalIgnoreCase))
            return BalancedQualityMode;
        if (string.Equals(clean, MaxQualityMode, StringComparison.OrdinalIgnoreCase))
            return MaxQualityMode;
        return HighQualityMode;
    }

    public static void ApplyQualityMode(string? mode)
    {
        _qualityMode = NormalizeQualityMode(mode);
    }

    private static RenderQuality CurrentQuality => _qualityMode switch
    {
        BalancedQualityMode => new RenderQuality(ResponsiveMaxRenderScale, 96_000_000f, 8.0f, 96_000_000f, 512f, 8.0f),
        MaxQualityMode => new RenderQuality(4.0f, 240_000_000f, DetailRenderMaxScale, 160_000_000f, 768f, DetailRenderMaxScale),
        _ => new RenderQuality(3.0f, 160_000_000f, 12.0f, 120_000_000f, 640f, 12.0f),
    };

    public static bool ShouldUseFastNavigationFrame(
        bool simplifyNavigationRendering,
        bool isFastNavigating,
        float zoom,
        int activePageMeasurementCount,
        bool hasBlockingInteraction)
    {
        return isFastNavigating && !hasBlockingInteraction;
    }

    public static float SelectRenderScale(
        float zoom,
        IReadOnlyList<float> renderScaleSteps,
        float pageWidthPt = 0,
        float pageHeightPt = 0)
    {
        if (zoom <= 0 || renderScaleSteps.Count == 0)
            return 1.0f;

        float maxScale = Math.Min(renderScaleSteps[^1], CurrentQuality.ResponsiveMaxScale);
        maxScale = Math.Min(maxScale, PixelBudgetMaxRenderScale(pageWidthPt, pageHeightPt));
        float minScale = Math.Min(ResponsiveMinRenderScale, maxScale);
        float desired = Math.Clamp(zoom, minScale, maxScale);
        foreach (float step in renderScaleSteps)
        {
            if (desired <= step)
                return Math.Min(step, maxScale);
        }

        return maxScale;
    }

    public static float SelectSheetOverlayRenderScale(
        float zoom,
        float pageWidthPt = 0,
        float pageHeightPt = 0)
    {
        float maxScale = Math.Min(CurrentQuality.ResponsiveMaxScale, PixelBudgetMaxRenderScale(pageWidthPt, pageHeightPt));
        maxScale = Math.Min(maxScale, SheetOverlayPixelBudgetMaxRenderScale(pageWidthPt, pageHeightPt));
        if (maxScale <= 0)
            return SheetOverlayViewportRenderScale;

        float minScale = Math.Min(SheetOverlayLowZoomRenderScale, maxScale);
        float normalizedZoom = zoom <= 0 ? SheetOverlayLowZoomRenderScale : zoom;
        float minimumForZoom = normalizedZoom < ZoomRefreshMinZoom
            ? SheetOverlayLowZoomRenderScale
            : SheetOverlayViewportRenderScale;
        float desired = Math.Clamp(
            Math.Max(normalizedZoom, minimumForZoom),
            minScale,
            maxScale);
        foreach (float step in SheetOverlayRenderScaleSteps)
        {
            if (desired <= step)
                return Math.Min(step, maxScale);
        }

        return maxScale;
    }

    public static bool ShouldUseDetailRender(float zoom, float bitmapScale)
    {
        if (!DetailRenderEnabled || zoom < DetailRenderMinZoom || bitmapScale <= 0)
            return false;

        return zoom >= bitmapScale * DetailRenderMinScaleGain;
    }

    public static bool ShouldUseZoomRefreshRender(float zoom, float bitmapScale)
    {
        if (zoom < ZoomRefreshMinZoom || bitmapScale <= 0)
            return false;

        if (bitmapScale < ResponsiveMinRenderScale * 0.95f)
            return true;

        return zoom >= bitmapScale * DetailRenderMinScaleGain;
    }

    public static int SelectRasterSheetDisplayDpi(float zoom)
    {
        if (zoom <= 0)
            return RasterSheetDisplayDpiSteps[0];

        if (zoom >= 0.95f && zoom < 2.0f)
            return 144;

        int desiredDpi = (int)Math.Ceiling(Math.Clamp(zoom * 72.0f, 72.0f, RasterSheetDisplayDpiSteps[^1]) - 0.001f);
        foreach (int dpi in RasterSheetDisplayDpiSteps)
        {
            if (desiredDpi <= dpi)
                return dpi;
        }

        return RasterSheetDisplayDpiSteps[^1];
    }

    public static int SelectRasterSheetNavigationDpi(float zoom, int currentDpi, int targetDpi)
    {
        if (zoom <= 0 || currentDpi <= 0 || targetDpi <= 0)
            return 0;

        int targetIndex = 0;
        for (int i = 0; i < RasterSheetDisplayDpiSteps.Length; i++)
        {
            targetIndex = i;
            if (targetDpi <= RasterSheetDisplayDpiSteps[i])
                break;
        }

        int navigationIndex = Math.Max(0, targetIndex - 1);
        int selected = RasterSheetDisplayDpiSteps[navigationIndex];
        return Math.Min(selected, RasterSheetNavigationMaxDpi);
    }

    public static bool ShouldHoldRasterSheetQualityAfterNavigation(TimeSpan navigationIdle, int targetDpi)
    {
        if (targetDpi <= RasterSheetNavigationMaxDpi)
            return false;

        if (navigationIdle < TimeSpan.Zero)
            return true;

        return navigationIdle.TotalMilliseconds < RasterSheetMotionQualityRestoreQuietMs;
    }

    public static TimeSpan RasterSheetQualityRestoreDelay(TimeSpan navigationIdle)
    {
        if (navigationIdle < TimeSpan.Zero)
            navigationIdle = TimeSpan.Zero;

        TimeSpan target = TimeSpan.FromMilliseconds(RasterSheetMotionQualityRestoreQuietMs);
        return navigationIdle >= target ? TimeSpan.Zero : target - navigationIdle;
    }

    public static bool ShouldPreferLowerRasterSheetDpi(int currentDpi, int targetDpi)
    {
        if (currentDpi <= 0 || targetDpi <= 0 || currentDpi <= targetDpi)
            return false;

        return currentDpi > targetDpi * LowZoomBitmapDowngradeRatio;
    }

    public static bool ShouldPreferLowerScalePageBitmapForNavigation(
        float zoom,
        float currentBitmapScale,
        float targetBitmapScale)
    {
        if (zoom <= 0 ||
            currentBitmapScale <= 0 ||
            targetBitmapScale <= 0 ||
            zoom >= FarZoomFastFrameThreshold)
        {
            return false;
        }

        return currentBitmapScale > targetBitmapScale * LowZoomBitmapDowngradeRatio;
    }

    public static bool ShouldSkipFullPageSharpUpgradeAtLowZoom(
        float zoom,
        float currentBitmapScale,
        float targetBitmapScale)
    {
        if (zoom >= PageSwitchSharpUpgradeMinZoom ||
            currentBitmapScale <= 0 ||
            targetBitmapScale <= 0 ||
            currentBitmapScale < FastPageSwitchPreviewRenderScale * 0.95f)
        {
            return false;
        }

        return targetBitmapScale > currentBitmapScale * 1.05f;
    }

    public static bool ShouldSkipFullRefreshDuringDetail(float bitmapScale) =>
        bitmapScale >= ResponsiveMinRenderScale * 0.95f;

    public static bool ShouldPreferDetailRenderOverFullRefresh(float zoom, float bitmapScale) =>
        ShouldUseDetailRender(zoom, bitmapScale) &&
        bitmapScale < ResponsiveMinRenderScale * 0.95f;

    public static float SelectDetailRenderScale(
        float zoom,
        float clipWidthPt,
        float clipHeightPt,
        float bitmapScale)
    {
        if (!ShouldUseDetailRender(zoom, bitmapScale) || clipWidthPt <= 0 || clipHeightPt <= 0)
            return 0;

        float minScale = bitmapScale * DetailRenderMinScaleGain;
        float maxScale = Math.Min(CurrentQuality.DetailMaxScale, CurrentQuality.DetailInteractiveMaxScale);
        if (minScale > maxScale)
            return 0;

        float target = Math.Clamp(zoom, minScale, maxScale);
        float clipPoints = clipWidthPt * clipHeightPt;
        if (clipPoints > 0)
        {
            float budgetScale = MathF.Sqrt(CurrentQuality.DetailMaxPixels / clipPoints);
            target = Math.Min(target, budgetScale);
        }

        return target >= minScale ? Math.Max(target, bitmapScale) : 0;
    }

    public static bool ShouldUseDetailRenderPrefetch(float zoom, bool isFastNavigating) =>
        ShouldUseDetailRenderPrefetch(zoom, isFastNavigating, allowDuringNavigationPrefetch: false);

    public static bool ShouldUseDetailRenderPrefetch(
        float zoom,
        bool isFastNavigating,
        bool allowDuringNavigationPrefetch) =>
        DetailRenderPrefetchEnabled &&
        !string.Equals(QualityMode, BalancedQualityMode, StringComparison.Ordinal) &&
        (!isFastNavigating || allowDuringNavigationPrefetch) &&
        zoom >= DetailRenderPrefetchMinZoom;

    private static float PixelBudgetMaxRenderScale(float pageWidthPt, float pageHeightPt)
    {
        if (pageWidthPt <= 0 || pageHeightPt <= 0)
            return CurrentQuality.ResponsiveMaxScale;

        float pagePoints = pageWidthPt * pageHeightPt;
        if (pagePoints <= 0)
            return CurrentQuality.ResponsiveMaxScale;

        float budgetScale = MathF.Sqrt(CurrentQuality.ResponsiveMaxPixels / pagePoints);
        return Math.Clamp(budgetScale, ResponsiveMinRenderScale, CurrentQuality.ResponsiveMaxScale);
    }

    private static float SheetOverlayPixelBudgetMaxRenderScale(float pageWidthPt, float pageHeightPt)
    {
        if (pageWidthPt <= 0 || pageHeightPt <= 0)
            return CurrentQuality.ResponsiveMaxScale;

        float pagePoints = pageWidthPt * pageHeightPt;
        if (pagePoints <= 0)
            return CurrentQuality.ResponsiveMaxScale;

        float budgetScale = MathF.Sqrt(SheetOverlayMaxRenderPixels / pagePoints);
        return Math.Clamp(budgetScale, ResponsiveMinRenderScale, CurrentQuality.ResponsiveMaxScale);
    }

    public static bool ShouldDrawMeasurementLabels(
        float zoom,
        int activePageMeasurementCount,
        bool fastNavigationFrame)
    {
        return activePageMeasurementCount <= DenseMeasurementLabelThreshold;
    }

    public static bool ShouldDrawMeasurementDetails(
        float zoom,
        int activePageMeasurementCount,
        bool fastNavigationFrame)
    {
        return !fastNavigationFrame &&
               zoom >= MeasurementLabelMinZoom &&
               activePageMeasurementCount <= DenseMeasurementDetailThreshold;
    }

    public static bool ShouldDrawSheetOverlay(
        bool fastNavigationFrame,
        bool isOverlayEditing)
    {
        return !fastNavigationFrame || isOverlayEditing;
    }

    public static bool ShouldDrawMeasurementGeometry(
        int activePageMeasurementCount,
        bool fastNavigationFrame)
    {
        return true;
    }

    public static bool ShouldUseSimplifiedAreaPaint(
        float zoom,
        int activePageMeasurementCount,
        bool fastNavigationFrame)
    {
        return activePageMeasurementCount >= DenseMeasurementLabelThreshold &&
               (fastNavigationFrame || zoom <= FarZoomFastFrameThreshold);
    }

    public static float VisibleGeometryPaddingPdf(float zoom) =>
        VisibleGeometryPaddingScreenPx / Math.Max(zoom, 0.001f);

    public static float ClampPanWithOverscroll(float pan, float pageSizePt, float visibleSizePt)
    {
        if (pageSizePt <= 0 || visibleSizePt <= 0)
            return pan;

        float margin = visibleSizePt * PanOverscrollScreenFraction;
        float min = -margin;
        float max = pageSizePt - visibleSizePt + margin;
        if (max < min)
        {
            float center = (min + max) / 2f;
            min = center;
            max = center;
        }

        return Math.Clamp(pan, min, max);
    }

    private readonly record struct RenderQuality(
        float ResponsiveMaxScale,
        float ResponsiveMaxPixels,
        float DetailMaxScale,
        float DetailMaxPixels,
        float DetailPaddingScreenPx,
        float DetailInteractiveMaxScale);
}
