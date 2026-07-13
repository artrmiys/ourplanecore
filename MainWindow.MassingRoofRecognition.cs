using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using OurPlanCore.Controls;
using SkiaSharp;
using Path = System.IO.Path;

namespace OurPlanCore;

public partial class MainWindow
{
    // Auto Roof recognition request prompt, crop context, and marker filters.

    private void QueueRoofRecognitionRequest()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before running Auto Roof.";
            return;
        }

        try
        {
            IReadOnlyList<SmartAiMarker> allMarkers = SmartContextStore.LoadAiMarkers(_currentJob);
            PageInfo? page = _currentPage ?? allMarkers
                .Where(IsRoofRecognitionSourceMarker)
                .Select(ResolveMarkerPage)
                .FirstOrDefault(candidate => candidate != null);

            if (page == null)
            {
                TxtStatus.Text = "Open a sheet or place roof/exterior markers before running Auto Roof.";
                return;
            }

            List<SmartAiMarker> pageMarkers = allMarkers
                .Where(marker => MarkerBelongsToPage(marker, page, _currentJob))
                .Where(IsRoofRecognitionSourceMarker)
                .ToList();

            if (!TrySaveRoofRecognitionCrop(
                    page,
                    pageMarkers,
                    out string cropPath,
                    out SKRect cropRect,
                    out string cropMode,
                    out string error))
            {
                TxtStatus.Text = $"Auto Roof crop skipped: {error}";
                MessageBox.Show(
                    $"Cannot save Auto Roof crop:\n{error}",
                    "Auto Roof",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            List<string> contextCropPaths = RoofRecognitionContextCropPaths(pageMarkers, cropPath);
            string prompt = BuildRoofRecognitionRequestPrompt(
                page,
                pageMarkers,
                cropPath,
                cropRect,
                cropMode,
                contextCropPaths);

            string details =
                "Auto Roof recognition queued.\n\n" +
                "Review mode:\n" +
                "- AI may suggest roof markers only.\n" +
                "- Accepted candidates become AI markers after user review.\n" +
                "- 3D roof geometry is still rebuilt manually with Build 3D Draft.\n\n" +
                "Context:\n" +
                $"- Page: {page.Name}\n" +
                $"- AI crop: {cropPath}\n" +
                $"- PDF crop: {FormatPdfRect(cropRect)}\n" +
                $"- Crop mode: {cropMode}\n" +
                $"- Source roof/footprint markers on page: {pageMarkers.Count}\n" +
                $"- Marker evidence crops attached: {contextCropPaths.Count}\n\n" +
                prompt;

            SmartObservation observation = SmartContextStore.AddObservation(
                _currentJob,
                page,
                "roof_recognition_request",
                details);

            SmartContextStore.AddAiRequest(
                _currentJob,
                page,
                observation,
                "roof_recognition_request",
                prompt,
                cropPath,
                $"Auto Roof source markers: {pageMarkers.Count}",
                contextCropPaths);

            LoadObservationsInbox();
            TxtStatus.Text = $"Queued Auto Roof for {page.Name} with {pageMarkers.Count} source marker(s).";
        }
        catch (Exception ex)
        {
            ShowOperationError("Auto Roof", ex);
        }
    }

    private bool TrySaveRoofRecognitionCrop(
        PageInfo page,
        IReadOnlyList<SmartAiMarker> pageMarkers,
        out string relativePath,
        out SKRect cropRect,
        out string cropMode,
        out string error)
    {
        SKRect requested = RoofRecognitionCropRect(pageMarkers, out cropMode);
        return TrySavePageCrop(
            page,
            requested,
            "roof_recognition",
            cropMode,
            out relativePath,
            out cropRect,
            out error);
    }

    private static SKRect RoofRecognitionCropRect(
        IReadOnlyList<SmartAiMarker> pageMarkers,
        out string cropMode)
    {
        var points = new List<SKPoint>();
        foreach (SmartAiMarker marker in pageMarkers)
        {
            if (marker.PdfRect.Right > marker.PdfRect.Left &&
                marker.PdfRect.Bottom > marker.PdfRect.Top)
            {
                points.Add(new SKPoint(marker.PdfRect.Left, marker.PdfRect.Top));
                points.Add(new SKPoint(marker.PdfRect.Right, marker.PdfRect.Bottom));
            }

            if (!float.IsNaN(marker.PdfPoint.X) && !float.IsNaN(marker.PdfPoint.Y))
                points.Add(new SKPoint(marker.PdfPoint.X, marker.PdfPoint.Y));
        }

        if (points.Count == 0)
        {
            cropMode = "full_page";
            return SKRect.Create(0, 0, RoofRecognitionFullPageSizePt, RoofRecognitionFullPageSizePt);
        }

        cropMode = "marker_bounds";
        SKRect bounds = PointsBounds(points);
        float centerX = (bounds.Left + bounds.Right) / 2f;
        float centerY = (bounds.Top + bounds.Bottom) / 2f;
        float width = Math.Max(bounds.Width + RoofRecognitionContextPaddingPt * 2, RoofRecognitionMinCropSizePt);
        float height = Math.Max(bounds.Height + RoofRecognitionContextPaddingPt * 2, RoofRecognitionMinCropSizePt);
        return SKRect.Create(centerX - width / 2f, centerY - height / 2f, width, height);
    }

    private List<string> RoofRecognitionContextCropPaths(
        IReadOnlyList<SmartAiMarker> pageMarkers,
        string primaryCropPath)
    {
        return pageMarkers
            .Where(marker => IsExplicitRoofRecognitionMarker(marker) || AiMarkerTypeEquals(marker, "exterior_corner"))
            .Select(marker => marker.CropPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !string.Equals(path, primaryCropPath, StringComparison.OrdinalIgnoreCase))
            .Where(AiContextFileExists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    private string BuildRoofRecognitionRequestPrompt(
        PageInfo page,
        IReadOnlyList<SmartAiMarker> pageMarkers,
        string cropPath,
        SKRect cropRect,
        string cropMode,
        IReadOnlyList<string> contextCropPaths)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Queue Auto Roof recognition for this construction plan sheet.");
        sb.AppendLine("Return only reviewable roof marker candidates; do not apply geometry or estimate quantities.");
        sb.AppendLine();
        sb.AppendLine("Allowed marker candidate action.type values:");
        foreach (string markerType in RoofRecognitionMarkerTypes)
            sb.AppendLine($"- {markerType}");
        sb.AppendLine();
        sb.AppendLine("Context:");
        sb.AppendLine($"- Page: {page.Name}");
        sb.AppendLine($"- Main roof crop: {cropPath}");
        sb.AppendLine($"- Crop mode: {cropMode}");
        sb.AppendLine($"- PDF crop: {FormatPdfRect(cropRect)}");
        sb.AppendLine($"- Extra marker crop images: {contextCropPaths.Count}");

        if (_currentJob != null)
        {
            string modelPath = SmartMassingDraftService.ModelPath(_currentJob);
            sb.AppendLine(File.Exists(modelPath)
                ? $"- Existing 3D draft: {Path.GetRelativePath(_currentJob.RootPath, modelPath)}"
                : "- Existing 3D draft: none yet");
        }

        sb.AppendLine();
        sb.AppendLine("Known markers on this page:");
        if (pageMarkers.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (SmartAiMarker marker in pageMarkers.Take(80))
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "- {0}: {1} {2} at x={3:F1}, y={4:F1}; value={5}; note={6}",
                    marker.Id,
                    marker.Type,
                    marker.SampleKind,
                    marker.PdfPoint.X,
                    marker.PdfPoint.Y,
                    string.IsNullOrWhiteSpace(marker.Value) ? "-" : marker.Value.Trim(),
                    string.IsNullOrWhiteSpace(marker.Note) ? "-" : marker.Note.Trim()));
            }
        }

        AppendOpeningProjectionFeedbackToPrompt(sb);

        return sb.ToString();
    }

    private void AppendOpeningProjectionFeedbackToPrompt(StringBuilder sb)
    {
        if (_currentJob == null)
            return;

        IReadOnlyList<SmartMarkerFeedbackRecord> feedback = SmartLearningStore.LoadProjectMarkerFeedback(_currentJob)
            .Where(record => string.Equals(record.EventType, "3d_opening_projection_review", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => record.CreatedAtUtc)
            .Take(12)
            .ToList();
        if (feedback.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("Recent reviewed opening projection feedback:");
        foreach (SmartMarkerFeedbackRecord record in feedback)
        {
            string outcome = string.IsNullOrWhiteSpace(record.Outcome) ? "reviewed" : record.Outcome.Trim();
            string markerType = string.IsNullOrWhiteSpace(record.SourceMarkerType) ? "opening_sample" : record.SourceMarkerType.Trim();
            string page = string.IsNullOrWhiteSpace(record.Page) ? "-" : record.Page.Trim();
            string notes = string.IsNullOrWhiteSpace(record.Notes) ? "-" : record.Notes.Trim();
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "- {0}: {1} on {2}; marker={3}; confidence={4:P0}; notes={5}",
                outcome,
                record.Label,
                page,
                markerType,
                record.Confidence,
                notes));
        }
    }

    private static bool IsRoofRecognitionSourceMarker(SmartAiMarker marker) =>
        AiMarkerTypeEquals(marker, "exterior_corner") ||
        AiMarkerTypeEquals(marker, "wall_height_sample") ||
        IsExplicitRoofRecognitionMarker(marker);

    private static bool IsExplicitRoofRecognitionMarker(SmartAiMarker marker) =>
        RoofRecognitionMarkerTypes.Any(type => AiMarkerTypeEquals(marker, type));

    private static bool AiMarkerTypeEquals(SmartAiMarker marker, string type) =>
        string.Equals(marker.Type, type, StringComparison.OrdinalIgnoreCase);
}
