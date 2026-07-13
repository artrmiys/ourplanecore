using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OurPlanCore.Controls;
using SkiaSharp;


namespace OurPlanCore;

public partial class MainWindow
{
    // AI observation navigation, crop, marker, and file actions.

    private bool CanGoToObservationPage(ObservationDisplayItem item) =>
        _currentJob != null && !string.IsNullOrWhiteSpace(item.Page) && FindPageByName(item.Page) != null;

    private void GoToObservationPage(ObservationDisplayItem item)
    {
        if (FindPageByName(item.Page) is { } page)
            SelectPageByFolder(page.FolderPath);
    }

    private bool CanOpenObservationCrop(ObservationDisplayItem item) =>
        _currentJob != null &&
        !string.IsNullOrWhiteSpace(item.CropRelativePath) &&
        File.Exists(ObservationCropPath(item));

    private void OpenObservationCrop(ObservationDisplayItem item)
    {
        string path = ObservationCropPath(item);
        if (!File.Exists(path))
        {
            TxtStatus.Text = "AI crop file is missing.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    private void OpenObservationCropFolder(ObservationDisplayItem item)
    {
        string path = ObservationCropPath(item);
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
            OpenFolderInExplorer(folder);
    }

    private string ObservationCropPath(ObservationDisplayItem item)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(item.CropRelativePath))
            return "";

        return Path.GetFullPath(Path.Combine(_currentJob.AIContextRoot, item.CropRelativePath));
    }

    private bool CanOpenAiMarkerFile(ObservationDisplayItem item) =>
        _currentJob != null && File.Exists(AiMarkerPath(item));

    private bool CanOpenCropBookmarkFile(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiCropBookmark? bookmark = SmartContextStore.FindCropBookmarkByObservation(_currentJob, item.Observation.Id);
        return bookmark != null && File.Exists(SmartContextStore.CropBookmarkPath(_currentJob, bookmark.Id));
    }

    private void OpenCropBookmarkFile(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiCropBookmark? bookmark = SmartContextStore.FindCropBookmarkByObservation(_currentJob, item.Observation.Id);
        if (bookmark == null)
        {
            TxtStatus.Text = "Crop bookmark JSON is missing.";
            return;
        }

        OpenJsonFile(
            SmartContextStore.CropBookmarkPath(_currentJob, bookmark.Id),
            "Crop bookmark JSON is missing.");
    }

