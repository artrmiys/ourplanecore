using System.Linq;
using System.Windows.Input;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private bool _isReadOnlyMode;

    public bool IsReadOnlyMode
    {
        get => _isReadOnlyMode;
        set
        {
            if (_isReadOnlyMode == value)
                return;

            _isReadOnlyMode = value;
            if (!value)
                return;

            CancelMutableInteractionForReadOnly();
            _tool = ViewerTool.Select;
            UpdateCursor();
            PostStatus("Read-only: navigation, selection, and copy remain available.");
            RequestRepaint();
        }
    }

    private bool HandleReadOnlyLeftMouseDown(SKPoint pdf)
    {
        if (!IsReadOnlyMode || _tool == ViewerTool.Pan)
            return false;

        if (_tool != ViewerTool.Select)
            _tool = ViewerTool.Select;

        bool remove = IsDeselectModifierActive();
        if (IsSelectionModifierActive())
        {
            if (TryHitMeasurement(pdf, out Measurement toggledMeasurement))
                ToggleMeasurementSelection(toggledMeasurement);
            else if (TryHitAnnotation(pdf, out PageAnnotation toggledAnnotation))
                ToggleAnnotationSelection(toggledAnnotation);
            else
                BeginBoxSelection(pdf, additive: true, removeMode: remove);

            return true;
        }

        if (TryHitMeasurement(pdf, out Measurement measurement))
        {
            SelectMeasurement(measurement, -1);
            return true;
        }

        if (TryHitAnnotation(pdf, out PageAnnotation annotation))
        {
            SelectAnnotation(annotation, -1);
            return true;
        }

        BeginBoxSelection(pdf, additive: false, removeMode: false);
        return true;
    }

    private bool HandleReadOnlyKeyDown(KeyEventArgs e)
    {
        if (!IsReadOnlyMode)
            return false;

        Key key = KeyboardShortcutKeys.EffectiveKey(e);
        ModifierKeys modifiers = Keyboard.Modifiers;
        bool blocked = key is Key.Delete or Key.Back or Key.F2 ||
                       key == Key.T && modifiers == ModifierKeys.None ||
                       key == Key.Enter && _pdfLayerTraceEnabled ||
                       modifiers == ModifierKeys.Control && key is Key.V or Key.X or Key.Z or Key.Y ||
                       modifiers == ModifierKeys.None && key is
                           Key.S or Key.R or Key.H or Key.D or Key.B or Key.O or
                           Key.N or Key.P or Key.L or Key.A or Key.J or Key.X;
        if (!blocked)
            return false;

        PostStatus("Read-only: this command cannot change the job.");
        e.Handled = true;
        return true;
    }

    private void CancelMutableInteractionForReadOnly()
    {
        RestoreMeasurementDragOriginals();
        RestoreAnnotationDragOriginals();
        RestoreTransformOriginals();
        CancelSheetOverlayDrag(silent: true);
        CancelSheetOverlayPointEdit(silent: true);
        CancelAiCropNoteSelection(postStatus: false);
        CancelSimilarCountSelection(postStatus: false);
        CancelJoistDirectionCapture();
        SetThreeDRoofMode(false, _threeDRoofGuideKind);
        SetThreeDRoofEdgeSelectMode(false);
        if (_pdfLayerTraceEnabled)
            SetPdfLayerTraceEnabled(false);
        CancelDrawing(clearSelection: false);
        ClearSelection();
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private void RestoreMeasurementDragOriginals()
    {
        foreach (var (measurement, originals) in _dragMeasurementVertexOriginalPoints)
        {
            foreach (var (index, point) in originals)
            {
                if (index >= 0 && index < measurement.Points.Count)
                    measurement.Points[index] = point;
            }
        }

        foreach (var (measurement, points) in _dragSelectionOriginalPoints)
            RestoreMeasurementGeometry(measurement, points, _dragSelectionOriginalHoles.GetValueOrDefault(measurement));

        if (_dragSelectionOriginalPoints.Count == 0 &&
            _selectedMeasurement != null &&
            _dragMeasurementOriginalPoints.Count > 0)
        {
            RestoreMeasurementGeometry(
                _selectedMeasurement,
                _dragMeasurementOriginalPoints,
                _dragMeasurementOriginalHoles);
        }
    }

    private void RestoreAnnotationDragOriginals()
    {
        foreach (var (annotation, points) in _dragAnnotationSelectionOriginalPoints)
            ReplacePoints(annotation.Points, points);

        if (_dragAnnotationSelectionOriginalPoints.Count == 0 &&
            _selectedAnnotation != null &&
            _dragAnnotationOriginalPoints.Count > 0)
        {
            ReplacePoints(_selectedAnnotation.Points, _dragAnnotationOriginalPoints);
        }
    }

    private void RestoreTransformOriginals()
    {
        foreach (var (measurement, points) in _transformMeasurementOriginalPoints)
        {
            RestoreMeasurementGeometry(
                measurement,
                points,
                _transformMeasurementOriginalHoles.GetValueOrDefault(measurement));
            if (_transformMeasurementOriginalJoistDirections.TryGetValue(measurement, out double direction))
                measurement.JoistDirectionDegrees = direction;
        }

        foreach (var (annotation, points) in _transformAnnotationOriginalPoints)
            ReplacePoints(annotation.Points, points);
    }

    private static void RestoreMeasurementGeometry(
        Measurement measurement,
        System.Collections.Generic.IEnumerable<SKPoint> points,
        System.Collections.Generic.IEnumerable<System.Collections.Generic.List<SKPoint>>? holes)
    {
        ReplacePoints(measurement.Points, points);
        measurement.Holes.Clear();
        if (holes != null)
            measurement.Holes.AddRange(holes.Select(hole => hole.ToList()));
    }

    private static void ReplacePoints(
        System.Collections.Generic.List<SKPoint> target,
        System.Collections.Generic.IEnumerable<SKPoint> source)
    {
        target.Clear();
        target.AddRange(source);
    }
}
