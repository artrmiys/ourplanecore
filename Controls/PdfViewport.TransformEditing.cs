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
        return TransformSelectedPoints(point => new SKPoint(2 * center.X - point.X, point.Y), "Mirrored selection horizontal.");
    }

    public bool MirrorSelectedVertical()
    {
        SKPoint center = CurrentSelectionCenter();
        return TransformSelectedPoints(point => new SKPoint(point.X, 2 * center.Y - point.Y), "Mirrored selection vertical.");
    }

    public bool RotateSelectedBy(double degrees)
    {
        if (Math.Abs(degrees) < 0.001)
            return HasSelectedTransformTargets();

        SKPoint center = CurrentSelectionCenter();
        float radians = (float)(degrees * Math.PI / 180.0);
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return TransformSelectedPoints(point => RotatePoint(point, center, cos, sin), $"Rotated selection {degrees:0.#} deg.");
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
            $"Scaled selection {factor:0.##}x.");
    }

    private bool TransformSelectedPoints(Func<SKPoint, SKPoint> transform, string status)
    {
        var measurements = SelectedTransformMeasurements();
        var annotations = SelectedTransformAnnotations();
        if (measurements.Count == 0 && annotations.Count == 0)
        {
            PostStatus("Select measurements or markups before editing transform.");
            return false;
        }

        foreach (PageAnnotation annotation in annotations)
            EnsureAnnotationTransformPoints(annotation);

        foreach (Measurement measurement in measurements)
            ApplyTransform(measurement.Points, transform);
        foreach (PageAnnotation annotation in annotations)
            ApplyTransform(annotation.Points, transform);

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
        _transformStartDistance = Math.Max(0.001f, Distance(pdf, _transformCenter));
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
        _transformAnnotationOriginalPoints.Clear();
        foreach (Measurement measurement in measurements)
            _transformMeasurementOriginalPoints[measurement] = measurement.Points.ToList();
        foreach (PageAnnotation annotation in annotations)
        {
            EnsureAnnotationTransformPoints(annotation);
            _transformAnnotationOriginalPoints[annotation] = annotation.Points.ToList();
        }

        return true;
    }

    private void UpdateTransformDrag(SKPoint pdf)
    {
        if (!_draggingTransformScale && !_draggingTransformRotate)
            return;

        if (_draggingTransformScale)
        {
            float distance = Distance(pdf, _transformCenter);
            float factor = Math.Clamp(distance / Math.Max(_transformStartDistance, 0.001f), 0.05f, 20f);
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
        }

        foreach (var (annotation, originalPoints) in _transformAnnotationOriginalPoints)
        {
            for (int i = 0; i < annotation.Points.Count && i < originalPoints.Count; i++)
                annotation.Points[i] = transform(originalPoints[i]);
        }
    }

    private void FinishTransformDrag()
    {
        if (!_draggingTransformScale && !_draggingTransformRotate)
            return;

        var changedMeasurements = _transformMeasurementOriginalPoints.Keys.ToList();
        var changedAnnotations = _transformAnnotationOriginalPoints.Keys.ToList();
        _draggingTransformScale = false;
        _draggingTransformRotate = false;
        _transformMeasurementOriginalPoints.Clear();
        _transformAnnotationOriginalPoints.Clear();
        if (IsMouseCaptured)
            ReleaseMouseCapture();

        foreach (Measurement measurement in changedMeasurements)
            MeasurementChanged?.Invoke(measurement);
        foreach (PageAnnotation annotation in changedAnnotations)
            PageAnnotationChanged?.Invoke(annotation);
        RequestRepaint();
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
        if (bounds.Width < 0.001f)
            bounds.Inflate(8f / Math.Max(_zoom, 0.001f), 0);
        if (bounds.Height < 0.001f)
            bounds.Inflate(0, 8f / Math.Max(_zoom, 0.001f));
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
        float inflate = 18f / Math.Max(_zoom, 0.001f);
        bounds.Inflate(inflate, inflate);
        return bounds;
    }

    private IEnumerable<(TransformHandleKind Kind, SKPoint Point)> TransformHandlePoints(SKRect bounds)
    {
        yield return (TransformHandleKind.ScaleTopLeft, new SKPoint(bounds.Left, bounds.Top));
        yield return (TransformHandleKind.ScaleTopRight, new SKPoint(bounds.Right, bounds.Top));
        yield return (TransformHandleKind.ScaleBottomRight, new SKPoint(bounds.Right, bounds.Bottom));
        yield return (TransformHandleKind.ScaleBottomLeft, new SKPoint(bounds.Left, bounds.Bottom));
        yield return (TransformHandleKind.Rotate, new SKPoint((bounds.Left + bounds.Right) / 2f, bounds.Top - 22f / Math.Max(_zoom, 0.001f)));
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
            foreach (SKPoint point in measurement.Points)
                yield return point;

        foreach (PageAnnotation annotation in SelectedTransformAnnotations())
            foreach (SKPoint point in AnnotationTransformPoints(annotation))
                yield return point;
    }

    private bool HasSelectedTransformTargets() =>
        SelectedTransformMeasurements().Count > 0 || SelectedTransformAnnotations().Count > 0;

    private void EnsureAnnotationTransformPoints(PageAnnotation annotation)
    {
        if (!IsRectangleAnnotation(annotation) || annotation.Points.Count != 2)
            return;

        IReadOnlyList<SKPoint> corners = RectangleCorners(annotation.Points);
        annotation.Points.Clear();
        annotation.Points.AddRange(corners);
    }

    private static IReadOnlyList<SKPoint> AnnotationTransformPoints(PageAnnotation annotation)
    {
        if (!IsRectangleAnnotation(annotation) || annotation.Points.Count != 2)
            return annotation.Points;

        return RectangleCorners(annotation.Points);
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
        MathF.Sqrt(DistanceSquared(a, b));

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
