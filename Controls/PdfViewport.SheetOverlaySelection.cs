using System;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed record SheetOverlayTransformSnapshot(
    float OffsetXPt,
    float OffsetYPt,
    float OverlayScale,
    float OverlayRotationDegrees,
    float WidthPt,
    float HeightPt,
    float SuggestedOffsetRangePt);

public sealed partial class PdfViewport
{
    private enum SheetOverlaySelectionHandle
    {
        None,
        Move,
        Scale,
        Rotation,
    }

    private static readonly SKColor SheetOverlaySelectionColor = new(0xF4, 0x9B, 0x24);
    private bool _sheetOverlaySelectionActive;
    private bool _sheetOverlaySelectionCanEdit;
    private bool _sheetOverlayTransformPreviewActive;
    private bool _sheetOverlaySelectionInputInstalled;
    private SheetOverlayTransformSnapshot? _sheetOverlayTransformPreviewStart;
    private SheetOverlaySelectionHandle _sheetOverlaySelectionHandle;
    private SheetOverlayTransformSnapshot? _sheetOverlayHandleStart;
    private SKPoint _sheetOverlayHandleStartPointerPdf;
    private SKPoint _sheetOverlayHandleCenter;
    private float _sheetOverlayHandleStartDistance;
    private float _sheetOverlayHandleStartAngle;

    public event Action<SheetOverlayTransformSnapshot>? SheetOverlayTransformPreviewChanged;

    public bool IsSheetOverlaySelectionActive => _sheetOverlaySelectionActive;

    private bool HasPendingSheetOverlayTransformGesture =>
        _sheetOverlayTransformPreviewActive ||
        _sheetOverlaySelectionHandle != SheetOverlaySelectionHandle.None ||
        _draggingSheetOverlay ||
        IsSheetOverlayPointEditing;

    public void SetSheetOverlaySelectionActive(bool active, bool canEdit = true)
    {
        bool nextCanEdit = active && canEdit && !IsReadOnlyMode;
        if (nextCanEdit)
            EnsureSheetOverlaySelectionInputInstalled();

        if (_sheetOverlaySelectionActive == active &&
            _sheetOverlaySelectionCanEdit == nextCanEdit)
        {
            RequestRepaint();
            return;
        }

        if (!active || !nextCanEdit)
        {
            _sheetOverlaySelectionHandle = SheetOverlaySelectionHandle.None;
            _sheetOverlayHandleStart = null;
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            CancelSheetOverlayTransformPreview(postStatus: false);
        }

        _sheetOverlaySelectionActive = active;
        _sheetOverlaySelectionCanEdit = nextCanEdit;
        RequestRepaint();
    }

    public SheetOverlayTransformSnapshot? CurrentSheetOverlayTransform()
    {
        if (_sheetOverlayBitmap == null ||
            !TryGetSheetOverlaySize(out float width, out float height))
        {
            return null;
        }

        return BuildSheetOverlayTransformSnapshot(width, height);
    }

    public SheetOverlayTransformSnapshot? BeginSheetOverlayTransformPreview()
    {
        if (!_sheetOverlaySelectionCanEdit || IsReadOnlyMode)
            return null;

        SheetOverlayTransformSnapshot? current = CurrentSheetOverlayTransform();
        if (current == null)
            return null;

        if (!_sheetOverlayTransformPreviewActive)
        {
            _sheetOverlayTransformPreviewStart = current;
            _sheetOverlayTransformPreviewActive = true;
            ClearStaticPageFrameCache();
        }

        return current;
    }

    public SheetOverlayTransformSnapshot? PreviewSheetOverlayTransform(
        float offsetXPt,
        float offsetYPt,
        float overlayScale,
        float overlayRotationDegrees,
        bool preserveCenterForScale)
    {
        SheetOverlayTransformSnapshot? current = BeginSheetOverlayTransformPreview();
        if (current == null)
            return null;

        float nextScale = NormalizeSheetOverlayScale(overlayScale);
        float nextRotation = NormalizeSheetOverlayRotation(overlayRotationDegrees);
        float nextX = Math.Clamp(offsetXPt, -100000f, 100000f);
        float nextY = Math.Clamp(offsetYPt, -100000f, 100000f);
        if (preserveCenterForScale &&
            MathF.Abs(nextScale - _sheetOverlayScale) > 0.00001f)
        {
            SKPoint center = SheetOverlayDisplayCenter(
                current.WidthPt,
                current.HeightPt,
                _sheetOverlayOffsetXPt,
                _sheetOverlayOffsetYPt,
                _sheetOverlayScale,
                _sheetOverlayRotationDegrees);
            SKPoint nextCenterVector = OverlayLocalTransformVector(
                new SKPoint(current.WidthPt / 2f, current.HeightPt / 2f),
                nextScale,
                nextRotation);
            nextX = center.X - nextCenterVector.X;
            nextY = center.Y - nextCenterVector.Y;
        }

        ApplySheetOverlayTransformPreview(nextX, nextY, nextScale, nextRotation);
        return CurrentSheetOverlayTransform();
    }

