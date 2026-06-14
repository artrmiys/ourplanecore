using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed record ViewportSimilarCountRequest(SKRect PdfRect, string PageFolder);
public sealed record ViewportSimilarCountPreviewMarker(
    SKPoint CenterPdf,
    bool Included,
    float Score = 1f,
    int RotationDegrees = 0,
    bool Mirrored = false,
    bool AlreadyCounted = false);

// "Count similar symbols": rubber-band template selection (mirrors the AI
// crop note interaction) plus a ghost-marker preview layer for the matches
// while the threshold dialog is open.
public sealed partial class PdfViewport
{
    private const float SimilarCountMinimumBitmapScale = 1.25f;
    private const float SimilarCountRequestedBitmapScale = 1.5f;

    private bool _similarCountSelecting;
    private bool _similarCountDragging;
    private bool _similarCountWaitingForReadableBitmap;
    private string _similarCountWaitingPdfPath = "";
    private int _similarCountWaitingPdfIndex = -1;
    private string _similarCountWaitingPageFolder = "";
    private SKPoint _similarCountStartPdf;
    private SKPoint _similarCountEndPdf;
    private IReadOnlyList<ViewportSimilarCountPreviewMarker>? _similarCountPreview;
    private string _similarCountPreviewPageFolder = "";
    private int _similarCountHoverMarkerIndex = -1;

    public event Action<ViewportSimilarCountRequest>? SimilarCountSelectionCompleted;
    public event Action<int>? SimilarCountPreviewMarkerToggled;

    public void BeginSimilarCountSelection()
    {
        if (!TryEnsureSimilarCountBitmapReady(out string status))
        {
            if (CanWaitForSimilarCountReadableBitmap())
                StartWaitingForSimilarCountReadableBitmap();
            PostStatus(status);
            return;
        }

        StartSimilarCountSelection();
    }

    private void StartSimilarCountSelection()
    {
        _similarCountWaitingForReadableBitmap = false;
        CancelDrawing(clearSelection: false);
        _similarCountSelecting = true;
        _similarCountDragging = false;
        Focus();
        RequestRepaint();
        PostStatus("Count similar: left-drag a tight box around ONE symbol. Esc cancels.");
    }

    private bool CanWaitForSimilarCountReadableBitmap() =>
        HasSimilarCountRenderTarget() &&
        (!HasCurrentSimilarCountBitmap() ||
         _pdfW <= 0 ||
         _pdfH <= 0 ||
         _bitmapScale <= 0 ||
         _bitmapScale < SimilarCountMinimumBitmapScale);

    private void StartWaitingForSimilarCountReadableBitmap()
    {
        _similarCountWaitingForReadableBitmap = true;
        _similarCountWaitingPdfPath = _pdfPath;
        _similarCountWaitingPdfIndex = _pdfIndex;
        _similarCountWaitingPageFolder = _pageFolder;
    }

    private void TryStartPendingSimilarCountSelection()
    {
        if (!_similarCountWaitingForReadableBitmap)
            return;

        if (_similarCountSelecting || _similarCountDragging)
            return;

        if (_pdfIndex != _similarCountWaitingPdfIndex ||
            !string.Equals(_pdfPath, _similarCountWaitingPdfPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_pageFolder, _similarCountWaitingPageFolder, StringComparison.OrdinalIgnoreCase))
        {
            ResetSimilarCountSelection();
            return;
        }

        if (_pageBitmap == null ||
            _bitmapScale < SimilarCountMinimumBitmapScale ||
            !HasCurrentSimilarCountBitmap())
        {
            return;
        }

        StartSimilarCountSelection();
    }

    public void SetSimilarCountPreview(IReadOnlyList<SKPoint>? centersPdf)
    {
        _similarCountPreview = centersPdf?
            .Select(center => new ViewportSimilarCountPreviewMarker(center, Included: true))
            .ToList();
        _similarCountPreviewPageFolder = _similarCountPreview is { Count: > 0 } ? _pageFolder : "";
        _similarCountHoverMarkerIndex = -1;
        RequestRepaint();
    }

    public void SetSimilarCountPreviewMarkers(
        IReadOnlyList<ViewportSimilarCountPreviewMarker>? markers,
        string? pageFolder = null)
    {
        _similarCountPreview = markers;
        _similarCountPreviewPageFolder = markers is { Count: > 0 } ? pageFolder ?? _pageFolder : "";
        _similarCountHoverMarkerIndex = -1;
        RequestRepaint();
    }

