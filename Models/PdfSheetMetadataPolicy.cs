using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OurPlanCore;

public static class PdfSheetMetadataPolicy
{
    private static readonly JsonSerializerOptions ConfigJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SheetMetadataConfidence ConfidenceLevel(string? value)
    {
        string clean = NormalizeToken(value);
        if (clean.Contains("high", StringComparison.Ordinal) ||
            clean.Contains("exact", StringComparison.Ordinal) ||
            clean.Contains("verified", StringComparison.Ordinal))
        {
            return SheetMetadataConfidence.High;
        }

        if (clean.Contains("medium", StringComparison.Ordinal) ||
            clean.Contains("pdf-text", StringComparison.Ordinal) ||
            clean.Contains("title-block", StringComparison.Ordinal) ||
            clean.Contains("sheet-index", StringComparison.Ordinal) ||
            clean.Contains("drawing-index", StringComparison.Ordinal))
        {
            return SheetMetadataConfidence.Medium;
        }

        return SheetMetadataConfidence.Low;
    }

    public static bool MeetsConfidence(string? value, SheetMetadataConfidence minimum) =>
        ConfidenceLevel(value) >= minimum;

    public static string FieldConfidence(string? fieldConfidence, string? fallback, bool hasValue)
    {
        if (!string.IsNullOrWhiteSpace(fieldConfidence))
            return fieldConfidence.Trim();
        if (!hasValue)
            return "low";
        return string.IsNullOrWhiteSpace(fallback) ? "low" : fallback.Trim();
    }

    public static string FieldSource(string? fieldSource, string? fallback) =>
        string.IsNullOrWhiteSpace(fieldSource)
            ? string.IsNullOrWhiteSpace(fallback) ? "unknown" : fallback.Trim()
            : fieldSource.Trim();

    public static bool IsInferredSource(string? source) =>
        (source ?? "").Contains("infer", StringComparison.OrdinalIgnoreCase) ||
        (source ?? "").Contains("guess", StringComparison.OrdinalIgnoreCase);

    public static bool? ParseSuffixScalePolicy(string? value)
    {
        string clean = (value ?? "").Trim().ToLowerInvariant();
        return clean switch
        {
            "skip" or "no-scale" or "noscale" or "true" => true,
            "allow" or "scale" or "false" => false,
            _ => null,
        };
    }

