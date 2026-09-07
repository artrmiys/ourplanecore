using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace OurPlanCore;

public sealed class PdfSheetMetadata
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("generated_at_utc")]
    public string GeneratedAtUtc { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("detector_version")]
    public string DetectorVersion { get; set; } = "legacy-v1";

    [JsonPropertyName("detector_preset")]
    public string DetectorPreset { get; set; } = "";

    [JsonPropertyName("detector_config_fingerprint")]
    public string DetectorConfigFingerprint { get; set; } = "";

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

    [JsonPropertyName("title_source")]
    public string TitleSource { get; set; } = "";

    [JsonPropertyName("title_confidence")]
    public string TitleConfidence { get; set; } = "";

    [JsonPropertyName("title_evidence")]
    public string TitleEvidence { get; set; } = "";

    [JsonPropertyName("suffix")]
    public string Suffix { get; set; } = "";

    [JsonPropertyName("suffix_source")]
    public string SuffixSource { get; set; } = "";

    [JsonPropertyName("suffix_confidence")]
    public string SuffixConfidence { get; set; } = "";

    [JsonPropertyName("suffix_evidence")]
    public string SuffixEvidence { get; set; } = "";

    [JsonPropertyName("suffix_scale_policy")]
    public string SuffixScalePolicy { get; set; } = "";

    [JsonPropertyName("suffix_override_action")]
    public string SuffixOverrideAction { get; set; } = "";

    [JsonPropertyName("suffix_explicit_clear")]
    public bool SuffixExplicitClear { get; set; }

    [JsonPropertyName("skip_scale")]
    public bool SkipScale { get; set; }

    [JsonPropertyName("skip_reason")]
    public string SkipReason { get; set; } = "";

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

    [JsonPropertyName("scale_source")]
    public string ScaleSource { get; set; } = "";

    [JsonPropertyName("scale_override_action")]
    public string ScaleOverrideAction { get; set; } = "";

    [JsonPropertyName("scale_confidence")]
    public string ScaleConfidence { get; set; } = "";

    [JsonPropertyName("scale_evidence")]
    public string ScaleEvidence { get; set; } = "";

    [JsonPropertyName("rename_candidate")]
    public string RenameCandidate { get; set; } = "";

    [JsonPropertyName("rename_override_applied")]
    public bool RenameOverrideApplied { get; set; }

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
            return PdfSheetMetadataService.VisibleSheetDisplayName(RenameCandidate).ToLowerInvariant();

        string key = (EffectiveSheetKey ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(key))
            return "";
        string suffix = (Suffix ?? "").Trim().ToLowerInvariant();
        string proposed = string.IsNullOrWhiteSpace(Suffix)
            ? key
            : $"{key} {suffix}";
        return PdfSheetMetadataService.VisibleSheetDisplayName(proposed);
    }

    public bool CanApplyScale() =>
        !SkipScale && SelectedScaleMetersPerPt > 0;
}

public static partial class PdfSheetMetadataService
{
    private const double PdfPointMeters = ViewportConstants.PdfPointMeters;

    public static string VisibleSheetDisplayName(string value)
    {
        string clean = Regex.Replace((value ?? "").Trim(), @"\s+", " ");
        return Regex.Replace(clean, @"\s+\(\d+\)\s*$", "", RegexOptions.CultureInvariant).Trim();
    }

    public static string FormatImperialScale(double scaleMetersPerPt)
    {
        double ratio = scaleMetersPerPt > 0 ? scaleMetersPerPt / PdfPointMeters : 0;
        return FormatImperialScaleRatio(ratio);
    }

    public static string FormatImperialScaleExact(double scaleMetersPerPt)
    {
        double ratio = scaleMetersPerPt > 0 ? scaleMetersPerPt / PdfPointMeters : 0;
        return FormatImperialScaleRatioCore(ratio, snapToNearbyPreset: false);
    }

    public static string FormatImperialScaleRatio(double ratio) =>
        FormatImperialScaleRatioCore(ratio, snapToNearbyPreset: true);

