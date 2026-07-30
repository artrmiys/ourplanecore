using System;
using System.Collections.Generic;
using System.Linq;
using OurPlanCore;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private readonly List<SheetOverlayBitmapLayer> _sheetOverlayLayersBelow = [];
    private readonly List<SheetOverlayBitmapLayer> _sheetOverlayLayersAbove = [];
    private float _sheetOverlayOpacity = 1f;
    private string _sheetOverlayId = "";

    public bool HasSheetOverlay =>
        _sheetOverlayBitmap != null ||
        _sheetOverlayLayersBelow.Count > 0 ||
        _sheetOverlayLayersAbove.Count > 0;

    public bool HasSheetOverlayBinding(
        string targetPageFolder,
        string overlayPageFolder,
        string overlayId = "") =>
        _sheetOverlayBitmap != null &&
        SheetOverlayReciprocalService.SameFolder(_sheetOverlayTargetPageFolder, targetPageFolder) &&
        SheetOverlayReciprocalService.SameFolder(_sheetOverlaySourcePageFolder, overlayPageFolder) &&
        (string.IsNullOrWhiteSpace(overlayId) ||
         string.Equals(_sheetOverlayId, overlayId, StringComparison.OrdinalIgnoreCase));

    public void SetSheetOverlayLayers(
        IReadOnlyList<SheetOverlayBitmapLayer> layers,
        string activeOverlayId,
        string targetPageFolder)
    {
        int activeIndex = -1;
        for (int i = 0; i < layers.Count; i++)
        {
            if (string.Equals(layers[i].Id, activeOverlayId, StringComparison.OrdinalIgnoreCase))
            {
                activeIndex = i;
                break;
            }
        }

        SheetOverlayBitmapLayer? active = activeIndex >= 0 ? layers[activeIndex] : null;
        string nextOverlayPageFolder = active?.SourcePageFolder ?? "";
        string nextOverlayId = active?.Id ?? "";
        bool sameBinding =
            active != null &&
            !string.IsNullOrWhiteSpace(_sheetOverlayTargetPageFolder) &&
            !string.IsNullOrWhiteSpace(_sheetOverlaySourcePageFolder) &&
            SheetOverlayReciprocalService.SameFolder(
                _sheetOverlayTargetPageFolder,
                targetPageFolder) &&
            SheetOverlayReciprocalService.SameFolder(
                _sheetOverlaySourcePageFolder,
                nextOverlayPageFolder) &&
            string.Equals(_sheetOverlayId, nextOverlayId, StringComparison.OrdinalIgnoreCase);
        bool preserveTransformGesture =
            sameBinding &&
            HasPendingSheetOverlayTransformGesture;
        float liveOffsetXPt = _sheetOverlayOffsetXPt;
        float liveOffsetYPt = _sheetOverlayOffsetYPt;
        float liveOverlayScale = _sheetOverlayScale;
        float liveOverlayRotationDegrees = _sheetOverlayRotationDegrees;

        ClearSheetOverlayCore(
            preserveTransformGesture,
            preserveBindingIdentity: sameBinding);
        _sheetOverlayTargetPageFolder = targetPageFolder ?? "";

        for (int i = 0; i < layers.Count; i++)
        {
            SheetOverlayBitmapLayer layer = layers[i];
            if (i < activeIndex || activeIndex < 0)
                _sheetOverlayLayersBelow.Add(layer);
            else if (i > activeIndex)
                _sheetOverlayLayersAbove.Add(layer);
        }

        if (active != null)
        {
            _sheetOverlayBitmap = active.Bitmap;
            _sheetOverlayWidthPt = active.WidthPt;
            _sheetOverlayHeightPt = active.HeightPt;
            _sheetOverlayOffsetXPt = preserveTransformGesture ? liveOffsetXPt : active.OffsetXPt;
            _sheetOverlayOffsetYPt = preserveTransformGesture ? liveOffsetYPt : active.OffsetYPt;
            _sheetOverlayScale = NormalizeSheetOverlayScale(
                preserveTransformGesture ? liveOverlayScale : active.Scale);
            _sheetOverlayRotationDegrees = NormalizeSheetOverlayRotation(
                preserveTransformGesture ? liveOverlayRotationDegrees : active.RotationDegrees);
            _sheetOverlayOpacity = Math.Clamp(active.Opacity, 0.05f, 1f);
            _sheetOverlayBitmapScale = active.BitmapScale > 0
                ? active.BitmapScale
                : InferSheetOverlayBitmapScale(active.Bitmap, active.WidthPt);
            _lastSheetOverlayRefreshRequestScale = _sheetOverlayBitmapScale;
            _sheetOverlayName = active.Name ?? "";
            _sheetOverlaySourcePageFolder = nextOverlayPageFolder;
            _sheetOverlayId = nextOverlayId;
            if (!string.IsNullOrWhiteSpace(active.PdfPath))
            {
                SetOverlayPdfSnapSource(
                    active.PdfPath,
                    active.PdfPageIndex,
                    _sheetOverlayName,
                    active.PdfLayers);
            }
        }

        float loadedRenderScale = layers
            .Where(layer => layer.BitmapScale > 0)
            .Select(layer => layer.BitmapScale)
            .DefaultIfEmpty(0)
            .Min();
        _lastSheetOverlayRefreshRequestScale = loadedRenderScale;
        MaybeRequestSheetOverlayRenderScaleRefresh();
        RequestRepaint();
    }

    public bool PreviewSheetOverlayOpacity(
        string targetPageFolder,
        string overlayPageFolder,
        string overlayId,
        float opacity)
    {
        if (_sheetOverlayBitmap == null ||
            !SheetOverlayReciprocalService.SameFolder(_pageFolder, targetPageFolder) ||
            !SheetOverlayReciprocalService.SameFolder(
                _sheetOverlaySourcePageFolder,
                overlayPageFolder) ||
            !string.Equals(_sheetOverlayId, overlayId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _sheetOverlayOpacity = Math.Clamp(opacity, 0.05f, 1f);
        ClearStaticPageFrameCache();
        RequestRepaint();
        return true;
    }

    private static void DisposeSheetOverlayLayers(List<SheetOverlayBitmapLayer> layers)
    {
        foreach (SheetOverlayBitmapLayer layer in layers)
            layer.Bitmap.Dispose();
        layers.Clear();
    }

    private void DrawSheetOverlay(SKCanvas canvas, SKRect visiblePdf)
    {
        if (!HasSheetOverlay || _pdfW <= 0 || _pdfH <= 0)
            return;

        foreach (SheetOverlayBitmapLayer layer in _sheetOverlayLayersBelow)
            DrawSheetOverlayLayer(canvas, visiblePdf, layer);

        if (_sheetOverlayBitmap != null)
        {
            DrawSheetOverlayBitmap(
                canvas,
                visiblePdf,
                _sheetOverlayBitmap,
                _sheetOverlayWidthPt,
                _sheetOverlayHeightPt,
                _sheetOverlayOffsetXPt,
                _sheetOverlayOffsetYPt,
                _sheetOverlayScale,
                _sheetOverlayRotationDegrees,
                _sheetOverlayOpacity,
                CurrentSheetOverlayFilterQuality());
        }

        foreach (SheetOverlayBitmapLayer layer in _sheetOverlayLayersAbove)
            DrawSheetOverlayLayer(canvas, visiblePdf, layer);
    }

    private void DrawSheetOverlayLayer(
        SKCanvas canvas,
        SKRect visiblePdf,
        SheetOverlayBitmapLayer layer)
    {
        SKFilterQuality quality = _renderNavigationFastFrame || layer.BitmapScale <= 0
            ? SKFilterQuality.None
            : layer.BitmapScale > Math.Max(0.001f, _zoom * layer.Scale) * 1.05f
                ? SKFilterQuality.Medium
                : SKFilterQuality.None;
        DrawSheetOverlayBitmap(
            canvas,
            visiblePdf,
            layer.Bitmap,
            layer.WidthPt,
            layer.HeightPt,
            layer.OffsetXPt,
            layer.OffsetYPt,
            layer.Scale,
            layer.RotationDegrees,
            layer.Opacity,
            quality);
    }

    private void DrawSheetOverlayBitmap(
        SKCanvas canvas,
        SKRect visiblePdf,
        SKBitmap bitmap,
        float widthPt,
        float heightPt,
        float offsetXPt,
        float offsetYPt,
        float scale,
        float rotationDegrees,
        float opacity,
        SKFilterQuality filterQuality)
    {
        float width = widthPt > 0 ? widthPt : _pdfW;
        float height = heightPt > 0 ? heightPt : _pdfH;
        if (width <= 0 || height <= 0)
            return;

        SKRect displayBounds = OverlayDisplayBounds(
            width,
            height,
            offsetXPt,
            offsetYPt,
            scale,
            rotationDegrees);
        if (!RectsIntersect(displayBounds, visiblePdf))
            return;

        using var paint = new SKPaint
        {
            IsAntialias = false,
            FilterQuality = filterQuality,
            Color = SKColors.White.WithAlpha(
                (byte)Math.Clamp(
                    (int)Math.Round(Math.Clamp(opacity, 0.05f, 1f) * 255),
                    13,
                    255)),
        };
        canvas.Save();
        canvas.ClipRect(visiblePdf);
        canvas.Translate(offsetXPt, offsetYPt);
        canvas.RotateDegrees(rotationDegrees);
        canvas.Scale(scale);
        canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height), paint);
        canvas.Restore();
    }

    private SKFilterQuality CurrentSheetOverlayFilterQuality()
    {
        if (_renderNavigationFastFrame || _sheetOverlayBitmapScale <= 0)
            return SKFilterQuality.None;

        float displayedScale = Math.Max(0.001f, _zoom * _sheetOverlayScale);
        return _sheetOverlayBitmapScale > displayedScale * 1.05f
            ? SKFilterQuality.Medium
            : SKFilterQuality.None;
    }

    private static SKRect OverlayDisplayBounds(
        float width,
        float height,
        float offsetXPt,
        float offsetYPt,
        float scale,
        float rotationDegrees)
    {
        SKPoint Transform(SKPoint point)
        {
            SKPoint vector = OverlayLocalTransformVector(point, scale, rotationDegrees);
            return new SKPoint(offsetXPt + vector.X, offsetYPt + vector.Y);
        }

        SKPoint p0 = Transform(new SKPoint(0, 0));
        SKPoint p1 = Transform(new SKPoint(width, 0));
        SKPoint p2 = Transform(new SKPoint(width, height));
        SKPoint p3 = Transform(new SKPoint(0, height));
        return new SKRect(
            MathF.Min(MathF.Min(p0.X, p1.X), MathF.Min(p2.X, p3.X)),
            MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y)),
            MathF.Max(MathF.Max(p0.X, p1.X), MathF.Max(p2.X, p3.X)),
            MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y)));
    }
}
