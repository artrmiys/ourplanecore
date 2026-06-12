using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed record ViewportSimilarCountRequest(SKRect PdfRect, string PageFolder);

// "Count similar symbols": rubber-band template selection (mirrors the AI
// crop note interaction) plus a ghost-marker preview layer for the matches
// while the threshold dialog is open.
public sealed partial class PdfViewport
{
    private const float SimilarCountMinimumBitmapScale = 0.95f;
    private const float SimilarCountRequestedBitmapScale = 1.0f;

    private bool _similarCountSelecting;
    private bool _similarCountDragging;
    private SKPoint _similarCountStartPdf;
    private SKPoint _similarCountEndPdf;
    private IReadOnlyList<SKPoint>? _similarCountPreview;

    public event Action<ViewportSimilarCountRequest>? SimilarCountSelectionCompleted;

    public void BeginSimilarCountSelection()
    {
        if (!TryEnsureSimilarCountBitmapReady(out string status))
        {
            PostStatus(status);
            return;
        }

        CancelDrawing(clearSelection: false);
        _similarCountSelecting = true;
        _similarCountDragging = false;
        Focus();
        RequestRepaint();
        PostStatus("Count similar: left-drag a tight box around ONE symbol. Esc cancels.");
    }

    public void SetSimilarCountPreview(IReadOnlyList<SKPoint>? centersPdf)
    {
        _similarCountPreview = centersPdf;
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

        if (_pageBitmap == null || _pdfW <= 0 || _pdfH <= 0 || _bitmapScale <= 0)
        {
            status = "Count similar: open a rendered sheet first.";
            return false;
        }

        if (_bitmapScale >= SimilarCountMinimumBitmapScale)
        {
            status = "";
            return true;
        }

        QueueSimilarCountReadableBitmap();
        status = $"Count similar: sheet is still sharpening ({_bitmapScale:0.##}x). Try again in a moment.";
        return false;
    }

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
                    statusAfter: "Count similar: sharper sheet ready. Drag a box around the pattern again.",
                    allowImmediateCache: true,
                    allowLiveRender: true,
                    allowMemoryBitmap: true);
            }
            else
            {
                QueueDocnetRender(
                    SimilarCountRequestedBitmapScale,
                    statusAfter: "Count similar: sharper sheet ready. Drag a box around the pattern again.");
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
        if (!_similarCountSelecting)
            return false;

        _similarCountStartPdf = ClampAiCropPoint(pdf);
        _similarCountEndPdf = _similarCountStartPdf;
        _similarCountDragging = true;
        CaptureMouse();
        RequestRepaint();
        return true;
    }

    private bool HandleSimilarCountMouseMove(Point screen)
    {
        if (!_similarCountSelecting || !_similarCountDragging)
            return false;

        _similarCountEndPdf = ClampAiCropPoint(ScreenToPdf((float)screen.X, (float)screen.Y));
        _lastPointerPdf = _similarCountEndPdf;
        RequestRepaint();
        return true;
    }

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
        if (!_similarCountSelecting)
            return false;

        ResetSimilarCountSelection();
        if (postStatus)
            PostStatus("Count similar cancelled.");
        RequestRepaint();
        return true;
    }

    private void ResetSimilarCountSelection()
    {
        _similarCountSelecting = false;
        _similarCountDragging = false;
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
        IReadOnlyList<SKPoint>? preview = _similarCountPreview;
        if (preview == null || preview.Count == 0)
            return;

        float safeZoom = Math.Max(_zoom, 0.001f);
        float radius = 7f / safeZoom;
        using var fill = new SKPaint
        {
            Color = new SKColor(0x21, 0x96, 0xF3, 110),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = new SKColor(0x0D, 0x47, 0xA1),
            StrokeWidth = 1.6f / safeZoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        foreach (SKPoint center in preview)
        {
            canvas.DrawCircle(center, radius, fill);
            canvas.DrawCircle(center, radius, stroke);
        }
    }
}
