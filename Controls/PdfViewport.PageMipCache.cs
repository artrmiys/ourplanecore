using System;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    // Downsampled copy of the current page bitmap used while the view is zoomed
    // far out. Without it Skia re-minifies the full-resolution sheet on every
    // frame, which costs 50-90 ms on large sheets even with zero measurements.
    private SKBitmap? _pageBitmapMip;
    private float _pageBitmapMipFactor;
    private long _pageBitmapMipGeneration = -1;

    private const float PageMipMaxZoomRatio = 0.5f;
    private const float PageMipMinFactor = 1f / 16f;
    private const long PageMipMaxSourcePixels = 96_000_000;

    private (SKBitmap Bitmap, float BitmapScale) SelectPageBitmapForPaint()
    {
        SKBitmap bitmap = _pageBitmap!;
        if (_usingRasterSheetRender || _bitmapScale <= 0 || _zoom <= 0)
            return (bitmap, _bitmapScale);

        if (_zoom > _bitmapScale * PageMipMaxZoomRatio)
            return (bitmap, _bitmapScale);

        // Finest power-of-two factor whose mip still covers screen resolution.
        float factor = 0.5f;
        while (factor > PageMipMinFactor && _bitmapScale * factor * 0.5f >= _zoom)
            factor *= 0.5f;

        bool generationCurrent = _pageBitmapMip != null && _pageBitmapMipGeneration == _pageBitmapGeneration;
        if (generationCurrent && Math.Abs(_pageBitmapMipFactor - factor) < 0.001f)
            return (_pageBitmapMip!, _bitmapScale * _pageBitmapMipFactor);

        bool buildAllowed = !_renderNavigationFastFrame &&
                            (long)bitmap.Width * bitmap.Height <= PageMipMaxSourcePixels;
        if (!buildAllowed)
        {
            // A mip at a neighbouring factor is still correct imagery as long
            // as it is not coarser than the screen needs.
            if (generationCurrent && _bitmapScale * _pageBitmapMipFactor >= _zoom)
                return (_pageBitmapMip!, _bitmapScale * _pageBitmapMipFactor);

            return (bitmap, _bitmapScale);
        }

        SKBitmap? mip = null;
        try
        {
            int w = Math.Max(1, (int)MathF.Round(bitmap.Width * factor));
            int h = Math.Max(1, (int)MathF.Round(bitmap.Height * factor));
            mip = bitmap.Resize(new SKImageInfo(w, h, bitmap.ColorType, bitmap.AlphaType), SKFilterQuality.Medium);
        }
        catch
        {
            mip?.Dispose();
            mip = null;
        }

        if (mip == null)
            return (bitmap, _bitmapScale);

        _pageBitmapMip?.Dispose();
        _pageBitmapMip = mip;
        _pageBitmapMipFactor = factor;
        _pageBitmapMipGeneration = _pageBitmapGeneration;
        return (mip, _bitmapScale * factor);
    }

    private void ClearPageBitmapMip()
    {
        _pageBitmapMip?.Dispose();
        _pageBitmapMip = null;
        _pageBitmapMipFactor = 0f;
        _pageBitmapMipGeneration = -1;
    }
}
