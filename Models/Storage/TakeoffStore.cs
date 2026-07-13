using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SkiaSharp;

namespace OurPlanCore;

internal static class TakeoffStore
{
    public static string CreateTakeoffFolder(OurPlanCoreJob job, string parentFolder, string name)
    {
        string targetParent = OurPlanCoreJobStore.IsSameOrDescendant(job.TakeoffsRoot, parentFolder)
            ? parentFolder
            : job.TakeoffsRoot;
        string folder = OurPlanCoreJobStore.CreateFolderAllowDuplicateName(targetParent, name);
        OurPlanCoreJobStore.SetProperty(folder, "SmartNodeKind", "folder");
        return folder;
    }

    public static TakeoffItem CreateTakeoffItem(OurPlanCoreJob job, string name, string color) =>
        CreateTakeoffItem(job, job.TakeoffsRoot, name, color, "line");

    public static TakeoffItem CreateTakeoffItem(
        OurPlanCoreJob job,
        string parentFolder,
        string name,
        string color,
        string measurementType)
    {
        string targetParent = OurPlanCoreJobStore.IsSameOrDescendant(job.TakeoffsRoot, parentFolder)
            ? parentFolder
            : job.TakeoffsRoot;
        string folder = OurPlanCoreJobStore.CreateFolderAllowDuplicateName(targetParent, name);
        OurPlanCoreJobStore.SetProperty(folder, "SmartNodeKind", "item");
        OurPlanCoreJobStore.SetProperty(folder, "Color", color);
        OurPlanCoreJobStore.SetProperty(folder, "MeasurementType", OurPlanCoreJobStore.NormalizeMeasurementType(measurementType));
        OurPlanCoreJobStore.SetProperty(folder, "CountSymbol", CountDisplaySymbol.Circle);
        return new TakeoffItem
        {
            Name = OurPlanCoreJobStore.DisplayName(folder),
            Color = color,
            FolderPath = folder,
            MeasurementType = OurPlanCoreJobStore.NormalizeMeasurementType(measurementType),
            CountSymbol = CountDisplaySymbol.Circle,
        };
    }

    public static IReadOnlyList<TakeoffItem> LoadTakeoffItems(OurPlanCoreJob job)
    {
        var items = new List<TakeoffItem>();
        if (!Directory.Exists(job.TakeoffsRoot)) return items;

        foreach (string folder in OurPlanCoreJobStore.EnumerateSelfAndDescendants(job.TakeoffsRoot).Skip(1))
            if (TryReadTakeoffItem(folder) is { } item)
                items.Add(item);

        return items;
    }

    public static TakeoffItem? TryReadTakeoffItem(string folder)
    {
        if (!IsTakeoffItemFolder(folder)) return null;

        var measurements = LoadMeasurements(folder);
        string measurementType = OurPlanCoreJobStore.NormalizeMeasurementType(
            OurPlanCoreJobStore.ReadProperty(folder, "MeasurementType") ??
            measurements.FirstOrDefault()?.MType ??
            "line");
        bool isJoistTakeoff = ParseBool(OurPlanCoreJobStore.ReadProperty(folder, "JoistEnabled"));
        string? joistShowLabels = OurPlanCoreJobStore.ReadProperty(folder, "JoistShowLabels");
        string? joistShowLabelsUserSet = OurPlanCoreJobStore.ReadProperty(folder, "JoistShowLabelsUserSet");

        var item = new TakeoffItem
        {
            Name = OurPlanCoreJobStore.DisplayName(folder),
            Color = OurPlanCoreJobStore.ReadProperty(folder, "Color") ?? "#FF4444",
            FolderPath = folder,
            MeasurementType = measurementType,
            CountSymbol = CountDisplaySymbol.Normalize(OurPlanCoreJobStore.ReadProperty(folder, "CountSymbol")),
            UnitPrice = ParseDouble(OurPlanCoreJobStore.ReadProperty(folder, "UnitPrice")),
            Notes = OurPlanCoreJobStore.ReadProperty(folder, "Notes") ?? "",
            IsJoistTakeoff = isJoistTakeoff,
            JoistType = OurPlanCoreJobStore.ReadProperty(folder, "JoistType") ?? "",
            JoistSpacingInches = ParsePositiveDouble(OurPlanCoreJobStore.ReadProperty(folder, "JoistSpacingInches"), 16),
            JoistDirectionDegrees = ParseDouble(OurPlanCoreJobStore.ReadProperty(folder, "JoistDirectionDegrees")),
            JoistDirectionFollowsAreaRotation = ParseBool(OurPlanCoreJobStore.ReadProperty(folder, "JoistDirectionFollowsAreaRotation"), fallback: true),
            JoistAddEndJoist = ParseBool(OurPlanCoreJobStore.ReadProperty(folder, "JoistAddEndJoist"), fallback: true),
            JoistPitch = JoistTakeoffCalculator.NormalizePitch(OurPlanCoreJobStore.ReadProperty(folder, "JoistPitch")),
            JoistLengthRounding = JoistTakeoffCalculator.NormalizeLengthRounding(OurPlanCoreJobStore.ReadProperty(folder, "JoistLengthRounding")),
            JoistShowLabels = ParseJoistShowLabels(joistShowLabels, joistShowLabelsUserSet, measurementType, isJoistTakeoff),
            JoistShowLabelsUserSet = ParseBool(joistShowLabelsUserSet),
            JoistDetailedLabels = ParseBool(OurPlanCoreJobStore.ReadProperty(folder, "JoistDetailedLabels"), fallback: true),
            MultiLineOffsets = ParseMultiLineOffsets(OurPlanCoreJobStore.ReadProperty(folder, "MultiLineOffsets"), folder),
        };
        item.Measurements.AddRange(measurements);
        ApplyTakeoffPropertiesToMeasurements(item);
        return item;
    }

