using System;
using System.Collections.Generic;

namespace OurPlaneCore;

public static class ViewportRenderPolicy
{
    public const float HighZoomFastFrameThreshold = 2.0f;
    public const float FarZoomFastFrameThreshold = 0.85f;
    public const float ResponsiveMaxRenderScale = 1.5f;
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

    public static bool ShouldUseFastNavigationFrame(
        bool simplifyNavigationRendering,
        bool isFastNavigating,
        float zoom,
        int activePageMeasurementCount,
        bool hasBlockingInteraction)
    {
        return isFastNavigating &&
               !hasBlockingInteraction &&
               (simplifyNavigationRendering ||
                zoom <= FarZoomFastFrameThreshold ||
                zoom >= HighZoomFastFrameThreshold ||
                activePageMeasurementCount >= DenseNavigationFastFrameThreshold);
    }

    public static float SelectRenderScale(float zoom, IReadOnlyList<float> renderScaleSteps)
    {
        if (zoom <= 0 || renderScaleSteps.Count == 0)
            return 1.0f;

        float maxScale = Math.Min(renderScaleSteps[^1], ResponsiveMaxRenderScale);
        float desired = Math.Clamp(zoom, renderScaleSteps[0], maxScale);
        foreach (float step in renderScaleSteps)
        {
            if (desired <= step)
                return Math.Min(step, maxScale);
        }

        return maxScale;
    }

    public static bool ShouldDrawMeasurementLabels(
        float zoom,
        int activePageMeasurementCount,
        bool fastNavigationFrame)
    {
        return !fastNavigationFrame &&
               zoom >= MeasurementLabelMinZoom &&
               activePageMeasurementCount <= DenseMeasurementLabelThreshold;
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
}
