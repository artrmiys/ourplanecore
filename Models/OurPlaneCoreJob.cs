using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

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
    internal static readonly JsonSerializerOptions JsonOptions = new()
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

    public static OurPlaneCoreJob CreateJob(string parentDir, string jobName) =>
        JobLayout.CreateJob(parentDir, jobName);

    public static OurPlaneCoreJob LoadJob(string rootPath) =>
        JobLayout.LoadJob(rootPath);

    public static string EnsureFolder(string parentFolder, string name) =>
        JobLayout.EnsureFolder(parentFolder, name);

    public static string CreateFolder(string parentFolder, string name) =>
        JobLayout.CreateFolder(parentFolder, name);

    public static string DefaultImportFolder(OurPlaneCoreJob job) =>
        JobLayout.DefaultImportFolder(job);

    public static IReadOnlyList<PageInfo> ImportPdf(
        OurPlaneCoreJob job,
        string pdfSourcePath,
        IReadOnlyList<string> pageNames,
        string destinationFolder,
        IReadOnlyDictionary<int, IReadOnlyList<PdfLayerInfo>>? pdfLayerCache = null) =>
        PageStore.ImportPdf(job, pdfSourcePath, pageNames, destinationFolder, pdfLayerCache);

    public static PageInfo CreatePageFromPdf(
        OurPlaneCoreJob job,
        string pdfSourcePath,
        string displayName,
        string destinationFolder,
        int pdfPage = 0,
        double scaleMetersPerPt = 0) =>
        PageStore.CreatePageFromPdf(job, pdfSourcePath, displayName, destinationFolder, pdfPage, scaleMetersPerPt);

    public static SourceInfo? ReadSource(string pageFolder) =>
        PageStore.ReadSource(pageFolder);

    public static PageInfo? TryReadPage(string pageFolder) =>
        PageStore.TryReadPage(pageFolder);

    public static void SavePageScale(string pageFolder, double scaleMetersPerPt) =>
        PageStore.SavePageScale(pageFolder, scaleMetersPerPt);

    public static void SavePageLegendTakeoffOrder(string pageFolder, IReadOnlyList<string> legendTakeoffOrder) =>
        PageStore.SavePageLegendTakeoffOrder(pageFolder, legendTakeoffOrder);

    public static void SavePageOverlay(
        string pageFolder,
        string overlayPageFolder,
        string overlayColor,
        double overlayOpacity) =>
        PageStore.SavePageOverlay(pageFolder, overlayPageFolder, overlayColor, overlayOpacity);

    public static void ClearPageOverlay(string pageFolder) =>
        PageStore.ClearPageOverlay(pageFolder);

    public static void SavePageOverlayTransform(
        string pageFolder,
        double offsetXPt,
        double offsetYPt,
        double overlayScale) =>
        PageStore.SavePageOverlayTransform(pageFolder, offsetXPt, offsetYPt, overlayScale);

    public static string CreateTakeoffFolder(OurPlaneCoreJob job, string parentFolder, string name) =>
        TakeoffStore.CreateTakeoffFolder(job, parentFolder, name);

    public static TakeoffItem CreateTakeoffItem(OurPlaneCoreJob job, string name, string color) =>
        TakeoffStore.CreateTakeoffItem(job, name, color);

    public static TakeoffItem CreateTakeoffItem(OurPlaneCoreJob job, string parentFolder, string name, string color, string measurementType) =>
        TakeoffStore.CreateTakeoffItem(job, parentFolder, name, color, measurementType);

    public static IReadOnlyList<TakeoffItem> LoadTakeoffItems(OurPlaneCoreJob job) =>
        TakeoffStore.LoadTakeoffItems(job);

    public static TakeoffItem? TryReadTakeoffItem(string folder) =>
        TakeoffStore.TryReadTakeoffItem(folder);

    public static void SaveTakeoffItem(TakeoffItem item) =>
        TakeoffStore.SaveTakeoffItem(item);

    public static void ApplyTakeoffPropertiesToMeasurements(TakeoffItem item) =>
        TakeoffStore.ApplyTakeoffPropertiesToMeasurements(item);

    public static List<Measurement> LoadMeasurements(string takeoffFolder) =>
        TakeoffStore.LoadMeasurements(takeoffFolder);

    public static void SaveMeasurements(string takeoffFolder, IEnumerable<Measurement> measurements) =>
        TakeoffStore.SaveMeasurements(takeoffFolder, measurements);

    public static List<PageAnnotation> LoadPageAnnotations(string pageFolder) =>
        PageAnnotationStore.LoadPageAnnotations(pageFolder);

    public static void SavePageAnnotations(string pageFolder, IEnumerable<PageAnnotation> annotations) =>
        PageAnnotationStore.SavePageAnnotations(pageFolder, annotations);

    public static bool IsPageFolder(string folder) =>
        File.Exists(Path.Combine(folder, "source.json"));

    public static string DisplayName(string folder) =>
        ReadName(folder) ?? Path.GetFileName(folder);

    public static string PageAnnotationsJsonPath(string pageFolder) =>
        PageAnnotationStore.PageAnnotationsJsonPath(pageFolder);

    public static string NormalizePageAnnotationKind(string value) =>
        PageAnnotationStore.NormalizePageAnnotationKind(value);

    public static bool IsTakeoffItemFolder(string folder) =>
        TakeoffStore.IsTakeoffItemFolder(folder);

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
        var pageSources = PageStore.CollectPageSources(sourcePath);

        CopyDirectory(sourcePath, destPath);
        PageStore.RewritePageSources(destPath, pageSources);
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
        var pageSources = PageStore.CollectPageSources(sourcePath);

        Directory.Move(sourcePath, destPath);
        PageStore.RewritePageSources(destPath, pageSources);
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

    public static void SavePageLayerCache(string pageFolder, IReadOnlyList<PdfLayerInfo> pdfLayers) =>
        PageStore.SavePageLayerCache(pageFolder, pdfLayers);

    public static string PageLayersJsonPath(string pageFolder) =>
        PageStore.PageLayersJsonPath(pageFolder);

    public static string SourcePdfMetadataPath(string pageFolder) =>
        PageStore.SourcePdfMetadataPath(pageFolder);

    public static PdfSheetMetadata? ReadSourcePdfMetadata(string pageFolder) =>
        PageStore.ReadSourcePdfMetadata(pageFolder);

    public static void WriteSourcePdfMetadata(string pageFolder, PdfSheetMetadata metadata) =>
        PageStore.WriteSourcePdfMetadata(pageFolder, metadata);

    public static PageLayerManifest? ReadPageLayerManifest(string pageFolder) =>
        PageStore.ReadPageLayerManifest(pageFolder);

    internal static void WriteItemDataXml(string folder, string itemClass, string name, int orderIndex)
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

    internal static int GetNextOrderIndex(string parentFolder)
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

    internal static void UpdateItemName(string folder, string name)
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
            string path = TakeoffStore.MeasurementsJsonPath(dir);
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

    internal static void SetProperty(string folder, string propertyName, string value)
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

    internal static string? ReadProperty(string folder, string propertyName)
    {
        XElement? root = ReadDataRoot(folder);
        return root?
            .Element("Properties")?
            .Elements("Property")
            .FirstOrDefault(p => p.Attribute("Name")?.Value == propertyName)?
            .Attribute("Value")?
            .Value;
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

    internal static string UniqueDirectoryPath(string desiredPath)
    {
        if (!Directory.Exists(desiredPath)) return desiredPath;

        for (int i = 2; ; i++)
        {
            string candidate = $"{desiredPath} ({i})";
            if (!Directory.Exists(candidate)) return candidate;
        }
    }

    internal static string UniqueFilePath(string desiredPath)
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

    internal static IEnumerable<string> EnumerateSelfAndDescendants(string rootFolder)
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

    internal static void QuarantineCorruptJson(string path, string context, Exception exception)
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
