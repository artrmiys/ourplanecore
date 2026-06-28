using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OurPlaneCore;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private void SelectMeasurement(Measurement measurement, int vertexIndex)
    {
        SetSelectedMeasurements([measurement], measurement, vertexIndex);
    }

    private void SetSelectedMeasurements(IReadOnlyList<Measurement> measurements, Measurement? primary, int vertexIndex)
    {
        var next = measurements
            .Where(m => _measurementSet.Contains(m) && IsMeasurementOnActivePage(m))
            .Distinct()
            .ToList();

        Measurement? nextPrimary = primary != null && next.Contains(primary)
            ? primary
            : next.LastOrDefault();

        bool setChanged = _selectedMeasurements.Count != next.Count ||
                          next.Any(m => !_selectedMeasurements.Contains(m));
        bool primaryChanged = !ReferenceEquals(_selectedMeasurement, nextPrimary);
        bool vertexChanged = _selectedVertexIndex != vertexIndex;

        _selectedMeasurements.Clear();
        foreach (Measurement measurement in next)
            _selectedMeasurements.Add(measurement);

        _selectedMeasurement = nextPrimary;
        _selectedVertexIndex = nextPrimary == null ? -1 : vertexIndex;
        ClearMeasurementVertexSelection();
        if (nextPrimary != null && vertexIndex >= 0)
            VertexSelectionSet(nextPrimary, create: true).Add(vertexIndex);

        if (next.Count > 0)
            ClearAnnotationSelection();

        if (primaryChanged)
            MeasurementSelectionChanged?.Invoke(nextPrimary);
        if (setChanged)
            MeasurementsSelectionChanged?.Invoke(next);
        if (primaryChanged || setChanged || vertexChanged)
            RequestRepaint();
        PublishTransformSelectionChanged();
    }

    private void ToggleMeasurementSelection(Measurement measurement)
    {
        if (!_measurementSet.Contains(measurement) || !IsMeasurementOnActivePage(measurement))
            return;

        var selected = GetSelectedMeasurements().ToList();
        if (selected.Contains(measurement))
            selected.Remove(measurement);
        else
            selected.Add(measurement);

        SetSelectedMeasurements(selected, selected.Contains(measurement) ? measurement : selected.LastOrDefault(), -1);
        PostStatus(selected.Count == 0
            ? "Selection cleared."
            : $"Selected {selected.Count} measurement(s). Ctrl+C copies, Ctrl+V pastes.");
    }

    private void SelectAllActivePageMeasurements()
    {
        var selected = ActivePageMeasurements().ToList();
        SetSelectedMeasurements(selected, selected.LastOrDefault(), -1);
        PostStatus(selected.Count == 0
            ? "No measurements on this sheet."
            : $"Selected all {selected.Count} measurement(s) on this sheet.");
    }

    private void SelectAnnotation(PageAnnotation annotation, int vertexIndex)
    {
        SetSelectedAnnotations([annotation], annotation, vertexIndex);
    }

    private IReadOnlyList<PageAnnotation> GetSelectedAnnotations() =>
        _selectedAnnotations
            .Where(annotation => _annotations.Contains(annotation) && IsAnnotationVisibleOnActivePage(annotation))
            .ToList();

    private void SetSelectedAnnotations(IReadOnlyList<PageAnnotation> annotations, PageAnnotation? primary, int vertexIndex)
    {
        var next = annotations
            .Where(annotation => _annotations.Contains(annotation) && IsAnnotationVisibleOnActivePage(annotation))
            .Distinct()
            .ToList();
        PageAnnotation? nextPrimary = primary != null && next.Contains(primary)
            ? primary
            : next.LastOrDefault();

        bool setChanged = _selectedAnnotations.Count != next.Count ||
                          next.Any(annotation => !_selectedAnnotations.Contains(annotation));
        bool primaryChanged = !ReferenceEquals(_selectedAnnotation, nextPrimary);
        bool vertexChanged = _selectedAnnotationVertexIndex != vertexIndex;

        if (next.Count > 0 &&
            (_selectedMeasurements.Count > 0 || _selectedMeasurement != null))
        {
            _selectedMeasurements.Clear();
            _selectedMeasurement = null;
            _selectedVertexIndex = -1;
            ClearMeasurementVertexSelection();
            MeasurementSelectionChanged?.Invoke(null);
            MeasurementsSelectionChanged?.Invoke(Array.Empty<Measurement>());
        }

        _selectedAnnotations.Clear();
        foreach (PageAnnotation annotation in next)
            _selectedAnnotations.Add(annotation);

        _selectedAnnotation = nextPrimary;
        _selectedAnnotationVertexIndex = nextPrimary == null ? -1 : vertexIndex;
        if (primaryChanged || setChanged || vertexChanged)
            RequestRepaint();
        PublishTransformSelectionChanged();
    }

    private void ToggleAnnotationSelection(PageAnnotation annotation)
    {
        if (!_annotations.Contains(annotation) || !IsAnnotationVisibleOnActivePage(annotation))
            return;

        var selected = GetSelectedAnnotations().ToList();
        if (selected.Contains(annotation))
            selected.Remove(annotation);
        else
            selected.Add(annotation);

        SetSelectedAnnotations(selected, selected.Contains(annotation) ? annotation : selected.LastOrDefault(), -1);
        PostStatus(selected.Count == 0
            ? "Selection cleared."
            : $"Selected {selected.Count} markup(s). Delete removes them.");
    }

    private void CenterOnMeasurement(Measurement measurement)
    {
        if (measurement.Points.Count == 0 || !HasViewportCanvasSize || _zoom <= 0)
            return;

        SKRect bounds = MeasurementBounds(measurement);
        float centerX = (bounds.Left + bounds.Right) / 2f;
        float centerY = (bounds.Top + bounds.Bottom) / 2f;
        float visibleW = ScreenToPdfDistance(ViewportCanvasWidth);
        float visibleH = ScreenToPdfDistance(ViewportCanvasHeight);

        _panX = centerX - visibleW / 2f;
        _panY = centerY - visibleH / 2f;
        ClampPanToPage();
        ScheduleRerenderForZoom(force: false);
    }

    private void ClampPanToPage()
    {
        if (_pdfW <= 0 || _pdfH <= 0 || !HasViewportCanvasSize || _zoom <= 0)
            return;

        float visibleW = ScreenToPdfDistance(ViewportCanvasWidth);
        float visibleH = ScreenToPdfDistance(ViewportCanvasHeight);
        _panX = ViewportRenderPolicy.ClampPanWithOverscroll(_panX, _pdfW, visibleW);
        _panY = ViewportRenderPolicy.ClampPanWithOverscroll(_panY, _pdfH, visibleH);
    }

    private void ClearSelection()
    {
        bool changed = _selectedMeasurement != null ||
                       _selectedMeasurements.Count > 0 ||
                       _selectedAnnotation != null ||
                       _selectedAnnotations.Count > 0;
        _selectedMeasurement = null;
        _selectedMeasurements.Clear();
        _selectedVertexIndex = -1;
        ClearMeasurementVertexSelection();
        ClearAnnotationSelection();
        _draggingVertex = false;
        _draggingMeasurement = false;
        _draggingAnnotationVertex = false;
        _draggingAnnotation = false;
        _draggingTransformScale = false;
        _draggingTransformRotate = false;
        _dragMeasurementChanged = false;
        _dragAnnotationChanged = false;
        _dragMeasurementOriginalPoints.Clear();
        _dragMeasurementOriginalHoles.Clear();
        _dragMeasurementVertexOriginalPoints.Clear();
        _dragSelectionOriginalPoints.Clear();
        _dragSelectionOriginalHoles.Clear();
        _dragAnnotationOriginalPoints.Clear();
        _dragAnnotationSelectionOriginalPoints.Clear();
        _transformMeasurementOriginalPoints.Clear();
        _transformMeasurementOriginalHoles.Clear();
        _transformMeasurementOriginalJoistDirections.Clear();
        _transformAnnotationOriginalPoints.Clear();
        if (changed)
        {
            MeasurementSelectionChanged?.Invoke(null);
            MeasurementsSelectionChanged?.Invoke(Array.Empty<Measurement>());
        }
        PublishTransformSelectionChanged();
    }

    private void ClearAnnotationSelection()
    {
        _selectedAnnotation = null;
        _selectedAnnotations.Clear();
        _selectedAnnotationVertexIndex = -1;
    }

    private void DeleteSelectedOverlay()
    {
        if (TryDeleteSelectedMeasurementVertices())
            return;

        if (GetSelectedMeasurements().Count > 0)
        {
            DeleteSelectedMeasurement();
            return;
        }

        DeleteSelectedAnnotation();
    }

    private void DeleteSelectedMeasurement()
    {
        var selected = GetSelectedMeasurements();
        if (selected.Count == 0)
            return;

        PushRemovedMeasurementsUndo(selected, $"restore {selected.Count} deleted measurement(s)");
        foreach (Measurement removed in selected)
        {
            _measurements.Remove(removed);
            _measurementSet.Remove(removed);
            RemoveMeasurementFromPageIndex(removed);
            ForgetMeasurementState(removed);
        }
        ClearSelection();
        RequestRepaint();
        PostStatus(selected.Count == 1
            ? $"Deleted {selected[0].MType}."
            : $"Deleted {selected.Count} selected measurements.");
        NotifyMeasurementsRemoved(selected);
    }

    private void DeleteSelectedAnnotation()
    {
        var selected = GetSelectedAnnotations();
        if (selected.Count == 0 && _selectedAnnotation != null)
            selected = [_selectedAnnotation];
        if (selected.Count == 0)
            return;

        DeletePageAnnotations(selected);
    }

    private bool IsMeasurementOnActivePage(Measurement measurement) =>
        IsSamePageFolder(measurement.PageFolder, _pageFolder) &&
        IsMeasurementTakeoffVisible(measurement);

    private bool IsMeasurementTakeoffVisible(Measurement measurement)
    {
        if (!string.IsNullOrWhiteSpace(measurement.Id) &&
            _hiddenMeasurementIds.Contains(measurement.Id.Trim()))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(measurement.TakeoffFolder))
            return true;

        return !_hiddenTakeoffFolders.Contains(NormalizePageFolderForCompare(measurement.TakeoffFolder));
    }

    private bool IsAnnotationOnActivePage(PageAnnotation annotation) =>
        IsSamePageFolder(annotation.PageFolder, _pageFolder);

    private bool IsAnnotationVisibleOnActivePage(PageAnnotation annotation) =>
        IsAnnotationOnActivePage(annotation) &&
        (!_hideRulerAnnotations || !IsRulerAnnotation(annotation) || !annotation.Hidden);

    private static bool IsRulerAnnotation(PageAnnotation annotation) =>
        string.Equals(
            OurPlaneCoreJobStore.NormalizePageAnnotationKind(annotation.Kind),
            "dimension",
            StringComparison.OrdinalIgnoreCase);

    private bool IsMeasurementSelected(Measurement measurement) =>
        _selectedMeasurements.Contains(measurement);

    private bool IsAnnotationSelected(PageAnnotation annotation) =>
        _selectedAnnotations.Contains(annotation);

    private void PruneHiddenAnnotationSelection()
    {
        if (_selectedAnnotations.Count == 0 && _selectedAnnotation == null)
            return;

        var visible = GetSelectedAnnotations();
        if (visible.Count == _selectedAnnotations.Count &&
            (_selectedAnnotation == null || visible.Contains(_selectedAnnotation)))
        {
            return;
        }

        SetSelectedAnnotations(visible, visible.Contains(_selectedAnnotation) ? _selectedAnnotation : visible.LastOrDefault(), -1);
    }

    private IReadOnlyList<Measurement> ActivePageMeasurements()
    {
        if (string.IsNullOrWhiteSpace(_pageFolder))
        {
            if (!HasMeasurementVisibilityFilters())
                return _measurements;

            return VisibleActivePageMeasurements("", _measurements);
        }

        string key = NormalizePageFolderForCompare(_pageFolder);
        if (!_measurementsByPage.TryGetValue(key, out List<Measurement>? measurements))
            return Array.Empty<Measurement>();

        if (!HasMeasurementVisibilityFilters())
            return measurements;

        return VisibleActivePageMeasurements(key, measurements);
    }

    private bool HasMeasurementVisibilityFilters() =>
        _hiddenTakeoffFolders.Count != 0 ||
        _hiddenMeasurementIds.Count != 0;

    private IReadOnlyList<Measurement> VisibleActivePageMeasurements(
        string key,
        IReadOnlyList<Measurement> measurements)
    {
        if (measurements.Count == 0)
            return Array.Empty<Measurement>();

        int folderHash = TakeoffFolderHash(measurements);
        if (_visibleActivePageMeasurementsIndexVersion == _measurementIndexVersion &&
            _visibleActivePageMeasurementsHiddenVersion == _measurementVisibilityVersion &&
            _visibleActivePageMeasurementsFolderHash == folderHash &&
            string.Equals(_visibleActivePageMeasurementsPageKey, key, StringComparison.OrdinalIgnoreCase))
        {
            return _visibleActivePageMeasurements;
        }

        _visibleActivePageMeasurements.Clear();
        foreach (Measurement measurement in measurements)
        {
            if (IsMeasurementTakeoffVisible(measurement))
                _visibleActivePageMeasurements.Add(measurement);
        }

        _visibleActivePageMeasurementsPageKey = key;
        _visibleActivePageMeasurementsIndexVersion = _measurementIndexVersion;
        _visibleActivePageMeasurementsHiddenVersion = _measurementVisibilityVersion;
        _visibleActivePageMeasurementsFolderHash = folderHash;
        return _visibleActivePageMeasurements;
    }

    private IReadOnlyList<Measurement> ActivePageMeasurementsNear(SKRect rect) =>
        ActivePageMeasurementSpatialIndex().Query(rect);

    private IReadOnlyList<ViewportMeasurementVertexCandidate> ActivePageMeasurementVerticesNear(SKRect rect) =>
        ActivePageMeasurementSpatialIndex().QueryVertices(rect);

    private IReadOnlyList<ViewportMeasurementSegmentCandidate> ActivePageMeasurementSegmentsNear(SKRect rect) =>
        ActivePageMeasurementSpatialIndex().QuerySegments(rect);

    private ViewportMeasurementSpatialIndex ActivePageMeasurementSpatialIndex()
    {
        IReadOnlyList<Measurement> activeMeasurements = ActivePageMeasurements();
        string key = string.IsNullOrWhiteSpace(_pageFolder)
            ? ""
            : NormalizePageFolderForCompare(_pageFolder);
        int folderHash = TakeoffFolderHash(activeMeasurements);

        if (_activePageMeasurementSpatialIndex != null &&
            _activePageMeasurementSpatialIndexVersion == _measurementIndexVersion &&
            _activePageMeasurementSpatialIndexGeometryVersion == _measurementGeometryVersion &&
            _activePageMeasurementSpatialIndexHiddenVersion == _measurementVisibilityVersion &&
            _activePageMeasurementSpatialIndexFolderHash == folderHash &&
            string.Equals(_activePageMeasurementSpatialIndexPageKey, key, StringComparison.OrdinalIgnoreCase))
        {
            return _activePageMeasurementSpatialIndex;
        }

        _activePageMeasurementSpatialIndex = new ViewportMeasurementSpatialIndex(activeMeasurements);
        _activePageMeasurementSpatialIndexPageKey = key;
        _activePageMeasurementSpatialIndexVersion = _measurementIndexVersion;
        _activePageMeasurementSpatialIndexGeometryVersion = _measurementGeometryVersion;
        _activePageMeasurementSpatialIndexHiddenVersion = _measurementVisibilityVersion;
        _activePageMeasurementSpatialIndexFolderHash = folderHash;
        return _activePageMeasurementSpatialIndex;
    }

    private void InvalidateActivePageMeasurementSpatialIndex()
    {
        _measurementGeometryVersion++;
        _activePageMeasurementSpatialIndex = null;
        _activePageMeasurementSpatialIndexPageKey = "";
        _activePageMeasurementSpatialIndexVersion = -1;
        _activePageMeasurementSpatialIndexGeometryVersion = -1;
        _activePageMeasurementSpatialIndexHiddenVersion = -1;
        _activePageMeasurementSpatialIndexFolderHash = 0;
    }

    private SKRect MeasurementHitSearchRect(SKPoint pdf, float screenTolerancePx)
    {
        float tol = screenTolerancePx / Math.Max(_zoom, 0.01f);
        return SKRect.Create(pdf.X - tol, pdf.Y - tol, tol * 2f, tol * 2f);
    }

    private static int TakeoffFolderHash(IReadOnlyList<Measurement> measurements)
    {
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < measurements.Count; i++)
            {
                hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(
                    measurements[i].TakeoffFolder ?? string.Empty);
            }

            return hash;
        }
    }

    private void IndexMeasurementByPage(Measurement measurement)
    {
        if (string.IsNullOrWhiteSpace(measurement.PageFolder))
        {
            InvalidateActivePageMeasurementCache();
            return;
        }

        string key = NormalizePageFolderForCompare(measurement.PageFolder);
        if (!_measurementsByPage.TryGetValue(key, out List<Measurement>? measurements))
        {
            measurements = [];
            _measurementsByPage[key] = measurements;
        }

        if (!measurements.Contains(measurement))
            measurements.Add(measurement);
        InvalidateActivePageMeasurementCache();
    }

    private void RemoveMeasurementFromPageIndex(Measurement measurement)
    {
        if (string.IsNullOrWhiteSpace(measurement.PageFolder))
        {
            InvalidateActivePageMeasurementCache();
            return;
        }

        string key = NormalizePageFolderForCompare(measurement.PageFolder);
        if (!_measurementsByPage.TryGetValue(key, out List<Measurement>? measurements))
        {
            InvalidateActivePageMeasurementCache();
            return;
        }

        measurements.Remove(measurement);
        if (measurements.Count == 0)
            _measurementsByPage.Remove(key);
        InvalidateActivePageMeasurementCache();
    }

    private void InvalidateActivePageMeasurementCache()
    {
        _measurementIndexVersion++;
        _visibleActivePageMeasurements.Clear();
        _visibleActivePageMeasurementsPageKey = "";
        _visibleActivePageMeasurementsIndexVersion = -1;
        _visibleActivePageMeasurementsHiddenVersion = -1;
        _visibleActivePageMeasurementsFolderHash = 0;
        InvalidateActivePageMeasurementSpatialIndex();
    }

    private void InvalidateMeasurementVisibilityCache()
    {
        _measurementVisibilityVersion++;
        _visibleActivePageMeasurements.Clear();
        _visibleActivePageMeasurementsPageKey = "";
        _visibleActivePageMeasurementsIndexVersion = -1;
        _visibleActivePageMeasurementsHiddenVersion = -1;
        _visibleActivePageMeasurementsFolderHash = 0;
        InvalidateActivePageMeasurementSpatialIndex();
    }

    private void ForgetMeasurementState(Measurement measurement)
    {
        _selectedMeasurements.Remove(measurement);
        _selectedMeasurementVertexIndices.Remove(measurement);
        _dragMeasurementVertexOriginalPoints.Remove(measurement);
        _dragSelectionOriginalPoints.Remove(measurement);
        _dragSelectionOriginalHoles.Remove(measurement);
        _transformMeasurementOriginalPoints.Remove(measurement);
        _transformMeasurementOriginalHoles.Remove(measurement);

        if (ReferenceEquals(_selectedMeasurement, measurement))
        {
            _selectedMeasurement = null;
            _dragMeasurementOriginalPoints.Clear();
            _dragMeasurementOriginalHoles.Clear();
        }
    }

    private void PruneHiddenMeasurementSelection()
    {
        var hiddenSelected = _selectedMeasurements
            .Where(measurement => !IsMeasurementOnActivePage(measurement))
            .ToList();
        if (hiddenSelected.Count == 0)
            return;

        foreach (Measurement measurement in hiddenSelected)
            ForgetMeasurementState(measurement);

        if (_selectedMeasurements.Count == 0)
        {
            ClearSelection();
            return;
        }

        if (_selectedMeasurement == null || !_selectedMeasurements.Contains(_selectedMeasurement))
        {
            _selectedMeasurement = _selectedMeasurements.LastOrDefault();
            _selectedVertexIndex = -1;
            ClearMeasurementVertexSelection();
        }

        MeasurementSelectionChanged?.Invoke(_selectedMeasurement);
        MeasurementsSelectionChanged?.Invoke(_selectedMeasurements.ToList());
        PublishTransformSelectionChanged();
    }

    private static bool IsSamePageFolder(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(NormalizePageFolderForCompare(left), NormalizePageFolderForCompare(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePageFolderForCompare(string path)
    {
        string trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0)
            return "";

        try
        {
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return trimmed;
        }
    }
}
