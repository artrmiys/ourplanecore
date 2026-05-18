using System;
using System.Collections.Generic;
using System.Linq;
using OurPlaneCore;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private enum HoleSelectMode { Replace, Add, Remove }

    // Copied cut regions (area holes), stored in PDF coordinates.
    private readonly List<List<SKPoint>> _holeClipboard = [];

    private static IEnumerable<int> HoleGlobalIndices(Measurement measurement, int holeIndex) =>
        MeasurementVertices(measurement)
            .Where(v => v.HoleIndex == holeIndex)
            .Select(v => v.GlobalIndex);

    private bool TryHitMeasurementHole(SKPoint pdf, out Measurement measurement, out int holeIndex)
    {
        SKRect pointRect = SKRect.Create(pdf.X, pdf.Y, 0, 0);
        IReadOnlyList<Measurement> candidates = ActivePageMeasurementsNear(pointRect);
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            Measurement m = candidates[i];
            if (m.MType != "area")
                continue;

            for (int h = 0; h < m.Holes.Count; h++)
            {
                List<SKPoint> hole = m.Holes[h];
                if (hole.Count >= 3 && PointInPolygon(pdf, hole))
                {
                    measurement = m;
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
        var indices = HoleGlobalIndices(measurement, holeIndex).ToList();
        if (indices.Count == 0)
            return;

        if (mode == HoleSelectMode.Replace)
        {
            ClearMeasurementVertexSelection();
            SetSelectedMeasurements([measurement], measurement, -1);
        }
        else if (!_selectedMeasurements.Contains(measurement))
        {
            SetSelectedMeasurements([measurement], measurement, -1);
        }

        if (mode == HoleSelectMode.Remove)
        {
            if (_selectedMeasurementVertexIndices.TryGetValue(measurement, out HashSet<int>? existing))
            {
                foreach (int gi in indices)
                    existing.Remove(gi);
                if (existing.Count == 0)
                    _selectedMeasurementVertexIndices.Remove(measurement);
            }
        }
        else
        {
            HashSet<int> set = VertexSelectionSet(measurement, create: true);
            foreach (int gi in indices)
                set.Add(gi);
        }

        _selectedMeasurement = measurement;
        _selectedVertexIndex = IsMeasurementVertexSelected(measurement, indices[0])
            ? indices[0]
            : LastSelectedVertexIndex();
        RequestRepaint();

        int holeCount = SelectedWholeHoles().Count;
        PostStatus(mode == HoleSelectMode.Remove
            ? $"Cut region deselected. {holeCount} cut region(s) selected."
            : $"Cut region selected ({holeCount} total). Ctrl+C copies; Ctrl-click adds more; drag handles or Delete to edit.");
    }

    // Returns (measurement, holeIndex) for every cut region that is *fully*
    // selected. Returns an empty list when the vertex selection mixes outer
    // boundary points or only partially covers a hole.
    private List<(Measurement Measurement, int HoleIndex)> SelectedWholeHoles()
    {
        var result = new List<(Measurement, int)>();
        foreach (var (measurement, set) in _selectedMeasurementVertexIndices)
        {
            if (set.Count == 0)
                continue;

            var byHole = new Dictionary<int, int>();
            foreach (int gi in set)
            {
                if (!TryResolveMeasurementVertex(measurement, gi, out MeasurementVertexRef v) || !v.IsHole)
                    return [];

                byHole[v.HoleIndex] = byHole.GetValueOrDefault(v.HoleIndex) + 1;
            }

            foreach (var (holeIndex, count) in byHole)
            {
                if (holeIndex >= 0 &&
                    holeIndex < measurement.Holes.Count &&
                    measurement.Holes[holeIndex].Count >= 3 &&
                    count == measurement.Holes[holeIndex].Count)
                {
                    result.Add((measurement, holeIndex));
                }
                else
                {
                    return [];
                }
            }
        }

        return result;
    }

    private bool CopySelectedCutRegions()
    {
        var holes = SelectedWholeHoles();
        if (holes.Count == 0)
            return false;

        _holeClipboard.Clear();
        foreach (var (measurement, holeIndex) in holes)
        {
            _holeClipboard.Add(measurement.Holes[holeIndex]
                .Select(p => new SKPoint(p.X, p.Y))
                .ToList());
        }

        PostStatus($"Copied {_holeClipboard.Count} cut region(s). Ctrl+V pastes them into the area under the cursor.");
        return true;
    }

    private bool PasteCutRegions(SKPoint? atPdf)
    {
        if (_holeClipboard.Count == 0)
            return false;

        if (!atPdf.HasValue)
        {
            PostStatus("Paste cut region: move the cursor over an Area, then press Ctrl+V.");
            return true;
        }

        SKPoint at = atPdf.Value;
        if (!TryResolveAreaCutTarget(at, out Measurement target, out string status))
        {
            PostStatus(string.IsNullOrEmpty(status)
                ? "Paste cut region: hover inside an Area and try again."
                : status);
            return true;
        }

        var allPts = _holeClipboard.SelectMany(h => h).ToList();
        SKPoint src = Centroid(allPts);
        SKPoint offset = new(at.X - src.X, at.Y - src.Y);

        List<SKPoint> beforePoints = target.Points.ToList();
        List<List<SKPoint>> beforeHoles = CloneHoles(target.Holes);
        int added = 0;
        string? firstError = null;
        foreach (var hole in _holeClipboard)
        {
            var moved = hole.Select(p => new SKPoint(p.X + offset.X, p.Y + offset.Y)).ToList();
            if (CanApplyAreaCut(target, moved, out string err))
            {
                target.Holes.Add(moved);
                added++;
            }
            else
            {
                firstError ??= err;
            }
        }

        if (added == 0)
        {
            PostStatus(firstError ?? "Paste cut region: it did not fit inside the Area.");
            return true;
        }

        PushMeasurementUndoSnapshot(target, beforePoints, beforeHoles, "paste cut region", "cut-region-paste");
        SelectMeasurement(target, -1);
        NotifyMeasurementsChanged([target]);
        RequestRepaint();
        PostStatus(firstError == null
            ? $"Pasted {added} cut region(s). New area {target.Label(ScaleMetersPerPt, UnitMode)}."
            : $"Pasted {added} cut region(s); some did not fit inside the Area.");
        return true;
    }
}
