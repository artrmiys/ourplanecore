using System;
using System.IO;
using System.Linq;

namespace OurPlanCore;

public static partial class PdfSheetMetadataService
{
    public static SmartSheetLearningRecord BuildLearningRecord(
        PageInfo page,
        PdfSheetMetadata metadata,
        string userOutcome,
        string note,
        SmartSheetLearningDecision? final = null,
        SmartSheetLearningDecision? detected = null,
        string nameOutcome = "not_reviewed",
        string suffixOutcome = "not_reviewed",
        string scaleOutcome = "not_reviewed")
    {
        SmartSheetLearningDecision detection = detected ?? DetectedDecision(metadata);
        string sourcePdf = string.IsNullOrWhiteSpace(metadata.PdfPath) ? page.PdfPath : metadata.PdfPath;
        string fingerprint = BuildPdfFingerprint(sourcePdf);
        string detectorVersion = string.IsNullOrWhiteSpace(metadata.DetectorVersion)
            ? "legacy-v1"
            : metadata.DetectorVersion.Trim();
        string detectorConfigFingerprint = string.IsNullOrWhiteSpace(metadata.DetectorConfigFingerprint)
            ? PdfSheetMetadataPolicy.ConfigFingerprint(SheetMetadataRulesService.Active)
            : metadata.DetectorConfigFingerprint.Trim();

        return new SmartSheetLearningRecord
        {
            SchemaVersion = 2,
            EventType = "sheet_feedback",
            Source = string.IsNullOrWhiteSpace(metadata.Source) ? "pdf-text" : metadata.Source,
            UserOutcome = userOutcome,
            Reviewed = true,
            DetectorVersion = detectorVersion,
            DetectorConfigFingerprint = detectorConfigFingerprint,
            PdfFingerprint = fingerprint,
            ObservationKey = PdfSheetMetadataPolicy.BuildObservationKey(
                fingerprint,
                metadata.PageIndex,
                detectorVersion,
                detectorConfigFingerprint),
            NameOutcome = nameOutcome,
            SuffixOutcome = suffixOutcome,
            ScaleOutcome = scaleOutcome,
            SourcePdf = sourcePdf,
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

    public static SmartSheetLearningDecision DetectedDecision(PdfSheetMetadata metadata) =>
        new()
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

    public static SmartSheetLearningDecision FinalDecision(
        PageInfo page,
        PdfSheetMetadata metadata,
        string pageName,
        double scaleMetersPerPt) =>
        new()
        {
            PageName = pageName,
            SheetLabel = metadata.SheetLabel,
            SheetKey = PdfSheetMetadataPolicy.ExtractSheetKey(pageName, metadata.EffectiveSheetKey),
            SheetTitle = metadata.SheetTitle,
            Suffix = PdfSheetMetadataPolicy.ExtractSuffix(pageName, metadata.EffectiveSheetKey),
            SkipScale = scaleMetersPerPt <= 0 && metadata.SkipScale,
            ScaleText = scaleMetersPerPt > 0
                ? PdfSheetMetadataPolicy.AppliedScaleText(metadata.EffectiveScaleText, scaleMetersPerPt)
                : "",
            ScaleMetersPerPt = scaleMetersPerPt,
            Confidence = "manual_review",
        };

    public static string BuildPdfFingerprint(string? pdfPath)
    {
        if (!string.IsNullOrWhiteSpace(pdfPath) && File.Exists(pdfPath))
            return PdfPreviewRenderCache.BuildPdfFingerprint(new FileInfo(pdfPath));

        return string.IsNullOrWhiteSpace(pdfPath)
            ? "unknown-pdf"
            : Path.GetFullPath(pdfPath).ToLowerInvariant();
    }
}
