using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using OurPlaneCore;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    public bool HasTransformSelection => HasSelectedTransformTargets();

    public bool MirrorSelectedHorizontal()
    {
        SKPoint center = CurrentSelectionCenter();
        return TransformSelectedPoints(
            point => new SKPoint(2 * center.X - point.X, point.Y),
            "Mirrored selection horizontal.",
            "mirror-horizontal",
            coalesceUndo: false);
    }

    public bool MirrorSelectedVertical()
    {
        SKPoint center = CurrentSelectionCenter();
        return TransformSelectedPoints(
            point => new SKPoint(point.X, 2 * center.Y - point.Y),
            "Mirrored selection vertical.",
            "mirror-vertical",
            coalesceUndo: false);
    }

    public bool RotateSelectedBy(double degrees)
    {
        if (Math.Abs(degrees) < 0.001)
            return HasSelectedTransformTargets();

        SKPoint center = CurrentSelectionCenter();
        float radians = (float)(degrees * Math.PI / 180.0);
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return TransformSelectedPoints(
            point => RotatePoint(point, center, cos, sin),
            $"Rotated selection {degrees:0.#} deg.",
            "rotate-slider",
            coalesceUndo: true);
    }

    public bool ScaleSelectedBy(double factor)
    {
        if (factor <= 0 || Math.Abs(factor - 1) < 0.0001)
            return HasSelectedTransformTargets();

        factor = Math.Clamp(factor, 0.05, 20.0);
        SKPoint center = CurrentSelectionCenter();
        return TransformSelectedPoints(
            point => new SKPoint(
                center.X + (float)((point.X - center.X) * factor),
                center.Y + (float)((point.Y - center.Y) * factor)),
            $"Scaled selection {factor:0.##}x.",
            "scale-slider",
            coalesceUndo: true);
    }

    private bool TransformSelectedPoints(
        Func<SKPoint, SKPoint> transform,
        string status,
        string undoKey,
        bool coalesceUndo)
    {
        var measurements = SelectedTransformMeasurements();
        var annotations = SelectedTransformAnnotations();
        if (measurements.Count == 0 && annotations.Count == 0)
        {
            PostStatus("Select measurements or markups before editing transform.");
            return false;
        }

        PushGeometryUndoSnapshot(measurements, annotations, "selection transform", undoKey, coalesceUndo);
        foreach (Measurement measurement in measurements)
        {
            ApplyTransform(measurement.Points, transform);
            ApplyTransformToHoles(measurement.Holes, transform);
        }
        foreach (PageAnnotation annotation in annotations)
            ApplyAnnotationTransform(annotation, transform);

        RequestRepaint();
        foreach (Measurement measurement in measurements)
            MeasurementChanged?.Invoke(measurement);
        foreach (PageAnnotation annotation in annotations)
            PageAnnotationChanged?.Invoke(annotation);
        PostStatus(status);
        return true;
    }

    private static void ApplyTransform(List<SKPoint> points, Func<SKPoint, SKPoint> transform)
    {
        for (int i = 0; i < points.Count; i++)
            points[i] = transform(points[i]);
    }

    private static void ApplyTransformToHoles(List<List<SKPoint>> holes, Func<SKPoint, SKPoint> transform)
    {
        foreach (var hole in holes)
            ApplyTransform(hole, transform);
    }

    private static void RestoreTransformedHoles(
        List<List<SKPoint>> target,
        IReadOnlyList<IReadOnlyList<SKPoint>> originals,
        Func<SKPoint, SKPoint> transform)
    {
        target.Clear();
        foreach (var originalHole in originals)
            target.Add(originalHole.Select(transform).ToList());
    }

    private static SKPoint RotatePoint(SKPoint point, SKPoint center, float cos, float sin)
    {
        float dx = point.X - center.X;
        float dy = point.Y - center.Y;
        return new SKPoint(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }

    private bool TryBeginTransformHandleEdit(SKPoint pdf, Point screen)
    {
        if (!TryHitTransformHandle(pdf, out TransformHandleKind handle))
            return false;

        if (!CaptureTransformOriginals())
            return false;

        _draggingTransformScale = handle != TransformHandleKind.Rotate;
        _draggingTransformRotate = handle == TransformHandleKind.Rotate;
        _dragScreenStart = screen;
        _transformCenter = CurrentSelectionCenter();
        _transformStartDistance = Math.Max(ViewportConstants.ZeroLengthEpsilon, Distance(pdf, _transformCenter));
        _transformStartAngle = AngleFromCenter(_transformCenter, pdf);
        CaptureMouse();
        PostStatus(_draggingTransformRotate
            ? "Rotate selection: drag orange handle."
            : "Scale selection: drag orange corner handle.");
        return true;
    }

    private bool CaptureTransformOriginals()
    {
        var measurements = SelectedTransformMeasurements();
        var annotations = SelectedTransformAnnotations();
        if (measurements.Count == 0 && annotations.Count == 0)
            return false;

        _transformMeasurementOriginalPoints.Clear();
        _transformMeasurementOriginalHoles.Clear();
        _transformAnnotationOriginalPoints.Clear();
        foreach (Measurement measurement in measurements)
        {
            _transformMeasurementOriginalPoints[measurement] = measurement.Points.ToList();
            _transformMeasurementOriginalHoles[measurement] = CloneHoles(measurement.Holes);
        }
        foreach (PageAnnotation annotation in annotations)
            _transformAnnotationOriginalPoints[annotation] = annotation.Points.ToList();

        return true;
    }

    private void UpdateTransformDrag(SKPoint pdf)
    {
        if (!_draggingTransformScale && !_draggingTransformRotate)
            return;

        if (_draggingTransformScale)
        {
            float distance = Distance(pdf, _transformCenter);
            float factor = Math.Clamp(distance / Math.Max(_transformStartDistance, ViewportConstants.ZeroLengthEpsilon), 0.05f, 20f);
            ApplyTransformFromOriginal(point => new SKPoint(
                _transformCenter.X + (point.X - _transformCenter.X) * factor,
                _transformCenter.Y + (point.Y - _transformCenter.Y) * factor));
            PostStatus($"Scaling selection: {factor:0.##}x.");
        }
        else
        {
            float angle = AngleFromCenter(_transformCenter, pdf);
            float delta = angle - _transformStartAngle;
            float cos = MathF.Cos(delta);
            float sin = MathF.Sin(delta);
            ApplyTransformFromOriginal(point => RotatePoint(point, _transformCenter, cos, sin));
            PostStatus($"Rotating selection: {delta * 180f / MathF.PI:0.#} deg.");
        }

        RequestRepaint();
    }

    private void ApplyTransformFromOriginal(Func<SKPoint, SKPoint> transform)
    {
        foreach (var (measurement, originalPoints) in _transformMeasurementOriginalPoints)
        {
            for (int i = 0; i < measurement.Points.Count && i < originalPoints.Count; i++)
                measurement.Points[i] = transform(originalPoints[i]);

            if (_transformMeasurementOriginalHoles.TryGetValue(measurement, out var originalHoles))
                RestoreTransformedHoles(measurement.Holes, originalHoles, transform);
        }

        foreach (var (annotation, originalPoints) in _transformAnnotationOriginalPoints)
            RestoreAnnotationTransform(annotation, originalPoints, transform);
    }

    private void FinishTransformDrag()
    {
        if (!_draggingTransformScale && !_draggingTransformRotate)
            return;

        var changedMeasurements = _transformMeasurementOriginalPoints.Keys.ToList();
        var changedAnnotations = _transformAnnotationOriginalPoints.Keys.ToList();
        bool changed = TransformOriginalsChanged();
        if (changed)
            PushGeometryUndoSnapshotFromOriginals(
                _transformMeasurementOriginalPoints,
                _transformMeasurementOriginalHoles,
                _transformAnnotationOriginalPoints,
                "canvas transform",
                "canvas-transform");

        _draggingTransformScale = false;
        _draggingTransformRotate = false;
        _transformMeasurementOriginalPoints.Clear();
        _transformMeasurementOriginalHoles.Clear();
        _transformAnnotationOriginalPoints.Clear();
        if (IsMouseCaptured)
            ReleaseMouseCapture();

        if (changed)
        {
            foreach (Measurement measurement in changedMeasurements)
                MeasurementChanged?.Invoke(measurement);
            foreach (PageAnnotation annotation in changedAnnotations)
                PageAnnotationChanged?.Invoke(annotation);
        }

        RequestRepaint();
    }

    private bool TransformOriginalsChanged()
    {
        foreach (var (measurement, originalPoints) in _transformMeasurementOriginalPoints)
            if (!SamePoints(measurement.Points, originalPoints) ||
                _transformMeasurementOriginalHoles.TryGetValue(measurement, out var holes) &&
                !SameHoles(measurement.Holes, holes))
            {
                return true;
            }

        foreach (var (annotation, originalPoints) in _transformAnnotationOriginalPoints)
            if (!SamePoints(annotation.Points, originalPoints))
                return true;

        return false;
    }

    private static bool SamePoints(IReadOnlyList<SKPoint> left, IReadOnlyList<SKPoint> right)
    {
        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++)
        {
            if (Math.Abs(left[i].X - right[i].X) > ViewportConstants.ZeroLengthEpsilon ||
                Math.Abs(left[i].Y - right[i].Y) > ViewportConstants.ZeroLengthEpsilon)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameHoles(IReadOnlyList<IReadOnlyList<SKPoint>> left, IReadOnlyList<IReadOnlyList<SKPoint>> right)
    {
        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++)
            if (!SamePoints(left[i], right[i]))
                return false;

        return true;
    }
    private bool TryHitTransformHandle(SKPoint pdf, out TransformHandleKind handle)
    {
        handle = TransformHandleKind.None;
        if (!TryGetTransformBounds(out SKRect bounds))
            return false;

        SKRect outer = TransformHandleBounds(bounds);
        float tol = 12f / Math.Max(_zoom, 0.01f);
        foreach ((TransformHandleKind candidate, SKPoint point) in TransformHandlePoints(outer))
        {
            if (Distance(pdf, point) <= tol)
            {
                handle = candidate;
                return true;
            }
        }

        return false;
    }

    private bool TryGetTransformBounds(out SKRect bounds)
    {
        var points = SelectedTransformPoints().ToList();
        if (points.Count == 0)
        {
            bounds = SKRect.Empty;
            return false;
        }

        bounds = PointsBounds(points);
        if (bounds.Width < ViewportConstants.ZeroLengthEpsilon)
            bounds.Inflate(ScreenToPdfDistance(8f), 0);
        if (bounds.Height < ViewportConstants.ZeroLengthEpsilon)
            bounds.Inflate(0, ScreenToPdfDistance(8f));
        return true;
    }

    private SKPoint CurrentSelectionCenter()
    {
        if (!TryGetTransformBounds(out SKRect bounds))
            return default;

        return new SKPoint((bounds.Left + bounds.Right) / 2f, (bounds.Top + bounds.Bottom) / 2f);
    }

    private SKRect TransformHandleBounds(SKRect bounds)
    {
        float inflate = ScreenToPdfDistance(18f);
        bounds.Inflate(inflate, inflate);
        return bounds;
    }

    private IEnumerable<(TransformHandleKind Kind, SKPoint Point)> TransformHandlePoints(SKRect bounds)
    {
        yield return (TransformHandleKind.ScaleTopLeft, new SKPoint(bounds.Left, bounds.Top));
        yield return (TransformHandleKind.ScaleTopRight, new SKPoint(bounds.Right, bounds.Top));
        yield return (TransformHandleKind.ScaleBottomRight, new SKPoint(bounds.Right, bounds.Bottom));
        yield return (TransformHandleKind.ScaleBottomLeft, new SKPoint(bounds.Left, bounds.Bottom));
        yield return (TransformHandleKind.Rotate, new SKPoint((bounds.Left + bounds.Right) / 2f, bounds.Top - ScreenToPdfDistance(22f)));
    }

    private IReadOnlyList<Measurement> SelectedTransformMeasurements() =>
        GetSelectedMeasurements()
            .Where(measurement => IsMeasurementOnActivePage(measurement))
            .ToList();

    private IReadOnlyList<PageAnnotation> SelectedTransformAnnotations() =>
        _selectedAnnotation != null && IsAnnotationOnActivePage(_selectedAnnotation)
            ? [_selectedAnnotation]
            : [];

    private IEnumerable<SKPoint> SelectedTransformPoints()
    {
        foreach (Measurement measurement in SelectedTransformMeasurements())
        {
            foreach (SKPoint point in measurement.Points)
                yield return point;
            foreach (var hole in measurement.Holes)
                foreach (SKPoint point in hole)
                    yield return point;
        }

        foreach (PageAnnotation annotation in SelectedTransformAnnotations())
            foreach (SKPoint point in AnnotationTransformPoints(annotation))
                yield return point;
    }

    private bool HasSelectedTransformTargets() =>
        SelectedTransformMeasurements().Count > 0 || SelectedTransformAnnotations().Count > 0;

    private static IReadOnlyList<SKPoint> AnnotationTransformPoints(PageAnnotation annotation)
    {
        if (!IsRectangleAnnotation(annotation) || annotation.Points.Count != 2)
            return annotation.Points;

        return RectangleCorners(annotation.Points);
    }

    private static void ApplyAnnotationTransform(PageAnnotation annotation, Func<SKPoint, SKPoint> transform)
    {
        if (IsRectangleAnnotation(annotation) && annotation.Points.Count == 2)
        {
            StoreRectangleFromTransformedCorners(annotation.Points, RectangleCorners(annotation.Points).Select(transform));
            return;
        }

        ApplyTransform(annotation.Points, transform);
    }

    private static void RestoreAnnotationTransform(
        PageAnnotation annotation,
        IReadOnlyList<SKPoint> originalPoints,
        Func<SKPoint, SKPoint> transform)
    {
        if (IsRectangleAnnotation(annotation) && originalPoints.Count == 2)
        {
            StoreRectangleFromTransformedCorners(annotation.Points, RectangleCorners(originalPoints).Select(transform));
            return;
        }

        for (int i = 0; i < annotation.Points.Count && i < originalPoints.Count; i++)
            annotation.Points[i] = transform(originalPoints[i]);
    }

    private static void StoreRectangleFromTransformedCorners(
        List<SKPoint> target,
        IEnumerable<SKPoint> transformedCorners)
    {
        var corners = transformedCorners.ToList();
        if (corners.Count == 0)
            return;

        SKRect bounds = PointsBounds(corners);
        target.Clear();
        target.Add(new SKPoint(bounds.Left, bounds.Top));
        target.Add(new SKPoint(bounds.Right, bounds.Bottom));
    }

    private static bool IsRectangleAnnotation(PageAnnotation annotation) =>
        OurPlaneCoreJobStore.NormalizePageAnnotationKind(annotation.Kind) == "rectangle";

    private static IReadOnlyList<SKPoint> RectangleCorners(IReadOnlyList<SKPoint> points)
    {
        if (points.Count < 2)
            return points;

        SKRect rect = NormalizeRect(points[0], points[1]);
        return
        [
            new SKPoint(rect.Left, rect.Top),
            new SKPoint(rect.Right, rect.Top),
            new SKPoint(rect.Right, rect.Bottom),
            new SKPoint(rect.Left, rect.Bottom),
        ];
    }

    private static float Distance(SKPoint a, SKPoint b) =>
        MeasurementGeometry.Distance(a, b);

    private static float AngleFromCenter(SKPoint center, SKPoint point) =>
        MathF.Atan2(point.Y - center.Y, point.X - center.X);

    private void PublishTransformSelectionChanged() =>
        TransformSelectionChanged?.Invoke(HasTransformSelection);

    private enum TransformHandleKind
    {
        None,
        ScaleTopLeft,
        ScaleTopRight,
        ScaleBottomRight,
        ScaleBottomLeft,
        Rotate,
    }

}
