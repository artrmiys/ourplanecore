using System;
using System.Collections.Generic;
using System.IO;
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

// ── Simple layer info returned to the UI ─────────────────────────────────────

public sealed record PdfLayer(int Number, string Name, bool IsOn, bool IsHighlighted = false);
public sealed record ViewportContextRequest(
    double ScreenX,
    double ScreenY,
    float PdfX,
    float PdfY,
    string PageFolder,
    Measurement? Measurement);

// ── Tool enum ────────────────────────────────────────────────────────────────

public enum ViewerTool { Pan, Scale, Point, Line, Area }

// ── Main control ─────────────────────────────────────────────────────────────

public sealed class PdfViewport : SKElement
{
    // ── PDF / render state ────────────────────────────────────────────────────
    private static readonly IDocLib _docLib = DocLib.Instance;

    private string  _pdfPath  = "";
    private int     _pdfIndex = 0;

    private SKBitmap? _pageBitmap;
    private float     _pdfW, _pdfH;        // page size in PDF points (1pt = 1/72 in)
    private float     _bitmapScale;         // bitmap pixels per PDF point
    private float     _renderedScale;

    // ── Viewport transform ────────────────────────────────────────────────────
    private float _zoom = 1f;              // screen pixels per PDF point
    private float _panX, _panY;            // PDF-point coord of top-left of screen

    // ── Pan drag ──────────────────────────────────────────────────────────────
    private Point? _dragStart;
    private float  _dragPanX0, _dragPanY0;
    private Point? _rightClickStart;
    private SKPoint? _rightClickPdf;
    private Measurement? _rightClickMeasurement;
    private bool _rightClickMoved;

    // ── Drawing tools ─────────────────────────────────────────────────────────
    private ViewerTool              _tool       = ViewerTool.Pan;
    private readonly List<SKPoint>  _drawPts    = [];   // in-progress PDF-space points
    private readonly List<int>      _tempIds    = [];   // canvas items to clear (not used in Skia, kept for parity)
    private SKPoint?                _rubberEnd;          // rubber-band endpoint

    // Scale calibration
    private readonly List<SKPoint> _scalePts = [];
    public  double   ScaleMetersPerPt { get; set; } = 0.0;
    public  string   ActiveColor      { get; set; } = "#FF4444";
    public  string   ActiveTakeoffFolder { get; set; } = "";
    public  UnitMode UnitMode         { get; set; } = UnitMode.Imperial;
    public  string   ViewBackgroundColor { get; set; } = "#FFFFFF";

    // ── Measurements ──────────────────────────────────────────────────────────
    private readonly List<Measurement> _measurements = [];
    private string _pageFolder = "";
    private Measurement? _selectedMeasurement;
    private int _selectedVertexIndex = -1;
    private bool _draggingVertex;
    private readonly Dictionary<int, bool> _layerStates = [];
    private readonly HashSet<int> _highlightedLayers = [];
    private List<PdfLayer> _layers = [];
    private IReadOnlyList<PdfLayerInfo>? _cachedLayers;
    private bool _usingLayerRenderer;
    private readonly System.Windows.Threading.DispatcherTimer _zoomRerenderTimer;
    private bool _zoomRerenderForce;
    private bool _repaintQueued;
    private bool _isViewDragging;
    private DateTime _lastPointerStatusAt = DateTime.MinValue;
    private readonly Dictionary<string, SKColor> _colorCache = new(StringComparer.OrdinalIgnoreCase);
    private LayerRenderRequest? _pendingLayerRender;
    private bool _layerRenderInProgress;
    private int _layerRenderVersion;

    private sealed record LayerRenderRequest(
        int Version,
        string PdfPath,
        int PdfIndex,
        string PageFolder,
        float RenderScale,
        bool ResetLayerStates,
        Dictionary<int, bool> LayerStates,
        HashSet<int> HighlightedLayers,
        IReadOnlyList<PdfLayerInfo>? CachedLayers,
        string? StatusAfter,
        bool FireLayersAfter);

    private sealed record LayerRenderCompletion(
        LayerRenderRequest Request,
        bool Ok,
        PdfLayerRenderResult Result,
        string Error);

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<string>?                          StatusChanged;
    public event Action<double>?                          ScaleChanged;
    public event Action<string>?                          ToolChanged;
    public event Action<IReadOnlyList<PdfLayer>>?         LayersChanged;
    public event Action<IReadOnlyList<PdfLayerInfo>>?     PdfLayersDiscovered;
    public event Action<Measurement>?                     MeasurementAdded;
    public event Action<Measurement>?                     MeasurementRemoved;
    public event Action<Measurement>?                     MeasurementChanged;
    public event Action<ViewportContextRequest>?          ContextRequested;

    // ── Constants ─────────────────────────────────────────────────────────────
    private const float ZoomMin    = 0.05f;
    private const float ZoomMax    = 16.0f;
    private const float RenderDpi  = 144f;           // initial render quality (2 px/pt)
    private static readonly float[] RenderScaleSteps = [0.75f, 1.00f, 1.50f, 2.25f, 3.00f, 4.00f];

    private static readonly SKColor TempColor = new(0xFF, 0xD7, 0x00);   // yellow
    private static readonly SKColor ScaleClr  = new(0x00, 0xE5, 0xFF);   // cyan

