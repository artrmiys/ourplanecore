using System;
using System.Collections.Generic;

namespace OurPlaneCore;

public static class ViewportRenderPolicy
{
    public const float HighZoomFastFrameThreshold = 2.0f;
    public const float ResponsiveMaxRenderScale = 1.5f;

    public static bool ShouldUseFastNavigationFrame(
        bool simplifyNavigationRendering,
        bool isFastNavigating,
        float zoom,
        bool hasBlockingInteraction)
    {
        return isFastNavigating &&
               !hasBlockingInteraction &&
               (simplifyNavigationRendering || zoom >= HighZoomFastFrameThreshold);
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
}
