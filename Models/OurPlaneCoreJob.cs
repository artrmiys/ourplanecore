using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using SkiaSharp;

namespace OurPlaneCore;

public sealed class OurPlaneCoreJob
{
    public string Name { get; init; } = "";
    public string RootPath { get; init; } = "";
    public string PagesRoot => Path.Combine(RootPath, "Pages");
    public string TakeoffsRoot => Path.Combine(RootPath, "Takeoffs");
    public string AIContextRoot => Path.Combine(RootPath, "AI_Context");
}

public sealed class PageFolderNode
{
    public string Name { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public bool IsRoot { get; init; }
}

public sealed class TakeoffFolderNode
{
    public string Name { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public bool IsRoot { get; init; }
}

public sealed class PageInfo
{
    public string Name { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public string PdfPath { get; init; } = "";
    public int PdfPage { get; init; }
    public double ScaleMetersPerPt { get; set; }
    public bool PdfLayersCached { get; init; }
    public IReadOnlyList<PdfLayerInfo> PdfLayers { get; init; } = [];
    public List<string> LegendTakeoffOrder { get; set; } = [];
    public string OverlayPageFolder { get; init; } = "";
    public string OverlayColor { get; init; } = "#E53935";
    public double OverlayOpacity { get; init; } = 0.55;
    public double OverlayOffsetXPt { get; init; }
    public double OverlayOffsetYPt { get; init; }
    public double OverlayScale { get; init; } = 1.0;
}

public sealed class SourceInfo
{
    [JsonPropertyName("pdf")]
    public string Pdf { get; set; } = "";

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("scale_m_per_pt")]
    public double ScaleMetersPerPt { get; set; }

    [JsonPropertyName("pdf_layers_cached")]
    public bool PdfLayersCached { get; set; }

    [JsonPropertyName("pdf_layers")]
    public List<PdfLayerInfo> PdfLayers { get; set; } = [];

    [JsonPropertyName("legend_takeoff_order")]
    public List<string> LegendTakeoffOrder { get; set; } = [];

    [JsonPropertyName("overlay_page_folder")]
    public string OverlayPageFolder { get; set; } = "";

    [JsonPropertyName("overlay_color")]
    public string OverlayColor { get; set; } = "#E53935";

    [JsonPropertyName("overlay_opacity")]
    public double OverlayOpacity { get; set; } = 0.55;

    [JsonPropertyName("overlay_offset_x_pt")]
    public double OverlayOffsetXPt { get; set; }

    [JsonPropertyName("overlay_offset_y_pt")]
    public double OverlayOffsetYPt { get; set; }

    [JsonPropertyName("overlay_scale")]
    public double OverlayScale { get; set; } = 1.0;
}

public sealed class PdfLayerInfo
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("on")]
    public bool IsOn { get; set; } = true;
}

public sealed class PageLayerManifest
{
    [JsonPropertyName("source_pdf")]
    public string SourcePdf { get; set; } = "";

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    [JsonPropertyName("generated_at_utc")]
    public string GeneratedAtUtc { get; set; } = "";

    [JsonPropertyName("layer_count")]
    public int LayerCount { get; set; }

    [JsonPropertyName("layers")]
    public List<PdfLayerInfo> Layers { get; set; } = [];
}

internal sealed class MeasurementDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("mtype")]
    public string MType { get; set; } = "line";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("points_pdf")]
    public List<PointDto> PointsPdf { get; set; } = [];

    [JsonPropertyName("holes_pdf")]
    public List<List<PointDto>> HolesPdf { get; set; } = [];

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FF4444";

    [JsonPropertyName("page_folder")]
    public string PageFolder { get; set; } = "";

    [JsonPropertyName("scale_m_per_pt")]
    public double ScaleMetersPerPt { get; set; }

    [JsonPropertyName("joist_direction_degrees")]
    public double JoistDirectionDegrees { get; set; }

    [JsonPropertyName("joist_direction_locked")]
    public bool JoistDirectionLocked { get; set; }
}

internal sealed class PageAnnotationDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "line";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#1565C0";

    [JsonPropertyName("page_folder")]
    public string PageFolder { get; set; } = "";

    [JsonPropertyName("scale_m_per_pt")]
    public double ScaleMetersPerPt { get; set; }

    [JsonPropertyName("points_pdf")]
    public List<PointDto> PointsPdf { get; set; } = [];
}

internal sealed record PointDto(float X, float Y);