    public bool TryCreateSimilarCountSession(
        SKRect pdfRect,
        out SimilarSymbolMatchSession? session,
        out float bitmapScale,
        out string error)
    {
        session = null;
        bitmapScale = 0f;
        if (!TryEnsureSimilarCountBitmapReady(out error))
        {
            return false;
        }

        SKBitmap pageBitmap = _pageBitmap!;
        bitmapScale = _bitmapScale;
        var pixelRect = new SKRectI(
            (int)Math.Floor(pdfRect.Left * _bitmapScale),
            (int)Math.Floor(pdfRect.Top * _bitmapScale),
            (int)Math.Ceiling(pdfRect.Right * _bitmapScale),
            (int)Math.Ceiling(pdfRect.Bottom * _bitmapScale));
        session = SimilarSymbolMatchSession.TryCreate(pageBitmap, pixelRect, out error);
        return session != null;
    }

    private bool TryEnsureSimilarCountBitmapReady(out string status)
    {
        PrepareBitmapForImmediateRepaint();

        if (!HasSimilarCountRenderTarget())
        {
            status = "Count similar: open a sheet first.";
            return false;
        }

        if (!HasCurrentSimilarCountBitmap() ||
            _pdfW <= 0 ||
            _pdfH <= 0 ||
            _bitmapScale <= 0)
        {
            QueueSimilarCountReadableBitmap();
            status = "Count similar: sheet is rendering. Selection will start automatically.";
            return false;
        }

        if (_bitmapScale >= SimilarCountMinimumBitmapScale)
        {
            status = "";
            return true;
        }

        QueueSimilarCountReadableBitmap();
        if (_bitmapScale >= SimilarCountMinimumBitmapScale)
        {
            status = "";
            return true;
        }

        status = $"Count similar: sheet is sharpening ({_bitmapScale:0.##}x). Selection will start automatically.";
        return false;
    }

    private bool HasSimilarCountRenderTarget() =>
        !string.IsNullOrWhiteSpace(_pdfPath) &&
        _pdfIndex >= 0 &&
        !string.IsNullOrWhiteSpace(_pageFolder);

    private bool HasCurrentSimilarCountBitmap() =>
        IsPageBitmapFor(_pdfPath, _pdfIndex, _pageFolder);

