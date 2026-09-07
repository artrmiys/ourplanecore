using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore;

public sealed record MeasurementMoveResult(
    TakeoffItem TargetItem,
    IReadOnlyList<TakeoffItem> SourceItems,
    IReadOnlyList<TakeoffItem> ChangedItems,
    IReadOnlyList<Measurement> MovedMeasurements,
    IReadOnlyList<Measurement> SelectedMeasurements,
    int CoalescedLineCount,
    int CoalescedAreaCount,
    IReadOnlyList<string> PageFolders);

public static class MeasurementMergeSplitService
{
    private const double LineMergeTolerancePt = 0.75;
    private const double LineMergeScaleTolerance = 0.000000001;

    public static MeasurementMoveResult MoveMeasurementsToTakeoff(
        IReadOnlyList<TakeoffItem> allItems,
        IReadOnlyList<Measurement> measurements,
        TakeoffItem target)
    {
        ArgumentNullException.ThrowIfNull(allItems);
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(target);

        var selected = measurements
            .Where(measurement => measurement != null)
            .Distinct()
            .ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("Select one or more measurement segments first.");

        string targetType = OurPlanCoreJobStore.NormalizeMeasurementType(target.MeasurementType);
        if (string.IsNullOrWhiteSpace(targetType))
            throw new InvalidOperationException("Target takeoff type is not valid.");

        var sourceByMeasurement = BuildSourceLookup(allItems);
        var sourceItems = new List<TakeoffItem>();
        foreach (Measurement measurement in selected)
        {
            string measurementType = OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType);
            if (measurementType != targetType)
            {
                throw new InvalidOperationException(
                    $"Cannot move {MeasurementTypeTitle(measurementType)} into {MeasurementTypeTitle(targetType)} takeoff.");
            }

            if (!sourceByMeasurement.TryGetValue(measurement, out TakeoffItem? source))
                throw new InvalidOperationException("Selected measurement was not found in the takeoffs tree.");

            if (!sourceItems.Contains(source))
                sourceItems.Add(source);
        }

        var moved = new List<Measurement>();
        var changed = new List<TakeoffItem>();
        foreach (Measurement measurement in selected)
        {
            TakeoffItem source = sourceByMeasurement[measurement];
            if (ReferenceEquals(source, target))
                continue;

            source.Measurements.Remove(measurement);
            AddDistinct(changed, source);

            if (!target.Measurements.Contains(measurement))
                target.Measurements.Add(measurement);
            measurement.TakeoffFolder = target.FolderPath;
            measurement.Color = target.Color;
            if (targetType == "point")
                measurement.CountSymbol = CountDisplaySymbol.Normalize(target.CountSymbol);

            moved.Add(measurement);
            AddDistinct(changed, target);
        }

        if (moved.Count == 0)
            throw new InvalidOperationException("Selected measurement segments already belong to the target takeoff.");

