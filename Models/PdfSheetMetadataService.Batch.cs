using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace OurPlanCore;

public sealed record PdfSheetMetadataAnalysisItem(
    PageInfo Page,
    bool Ok,
    PdfSheetMetadata? Metadata,
    string Error,
    bool FromCache);

public sealed record PdfSheetMetadataAnalysisProgress(
    int Completed,
    int Total,
    int CacheHits,
    string CurrentPdf);

public static partial class PdfSheetMetadataService
{
    // Keep one document open for almost the whole permit set. A 128-page chunk
    // stays below the dedicated 60s metadata timeout on the real Peerless set.
    private const int SheetMetadataBatchChunkSize = 128;

    public static IReadOnlyList<PdfSheetMetadataAnalysisItem> AnalyzePages(
        OurPlanCoreJob job,
        IReadOnlyList<PageInfo> pages,
        bool persistMetadata,
        bool forceReanalyze = false,
        CancellationToken cancellationToken = default,
        Action<PdfSheetMetadataAnalysisProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(pages);

        var watch = Stopwatch.StartNew();
        SheetMetadataConfig config = SheetMetadataRulesService.Active.Clone();
        PdfSheetMetadataCropTemplate? cropTemplate = PdfSheetMetadataCropService.LoadTemplate(job);
        bool useCache = config.DetectorMode == SheetMetadataDetectorMode.IdealV3 && !forceReanalyze;
        var results = new Dictionary<string, PdfSheetMetadataAnalysisItem>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<PageInfo>();
        int completed = 0;
        int cacheHits = 0;

        foreach (PageInfo page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (useCache && SheetMetadataAnalysisCache.TryLoad(job, page, config, cropTemplate, out PdfSheetMetadata? cached))
            {
                NormalizeMetadata(page, cached!, job);
                if (persistMetadata)
                    OurPlanCoreJobStore.WriteSourcePdfMetadata(page.FolderPath, cached!);
                results[page.FolderPath] = new PdfSheetMetadataAnalysisItem(page, true, cached, "", true);
                completed++;
                cacheHits++;
                progress?.Invoke(new PdfSheetMetadataAnalysisProgress(
                    completed, pages.Count, cacheHits, Path.GetFileName(page.PdfPath)));
            }
            else
            {
                pending.Add(page);
            }
        }

        foreach (IGrouping<string, PageInfo> group in pending
                     .Where(page => File.Exists(page.PdfPath))
                     .GroupBy(page => Path.GetFullPath(page.PdfPath), StringComparer.OrdinalIgnoreCase))
        {
            List<PageInfo> pdfPages = group.ToList();
            for (int offset = 0; offset < pdfPages.Count; offset += SheetMetadataBatchChunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<PageInfo> chunk = pdfPages.Skip(offset).Take(SheetMetadataBatchChunkSize).ToList();
                AnalyzeChunk(job, chunk, config, cropTemplate, persistMetadata, results);
                completed += chunk.Count;
                progress?.Invoke(new PdfSheetMetadataAnalysisProgress(
                    completed, pages.Count, cacheHits, Path.GetFileName(group.Key)));
            }
        }

        foreach (PageInfo page in pending.Where(page => !File.Exists(page.PdfPath)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool ok = TryAnalyzePage(job, page, out PdfSheetMetadata metadata, out string error);
            if (ok && persistMetadata)
                OurPlanCoreJobStore.WriteSourcePdfMetadata(page.FolderPath, metadata);
            results[page.FolderPath] = new PdfSheetMetadataAnalysisItem(
                page, ok, ok ? metadata : null, error, false);
            completed++;
            progress?.Invoke(new PdfSheetMetadataAnalysisProgress(
                completed, pages.Count, cacheHits, Path.GetFileName(page.PdfPath)));
        }

        IReadOnlyList<PdfSheetMetadataAnalysisItem> ordered = pages
            .Select(page => results.TryGetValue(page.FolderPath, out PdfSheetMetadataAnalysisItem? item)
                ? item
                : new PdfSheetMetadataAnalysisItem(page, false, null, "Sheet metadata analysis returned no result.", false))
            .ToList();
        AppLog.Info(
            $"Sheet metadata batch complete; pages={pages.Count}; cache_hits={cacheHits}; " +
            $"failed={ordered.Count(item => !item.Ok)}; elapsed={watch.ElapsedMilliseconds}ms; " +
            $"detector={config.DetectorMode}");
        return ordered;
    }

    private static void AnalyzeChunk(
        OurPlanCoreJob job,
        IReadOnlyList<PageInfo> pages,
        SheetMetadataConfig config,
        PdfSheetMetadataCropTemplate? cropTemplate,
        bool persistMetadata,
        Dictionary<string, PdfSheetMetadataAnalysisItem> results)
    {
        var request = new SheetMetaBatchRequest
        {
            Pdf = pages[0].PdfPath,
            Pages = pages.Select(page => page.PdfPage).ToList(),
            SheetMetadataConfig = config,
            CropTemplate = cropTemplate,
        };

        if (!PdfLayerRenderService.TryInvokeHelper(
                "sheetmeta_batch", request, out SheetMetaBatchResponse? response, out string batchError) ||
            response == null || !response.Ok)
        {
            AnalyzeChunkFallback(job, pages, persistMetadata, results, response?.Error ?? batchError);
            return;
        }

        Dictionary<int, SheetMetaBatchItem> responseByPage = response.Results
            .GroupBy(item => item.Page)
            .ToDictionary(group => group.Key, group => group.First());
        long cacheMs = 0;
        long normalizeMs = 0;
        long persistMs = 0;
        foreach (PageInfo page in pages)
        {
            if (!responseByPage.TryGetValue(page.PdfPage, out SheetMetaBatchItem? item) ||
                !item.Ok || item.Metadata == null)
            {
                string error = item?.Error ?? "Batch helper returned no metadata for this page.";
                results[page.FolderPath] = new PdfSheetMetadataAnalysisItem(page, false, null, error, false);
                continue;
            }

            PdfSheetMetadata rawMetadata = item.Metadata;
            var stageWatch = Stopwatch.StartNew();
            SheetMetadataAnalysisCache.TrySave(job, page, config, cropTemplate, rawMetadata);
            cacheMs += stageWatch.ElapsedMilliseconds;
            stageWatch.Restart();
            NormalizeMetadata(page, rawMetadata, job);
            normalizeMs += stageWatch.ElapsedMilliseconds;
            stageWatch.Restart();
            if (persistMetadata)
                OurPlanCoreJobStore.WriteSourcePdfMetadata(page.FolderPath, rawMetadata);
            persistMs += stageWatch.ElapsedMilliseconds;
            results[page.FolderPath] = new PdfSheetMetadataAnalysisItem(page, true, rawMetadata, "", false);
        }
        if (cacheMs + normalizeMs + persistMs >= 150)
        {
            AppLog.Info(
                $"Sheet metadata batch postprocess; pages={pages.Count}; cache={cacheMs}ms; " +
                $"normalize={normalizeMs}ms; persist={persistMs}ms");
        }
    }

    private static void AnalyzeChunkFallback(
        OurPlanCoreJob job,
        IReadOnlyList<PageInfo> pages,
        bool persistMetadata,
        Dictionary<string, PdfSheetMetadataAnalysisItem> results,
        string batchError)
    {
        AppLog.Warn($"Sheet metadata batch fell back to per-page analysis: {batchError}");
        foreach (PageInfo page in pages)
        {
            bool ok = TryAnalyzePage(job, page, out PdfSheetMetadata metadata, out string error);
            if (ok && persistMetadata)
                OurPlanCoreJobStore.WriteSourcePdfMetadata(page.FolderPath, metadata);
            results[page.FolderPath] = new PdfSheetMetadataAnalysisItem(
                page, ok, ok ? metadata : null, error, false);
        }
    }

    private sealed class SheetMetaBatchRequest
    {
        public string Pdf { get; set; } = "";
        public List<int> Pages { get; set; } = [];
        public SheetMetadataConfig SheetMetadataConfig { get; set; } = SheetMetadataConfig.BuildDefault();
        public PdfSheetMetadataCropTemplate? CropTemplate { get; set; }
    }

    private sealed class SheetMetaBatchResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public List<SheetMetaBatchItem> Results { get; set; } = [];
    }

