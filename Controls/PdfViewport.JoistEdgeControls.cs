using System;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private const float JoistEdgeControlSizePx = 17f;
    private const float JoistEdgeControlInsetPx = 14f;
    private const float JoistEdgeControlHitPaddingPx = 5f;

    private enum JoistEdgeControlSide
    {
        Start,
        End,
    }

    private void DrawJoistEdgeControls(SKCanvas canvas)
    {
        if (_renderNavigationFastFrame || _tool != ViewerTool.Select)
            return;

        foreach (Measurement area in GetSelectedMeasurements().Where(IsJoistEdgeControlTarget))
        {
            (SKPoint startCenter, SKPoint endCenter) = JoistEdgeControlCenters(area);
            (bool startEnabled, bool endEnabled) = JoistTakeoffCalculator.ResolveEdgeJoists(area);
            DrawJoistEdgeControl(canvas, startCenter, startEnabled);
            DrawJoistEdgeControl(canvas, endCenter, endEnabled);
        }
    }

    private void DrawJoistEdgeControl(SKCanvas canvas, SKPoint center, bool isChecked)
    {
        float half = ScreenToPdfDistance(JoistEdgeControlSizePx / 2f);
        float radius = ScreenToPdfDistance(3f);
        var rect = new SKRect(center.X - half, center.Y - half, center.X + half, center.Y + half);

        using var shadow = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(85),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        using var fill = new SKPaint
        {
            Color = isChecked ? new SKColor(0xF4, 0x9B, 0x24) : SKColors.White,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        using var border = new SKPaint
        {
            Color = new SKColor(0x43, 0x57, 0x6B),
            StrokeWidth = ScreenToPdfDistance(1.5f),
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        var shadowRect = rect;
        float shadowOffset = ScreenToPdfDistance(1.5f);
        shadowRect.Offset(shadowOffset, shadowOffset);
        canvas.DrawRoundRect(shadowRect, radius, radius, shadow);
        canvas.DrawRoundRect(rect, radius, radius, fill);
        canvas.DrawRoundRect(rect, radius, radius, border);

        if (!isChecked)
            return;

        using var tick = new SKPaint
        {
            Color = SKColors.White,
            StrokeWidth = ScreenToPdfDistance(2.2f),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };
        using var path = new SKPath();
        path.MoveTo(center.X - half * 0.50f, center.Y);
        path.LineTo(center.X - half * 0.12f, center.Y + half * 0.38f);
        path.LineTo(center.X + half * 0.55f, center.Y - half * 0.43f);
        canvas.DrawPath(path, tick);
    }

    private bool TryToggleJoistEdgeControl(SKPoint pdf)
    {
        if (IsReadOnlyMode || _tool != ViewerTool.Select)
            return false;

        foreach (Measurement area in GetSelectedMeasurements().Where(IsJoistEdgeControlTarget).Reverse())
        {
            (SKPoint startCenter, SKPoint endCenter) = JoistEdgeControlCenters(area);
            float radius = ScreenToPdfDistance(
                JoistEdgeControlSizePx / 2f + JoistEdgeControlHitPaddingPx);
            JoistEdgeControlSide? side = DistanceSquared(pdf, startCenter) <= radius * radius
                ? JoistEdgeControlSide.Start
                : DistanceSquared(pdf, endCenter) <= radius * radius
                    ? JoistEdgeControlSide.End
                    : null;
            if (!side.HasValue)
                continue;

            PushGeometryUndoSnapshot([area], [], "restore Joist Area edge option", "joist-edge-toggle");
            (bool startEnabled, bool endEnabled) = JoistTakeoffCalculator.ResolveEdgeJoists(area);
            area.JoistStartEdgeEnabled = side == JoistEdgeControlSide.Start
                ? !startEnabled
                : startEnabled;
            area.JoistEndEdgeEnabled = side == JoistEdgeControlSide.End
                ? !endEnabled
                : endEnabled;
            area.JoistEdgeOverridesSet = true;
            NotifyMeasurementsChanged([area]);
            RequestRepaint();
            string edgeName = side == JoistEdgeControlSide.Start ? "start" : "end";
            bool enabled = side == JoistEdgeControlSide.Start
                ? area.JoistStartEdgeEnabled
                : area.JoistEndEdgeEnabled;
            PostStatus($"Joist Area {edgeName} edge: {(enabled ? "on" : "off")}. Ctrl+Z restores it.");
            return true;
        }

        return false;
    }

    private (SKPoint StartCenter, SKPoint EndCenter) JoistEdgeControlCenters(Measurement area)
    {
        SKRect bounds = RawMeasurementBounds(area);
        float inset = ScreenToPdfDistance(JoistEdgeControlInsetPx);
        float half = ScreenToPdfDistance(JoistEdgeControlSizePx / 2f + 2f);
        float x = bounds.Left + inset;
        float top = bounds.Top + inset;
        float bottom = bounds.Bottom - inset;
        if (bottom - top < inset * 2f)
        {
            float center = (bounds.Top + bounds.Bottom) / 2f;
            top = center - inset;
            bottom = center + inset;
        }

        SKPoint interior = FindJoistEdgeControlInterior(area, bounds);
        SKPoint start = MoveJoistEdgeControlInside(area, new SKPoint(x, top), interior, half);
        SKPoint end = MoveJoistEdgeControlInside(area, new SKPoint(x, bottom), interior, half);
        return (start, end);
    }

    private static SKPoint FindJoistEdgeControlInterior(Measurement area, SKRect bounds)
    {
        var boundsCenter = new SKPoint(
            (bounds.Left + bounds.Right) / 2f,
            (bounds.Top + bounds.Bottom) / 2f);
        if (PointInMeasurementFill(area, boundsCenter))
            return boundsCenter;

        SKPoint centroid = Centroid(area.Points);
        if (PointInMeasurementFill(area, centroid))
            return centroid;

        const int samples = 9;
        for (int row = 1; row < samples; row++)
        {
            float y = bounds.Top + bounds.Height * row / samples;
            for (int column = 1; column < samples; column++)
            {
                float x = bounds.Left + bounds.Width * column / samples;
                var candidate = new SKPoint(x, y);
                if (PointInMeasurementFill(area, candidate))
                    return candidate;
            }
        }

        return area.Points[0];
    }

    private static SKPoint MoveJoistEdgeControlInside(
        Measurement area,
        SKPoint target,
        SKPoint interior,
        float half)
    {
        if (JoistEdgeControlFitsArea(area, target, half))
            return target;

        const int steps = 32;
        for (int step = 1; step <= steps; step++)
        {
            float progress = step / (float)steps;
            var candidate = new SKPoint(
                target.X + (interior.X - target.X) * progress,
                target.Y + (interior.Y - target.Y) * progress);
            if (JoistEdgeControlFitsArea(area, candidate, half))
                return candidate;
        }

        return PointInMeasurementFill(area, interior) ? interior : target;
    }

    private static bool JoistEdgeControlFitsArea(Measurement area, SKPoint center, float half)
    {
        float sample = half * 0.92f;
        return PointInMeasurementFill(area, center) &&
               PointInMeasurementFill(area, new SKPoint(center.X - sample, center.Y - sample)) &&
               PointInMeasurementFill(area, new SKPoint(center.X + sample, center.Y - sample)) &&
               PointInMeasurementFill(area, new SKPoint(center.X - sample, center.Y + sample)) &&
               PointInMeasurementFill(area, new SKPoint(center.X + sample, center.Y + sample));
    }

    private static bool IsJoistEdgeControlTarget(Measurement measurement) =>
        measurement.JoistEnabled &&
        OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
        measurement.Points.Count >= 3;
}