    public void CommitSheetOverlayTransformPreview(string status)
    {
        if (!_sheetOverlayTransformPreviewActive)
            return;
        if (!_sheetOverlaySelectionCanEdit || IsReadOnlyMode)
        {
            CancelSheetOverlayTransformPreview(postStatus: false);
            return;
        }

        SheetOverlayTransformSnapshot? start = _sheetOverlayTransformPreviewStart;
        SheetOverlayTransformSnapshot? current = CurrentSheetOverlayTransform();
        _sheetOverlayTransformPreviewActive = false;
        _sheetOverlayTransformPreviewStart = null;
        ClearStaticPageFrameCache();
        RequestRepaint();

        if (start == null || current == null || !HasSheetOverlayTransformChanged(start, current))
            return;

        CommitSheetOverlayTransformChange(start, current, status);
    }

    public void CancelSheetOverlayTransformPreview(bool postStatus = true)
    {
        if (!_sheetOverlayTransformPreviewActive)
            return;

        SheetOverlayTransformSnapshot? start = _sheetOverlayTransformPreviewStart;
        _sheetOverlayTransformPreviewActive = false;
        _sheetOverlayTransformPreviewStart = null;
        if (start != null)
        {
            _sheetOverlayOffsetXPt = start.OffsetXPt;
            _sheetOverlayOffsetYPt = start.OffsetYPt;
            _sheetOverlayScale = start.OverlayScale;
            _sheetOverlayRotationDegrees = start.OverlayRotationDegrees;
        }

        ClearStaticPageFrameCache();
        if (CurrentSheetOverlayTransform() is { } restored)
            SheetOverlayTransformPreviewChanged?.Invoke(restored);
        if (postStatus)
            PostStatus("Overlay transform preview cancelled.");
        RequestRepaint();
    }

    private void ApplySheetOverlayTransformPreview(
        float offsetXPt,
        float offsetYPt,
        float overlayScale,
        float overlayRotationDegrees)
    {
        _sheetOverlayOffsetXPt = Math.Clamp(offsetXPt, -100000f, 100000f);
        _sheetOverlayOffsetYPt = Math.Clamp(offsetYPt, -100000f, 100000f);
        _sheetOverlayScale = NormalizeSheetOverlayScale(overlayScale);
        _sheetOverlayRotationDegrees = NormalizeSheetOverlayRotation(overlayRotationDegrees);
        ClearStaticPageFrameCache();
        if (CurrentSheetOverlayTransform() is { } current)
            SheetOverlayTransformPreviewChanged?.Invoke(current);
        RequestRepaint();
    }

    private SheetOverlayTransformSnapshot BuildSheetOverlayTransformSnapshot(float width, float height)
    {
        float contentRange = MathF.Max(
            MathF.Max(_pdfW, _pdfH),
            MathF.Max(width * _sheetOverlayScale, height * _sheetOverlayScale));
        float currentRange = MathF.Max(
            MathF.Abs(_sheetOverlayOffsetXPt),
            MathF.Abs(_sheetOverlayOffsetYPt)) * 1.25f + 72f;
        return new SheetOverlayTransformSnapshot(
            _sheetOverlayOffsetXPt,
            _sheetOverlayOffsetYPt,
            _sheetOverlayScale,
            _sheetOverlayRotationDegrees,
            width,
            height,
            Math.Clamp(MathF.Max(720f, MathF.Max(contentRange, currentRange)), 720f, 100000f));
    }

    private static bool HasSheetOverlayTransformChanged(
        SheetOverlayTransformSnapshot start,
        SheetOverlayTransformSnapshot current) =>
        MathF.Abs(start.OffsetXPt - current.OffsetXPt) > 0.0001f ||
        MathF.Abs(start.OffsetYPt - current.OffsetYPt) > 0.0001f ||
        MathF.Abs(start.OverlayScale - current.OverlayScale) > 0.00001f ||
        MathF.Abs(start.OverlayRotationDegrees - current.OverlayRotationDegrees) > 0.0001f;

