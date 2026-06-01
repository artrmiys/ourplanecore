using System;
using System.Collections.Generic;

namespace OurPlaneCore;

public static class ViewportRenderPolicy
{
    public const float HighZoomFastFrameThreshold = 2.0f;
    public const float FarZoomFastFrameThreshold = 0.85f;
    public const float ResponsiveMinRenderScale = 1.0f;
    public const float ResponsiveMaxRenderScale = 2.25f;
    public const float ResponsiveMaxRenderPixels = 24_000_000f;
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

        float maxScale = Math.Min(renderScaleSteps[^1], ResponsiveMaxRenderScale);
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

    private static float PixelBudgetMaxRenderScale(float pageWidthPt, float pageHeightPt)
    {
        if (pageWidthPt <= 0 || pageHeightPt <= 0)
            return ResponsiveMaxRenderScale;

        float pagePoints = pageWidthPt * pageHeightPt;
        if (pagePoints <= 0)
            return ResponsiveMaxRenderScale;

        float budgetScale = MathF.Sqrt(ResponsiveMaxRenderPixels / pagePoints);
        return Math.Clamp(budgetScale, ResponsiveMinRenderScale, ResponsiveMaxRenderScale);
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
        return !fastNavigationFrame ||
               activePageMeasurementCount <= DenseMeasurementGeometryThreshold;
    }

    public static float VisibleGeometryPaddingPdf(float zoom) =>
        VisibleGeometryPaddingScreenPx / Math.Max(zoom, 0.001f);
}