    // ── Constructor ───────────────────────────────────────────────────────────
    public PdfViewport()
    {
        Focusable     = true;
        ClipToBounds  = true;
        // Suppress WPF context menu so right-button is free for pan
        ContextMenu   = null;
        _zoomRerenderTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180),
        };
        _zoomRerenderTimer.Tick += (_, _) =>
        {
            _zoomRerenderTimer.Stop();
            bool force = _zoomRerenderForce;
            _zoomRerenderForce = false;
            RerenderForZoomIfNeeded(force);
            RequestRepaint();
        };
    }

    private void RequestRepaint()
    {
        if (_repaintQueued)
            return;

        _repaintQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            _repaintQueued = false;
            InvalidateVisual();
        }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Public API
    // ═════════════════════════════════════════════════════════════════════════

    public void LoadPage(
        string pdfPath,
        int pageIndex = 0,
        string pageFolder = "",
        IReadOnlyList<PdfLayerInfo>? cachedLayers = null)
    {
        _pdfPath    = pdfPath;
        _pdfIndex   = pageIndex;
        _pageFolder = pageFolder;
        _cachedLayers = cachedLayers;

        _pageBitmap?.Dispose();
        _pageBitmap = null;
        _drawPts.Clear();
        _scalePts.Clear();
        _rubberEnd = null;
        ClearSelection();
        _layerStates.Clear();
        _highlightedLayers.Clear();
        _layers.Clear();
        _usingLayerRenderer = false;
        _pendingLayerRender = null;
        _layerRenderVersion++;

        bool hasPreview = false;
        try
        {
            RenderPageWithDocnet(Math.Clamp(CurrentRenderScale(), 0.50f, 1.25f));
            hasPreview = true;
        }
        catch (Exception ex)
        {
            PostStatus($"Fast PDF preview unavailable: {ex.Message}");
        }

        // Fit after WPF has finished layout
        if (hasPreview)
        {
            Dispatcher.InvokeAsync(() =>
            {
                ZoomFit();
                QueueLayerRender(
                    resetLayerStates: true,
                    renderScale: CurrentRenderScale(),
                    statusAfter: $"Loaded: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}",
                    fireLayersAfter: true);
            });
        }
        else
        {
            QueueLayerRender(
                resetLayerStates: true,
                renderScale: CurrentRenderScale(),
                statusAfter: $"Loaded: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}",
                fireLayersAfter: true);
        }
        PostStatus($"Rendering: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}");

        // Fire layers event
        FireLayersChanged();
    }

    public void SetTool(string name)
    {
        _tool = name.ToLower() switch
        {
            "scale" => ViewerTool.Scale,
            "point" => ViewerTool.Point,
            "line"  => ViewerTool.Line,
            "area"  => ViewerTool.Area,
            _       => ViewerTool.Pan,
        };
        CancelDrawing();
        UpdateCursor();
        PostRecordPrompt();
    }

    public void ZoomFit()
    {
        if (_pdfW <= 0 || ActualWidth < 2 || ActualHeight < 2) return;
        _zoom = (float)Math.Min(ActualWidth / _pdfW, ActualHeight / _pdfH) * 0.95f;
        _panX = _panY = 0;
        ScheduleRerenderForZoom(force: true);
        RequestRepaint();
    }

    public void ZoomIn()  => ApplyZoom(1.25f, (float)(ActualWidth  / 2), (float)(ActualHeight / 2));
    public void ZoomOut() => ApplyZoom(0.80f, (float)(ActualWidth  / 2), (float)(ActualHeight / 2));

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
            new Dictionary<int, bool>(_layerStates),
            new HashSet<int>(_highlightedLayers),
            _cachedLayers,
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
                _highlightedLayers.Contains(layer.Number)))
            .ToList();
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

    // ── Undo ─────────────────────────────────────────────────────────────────

    public void UndoLast()
    {
        if (_drawPts.Count > 0)
        {
            _drawPts.RemoveAt(_drawPts.Count - 1);
            _rubberEnd = _drawPts.Count > 0 ? _rubberEnd : null;
            RequestRepaint();
            if (_drawPts.Count > 0)
                PostRecordPrompt();
            else
                PostStatus("Undo: drawing cleared.");
            return;
        }

        for (int i = _measurements.Count - 1; i >= 0; i--)
        {
            if (_measurements[i].PageFolder == _pageFolder)
            {
                var m = _measurements[i];
                _measurements.RemoveAt(i);
                if (ReferenceEquals(_selectedMeasurement, m))
                    ClearSelection();
                RequestRepaint();
                PostStatus($"Undo: removed {m.MType}");
                MeasurementRemoved?.Invoke(m);
                return;
            }
        }
        PostStatus("Nothing to undo on this page.");
    }

    // Remove specific measurements without firing MeasurementRemoved events
    // (caller handles model cleanup; this just keeps the render list consistent)
    public void DeleteMeasurements(IEnumerable<Measurement> toRemove)
    {
        foreach (var m in toRemove.ToList())
        {
            _measurements.Remove(m);
            if (ReferenceEquals(_selectedMeasurement, m))
                ClearSelection();
        }
        RequestRepaint();
    }

    // Bulk-load measurements restored from a saved file
    public void LoadMeasurements(IEnumerable<Measurement> measurements)
    {
        SetMeasurements(measurements);
    }

    public void FocusMeasurement(Measurement measurement)
    {
        if (!_measurements.Contains(measurement))
            return;

        SelectMeasurement(measurement, -1);
        CenterOnMeasurement(measurement);
        RequestRepaint();
        PostStatus($"Selected {ToolTitle(measurement.MType)} section. Delete removes it; drag blue handles to edit.");
    }

    public void SetMeasurements(IEnumerable<Measurement> measurements)
    {
        _measurements.Clear();
        _measurements.AddRange(measurements);
        ClearSelection();
        RequestRepaint();
    }

    public void ClearPage()
    {
        _pdfPath = "";
        _pdfIndex = 0;
        _pageFolder = "";
        _cachedLayers = null;
        _zoomRerenderTimer.Stop();
        _zoomRerenderForce = false;
        _isViewDragging = false;
        _pendingLayerRender = null;
        _layerRenderVersion++;
        _pageBitmap?.Dispose();
        _pageBitmap = null;
        _pdfW = _pdfH = 0;
        _drawPts.Clear();
        _scalePts.Clear();
        _rubberEnd = null;
        ClearSelection();
        RequestRepaint();
        FireLayersChanged();
    }

    public int GetPageCount(string pdfPath)
    {
        try
        {
            using var docReader = _docLib.GetDocReader(pdfPath, new PageDimensions(1.0));
            return docReader.GetPageCount();
        }
        catch { return 0; }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Rendering
    // ═════════════════════════════════════════════════════════════════════════

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(GetCachedColor(ViewBackgroundColor, SKColors.White));

        if (_pageBitmap == null) return;
        SKRect visiblePdf = GetVisiblePdfRect();

        // ── PDF page bitmap ───────────────────────────────────────────────────
        // PDF point (px,py) → screen pixel (sx,sy):
        //   sx = (px - panX) * zoom
        //   sy = (py - panY) * zoom
        // bitmap pixel (bx,by) → screen pixel:
        //   sx = bx * zoom/bitmapScale - panX*zoom
        {
            using var bitmapPaint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = _isViewDragging ? SKFilterQuality.Low : SKFilterQuality.Medium,
            };

            float visibleW = (float)ActualWidth / Math.Max(_zoom, 0.001f);
            float visibleH = (float)ActualHeight / Math.Max(_zoom, 0.001f);
            float srcLeft = Math.Clamp(_panX * _bitmapScale, 0, _pageBitmap.Width);
            float srcTop = Math.Clamp(_panY * _bitmapScale, 0, _pageBitmap.Height);
            float srcRight = Math.Clamp((_panX + visibleW) * _bitmapScale, 0, _pageBitmap.Width);
            float srcBottom = Math.Clamp((_panY + visibleH) * _bitmapScale, 0, _pageBitmap.Height);

            if (srcRight > srcLeft && srcBottom > srcTop)
            {
                var src = new SKRect(srcLeft, srcTop, srcRight, srcBottom);
                var dst = new SKRect(
                    (srcLeft / _bitmapScale - _panX) * _zoom,
                    (srcTop / _bitmapScale - _panY) * _zoom,
                    (srcRight / _bitmapScale - _panX) * _zoom,
                    (srcBottom / _bitmapScale - _panY) * _zoom);
                canvas.DrawBitmap(_pageBitmap, src, dst, bitmapPaint);
            }
        }

        // ── Measurement overlay (PDF-point coordinate system) ─────────────────
        {
            var measMtx = SKMatrix.CreateScaleTranslation(
                _zoom, _zoom, -_panX * _zoom, -_panY * _zoom);
            using var saved = new SKAutoCanvasRestore(canvas, true);
            canvas.SetMatrix(measMtx);
            DrawMeasurements(canvas, visiblePdf);
            DrawInProgress(canvas);
        }
    }

    // ── Draw finalized measurements ───────────────────────────────────────────

    private void DrawMeasurements(SKCanvas canvas, SKRect visiblePdf)
    {
        foreach (var m in _measurements)
        {
            if (m.PageFolder != _pageFolder) continue;
            if (!ReferenceEquals(m, _selectedMeasurement) && !IsMeasurementVisible(m, visiblePdf))
                continue;
            DrawMeasurement(canvas, m);
            if (ReferenceEquals(m, _selectedMeasurement))
                DrawSelectionHandles(canvas, m);
        }
    }

    private void DrawMeasurement(SKCanvas canvas, Measurement m)
    {
        SKColor color = GetCachedColor(m.Color, SKColors.Red);
        using var stroke = new SKPaint
        {
            Color       = color,
            StrokeWidth = (ReferenceEquals(m, _selectedMeasurement) ? 3f : 2f) / _zoom,       // constant screen width regardless of zoom
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
        };
        using var fill = new SKPaint
        {
            Color       = color.WithAlpha(180),
            IsAntialias = true,
            Style       = SKPaintStyle.Fill,
        };

        var pts = m.Points;

        switch (m.MType)
        {
            case "point":
                float r = 5f / _zoom;
                foreach (var p in pts)
                    canvas.DrawCircle(p, r, fill);
                break;

            case "line" when pts.Count >= 2:
                using (var path = new SKPath())
                {
                path.MoveTo(pts[0]);
                for (int i = 1; i < pts.Count; i++) path.LineTo(pts[i]);
                canvas.DrawPath(path, stroke);
                }
                float pr = 3f / _zoom;
                foreach (var p in pts)
                    canvas.DrawCircle(p, pr, fill);
                DrawLabel(canvas, pts[^1], m.Label(ScaleMetersPerPt, UnitMode), m.Color);
                break;

            case "area" when pts.Count >= 3:
                using (var poly = new SKPath())
                {
                poly.MoveTo(pts[0]);
                for (int i = 1; i < pts.Count; i++) poly.LineTo(pts[i]);
                poly.Close();
                using (var fillTrans = fill.Clone())
                {
                    fillTrans.Color = fillTrans.Color.WithAlpha(60);
                    canvas.DrawPath(poly, fillTrans);
                }
                canvas.DrawPath(poly, stroke);
                }
                var cen = Centroid(pts);
                DrawLabel(canvas, cen, m.Label(ScaleMetersPerPt, UnitMode), m.Color);
                break;
        }
    }

    private void DrawSelectionHandles(SKCanvas canvas, Measurement m)
    {
        if (m.Points.Count == 0) return;

        float radius = 5f / _zoom;
        using var fill = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var activeFill = new SKPaint
        {
            Color = TempColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = SKColors.DodgerBlue,
            StrokeWidth = 1.5f / _zoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        for (int i = 0; i < m.Points.Count; i++)
        {
            var rect = SKRect.Create(m.Points[i].X - radius, m.Points[i].Y - radius, radius * 2, radius * 2);
            canvas.DrawRect(rect, i == _selectedVertexIndex ? activeFill : fill);
            canvas.DrawRect(rect, stroke);
        }
    }

    private void DrawLabel(SKCanvas canvas, SKPoint pos, string text, string hexColor)
    {
        if (string.IsNullOrEmpty(text)) return;

        float fontSize = 9f / _zoom;
        using var textPaint = new SKPaint
        {
            Color       = SKColors.White,
            TextSize    = fontSize,
            IsAntialias = true,
            Typeface    = SKTypeface.FromFamilyName("Consolas"),
        };

        var bounds = new SKRect();
        textPaint.MeasureText(text, ref bounds);
        float pad = 2f / _zoom;
        var bg = new SKRect(pos.X + pad, pos.Y - bounds.Height - pad,
                            pos.X + bounds.Width + pad * 3, pos.Y + pad);

        using var bgPaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(180),
            Style = SKPaintStyle.Fill,
        };
        using var borderPaint = new SKPaint
        {
            Color       = GetCachedColor(hexColor, SKColors.DodgerBlue),
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 1f / _zoom,
        };
        canvas.DrawRect(bg, bgPaint);
        canvas.DrawRect(bg, borderPaint);
        canvas.DrawText(text, pos.X + pad * 1.5f, pos.Y - pad, textPaint);
    }

    // ── Draw in-progress line / area ──────────────────────────────────────────

    private void DrawInProgress(SKCanvas canvas)
    {
        using var tempPaint = new SKPaint
        {
            Color       = TempColor,
            StrokeWidth = 2f / _zoom,
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
        };
        using var dotPaint = new SKPaint
        {
            Color       = TempColor,
            IsAntialias = true,
            Style       = SKPaintStyle.Fill,
        };

        float r = 4f / _zoom;

        foreach (var p in _drawPts)
            canvas.DrawCircle(p, r, dotPaint);

        if (_drawPts.Count >= 2)
        {
            for (int i = 1; i < _drawPts.Count; i++)
                canvas.DrawLine(_drawPts[i - 1], _drawPts[i], tempPaint);
        }

        // Rubber-band
        if (_drawPts.Count > 0 && _rubberEnd.HasValue)
        {
            using var rubber = new SKPaint
            {
                Color       = TempColor,
                StrokeWidth = 1f / _zoom,
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
                PathEffect  = SKPathEffect.CreateDash([4f / _zoom, 4f / _zoom], 0),
            };
            canvas.DrawLine(_drawPts[^1], _rubberEnd.Value, rubber);
        }

        // Scale line
        if (_scalePts.Count >= 1)
        {
            using var scPaint = new SKPaint
            {
                Color       = ScaleClr,
                StrokeWidth = 2f / _zoom,
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
            };
            using var scDot = new SKPaint
            {
                Color       = ScaleClr,
                IsAntialias = true,
                Style       = SKPaintStyle.Fill,
            };
            float sr = 6f / _zoom;
            foreach (var p in _scalePts)
                canvas.DrawCircle(p, sr, scDot);
            if (_scalePts.Count == 2)
                canvas.DrawLine(_scalePts[0], _scalePts[1], scPaint);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Mouse / keyboard events
    // ═════════════════════════════════════════════════════════════════════════

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Focus();
        var pos    = e.GetPosition(this);
        float fac  = e.Delta > 0 ? 1.12f : 1f / 1.12f;
        ApplyZoom(fac, (float)pos.X, (float)pos.Y);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        var pos = e.GetPosition(this);

        if (e.ChangedButton == MouseButton.Right &&
            _drawPts.Count > 0 &&
            _tool is ViewerTool.Line or ViewerTool.Area)
        {
            CompleteOrCancelDrawing();
            e.Handled = true;
            return;
        }

        if (e.RightButton == MouseButtonState.Pressed && _pageBitmap != null)
        {
            var pdf = ScreenToPdf((float)pos.X, (float)pos.Y);
            _rightClickStart = pos;
            _rightClickPdf = pdf;
            _rightClickMoved = false;
            _rightClickMeasurement = null;
            if (TryHitMeasurement(pdf, out Measurement measurement))
            {
                _rightClickMeasurement = measurement;
                SelectMeasurement(measurement, -1);
            }
        }

        if (e.LeftButton == MouseButtonState.Pressed &&
            _tool == ViewerTool.Pan &&
            _pageBitmap != null)
        {
            var pdf = ScreenToPdf((float)pos.X, (float)pos.Y);
            if (TryBeginMeasurementEdit(pdf))
            {
                if (_draggingVertex)
                    CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        bool isPanButton = e.MiddleButton == MouseButtonState.Pressed
                        || e.RightButton  == MouseButtonState.Pressed
                        || (_tool == ViewerTool.Pan && e.LeftButton == MouseButtonState.Pressed);

        if (isPanButton)
        {
            _dragStart  = pos;
            _dragPanX0  = _panX;
            _dragPanY0  = _panY;
            CaptureMouse();
            _isViewDragging = true;
            e.Handled = true;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed && _pageBitmap != null)
        {
            var pdf = ScreenToPdf((float)pos.X, (float)pos.Y);
            ClearSelection();

            // Double-click (ClickCount==2) finishes a line/area without adding an extra point.
            // Single-click (ClickCount==1) adds a vertex as usual.
            if (e.ClickCount == 2 && _tool is ViewerTool.Line or ViewerTool.Area)
            {
                FinalizeDrawing();
                e.Handled = true;
                return;
            }

            HandleLeftClick(pdf);
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_rightClickStart.HasValue &&
            DistanceSquared(_rightClickStart.Value, pos) > 16)
        {
            _rightClickMoved = true;
        }

        if (_draggingVertex &&
            _selectedMeasurement != null &&
            _selectedVertexIndex >= 0 &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            _selectedMeasurement.Points[_selectedVertexIndex] = ScreenToPdf((float)pos.X, (float)pos.Y);
            MeasurementChanged?.Invoke(_selectedMeasurement);
            RequestRepaint();
            e.Handled = true;
            return;
        }

        // Pan drag
        if (_dragStart.HasValue && (
            e.MiddleButton == MouseButtonState.Pressed ||
            e.RightButton  == MouseButtonState.Pressed ||
            e.LeftButton   == MouseButtonState.Pressed))
        {
            _panX = _dragPanX0 - (float)((pos.X - _dragStart.Value.X) / _zoom);
            _panY = _dragPanY0 - (float)((pos.Y - _dragStart.Value.Y) / _zoom);
            RequestRepaint();
        }

        // Rubber-band
        if (_drawPts.Count > 0 && _tool is ViewerTool.Line or ViewerTool.Area)
        {
            _rubberEnd = ScreenToPdf((float)pos.X, (float)pos.Y);
            RequestRepaint();
        }

        PostPointerStatus(ScreenToPdf((float)pos.X, (float)pos.Y));

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        bool showContextMenu = e.ChangedButton == MouseButton.Right &&
                               _rightClickStart.HasValue &&
                               !_rightClickMoved &&
                               _rightClickPdf.HasValue &&
                               _pageBitmap != null;
        Point contextScreen = _rightClickStart ?? e.GetPosition(this);
        SKPoint contextPdf = _rightClickPdf ?? ScreenToPdf((float)contextScreen.X, (float)contextScreen.Y);
        Measurement? contextMeasurement = _rightClickMeasurement;

        if (_draggingVertex)
        {
            _draggingVertex = false;
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            RequestRepaint();
            e.Handled = true;
            return;
        }

        if (_dragStart.HasValue &&
            e.MiddleButton != MouseButtonState.Pressed &&
            e.RightButton  != MouseButtonState.Pressed &&
            e.LeftButton   != MouseButtonState.Pressed)
        {
            _dragStart = null;
            _isViewDragging = false;
            ReleaseMouseCapture();
            RequestRepaint();
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            _rightClickStart = null;
            _rightClickPdf = null;
            _rightClickMeasurement = null;
            _rightClickMoved = false;
        }

        if (showContextMenu)
        {
            ContextRequested?.Invoke(new ViewportContextRequest(
                contextScreen.X,
                contextScreen.Y,
                contextPdf.X,
                contextPdf.Y,
                _pageFolder,
                contextMeasurement));
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CompleteOrCancelDrawing();
                e.Handled = true;
                break;
            case Key.C:
                if (_drawPts.Count > 0 && _tool is ViewerTool.Line or ViewerTool.Area)
                {
                    CompleteOrCancelDrawing();
                    e.Handled = true;
                }
                break;
            case Key.Delete:
                DeleteSelectedMeasurement();
                e.Handled = true;
                break;
            case Key.F:
                ZoomFit();
                e.Handled = true;
                break;
            case Key.Add when Keyboard.Modifiers == ModifierKeys.Control:
            case Key.OemPlus when Keyboard.Modifiers == ModifierKeys.Control:
                ZoomIn(); e.Handled = true;
                break;
            case Key.Subtract when Keyboard.Modifiers == ModifierKeys.Control:
            case Key.OemMinus  when Keyboard.Modifiers == ModifierKeys.Control:
                ZoomOut(); e.Handled = true;
                break;
            case Key.Z when Keyboard.Modifiers == ModifierKeys.Control:
                UndoLast(); e.Handled = true;
                break;
            case Key.Back:
                UndoLast(); e.Handled = true;
                break;
            // Tool hotkeys
            case Key.V: ToolChanged?.Invoke("pan");   e.Handled = true; break;
            case Key.S: ToolChanged?.Invoke("scale"); e.Handled = true; break;
            case Key.P: ToolChanged?.Invoke("point"); e.Handled = true; break;
            case Key.L: ToolChanged?.Invoke("line");  e.Handled = true; break;
            case Key.A: ToolChanged?.Invoke("area");  e.Handled = true; break;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Drawing tool logic
    // ═════════════════════════════════════════════════════════════════════════

    private void HandleLeftClick(SKPoint pdf)
    {
        switch (_tool)
        {
            case ViewerTool.Scale:
                HandleScaleClick(pdf);
                break;
            case ViewerTool.Point:
                _drawPts.Add(pdf);
                FinalizeDrawing();
                break;
            case ViewerTool.Line:
            case ViewerTool.Area:
                _drawPts.Add(pdf);
                RequestRepaint();
                PostRecordPrompt();
                break;
        }
    }

    private void HandleScaleClick(SKPoint pdf)
    {
        _scalePts.Add(pdf);
        if (_scalePts.Count == 2)
        {
            float dx = _scalePts[1].X - _scalePts[0].X;
            float dy = _scalePts[1].Y - _scalePts[0].Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);  // PDF points

            const double PT_M = 25.4 / 72.0 / 1000.0;
            string hint =
                $"Measured {dist:F1} pt on PDF\n" +
                $"(At 1:100 ≈ {dist * PT_M * 100:F3} m  |  1:50 ≈ {dist * PT_M * 50:F3} m)\n\n" +
                "Enter real distance in metres:";

            var dlg = new ScaleInputDialog(hint);
            if (dlg.ShowDialog() == true && dlg.Value > 0)
            {
                ScaleMetersPerPt = dlg.Value / dist;
                ScaleChanged?.Invoke(ScaleMetersPerPt);
                PostStatus($"Scale set: 1:{ScaleMetersPerPt / PT_M:F0}  (1pt = {ScaleMetersPerPt:F6} m)");
            }

            _scalePts.Clear();
            RequestRepaint();
        }
        else
        {
            RequestRepaint();
            PostStatus("Scale: click the second point of a known distance.");
        }
    }

    private void FinalizeDrawing()
    {
        if (_drawPts.Count == 0) return;
        if (_tool == ViewerTool.Line  && _drawPts.Count < 2) { CancelDrawing(); return; }
        if (_tool == ViewerTool.Area  && _drawPts.Count < 3) { CancelDrawing(); return; }

        var m = new Measurement
        {
            MType      = _tool.ToString().ToLower(),
            Points     = new List<SKPoint>(_drawPts),
            Color      = ActiveColor,
            PageFolder = _pageFolder,
            TakeoffFolder = ActiveTakeoffFolder,
            ScaleMetersPerPt = ScaleMetersPerPt,
        };
        _measurements.Add(m);
        _drawPts.Clear();
        _rubberEnd = null;
        RequestRepaint();
        PostStatus($"Added {ToolTitle(m.MType)} section  {m.Label(ScaleMetersPerPt, UnitMode)}");
        MeasurementAdded?.Invoke(m);
        PostRecordPrompt();
    }

    private void CompleteOrCancelDrawing()
    {
        if (_tool == ViewerTool.Line && _drawPts.Count >= 2)
        {
            FinalizeDrawing();
            return;
        }

        if (_tool == ViewerTool.Area && _drawPts.Count >= 3)
        {
            FinalizeDrawing();
            return;
        }

        CancelDrawing();
        PostStatus("Cancelled.");
    }

    private void PostRecordPrompt()
    {
        switch (_tool)
        {
            case ViewerTool.Point:
                PostStatus("Count Record: click each item to add a count. Turn Record off for Pan.");
                break;
            case ViewerTool.Line:
                PostStatus(_drawPts.Count switch
                {
                    0 => "Line Record: click the first point.",
                    1 => "Line Record: click the next point. Backspace/Ctrl+Z undo.",
                    _ => "Line Record: click next point, or right-click / Esc / C / double-click to finish.",
                });
                break;
            case ViewerTool.Area:
                PostStatus(_drawPts.Count switch
                {
                    0 => "Area Record: click the first corner.",
                    1 => "Area Record: click the next corner. Backspace/Ctrl+Z undo.",
                    2 => "Area Record: click at least one more corner, then finish.",
                    _ => "Area Record: click next corner, or right-click / Esc / C / double-click to finish.",
                });
                break;
            case ViewerTool.Scale:
                PostStatus(_scalePts.Count == 0
                    ? "Scale: click the first point of a known distance."
                    : "Scale: click the second point of a known distance.");
                break;
        }
    }

    private static string ToolTitle(string type) =>
        type switch
        {
            "point" => "Count",
            "line" => "Line",
            "area" => "Area",
            _ => type,
        };

    private void CancelDrawing()
    {
        _drawPts.Clear();
        _scalePts.Clear();
        _rubberEnd = null;
        if (_draggingVertex && IsMouseCaptured)
            ReleaseMouseCapture();
        ClearSelection();
        RequestRepaint();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private bool TryBeginMeasurementEdit(SKPoint pdf)
    {
        if (TryHitVertex(pdf, out Measurement vertexMeasurement, out int vertexIndex))
        {
            SelectMeasurement(vertexMeasurement, vertexIndex);
            _draggingVertex = true;
            PostStatus($"Editing {vertexMeasurement.MType} vertex {vertexIndex + 1}. Drag to move.");
            return true;
        }

        if (TryHitMeasurement(pdf, out Measurement measurement))
        {
            SelectMeasurement(measurement, -1);
            PostStatus($"Selected {measurement.MType}. Drag a blue handle to edit, Delete to remove.");
            return true;
        }

        ClearSelection();
        RequestRepaint();
        return false;
    }

    private bool TryHitVertex(SKPoint pdf, out Measurement measurement, out int vertexIndex)
    {
        float tol = 8f / Math.Max(_zoom, 0.01f);
        float tolSq = tol * tol;

        for (int i = _measurements.Count - 1; i >= 0; i--)
        {
            Measurement m = _measurements[i];
            if (m.PageFolder != _pageFolder) continue;

            for (int p = m.Points.Count - 1; p >= 0; p--)
            {
                if (DistanceSquared(pdf, m.Points[p]) <= tolSq)
                {
                    measurement = m;
                    vertexIndex = p;
                    return true;
                }
            }
        }

        measurement = null!;
        vertexIndex = -1;
        return false;
    }

    private bool TryHitMeasurement(SKPoint pdf, out Measurement measurement)
    {
        float tol = 6f / Math.Max(_zoom, 0.01f);

        for (int i = _measurements.Count - 1; i >= 0; i--)
        {
            Measurement m = _measurements[i];
            if (m.PageFolder != _pageFolder) continue;

            if (m.MType == "point")
            {
                if (m.Points.Any(p => DistanceSquared(pdf, p) <= tol * tol))
                {
                    measurement = m;
                    return true;
                }
                continue;
            }

            for (int p = 1; p < m.Points.Count; p++)
            {
                if (DistanceToSegment(pdf, m.Points[p - 1], m.Points[p]) <= tol)
                {
                    measurement = m;
                    return true;
                }
            }

            if (m.MType == "area" && m.Points.Count > 2 &&
                DistanceToSegment(pdf, m.Points[^1], m.Points[0]) <= tol)
            {
                measurement = m;
                return true;
            }
        }

        measurement = null!;
        return false;
    }

    private void SelectMeasurement(Measurement measurement, int vertexIndex)
    {
        _selectedMeasurement = measurement;
        _selectedVertexIndex = vertexIndex;
        RequestRepaint();
    }

    private void CenterOnMeasurement(Measurement measurement)
    {
        if (measurement.Points.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0 || _zoom <= 0)
            return;

        SKRect bounds = MeasurementBounds(measurement);
        float centerX = (bounds.Left + bounds.Right) / 2f;
        float centerY = (bounds.Top + bounds.Bottom) / 2f;
        float visibleW = (float)ActualWidth / _zoom;
        float visibleH = (float)ActualHeight / _zoom;

        _panX = centerX - visibleW / 2f;
        _panY = centerY - visibleH / 2f;
        ClampPanToPage();
        ScheduleRerenderForZoom(force: false);
    }

    private void ClampPanToPage()
    {
        if (_pdfW <= 0 || _pdfH <= 0 || ActualWidth <= 0 || ActualHeight <= 0 || _zoom <= 0)
            return;

        float visibleW = (float)ActualWidth / _zoom;
        float visibleH = (float)ActualHeight / _zoom;
        _panX = Math.Clamp(_panX, 0, Math.Max(0, _pdfW - visibleW));
        _panY = Math.Clamp(_panY, 0, Math.Max(0, _pdfH - visibleH));
    }

    private void ClearSelection()
    {
        _selectedMeasurement = null;
        _selectedVertexIndex = -1;
        _draggingVertex = false;
    }

    private void DeleteSelectedMeasurement()
    {
        if (_selectedMeasurement == null)
            return;

        Measurement removed = _selectedMeasurement;
        _measurements.Remove(removed);
        ClearSelection();
        RequestRepaint();
        PostStatus($"Deleted {removed.MType}.");
        MeasurementRemoved?.Invoke(removed);
    }

    private static float DistanceSquared(SKPoint a, SKPoint b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static double DistanceSquared(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
    {
        float vx = b.X - a.X;
        float vy = b.Y - a.Y;
        float lenSq = vx * vx + vy * vy;
        if (lenSq <= 0.0001f)
            return MathF.Sqrt(DistanceSquared(p, a));

        float t = ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / lenSq;
        t = Math.Clamp(t, 0f, 1f);
        var projection = new SKPoint(a.X + t * vx, a.Y + t * vy);
        return MathF.Sqrt(DistanceSquared(p, projection));
    }

    private void ApplyZoom(float factor, float screenX, float screenY)
    {
        float newZoom = Math.Clamp(_zoom * factor, ZoomMin, ZoomMax);
        if (Math.Abs(newZoom - _zoom) < 0.0001f) return;

        // Keep the PDF point under cursor fixed
        float pdfX = screenX / _zoom + _panX;
        float pdfY = screenY / _zoom + _panY;
        _zoom  = newZoom;
        _panX  = pdfX - screenX / _zoom;
        _panY  = pdfY - screenY / _zoom;
        ScheduleRerenderForZoom(force: false);
        RequestRepaint();
        PostStatus($"Zoom: {_zoom * 100:F0}%");
    }

    private float CurrentRenderScale()
    {
        if (_zoom <= 0)
            return 1.0f;

        float desired = Math.Clamp(_zoom, RenderScaleSteps[0], RenderScaleSteps[^1]);
        foreach (float step in RenderScaleSteps)
        {
            if (desired <= step)
                return step;
        }
        return RenderScaleSteps[^1];
    }

    private void RerenderForZoomIfNeeded(bool force)
    {
        if (string.IsNullOrWhiteSpace(_pdfPath) || _pdfW <= 0 || _pdfH <= 0)
            return;

        float desired = CurrentRenderScale();
        if (!force && _renderedScale > 0)
        {
            float ratio = desired / _renderedScale;
            if (ratio > 0.72f && ratio < 1.38f)
                return;
        }

        if (_usingLayerRenderer)
        {
            QueueLayerRender(resetLayerStates: false, renderScale: desired);
            return;
        }

        try
        {
            RenderPageWithDocnet(desired);
        }
        catch (Exception ex)
        {
            PostStatus($"Render error: {ex.Message}");
        }
    }

    private void ScheduleRerenderForZoom(bool force)
    {
        if (string.IsNullOrWhiteSpace(_pdfPath) || _pdfW <= 0 || _pdfH <= 0)
            return;

        if (!force && _renderedScale > 0)
        {
            float desired = CurrentRenderScale();
            float ratio = desired / _renderedScale;
            if (ratio > 0.72f && ratio < 1.38f)
                return;
        }

        _zoomRerenderForce = _zoomRerenderForce || force;
        _zoomRerenderTimer.Stop();
        _zoomRerenderTimer.Start();
    }

    private void PostPointerStatus(SKPoint p)
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastPointerStatusAt).TotalMilliseconds < 50)
            return;

        _lastPointerStatusAt = now;
        string scaleStr = ScaleMetersPerPt > 0
            ? $"  |  1:{ScaleMetersPerPt / (25.4 / 72.0 / 1000.0):F0}"
            : "  |  scale: not set";
        PostStatus($"x={p.X:F1}  y={p.Y:F1} pt  |  zoom: {_zoom * 100:F0}%{scaleStr}");
    }

    private SKPoint ScreenToPdf(float sx, float sy)
        => new(sx / _zoom + _panX, sy / _zoom + _panY);

    private SKRect GetVisiblePdfRect(float screenPadding = 64f)
    {
        float safeZoom = Math.Max(_zoom, 0.001f);
        float pad = screenPadding / safeZoom;
        float visibleW = (float)ActualWidth / safeZoom;
        float visibleH = (float)ActualHeight / safeZoom;
        return new SKRect(
            _panX - pad,
            _panY - pad,
            _panX + visibleW + pad,
            _panY + visibleH + pad);
    }

    private static bool IsMeasurementVisible(Measurement measurement, SKRect visiblePdf)
    {
        if (measurement.Points.Count == 0)
            return false;

        SKRect bounds = MeasurementBounds(measurement);
        return bounds.Left <= visiblePdf.Right &&
               bounds.Right >= visiblePdf.Left &&
               bounds.Top <= visiblePdf.Bottom &&
               bounds.Bottom >= visiblePdf.Top;
    }

    private static SKRect MeasurementBounds(Measurement measurement)
    {
        float left = float.PositiveInfinity;
        float top = float.PositiveInfinity;
        float right = float.NegativeInfinity;
        float bottom = float.NegativeInfinity;

        foreach (var point in measurement.Points)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }

        var bounds = new SKRect(left, top, right, bottom);
        bounds.Inflate(96f, 96f);
        return bounds;
    }

    private static SKPoint Centroid(List<SKPoint> pts)
        => new(pts.Average(p => p.X), pts.Average(p => p.Y));

    private SKColor GetCachedColor(string hex, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        if (_colorCache.TryGetValue(hex, out SKColor color))
            return color;

        color = ParseColor(hex, fallback);
        _colorCache[hex] = color;
        return color;
    }

    private static SKColor ParseColor(string hex, SKColor fallback)
    {
        try
        {
            return SKColor.Parse(hex);
        }
        catch
        {
            return fallback;
        }
    }

    private void UpdateCursor()
    {
        Cursor = _tool switch
        {
            ViewerTool.Pan => Cursors.Hand,
            _              => Cursors.Cross,
        };
    }

    private void PostStatus(string msg) => StatusChanged?.Invoke(msg);

    private string LayerName(int layerNumber) =>
        _layers.FirstOrDefault(layer => layer.Number == layerNumber)?.Name ?? $"Layer {layerNumber}";

    private void FireLayersChanged()
    {
        LayersChanged?.Invoke(_layers);
    }
}
