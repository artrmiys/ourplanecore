using System;
using System.Collections.Generic;
using System.Linq;
using OurPlaneCore;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    public void SetActivePdfLayerTraceLayer(PdfLayer layer) =>
        SetActivePdfLayerTraceLayer(layer.Number, layer.Name);

    public void SetActivePdfLayerTraceLayer(int layerNumber, string layerName)
    {
        _activePdfLayerTraceLayer = layerNumber;
        _activePdfLayerTraceLayerName = layerName;
        _pdfLayerTraceLayerExplicitlySelected = true;
        ClearPdfLayerTraceSession(keepCandidateLayer: true, queueRender: false);
        if (_pdfLayerTraceEnabled)
        {
            _pdfLayerTraceReadyToApply = true;
            SetPdfLayerTracePreviewLayer(layerNumber, forceRender: true);
        }
        PublishPdfLayerTraceState();
        if (_pdfLayerTraceEnabled)
            PostPdfLayerTraceStatus();
    }

    public void SetPdfLayerTraceEnabled(bool enabled)
    {
        _pdfLayerTraceEnabled = enabled;
        if (enabled)
        {
            CancelDrawing();
            ClearSelection();
            bool useExplicitLayer = _pdfLayerTraceLayerExplicitlySelected && _activePdfLayerTraceLayer.HasValue;
            ClearPdfLayerTraceSession(keepCandidateLayer: useExplicitLayer, queueRender: false);
            if (useExplicitLayer && _activePdfLayerTraceLayer.HasValue)
            {
                _pdfLayerTraceReadyToApply = true;
                SetPdfLayerTracePreviewLayer(_activePdfLayerTraceLayer.Value, forceRender: true);
            }
            else
            {
                RefreshPdfLayerTraceRender();
            }
            Focus();
        }
        else
        {
            ClearPdfLayerTraceSession();
        }

        PublishPdfLayerTraceState();
        PostPdfLayerTraceStatus();
    }

    public void TogglePdfLayerTraceEnabled() =>
        SetPdfLayerTraceEnabled(!_pdfLayerTraceEnabled);

    public void SetPdfLayerTraceMode(PdfLayerTraceMode mode)
    {
        _pdfLayerTraceMode = mode;
        PublishPdfLayerTraceState();
        if (_pdfLayerTraceEnabled)
            PostPdfLayerTraceStatus();
    }

    public void CyclePdfLayerTraceMode()
    {
        _pdfLayerTraceMode = _pdfLayerTraceMode switch
        {
            PdfLayerTraceMode.Full => PdfLayerTraceMode.Edge,
            PdfLayerTraceMode.Edge => PdfLayerTraceMode.Point,
            PdfLayerTraceMode.Point => PdfLayerTraceMode.AllEdges,
            _ => PdfLayerTraceMode.Full,
        };
        PublishPdfLayerTraceState();
        PostPdfLayerTraceStatus();
    }

    public void TraceActivePdfLayerFromCurrentPointer() =>
        AdvancePdfLayerTrace(_lastPointerPdf);

    public void ApplyActivePdfLayerTraceFromCurrentPointer() =>
        TraceActivePdfLayer(_lastPointerPdf);

    private bool AdvancePdfLayerTrace(SKPoint? pickPoint)
    {
        if (!_pdfLayerTraceEnabled)
            return false;

        if (_pdfLayerTraceChoosingLayer || (_pdfLayerTraceCandidates.Count > 0 && !_pdfLayerTraceReadyToApply))
        {
            LockPdfLayerTraceCandidate();
            return true;
        }

        if (!_pdfLayerTraceReadyToApply)
            return BeginPdfLayerTraceAt(pickPoint, lockCandidate: true);

        return TraceActivePdfLayer(pickPoint ?? _pdfLayerTracePickPoint);
    }

    private bool BeginPdfLayerTraceAt(SKPoint? pickPoint, bool lockCandidate = false)
    {
        if (pickPoint == null)
        {
            PostStatus("Layer Trace: move over the PDF or click a layer first.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_pdfPath) || _pageBitmap == null)
        {
            PostStatus("Open a PDF page before using Layer Trace.");
            return false;
        }

        if (!PdfLayerRenderService.TryProbeLayers(
                _pdfPath,
                _pdfIndex,
                pickPoint.Value,
                _cachedLayers ?? _layers
                    .Select(layer => new PdfLayerInfo { Number = layer.Number, Name = layer.Name, IsOn = layer.IsOn })
                    .ToList(),
                out PdfLayerProbeResult probe,
                out string error))
        {
            PostStatus($"Layer probe failed: {error}");
            return false;
        }

        return ApplyPdfLayerTraceProbeResult(pickPoint.Value, probe, lockCandidate);
    }

    private void UpdatePdfLayerTraceHover(SKPoint pickPoint)
    {
        if (!_pdfLayerTraceEnabled || _pdfLayerTraceReadyToApply)
        {
            PostPdfLayerTraceStatus();
            return;
        }

        if (string.IsNullOrWhiteSpace(_pdfPath) || _pageBitmap == null)
        {
            PostStatus("Open a PDF page before using Layer Trace.");
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (_lastPdfLayerTraceProbePoint is { } lastPoint &&
            (now - _lastPdfLayerTraceProbeAt).TotalMilliseconds < 110 &&
            DistanceSquared(lastPoint, pickPoint) < 36)
        {
            PostPdfLayerTraceStatus();
            return;
        }

        if (_pdfLayerTraceProbeInProgress)
        {
            _pendingPdfLayerTraceProbePoint = pickPoint;
            return;
        }

        _lastPdfLayerTraceProbePoint = pickPoint;
        _lastPdfLayerTraceProbeAt = now;
        StartPdfLayerTraceHoverProbe(pickPoint);
    }

    private async void StartPdfLayerTraceHoverProbe(SKPoint pickPoint)
    {
        if (_pdfLayerTraceProbeInProgress)
            return;

        _pdfLayerTraceProbeInProgress = true;
        int version = ++_pdfLayerTraceProbeVersion;
        string pdfPath = _pdfPath;
        int pdfIndex = _pdfIndex;
        string pageFolder = _pageFolder;
        IReadOnlyList<PdfLayerInfo>? layers = LayerRenderCachedLayers();

        PdfLayerProbeResult probe = new();
        string error = "";
        bool ok = await Task.Run(() => PdfLayerRenderService.TryProbeLayers(
            pdfPath,
            pdfIndex,
            pickPoint,
            layers,
            out probe,
            out error));

        _pdfLayerTraceProbeInProgress = false;
        if (version == _pdfLayerTraceProbeVersion &&
            _pdfLayerTraceEnabled &&
            !_pdfLayerTraceReadyToApply &&
            string.Equals(pdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
            pdfIndex == _pdfIndex &&
            string.Equals(pageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase))
        {
            if (ok)
                ApplyPdfLayerTraceProbeResult(pickPoint, probe, lockCandidate: false);
            else
                PostStatus($"Layer probe failed: {error}");
        }

        if (_pendingPdfLayerTraceProbePoint is { } pending &&
            _pdfLayerTraceEnabled &&
            !_pdfLayerTraceReadyToApply)
        {
            _pendingPdfLayerTraceProbePoint = null;
            UpdatePdfLayerTraceHover(pending);
        }
    }

    private bool ApplyPdfLayerTraceProbeResult(SKPoint pickPoint, PdfLayerProbeResult probe, bool lockCandidate)
    {
        _pdfLayerTraceCandidates = probe.Candidates.ToList();
        _pdfLayerTraceCandidateIndex = 0;
        _pdfLayerTracePickPoint = pickPoint;

        if (_pdfLayerTraceCandidates.Count == 0)
        {
            _pdfLayerTraceChoosingLayer = false;
            _pdfLayerTraceReadyToApply = false;
            _activePdfLayerTraceLayer = null;
            _activePdfLayerTraceLayerName = "";
            SetPdfLayerTracePreviewLayer(null);
            PostStatus("Layer Trace: no PDF layer found under this point.");
            PublishPdfLayerTraceState();
            return false;
        }

        ApplyCurrentPdfLayerTraceCandidate();
        if (lockCandidate)
            LockPdfLayerTraceCandidate();
        else
        {
            _pdfLayerTraceChoosingLayer = _pdfLayerTraceCandidates.Count > 1;
            _pdfLayerTraceReadyToApply = false;
            RequestRepaint();
            PostPdfLayerTraceStatus();
            PublishPdfLayerTraceState();
        }

        return true;
    }

    private void CyclePdfLayerTraceCandidate()
    {
        if (_pdfLayerTraceCandidates.Count <= 1)
            return;

        _pdfLayerTraceCandidateIndex = (_pdfLayerTraceCandidateIndex + 1) % _pdfLayerTraceCandidates.Count;
        ApplyCurrentPdfLayerTraceCandidate();
        RequestRepaint();
        PostPdfLayerTraceStatus();
        PublishPdfLayerTraceState();
    }

    private void LockPdfLayerTraceCandidate()
    {
        if (_pdfLayerTraceCandidates.Count == 0)
            return;

        ApplyCurrentPdfLayerTraceCandidate();
        _pdfLayerTraceChoosingLayer = false;
        _pdfLayerTraceReadyToApply = true;
        RefreshPdfLayerTraceRender();
        PostPdfLayerTraceStatus();
        PublishPdfLayerTraceState();
    }

    private void ApplyCurrentPdfLayerTraceCandidate()
    {
        if (_pdfLayerTraceCandidates.Count == 0)
            return;

        _pdfLayerTraceCandidateIndex = Math.Clamp(_pdfLayerTraceCandidateIndex, 0, _pdfLayerTraceCandidates.Count - 1);
        PdfLayerProbeCandidate candidate = _pdfLayerTraceCandidates[_pdfLayerTraceCandidateIndex];
        _activePdfLayerTraceLayer = candidate.Layer;
        _activePdfLayerTraceLayerName = candidate.LayerName;
        SetPdfLayerTracePreviewLayer(candidate.Layer);
    }

    private PdfLayerProbeCandidate? CurrentPdfLayerTraceCandidate() =>
        _pdfLayerTraceCandidates.Count == 0
            ? null
            : _pdfLayerTraceCandidates[Math.Clamp(_pdfLayerTraceCandidateIndex, 0, _pdfLayerTraceCandidates.Count - 1)];

    private void ClearPdfLayerTraceSession(bool keepCandidateLayer = false, bool queueRender = true)
    {
        _pdfLayerTraceCandidates.Clear();
        _pdfLayerTraceCandidateIndex = 0;
        _pdfLayerTraceChoosingLayer = false;
        _pdfLayerTraceReadyToApply = false;
        _pdfLayerTracePickPoint = null;
        _pendingPdfLayerTraceProbePoint = null;
        _pdfLayerTraceProbeVersion++;
        SetPdfLayerTracePreviewLayer(null, forceRender: queueRender, queueRender: queueRender);
        if (!keepCandidateLayer)
        {
            _activePdfLayerTraceLayer = null;
            _activePdfLayerTraceLayerName = "";
            _pdfLayerTraceLayerExplicitlySelected = false;
        }
        RequestRepaint();
    }

    private void SetPdfLayerTracePreviewLayer(int? layerNumber, bool forceRender = false, bool queueRender = true)
    {
        if (!forceRender && _pdfLayerTracePreviewLayer == layerNumber)
            return;

        _pdfLayerTracePreviewLayer = layerNumber;
        if (queueRender)
            RefreshPdfLayerTraceRender();
    }

    private void RefreshPdfLayerTraceRender()
    {
        UpdateLayerSnapshot(_layers);
        FireLayersChanged();
        if (_usingLayerRenderer && _pageBitmap != null && !string.IsNullOrWhiteSpace(_pdfPath))
        {
            QueueLayerRender(
                resetLayerStates: false,
                renderScale: CurrentRenderScale(),
                fireLayersAfter: true);
        }
        else
        {
            RequestRepaint();
        }
    }

    private HashSet<int> EffectiveHighlightedLayers()
    {
        var highlighted = new HashSet<int>(_highlightedLayers);
        if (_pdfLayerTracePreviewLayer.HasValue)
            highlighted.Add(_pdfLayerTracePreviewLayer.Value);
        return highlighted;
    }

    private Dictionary<int, bool> EffectiveLayerStates()
    {
        var states = new Dictionary<int, bool>(_layerStates);
        if (!_pdfLayerTraceEnabled || !_pdfLayerTraceReadyToApply || !_activePdfLayerTraceLayer.HasValue)
            return states;

        IReadOnlyList<PdfLayerInfo> layers = LayerRenderCachedLayers() ?? Array.Empty<PdfLayerInfo>();
        foreach (PdfLayerInfo layer in layers)
            states[layer.Number] = layer.Number == _activePdfLayerTraceLayer.Value;
        states[_activePdfLayerTraceLayer.Value] = true;
        return states;
    }

    private IReadOnlyList<PdfLayerInfo>? LayerRenderCachedLayers()
    {
        List<PdfLayerInfo>? layers = _cachedLayers?.ToList()
            ?? _layers
                .Select(layer => new PdfLayerInfo { Number = layer.Number, Name = layer.Name, IsOn = layer.IsOn })
                .ToList();

        if (_pdfLayerTracePreviewLayer.HasValue &&
            !layers.Any(layer => layer.Number == _pdfLayerTracePreviewLayer.Value))
        {
            layers.Add(new PdfLayerInfo
            {
                Number = _pdfLayerTracePreviewLayer.Value,
                Name = _activePdfLayerTraceLayerName,
                IsOn = true,
            });
        }

        return layers;
    }

    private bool TraceActivePdfLayer(SKPoint? pickPoint)
    {
        if (!_pdfLayerTraceEnabled)
            return false;
        if (string.IsNullOrWhiteSpace(_pdfPath) || _pageBitmap == null)
        {
            PostStatus("Open a PDF page before using Layer Trace.");
            return false;
        }

        if (!_activePdfLayerTraceLayer.HasValue)
        {
            PostStatus("Layer Trace: hover PDF geometry and click/Enter to lock a layer before tracing.");
            return false;
        }

        if (ScaleMetersPerPt <= 0)
        {
            PostScaleRequiredStatus();
            return false;
        }

        if (!PdfLayerRenderService.TryTraceLayer(
                _pdfPath,
                _pdfIndex,
                _activePdfLayerTraceLayer.Value,
                _activePdfLayerTraceLayerName,
                _pdfLayerTraceMode,
                _pdfLayerTraceMode is PdfLayerTraceMode.Edge or PdfLayerTraceMode.Point ? pickPoint : null,
                _cachedLayers ?? _layers
                    .Select(layer => new PdfLayerInfo { Number = layer.Number, Name = layer.Name, IsOn = layer.IsOn })
                    .ToList(),
                out PdfLayerTraceResult trace,
                out string error))
        {
            PostStatus($"Layer Trace failed: {error}");
            return false;
        }

        var added = new List<Measurement>();
        foreach (PdfLayerTraceMeasurement item in trace.Measurements)
        {
            string mType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MType);
            if (mType == "line" && item.Points.Count < 2)
                continue;
            if (mType == "area" && item.Points.Count < 3)
                continue;

            var measurement = new Measurement
            {
                Name = item.Name,
                Notes = item.Notes,
                MType = mType,
                Points = item.Points.ToList(),
                Color = ActiveColor,
                PageFolder = _pageFolder,
                TakeoffFolder = ActiveTakeoffFolder,
                ScaleMetersPerPt = ScaleMetersPerPt,
            };
            _measurements.Add(measurement);
            MeasurementAdded?.Invoke(measurement);
            if (_measurements.Contains(measurement))
                added.Add(measurement);
        }

        if (added.Count == 0)
        {
            PostStatus("Layer Trace found no usable takeoff geometry.");
            return false;
        }

        SelectMeasurements(added);
        ClearPdfLayerTraceSession();
        RequestRepaint();
        string kind = added.Count == 1 ? ToolTitle(added[0].MType) : $"{added.Count} lines";
        PostStatus($"Layer Trace added {kind} from '{trace.LayerName}' ({LayerTraceModeTitle(_pdfLayerTraceMode)}).");
        return true;
    }

    private void EnsureActivePdfLayerTraceLayer()
    {
        if (_layers.Count == 0)
        {
            _activePdfLayerTraceLayer = null;
            _activePdfLayerTraceLayerName = "";
            return;
        }

        if (_activePdfLayerTraceLayer.HasValue &&
            _layers.Any(layer => layer.Number == _activePdfLayerTraceLayer.Value))
        {
            PdfLayer current = _layers.First(layer => layer.Number == _activePdfLayerTraceLayer.Value);
            _activePdfLayerTraceLayerName = current.Name;
            return;
        }

        if (_activePdfLayerTraceLayer.HasValue &&
            !string.IsNullOrWhiteSpace(_activePdfLayerTraceLayerName))
        {
            return;
        }

        PdfLayer preferred = _layers.FirstOrDefault(layer => layer.IsHighlighted) ?? _layers[0];
        _activePdfLayerTraceLayer = preferred.Number;
        _activePdfLayerTraceLayerName = preferred.Name;
    }

    private void PostPdfLayerTraceStatus()
    {
        if (!_pdfLayerTraceEnabled)
        {
            PostStatus("Layer Trace off.");
            return;
        }

        string layer = string.IsNullOrWhiteSpace(_activePdfLayerTraceLayerName)
            ? "select a PDF layer"
            : _activePdfLayerTraceLayerName;
        if (_pdfLayerTraceChoosingLayer)
        {
            PostStatus($"Layer Trace: {layer} ({_pdfLayerTraceCandidateIndex + 1}/{_pdfLayerTraceCandidates.Count}). Tab selects layer; click/Enter locks it.");
            return;
        }
        if (_pdfLayerTraceCandidates.Count > 0 && !_pdfLayerTraceReadyToApply)
        {
            PostStatus($"Layer Trace hover: {layer}. Click/Enter locks layer; Tab cycles candidates if available.");
            return;
        }
        if (_pdfLayerTraceReadyToApply)
        {
            PostStatus($"Layer Trace: {layer}, {LayerTraceModeTitle(_pdfLayerTraceMode)}. Tab changes mode; click/Enter creates takeoff.");
            return;
        }

        PostStatus("Layer Trace: hover PDF geometry to preview a layer, click/Enter to lock it.");
    }

    private void PublishPdfLayerTraceState() =>
        PdfLayerTraceStateChanged?.Invoke();

    private void DrawPdfLayerTraceOverlay(SKCanvas canvas)
    {
        if (!_pdfLayerTraceEnabled || CurrentPdfLayerTraceCandidate() is not { } candidate)
            return;

        float safeZoom = Math.Max(_zoom, 0.001f);
        using var fill = new SKPaint
        {
            Color = new SKColor(0xFF, 0xD7, 0x00, 34),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = _pdfLayerTraceChoosingLayer
                ? new SKColor(0xFF, 0xA0, 0x00)
                : new SKColor(0x00, 0x96, 0x88),
            StrokeWidth = 2.0f / safeZoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([10f / safeZoom, 5f / safeZoom], 0),
        };
        canvas.DrawRect(candidate.Bounds, fill);
        canvas.DrawRect(candidate.Bounds, stroke);

        if (_pdfLayerTracePickPoint.HasValue)
        {
            float r = 5f / safeZoom;
            using var dot = new SKPaint
            {
                Color = new SKColor(0xFF, 0xA0, 0x00),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            canvas.DrawCircle(_pdfLayerTracePickPoint.Value, r, dot);
        }
    }

    private bool IsPdfLayerTraceSoloActive() =>
        _pdfLayerTraceEnabled && _pdfLayerTraceReadyToApply && _activePdfLayerTraceLayer.HasValue;

    private void DrawPdfLayerTraceGhost(SKCanvas canvas, SKRect pageRect)
    {
        if (!_pdfLayerTraceEnabled || IsPdfLayerTraceSoloActive())
            return;

        using var ghost = new SKPaint
        {
            Color = GetCachedColor(ViewBackgroundColor, SKColors.White).WithAlpha(190),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };
        canvas.DrawRect(pageRect, ghost);
    }
}
