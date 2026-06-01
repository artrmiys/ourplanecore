using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private SKBitmap? _detailBitmap;
    private SKRect _detailPdfRect;
    private float _detailBitmapScale;
    private string _detailPageKey = "";
    private readonly List<DetailRenderTile> _detailTiles = [];
    private long _detailTileClock;
    private long _detailTileBytes;
    private DetailRenderRequest? _pendingDetailRender;
    private DetailRenderRequest? _activeDetailRender;
    private bool _detailRenderInProgress;
    private int _detailRenderVersion;
    private const int MaxDetailRenderTileEntries = 64;
    private static readonly long MaxDetailRenderTileBytes =
        ResolveViewportRamBudget(2_400_000_000L, 4_800_000_000L, 0.07);

    private sealed record DetailRenderRequest(
        int Version,
        string PdfPath,
        int PdfIndex,
        string PageFolder,
        SKRect ClipRect,
        float RenderScale,
        Dictionary<int, bool> LayerStates,
        HashSet<int> HighlightedLayers,
        IReadOnlyList<PdfLayerInfo>? CachedLayers);

    private sealed class DetailRenderTile
    {
        public required SKBitmap Bitmap { get; init; }
        public SKRect PdfRect { get; init; }
        public float BitmapScale { get; init; }
        public string PageKey { get; init; } = "";
        public long EstimatedBytes { get; init; }
        public long LastUsed { get; set; }
    }

    private void ClearDetailRender()
    {
        _pendingDetailRender = null;
        _detailRenderVersion++;
        ClearDetailRenderBitmap();
    }

    private void ClearDetailRenderBitmap()
    {
        foreach (DetailRenderTile tile in _detailTiles)
            tile.Bitmap.Dispose();
        _detailTiles.Clear();
        _detailTileBytes = 0;
        _detailBitmap = null;
        _detailPdfRect = SKRect.Empty;
        _detailBitmapScale = 0;
        _detailPageKey = "";
    }

    private void QueueDetailRenderIfNeeded(bool force)
    {
        if (!TryBuildDetailRenderRequest(force, out DetailRenderRequest? request))
            return;
        if (request == null)
            return;

        if (!force && DetailRequestCoversCurrentView(_activeDetailRender, request.RenderScale))
            return;

        if (!force && DetailRequestCoversCurrentView(_pendingDetailRender, request.RenderScale))
            return;

        if (!force && IsSameDetailRequest(_activeDetailRender, request))
            return;

        if (!force && IsSameDetailRequest(_pendingDetailRender, request))
            return;

        _pendingDetailRender = request with { Version = ++_detailRenderVersion };
        _ = StartNextDetailRenderAsync();
    }

    private bool TryBuildDetailRenderRequest(bool force, out DetailRenderRequest? request)
    {
        request = null;
        if (!ViewportRenderPolicy.DetailRenderEnabled ||
            string.IsNullOrWhiteSpace(_pdfPath) ||
            _pageBitmap == null ||
            _pdfW <= 0 ||
            _pdfH <= 0 ||
            _bitmapScale <= 0 ||
            _showingPreviousPageDuringSwitch)
        {
            return false;
        }

        SKRect clip = ClampPdfRectToPage(GetVisiblePdfRect(ViewportRenderPolicy.CurrentDetailRenderPaddingScreenPx));
        if (clip.Width <= 0 || clip.Height <= 0)
            return false;

        float targetScale = ViewportRenderPolicy.SelectDetailRenderScale(
            _zoom,
            clip.Width,
            clip.Height,
            _bitmapScale);
        if (targetScale <= 0)
            return false;

        if (!force && DetailRenderCoversCurrentView(targetScale))
            return false;

        int version = _detailRenderVersion + 1;
        request = new DetailRenderRequest(
            version,
            _pdfPath,
            _pdfIndex,
            _pageFolder,
            clip,
            targetScale,
            EffectiveLayerStates(),
            EffectiveHighlightedLayers(),
            LayerRenderCachedLayers());
        return true;
    }

    private bool DetailRenderCoversCurrentView(float targetScale)
    {
        SKRect visible = ClampPdfRectToPage(GetVisiblePdfRect());
        string pageKey = DetailPageKey(_pdfPath, _pdfIndex, _pageFolder);
        foreach (DetailRenderTile tile in _detailTiles)
        {
            if (!string.Equals(tile.PageKey, pageKey, StringComparison.OrdinalIgnoreCase))
                continue;

            if (tile.BitmapScale < targetScale * 0.92f)
                continue;

            if (!RectContains(tile.PdfRect, visible, tolerancePt: 0.5f))
                continue;

            tile.LastUsed = ++_detailTileClock;
            return true;
        }

        return false;
    }

    private bool DetailRequestCoversCurrentView(DetailRenderRequest? request, float targetScale)
    {
        if (request == null)
            return false;

        if (!string.Equals(request.PdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) ||
            request.PdfIndex != _pdfIndex ||
            !string.Equals(request.PageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (request.RenderScale < targetScale * 0.92f)
            return false;

        SKRect visible = ClampPdfRectToPage(GetVisiblePdfRect());
        return RectContains(request.ClipRect, visible, tolerancePt: 0.5f);
    }

    private async Task StartNextDetailRenderAsync()
    {
        if (_detailRenderInProgress || _pendingDetailRender == null)
            return;

        DetailRenderRequest request = _pendingDetailRender;
        _pendingDetailRender = null;
        _activeDetailRender = request;
        _detailRenderInProgress = true;
        try
        {
            Stopwatch renderWatch = Stopwatch.StartNew();
            var renderResult = await PdfLayerRenderService.TryRenderAsync(
                request.PdfPath,
                request.PdfIndex,
                request.RenderScale,
                request.LayerStates,
                request.HighlightedLayers,
                request.CachedLayers,
                request.ClipRect);
            renderWatch.Stop();
            ReportViewportRenderProfile(
                "detail",
                request.PageFolder,
                request.PdfPath,
                request.PdfIndex,
                request.RenderScale,
                renderWatch.ElapsedMilliseconds,
                fromCache: false,
                request.ClipRect);

            if (!IsCurrentDetailRequest(request))
                return;

            SKBitmap? decodedBitmap = null;
            if (renderResult.Ok)
                decodedBitmap = await Task.Run(() => SKBitmap.Decode(renderResult.Result.ImageBytes));

            if (renderResult.Ok)
                ApplyDetailRenderResult(request, renderResult.Result, decodedBitmap);
            else if (!string.IsNullOrWhiteSpace(renderResult.Error))
                AppLog.Warn($"Viewport detail render unavailable: {renderResult.Error}");
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Viewport detail render failed.");
        }
        finally
        {
            if (ReferenceEquals(_activeDetailRender, request))
                _activeDetailRender = null;
            _detailRenderInProgress = false;
            if (_pendingDetailRender != null)
                _ = StartNextDetailRenderAsync();
        }
    }

    private void ApplyDetailRenderResult(DetailRenderRequest request, PdfLayerRenderResult render, SKBitmap? bitmap)
    {
        if (bitmap == null)
        {
            AppLog.Warn("Viewport detail render returned an unreadable image.");
            return;
        }

        if (!IsCurrentDetailRequest(request))
        {
            bitmap.Dispose();
            return;
        }

        SKRect clip = ClampPdfRectToPage(render.ClipRect ?? request.ClipRect);
        if (clip.Width <= 0 || clip.Height <= 0)
        {
            bitmap.Dispose();
            return;
        }

        _detailBitmap = bitmap;
        _detailPdfRect = clip;
        _detailBitmapScale = clip.Width > 0 ? bitmap.Width / clip.Width : request.RenderScale;
        _detailPageKey = DetailPageKey(request.PdfPath, request.PdfIndex, request.PageFolder);
        AddDetailRenderTile(bitmap, clip, _detailBitmapScale, _detailPageKey);
        RequestRepaint();
    }

    private void DrawDetailRenderTile(SKCanvas canvas)
    {
        if (_detailTiles.Count == 0)
        {
            return;
        }

        SKRect visible = ClampPdfRectToPage(GetVisiblePdfRect());
        string pageKey = DetailPageKey(_pdfPath, _pdfIndex, _pageFolder);
        foreach (DetailRenderTile tile in _detailTiles.ToList())
        {
            if (!string.Equals(tile.PageKey, pageKey, StringComparison.OrdinalIgnoreCase) ||
                tile.BitmapScale <= 0 ||
                tile.PdfRect.Width <= 0 ||
                tile.PdfRect.Height <= 0 ||
                !Intersects(tile.PdfRect, visible))
            {
                continue;
            }

            tile.LastUsed = ++_detailTileClock;
            using var paint = new SKPaint
            {
                IsAntialias = false,
                FilterQuality = _zoom > tile.BitmapScale * 1.05f ? SKFilterQuality.High : SKFilterQuality.Medium,
            };
            var src = new SKRect(0, 0, tile.Bitmap.Width, tile.Bitmap.Height);
            var dst = new SKRect(
                (tile.PdfRect.Left - _panX) * _zoom,
                (tile.PdfRect.Top - _panY) * _zoom,
                (tile.PdfRect.Right - _panX) * _zoom,
                (tile.PdfRect.Bottom - _panY) * _zoom);
            canvas.DrawBitmap(tile.Bitmap, src, dst, paint);
        }
    }

    private void AddDetailRenderTile(SKBitmap bitmap, SKRect clip, float bitmapScale, string pageKey)
    {
        long estimatedBytes = EstimateBitmapBytes(bitmap);
        _detailTiles.Add(new DetailRenderTile
        {
            Bitmap = bitmap,
            PdfRect = clip,
            BitmapScale = bitmapScale,
            PageKey = pageKey,
            EstimatedBytes = estimatedBytes,
            LastUsed = ++_detailTileClock,
        });
        _detailTileBytes += estimatedBytes;
        TrimDetailRenderTiles();
    }

    private void TrimDetailRenderTiles()
    {
        while (_detailTiles.Count > MaxDetailRenderTileEntries || _detailTileBytes > MaxDetailRenderTileBytes)
        {
            DetailRenderTile? oldest = _detailTiles
                .OrderBy(tile => tile.LastUsed)
                .FirstOrDefault(tile => !ReferenceEquals(tile.Bitmap, _detailBitmap))
                ?? _detailTiles.OrderBy(tile => tile.LastUsed).FirstOrDefault();
            if (oldest == null)
                return;

            _detailTileBytes -= oldest.EstimatedBytes;
            _detailTiles.Remove(oldest);
            if (ReferenceEquals(oldest.Bitmap, _detailBitmap))
            {
                DetailRenderTile? newest = _detailTiles.LastOrDefault();
                _detailBitmap = newest?.Bitmap;
                _detailPdfRect = newest?.PdfRect ?? SKRect.Empty;
                _detailBitmapScale = newest?.BitmapScale ?? 0;
                _detailPageKey = newest?.PageKey ?? "";
            }
            oldest.Bitmap.Dispose();
        }
    }

    private static long EstimateBitmapBytes(SKBitmap bitmap) =>
        (long)Math.Max(0, bitmap.Width) * Math.Max(0, bitmap.Height) * 4;

    private void ReportViewportRenderProfile(
        string kind,
        string pageFolder,
        string pdfPath,
        int pdfIndex,
        float renderScale,
        long elapsedMs,
        bool fromCache,
        SKRect? clipRect)
    {
        string clip = clipRect.HasValue
            ? $" clip={clipRect.Value.Left:0.#},{clipRect.Value.Top:0.#},{clipRect.Value.Right:0.#},{clipRect.Value.Bottom:0.#};"
            : "";
        AppLog.Info(
            $"Viewport render profile kind={kind}; elapsed={elapsedMs}ms; cache={fromCache}; " +
            $"zoom={_zoom:0.###}; bitmapScale={_bitmapScale:0.###}; renderedScale={_renderedScale:0.###}; " +
            $"targetScale={renderScale:0.###};{clip} page='{pageFolder}'; " +
            $"pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pdfIndex + 1}");
    }

    private bool IsCurrentDetailRequest(DetailRenderRequest request) =>
        request.Version == _detailRenderVersion &&
        string.Equals(request.PdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
        request.PdfIndex == _pdfIndex &&
        string.Equals(request.PageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameDetailRequest(DetailRenderRequest? existing, DetailRenderRequest next) =>
        existing != null &&
        string.Equals(existing.PdfPath, next.PdfPath, StringComparison.OrdinalIgnoreCase) &&
        existing.PdfIndex == next.PdfIndex &&
        string.Equals(existing.PageFolder, next.PageFolder, StringComparison.OrdinalIgnoreCase) &&
        Math.Abs(existing.RenderScale - next.RenderScale) < 0.02f &&
        RectNearlyEquals(existing.ClipRect, next.ClipRect, tolerancePt: 0.5f);

    private SKRect ClampPdfRectToPage(SKRect rect)
    {
        if (_pdfW <= 0 || _pdfH <= 0)
            return SKRect.Empty;

        float left = Math.Clamp(rect.Left, 0, _pdfW);
        float top = Math.Clamp(rect.Top, 0, _pdfH);
        float right = Math.Clamp(rect.Right, left, _pdfW);
        float bottom = Math.Clamp(rect.Bottom, top, _pdfH);
        return right > left && bottom > top ? new SKRect(left, top, right, bottom) : SKRect.Empty;
    }

    private static bool RectContains(SKRect outer, SKRect inner, float tolerancePt) =>
        outer.Left <= inner.Left + tolerancePt &&
        outer.Top <= inner.Top + tolerancePt &&
        outer.Right >= inner.Right - tolerancePt &&
        outer.Bottom >= inner.Bottom - tolerancePt;

    private static bool RectNearlyEquals(SKRect a, SKRect b, float tolerancePt) =>
        Math.Abs(a.Left - b.Left) <= tolerancePt &&
        Math.Abs(a.Top - b.Top) <= tolerancePt &&
        Math.Abs(a.Right - b.Right) <= tolerancePt &&
        Math.Abs(a.Bottom - b.Bottom) <= tolerancePt;

    private static bool Intersects(SKRect a, SKRect b) =>
        a.Left < b.Right &&
        a.Right > b.Left &&
        a.Top < b.Bottom &&
        a.Bottom > b.Top;

    private static string DetailPageKey(string pdfPath, int pdfIndex, string pageFolder) =>
        string.Join('|', pdfPath.ToLowerInvariant(), pdfIndex, pageFolder.ToLowerInvariant());
}
