using System;
using System.Collections.Generic;
using System.Linq;
using OurPlanCore;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private bool _boxSelectAnnotationDomain;

    private void BeginBoxSelection(SKPoint pdf, bool additive, bool removeMode = false)
    {
        ClearInProgressInputForEdit();
        _boxSelecting = true;
        _boxVertexMode = false;
        _boxSelectStartPdf = pdf;
        _boxSelectEndPdf = pdf;
        _boxSelectAdditive = additive;
        _boxSelectRemove = removeMode;
        _boxSelectAnnotationDomain = _annotationSelectionDomain;
        CaptureMouse();
        PostStatus(removeMode
            ? "Deselect: drag a box around objects to remove them from the selection."
            : additive
                ? "Select: drag a box to add objects to the selection."
                : "Select: drag a box around objects.");
        RequestRepaint();
    }

    // Alt-mode box/click that targets the selected Count/Line/Area object's vertices.
    private void BeginVertexBoxSelection(SKPoint pdf)
    {
        ClearInProgressInputForEdit();
        _boxSelecting = true;
        _boxVertexMode = true;
        _boxSelectStartPdf = pdf;
        _boxSelectEndPdf = pdf;
        _boxSelectAdditive = true;
        _boxSelectRemove = false;
        CaptureMouse();
        PostStatus("Vertices: Alt-click or Alt-box handles to toggle them; Alt-click body selects/clears all.");
        RequestRepaint();
    }

    private void FinishBoxSelection()
    {
        if (!_boxSelecting)
            return;

        SKRect rect = NormalizeRect(_boxSelectStartPdf, _boxSelectEndPdf);
        bool tiny = PdfToScreenDistance(rect.Width) < 4f &&
                    PdfToScreenDistance(rect.Height) < 4f;
        _boxSelecting = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();

        if (_boxVertexMode)
        {
            _boxVertexMode = false;
            FinishVertexBoxSelection(rect, tiny);
            return;
        }

        if (tiny)
        {
            if (_boxSelectAdditive)
            {
                // Precise click on a handle still toggles that single vertex.
                if (TryToggleMeasurementVertexSelection(_boxSelectStartPdf, _boxSelectRemove))
                {
                    RequestRepaint();
                    return;
                }

                if (_boxSelectAnnotationDomain &&
                    TryHitAnnotation(_boxSelectStartPdf, out PageAnnotation preferredAnnotation))
                {
                    ApplyAnnotationClickGesture(preferredAnnotation, _boxSelectRemove);
                    RequestRepaint();
                    return;
                }

                if (TryHitMeasurement(_boxSelectStartPdf, out Measurement hitMeasurement))
                {
                    ApplyAdditiveObjectGesture([hitMeasurement], [], _boxSelectRemove);
                    return;
                }

                if (TryHitAnnotation(_boxSelectStartPdf, out PageAnnotation toggledAnnotation))
                {
                    ApplyAnnotationClickGesture(toggledAnnotation, _boxSelectRemove);
                    RequestRepaint();
                    return;
                }
            }

            if (!_boxSelectAdditive)
                ClearSelection();
            RequestRepaint();
            PostStatus("Select: no box drawn.");
            return;
        }

        bool selectTouched = _boxSelectEndPdf.X < _boxSelectStartPdf.X;
        var hits = ActivePageMeasurementsNear(rect)
            .Where(m => selectTouched
                ? MeasurementIntersectsRect(m, rect)
                : MeasurementContainedInRect(m, rect))
            .ToList();
        IReadOnlyList<CutRegionRef> cutRegionHits = CutRegionSelectionService.FindInMarquee(
            ActivePageMeasurementsNear(rect),
            rect,
            selectTouched);
        var cutRegionParents = new HashSet<Measurement>(
            cutRegionHits.Select(cutRegion => cutRegion.Parent));
        hits.RemoveAll(measurement =>
            cutRegionParents.Contains(measurement) &&
            measurement.MType == "area" &&
            !CutRegionSelectionService.OuterBoundaryIntersectsRect(measurement.Points, rect));
        var annotationHits = _annotations.Where(annotation =>
                IsAnnotationVisibleOnActivePage(annotation) &&
                (selectTouched
                    ? AnnotationIntersectsRect(annotation, rect)
                    : AnnotationContainedInRect(annotation, rect)))
                .ToList();
        if (_boxSelectAnnotationDomain && annotationHits.Count > 0)
        {
            hits.Clear();
            cutRegionHits = [];
        }
        else if (hits.Count > 0 || cutRegionHits.Count > 0)
            annotationHits.Clear();

        if (_boxSelectAdditive)
        {
            if (hits.Count > 0 || cutRegionHits.Count > 0)
            {
                ApplyAdditiveObjectGesture(hits, cutRegionHits, _boxSelectRemove);
                return;
            }

            if (annotationHits.Count > 0)
            {
                if (_boxSelectRemove)
                {
                    var trimmed = GetSelectedAnnotations().Where(a => !annotationHits.Contains(a)).ToList();
                    SetSelectedAnnotations(trimmed, trimmed.LastOrDefault(), -1);
                }
                else
                {
                    var combinedAnnotations = GetSelectedAnnotations().ToList();
                    foreach (PageAnnotation hit in annotationHits)
                    {
                        if (!combinedAnnotations.Contains(hit))
                            combinedAnnotations.Add(hit);
                    }
                    SetSelectedAnnotations(combinedAnnotations, annotationHits.LastOrDefault(), -1);
                }
                RequestRepaint();
                PostStatus($"{GetSelectedAnnotations().Count} markup(s) selected.");
                return;
            }

            RequestRepaint();
            PostStatus("Select: nothing in box; current selection kept.");
            return;
        }

        if (annotationHits.Count > 0)
            SetSelectedAnnotations(annotationHits, annotationHits.LastOrDefault(), -1);
        else
            SetMixedMeasurementSelection(hits, cutRegionHits, hits.LastOrDefault());

        RequestRepaint();
        PostStatus(annotationHits.Count > 0
            ? annotationHits.Count == 1
                ? $"Selected {ToolTitle(annotationHits[0].Kind)} markup."
                : $"Selected {annotationHits.Count} markups. Delete removes them."
            : hits.Count == 0 && cutRegionHits.Count == 0
            ? selectTouched
                ? "Crossing select: no measurements touched by box."
                : "Window select: no measurements fully inside box."
            : selectTouched
                ? $"Selected {GetSelectedMeasurements().Count} measurement(s) and {SelectedCutRegionCount} touched cutout(s)."
                : $"Selected {GetSelectedMeasurements().Count} measurement(s) and {SelectedCutRegionCount} enclosed cutout(s).");
    }

    // Ctrl adds/toggles objects; Ctrl+Shift removes them. Shift alone is Ortho.
    private void ApplyAdditiveObjectGesture(
        IReadOnlyList<Measurement> hits,
        IReadOnlyList<CutRegionRef> cutRegionHits,
        bool removeMode)
    {
        var selected = GetSelectedMeasurements().ToList();
        var selectedCutRegions = SelectedCutRegions().ToList();

        if (removeMode)
        {
            var combined = selected.Where(m => !hits.Contains(m)).ToList();
            var combinedCutRegions = selectedCutRegions
                .Where(cutRegion => !cutRegionHits.Contains(cutRegion))
                .ToList();
            int removed = selected.Count - combined.Count +
                          selectedCutRegions.Count - combinedCutRegions.Count;
            SetMixedMeasurementSelection(combined, combinedCutRegions, combined.LastOrDefault());
            RequestRepaint();
            PostStatus(combined.Count == 0 && combinedCutRegions.Count == 0
                ? "Selection cleared."
                : $"Removed {removed} object(s). {combined.Count} measurement(s) and {combinedCutRegions.Count} cutout(s) remain.");
            return;
        }

        var newOnes = hits.Where(m => !selected.Contains(m)).ToList();
        var newCutRegions = cutRegionHits
            .Where(cutRegion =>
                !selectedCutRegions.Contains(cutRegion) &&
                !selected.Contains(cutRegion.Parent) &&
                !newOnes.Contains(cutRegion.Parent))
            .ToList();
        if (newOnes.Count > 0 || newCutRegions.Count > 0)
        {
            var combined = selected.Concat(newOnes).ToList();
            var combinedCutRegions = selectedCutRegions
                .Concat(newCutRegions)
                .Where(cutRegion => !combined.Contains(cutRegion.Parent))
                .ToList();
            SetMixedMeasurementSelection(
                combined,
                combinedCutRegions,
                newOnes.LastOrDefault() ?? combined.LastOrDefault());
            RequestRepaint();
            PostStatus($"Selected {combined.Count} measurement(s) and {combinedCutRegions.Count} cutout(s). Ctrl+C copies the bundle.");
            return;
        }

        // All hits already selected. Alt changes the vertex selection set; direct handle drag edits it.
        PostStatus($"{selected.Count} measurement(s) and {selectedCutRegions.Count} cutout(s) already selected.");
    }

    private void ApplyAnnotationClickGesture(PageAnnotation annotation, bool removeMode)
    {
        if (!removeMode)
        {
            ToggleAnnotationSelection(annotation);
            return;
        }

        var selected = GetSelectedAnnotations().ToList();
        if (!selected.Remove(annotation))
            return;

        SetSelectedAnnotations(selected, selected.LastOrDefault(), -1);
        PostStatus(selected.Count == 0
            ? "Selection cleared."
            : $"Removed markup. {selected.Count} still selected.");
    }

    private void SelectAllVerticesOf(IReadOnlyList<Measurement> measurements)
    {
        Measurement? primary = null;
        int primaryIndex = -1;
        foreach (Measurement m in measurements)
        {
            if (!IsMeasurementOnActivePage(m) || !CanEditMeasurementVertices(m))
                continue;

            HashSet<int> set = VertexSelectionSet(m, create: true);
            foreach (MeasurementVertexRef v in MeasurementVertices(m))
            {
                set.Add(v.GlobalIndex);
                primary = m;
                primaryIndex = v.GlobalIndex;
            }
            if (set.Count == 0)
                _selectedMeasurementVertexIndices.Remove(m);
        }

        if (primary != null)
        {
            _selectedMeasurement = primary;
            _selectedVertexIndex = primaryIndex;
        }

        RequestRepaint();
        int total = SelectedVertexCount();
        PostStatus(total == 0
            ? "No editable handles to select."
            : $"Selected all {total} handle(s). Drag a handle to move (Shift = ortho); Delete removes them.");
    }

    // Alt gesture resolved: a single handle click, an Alt-click on the body,
    // an Alt-click on empty (= clear), or a marquee over handles. Every hit
    // toggles so add/remove are both plain Alt.
    private void FinishVertexBoxSelection(SKRect rect, bool tiny)
    {
        var targets = GetSelectedMeasurements()
            .Where(m => IsMeasurementOnActivePage(m) && CanEditMeasurementVertices(m))
            .ToList();
        if (targets.Count == 0)
        {
            RequestRepaint();
            PostStatus("Select a Count, Line, or Area object first, then hold Alt to edit its vertices.");
            return;
        }

        if (tiny)
        {
            if (TryHitVertexAmong(targets, _boxSelectStartPdf, out Measurement hm, out int hv))
            {
                HashSet<int> set = VertexSelectionSet(hm, create: true);
                if (set.Contains(hv))
                    set.Remove(hv);
                else
                    set.Add(hv);
                if (set.Count == 0)
                    _selectedMeasurementVertexIndices.Remove(hm);
                _selectedMeasurement = hm;
                _selectedVertexIndex = IsMeasurementVertexSelected(hm, hv) ? hv : LastSelectedVertexIndex();
                RequestRepaint();
                PostStatus($"{SelectedVertexCount()} handle(s) selected.");
                return;
            }

            if (TryHitSelectedMeasurement(_boxSelectStartPdf, out _))
            {
                ToggleAllVerticesOf(targets);
                return;
            }

            ClearMeasurementVertexSelection(); // Alt-click empty = clear handles
            _selectedVertexIndex = -1;
            RequestRepaint();
            PostStatus("Vertex selection cleared.");
            return;
        }

        int changed = 0;
        int added = 0;
        int removed = 0;
        Measurement? primary = null;
        int primaryVertex = -1;
        foreach (Measurement m in targets)
        {
            _selectedMeasurementVertexIndices.TryGetValue(m, out HashSet<int>? set);
            foreach (MeasurementVertexRef v in MeasurementVertices(m))
            {
                if (!RectContains(rect, v.Point))
                    continue;

                set ??= VertexSelectionSet(m, create: true);
                if (set.Contains(v.GlobalIndex))
                {
                    set.Remove(v.GlobalIndex);
                    changed++;
                    removed++;
                }
                else
                {
                    set.Add(v.GlobalIndex);
                    changed++;
                    added++;
                    primary = m;
                    primaryVertex = v.GlobalIndex;
                }
            }

            if (set != null && set.Count == 0)
                _selectedMeasurementVertexIndices.Remove(m);
        }

        if (primary != null)
        {
            _selectedMeasurement = primary;
            _selectedVertexIndex = primaryVertex;
        }
        else if (removed > 0)
        {
            _selectedVertexIndex = LastSelectedVertexIndex();
        }

        RequestRepaint();
        int total = SelectedVertexCount();
        PostStatus($"Toggled {changed} handle(s): +{added}, -{removed}. {total} selected.");
    }

    private void ToggleAllVerticesOf(IReadOnlyList<Measurement> measurements)
    {
        int selected = SelectedVertexCount();
        int editable = measurements
            .Where(m => IsMeasurementOnActivePage(m) && CanEditMeasurementVertices(m))
            .Sum(m => MeasurementVertices(m).Count());
        if (editable > 0 && selected >= editable)
        {
            ClearMeasurementVertexSelection();
            _selectedVertexIndex = -1;
            RequestRepaint();
            PostStatus("Vertex selection cleared.");
            return;
        }

        SelectAllVerticesOf(measurements);
    }

    private bool TryHitVertexAmong(
        IReadOnlyList<Measurement> targets,
        SKPoint pdf,
        out Measurement measurement,
        out int vertexIndex)
    {
        foreach (Measurement m in targets)
        {
            if (TryHitVertexOnMeasurement(m, pdf, SelectedVertexHitToleranceScreenPx, out vertexIndex))
            {
                measurement = m;
                return true;
            }
        }

        measurement = null!;
        vertexIndex = -1;
        return false;
    }

    private void CancelBoxSelection()
    {
        if (!_boxSelecting)
            return;

        _boxSelecting = false;
        _boxVertexMode = false;
        RequestRepaint();
    }

    private void PostBoxSelectionStatus()
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastPointerStatusAt).TotalMilliseconds < 120)
            return;

        _lastPointerStatusAt = now;
        SKRect rect = NormalizeRect(_boxSelectStartPdf, _boxSelectEndPdf);
        string mode = _boxSelectEndPdf.X < _boxSelectStartPdf.X
            ? "crossing"
            : "inside only";
        PostStatus($"Select box ({mode}): {PdfToScreenDistance(rect.Width):F0}px x {PdfToScreenDistance(rect.Height):F0}px.");
    }

}