    private sealed class SheetMetaBatchItem
    {
        public int Page { get; set; }
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public PdfSheetMetadata? Metadata { get; set; }
    }
}

internal static class SheetMetadataAnalysisCache
{
    private const int CacheSchemaVersion = 1;
    private const string DetectorRevision = "ideal-v3-layout-index-1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public static bool TryLoad(
        OurPlanCoreJob job,
        PageInfo page,
        SheetMetadataConfig config,
        PdfSheetMetadataCropTemplate? cropTemplate,
        out PdfSheetMetadata? metadata)
    {
        metadata = null;
        try
        {
            string key = BuildKey(page, config, cropTemplate);
            string path = CachePath(job, key);
            if (!File.Exists(path))
                return false;
            SheetMetadataCacheEntry? entry = JsonSerializer.Deserialize<SheetMetadataCacheEntry>(
                File.ReadAllText(path), JsonOptions);
            if (entry?.SchemaVersion != CacheSchemaVersion ||
                !string.Equals(entry.Key, key, StringComparison.Ordinal) ||
                entry.Metadata == null)
            {
                return false;
            }

            metadata = entry.Metadata;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Warn(ex, $"Sheet metadata cache read failed for {page.Name}");
            return false;
        }
    }

    public static void TrySave(
        OurPlanCoreJob job,
        PageInfo page,
        SheetMetadataConfig config,
        PdfSheetMetadataCropTemplate? cropTemplate,
        PdfSheetMetadata metadata)
    {
        if (config.DetectorMode != SheetMetadataDetectorMode.IdealV3)
            return;
        try
        {
            string key = BuildKey(page, config, cropTemplate);
            var entry = new SheetMetadataCacheEntry
            {
                SchemaVersion = CacheSchemaVersion,
                Key = key,
                Metadata = metadata,
            };
            IoUtil.WriteAllTextAtomic(CachePath(job, key), JsonSerializer.Serialize(entry, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Warn(ex, $"Sheet metadata cache write failed for {page.Name}");
        }
    }

    private static string BuildKey(
        PageInfo page,
        SheetMetadataConfig config,
        PdfSheetMetadataCropTemplate? cropTemplate)
    {
        var info = new FileInfo(page.PdfPath);
        string source = string.Join("\n",
            DetectorRevision,
            Path.GetFullPath(page.PdfPath).ToUpperInvariant(),
            info.Exists ? info.Length : -1,
            info.Exists ? info.LastWriteTimeUtc.Ticks : 0,
            page.PdfPage,
            JsonSerializer.Serialize(SheetMetadataConfig.UpgradeForCurrentSchema(config), JsonOptions),
            JsonSerializer.Serialize(cropTemplate, JsonOptions));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }

    private static string CachePath(OurPlanCoreJob job, string key) =>
        Path.Combine(job.AIContextRoot, "cache", "sheet_metadata_v3", $"{key}.json");

    private sealed class SheetMetadataCacheEntry
    {
        public int SchemaVersion { get; set; }
        public string Key { get; set; } = "";
        public PdfSheetMetadata? Metadata { get; set; }
    }
}
