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
    public const float ZoomRefreshMinZoom = 0.30f;
    public const bool DetailRenderEnabled = true;
    public const float DetailRenderMinZoom = 1.0f;
    public const float DetailRenderMinScaleGain = 1.18f;
    public const float DetailRenderMaxScale = 16.0f;
    public const float DetailRenderMaxPixels = 96_000_000f;
    public const float DetailRenderPaddingScreenPx = 1024f;
    public const bool DetailRenderPrefetchEnabled = true;
    public const float DetailRenderPrefetchMinZoom = 4.0f;
    public const int DetailRenderPrefetchTileCount = 2;
    public const int DetailRenderPrefetchConcurrency = 1;
    public const float DetailRenderPrefetchShiftFactor = 0.80f;
    public const int DetailRenderMaxPaintTiles = 2;
    public const float InstantPagePreviewRenderScale = 0.35f;
    public const float FastPageSwitchPreviewRenderScale = 0.15f;
    public const float InitialPagePreviewRenderScale = 0.75f;
    public const float SheetOverlayViewportRenderScale = 1.0f;
    public const float SheetOverlayExportRenderScale = 2.0f;
    public const float MeasurementLabelMinZoom = 0.95f;
    public const int DenseMeasurementLabelThreshold = 250;
    public const int DenseMeasurementDetailThreshold = 400;
    public const int DenseNavigationFastFrameThreshold = 300;
    public const int DenseMeasurementGeometryThreshold = 1500;
    public const int SlowFrameLogMs = 45;
    public const int SlowRenderLogMs = 220;
    public const int SlowSnapLogMs = 18;
    public const float VisibleGeometryPaddingScreenPx = 96f;
    private static string _qualityMode = HighQualityMode;

    public static string QualityMode => _qualityMode;
    public static float CurrentResponsiveMaxRenderScale => CurrentQuality.ResponsiveMaxScale;
    public static float CurrentDetailRenderPaddingScreenPx => CurrentQuality.DetailPaddingScreenPx;

    public static float DetailRenderPaddingScreenPxForZoom(float zoom)
    {
        float configured = CurrentQuality.DetailPaddingScreenPx;
        if (zoom < 2.0f)
            return Math.Min(configured, 384f);
        if (zoom < 4.0f)
            return Math.Min(configured, 512f);

        return configured;
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
        BalancedQualityMode => new RenderQuality(ResponsiveMaxRenderScale, 96_000_000f, 8.0f, 96_000_000f, 512f),
        MaxQualityMode => new RenderQuality(4.0f, 320_000_000f, DetailRenderMaxScale, 320_000_000f, 1024f),
        _ => new RenderQuality(3.0f, 192_000_000f, 12.0f, 192_000_000f, 768f),
    };

    public static bool ShouldUseFastNavigationFrame(
        bool simplifyNavigationRendering,
        bool isFastNavigating,
        float zoom,
        int activePageMeasurementCount,
        bool hasBlockingInteraction)
    {
        return simplifyNavigationRendering &&
               isFastNavigating &&
               !hasBlockingInteraction &&
               (zoom <= FarZoomFastFrameThreshold ||
                zoom >= HighZoomFastFrameThreshold ||
                activePageMeasurementCount >= DenseNavigationFastFrameThreshold);
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

    public static bool ShouldSkipFullRefreshDuringDetail(float bitmapScale) =>
        bitmapScale >= ResponsiveMinRenderScale * 0.95f;

    public static float SelectDetailRenderScale(
        float zoom,
        float clipWidthPt,
        float clipHeightPt,
        float bitmapScale)
    {
        if (!ShouldUseDetailRender(zoom, bitmapScale) || clipWidthPt <= 0 || clipHeightPt <= 0)
            return 0;

        float target = Math.Clamp(zoom, bitmapScale * DetailRenderMinScaleGain, CurrentQuality.DetailMaxScale);
        float clipPoints = clipWidthPt * clipHeightPt;
        if (clipPoints > 0)
        {
            float budgetScale = MathF.Sqrt(CurrentQuality.DetailMaxPixels / clipPoints);
            target = Math.Min(target, budgetScale);
        }

        return target >= bitmapScale * DetailRenderMinScaleGain ? Math.Max(target, bitmapScale) : 0;
    }

    public static bool ShouldUseDetailRenderPrefetch(float zoom, bool isFastNavigating) =>
        DetailRenderPrefetchEnabled &&
        !isFastNavigating &&
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
        return true;
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

    private readonly record struct RenderQuality(
        float ResponsiveMaxScale,
        float ResponsiveMaxPixels,
        float DetailMaxScale,
        float DetailMaxPixels,
        float DetailPaddingScreenPx);
}
