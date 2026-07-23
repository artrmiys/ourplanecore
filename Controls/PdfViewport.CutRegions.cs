using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using OurPlanCore;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    // Clipboard ownership is in PdfViewport.CutRegionClipboard.cs, where copy
    // commits the shared payload with MarkCutRegionClipboardCurrent();
    private enum HoleSelectMode { Replace, Add, Remove }

    private readonly HashSet<CutRegionRef> _selectedCutRegions = [];

    public int SelectedCutRegionCount => SelectedCutRegions().Count;

    internal IReadOnlyList<CutRegionRef> GetSelectedCutRegions() =>
        SelectedCutRegions();

    private IReadOnlyList<CutRegionRef> SelectedCutRegions() =>
        _selectedCutRegions
            .Where(IsValidCutRegion)
            .ToList();

    private bool IsValidCutRegion(CutRegionRef cutRegion) =>
        _measurementSet.Contains(cutRegion.Parent) &&
        IsMeasurementOnActivePage(cutRegion.Parent) &&
        cutRegion.Parent.MType == "area" &&
        cutRegion.HoleIndex >= 0 &&
        cutRegion.HoleIndex < cutRegion.Parent.Holes.Count &&
        cutRegion.Parent.Holes[cutRegion.HoleIndex].Count >= 3;

    private bool HasSelectedCutRegion(Measurement measurement) =>
        _selectedCutRegions.Any(cutRegion =>
            ReferenceEquals(cutRegion.Parent, measurement) &&
            IsValidCutRegion(cutRegion));

    private void PruneCutRegionSelection(Measurement measurement) =>
        _selectedCutRegions.RemoveWhere(cutRegion =>
            ReferenceEquals(cutRegion.Parent, measurement) &&
            !IsValidCutRegion(cutRegion));

    private bool TryHitMeasurementHole(SKPoint pdf, out Measurement measurement, out int holeIndex)
    {
        SKRect pointRect = SKRect.Create(pdf.X, pdf.Y, 0, 0);
        IReadOnlyList<Measurement> candidates = ActivePageMeasurementsNear(pointRect);
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            Measurement candidate = candidates[i];
            if (candidate.MType != "area")
                continue;

            for (int h = 0; h < candidate.Holes.Count; h++)
            {
                List<SKPoint> hole = candidate.Holes[h];
                if (hole.Count >= 3 && PointInPolygon(pdf, hole))
                {
                    measurement = candidate;
                    holeIndex = h;
                    return true;
                }
            }
        }

        measurement = null!;
        holeIndex = -1;
        return false;
    }

    private bool TrySelectCutRegionAt(SKPoint pdf, HoleSelectMode mode)
    {
        if (!TryHitMeasurementHole(pdf, out Measurement measurement, out int holeIndex))
            return false;

        SelectMeasurementHole(measurement, holeIndex, mode);
        return true;
    }

    private void SelectMeasurementHole(Measurement measurement, int holeIndex, HoleSelectMode mode)
    {
        var cutRegion = new CutRegionRef(measurement, holeIndex);
        if (!IsValidCutRegion(cutRegion))
            return;

        if (mode == HoleSelectMode.Replace)
        {
            SetSelectedMeasurements([], null, -1, preserveCutRegions: true);
            _selectedCutRegions.Clear();
        }

        if (mode == HoleSelectMode.Remove)
            _selectedCutRegions.Remove(cutRegion);
        else
            _selectedCutRegions.Add(cutRegion);

        ClearMeasurementVertexSelection();
        ClearAnnotationSelection();
        _annotationSelectionDomain = false;
        RequestRepaint();
        PublishTransformSelectionChanged();

        int holeCount = SelectedCutRegionCount;
        int measurementCount = GetSelectedMeasurements().Count;
        PostStatus(mode == HoleSelectMode.Remove
            ? $"Cutout deselected. {measurementCount} measurement(s) and {holeCount} cutout(s) selected."
            : $"Cutout selected. {measurementCount} measurement(s) and {holeCount} cutout(s) selected; the parent Area is unchanged.");
    }

    private void SetMixedMeasurementSelection(
        IReadOnlyList<Measurement> measurements,
        IReadOnlyList<CutRegionRef> cutRegions,
        Measurement? primary)
    {
        SetSelectedMeasurements(measurements, primary, -1, preserveCutRegions: true);
        _selectedCutRegions.Clear();
        foreach (CutRegionRef cutRegion in cutRegions.Where(IsValidCutRegion))
        {
            if (!measurements.Contains(cutRegion.Parent))
                _selectedCutRegions.Add(cutRegion);
        }

        if (_selectedCutRegions.Count > 0)
        {
            ClearAnnotationSelection();
            _annotationSelectionDomain = false;
        }

        RequestRepaint();
        PublishTransformSelectionChanged();
    }

    private bool TryBeginSelectedCutRegionBundleMove(SKPoint pdf, Point screen)
    {
        IReadOnlyList<CutRegionRef> cutRegions = SelectedTransformCutRegions();
        if (cutRegions.Count == 0)
            return false;

        if (TryHitVertexAmong(SelectedTransformMeasurements(), pdf, out _, out _))
            return false;

        bool hitCutout = cutRegions.Any(cutRegion =>
            PointInPolygon(pdf, cutRegion.Parent.Holes[cutRegion.HoleIndex]));
        bool hitMeasurement = TryHitSelectedMeasurement(pdf, out _);
        if (!hitCutout && !hitMeasurement || !CaptureTransformOriginals())
            return false;

        _draggingTransformMove = true;
        _dragScreenStart = screen;
        _transformStartPdf = pdf;
        CaptureMouse();
        PostStatus("Moving selected measurements and cutouts together.");
        return true;
    }

    private bool TryDeleteSelectedCutRegions()
    {
        IReadOnlyList<CutRegionRef> selected = SelectedCutRegions();
        if (selected.Count == 0)
            return false;

        var parents = selected.Select(cutRegion => cutRegion.Parent).Distinct().ToList();
        var beforePoints = parents.ToDictionary(parent => parent, parent => parent.Points.ToList());
        var beforeHoles = parents.ToDictionary(parent => parent, parent => CloneHoles(parent.Holes));
        foreach (var group in selected.GroupBy(cutRegion => cutRegion.Parent))
        {
            foreach (int holeIndex in group.Select(cutRegion => cutRegion.HoleIndex).Distinct().OrderByDescending(index => index))
                group.Key.Holes.RemoveAt(holeIndex);
        }

        PushMeasurementUndoSnapshots(beforePoints, beforeHoles, "restore deleted cutouts", "delete-cutouts");
        _selectedCutRegions.Clear();
        NotifyMeasurementsChanged(parents);
        RequestRepaint();
        PublishTransformSelectionChanged();
        PostStatus($"Deleted {selected.Count} cutout(s); parent Area remains.");
        return true;
    }

    private void DrawSelectedCutRegionOverlay(SKCanvas canvas, Measurement measurement)
    {
        var selected = SelectedCutRegions()
            .Where(cutRegion => ReferenceEquals(cutRegion.Parent, measurement))
            .ToList();
        if (selected.Count == 0)
            return;

        float safeZoom = Math.Max(_zoom, 0.001f);
        using var stroke = new SKPaint
        {
            Color = new SKColor(0x16, 0xC7, 0xD9),
            StrokeWidth = 2.4f / safeZoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        using var handleFill = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        float radius = 4.5f / safeZoom;
        foreach (CutRegionRef cutRegion in selected)
        {
            IReadOnlyList<SKPoint> points = measurement.Holes[cutRegion.HoleIndex];
            using var path = new SKPath();
            path.MoveTo(points[0]);
            for (int i = 1; i < points.Count; i++)
                path.LineTo(points[i]);
            path.Close();
            canvas.DrawPath(path, stroke);
            foreach (SKPoint point in points)
            {
                canvas.DrawCircle(point, radius, handleFill);
                canvas.DrawCircle(point, radius, stroke);
            }
        }
    }
}