        foreach (TakeoffItem item in changed)
            OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);

        MeasurementCoalesceResult coalesce = targetType switch
        {
            "line" => CoalesceMovedLines(target, moved),
            "area" => CoalesceMovedAreas(target, moved),
            _ => new MeasurementCoalesceResult(moved, 0, 0),
        };

        var pages = moved
            .Select(measurement => measurement.PageFolder)
            .Where(page => !string.IsNullOrWhiteSpace(page))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MeasurementMoveResult(
            target,
            sourceItems,
            changed,
            moved,
            coalesce.SelectedMeasurements,
            coalesce.CoalescedLineCount,
            coalesce.CoalescedAreaCount,
            pages);
    }

    private static MeasurementCoalesceResult CoalesceMovedLines(TakeoffItem target, IReadOnlyList<Measurement> moved)
    {
        var selected = moved
            .Where(target.Measurements.Contains)
            .Distinct()
            .ToList();
        if (selected.Count == 0)
            return new MeasurementCoalesceResult([], 0, 0);

        int coalesced = 0;
        bool changed;
        do
        {
            changed = false;
            foreach (Measurement selectedLine in selected.ToList())
            {
                if (!target.Measurements.Contains(selectedLine))
                    continue;

                foreach (Measurement candidate in target.Measurements.ToList())
                {
                    if (ReferenceEquals(selectedLine, candidate) ||
                        !TryMergeLineMeasurements(selectedLine, candidate, out List<SKPoint> mergedPoints))
                    {
                        continue;
                    }

                    Measurement survivor = ChooseMergeSurvivor(target, selectedLine, candidate);
                    Measurement removed = ReferenceEquals(survivor, selectedLine) ? candidate : selectedLine;
                    survivor.Points.Clear();
                    survivor.Points.AddRange(OrientMergedLinePoints(survivor, mergedPoints));
                    target.Measurements.Remove(removed);
                    ReplaceSelectedMeasurement(selected, removed, survivor);
                    coalesced++;
                    changed = true;
                    break;
                }

                if (changed)
                    break;
            }
        }
        while (changed);

        selected = selected
            .Where(target.Measurements.Contains)
            .Distinct()
            .ToList();

        return new MeasurementCoalesceResult(selected, coalesced, 0);
    }

    private static MeasurementCoalesceResult CoalesceMovedAreas(TakeoffItem target, IReadOnlyList<Measurement> moved)
    {
        var selected = moved
            .Where(target.Measurements.Contains)
            .Distinct()
            .ToList();
        if (selected.Count == 0)
            return new MeasurementCoalesceResult([], 0, 0);

        int coalesced = 0;
        bool changed;
        do
        {
            changed = false;
            foreach (Measurement selectedArea in selected.ToList())
            {
                if (!target.Measurements.Contains(selectedArea))
                    continue;

                foreach (Measurement candidate in target.Measurements.ToList())
                {
                    if (ReferenceEquals(selectedArea, candidate) ||
                        !MeasurementAreaBooleanService.TryUnion(selectedArea, candidate, out AreaBooleanGeometry geometry, out _))
                    {
                        continue;
                    }

                    Measurement survivor = ChooseMergeSurvivor(target, selectedArea, candidate);
                    Measurement removed = ReferenceEquals(survivor, selectedArea) ? candidate : selectedArea;
                    ApplyAreaGeometry(survivor, geometry);
                    MergeExtraJoists(survivor, removed);
                    target.Measurements.Remove(removed);
                    ReplaceSelectedMeasurement(selected, removed, survivor);
                    coalesced++;
                    changed = true;
                    break;
                }

                if (changed)
                    break;
            }
        }
        while (changed);

        selected = selected
            .Where(target.Measurements.Contains)
            .Distinct()
            .ToList();

        return new MeasurementCoalesceResult(selected, 0, coalesced);
    }

    private static bool TryMergeLineMeasurements(
        Measurement first,
        Measurement second,
        out List<SKPoint> mergedPoints)
    {
        mergedPoints = [];
        if (OurPlanCoreJobStore.NormalizeMeasurementType(first.MType) != "line" ||
            OurPlanCoreJobStore.NormalizeMeasurementType(second.MType) != "line" ||
            !SamePage(first.PageFolder, second.PageFolder) ||
            !CompatibleScales(first.ScaleMetersPerPt, second.ScaleMetersPerPt) ||
            !TryBuildLineSpan(first, out LineSpan firstSpan) ||
            !TryBuildLineSpan(second, out LineSpan secondSpan) ||
            !SameLine(firstSpan, secondSpan) ||
            !SpansTouchOrOverlap(firstSpan, secondSpan))
        {
            return false;
        }

        double secondMinOnFirst = Project(firstSpan.Origin, firstSpan.Dx, firstSpan.Dy, PointAt(secondSpan, secondSpan.Min));
        double secondMaxOnFirst = Project(firstSpan.Origin, firstSpan.Dx, firstSpan.Dy, PointAt(secondSpan, secondSpan.Max));
        double min = Math.Min(firstSpan.Min, Math.Min(secondMinOnFirst, secondMaxOnFirst));
        double max = Math.Max(firstSpan.Max, Math.Max(secondMinOnFirst, secondMaxOnFirst));
        if (max - min <= LineMergeTolerancePt)
            return false;

        mergedPoints =
        [
            PointAt(firstSpan, min),
            PointAt(firstSpan, max),
        ];
        return true;
    }

    private static bool TryBuildLineSpan(Measurement measurement, out LineSpan span)
    {
        span = default;
        if (measurement.Points.Count < 2)
            return false;

        SKPoint origin = measurement.Points[0];
        SKPoint? axisEnd = null;
        double axisLength = 0;
        foreach (SKPoint point in measurement.Points.Skip(1))
        {
            double distance = Distance(origin, point);
            if (distance > axisLength)
            {
                axisEnd = point;
                axisLength = distance;
            }
        }

        if (axisEnd == null || axisLength <= LineMergeTolerancePt)
            return false;

        double dx = (axisEnd.Value.X - origin.X) / axisLength;
        double dy = (axisEnd.Value.Y - origin.Y) / axisLength;
        double min = 0;
        double max = 0;
        foreach (SKPoint point in measurement.Points)
        {
            double projection = Project(origin, dx, dy, point);
            double perpendicular = Math.Abs(CrossDistance(origin, dx, dy, point));
            if (perpendicular > LineMergeTolerancePt)
                return false;

            min = Math.Min(min, projection);
            max = Math.Max(max, projection);
        }

        if (max - min <= LineMergeTolerancePt)
            return false;

        span = new LineSpan(origin, dx, dy, min, max);
        return true;
    }

    private static bool SameLine(LineSpan first, LineSpan second)
    {
        double axisCross = Math.Abs(first.Dx * second.Dy - first.Dy * second.Dx);
        if (axisCross > 0.000001)
            return false;

        return DistanceToLine(first, PointAt(second, second.Min)) <= LineMergeTolerancePt &&
               DistanceToLine(first, PointAt(second, second.Max)) <= LineMergeTolerancePt;
    }

    private static bool SpansTouchOrOverlap(LineSpan first, LineSpan second)
    {
        double secondMinOnFirst = Project(first.Origin, first.Dx, first.Dy, PointAt(second, second.Min));
        double secondMaxOnFirst = Project(first.Origin, first.Dx, first.Dy, PointAt(second, second.Max));
        double min = Math.Min(secondMinOnFirst, secondMaxOnFirst);
        double max = Math.Max(secondMinOnFirst, secondMaxOnFirst);
        return first.Min <= max + LineMergeTolerancePt &&
               min <= first.Max + LineMergeTolerancePt;
    }

    private static Measurement ChooseMergeSurvivor(TakeoffItem target, Measurement first, Measurement second)
    {
        int firstIndex = target.Measurements.IndexOf(first);
        int secondIndex = target.Measurements.IndexOf(second);
        return firstIndex <= secondIndex ? first : second;
    }

    private static List<SKPoint> OrientMergedLinePoints(Measurement survivor, IReadOnlyList<SKPoint> mergedPoints)
    {
        if (mergedPoints.Count != 2 || survivor.Points.Count < 2)
            return mergedPoints.ToList();

        SKPoint first = survivor.Points[0];
        SKPoint last = survivor.Points[^1];
        double existingDx = last.X - first.X;
        double existingDy = last.Y - first.Y;
        double mergedDx = mergedPoints[1].X - mergedPoints[0].X;
        double mergedDy = mergedPoints[1].Y - mergedPoints[0].Y;
        double dot = existingDx * mergedDx + existingDy * mergedDy;
        return dot >= 0
            ? mergedPoints.ToList()
            : [mergedPoints[1], mergedPoints[0]];
    }

    private static void ReplaceSelectedMeasurement(List<Measurement> selected, Measurement removed, Measurement survivor)
    {
        int index = selected.IndexOf(removed);
        if (index >= 0)
            selected[index] = survivor;
        else if (!selected.Contains(survivor))
            selected.Add(survivor);
    }

    private static void ApplyAreaGeometry(Measurement area, AreaBooleanGeometry geometry)
    {
        area.Points.Clear();
        area.Points.AddRange(geometry.Points.Select(ClonePoint));
        area.Holes.Clear();
        area.Holes.AddRange(geometry.Holes.Select(hole => hole.Select(ClonePoint).ToList()));
    }

    private static void MergeExtraJoists(Measurement survivor, Measurement removed)
    {
        var existingIds = new HashSet<string>(
            survivor.ExtraJoists
                .Where(extra => !string.IsNullOrWhiteSpace(extra.Id))
                .Select(extra => extra.Id),
            StringComparer.OrdinalIgnoreCase);

        foreach (JoistExtraSegment extra in removed.ExtraJoists)
        {
            string id = string.IsNullOrWhiteSpace(extra.Id)
                ? Guid.NewGuid().ToString()
                : extra.Id;
            if (!existingIds.Add(id))
                continue;

            survivor.ExtraJoists.Add(new JoistExtraSegment
            {
                Id = id,
                Start = ClonePoint(extra.Start),
                End = ClonePoint(extra.End),
            });
        }
    }

    private static SKPoint ClonePoint(SKPoint point) =>
        new(point.X, point.Y);

    private static bool SamePage(string first, string second) =>
        string.Equals(first ?? "", second ?? "", StringComparison.OrdinalIgnoreCase);

    private static bool CompatibleScales(double first, double second) =>
        first <= 0 ||
        second <= 0 ||
        Math.Abs(first - second) <= LineMergeScaleTolerance;

    private static double Distance(SKPoint first, SKPoint second)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Project(SKPoint origin, double dx, double dy, SKPoint point) =>
        (point.X - origin.X) * dx + (point.Y - origin.Y) * dy;

    private static double CrossDistance(SKPoint origin, double dx, double dy, SKPoint point) =>
        (point.X - origin.X) * dy - (point.Y - origin.Y) * dx;

    private static double DistanceToLine(LineSpan span, SKPoint point) =>
        Math.Abs(CrossDistance(span.Origin, span.Dx, span.Dy, point));

    private static SKPoint PointAt(LineSpan span, double projection) =>
        new(
            (float)(span.Origin.X + span.Dx * projection),
            (float)(span.Origin.Y + span.Dy * projection));

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

    private static string MeasurementTypeTitle(string measurementType) =>
        measurementType switch
        {
            "line" => "Line",
            "area" => "Area",
            "point" => "Count",
            _ => "measurement",
        };

    private readonly record struct LineSpan(SKPoint Origin, double Dx, double Dy, double Min, double Max);

    private sealed record MeasurementCoalesceResult(
        IReadOnlyList<Measurement> SelectedMeasurements,
        int CoalescedLineCount,
        int CoalescedAreaCount);
}