    private void DrawSheetOverlaySelection(SKCanvas canvas)
    {
        if (!_sheetOverlaySelectionActive ||
            _sheetOverlayBitmap == null ||
            !TryGetSheetOverlaySelectionGeometry(
                out SKPoint p0,
                out SKPoint p1,
                out SKPoint p2,
                out SKPoint p3,
                out SKPoint rotationHandle))
        {
            return;
        }

        float strokeWidth = ScreenToPdfDistance(1.6f);
        using var outline = new SKPaint
        {
            Color = SheetOverlaySelectionColor,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            PathEffect = SKPathEffect.CreateDash(
                [ScreenToPdfDistance(6f), ScreenToPdfDistance(4f)],
                0),
        };
        using var handleFill = new SKPaint
        {
            Color = SheetOverlaySelectionColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var handleOutline = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = ScreenToPdfDistance(1f),
        };

        canvas.DrawLine(p0, p1, outline);
        canvas.DrawLine(p1, p2, outline);
        canvas.DrawLine(p2, p3, outline);
        canvas.DrawLine(p3, p0, outline);

        if (!_sheetOverlaySelectionCanEdit || IsReadOnlyMode)
            return;

        float scaleHandleRadius = ScreenToPdfDistance(4.5f);
        foreach (SKPoint point in new[] { p0, p1, p2, p3 })
        {
            SKRect rect = SKRect.Create(
                point.X - scaleHandleRadius,
                point.Y - scaleHandleRadius,
                scaleHandleRadius * 2f,
                scaleHandleRadius * 2f);
            canvas.DrawRect(rect, handleFill);
            canvas.DrawRect(rect, handleOutline);
        }

        SKPoint topMid = SheetOverlayMidpoint(p0, p1);
        canvas.DrawLine(topMid, rotationHandle, outline);
        float rotationRadius = ScreenToPdfDistance(5f);
        canvas.DrawCircle(rotationHandle, rotationRadius, handleFill);
        canvas.DrawCircle(rotationHandle, rotationRadius, handleOutline);
    }

    private bool TryGetSheetOverlaySelectionGeometry(
        out SKPoint p0,
        out SKPoint p1,
        out SKPoint p2,
        out SKPoint p3,
        out SKPoint rotationHandle)
    {
        p0 = default;
        p1 = default;
        p2 = default;
        p3 = default;
        rotationHandle = default;
        if (!TryGetSheetOverlaySize(out float width, out float height))
            return false;

        p0 = OverlayLocalToDisplay(new SKPoint(0, 0));
        p1 = OverlayLocalToDisplay(new SKPoint(width, 0));
        p2 = OverlayLocalToDisplay(new SKPoint(width, height));
        p3 = OverlayLocalToDisplay(new SKPoint(0, height));
        SKPoint center = SheetOverlayMidpoint(p0, p2);
        SKPoint topMid = SheetOverlayMidpoint(p0, p1);
        SKPoint outward = NormalizeVector(new SKPoint(topMid.X - center.X, topMid.Y - center.Y));
        rotationHandle = new SKPoint(
            topMid.X + outward.X * ScreenToPdfDistance(24f),
            topMid.Y + outward.Y * ScreenToPdfDistance(24f));
        return true;
    }

    private void EnsureSheetOverlaySelectionInputInstalled()
    {
        if (_sheetOverlaySelectionInputInstalled)
            return;

        PreviewMouseLeftButtonDown += SheetOverlaySelection_PreviewMouseLeftButtonDown;
        PreviewMouseMove += SheetOverlaySelection_PreviewMouseMove;
        PreviewMouseLeftButtonUp += SheetOverlaySelection_PreviewMouseLeftButtonUp;
        LostMouseCapture += SheetOverlaySelection_LostMouseCapture;
        PreviewKeyDown += SheetOverlaySelection_PreviewKeyDown;
        _sheetOverlaySelectionInputInstalled = true;
    }

    private void SheetOverlaySelection_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_sheetOverlaySelectionActive ||
            !_sheetOverlaySelectionCanEdit ||
            IsReadOnlyMode ||
            _sheetOverlayBitmap == null ||
            IsSheetOverlayDragModifierActive() ||
            e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        Point canvasPoint = ViewPointToCanvas(e.GetPosition(this));
        SKPoint pdf = ScreenToPdf((float)canvasPoint.X, (float)canvasPoint.Y);
        SheetOverlaySelectionHandle handle = HitSheetOverlaySelectionHandle(pdf);
        if (handle == SheetOverlaySelectionHandle.None)
            return;

