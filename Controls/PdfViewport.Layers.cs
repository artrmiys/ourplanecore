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
    // ── Layer API ─────────────────────────────────────────────────────────────

    private static DocnetRenderResult RenderPageBitmapWithDocnet(string pdfPath, int pdfIndex, float renderScale)
    {
        float scale = Math.Clamp(renderScale, 0.20f, 4.0f);
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
        _pdfW = render.WidthPt;
        _pdfH = render.HeightPt;
        _bitmapScale = render.BitmapScale;
        _layers = [];
        _usingLayerRenderer = false;
        _renderedScale = render.BitmapScale;
        _showingPreviousPageDuringSwitch = false;
    }

    private void ApplyCachedBitmapRender(CachedBitmapRender render)
    {
        _pageBitmap?.Dispose();
        _pageBitmap = render.Bitmap;
        _pdfW = render.WidthPt;
        _pdfH = render.HeightPt;
        _bitmapScale = render.BitmapScale;
        _layers = [];
        _usingLayerRenderer = false;
        _renderedScale = render.BitmapScale;
        _showingPreviousPageDuringSwitch = false;
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

        float scale = Math.Clamp(renderScale, 0.20f, 4.0f);
        int version = ++_docnetRenderVersion;
        _pendingDocnetRender = new DocnetRenderRequest(
            version,
            _pdfPath,
            _pdfIndex,
            _pageFolder,
            scale,
            restoreView,
            fitAfter,
            queueLayerAfter,
            resetLayerStates,
            statusAfter,
            fireLayersAfter);

        _ = StartNextDocnetRenderAsync();
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
            if (!fromCache)
            {
                render = await Task.Run(() =>
                    RenderPageBitmapWithDocnet(request.PdfPath, request.PdfIndex, request.RenderScale));
                DocnetRenderCache.Put(cacheKey, render);
            }
            renderWatch.Stop();
            ReportSlowPdfRender("docnet", request, renderWatch.ElapsedMilliseconds, fromCache);

            if (request.Version == _docnetRenderVersion &&
                string.Equals(request.PdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
                request.PdfIndex == _pdfIndex &&
                string.Equals(request.PageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase))
            {
                if (fromCache)
                    ApplyCachedBitmapRender(cached);
                else if (render != null)
                    ApplyDocnetRenderResult(render);
                ApplyDocnetRenderContinuation(request);
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
                request.FireLayersAfter);
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.StatusAfter))
            PostStatus(request.StatusAfter);
    }

    private void ReportSlowPdfRender(string kind, DocnetRenderRequest request, long elapsedMs, bool fromCache)
    {
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

    private bool ApplyLayerRenderResult(PdfLayerRenderResult render, bool resetLayerStates)
    {
        var bitmap = SKBitmap.Decode(render.ImageBytes);
        if (bitmap == null)
        {
            PostStatus("Layer renderer returned an unreadable image.");
            return false;
        }

        _pageBitmap?.Dispose();
        _pageBitmap = bitmap;
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
        _pendingDocnetRender = null;
        _docnetRenderVersion++;
        _showingPreviousPageDuringSwitch = false;
        RequestRepaint();
        return true;
    }

    private void QueueLayerRender(
        bool resetLayerStates,
        float renderScale,
        string? statusAfter = null,
        bool fireLayersAfter = false,
        ViewState? restoreView = null,
        bool fitAfter = false)
    {
        if (string.IsNullOrWhiteSpace(_pdfPath))
            return;

        int version = ++_layerRenderVersion;
        _pendingLayerRender = new LayerRenderRequest(
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

        _ = StartNextLayerRenderAsync();
    }

    private void QueueInitialLayerDiscoveryOrRender(
        bool resetLayerStates,
        float renderScale,
        string? statusAfter,
        bool fireLayersAfter)
    {
        QueueLayerRender(resetLayerStates, renderScale, statusAfter, fireLayersAfter);
    }

    public void DiscoverPdfLayersOnDemand()
    {
        if (string.IsNullOrWhiteSpace(_pdfPath))
        {
            PostStatus("PDF Layers: open a page first.");
            return;
        }

        _cachedLayers = null;
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
            PdfLayersDiscovered?.Invoke(_cachedLayers);
            if (_cachedLayers.Count == 0)
            {
                QueueLayerRender(resetLayerStates, renderScale, statusAfter, fireLayersAfter);
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
                    if (ApplyLayerRenderResult(completion.Result, completion.Request.ResetLayerStates))
                    {
                        ApplyLayerRenderContinuation(completion.Request);
                        if (completion.Request.FireLayersAfter)
                            FireLayersChanged();
                        if (!string.IsNullOrWhiteSpace(completion.Request.StatusAfter))
                            PostStatus(completion.Request.StatusAfter);
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

    private void ReportSlowLayerRender(LayerRenderRequest request, long elapsedMs)
    {
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
