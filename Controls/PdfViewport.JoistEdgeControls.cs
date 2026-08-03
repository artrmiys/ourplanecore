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
            (SKPoint topCenter, SKPoint bottomCenter) = JoistEdgeControlCenters(area);
            (bool startEnabled, bool endEnabled) = JoistTakeoffCalculator.ResolveEdgeJoists(area);
            JoistEdgeControlSide topSide = TopJoistEdgeControlSide(area);
            DrawJoistEdgeControl(
                canvas,
                topCenter,
                EdgeEnabled(topSide, startEnabled, endEnabled));
            DrawJoistEdgeControl(
                canvas,
                bottomCenter,
                EdgeEnabled(OppositeEdgeControlSide(topSide), startEnabled, endEnabled));
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
            (SKPoint topCenter, SKPoint bottomCenter) = JoistEdgeControlCenters(area);
            float radius = ScreenToPdfDistance(
                JoistEdgeControlSizePx / 2f + JoistEdgeControlHitPaddingPx);
            JoistEdgeControlSide topSide = TopJoistEdgeControlSide(area);
            bool hitTopControl = DistanceSquared(pdf, topCenter) <= radius * radius;
            JoistEdgeControlSide? side = hitTopControl
                ? topSide
                : DistanceSquared(pdf, bottomCenter) <= radius * radius
                    ? OppositeEdgeControlSide(topSide)
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
            string edgeName = hitTopControl ? "upper/left" : "lower/right";
            bool enabled = side == JoistEdgeControlSide.Start
                ? area.JoistStartEdgeEnabled
                : area.JoistEndEdgeEnabled;
            PostStatus($"Joist Area {edgeName} edge: {(enabled ? "on" : "off")}. Ctrl+Z restores it.");
            return true;
        }

        return false;
    }

    private (SKPoint TopCenter, SKPoint BottomCenter) JoistEdgeControlCenters(Measurement area)
    {
        SKRect bounds = RawMeasurementBounds(area);
        float inset = ScreenToPdfDistance(JoistEdgeControlInsetPx);
        float left = bounds.Left + inset;
        return (
            new SKPoint(left, bounds.Top + inset),
            new SKPoint(left, bounds.Bottom - inset));
    }

    private static JoistEdgeControlSide TopJoistEdgeControlSide(Measurement area)
    {
        double direction = JoistTakeoffCalculator.NormalizeDirectionDegrees(
            area.JoistDirectionDegrees);
        double radians = direction * Math.PI / 180.0;
        double normalX = -Math.Sin(radians);
        double normalY = Math.Cos(radians);
        bool startIsTopOrLeft = Math.Abs(normalY) >= Math.Abs(normalX)
            ? normalY >= 0
            : normalX >= 0;
        return startIsTopOrLeft
            ? JoistEdgeControlSide.Start
            : JoistEdgeControlSide.End;
    }

    private static JoistEdgeControlSide OppositeEdgeControlSide(JoistEdgeControlSide side) =>
        side == JoistEdgeControlSide.Start
            ? JoistEdgeControlSide.End
            : JoistEdgeControlSide.Start;

    private static bool EdgeEnabled(
        JoistEdgeControlSide side,
        bool startEnabled,
        bool endEnabled) =>
        side == JoistEdgeControlSide.Start ? startEnabled : endEnabled;

    private static bool IsJoistEdgeControlTarget(Measurement measurement) =>
        measurement.JoistEnabled &&
        OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
        measurement.Points.Count >= 3;
}
