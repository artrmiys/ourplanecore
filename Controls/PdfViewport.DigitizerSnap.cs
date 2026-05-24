using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Docnet.Core;
using Docnet.Core.Models;
using OurPlaneCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private SKPoint ResolveDigitizerPoint(SKPoint rawPdf, bool updatePreview)
    {
        if (TryFindDigitizerSnapPoint(rawPdf, out SKPoint snapped, out string snapKind))
        {
            if (updatePreview)
                SetSnapPreview(snapped, snapKind);
            return snapped;
        }

        if (updatePreview)
            SetSnapPreview(null);

        if (ShouldPreviewAsBox())
            return rawPdf;

        return TryGetOrthoAnchor(out SKPoint anchor) && IsOrthoActive()
            ? ApplyOrtho(anchor, rawPdf)
            : rawPdf;
    }

    private bool TryFindDigitizerSnapPoint(SKPoint rawPdf, out SKPoint snapped, out string snapKind)
    {
        float tolerance = ScreenToPdfDistance(SnapToleranceScreenPx);
        float best = tolerance * tolerance;
        SKPoint bestPoint = default;
        string bestKind = "";
        snapped = default;
        snapKind = "";
        bool found = false;

        void Consider(SKPoint candidate, string kind)
        {
            float distance = DistanceSquared(rawPdf, candidate);
            if (distance >= best)
                return;

            best = distance;
            bestPoint = candidate;
            bestKind = kind;
            found = true;
        }

        if (SnapEnabled && TryFindSnapPoint(rawPdf, out SKPoint measurementSnap, out string measurementKind))
            Consider(measurementSnap, measurementKind);

        if (PdfSnapEnabled && TryFindPdfSnapPoint(rawPdf, tolerance, out SKPoint pdfSnap, out string pdfKind))
            Consider(pdfSnap, pdfKind);

        snapped = bestPoint;
        snapKind = bestKind;
        return found;
    }

    private bool IsOrthoActive()
    {
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        return OrthoEnabled ^ shift;
    }

    private static bool IsSelectionModifierActive() =>
        (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None;

    // Shift (without Ctrl) means "remove from selection" while a selection
    // modifier is active. Ctrl means "add to selection".
    private static bool IsDeselectModifierActive() =>
        (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift &&
        (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.None;

    // Alt switches the box/click into vertex (handle) mode on the selected
    // Line/Area object(s). Alt alone toggles handle selection.
    private static bool IsVertexModifierActive() =>
        (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

    private static bool IsAddModifierActive() =>
        (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

    private static bool IsRemoveModifierActive() =>
        (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift &&
        (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.None;

    private bool ShouldPreviewAsBox() =>
        _tool == ViewerTool.DrawRect ||
        _tool == ViewerTool.Openings ||
        _tool == ViewerTool.DrawCloud ||
        _tool == ViewerTool.AreaCut && BoxModeEnabled ||
        BoxModeEnabled && _tool is (ViewerTool.Line or ViewerTool.Area);

    private bool TryGetOrthoAnchor(out SKPoint anchor)
    {
        if (_joistDirectionMeasurement != null && _joistDirectionPts.Count > 0)
        {
            anchor = _joistDirectionPts[^1];
            return true;
        }

        if (_tool is ViewerTool.Line or ViewerTool.Area or ViewerTool.Ruler or ViewerTool.Beam or ViewerTool.Openings or ViewerTool.DrawLine or ViewerTool.DrawArrow or ViewerTool.DrawRect or ViewerTool.DrawCloud or ViewerTool.DrawArea &&
            _drawPts.Count > 0)
        {
            anchor = _drawPts[^1];
            return true;
        }

        if (_tool == ViewerTool.Scale && _scalePts.Count > 0)
        {
            anchor = _scalePts[^1];
            return true;
        }

        anchor = default;
        return false;
    }

    private static SKPoint ApplyOrtho(SKPoint anchor, SKPoint point)
    {
        float dx = point.X - anchor.X;
        float dy = point.Y - anchor.Y;
        float length = MeasurementGeometry.Distance(point, anchor);
        if (length <= ViewportConstants.ZeroLengthEpsilon)
            return point;

        float angle = MathF.Atan2(dy, dx);
        float step = MathF.PI / 4f;
        float snappedAngle = MathF.Round(angle / step) * step;
        return new SKPoint(
            anchor.X + MathF.Cos(snappedAngle) * length,
            anchor.Y + MathF.Sin(snappedAngle) * length);
    }

    private bool TryFindSnapPoint(SKPoint rawPdf, out SKPoint snapped, out string snapKind)
    {
        Stopwatch snapWatch = Stopwatch.StartNew();
        float tolerance = ScreenToPdfDistance(SnapToleranceScreenPx);
        float best = tolerance * tolerance;
        SKPoint bestPoint = default;
        string bestKind = "";
        bool found = false;
        var segments = new List<SnapSegment>();
        int consideredSnapPoints = 0;
        int candidateMeasurements = 0;
        int skippedMeasurements = 0;
        int candidateAnnotations = 0;

        SKRect searchRect = SKRect.Create(
            rawPdf.X - tolerance,
            rawPdf.Y - tolerance,
            tolerance * 2,
            tolerance * 2);

        void Consider(SKPoint candidate, string kind)
        {
            if (!RectContains(searchRect, candidate))
                return;

            consideredSnapPoints++;
            float distance = DistanceSquared(rawPdf, candidate);
            if (distance >= best)
                return;

            best = distance;
            bestPoint = candidate;
            bestKind = kind;
            found = true;
        }
        IReadOnlyList<Measurement> activeMeasurements = ActivePageMeasurements();
        int activeMeasurementCount = activeMeasurements.Count;
        IReadOnlyList<ViewportMeasurementVertexCandidate> measurementVertices =
            ActivePageMeasurementVerticesNear(searchRect);
        IReadOnlyList<ViewportMeasurementSegmentCandidate> measurementSegments =
            ActivePageMeasurementSegmentsNear(searchRect);
        var candidateMeasurementSet = new HashSet<Measurement>();

        void ConsiderPolyline(
            IReadOnlyList<SKPoint> points,
            bool closed,
            bool includeEndpoints,
            bool boundsAlreadyChecked = false)
        {
            if (points.Count == 0)
                return;

            if (!boundsAlreadyChecked && !RectsIntersect(PointsBounds(points), searchRect))
                return;

            if (includeEndpoints)
            {
                foreach (SKPoint point in points)
                    Consider(point, "endpoint");
            }

            for (int i = 1; i < points.Count; i++)
            {
                SKPoint start = points[i - 1];
                SKPoint end = points[i];
                if (!SegmentBoundsIntersectRect(start, end, searchRect) ||
                    !SegmentIntersectsRect(start, end, searchRect))
                    continue;

                Consider(Midpoint(start, end), "midpoint");
                segments.Add(new SnapSegment(start, end));
            }

            if (closed && points.Count > 2)
            {
                SKPoint start = points[^1];
                SKPoint end = points[0];
                if (SegmentBoundsIntersectRect(start, end, searchRect) &&
                    SegmentIntersectsRect(start, end, searchRect))
                {
                    Consider(Midpoint(start, end), "midpoint");
                    segments.Add(new SnapSegment(start, end));
                }
            }
        }

        for (int i = 0; i < _drawPts.Count; i++)
        {
            if (i == _drawPts.Count - 1 && _tool is ViewerTool.Line or ViewerTool.Area or ViewerTool.AreaCut)
                continue;

            Consider(_drawPts[i], "endpoint");
        }
        ConsiderPolyline(_drawPts, _tool == ViewerTool.Area || _tool == ViewerTool.AreaCut && !BoxModeEnabled, includeEndpoints: false);

        foreach (SKPoint point in _scalePts)
            Consider(point, "endpoint");

        foreach (ViewportMeasurementVertexCandidate vertex in measurementVertices)
        {
            candidateMeasurementSet.Add(vertex.Measurement);
            Consider(vertex.Point, "endpoint");
        }

        foreach (ViewportMeasurementSegmentCandidate segment in measurementSegments)
        {
            if (!SegmentIntersectsRect(segment.Start, segment.End, searchRect))
                continue;

            candidateMeasurementSet.Add(segment.Measurement);
            Consider(Midpoint(segment.Start, segment.End), "midpoint");
            segments.Add(new SnapSegment(segment.Start, segment.End));
        }

        candidateMeasurements = candidateMeasurementSet.Count;
        skippedMeasurements = Math.Max(0, activeMeasurementCount - candidateMeasurements);

        foreach (PageAnnotation annotation in _annotations)
        {
            if (!IsAnnotationVisibleOnActivePage(annotation) || annotation.Points.Count < 2)
                continue;

            IReadOnlyList<SKPoint> points = AnnotationSnapPoints(annotation);
            if (!RectsIntersect(PointsBounds(points), searchRect))
                continue;

            candidateAnnotations++;
            ConsiderPolyline(points, closed: false, includeEndpoints: true);
        }

        for (int i = 0; i < segments.Count; i++)
        {
            for (int j = i + 1; j < segments.Count; j++)
            {
                SnapSegment a = segments[i];
                SnapSegment b = segments[j];
                if (SharesEndpoint(a, b))
                    continue;

                if (TrySegmentIntersectionPoint(a.Start, a.End, b.Start, b.End, out SKPoint intersection))
                    Consider(intersection, "intersection");
            }
        }

        snapped = bestPoint;
        snapKind = bestKind;
        ReportSlowSnapSearch(
            snapWatch.ElapsedMilliseconds,
            activeMeasurementCount,
            candidateMeasurements,
            skippedMeasurements,
            candidateAnnotations,
            consideredSnapPoints,
            segments.Count,
            found);
        return found;
    }

    private void ReportSlowSnapSearch(
        long elapsedMs,
        int activeMeasurementCount,
        int candidateMeasurements,
        int skippedMeasurements,
        int candidateAnnotations,
        int consideredSnapPoints,
        int segmentCandidateCount,
        bool found)
    {
        if (elapsedMs < ViewportRenderPolicy.SlowSnapLogMs)
            return;

        DateTime now = DateTime.UtcNow;
        if ((now - _lastSlowSnapLogAt).TotalSeconds < 2)
            return;

        _lastSlowSnapLogAt = now;
        AppLog.Info(
            $"Viewport slow snap {elapsedMs}ms; tool={_tool}; zoom={_zoom:0.###}; page='{_pageFolder}'; " +
            $"activeMeasurements={activeMeasurementCount}; candidateMeasurements={candidateMeasurements}; " +
            $"skippedMeasurements={skippedMeasurements}; candidateAnnotations={candidateAnnotations}; " +
            $"snapPoints={consideredSnapPoints}; segments={segmentCandidateCount}; found={found}; pdfSnap={PdfSnapEnabled}");
    }

    private void SetSnapPreview(SKPoint? point, string kind = "")
    {
        bool changed = (_snapPreview.HasValue != point.HasValue) ||
                       (_snapPreview.HasValue && point.HasValue &&
                        DistanceSquared(_snapPreview.Value, point.Value) > 0.001f) ||
                       !string.Equals(_snapPreviewKind, kind, StringComparison.OrdinalIgnoreCase);
        _snapPreview = point;
        _snapPreviewKind = point.HasValue ? kind : "";
        if (changed)
            RequestRepaint();
    }

    private static SKPoint Midpoint(SKPoint a, SKPoint b) =>
        new((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);

    private static bool SegmentBoundsIntersectRect(SKPoint start, SKPoint end, SKRect rect) =>
        Math.Min(start.X, end.X) <= rect.Right &&
        Math.Max(start.X, end.X) >= rect.Left &&
        Math.Min(start.Y, end.Y) <= rect.Bottom &&
        Math.Max(start.Y, end.Y) >= rect.Top;

    private static IReadOnlyList<SKPoint> AnnotationSnapPoints(PageAnnotation annotation)
    {
        if (annotation.Points.Count < 2)
            return annotation.Points;

        string kind = OurPlaneCoreJobStore.NormalizePageAnnotationKind(annotation.Kind);
        if (kind == "area")
            return annotation.Points.Append(annotation.Points[0]).ToList();

        if (kind != "rectangle" && kind != "note" && kind != "cloud")
            return annotation.Points.Take(2).ToList();

        IReadOnlyList<SKPoint> corners = AnnotationRectangleCorners(annotation.Points);
        return corners
            .Append(corners[0])
            .ToList();
    }

    private static bool SharesEndpoint(SnapSegment left, SnapSegment right) =>
        DistanceSquared(left.Start, right.Start) <= ViewportConstants.GeometryEpsilon ||
        DistanceSquared(left.Start, right.End) <= ViewportConstants.GeometryEpsilon ||
        DistanceSquared(left.End, right.Start) <= ViewportConstants.GeometryEpsilon ||
        DistanceSquared(left.End, right.End) <= ViewportConstants.GeometryEpsilon;

    private sealed record SnapSegment(SKPoint Start, SKPoint End);
}
