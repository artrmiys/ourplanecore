using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OurPlanCore.Controls;

namespace OurPlanCore;

public static class SheetLegendBuilder
{
    public static IReadOnlyList<SheetLegendEntry> Build(
        OurPlanCoreJob job,
        PageInfo page,
        IEnumerable<TakeoffItem> takeoffs,
        UnitMode unitMode)
    {
        var pageTakeoffs = takeoffs
            .Where(item => !string.IsNullOrWhiteSpace(item.FolderPath))
            .Where(item => item.Measurements.Any(measurement => IsSamePageFolder(measurement.PageFolder, page.FolderPath)))
            .GroupBy(item => LegendOrderKey(job, item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (pageTakeoffs.Count == 0)
            return [];

        var hidden = page.HiddenTakeoffs
            .Select(key => NormalizeLegendOrderKey(job, key))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hiddenMeasurements = page.HiddenMeasurements
            .Select(NormalizeMeasurementId)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return OrderedTakeoffs(job, page, pageTakeoffs)
            .Where(item => !hidden.Contains(LegendOrderKey(job, item.FolderPath)))
            .Select(item => BuildEntry(page, item, unitMode, hiddenMeasurements))
            .Where(entry => entry != null)
            .Cast<SheetLegendEntry>()
            .ToList();
    }

    private static SheetLegendEntry? BuildEntry(
        PageInfo page,
        TakeoffItem item,
        UnitMode unitMode,
        IReadOnlySet<string> hiddenMeasurements)
    {
        var measurements = item.Measurements
            .Where(measurement => IsSamePageFolder(measurement.PageFolder, page.FolderPath))
            .Where(measurement => !hiddenMeasurements.Contains(NormalizeMeasurementId(measurement.Id)))
            .ToList();
        if (measurements.Count == 0)
            return null;

        string measurementType = OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        return new SheetLegendEntry(
            item.Color,
            item.Name,
            QuantityText(page, item, measurements, unitMode),
            item.IsJoistArea ? "Joist" : MeasurementTypeTitle(measurementType),
            item.IsJoistArea ? "Joist" : MeasurementTypeTitle(measurementType),
            [],
            MeasurementGlyph.Parse(measurementType, joist: item.IsJoistArea, countSymbol: item.CountSymbol));
    }

    private static IReadOnlyList<TakeoffItem> OrderedTakeoffs(
        OurPlanCoreJob job,
        PageInfo page,
        IReadOnlyList<TakeoffItem> takeoffs)
    {
        var byKey = takeoffs
            .GroupBy(item => LegendOrderKey(job, item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<TakeoffItem>();

        IEnumerable<string> storedKeys = IsManualOrder(page)
            ? page.LegendTakeoffOrder.Select(key => NormalizeLegendOrderKey(job, key))
            : [];
        foreach (string storedKey in storedKeys)
        {
            if (string.IsNullOrWhiteSpace(storedKey) || !byKey.TryGetValue(storedKey, out TakeoffItem? takeoff))
                continue;
            if (!used.Add(storedKey))
                continue;

            ordered.Add(takeoff);
        }

        ordered.AddRange(TakeoffAutoRoutingService.SortPageLegendItems(takeoffs
            .Where(item => !used.Contains(LegendOrderKey(job, item.FolderPath)))));

        return ordered;
    }

    private static bool IsManualOrder(PageInfo page) =>
        string.Equals(page.LegendTakeoffOrderMode, "manual", StringComparison.OrdinalIgnoreCase);

    private static string QuantityText(
        PageInfo page,
        TakeoffItem item,
        IReadOnlyList<Measurement> measurements,
        UnitMode unitMode)
    {
        string measurementType = OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double fallbackScale = page.ScaleMetersPerPt;

        if (measurementType == "point")
            return Units.FormatCount(measurements.Sum(measurement => measurement.Points.Count));

        bool hasScale = fallbackScale > 0 || measurements.Any(measurement => measurement.ScaleMetersPerPt > 0);
        if (item.IsJoistArea)
        {
            return hasScale
                ? Units.FormatArea(measurements.Sum(measurement => measurement.AreaValue(fallbackScale)), unitMode)
                : $"{measurements.Sum(measurement => measurement.Points.Count)} pts";
        }

        if (!hasScale)
        {
            if (measurementType == "line")
                return $"{measurements.Sum(measurement => Math.Max(0, measurement.Points.Count - 1))} seg";
            if (measurementType == "area")
                return $"{measurements.Sum(measurement => measurement.Points.Count)} pts";
        }

        double total = measurements.Sum(measurement => measurement.Value(fallbackScale));
        return measurementType switch
        {
            "line" => Units.FormatLength(total, unitMode),
            "area" => Units.FormatArea(total, unitMode),
            _ => Units.FormatCount(total),
        };
    }

    private static string MeasurementTypeTitle(string measurementType) =>
        OurPlanCoreJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "point" => "Count",
            "area" => "Area",
            _ => "Line",
        };

    private static string LegendOrderKey(OurPlanCoreJob job, string folderPath) =>
        NormalizeLegendOrderKey(job, folderPath);

    private static string NormalizeLegendOrderKey(OurPlanCoreJob job, string value)
    {
        string clean = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return "";

        if (Path.IsPathFullyQualified(clean))
        {
            string full = NormalizePath(clean);
            if (OurPlanCoreJobStore.IsSameOrDescendant(job.TakeoffsRoot, full))
                clean = Path.GetRelativePath(job.TakeoffsRoot, full);
        }

        return clean.Replace('\\', '/').Trim('/');
    }

    private static string NormalizeMeasurementId(string? value) =>
        (value ?? "").Trim();

    private static bool IsSamePageFolder(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
