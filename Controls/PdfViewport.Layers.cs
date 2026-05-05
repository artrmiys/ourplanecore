using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Docnet.Core;
using Docnet.Core.Models;
using SmartTakeoffs;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace SmartTakeoffs.Controls;

public sealed partial class PdfViewport
{
    // ── Layer API ─────────────────────────────────────────────────────────────

    private void RenderPageWithDocnet(float renderScale)
    {
        float scale = Math.Clamp(renderScale, 0.20f, 4.0f);
        using var docReader  = _docLib.GetDocReader(_pdfPath, new PageDimensions(scale));
        using var pageReader = docReader.GetPageReader(_pdfIndex);

        int bw = pageReader.GetPageWidth();
        int bh = pageReader.GetPageHeight();
        _pdfW        = bw / scale;
        _pdfH        = bh / scale;
        _bitmapScale = scale;

        byte[] bytes = pageReader.GetImage();

        var info = new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul);
        _pageBitmap?.Dispose();
        _pageBitmap = new SKBitmap(info);
        Marshal.Copy(bytes, 0, _pageBitmap.GetPixels(), bytes.Length);
        _layers = [];
        _usingLayerRenderer = false;
        _renderedScale = scale;
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

        bool needsFit = _pdfW <= 0 || _pdfH <= 0;
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
        _usingLayerRenderer = true;
        RequestRepaint();
        if (needsFit)
            Dispatcher.InvokeAsync(ZoomFit);
        return true;
    }

    private void QueueLayerRender(
        bool resetLayerStates,
        float renderScale,
        string? statusAfter = null,
        bool fireLayersAfter = false)
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
            statusAfter,
            fireLayersAfter);

        StartNextLayerRender();
    }

    private async void StartNextLayerRender()
    {
        if (_layerRenderInProgress || _pendingLayerRender == null)
            return;

        LayerRenderRequest request = _pendingLayerRender;
        _pendingLayerRender = null;
        _layerRenderInProgress = true;

        LayerRenderCompletion completion = await Task.Run(() =>
        {
            bool ok = PdfLayerRenderService.TryRender(
                request.PdfPath,
                request.PdfIndex,
                request.RenderScale,
                request.LayerStates,
                request.HighlightedLayers,
                request.CachedLayers,
                out PdfLayerRenderResult render,
                out string error);
            return new LayerRenderCompletion(request, ok, render, error);
        });

        _layerRenderInProgress = false;

        if (completion.Request.Version == _layerRenderVersion &&
            string.Equals(completion.Request.PdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
            completion.Request.PdfIndex == _pdfIndex &&
            string.Equals(completion.Request.PageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase))
        {
            if (completion.Ok)
            {
                if (ApplyLayerRenderResult(completion.Result, completion.Request.ResetLayerStates))
                {
                    if (completion.Request.FireLayersAfter)
                        FireLayersChanged();
                    if (!string.IsNullOrWhiteSpace(completion.Request.StatusAfter))
                        PostStatus(completion.Request.StatusAfter);
                }
            }
            else if (!string.IsNullOrWhiteSpace(completion.Error))
            {
                PostStatus($"Layer render unavailable: {completion.Error}");
            }
        }

        if (_pendingLayerRender != null)
            StartNextLayerRender();
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
