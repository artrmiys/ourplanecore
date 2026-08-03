using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore;

/// <summary>
/// Moves whole Count measurements or extracts explicitly selected Count markers
/// into another takeoff. Line and Area moves remain owned by
/// <see cref="MeasurementMergeSplitService"/>.
/// </summary>
public static class PointMeasurementSplitService
{
    public static MeasurementMoveResult MoveMeasurementsToTakeoff(
        IReadOnlyList<TakeoffItem> allItems,
        IReadOnlyList<Measurement> measurements,
        IReadOnlyDictionary<Measurement, IReadOnlyList<int>>? selectedPointIndices,
        TakeoffItem target)
    {
        ArgumentNullException.ThrowIfNull(allItems);
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(target);

        List<Measurement> selected = measurements
            .Where(measurement => measurement != null)
            .Distinct()
            .ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("Select one or more Count markers first.");

        bool usesPointSelection = selectedPointIndices is { Count: > 0 };
        if (!usesPointSelection)
        {
            return MeasurementMergeSplitService.MoveMeasurementsToTakeoff(
                allItems,
                selected,
                target);
        }

        List<PointSelection> pointSelections = BuildPointSelections(selected, selectedPointIndices);
        if (pointSelections.Count == 0)
        {
            throw new InvalidOperationException(
                "Selected Count markers are no longer available. Select them again.");
        }

        string targetType = OurPlanCoreJobStore.NormalizeMeasurementType(target.MeasurementType);
        if (targetType != "point")
            throw new InvalidOperationException("Selected Count markers can only be moved into a Count takeoff.");

        Dictionary<Measurement, TakeoffItem> sourceByMeasurement = BuildSourceLookup(allItems);
        var sourceItems = new List<TakeoffItem>();
        foreach (PointSelection selection in pointSelections)
        {
            if (!sourceByMeasurement.TryGetValue(selection.Measurement, out TakeoffItem? source))
                throw new InvalidOperationException("Selected Count measurement was not found in the takeoffs tree.");

            AddDistinct(sourceItems, source);
        }

        var moved = new List<Measurement>();
        var changed = new List<TakeoffItem>();
        foreach (PointSelection selection in pointSelections)
        {
            Measurement measurement = selection.Measurement;
            TakeoffItem source = sourceByMeasurement[measurement];
            if (ReferenceEquals(source, target))
                continue;

            Measurement movedMeasurement;
            if (selection.Indices.Count == measurement.Points.Count)
            {
                source.Measurements.Remove(measurement);
                movedMeasurement = measurement;
            }
            else
            {
                var selectedIndices = selection.Indices.ToHashSet();
                List<SKPoint> selectedPoints = selection.Indices
                    .Select(index => ClonePoint(measurement.Points[index]))
                    .ToList();
                List<SKPoint> remainingPoints = measurement.Points
                    .Where((_, index) => !selectedIndices.Contains(index))
                    .Select(ClonePoint)
                    .ToList();

                measurement.Points.Clear();
                measurement.Points.AddRange(remainingPoints);
                movedMeasurement = ClonePointMeasurement(measurement, selectedPoints);
            }

            AddDistinct(changed, source);
            target.Measurements.Add(movedMeasurement);
            ApplyTargetProperties(movedMeasurement, target);
            moved.Add(movedMeasurement);
            AddDistinct(changed, target);
        }

        if (moved.Count == 0)
            throw new InvalidOperationException("Selected Count markers already belong to the target takeoff.");

        foreach (TakeoffItem item in changed)
            OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);

        List<string> pages = moved
            .Select(measurement => measurement.PageFolder)
            .Where(page => !string.IsNullOrWhiteSpace(page))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MeasurementMoveResult(
            target,
            sourceItems,
            changed,
            moved,
            moved,
            0,
            0,
            pages);
    }

    private static List<PointSelection> BuildPointSelections(
        IReadOnlyList<Measurement> selected,
        IReadOnlyDictionary<Measurement, IReadOnlyList<int>>? selectedPointIndices)
    {
        if (selectedPointIndices == null || selectedPointIndices.Count == 0)
            return [];

        var selections = new List<PointSelection>();
        foreach (Measurement measurement in selected)
        {
            if (OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) != "point" ||
                !selectedPointIndices.TryGetValue(measurement, out IReadOnlyList<int>? indices))
            {
                continue;
            }

            List<int> validIndices = indices
                .Where(index => index >= 0 && index < measurement.Points.Count)
                .Distinct()
                .OrderBy(index => index)
                .ToList();
            if (validIndices.Count > 0)
                selections.Add(new PointSelection(measurement, validIndices));
        }

        return selections;
    }

    private static Measurement ClonePointMeasurement(Measurement source, List<SKPoint> points) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = source.Name,
            Notes = source.Notes,
            MType = source.MType,
            Points = points,
            Holes = source.Holes
                .Select(hole => hole.Select(ClonePoint).ToList())
                .ToList(),
            Color = source.Color,
            CountSymbol = source.CountSymbol,
            PageFolder = source.PageFolder,
            TakeoffFolder = source.TakeoffFolder,
            ScaleMetersPerPt = source.ScaleMetersPerPt,
            JoistEnabled = source.JoistEnabled,
            JoistType = source.JoistType,
            JoistSpacingInches = source.JoistSpacingInches,
            JoistDirectionDegrees = source.JoistDirectionDegrees,
            JoistDirectionLocked = source.JoistDirectionLocked,
            JoistDirectionFollowsAreaRotation = source.JoistDirectionFollowsAreaRotation,
            JoistAddEndJoist = source.JoistAddEndJoist,
            JoistStartEdgeEnabled = source.JoistStartEdgeEnabled,
            JoistEndEdgeEnabled = source.JoistEndEdgeEnabled,
            JoistEdgeOverridesSet = source.JoistEdgeOverridesSet,
            JoistPitch = source.JoistPitch,
            JoistLengthRounding = source.JoistLengthRounding,
            JoistShowLabels = source.JoistShowLabels,
            JoistDetailedLabels = source.JoistDetailedLabels,
        };

    private static void ApplyTargetProperties(Measurement measurement, TakeoffItem target)
    {
        measurement.TakeoffFolder = target.FolderPath;
        measurement.Color = target.Color;
        measurement.CountSymbol = CountDisplaySymbol.Normalize(target.CountSymbol);
    }

    private static Dictionary<Measurement, TakeoffItem> BuildSourceLookup(IReadOnlyList<TakeoffItem> allItems)
    {
        var lookup = new Dictionary<Measurement, TakeoffItem>();
        foreach (TakeoffItem item in allItems)
        foreach (Measurement measurement in item.Measurements)
            lookup.TryAdd(measurement, item);
        return lookup;
    }

    private static void AddDistinct(List<TakeoffItem> items, TakeoffItem item)
    {
        if (!items.Contains(item))
            items.Add(item);
    }

    private static SKPoint ClonePoint(SKPoint point) =>
        new(point.X, point.Y);

    private sealed record PointSelection(Measurement Measurement, IReadOnlyList<int> Indices);
}