    private static string FormatImperialScaleRatioCore(double ratio, bool snapToNearbyPreset)
    {
        if (ratio <= 0)
            return "";

        (double Ratio, string Label)[] presets =
        [
            (1,   "1\" = 1\""),
            (2,   "6\" = 1'0\""),
            (3,   "4\" = 1'0\""),
            (4,   "3\" = 1'0\""),
            (6,   "2\" = 1'0\""),
            (8,   "1 1/2\" = 1'0\""),
            (9.6, "1 1/4\" = 1'0\""),
            (12,  "1\" = 1'0\""),
            (16,  "3/4\" = 1'0\""),
            (19.2,"5/8\" = 1'0\""),
            (24,  "1/2\" = 1'0\""),
            (32,  "3/8\" = 1'0\""),
            (48,  "1/4\" = 1'0\""),
            (64,  "3/16\" = 1'0\""),
            (96,  "1/8\" = 1'0\""),
            (128, "3/32\" = 1'0\""),
            (192, "1/16\" = 1'0\""),
            (384, "1/32\" = 1'0\""),
        ];

        foreach (var preset in presets)
        {
            if (snapToNearbyPreset && Math.Abs(ratio - preset.Ratio) <= 0.25)
                return preset.Label;
            if (!snapToNearbyPreset && Math.Abs(ratio - preset.Ratio) <= 0.000000001)
                return preset.Label;
        }

        double feetPerInch = ratio / 12.0;
        double roundedFeet = Math.Round(feetPerInch);
        if (roundedFeet >= 10 && Math.Abs(feetPerInch - roundedFeet) <= 0.05)
            return $"1\" = {roundedFeet:0}'0\"";

        double inchesPerFoot = 12.0 / ratio;
        string inchLabel = FormatScaleInches(inchesPerFoot);
        return string.IsNullOrWhiteSpace(inchLabel)
            ? $"1:{ratio:F0}"
            : $"{inchLabel}\" = 1'0\"";
    }

    private static string FormatScaleInches(double inches)
    {
        if (inches <= 0)
            return "";

        int numerator64 = (int)Math.Round(inches * 64);
        double roundedFraction = numerator64 / 64.0;
        if (Math.Abs(roundedFraction - inches) <= 0.002)
        {
            int whole = numerator64 / 64;
            int remainder = numerator64 % 64;
            if (remainder == 0)
                return whole.ToString(CultureInfo.InvariantCulture);

            int divisor = GreatestCommonDivisor(remainder, 64);
            int numerator = remainder / divisor;
            int denominator = 64 / divisor;
            return whole > 0
                ? $"{whole} {numerator}/{denominator}"
                : $"{numerator}/{denominator}";
        }

        return inches.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        left = Math.Abs(left);
        right = Math.Abs(right);
        while (right != 0)
        {
            int next = left % right;
            left = right;
            right = next;
        }

        return left == 0 ? 1 : left;
    }

    public static bool NeedsFallback(PdfSheetMetadata? metadata) =>
        metadata == null ||
        string.Equals(metadata.Confidence, "no-text", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metadata.Source, "pdf-empty-text", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(metadata.SheetLabel) ||
        string.IsNullOrWhiteSpace(metadata.SheetTitle) ||
        (string.IsNullOrWhiteSpace(metadata.Suffix) &&
         PdfSheetMetadataPolicy.ConfidenceLevel(metadata.SuffixConfidence) == SheetMetadataConfidence.Low) ||
        (!metadata.SkipScale &&
         metadata.SelectedScaleMetersPerPt <= 0 &&
         (string.IsNullOrWhiteSpace(metadata.Suffix) ||
          SheetMetadataRulesService.Active.IsScaleCapableSuffix(metadata.Suffix)));

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
        OurPlanCoreJob job,
        PageInfo page,
        out PdfSheetMetadata metadata,
        out string error)
    {
        if (File.Exists(page.PdfPath))
            return TryAnalyzePage(page, out metadata, out error, job);

        return TryAnalyzeFromResolvedSource(job, page, out metadata, out error);
    }

