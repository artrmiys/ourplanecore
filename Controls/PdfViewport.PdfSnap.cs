using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private bool _pdfSnapEnabled;
    private PdfSnapPointIndex _pdfSnapIndex = PdfSnapPointIndex.Empty;
    private string _pdfSnapCacheKey = "";
    private bool _pdfSnapLoadInProgress;
    private bool _pdfSnapReloadPending;
    private int _pdfSnapLoadVersion;

    public bool PdfSnapEnabled
    {
        get => _pdfSnapEnabled;
        set
        {
            if (_pdfSnapEnabled == value)
                return;

            _pdfSnapEnabled = value;
            SetSnapPreview(null);
            PdfSnapChanged?.Invoke(_pdfSnapEnabled);
            if (_pdfSnapEnabled)
                QueuePdfSnapPointLoad(force: false);
            PostRecordPrompt();
        }
    }

    public event Action<bool>? PdfSnapChanged;

    private void ResetPdfSnapCache()
    {
        _pdfSnapIndex = PdfSnapPointIndex.Empty;
        _pdfSnapCacheKey = "";
        _pdfSnapReloadPending = false;
        _pdfSnapLoadVersion++;
    }

    private void QueuePdfSnapPointLoad(bool force)
    {
        if (!_pdfSnapEnabled || string.IsNullOrWhiteSpace(_pdfPath))
            return;

        IReadOnlyList<PdfLayerInfo>? layers = CurrentPdfSnapVisibleLayers();
        string cacheKey = PdfSnapCacheKey(_pdfPath, _pdfIndex, _pageFolder, PdfSnapLayerStateKey(layers));
        if (!force && string.Equals(_pdfSnapCacheKey, cacheKey, StringComparison.Ordinal))
            return;

        if (_pdfSnapLoadInProgress)
        {
            _pdfSnapReloadPending = true;
            return;
        }

        _pdfSnapLoadInProgress = true;
        int version = ++_pdfSnapLoadVersion;
        _ = LoadPdfSnapPointsAsync(version, _pdfPath, _pdfIndex, _pageFolder, layers, cacheKey);
    }

    private async Task LoadPdfSnapPointsAsync(
        int version,
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        IReadOnlyList<PdfLayerInfo>? layers,
        string cacheKey)
    {
        try
        {
            var result = await PdfGeometrySnapService.TryReadSnapPointsAsync(pdfPath, pdfIndex, layers);
            if (!IsCurrentPdfSnapRequest(version, pdfPath, pdfIndex, pageFolder))
                return;

            if (!result.Ok)
            {
                _pdfSnapIndex = PdfSnapPointIndex.Empty;
                _pdfSnapCacheKey = cacheKey;
                PostStatus($"PDF Snap unavailable: {result.Error}");
                return;
            }

            _pdfSnapIndex = new PdfSnapPointIndex(result.Result.Points);
            _pdfSnapCacheKey = cacheKey;
            if (_pdfSnapEnabled)
                PostStatus($"PDF Snap ready: {_pdfSnapIndex.Count} points.");
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "PDF snap point load failed.");
            if (IsCurrentPdfSnapRequest(version, pdfPath, pdfIndex, pageFolder))
                PostStatus($"PDF Snap failed: {ex.Message}");
        }
        finally
        {
            _pdfSnapLoadInProgress = false;
            if (_pdfSnapReloadPending)
            {
                _pdfSnapReloadPending = false;
                QueuePdfSnapPointLoad(force: true);
            }
        }
    }

    private bool IsCurrentPdfSnapRequest(int version, string pdfPath, int pdfIndex, string pageFolder) =>
        version == _pdfSnapLoadVersion &&
        _pdfSnapEnabled &&
        string.Equals(pdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
        pdfIndex == _pdfIndex &&
        string.Equals(pageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<PdfLayerInfo>? CurrentPdfSnapVisibleLayers()
    {
        IReadOnlyList<PdfLayerInfo>? source = _cachedLayers?.Count > 0
            ? _cachedLayers
            : _layers.Count > 0
                ? _layers
                    .Select(layer => new PdfLayerInfo
                    {
                        Number = layer.Number,
                        Name = layer.Name,
                        IsOn = layer.IsOn,
                    })
                    .ToList()
                : null;

        if (source == null || source.Count == 0)
            return null;

        return source
            .Select(layer => new PdfLayerInfo
            {
                Number = layer.Number,
                Name = layer.Name,
                IsOn = _layerStates.TryGetValue(layer.Number, out bool on) ? on : layer.IsOn,
            })
            .ToList();
    }

    private static string PdfSnapCacheKey(string pdfPath, int pdfIndex, string pageFolder, string layerStateKey) =>
        $"{pdfPath}|{pdfIndex}|{pageFolder}|{layerStateKey}";

    private static string PdfSnapLayerStateKey(IReadOnlyList<PdfLayerInfo>? layers) =>
        layers == null || layers.Count == 0
            ? "no-layers"
            : string.Join("|", layers.Select(layer => $"{layer.Number}:{layer.IsOn}"));

    private bool TryFindPdfSnapPoint(SKPoint rawPdf, float tolerancePt, out SKPoint snapped, out string snapKind)
    {
        snapped = default;
        snapKind = "";
        if (!PdfSnapEnabled)
            return false;

        if (!IsPdfSnapCacheCurrent())
            QueuePdfSnapPointLoad(force: false);

        if (!_pdfSnapIndex.TryFind(rawPdf, tolerancePt, out PdfGeometrySnapPoint snap))
            return false;

        snapped = snap.Point;
        snapKind = string.Equals(snap.Kind, "pdf-corner", StringComparison.OrdinalIgnoreCase)
            ? "pdf-corner"
            : "pdf-point";
        return true;
    }

    private bool IsPdfSnapCacheCurrent()
    {
        if (string.IsNullOrWhiteSpace(_pdfSnapCacheKey) || string.IsNullOrWhiteSpace(_pdfPath))
            return false;

        IReadOnlyList<PdfLayerInfo>? layers = CurrentPdfSnapVisibleLayers();
        string cacheKey = PdfSnapCacheKey(_pdfPath, _pdfIndex, _pageFolder, PdfSnapLayerStateKey(layers));
        return string.Equals(_pdfSnapCacheKey, cacheKey, StringComparison.Ordinal);
    }
}