    private void QueueSimilarCountReadableBitmap()
    {
        if (string.IsNullOrWhiteSpace(_pdfPath) || _pdfIndex < 0)
            return;

        try
        {
            if (TryApplyReadyRasterSheetForCurrentZoom() ||
                _bitmapScale >= SimilarCountMinimumBitmapScale)
            {
                RequestRepaint();
                return;
            }

            if (_rasterSheetSource?.Enabled == true &&
                !_pdfLayersLoadedForPage &&
                !_usingLayerRenderer &&
                !RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource))
            {
                QueueRasterSheetBitmapApplyAfterWarmup(
                    _pdfPath,
                    _pdfIndex,
                    _pageFolder,
                    _rasterSheetSource,
                    preferOverview: false,
                    allowLowZoomFullRaster: true);
            }
            else if (_usingLayerRenderer)
            {
                QueueLayerRender(
                    resetLayerStates: false,
                    renderScale: SimilarCountRequestedBitmapScale,
                    statusAfter: "Count similar: sharper sheet ready.",
                    allowImmediateCache: true,
                    allowLiveRender: true,
                    allowMemoryBitmap: true);
            }
            else
            {
                QueueDocnetRender(
                    SimilarCountRequestedBitmapScale,
                    statusAfter: "Count similar: sharper sheet ready.");
            }

            RequestRepaint();
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Similar count readable bitmap request failed.");
        }
    }

    private bool HandleSimilarCountMouseDown(SKPoint pdf)
    {
        if (TryToggleSimilarCountPreviewMarker(pdf))
            return true;

        if (!_similarCountSelecting)
            return false;

        _similarCountStartPdf = ClampAiCropPoint(pdf);
        _similarCountEndPdf = _similarCountStartPdf;
        _similarCountDragging = true;
        CaptureMouse();
        RequestRepaint();
        return true;
    }

    private bool TryToggleSimilarCountPreviewMarker(SKPoint pdf)
    {
        if (_similarCountSelecting)
            return false;

        int bestIndex = SimilarCountPreviewMarkerHitIndex(pdf);
        if (bestIndex < 0)
            return false;

        SimilarCountPreviewMarkerToggled?.Invoke(bestIndex);
        return true;
    }

    private bool HandleSimilarCountMouseMove(Point screen)
    {
        if (!_similarCountSelecting || !_similarCountDragging)
        {
            if (Mouse.LeftButton == MouseButtonState.Released &&
                Mouse.MiddleButton == MouseButtonState.Released &&
                Mouse.RightButton == MouseButtonState.Released)
            {
                return TryPostSimilarCountPreviewMarkerStatus(ScreenToPdf((float)screen.X, (float)screen.Y));
            }

            return false;
        }

        _similarCountEndPdf = ClampAiCropPoint(ScreenToPdf((float)screen.X, (float)screen.Y));
        _lastPointerPdf = _similarCountEndPdf;
        PostSimilarCountSelectionStatus();
        RequestRepaint();
        return true;
    }

    private void PostSimilarCountSelectionStatus()
    {
        SKRect rect = NormalizeRect(_similarCountStartPdf, _similarCountEndPdf);
        float widthPx = PdfToScreenDistance(rect.Width);
        float heightPx = PdfToScreenDistance(rect.Height);
        string size = $"{widthPx:0} x {heightPx:0}px";
        string hint = widthPx < 8f || heightPx < 8f
            ? "drag a larger box"
            : "keep it tight around one symbol";
        PostStatus($"Count similar: selecting {size}; {hint}.");
    }

    private int SimilarCountPreviewMarkerHitIndex(SKPoint pdf)
    {
        IReadOnlyList<ViewportSimilarCountPreviewMarker>? preview = _similarCountPreview;
        if (preview == null || preview.Count == 0 || !IsSimilarCountPreviewForCurrentPage())
            return -1;

        float tolerance = ScreenToPdfDistance(13f);
        float bestDistanceSq = tolerance * tolerance;
        int bestIndex = -1;
        for (int i = 0; i < preview.Count; i++)
        {
            SKPoint center = preview[i].CenterPdf;
            float dx = center.X - pdf.X;
            float dy = center.Y - pdf.Y;
            float distanceSq = dx * dx + dy * dy;
            if (distanceSq >= bestDistanceSq)
                continue;

            bestDistanceSq = distanceSq;
            bestIndex = i;
        }

        return bestIndex;
    }

    private bool TryPostSimilarCountPreviewMarkerStatus(SKPoint pdf)
    {
        int index = SimilarCountPreviewMarkerHitIndex(pdf);
        if (index < 0)
        {
            _similarCountHoverMarkerIndex = -1;
            return false;
        }

        if (_similarCountHoverMarkerIndex == index)
            return true;

        _similarCountHoverMarkerIndex = index;
        ViewportSimilarCountPreviewMarker marker = _similarCountPreview![index];
        PostStatus(SimilarCountPreviewMarkerStatus(marker));
        return true;
    }

    private static string SimilarCountPreviewMarkerStatus(ViewportSimilarCountPreviewMarker marker)
    {
        string confidence = marker.Score > 0f &&
                            marker.Score < (float)AppSettingsStore.SimilarCountThresholdDefault
            ? "weak"
            : "strong";
        string state = marker.AlreadyCounted
            ? "already counted"
            : marker.Included ? "included" : "excluded";
        string action = marker.AlreadyCounted
            ? "locked"
            : marker.Included ? "click to exclude" : "click to include";
        string variant = marker.RotationDegrees != 0 || marker.Mirrored
            ? $" | {(marker.Mirrored ? "mirrored" : "normal")}, rotated {marker.RotationDegrees}°"
            : "";

        return $"Count similar marker: {confidence} {marker.Score:0.00}, {state}; {action}{variant}.";
    }

    private bool IsSimilarCountPreviewForCurrentPage() =>
        !string.IsNullOrWhiteSpace(_similarCountPreviewPageFolder) &&
        string.Equals(_similarCountPreviewPageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase);

    private bool FinishSimilarCountSelection()
    {
        if (!_similarCountSelecting || !_similarCountDragging)
            return false;

        SKRect rect = NormalizeRect(_similarCountStartPdf, _similarCountEndPdf);
        bool tooSmall = PdfToScreenDistance(rect.Width) < 8f ||
                        PdfToScreenDistance(rect.Height) < 8f;
        string pageFolder = _pageFolder;
        ResetSimilarCountSelection();

        if (tooSmall)
        {
            PostStatus("Count similar cancelled: the box is too small.");
            RequestRepaint();
            return true;
        }

        RequestRepaint();
        SimilarCountSelectionCompleted?.Invoke(new ViewportSimilarCountRequest(rect, pageFolder));
        return true;
    }

    private bool CancelSimilarCountSelection(bool postStatus = true)
    {
        if (!_similarCountSelecting && !_similarCountWaitingForReadableBitmap)
            return false;

        ResetSimilarCountSelection();
        if (postStatus)
            PostStatus("Count similar cancelled.");
        RequestRepaint();
        return true;
    }

    private void ResetSimilarCountSelection()
    {
        _similarCountWaitingForReadableBitmap = false;
        _similarCountWaitingPdfPath = "";
        _similarCountWaitingPdfIndex = -1;
        _similarCountWaitingPageFolder = "";
        _similarCountSelecting = false;
        _similarCountDragging = false;
        _similarCountHoverMarkerIndex = -1;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private void DrawSimilarCountSelectionOverlay(SKCanvas canvas)
    {
        if (!_similarCountSelecting)
            return;

        float safeZoom = Math.Max(_zoom, 0.001f);
        SKRect rect = NormalizeRect(_similarCountStartPdf, _similarCountEndPdf);
        using var fill = new SKPaint
        {
            Color = new SKColor(0x21, 0x96, 0xF3, 40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = new SKColor(0x21, 0x96, 0xF3),
            StrokeWidth = 2f / safeZoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([9f / safeZoom, 4f / safeZoom], 0),
        };
        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, stroke);
    }

    private void DrawSimilarCountPreviewOverlay(SKCanvas canvas)
    {
        IReadOnlyList<ViewportSimilarCountPreviewMarker>? preview = _similarCountPreview;
        if (preview == null || preview.Count == 0 || !IsSimilarCountPreviewForCurrentPage())
            return;

        float safeZoom = Math.Max(_zoom, 0.001f);
        float radius = 7f / safeZoom;
        using var includedFill = new SKPaint
        {
            Color = new SKColor(0x21, 0x96, 0xF3, 110),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var includedStroke = new SKPaint
        {
            Color = new SKColor(0x0D, 0x47, 0xA1),
            StrokeWidth = 1.6f / safeZoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        using var weakFill = new SKPaint
        {
            Color = new SKColor(0xFB, 0x8C, 0x00, 120),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var weakStroke = new SKPaint
        {
            Color = new SKColor(0xE6, 0x51, 0x00),
            StrokeWidth = 1.8f / safeZoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        using var variantStroke = new SKPaint
        {
            Color = new SKColor(0x6A, 0x1B, 0x9A),
            StrokeWidth = 1.2f / safeZoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        using var countedStroke = new SKPaint
        {
            Color = new SKColor(0x54, 0x6E, 0x7A),
            StrokeWidth = 1.8f / safeZoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        using var excludedStroke = new SKPaint
        {
            Color = new SKColor(0xC6, 0x28, 0x28),
            StrokeWidth = 1.8f / safeZoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        foreach (ViewportSimilarCountPreviewMarker marker in preview)
        {
            SKPoint center = marker.CenterPdf;
            bool weakerMatch = marker.Score > 0f &&
                                marker.Score < (float)AppSettingsStore.SimilarCountThresholdDefault;
            if (marker.AlreadyCounted)
            {
                canvas.DrawCircle(center, radius, countedStroke);
                float check = radius * 0.7f;
                canvas.DrawLine(
                    new SKPoint(center.X - check, center.Y),
                    new SKPoint(center.X - check * 0.15f, center.Y + check * 0.65f),
                    countedStroke);
                canvas.DrawLine(
                    new SKPoint(center.X - check * 0.15f, center.Y + check * 0.65f),
                    new SKPoint(center.X + check, center.Y - check * 0.55f),
                    countedStroke);
                continue;
            }

            if (marker.Included)
            {
                canvas.DrawCircle(center, radius, weakerMatch ? weakFill : includedFill);
                canvas.DrawCircle(center, radius, weakerMatch ? weakStroke : includedStroke);
                if (marker.RotationDegrees != 0 || marker.Mirrored)
                    canvas.DrawCircle(center, radius * 1.45f, variantStroke);
                continue;
            }

            canvas.DrawCircle(center, radius, weakerMatch ? weakStroke : excludedStroke);
            float slash = radius * 0.72f;
            canvas.DrawLine(
                new SKPoint(center.X - slash, center.Y - slash),
                new SKPoint(center.X + slash, center.Y + slash),
                excludedStroke);
            canvas.DrawLine(
                new SKPoint(center.X - slash, center.Y + slash),
                new SKPoint(center.X + slash, center.Y - slash),
                excludedStroke);
        }
    }
}