    public static bool TryAnalyzePage(
        PageInfo page,
        out PdfSheetMetadata metadata,
        out string error,
        OurPlanCoreJob? job = null)
    {
        metadata = new PdfSheetMetadata();
        error = "";

        var request = new SheetMetaRequest
        {
            Pdf = page.PdfPath,
            Page = page.PdfPage,
            SheetMetadataConfig = SheetMetadataRulesService.Active.Clone(),
            CropTemplate = job == null ? null : PdfSheetMetadataCropService.LoadTemplate(job),
            CropTemplates = job == null ? null : PdfSheetMetadataCropService.LoadCatalog(job),
        };

        if (!PdfLayerRenderService.TryInvokeHelper("sheetmeta", request, out SheetMetaResponse? response, out error))
            return false;

        if (response == null || !response.Ok)
        {
            error = response?.Error ?? "PyMuPDF did not return sheet metadata.";
            return false;
        }

        metadata = response.Metadata ?? new PdfSheetMetadata();
        NormalizeMetadata(page, metadata, job);
        return true;
    }

    public static bool TryAnalyzeAndSave(
        OurPlanCoreJob job,
        PageInfo page,
        out PdfSheetMetadata metadata,
        out string error)
    {
        if (!TryAnalyzePage(job, page, out metadata, out error))
            return false;

        OurPlanCoreJobStore.WriteSourcePdfMetadata(page.FolderPath, metadata);
        return true;
    }

    private static bool TryAnalyzeFromResolvedSource(
        OurPlanCoreJob job,
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
            error = $"Source PDF is missing and no source candidate folder was found: {page.PdfPath}";
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
                AppLog.Warn(ex, $"PDF source scan failed for {pdf}");
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

                if (!TryAnalyzePage(candidatePage, out PdfSheetMetadata candidate, out string candidateError, job))
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

    private static IEnumerable<string> ResolveSourcePdfCandidates(OurPlanCoreJob job)
    {
        var roots = new List<string>();
        string jobSources = Path.Combine(job.RootPath, "sources");
        if (Directory.Exists(jobSources))
            roots.Add(jobSources);

        string? projectSource = ResolveProjectSourceRoot(job.RootPath);
        if (!string.IsNullOrWhiteSpace(projectSource) && Directory.Exists(projectSource))
            roots.Add(projectSource);

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

    private static string? ResolveProjectSourceRoot(string jobRoot)
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
        Match match = Regex.Match(value ?? "", @"\b([A-Za-z]{1,3})-?(\d{1,4}(?:\.\d+)?[A-Za-z]?)\b");
        if (!match.Success)
            return "";
        return (match.Groups[1].Value + match.Groups[2].Value).ToLowerInvariant();
    }

    public static bool TryBuildMetadataFromFallbackResponse(
        PageInfo page,
        SmartAiRequest request,
        SmartAiResponse response,
        out PdfSheetMetadata metadata,
        out string error,
        OurPlanCoreJob? job = null)
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
                    DetectorVersion = JsonString(root, "detector_version"),
                    DetectorPreset = JsonString(root, "detector_preset"),
                    DetectorConfigFingerprint = JsonString(root, "detector_config_fingerprint"),
                    PdfPath = page.PdfPath,
                    PageIndex = page.PdfPage,
                    PageNumber = page.PdfPage + 1,
                    SheetLabel = JsonString(root, "sheet_label"),
                    SheetKey = JsonString(root, "sheet_key"),
                    NormalizedSheetName = JsonString(root, "normalized_sheet_name"),
                    SheetTitle = JsonString(root, "sheet_title"),
                    TitleSource = JsonString(root, "title_source"),
                    TitleConfidence = JsonString(root, "title_confidence"),
                    TitleEvidence = JsonString(root, "title_evidence"),
                    Suffix = JsonString(root, "suffix"),
                    SuffixSource = JsonString(root, "suffix_source"),
                    SuffixConfidence = JsonString(root, "suffix_confidence"),
                    SuffixEvidence = JsonString(root, "suffix_evidence"),
                    SuffixScalePolicy = JsonString(root, "suffix_scale_policy"),
                    SuffixOverrideAction = JsonString(root, "suffix_override_action"),
                    SuffixExplicitClear = JsonBool(root, "suffix_explicit_clear"),
                    SkipScale = JsonBool(root, "skip_scale"),
                    SkipReason = JsonString(root, "skip_reason"),
                    SelectedScaleText = JsonString(root, "selected_scale_text"),
                    ScaleText = JsonString(root, "scale_text"),
                    ScaleSource = JsonString(root, "scale_source"),
                    ScaleOverrideAction = JsonString(root, "scale_override_action"),
                    ScaleConfidence = JsonString(root, "scale_confidence"),
                    ScaleEvidence = JsonString(root, "scale_evidence"),
                    RenameCandidate = JsonString(root, "rename_candidate"),
                    RenameOverrideApplied = JsonBool(root, "rename_override_applied"),
                    Confidence = string.IsNullOrWhiteSpace(JsonString(root, "confidence"))
                        ? "gpt-image"
                        : JsonString(root, "confidence"),
                    Warnings = JsonStringArray(root, "warnings"),
                };