    public static void SaveTakeoffItem(TakeoffItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FolderPath) || !Directory.Exists(item.FolderPath))
            return;

        OurPlanCoreJobStore.UpdateItemName(item.FolderPath, item.Name);
        item.CountSymbol = CountDisplaySymbol.Normalize(item.CountSymbol);
        OurPlanCoreJobStore.SetProperties(item.FolderPath, new[]
        {
            new KeyValuePair<string, string>("SmartNodeKind", "item"),
            new KeyValuePair<string, string>("Color", item.Color),
            new KeyValuePair<string, string>("MeasurementType", OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType)),
            new KeyValuePair<string, string>("CountSymbol", item.CountSymbol),
            new KeyValuePair<string, string>("UnitPrice", item.UnitPrice.ToString("G17", CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("Notes", item.Notes),
            new KeyValuePair<string, string>("JoistEnabled", item.IsJoistArea.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("JoistType", item.JoistType ?? ""),
            new KeyValuePair<string, string>("JoistSpacingInches", Math.Max(0.001, item.JoistSpacingInches).ToString("G17", CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("JoistDirectionDegrees", item.JoistDirectionDegrees.ToString("G17", CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("JoistDirectionFollowsAreaRotation", item.JoistDirectionFollowsAreaRotation.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("JoistAddEndJoist", item.JoistAddEndJoist.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("JoistPitch", JoistTakeoffCalculator.NormalizePitch(item.JoistPitch)),
            new KeyValuePair<string, string>("JoistLengthRounding", JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding)),
            new KeyValuePair<string, string>("JoistShowLabels", item.JoistShowLabels.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("JoistShowLabelsUserSet", item.JoistShowLabelsUserSet.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("JoistDetailedLabels", item.JoistDetailedLabels.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("MultiLineOffsets", SerializeMultiLineOffsets(item.MultiLineOffsets, item.FolderPath)),
            new KeyValuePair<string, string>("MeasurementCount", item.Measurements.Count.ToString()),
            new KeyValuePair<string, string>("MeasuredPageCount", MeasuredPageCount(item).ToString()),
        });
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
            if (OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "point")
            {
                measurement.CountSymbol = string.IsNullOrWhiteSpace(measurement.CountSymbol)
                    ? CountDisplaySymbol.Normalize(item.CountSymbol)
                    : CountDisplaySymbol.Normalize(measurement.CountSymbol);
            }
            measurement.JoistEnabled = joistEnabled &&
                OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area";
            measurement.JoistType = item.JoistType ?? "";
            measurement.JoistSpacingInches = item.JoistSpacingInches > 0 ? item.JoistSpacingInches : 16;
            if (!measurement.JoistDirectionLocked)
                measurement.JoistDirectionDegrees = item.JoistDirectionDegrees;
            measurement.JoistDirectionFollowsAreaRotation = item.JoistDirectionFollowsAreaRotation;
            measurement.JoistAddEndJoist = item.JoistAddEndJoist;
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
            var dtos = ParseMeasurementDtos(File.ReadAllText(path));
            return dtos.Select(dto => ToMeasurement(dto, takeoffFolder)).ToList();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            OurPlanCoreJobStore.QuarantineCorruptJson(path, "LoadMeasurements", ex);
            return [];
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"LoadMeasurements failed for {path}");
            return [];
        }
    }

    // Current on-disk format version for measurements.json. Writes still emit
    // the legacy bare array, so this is the version a future envelope-writing
    // build would stamp; the reader below already tolerates that envelope.
    internal const int CurrentMeasurementsSchemaVersion = 1;

    // Accept both the legacy bare array and a { schema_version, measurements }
    // envelope so a future format bump cannot make old files look corrupt.
    private static List<MeasurementDto> ParseMeasurementDtos(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        int i = 0;
        while (i < json.Length && char.IsWhiteSpace(json[i]))
            i++;

        if (i < json.Length && json[i] == '{')
            return JsonSerializer.Deserialize<MeasurementsFileDto>(json)?.Measurements ?? [];

        return JsonSerializer.Deserialize<List<MeasurementDto>>(json) ?? [];
    }

    public static void SaveMeasurements(string takeoffFolder, IEnumerable<Measurement> measurements)
    {
        Directory.CreateDirectory(takeoffFolder);
        var dtos = measurements.Select(measurement => ToDto(measurement, takeoffFolder)).ToList();

        try
        {
            IoUtil.WriteAllTextAtomic(
                MeasurementsJsonPath(takeoffFolder),
                JsonSerializer.Serialize(dtos, OurPlanCoreJobStore.JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(MeasurementsJsonPath(takeoffFolder))}': {ex.Message}", ex);
        }
    }

    public static bool IsTakeoffItemFolder(string folder)
    {
        string? kind = OurPlanCoreJobStore.ReadProperty(folder, "SmartNodeKind");
        if (string.Equals(kind, "item", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(kind, "folder", StringComparison.OrdinalIgnoreCase))
            return false;

        return File.Exists(MeasurementsJsonPath(folder)) ||
            OurPlanCoreJobStore.ReadProperty(folder, "Color") != null;
    }

    internal static string MeasurementsJsonPath(string takeoffFolder) =>
        Path.Combine(takeoffFolder, "measurements.json");

    private static int MeasuredPageCount(TakeoffItem item) =>
        item.Measurements
            .Select(measurement => measurement.PageFolder)
            .Where(page => !string.IsNullOrWhiteSpace(page))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    // measurements.json historically stored PageFolder as an absolute path,
    // which breaks when a job folder is moved or synced to another machine.
    // New saves store it relative to the job root; loads accept both forms.
    private static string? JobRootFromTakeoffFolder(string takeoffFolder)
    {
        try
        {
            for (DirectoryInfo? dir = new(takeoffFolder); dir != null; dir = dir.Parent)
                if (string.Equals(dir.Name, "Takeoffs", StringComparison.OrdinalIgnoreCase))
                    return dir.Parent?.FullName;
        }
        catch (Exception ex) when (ex is ArgumentException or System.Security.SecurityException or PathTooLongException)
        {
            // Fall through: callers keep the stored path as-is.
        }
        return null;
    }

    private static string ResolveJobRelativeFolder(string? stored, string takeoffFolder)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return "";
        if (Path.IsPathRooted(stored))
            return stored;

        string? jobRoot = JobRootFromTakeoffFolder(takeoffFolder);
        if (jobRoot == null)
            return stored;

        try
        {
            return Path.GetFullPath(Path.Combine(jobRoot, stored));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            return stored;
        }
    }

    private static string ToJobRelativeFolder(string folder, string takeoffFolder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder))
            return folder ?? "";

        string? jobRoot = JobRootFromTakeoffFolder(takeoffFolder);
        if (jobRoot == null)
            return folder;

        try
        {
            string relative = Path.GetRelativePath(jobRoot, folder);
            return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
                ? folder
                : relative;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            return folder;
        }
    }

    private static Measurement ToMeasurement(MeasurementDto dto, string takeoffFolder)
    {
        string pageFolder = ResolveJobRelativeFolder(dto.PageFolder, takeoffFolder);
        double scale = dto.ScaleMetersPerPt;
        if (scale <= 0 && !string.IsNullOrWhiteSpace(pageFolder))
            scale = OurPlanCoreJobStore.ReadSource(pageFolder)?.ScaleMetersPerPt ?? 0;

        return new Measurement
        {
            Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
            Name = dto.Name ?? "",
            Notes = dto.Notes ?? "",
            MType = OurPlanCoreJobStore.NormalizeMeasurementType(dto.MType),
            Color = dto.Color,
            CountSymbol = string.IsNullOrWhiteSpace(dto.CountSymbol)
                ? ""
                : CountDisplaySymbol.Normalize(dto.CountSymbol),
            PageFolder = pageFolder,
            TakeoffFolder = takeoffFolder,
            ScaleMetersPerPt = scale,
            JoistDirectionDegrees = dto.JoistDirectionDegrees,
            JoistDirectionLocked = dto.JoistDirectionLocked,
            JoistDirectionFollowsAreaRotation = dto.JoistDirectionFollowsAreaRotation,
            JoistAddEndJoist = dto.JoistAddEndJoist,
            Points = dto.PointsPdf.Select(p => new SKPoint(p.X, p.Y)).ToList(),
            Holes = dto.HolesPdf
                .Select(hole => hole.Select(p => new SKPoint(p.X, p.Y)).ToList())
                .Where(hole => hole.Count >= 3)
                .ToList(),
        };
    }

    private static MeasurementDto ToDto(Measurement measurement, string takeoffFolder) =>
        new()
        {
            Id = measurement.Id,
            Name = measurement.Name,
            Notes = measurement.Notes,
            MType = OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType),
            Color = measurement.Color,
            CountSymbol = CountDisplaySymbol.Normalize(measurement.CountSymbol),
            PageFolder = ToJobRelativeFolder(measurement.PageFolder, takeoffFolder),
            ScaleMetersPerPt = measurement.ScaleMetersPerPt,
            JoistDirectionDegrees = measurement.JoistDirectionDegrees,
            JoistDirectionLocked = measurement.JoistDirectionLocked,
            JoistDirectionFollowsAreaRotation = measurement.JoistDirectionFollowsAreaRotation,
            JoistAddEndJoist = measurement.JoistAddEndJoist,
            PointsPdf = measurement.Points.Select(p => new PointDto(p.X, p.Y)).ToList(),
            HolesPdf = measurement.Holes
                .Where(hole => hole.Count >= 3)
                .Select(hole => hole.Select(p => new PointDto(p.X, p.Y)).ToList())
                .ToList(),
        };

    private static List<MultiLineOffsetConfig> ParseMultiLineOffsets(string? value, string takeoffFolder)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        try
        {
            var parsed = JsonSerializer.Deserialize<List<MultiLineOffsetConfig>>(value) ?? [];
            var configs = parsed
                .Where(config => config != null && config.Meters > 0)
                .ToList();
            foreach (MultiLineOffsetConfig config in configs)
                config.CompanionFolder = ResolveJobRelativeFolder(config.CompanionFolder, takeoffFolder);
            return configs;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string SerializeMultiLineOffsets(List<MultiLineOffsetConfig> offsets, string takeoffFolder)
    {
        if (offsets.Count == 0)
            return "";

        var portable = offsets
            .Select(config => new MultiLineOffsetConfig
            {
                Name = config.Name,
                Color = config.Color,
                Meters = config.Meters,
                RightSide = config.RightSide,
                CompanionFolder = ToJobRelativeFolder(config.CompanionFolder, takeoffFolder),
            })
            .ToList();
        return JsonSerializer.Serialize(portable);
    }

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

    private static bool ParseJoistShowLabels(
        string? value,
        string? userSetValue,
        string measurementType,
        bool isJoistTakeoff)
    {
        bool isJoistArea = isJoistTakeoff &&
            OurPlanCoreJobStore.NormalizeMeasurementType(measurementType) == "area";
        if (bool.TryParse(value, out bool parsed))
        {
            if (parsed || ParseBool(userSetValue))
                return parsed;

            return isJoistArea && JoistTakeoffDefaults.ShowLabels;
        }

        return isJoistArea && JoistTakeoffDefaults.ShowLabels;
    }
}
