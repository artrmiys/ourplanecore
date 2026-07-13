using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlanCore.Controls;

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
    private DateTime _detailRenderStartedUtc = DateTime.MinValue;
    private bool _detailRenderStartQueued;
    private int _detailRenderVersion;
    private int _detailTileGeneration;
    private DateTime _detailRenderHoldUntilUtc = DateTime.MinValue;
    private bool _detailRenderHoldResumeQueued;
    private readonly object _detailPrefetchGate = new();
    private readonly HashSet<string> _detailPrefetchInFlight = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxDetailRenderTileEntries = 32;
    private static readonly long MaxDetailRenderTileBytes =
        ResolveViewportRamBudget(160_000_000L, 512_000_000L, 0.025);
    private static readonly SemaphoreSlim DetailTilePrefetchSemaphore =
        new(ViewportRenderPolicy.DetailRenderPrefetchConcurrency, ViewportRenderPolicy.DetailRenderPrefetchConcurrency);

    private sealed record DetailRenderRequest(
        int Version,
        int TileGeneration,
        string PdfPath,
        int PdfIndex,
        string PageFolder,
        SKRect ClipRect,
        float RenderScale,
        Dictionary<int, bool> LayerStates,
        HashSet<int> HighlightedLayers,
        IReadOnlyList<PdfLayerInfo>? CachedLayers,
        bool AllowDuringNavigationPrefetch = false);

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
        // In-flight detail renders finish in ~25-300ms and are discarded by the
        // version check, so let them complete: killing the worker here costs a
        // 300-1100ms python restart and drops its document/display-list caches.
        // Only kill a render that looks genuinely stuck.
        if (_detailRenderInProgress && DetailRenderLooksStuck())
            PdfLayerRenderService.CancelDetailRenderWorker();
        ClearDetailRenderBitmap();
    }

    private bool DetailRenderLooksStuck() =>
        _detailRenderStartedUtc != DateTime.MinValue &&
        (DateTime.UtcNow - _detailRenderStartedUtc).TotalMilliseconds > 2000;

    private string _detailDocPrewarmedPageKey = "";

    /// <summary>
    /// Sends a tiny clipped render to the detail worker shortly after page open
    /// so the worker builds its document + display-list caches in the
    /// background. The first interactive detail tile then renders in ~25ms
    /// instead of paying the 200-500ms doc/display-list build cost.
    /// </summary>
    private void QueueDetailRenderDocPrewarm()
    {
        if (!ViewportRenderPolicy.DetailRenderEnabled || string.IsNullOrWhiteSpace(_pdfPath))
            return;

        string pageKey = $"{_pdfPath}|{_pdfIndex}";
        if (string.Equals(_detailDocPrewarmedPageKey, pageKey, StringComparison.OrdinalIgnoreCase))
            return;

        _detailDocPrewarmedPageKey = pageKey;
        string pdfPath = _pdfPath;
        int pdfIndex = _pdfIndex;
        IReadOnlyList<PdfLayerInfo>? cachedLayers = LayerRenderCachedLayers();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ViewportRenderPolicy.DetailRenderDocPrewarmDelayMs).ConfigureAwait(false);
                if (!string.Equals(_pdfPath, pdfPath, StringComparison.OrdinalIgnoreCase) || _pdfIndex != pdfIndex)
                    return;

                await PdfLayerRenderService.TryRenderAsync(
                    pdfPath,
                    pdfIndex,
                    1.0,
                    EmptyLayerStates,
                    EmptyHighlightedLayers,
                    cachedLayers,
                    new SKRect(0, 0, 32, 32)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Warn(ex, "Viewport detail doc prewarm failed.");
            }
        });
    }

    private void BeginPageSwitchDetailRenderHold()
    {
        _detailRenderHoldUntilUtc = DateTime.UtcNow.AddMilliseconds(
            ViewportRenderPolicy.PageSwitchDetailRenderDelayMs);
    }

    private void ClearDetailRenderBitmap()
    {
        _detailTileGeneration++;
        lock (_detailPrefetchGate)
            _detailPrefetchInFlight.Clear();

        foreach (DetailRenderTile tile in _detailTiles)
            tile.Bitmap.Dispose();
        _detailTiles.Clear();
        _detailTileBytes = 0;
        _detailBitmap = null;
        _detailPdfRect = SKRect.Empty;
        _detailBitmapScale = 0;
        _detailPageKey = "";
    }

    // Opt-in diagnostics for "the view is blurry but no detail render starts":
    // logs why each queue attempt was rejected. Env-gated so production runs
    // pay nothing; enable with ONC_DETAIL_DIAG=1.
    private static readonly bool DetailRenderDiagEnabled =
        Environment.GetEnvironmentVariable("ONC_DETAIL_DIAG") == "1";

    private void DetailRenderDiag(string reason)
    {
        if (DetailRenderDiagEnabled)
        {
            AppLog.Info(
                $"Viewport detail diag; reason={reason}; zoom={_zoom:0.###}; " +
                $"bitmapScale={_bitmapScale:0.###}; fastNav={_isFastNavigating}; " +
                $"prevPage={_showingPreviousPageDuringSwitch}; page='{_pageFolder}'");
        }
    }

    private void QueueDetailRenderIfNeeded(bool force, bool immediate = false)
    {
        if (ShouldHoldDetailRender(force))
        {
            DetailRenderDiag("hold");
            QueueDetailRenderAfterHold();
            return;
        }

        if (!force && _isFastNavigating)
        {
            DetailRenderDiag("fast-nav");
            return;
        }

        if (!TryBuildDetailRenderRequest(force, out DetailRenderRequest? request))
        {
            DetailRenderDiag("build-rejected");
            return;
        }
        if (request == null)
            return;

        if (!force && DetailRequestCoversCurrentView(_activeDetailRender, request.RenderScale))
        {
            DetailRenderDiag("covered-by-active");
            return;
        }

        if (!force && DetailRequestCoversCurrentView(_pendingDetailRender, request.RenderScale))
        {
            DetailRenderDiag("covered-by-pending");
            return;
        }

        if (!force && IsLiveDetailRequest(_activeDetailRender) && IsSameDetailRequest(_activeDetailRender, request))
        {
            DetailRenderDiag("same-as-active");
            return;
        }

        if (!force && IsLiveDetailRequest(_pendingDetailRender) && IsSameDetailRequest(_pendingDetailRender, request))
        {
            DetailRenderDiag("same-as-pending");
            return;
        }

        DetailRenderDiag("queued");
        _pendingDetailRender = request with { Version = ++_detailRenderVersion };
        QueueDetailRenderStart(force || immediate);
    }

    private void QueueDetailRenderStart(bool immediate)
    {
        TimeSpan delay = DetailRenderStartDelay(immediate);
        if (delay <= TimeSpan.Zero)
        {
            _detailRenderStartQueued = false;
            _ = StartNextDetailRenderAsync();
            return;
        }

        if (_detailRenderStartQueued)
            return;

        _detailRenderStartQueued = true;
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(async () =>
            {
                try
                {
                    await Task.Delay(delay);
                }
                catch
                {
                    // Best-effort coalescing; no cancellation token is used here.
                }
                finally
                {
                    _detailRenderStartQueued = false;
                    _ = StartNextDetailRenderAsync();
                }
            }));
    }

    private TimeSpan DetailRenderStartDelay(bool immediate)
    {
        TimeSpan coalesceDelay = immediate
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(ViewportRenderPolicy.DetailRenderCoalesceDelayMs);
        TimeSpan navigationDelay = DetailRenderNavigationQuietDelay();
        return navigationDelay > coalesceDelay ? navigationDelay : coalesceDelay;
    }

    private TimeSpan DetailRenderNavigationQuietDelay()
        => NavigationQuietDelay(TimeSpan.FromMilliseconds(ViewportRenderPolicy.DetailRenderNavigationQuietMs));

    private bool ShouldHoldDetailRender(bool force) =>
        force &&
        DateTime.UtcNow < _detailRenderHoldUntilUtc;

    private void QueueDetailRenderAfterHold()
    {
        if (_detailRenderHoldResumeQueued)
            return;

        _detailRenderHoldResumeQueued = true;
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(async () =>
            {
                try
                {
                    TimeSpan delay = _detailRenderHoldUntilUtc - DateTime.UtcNow;
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay);

                    _detailRenderHoldResumeQueued = false;
                    QueueDetailRenderIfNeeded(force: false, immediate: true);
                }
                catch (Exception ex)
                {
                    _detailRenderHoldResumeQueued = false;
                    AppLog.Warn(ex, "Viewport delayed detail render failed.");
                }
            }));
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

        SKRect desiredClip = ClampPdfRectToPage(GetVisiblePdfRect(
            ViewportRenderPolicy.DetailRenderPaddingScreenPxForZoom(_zoom)));
        if (desiredClip.Width <= 0 || desiredClip.Height <= 0)
            return false;

        float targetScale = ViewportRenderPolicy.SelectDetailRenderScale(
            _zoom,
            desiredClip.Width,
            desiredClip.Height,
            _bitmapScale);
        if (targetScale <= 0)
            return false;

        SKRect clip = BuildStableDetailRenderClip(desiredClip, targetScale);
        if (!RectNearlyEquals(clip, desiredClip, tolerancePt: 0.5f))
        {
            float stableScale = ViewportRenderPolicy.SelectDetailRenderScale(
                _zoom,
                clip.Width,
                clip.Height,
                _bitmapScale);
            if (stableScale >= targetScale * 0.92f)
                targetScale = stableScale;
            else
                clip = desiredClip;
        }

        if (!force && DetailRenderCoversCurrentView(targetScale))
            return false;

        int version = _detailRenderVersion + 1;
        request = new DetailRenderRequest(
            version,
            _detailTileGeneration,
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

    private SKRect BuildStableDetailRenderClip(SKRect desiredClip, float renderScale)
    {
        if (renderScale <= 0 ||
            desiredClip.Width <= 0 ||
            desiredClip.Height <= 0 ||
            _pdfW <= 0 ||
            _pdfH <= 0)
        {
            return desiredClip;
        }

        float tilePdf = ViewportRenderPolicy.DetailRenderStableTileScreenPx / renderScale;
        if (tilePdf <= 0)
            return desiredClip;

        SKRect stable = ClampPdfRectToPage(new SKRect(
            MathF.Floor(desiredClip.Left / tilePdf) * tilePdf,
            MathF.Floor(desiredClip.Top / tilePdf) * tilePdf,
            MathF.Ceiling(desiredClip.Right / tilePdf) * tilePdf,
            MathF.Ceiling(desiredClip.Bottom / tilePdf) * tilePdf));
        if (stable.Width <= 0 || stable.Height <= 0)
            return desiredClip;

        float desiredArea = desiredClip.Width * desiredClip.Height;
        float stableArea = stable.Width * stable.Height;
        if (desiredArea <= 0 ||
            stableArea > desiredArea * ViewportRenderPolicy.DetailRenderStableTileMaxExpansionFactor)
        {
            return desiredClip;
        }

        float stablePixels = stableArea * renderScale * renderScale;
        if (stablePixels > ViewportRenderPolicy.DetailRenderMaxPixels)
            return desiredClip;

        return stable;
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
            QueueAdjacentDetailRenderPrefetchFromTile(tile, targetScale);
            return true;
        }

        return false;
    }

    private bool DetailRenderCoversVisibleViewForPaint()
    {
        if (_detailTiles.Count == 0 || _zoom <= 0)
            return false;

        SKRect visible = ClampPdfRectToPage(GetVisiblePdfRect());
        string pageKey = DetailPageKey(_pdfPath, _pdfIndex, _pageFolder);
        float minimumPaintScale = Math.Max(_bitmapScale, _zoom * 0.90f);
        foreach (DetailRenderTile tile in _detailTiles)
        {
            if (!string.Equals(tile.PageKey, pageKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (tile.BitmapScale < minimumPaintScale)
                continue;
            if (RectContains(tile.PdfRect, visible, tolerancePt: 0.5f))
                return true;
        }

        return false;
    }

    private bool DetailRequestCoversCurrentView(DetailRenderRequest? request, float targetScale)
    {
        if (request == null)
            return false;

        // An invalidated request (version/generation bumped by e.g. a raster
        // DPI upgrade) will be dropped at start — it must not suppress
        // queueing the fresh request that would actually sharpen the view.
        if (request.Version != _detailRenderVersion ||
            request.TileGeneration != _detailTileGeneration)
        {
            return false;
        }

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
        {
            if (_pendingDetailRender != null)
                DetailRenderDiag("start-blocked-in-progress");
            return;
        }

        if (DetailRenderNavigationQuietDelay() > TimeSpan.Zero)
        {
            DetailRenderDiag("start-blocked-nav-quiet");
            QueueDetailRenderStart(immediate: true);
            return;
        }

        DetailRenderRequest request = _pendingDetailRender;
        _pendingDetailRender = null;
        if (!IsCurrentDetailRequest(request))
        {
            // The queued request was invalidated while it waited (typically a
            // raster DPI upgrade bumping the version/generation right after
            // page open). Without an immediate re-queue the view stays blurry
            // until the next user interaction — nothing else re-checks.
            DetailRenderDiag("start-dropped-stale");
            QueueDetailRenderIfNeeded(force: false, immediate: true);
            return;
        }

        DetailRenderDiag("start-render");

        _activeDetailRender = request;
        _detailRenderInProgress = true;
        _detailRenderStartedUtc = DateTime.UtcNow;
        try
        {
            PausePreviewPrefetchFor(ViewportRenderPolicy.PreviewPrefetchActiveRenderHoldMs);
            if (await TryApplyDetailRenderFromDiskAsync(request))
                return;

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
            if (!IsCurrentDetailRequest(request))
            {
                // The view/bitmap changed while this render was in flight
                // (typically a raster DPI upgrade landing mid-render). The
                // result is useless, but the CURRENT view still needs a sharp
                // tile — without an immediate re-queue nothing else asks.
                DetailRenderDiag("completed-dropped-stale");
                QueueDetailRenderIfNeeded(force: false, immediate: true);
                return;
            }

            ReportViewportRenderProfile(
                "detail",
                request.PageFolder,
                request.PdfPath,
                request.PdfIndex,
                request.RenderScale,
                renderWatch.ElapsedMilliseconds,
                fromCache: false,
                request.ClipRect);

            SKBitmap? decodedBitmap = null;
            if (renderResult.Ok)
            {
                decodedBitmap = await Task.Run(() => DecodePdfLayerRenderBitmapWithMetrics(
                    "detail",
                    request.PageFolder,
                    request.PdfPath,
                    request.PdfIndex,
                    renderResult.Result));
            }

            if (renderResult.Ok)
            {
                ApplyDetailRenderResult(request, renderResult.Result, decodedBitmap);
                QueueDetailTileDiskWrite(request, renderResult.Result);
            }
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
            PausePreviewPrefetchFor(ViewportRenderPolicy.PreviewPrefetchAfterActiveRenderHoldMs);
            if (_pendingDetailRender != null)
                _ = StartNextDetailRenderAsync();
        }
    }

    // Disk-cached tiles restore sharpness after a page switch without paying
    // the 450-1700ms live render for the first tile at a returning zoom.
    private async Task<bool> TryApplyDetailRenderFromDiskAsync(DetailRenderRequest request)
    {
        if (!DetailTileDiskCache.IsCacheableRequest(request.LayerStates, request.HighlightedLayers, request.CachedLayers))
            return false;

        Stopwatch readWatch = Stopwatch.StartNew();
        (bool hit, byte[] imageBytes, SKRect appliedClip) = await Task.Run(() =>
        {
            bool ok = DetailTileDiskCache.TryRead(
                request.PdfPath,
                request.PdfIndex,
                request.RenderScale,
                request.ClipRect,
                out byte[] bytes,
                out SKRect clip);
            return (ok, bytes, clip);
        });
        if (!hit || !IsCurrentDetailRequest(request))
            return false;

        SKBitmap? bitmap = await Task.Run(() => SKBitmap.Decode(imageBytes));
        readWatch.Stop();
        if (bitmap == null)
            return false;

        if (!IsCurrentDetailRequest(request))
        {
            bitmap.Dispose();
            return false;
        }

        ReportViewportRenderProfile(
            "detail",
            request.PageFolder,
            request.PdfPath,
            request.PdfIndex,
            request.RenderScale,
            readWatch.ElapsedMilliseconds,
            fromCache: true,
            request.ClipRect);
        ApplyDetailRenderResult(request, new PdfLayerRenderResult { ClipRect = appliedClip }, bitmap);
        return true;
    }

    private static void QueueDetailTileDiskWrite(DetailRenderRequest request, PdfLayerRenderResult render)
    {
        if (!DetailTileDiskCache.IsCacheableRequest(request.LayerStates, request.HighlightedLayers, request.CachedLayers))
            return;

        DetailTileDiskCache.QueueWrite(
            request.PdfPath,
            request.PdfIndex,
            request.RenderScale,
            request.ClipRect,
            render.ClipRect ?? request.ClipRect,
            render);
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
            DetailRenderDiag("apply-dropped-stale");
            QueueDetailRenderIfNeeded(force: false, immediate: true);
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
        QueueAdjacentDetailRenderPrefetch(
            request with { AllowDuringNavigationPrefetch = true },
            clip);
        RequestRepaint();
    }

    private void DrawDetailRenderTile(SKCanvas canvas)
    {
        if (_detailTiles.Count == 0)
            return;

        SKRect visible = ClampPdfRectToPage(GetVisiblePdfRect());
        string pageKey = DetailPageKey(_pdfPath, _pdfIndex, _pageFolder);
        var eligibleTiles = _detailTiles
            .Where(tile =>
                string.Equals(tile.PageKey, pageKey, StringComparison.OrdinalIgnoreCase) &&
                tile.BitmapScale > 0 &&
                tile.PdfRect.Width > 0 &&
                tile.PdfRect.Height > 0 &&
                Intersects(tile.PdfRect, visible))
            .ToList();
        if (eligibleTiles.Count == 0)
            return;

        float minimumPaintScale = Math.Max(_bitmapScale, _zoom * 0.90f);
        DetailRenderTile? coveringTile = eligibleTiles
            .Where(tile => tile.BitmapScale >= minimumPaintScale &&
                           RectContains(tile.PdfRect, visible, tolerancePt: 0.5f))
            .OrderByDescending(tile => tile.BitmapScale)
            .ThenByDescending(tile => tile.LastUsed)
            .FirstOrDefault();
        if (coveringTile != null)
        {
            DrawDetailRenderTileBitmap(canvas, coveringTile);
            return;
        }

        foreach (DetailRenderTile tile in eligibleTiles
                     .OrderByDescending(tile => IntersectionArea(tile.PdfRect, visible))
                     .ThenByDescending(tile => tile.BitmapScale)
                     .ThenByDescending(tile => tile.LastUsed)
                     .Take(ViewportRenderPolicy.DetailRenderMaxPaintTiles))
        {
            DrawDetailRenderTileBitmap(canvas, tile);
        }
    }

    private void DrawDetailRenderTileBitmap(SKCanvas canvas, DetailRenderTile tile)
    {
        tile.LastUsed = ++_detailTileClock;
        using var paint = new SKPaint
        {
            IsAntialias = false,
            FilterQuality = CurrentDetailTileFilterQuality(tile),
        };
        var src = new SKRect(0, 0, tile.Bitmap.Width, tile.Bitmap.Height);
        var dst = new SKRect(
            (tile.PdfRect.Left - _panX) * _zoom,
            (tile.PdfRect.Top - _panY) * _zoom,
            (tile.PdfRect.Right - _panX) * _zoom,
            (tile.PdfRect.Bottom - _panY) * _zoom);
        canvas.DrawBitmap(tile.Bitmap, src, dst, paint);
    }

    private SKFilterQuality CurrentDetailTileFilterQuality(DetailRenderTile tile)
    {
        if (_zoom <= 0 || tile.BitmapScale <= 0)
            return SKFilterQuality.Low;

        if (_renderNavigationFastFrame)
        {
            float scaleRatio = _zoom / tile.BitmapScale;
            if (Math.Abs(scaleRatio - 1f) <= 0.08f)
                return SKFilterQuality.None;

            return SKFilterQuality.Low;
        }

        return _zoom > tile.BitmapScale * 1.05f
            ? SKFilterQuality.Low
            : SKFilterQuality.Medium;
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

    private static SKBitmap? DecodePdfLayerRenderBitmap(PdfLayerRenderResult render)
    {
        if (!render.HasRawImage)
            return SKBitmap.Decode(render.ImageBytes);

        // Shared parallel, allocation-light raw->BGRA decoder (PdfLayerRenderService).
        return PdfLayerRenderService.CreateBitmapFromRawRender(render);
    }

    private static SKBitmap? DecodePdfLayerRenderBitmapWithMetrics(
        string kind,
        string pageFolder,
        string pdfPath,
        int pdfIndex,
        PdfLayerRenderResult render)
    {
        Stopwatch watch = Stopwatch.StartNew();
        SKBitmap? bitmap = DecodePdfLayerRenderBitmap(render);
        watch.Stop();
        ViewportPerformanceRecorder.RecordBitmapDecode(
            kind,
            pageFolder,
            Path.GetFileName(pdfPath),
            pdfIndex + 1,
            watch.ElapsedMilliseconds,
            bitmap != null);
        return bitmap;
    }

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
        ViewportPerformanceRecorder.RecordRenderProfile(
            kind,
            pageFolder,
            Path.GetFileName(pdfPath),
            pdfIndex + 1,
            _zoom,
            _bitmapScale,
            _renderedScale,
            renderScale,
            elapsedMs,
            fromCache,
            clipRect);
    }

    private bool IsCurrentDetailRequest(DetailRenderRequest request) =>
        request.Version == _detailRenderVersion &&
        request.TileGeneration == _detailTileGeneration &&
        string.Equals(request.PdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
        request.PdfIndex == _pdfIndex &&
        string.Equals(request.PageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase) &&
        CurrentViewStillMatchesDetailRequest(request);

    private bool IsCurrentDetailPrefetchRequest(DetailRenderRequest request) =>
        request.TileGeneration == _detailTileGeneration &&
        string.Equals(request.PdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
        request.PdfIndex == _pdfIndex &&
        string.Equals(request.PageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase) &&
        ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale);

    private bool CurrentViewStillMatchesDetailRequest(DetailRenderRequest request)
    {
        if (!ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale))
            return false;

        SKRect visible = ClampPdfRectToPage(GetVisiblePdfRect());
        if (visible.Width <= 0 || visible.Height <= 0)
            return false;

        float currentTargetScale = ViewportRenderPolicy.SelectDetailRenderScale(
            _zoom,
            visible.Width,
            visible.Height,
            _bitmapScale);
        if (currentTargetScale <= 0 || request.RenderScale < currentTargetScale * 0.85f)
            return false;

        return RectContains(request.ClipRect, visible, tolerancePt: 0.5f);
    }

    // A request whose version/generation no longer match was invalidated (page
    // bitmap swap, raster DPI upgrade, page switch): its result will be
    // dropped, so it must never suppress queueing a replacement.
    private bool IsLiveDetailRequest(DetailRenderRequest? request) =>
        request != null &&
        request.Version == _detailRenderVersion &&
        request.TileGeneration == _detailTileGeneration;

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

    private static float IntersectionArea(SKRect a, SKRect b)
    {
        float width = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left));
        float height = Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top));
        return width * height;
    }

    private static string DetailPageKey(string pdfPath, int pdfIndex, string pageFolder) =>
        string.Join('|', pdfPath.ToLowerInvariant(), pdfIndex, pageFolder.ToLowerInvariant());
}
