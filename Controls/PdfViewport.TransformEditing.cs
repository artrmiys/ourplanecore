using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using OurPlanCore;
using SkiaSharp;

namespace OurPlanCore.Controls;

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
            coalesceUndo: true,
            rotationDegrees: degrees);
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
        bool coalesceUndo,
        double? rotationDegrees = null)
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
            ApplyJoistDirectionRotation(measurement, rotationDegrees);
        }
        foreach (PageAnnotation annotation in annotations)
            ApplyAnnotationTransform(annotation, transform);

        RequestRepaint();
        NotifyMeasurementsChanged(measurements);
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
        _transformHandle = handle;
        _dragScreenStart = screen;
        _transformCenter = CurrentSelectionCenter();
        _transformStartDistance = Math.Max(ViewportConstants.ZeroLengthEpsilon, Distance(pdf, _transformCenter));
        _transformStartAngle = AngleFromCenter(_transformCenter, pdf);
        _transformStartBounds = TryGetTransformBounds(out SKRect startBounds) ? startBounds : SKRect.Empty;
        _transformStartPdf = pdf;
        CaptureMouse();
        PostStatus(_draggingTransformRotate
            ? "Rotate selection: drag orange handle."
            : IsEdgeHandle(handle)
                ? "Resize selection: drag the orange edge handle (hold Shift = keep proportions)."
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
        _transformMeasurementOriginalJoistDirections.Clear();
        _transformAnnotationOriginalPoints.Clear();
        foreach (Measurement measurement in measurements)
        {
            _transformMeasurementOriginalPoints[measurement] = measurement.Points.ToList();
            _transformMeasurementOriginalHoles[measurement] = CloneHoles(measurement.Holes);
            _transformMeasurementOriginalJoistDirections[measurement] = measurement.JoistDirectionDegrees;
        }
        foreach (PageAnnotation annotation in annotations)
            _transformAnnotationOriginalPoints[annotation] = annotation.Points.ToList();

        return true;
    }

    private void UpdateTransformDrag(SKPoint pdf)
    {
        if (!_draggingTransformScale && !_draggingTransformRotate)
            return;

        if (_draggingTransformScale && ShouldScaleFromTopLeftAnchor(_transformHandle))
        {
            UpdateTopLeftAnchoredScaleDrag(pdf);
        }
        else if (_draggingTransformScale && IsEdgeHandle(_transformHandle))
        {
            UpdateEdgeResizeDrag(pdf);
        }
        else if (_draggingTransformScale)
        {
            UpdateCenteredScaleDrag(pdf);
        }
        else
        {
            float angle = AngleFromCenter(_transformCenter, pdf);
            double deltaDegrees = (angle - _transformStartAngle) * 180.0 / Math.PI;
            bool snapped = IsTransformRotationSnapActive();
            if (snapped)
                deltaDegrees = TransformEditConstraints.SnapRotationDegrees(deltaDegrees);

            float delta = (float)(deltaDegrees * Math.PI / 180.0);
            float cos = MathF.Cos(delta);
            float sin = MathF.Sin(delta);
            ApplyTransformFromOriginal(
                point => RotatePoint(point, _transformCenter, cos, sin),
                rotationDegrees: deltaDegrees);
            PostStatus(snapped
                ? $"Rotating selection: {deltaDegrees:0.#} deg. (15 deg snap)"
                : $"Rotating selection: {deltaDegrees:0.#} deg.");
        }

        RequestRepaint();
    }

    private void ApplyTransformFromOriginal(Func<SKPoint, SKPoint> transform, double? rotationDegrees = null)
    {
        foreach (var (measurement, originalPoints) in _transformMeasurementOriginalPoints)
        {
            for (int i = 0; i < measurement.Points.Count && i < originalPoints.Count; i++)
                measurement.Points[i] = transform(originalPoints[i]);

            if (_transformMeasurementOriginalHoles.TryGetValue(measurement, out var originalHoles))
                RestoreTransformedHoles(measurement.Holes, originalHoles, transform);
            ApplyJoistDirectionRotationFromOriginal(measurement, rotationDegrees);
        }

        foreach (var (annotation, originalPoints) in _transformAnnotationOriginalPoints)
            RestoreAnnotationTransform(annotation, originalPoints, transform);
    }

    // Drag one edge of the orange frame to resize the selection along that
    // axis. The opposite edge stays anchored. Holding Shift (ortho) keeps the
    // selection proportional by scaling the other axis by the same factor.
    private void UpdateEdgeResizeDrag(SKPoint pdf)
    {
        SKRect b = _transformStartBounds;
        float eps = Math.Max(ViewportConstants.ZeroLengthEpsilon, 0.0001f);
        float cx = (b.Left + b.Right) / 2f;
        float cy = (b.Top + b.Bottom) / 2f;
        bool proportional = IsOrthoActive();
        bool horizontal = _transformHandle is TransformHandleKind.ScaleLeft or TransformHandleKind.ScaleRight;

        float factor;
        if (horizontal)
        {
            float anchorX = _transformHandle == TransformHandleKind.ScaleLeft ? b.Right : b.Left;
            float origReach = _transformStartPdf.X - anchorX;
            factor = Math.Abs(origReach) < eps
                ? 1f
                : Math.Clamp((pdf.X - anchorX) / origReach, 0.05f, 20f);

            float fx = factor;
            float fy = proportional ? factor : 1f;
            ApplyTransformFromOriginal(point => new SKPoint(
                anchorX + (point.X - anchorX) * fx,
                cy + (point.Y - cy) * fy));
        }
        else
        {
            float anchorY = _transformHandle == TransformHandleKind.ScaleTop ? b.Bottom : b.Top;
            float origReach = _transformStartPdf.Y - anchorY;
            factor = Math.Abs(origReach) < eps
                ? 1f
                : Math.Clamp((pdf.Y - anchorY) / origReach, 0.05f, 20f);

            float fy = factor;
            float fx = proportional ? factor : 1f;
            ApplyTransformFromOriginal(point => new SKPoint(
                cx + (point.X - cx) * fx,
                anchorY + (point.Y - anchorY) * fy));
        }

        PostStatus(proportional
            ? $"Resizing selection (proportional): {factor:0.##}x."
            : $"Resizing selection: {factor:0.##}x along {(horizontal ? "width" : "height")}.");
    }

    private void UpdateCenteredScaleDrag(SKPoint pdf)
    {
        float distance = Distance(pdf, _transformCenter);
        float factor = Math.Clamp(distance / Math.Max(_transformStartDistance, ViewportConstants.ZeroLengthEpsilon), 0.05f, 20f);
        ApplyTransformFromOriginal(point => new SKPoint(
            _transformCenter.X + (point.X - _transformCenter.X) * factor,
            _transformCenter.Y + (point.Y - _transformCenter.Y) * factor));
        PostStatus($"Scaling selection: {factor:0.##}x.");
    }

    private void UpdateTopLeftAnchoredScaleDrag(SKPoint pdf)
    {
        SKRect b = _transformStartBounds;
        SKPoint anchor = new(b.Left, b.Top);
        float eps = Math.Max(ViewportConstants.ZeroLengthEpsilon, 0.0001f);
        float fx = 1f;
        float fy = 1f;

        switch (_transformHandle)
        {
            case TransformHandleKind.ScaleRight:
                fx = ScaleFactor(pdf.X, anchor.X, _transformStartPdf.X, eps);
                if (IsOrthoActive())
                    fy = fx;
                break;
            case TransformHandleKind.ScaleBottom:
                fy = ScaleFactor(pdf.Y, anchor.Y, _transformStartPdf.Y, eps);
                if (IsOrthoActive())
                    fx = fy;
                break;
            case TransformHandleKind.ScaleTopRight:
            case TransformHandleKind.ScaleBottomRight:
                float startDistance = Math.Max(eps, Distance(_transformStartPdf, anchor));
                float distance = Math.Max(eps, Distance(pdf, anchor));
                fx = fy = Math.Clamp(distance / startDistance, 0.05f, 20f);
                break;
        }

        ApplyTransformFromOriginal(point => new SKPoint(
            anchor.X + (point.X - anchor.X) * fx,
            anchor.Y + (point.Y - anchor.Y) * fy));
        PostStatus(Math.Abs(fx - fy) <= 0.001f
            ? $"Scaling selection from top-left: {fx:0.##}x."
            : $"Resizing selection from top-left: width {fx:0.##}x, height {fy:0.##}x.");
    }

    private static float ScaleFactor(float current, float anchor, float start, float eps)
    {
        float original = start - anchor;
        return Math.Abs(original) < eps
            ? 1f
            : Math.Clamp((current - anchor) / original, 0.05f, 20f);
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
                _transformMeasurementOriginalJoistDirections,
                _transformAnnotationOriginalPoints,
                "canvas transform",
                "canvas-transform");

        _draggingTransformScale = false;
        _draggingTransformRotate = false;
        _transformHandle = TransformHandleKind.None;
        _transformMeasurementOriginalPoints.Clear();
        _transformMeasurementOriginalHoles.Clear();
        _transformMeasurementOriginalJoistDirections.Clear();
        _transformAnnotationOriginalPoints.Clear();
        if (IsMouseCaptured)
            ReleaseMouseCapture();

        if (changed)
        {
            NotifyMeasurementsChanged(changedMeasurements);
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
                !SameHoles(measurement.Holes, holes) ||
                _transformMeasurementOriginalJoistDirections.TryGetValue(measurement, out double direction) &&
                Math.Abs(measurement.JoistDirectionDegrees - direction) > 0.0001)
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
        yield return (TransformHandleKind.ScaleLeft, new SKPoint(bounds.Left, (bounds.Top + bounds.Bottom) / 2f));
        yield return (TransformHandleKind.ScaleRight, new SKPoint(bounds.Right, (bounds.Top + bounds.Bottom) / 2f));
        yield return (TransformHandleKind.ScaleTop, new SKPoint((bounds.Left + bounds.Right) / 2f, bounds.Top));
        yield return (TransformHandleKind.ScaleBottom, new SKPoint((bounds.Left + bounds.Right) / 2f, bounds.Bottom));
        yield return (TransformHandleKind.Rotate, new SKPoint((bounds.Left + bounds.Right) / 2f, bounds.Top - ScreenToPdfDistance(22f)));
    }

    private IReadOnlyList<Measurement> SelectedTransformMeasurements() =>
        GetSelectedMeasurements()
            .Where(measurement => IsMeasurementOnActivePage(measurement))
            .ToList();

    private IReadOnlyList<PageAnnotation> SelectedTransformAnnotations() =>
        _selectedAnnotations
            .Where(annotation => IsAnnotationVisibleOnActivePage(annotation))
            .ToList();

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

    private static void ApplyJoistDirectionRotation(Measurement measurement, double? rotationDegrees)
    {
        if (!rotationDegrees.HasValue || !ShouldRotateJoistDirection(measurement))
            return;

        measurement.JoistDirectionDegrees = JoistTakeoffCalculator.NormalizeDirectionDegrees(
            measurement.JoistDirectionDegrees + rotationDegrees.Value);
    }

    private void ApplyJoistDirectionRotationFromOriginal(Measurement measurement, double? rotationDegrees)
    {
        if (!rotationDegrees.HasValue || !ShouldRotateJoistDirection(measurement))
            return;

        double original = _transformMeasurementOriginalJoistDirections.TryGetValue(measurement, out double value)
            ? value
            : measurement.JoistDirectionDegrees;
        measurement.JoistDirectionDegrees = JoistTakeoffCalculator.NormalizeDirectionDegrees(
            original + rotationDegrees.Value);
    }

    private static bool ShouldRotateJoistDirection(Measurement measurement) =>
        measurement.JoistEnabled &&
        measurement.JoistDirectionLocked &&
        measurement.JoistDirectionFollowsAreaRotation &&
        OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area";

    private static IReadOnlyList<SKPoint> AnnotationTransformPoints(PageAnnotation annotation)
    {
        if (!IsRectangularAnnotation(annotation))
            return annotation.Points;

        return AnnotationRectangleCorners(annotation.Points);
    }

    private static void ApplyAnnotationTransform(PageAnnotation annotation, Func<SKPoint, SKPoint> transform)
    {
        if (IsRectangularAnnotation(annotation))
        {
            StoreTransformedAnnotationCorners(
                annotation.Points,
                AnnotationRectangleCorners(annotation.Points).Select(transform));
            return;
        }

        ApplyTransform(annotation.Points, transform);
    }

    private static void RestoreAnnotationTransform(
        PageAnnotation annotation,
        IReadOnlyList<SKPoint> originalPoints,
        Func<SKPoint, SKPoint> transform)
    {
        if (IsRectangularAnnotation(annotation))
        {
            StoreTransformedAnnotationCorners(
                annotation.Points,
                AnnotationRectangleCorners(originalPoints).Select(transform));
            return;
        }

        for (int i = 0; i < annotation.Points.Count && i < originalPoints.Count; i++)
            annotation.Points[i] = transform(originalPoints[i]);
    }

    private static void StoreTransformedAnnotationCorners(
        List<SKPoint> target,
        IEnumerable<SKPoint> transformedCorners)
    {
        var corners = transformedCorners.ToList();
        if (corners.Count < 4)
            return;

        target.Clear();
        target.AddRange(corners.Take(4));
    }

    private static bool IsRectangularAnnotation(PageAnnotation annotation) =>
        OurPlanCoreJobStore.NormalizePageAnnotationKind(annotation.Kind) is "rectangle" or "note" or "cloud" or "highlight";

    private static IReadOnlyList<SKPoint> AnnotationRectangleCorners(IReadOnlyList<SKPoint> points)
    {
        if (points.Count >= 4)
            return points.Take(4).ToList();

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
        ScaleLeft,
        ScaleRight,
        ScaleTop,
        ScaleBottom,
        Rotate,
    }

    private static bool IsEdgeHandle(TransformHandleKind kind) =>
        kind is TransformHandleKind.ScaleLeft
             or TransformHandleKind.ScaleRight
             or TransformHandleKind.ScaleTop
             or TransformHandleKind.ScaleBottom;

    private static bool ShouldScaleFromTopLeftAnchor(TransformHandleKind kind) =>
        kind is TransformHandleKind.ScaleTopRight
             or TransformHandleKind.ScaleBottomRight
             or TransformHandleKind.ScaleRight
             or TransformHandleKind.ScaleBottom;

    private static bool IsTransformRotationSnapActive() =>
        (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
}
