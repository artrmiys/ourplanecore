using System;
using System.Collections.Generic;
using System.Linq;
using OurPlanCore;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private enum ViewportClipboardPayload
    {
        None,
        CutRegions,
        Measurements,
        Annotations,
    }

    private sealed record AnnotationClipboardEntry(
        string Kind,
        string Text,
        string Color,
        double StrokeWidth,
        double ScaleMetersPerPt,
        bool Hidden,
        IReadOnlyList<SKPoint> Points);

    private static readonly object AnnotationClipboardGate = new();
    private static List<AnnotationClipboardEntry> _annotationClipboard = [];
    private static ViewportClipboardPayload _currentViewportClipboardPayload;

    public IReadOnlyList<PageAnnotation> GetSelectedPageAnnotations() =>
        GetSelectedAnnotations();

    public int AnnotationClipboardCount
    {
        get
        {
            lock (AnnotationClipboardGate)
                return _annotationClipboard.Count;
        }
    }

    private bool IsAnnotationClipboardCurrent
    {
        get
        {
            lock (AnnotationClipboardGate)
            {
                return _currentViewportClipboardPayload == ViewportClipboardPayload.Annotations &&
                       _annotationClipboard.Count > 0;
            }
        }
    }

    private bool IsCutRegionClipboardCurrent
    {
        get
        {
            lock (AnnotationClipboardGate)
            {
                return _currentViewportClipboardPayload == ViewportClipboardPayload.CutRegions &&
                       _holeClipboard.Count > 0;
            }
        }
    }

    private void MarkCutRegionClipboardCurrent()
    {
        lock (AnnotationClipboardGate)
            _currentViewportClipboardPayload = ViewportClipboardPayload.CutRegions;
    }

    public void MarkMeasurementClipboardCurrent()
    {
        _holeClipboard.Clear();
        lock (AnnotationClipboardGate)
            _currentViewportClipboardPayload = ViewportClipboardPayload.Measurements;
    }

    private bool CopySelectedPageAnnotations() =>
        CopyPageAnnotations(GetSelectedAnnotations());

    public bool CopyPageAnnotations(IEnumerable<PageAnnotation> annotations)
    {
        var source = annotations
            .Where(annotation =>
                _annotations.Contains(annotation) &&
                IsAnnotationVisibleOnActivePage(annotation))
            .Distinct()
            .ToList();
        if (source.Count == 0)
            return false;

        _holeClipboard.Clear();
        var entries = source
            .Select(annotation => new AnnotationClipboardEntry(
                annotation.Kind,
                annotation.Text,
                annotation.Color,
                annotation.StrokeWidth,
                annotation.ScaleMetersPerPt,
                annotation.Hidden,
                annotation.Points.Select(point => new SKPoint(point.X, point.Y)).ToList()))
            .ToList();
        lock (AnnotationClipboardGate)
        {
            _annotationClipboard = entries;
            _currentViewportClipboardPayload = ViewportClipboardPayload.Annotations;
        }

        PostStatus(
            $"Copied {entries.Count} markup(s). Paste uses the copied set's top-left corner as the cursor anchor.");
        return true;
    }

    public bool PasteCopiedPageAnnotations(SKPoint? pasteAtPdf)
    {
        if (IsReadOnlyMode)
        {
            PostStatus("Read-only: markups cannot be pasted.");
            return false;
        }

        List<AnnotationClipboardEntry> entries;
        lock (AnnotationClipboardGate)
            entries = _annotationClipboard.ToList();
        if (entries.Count == 0)
        {
            PostStatus("No copied markups to paste.");
            return false;
        }

        var allPoints = entries.SelectMany(entry => entry.Points).ToList();
        if (allPoints.Count == 0)
        {
            PostStatus("Copied markups contain no geometry.");
            return false;
        }

        float left = allPoints.Min(point => point.X);
        float top = allPoints.Min(point => point.Y);
        SKPoint target = pasteAtPdf ??
                         _lastPointerPdf ??
                         new SKPoint(left + ScreenToPdfDistance(12f), top + ScreenToPdfDistance(12f));
        var offset = new SKPoint(target.X - left, target.Y - top);
        var pasted = entries
            .Select(entry => new PageAnnotation
            {
                Kind = entry.Kind,
                Text = entry.Text,
                Color = entry.Color,
                StrokeWidth = entry.StrokeWidth,
                PageFolder = _pageFolder,
                ScaleMetersPerPt = ScaleMetersPerPt > 0
                    ? ScaleMetersPerPt
                    : entry.ScaleMetersPerPt,
                Hidden = entry.Hidden,
                Points = entry.Points
                    .Select(point => new SKPoint(point.X + offset.X, point.Y + offset.Y))
                    .ToList(),
            })
            .ToList();

        _annotations.AddRange(pasted);
        PushAddedAnnotationsUndo(pasted, $"remove pasted {pasted.Count} markup(s)");
        SetSelectedAnnotations(pasted, pasted.LastOrDefault(), -1);
        RequestRepaint();
        foreach (PageAnnotation annotation in pasted)
            PageAnnotationAdded?.Invoke(annotation);
        PostStatus($"Pasted {pasted.Count} markup(s) to this sheet.");
        return true;
    }
}
