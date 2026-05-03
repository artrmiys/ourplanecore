using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace SmartTakeoffs;

public sealed class PdfSheetMetadata
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("generated_at_utc")]
    public string GeneratedAtUtc { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("pdf_path")]
    public string PdfPath { get; set; } = "";

    [JsonPropertyName("page_index")]
    public int PageIndex { get; set; }

    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    [JsonPropertyName("width_pt")]
    public double WidthPt { get; set; }

    [JsonPropertyName("height_pt")]
    public double HeightPt { get; set; }

    [JsonPropertyName("sheet_label")]
    public string SheetLabel { get; set; } = "";

    [JsonPropertyName("sheet_key")]
    public string SheetKey { get; set; } = "";

    [JsonPropertyName("normalized_sheet_name")]
    public string NormalizedSheetName { get; set; } = "";

    [JsonPropertyName("sheet_title")]
    public string SheetTitle { get; set; } = "";

    [JsonPropertyName("suffix")]
    public string Suffix { get; set; } = "";

    [JsonPropertyName("skip_scale")]
    public bool SkipScale { get; set; }

    [JsonPropertyName("title_scale_text")]
    public string TitleScaleText { get; set; } = "";

    [JsonPropertyName("title_scale_raw")]
    public string TitleScaleRaw { get; set; } = "";

    [JsonPropertyName("body_scales")]
    public List<string> BodyScales { get; set; } = [];

    [JsonPropertyName("all_scales")]
    public List<string> AllScales { get; set; } = [];

    [JsonPropertyName("selected_scale_text")]
    public string SelectedScaleText { get; set; } = "";

    [JsonPropertyName("scale_text")]
    public string ScaleText { get; set; } = "";

    [JsonPropertyName("selected_scale_ratio")]
    public double SelectedScaleRatio { get; set; }

    [JsonPropertyName("selected_scale_m_per_pt")]
    public double SelectedScaleMetersPerPt { get; set; }

    [JsonPropertyName("rename_candidate")]
    public string RenameCandidate { get; set; } = "";

    [JsonPropertyName("has_details")]
    public bool HasDetails { get; set; }

    [JsonPropertyName("has_schedule")]
    public bool HasSchedule { get; set; }

    [JsonPropertyName("layers")]
    public List<PdfLayerInfo> Layers { get; set; } = [];

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "";

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];

    public string EffectiveScaleText =>
        !string.IsNullOrWhiteSpace(SelectedScaleText) ? SelectedScaleText : ScaleText;

    public string EffectiveSheetKey =>
        !string.IsNullOrWhiteSpace(SheetKey) ? SheetKey : NormalizedSheetName;

    public string ProposedPageName()
    {
        if (!string.IsNullOrWhiteSpace(RenameCandidate))
            return RenameCandidate.Trim();

        string key = EffectiveSheetKey.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return "-";
        return string.IsNullOrWhiteSpace(Suffix)
            ? key
            : $"{key} {Suffix.Trim()}";
    }

    public bool CanApplyScale() =>
        !SkipScale && SelectedScaleMetersPerPt > 0;
}

