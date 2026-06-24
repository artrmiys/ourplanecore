using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OurPlaneCore;

internal static class PageStore
{
    public static IReadOnlyList<PageInfo> ImportPdf(
        OurPlaneCoreJob job,
        string pdfSourcePath,
        IReadOnlyList<string> pageNames,
        string destinationFolder,
        IReadOnlyDictionary<int, IReadOnlyList<PdfLayerInfo>>? pdfLayerCache = null)
    {
        string sourcesDir = JobLayout.EnsureFolder(job.RootPath, "sources");
        string pdfDest = OurPlaneCoreJobStore.UniqueFilePath(Path.Combine(sourcesDir, Path.GetFileName(pdfSourcePath)));
        if (!File.Exists(pdfDest))
            File.Copy(pdfSourcePath, pdfDest);

        var created = new List<PageInfo>();
        for (int i = 0; i < pageNames.Count; i++)
        {
            string displayName = string.IsNullOrWhiteSpace(pageNames[i])
                ? $"Page {i + 1}"
                : pageNames[i].Trim();
            string pageFolder = OurPlaneCoreJobStore.UniqueDirectoryPath(Path.Combine(destinationFolder, OurPlaneCoreJobStore.SanitizeName(displayName, 120)));
            Directory.CreateDirectory(pageFolder);

            OurPlaneCoreJobStore.WriteItemDataXml(pageFolder, "Page", displayName, OurPlaneCoreJobStore.GetNextOrderIndex(destinationFolder));
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
        string sourcesDir = JobLayout.EnsureFolder(job.RootPath, "sources");
        string pdfDest = OurPlaneCoreJobStore.UniqueFilePath(Path.Combine(sourcesDir, Path.GetFileName(pdfSourcePath)));
        if (!File.Exists(pdfDest))
            File.Copy(pdfSourcePath, pdfDest);

        string cleanName = string.IsNullOrWhiteSpace(displayName)
            ? $"Page {pdfPage + 1}"
            : displayName.Trim();
        string pageFolder = OurPlaneCoreJobStore.UniqueDirectoryPath(Path.Combine(destinationFolder, OurPlaneCoreJobStore.SanitizeName(cleanName, 120)));
        Directory.CreateDirectory(pageFolder);

        OurPlaneCoreJobStore.WriteItemDataXml(pageFolder, "Page", cleanName, OurPlaneCoreJobStore.GetNextOrderIndex(destinationFolder));
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

    public static PageInfo CreateBlankPage(
        OurPlaneCoreJob job,
        string displayName,
        string destinationFolder,
        float widthPt = BlankPagePdfService.DefaultWidthPt,
        float heightPt = BlankPagePdfService.DefaultHeightPt)
    {
        string cleanName = string.IsNullOrWhiteSpace(displayName)
            ? "Blank Sheet"
            : displayName.Trim();
        string sourcesDir = JobLayout.EnsureFolder(job.RootPath, "sources");
        string pdfFileName = $"{OurPlaneCoreJobStore.SanitizeName(cleanName, 80)}.blank.pdf";
        string pdfDest = OurPlaneCoreJobStore.UniqueFilePath(Path.Combine(sourcesDir, pdfFileName));
        BlankPagePdfService.WriteBlankPdf(pdfDest, widthPt, heightPt);

        string pageFolder = OurPlaneCoreJobStore.UniqueDirectoryPath(Path.Combine(destinationFolder, OurPlaneCoreJobStore.SanitizeName(cleanName, 120)));
        Directory.CreateDirectory(pageFolder);

        OurPlaneCoreJobStore.WriteItemDataXml(pageFolder, "Page", cleanName, OurPlaneCoreJobStore.GetNextOrderIndex(destinationFolder));
        WriteSource(pageFolder, pdfDest, 0, 0);
        WriteSourcePdfMetadata(pageFolder, new PdfSheetMetadata
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Source = "manual-blank",
            PdfPath = pdfDest,
            PageIndex = 0,
            PageNumber = 1,
            WidthPt = widthPt,
            HeightPt = heightPt,
            SheetLabel = cleanName,
            RenameCandidate = cleanName,
            SkipScale = true,
            Confidence = "manual",
        });

        return new PageInfo
        {
            Name = cleanName,
            FolderPath = pageFolder,
            PdfPath = pdfDest,
            PdfPage = 0,
            ScaleMetersPerPt = 0,
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
            OurPlaneCoreJobStore.QuarantineCorruptJson(path, "ReadSource", ex);
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
        if (src == null && !PageSourceRepair.TryRepairFromMetadata(pageFolder, out src))
            return null;

        string pdfPath = Path.GetFullPath(Path.Combine(pageFolder, src.Pdf));
        return new PageInfo
        {
            Name = OurPlaneCoreJobStore.ReadName(pageFolder) ?? Path.GetFileName(pageFolder),
            FolderPath = pageFolder,
            PdfPath = pdfPath,
            PdfPage = src.Page,
            ScaleMetersPerPt = src.ScaleMetersPerPt,
            PdfLayersCached = src.PdfLayersCached,
            PdfLayers = src.PdfLayers,
            LegendTakeoffOrder = src.LegendTakeoffOrder ?? [],
            LegendTakeoffOrderMode = NormalizeLegendTakeoffOrderMode(src.LegendTakeoffOrderMode),
            TakeoffLayerOrder = PageTakeoffLayerOrderStore.Load(pageFolder).ToList(),
            HiddenTakeoffs = NormalizeStringList(src.HiddenTakeoffs),
            OverlayPageFolder = ResolveRelativePagePath(pageFolder, src.OverlayPageFolder),
            OverlayVisible = src.OverlayVisible,
            OverlayColor = string.IsNullOrWhiteSpace(src.OverlayColor) ? "#E53935" : src.OverlayColor,
            OverlayOpacity = NormalizeOverlayOpacity(src.OverlayOpacity),
            OverlayOffsetXPt = NormalizeOverlayOffset(src.OverlayOffsetXPt),
            OverlayOffsetYPt = NormalizeOverlayOffset(src.OverlayOffsetYPt),
            OverlayScale = NormalizeOverlayScale(src.OverlayScale),
            OverlayRotationDegrees = NormalizeOverlayRotation(src.OverlayRotationDegrees),
            RasterSheet = NormalizeRasterSheet(src.RasterSheet),
        };
    }

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
            src.LegendTakeoffOrderMode,
            src.OverlayPageFolder,
            src.OverlayVisible,
            src.OverlayColor,
            src.OverlayOpacity,
            src.OverlayOffsetXPt,
            src.OverlayOffsetYPt,
            src.OverlayScale,
            src.OverlayRotationDegrees,
            src.HiddenTakeoffs,
            src.RasterSheet);
    }

