using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Input;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private enum SheetOverlayPointEditStep
    {
        None,
        MoveSource,
        MoveTarget,
        ScaleSource,
        ScaleTarget,
    }

    private SKBitmap? _sheetOverlayBitmap;
    private float _sheetOverlayWidthPt;
    private float _sheetOverlayHeightPt;
    private float _sheetOverlayOffsetXPt;
    private float _sheetOverlayOffsetYPt;
    private float _sheetOverlayScale = 1f;
    private float _sheetOverlayBitmapScale;
    private float _lastSheetOverlayRefreshRequestScale;
    private string _sheetOverlayName = "";
    private SheetOverlayPointEditStep _sheetOverlayPointEditStep;
    private SKPoint _sheetOverlayEditAnchorLocal;
    private SKPoint _sheetOverlayEditAnchorTarget;
    private SKPoint _sheetOverlayEditScaleLocal;

    public bool HasSheetOverlay => _sheetOverlayBitmap != null;

    public void SetSheetOverlay(
        SKBitmap bitmap,
        float widthPt,
        float heightPt,
        string overlayName,
        float offsetXPt = 0,
        float offsetYPt = 0,
        float overlayScale = 1,
        string overlayPdfPath = "",
        int overlayPageIndex = 0,
        IReadOnlyList<PdfLayerInfo>? overlayLayers = null,
        float bitmapScale = 0)
    {
        ClearSheetOverlay();
        _sheetOverlayBitmap = bitmap;
        _sheetOverlayWidthPt = widthPt;
        _sheetOverlayHeightPt = heightPt;
        _sheetOverlayOffsetXPt = offsetXPt;
        _sheetOverlayOffsetYPt = offsetYPt;
        _sheetOverlayScale = NormalizeSheetOverlayScale(overlayScale);
        _sheetOverlayBitmapScale = bitmapScale > 0 ? bitmapScale : InferSheetOverlayBitmapScale(bitmap, widthPt);
        _lastSheetOverlayRefreshRequestScale = _sheetOverlayBitmapScale;
        _sheetOverlayName = overlayName ?? "";
        if (!string.IsNullOrWhiteSpace(overlayPdfPath))
            SetOverlayPdfSnapSource(overlayPdfPath, overlayPageIndex, _sheetOverlayName, overlayLayers);
        CancelSheetOverlayPointEdit(silent: true);
        MaybeRequestSheetOverlayRenderScaleRefresh();
        RequestRepaint();
    }

    public void ClearSheetOverlay()
    {
        _sheetOverlayBitmap?.Dispose();
        _sheetOverlayBitmap = null;
        _sheetOverlayWidthPt = 0;
        _sheetOverlayHeightPt = 0;
        _sheetOverlayOffsetXPt = 0;
        _sheetOverlayOffsetYPt = 0;
        _sheetOverlayScale = 1;
        _sheetOverlayBitmapScale = 0;
        _lastSheetOverlayRefreshRequestScale = 0;
        _sheetOverlayName = "";
        ClearOverlayPdfSnapSource();
        CancelSheetOverlayPointEdit(silent: true);
        RequestRepaint();
    }

    public void BeginSheetOverlayPointEdit()
    {
        if (_sheetOverlayBitmap == null)
        {
            PostStatus("Set a sheet overlay before editing it.");
            return;
        }

        _sheetOverlayPointEditStep = SheetOverlayPointEditStep.MoveSource;
        _sheetOverlayEditAnchorLocal = default;
        _sheetOverlayEditAnchorTarget = default;
        _sheetOverlayEditScaleLocal = default;
        PostStatus("Overlay edit: click a point on the overlay to grab it.");
        RequestRepaint();
        Focus();
    }

    private bool IsSheetOverlayPointEditing =>
        _sheetOverlayPointEditStep != SheetOverlayPointEditStep.None;

    private bool TryHandleSheetOverlayTransformShortcut(KeyEventArgs e)
    {
        if (_sheetOverlayBitmap == null)
            return false;

        ModifierKeys modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Control) == 0 ||
            (modifiers & ModifierKeys.Alt) == 0)
        {
            return false;
        }

        bool fine = (modifiers & ModifierKeys.Shift) != 0;
        float nudgePt = fine ? 1f : 6f;
        float scaleFactor = fine ? 1.01f : 1.05f;
        Key key = OurPlaneCore.KeyboardShortcutKeys.EffectiveKey(e);

        switch (key)
        {
            case Key.Left:
                ApplySheetOverlayTransform(
                    _sheetOverlayOffsetXPt - nudgePt,
                    _sheetOverlayOffsetYPt,
                    _sheetOverlayScale,
                    BuildSheetOverlayTransformStatus(
                        "Overlay moved",
                        _sheetOverlayOffsetXPt - nudgePt,
                        _sheetOverlayOffsetYPt,
                        _sheetOverlayScale));
                break;
            case Key.Right:
                ApplySheetOverlayTransform(
                    _sheetOverlayOffsetXPt + nudgePt,
                    _sheetOverlayOffsetYPt,
                    _sheetOverlayScale,
                    BuildSheetOverlayTransformStatus(
                        "Overlay moved",
                        _sheetOverlayOffsetXPt + nudgePt,
                        _sheetOverlayOffsetYPt,
                        _sheetOverlayScale));
                break;
            case Key.Up:
                ApplySheetOverlayTransform(
                    _sheetOverlayOffsetXPt,
                    _sheetOverlayOffsetYPt - nudgePt,
                    _sheetOverlayScale,
                    BuildSheetOverlayTransformStatus(
                        "Overlay moved",
                        _sheetOverlayOffsetXPt,
                        _sheetOverlayOffsetYPt - nudgePt,
                        _sheetOverlayScale));
                break;
            case Key.Down:
                ApplySheetOverlayTransform(
                    _sheetOverlayOffsetXPt,
                    _sheetOverlayOffsetYPt + nudgePt,
                    _sheetOverlayScale,
                    BuildSheetOverlayTransformStatus(
                        "Overlay moved",
                        _sheetOverlayOffsetXPt,
                        _sheetOverlayOffsetYPt + nudgePt,
                        _sheetOverlayScale));
                break;
            case Key.Add:
            case Key.OemPlus:
                ApplySheetOverlayTransform(
                    _sheetOverlayOffsetXPt,
                    _sheetOverlayOffsetYPt,
                    _sheetOverlayScale * scaleFactor,
                    BuildSheetOverlayTransformStatus(
                        "Overlay scaled",
                        _sheetOverlayOffsetXPt,
                        _sheetOverlayOffsetYPt,
                        NormalizeSheetOverlayScale(_sheetOverlayScale * scaleFactor)));
                break;
            case Key.Subtract:
            case Key.OemMinus:
                ApplySheetOverlayTransform(
                    _sheetOverlayOffsetXPt,
                    _sheetOverlayOffsetYPt,
                    _sheetOverlayScale / scaleFactor,
                    BuildSheetOverlayTransformStatus(
                        "Overlay scaled",
                        _sheetOverlayOffsetXPt,
                        _sheetOverlayOffsetYPt,
                        NormalizeSheetOverlayScale(_sheetOverlayScale / scaleFactor)));
                break;
            case Key.D0:
            case Key.NumPad0:
                ApplySheetOverlayTransform(0, 0, 1, "Overlay transform reset.");
                break;
            default:
                return false;
        }

        e.Handled = true;
        return true;
    }

    private bool HandleSheetOverlayPointEditClick(SKPoint pdf)
    {
        if (!IsSheetOverlayPointEditing)
            return false;

        if (_sheetOverlayBitmap == null)
        {
            CancelSheetOverlayPointEdit(silent: true);
            PostStatus("Overlay edit cancelled: overlay is missing.");
            return true;
        }

        switch (_sheetOverlayPointEditStep)
        {
            case SheetOverlayPointEditStep.MoveSource:
                _sheetOverlayEditAnchorLocal = OverlayDisplayToLocal(pdf);
                _sheetOverlayPointEditStep = SheetOverlayPointEditStep.MoveTarget;
                PostStatus("Overlay edit: click where that point should land.");
                break;

            case SheetOverlayPointEditStep.MoveTarget:
                _sheetOverlayEditAnchorTarget = pdf;
                ApplySheetOverlayTransform(
                    pdf.X - _sheetOverlayEditAnchorLocal.X * _sheetOverlayScale,
                    pdf.Y - _sheetOverlayEditAnchorLocal.Y * _sheetOverlayScale,
                    _sheetOverlayScale,
                    "Overlay moved by point. Click a second overlay point to scale, or press Esc to finish.");
                _sheetOverlayPointEditStep = SheetOverlayPointEditStep.ScaleSource;
                break;

            case SheetOverlayPointEditStep.ScaleSource:
                _sheetOverlayEditScaleLocal = OverlayDisplayToLocal(pdf);
                if (OverlayDistance(_sheetOverlayEditAnchorLocal, _sheetOverlayEditScaleLocal) < 0.01f)
                {
                    PostStatus("Overlay scale: second point is too close to the first point.");
                    break;
                }

                _sheetOverlayPointEditStep = SheetOverlayPointEditStep.ScaleTarget;
                PostStatus("Overlay edit: click where the second point should land.");
                break;

            case SheetOverlayPointEditStep.ScaleTarget:
                float localDistance = OverlayDistance(_sheetOverlayEditAnchorLocal, _sheetOverlayEditScaleLocal);
                float targetDistance = OverlayDistance(_sheetOverlayEditAnchorTarget, pdf);
                if (localDistance < 0.01f || targetDistance < 0.01f)
                {
                    PostStatus("Overlay scale: choose two separated points.");
                    _sheetOverlayPointEditStep = SheetOverlayPointEditStep.ScaleSource;
                    break;
                }

                float newScale = NormalizeSheetOverlayScale(targetDistance / localDistance);
                ApplySheetOverlayTransform(
                    _sheetOverlayEditAnchorTarget.X - _sheetOverlayEditAnchorLocal.X * newScale,
                    _sheetOverlayEditAnchorTarget.Y - _sheetOverlayEditAnchorLocal.Y * newScale,
                    newScale,
                    $"Overlay scaled by two points: {newScale:0.###}x.");
                CancelSheetOverlayPointEdit(silent: true);
                break;
        }

        RequestRepaint();
        return true;
    }

    private void CancelSheetOverlayPointEdit(bool silent = false)
    {
        bool wasEditing = IsSheetOverlayPointEditing;
        _sheetOverlayPointEditStep = SheetOverlayPointEditStep.None;
        _sheetOverlayEditAnchorLocal = default;
        _sheetOverlayEditAnchorTarget = default;
        _sheetOverlayEditScaleLocal = default;
        if (wasEditing && !silent)
            PostStatus("Overlay edit cancelled.");
        RequestRepaint();
    }

    private void ApplySheetOverlayTransform(float offsetXPt, float offsetYPt, float overlayScale, string status)
    {
        _sheetOverlayOffsetXPt = offsetXPt;
        _sheetOverlayOffsetYPt = offsetYPt;
        _sheetOverlayScale = NormalizeSheetOverlayScale(overlayScale);
        SheetOverlayTransformChanged?.Invoke(new SheetOverlayTransformChange(
            _sheetOverlayOffsetXPt,
            _sheetOverlayOffsetYPt,
            _sheetOverlayScale,
            status));
        PostStatus(status);
    }

    private static string BuildSheetOverlayTransformStatus(
        string prefix,
        float offsetXPt,
        float offsetYPt,
        float overlayScale) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}: X {1:0.###}, Y {2:0.###}, scale {3:0.###}x.",
            prefix,
            offsetXPt,
            offsetYPt,
            overlayScale);

    private SKPoint OverlayDisplayToLocal(SKPoint displayPoint)
    {
        float scale = Math.Max(_sheetOverlayScale, 0.001f);
        return new SKPoint(
            (displayPoint.X - _sheetOverlayOffsetXPt) / scale,
            (displayPoint.Y - _sheetOverlayOffsetYPt) / scale);
    }

    private void DrawSheetOverlay(SKCanvas canvas, SKRect visiblePdf)
    {
        if (_sheetOverlayBitmap == null || _pdfW <= 0 || _pdfH <= 0)
            return;

        using var paint = new SKPaint
        {
            // Sheet overlays are alignment references; keep linework crisp.
            IsAntialias = false,
            FilterQuality = SKFilterQuality.None,
        };

        float width = _sheetOverlayWidthPt > 0 ? _sheetOverlayWidthPt : _pdfW;
        float height = _sheetOverlayHeightPt > 0 ? _sheetOverlayHeightPt : _pdfH;
        var dest = new SKRect(
            _sheetOverlayOffsetXPt,
            _sheetOverlayOffsetYPt,
            _sheetOverlayOffsetXPt + width * _sheetOverlayScale,
            _sheetOverlayOffsetYPt + height * _sheetOverlayScale);
        if (!RectsIntersect(dest, visiblePdf))
            return;

        SKRect drawDest = IntersectRects(dest, visiblePdf);
        if (drawDest.Width <= 0 || drawDest.Height <= 0 || dest.Width <= 0 || dest.Height <= 0)
            return;

        var src = new SKRect(
            (drawDest.Left - dest.Left) / dest.Width * _sheetOverlayBitmap.Width,
            (drawDest.Top - dest.Top) / dest.Height * _sheetOverlayBitmap.Height,
            (drawDest.Right - dest.Left) / dest.Width * _sheetOverlayBitmap.Width,
            (drawDest.Bottom - dest.Top) / dest.Height * _sheetOverlayBitmap.Height);
        canvas.DrawBitmap(_sheetOverlayBitmap, src, drawDest, paint);
    }

    private static SKRect IntersectRects(SKRect a, SKRect b)
    {
        float left = Math.Max(a.Left, b.Left);
        float top = Math.Max(a.Top, b.Top);
        float right = Math.Min(a.Right, b.Right);
        float bottom = Math.Min(a.Bottom, b.Bottom);
        return right > left && bottom > top
            ? new SKRect(left, top, right, bottom)
            : SKRect.Empty;
    }

    private void DrawSheetOverlayEditGuides(SKCanvas canvas)
    {
        if (!IsSheetOverlayPointEditing || _sheetOverlayBitmap == null)
            return;

        using var pointPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0x7A, 0xCC, 235),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var linePaint = new SKPaint
        {
            Color = new SKColor(0x00, 0x7A, 0xCC, 210),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = ScreenToPdfDistance(1.5f),
        };
        float radius = ScreenToPdfDistance(5f);

        if (_sheetOverlayPointEditStep is SheetOverlayPointEditStep.MoveTarget or SheetOverlayPointEditStep.ScaleSource or SheetOverlayPointEditStep.ScaleTarget)
        {
            SKPoint anchor = OverlayLocalToDisplay(_sheetOverlayEditAnchorLocal);
            canvas.DrawCircle(anchor, radius, pointPaint);
            if (_lastPointerPdf.HasValue && _sheetOverlayPointEditStep == SheetOverlayPointEditStep.MoveTarget)
                canvas.DrawLine(anchor, _lastPointerPdf.Value, linePaint);
        }

        if (_sheetOverlayPointEditStep == SheetOverlayPointEditStep.ScaleTarget)
        {
            SKPoint scalePoint = OverlayLocalToDisplay(_sheetOverlayEditScaleLocal);
            canvas.DrawCircle(scalePoint, radius, pointPaint);
            canvas.DrawLine(_sheetOverlayEditAnchorTarget, scalePoint, linePaint);
            if (_lastPointerPdf.HasValue)
                canvas.DrawLine(_sheetOverlayEditAnchorTarget, _lastPointerPdf.Value, linePaint);
        }
    }

    private SKPoint OverlayLocalToDisplay(SKPoint localPoint) =>
        new(
            _sheetOverlayOffsetXPt + localPoint.X * _sheetOverlayScale,
            _sheetOverlayOffsetYPt + localPoint.Y * _sheetOverlayScale);

    private static float OverlayDistance(SKPoint a, SKPoint b) =>
        MeasurementGeometry.Distance(a, b);

    private static float NormalizeSheetOverlayScale(float scale) =>
        float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0
            ? 1f
            : Math.Clamp(scale, 0.05f, 20f);

    private void MaybeRequestSheetOverlayRenderScaleRefresh()
    {
        if (_sheetOverlayBitmap == null || _sheetOverlayBitmapScale <= 0 || _zoom <= 0 || _isFastNavigating)
            return;

        float desired = ViewportRenderPolicy.SelectSheetOverlayRenderScale(
            _zoom,
            _sheetOverlayWidthPt,
            _sheetOverlayHeightPt);
        if (desired <= _sheetOverlayBitmapScale * 1.18f ||
            desired <= _lastSheetOverlayRefreshRequestScale * 1.01f)
        {
            return;
        }

        _lastSheetOverlayRefreshRequestScale = desired;
        SheetOverlayRenderScaleRefreshRequested?.Invoke(desired);
    }

    private static float InferSheetOverlayBitmapScale(SKBitmap bitmap, float widthPt)
    {
        if (bitmap.Width <= 0 || widthPt <= 0)
            return 0;

        return bitmap.Width / widthPt;
    }
}