public static class PdfSheetMetadataService
{
    public static bool NeedsFallback(PdfSheetMetadata? metadata) =>
        metadata == null ||
        string.Equals(metadata.Confidence, "no-text", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metadata.Source, "pdf-empty-text", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(metadata.SheetLabel) ||
        string.IsNullOrWhiteSpace(metadata.SheetTitle) ||
        (!metadata.SkipScale &&
         metadata.SelectedScaleMetersPerPt <= 0 &&
         IsScaleLikelyNeeded(metadata.Suffix));

    public static bool TrySaveFallbackCrop(
        PageInfo page,
        string outputPath,
        out SKRect cropPdfRect,
        out string error)
    {
        cropPdfRect = SKRect.Empty;
        error = "";

        if (!PdfLayerRenderService.TryRender(
                page.PdfPath,
                page.PdfPage,
                1.50,
                new Dictionary<int, bool>(),
                [],
                page.PdfLayersCached ? page.PdfLayers : null,
                out PdfLayerRenderResult render,
                out error))
        {
            return false;
        }

        using SKBitmap? bitmap = SKBitmap.Decode(render.ImageBytes);
        if (bitmap == null)
        {
            error = "Rendered PDF image could not be decoded.";
            return false;
        }

        int cropTop = Math.Max(0, (int)(bitmap.Height * 0.58));
        var sourceRect = new SKRectI(0, cropTop, bitmap.Width, bitmap.Height);
        using var crop = new SKBitmap(sourceRect.Width, sourceRect.Height);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(bitmap, sourceRect, new SKRect(0, 0, crop.Width, crop.Height));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using SKData data = crop.Encode(SKEncodedImageFormat.Png, 95);
        using FileStream stream = File.Create(outputPath);
        data.SaveTo(stream);

        float topPdf = (float)(render.HeightPt * 0.58);
        cropPdfRect = new SKRect(0, topPdf, render.WidthPt, render.HeightPt);
        return true;
    }

    public static bool TryAnalyzePage(
        SmartTakeoffsJob job,
        PageInfo page,
        out PdfSheetMetadata metadata,
        out string error)
    {
        if (File.Exists(page.PdfPath))
            return TryAnalyzePage(page, out metadata, out error);

        return TryAnalyzeFromResolvedSource(job, page, out metadata, out error);
    }

    public static bool TryAnalyzePage(
        PageInfo page,
        out PdfSheetMetadata metadata,
        out string error)
    {
        metadata = new PdfSheetMetadata();
        error = "";

        var request = new SheetMetaRequest
        {
            Pdf = page.PdfPath,
            Page = page.PdfPage,
        };

        if (!PdfLayerRenderService.TryInvokeHelper("sheetmeta", request, out SheetMetaResponse? response, out error))
            return false;

        if (response == null || !response.Ok)
        {
            error = response?.Error ?? "PyMuPDF did not return sheet metadata.";
            return false;
        }

        metadata = response.Metadata ?? new PdfSheetMetadata();
        NormalizeMetadata(page, metadata);
        return true;
    }

    public static bool TryAnalyzeAndSave(
        SmartTakeoffsJob job,
        PageInfo page,
        out PdfSheetMetadata metadata,
        out string error)
    {
        if (!TryAnalyzePage(job, page, out metadata, out error))
            return false;

        SmartTakeoffsJobStore.WriteSourcePdfMetadata(page.FolderPath, metadata);
        SmartLearningStore.AppendSheetFeedback(
            job,
            page,
            BuildLearningRecord(page, metadata, "pending_review", "Detection saved from PDF metadata analyze."));
        return true;
    }

    private static bool TryAnalyzeFromResolvedSource(
        SmartTakeoffsJob job,
        PageInfo page,
        out PdfSheetMetadata metadata,
        out string error)
    {
        metadata = new PdfSheetMetadata();
        error = "";

        string targetSheetKey = ExtractSheetKey(page.Name);
        var candidatePdfs = ResolveSourcePdfCandidates(job).ToList();
        if (candidatePdfs.Count == 0)
        {
            error = $"Source PDF is missing and no E-Wood/source candidate folder was found: {page.PdfPath}";
            return false;
        }

        var scanErrors = new List<string>();
        foreach (string pdf in candidatePdfs)
        {
            int pageCount;
            try
            {
                using var doc = Docnet.Core.DocLib.Instance.GetDocReader(pdf, new Docnet.Core.Models.PageDimensions(1.0));
                pageCount = doc.GetPageCount();
            }
            catch (Exception ex)
            {
                scanErrors.Add($"{Path.GetFileName(pdf)}: {ex.Message}");
                continue;
            }

            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var candidatePage = new PageInfo
                {
                    Name = page.Name,
                    FolderPath = page.FolderPath,
                    PdfPath = pdf,
                    PdfPage = pageIndex,
                    ScaleMetersPerPt = page.ScaleMetersPerPt,
                    PdfLayersCached = false,
                    PdfLayers = [],
                };

                if (!TryAnalyzePage(candidatePage, out PdfSheetMetadata candidate, out string candidateError))
                {
                    scanErrors.Add($"{Path.GetFileName(pdf)} page {pageIndex + 1}: {candidateError}");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(targetSheetKey) &&
                    string.Equals(candidate.EffectiveSheetKey, targetSheetKey, StringComparison.OrdinalIgnoreCase))
                {
                    candidate.Warnings.Add($"source PDF was resolved from {pdf}");
                    metadata = candidate;
                    return true;
                }

                if (string.IsNullOrWhiteSpace(targetSheetKey) && pageIndex == page.PdfPage)
                {
                    candidate.Warnings.Add($"source PDF fallback used page order from {pdf}");
                    metadata = candidate;
                    return true;
                }
            }
        }

        error = scanErrors.Count > 0
            ? "Could not match page to source PDF. " + string.Join("; ", scanErrors.Take(5))
            : "Could not match page to source PDF.";
        return false;
    }

