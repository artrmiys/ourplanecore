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
    private const string RasterSheetBitmapCacheWarmingReason = "bitmap cache warming";

    // ── Layer API ─────────────────────────────────────────────────────────────

    private static DocnetRenderResult RenderPageBitmapWithDocnet(string pdfPath, int pdfIndex, float renderScale)
    {
        float scale = Math.Clamp(renderScale, 0.10f, 4.0f);
        using var docReader = _docLib.GetDocReader(pdfPath, new PageDimensions(scale));
        using var pageReader = docReader.GetPageReader(pdfIndex);

        int bw = pageReader.GetPageWidth();
        int bh = pageReader.GetPageHeight();
        byte[] bytes = pageReader.GetImage();

        var info = new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);

        return new DocnetRenderResult(
            bw / scale,
            bh / scale,
            scale,
            bitmap);
    }

    private void ApplyDocnetRenderResult(DocnetRenderResult render)
    {
        _pageBitmap?.Dispose();
        _pageBitmap = render.Bitmap;
        MarkPageBitmapIdentity(_pdfPath, _pdfIndex, _pageFolder);
        _pdfW = render.WidthPt;
        _pdfH = render.HeightPt;
        _bitmapScale = render.BitmapScale;
        _layers = [];
        _usingLayerRenderer = false;
        _usingRasterSheetRender = false;
        _usingRasterSheetOverviewRender = false;
        ClearRasterSheetVisualSegments();
        _renderedScale = render.BitmapScale;
        _showingPreviousPageDuringSwitch = false;
        ClearDetailRenderBitmap();
    }

    private void ApplyCachedBitmapRender(CachedBitmapRender render)
    {
        _pageBitmap?.Dispose();
        _pageBitmap = render.Bitmap;
        MarkPageBitmapIdentity(_pdfPath, _pdfIndex, _pageFolder);
        _pdfW = render.WidthPt;
        _pdfH = render.HeightPt;
        _bitmapScale = render.BitmapScale;
        _layers = [];
        _usingLayerRenderer = false;
        _usingRasterSheetRender = false;
        _usingRasterSheetOverviewRender = false;
        ClearRasterSheetVisualSegments();
        _renderedScale = render.BitmapScale;
        _showingPreviousPageDuringSwitch = false;
        ClearDetailRenderBitmap();
    }

    private bool TryApplyRasterSheetRender(
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        RasterSheetSource? rasterSheet,
        ViewState? restoreView,
        bool fitAfter,
        bool preferOverview,
        out string skipReason) =>
        TryApplyRasterSheetRender(
            pdfPath,
            pdfIndex,
            pageFolder,
            rasterSheet,
            restoreView,
            fitAfter,
            preferOverview,
            requireCachedBitmap: false,
            out skipReason);

    private bool TryApplyRasterSheetRender(
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        RasterSheetSource? rasterSheet,
        ViewState? restoreView,
        bool fitAfter,
        bool preferOverview,
        bool requireCachedBitmap,
        out string skipReason)
    {
        skipReason = "";
        bool usingOverview = false;
        if (preferOverview)
        {
            if (TryGetRasterSheetBitmapCache(
                    pageFolder,
                    pdfPath,
                    rasterSheet,
                    preferOverview: true,
                    out RasterSheetBitmapResult cachedOverview))
            {
                return ApplyRasterSheetBitmapRender(
                    pdfPath,
                    pdfIndex,
                    pageFolder,
                    rasterSheet,
                    cachedOverview,
                    restoreView,
                    fitAfter,
                    usingOverview: true);
            }

            if (requireCachedBitmap)
            {
                skipReason = RasterSheetBitmapCacheMissReason(pageFolder, pdfPath, rasterSheet, preferOverview: true);
                return false;
            }

            if (RasterSheetCacheService.TryReadOverviewReady(
                    pageFolder,
                    pdfPath,
                    rasterSheet,
                    out RasterSheetBitmapResult overview,
                    out string overviewReason))
            {
                TryPutRasterSheetBitmapCache(pageFolder, pdfPath, rasterSheet, preferOverview: true, overview);
                return ApplyRasterSheetBitmapRender(
                    pdfPath,
                    pdfIndex,
                    pageFolder,
                    rasterSheet,
                    overview,
                    restoreView,
                    fitAfter,
                    usingOverview: true);
            }

            skipReason = overviewReason;
            if (rasterSheet?.Enabled == true)
            {
                AppLog.Info(
                    $"Viewport raster sheet overview skipped; reason='{overviewReason}'; page='{pageFolder}'; " +
                    $"pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pdfIndex + 1}");
            }
            return false;
        }

        if (TryGetRasterSheetBitmapCache(
                pageFolder,
                pdfPath,
                rasterSheet,
                preferOverview: false,
                out RasterSheetBitmapResult cachedRaster))
        {
            return ApplyRasterSheetBitmapRender(
                pdfPath,
                pdfIndex,
                pageFolder,
                rasterSheet,
                cachedRaster,
                restoreView,
                fitAfter,
                usingOverview);
        }

        if (requireCachedBitmap)
        {
            skipReason = RasterSheetBitmapCacheMissReason(pageFolder, pdfPath, rasterSheet, preferOverview: false);
            return false;
        }

        if (!RasterSheetCacheService.TryReadReady(
                pageFolder,
                pdfPath,
                rasterSheet,
                out RasterSheetBitmapResult raster,
                out string reason))
        {
            skipReason = reason;
            if (rasterSheet?.Enabled == true)
            {
                AppLog.Info(
                    $"Viewport raster sheet skipped; reason='{reason}'; page='{pageFolder}'; " +
                    $"pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pdfIndex + 1}");
            }
            return false;
        }

        TryPutRasterSheetBitmapCache(pageFolder, pdfPath, rasterSheet, preferOverview: false, raster);
        return ApplyRasterSheetBitmapRender(
            pdfPath,
            pdfIndex,
            pageFolder,
            rasterSheet,
            raster,
            restoreView,
            fitAfter,
            usingOverview);
    }

    private static bool IsRasterSheetBitmapCacheWarmingReason(string reason) =>
        string.Equals(reason, RasterSheetBitmapCacheWarmingReason, StringComparison.OrdinalIgnoreCase);

    private static string RasterSheetBitmapCacheMissReason(
        string pageFolder,
        string pdfPath,
        RasterSheetSource? rasterSheet,
        bool preferOverview)
    {
        if (preferOverview)
        {
            if (!RasterSheetCacheService.HasSourceImageOverview(rasterSheet))
                return "overview image is missing";
            if (RasterSheetCacheService.ShouldBuildSourceImageOverview(
                    pageFolder,
                    pdfPath,
                    rasterSheet,
                    out string overviewReason))
            {
                return string.IsNullOrWhiteSpace(overviewReason)
                    ? "source image overview missing"
                    : overviewReason;
            }

            return RasterSheetBitmapCacheWarmingReason;
        }

        return RasterSheetCacheService.ShouldRebuildForReadableDisplay(
            pageFolder,
            pdfPath,
            rasterSheet,
            out string reason)
            ? reason
            : RasterSheetBitmapCacheWarmingReason;
    }

    private bool TryApplyRasterSheetRender(
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        RasterSheetSource? rasterSheet,
        ViewState? restoreView,
        bool fitAfter,
        out string skipReason) =>
        TryApplyRasterSheetRender(
            pdfPath,
            pdfIndex,
            pageFolder,
            rasterSheet,
            restoreView,
            fitAfter,
            preferOverview: false,
            out skipReason);

    private bool ApplyRasterSheetBitmapRender(
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        RasterSheetSource? rasterSheet,
        RasterSheetBitmapResult raster,
        ViewState? restoreView,
        bool fitAfter,
        bool usingOverview)
    {
        _pageBitmap?.Dispose();
        _pageBitmap = raster.Bitmap;
        MarkPageBitmapIdentity(pdfPath, pdfIndex, pageFolder);
        _pdfW = raster.WidthPt;
        _pdfH = raster.HeightPt;
        _bitmapScale = raster.BitmapScale;
        _layers = [];
        _usingLayerRenderer = false;
        _usingRasterSheetRender = true;
        _usingRasterSheetOverviewRender = usingOverview;
        QueueRasterSheetVisualSegmentsLoad(pageFolder, pdfPath, pdfIndex, rasterSheet);
        _renderedScale = raster.BitmapScale;
        _showingPreviousPageDuringSwitch = false;
        ClearDetailRenderBitmap();
        ApplyInitialPreviewView(restoreView, fitAfter);
        AppLog.Info(
            $"Viewport raster sheet {(usingOverview ? "overview " : "")}cache hit; page='{pageFolder}'; " +
            $"pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pdfIndex + 1}; scale={raster.BitmapScale:0.###}; " +
            $"image='{raster.ImagePath}'");
        ReportViewportRenderProfile(
            usingOverview ? "raster-sheet-overview" : "raster-sheet",
            pageFolder,
            pdfPath,
            pdfIndex,
            raster.BitmapScale,
            elapsedMs: 0,
            fromCache: true,
            clipRect: null);
        return true;
    }

    private bool TryApplyPersistedPreviewRender(
        string pdfPath,
        int pdfIndex,
        float renderScale,
        ViewState? restoreView,
        bool fitAfter,
        bool allowDiskRead = true,
        bool requestRepaint = true)
    {
        if (TryApplyPersistedPreviewRenderScale(pdfPath, pdfIndex, renderScale, restoreView, fitAfter, allowDiskRead, requestRepaint))
            return true;

        float fastScale = ViewportRenderPolicy.FastPageSwitchPreviewRenderScale;
        if (Math.Abs(renderScale - fastScale) > 0.001f &&
            TryApplyPersistedPreviewRenderScale(pdfPath, pdfIndex, fastScale, restoreView, fitAfter, allowDiskRead, requestRepaint))
        {
            return true;
        }

        return ShouldUseColdPageSwitchPreview(restoreView, fitAfter) &&
               TryApplyPersistedPreviewRenderScale(
                   pdfPath,
                   pdfIndex,
                    ViewportRenderPolicy.ColdPageSwitchPreviewRenderScale,
                    restoreView,
                    fitAfter,
                    allowDiskRead,
                    requestRepaint);
    }

    private static float PageSwitchLivePreviewScale(ViewState? restoreView, bool fitAfter) =>
        ShouldUseColdPageSwitchPreview(restoreView, fitAfter)
            ? ViewportRenderPolicy.ColdPageSwitchPreviewRenderScale
            : ViewportRenderPolicy.FastPageSwitchPreviewRenderScale;

    private static bool ShouldUseColdPageSwitchPreview(ViewState? restoreView, bool fitAfter)
    {
        if (fitAfter || !restoreView.HasValue)
            return true;

        return restoreView.Value.Zoom <= ViewportRenderPolicy.ColdPageSwitchPreviewRenderScale * 0.95f;
    }

    private bool TryApplyPersistedPreviewRenderScale(
        string pdfPath,
        int pdfIndex,
        float renderScale,
        ViewState? restoreView,
        bool fitAfter,
        bool allowDiskRead = true,
        bool requestRepaint = true)
    {
        string cacheKey = DocnetRenderCacheKey(pdfPath, pdfIndex, renderScale);
        if (PersistedPreviewBitmapCache.TryGet(cacheKey, out CachedBitmapRender cachedPreview))
        {
            ApplyPreviewBitmapRender(cachedPreview.Bitmap, cachedPreview.WidthPt, cachedPreview.HeightPt, cachedPreview.BitmapScale, restoreView, fitAfter);
            AppLog.Info(
                $"Viewport PyMuPDF preview memory cache hit; page='{_pageFolder}'; " +
                $"pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pdfIndex + 1}; scale={renderScale:0.###}");
            ReportViewportRenderProfile(
                "preview-memory",
                _pageFolder,
                pdfPath,
                pdfIndex,
                renderScale,
                elapsedMs: 0,
                fromCache: true,
                clipRect: null);
            if (requestRepaint)
                RequestRepaint();
            return true;
        }

        if (!allowDiskRead)
            return false;

        if (!TryReadPersistedPreviewBitmap(pdfPath, pdfIndex, renderScale, out CachedBitmapRender render))
            return false;

        ApplyPreviewBitmapRender(render.Bitmap, render.WidthPt, render.HeightPt, render.BitmapScale, restoreView, fitAfter);
        PersistedPreviewBitmapCache.Put(cacheKey, render.WidthPt, render.HeightPt, render.BitmapScale, render.Bitmap);
        DocnetRenderCache.Put(cacheKey, render.WidthPt, render.HeightPt, render.BitmapScale, render.Bitmap);
        AppLog.Info(
            $"Viewport PyMuPDF preview cache hit; page='{_pageFolder}'; " +
            $"pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pdfIndex + 1}; scale={renderScale:0.###}");
        ReportViewportRenderProfile(
            "preview",
            _pageFolder,
            pdfPath,
            pdfIndex,
            renderScale,
            elapsedMs: 0,
            fromCache: true,
            clipRect: null);
        if (requestRepaint)
            RequestRepaint();
        return true;
    }

    private void ApplyPreviewBitmapRender(
        SKBitmap bitmap,
        float widthPt,
        float heightPt,
        float bitmapScale,
        ViewState? restoreView,
        bool fitAfter)
    {
        _pageBitmap?.Dispose();
        _pageBitmap = bitmap;
        MarkPageBitmapIdentity(_pdfPath, _pdfIndex, _pageFolder);
        _pdfW = widthPt;
        _pdfH = heightPt;
        _bitmapScale = bitmapScale;
        _renderedScale = bitmapScale;
        _usingLayerRenderer = false;
        _usingRasterSheetRender = false;
        _usingRasterSheetOverviewRender = false;
        ClearRasterSheetVisualSegments();
        _showingPreviousPageDuringSwitch = false;
        ClearDetailRenderBitmap();
        ApplyInitialPreviewView(restoreView, fitAfter);
    }

    private bool TryApplyPersistedCleanLayerRender(LayerRenderRequest request)
    {
        if (!PdfPreviewRenderCache.IsCleanRenderRequest(
                request.PdfPath,
                request.PdfIndex,
                request.RenderScale,
                request.LayerStates,
                request.HighlightedLayers))
        {
            return false;
        }
        if (!PdfPreviewRenderCache.TryReadCleanRender(
                request.PdfPath,
                request.PdfIndex,
                request.RenderScale,
                out PdfLayerRenderResult render))
        {
            return false;
        }
        if (!render.LayersCaptured && _cachedLayers == null)
            return false;

        var bitmap = SKBitmap.Decode(render.ImageBytes);
        if (bitmap == null)
            return false;

        IReadOnlyList<PdfLayerInfo>? capturedLayers = render.LayersCaptured
            ? render.Layers
                .Select(layer => new PdfLayerInfo { Number = layer.Number, Name = layer.Name, IsOn = layer.IsOn })
                .ToList()
            : null;

        if (_cachedLayers == null && capturedLayers != null)
        {
            _cachedLayers = capturedLayers;
            PdfLayersDiscovered?.Invoke(capturedLayers);
        }

        _pageBitmap?.Dispose();
        _pageBitmap = bitmap;
        MarkPageBitmapIdentity(request.PdfPath, request.PdfIndex, request.PageFolder);
        _pdfW = render.WidthPt;
        _pdfH = render.HeightPt;
        _bitmapScale = _pdfW > 0 ? _pageBitmap.Width / _pdfW : request.RenderScale;
        _renderedScale = _bitmapScale;
        if (request.ResetLayerStates)
        {
            _layerStates.Clear();
            IEnumerable<PdfLayerInfo> resetLayers = capturedLayers ?? _cachedLayers ?? Array.Empty<PdfLayerInfo>();
            foreach (PdfLayerInfo layer in resetLayers)
                _layerStates[layer.Number] = layer.IsOn;
        }

        if (render.LayersCaptured)
        {
            _pdfLayersLoadedForPage = true;
            UpdateLayerSnapshot(render.Layers);
        }
        else if (_cachedLayers != null)
        {
            _pdfLayersLoadedForPage = true;
            UpdateLayerSnapshot(_cachedLayers
                .Select(layer => new PdfLayer(layer.Number, layer.Name, layer.IsOn)));
        }

        if (_pdfSnapEnabled && request.ResetLayerStates)
            QueuePdfSnapPointLoad(force: true);
        _usingLayerRenderer = true;
        _usingRasterSheetRender = false;
        _usingRasterSheetOverviewRender = false;
        ClearRasterSheetVisualSegments();
        _pendingDocnetRender = null;
        _docnetRenderVersion++;
        _showingPreviousPageDuringSwitch = false;
        ClearDetailRenderBitmap();
        ApplyLayerRenderContinuation(request);
        QueueDetailRenderIfNeeded(force: ShouldForceDetailAfterLayerApply(request));
        if (request.FireLayersAfter)
            FireLayersChanged();
        if (!string.IsNullOrWhiteSpace(request.StatusAfter))
            PostStatus(request.StatusAfter);
        CacheLayerBitmapRender(request);
        AppLog.Info(
            $"Viewport PyMuPDF render cache hit; page='{request.PageFolder}'; " +
            $"pdf='{Path.GetFileName(request.PdfPath)}'; pdfPage={request.PdfIndex + 1}; scale={request.RenderScale:0.###}");
        ReportViewportRenderProfile(
            "layer",
            request.PageFolder,
            request.PdfPath,
            request.PdfIndex,
            request.RenderScale,
            elapsedMs: 0,
            fromCache: true,
            clipRect: null);
        RequestRepaint();
        return true;
    }

    private bool TryApplyLayerBitmapCache(LayerRenderRequest request, out bool exactHit)
    {
        exactHit = true;
        if (!LayerBitmapCache.TryGet(LayerRenderBitmapCacheKey(request), out CachedLayerBitmapRender cached))
        {
            exactHit = false;
            if (request.RenderScale <= ViewportRenderPolicy.ResponsiveMinRenderScale * 1.05f ||
                !LayerBitmapCache.TryGetBest(
                    LayerRenderBitmapCacheSignature(request),
                    request.RenderScale,
                    out cached))
            {
                return false;
            }
        }

        if (ShouldKeepHighScaleLayerBitmapInCache(request, cached.BitmapScale))
        {
            ApplyLayerRenderMetadataOnly(request, cached.WidthPt, cached.HeightPt, cached.Layers);
            cached.Bitmap.Dispose();
            ReportViewportRenderProfile(
                exactHit ? "layer-memory-deferred" : "layer-memory-best-deferred",
                request.PageFolder,
                request.PdfPath,
                request.PdfIndex,
                request.RenderScale,
                elapsedMs: 0,
                fromCache: true,
                clipRect: null);
            RequestRepaint();
            return true;
        }

        _pageBitmap?.Dispose();
        _pageBitmap = cached.Bitmap;
        MarkPageBitmapIdentity(request.PdfPath, request.PdfIndex, request.PageFolder);
        _pdfW = cached.WidthPt;
        _pdfH = cached.HeightPt;
        _bitmapScale = cached.BitmapScale;
        _renderedScale = cached.BitmapScale;
        _usingLayerRenderer = true;
        _usingRasterSheetRender = false;
        _usingRasterSheetOverviewRender = false;
        ClearRasterSheetVisualSegments();
        _pendingDocnetRender = null;
        _docnetRenderVersion++;
        _showingPreviousPageDuringSwitch = false;
        ClearDetailRenderBitmap();

        if (request.ResetLayerStates)
        {
            _layerStates.Clear();
            foreach (PdfLayer layer in cached.Layers)
                _layerStates[layer.Number] = layer.IsOn;
        }

        UpdateLayerSnapshot(cached.Layers);
        _pdfLayersLoadedForPage = true;
        if (_cachedLayers == null)
        {
            _cachedLayers = cached.Layers
                .Select(layer => new PdfLayerInfo { Number = layer.Number, Name = layer.Name, IsOn = layer.IsOn })
                .ToList();
            PdfLayersDiscovered?.Invoke(_cachedLayers);
        }

        if (_pdfSnapEnabled && request.ResetLayerStates)
            QueuePdfSnapPointLoad(force: true);
        ApplyLayerRenderContinuation(request);
        QueueDetailRenderIfNeeded(force: ShouldForceDetailAfterLayerApply(request));
        if (request.FireLayersAfter)
            FireLayersChanged();
        if (!string.IsNullOrWhiteSpace(request.StatusAfter))
            PostStatus(request.StatusAfter);
        ReportViewportRenderProfile(
            exactHit ? "layer-memory" : "layer-memory-best",
            request.PageFolder,
            request.PdfPath,
            request.PdfIndex,
            request.RenderScale,
            elapsedMs: 0,
            fromCache: true,
            clipRect: null);
        RequestRepaint();
        return true;
    }

    private bool ShouldKeepHighScaleLayerBitmapInCache(LayerRenderRequest request, float bitmapScale)
    {
        if (request.RenderScale < 3.0f ||
            bitmapScale < 3.0f ||
            _zoom < ViewportRenderPolicy.DetailRenderMinZoom ||
            _pageBitmap == null ||
            _bitmapScale <= 0)
        {
            return false;
        }

        bool currentBitmapSharpEnough = _bitmapScale >= Math.Min(_zoom, bitmapScale) * 0.90f;
        return currentBitmapSharpEnough || ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale);
    }

    private void ApplyLayerRenderMetadataOnly(
        LayerRenderRequest request,
        float widthPt,
        float heightPt,
        IReadOnlyList<PdfLayer> layers)
    {
        _pdfW = widthPt;
        _pdfH = heightPt;
        _renderedScale = Math.Max(_renderedScale, request.RenderScale);
        _usingLayerRenderer = true;
        _usingRasterSheetRender = false;
        _usingRasterSheetOverviewRender = false;
        ClearRasterSheetVisualSegments();
        _pendingDocnetRender = null;
        _docnetRenderVersion++;
        _showingPreviousPageDuringSwitch = false;

        if (request.ResetLayerStates)
        {
            _layerStates.Clear();
            foreach (PdfLayer layer in layers)
                _layerStates[layer.Number] = layer.IsOn;
        }

        UpdateLayerSnapshot(layers);
        _pdfLayersLoadedForPage = true;
        if (_cachedLayers == null)
        {
            _cachedLayers = layers
                .Select(layer => new PdfLayerInfo { Number = layer.Number, Name = layer.Name, IsOn = layer.IsOn })
                .ToList();
            PdfLayersDiscovered?.Invoke(_cachedLayers);
        }

        if (_pdfSnapEnabled && request.ResetLayerStates)
            QueuePdfSnapPointLoad(force: true);
        ApplyLayerRenderContinuation(request);
        QueueDetailRenderIfNeeded(force: ShouldForceDetailAfterLayerApply(request));
        if (request.FireLayersAfter)
            FireLayersChanged();
        if (!string.IsNullOrWhiteSpace(request.StatusAfter))
            PostStatus(request.StatusAfter);
    }

    private void ApplyInitialPreviewView(ViewState? restoreView, bool fitAfter)
    {
        if (restoreView.HasValue)
        {
            _zoom = Math.Clamp(restoreView.Value.Zoom, ZoomMin, ZoomMax);
            _panX = restoreView.Value.PanX;
            _panY = restoreView.Value.PanY;
            ClampPanToPage();
            return;
        }

        if (!fitAfter || _pdfW <= 0 || ViewportCanvasWidth < 2 || ViewportCanvasHeight < 2)
            return;

        _zoom = Math.Min(ViewportCanvasWidth / _pdfW, ViewportCanvasHeight / _pdfH) * 0.95f;
        _panX = 0;
        _panY = 0;
    }

    private void QueueDocnetRender(
        float renderScale,
        ViewState? restoreView = null,
        bool fitAfter = false,
        bool queueLayerAfter = false,
        bool resetLayerStates = false,
        string? statusAfter = null,
        bool fireLayersAfter = false)
    {
        if (string.IsNullOrWhiteSpace(_pdfPath))
            return;

        float scale = Math.Clamp(renderScale, 0.10f, 4.0f);
        if (ShouldSkipQueuedFullPageSharpUpgradeAtLowZoom(scale))
            return;

        if (IsPreviewRenderScale(scale))
            PausePreviewPrefetchFor(ViewportRenderPolicy.PreviewPrefetchNavigationQuietMs);

        int version = ++_docnetRenderVersion;
        var request = new DocnetRenderRequest(
            version,
            _pdfPath,
            _pdfIndex,
            _pageFolder,
            scale,
            LayerRenderCachedLayers(),
            restoreView,
            fitAfter,
            queueLayerAfter,
            resetLayerStates,
            statusAfter,
            fireLayersAfter);

        if (IsFastPreviewRenderScale(scale))
        {
            _pendingDocnetRender = null;
            _ = StartFastPreviewRenderAsync(request);
            return;
        }

        _pendingDocnetRender = request;
        _ = StartNextDocnetRenderAsync();
    }

    private async Task StartFastPreviewRenderAsync(DocnetRenderRequest request)
    {
        try
        {
            Stopwatch renderWatch = Stopwatch.StartNew();
            string cacheKey = DocnetRenderCacheKey(request.PdfPath, request.PdfIndex, request.RenderScale);
            bool fromCache = DocnetRenderCache.TryGet(cacheKey, out CachedBitmapRender cached);
            DocnetRenderResult? render = null;
            bool usedFastPreviewRenderer = false;
            if (!fromCache)
            {
                await Task.Delay(ViewportRenderPolicy.FastPageSwitchPreviewCoalesceMs);
                if (!IsCurrentPageDocnetRenderTarget(request.PdfPath, request.PdfIndex, request.PageFolder, request.Version))
                    return;

                await LivePreviewRenderSemaphore.WaitAsync();
                try
                {
                    if (!IsCurrentPageDocnetRenderTarget(request.PdfPath, request.PdfIndex, request.PageFolder, request.Version))
                        return;

                    fromCache = DocnetRenderCache.TryGet(cacheKey, out cached);
                    if (!fromCache)
                    {
                        PausePreviewPrefetchFor(ViewportRenderPolicy.PreviewPrefetchActiveRenderHoldMs);
                        (render, usedFastPreviewRenderer) = await TryRenderFastPreviewForPageSwitchAsync(request);
                        if (render == null)
                            return;

                        DocnetRenderCache.Put(cacheKey, render);
                        TryWriteDocnetPreviewCache(request, render);
                        PausePreviewPrefetchFor(ViewportRenderPolicy.PreviewPrefetchAfterActiveRenderHoldMs);
                    }
                }
                finally
                {
                    LivePreviewRenderSemaphore.Release();
                }
            }
            renderWatch.Stop();
            ReportSlowPdfRender(
                usedFastPreviewRenderer ? "preview-pymupdf" : "docnet",
                request,
                renderWatch.ElapsedMilliseconds,
                fromCache);

            if (request.Version == _docnetRenderVersion &&
                string.Equals(request.PdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
                request.PdfIndex == _pdfIndex &&
                string.Equals(request.PageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase))
            {
                if (ShouldSkipLowerQualityDocnetPreview(request))
                {
                    if (fromCache)
                        cached.Bitmap.Dispose();
                    else
                        render?.Bitmap.Dispose();
                }
                else if (fromCache)
                {
                    ApplyCachedBitmapRender(cached);
                }
                else if (render != null)
                {
                    ApplyDocnetRenderResult(render);
                }

                ApplyDocnetRenderContinuation(request);
                QueueDetailRenderIfNeeded(force: false);
                RequestRepaint();
            }
            else if (render != null)
            {
                render.Bitmap.Dispose();
            }
            else if (fromCache)
            {
                cached.Bitmap.Dispose();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Fast PDF preview render failed.");
            if (request.Version == _docnetRenderVersion)
            {
                _showingPreviousPageDuringSwitch = false;
                RequestRepaint();
                PostStatus($"Render error: {ex.Message}");
            }
        }
    }

    private async Task StartNextDocnetRenderAsync()
    {
        if (_docnetRenderInProgress || _pendingDocnetRender == null)
            return;

        DocnetRenderRequest request = _pendingDocnetRender;
        _pendingDocnetRender = null;
        _docnetRenderInProgress = true;
        try
        {
            Stopwatch renderWatch = Stopwatch.StartNew();
            string cacheKey = DocnetRenderCacheKey(request.PdfPath, request.PdfIndex, request.RenderScale);
            bool fromCache = DocnetRenderCache.TryGet(cacheKey, out CachedBitmapRender cached);
            DocnetRenderResult? render = null;
            bool usedFastPreviewRenderer = false;
            if (!fromCache)
            {
                if (IsPreviewRenderScale(request.RenderScale) && !IsFastPreviewRenderScale(request.RenderScale))
                {
                    await WaitForPreviewPrefetchQuietWindowAsync().ConfigureAwait(false);
                    if (!IsCurrentPageDocnetRenderTarget(request.PdfPath, request.PdfIndex, request.PageFolder, request.Version))
                        return;

                    if (ShouldSkipQueuedNonFastPreviewForCurrentView(request.RenderScale))
                        return;
                }

                if (ShouldSkipQueuedFullPageSharpUpgradeAtLowZoom(request.RenderScale))
                    return;

                if (IsPreviewRenderScale(request.RenderScale))
                    PausePreviewPrefetchFor(ViewportRenderPolicy.PreviewPrefetchActiveRenderHoldMs);

                render = await TryRenderPreviewWithPyMuPdfAsync(request);
                usedFastPreviewRenderer = render != null;
                render ??= await Task.Run(() =>
                    RenderPageBitmapWithDocnet(request.PdfPath, request.PdfIndex, request.RenderScale));
                DocnetRenderCache.Put(cacheKey, render);
                TryWriteDocnetPreviewCache(request, render);
                if (IsPreviewRenderScale(request.RenderScale))
                    PausePreviewPrefetchFor(ViewportRenderPolicy.PreviewPrefetchAfterActiveRenderHoldMs);
            }
            renderWatch.Stop();
            ReportSlowPdfRender(
                usedFastPreviewRenderer ? "preview-pymupdf" : "docnet",
                request,
                renderWatch.ElapsedMilliseconds,
                fromCache);

            if (request.Version == _docnetRenderVersion &&
                string.Equals(request.PdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
                request.PdfIndex == _pdfIndex &&
                string.Equals(request.PageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase))
            {
                if (ShouldSkipLowerQualityDocnetPreview(request))
                {
                    if (fromCache)
                        cached.Bitmap.Dispose();
                    else if (render != null)
                        render.Bitmap.Dispose();
                    AppLog.Info(
                        $"Viewport skipped lower-quality Docnet preview; page='{request.PageFolder}'; " +
                        $"pdf='{Path.GetFileName(request.PdfPath)}'; pdfPage={request.PdfIndex + 1}; " +
                        $"currentScale={_bitmapScale:0.###}; targetScale={request.RenderScale:0.###}");
                }
                else if (fromCache)
                {
                    ApplyCachedBitmapRender(cached);
                }
                else if (render != null)
                {
                    ApplyDocnetRenderResult(render);
                }

                ApplyDocnetRenderContinuation(request);
                QueueDetailRenderIfNeeded(force: false);
                RequestRepaint();
            }
            else if (render != null)
            {
                render.Bitmap.Dispose();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "PDF render failed.");
            if (request.Version == _docnetRenderVersion)
            {
                _showingPreviousPageDuringSwitch = false;
                RequestRepaint();
                PostStatus($"Render error: {ex.Message}");
            }
        }
        finally
        {
            _docnetRenderInProgress = false;
            if (_pendingDocnetRender != null)
                _ = StartNextDocnetRenderAsync();
        }
    }

    private void ApplyDocnetRenderContinuation(DocnetRenderRequest request)
    {
        if (request.RestoreView.HasValue)
            RestoreViewState(request.RestoreView.Value);
        else if (request.FitAfter)
            ZoomFit();

        if (request.QueueLayerAfter)
        {
            QueueInitialLayerDiscoveryOrRender(
                request.ResetLayerStates,
                CurrentRenderScale(),
                request.StatusAfter,
                request.FireLayersAfter,
                allowImmediateCache: false,
                allowLiveRender: false,
                allowMemoryBitmap: true);
            QueueSharpLayerRenderAfterPreview(
                request.PdfPath,
                request.PdfIndex,
                request.PageFolder,
                request.ResetLayerStates,
                request.StatusAfter,
                request.FireLayersAfter);
            return;
        }

        if (IsFastPreviewRenderScale(request.RenderScale))
            QueueSharpBaseRenderAfterPreview(request.PdfPath, request.PdfIndex, request.PageFolder);

        if (!string.IsNullOrWhiteSpace(request.StatusAfter))
            PostStatus(request.StatusAfter);
    }

    private bool ShouldSkipLowerQualityDocnetPreview(DocnetRenderRequest request)
    {
        if (!IsPageBitmapFor(request.PdfPath, request.PdfIndex, request.PageFolder) ||
            _pageBitmap == null ||
            _bitmapScale <= request.RenderScale * 1.05f)
        {
            return false;
        }

        return !ViewportRenderPolicy.ShouldPreferLowerScalePageBitmapForNavigation(
            _zoom,
            _bitmapScale,
            request.RenderScale);
    }

    private static bool IsFastPreviewRenderScale(float renderScale) =>
        Math.Abs(renderScale - ViewportRenderPolicy.FastPageSwitchPreviewRenderScale) <= 0.001f ||
        Math.Abs(renderScale - ViewportRenderPolicy.ColdPageSwitchPreviewRenderScale) <= 0.001f;

    private static bool IsPreviewRenderScale(float renderScale) =>
        IsFastPreviewRenderScale(renderScale) ||
        Math.Abs(renderScale - ViewportRenderPolicy.InitialPagePreviewRenderScale) <= 0.001f;

    private static async Task<(DocnetRenderResult? Render, bool UsedPyMuPdf)> TryRenderFastPreviewForPageSwitchAsync(DocnetRenderRequest request)
    {
        DocnetRenderResult? docnet = await TryRenderPreviewWithDocnetAsync(request);
        if (docnet != null)
            return (docnet, false);

        DocnetRenderResult? pymupdf = await TryRenderPreviewWithPyMuPdfAsync(request);
        return (pymupdf, pymupdf != null);
    }

    private static async Task<DocnetRenderResult?> TryRenderPreviewWithDocnetAsync(DocnetRenderRequest request)
    {
        if (!IsFastPreviewRenderScale(request.RenderScale))
            return null;

        try
        {
            return await Task.Run(() =>
                RenderPageBitmapWithDocnet(request.PdfPath, request.PdfIndex, request.RenderScale));
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport fast Docnet preview failed for {Path.GetFileName(request.PdfPath)} page {request.PdfIndex + 1}");
            return null;
        }
    }

    private static async Task<DocnetRenderResult?> TryRenderPreviewWithPyMuPdfAsync(DocnetRenderRequest request)
    {
        if (!IsPreviewRenderScale(request.RenderScale))
            return null;

        try
        {
            var renderResult = await PdfLayerRenderService.TryRenderAsync(
                request.PdfPath,
                request.PdfIndex,
                request.RenderScale,
                EmptyLayerStates,
                EmptyHighlightedLayers,
                request.CachedLayers);
            if (!renderResult.Ok)
            {
                if (!string.IsNullOrWhiteSpace(renderResult.Error))
                    AppLog.Warn($"Viewport fast PyMuPDF preview unavailable: {renderResult.Error}");
                return null;
            }

            SKBitmap? bitmap = await Task.Run(() => DecodePdfLayerRenderBitmap(renderResult.Result));
            if (bitmap == null ||
                renderResult.Result.WidthPt <= 0 ||
                renderResult.Result.HeightPt <= 0)
            {
                bitmap?.Dispose();
                return null;
            }

            float bitmapScale = renderResult.Result.WidthPt > 0
                ? bitmap.Width / renderResult.Result.WidthPt
                : request.RenderScale;
            return new DocnetRenderResult(
                renderResult.Result.WidthPt,
                renderResult.Result.HeightPt,
                bitmapScale,
                bitmap);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport fast PyMuPDF preview failed for {Path.GetFileName(request.PdfPath)} page {request.PdfIndex + 1}");
            return null;
        }
    }

    private void MarkPageBitmapIdentity(string pdfPath, int pdfIndex, string pageFolder)
    {
        _pageBitmapPdfPath = pdfPath;
        _pageBitmapPdfIndex = pdfIndex;
        _pageBitmapPageFolder = pageFolder;
        _pageBitmapGeneration++;
        _pagePaintGeneration = 0;
        _pagePaintedPageFolder = "";
    }

    private void ClearPageBitmapIdentity()
    {
        _pageBitmapPdfPath = "";
        _pageBitmapPdfIndex = -1;
        _pageBitmapPageFolder = "";
        _pageBitmapGeneration++;
        _pagePaintGeneration = 0;
        _pagePaintedPageFolder = "";
    }

    private void MarkCurrentPagePainted()
    {
        if (_pageBitmap == null ||
            _showingPreviousPageDuringSwitch ||
            _pageBitmapGeneration <= 0)
        {
            return;
        }

        _pagePaintGeneration = _pageBitmapGeneration;
        _pagePaintedPageFolder = _pageFolder;
    }

    private bool IsPageBitmapFor(string pdfPath, int pdfIndex, string pageFolder) =>
        _pageBitmap != null &&
        _pageBitmapPdfIndex == pdfIndex &&
        string.Equals(_pageBitmapPdfPath, pdfPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(_pageBitmapPageFolder, pageFolder, StringComparison.OrdinalIgnoreCase);

    private void QueueSharpLayerRenderAfterPreview(
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        bool resetLayerStates,
        string? statusAfter,
        bool fireLayersAfter,
        int deferralCount = 0)
    {
        int layerVersion = _layerRenderVersion;
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(async () =>
            {
                try
                {
                    await Task.Delay(ViewportRenderPolicy.PageSwitchSharpUpgradeDelayMs);
                    if (!IsCurrentPageRenderTarget(pdfPath, pdfIndex, pageFolder, layerVersion))
                        return;

                    if (ShouldDelaySharpLayerUpgrade(deferralCount))
                    {
                        QueueSharpLayerRenderAfterPreview(
                            pdfPath,
                            pdfIndex,
                            pageFolder,
                            resetLayerStates,
                            statusAfter,
                            fireLayersAfter,
                            deferralCount + 1);
                        return;
                    }

                    if (ShouldUseDetailRenderForSharpUpgrade())
                    {
                        QueueDetailRenderIfNeeded(force: false, immediate: true);
                        return;
                    }

                    if (ShouldSkipSharpLayerUpgradeForLowZoom())
                        return;

                    float renderScale = CurrentPostPreviewBaseRenderScale();
                    if (_bitmapScale >= renderScale * 0.95f)
                    {
                        QueueDetailRenderIfNeeded(force: false, immediate: true);
                        return;
                    }

                    QueueLayerRender(
                        resetLayerStates,
                        renderScale,
                        statusAfter,
                        fireLayersAfter,
                        allowImmediateCache: true,
                        allowLiveRender: true,
                        allowMemoryBitmap: true);
                }
                catch (Exception ex)
                {
                    AppLog.Warn(ex, "Viewport sharp page upgrade failed.");
                }
            }));
    }

    private void QueueSharpBaseRenderAfterPreview(
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        int deferralCount = 0)
    {
        int docnetVersion = _docnetRenderVersion;
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(async () =>
            {
                try
                {
                    await Task.Delay(ViewportRenderPolicy.PageSwitchSharpUpgradeDelayMs);
                    if (!IsCurrentPageDocnetRenderTarget(pdfPath, pdfIndex, pageFolder, docnetVersion) ||
                        _pdfLayersLoadedForPage)
                    {
                        return;
                    }

                    if (ShouldDelaySharpLayerUpgrade(deferralCount))
                    {
                        QueueSharpBaseRenderAfterPreview(
                            pdfPath,
                            pdfIndex,
                            pageFolder,
                            deferralCount + 1);
                        return;
                    }

                    if (ShouldUseDetailRenderForSharpUpgrade())
                    {
                        QueueDetailRenderIfNeeded(force: false, immediate: true);
                        return;
                    }

                    if (ShouldSkipSharpLayerUpgradeForLowZoom())
                        return;

                    float renderScale = CurrentPostPreviewBaseRenderScale();
                    if (_bitmapScale >= renderScale * 0.95f)
                    {
                        QueueDetailRenderIfNeeded(force: false, immediate: true);
                        return;
                    }

                    QueueDocnetRender(renderScale);
                }
                catch (Exception ex)
                {
                    AppLog.Warn(ex, "Viewport sharp base page upgrade failed.");
                }
            }));
    }

    private bool ShouldDelaySharpLayerUpgrade(int deferralCount)
    {
        if (deferralCount >= ViewportRenderPolicy.PageSwitchSharpUpgradeMaxDeferrals)
            return false;

        if (_isFastNavigating)
            return true;

        double idleMs = (DateTime.UtcNow - _lastFastNavigationAt).TotalMilliseconds;
        return idleMs >= 0 && idleMs < ViewportRenderPolicy.PageSwitchSharpUpgradeIdleMs;
    }

    private bool ShouldUseDetailRenderForSharpUpgrade() =>
        ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale);

    private float CurrentPostPreviewBaseRenderScale()
    {
        float renderScale = CurrentBaseRenderScale();
        if (_zoom < ViewportRenderPolicy.FarZoomFastFrameThreshold)
            return Math.Max(renderScale, ViewportRenderPolicy.InitialPagePreviewRenderScale);

        return Math.Max(renderScale, ViewportRenderPolicy.ResponsiveMinRenderScale);
    }

    private bool ShouldSkipSharpLayerUpgradeForLowZoom()
    {
        if (_zoom >= ViewportRenderPolicy.PageSwitchSharpUpgradeMinZoom)
            return false;

        float renderScale = CurrentPostPreviewBaseRenderScale();
        if (ShouldSkipQueuedFullPageSharpUpgradeAtLowZoom(renderScale))
            return true;

        if (ViewportRenderPolicy.ShouldPreferLowerScalePageBitmapForNavigation(
                _zoom,
                _bitmapScale,
                renderScale))
        {
            return false;
        }

        return _bitmapScale >= renderScale * 0.95f;
    }

    private bool ShouldSkipQueuedFullPageSharpUpgradeAtLowZoom(float renderScale)
    {
        if (ViewportRenderPolicy.ShouldSkipFullPageSharpUpgradeAtLowZoom(_zoom, _bitmapScale, renderScale))
            return true;

        return _zoom < ViewportRenderPolicy.PageSwitchSharpUpgradeMinZoom &&
               _bitmapScale <= 0 &&
               renderScale > ViewportRenderPolicy.FastPageSwitchPreviewRenderScale * 1.05f;
    }

    private bool ShouldSkipQueuedNonFastPreviewForCurrentView(float renderScale)
    {
        if (!IsPreviewRenderScale(renderScale) ||
            IsFastPreviewRenderScale(renderScale))
        {
            return false;
        }

        if (_usingRasterSheetRender || _usingRasterSheetOverviewRender)
            return true;

        if (_zoom < ViewportRenderPolicy.FarZoomFastFrameThreshold &&
            _bitmapScale >= ViewportRenderPolicy.FastPageSwitchPreviewRenderScale * 0.95f)
        {
            return true;
        }

        if (_bitmapScale >= renderScale * 0.95f)
            return true;

        if (ShouldUseDetailRenderForSharpUpgrade())
        {
            QueueDetailRenderIfNeeded(force: false, immediate: true);
            return true;
        }

        return false;
    }

    private bool IsCurrentPageRenderTarget(
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        int layerVersion) =>
        layerVersion == _layerRenderVersion &&
        string.Equals(pdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
        pdfIndex == _pdfIndex &&
        string.Equals(pageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase);

    private bool IsCurrentPageDocnetRenderTarget(
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        int docnetVersion) =>
        docnetVersion == _docnetRenderVersion &&
        string.Equals(pdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
        pdfIndex == _pdfIndex &&
        string.Equals(pageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase);

    private void ReportSlowPdfRender(string kind, DocnetRenderRequest request, long elapsedMs, bool fromCache)
    {
        ReportViewportRenderProfile(
            kind,
            request.PageFolder,
            request.PdfPath,
            request.PdfIndex,
            request.RenderScale,
            elapsedMs,
            fromCache,
            clipRect: null);

        if (fromCache || elapsedMs < ViewportRenderPolicy.SlowRenderLogMs)
            return;

        DateTime now = DateTime.UtcNow;
        if ((now - _lastSlowRenderLogAt).TotalSeconds < 2)
            return;

        _lastSlowRenderLogAt = now;
        AppLog.Info(
            $"Viewport slow {kind} render {elapsedMs}ms; page='{request.PageFolder}'; " +
            $"pdf='{Path.GetFileName(request.PdfPath)}'; pdfPage={request.PdfIndex + 1}; scale={request.RenderScale:0.###}");
    }

    private bool RenderPageWithLayers(bool resetLayerStates, float renderScale)
    {
        if (!PdfLayerRenderService.TryRender(
                _pdfPath,
                _pdfIndex,
                Math.Clamp(renderScale, 0.20f, 4.0f),
                _layerStates,
                _highlightedLayers,
                _cachedLayers,
                out PdfLayerRenderResult render,
                out string error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                PostStatus($"Layer renderer unavailable: {error}");
            return false;
        }

        return ApplyLayerRenderResult(render, resetLayerStates);
    }

    private static void TryWriteDocnetPreviewCache(DocnetRenderRequest request, DocnetRenderResult render)
    {
        if (!IsPreviewRenderScale(request.RenderScale) ||
            render.WidthPt <= 0 ||
            render.HeightPt <= 0 ||
            render.Bitmap.Width <= 0 ||
            render.Bitmap.Height <= 0)
        {
            return;
        }

        try
        {
            using SKImage image = SKImage.FromBitmap(render.Bitmap);
            using SKData? data = image.Encode(SKEncodedImageFormat.Png, 85);
            if (data == null || data.Size <= 0)
                return;

            var result = new PdfLayerRenderResult
            {
                ImageBytes = data.ToArray(),
                WidthPt = render.WidthPt,
                HeightPt = render.HeightPt,
                Layers = [],
                LayersCaptured = false,
            };
            if (IsFastPreviewRenderScale(request.RenderScale))
                PdfPreviewRenderCache.TryWriteCleanPreview(request.PdfPath, request.PdfIndex, request.RenderScale, result);
            else
                PdfPreviewRenderCache.TryWriteCleanRender(request.PdfPath, request.PdfIndex, request.RenderScale, result);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport fast Docnet preview cache write failed for {Path.GetFileName(request.PdfPath)} page {request.PdfIndex + 1}");
        }
    }

    private bool ApplyLayerRenderResult(
        PdfLayerRenderResult render,
        bool resetLayerStates,
        SKBitmap? decodedBitmap = null)
    {
        var bitmap = decodedBitmap ?? DecodePdfLayerRenderBitmap(render);
        if (bitmap == null)
        {
            PostStatus("Layer renderer returned an unreadable image.");
            return false;
        }

        _pageBitmap?.Dispose();
        _pageBitmap = bitmap;
        MarkPageBitmapIdentity(_pdfPath, _pdfIndex, _pageFolder);
        _pdfW = render.WidthPt;
        _pdfH = render.HeightPt;
        _bitmapScale = _pdfW > 0 ? _pageBitmap.Width / _pdfW : RenderDpi / 72f;
        _renderedScale = _bitmapScale;
        if (_cachedLayers == null)
        {
            _cachedLayers = render.Layers
                .Select(layer => new PdfLayerInfo { Number = layer.Number, Name = layer.Name, IsOn = layer.IsOn })
                .ToList();
            PdfLayersDiscovered?.Invoke(_cachedLayers);
        }

        if (resetLayerStates)
        {
            _layerStates.Clear();
            foreach (var layer in render.Layers)
                _layerStates[layer.Number] = layer.IsOn;
        }

        UpdateLayerSnapshot(render.Layers);
        if (_pdfSnapEnabled && resetLayerStates)
            QueuePdfSnapPointLoad(force: true);
        _usingLayerRenderer = true;
        _usingRasterSheetRender = false;
        _usingRasterSheetOverviewRender = false;
        ClearRasterSheetVisualSegments();
        _pendingDocnetRender = null;
        _docnetRenderVersion++;
        _showingPreviousPageDuringSwitch = false;
        ClearDetailRenderBitmap();
        RequestRepaint();
        return true;
    }

    private void QueueLayerRender(
        bool resetLayerStates,
        float renderScale,
        string? statusAfter = null,
        bool fireLayersAfter = false,
        ViewState? restoreView = null,
        bool fitAfter = false,
        bool allowImmediateCache = true,
        bool allowLiveRender = true,
        bool allowMemoryBitmap = true)
    {
        if (string.IsNullOrWhiteSpace(_pdfPath))
            return;

        int version = ++_layerRenderVersion;
        LayerRenderRequest request = new(
            version,
            _pdfPath,
            _pdfIndex,
            _pageFolder,
            Math.Clamp(renderScale, 0.20f, 4.0f),
            resetLayerStates,
            EffectiveLayerStates(),
            EffectiveHighlightedLayers(),
            LayerRenderCachedLayers(),
            restoreView,
            fitAfter,
            statusAfter,
            fireLayersAfter);

        bool preserveDetailDuringZoomRefresh = ShouldPreserveDetailDuringLayerRender(request);
        if (!preserveDetailDuringZoomRefresh)
            ClearDetailRender();

        if (allowMemoryBitmap && TryApplyLayerBitmapCache(request, out bool exactLayerCacheHit))
        {
            if (exactLayerCacheHit)
            {
                _pendingLayerRender = null;
                return;
            }
        }

        if (allowImmediateCache && TryApplyPersistedCleanLayerRender(request))
        {
            _pendingLayerRender = null;
            return;
        }

        if (allowLiveRender && ShouldUseCacheOnlyForAutomaticLayerRender(request))
        {
            CompleteCacheOnlyLayerRender(request);
            return;
        }

        if (!allowLiveRender)
        {
            CompleteCacheOnlyLayerRender(request);
            return;
        }

        _pendingLayerRender = request;
        _ = StartNextLayerRenderAsync();
    }

    private void CompleteCacheOnlyLayerRender(LayerRenderRequest request)
    {
        bool automaticViewportRender = IsAutomaticViewportLayerRender(request);
        bool forceDetail = ShouldForceDetailAfterLayerApply(request);
        ApplyLayerRenderContinuation(request);

        IReadOnlyList<PdfLayerInfo>? cachedLayers = request.CachedLayers ?? _cachedLayers;
        if (request.ResetLayerStates)
        {
            _layerStates.Clear();
            if (cachedLayers != null)
            {
                foreach (PdfLayerInfo layer in cachedLayers)
                    _layerStates[layer.Number] = layer.IsOn;
            }
        }

        if (cachedLayers != null)
        {
            if (_cachedLayers == null)
                _cachedLayers = cachedLayers;

            UpdateLayerSnapshot(cachedLayers.Select(layer =>
                new PdfLayer(layer.Number, layer.Name, layer.IsOn)));
        }

        if (!automaticViewportRender || forceDetail)
            QueueDetailRenderIfNeeded(force: forceDetail);
        if (request.FireLayersAfter)
            FireLayersChanged();
        if (!string.IsNullOrWhiteSpace(request.StatusAfter))
            PostStatus(request.StatusAfter);
        ReportViewportRenderProfile(
            "layer-cache-only",
            request.PageFolder,
            request.PdfPath,
            request.PdfIndex,
            request.RenderScale,
            elapsedMs: 0,
            fromCache: true,
            clipRect: null);
        if (!automaticViewportRender || request.RestoreView.HasValue || request.FitAfter || forceDetail)
            RequestRepaint();
    }

    private static bool ShouldPreserveDetailDuringLayerRender(LayerRenderRequest request) =>
        !request.ResetLayerStates &&
        !request.RestoreView.HasValue &&
        !request.FitAfter &&
        string.IsNullOrWhiteSpace(request.StatusAfter) &&
        !request.FireLayersAfter;

    private bool ShouldUseCacheOnlyForAutomaticLayerRender(LayerRenderRequest request)
    {
        if (!IsAutomaticViewportLayerRender(request) || request.HighlightedLayers.Count > 0)
            return false;

        if (request.RenderScale <= ViewportRenderPolicy.ResponsiveMinRenderScale * 1.05f &&
            _zoom < ViewportRenderPolicy.PageSwitchSharpUpgradeMinZoom)
        {
            return true;
        }

        return ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale);
    }

    private static bool IsAutomaticViewportLayerRender(LayerRenderRequest request) =>
        string.IsNullOrWhiteSpace(request.StatusAfter) ||
        request.StatusAfter.StartsWith("Loaded:", StringComparison.Ordinal);

    private static bool ShouldForceDetailAfterLayerApply(LayerRenderRequest request) =>
        request.HighlightedLayers.Count > 0;

    private bool TryApplyHotLayerBitmapForPageOpen(
        bool resetLayerStates,
        float renderScale,
        string? statusAfter,
        bool fireLayersAfter,
        ViewState? restoreView,
        bool fitAfter)
    {
        if (string.IsNullOrWhiteSpace(_pdfPath))
            return false;

        LayerRenderRequest request = new(
            ++_layerRenderVersion,
            _pdfPath,
            _pdfIndex,
            _pageFolder,
            Math.Clamp(renderScale, 0.20f, 4.0f),
            resetLayerStates,
            EffectiveLayerStates(),
            EffectiveHighlightedLayers(),
            LayerRenderCachedLayers(),
            restoreView,
            fitAfter,
            statusAfter,
            fireLayersAfter);

        if (!TryApplyLayerBitmapCache(request, out _))
            return false;

        _pendingLayerRender = null;
        return true;
    }

    private bool TryApplyPersistedDefaultCleanRender(
        float renderScale,
        ViewState? restoreView,
        bool fitAfter,
        string? statusAfter,
        bool fireLayersAfter)
    {
        if (string.IsNullOrWhiteSpace(_pdfPath))
            return false;

        int version = ++_layerRenderVersion;
        LayerRenderRequest request = new(
            version,
            _pdfPath,
            _pdfIndex,
            _pageFolder,
            Math.Clamp(renderScale, 0.20f, 4.0f),
            true,
            EffectiveLayerStates(),
            EffectiveHighlightedLayers(),
            LayerRenderCachedLayers(),
            restoreView,
            fitAfter,
            statusAfter,
            fireLayersAfter);

        return TryApplyPersistedCleanLayerRender(request);
    }

    private void QueueInitialLayerDiscoveryOrRender(
        bool resetLayerStates,
        float renderScale,
        string? statusAfter,
        bool fireLayersAfter,
        bool allowImmediateCache = true,
        bool allowLiveRender = true,
        bool allowMemoryBitmap = true)
    {
        QueueLayerRender(
            resetLayerStates,
            renderScale,
            statusAfter,
            fireLayersAfter,
            allowImmediateCache: allowImmediateCache,
            allowLiveRender: allowLiveRender,
            allowMemoryBitmap: allowMemoryBitmap);
    }

    public void DiscoverPdfLayersOnDemand()
    {
        if (string.IsNullOrWhiteSpace(_pdfPath))
        {
            PostStatus("PDF Layers: open a page first.");
            return;
        }

        if (_cachedLayers != null)
        {
            PostStatus("PDF Layers: loading cached page layers...");
            if (_cachedLayers.Count == 0)
            {
                CompleteLayerlessRender("PDF Layers loaded.", fireLayersAfter: true);
                return;
            }

            QueueLayerRender(
                resetLayerStates: true,
                renderScale: CurrentRenderScale(),
                statusAfter: "PDF Layers loaded.",
                fireLayersAfter: true);
            return;
        }

        DiscoverLayersThenRender(
            resetLayerStates: true,
            renderScale: CurrentRenderScale(),
            statusAfter: "PDF Layers loaded.",
            fireLayersAfter: true);
    }

    private void DiscoverLayersThenRender(
        bool resetLayerStates,
        float renderScale,
        string? statusAfter,
        bool fireLayersAfter)
    {
        int version = ++_layerRenderVersion;
        string pdfPath = _pdfPath;
        int pdfIndex = _pdfIndex;
        string pageFolder = _pageFolder;
        PostStatus("PDF Layers: scanning page layers...");
        _ = DiscoverLayersThenRenderAsync(
            version,
            pdfPath,
            pdfIndex,
            pageFolder,
            resetLayerStates,
            Math.Clamp(renderScale, 0.20f, 4.0f),
            statusAfter,
            fireLayersAfter);
    }

    private async Task DiscoverLayersThenRenderAsync(
        int version,
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        bool resetLayerStates,
        float renderScale,
        string? statusAfter,
        bool fireLayersAfter)
    {
        if (string.IsNullOrWhiteSpace(pdfPath))
            return;

        try
        {
            var layerResult = await PdfLayerRenderService.TryReadVisibleLayersAsync(pdfPath, pdfIndex);
            if (version != _layerRenderVersion ||
                !string.Equals(pdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) ||
                pdfIndex != _pdfIndex ||
                !string.Equals(pageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!layerResult.Ok)
            {
                _cachedLayers = [];
                CompleteLayerlessRender(statusAfter, fireLayersAfter);
                if (!string.IsNullOrWhiteSpace(layerResult.Error))
                    PostStatus($"PDF layer discovery unavailable: {layerResult.Error}");
                return;
            }

            _cachedLayers = layerResult.Layers;
            _pdfLayersLoadedForPage = true;
            PdfLayersDiscovered?.Invoke(_cachedLayers);
            if (_cachedLayers.Count == 0)
            {
                CompleteLayerlessRender(statusAfter, fireLayersAfter);
                return;
            }

            QueueLayerRender(resetLayerStates, renderScale, statusAfter, fireLayersAfter);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"PDF layer discovery failed for {pdfPath} page {pdfIndex + 1}");
            if (version != _layerRenderVersion)
                return;

            _cachedLayers = [];
            CompleteLayerlessRender(statusAfter, fireLayersAfter);
            PostStatus($"PDF layer discovery failed: {ex.Message}");
        }
    }

    private void CompleteLayerlessRender(string? statusAfter, bool fireLayersAfter)
    {
        _pdfLayersLoadedForPage = true;
        _layers = [];
        _usingLayerRenderer = false;
        _showingPreviousPageDuringSwitch = false;
        if (fireLayersAfter)
            FireLayersChanged();
        if (!string.IsNullOrWhiteSpace(statusAfter))
            PostStatus(statusAfter);
        RequestRepaint();
    }

    private async Task StartNextLayerRenderAsync()
    {
        if (_layerRenderInProgress || _pendingLayerRender == null)
            return;

        LayerRenderRequest request = _pendingLayerRender;
        _pendingLayerRender = null;
        _layerRenderInProgress = true;
        try
        {
            Stopwatch renderWatch = Stopwatch.StartNew();
            var renderResult = await PdfLayerRenderService.TryRenderAsync(
                request.PdfPath,
                request.PdfIndex,
                request.RenderScale,
                request.LayerStates,
                request.HighlightedLayers,
                request.CachedLayers);
            renderWatch.Stop();
            SKBitmap? decodedBitmap = null;
            if (renderResult.Ok)
                decodedBitmap = await Task.Run(() => DecodePdfLayerRenderBitmap(renderResult.Result));
            ReportSlowLayerRender(request, renderWatch.ElapsedMilliseconds);
            LayerRenderCompletion completion = new(
                request,
                renderResult.Ok,
                renderResult.Result,
                renderResult.Error);

            if (completion.Request.Version == _layerRenderVersion &&
                string.Equals(completion.Request.PdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
                completion.Request.PdfIndex == _pdfIndex &&
                string.Equals(completion.Request.PageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase))
            {
                if (completion.Ok)
                {
                    if (decodedBitmap != null &&
                        ShouldKeepHighScaleLayerBitmapInCache(completion.Request, completion.Request.RenderScale))
                    {
                        CacheLayerBitmapRender(completion.Request, completion.Result, decodedBitmap);
                        ApplyLayerRenderMetadataOnly(
                            completion.Request,
                            completion.Result.WidthPt,
                            completion.Result.HeightPt,
                            completion.Result.Layers);
                        decodedBitmap.Dispose();
                        decodedBitmap = null;
                        return;
                    }

                    bool applied = ApplyLayerRenderResult(completion.Result, completion.Request.ResetLayerStates, decodedBitmap);
                    if (applied)
                    {
                        decodedBitmap = null;
                        CacheLayerBitmapRender(completion.Request);
                        ApplyLayerRenderContinuation(completion.Request);
                        QueueDetailRenderIfNeeded(force: ShouldForceDetailAfterLayerApply(completion.Request));
                        if (completion.Request.FireLayersAfter)
                            FireLayersChanged();
                        if (!string.IsNullOrWhiteSpace(completion.Request.StatusAfter))
                            PostStatus(completion.Request.StatusAfter);
                    }
                    else
                    {
                        decodedBitmap?.Dispose();
                    }
                }
                else if (!string.IsNullOrWhiteSpace(completion.Error))
                {
                    PostStatus($"Layer render unavailable: {completion.Error}");
                    QueueDocnetRender(
                        completion.Request.RenderScale,
                        statusAfter: completion.Request.StatusAfter);
                }
            }
            else
            {
                if (completion.Ok && decodedBitmap != null)
                    CacheLayerBitmapRender(completion.Request, completion.Result, decodedBitmap);
                decodedBitmap?.Dispose();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "PDF layer render failed.");
            PostStatus($"Layer render failed: {ex.Message}");
            if (request.Version == _layerRenderVersion &&
                string.Equals(request.PdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
                request.PdfIndex == _pdfIndex &&
                string.Equals(request.PageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase))
            {
                QueueDocnetRender(request.RenderScale, statusAfter: request.StatusAfter);
            }
        }
        finally
        {
            _layerRenderInProgress = false;
            if (_pendingLayerRender != null)
                _ = StartNextLayerRenderAsync();
        }
    }

    private void ApplyLayerRenderContinuation(LayerRenderRequest request)
    {
        if (request.RestoreView.HasValue)
            RestoreViewState(request.RestoreView.Value);
        else if (request.FitAfter)
            ZoomFit();
    }

    private void CacheLayerBitmapRender(LayerRenderRequest request)
    {
        if (_pageBitmap == null || _pdfW <= 0 || _pdfH <= 0 || _bitmapScale <= 0)
            return;

        LayerBitmapCache.Put(
            LayerRenderBitmapCacheKey(request),
            LayerRenderBitmapCacheSignature(request),
            _pdfW,
            _pdfH,
            _bitmapScale,
            _pageBitmap,
            _layers);
    }

    private void CacheLayerBitmapRender(
        LayerRenderRequest request,
        PdfLayerRenderResult render,
        SKBitmap bitmap)
    {
        if (render.WidthPt <= 0 || render.HeightPt <= 0 || bitmap.Width <= 0 || bitmap.Height <= 0)
            return;

        float bitmapScale = bitmap.Width / render.WidthPt;
        LayerBitmapCache.Put(
            LayerRenderBitmapCacheKey(request),
            LayerRenderBitmapCacheSignature(request),
            render.WidthPt,
            render.HeightPt,
            bitmapScale,
            bitmap,
            render.Layers);
    }

    private void ReportSlowLayerRender(LayerRenderRequest request, long elapsedMs)
    {
        ReportViewportRenderProfile(
            "layer",
            request.PageFolder,
            request.PdfPath,
            request.PdfIndex,
            request.RenderScale,
            elapsedMs,
            fromCache: false,
            clipRect: null);

        if (elapsedMs < ViewportRenderPolicy.SlowRenderLogMs)
            return;

        DateTime now = DateTime.UtcNow;
        if ((now - _lastSlowRenderLogAt).TotalSeconds < 2)
            return;

        _lastSlowRenderLogAt = now;
        AppLog.Info(
            $"Viewport slow layer render {elapsedMs}ms; page='{request.PageFolder}'; " +
            $"pdf='{Path.GetFileName(request.PdfPath)}'; pdfPage={request.PdfIndex + 1}; scale={request.RenderScale:0.###}; " +
            $"layers={request.LayerStates.Count}; highlights={request.HighlightedLayers.Count}");
    }

    private void UpdateLayerSnapshot(IEnumerable<PdfLayer> layers)
    {
        _layers = layers
            .Select(layer => new PdfLayer(
                layer.Number,
                layer.Name,
                _layerStates.TryGetValue(layer.Number, out bool on) ? on : layer.IsOn,
                _highlightedLayers.Contains(layer.Number) || _pdfLayerTracePreviewLayer == layer.Number))
            .ToList();
        if (!_pdfLayerTraceEnabled)
            EnsureActivePdfLayerTraceLayer();
        PublishPdfLayerTraceState();
    }

    public void SetLayerVisible(int configNumber, bool on)
    {
        if (_pageBitmap == null || !_usingLayerRenderer || _layers.Count == 0)
        {
            PostStatus("No PDF layers are available on this page.");
            return;
        }

        _layerStates[configNumber] = on;
        ResetPdfSnapCache();
        QueuePdfSnapPointLoad(force: true);
        UpdateLayerSnapshot(_layers);
        FireLayersChanged();
        QueueLayerRender(
            resetLayerStates: false,
            renderScale: CurrentRenderScale(),
            statusAfter: $"Layer {(on ? "on" : "off")}: {LayerName(configNumber)}",
            fireLayersAfter: true);

        #if false
        if (_pageBitmap == null) return;
        try
        {
            // PyMuPDF 1.22+ equivalent: set_layer_ui_config(number, 0=on / 1=off)
            // Docnet.Core doesn't expose OCGs directly — reload page after toggling
            // For now: mark dirty and re-render (OCG toggle via PDFium P/Invoke added Phase 2)
            PostStatus("PDF layer toggling is unavailable in the current Docnet.Core renderer.");
        }
        catch (Exception ex) { PostStatus($"Layer error: {ex.Message}"); }
        #endif
    }

    public void SetAllLayers(bool on)
    {
        if (_pageBitmap == null || !_usingLayerRenderer || _layers.Count == 0)
        {
            PostStatus("No PDF layers are available on this page.");
            return;
        }

        foreach (var layer in _layers)
            _layerStates[layer.Number] = on;

        ResetPdfSnapCache();
        QueuePdfSnapPointLoad(force: true);
        UpdateLayerSnapshot(_layers);
        FireLayersChanged();
        QueueLayerRender(
            resetLayerStates: false,
            renderScale: CurrentRenderScale(),
            statusAfter: $"All PDF layers {(on ? "on" : "off")}.",
            fireLayersAfter: true);

        #if false
        PostStatus("PDF layer toggling is unavailable in the current Docnet.Core renderer.");
        FireLayersChanged();
        #endif
    }

    public void SetLayerHighlighted(int configNumber, bool highlighted)
    {
        if (_pageBitmap == null || !_usingLayerRenderer || _layers.Count == 0)
        {
            PostStatus("No PDF layers are available on this page.");
            return;
        }

        if (highlighted)
            _highlightedLayers.Add(configNumber);
        else
            _highlightedLayers.Remove(configNumber);

        UpdateLayerSnapshot(_layers);
        FireLayersChanged();
        QueueLayerRender(
            resetLayerStates: false,
            renderScale: CurrentRenderScale(),
            statusAfter: $"{(highlighted ? "Highlighted" : "Unhighlighted")} layer: {LayerName(configNumber)}",
            fireLayersAfter: true);
    }

    public void ClearLayerHighlights()
    {
        if (_highlightedLayers.Count == 0)
            return;

        _highlightedLayers.Clear();
        UpdateLayerSnapshot(_layers);
        FireLayersChanged();
        QueueLayerRender(
            resetLayerStates: false,
            renderScale: CurrentRenderScale(),
            statusAfter: "Cleared PDF layer highlights.",
            fireLayersAfter: true);
    }

    private string LayerName(int layerNumber) =>
        _layers.FirstOrDefault(layer => layer.Number == layerNumber)?.Name ?? $"Layer {layerNumber}";

    private static string LayerTraceModeTitle(PdfLayerTraceMode mode) => mode switch
    {
        PdfLayerTraceMode.Edge => "Edge",
        PdfLayerTraceMode.Point => "Point",
        PdfLayerTraceMode.AllEdges => "All Edges",
        _ => "Full",
    };

    private void FireLayersChanged()
    {
        LayersChanged?.Invoke(_layers);
    }

}
