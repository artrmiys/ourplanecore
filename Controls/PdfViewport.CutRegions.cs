using System;
using System.Collections.Generic;
using System.Linq;
using OurPlanCore;
using SkiaSharp;

namespace OurPlanCore.Controls;

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

        PostStatus($"Copied {_holeClipboard.Count} cut region(s). Ctrl+V pastes using the copied set's top-left corner as the cursor anchor.");
        return true;
    }

    private bool PasteCutRegions(SKPoint? atPdf)
    {
        if (_holeClipboard.Count == 0)
            return false;

        // Anchor probe: the cursor when we have one, otherwise the selected
        // Area's centre so a keyboard-only Ctrl+V still resolves a target.
        SKPoint probe = atPdf ?? (_selectedMeasurement is { MType: "area" } a && a.Points.Count >= 3
            ? Centroid(a.Points)
            : default);

        if (!TryResolvePasteCutTarget(probe, out Measurement target, out string status))
        {
            PostStatus(status);
            return true;
        }

        // Land the cut at the cursor; without one, drop it onto the target Area's
        // centre so the paste is always visible rather than flung to the origin.
        SKPoint at = atPdf ?? Centroid(target.Points);
        // Anchor the paste by the copied cut's top-left corner, matching how
        // measurement paste maps the cursor (CalculateMeasurementPasteOffset).
        var allPts = _holeClipboard.SelectMany(h => h).ToList();
        SKPoint src = new(allPts.Min(p => p.X), allPts.Min(p => p.Y));
        SKPoint offset = new(at.X - src.X, at.Y - src.Y);

        List<SKPoint> beforePoints = target.Points.ToList();
        List<List<SKPoint>> beforeHoles = CloneHoles(target.Holes);
        int added = 0;
        // Paste is intentionally permissive: a copied cut region is dropped in as-is
        // even if it pokes past the Area edge or overlaps another hole. The area math
        // (Measurement.PolygonAreaPt) already subtracts holes and clamps to >= 0, so an
        // out-of-bounds cut is harmless. Only the live Area-Cut draw tool keeps the
        // stricter containment guard.
        foreach (var hole in _holeClipboard)
        {
            var moved = hole.Select(p => new SKPoint(p.X + offset.X, p.Y + offset.Y)).ToList();
            if (moved.Count < 3)
                continue;

            target.Holes.Add(moved);
            added++;
        }

        if (added == 0)
        {
            PostStatus("Paste cut region: the copied cut region is empty.");
            return true;
        }

        PushMeasurementUndoSnapshot(target, beforePoints, beforeHoles, "paste cut region", "cut-region-paste");
        SelectMeasurement(target, -1);
        NotifyMeasurementsChanged([target]);
        RequestRepaint();
        PostStatus($"Pasted {added} cut region(s). New area {target.Label(ScaleMetersPerPt, UnitMode)}.");
        return true;
    }

    // Lenient target lookup for *pasting* cut regions. Unlike the live Area-Cut
    // draw tool, paste must not require the anchor to sit inside an Area's fill —
    // cuts are routinely placed on edges or over existing holes.
    private bool TryResolvePasteCutTarget(SKPoint probe, out Measurement target, out string status)
    {
        target = null!;
        status = "";

        // 1. Cursor genuinely inside an Area fill — the ideal anchor.
        if (TryResolveAreaCutTarget(probe, out Measurement direct, out _))
        {
            target = direct;
            return true;
        }

        // 2. A selected Area is the natural destination even when the cursor
        //    (and the pasted cut) lands outside its fill or on a hole.
        if (_selectedMeasurement is { MType: "area" } selected &&
            selected.Points.Count >= 3 &&
            IsMeasurementOnActivePage(selected))
        {
            target = selected;
            return true;
        }

        // 3. Otherwise pick an Area on the active sheet: the one whose outer
        //    outline contains the probe (holes ignored), else the nearest.
        Measurement? nearest = null;
        double nearestDist = double.MaxValue;
        foreach (Measurement m in ActivePageMeasurements())
        {
            if (m.MType != "area" || m.Points.Count < 3)
                continue;

            if (PointInPolygon(probe, m.Points))
            {
                target = m;
                return true;
            }

            SKPoint c = Centroid(m.Points);
            double d = (c.X - probe.X) * (c.X - probe.X) + (c.Y - probe.Y) * (c.Y - probe.Y);
            if (d < nearestDist)
            {
                nearestDist = d;
                nearest = m;
            }
        }

        if (nearest != null)
        {
            target = nearest;
            return true;
        }

        status = "Paste cut region: this sheet has no Area to paste into. Draw an Area first.";
        return false;
    }
}