    private static IEnumerable<string> ResolveSourcePdfCandidates(SmartTakeoffsJob job)
    {
        var roots = new List<string>();
        string jobSources = Path.Combine(job.RootPath, "sources");
        if (Directory.Exists(jobSources))
            roots.Add(jobSources);

        string? ewoodSource = ResolveEWoodSourceRoot(job.RootPath);
        if (!string.IsNullOrWhiteSpace(ewoodSource) && Directory.Exists(ewoodSource))
            roots.Add(ewoodSource);

        return roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(root =>
            {
                try
                {
                    return Directory.EnumerateFiles(root, "*.pdf", SearchOption.AllDirectories);
                }
                catch
                {
                    return [];
                }
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveEWoodSourceRoot(string jobRoot)
    {
        string full = Path.GetFullPath(jobRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string marker = Path.Combine("3.Final_for_check", "---", "Jobs");
        int markerIndex = full.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return null;

        string beforeMarker = full[..markerIndex];
        string jobName = Path.GetFileName(full);
        if (string.IsNullOrWhiteSpace(jobName))
            return null;

        return Path.Combine(beforeMarker, "2.New_Projects", jobName);
    }

    private static string ExtractSheetKey(string value)
    {
        Match match = Regex.Match(value ?? "", @"\b([A-Za-z]{1,3})-?(\d{1,4}[A-Za-z]?)\b");
        if (!match.Success)
            return "";
        return (match.Groups[1].Value + match.Groups[2].Value).ToLowerInvariant();
    }

    private static bool IsScaleLikelyNeeded(string suffix) =>
        string.IsNullOrWhiteSpace(suffix) ||
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1st", "2nd", "3rd", "4th", "5th", "rf", "f", "b", "sec", "el", "u", "v", "wt", "ft", "sv", "sw",
        }.Contains(suffix);

    public static SmartSheetLearningRecord BuildLearningRecord(
        PageInfo page,
        PdfSheetMetadata metadata,
        string userOutcome,
        string note,
        SmartSheetLearningDecision? final = null)
    {
        var detection = new SmartSheetLearningDecision
        {
            PageName = metadata.ProposedPageName(),
            SheetLabel = metadata.SheetLabel,
            SheetKey = metadata.EffectiveSheetKey,
            SheetTitle = metadata.SheetTitle,
            Suffix = metadata.Suffix,
            SkipScale = metadata.SkipScale,
            ScaleText = metadata.EffectiveScaleText,
            ScaleMetersPerPt = metadata.SelectedScaleMetersPerPt,
            Confidence = string.IsNullOrWhiteSpace(metadata.Confidence) ? metadata.Source : metadata.Confidence,
        };

        return new SmartSheetLearningRecord
        {
            EventType = "sheet_feedback",
            Source = string.IsNullOrWhiteSpace(metadata.Source) ? "pdf-text" : metadata.Source,
            UserOutcome = userOutcome,
            SourcePdf = metadata.PdfPath,
            PdfPage = metadata.PageIndex,
            Detection = detection,
            Final = final ?? new SmartSheetLearningDecision(),
            Layers = metadata.Layers
                .OrderBy(layer => layer.Number)
                .Select(layer => new PdfLayerInfo
                {
                    Number = layer.Number,
                    Name = layer.Name,
                    IsOn = layer.IsOn,
                })
                .ToList(),
            Warnings = metadata.Warnings.ToList(),
            Note = note,
        };
    }

    public static SmartSheetLearningDecision FinalDecision(
        PageInfo page,
        PdfSheetMetadata metadata,
        string pageName,
        double scaleMetersPerPt) =>
        new()
        {
            PageName = pageName,
            SheetLabel = metadata.SheetLabel,
            SheetKey = metadata.EffectiveSheetKey,
            SheetTitle = metadata.SheetTitle,
            Suffix = metadata.Suffix,
            SkipScale = metadata.SkipScale,
            ScaleText = scaleMetersPerPt > 0 ? metadata.EffectiveScaleText : "",
            ScaleMetersPerPt = scaleMetersPerPt,
            Confidence = "manual_review",
        };

    public static bool TryBuildMetadataFromFallbackResponse(
        PageInfo page,
        SmartAiRequest request,
        SmartAiResponse response,
        out PdfSheetMetadata metadata,
        out string error)
    {
        metadata = new PdfSheetMetadata();
        error = "";

        foreach (string json in CandidateJsonBlocks(response.OutputText))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                metadata = new PdfSheetMetadata
                {
                    SchemaVersion = 1,
                    GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                    Source = "gpt-image",
                    PdfPath = page.PdfPath,
                    PageIndex = page.PdfPage,
                    PageNumber = page.PdfPage + 1,
                    SheetLabel = JsonString(root, "sheet_label"),
                    SheetKey = JsonString(root, "sheet_key"),
                    NormalizedSheetName = JsonString(root, "normalized_sheet_name"),
                    SheetTitle = JsonString(root, "sheet_title"),
                    Suffix = JsonString(root, "suffix"),
                    SkipScale = JsonBool(root, "skip_scale"),
                    SelectedScaleText = JsonString(root, "selected_scale_text"),
                    ScaleText = JsonString(root, "scale_text"),
                    Confidence = string.IsNullOrWhiteSpace(JsonString(root, "confidence"))
                        ? "gpt-image"
                        : JsonString(root, "confidence"),
                    Warnings = JsonStringArray(root, "warnings"),
                };

                if (string.IsNullOrWhiteSpace(metadata.ScaleText))
                    metadata.ScaleText = metadata.SelectedScaleText;
                if (string.IsNullOrWhiteSpace(metadata.SelectedScaleText))
                    metadata.SelectedScaleText = metadata.ScaleText;

                NormalizeMetadata(page, metadata);
                metadata.Warnings.Add($"metadata fallback response: {request.Id}");
                return true;
            }
            catch
            {
                // Try the next candidate block.
            }
        }

        error = "AI response did not contain a readable sheet metadata JSON object.";
        return false;
    }

    private static void NormalizeMetadata(PageInfo page, PdfSheetMetadata metadata)
    {
        metadata.SchemaVersion = metadata.SchemaVersion <= 0 ? 1 : metadata.SchemaVersion;
        metadata.PdfPath = string.IsNullOrWhiteSpace(metadata.PdfPath) ? page.PdfPath : metadata.PdfPath;
        metadata.PageIndex = metadata.PageIndex < 0 ? page.PdfPage : metadata.PageIndex;
        metadata.PageNumber = metadata.PageNumber <= 0 ? metadata.PageIndex + 1 : metadata.PageNumber;
        metadata.SheetKey = NormalizeSheetKey(metadata.SheetKey, metadata.SheetLabel);
        metadata.NormalizedSheetName = string.IsNullOrWhiteSpace(metadata.NormalizedSheetName)
            ? metadata.SheetKey
            : NormalizeSheetKey(metadata.NormalizedSheetName, metadata.SheetLabel);

        if (metadata.BodyScales.Count == 0 && metadata.AllScales.Count > 0)
            metadata.BodyScales = metadata.AllScales.ToList();
        if (metadata.AllScales.Count == 0 && metadata.BodyScales.Count > 0)
            metadata.AllScales = metadata.BodyScales.ToList();

        SmartLearningStore.ApplyLearnedRules(metadata);

        if (string.IsNullOrWhiteSpace(metadata.SelectedScaleText) && !string.IsNullOrWhiteSpace(metadata.ScaleText))
            metadata.SelectedScaleText = metadata.ScaleText;
        if (string.IsNullOrWhiteSpace(metadata.ScaleText) && !string.IsNullOrWhiteSpace(metadata.SelectedScaleText))
            metadata.ScaleText = metadata.SelectedScaleText;

        if (metadata.SelectedScaleMetersPerPt <= 0 && !string.IsNullOrWhiteSpace(metadata.EffectiveScaleText))
        {
            double ratio = ParseScaleRatio(metadata.EffectiveScaleText);
            metadata.SelectedScaleRatio = ratio;
            metadata.SelectedScaleMetersPerPt = ratio > 0
                ? (25.4 / 72.0 / 1000.0) * ratio
                : 0;
        }

        if (string.IsNullOrWhiteSpace(metadata.RenameCandidate))
            metadata.RenameCandidate = metadata.ProposedPageName();
    }

    private static string NormalizeSheetKey(string value, string sheetLabel)
    {
        string source = string.IsNullOrWhiteSpace(value) ? sheetLabel : value;
        return new string(source
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static double ParseScaleRatio(string scaleText)
    {
        string clean = scaleText.Trim();
        if (string.Equals(
                clean.Replace(" ", ""),
                "1\"=1\"",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        string left = clean.Split('=')[0]
            .Replace("\"", "", StringComparison.Ordinal)
            .Trim();
        double inches = ParseInches(left);
        return inches > 0 ? 12.0 / inches : 0;
    }

    private static double ParseInches(string value)
    {
        try
        {
            if (value.Contains('-', StringComparison.Ordinal))
            {
                string[] pieces = value.Split('-', 2);
                return double.Parse(pieces[0], CultureInfo.InvariantCulture) + ParseFraction(pieces[1]);
            }

            if (value.Contains('/', StringComparison.Ordinal))
                return ParseFraction(value);

            return double.Parse(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    private static double ParseFraction(string value)
    {
        string[] pieces = value.Split('/', 2);
        if (pieces.Length != 2)
            return 0;
        double numerator = double.Parse(pieces[0], CultureInfo.InvariantCulture);
        double denominator = double.Parse(pieces[1], CultureInfo.InvariantCulture);
        return denominator == 0 ? 0 : numerator / denominator;
    }

    private static IEnumerable<string> CandidateJsonBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        int searchAt = 0;
        while (true)
        {
            int fenceStart = text.IndexOf("```", searchAt, StringComparison.Ordinal);
            if (fenceStart < 0)
                break;

            int contentStart = text.IndexOf('\n', fenceStart);
            if (contentStart < 0)
                break;

            int fenceEnd = text.IndexOf("```", contentStart + 1, StringComparison.Ordinal);
            if (fenceEnd < 0)
                break;

            string block = text[(contentStart + 1)..fenceEnd].Trim();
            if (block.StartsWith('{'))
                yield return block;

            searchAt = fenceEnd + 3;
        }

        string trimmed = text.Trim();
        if (trimmed.StartsWith('{'))
            yield return trimmed;

        int objectStart = text.IndexOf('{');
        int objectEnd = text.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart)
            yield return text[objectStart..(objectEnd + 1)];
    }

    private static string JsonString(JsonElement element, string name) =>
        TryGetProperty(element, name, out JsonElement value)
            ? value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : value.ToString()
            : "";

    private static bool JsonBool(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out JsonElement value))
            return false;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();
        return value.ValueKind == JsonValueKind.String &&
               bool.TryParse(value.GetString(), out bool parsed) &&
               parsed;
    }

    private static List<string> JsonStringArray(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed class SheetMetaRequest
    {
        public string Pdf { get; set; } = "";
        public int Page { get; set; }
    }

    private sealed class SheetMetaResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public PdfSheetMetadata? Metadata { get; set; }
    }
}
