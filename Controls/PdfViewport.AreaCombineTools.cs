using System;
using System.Collections.Generic;
using System.Linq;
using OurPlaneCore;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public enum AreaCombineMode
{
    /// <summary>Merge every selected area into one (originals removed).</summary>
    Union,

    /// <summary>Subtract later-selected areas from the first-selected one; the cutters stay untouched.</summary>
    Subtract,

    /// <summary>Keep only the region shared by all selected areas (originals removed).</summary>
    Intersect,

    /// <summary>Keep every area but trim overlaps so no region is counted twice; the first-selected area wins.</summary>
    RemoveOverlap,

    /// <summary>Split into disjoint pieces: each area keeps its exclusive part, the overlap becomes a separate new area.</summary>
    Divide,
}

public sealed partial class PdfViewport
{
    public void CombineSelectedAreas(AreaCombineMode mode)
    {
        List<Measurement> areas = GetSelectedMeasurements()
            .Where(measurement => measurement.MType == "area")
            .ToList();
        if (areas.Count < 2)
        {
            PostStatus("Combine: select two or more Area measurements first.");
            return;
        }

        Measurement baseArea = areas[0];
        var perTarget = new List<(Measurement Target, List<AreaBooleanGeometry> Geometries)>();
        var extras = new List<(Measurement Source, AreaBooleanGeometry Geometry)>();
        string status;

        switch (mode)
        {
            case AreaCombineMode.Union:
            {
                if (!MeasurementAreaBooleanService.TryCombine(areas, SKPathOp.Union, out List<AreaBooleanGeometry> geometries, out string error))
                {
                    PostStatus(error);
                    return;
                }

                perTarget.Add((baseArea, geometries));
                foreach (Measurement other in areas.Skip(1))
                    perTarget.Add((other, []));
                status = $"Combine union: {areas.Count} area(s) merged into {geometries.Count}.";
                break;
            }

            case AreaCombineMode.Subtract:
            {
                if (!MeasurementAreaBooleanService.TryCombine(areas, SKPathOp.Difference, out List<AreaBooleanGeometry> geometries, out string error))
                {
                    PostStatus(error);
                    return;
                }

                perTarget.Add((baseArea, geometries));
                status = $"Combine subtract: {areas.Count - 1} area(s) subtracted from the first, {geometries.Count} piece(s) left.";
                break;
            }

            case AreaCombineMode.Intersect:
            {
                if (!MeasurementAreaBooleanService.TryCombine(areas, SKPathOp.Intersect, out List<AreaBooleanGeometry> geometries, out string error))
                {
                    PostStatus(string.Equals(error, "Combine: result area is empty.", StringComparison.OrdinalIgnoreCase)
                        ? "Combine: the selected areas do not overlap."
                        : error);
                    return;
                }

                perTarget.Add((baseArea, geometries));
                foreach (Measurement other in areas.Skip(1))
                    perTarget.Add((other, []));
                status = $"Combine intersect: kept {geometries.Count} overlap piece(s) of {areas.Count} area(s).";
                break;
            }

            case AreaCombineMode.RemoveOverlap:
            {
                if (!MeasurementAreaBooleanService.TryRemoveOverlap(areas, out List<List<AreaBooleanGeometry>?> trimmedPerArea, out string error))
                {
                    PostStatus(error);
                    return;
                }

                for (int i = 1; i < areas.Count; i++)
                {
                    if (trimmedPerArea[i] is { } trimmed)
                        perTarget.Add((areas[i], trimmed));
                }

                status = $"Combine remove overlap: trimmed {areas.Count - 1} area(s) against the first-selected.";
                break;
            }

            case AreaCombineMode.Divide:
            {
                if (!MeasurementAreaBooleanService.TryDivide(
                        areas,
                        out List<List<AreaBooleanGeometry>> exclusivePerArea,
                        out List<AreaBooleanGeometry> sharedGeometries,
                        out string error))
                {
                    PostStatus(error);
                    return;
                }

                for (int i = 0; i < areas.Count; i++)
                    perTarget.Add((areas[i], exclusivePerArea[i]));
                foreach (AreaBooleanGeometry shared in sharedGeometries)
                    extras.Add((baseArea, shared));
                status = $"Combine divide: {areas.Count} area(s) split into exclusive parts + {sharedGeometries.Count} overlap piece(s).";
                break;
            }

            default:
                return;
        }

        // Areas on one sheet share one calibration, but each measurement keeps
        // its own scale copy — flag the odd case where the copies disagree.
        bool storedScalesDiffer = areas.Skip(1).Any(area =>
            area.ScaleMetersPerPt > 0 &&
            baseArea.ScaleMetersPerPt > 0 &&
            Math.Abs(area.ScaleMetersPerPt - baseArea.ScaleMetersPerPt) > 0.000000001);
        if (storedScalesDiffer)
            status += " Stored scales differed; result keeps the first area's scale.";

        ApplyAreaCombineResult(perTarget, extras, status);
    }

    private void ApplyAreaCombineResult(
        IReadOnlyList<(Measurement Target, List<AreaBooleanGeometry> Geometries)> perTarget,
        IReadOnlyList<(Measurement Source, AreaBooleanGeometry Geometry)> extras,
        string status)
    {
        var beforePoints = new Dictionary<Measurement, List<SKPoint>>();
        var beforeHoles = new Dictionary<Measurement, List<List<SKPoint>>>();
        var changed = new List<Measurement>();
        var removedIndexes = new Dictionary<Measurement, int>();
        var removed = new List<Measurement>();
        var added = new List<Measurement>();

        foreach ((Measurement target, List<AreaBooleanGeometry> geometries) in perTarget)
        {
            if (geometries.Count == 1)
            {
                beforePoints[target] = target.Points.ToList();
                beforeHoles[target] = CloneHoles(target.Holes);
                ApplyAreaGeometry(target, geometries[0]);
                changed.Add(target);
                continue;
            }

            int index = _measurements.IndexOf(target);
            if (index < 0)
                continue;

            removedIndexes[target] = index;
            removed.Add(target);
            foreach (AreaBooleanGeometry geometry in geometries)
                added.Add(CloneAreaMeasurement(target, geometry));
        }

        foreach ((Measurement source, AreaBooleanGeometry geometry) in extras)
            added.Add(CloneAreaMeasurement(source, geometry));

        if (changed.Count == 0 && removed.Count == 0 && added.Count == 0)
        {
            PostStatus("Combine: nothing to change.");
            return;
        }

        foreach (Measurement measurement in removed)
        {
            _measurements.Remove(measurement);
            _measurementSet.Remove(measurement);
            RemoveMeasurementFromPageIndex(measurement);
            ForgetMeasurementState(measurement);
        }

        foreach (Measurement measurement in added)
        {
            _measurements.Add(measurement);
            _measurementSet.Add(measurement);
            IndexMeasurementByPage(measurement);
        }

        PushMixedMeasurementUndo(
            beforePoints,
            beforeHoles,
            removedIndexes,
            added,
            "combine areas",
            "combine");

        var resultSelection = changed.Concat(added).ToList();
        SetSelectedMeasurements(resultSelection, resultSelection.LastOrDefault(), -1);

        NotifyMeasurementsChanged(changed);
        NotifyMeasurementsRemoved(removed);
        NotifyMeasurementsAdded(added);
        RequestRepaint();
        PostStatus(status);
    }
}
