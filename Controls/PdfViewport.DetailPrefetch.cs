using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private void QueueAdjacentDetailRenderPrefetchFromTile(DetailRenderTile tile, float targetScale)
    {
        if (!ViewportRenderPolicy.ShouldUseDetailRenderPrefetch(_zoom, _isFastNavigating) ||
            string.IsNullOrWhiteSpace(_pdfPath) ||
            _pdfIndex < 0 ||
            tile.PdfRect.Width <= 0 ||
            tile.PdfRect.Height <= 0)
        {
            return;
        }

        var request = new DetailRenderRequest(
            _detailRenderVersion,
            _detailTileGeneration,
            _pdfPath,
            _pdfIndex,
            _pageFolder,
            tile.PdfRect,
            Math.Max(targetScale, tile.BitmapScale),
            EffectiveLayerStates(),
            EffectiveHighlightedLayers(),
            LayerRenderCachedLayers());
        QueueAdjacentDetailRenderPrefetch(request, tile.PdfRect);
    }

    private void QueueAdjacentDetailRenderPrefetch(DetailRenderRequest source, SKRect sourceClip)
    {
        if (!ViewportRenderPolicy.ShouldUseDetailRenderPrefetch(_zoom, _isFastNavigating) ||
            !IsCurrentDetailRequest(source) ||
            source.RenderScale <= 0 ||
            sourceClip.Width <= 0 ||
            sourceClip.Height <= 0)
        {
            return;
        }

        SKRect visible = ClampPdfRectToPage(GetVisiblePdfRect());
        if (visible.Width <= 0 || visible.Height <= 0)
            return;

        foreach (SKRect clip in BuildAdjacentDetailPrefetchClips(sourceClip, visible))
        {
            if (DetailRenderTileCoversRect(clip, source.RenderScale))
                continue;

            string key = DetailPrefetchKey(source, clip);
            lock (_detailPrefetchGate)
            {
                if (!_detailPrefetchInFlight.Add(key))
                    continue;
            }

            DetailRenderRequest request = source with { ClipRect = clip };
            _ = PrefetchDetailRenderTileAsync(request, key);
        }
    }

    private IReadOnlyList<SKRect> BuildAdjacentDetailPrefetchClips(SKRect sourceClip, SKRect visible)
    {
        float shiftX = Math.Max(visible.Width * ViewportRenderPolicy.DetailRenderPrefetchShiftFactor, sourceClip.Width * 0.45f);
        float shiftY = Math.Max(visible.Height * ViewportRenderPolicy.DetailRenderPrefetchShiftFactor, sourceClip.Height * 0.45f);
        var clips = new List<SKRect>
        {
            OffsetAndClampDetailClip(sourceClip, shiftX, 0),
            OffsetAndClampDetailClip(sourceClip, -shiftX, 0),
            OffsetAndClampDetailClip(sourceClip, 0, shiftY),
            OffsetAndClampDetailClip(sourceClip, 0, -shiftY),
        };

        return clips
            .Where(clip => clip.Width > 0 &&
                           clip.Height > 0 &&
                           !RectNearlyEquals(clip, sourceClip, tolerancePt: 0.5f))
            .GroupBy(QuantizedRectKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(ViewportRenderPolicy.DetailRenderPrefetchTileCount)
            .ToList();
    }

    private SKRect OffsetAndClampDetailClip(SKRect sourceClip, float dx, float dy) =>
        ClampPdfRectToPage(new SKRect(
            sourceClip.Left + dx,
            sourceClip.Top + dy,
            sourceClip.Right + dx,
            sourceClip.Bottom + dy));

    private async Task PrefetchDetailRenderTileAsync(DetailRenderRequest request, string key)
    {
        SKBitmap? decodedBitmap = null;
        try
        {
            await Task.Delay(ViewportRenderPolicy.DetailRenderPrefetchDelayMs);
            await DetailTilePrefetchSemaphore.WaitAsync();
            try
            {
                if (!IsCurrentDetailPrefetchRequest(request) ||
                    !ViewportRenderPolicy.ShouldUseDetailRenderPrefetch(_zoom, _isFastNavigating) ||
                    DetailRenderTileCoversRect(request.ClipRect, request.RenderScale))
                {
                    return;
                }

                Stopwatch renderWatch = Stopwatch.StartNew();
                var renderResult = await PdfLayerRenderService.TryRenderDedicatedProcessAsync(
                    request.PdfPath,
                    request.PdfIndex,
                    request.RenderScale,
                    request.LayerStates,
                    request.HighlightedLayers,
                    request.CachedLayers,
                    request.ClipRect);
                renderWatch.Stop();

                if (!renderResult.Ok)
                {
                    if (!string.IsNullOrWhiteSpace(renderResult.Error))
                        AppLog.Warn($"Viewport detail prefetch unavailable: {renderResult.Error}");
                    return;
                }

                decodedBitmap = await Task.Run(() => SKBitmap.Decode(renderResult.Result.ImageBytes));
                if (decodedBitmap == null || !IsCurrentDetailPrefetchRequest(request))
                    return;

                SKRect clip = ClampPdfRectToPage(renderResult.Result.ClipRect ?? request.ClipRect);
                if (clip.Width <= 0 || clip.Height <= 0)
                    return;

                float bitmapScale = clip.Width > 0 ? decodedBitmap.Width / clip.Width : request.RenderScale;
                AddDetailRenderTile(decodedBitmap, clip, bitmapScale, DetailPageKey(request.PdfPath, request.PdfIndex, request.PageFolder));
                decodedBitmap = null;
                ReportViewportRenderProfile(
                    "detail-prefetch",
                    request.PageFolder,
                    request.PdfPath,
                    request.PdfIndex,
                    request.RenderScale,
                    renderWatch.ElapsedMilliseconds,
                    fromCache: false,
                    clip);
                RequestRepaint();
            }
            finally
            {
                DetailTilePrefetchSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Viewport detail prefetch failed.");
        }
        finally
        {
            decodedBitmap?.Dispose();
            lock (_detailPrefetchGate)
                _detailPrefetchInFlight.Remove(key);
        }
    }

    private bool DetailRenderTileCoversRect(SKRect rect, float targetScale)
    {
        string pageKey = DetailPageKey(_pdfPath, _pdfIndex, _pageFolder);
        foreach (DetailRenderTile tile in _detailTiles)
        {
            if (!string.Equals(tile.PageKey, pageKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (tile.BitmapScale < targetScale * 0.92f)
                continue;
            if (RectContains(tile.PdfRect, rect, tolerancePt: 0.5f))
                return true;
        }

        return false;
    }

    private static string DetailPrefetchKey(DetailRenderRequest request, SKRect clip)
    {
        var sb = new StringBuilder();
        sb.Append(request.PdfPath.ToLowerInvariant())
          .Append('|').Append(request.PdfIndex)
          .Append('|').Append(request.PageFolder.ToLowerInvariant())
          .Append('|').Append(request.RenderScale.ToString("0.###", CultureInfo.InvariantCulture))
          .Append('|').Append(QuantizedRectKey(clip))
          .Append("|layers:");
        foreach (var pair in request.LayerStates.OrderBy(pair => pair.Key))
            sb.Append(pair.Key).Append('=').Append(pair.Value ? '1' : '0').Append(';');
        sb.Append("|hi:");
        foreach (int layer in request.HighlightedLayers.OrderBy(value => value))
            sb.Append(layer).Append(';');
        sb.Append("|visible:");
        if (request.CachedLayers != null)
        {
            foreach (PdfLayerInfo layer in request.CachedLayers.OrderBy(layer => layer.Number))
                sb.Append(layer.Number).Append('=').Append(layer.IsOn ? '1' : '0').Append(':').Append(layer.Name).Append(';');
        }

        return sb.ToString();
    }

    private static string QuantizedRectKey(SKRect rect) =>
        $"{Math.Round(rect.Left, 1)},{Math.Round(rect.Top, 1)},{Math.Round(rect.Right, 1)},{Math.Round(rect.Bottom, 1)}";
}