                if (string.IsNullOrWhiteSpace(metadata.ScaleText))
                    metadata.ScaleText = metadata.SelectedScaleText;
                if (string.IsNullOrWhiteSpace(metadata.SelectedScaleText))
                    metadata.SelectedScaleText = metadata.ScaleText;

                NormalizeMetadata(page, metadata, job);
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

    private static void NormalizeMetadata(PageInfo page, PdfSheetMetadata metadata, OurPlanCoreJob? job = null)
    {
        metadata.SchemaVersion = metadata.SchemaVersion <= 0 ? 1 : metadata.SchemaVersion;
        metadata.DetectorVersion = string.IsNullOrWhiteSpace(metadata.DetectorVersion)
            ? "legacy-v1"
            : metadata.DetectorVersion.Trim();
        SheetMetadataConfig config = SheetMetadataRulesService.Active;
        metadata.DetectorPreset = string.IsNullOrWhiteSpace(metadata.DetectorPreset)
            ? config.PresetName
            : metadata.DetectorPreset.Trim();
        metadata.DetectorConfigFingerprint = string.IsNullOrWhiteSpace(metadata.DetectorConfigFingerprint)
            ? PdfSheetMetadataPolicy.ConfigFingerprint(config)
            : metadata.DetectorConfigFingerprint.Trim();
        metadata.PdfPath = string.IsNullOrWhiteSpace(metadata.PdfPath) ? page.PdfPath : metadata.PdfPath;
        metadata.PageIndex = metadata.PageIndex < 0 ? page.PdfPage : metadata.PageIndex;
        metadata.PageNumber = metadata.PageNumber <= 0 ? metadata.PageIndex + 1 : metadata.PageNumber;
        metadata.Suffix = (metadata.Suffix ?? "").Trim().ToLowerInvariant();
        metadata.SuffixSource = (metadata.SuffixSource ?? "").Trim();
        metadata.SuffixOverrideAction = (metadata.SuffixOverrideAction ?? "").Trim();
        metadata.ScaleSource = (metadata.ScaleSource ?? "").Trim();
        metadata.ScaleOverrideAction = (metadata.ScaleOverrideAction ?? "").Trim();
        metadata.RenameCandidate = (metadata.RenameCandidate ?? "").Trim();
        metadata.SheetKey = NormalizeSheetKey(metadata.SheetKey, metadata.SheetLabel);
        metadata.NormalizedSheetName = string.IsNullOrWhiteSpace(metadata.NormalizedSheetName)
            ? metadata.SheetKey
            : NormalizeSheetKey(metadata.NormalizedSheetName, metadata.SheetLabel);

        if (metadata.BodyScales.Count == 0 && metadata.AllScales.Count > 0)
            metadata.BodyScales = metadata.AllScales.ToList();
        if (metadata.AllScales.Count == 0 && metadata.BodyScales.Count > 0)
            metadata.AllScales = metadata.BodyScales.ToList();

        if (job != null)
            SmartLearningStore.ApplyProjectLearnedRules(job, metadata);
        SmartLearningStore.ApplyLearnedRules(metadata);

        if (config.ShouldSkipScaleSuffix(
                metadata.Suffix,
                PdfSheetMetadataPolicy.ParseSuffixScalePolicy(metadata.SuffixScalePolicy)))
        {
            metadata.SkipScale = true;
            metadata.SkipReason = string.IsNullOrWhiteSpace(metadata.SkipReason)
                ? $"suffix '{metadata.Suffix}' is configured as no-scale"
                : metadata.SkipReason;
            metadata.SelectedScaleText = "";
            metadata.ScaleText = "";
            metadata.SelectedScaleRatio = 0;
            metadata.SelectedScaleMetersPerPt = 0;
        }
        else if (!config.AllowScaleInference &&
                 PdfSheetMetadataPolicy.IsInferredSource(metadata.ScaleSource))
        {
            metadata.Warnings.Add("inferred scale suppressed by active sheet metadata settings");
            metadata.SelectedScaleText = "";
            metadata.ScaleText = "";
            metadata.SelectedScaleRatio = 0;
            metadata.SelectedScaleMetersPerPt = 0;
        }

        PdfSheetMetadataPolicy.PreserveReviewedScaleClear(page, metadata, config);

        if (string.IsNullOrWhiteSpace(metadata.SelectedScaleText) && !string.IsNullOrWhiteSpace(metadata.ScaleText))
            metadata.SelectedScaleText = metadata.ScaleText;
        if (string.IsNullOrWhiteSpace(metadata.ScaleText) && !string.IsNullOrWhiteSpace(metadata.SelectedScaleText))
            metadata.ScaleText = metadata.SelectedScaleText;

        if (metadata.SelectedScaleMetersPerPt <= 0 && !string.IsNullOrWhiteSpace(metadata.EffectiveScaleText))
        {
            double ratio = ParseScaleRatio(metadata.EffectiveScaleText);
            metadata.SelectedScaleRatio = ratio;
            metadata.SelectedScaleMetersPerPt = ratio > 0
                ? ViewportConstants.PdfPointMeters * ratio
                : 0;
        }

        metadata.RenameCandidate = string.IsNullOrWhiteSpace(metadata.RenameCandidate)
            ? metadata.ProposedPageName()
            : NormalizeProposedPageName(metadata.RenameCandidate);

        metadata.TitleSource = PdfSheetMetadataPolicy.FieldSource(metadata.TitleSource, metadata.Source);
        metadata.SuffixSource = PdfSheetMetadataPolicy.FieldSource(metadata.SuffixSource, metadata.Source);
        metadata.ScaleSource = PdfSheetMetadataPolicy.FieldSource(metadata.ScaleSource, metadata.Source);
        metadata.TitleConfidence = PdfSheetMetadataPolicy.FieldConfidence(
            metadata.TitleConfidence,
            metadata.Confidence,
            !string.IsNullOrWhiteSpace(metadata.SheetTitle));
        metadata.SuffixConfidence = PdfSheetMetadataPolicy.FieldConfidence(
            metadata.SuffixConfidence,
            metadata.Confidence,
            !string.IsNullOrWhiteSpace(metadata.Suffix));
        metadata.ScaleConfidence = PdfSheetMetadataPolicy.FieldConfidence(
            metadata.ScaleConfidence,
            metadata.Confidence,
            metadata.SelectedScaleMetersPerPt > 0);
    }

