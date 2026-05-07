using System;
using System.Collections.Generic;

namespace OurPlaneCore;

public static class ViewportRenderPolicy
{
    public const float HighZoomFastFrameThreshold = 3.0f;

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

        float desired = Math.Clamp(zoom, renderScaleSteps[0], renderScaleSteps[^1]);
        foreach (float step in renderScaleSteps)
        {
            if (desired <= step)
                return step;
        }

        return renderScaleSteps[^1];
    }
}