    private bool CanEditAiMarker(ObservationDisplayItem item) =>
        _currentJob != null && SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id) != null;

    private void EditAiMarker(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id);
        if (marker == null)
        {
            TxtStatus.Text = "AI marker JSON is missing.";
            LoadObservationsInbox();
            RefreshAiMarkersOverlay();
            return;
        }

        AiMarkerInput? input = ShowAiMarkerDialog(marker);
        if (input == null)
            return;

        marker.Type = NormalizeAiMarkerType(input.MarkerType);
        marker.SampleKind = NormalizeAiMarkerSampleKind(input.SampleKind);
        marker.Value = input.Value.Trim();
        marker.Note = input.Note.Trim();

        SmartContextStore.SaveAiMarker(_currentJob, marker);
        RefreshAiMarkersOverlay();
        LoadObservationsInbox();
        TxtStatus.Text = $"Updated AI marker {marker.Type}.";
    }

    private void DeleteAiMarker(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id);
        if (marker == null)
        {
            TxtStatus.Text = "AI marker JSON is already missing.";
            LoadObservationsInbox();
            RefreshAiMarkersOverlay();
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            $"Delete marker '{marker.Type}' from the active marker set?\n\nCrop evidence and the observation log stay in AI_Context.",
            "Delete AI Marker",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        SmartContextStore.DeleteAiMarker(_currentJob, marker.Id);
        RefreshAiMarkersOverlay();
        LoadObservationsInbox();
        TxtStatus.Text = $"Deleted AI marker {marker.Type}; evidence files were kept.";
    }

    private bool CanHideAiMarkerType(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id);
        return marker != null && !_hiddenAiMarkerTypes.Contains(marker.Type);
    }

    private void HideAiMarkerType(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id);
        if (marker == null)
        {
            TxtStatus.Text = "AI marker JSON is missing.";
            return;
        }

        _hiddenAiMarkerTypes.Add(marker.Type);
        SavePersistedMarkerVisibility();
        RefreshAiMarkersOverlay();
        TxtStatus.Text = $"Hidden AI marker type '{marker.Type}' from the canvas overlay and saved it for this job.";
    }

    private void ShowAllMarkerTypes()
    {
        _hiddenAiMarkerTypes.Clear();
        SavePersistedMarkerVisibility();
        RefreshAiMarkersOverlay();
        TxtStatus.Text = "Showing all AI marker types on the canvas overlay and saved it for this job.";
    }

    private void OpenAiMarkerFile(ObservationDisplayItem item)
    {
        string path = AiMarkerPath(item);
        if (!File.Exists(path))
        {
            TxtStatus.Text = "AI marker JSON is missing.";
            return;
        }

        OpenJsonFile(path, "AI marker JSON is missing.");
    }

    private bool CanFindSimilarFromMarker(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id);
        return marker != null && AiContextFileExists(marker.CropPath);
    }

    private void QueueFindSimilarFromMarker(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id);
        if (marker == null)
        {
            TxtStatus.Text = "AI marker JSON is missing.";
            return;
        }

        if (string.IsNullOrWhiteSpace(marker.CropPath))
        {
            TxtStatus.Text = "AI marker has no crop for Find Similar.";
            return;
        }

        if (!AiContextFileExists(marker.CropPath))
        {
            TxtStatus.Text = "AI marker crop file is missing.";
            return;
        }

        PageInfo? page = ResolveMarkerPage(marker);
        string nearbyContextDetails = BuildFindSimilarNearbyContext(
            marker,
            page,
            out string nearbyCropPath,
            out SKRect nearbyCropRect);
        string prompt = BuildFindSimilarMarkerRequestPrompt(
            marker,
            item.Observation,
            nearbyCropPath,
            nearbyCropRect,
            nearbyContextDetails);
        string details =
            "Find Similar From Marker queued.\n\n" +
            "Source marker:\n" +
            $"- Id: {marker.Id}\n" +
            $"- Type: {marker.Type}\n" +
            $"- Sample kind: {marker.SampleKind}\n" +
            $"- AI crop: {marker.CropPath}\n\n" +
            nearbyContextDetails +
            "\n" +
            prompt;

        SmartObservation observation = SmartContextStore.AddObservation(
            _currentJob,
            page,
            "find_similar_marker_request",
            details);

        SmartContextStore.AddAiRequest(
            _currentJob,
            page,
            observation,
            "find_similar_marker_request",
            prompt,
            marker.CropPath,
            $"Source marker id: {marker.Id}",
            string.IsNullOrWhiteSpace(nearbyCropPath) ? [] : [nearbyCropPath]);

        LoadObservationsInbox();
        TxtStatus.Text = string.IsNullOrWhiteSpace(nearbyCropPath)
            ? $"Queued Find Similar From Marker for {marker.Type} ({marker.Id}); nearby crop was not available."
            : $"Queued Find Similar From Marker for {marker.Type} ({marker.Id}) with nearby sheet context.";
    }

    private PageInfo? ResolveMarkerPage(SmartAiMarker marker)
    {
        if (_currentJob == null)
            return null;

        if (!string.IsNullOrWhiteSpace(marker.PageFolder))
        {
            string folder = Path.IsPathFullyQualified(marker.PageFolder)
                ? marker.PageFolder
                : Path.GetFullPath(Path.Combine(_currentJob.RootPath, marker.PageFolder));
            PageInfo? page = OurPlanCoreJobStore.TryReadPage(folder);
            if (page != null)
                return page;
        }

        return FindPageByName(marker.Page);
    }

    private bool AiContextFileExists(string path)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(path))
            return false;

        string fullPath = Path.IsPathFullyQualified(path)
            ? path
            : Path.GetFullPath(Path.Combine(_currentJob.AIContextRoot, path));
        return File.Exists(fullPath);
    }

    private string BuildFindSimilarNearbyContext(
        SmartAiMarker marker,
        PageInfo? page,
        out string nearbyCropPath,
        out SKRect nearbyCropRect)
    {
        nearbyCropPath = "";
        nearbyCropRect = SKRect.Empty;

        if (page == null)
            return "Nearby sheet context:\n- Unavailable: marker page could not be resolved.\n";

        SKRect requested = FindSimilarNearbyCropRect(marker);
        if (!TrySavePageCrop(
                page,
                requested,
                "find_similar_nearby",
                $"{marker.Type}_{marker.Id}",
                out nearbyCropPath,
                out nearbyCropRect,
                out string error))
        {
            return
                "Nearby sheet context:\n" +
                $"- Unavailable: {error}\n";
        }

        return
            "Nearby sheet context:\n" +
            $"- Nearby crop: {nearbyCropPath}\n" +
            $"- Nearby PDF crop: {FormatPdfRect(nearbyCropRect)}\n";
    }

    private static SKRect FindSimilarNearbyCropRect(SmartAiMarker marker)
    {
        float centerX = marker.PdfPoint.X;
        float centerY = marker.PdfPoint.Y;
        float width = FindSimilarNearbyContextMinSizePt;
        float height = FindSimilarNearbyContextMinSizePt;

        if (marker.PdfRect.Right > marker.PdfRect.Left && marker.PdfRect.Bottom > marker.PdfRect.Top)
        {
            centerX = (marker.PdfRect.Left + marker.PdfRect.Right) / 2f;
            centerY = (marker.PdfRect.Top + marker.PdfRect.Bottom) / 2f;
            width = Math.Max(
                marker.PdfRect.Right - marker.PdfRect.Left + FindSimilarNearbyContextPaddingPt * 2,
                FindSimilarNearbyContextMinSizePt);
            height = Math.Max(
                marker.PdfRect.Bottom - marker.PdfRect.Top + FindSimilarNearbyContextPaddingPt * 2,
                FindSimilarNearbyContextMinSizePt);
        }

        return SKRect.Create(centerX - width / 2f, centerY - height / 2f, width, height);
    }

    private static string BuildFindSimilarMarkerRequestPrompt(
        SmartAiMarker marker,
        SmartObservation sourceObservation,
        string nearbyCropPath,
        SKRect nearbyCropRect,
        string nearbyContextDetails)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Find similar plan conditions from this saved AI marker.");
        sb.AppendLine("Create reviewable AI action drafts only. Do not apply measurements automatically.");
        sb.AppendLine("Use the marker crop as the visual example and the nearby sheet crop as local context.");
        sb.AppendLine();
        sb.AppendLine("Source marker id:");
        sb.AppendLine(marker.Id);
        sb.AppendLine();
        sb.AppendLine("Marker summary:");
        sb.AppendLine($"- Page: {marker.Page}");
        sb.AppendLine($"- Type: {marker.Type}");
        sb.AppendLine($"- Sample kind: {marker.SampleKind}");
        sb.AppendLine($"- PDF point: {marker.PdfPoint.X:F1}, {marker.PdfPoint.Y:F1}");
        sb.AppendLine($"- PDF crop: left={marker.PdfRect.Left:F1}, top={marker.PdfRect.Top:F1}, right={marker.PdfRect.Right:F1}, bottom={marker.PdfRect.Bottom:F1}");
        sb.AppendLine($"- AI crop: {marker.CropPath}");
        if (!string.IsNullOrWhiteSpace(nearbyCropPath))
        {
            sb.AppendLine($"- Nearby sheet crop: {nearbyCropPath}");
            sb.AppendLine($"- Nearby sheet PDF crop: {FormatPdfRect(nearbyCropRect)}");
        }
        if (!string.IsNullOrWhiteSpace(marker.Value))
            sb.AppendLine($"- Value: {marker.Value}");
        if (!string.IsNullOrWhiteSpace(marker.Note))
            sb.AppendLine($"- Note: {marker.Note}");
        sb.AppendLine();
        sb.Append(nearbyContextDetails.TrimEnd());
        sb.AppendLine();
        sb.AppendLine("Source observation:");
        sb.AppendLine(sourceObservation.Text);
        return sb.ToString();
    }

    private void OpenJsonFile(string path, string missingStatus)
    {
        if (!File.Exists(path))
        {
            TxtStatus.Text = missingStatus;
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }
}