public static class OurPlaneCoreJobStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
    private static readonly IComparer<string> NaturalNameComparer = new NaturalStringComparer();
    private static readonly object CorruptJsonLock = new();
    private static readonly List<string> CorruptJsonFiles = [];

    public static IReadOnlyList<string> DrainCorruptJsonFiles()
    {
        lock (CorruptJsonLock)
        {
            var files = CorruptJsonFiles.ToList();
            CorruptJsonFiles.Clear();
            return files;
        }
    }

    public static OurPlaneCoreJob CreateJob(string parentDir, string jobName)
    {
        string root = Path.Combine(parentDir, SanitizeName(jobName, 120));
        Directory.CreateDirectory(root);
        WriteItemDataXml(root, "Folder", jobName, 0);

        EnsureFolder(root, "sources");
        string pages = EnsureFolder(root, "Pages");
        string imported = EnsureFolder(pages, "00. imported");
        EnsureFolder(imported, "Arch");
        EnsureFolder(imported, "Struct");
        EnsureFolder(pages, "--------others");
        EnsureFolder(root, "Takeoffs");
        SmartContextStore.EnsureProjectContext(root, jobName);

        return LoadJob(root);
    }

    public static OurPlaneCoreJob LoadJob(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException(rootPath);

        if (!File.Exists(Path.Combine(rootPath, "Data.xml")))
            WriteItemDataXml(rootPath, "Folder", Path.GetFileName(rootPath), 0);

        string name = ReadName(rootPath) ?? Path.GetFileName(rootPath);
        var job = new OurPlaneCoreJob { Name = name, RootPath = rootPath };

        EnsureFolder(rootPath, "sources");
        string pages = EnsureFolder(rootPath, "Pages");
        string imported = EnsureFolder(pages, "00. imported");
        EnsureFolder(imported, "Arch");
        EnsureFolder(imported, "Struct");
        EnsureFolder(pages, "--------others");
        EnsureFolder(rootPath, "Takeoffs");
        SmartContextStore.EnsureProjectContext(rootPath, name);

        return job;
    }

    public static string EnsureFolder(string parentFolder, string name)
    {
        string path = Path.Combine(parentFolder, SanitizeName(name, 120));
        Directory.CreateDirectory(path);
        string dataXml = Path.Combine(path, "Data.xml");
        if (!File.Exists(dataXml))
            WriteItemDataXml(path, "Folder", name, GetNextOrderIndex(parentFolder));
        return path;
    }

    public static string CreateFolder(string parentFolder, string name)
    {
        string cleanName = SanitizeName(name, 120);
        string path = Path.Combine(parentFolder, cleanName);
        if (Directory.Exists(path))
            throw new IOException($"'{cleanName}' already exists in this folder.");

        Directory.CreateDirectory(path);
        WriteItemDataXml(path, "Folder", cleanName, GetNextOrderIndex(parentFolder));
        return path;
    }

    public static string DefaultImportFolder(OurPlaneCoreJob job)
    {
        string imported = EnsureFolder(job.PagesRoot, "00. imported");
        return EnsureFolder(imported, "Arch");
    }

    public static IReadOnlyList<PageInfo> ImportPdf(
        OurPlaneCoreJob job,
        string pdfSourcePath,
        IReadOnlyList<string> pageNames,
        string destinationFolder,
        IReadOnlyDictionary<int, IReadOnlyList<PdfLayerInfo>>? pdfLayerCache = null)
    {
        string sourcesDir = EnsureFolder(job.RootPath, "sources");
        string pdfDest = UniqueFilePath(Path.Combine(sourcesDir, Path.GetFileName(pdfSourcePath)));
        if (!File.Exists(pdfDest))
            File.Copy(pdfSourcePath, pdfDest);

        var created = new List<PageInfo>();
        for (int i = 0; i < pageNames.Count; i++)
        {
            string displayName = string.IsNullOrWhiteSpace(pageNames[i])
                ? $"Page {i + 1}"
                : pageNames[i].Trim();
            string pageFolder = UniqueDirectoryPath(Path.Combine(destinationFolder, SanitizeName(displayName, 120)));
            Directory.CreateDirectory(pageFolder);

            WriteItemDataXml(pageFolder, "Page", displayName, GetNextOrderIndex(destinationFolder));
            if (pdfLayerCache != null && pdfLayerCache.TryGetValue(i, out var cachedLayers))
                WriteSource(pageFolder, pdfDest, i, 0, cachedLayers, pdfLayersCached: true);
            else
                WriteSource(pageFolder, pdfDest, i, 0);

            created.Add(new PageInfo
            {
                Name = displayName,
                FolderPath = pageFolder,
                PdfPath = pdfDest,
                PdfPage = i,
                ScaleMetersPerPt = 0,
                PdfLayersCached = pdfLayerCache != null && pdfLayerCache.ContainsKey(i),
                PdfLayers = pdfLayerCache != null && pdfLayerCache.TryGetValue(i, out var layers)
                    ? layers.ToList()
                    : [],
            });
        }

        return created;
    }

    public static PageInfo CreatePageFromPdf(
        OurPlaneCoreJob job,
        string pdfSourcePath,
        string displayName,
        string destinationFolder,
        int pdfPage = 0,
        double scaleMetersPerPt = 0)
    {
        string sourcesDir = EnsureFolder(job.RootPath, "sources");
        string pdfDest = UniqueFilePath(Path.Combine(sourcesDir, Path.GetFileName(pdfSourcePath)));
        if (!File.Exists(pdfDest))
            File.Copy(pdfSourcePath, pdfDest);

        string cleanName = string.IsNullOrWhiteSpace(displayName)
            ? $"Page {pdfPage + 1}"
            : displayName.Trim();
        string pageFolder = UniqueDirectoryPath(Path.Combine(destinationFolder, SanitizeName(cleanName, 120)));
        Directory.CreateDirectory(pageFolder);

        WriteItemDataXml(pageFolder, "Page", cleanName, GetNextOrderIndex(destinationFolder));
        WriteSource(pageFolder, pdfDest, pdfPage, scaleMetersPerPt);
        return new PageInfo
        {
            Name = cleanName,
            FolderPath = pageFolder,
            PdfPath = pdfDest,
            PdfPage = pdfPage,
            ScaleMetersPerPt = scaleMetersPerPt,
        };
    }

    public static SourceInfo? ReadSource(string pageFolder)
    {
        string path = Path.Combine(pageFolder, "source.json");
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<SourceInfo>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            QuarantineCorruptJson(path, "ReadSource", ex);
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"ReadSource failed for {path}");
            return null;
        }
    }

    public static PageInfo? TryReadPage(string pageFolder)
    {
        SourceInfo? src = ReadSource(pageFolder);
        if (src == null) return null;

        string pdfPath = Path.GetFullPath(Path.Combine(pageFolder, src.Pdf));
        return new PageInfo
        {
            Name = ReadName(pageFolder) ?? Path.GetFileName(pageFolder),
            FolderPath = pageFolder,
            PdfPath = pdfPath,
            PdfPage = src.Page,
            ScaleMetersPerPt = src.ScaleMetersPerPt,
            PdfLayersCached = src.PdfLayersCached,
            PdfLayers = src.PdfLayers,
            LegendTakeoffOrder = src.LegendTakeoffOrder ?? [],
            OverlayPageFolder = ResolveRelativePagePath(pageFolder, src.OverlayPageFolder),
            OverlayColor = string.IsNullOrWhiteSpace(src.OverlayColor) ? "#E53935" : src.OverlayColor,
            OverlayOpacity = NormalizeOverlayOpacity(src.OverlayOpacity),
            OverlayOffsetXPt = NormalizeOverlayOffset(src.OverlayOffsetXPt),
            OverlayOffsetYPt = NormalizeOverlayOffset(src.OverlayOffsetYPt),
            OverlayScale = NormalizeOverlayScale(src.OverlayScale),
        };
    }

    private static string ResolveRelativePagePath(string pageFolder, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(pageFolder, path));
        }
        catch
        {
            return path;
        }
    }

    private static string MakeRelativePageReference(string pageFolder, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            string full = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(pageFolder, path));
            return Path.GetRelativePath(pageFolder, full);
        }
        catch
        {
            return path;
        }
    }

    private static double NormalizeOverlayOpacity(double opacity)
    {
        if (double.IsNaN(opacity) || double.IsInfinity(opacity) || opacity <= 0)
            return 0.55;

        return Math.Clamp(opacity, 0.05, 1.0);
    }

    private static double NormalizeOverlayOffset(double offset) =>
        double.IsNaN(offset) || double.IsInfinity(offset)
            ? 0
            : Math.Clamp(offset, -100000, 100000);

    private static double NormalizeOverlayScale(double scale) =>
        double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0
            ? 1.0
            : Math.Clamp(scale, 0.05, 20.0);

    public static void SavePageScale(string pageFolder, double scaleMetersPerPt)
    {
        SourceInfo? src = ReadSource(pageFolder);
        if (src == null) return;

        string pdfAbs = Path.GetFullPath(Path.Combine(pageFolder, src.Pdf));
        WriteSource(
            pageFolder,
            pdfAbs,
            src.Page,
            scaleMetersPerPt,
            src.PdfLayers,
            src.PdfLayersCached,
            src.LegendTakeoffOrder,
            src.OverlayPageFolder,
            src.OverlayColor,
            src.OverlayOpacity,
            src.OverlayOffsetXPt,
            src.OverlayOffsetYPt,
            src.OverlayScale);
    }

    public static void SavePageLegendTakeoffOrder(string pageFolder, IReadOnlyList<string> legendTakeoffOrder)
    {
        SourceInfo? src = ReadSource(pageFolder);
        if (src == null) return;

        string pdfAbs = Path.GetFullPath(Path.Combine(pageFolder, src.Pdf));
        WriteSource(
            pageFolder,
            pdfAbs,
            src.Page,
            src.ScaleMetersPerPt,
            src.PdfLayers,
            src.PdfLayersCached,
            legendTakeoffOrder,
            src.OverlayPageFolder,
            src.OverlayColor,
            src.OverlayOpacity,
            src.OverlayOffsetXPt,
            src.OverlayOffsetYPt,
            src.OverlayScale);
    }

    public static void SavePageOverlay(
        string pageFolder,
        string overlayPageFolder,
        string overlayColor,
        double overlayOpacity)
    {
        SourceInfo? src = ReadSource(pageFolder);
        if (src == null) return;

        string pdfAbs = Path.GetFullPath(Path.Combine(pageFolder, src.Pdf));
        WriteSource(
            pageFolder,
            pdfAbs,
            src.Page,
            src.ScaleMetersPerPt,
            src.PdfLayers,
            src.PdfLayersCached,
            src.LegendTakeoffOrder,
            overlayPageFolder,
            string.IsNullOrWhiteSpace(overlayColor) ? "#E53935" : overlayColor,
            NormalizeOverlayOpacity(overlayOpacity),
            src.OverlayOffsetXPt,
            src.OverlayOffsetYPt,
            src.OverlayScale);
    }

    public static void ClearPageOverlay(string pageFolder) =>
        SavePageOverlay(pageFolder, "", "#E53935", 0.55);

    public static void SavePageOverlayTransform(
        string pageFolder,
        double offsetXPt,
        double offsetYPt,
        double overlayScale)
    {
        SourceInfo? src = ReadSource(pageFolder);
        if (src == null) return;

        string pdfAbs = Path.GetFullPath(Path.Combine(pageFolder, src.Pdf));
        WriteSource(
            pageFolder,
            pdfAbs,
            src.Page,
            src.ScaleMetersPerPt,
            src.PdfLayers,
            src.PdfLayersCached,
            src.LegendTakeoffOrder,
            src.OverlayPageFolder,
            src.OverlayColor,
            src.OverlayOpacity,
            offsetXPt,
            offsetYPt,
            overlayScale);
    }

    public static string CreateTakeoffFolder(OurPlaneCoreJob job, string parentFolder, string name)
    {
        string targetParent = IsSameOrDescendant(job.TakeoffsRoot, parentFolder)
            ? parentFolder
            : job.TakeoffsRoot;
        string folder = CreateFolder(targetParent, name);
        SetProperty(folder, "SmartNodeKind", "folder");
        return folder;
    }

    public static TakeoffItem CreateTakeoffItem(OurPlaneCoreJob job, string name, string color) =>
        CreateTakeoffItem(job, job.TakeoffsRoot, name, color, "line");

    public static TakeoffItem CreateTakeoffItem(OurPlaneCoreJob job, string parentFolder, string name, string color, string measurementType)
    {
        string targetParent = IsSameOrDescendant(job.TakeoffsRoot, parentFolder)
            ? parentFolder
            : job.TakeoffsRoot;
        string folder = CreateFolder(targetParent, name);
        SetProperty(folder, "SmartNodeKind", "item");
        SetProperty(folder, "Color", color);
        SetProperty(folder, "MeasurementType", NormalizeMeasurementType(measurementType));
        return new TakeoffItem
        {
            Name = DisplayName(folder),
            Color = color,
            FolderPath = folder,
            MeasurementType = NormalizeMeasurementType(measurementType),
        };
    }

    public static IReadOnlyList<TakeoffItem> LoadTakeoffItems(OurPlaneCoreJob job)
    {
        var items = new List<TakeoffItem>();
        if (!Directory.Exists(job.TakeoffsRoot)) return items;

        foreach (string folder in EnumerateSelfAndDescendants(job.TakeoffsRoot).Skip(1))
            if (TryReadTakeoffItem(folder) is { } item)
                items.Add(item);

        return items;
    }

    public static TakeoffItem? TryReadTakeoffItem(string folder)
    {
        if (!IsTakeoffItemFolder(folder)) return null;

        var measurements = LoadMeasurements(folder);
        string measurementType = NormalizeMeasurementType(
            ReadProperty(folder, "MeasurementType") ??
            measurements.FirstOrDefault()?.MType ??
            "line");

        var item = new TakeoffItem
        {
            Name = DisplayName(folder),
            Color = ReadProperty(folder, "Color") ?? "#FF4444",
            FolderPath = folder,
            MeasurementType = measurementType,
            UnitPrice = ParseDouble(ReadProperty(folder, "UnitPrice")),
            Notes = ReadProperty(folder, "Notes") ?? "",
            IsJoistTakeoff = ParseBool(ReadProperty(folder, "JoistEnabled")),
            JoistType = ReadProperty(folder, "JoistType") ?? "",
            JoistSpacingInches = ParsePositiveDouble(ReadProperty(folder, "JoistSpacingInches"), 16),
            JoistDirectionDegrees = ParseDouble(ReadProperty(folder, "JoistDirectionDegrees")),
            JoistPitch = JoistTakeoffCalculator.NormalizePitch(ReadProperty(folder, "JoistPitch")),
            JoistLengthRounding = JoistTakeoffCalculator.NormalizeLengthRounding(ReadProperty(folder, "JoistLengthRounding")),
            JoistShowLabels = ParseBool(ReadProperty(folder, "JoistShowLabels")),
            JoistDetailedLabels = ParseBool(ReadProperty(folder, "JoistDetailedLabels"), fallback: true),
        };
        item.Measurements.AddRange(measurements);
        ApplyTakeoffPropertiesToMeasurements(item);
        return item;
    }

    public static void SaveTakeoffItem(TakeoffItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FolderPath) || !Directory.Exists(item.FolderPath))
            return;

        UpdateItemName(item.FolderPath, item.Name);
        SetProperty(item.FolderPath, "SmartNodeKind", "item");
        SetProperty(item.FolderPath, "Color", item.Color);
        SetProperty(item.FolderPath, "MeasurementType", NormalizeMeasurementType(item.MeasurementType));
        SetProperty(item.FolderPath, "UnitPrice", item.UnitPrice.ToString("G17", CultureInfo.InvariantCulture));
        SetProperty(item.FolderPath, "Notes", item.Notes);
        SetProperty(item.FolderPath, "JoistEnabled", (item.IsJoistArea).ToString(CultureInfo.InvariantCulture));
        SetProperty(item.FolderPath, "JoistType", item.JoistType ?? "");
        SetProperty(item.FolderPath, "JoistSpacingInches", Math.Max(0.001, item.JoistSpacingInches).ToString("G17", CultureInfo.InvariantCulture));
        SetProperty(item.FolderPath, "JoistDirectionDegrees", item.JoistDirectionDegrees.ToString("G17", CultureInfo.InvariantCulture));
        SetProperty(item.FolderPath, "JoistPitch", JoistTakeoffCalculator.NormalizePitch(item.JoistPitch));
        SetProperty(item.FolderPath, "JoistLengthRounding", JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding));
        SetProperty(item.FolderPath, "JoistShowLabels", item.JoistShowLabels.ToString(CultureInfo.InvariantCulture));
        SetProperty(item.FolderPath, "JoistDetailedLabels", item.JoistDetailedLabels.ToString(CultureInfo.InvariantCulture));
        SetProperty(item.FolderPath, "MeasurementCount", item.Measurements.Count.ToString());
        SetProperty(item.FolderPath, "MeasuredPageCount", item.Measurements
            .Select(m => m.PageFolder)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()
            .ToString());
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
                NormalizeMeasurementType(measurement.MType) == "area";
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
            return dtos.Select(dto =>
            {
                double scale = dto.ScaleMetersPerPt;
                if (scale <= 0 && !string.IsNullOrWhiteSpace(dto.PageFolder))
                    scale = ReadSource(dto.PageFolder)?.ScaleMetersPerPt ?? 0;

                return new Measurement
                {
                    Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                    Name = dto.Name ?? "",
                    Notes = dto.Notes ?? "",
                    MType = NormalizeMeasurementType(dto.MType),
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
            }).ToList();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            QuarantineCorruptJson(path, "LoadMeasurements", ex);
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
        var dtos = measurements.Select(m => new MeasurementDto
        {
            Id = m.Id,
            Name = m.Name,
            Notes = m.Notes,
            MType = NormalizeMeasurementType(m.MType),
            Color = m.Color,
            PageFolder = m.PageFolder,
            ScaleMetersPerPt = m.ScaleMetersPerPt,
            JoistDirectionDegrees = m.JoistDirectionDegrees,
            JoistDirectionLocked = m.JoistDirectionLocked,
            PointsPdf = m.Points.Select(p => new PointDto(p.X, p.Y)).ToList(),
            HolesPdf = m.Holes
                .Where(hole => hole.Count >= 3)
                .Select(hole => hole.Select(p => new PointDto(p.X, p.Y)).ToList())
                .ToList(),
        }).ToList();

        try
        {
            IoUtil.WriteAllTextAtomic(
                MeasurementsJsonPath(takeoffFolder),
                JsonSerializer.Serialize(dtos, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(MeasurementsJsonPath(takeoffFolder))}': {ex.Message}", ex);
        }
    }

    public static List<PageAnnotation> LoadPageAnnotations(string pageFolder)
    {
        string path = PageAnnotationsJsonPath(pageFolder);
        if (!File.Exists(path)) return [];

        try
        {
            var dtos = JsonSerializer.Deserialize<List<PageAnnotationDto>>(File.ReadAllText(path)) ?? [];
            return dtos.Select(dto => new PageAnnotation
            {
                Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                Kind = NormalizePageAnnotationKind(dto.Kind),
                Text = dto.Text ?? "",
                Color = string.IsNullOrWhiteSpace(dto.Color) ? "#1565C0" : dto.Color,
                PageFolder = string.IsNullOrWhiteSpace(dto.PageFolder) ? pageFolder : dto.PageFolder,
                ScaleMetersPerPt = dto.ScaleMetersPerPt,
                Points = dto.PointsPdf.Select(p => new SKPoint(p.X, p.Y)).ToList(),
            }).ToList();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            QuarantineCorruptJson(path, "LoadPageAnnotations", ex);
            return [];
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"LoadPageAnnotations failed for {path}");
            return [];
        }
    }

    public static void SavePageAnnotations(string pageFolder, IEnumerable<PageAnnotation> annotations)
    {
        Directory.CreateDirectory(pageFolder);
        var dtos = annotations.Select(annotation => new PageAnnotationDto
        {
            Id = annotation.Id,
            Kind = NormalizePageAnnotationKind(annotation.Kind),
            Text = annotation.Text ?? "",
            Color = string.IsNullOrWhiteSpace(annotation.Color) ? "#1565C0" : annotation.Color,
            PageFolder = string.IsNullOrWhiteSpace(annotation.PageFolder) ? pageFolder : annotation.PageFolder,
            ScaleMetersPerPt = annotation.ScaleMetersPerPt,
            PointsPdf = annotation.Points.Select(p => new PointDto(p.X, p.Y)).ToList(),
        }).ToList();

        try
        {
            IoUtil.WriteAllTextAtomic(
                PageAnnotationsJsonPath(pageFolder),
                JsonSerializer.Serialize(dtos, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(PageAnnotationsJsonPath(pageFolder))}': {ex.Message}", ex);
        }
    }

    public static bool IsPageFolder(string folder) =>
        File.Exists(Path.Combine(folder, "source.json"));

    public static string DisplayName(string folder) =>
        ReadName(folder) ?? Path.GetFileName(folder);

    private static string MeasurementsJsonPath(string takeoffFolder) =>
        Path.Combine(takeoffFolder, "measurements.json");

    public static string PageAnnotationsJsonPath(string pageFolder) =>
        Path.Combine(pageFolder, "annotations.json");

    public static string NormalizePageAnnotationKind(string value)
    {
        string clean = (value ?? "").Trim().ToLowerInvariant();
        return clean switch
        {
            "dimension" or "ruler" => "dimension",
            "arrow" => "arrow",
            "rectangle" or "rect" or "box" => "rectangle",
            _ => "line",
        };
    }

    public static bool IsTakeoffItemFolder(string folder)
    {
        string? kind = ReadProperty(folder, "SmartNodeKind");
        if (string.Equals(kind, "item", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(kind, "folder", StringComparison.OrdinalIgnoreCase))
            return false;

        return File.Exists(MeasurementsJsonPath(folder)) || ReadProperty(folder, "Color") != null;
    }

    public static IReadOnlyList<string> GetOrderedChildDirectories(string parentFolder)
    {
        if (!Directory.Exists(parentFolder)) return [];
        return Directory.EnumerateDirectories(parentFolder)
            .OrderBy(GetOrderIndex)
            .ThenBy(DisplayName, NaturalNameComparer)
            .ToList();
    }

    public static int GetOrderIndex(string folder)
    {
        string? raw = ReadProperty(folder, "OrderIndex");
        return int.TryParse(raw, out int order) ? order : int.MaxValue;
    }

    public static void SetOrderIndex(string folder, int orderIndex) =>
        SetProperty(folder, "OrderIndex", orderIndex.ToString());

    public static string RenameNode(string folder, string requestedName)
    {
        string cleanName = SanitizeName(requestedName, 120);
        string parent = Path.GetDirectoryName(folder)
            ?? throw new InvalidOperationException("Cannot rename a root folder.");
        string target = Path.Combine(parent, cleanName);

        if (!string.Equals(folder, target, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(target))
        {
            throw new IOException($"'{cleanName}' already exists in this folder.");
        }

        if (!string.Equals(folder, target, StringComparison.OrdinalIgnoreCase))
            Directory.Move(folder, target);

        UpdateItemName(target, cleanName);
        return target;
    }

    public static string RenamePageAllowDuplicateName(string folder, string requestedName)
    {
        string cleanName = SanitizeName(requestedName, 120);
        string parent = Path.GetDirectoryName(folder)
            ?? throw new InvalidOperationException("Cannot rename a root folder.");
        string desiredTarget = Path.Combine(parent, cleanName);
        string target = string.Equals(folder, desiredTarget, StringComparison.OrdinalIgnoreCase)
            ? folder
            : UniqueDirectoryPath(desiredTarget);

        if (!string.Equals(folder, target, StringComparison.OrdinalIgnoreCase))
            Directory.Move(folder, target);

        UpdateItemName(target, cleanName);
        return target;
    }

    public static string CopyNode(string sourcePath, string targetFolder)
    {
        string displayName = DisplayName(sourcePath);
        string copyName = UniqueCopyDisplayName(targetFolder, displayName);
        string destPath = Path.Combine(targetFolder, SanitizeName(copyName, 120));
        var pageSources = CollectPageSources(sourcePath);

        CopyDirectory(sourcePath, destPath);
        RewritePageSources(destPath, pageSources);
        RegenerateGuidsRecursively(destPath);
        RegenerateMeasurementIdsRecursively(destPath);
        UpdateItemName(destPath, copyName);
        SetOrderIndex(destPath, GetNextOrderIndex(targetFolder));
        return destPath;
    }

    public static string MoveNode(string sourcePath, string targetFolder)
    {
        string sourceParent = Path.GetDirectoryName(sourcePath) ?? "";
        if (string.Equals(sourceParent, targetFolder, StringComparison.OrdinalIgnoreCase))
            return sourcePath;

        string displayName = DisplayName(sourcePath);
        string targetName = Directory.Exists(Path.Combine(targetFolder, SanitizeName(displayName, 120)))
            ? UniqueCopyDisplayName(targetFolder, displayName)
            : displayName;
        string destPath = Path.Combine(targetFolder, SanitizeName(targetName, 120));
        var pageSources = CollectPageSources(sourcePath);

        Directory.Move(sourcePath, destPath);
        RewritePageSources(destPath, pageSources);
        if (!string.Equals(displayName, targetName, StringComparison.Ordinal))
            UpdateItemName(destPath, targetName);
        SetOrderIndex(destPath, GetNextOrderIndex(targetFolder));
        NormalizeOrder(sourceParent);
        return destPath;
    }

    public static string DuplicatePage(string pageFolder)
    {
        if (!IsPageFolder(pageFolder))
            throw new InvalidOperationException("Only page nodes can be duplicated.");
        string parent = Path.GetDirectoryName(pageFolder)
            ?? throw new InvalidOperationException("Cannot duplicate this page.");
        return CopyNode(pageFolder, parent);
    }

    public static bool MoveSibling(string folder, int offset)
    {
        string parent = Path.GetDirectoryName(folder) ?? "";
        var siblings = GetOrderedChildDirectories(parent).ToList();
        int index = siblings.FindIndex(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase));
        int target = index + offset;
        if (index < 0 || target < 0 || target >= siblings.Count) return false;

        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);
        ApplySiblingOrder(siblings);
        return true;
    }

    public static bool CanMoveSiblings(IEnumerable<string> folders, int offset) =>
        TryBuildSiblingMove(folders, offset, out _);

    public static bool MoveSiblings(IEnumerable<string> folders, int offset)
    {
        if (!TryBuildSiblingMove(folders, offset, out var siblings))
            return false;

        ApplySiblingOrder(siblings);
        return true;
    }

    public static bool CanMoveSiblingsToPosition(IEnumerable<string> folders, string targetFolder, bool after) =>
        TryBuildSiblingPositionMove(folders, targetFolder, after, out _);

    public static bool MoveSiblingsToPosition(IEnumerable<string> folders, string targetFolder, bool after)
    {
        if (!TryBuildSiblingPositionMove(folders, targetFolder, after, out var siblings))
            return false;

        ApplySiblingOrder(siblings);
        return true;
    }

    public static void SortChildren(string parentFolder, bool descending)
    {
        var children = Directory.EnumerateDirectories(parentFolder)
            .OrderBy(DisplayName, NaturalNameComparer)
            .ToList();
        if (descending) children.Reverse();
        ApplySiblingOrder(children);
    }

    public static void NormalizeOrder(string parentFolder)
    {
        if (!Directory.Exists(parentFolder)) return;
        ApplySiblingOrder(GetOrderedChildDirectories(parentFolder));
    }

    public static string? ReadName(string folder)
    {
        XElement? root = ReadDataRoot(folder);
        return root?.Attribute("Name")?.Value;
    }

    public static string? ReadClass(string folder)
    {
        XElement? root = ReadDataRoot(folder);
        return root?.Attribute("Class")?.Value;
    }

    public static string SanitizeName(string name, int maxLength)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        char[] chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        string cleaned = new string(chars).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Untitled";
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength].TrimEnd();
    }

    public static bool IsSameOrDescendant(string possibleParent, string possibleChild)
    {
        string parent = FullPathWithSeparator(possibleParent);
        string child = FullPathWithSeparator(possibleChild);
        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeMeasurementType(string value)
    {
        string clean = (value ?? "").Trim().ToLowerInvariant();
        return clean switch
        {
            "point" or "count" or "counts" or "ea" or "each" => "point",
            "area" or "sf" or "sqft" or "square" => "area",
            "line" or "linear" or "lf" or "ft" => "line",
            _ => "line",
        };
    }

    public static void SavePageLayerCache(string pageFolder, IReadOnlyList<PdfLayerInfo> pdfLayers)
    {
        SourceInfo? src = ReadSource(pageFolder);
        if (src == null) return;

        string pdfAbs = Path.GetFullPath(Path.Combine(pageFolder, src.Pdf));
        WriteSource(
            pageFolder,
            pdfAbs,
            src.Page,
            src.ScaleMetersPerPt,
            pdfLayers,
            pdfLayersCached: true,
            src.LegendTakeoffOrder,
            src.OverlayPageFolder,
            src.OverlayColor,
            src.OverlayOpacity,
            src.OverlayOffsetXPt,
            src.OverlayOffsetYPt,
            src.OverlayScale);
    }

    public static string PageLayersJsonPath(string pageFolder) =>
        Path.Combine(pageFolder, "layers.json");

    public static string SourcePdfMetadataPath(string pageFolder) =>
        Path.Combine(pageFolder, "source_pdf.json");

    public static PdfSheetMetadata? ReadSourcePdfMetadata(string pageFolder)
    {
        string path = SourcePdfMetadataPath(pageFolder);
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<PdfSheetMetadata>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            QuarantineCorruptJson(path, "ReadSourcePdfMetadata", ex);
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"ReadSourcePdfMetadata failed for {path}");
            return null;
        }
    }

    public static void WriteSourcePdfMetadata(string pageFolder, PdfSheetMetadata metadata)
    {
        Directory.CreateDirectory(pageFolder);
        try
        {
            IoUtil.WriteAllTextAtomic(
                SourcePdfMetadataPath(pageFolder),
                JsonSerializer.Serialize(metadata, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(SourcePdfMetadataPath(pageFolder))}': {ex.Message}", ex);
        }
    }

    public static PageLayerManifest? ReadPageLayerManifest(string pageFolder)
    {
        string path = PageLayersJsonPath(pageFolder);
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<PageLayerManifest>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            QuarantineCorruptJson(path, "ReadPageLayerManifest", ex);
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"ReadPageLayerManifest failed for {path}");
            return null;
        }
    }

    private static void WriteSource(
        string pageFolder,
        string pdfAbsPath,
        int pageIndex,
        double scaleMetersPerPt,
        IReadOnlyList<PdfLayerInfo>? pdfLayers = null,
        bool pdfLayersCached = false,
        IReadOnlyList<string>? legendTakeoffOrder = null,
        string overlayPageFolder = "",
        string overlayColor = "#E53935",
        double overlayOpacity = 0.55,
        double overlayOffsetXPt = 0,
        double overlayOffsetYPt = 0,
        double overlayScale = 1.0)
    {
        var src = new SourceInfo
        {
            Pdf = Path.GetRelativePath(pageFolder, pdfAbsPath),
            Page = pageIndex,
            ScaleMetersPerPt = scaleMetersPerPt,
            PdfLayersCached = pdfLayersCached,
            PdfLayers = pdfLayers?.ToList() ?? [],
            LegendTakeoffOrder = legendTakeoffOrder?
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [],
            OverlayPageFolder = MakeRelativePageReference(pageFolder, overlayPageFolder),
            OverlayColor = string.IsNullOrWhiteSpace(overlayColor) ? "#E53935" : overlayColor,
            OverlayOpacity = NormalizeOverlayOpacity(overlayOpacity),
            OverlayOffsetXPt = NormalizeOverlayOffset(overlayOffsetXPt),
            OverlayOffsetYPt = NormalizeOverlayOffset(overlayOffsetYPt),
            OverlayScale = NormalizeOverlayScale(overlayScale),
        };
        try
        {
            IoUtil.WriteAllTextAtomic(
                Path.Combine(pageFolder, "source.json"),
                JsonSerializer.Serialize(src, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(Path.Combine(pageFolder, "source.json"))}': {ex.Message}", ex);
        }
        WritePageLayerManifest(pageFolder, src);
    }

    private static void WritePageLayerManifest(string pageFolder, SourceInfo src)
    {
        string path = PageLayersJsonPath(pageFolder);
        if (src.PdfLayers.Count == 0)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        var manifest = new PageLayerManifest
        {
            SourcePdf = src.Pdf,
            Page = src.Page,
            PageNumber = src.Page + 1,
            GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            LayerCount = src.PdfLayers.Count,
            Layers = src.PdfLayers
                .OrderBy(layer => layer.Number)
                .Select(layer => new PdfLayerInfo
                {
                    Number = layer.Number,
                    Name = layer.Name,
                    IsOn = layer.IsOn,
                })
                .ToList(),
        };

        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(manifest, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    private static void RewritePageSources(string newRoot, IReadOnlyList<PageSourceSnapshot> snapshots)
    {
        foreach (var snap in snapshots)
        {
            string targetFolder = snap.RelativeFolder == "."
                ? newRoot
                : Path.Combine(newRoot, snap.RelativeFolder);
            if (Directory.Exists(targetFolder))
                WriteSource(
                    targetFolder,
                    snap.PdfAbsPath,
                    snap.Page,
                    snap.ScaleMetersPerPt,
                    snap.PdfLayers,
                    snap.PdfLayersCached,
                    overlayPageFolder: snap.OverlayPageFolder,
                    overlayColor: snap.OverlayColor,
                    overlayOpacity: snap.OverlayOpacity,
                    overlayOffsetXPt: snap.OverlayOffsetXPt,
                    overlayOffsetYPt: snap.OverlayOffsetYPt,
                    overlayScale: snap.OverlayScale);
        }
    }

    private static List<PageSourceSnapshot> CollectPageSources(string rootFolder)
    {
        var snapshots = new List<PageSourceSnapshot>();
        if (!Directory.Exists(rootFolder)) return snapshots;

        foreach (string dir in EnumerateSelfAndDescendants(rootFolder))
        {
            SourceInfo? src = ReadSource(dir);
            if (src == null) continue;

            string rel = Path.GetRelativePath(rootFolder, dir);
            string pdfAbs = Path.GetFullPath(Path.Combine(dir, src.Pdf));
            snapshots.Add(new PageSourceSnapshot(
                rel,
                pdfAbs,
                src.Page,
                src.ScaleMetersPerPt,
                src.PdfLayersCached,
                src.PdfLayers,
                ResolveRelativePagePath(dir, src.OverlayPageFolder),
                src.OverlayColor,
                src.OverlayOpacity,
                src.OverlayOffsetXPt,
                src.OverlayOffsetYPt,
                src.OverlayScale));
        }

        return snapshots;
    }

    private static void WriteItemDataXml(string folder, string itemClass, string name, int orderIndex)
    {
        string guid = Guid.NewGuid().ToString().ToUpperInvariant();
        var root = new XElement("Item",
            new XAttribute("Class", itemClass),
            new XAttribute("Name", name),
            new XAttribute("GUID", guid),
            new XElement("Properties",
                new XElement("Property", new XAttribute("Name", "OrderIndex"), new XAttribute("Value", orderIndex)),
                new XElement("Property", new XAttribute("Name", "Name"), new XAttribute("Value", name)),
                new XElement("Property", new XAttribute("Name", "Type"), new XAttribute("Value", itemClass)),
                new XElement("Property", new XAttribute("Name", "GUID"), new XAttribute("Value", guid))));

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        doc.Save(Path.Combine(folder, "Data.xml"));
    }

    private static int GetNextOrderIndex(string parentFolder)
    {
        if (!Directory.Exists(parentFolder)) return 1;

        int max = 0;
        foreach (string dir in Directory.EnumerateDirectories(parentFolder))
        {
            string? raw = ReadProperty(dir, "OrderIndex");
            if (int.TryParse(raw, out int order))
                max = Math.Max(max, order);
        }
        return max + 1;
    }

    private static bool TryBuildSiblingMove(IEnumerable<string> folders, int offset, out List<string> siblings)
    {
        siblings = [];
        if (offset == 0)
            return false;

        var selected = folders
            .Where(Directory.Exists)
            .Select(NormalizeFolderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selected.Count == 0)
            return false;

        string parent = Path.GetDirectoryName(selected[0]) ?? "";
        if (string.IsNullOrWhiteSpace(parent) ||
            selected.Any(path => !string.Equals(Path.GetDirectoryName(path) ?? "", parent, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var orderedSiblings = GetOrderedChildDirectories(parent)
            .Select(NormalizeFolderPath)
            .ToList();
        siblings = orderedSiblings;
        if (siblings.Count <= 1 || selected.Count >= siblings.Count)
            return false;

        var selectedSet = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedSet.Any(path => !orderedSiblings.Contains(path, StringComparer.OrdinalIgnoreCase)))
            return false;

        bool moved = false;
        if (offset < 0)
        {
            for (int i = 1; i < siblings.Count; i++)
            {
                if (selectedSet.Contains(siblings[i]) && !selectedSet.Contains(siblings[i - 1]))
                {
                    (siblings[i - 1], siblings[i]) = (siblings[i], siblings[i - 1]);
                    moved = true;
                }
            }
        }
        else
        {
            for (int i = siblings.Count - 2; i >= 0; i--)
            {
                if (selectedSet.Contains(siblings[i]) && !selectedSet.Contains(siblings[i + 1]))
                {
                    (siblings[i], siblings[i + 1]) = (siblings[i + 1], siblings[i]);
                    moved = true;
                }
            }
        }

        return moved;
    }

    private static bool TryBuildSiblingPositionMove(
        IEnumerable<string> folders,
        string targetFolder,
        bool after,
        out List<string> siblings)
    {
        siblings = [];

        var selected = folders
            .Where(Directory.Exists)
            .Select(NormalizeFolderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selected.Count == 0 || !Directory.Exists(targetFolder))
            return false;

        string target = NormalizeFolderPath(targetFolder);
        string parent = Path.GetDirectoryName(selected[0]) ?? "";
        string targetParent = Path.GetDirectoryName(target) ?? "";
        if (string.IsNullOrWhiteSpace(parent) ||
            !string.Equals(parent, targetParent, StringComparison.OrdinalIgnoreCase) ||
            selected.Any(path => !string.Equals(Path.GetDirectoryName(path) ?? "", parent, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var orderedSiblings = GetOrderedChildDirectories(parent)
            .Select(NormalizeFolderPath)
            .ToList();
        var selectedSet = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedSet.Contains(target) ||
            selectedSet.Any(path => !orderedSiblings.Contains(path, StringComparer.OrdinalIgnoreCase)) ||
            !orderedSiblings.Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var moving = orderedSiblings
            .Where(path => selectedSet.Contains(path))
            .ToList();
        var remaining = orderedSiblings
            .Where(path => !selectedSet.Contains(path))
            .ToList();
        int targetIndex = remaining.FindIndex(path => string.Equals(path, target, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0)
            return false;

        int insertIndex = after ? targetIndex + 1 : targetIndex;
        remaining.InsertRange(insertIndex, moving);
        siblings = remaining;
        return !orderedSiblings.SequenceEqual(siblings, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeFolderPath(string folder) =>
        Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void ApplySiblingOrder(IEnumerable<string> orderedFolders)
    {
        int order = 1;
        foreach (string folder in orderedFolders)
            SetOrderIndex(folder, order++);
    }

    private static void UpdateItemName(string folder, string name)
    {
        string path = Path.Combine(folder, "Data.xml");
        if (!File.Exists(path)) return;

        XDocument doc = XDocument.Load(path);
        XElement? root = doc.Root;
        if (root == null) return;

        root.SetAttributeValue("Name", name);
        SetProperty(root, "Name", name);
        doc.Save(path);
    }

    private static void RegenerateGuidsRecursively(string folder)
    {
        foreach (string dir in EnumerateSelfAndDescendants(folder))
            RegenerateDataXmlGuid(dir);
    }

    private static void RegenerateMeasurementIdsRecursively(string folder)
    {
        foreach (string dir in EnumerateSelfAndDescendants(folder))
        {
            string path = MeasurementsJsonPath(dir);
            if (!File.Exists(path)) continue;

            try
            {
                var measurements = LoadMeasurements(dir);
                foreach (var measurement in measurements)
                    measurement.Id = Guid.NewGuid().ToString();
                SaveMeasurements(dir, measurements);
            }
            catch
            {
                // Leave the copied file readable even if legacy measurement JSON is malformed.
            }
        }
    }

    private static void RegenerateDataXmlGuid(string folder)
    {
        string path = Path.Combine(folder, "Data.xml");
        if (!File.Exists(path)) return;

        XDocument doc = XDocument.Load(path);
        XElement? root = doc.Root;
        if (root == null) return;

        string guid = Guid.NewGuid().ToString().ToUpperInvariant();
        root.SetAttributeValue("GUID", guid);
        SetProperty(root, "GUID", guid);
        doc.Save(path);
    }

    private static void SetProperty(string folder, string propertyName, string value)
    {
        string path = Path.Combine(folder, "Data.xml");
        if (!File.Exists(path)) return;

        XDocument doc = XDocument.Load(path);
        XElement? root = doc.Root;
        if (root == null) return;

        SetProperty(root, propertyName, value);
        doc.Save(path);
    }

    private static void SetProperty(XElement root, string propertyName, string value)
    {
        XElement props = root.Element("Properties") ?? new XElement("Properties");
        if (props.Parent == null)
            root.Add(props);

        XElement? prop = props.Elements("Property")
            .FirstOrDefault(p => p.Attribute("Name")?.Value == propertyName);
        if (prop == null)
        {
            prop = new XElement("Property", new XAttribute("Name", propertyName));
            props.Add(prop);
        }

        prop.SetAttributeValue("Value", value);
    }

    private static string? ReadProperty(string folder, string propertyName)
    {
        XElement? root = ReadDataRoot(folder);
        return root?
            .Element("Properties")?
            .Elements("Property")
            .FirstOrDefault(p => p.Attribute("Name")?.Value == propertyName)?
            .Attribute("Value")?
            .Value;
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

    private static XElement? ReadDataRoot(string folder)
    {
        string path = Path.Combine(folder, "Data.xml");
        if (!File.Exists(path)) return null;

        try
        {
            return XDocument.Load(path).Root;
        }
        catch
        {
            return null;
        }
    }

    private static string UniqueDirectoryPath(string desiredPath)
    {
        if (!Directory.Exists(desiredPath)) return desiredPath;

        for (int i = 2; ; i++)
        {
            string candidate = $"{desiredPath} ({i})";
            if (!Directory.Exists(candidate)) return candidate;
        }
    }

    private static string UniqueFilePath(string desiredPath)
    {
        if (!File.Exists(desiredPath)) return desiredPath;

        string dir = Path.GetDirectoryName(desiredPath) ?? "";
        string name = Path.GetFileNameWithoutExtension(desiredPath);
        string ext = Path.GetExtension(desiredPath);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static string UniqueCopyDisplayName(string targetFolder, string displayName)
    {
        string first = $"{displayName} - Copy";
        if (!Directory.Exists(Path.Combine(targetFolder, SanitizeName(first, 120))))
            return first;

        for (int i = 2; ; i++)
        {
            string candidate = $"{displayName} - Copy {i}";
            if (!Directory.Exists(Path.Combine(targetFolder, SanitizeName(candidate, 120))))
                return candidate;
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (string file in Directory.EnumerateFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: false);
        }

        foreach (string childDir in Directory.EnumerateDirectories(sourceDir))
        {
            string destChild = Path.Combine(destDir, Path.GetFileName(childDir));
            CopyDirectory(childDir, destChild);
        }
    }

    private static IEnumerable<string> EnumerateSelfAndDescendants(string rootFolder)
    {
        yield return rootFolder;
        foreach (string dir in Directory.EnumerateDirectories(rootFolder, "*", SearchOption.AllDirectories))
            yield return dir;
    }

    private static string FullPathWithSeparator(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full + Path.DirectorySeparatorChar;
    }

    private static void QuarantineCorruptJson(string path, string context, Exception exception)
    {
        AppLog.Error(exception, $"{context} failed for {path}");
        string targetPath = "";
        try
        {
            if (File.Exists(path))
            {
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                targetPath = UniqueCorruptJsonPath($"{path}.corrupt-{timestamp}");
                File.Move(path, targetPath);
            }
        }
        catch (Exception moveException)
        {
            AppLog.Warn(moveException, $"Failed to quarantine corrupt JSON {path}");
        }

        lock (CorruptJsonLock)
        {
            CorruptJsonFiles.Add(string.IsNullOrWhiteSpace(targetPath)
                ? path
                : $"{path} -> {targetPath}");
        }
    }

    private static string UniqueCorruptJsonPath(string desiredPath)
    {
        if (!File.Exists(desiredPath))
            return desiredPath;

        for (int i = 2; ; i++)
        {
            string candidate = $"{desiredPath}-{i}";
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private sealed record PageSourceSnapshot(
        string RelativeFolder,
        string PdfAbsPath,
        int Page,
        double ScaleMetersPerPt,
        bool PdfLayersCached,
        IReadOnlyList<PdfLayerInfo> PdfLayers,
        string OverlayPageFolder,
        string OverlayColor,
        double OverlayOpacity,
        double OverlayOffsetXPt,
        double OverlayOffsetYPt,
        double OverlayScale);

    private sealed class NaturalStringComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int ix = 0;
            int iy = 0;
            while (ix < x.Length && iy < y.Length)
            {
                char cx = x[ix];
                char cy = y[iy];
                if (char.IsDigit(cx) && char.IsDigit(cy))
                {
                    int nx = ReadNumberToken(x, ref ix);
                    int ny = ReadNumberToken(y, ref iy);
                    int numberCompare = nx.CompareTo(ny);
                    if (numberCompare != 0)
                        return numberCompare;
                    continue;
                }

                int charCompare = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
                if (charCompare != 0)
                    return charCompare;
                ix++;
                iy++;
            }

            return x.Length.CompareTo(y.Length);
        }

        private static int ReadNumberToken(string text, ref int index)
        {
            int start = index;
            while (index < text.Length && char.IsDigit(text[index]))
                index++;

            string digits = text[start..index].TrimStart('0');
            if (digits.Length == 0) return 0;
            return int.TryParse(digits, out int value) ? value : int.MaxValue;
        }
    }
}
