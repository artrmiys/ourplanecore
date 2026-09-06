using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OurPlanCore;

internal static class PageStore
{
    public static IReadOnlyList<PageInfo> ImportPdf(
        OurPlanCoreJob job,
        string pdfSourcePath,
        IReadOnlyList<string> pageNames,
        string destinationFolder,
        IReadOnlyDictionary<int, IReadOnlyList<PdfLayerInfo>>? pdfLayerCache = null)
    {
        using var operation = JobOperationJournal.Begin(job.RootPath, "Import PDF pages");
        JobWriteAccess.Demand(destinationFolder, "import PDF pages");
        string sourcesDir = JobLayout.EnsureFolder(job.RootPath, "sources");
        string pdfDest = OurPlanCoreJobStore.UniqueFilePath(Path.Combine(sourcesDir, Path.GetFileName(pdfSourcePath)));
        JobWriteAccess.Demand(pdfDest, "copy source PDF");
        if (!File.Exists(pdfDest))
            File.Copy(pdfSourcePath, pdfDest);

        var created = new List<PageInfo>();
        for (int i = 0; i < pageNames.Count; i++)
        {
            string displayName = string.IsNullOrWhiteSpace(pageNames[i])
                ? $"Page {i + 1}"
                : pageNames[i].Trim();
            string pageFolder = OurPlanCoreJobStore.UniqueDirectoryPath(Path.Combine(destinationFolder, OurPlanCoreJobStore.SanitizeName(displayName, 120)));
            Directory.CreateDirectory(pageFolder);

            OurPlanCoreJobStore.WriteItemDataXml(pageFolder, "Page", displayName, OurPlanCoreJobStore.GetNextOrderIndex(destinationFolder));
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

        operation.Commit();
        return created;
    }

    public static PageInfo CreatePageFromPdf(
        OurPlanCoreJob job,
        string pdfSourcePath,
        string displayName,
        string destinationFolder,
        int pdfPage = 0,
        double scaleMetersPerPt = 0)
    {
        JobWriteAccess.Demand(destinationFolder, "create PDF page");
        string sourcesDir = JobLayout.EnsureFolder(job.RootPath, "sources");
        string pdfDest = OurPlanCoreJobStore.UniqueFilePath(Path.Combine(sourcesDir, Path.GetFileName(pdfSourcePath)));
        JobWriteAccess.Demand(pdfDest, "copy source PDF");
        if (!File.Exists(pdfDest))
            File.Copy(pdfSourcePath, pdfDest);

        string cleanName = string.IsNullOrWhiteSpace(displayName)
            ? $"Page {pdfPage + 1}"
            : displayName.Trim();
        string pageFolder = OurPlanCoreJobStore.UniqueDirectoryPath(Path.Combine(destinationFolder, OurPlanCoreJobStore.SanitizeName(cleanName, 120)));
        Directory.CreateDirectory(pageFolder);

        OurPlanCoreJobStore.WriteItemDataXml(pageFolder, "Page", cleanName, OurPlanCoreJobStore.GetNextOrderIndex(destinationFolder));
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
        OurPlanCoreJob job,
        string displayName,
        string destinationFolder,
        float widthPt = BlankPagePdfService.DefaultWidthPt,
        float heightPt = BlankPagePdfService.DefaultHeightPt)
    {
        JobWriteAccess.Demand(destinationFolder, "create blank page");
        string cleanName = string.IsNullOrWhiteSpace(displayName)
            ? "Blank Sheet"
            : displayName.Trim();
        string sourcesDir = JobLayout.EnsureFolder(job.RootPath, "sources");
        string pdfFileName = $"{OurPlanCoreJobStore.SanitizeName(cleanName, 80)}.blank.pdf";
        string pdfDest = OurPlanCoreJobStore.UniqueFilePath(Path.Combine(sourcesDir, pdfFileName));
        JobWriteAccess.Demand(pdfDest, "create blank page PDF");
        BlankPagePdfService.WriteBlankPdf(pdfDest, widthPt, heightPt);

        string pageFolder = OurPlanCoreJobStore.UniqueDirectoryPath(Path.Combine(destinationFolder, OurPlanCoreJobStore.SanitizeName(cleanName, 120)));
        Directory.CreateDirectory(pageFolder);

        OurPlanCoreJobStore.WriteItemDataXml(pageFolder, "Page", cleanName, OurPlanCoreJobStore.GetNextOrderIndex(destinationFolder));
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
        return ReadSourceResult(pageFolder).Value;
    }

    internal static DataFileResult<SourceInfo> ReadSourceResult(string pageFolder) =>
        DataFileReader.Read(Path.Combine(pageFolder, "source.json"), json =>
        {
            SourceInfo source = JsonSerializer.Deserialize<SourceInfo>(json) ?? throw new JsonException("Missing page source.");
            PageReferenceSafety.ValidateSource(pageFolder, source);
            return source;
        });

    public static PageInfo? TryReadPage(string pageFolder)
    {
        DataFileResult<SourceInfo> loaded = ReadSourceResult(pageFolder);
        SourceInfo? src = loaded.Value;
        if (src == null)
        {
            if (loaded.State != DataFileState.Missing || !JobWriteAccess.IsWriteAllowed(pageFolder) ||
                !PageSourceRepair.TryRepairFromMetadata(pageFolder, out src))
            {
                return null;
            }
        }

        string pdfPath = Path.GetFullPath(Path.Combine(pageFolder, src.Pdf));
        SheetOverlayLayerCollection overlays = SheetOverlayLayerStore.Load(pageFolder, src);
        SheetOverlayLayerInfo? activeOverlay = overlays.ActiveLayer;
        return new PageInfo
        {
            Name = OurPlanCoreJobStore.ReadName(pageFolder) ?? Path.GetFileName(pageFolder),
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
            HiddenMeasurements = NormalizeStringList(src.HiddenMeasurements),
            ActiveOverlayId = activeOverlay?.Id ?? "",
            OverlayLayers = overlays.Layers,
            OverlayPageFolder = activeOverlay?.SourcePageFolder ?? "",
            OverlayVisible = activeOverlay?.IsVisible ?? false,
            OverlayColor = activeOverlay?.Color ?? "#E53935",
            OverlayOpacity = activeOverlay?.Opacity ?? 1.0,
            OverlayOffsetXPt = activeOverlay?.OffsetXPt ?? 0,
            OverlayOffsetYPt = activeOverlay?.OffsetYPt ?? 0,
            OverlayScale = activeOverlay?.Scale ?? 1.0,
            OverlayRotationDegrees = activeOverlay?.RotationDegrees ?? 0,
            RasterSheet = NormalizeRasterSheet(src.RasterSheet),
        };
    }

    public static void SavePageScale(string pageFolder, double scaleMetersPerPt)
    {
        SourceInfo? src = ReadSource(pageFolder);
        if (src == null) return;
        if (src.ScaleMetersPerPt.Equals(scaleMetersPerPt))
            return;

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
            src.RasterSheet,
            src.HiddenMeasurements);
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
            src.RasterSheet,
            src.HiddenMeasurements);
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
            src.RasterSheet,
            src.HiddenMeasurements);
    }

    public static void SavePageHiddenMeasurements(string pageFolder, IReadOnlyList<string> hiddenMeasurements)
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
            src.RasterSheet,
            hiddenMeasurements);
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
            rasterSheet: null,
            hiddenMeasurements: src.HiddenMeasurements);
    }

    public static void SavePageOverlay(
        string pageFolder,
        string overlayPageFolder,
        string overlayColor,
        double overlayOpacity)
    {
        if (string.IsNullOrWhiteSpace(overlayPageFolder))
        {
            SheetOverlayLayerInfo? active = SheetOverlayLayerStore.Load(pageFolder).ActiveLayer;
            if (active != null)
                SheetOverlayLayerStore.Remove(pageFolder, active.Id);
            return;
        }

        SheetOverlayLayerStore.ReplaceActive(
            pageFolder,
            overlayPageFolder,
            overlayColor,
            overlayOpacity);
    }

    public static void SavePageOverlayVisibility(string pageFolder, bool isVisible)
    {
        SheetOverlayLayerInfo? active = SheetOverlayLayerStore.Load(pageFolder).ActiveLayer;
        if (active != null)
            SheetOverlayLayerStore.SetVisibility(pageFolder, active.Id, isVisible);
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
        SheetOverlayLayerInfo? active = SheetOverlayLayerStore.Load(pageFolder).ActiveLayer;
        if (active == null)
            return;
        SheetOverlayLayerStore.SetTransform(
            pageFolder,
            active.Id,
            overlayOffsetXPt,
            overlayOffsetYPt,
            overlayScale,
            overlayRotationDegrees ?? active.RotationDegrees);
    }

    internal static void WriteLegacyOverlayMirror(
        string pageFolder,
        SheetOverlayLayerInfo? layer)
    {
        SourceInfo? src = ReadSource(pageFolder);
        if (src == null)
            return;

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
            layer?.SourcePageFolder ?? "",
            layer?.IsVisible ?? false,
            layer?.Color ?? "#E53935",
            layer?.Opacity ?? 1.0,
            layer?.OffsetXPt ?? 0,
            layer?.OffsetYPt ?? 0,
            layer?.Scale ?? 1.0,
            layer?.RotationDegrees ?? 0,
            src.HiddenTakeoffs,
            src.RasterSheet,
            src.HiddenMeasurements);
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
            src.RasterSheet,
            src.HiddenMeasurements);
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
            rasterSheet,
            src.HiddenMeasurements);
    }

    public static string PageLayersJsonPath(string pageFolder) =>
        Path.Combine(pageFolder, "layers.json");

    public static string SourcePdfMetadataPath(string pageFolder) =>
        Path.Combine(pageFolder, "source_pdf.json");

    public static PdfSheetMetadata? ReadSourcePdfMetadata(string pageFolder)
    {
        return DataFileReader.Read(SourcePdfMetadataPath(pageFolder), json =>
        {
            PdfSheetMetadata metadata = JsonSerializer.Deserialize<PdfSheetMetadata>(json, OurPlanCoreJobStore.JsonOptions)
                ?? throw new JsonException("Missing sheet metadata.");
            if (!string.IsNullOrWhiteSpace(metadata.PdfPath) && !Path.IsPathRooted(metadata.PdfPath))
                metadata.PdfPath = Path.GetFullPath(Path.Combine(pageFolder, metadata.PdfPath));
            return metadata;
        }).Value;
    }

    public static void WriteSourcePdfMetadata(string pageFolder, PdfSheetMetadata metadata)
    {
        JobWriteAccess.Demand(SourcePdfMetadataPath(pageFolder), "save PDF metadata");
        Directory.CreateDirectory(pageFolder);
        try
        {
            IoUtil.WriteAllTextAtomic(
                SourcePdfMetadataPath(pageFolder),
                JsonSerializer.Serialize(metadata, OurPlanCoreJobStore.JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(SourcePdfMetadataPath(pageFolder))}': {ex.Message}", ex);
        }
    }

    public static PageLayerManifest? ReadPageLayerManifest(string pageFolder) =>
        DataFileReader.ReadJson<PageLayerManifest>(PageLayersJsonPath(pageFolder)).Value;

    public static void RewritePageSources(string newRoot, IReadOnlyList<PageSourceSnapshot> snapshots)
    {
        JobWriteAccess.Demand(newRoot, "rewrite page sources");
        foreach (var snap in snapshots)
        {
            string targetFolder = snap.RelativeFolder == "."
                ? newRoot
                : Path.Combine(newRoot, snap.RelativeFolder);
            if (Directory.Exists(targetFolder))
            {
                WriteSource(
                    targetFolder,
                    snap.PdfAbsPath,
                    snap.Page,
                    snap.ScaleMetersPerPt,
                    snap.PdfLayers,
                    snap.PdfLayersCached,
                    legendTakeoffOrder: snap.LegendTakeoffOrder,
                    legendTakeoffOrderMode: snap.LegendTakeoffOrderMode,
                    overlayPageFolder: snap.OverlayPageFolder,
                    overlayVisible: snap.OverlayVisible,
                    overlayColor: snap.OverlayColor,
                    overlayOpacity: snap.OverlayOpacity,
                    overlayOffsetXPt: snap.OverlayOffsetXPt,
                    overlayOffsetYPt: snap.OverlayOffsetYPt,
                    overlayScale: snap.OverlayScale,
                    overlayRotationDegrees: snap.OverlayRotationDegrees,
                    hiddenTakeoffs: snap.HiddenTakeoffs,
                    rasterSheet: snap.RasterSheet,
                    hiddenMeasurements: snap.HiddenMeasurements,
                    additionalData: snap.AdditionalData ?? []);
                if (snap.OverlayLayers.Layers.Count > 0)
                    SheetOverlayLayerStore.SaveSnapshot(targetFolder, snap.OverlayLayers);
            }
        }
    }

    public static List<PageSourceSnapshot> CollectPageSources(string rootFolder)
    {
        var snapshots = new List<PageSourceSnapshot>();
        if (!Directory.Exists(rootFolder)) return snapshots;

        foreach (string dir in OurPlanCoreJobStore.EnumerateSelfAndDescendants(rootFolder))
        {
            SourceInfo? src = ReadSource(dir);
            if (src == null) continue;

            string rel = Path.GetRelativePath(rootFolder, dir);
            string pdfAbs = Path.GetFullPath(Path.Combine(dir, src.Pdf));
            SheetOverlayLayerCollection overlayLayers = SheetOverlayLayerStore.Load(dir, src);
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
                overlayLayers.Clone(),
                NormalizeStringList(src.HiddenTakeoffs),
                NormalizeStringList(src.HiddenMeasurements),
                NormalizeRasterSheet(src.RasterSheet),
                src.LegendTakeoffOrder?.ToList() ?? [],
                src.LegendTakeoffOrderMode,
                src.AdditionalData == null ? [] : new(src.AdditionalData)));
        }

        return snapshots;
    }

    public static int RebasePageOverlayReferences(string pagesRoot, IReadOnlyList<(string OldPath, string NewPath)> moves)
    {
        var normalizedMoves = moves
            .Select(move => (OldPath: NormalizeFolderPath(move.OldPath), NewPath: NormalizeFolderPath(move.NewPath)))
            .Where(move => !string.Equals(move.OldPath, move.NewPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (normalizedMoves.Count == 0 || !Directory.Exists(pagesRoot))
            return 0;

        JobWriteAccess.Demand(pagesRoot, "rebase page overlay references");

        int changed = 0;
        foreach (string dir in OurPlanCoreJobStore.EnumerateSelfAndDescendants(pagesRoot))
        {
            SourceInfo? src = ReadSource(dir);
            if (src == null)
                continue;

            SheetOverlayLayerCollection collection = SheetOverlayLayerStore.Load(dir, src);
            bool overlayChanged = false;
            var rebasedLayers = new List<SheetOverlayLayerInfo>();
            foreach (SheetOverlayLayerInfo layer in collection.Layers)
            {
                string sourcePath = ResolveRelativePagePath(dir, layer.SourcePageFolder);
                if (!TryRebaseMovedPagePath(normalizedMoves, sourcePath, out string rebasedSourcePath) ||
                    SamePageReference(dir, layer.SourcePageFolder, rebasedSourcePath))
                {
                    rebasedLayers.Add(layer.Clone());
                    continue;
                }

                overlayChanged = true;
                rebasedLayers.Add(new SheetOverlayLayerInfo
                {
                    Id = layer.Id,
                    SourcePageFolder = rebasedSourcePath,
                    IsVisible = layer.IsVisible,
                    Color = layer.Color,
                    Opacity = layer.Opacity,
                    OffsetXPt = layer.OffsetXPt,
                    OffsetYPt = layer.OffsetYPt,
                    Scale = layer.Scale,
                    RotationDegrees = layer.RotationDegrees,
                });
            }

            if (!overlayChanged)
                continue;

            SheetOverlayLayerStore.SaveSnapshot(
                dir,
                new SheetOverlayLayerCollection
                {
                    ActiveOverlayId = collection.ActiveOverlayId,
                    Layers = rebasedLayers,
                });
            changed++;
        }

        return changed;
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
        RasterSheetSource? rasterSheet = null,
        IReadOnlyList<string>? hiddenMeasurements = null,
        Dictionary<string, JsonElement>? additionalData = null)
    {
        string sourcePath = Path.Combine(pageFolder, "source.json");
        string layersPath = PageLayersJsonPath(pageFolder);
        JobWriteAccess.Demand(sourcePath, "save page source");
        JobWriteAccess.Demand(layersPath, "save page layer manifest");
        var src = new SourceInfo
        {
            AdditionalData = additionalData ?? ReadSource(pageFolder)?.AdditionalData,
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
            HiddenMeasurements = NormalizeStringList(hiddenMeasurements),
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
                sourcePath,
                JsonSerializer.Serialize(src, OurPlanCoreJobStore.JsonOptions));
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
            {
                JobWriteAccess.Demand(path, "delete page layer manifest");
                File.Delete(path);
            }
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
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(manifest, OurPlanCoreJobStore.JsonOptions));
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

    private static bool TryRebaseMovedPagePath(
        IReadOnlyList<(string OldPath, string NewPath)> moves,
        string path,
        out string rebased)
    {
        string current = NormalizeFolderPath(path);
        foreach (var move in moves)
        {
            if (!OurPlanCoreJobStore.IsSameOrDescendant(move.OldPath, current))
                continue;

            string relative = Path.GetRelativePath(move.OldPath, current);
            rebased = relative == "."
                ? move.NewPath
                : Path.GetFullPath(Path.Combine(move.NewPath, relative));
            return true;
        }

        rebased = current;
        return false;
    }

    private static string NormalizeFolderPath(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
        copy.PinnedDpi = RasterSheetCacheService.IsSourceImageRasterProfile(copy)
            ? 0
            : RasterSheetCacheService.NormalizePinnedRasterDpi(copy.PinnedDpi);
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
    SheetOverlayLayerCollection OverlayLayers,
    IReadOnlyList<string> HiddenTakeoffs,
    IReadOnlyList<string> HiddenMeasurements,
    RasterSheetSource? RasterSheet,
    IReadOnlyList<string> LegendTakeoffOrder,
    string LegendTakeoffOrderMode,
    Dictionary<string, JsonElement>? AdditionalData);
