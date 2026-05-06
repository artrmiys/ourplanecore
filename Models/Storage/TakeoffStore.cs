using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SkiaSharp;

namespace OurPlaneCore;

internal static class TakeoffStore
{
    public static string CreateTakeoffFolder(OurPlaneCoreJob job, string parentFolder, string name)
    {
        string targetParent = OurPlaneCoreJobStore.IsSameOrDescendant(job.TakeoffsRoot, parentFolder)
            ? parentFolder
            : job.TakeoffsRoot;
        string folder = OurPlaneCoreJobStore.CreateFolder(targetParent, name);
        OurPlaneCoreJobStore.SetProperty(folder, "SmartNodeKind", "folder");
        return folder;
    }

    public static TakeoffItem CreateTakeoffItem(OurPlaneCoreJob job, string name, string color) =>
        CreateTakeoffItem(job, job.TakeoffsRoot, name, color, "line");

    public static TakeoffItem CreateTakeoffItem(
        OurPlaneCoreJob job,
        string parentFolder,
        string name,
        string color,
        string measurementType)
    {
        string targetParent = OurPlaneCoreJobStore.IsSameOrDescendant(job.TakeoffsRoot, parentFolder)
            ? parentFolder
            : job.TakeoffsRoot;
        string folder = OurPlaneCoreJobStore.CreateFolder(targetParent, name);
        OurPlaneCoreJobStore.SetProperty(folder, "SmartNodeKind", "item");
        OurPlaneCoreJobStore.SetProperty(folder, "Color", color);
        OurPlaneCoreJobStore.SetProperty(folder, "MeasurementType", OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType));
        return new TakeoffItem
        {
            Name = OurPlaneCoreJobStore.DisplayName(folder),
            Color = color,
            FolderPath = folder,
            MeasurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType),
        };
    }

    public static IReadOnlyList<TakeoffItem> LoadTakeoffItems(OurPlaneCoreJob job)
    {
        var items = new List<TakeoffItem>();
        if (!Directory.Exists(job.TakeoffsRoot)) return items;

        foreach (string folder in OurPlaneCoreJobStore.EnumerateSelfAndDescendants(job.TakeoffsRoot).Skip(1))
            if (TryReadTakeoffItem(folder) is { } item)
                items.Add(item);

        return items;
    }

    public static TakeoffItem? TryReadTakeoffItem(string folder)
    {
        if (!IsTakeoffItemFolder(folder)) return null;

        var measurements = LoadMeasurements(folder);
        string measurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(
            OurPlaneCoreJobStore.ReadProperty(folder, "MeasurementType") ??
            measurements.FirstOrDefault()?.MType ??
            "line");

        var item = new TakeoffItem
        {
            Name = OurPlaneCoreJobStore.DisplayName(folder),
            Color = OurPlaneCoreJobStore.ReadProperty(folder, "Color") ?? "#FF4444",
            FolderPath = folder,
            MeasurementType = measurementType,
            UnitPrice = ParseDouble(OurPlaneCoreJobStore.ReadProperty(folder, "UnitPrice")),
            Notes = OurPlaneCoreJobStore.ReadProperty(folder, "Notes") ?? "",
            IsJoistTakeoff = ParseBool(OurPlaneCoreJobStore.ReadProperty(folder, "JoistEnabled")),
            JoistType = OurPlaneCoreJobStore.ReadProperty(folder, "JoistType") ?? "",
            JoistSpacingInches = ParsePositiveDouble(OurPlaneCoreJobStore.ReadProperty(folder, "JoistSpacingInches"), 16),
            JoistDirectionDegrees = ParseDouble(OurPlaneCoreJobStore.ReadProperty(folder, "JoistDirectionDegrees")),
            JoistPitch = JoistTakeoffCalculator.NormalizePitch(OurPlaneCoreJobStore.ReadProperty(folder, "JoistPitch")),
            JoistLengthRounding = JoistTakeoffCalculator.NormalizeLengthRounding(OurPlaneCoreJobStore.ReadProperty(folder, "JoistLengthRounding")),
            JoistShowLabels = ParseBool(OurPlaneCoreJobStore.ReadProperty(folder, "JoistShowLabels")),
            JoistDetailedLabels = ParseBool(OurPlaneCoreJobStore.ReadProperty(folder, "JoistDetailedLabels"), fallback: true),
        };
        item.Measurements.AddRange(measurements);
        ApplyTakeoffPropertiesToMeasurements(item);
        return item;
    }

    public static void SaveTakeoffItem(TakeoffItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FolderPath) || !Directory.Exists(item.FolderPath))
            return;

        OurPlaneCoreJobStore.UpdateItemName(item.FolderPath, item.Name);
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "SmartNodeKind", "item");
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "Color", item.Color);
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "MeasurementType", OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType));
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "UnitPrice", item.UnitPrice.ToString("G17", CultureInfo.InvariantCulture));
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "Notes", item.Notes);
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "JoistEnabled", item.IsJoistArea.ToString(CultureInfo.InvariantCulture));
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "JoistType", item.JoistType ?? "");
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "JoistSpacingInches", Math.Max(0.001, item.JoistSpacingInches).ToString("G17", CultureInfo.InvariantCulture));
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "JoistDirectionDegrees", item.JoistDirectionDegrees.ToString("G17", CultureInfo.InvariantCulture));
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "JoistPitch", JoistTakeoffCalculator.NormalizePitch(item.JoistPitch));
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "JoistLengthRounding", JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding));
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "JoistShowLabels", item.JoistShowLabels.ToString(CultureInfo.InvariantCulture));
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "JoistDetailedLabels", item.JoistDetailedLabels.ToString(CultureInfo.InvariantCulture));
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "MeasurementCount", item.Measurements.Count.ToString());
        OurPlaneCoreJobStore.SetProperty(item.FolderPath, "MeasuredPageCount", MeasuredPageCount(item).ToString());
        ApplyTakeoffPropertiesToMeasurements(item);
        SaveMeasurements(item.FolderPath, item.Measurements);
    }

    public static void ApplyTakeoffPropertiesToMeasurements(TakeoffItem item)
    {
        bool joistEnabled = item.IsJoistArea;
        string rounding = JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding);
        foreach (Measurement measurement in item.Measurements)
        {
            measurement.TakeoffFolder = item.FolderPath;
            measurement.JoistEnabled = joistEnabled &&
                OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area";
            measurement.JoistType = item.JoistType ?? "";
            measurement.JoistSpacingInches = item.JoistSpacingInches > 0 ? item.JoistSpacingInches : 16;
            if (!measurement.JoistDirectionLocked)
                measurement.JoistDirectionDegrees = item.JoistDirectionDegrees;
            measurement.JoistPitch = JoistTakeoffCalculator.NormalizePitch(item.JoistPitch);
            measurement.JoistLengthRounding = rounding;
            measurement.JoistShowLabels = item.JoistShowLabels;
            measurement.JoistDetailedLabels = item.JoistDetailedLabels;
        }
    }

    public static List<Measurement> LoadMeasurements(string takeoffFolder)
    {
        string path = MeasurementsJsonPath(takeoffFolder);
        if (!File.Exists(path)) return [];

        try
        {
            var dtos = JsonSerializer.Deserialize<List<MeasurementDto>>(File.ReadAllText(path)) ?? [];
            return dtos.Select(dto => ToMeasurement(dto, takeoffFolder)).ToList();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            OurPlaneCoreJobStore.QuarantineCorruptJson(path, "LoadMeasurements", ex);
            return [];
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"LoadMeasurements failed for {path}");
            return [];
        }
    }

    public static void SaveMeasurements(string takeoffFolder, IEnumerable<Measurement> measurements)
    {
        Directory.CreateDirectory(takeoffFolder);
        var dtos = measurements.Select(ToDto).ToList();

        try
        {
            IoUtil.WriteAllTextAtomic(
                MeasurementsJsonPath(takeoffFolder),
                JsonSerializer.Serialize(dtos, OurPlaneCoreJobStore.JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(MeasurementsJsonPath(takeoffFolder))}': {ex.Message}", ex);
        }
    }

    public static bool IsTakeoffItemFolder(string folder)
    {
        string? kind = OurPlaneCoreJobStore.ReadProperty(folder, "SmartNodeKind");
        if (string.Equals(kind, "item", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(kind, "folder", StringComparison.OrdinalIgnoreCase))
            return false;

        return File.Exists(MeasurementsJsonPath(folder)) ||
            OurPlaneCoreJobStore.ReadProperty(folder, "Color") != null;
    }

    internal static string MeasurementsJsonPath(string takeoffFolder) =>
        Path.Combine(takeoffFolder, "measurements.json");

    private static int MeasuredPageCount(TakeoffItem item) =>
        item.Measurements
            .Select(measurement => measurement.PageFolder)
            .Where(page => !string.IsNullOrWhiteSpace(page))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static Measurement ToMeasurement(MeasurementDto dto, string takeoffFolder)
    {
        double scale = dto.ScaleMetersPerPt;
        if (scale <= 0 && !string.IsNullOrWhiteSpace(dto.PageFolder))
            scale = OurPlaneCoreJobStore.ReadSource(dto.PageFolder)?.ScaleMetersPerPt ?? 0;

        return new Measurement
        {
            Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
            Name = dto.Name ?? "",
            Notes = dto.Notes ?? "",
            MType = OurPlaneCoreJobStore.NormalizeMeasurementType(dto.MType),
            Color = dto.Color,
            PageFolder = dto.PageFolder,
            TakeoffFolder = takeoffFolder,
            ScaleMetersPerPt = scale,
            JoistDirectionDegrees = dto.JoistDirectionDegrees,
            JoistDirectionLocked = dto.JoistDirectionLocked,
            Points = dto.PointsPdf.Select(p => new SKPoint(p.X, p.Y)).ToList(),
            Holes = dto.HolesPdf
                .Select(hole => hole.Select(p => new SKPoint(p.X, p.Y)).ToList())
                .Where(hole => hole.Count >= 3)
                .ToList(),
        };
    }

    private static MeasurementDto ToDto(Measurement measurement) =>
        new()
        {
            Id = measurement.Id,
            Name = measurement.Name,
            Notes = measurement.Notes,
            MType = OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType),
            Color = measurement.Color,
            PageFolder = measurement.PageFolder,
            ScaleMetersPerPt = measurement.ScaleMetersPerPt,
            JoistDirectionDegrees = measurement.JoistDirectionDegrees,
            JoistDirectionLocked = measurement.JoistDirectionLocked,
            PointsPdf = measurement.Points.Select(p => new PointDto(p.X, p.Y)).ToList(),
            HolesPdf = measurement.Holes
                .Where(hole => hole.Count >= 3)
                .Select(hole => hole.Select(p => new PointDto(p.X, p.Y)).ToList())
                .ToList(),
        };

    private static double ParseDouble(string? value)
    {
        return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 0;
    }

    private static double ParsePositiveDouble(string? value, double fallback)
    {
        double parsed = ParseDouble(value);
        return parsed > 0 ? parsed : fallback;
    }

    private static bool ParseBool(string? value) =>
        bool.TryParse(value, out bool parsed) && parsed;

    private static bool ParseBool(string? value, bool fallback) =>
        bool.TryParse(value, out bool parsed) ? parsed : fallback;
}