        Focus();
        if (BeginSheetOverlayTransformPreview() is not { } start)
            return;

        _sheetOverlaySelectionHandle = handle;
        _sheetOverlayHandleStart = start;
        _sheetOverlayHandleStartPointerPdf = pdf;
        _sheetOverlayHandleCenter = SheetOverlayDisplayCenter(
            start.WidthPt,
            start.HeightPt,
            start.OffsetXPt,
            start.OffsetYPt,
            start.OverlayScale,
            start.OverlayRotationDegrees);
        _sheetOverlayHandleStartDistance = Math.Max(
            0.001f,
            OverlayDistance(_sheetOverlayHandleCenter, pdf));
        _sheetOverlayHandleStartAngle = MathF.Atan2(
            pdf.Y - _sheetOverlayHandleCenter.Y,
            pdf.X - _sheetOverlayHandleCenter.X);
        CaptureMouse();
        PostStatus(handle switch
        {
            SheetOverlaySelectionHandle.Move =>
                "Overlay move: drag inside the orange frame; release to save.",
            SheetOverlaySelectionHandle.Scale =>
                "Overlay scale: drag the orange corner; release to save.",
            _ =>
                "Overlay rotation: drag the orange round handle; release to save.",
        });
        e.Handled = true;
    }

    private void SheetOverlaySelection_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_sheetOverlaySelectionHandle == SheetOverlaySelectionHandle.None ||
            _sheetOverlayHandleStart == null ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point canvasPoint = ViewPointToCanvas(e.GetPosition(this));
        SKPoint pdf = ScreenToPdf((float)canvasPoint.X, (float)canvasPoint.Y);
        SheetOverlayTransformSnapshot start = _sheetOverlayHandleStart;
        if (_sheetOverlaySelectionHandle == SheetOverlaySelectionHandle.Move)
        {
            ApplySheetOverlayTransformPreview(
                start.OffsetXPt + pdf.X - _sheetOverlayHandleStartPointerPdf.X,
                start.OffsetYPt + pdf.Y - _sheetOverlayHandleStartPointerPdf.Y,
                start.OverlayScale,
                start.OverlayRotationDegrees);
            e.Handled = true;
            return;
        }

        float scale = start.OverlayScale;
        float rotation = start.OverlayRotationDegrees;
        if (_sheetOverlaySelectionHandle == SheetOverlaySelectionHandle.Scale)
        {
            float distance = OverlayDistance(_sheetOverlayHandleCenter, pdf);
            scale = NormalizeSheetOverlayScale(
                start.OverlayScale * distance / _sheetOverlayHandleStartDistance);
        }
        else
        {
            float angle = MathF.Atan2(
                pdf.Y - _sheetOverlayHandleCenter.Y,
                pdf.X - _sheetOverlayHandleCenter.X);
            rotation = NormalizeSheetOverlayRotation(
                start.OverlayRotationDegrees +
                (angle - _sheetOverlayHandleStartAngle) * 180f / MathF.PI);
        }

        SKPoint centerVector = OverlayLocalTransformVector(
            new SKPoint(start.WidthPt / 2f, start.HeightPt / 2f),
            scale,
            rotation);
        ApplySheetOverlayTransformPreview(
            _sheetOverlayHandleCenter.X - centerVector.X,
            _sheetOverlayHandleCenter.Y - centerVector.Y,
            scale,
            rotation);
        e.Handled = true;
    }

    private void SheetOverlaySelection_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_sheetOverlaySelectionHandle == SheetOverlaySelectionHandle.None)
            return;

        FinishSheetOverlaySelectionHandle(commit: true);
        e.Handled = true;
    }

    private void SheetOverlaySelection_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_sheetOverlaySelectionHandle != SheetOverlaySelectionHandle.None)
            FinishSheetOverlaySelectionHandle(commit: false);
    }

    private void SheetOverlaySelection_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape ||
            (_sheetOverlaySelectionHandle == SheetOverlaySelectionHandle.None &&
             !_sheetOverlayTransformPreviewActive))
        {
            return;
        }

        FinishSheetOverlaySelectionHandle(commit: false);
        e.Handled = true;
    }

    private void FinishSheetOverlaySelectionHandle(bool commit)
    {
        SheetOverlaySelectionHandle handle = _sheetOverlaySelectionHandle;
        bool wasDragging = handle != SheetOverlaySelectionHandle.None;
        ResetSheetOverlaySelectionHandleState();

        if (commit && wasDragging)
        {
            string status = handle switch
            {
                SheetOverlaySelectionHandle.Move => "Overlay moved from orange frame.",
                SheetOverlaySelectionHandle.Scale => "Overlay scaled from orange corner.",
                SheetOverlaySelectionHandle.Rotation => "Overlay rotated from orange handle.",
                _ => "Overlay transform updated.",
            };
            CommitSheetOverlayTransformPreview(status);
            return;
        }

        CancelSheetOverlayTransformPreview();
    }

    private bool CancelPendingSheetOverlayTransformGesture(bool postStatus)
    {
        bool hadPendingGesture = HasPendingSheetOverlayTransformGesture;
        ResetSheetOverlaySelectionHandleState();
        CancelSheetOverlayTransformPreview(postStatus: false);
        CancelSheetOverlayDrag(silent: true);
        CancelSheetOverlayPointEdit(silent: true);
        if (hadPendingGesture && postStatus)
            PostStatus("Overlay transform cancelled.");
        return hadPendingGesture;
    }

    private void ResetSheetOverlaySelectionHandleState()
    {
        _sheetOverlaySelectionHandle = SheetOverlaySelectionHandle.None;
        _sheetOverlayHandleStart = null;
        _sheetOverlayHandleStartPointerPdf = default;
        _sheetOverlayHandleCenter = default;
        _sheetOverlayHandleStartDistance = 0;
        _sheetOverlayHandleStartAngle = 0;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private bool TryCancelPendingSheetOverlayTransformForUndo()
    {
        if (_sheetOverlaySelectionHandle != SheetOverlaySelectionHandle.None)
        {
            FinishSheetOverlaySelectionHandle(commit: false);
            return true;
        }

        if (_sheetOverlayTransformPreviewActive)
        {
            CancelSheetOverlayTransformPreview();
            return true;
        }

        if (_draggingSheetOverlay)
            return CancelSheetOverlayDrag();

        if (!IsSheetOverlayPointEditing)
            return false;

        bool transformWasCommitted =
            _sheetOverlayPointEditStep is
                SheetOverlayPointEditStep.ScaleSource or
                SheetOverlayPointEditStep.ScaleTarget;
        CancelSheetOverlayPointEdit(silent: true);
        return !transformWasCommitted;
    }

    private SheetOverlaySelectionHandle HitSheetOverlaySelectionHandle(SKPoint pdf)
    {
        if (!TryGetSheetOverlaySelectionGeometry(
                out SKPoint p0,
                out SKPoint p1,
                out SKPoint p2,
                out SKPoint p3,
                out SKPoint rotationHandle))
        {
            return SheetOverlaySelectionHandle.None;
        }

        float rotationTolerance = ScreenToPdfDistance(10f);
        if (OverlayDistance(pdf, rotationHandle) <= rotationTolerance)
            return SheetOverlaySelectionHandle.Rotation;

        float scaleTolerance = ScreenToPdfDistance(9f);
        foreach (SKPoint corner in new[] { p0, p1, p2, p3 })
        {
            if (OverlayDistance(pdf, corner) <= scaleTolerance)
                return SheetOverlaySelectionHandle.Scale;
        }

        if (TryGetSheetOverlaySize(out float width, out float height))
        {
            SKPoint local = OverlayDisplayToLocal(pdf);
            if (local.X >= 0 &&
                local.X <= width &&
                local.Y >= 0 &&
                local.Y <= height)
            {
                return SheetOverlaySelectionHandle.Move;
            }
        }

        return SheetOverlaySelectionHandle.None;
    }

    private static SKPoint SheetOverlayDisplayCenter(
        float width,
        float height,
        float offsetX,
        float offsetY,
        float scale,
        float rotation)
    {
        SKPoint centerVector = OverlayLocalTransformVector(
            new SKPoint(width / 2f, height / 2f),
            scale,
            rotation);
        return new SKPoint(offsetX + centerVector.X, offsetY + centerVector.Y);
    }

    private static SKPoint SheetOverlayMidpoint(SKPoint a, SKPoint b) =>
        new((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);

    private static SKPoint NormalizeVector(SKPoint vector)
    {
        float length = MathF.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
        return length <= 0.0001f
            ? new SKPoint(0, -1)
            : new SKPoint(vector.X / length, vector.Y / length);
    }
}
