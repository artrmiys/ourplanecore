using System;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    public void BeginAiCropNoteSelection(SKPoint anchorPdf)
    {
        if (_pageBitmap == null || _pdfW <= 0 || _pdfH <= 0)
        {
            PostStatus("AI crop note: open a rendered sheet first.");
            return;
        }

        CancelDrawing(clearSelection: false);
        _aiCropNoteAnchorPdf = ClampAiCropPoint(anchorPdf);
        _aiCropNoteStartPdf = _aiCropNoteAnchorPdf;
        _aiCropNoteEndPdf = _aiCropNoteAnchorPdf;
        _aiCropNoteSelecting = true;
        _aiCropNoteDragging = false;
        Focus();
        RequestRepaint();
        PostStatus("AI crop note: left-drag the area to read. Right-drag pans. Esc cancels.");
    }

    private bool HandleAiCropNoteMouseDown(SKPoint pdf)
    {
        if (!_aiCropNoteSelecting)
            return false;

        _aiCropNoteStartPdf = ClampAiCropPoint(pdf);
        _aiCropNoteEndPdf = _aiCropNoteStartPdf;
        _aiCropNoteDragging = true;
        CaptureMouse();
        RequestRepaint();
        PostStatus("AI crop note: drag around the text, schedule, detail, or note.");
        return true;
    }

    private bool HandleAiCropNoteMouseMove(Point screen)
    {
        if (!_aiCropNoteSelecting || !_aiCropNoteDragging)
            return false;

        _aiCropNoteEndPdf = ClampAiCropPoint(ScreenToPdf((float)screen.X, (float)screen.Y));
        _lastPointerPdf = _aiCropNoteEndPdf;
        SKRect rect = NormalizeRect(_aiCropNoteStartPdf, _aiCropNoteEndPdf);
        PostStatus($"AI crop note: selecting {PdfToScreenDistance(rect.Width):F0} x {PdfToScreenDistance(rect.Height):F0}px.");
        RequestRepaint();
        return true;
    }

    private bool FinishAiCropNoteSelection()
    {
        if (!_aiCropNoteSelecting || !_aiCropNoteDragging)
            return false;

        SKRect rect = NormalizeRect(_aiCropNoteStartPdf, _aiCropNoteEndPdf);
        bool tooSmall = PdfToScreenDistance(rect.Width) < 8f ||
                        PdfToScreenDistance(rect.Height) < 8f;
        SKPoint anchor = _aiCropNoteAnchorPdf;
        string pageFolder = _pageFolder;
        ResetAiCropNoteSelection();

        if (tooSmall)
        {
            PostStatus("AI crop note cancelled: crop is too small.");
            RequestRepaint();
            return true;
        }

        RequestRepaint();
        AiCropNoteSelectionCompleted?.Invoke(new ViewportAiCropSelectionRequest(rect, anchor, pageFolder));
        return true;
    }

    private bool CancelAiCropNoteSelection(bool postStatus = true)
    {
        if (!_aiCropNoteSelecting)
            return false;

        ResetAiCropNoteSelection();
        if (postStatus)
            PostStatus("AI crop note cancelled.");
        RequestRepaint();
        return true;
    }

    private void ResetAiCropNoteSelection()
    {
        _aiCropNoteSelecting = false;
        _aiCropNoteDragging = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private SKPoint ClampAiCropPoint(SKPoint point) =>
        new(
            Math.Clamp(point.X, 0, Math.Max(0, _pdfW)),
            Math.Clamp(point.Y, 0, Math.Max(0, _pdfH)));

    private void DrawAiCropNoteSelectionOverlay(SKCanvas canvas)
    {
        if (!_aiCropNoteSelecting)
            return;

        float safeZoom = Math.Max(_zoom, 0.001f);
        SKRect rect = NormalizeRect(_aiCropNoteStartPdf, _aiCropNoteEndPdf);
        using var fill = new SKPaint
        {
            Color = new SKColor(0xF9, 0xA8, 0x25, 42),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = new SKColor(0xF9, 0xA8, 0x25),
            StrokeWidth = 2f / safeZoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([9f / safeZoom, 4f / safeZoom], 0),
        };
        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, stroke);
    }
}
