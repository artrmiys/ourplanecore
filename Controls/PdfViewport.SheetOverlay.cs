using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Input;
using OurPlanCore;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private enum SheetOverlayPointEditStep
    {
        None,
        MoveSource,
        MoveTarget,
        ScaleSource,
        ScaleTarget,
    }

    private SKBitmap? _sheetOverlayBitmap;
    private float _sheetOverlayWidthPt;
    private float _sheetOverlayHeightPt;
    private float _sheetOverlayOffsetXPt;
    private float _sheetOverlayOffsetYPt;
    private float _sheetOverlayScale = 1f;
    private float _sheetOverlayRotationDegrees;
    private float _sheetOverlayBitmapScale;
    private float _lastSheetOverlayRefreshRequestScale;
    private string _sheetOverlayName = "";
    private string _sheetOverlayTargetPageFolder = "";
    private string _sheetOverlaySourcePageFolder = "";
    private SheetOverlayPointEditStep _sheetOverlayPointEditStep;
    private SKPoint _sheetOverlayEditAnchorLocal;
    private SKPoint _sheetOverlayEditAnchorTarget;
    private SKPoint _sheetOverlayEditScaleLocal;
    private SKPoint? _sheetOverlayPointEditSnapPreview;
    private bool _draggingSheetOverlay;
    private bool _sheetOverlayDragChanged;
    private SKPoint _sheetOverlayDragStartPdf;
    private float _sheetOverlayDragStartOffsetXPt;
    private float _sheetOverlayDragStartOffsetYPt;
    private SheetOverlayTransformSnapshot? _sheetOverlayDragStartTransform;

    public void SetSheetOverlay(
        SKBitmap bitmap,
        float widthPt,
        float heightPt,
        string overlayName,
        float offsetXPt = 0,
        float offsetYPt = 0,
        float overlayScale = 1,
        float overlayRotationDegrees = 0,
        string overlayPdfPath = "",
        int overlayPageIndex = 0,
        IReadOnlyList<PdfLayerInfo>? overlayLayers = null,
        float bitmapScale = 0,
        string overlayPageFolder = "",
        string overlayId = "",
        float overlayOpacity = 1)
    {
        SetSheetOverlayLayers(
        [
            new SheetOverlayBitmapLayer(
                overlayId,
                overlayPageFolder,
                overlayName,
                bitmap,
                widthPt,
                heightPt,
                offsetXPt,
                offsetYPt,
                overlayScale,
                overlayRotationDegrees,
                overlayOpacity,
                bitmapScale,
                overlayPdfPath,
                overlayPageIndex,
                overlayLayers ?? []),
        ],
        overlayId,
        _pageFolder);
    }

    public void PrepareSheetOverlayReload(
        string targetPageFolder,
        string overlayPageFolder,
        string overlayId = "")
    {
        ClearSheetOverlayCore(
            preserveTransformGesture: false,
            preserveBindingIdentity: false);
        _sheetOverlayTargetPageFolder = targetPageFolder ?? "";
        _sheetOverlaySourcePageFolder = overlayPageFolder ?? "";
        _sheetOverlayId = overlayId ?? "";
    }

    public void ClearSheetOverlay() =>
        ClearSheetOverlayCore(
            preserveTransformGesture: false,
            preserveBindingIdentity: false);

    private void ClearSheetOverlayCore(
        bool preserveTransformGesture,
        bool preserveBindingIdentity)
    {
        if (!preserveTransformGesture)
            CancelPendingSheetOverlayTransformGesture(postStatus: false);

        _sheetOverlayBitmap?.Dispose();
        _sheetOverlayBitmap = null;
        DisposeSheetOverlayLayers(_sheetOverlayLayersBelow);
        DisposeSheetOverlayLayers(_sheetOverlayLayersAbove);
        _sheetOverlayWidthPt = 0;
        _sheetOverlayHeightPt = 0;
        _sheetOverlayOffsetXPt = 0;
        _sheetOverlayOffsetYPt = 0;
        _sheetOverlayScale = 1;
        _sheetOverlayRotationDegrees = 0;
        _sheetOverlayOpacity = 1;
        _sheetOverlayBitmapScale = 0;
        _lastSheetOverlayRefreshRequestScale = 0;
        _sheetOverlayName = "";
        if (!preserveBindingIdentity)
        {
            _sheetOverlayTargetPageFolder = "";
            _sheetOverlaySourcePageFolder = "";
            _sheetOverlayId = "";
        }
        ClearOverlayPdfSnapSource();
        RequestRepaint();
    }

    public void BeginSheetOverlayPointEdit()
    {
        if (_sheetOverlayBitmap == null)
        {
            PostStatus("Set a sheet overlay before editing it.");
            return;
        }

        CancelPendingSheetOverlayTransformGesture(postStatus: false);
        _sheetOverlayPointEditStep = SheetOverlayPointEditStep.MoveSource;
        _sheetOverlayEditAnchorLocal = default;
        _sheetOverlayEditAnchorTarget = default;
        _sheetOverlayEditScaleLocal = default;
        ClearSheetOverlayPointEditSnapPreview();
        PostStatus("Overlay edit: click a point on the overlay to grab it.");
        RequestRepaint();
        Focus();
    }

    private bool IsSheetOverlayPointEditing =>
        _sheetOverlayPointEditStep != SheetOverlayPointEditStep.None;

    private bool TryBeginSheetOverlayDrag(SKPoint pdf)
    {
        if (_sheetOverlayBitmap == null ||
            HasPendingSheetOverlayTransformGesture ||
            !IsSheetOverlayDragModifierActive() ||
            !IsPointInsideSheetOverlay(pdf))
        {
            return false;
        }

        _draggingSheetOverlay = true;
        _sheetOverlayDragChanged = false;
        _sheetOverlayDragStartPdf = pdf;
        _sheetOverlayDragStartOffsetXPt = _sheetOverlayOffsetXPt;
        _sheetOverlayDragStartOffsetYPt = _sheetOverlayOffsetYPt;
        _sheetOverlayDragStartTransform = CurrentSheetOverlayTransform();
        CaptureMouse();
        PostStatus("Overlay drag: move the mouse, release to save. Hold Shift for fine movement.");
        RequestRepaint();
        return true;
    }

    private bool TryUpdateSheetOverlayDrag(SKPoint pdf)
    {
        if (!_draggingSheetOverlay)
            return false;

        float dragScale = IsSheetOverlayFineModifierActive() ? 0.25f : 1f;
        float deltaX = (pdf.X - _sheetOverlayDragStartPdf.X) * dragScale;
        float deltaY = (pdf.Y - _sheetOverlayDragStartPdf.Y) * dragScale;
        _sheetOverlayOffsetXPt = _sheetOverlayDragStartOffsetXPt + deltaX;
        _sheetOverlayOffsetYPt = _sheetOverlayDragStartOffsetYPt + deltaY;
        _sheetOverlayDragChanged =
            MathF.Abs(deltaX) > 0.001f ||
            MathF.Abs(deltaY) > 0.001f;
        PostStatus(BuildSheetOverlayTransformStatus(
            IsSheetOverlayFineModifierActive() ? "Overlay dragging fine" : "Overlay dragging",
            _sheetOverlayOffsetXPt,
            _sheetOverlayOffsetYPt,
            _sheetOverlayScale,
            _sheetOverlayRotationDegrees));
        RequestRepaint();
        return true;
    }

    private bool FinishSheetOverlayDrag()
    {
        if (!_draggingSheetOverlay)
            return false;

        bool changed = _sheetOverlayDragChanged;
        SheetOverlayTransformSnapshot? start = _sheetOverlayDragStartTransform;
        SheetOverlayTransformSnapshot? current = CurrentSheetOverlayTransform();
        _draggingSheetOverlay = false;
        _sheetOverlayDragChanged = false;
        _sheetOverlayDragStartTransform = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();

        if (changed && start != null && current != null)
        {
            CommitSheetOverlayTransformChange(
                start,
                current,
                BuildSheetOverlayTransformStatus(
                    "Overlay moved",
                    _sheetOverlayOffsetXPt,
                    _sheetOverlayOffsetYPt,
                    _sheetOverlayScale,
                    _sheetOverlayRotationDegrees));
        }
        else
        {
            PostStatus("Overlay drag cancelled.");
            RequestRepaint();
        }

        return true;
    }

    private bool CancelSheetOverlayDrag(bool silent = false)
    {
        if (!_draggingSheetOverlay)
            return false;

        _sheetOverlayOffsetXPt = _sheetOverlayDragStartOffsetXPt;
        _sheetOverlayOffsetYPt = _sheetOverlayDragStartOffsetYPt;
        _draggingSheetOverlay = false;
        _sheetOverlayDragChanged = false;
        _sheetOverlayDragStartTransform = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        if (!silent)
            PostStatus("Overlay drag cancelled.");
        RequestRepaint();
        return true;
    }

    private static bool IsSheetOverlayDragModifierActive()
    {
        ModifierKeys modifiers = Keyboard.Modifiers;
        return (modifiers & ModifierKeys.Control) != 0 &&
               (modifiers & ModifierKeys.Alt) != 0;
    }

    private static bool IsSheetOverlayFineModifierActive() =>
        (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

    private bool TryHandleSheetOverlayTransformShortcut(KeyEventArgs e)
    {
        if (_sheetOverlayBitmap == null)
            return false;

        ModifierKeys modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Control) == 0 ||
            (modifiers & ModifierKeys.Alt) == 0)
        {
            return false;
        }

        bool fine = (modifiers & ModifierKeys.Shift) != 0;
        float nudgePt = fine ? 1f : 6f;
        float scaleFactor = fine ? 1.01f : 1.05f;
        float rotationStep = fine ? 0.25f : 1f;
        Key key = OurPlanCore.KeyboardShortcutKeys.EffectiveKey(e);
        bool recognized =
            key is Key.Left or Key.Right or Key.Up or Key.Down or
                Key.Add or Key.OemPlus or Key.Subtract or Key.OemMinus or
                Key.OemOpenBrackets or Key.OemCloseBrackets or
                Key.D0 or Key.NumPad0;
        if (!recognized)
            return false;

        CancelPendingSheetOverlayTransformGesture(postStatus: false);

        switch (key)
        {
            case Key.Left:
                ApplySheetOverlayTransform(
                    _sheetOverlayOffsetXPt - nudgePt,
                    _sheetOverlayOffsetYPt,
                    _sheetOverlayScale,
                    _sheetOverlayRotationDegrees,
                    BuildSheetOverlayTransformStatus(
                        "Overlay moved",
                        _sheetOverlayOffsetXPt - nudgePt,
                        _sheetOverlayOffsetYPt,
                        _sheetOverlayScale,
                        _sheetOverlayRotationDegrees));
                break;
            case Key.Right:
                ApplySheetOverlayTransform(
                    _sheetOverlayOffsetXPt + nudgePt,
                    _sheetOverlayOffsetYPt,
                    _sheetOverlayScale,
                    _sheetOverlayRotationDegrees,
                    BuildSheetOverlayTransformStatus(
                        "Overlay moved",
                        _sheetOverlayOffsetXPt + nudgePt,
                        _sheetOverlayOffsetYPt,
                        _sheetOverlayScale,
                        _sheetOverlayRotationDegrees));
                break;
            case Key.Up:
                ApplySheetOverlayTransform(
                    _sheetOverlayOffsetXPt,
                    _sheetOverlayOffsetYPt - nudgePt,
                    _sheetOverlayScale,
                    _sheetOverlayRotationDegrees,
                    BuildSheetOverlayTransformStatus(
                        "Overlay moved",
                        _sheetOverlayOffsetXPt,
                        _sheetOverlayOffsetYPt - nudgePt,
                        _sheetOverlayScale,
                        _sheetOverlayRotationDegrees));
                break;
            case Key.Down:
                ApplySheetOverlayTransform(
                    _sheetOverlayOffsetXPt,
                    _sheetOverlayOffsetYPt + nudgePt,
                    _sheetOverlayScale,
                    _sheetOverlayRotationDegrees,
                    BuildSheetOverlayTransformStatus(
                        "Overlay moved",
                        _sheetOverlayOffsetXPt,
                        _sheetOverlayOffsetYPt + nudgePt,
                        _sheetOverlayScale,
                        _sheetOverlayRotationDegrees));
                break;
            case Key.Add:
            case Key.OemPlus:
                ApplySheetOverlayTransform(
                    _sheetOverlayOffsetXPt,
                    _sheetOverlayOffsetYPt,
                    _sheetOverlayScale * scaleFactor,
                    _sheetOverlayRotationDegrees,
                    BuildSheetOverlayTransformStatus(
                        "Overlay scaled",
                        _sheetOverlayOffsetXPt,
                        _sheetOverlayOffsetYPt,
                        NormalizeSheetOverlayScale(_sheetOverlayScale * scaleFactor),
                        _sheetOverlayRotationDegrees));
                break;
            case Key.Subtract:
            case Key.OemMinus:
                ApplySheetOverlayTransform(
                    _sheetOverlayOffsetXPt,
                    _sheetOverlayOffsetYPt,
                    _sheetOverlayScale / scaleFactor,
                    _sheetOverlayRotationDegrees,
                    BuildSheetOverlayTransformStatus(
                        "Overlay scaled",
                        _sheetOverlayOffsetXPt,
                        _sheetOverlayOffsetYPt,
                        NormalizeSheetOverlayScale(_sheetOverlayScale / scaleFactor),
                        _sheetOverlayRotationDegrees));
                break;
            case Key.OemOpenBrackets:
                ApplySheetOverlayTransform(
                    _sheetOverlayOffsetXPt,
                    _sheetOverlayOffsetYPt,
                    _sheetOverlayScale,
                    _sheetOverlayRotationDegrees - rotationStep,
                    BuildSheetOverlayTransformStatus(
                        "Overlay rotated",
                        _sheetOverlayOffsetXPt,
                        _sheetOverlayOffsetYPt,
                        _sheetOverlayScale,
                        NormalizeSheetOverlayRotation(_sheetOverlayRotationDegrees - rotationStep)));
                break;
            case Key.OemCloseBrackets:
                ApplySheetOverlayTransform(
                    _sheetOverlayOffsetXPt,
                    _sheetOverlayOffsetYPt,
                    _sheetOverlayScale,
                    _sheetOverlayRotationDegrees + rotationStep,
                    BuildSheetOverlayTransformStatus(
                        "Overlay rotated",
                        _sheetOverlayOffsetXPt,
                        _sheetOverlayOffsetYPt,
                        _sheetOverlayScale,
                        NormalizeSheetOverlayRotation(_sheetOverlayRotationDegrees + rotationStep)));
                break;
            case Key.D0:
            case Key.NumPad0:
                ApplySheetOverlayTransform(0, 0, 1, 0, "Overlay transform reset.");
                break;
            default:
                return false;
        }

        e.Handled = true;
        return true;
    }

    private bool HandleSheetOverlayPointEditClick(SKPoint pdf)
    {
        if (!IsSheetOverlayPointEditing)
            return false;

        if (_sheetOverlayBitmap == null)
        {
            CancelSheetOverlayPointEdit(silent: true);
            PostStatus("Overlay edit cancelled: overlay is missing.");
            return true;
        }

        switch (_sheetOverlayPointEditStep)
        {
            case SheetOverlayPointEditStep.MoveSource:
                _sheetOverlayEditAnchorLocal = ResolveSheetOverlaySourceLocalPoint(pdf, out string moveSourceSnapKind);
                ClearSheetOverlayPointEditSnapPreview();
                _sheetOverlayPointEditStep = SheetOverlayPointEditStep.MoveTarget;
                PostStatus(BuildSheetOverlayPointEditSnapStatus(
                    "Overlay edit: grabbed point",
                    moveSourceSnapKind,
                    "Click where that point should land."));
                break;

            case SheetOverlayPointEditStep.MoveTarget:
                _sheetOverlayEditAnchorTarget = ResolveSheetOverlayTargetPoint(pdf, out string moveTargetSnapKind);
                SKPoint anchorVector = OverlayLocalTransformVector(
                    _sheetOverlayEditAnchorLocal,
                    _sheetOverlayScale,
                    _sheetOverlayRotationDegrees);
                ApplySheetOverlayTransform(
                    _sheetOverlayEditAnchorTarget.X - anchorVector.X,
                    _sheetOverlayEditAnchorTarget.Y - anchorVector.Y,
                    _sheetOverlayScale,
                    _sheetOverlayRotationDegrees,
                    BuildSheetOverlayPointEditSnapStatus(
                        "Overlay moved by point",
                        moveTargetSnapKind,
                        "Click a second overlay point to scale, or press Esc to finish."));
                ClearSheetOverlayPointEditSnapPreview();
                _sheetOverlayPointEditStep = SheetOverlayPointEditStep.ScaleSource;
                break;

            case SheetOverlayPointEditStep.ScaleSource:
                _sheetOverlayEditScaleLocal = ResolveSheetOverlaySourceLocalPoint(pdf, out string scaleSourceSnapKind);
                if (OverlayDistance(_sheetOverlayEditAnchorLocal, _sheetOverlayEditScaleLocal) < 0.01f)
                {
                    PostStatus("Overlay scale: second point is too close to the first point.");
                    break;
                }

                _sheetOverlayPointEditStep = SheetOverlayPointEditStep.ScaleTarget;
                ClearSheetOverlayPointEditSnapPreview();
                PostStatus(BuildSheetOverlayPointEditSnapStatus(
                    "Overlay edit: grabbed second point",
                    scaleSourceSnapKind,
                    "Click where the second point should land."));
                break;

            case SheetOverlayPointEditStep.ScaleTarget:
                SKPoint scaleTarget = ResolveSheetOverlayTargetPoint(pdf, out string scaleTargetSnapKind);
                float localDistance = OverlayDistance(_sheetOverlayEditAnchorLocal, _sheetOverlayEditScaleLocal);
                float targetDistance = OverlayDistance(_sheetOverlayEditAnchorTarget, scaleTarget);
                if (localDistance < 0.01f || targetDistance < 0.01f)
                {
                    PostStatus("Overlay scale: choose two separated points.");
                    _sheetOverlayPointEditStep = SheetOverlayPointEditStep.ScaleSource;
                    break;
                }

                float newScale = NormalizeSheetOverlayScale(targetDistance / localDistance);
                float localAngle = MathF.Atan2(
                    _sheetOverlayEditScaleLocal.Y - _sheetOverlayEditAnchorLocal.Y,
                    _sheetOverlayEditScaleLocal.X - _sheetOverlayEditAnchorLocal.X);
                float targetAngle = MathF.Atan2(
                    scaleTarget.Y - _sheetOverlayEditAnchorTarget.Y,
                    scaleTarget.X - _sheetOverlayEditAnchorTarget.X);
                float newRotation = NormalizeSheetOverlayRotation((targetAngle - localAngle) * 180f / MathF.PI);
                SKPoint rotatedAnchor = OverlayLocalTransformVector(
                    _sheetOverlayEditAnchorLocal,
                    newScale,
                    newRotation);
                ApplySheetOverlayTransform(
                    _sheetOverlayEditAnchorTarget.X - rotatedAnchor.X,
                    _sheetOverlayEditAnchorTarget.Y - rotatedAnchor.Y,
                    newScale,
                    newRotation,
                    BuildSheetOverlayPointEditSnapStatus(
                        $"Overlay fit by two points: {newScale:0.###}x, rotation {newRotation:0.###} deg",
                        scaleTargetSnapKind,
                        ""));
                CancelSheetOverlayPointEdit(silent: true);
                break;
        }

        RequestRepaint();
        return true;
    }

    private void UpdateSheetOverlayPointEditPreview(SKPoint pdf)
    {
        ClearSheetOverlayPointEditSnapPreview();
        if (_sheetOverlayBitmap == null)
            return;

        if (_sheetOverlayPointEditStep is SheetOverlayPointEditStep.MoveSource or SheetOverlayPointEditStep.ScaleSource)
        {
            if (TryFindOverlayPdfSnapPoint(
                    pdf,
                    SheetOverlayPointEditSnapTolerancePt(),
                    out SKPoint snapped,
                    out _))
            {
                _sheetOverlayPointEditSnapPreview = snapped;
            }

            return;
        }

        if (_sheetOverlayPointEditStep is SheetOverlayPointEditStep.MoveTarget or SheetOverlayPointEditStep.ScaleTarget &&
            TryFindBasePdfSnapPoint(
                pdf,
                SheetOverlayPointEditSnapTolerancePt(),
                out SKPoint target,
                out _))
        {
            _sheetOverlayPointEditSnapPreview = target;
        }
    }

    private SKPoint ResolveSheetOverlaySourceLocalPoint(SKPoint pdf, out string snapKind)
    {
        if (TryFindOverlayPdfSnapPoint(pdf, SheetOverlayPointEditSnapTolerancePt(), out SKPoint snapped, out snapKind))
            return OverlayDisplayToLocal(snapped);

        snapKind = "";
        return OverlayDisplayToLocal(pdf);
    }

    private SKPoint ResolveSheetOverlayTargetPoint(SKPoint pdf, out string snapKind)
    {
        if (TryFindBasePdfSnapPoint(pdf, SheetOverlayPointEditSnapTolerancePt(), out SKPoint snapped, out snapKind))
            return snapped;

        snapKind = "";
        return pdf;
    }

    private float SheetOverlayPointEditSnapTolerancePt() =>
        ScreenToPdfDistance(14f);

    private static string BuildSheetOverlayPointEditSnapStatus(string prefix, string snapKind, string next)
    {
        string snap = string.IsNullOrWhiteSpace(snapKind) ? "" : $" snapped to {snapKind}";
        if (string.IsNullOrWhiteSpace(next))
            return $"{prefix}{snap}.";

        return $"{prefix}{snap}. {next}";
    }

    private void ClearSheetOverlayPointEditSnapPreview()
    {
        _sheetOverlayPointEditSnapPreview = null;
    }

    private void CancelSheetOverlayPointEdit(bool silent = false)
    {
        bool wasEditing = IsSheetOverlayPointEditing;
        _sheetOverlayPointEditStep = SheetOverlayPointEditStep.None;
        _sheetOverlayEditAnchorLocal = default;
        _sheetOverlayEditAnchorTarget = default;
        _sheetOverlayEditScaleLocal = default;
        ClearSheetOverlayPointEditSnapPreview();
        if (wasEditing && !silent)
            PostStatus("Overlay edit cancelled.");
        RequestRepaint();
    }

    public bool TryCommitSheetOverlayTransform(
        string targetPageFolder,
        string overlayPageFolder,
        float offsetXPt,
        float offsetYPt,
        float overlayScale,
        float overlayRotationDegrees,
        string status,
        string overlayId = "")
    {
        if (_sheetOverlayBitmap == null ||
            IsReadOnlyMode ||
            string.IsNullOrWhiteSpace(targetPageFolder) ||
            string.IsNullOrWhiteSpace(overlayPageFolder) ||
            !SheetOverlayReciprocalService.SameFolder(_pageFolder, targetPageFolder) ||
            !SheetOverlayReciprocalService.SameFolder(
                _sheetOverlayTargetPageFolder,
                targetPageFolder) ||
            !SheetOverlayReciprocalService.SameFolder(
                _sheetOverlaySourcePageFolder,
                overlayPageFolder) ||
            (!string.IsNullOrWhiteSpace(overlayId) &&
             !string.Equals(_sheetOverlayId, overlayId, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        CancelPendingSheetOverlayTransformGesture(postStatus: false);
        ApplySheetOverlayTransform(
            offsetXPt,
            offsetYPt,
            overlayScale,
            overlayRotationDegrees,
            status);
        return true;
    }

    private void ApplySheetOverlayTransform(
        float offsetXPt,
        float offsetYPt,
        float overlayScale,
        float overlayRotationDegrees,
        string status)
    {
        SheetOverlayTransformSnapshot? start = CurrentSheetOverlayTransform();
        _sheetOverlayOffsetXPt = Math.Clamp(offsetXPt, -100000f, 100000f);
        _sheetOverlayOffsetYPt = Math.Clamp(offsetYPt, -100000f, 100000f);
        _sheetOverlayScale = NormalizeSheetOverlayScale(overlayScale);
        _sheetOverlayRotationDegrees = NormalizeSheetOverlayRotation(overlayRotationDegrees);
        SheetOverlayTransformSnapshot? current = CurrentSheetOverlayTransform();
        if (start != null && current != null)
            CommitSheetOverlayTransformChange(start, current, status);
        else
            PostStatus(status);
        RequestRepaint();
    }

    private void CommitSheetOverlayTransformChange(
        SheetOverlayTransformSnapshot start,
        SheetOverlayTransformSnapshot current,
        string status)
    {
        if (!HasSheetOverlayTransformChanged(start, current))
        {
            PostStatus(status);
            return;
        }

        if (!_applyingViewportUndo)
        {
            PushSheetOverlayTransformUndo(
                _pageFolder,
                _sheetOverlaySourcePageFolder,
                _sheetOverlayId,
                start,
                current,
                "overlay transform");
        }

        PublishSheetOverlayTransformChange(current, status);
    }

    private void PublishSheetOverlayTransformChange(
        SheetOverlayTransformSnapshot transform,
        string status,
        bool postStatus = true)
    {
        SheetOverlayTransformChanged?.Invoke(new SheetOverlayTransformChange(
            _pageFolder,
            _sheetOverlaySourcePageFolder,
            _sheetOverlayId,
            transform.OffsetXPt,
            transform.OffsetYPt,
            transform.OverlayScale,
            transform.OverlayRotationDegrees,
            status));
        if (postStatus)
            PostStatus(status);
        MaybeRequestSheetOverlayRenderScaleRefresh();
        RequestRepaint();
    }

    private static string BuildSheetOverlayTransformStatus(
        string prefix,
        float offsetXPt,
        float offsetYPt,
        float overlayScale,
        float overlayRotationDegrees) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}: X {1:0.###}, Y {2:0.###}, scale {3:0.###}x, rotation {4:0.###} deg.",
            prefix,
            offsetXPt,
            offsetYPt,
            overlayScale,
            overlayRotationDegrees);

    private SKPoint OverlayDisplayToLocal(SKPoint displayPoint)
    {
        float scale = Math.Max(_sheetOverlayScale, 0.001f);
        float radians = _sheetOverlayRotationDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        float dx = displayPoint.X - _sheetOverlayOffsetXPt;
        float dy = displayPoint.Y - _sheetOverlayOffsetYPt;
        return new SKPoint(
            (dx * cos + dy * sin) / scale,
            (-dx * sin + dy * cos) / scale);
    }

    private static SKRect IntersectRects(SKRect a, SKRect b)
    {
        float left = Math.Max(a.Left, b.Left);
        float top = Math.Max(a.Top, b.Top);
        float right = Math.Min(a.Right, b.Right);
        float bottom = Math.Min(a.Bottom, b.Bottom);
        return right > left && bottom > top
            ? new SKRect(left, top, right, bottom)
            : SKRect.Empty;
    }

    private void DrawSheetOverlayEditGuides(SKCanvas canvas)
    {
        if ((!IsSheetOverlayPointEditing && !_draggingSheetOverlay) ||
            _sheetOverlayBitmap == null)
        {
            return;
        }

        using var pointPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0x7A, 0xCC, 235),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var linePaint = new SKPaint
        {
            Color = new SKColor(0x00, 0x7A, 0xCC, 210),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = ScreenToPdfDistance(1.5f),
        };
        using var snapPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xC1, 0x07, 240),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = ScreenToPdfDistance(2.0f),
        };
        float radius = ScreenToPdfDistance(5f);

        if (_draggingSheetOverlay)
        {
            DrawSheetOverlayDragGuide(canvas, linePaint);
            return;
        }

        if (_sheetOverlayPointEditStep is SheetOverlayPointEditStep.MoveTarget or SheetOverlayPointEditStep.ScaleSource or SheetOverlayPointEditStep.ScaleTarget)
        {
            SKPoint anchor = OverlayLocalToDisplay(_sheetOverlayEditAnchorLocal);
            canvas.DrawCircle(anchor, radius, pointPaint);
            SKPoint? guidePointer = SheetOverlayPointEditGuidePointer();
            if (guidePointer.HasValue && _sheetOverlayPointEditStep == SheetOverlayPointEditStep.MoveTarget)
                canvas.DrawLine(anchor, guidePointer.Value, linePaint);
        }

        if (_sheetOverlayPointEditStep == SheetOverlayPointEditStep.ScaleTarget)
        {
            SKPoint scalePoint = OverlayLocalToDisplay(_sheetOverlayEditScaleLocal);
            canvas.DrawCircle(scalePoint, radius, pointPaint);
            canvas.DrawLine(_sheetOverlayEditAnchorTarget, scalePoint, linePaint);
            SKPoint? guidePointer = SheetOverlayPointEditGuidePointer();
            if (guidePointer.HasValue)
                canvas.DrawLine(_sheetOverlayEditAnchorTarget, guidePointer.Value, linePaint);
        }

        DrawSheetOverlayPointEditSnapPreview(canvas, snapPaint, radius);
    }

    private SKPoint? SheetOverlayPointEditGuidePointer() =>
        _sheetOverlayPointEditSnapPreview ?? _lastPointerPdf;

    private void DrawSheetOverlayPointEditSnapPreview(SKCanvas canvas, SKPaint snapPaint, float radius)
    {
        if (!_sheetOverlayPointEditSnapPreview.HasValue)
            return;

        SKPoint preview = _sheetOverlayPointEditSnapPreview.Value;
        canvas.DrawCircle(preview, radius * 1.35f, snapPaint);
        canvas.DrawCircle(preview, radius * 0.45f, snapPaint);

        if (!_lastPointerPdf.HasValue ||
            OverlayDistance(_lastPointerPdf.Value, preview) <= ScreenToPdfDistance(2f))
        {
            return;
        }

        using var linePaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xC1, 0x07, 160),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = ScreenToPdfDistance(1.0f),
        };
        canvas.DrawLine(_lastPointerPdf.Value, preview, linePaint);
    }

    private void DrawSheetOverlayDragGuide(SKCanvas canvas, SKPaint linePaint)
    {
        if (!TryGetSheetOverlaySize(out float width, out float height))
            return;

        SKPoint p0 = OverlayLocalToDisplay(new SKPoint(0, 0));
        SKPoint p1 = OverlayLocalToDisplay(new SKPoint(width, 0));
        SKPoint p2 = OverlayLocalToDisplay(new SKPoint(width, height));
        SKPoint p3 = OverlayLocalToDisplay(new SKPoint(0, height));
        canvas.DrawLine(p0, p1, linePaint);
        canvas.DrawLine(p1, p2, linePaint);
        canvas.DrawLine(p2, p3, linePaint);
        canvas.DrawLine(p3, p0, linePaint);
    }

    private SKPoint OverlayLocalToDisplay(SKPoint localPoint) =>
        AddOffset(OverlayLocalTransformVector(localPoint, _sheetOverlayScale, _sheetOverlayRotationDegrees));

    private bool IsPointInsideSheetOverlay(SKPoint displayPoint)
    {
        if (!TryGetSheetOverlaySize(out float width, out float height))
            return false;

        SKPoint local = OverlayDisplayToLocal(displayPoint);
        float tolerance = ScreenToPdfDistance(8f) / Math.Max(_sheetOverlayScale, 0.001f);
        return local.X >= -tolerance &&
               local.Y >= -tolerance &&
               local.X <= width + tolerance &&
               local.Y <= height + tolerance;
    }

    private bool TryGetSheetOverlaySize(out float width, out float height)
    {
        width = _sheetOverlayWidthPt > 0 ? _sheetOverlayWidthPt : _pdfW;
        height = _sheetOverlayHeightPt > 0 ? _sheetOverlayHeightPt : _pdfH;
        return width > 0 && height > 0;
    }

    private SKRect OverlayDisplayBounds(float width, float height)
    {
        SKPoint p0 = OverlayLocalToDisplay(new SKPoint(0, 0));
        SKPoint p1 = OverlayLocalToDisplay(new SKPoint(width, 0));
        SKPoint p2 = OverlayLocalToDisplay(new SKPoint(width, height));
        SKPoint p3 = OverlayLocalToDisplay(new SKPoint(0, height));
        return new SKRect(
            MathF.Min(MathF.Min(p0.X, p1.X), MathF.Min(p2.X, p3.X)),
            MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y)),
            MathF.Max(MathF.Max(p0.X, p1.X), MathF.Max(p2.X, p3.X)),
            MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y)));
    }

    private SKPoint AddOffset(SKPoint vector) =>
        new(_sheetOverlayOffsetXPt + vector.X, _sheetOverlayOffsetYPt + vector.Y);

    private static SKPoint OverlayLocalTransformVector(
        SKPoint localPoint,
        float scale,
        float rotationDegrees)
    {
        float radians = rotationDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        float x = localPoint.X * scale;
        float y = localPoint.Y * scale;
        return new SKPoint(x * cos - y * sin, x * sin + y * cos);
    }

    private static float OverlayDistance(SKPoint a, SKPoint b) =>
        MeasurementGeometry.Distance(a, b);

    private static float NormalizeSheetOverlayScale(float scale) =>
        float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0
            ? 1f
            : Math.Clamp(scale, 0.05f, 20f);

    private static float NormalizeSheetOverlayRotation(float degrees)
    {
        if (float.IsNaN(degrees) || float.IsInfinity(degrees))
            return 0;

        float normalized = degrees % 360f;
        if (normalized > 180f)
            normalized -= 360f;
        if (normalized <= -180f)
            normalized += 360f;
        return normalized;
    }

    private void MaybeRequestSheetOverlayRenderScaleRefresh()
    {
        if (_sheetOverlayTransformPreviewActive ||
            !HasSheetOverlay ||
            _zoom <= 0 ||
            _isFastNavigating)
        {
            return;
        }

        float currentScale = float.MaxValue;
        float desired = 0;
        void ConsiderLayer(float widthPt, float heightPt, float scale, float bitmapScale)
        {
            if (widthPt <= 0 || heightPt <= 0 || bitmapScale <= 0)
                return;

            currentScale = Math.Min(currentScale, bitmapScale);
            desired = Math.Max(
                desired,
                ViewportRenderPolicy.SelectSheetOverlayRenderScale(
                    _zoom * scale,
                    widthPt,
                    heightPt));
        }

        if (_sheetOverlayBitmap != null)
        {
            ConsiderLayer(
                _sheetOverlayWidthPt,
                _sheetOverlayHeightPt,
                _sheetOverlayScale,
                _sheetOverlayBitmapScale);
        }
        foreach (SheetOverlayBitmapLayer layer in _sheetOverlayLayersBelow)
            ConsiderLayer(layer.WidthPt, layer.HeightPt, layer.Scale, layer.BitmapScale);
        foreach (SheetOverlayBitmapLayer layer in _sheetOverlayLayersAbove)
            ConsiderLayer(layer.WidthPt, layer.HeightPt, layer.Scale, layer.BitmapScale);

        if (currentScale == float.MaxValue ||
            desired <= currentScale * 1.18f ||
            desired <= _lastSheetOverlayRefreshRequestScale * 1.01f)
        {
            return;
        }

        _lastSheetOverlayRefreshRequestScale = desired;
        SheetOverlayRenderScaleRefreshRequested?.Invoke(desired);
    }

    private static float InferSheetOverlayBitmapScale(SKBitmap bitmap, float widthPt)
    {
        if (bitmap.Width <= 0 || widthPt <= 0)
            return 0;

        return bitmap.Width / widthPt;
    }
}
