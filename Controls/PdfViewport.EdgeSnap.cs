using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private EdgeSnapCandidate? _edgeSnapCandidate;
    private EdgeSnapPreview? _edgeSnapPreview;
    private int _edgeSnapCycleMode;

    private bool UpdateEdgeSnapPreview(SKPoint rawPdf)
    {
        if (!CanUseEdgeSnapPreview())
        {
            ClearEdgeSnapPreview();
            return false;
        }

        if (!TryFindEdgeSnapCandidate(rawPdf, out EdgeSnapCandidate? candidate) || candidate == null)
        {
            ClearEdgeSnapPreview();
            return false;
        }

        if (_edgeSnapCandidate == null ||
            !ReferenceEquals(_edgeSnapCandidate.Measurement, candidate.Measurement) ||
            _edgeSnapCandidate.SegmentIndex != candidate.SegmentIndex ||
            _edgeSnapCandidate.Closed != candidate.Closed)
        {
            _edgeSnapCycleMode = 0;
        }

        _edgeSnapCandidate = candidate;
        _edgeSnapPreview = BuildEdgeSnapPreview(candidate, _edgeSnapCycleMode);
        SetSnapPreview(null);
        RequestRepaint();
        return true;
    }

    private bool TryCycleEdgeSnapPreview()
    {
        if (!_lastPointerPdf.HasValue || !CanUseEdgeSnapPreview())
            return false;

        _edgeSnapCycleMode = (_edgeSnapCycleMode + 1) % 3;
        return UpdateEdgeSnapPreview(_lastPointerPdf.Value);
    }

    private bool TryCommitEdgeSnapPreview(SKPoint rawPdf)
    {
        if (!UpdateEdgeSnapPreview(rawPdf) || _edgeSnapPreview == null)
            return false;

        List<SKPoint> points = _edgeSnapPreview.Points.Select(ClonePoint).ToList();
        if (_tool == ViewerTool.Line)
        {
            if (_edgeSnapPreview.ClosedContour && _edgeSnapPreview.Mode == 2 && points.Count >= 3)
                points.Add(points[0]);
            if (points.Count < 2)
                return false;

            _drawPts.Clear();
            _drawPts.AddRange(points);
            ClearEdgeSnapPreview();
            FinalizeDrawing();
            return true;
        }

        if (_tool == ViewerTool.Area)
        {
            if (points.Count < 2)
                return false;

            _drawPts.Clear();
            _drawPts.AddRange(points);
            ClearEdgeSnapPreview();
            if (_edgeSnapPreview.ClosedContour && _edgeSnapPreview.Mode == 2 && points.Count >= 3)
                FinalizeDrawing();
            else
            {
                _rubberEnd = points[^1];
                RequestRepaint();
                PostRecordPrompt();
            }
            return true;
        }

        return false;
    }

    private bool CanUseEdgeSnapPreview() =>
        SnapEnabled &&
        _pageBitmap != null &&
        _drawPts.Count == 0 &&
        !BoxModeEnabled &&
        !_pdfLayerTraceEnabled &&
        !IsMissingScaleForLinearArea() &&
        _tool is ViewerTool.Line or ViewerTool.Area;

    private void ClearEdgeSnapPreview()
    {
        if (_edgeSnapCandidate == null && _edgeSnapPreview == null && _edgeSnapCycleMode == 0)
            return;

        _edgeSnapCandidate = null;
        _edgeSnapPreview = null;
        _edgeSnapCycleMode = 0;
        RequestRepaint();
    }

    private bool TryFindEdgeSnapCandidate(SKPoint rawPdf, out EdgeSnapCandidate? candidate)
    {
        candidate = null;
        float tolerance = ScreenToPdfDistance(SnapToleranceScreenPx);
        SKRect searchRect = SKRect.Create(
            rawPdf.X - tolerance,
            rawPdf.Y - tolerance,
            tolerance * 2f,
            tolerance * 2f);

        float best = tolerance;
        bool found = false;
        foreach (ViewportMeasurementSegmentCandidate segment in ActivePageMeasurementSegmentsNear(searchRect))
        {
            float distance = DistanceToSegment(rawPdf, segment.Start, segment.End);
            if (distance > best ||
                !TryResolveEdgeSnapSegment(segment.Measurement, segment.Start, segment.End, out var resolved) ||
                resolved == null)
            {
                continue;
            }

            best = distance;
            candidate = resolved with { Distance = distance };
            found = true;
        }

        return found;
    }

    private bool TryResolveEdgeSnapSegment(
        Measurement measurement,
        SKPoint start,
        SKPoint end,
        out EdgeSnapCandidate? candidate)
    {
        candidate = null;
        if (measurement.MType is not "line" and not "area")
            return false;

        if (TryResolveEdgeSnapSegment(measurement, measurement.Points, measurement.MType == "area", start, end, out candidate))
            return true;

        if (measurement.MType != "area")
            return false;

        foreach (List<SKPoint> hole in measurement.Holes)
            if (TryResolveEdgeSnapSegment(measurement, hole, closed: true, start, end, out candidate))
                return true;

        return false;
    }

    private bool TryResolveEdgeSnapSegment(
        Measurement measurement,
        IReadOnlyList<SKPoint> contour,
        bool closed,
        SKPoint start,
        SKPoint end,
        out EdgeSnapCandidate? candidate)
    {
        candidate = null;
        int segmentCount = closed ? contour.Count : Math.Max(0, contour.Count - 1);
        if (segmentCount <= 0)
            return false;

        for (int i = 0; i < segmentCount; i++)
        {
            SKPoint a = contour[i];
            SKPoint b = contour[(i + 1) % contour.Count];
            if (!SamePointPair(a, b, start, end))
                continue;

            candidate = new EdgeSnapCandidate(
                measurement,
                contour.ToList(),
                closed,
                i,
                0);
            return true;
        }

        return false;
    }

    private EdgeSnapPreview BuildEdgeSnapPreview(EdgeSnapCandidate candidate, int mode)
    {
        List<SKPoint> points = mode switch
        {
            1 => BuildAdjacentEdgeSnapPoints(candidate),
            2 => candidate.Contour.Select(ClonePoint).ToList(),
            _ => BuildSingleEdgeSnapPoints(candidate),
        };

        string label = mode switch
        {
            1 => "edge+",
            2 => candidate.Closed ? "contour" : "polyline",
            _ => "edge",
        };
        return new EdgeSnapPreview(points, mode, candidate.Closed, label);
    }

    private static List<SKPoint> BuildSingleEdgeSnapPoints(EdgeSnapCandidate candidate)
    {
        int n = candidate.Contour.Count;
        int i = Math.Clamp(candidate.SegmentIndex, 0, Math.Max(0, n - 1));
        return
        [
            ClonePoint(candidate.Contour[i]),
            ClonePoint(candidate.Contour[(i + 1) % n]),
        ];
    }

    private static List<SKPoint> BuildAdjacentEdgeSnapPoints(EdgeSnapCandidate candidate)
    {
        int n = candidate.Contour.Count;
        int i = Math.Clamp(candidate.SegmentIndex, 0, Math.Max(0, n - 1));
        if (candidate.Closed)
        {
            int previous = (i - 1 + n) % n;
            int next = (i + 1) % n;
            int afterNext = (i + 2) % n;
            return
            [
                ClonePoint(candidate.Contour[previous]),
                ClonePoint(candidate.Contour[i]),
                ClonePoint(candidate.Contour[next]),
                ClonePoint(candidate.Contour[afterNext]),
            ];
        }

        int from = Math.Max(0, i - 1);
        int to = Math.Min(n - 1, i + 2);
        var points = new List<SKPoint>();
        for (int index = from; index <= to; index++)
            points.Add(ClonePoint(candidate.Contour[index]));
        return points;
    }

    private void DrawEdgeSnapPreview(SKCanvas canvas)
    {
        if (_edgeSnapPreview == null || _edgeSnapPreview.Points.Count < 2)
            return;

        float stroke = ScreenToPdfDistance(3.2f);
        using var path = new SKPath();
        path.MoveTo(_edgeSnapPreview.Points[0]);
        for (int i = 1; i < _edgeSnapPreview.Points.Count; i++)
            path.LineTo(_edgeSnapPreview.Points[i]);
        if (_edgeSnapPreview.ClosedContour && _edgeSnapPreview.Mode == 2)
            path.Close();

        using var glow = new SKPaint
        {
            Color = new SKColor(0x00, 0x78, 0xD4, 72),
            StrokeWidth = stroke * 3f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        using var line = new SKPaint
        {
            Color = new SKColor(0x00, 0x78, 0xD4),
            StrokeWidth = stroke,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        using var dot = new SKPaint
        {
            Color = new SKColor(0x00, 0x78, 0xD4),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        canvas.DrawPath(path, glow);
        canvas.DrawPath(path, line);
        float radius = ScreenToPdfDistance(3.5f);
        foreach (SKPoint point in _edgeSnapPreview.Points)
            canvas.DrawCircle(point, radius, dot);

        DrawEdgeSnapLabel(canvas, _edgeSnapPreview);
    }

    private void DrawEdgeSnapLabel(SKCanvas canvas, EdgeSnapPreview preview)
    {
        SKPoint anchor = preview.Points[0];
        float textSize = ScreenToPdfDistance(10f);
        using var text = new SKPaint
        {
            Color = new SKColor(0x00, 0x78, 0xD4),
            IsAntialias = true,
            TextSize = textSize,
        };
        canvas.DrawText(preview.Label, anchor.X + ScreenToPdfDistance(8f), anchor.Y - ScreenToPdfDistance(8f), text);
    }

    private static SKPoint ClonePoint(SKPoint point) => new(point.X, point.Y);

    private static bool SamePointPair(SKPoint a, SKPoint b, SKPoint start, SKPoint end) =>
        EdgeSamePoint(a, start) && EdgeSamePoint(b, end) ||
        EdgeSamePoint(a, end) && EdgeSamePoint(b, start);

    private static bool EdgeSamePoint(SKPoint left, SKPoint right) =>
        DistanceSquared(left, right) <= ViewportConstants.GeometryEpsilon;

    private sealed record EdgeSnapCandidate(
        Measurement Measurement,
        IReadOnlyList<SKPoint> Contour,
        bool Closed,
        int SegmentIndex,
        float Distance);

    private sealed record EdgeSnapPreview(
        IReadOnlyList<SKPoint> Points,
        int Mode,
        bool ClosedContour,
        string Label);
}