    public static void SavePageLegendTakeoffOrder(
        string pageFolder,
        IReadOnlyList<string> legendTakeoffOrder,
        string legendTakeoffOrderMode = "")
    {
        SourceInfo? src = ReadSource(pageFolder);
        if (src == null) return;

        string pdfAbs = Path.GetFullPath(Path.Combine(pageFolder, src.Pdf));
        string mode = string.IsNullOrWhiteSpace(legendTakeoffOrderMode)
            ? (legendTakeoffOrder.Count == 0 ? "auto" : "manual")
            : NormalizeLegendTakeoffOrderMode(legendTakeoffOrderMode);
        WriteSource(
            pageFolder,
            pdfAbs,
            src.Page,
            src.ScaleMetersPerPt,
            src.PdfLayers,
            src.PdfLayersCached,
            legendTakeoffOrder,
            mode,
            src.OverlayPageFolder,
            src.OverlayVisible,
            src.OverlayColor,
            src.OverlayOpacity,
            src.OverlayOffsetXPt,
            src.OverlayOffsetYPt,
            src.OverlayScale,
            src.OverlayRotationDegrees,
            src.HiddenTakeoffs,
            src.RasterSheet);
    }

    public static void SavePageHiddenTakeoffs(string pageFolder, IReadOnlyList<string> hiddenTakeoffs)
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
            src.LegendTakeoffOrderMode,
            src.OverlayPageFolder,
            src.OverlayVisible,
            src.OverlayColor,
            src.OverlayOpacity,
            src.OverlayOffsetXPt,
            src.OverlayOffsetYPt,
            src.OverlayScale,
            src.OverlayRotationDegrees,
            hiddenTakeoffs,
            src.RasterSheet);
    }

    public static void ReplacePagePdf(string pageFolder, string pdfAbsPath, int pageIndex = 0)
    {
        SourceInfo? src = ReadSource(pageFolder);
        if (src == null) return;

        WriteSource(
            pageFolder,
            pdfAbsPath,
            pageIndex,
            src.ScaleMetersPerPt,
            pdfLayers: [],
            pdfLayersCached: false,
            src.LegendTakeoffOrder,
            src.LegendTakeoffOrderMode,
            src.OverlayPageFolder,
            src.OverlayVisible,
            src.OverlayColor,
            src.OverlayOpacity,
            src.OverlayOffsetXPt,
            src.OverlayOffsetYPt,
            src.OverlayScale,
            src.OverlayRotationDegrees,
            src.HiddenTakeoffs,
            rasterSheet: null);
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
        bool clearsOverlay = string.IsNullOrWhiteSpace(overlayPageFolder);
        bool changesOverlay = clearsOverlay ||
                              !SamePageReference(pageFolder, src.OverlayPageFolder, overlayPageFolder);
        WriteSource(
            pageFolder,
            pdfAbs,
            src.Page,
            src.ScaleMetersPerPt,
            src.PdfLayers,
            src.PdfLayersCached,
            src.LegendTakeoffOrder,
            src.LegendTakeoffOrderMode,
            overlayPageFolder,
            !clearsOverlay && src.OverlayVisible,
            string.IsNullOrWhiteSpace(overlayColor) ? "#E53935" : overlayColor,
            NormalizeOverlayOpacity(overlayOpacity),
            changesOverlay ? 0 : src.OverlayOffsetXPt,
            changesOverlay ? 0 : src.OverlayOffsetYPt,
            changesOverlay ? 1 : src.OverlayScale,
            changesOverlay ? 0 : src.OverlayRotationDegrees,
            src.HiddenTakeoffs,
            src.RasterSheet);
    }

    public static void SavePageOverlayVisibility(string pageFolder, bool isVisible)
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
            src.LegendTakeoffOrderMode,
            src.OverlayPageFolder,
            isVisible,
            src.OverlayColor,
            src.OverlayOpacity,
            src.OverlayOffsetXPt,
            src.OverlayOffsetYPt,
            src.OverlayScale,
            src.OverlayRotationDegrees,
            src.HiddenTakeoffs,
            src.RasterSheet);
    }

    public static void ClearPageOverlay(string pageFolder) =>
        SavePageOverlay(pageFolder, "", "#E53935", 0.55);

    public static void SavePageOverlayTransform(
        string pageFolder,
        double overlayOffsetXPt,
        double overlayOffsetYPt,
        double overlayScale,
        double? overlayRotationDegrees = null)
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
            src.LegendTakeoffOrderMode,
            src.OverlayPageFolder,
            src.OverlayVisible,
            src.OverlayColor,
            src.OverlayOpacity,
            NormalizeOverlayOffset(overlayOffsetXPt),
            NormalizeOverlayOffset(overlayOffsetYPt),
            NormalizeOverlayScale(overlayScale),
            NormalizeOverlayRotation(overlayRotationDegrees ?? src.OverlayRotationDegrees),
            src.HiddenTakeoffs,
            src.RasterSheet);
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
            src.LegendTakeoffOrderMode,
            src.OverlayPageFolder,
            src.OverlayVisible,
            src.OverlayColor,
            src.OverlayOpacity,
            src.OverlayOffsetXPt,
            src.OverlayOffsetYPt,
            src.OverlayScale,
            src.OverlayRotationDegrees,
            src.HiddenTakeoffs,
            src.RasterSheet);
    }

    public static void SavePageRasterSheet(string pageFolder, RasterSheetSource? rasterSheet)
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
            src.LegendTakeoffOrderMode,
            src.OverlayPageFolder,
            src.OverlayVisible,
            src.OverlayColor,
            src.OverlayOpacity,
            src.OverlayOffsetXPt,
            src.OverlayOffsetYPt,
            src.OverlayScale,
            src.OverlayRotationDegrees,
            src.HiddenTakeoffs,
            rasterSheet);
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
            return JsonSerializer.Deserialize<PdfSheetMetadata>(File.ReadAllText(path), OurPlaneCoreJobStore.JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            OurPlaneCoreJobStore.QuarantineCorruptJson(path, "ReadSourcePdfMetadata", ex);
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
                JsonSerializer.Serialize(metadata, OurPlaneCoreJobStore.JsonOptions));
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
            return JsonSerializer.Deserialize<PageLayerManifest>(File.ReadAllText(path), OurPlaneCoreJobStore.JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            OurPlaneCoreJobStore.QuarantineCorruptJson(path, "ReadPageLayerManifest", ex);
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"ReadPageLayerManifest failed for {path}");
            return null;
        }
    }

    public static void RewritePageSources(string newRoot, IReadOnlyList<PageSourceSnapshot> snapshots)
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
                    overlayVisible: snap.OverlayVisible,
                    overlayColor: snap.OverlayColor,
                    overlayOpacity: snap.OverlayOpacity,
                    overlayOffsetXPt: snap.OverlayOffsetXPt,
                    overlayOffsetYPt: snap.OverlayOffsetYPt,
                    overlayScale: snap.OverlayScale,
                    overlayRotationDegrees: snap.OverlayRotationDegrees,
                    hiddenTakeoffs: snap.HiddenTakeoffs,
                    rasterSheet: snap.RasterSheet);
        }
    }

    public static List<PageSourceSnapshot> CollectPageSources(string rootFolder)
    {
        var snapshots = new List<PageSourceSnapshot>();
        if (!Directory.Exists(rootFolder)) return snapshots;

        foreach (string dir in OurPlaneCoreJobStore.EnumerateSelfAndDescendants(rootFolder))
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
                src.OverlayVisible,
                src.OverlayColor,
                src.OverlayOpacity,
                src.OverlayOffsetXPt,
                src.OverlayOffsetYPt,
                src.OverlayScale,
                src.OverlayRotationDegrees,
                NormalizeStringList(src.HiddenTakeoffs),
                NormalizeRasterSheet(src.RasterSheet)));
        }

        return snapshots;
    }

    private static void WriteSource(
        string pageFolder,
        string pdfAbsPath,
        int pageIndex,
        double scaleMetersPerPt,
        IReadOnlyList<PdfLayerInfo>? pdfLayers = null,
        bool pdfLayersCached = false,
        IReadOnlyList<string>? legendTakeoffOrder = null,
        string legendTakeoffOrderMode = "auto",
        string overlayPageFolder = "",
        bool overlayVisible = true,
        string overlayColor = "#E53935",
        double overlayOpacity = 0.55,
        double overlayOffsetXPt = 0,
        double overlayOffsetYPt = 0,
        double overlayScale = 1.0,
        double overlayRotationDegrees = 0,
        IReadOnlyList<string>? hiddenTakeoffs = null,
        RasterSheetSource? rasterSheet = null)
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
            LegendTakeoffOrderMode = NormalizeLegendTakeoffOrderMode(legendTakeoffOrderMode),
            HiddenTakeoffs = NormalizeStringList(hiddenTakeoffs),
            OverlayPageFolder = MakeRelativePageReference(pageFolder, overlayPageFolder),
            OverlayVisible = overlayVisible,
            OverlayColor = string.IsNullOrWhiteSpace(overlayColor) ? "#E53935" : overlayColor,
            OverlayOpacity = NormalizeOverlayOpacity(overlayOpacity),
            OverlayOffsetXPt = NormalizeOverlayOffset(overlayOffsetXPt),
            OverlayOffsetYPt = NormalizeOverlayOffset(overlayOffsetYPt),
            OverlayScale = NormalizeOverlayScale(overlayScale),
            OverlayRotationDegrees = NormalizeOverlayRotation(overlayRotationDegrees),
            RasterSheet = NormalizeRasterSheet(rasterSheet),
        };
        try
        {
            IoUtil.WriteAllTextAtomic(
                Path.Combine(pageFolder, "source.json"),
                JsonSerializer.Serialize(src, OurPlaneCoreJobStore.JsonOptions));
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
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(manifest, OurPlaneCoreJobStore.JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
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

    private static bool SamePageReference(string pageFolder, string left, string right)
    {
        string leftResolved = ResolveRelativePagePath(pageFolder, left)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string rightResolved = ResolveRelativePagePath(pageFolder, right)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(leftResolved, rightResolved, StringComparison.OrdinalIgnoreCase);
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

    private static double NormalizeOverlayRotation(double degrees)
    {
        if (double.IsNaN(degrees) || double.IsInfinity(degrees))
            return 0;

        double normalized = degrees % 360.0;
        if (normalized > 180.0)
            normalized -= 360.0;
        if (normalized <= -180.0)
            normalized += 360.0;
        return Math.Round(normalized, 6);
    }

    private static List<string> NormalizeStringList(IEnumerable<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    private static RasterSheetSource? NormalizeRasterSheet(RasterSheetSource? rasterSheet)
    {
        if (rasterSheet == null)
            return null;

        RasterSheetSource copy = rasterSheet.Clone();
        copy.Image = (copy.Image ?? "").Trim();
        copy.OverviewImage = (copy.OverviewImage ?? "").Trim();
        copy.Format = string.IsNullOrWhiteSpace(copy.Format) ? "png" : copy.Format.Trim().ToLowerInvariant();
        copy.RenderProfile = (copy.RenderProfile ?? "").Trim().ToLowerInvariant();
        copy.RenderScale = double.IsNaN(copy.RenderScale) || double.IsInfinity(copy.RenderScale)
            ? 0
            : Math.Clamp(copy.RenderScale, 0, 8);
        copy.OverviewRenderScale = double.IsNaN(copy.OverviewRenderScale) || double.IsInfinity(copy.OverviewRenderScale)
            ? 0
            : Math.Clamp(copy.OverviewRenderScale, 0, 8);
        copy.WidthPt = double.IsNaN(copy.WidthPt) || double.IsInfinity(copy.WidthPt)
            ? 0
            : Math.Max(0, copy.WidthPt);
        copy.HeightPt = double.IsNaN(copy.HeightPt) || double.IsInfinity(copy.HeightPt)
            ? 0
            : Math.Max(0, copy.HeightPt);
        copy.PdfLastWriteUtcTicks = Math.Max(0, copy.PdfLastWriteUtcTicks);
        copy.PdfLength = Math.Max(0, copy.PdfLength);
        copy.GeneratedAtUtc = (copy.GeneratedAtUtc ?? "").Trim();
        copy.SnapIndex = (copy.SnapIndex ?? "").Trim();
        copy.SnapPointCount = Math.Max(0, copy.SnapPointCount);
        copy.SnapSegmentCount = Math.Max(0, copy.SnapSegmentCount);
        copy.SnapGeneratedAtUtc = (copy.SnapGeneratedAtUtc ?? "").Trim();
        copy.SnapSchemaVersion = Math.Max(0, copy.SnapSchemaVersion);
        return copy;
    }

    private static string NormalizeLegendTakeoffOrderMode(string? mode) =>
        string.Equals(mode?.Trim(), "manual", StringComparison.OrdinalIgnoreCase)
            ? "manual"
            : "auto";
}

internal sealed record PageSourceSnapshot(
    string RelativeFolder,
    string PdfAbsPath,
    int Page,
    double ScaleMetersPerPt,
    bool PdfLayersCached,
    IReadOnlyList<PdfLayerInfo> PdfLayers,
    string OverlayPageFolder,
    bool OverlayVisible,
    string OverlayColor,
    double OverlayOpacity,
    double OverlayOffsetXPt,
    double OverlayOffsetYPt,
    double OverlayScale,
    double OverlayRotationDegrees,
    IReadOnlyList<string> HiddenTakeoffs,
    RasterSheetSource? RasterSheet);