    private static string NormalizeSheetKey(string value, string sheetLabel)
    {
        string source = string.IsNullOrWhiteSpace(value) ? sheetLabel : value;
        string compact = Regex.Replace(source.Trim(), @"\s+", "").Replace("-", "");
        if (Regex.IsMatch(compact, @"^[A-Za-z]{1,3}\d{1,4}(?:\.(?:R\d+[A-Za-z]?|[0-9]?U\d+[A-Za-z]?|\d+[A-Za-z]{0,2}))?[A-Za-z]{0,2}$"))
            return compact.ToLowerInvariant();

        return new string(source
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static string NormalizeProposedPageName(string value)
    {
        string compact = VisibleSheetDisplayName(value);
        return string.IsNullOrWhiteSpace(compact)
            ? ""
            : compact.ToLowerInvariant();
    }

    public static bool TryParseScaleMetersPerPt(string scaleText, out double scaleMetersPerPt)
    {
        scaleMetersPerPt = 0;

        double ratio = ParseScaleRatio(scaleText);
        if (ratio <= 0)
            return false;

        scaleMetersPerPt = PdfPointMeters * ratio;
        return true;
    }

    private static double ParseScaleRatio(string scaleText)
    {
        string clean = NormalizeScaleInput(scaleText);
        if (string.IsNullOrWhiteSpace(clean) ||
            string.Equals(clean, "skip", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        Match ratioPairMatch = Regex.Match(
            clean,
            @"^(?<left>\d+(?:\.\d+)?)\s*(?::|k|r|к|to)\s*(?<right>\d+(?:\.\d+)?)$",
            RegexOptions.IgnoreCase);
        if (ratioPairMatch.Success &&
            double.TryParse(ratioPairMatch.Groups["left"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double leftRatio) &&
            double.TryParse(ratioPairMatch.Groups["right"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double rightRatio))
        {
            if (leftRatio <= 0 || rightRatio <= 0)
                return 0;

            if (leftRatio < 1.0 && Math.Abs(rightRatio - 1.0) <= 0.000001)
                return 12.0 / leftRatio;

            return rightRatio / leftRatio;
        }

        Match ratioMatch = Regex.Match(clean, @"^1\s*:\s*(?<ratio>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase);
        if (ratioMatch.Success &&
            double.TryParse(ratioMatch.Groups["ratio"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double directRatio))
        {
            return directRatio > 0 ? directRatio : 0;
        }

        if (!clean.Contains('=', StringComparison.Ordinal) &&
            double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out directRatio))
        {
            if (directRatio <= 0)
                return 0;

            return directRatio < 1.0 ? 12.0 / directRatio : directRatio;
        }

        if (string.Equals(
                clean.Replace(" ", ""),
                "1\"=1\"",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (clean.Contains('=', StringComparison.Ordinal))
        {
            string[] pieces = clean.Split('=', 2);
            double leftInches = ParseInches(
                pieces[0]
                    .Replace("\"", "", StringComparison.Ordinal)
                    .Replace("inches", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("inch", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("in.", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("in", "", StringComparison.OrdinalIgnoreCase)
                    .Trim());
            double rightInches = ParseRightScaleInches(pieces[1]);
            if (leftInches > 0 && leftInches < 1.0 && IsBareOneScaleRight(pieces[1]))
                return 12.0 / leftInches;

            return leftInches > 0 && rightInches > 0 ? rightInches / leftInches : 0;
        }

        double inches = ParseInches(clean.Replace("\"", "", StringComparison.Ordinal).Trim());
        return inches > 0 ? 12.0 / inches : 0;
    }

    private static string NormalizeScaleInput(string scaleText) =>
        (scaleText ?? "")
            .Trim()
            .Replace("\u201d", "\"", StringComparison.Ordinal)
            .Replace("\u201c", "\"", StringComparison.Ordinal)
            .Replace("\u2033", "\"", StringComparison.Ordinal)
            .Replace("\u2019", "'", StringComparison.Ordinal)
            .Replace("\u2018", "'", StringComparison.Ordinal)
            .Replace("\u2032", "'", StringComparison.Ordinal)
            .Replace(",", ".", StringComparison.Ordinal)
            .Replace(" feet", "'", StringComparison.OrdinalIgnoreCase)
            .Replace(" foot", "'", StringComparison.OrdinalIgnoreCase)
            .Replace(" ft", "'", StringComparison.OrdinalIgnoreCase)
            .Replace("'-0\"", "'0\"", StringComparison.Ordinal);

    private static bool IsBareOneScaleRight(string value)
    {
        string clean = NormalizeScaleInput(value)
            .Replace("\"", "", StringComparison.Ordinal)
            .Trim();
        return string.Equals(clean, "1", StringComparison.Ordinal);
    }

    private static double ParseRightScaleInches(string value)
    {
        string clean = NormalizeScaleInput(value);
        Match feetMatch = Regex.Match(clean, @"(?<feet>\d+(?:\.\d+)?)\s*(?:'|ft|feet|foot)", RegexOptions.IgnoreCase);
        if (feetMatch.Success)
        {
            double feet = double.Parse(feetMatch.Groups["feet"].Value, CultureInfo.InvariantCulture);
            string remainder = clean[feetMatch.Index..];
            remainder = remainder[(feetMatch.Length)..];
            Match inchesMatch = Regex.Match(remainder, @"^\s*-?\s*(?<inches>\d+(?:\s+\d+/\d+|-\d+/\d+|/\d+)?(?:\.\d+)?)\s*(?:""|in|inch|inches)?", RegexOptions.IgnoreCase);
            double inches = inchesMatch.Success ? ParseInches(inchesMatch.Groups["inches"].Value) : 0;
            return feet * 12.0 + Math.Max(0, inches);
        }

        Match dashFeetMatch = Regex.Match(clean, @"^\s*(?<feet>\d+(?:\.\d+)?)\s*-\s*(?<inches>\d+(?:\.\d+)?)\s*""?\s*$");
        if (dashFeetMatch.Success)
        {
            double feet = double.Parse(dashFeetMatch.Groups["feet"].Value, CultureInfo.InvariantCulture);
            double inches = double.Parse(dashFeetMatch.Groups["inches"].Value, CultureInfo.InvariantCulture);
            return feet * 12.0 + inches;
        }

        return ParseInches(
            clean
                .Replace("\"", "", StringComparison.Ordinal)
                .Replace("inches", "", StringComparison.OrdinalIgnoreCase)
                .Replace("inch", "", StringComparison.OrdinalIgnoreCase)
                .Replace("in.", "", StringComparison.OrdinalIgnoreCase)
                .Replace("in", "", StringComparison.OrdinalIgnoreCase)
                .Trim());
    }

    private static double ParseInches(string value)
    {
        try
        {
            string clean = value.Trim();
            Match mixedMatch = Regex.Match(clean, @"^(?<whole>\d+(?:\.\d+)?)\s+(?<fraction>\d+\s*/\s*\d+)$");
            if (mixedMatch.Success)
            {
                return double.Parse(mixedMatch.Groups["whole"].Value, CultureInfo.InvariantCulture) +
                       ParseFraction(mixedMatch.Groups["fraction"].Value);
            }

            if (clean.Contains('-', StringComparison.Ordinal))
            {
                string[] pieces = clean.Split('-', 2);
                return double.Parse(pieces[0], CultureInfo.InvariantCulture) + ParseFraction(pieces[1]);
            }

            if (clean.Contains('/', StringComparison.Ordinal))
                return ParseFraction(clean);

            return double.Parse(clean, CultureInfo.InvariantCulture);
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
        [JsonPropertyName("pdf")]
        public string Pdf { get; set; } = "";

        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("sheet_metadata_config")]
        public SheetMetadataConfig SheetMetadataConfig { get; set; } = SheetMetadataConfig.BuildDefault();

        [JsonPropertyName("crop_template")]
        public PdfSheetMetadataCropTemplate? CropTemplate { get; set; }

        [JsonPropertyName("crop_templates")]
        public PdfSheetMetadataCropCatalog? CropTemplates { get; set; }
    }

    private sealed class SheetMetaResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public PdfSheetMetadata? Metadata { get; set; }
    }
}
