using System;
using System.Collections.Generic;
using System.Globalization;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    private enum PagesClipboardMode { Copy, Cut }
    private sealed record PagesClipboardEntry(string SourcePath, bool IsPage);
    private sealed record PagesClipboard(IReadOnlyList<PagesClipboardEntry> Entries, PagesClipboardMode Mode);
    private enum TakeoffsClipboardMode { Copy, Cut }
    private sealed record TakeoffsClipboardEntry(string SourcePath, bool IsItem);
    private sealed record TakeoffsClipboard(IReadOnlyList<TakeoffsClipboardEntry> Entries, TakeoffsClipboardMode Mode);

    private sealed record BulkTakeoffPropertiesEdit(
        bool ApplyColor,
        string Color,
        bool ApplyUnitPrice,
        double UnitPrice,
        bool ApplyNotes,
        string Notes);
    private sealed record JoistTakeoffEdit(
        bool Enabled,
        string JoistType,
        double SpacingInches,
        double DirectionDegrees,
        bool DirectionFollowsAreaRotation,
        bool AddEndJoist,
        string Pitch,
        string LengthRounding,
        bool ShowLabels,
        bool DetailedLabels);
    private enum MeasurementPasteMode { SameTakeoffs, NewTakeoffs }
    private sealed record MeasurementJoistClipboard(
        bool Enabled,
        string JoistType,
        double SpacingInches,
        double DirectionDegrees,
        bool DirectionLocked,
        bool DirectionFollowsAreaRotation,
        bool AddEndJoist,
        string Pitch,
        string LengthRounding,
        bool ShowLabels,
        bool DetailedLabels);
    private sealed record TakeoffJoistClipboard(
        bool Enabled,
        string JoistType,
        double SpacingInches,
        double DirectionDegrees,
        bool DirectionFollowsAreaRotation,
        bool AddEndJoist,
        string Pitch,
        string LengthRounding,
        bool ShowLabels,
        bool DetailedLabels);
    private sealed record MeasurementClipboardEntry(
        string MeasurementType,
        string MeasurementName,
        string MeasurementNotes,
        string MeasurementColor,
        string MeasurementCountSymbol,
        IReadOnlyList<SKPoint> Points,
        IReadOnlyList<IReadOnlyList<SKPoint>> Holes,
        string SourcePageFolder,
        double ScaleMetersPerPt,
        string SourceTakeoffFolder,
        string SourceTakeoffName,
        string SourceTakeoffColor,
        string SourceTakeoffCountSymbol,
        double SourceTakeoffUnitPrice,
        string SourceTakeoffNotes,
        MeasurementJoistClipboard MeasurementJoist,
        TakeoffJoistClipboard SourceTakeoffJoist);
    private sealed record MeasurementClipboard(IReadOnlyList<MeasurementClipboardEntry> Entries);

    private sealed record TakeoffMeasurementNode(TakeoffItem Item, Measurement Measurement);
    private sealed record TakeoffSectionDrag(IReadOnlyList<TakeoffMeasurementNode> Nodes);
    private sealed record PageTakeoffNode(PageInfo Page, TakeoffItem Takeoff);
    private sealed record PageOverlayNode(PageInfo Page, string OverlayName);
    private sealed record PageTakeoffLegendDrag(string PageFolder, IReadOnlyList<string> TakeoffFolders);
    private sealed record PageBookmarkRow(
        string Name,
        string Page,
        string View,
        int Order,
        PageBookmark Bookmark);
    private sealed record AiMarkerInput(string MarkerType, string SampleKind, string Value, string Note);
    private sealed record MarkerSetInput(string Name, string Description);
    private sealed record PdfMetadataPageResult(PageInfo Page, bool Ok, PdfSheetMetadata? Metadata, string Error);
    private sealed record PdfMetadataApplySummary(int Renamed, int Scaled, int Failed);
    private sealed record MaterialReportSheetMetadataSummary(int Analyzed, int Renamed, int Scaled, int Failed);

    private sealed class PageTabState(string pageFolder, string pageName)
    {
        public string PageFolder { get; set; } = pageFolder;
        public string PageName { get; set; } = pageName;
        public PdfViewport.ViewState? ViewState { get; set; }
    }

    private sealed class ObservationDisplayItem
    {
        public string TypeShort   { get; }
        public string BadgeColor  { get; }
        public string Page        { get; }
        public string TextPreview { get; }
        public string QualityPreview { get; }
        public string TimeDisplay { get; }
        public string CropRelativePath { get; }
        public SmartObservation Observation { get; }

        public ObservationDisplayItem(
            SmartObservation obs,
            string statusPrefix = "",
            SmartAiMarker? marker = null,
            string markerQuality = "")
        {
            Observation = obs;
            Page = obs.Page;
            CropRelativePath = ExtractAiCropRelativePath(obs.Text);

            string raw = marker != null
                ? FormatMarkerPreview(marker)
                : (obs.Text ?? "").Replace('\r', ' ').Replace('\n', ' ');
            if (!string.IsNullOrWhiteSpace(markerQuality))
                raw = $"{raw} | {markerQuality.Trim()}";
            if (!string.IsNullOrWhiteSpace(statusPrefix))
                raw = statusPrefix + raw;
            QualityPreview = markerQuality;
            TextPreview = raw.Length > 120 ? raw[..117] + "…" : raw;

            (TypeShort, BadgeColor) = obs.Type switch
            {
                "ai_request"                    => ("Ask AI",      "#1565C0"),
                "text_read_request"             => ("Read Text",   "#00796B"),
                "pending_check"                 => ("Check",       "#E65100"),
                "trace_request"                 => ("SmartTrace",  "#2E7D32"),
                "trace_area_request"            => ("Trace Area",  "#2E7D32"),
                "missed_takeoff_check"          => ("Missed",      "#C62828"),
                "crop_context"                  => ("Crop",        "#4E342E"),
                "ai_marker"                     => ("Marker",      "#00897B"),
                "takeoff_suggestion"            => ("Suggestion",  "#6A1B9A"),
                "measurement_explain_request"   => ("Explain",     "#1565C0"),
                "find_similar_request"          => ("Find Similar","#00796B"),
                "find_similar_marker_request"   => ("Find Marker", "#00796B"),
                "roof_recognition_request"      => ("Auto Roof",   "#455A64"),
                "pdf_layer_context"             => ("PDF Layer",   "#37474F"),
                "pdf_layer_ai_request"          => ("Layer AI",    "#37474F"),
                "measurement_link_request"      => ("Link",        "#546E7A"),
                "crop_bookmark_request"         => ("Bookmark",    "#00695C"),
                "pdf_sheet_metadata_fallback"   => ("Sheet Meta",  "#5D4037"),
                "measurement_note"              => ("Note",        "#546E7A"),
                _                               => ("Manual",      "#546E7A"),
            };

            if (DateTime.TryParse(obs.CreatedAtUtc, null,
                    DateTimeStyles.RoundtripKind, out DateTime dt))
                TimeDisplay = dt.ToLocalTime().ToString("MM/dd HH:mm");
            else
                TimeDisplay = obs.CreatedAtUtc.Length > 16
                    ? obs.CreatedAtUtc[..16] : obs.CreatedAtUtc;
        }

        private static string FormatMarkerPreview(SmartAiMarker marker)
        {
            var parts = new List<string> { $"Marker {marker.Type}", marker.SampleKind };
            if (!string.IsNullOrWhiteSpace(marker.Value))
                parts.Add(marker.Value.Trim());
            if (!string.IsNullOrWhiteSpace(marker.Note))
                parts.Add(marker.Note.Trim());
            return string.Join(" | ", parts);
        }

        private static string ExtractAiCropRelativePath(string text)
        {
            foreach (string line in (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("- AI crop:", StringComparison.OrdinalIgnoreCase))
                    continue;

                return trimmed["- AI crop:".Length..].Trim();
            }

            return "";
        }
    }

    private sealed record EstimateDisplayRow(
        string Item,
        string Page,
        string Sections,
        string Quantity,
        string Unit,
        string UnitPrice,
        string Cost,
        string Notes,
        TakeoffItem? Takeoff,
        Measurement? Measurement);

    private sealed record TakeoffManagerRow(
        string Name,
        string Type,
        string Sections,
        string Total,
        string Unit,
        string UnitPrice,
        string Cost,
        string Notes,
        string Folder,
        TakeoffItem Item);

    private sealed class MassingMarkerReviewRow
    {
        public string MarkerId { get; init; } = "";
        public string Role { get; init; } = "";
        public string Type { get; init; } = "";
        public string Page { get; init; } = "";
        public string PdfPoint { get; init; } = "";
        public string DraftPoint { get; init; } = "";
        public string Status { get; init; } = "";
        public SmartAiMarker? Marker { get; init; }
        public bool HasCrop { get; init; }
    }

    private sealed record Massing3DObjectInfo(
        string Id,
        string Kind,
        string Label,
        IReadOnlyList<string> SourceMarkerIds,
        string Notes);
}
