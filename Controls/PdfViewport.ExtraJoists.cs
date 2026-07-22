using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using OurPlanCore;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private Measurement? _extraJoistPlacementMeasurement;
    private JoistExtraSegment? _extraJoistPlacementPreview;

    private List<JoistExtraSegment> _dragMeasurementOriginalExtraJoists = [];
    private readonly Dictionary<Measurement, List<JoistExtraSegment>> _dragSelectionOriginalExtraJoists = [];
    private readonly Dictionary<Measurement, List<JoistExtraSegment>> _transformMeasurementOriginalExtraJoists = [];

    public bool IsExtraJoistPlacementActive => _extraJoistPlacementMeasurement != null;

    public bool BeginExtraJoistPlacement(Measurement areaMeasurement)
    {
        if (IsReadOnlyMode)
        {
            PostStatus("Read-only: Extra Joist cannot change the job.");
            return false;
        }

        if (OurPlanCoreJobStore.NormalizeMeasurementType(areaMeasurement.MType) != "area" ||
            !areaMeasurement.JoistEnabled)
        {
            PostStatus("Extra Joist can only be added to a Joist Area.");
            return false;
        }

        if (!areaMeasurement.JoistDirectionLocked)
        {
            PostStatus("Set the Joist Area direction before adding an Extra Joist.");
            return false;
        }

        if (!_measurementSet.Contains(areaMeasurement) ||
            !IsMeasurementOnActivePage(areaMeasurement) ||
            !IsMeasurementTakeoffVisible(areaMeasurement))
        {
            PostStatus("Extra Joist was not started: open and show the owning Joist Area.");
            return false;
        }

        CancelExtraJoistPlacement(postStatus: false);
        CancelJoistDirectionCapture();
        CancelDrawing(clearSelection: false);
        SelectMeasurements([areaMeasurement]);
        _extraJoistPlacementMeasurement = areaMeasurement;
        _extraJoistPlacementPreview = null;
        Cursor = Cursors.Cross;
        PostStatus("Extra Joist: move inside the area and click once to place. Esc cancels.");
        RequestRepaint();
        return true;
    }

    public void CancelExtraJoistPlacement() =>
        CancelExtraJoistPlacement(postStatus: true);

    public bool DeleteNearestExtraJoist(Measurement areaMeasurement, SKPoint pdfPoint)
    {
        if (IsReadOnlyMode)
        {
            PostStatus("Read-only: Extra Joist cannot change the job.");
            return false;
        }

        if (!_measurementSet.Contains(areaMeasurement) ||
            !IsMeasurementOnActivePage(areaMeasurement) ||
            !IsMeasurementTakeoffVisible(areaMeasurement) ||
            !areaMeasurement.JoistEnabled ||
            areaMeasurement.ExtraJoists.Count == 0)
        {
            PostStatus("No Extra Joist is available at this location.");
            return false;
        }

        float tolerance = ScreenToPdfDistance(12f);
        int nearestIndex = -1;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < areaMeasurement.ExtraJoists.Count; i++)
        {
            JoistExtraSegment extra = areaMeasurement.ExtraJoists[i];
            float distance = DistanceToSegment(pdfPoint, extra.Start, extra.End);
            if (distance > tolerance || distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearestIndex = i;
        }

        if (nearestIndex < 0)
        {
            PostStatus("Right-click directly on an Extra Joist to delete it.");
            return false;
        }

        CancelExtraJoistPlacement(postStatus: false);
        PushGeometryUndoSnapshot([areaMeasurement], [], "restore deleted Extra Joist", "extra-joist-delete");
        areaMeasurement.ExtraJoists.RemoveAt(nearestIndex);
        NotifyMeasurementsChanged([areaMeasurement]);
        PostStatus("Deleted Extra Joist. Ctrl+Z restores it.");
        RequestRepaint();
        return true;
    }

    private bool CancelExtraJoistPlacement(bool postStatus)
    {
        if (_extraJoistPlacementMeasurement == null && _extraJoistPlacementPreview == null)
            return false;

        _extraJoistPlacementMeasurement = null;
        _extraJoistPlacementPreview = null;
        UpdateCursor();
        if (postStatus)
            PostStatus("Extra Joist placement cancelled.");
        RequestRepaint();
        return true;
    }

    private bool HandleExtraJoistPlacementMouseMove(SKPoint rawPdf)
    {
        if (_extraJoistPlacementMeasurement is not { } area)
            return false;

        if (!IsExtraJoistPlacementTargetCurrent(area))
        {
            CancelExtraJoistPlacement(postStatus: false);
            return true;
        }

        _extraJoistPlacementPreview = JoistTakeoffCalculator.TryClipExtraJoist(
            area,
            rawPdf,
            out JoistExtraSegment segment)
            ? segment
            : null;
        Cursor = Cursors.Cross;
        RequestPointerMoveRepaint();
        return true;
    }

    private bool HandleExtraJoistPlacementClick(SKPoint rawPdf)
    {
        if (_extraJoistPlacementMeasurement is not { } area)
            return false;

        if (!IsExtraJoistPlacementTargetCurrent(area))
        {
            CancelExtraJoistPlacement(postStatus: false);
            PostStatus("Extra Joist placement stopped because the owning area changed.");
            return true;
        }

        if (!JoistTakeoffCalculator.TryClipExtraJoist(area, rawPdf, out JoistExtraSegment segment))
        {
            _extraJoistPlacementPreview = null;
            PostStatus("Extra Joist: click inside the filled Joist Area (not inside a cutout).");
            RequestRepaint();
            return true;
        }

        PushGeometryUndoSnapshot([area], [], "remove added Extra Joist", "extra-joist-add");
        area.ExtraJoists.Add(CloneExtraJoist(segment));
        _extraJoistPlacementMeasurement = null;
        _extraJoistPlacementPreview = null;
        UpdateCursor();
        NotifyMeasurementsChanged([area]);
        PostStatus("Added Extra Joist. Ctrl+Z removes it.");
        RequestRepaint();
        return true;
    }

    private bool IsExtraJoistPlacementTargetCurrent(Measurement area) =>
        !IsReadOnlyMode &&
        _measurementSet.Contains(area) &&
        IsMeasurementOnActivePage(area) &&
        IsMeasurementTakeoffVisible(area) &&
        area.JoistEnabled &&
        area.JoistDirectionLocked;

    private void DrawExtraJoistPlacementPreview(SKCanvas canvas)
    {
        if (_extraJoistPlacementPreview is not { } preview)
            return;

        using var halo = new SKPaint
        {
            Color = SKColors.White.WithAlpha(235),
            StrokeWidth = ScreenToPdfDistance(6f),
            StrokeCap = SKStrokeCap.Round,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };
        using var highlight = new SKPaint
        {
            Color = new SKColor(0xFF, 0xC4, 0x00),
            StrokeWidth = ScreenToPdfDistance(3.2f),
            StrokeCap = SKStrokeCap.Round,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };
        canvas.DrawLine(preview.Start, preview.End, halo);
        canvas.DrawLine(preview.Start, preview.End, highlight);
    }

    private static List<JoistExtraSegment> CloneExtraJoists(IEnumerable<JoistExtraSegment> source) =>
        source.Select(CloneExtraJoist).ToList();

    private static JoistExtraSegment CloneExtraJoist(JoistExtraSegment source) =>
        new()
        {
            Id = source.Id,
            Start = new SKPoint(source.Start.X, source.Start.Y),
            End = new SKPoint(source.End.X, source.End.Y),
        };

    private static void RestoreExtraJoists(
        List<JoistExtraSegment> target,
        IEnumerable<JoistExtraSegment> source)
    {
        target.Clear();
        target.AddRange(CloneExtraJoists(source));
    }

    private static void ApplyTransformToExtraJoists(
        List<JoistExtraSegment> extraJoists,
        Func<SKPoint, SKPoint> transform)
    {
        foreach (JoistExtraSegment extra in extraJoists)
        {
            extra.Start = transform(extra.Start);
            extra.End = transform(extra.End);
        }
    }

    private static void RestoreTransformedExtraJoists(
        List<JoistExtraSegment> target,
        IEnumerable<JoistExtraSegment> originals,
        Func<SKPoint, SKPoint> transform)
    {
        target.Clear();
        foreach (JoistExtraSegment original in originals)
        {
            target.Add(new JoistExtraSegment
            {
                Id = original.Id,
                Start = transform(original.Start),
                End = transform(original.End),
            });
        }
    }

    private static void RestoreTranslatedExtraJoists(
        Measurement target,
        IEnumerable<JoistExtraSegment>? originals,
        SKPoint delta)
    {
        if (originals == null)
            return;

        RestoreTransformedExtraJoists(
            target.ExtraJoists,
            originals,
            point => new SKPoint(point.X + delta.X, point.Y + delta.Y));
    }

    private static bool SameExtraJoists(
        IReadOnlyList<JoistExtraSegment> left,
        IReadOnlyList<JoistExtraSegment> right)
    {
        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Id, right[i].Id, StringComparison.Ordinal) ||
                Math.Abs(left[i].Start.X - right[i].Start.X) > ViewportConstants.ZeroLengthEpsilon ||
                Math.Abs(left[i].Start.Y - right[i].Start.Y) > ViewportConstants.ZeroLengthEpsilon ||
                Math.Abs(left[i].End.X - right[i].End.X) > ViewportConstants.ZeroLengthEpsilon ||
                Math.Abs(left[i].End.Y - right[i].End.Y) > ViewportConstants.ZeroLengthEpsilon)
            {
                return false;
            }
        }

        return true;
    }

    private static SKPoint ExtraJoistMidpoint(JoistExtraSegment extra) =>
        new((extra.Start.X + extra.End.X) / 2f, (extra.Start.Y + extra.End.Y) / 2f);
}
