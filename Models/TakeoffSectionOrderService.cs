using System;
using System.Collections.Generic;
using System.Linq;

namespace OurPlanCore;

public static class TakeoffSectionOrderService
{
    public static bool CanMove(
        IReadOnlyList<Measurement> measurements,
        IEnumerable<string?> selectedMeasurementIds,
        int offset)
    {
        if (measurements.Count <= 1 || offset == 0)
            return false;

        HashSet<string> selectedIds = ValidSelectedIdSet(measurements, selectedMeasurementIds);
        if (selectedIds.Count == 0)
            return false;

        if (offset < 0)
        {
            for (int i = 1; i < measurements.Count; i++)
            {
                if (IsSelected(measurements[i], selectedIds) &&
                    !IsSelected(measurements[i - 1], selectedIds))
                    return true;
            }

            return false;
        }

        for (int i = 0; i < measurements.Count - 1; i++)
        {
            if (IsSelected(measurements[i], selectedIds) &&
                !IsSelected(measurements[i + 1], selectedIds))
                return true;
        }

        return false;
    }

    public static bool Move(
        IList<Measurement> measurements,
        IEnumerable<string?> selectedMeasurementIds,
        int offset)
    {
        var measurementList = ToReadOnlyList(measurements);
        var selectedIdsInput = selectedMeasurementIds.ToList();
        if (!CanMove(measurementList, selectedIdsInput, offset))
            return false;

        HashSet<string> selectedIds = ValidSelectedIdSet(measurementList, selectedIdsInput);
        if (offset < 0)
        {
            for (int i = 1; i < measurements.Count; i++)
            {
                if (IsSelected(measurements[i], selectedIds) &&
                    !IsSelected(measurements[i - 1], selectedIds))
                {
                    (measurements[i - 1], measurements[i]) = (measurements[i], measurements[i - 1]);
                }
            }
        }
        else
        {
            for (int i = measurements.Count - 2; i >= 0; i--)
            {
                if (IsSelected(measurements[i], selectedIds) &&
                    !IsSelected(measurements[i + 1], selectedIds))
                {
                    (measurements[i], measurements[i + 1]) = (measurements[i + 1], measurements[i]);
                }
            }
        }

        return true;
    }

    private static IReadOnlyList<Measurement> ToReadOnlyList(IList<Measurement> measurements) =>
        measurements as IReadOnlyList<Measurement> ?? measurements.ToList();

    private static HashSet<string> ValidSelectedIdSet(
        IReadOnlyList<Measurement> measurements,
        IEnumerable<string?> selectedMeasurementIds)
    {
        var existingIds = measurements
            .Select(measurement => measurement.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selectedIds = selectedMeasurementIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        selectedIds.IntersectWith(existingIds);
        return selectedIds;
    }

    private static bool IsSelected(Measurement measurement, HashSet<string> selectedIds) =>
        !string.IsNullOrWhiteSpace(measurement.Id) && selectedIds.Contains(measurement.Id);
}