    public static void PreserveReviewedScaleClear(
        PageInfo page,
        PdfSheetMetadata metadata,
        SheetMetadataConfig config)
    {
        if (!config.PreserveExistingManualScale)
            return;
        if (string.Equals(metadata.ScaleOverrideAction, "Set", StringComparison.OrdinalIgnoreCase) &&
            NormalizeToken(metadata.ScaleSource) == "sheet-override")
            return;

        PdfSheetMetadata? saved = OurPlanCoreJobStore.ReadSourcePdfMetadata(page.FolderPath);
        if (saved == null ||
            !saved.SkipScale ||
            !saved.ScaleSource.StartsWith("manual", StringComparison.OrdinalIgnoreCase) ||
            !saved.SkipReason.Contains("cleared", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        metadata.SkipScale = true;
        metadata.SkipReason = saved.SkipReason;
        metadata.SelectedScaleText = "";
        metadata.ScaleText = "";
        metadata.SelectedScaleRatio = 0;
        metadata.SelectedScaleMetersPerPt = 0;
        metadata.ScaleSource = saved.ScaleSource;
        metadata.ScaleConfidence = "high";
        metadata.ScaleEvidence = string.IsNullOrWhiteSpace(saved.ScaleEvidence)
            ? "existing reviewed scale clear preserved"
            : saved.ScaleEvidence;
    }

    public static string BuildSafeProposedPageName(
        string currentPageName,
        PdfSheetMetadata metadata,
        SheetMetadataConfig config,
        bool existingNameIsManual = false)
    {
        string current = PdfSheetMetadataService.VisibleSheetDisplayName(currentPageName);
        bool exactNameOverride = IsExactRenameOverride(metadata);
        bool exactSuffixOverride = IsExactSuffixOverride(metadata);
        bool explicitSuffixClear = IsExplicitSuffixClear(metadata);
        if (config.PreserveExistingManualName && existingNameIsManual && current.Length > 0 &&
            !exactNameOverride && !exactSuffixOverride && !explicitSuffixClear)
            return current;

        string proposed = PdfSheetMetadataService.VisibleSheetDisplayName(metadata.ProposedPageName());
        if (exactNameOverride || exactSuffixOverride || explicitSuffixClear)
            return proposed;
        string key = metadata.EffectiveSheetKey.Trim().ToLowerInvariant();
        if (!config.PreserveExistingManualSuffix || current.Length == 0 || key.Length == 0)
            return proposed;

        string currentSuffix = ExtractSuffix(current, key);
        if (currentSuffix.Length == 0)
            return proposed;

        string detectedSuffix = NormalizeSuffix(metadata.Suffix);
        string suffixConfidence = PdfSheetMetadataPolicy.FieldConfidence(
            metadata.SuffixConfidence,
            metadata.Confidence,
            detectedSuffix.Length > 0);
        bool preserve = detectedSuffix.Length == 0 ||
                        !MeetsConfidence(suffixConfidence, config.MinimumSuffixConfidence) ||
                        config.ShouldPreserveExistingSuffix(currentSuffix);
        return preserve ? $"{key} {currentSuffix}" : proposed;
    }

    public static bool CanAutoRename(
        PageInfo page,
        PdfSheetMetadata metadata,
        SheetMetadataConfig config,
        string proposedName)
    {
        if (string.IsNullOrWhiteSpace(proposedName) ||
            string.Equals(
                PdfSheetMetadataService.VisibleSheetDisplayName(page.Name),
                PdfSheetMetadataService.VisibleSheetDisplayName(proposedName),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsExactRenameOverride(metadata) || IsExactSuffixOverride(metadata))
            return true;

        string confidence = FieldConfidence(
            metadata.TitleConfidence,
            metadata.Confidence,
            !string.IsNullOrWhiteSpace(metadata.EffectiveSheetKey));
        if (!MeetsConfidence(confidence, config.MinimumRenameConfidence))
            return false;

        if (!string.IsNullOrWhiteSpace(metadata.Suffix) &&
            !MeetsConfidence(
                FieldConfidence(metadata.SuffixConfidence, metadata.Confidence, true),
                config.MinimumSuffixConfidence))
        {
            return false;
        }

        return true;
    }

    public static bool IsExactScaleAutoApplyCandidate(
        PageInfo page,
        PdfSheetMetadata metadata,
        SheetMetadataConfig config)
    {
        if (!metadata.CanApplyScale())
            return false;
        if (config.PreserveExistingManualScale && page.ScaleMetersPerPt > 0)
            return false;

        string confidence = FieldConfidence(
            metadata.ScaleConfidence,
            metadata.Confidence,
            metadata.SelectedScaleMetersPerPt > 0);
        if (!MeetsConfidence(confidence, config.MinimumScaleConfidence))
            return false;

        string source = NormalizeToken(metadata.ScaleSource);
        bool exactSource = source.Contains("sheet-index", StringComparison.Ordinal) ||
                           source.Contains("drawing-index", StringComparison.Ordinal) ||
                           source.Contains("title-block", StringComparison.Ordinal) ||
                           source.Contains("titleblock", StringComparison.Ordinal);
        if (!exactSource)
            return false;

        string evidence = $"{metadata.ScaleEvidence} {metadata.EffectiveScaleText}";
        return !evidence.Contains("as noted", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTrustedHighConfidenceScale(PdfSheetMetadata metadata)
    {
        if (IsExactScaleOverrideSet(metadata) || IsExactScaleOverrideClear(metadata))
            return true;

        string confidence = FieldConfidence(
            metadata.ScaleConfidence,
            metadata.Confidence,
            metadata.SelectedScaleMetersPerPt > 0);
        return ConfidenceLevel(confidence) == SheetMetadataConfidence.High;
    }

    public static bool IsTrustedHighConfidenceRename(PdfSheetMetadata metadata)
    {
        if (IsExactRenameOverride(metadata) || IsExactSuffixOverride(metadata))
            return true;

        if (ConfidenceLevel(FieldConfidence(
                metadata.TitleConfidence,
                metadata.Confidence,
                !string.IsNullOrWhiteSpace(metadata.EffectiveSheetKey))) != SheetMetadataConfidence.High ||
            ConfidenceLevel(FieldConfidence(
                metadata.SuffixConfidence,
                metadata.Confidence,
                !string.IsNullOrWhiteSpace(metadata.Suffix))) != SheetMetadataConfidence.High)
        {
            return false;
        }

        string titleSource = NormalizeToken(metadata.TitleSource);
        string suffixSource = NormalizeToken(metadata.SuffixSource);
        return !IsReviewOnlySource(titleSource) && !IsReviewOnlySource(suffixSource);
    }

    public static bool IsExactRenameOverride(PdfSheetMetadata metadata) =>
        metadata.RenameOverrideApplied &&
        !string.IsNullOrWhiteSpace(metadata.RenameCandidate);

    public static bool IsExactSuffixOverride(PdfSheetMetadata metadata)
    {
        string action = (metadata.SuffixOverrideAction ?? "").Trim();
        return NormalizeToken(metadata.SuffixSource) == "sheet-override" &&
               (action.Equals("Set", StringComparison.OrdinalIgnoreCase) ||
                action.Equals("Clear", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsExplicitSuffixClear(PdfSheetMetadata metadata)
    {
        if (!metadata.SuffixExplicitClear)
            return false;
        string source = NormalizeToken(metadata.SuffixSource);
        return source is "sheet-override" or "configured-rule";
    }

    public static bool IsExactScaleOverrideSet(PdfSheetMetadata metadata) =>
        string.Equals(metadata.ScaleOverrideAction, "Set", StringComparison.OrdinalIgnoreCase) &&
        NormalizeToken(metadata.ScaleSource) == "sheet-override" &&
        metadata.CanApplyScale();

    public static bool IsExactScaleOverrideClear(PdfSheetMetadata metadata) =>
        string.Equals(metadata.ScaleOverrideAction, "Clear", StringComparison.OrdinalIgnoreCase) &&
        NormalizeToken(metadata.ScaleSource) == "sheet-override" &&
        metadata.SkipScale;

    public static bool IsExistingNameMarkedManual(PageInfo page)
    {
        PdfSheetMetadata? saved = OurPlanCoreJobStore.ReadSourcePdfMetadata(page.FolderPath);
        if (saved == null)
            return false;

        if (IsManualSource(saved.Source) ||
            IsManualSource(saved.Confidence) ||
            IsManualSource(saved.TitleSource))
        {
            return true;
        }

        string current = PdfSheetMetadataService.VisibleSheetDisplayName(page.Name);
        string savedProposal = PdfSheetMetadataService.VisibleSheetDisplayName(saved.ProposedPageName());
        if (current.Length > 0 &&
            savedProposal.Length > 0 &&
            !string.Equals(current, savedProposal, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string visibleSuffix = ExtractSuffix(current, saved.EffectiveSheetKey);
        return visibleSuffix.Length > 0 &&
               !string.Equals(visibleSuffix, saved.Suffix, StringComparison.OrdinalIgnoreCase);
    }

    public static string ExtractSuffix(string? pageName, string? preferredSheetKey = null)
    {
        string clean = PdfSheetMetadataService.VisibleSheetDisplayName(pageName ?? "").ToLowerInvariant();
        string key = (preferredSheetKey ?? "").Trim().ToLowerInvariant();
        if (key.Length > 0)
        {
            if (string.Equals(clean, key, StringComparison.OrdinalIgnoreCase))
                return "";
            if (clean.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase))
                return NormalizeSuffix(clean[(key.Length + 1)..]);
        }

        int separator = clean.IndexOf(' ');
        return separator < 0 ? "" : NormalizeSuffix(clean[(separator + 1)..]);
    }

    public static string ExtractSheetKey(string? pageName, string? preferredSheetKey = null)
    {
        string preferred = (preferredSheetKey ?? "").Trim().ToLowerInvariant();
        if (preferred.Length > 0)
            return preferred;

        string clean = PdfSheetMetadataService.VisibleSheetDisplayName(pageName ?? "").ToLowerInvariant();
        int separator = clean.IndexOf(' ');
        return separator < 0 ? clean : clean[..separator];
    }

    public static bool SameScale(double left, double right) =>
        Math.Abs(left - right) <= 0.000000001;

    public static string ScaleDisplay(double scaleMetersPerPt) =>
        scaleMetersPerPt > 0
            ? PdfSheetMetadataService.FormatImperialScale(scaleMetersPerPt)
            : "";

    public static string ReviewScaleText(PdfSheetMetadata metadata)
    {
        string detected = metadata.EffectiveScaleText.Trim();
        if (!string.IsNullOrWhiteSpace(detected) &&
            PdfSheetMetadataService.TryParseScaleMetersPerPt(detected, out double parsed) &&
            SameScale(parsed, metadata.SelectedScaleMetersPerPt))
        {
            return detected;
        }

        return PdfSheetMetadataService.FormatImperialScaleExact(metadata.SelectedScaleMetersPerPt);
    }

    public static string AppliedScaleText(string reviewedText, double appliedScaleMetersPerPt)
    {
        string reviewed = (reviewedText ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(reviewed) &&
            PdfSheetMetadataService.TryParseScaleMetersPerPt(reviewed, out double parsed) &&
            SameScale(parsed, appliedScaleMetersPerPt))
        {
            return reviewed;
        }

        return PdfSheetMetadataService.FormatImperialScaleExact(appliedScaleMetersPerPt);
    }

    public static void PersistKeptScaleDecision(PdfSheetMetadata metadata, double scaleMetersPerPt)
    {
        bool keepDetectedNoScale = scaleMetersPerPt <= 0 && metadata.SkipScale;
        string keptReason = metadata.SkipReason;
        string keptText = metadata.EffectiveScaleText;

        metadata.SelectedScaleMetersPerPt = Math.Max(0, scaleMetersPerPt);
        metadata.SelectedScaleRatio = scaleMetersPerPt > 0
            ? scaleMetersPerPt / ViewportConstants.PdfPointMeters
            : 0;
        metadata.SelectedScaleText = scaleMetersPerPt > 0
            ? AppliedScaleText(keptText, scaleMetersPerPt)
            : "";
        metadata.ScaleText = metadata.SelectedScaleText;
        metadata.ScaleSource = "manual-review-keep";
        metadata.ScaleConfidence = "high";

        if (keepDetectedNoScale)
        {
            metadata.SkipScale = true;
            metadata.SkipReason = string.IsNullOrWhiteSpace(keptReason)
                ? "manual-review: detected no-scale decision kept"
                : keptReason;
            metadata.ScaleEvidence = $"detected no-scale decision kept: {metadata.SkipReason}";
            return;
        }

        metadata.SkipScale = false;
        metadata.SkipReason = scaleMetersPerPt > 0
            ? ""
            : "manual-review: existing empty scale kept";
        metadata.ScaleEvidence = scaleMetersPerPt > 0
            ? $"existing page scale kept: {metadata.SelectedScaleText}"
            : "existing empty page scale kept";
        if (scaleMetersPerPt > 0)
            metadata.SuffixScalePolicy = "allow";
    }

    public static string BuildObservationKey(string fingerprint, int pdfPage, string detectorVersion)
        => BuildObservationKey(fingerprint, pdfPage, detectorVersion, "");

    public static string BuildObservationKey(
        string fingerprint,
        int pdfPage,
        string detectorVersion,
        string detectorConfigFingerprint)
    {
        string cleanFingerprint = string.IsNullOrWhiteSpace(fingerprint) ? "unknown" : fingerprint.Trim();
        string cleanVersion = string.IsNullOrWhiteSpace(detectorVersion) ? "legacy-v1" : detectorVersion.Trim();
        string cleanConfig = string.IsNullOrWhiteSpace(detectorConfigFingerprint)
            ? "legacy-config"
            : detectorConfigFingerprint.Trim();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{cleanFingerprint}|page:{Math.Max(0, pdfPage)}|detector:{cleanVersion}|config:{cleanConfig}");
    }

    public static string ConfigFingerprint(SheetMetadataConfig config)
    {
        JsonElement element = JsonSerializer.SerializeToElement(
            config ?? SheetMetadataConfig.BuildDefault(),
            ConfigJsonOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
               }))
        {
            WriteCanonicalJson(writer, element);
        }

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in element.EnumerateObject().OrderBy(
                         property => property.Name,
                         StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonicalJson(writer, property.Value);
            }
            writer.WriteEndObject();
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (JsonElement item in element.EnumerateArray())
                WriteCanonicalJson(writer, item);
            writer.WriteEndArray();
            return;
        }

        element.WriteTo(writer);
    }

    private static string NormalizeSuffix(string? value) =>
        string.Join(' ', (value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeToken(string? value) =>
        (value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Replace('_', '-')
            .Replace(' ', '-');

    private static bool IsManualSource(string? value) =>
        NormalizeToken(value).StartsWith("manual", StringComparison.Ordinal);

    private static bool IsReviewOnlySource(string source) =>
        source.Contains("learned", StringComparison.Ordinal) ||
        source.Contains("gpt", StringComparison.Ordinal) ||
        source.Contains("infer", StringComparison.Ordinal) ||
        source is "body" or "filename" or "numeric-fallback" or "none";
}
