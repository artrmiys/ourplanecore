using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using SkiaSharp;

namespace SmartTakeoffs;

public sealed class SmartTakeoffsJob
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

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FF4444";

    [JsonPropertyName("page_folder")]
    public string PageFolder { get; set; } = "";

    [JsonPropertyName("scale_m_per_pt")]
    public double ScaleMetersPerPt { get; set; }
}

internal sealed record PointDto(float X, float Y);

public static class SmartTakeoffsJobStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
    private static readonly IComparer<string> NaturalNameComparer = new NaturalStringComparer();

    public static SmartTakeoffsJob CreateJob(string parentDir, string jobName)
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

    public static SmartTakeoffsJob LoadJob(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException(rootPath);

        if (!File.Exists(Path.Combine(rootPath, "Data.xml")))
            WriteItemDataXml(rootPath, "Folder", Path.GetFileName(rootPath), 0);

        string name = ReadName(rootPath) ?? Path.GetFileName(rootPath);
        var job = new SmartTakeoffsJob { Name = name, RootPath = rootPath };

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

    public static string DefaultImportFolder(SmartTakeoffsJob job)
    {
        string imported = EnsureFolder(job.PagesRoot, "00. imported");
        return EnsureFolder(imported, "Arch");
    }

    public static IReadOnlyList<PageInfo> ImportPdf(
        SmartTakeoffsJob job,
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

    public static SourceInfo? ReadSource(string pageFolder)
    {
        string path = Path.Combine(pageFolder, "source.json");
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<SourceInfo>(File.ReadAllText(path));
        }
        catch
        {
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
        };
    }

    public static void SavePageScale(string pageFolder, double scaleMetersPerPt)
    {
        SourceInfo? src = ReadSource(pageFolder);
        if (src == null) return;

        string pdfAbs = Path.GetFullPath(Path.Combine(pageFolder, src.Pdf));
        WriteSource(pageFolder, pdfAbs, src.Page, scaleMetersPerPt, src.PdfLayers, src.PdfLayersCached);
    }

    public static string CreateTakeoffFolder(SmartTakeoffsJob job, string parentFolder, string name)
    {
        string targetParent = IsSameOrDescendant(job.TakeoffsRoot, parentFolder)
            ? parentFolder
            : job.TakeoffsRoot;
        string folder = CreateFolder(targetParent, name);
        SetProperty(folder, "SmartNodeKind", "folder");
        return folder;
    }

    public static TakeoffItem CreateTakeoffItem(SmartTakeoffsJob job, string name, string color) =>
        CreateTakeoffItem(job, job.TakeoffsRoot, name, color, "line");

    public static TakeoffItem CreateTakeoffItem(SmartTakeoffsJob job, string parentFolder, string name, string color, string measurementType)
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

    public static IReadOnlyList<TakeoffItem> LoadTakeoffItems(SmartTakeoffsJob job)
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
        };
        item.Measurements.AddRange(measurements);
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
        SetProperty(item.FolderPath, "MeasurementCount", item.Measurements.Count.ToString());
        SetProperty(item.FolderPath, "MeasuredPageCount", item.Measurements
            .Select(m => m.PageFolder)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()
            .ToString());
        SaveMeasurements(item.FolderPath, item.Measurements);
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
                    Points = dto.PointsPdf.Select(p => new SKPoint(p.X, p.Y)).ToList(),
                };
            }).ToList();
        }
        catch
        {
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
            PointsPdf = m.Points.Select(p => new PointDto(p.X, p.Y)).ToList(),
        }).ToList();

        try
        {
            File.WriteAllText(
                MeasurementsJsonPath(takeoffFolder),
                JsonSerializer.Serialize(dtos, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(MeasurementsJsonPath(takeoffFolder))}': {ex.Message}", ex);
        }
    }

    public static bool IsPageFolder(string folder) =>
        File.Exists(Path.Combine(folder, "source.json"));

    public static string DisplayName(string folder) =>
        ReadName(folder) ?? Path.GetFileName(folder);

    private static string MeasurementsJsonPath(string takeoffFolder) =>
        Path.Combine(takeoffFolder, "measurements.json");

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

    public static string NormalizeMeasurementType(string value) =>
        value is "point" or "area" ? value : "line";

    public static void SavePageLayerCache(string pageFolder, IReadOnlyList<PdfLayerInfo> pdfLayers)
    {
        SourceInfo? src = ReadSource(pageFolder);
        if (src == null) return;

        string pdfAbs = Path.GetFullPath(Path.Combine(pageFolder, src.Pdf));
        WriteSource(pageFolder, pdfAbs, src.Page, src.ScaleMetersPerPt, pdfLayers, pdfLayersCached: true);
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
        catch
        {
            return null;
        }
    }

    public static void WriteSourcePdfMetadata(string pageFolder, PdfSheetMetadata metadata)
    {
        Directory.CreateDirectory(pageFolder);
        try
        {
            File.WriteAllText(
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
        catch
        {
            return null;
        }
    }

    private static void WriteSource(
        string pageFolder,
        string pdfAbsPath,
        int pageIndex,
        double scaleMetersPerPt,
        IReadOnlyList<PdfLayerInfo>? pdfLayers = null,
        bool pdfLayersCached = false)
    {
        var src = new SourceInfo
        {
            Pdf = Path.GetRelativePath(pageFolder, pdfAbsPath),
            Page = pageIndex,
            ScaleMetersPerPt = scaleMetersPerPt,
            PdfLayersCached = pdfLayersCached,
            PdfLayers = pdfLayers?.ToList() ?? [],
        };
        try
        {
            File.WriteAllText(
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
            File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
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
                    snap.PdfLayersCached);
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
                src.PdfLayers));
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

    private sealed record PageSourceSnapshot(
        string RelativeFolder,
        string PdfAbsPath,
        int Page,
        double ScaleMetersPerPt,
        bool PdfLayersCached,
        IReadOnlyList<PdfLayerInfo> PdfLayers);

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
