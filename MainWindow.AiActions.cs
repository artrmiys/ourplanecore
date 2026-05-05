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
using SmartTakeoffs.Controls;
using SkiaSharp;

namespace SmartTakeoffs;

public partial class MainWindow
{
    private void CreateMarkerSetFromCurrentFilter()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before creating marker sets.";
            return;
        }

        List<SmartAiMarker> markers = LoadMarkersForCurrentFilter(includeHiddenTypes: true);
        if (markers.Count == 0)
        {
            TxtStatus.Text = "No AI markers match the current filters.";
            MessageBox.Show(
                "No active AI markers match the current type/sample filters.",
                "Create Marker Set",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        MarkerSetInput? input = ShowMarkerSetDialog(DefaultMarkerSetName(markers), markers.Count);
        if (input == null)
            return;

        SmartAiMarkerSet set = SmartContextStore.SaveAiMarkerSet(
            _currentJob,
            input.Name,
            input.Description,
            SelectedMarkerTypeFilterForMarkers(),
            SelectedMarkerSampleFilter(),
            markers);
        TxtStatus.Text = $"Saved marker set '{set.Name}' with {set.MarkerCount} markers.";
    }

    private bool CanManageMarkerSets() =>
        _currentJob != null && SmartContextStore.LoadAiMarkerSets(_currentJob).Count > 0;

    private void ManageMarkerSets()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before managing marker sets.";
            return;
        }

        IReadOnlyList<SmartAiMarkerSet> sets = SmartContextStore.LoadAiMarkerSets(_currentJob);
        if (sets.Count == 0)
        {
            TxtStatus.Text = "No marker sets saved yet.";
            MessageBox.Show(
                "Create a marker set from the current marker filters first.",
                "Marker Sets",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new MarkerSetsDialog(sets) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
            return;

        MarkerSetsDialogResult result = dialog.Result;
        SmartAiMarkerSet set = result.MarkerSet;
        try
        {
            switch (result.Action)
            {
                case MarkerSetsDialogAction.Apply:
                    ApplyMarkerSetFilter(set);
                    TxtStatus.Text = $"Applied marker set '{set.Name}' ({set.MarkerCount} markers).";
                    break;

                case MarkerSetsDialogAction.Rename:
                    set.Name = result.Name;
                    set.Description = result.Description;
                    SmartContextStore.SaveAiMarkerSet(_currentJob, set);
                    TxtStatus.Text = $"Renamed marker set to '{set.Name}'.";
                    break;

                case MarkerSetsDialogAction.Delete:
                    DeleteMarkerSet(set);
                    break;

                case MarkerSetsDialogAction.OpenJson:
                    OpenJsonFile(
                        SmartContextStore.AiMarkerSetPath(_currentJob, set.Id),
                        "Marker set JSON is missing.");
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowOperationError("Marker Sets", ex);
        }
    }

    private void ApplyMarkerSetFilter(SmartAiMarkerSet set)
    {
        string typeFilter = string.IsNullOrWhiteSpace(set.TypeFilter)
            ? MarkerTypeFilterAllMarkers
            : set.TypeFilter.Trim();
        string sampleFilter = string.IsNullOrWhiteSpace(set.SampleKindFilter)
            ? MarkerSampleFilterAny
            : set.SampleKindFilter.Trim();

        if (string.Equals(typeFilter, MarkerTypeFilterAllInbox, StringComparison.OrdinalIgnoreCase))
            typeFilter = MarkerTypeFilterAllMarkers;

        SelectComboValue(ComboMarkerTypeFilter, typeFilter);
        SelectComboValue(ComboMarkerSampleFilter, sampleFilter);

        if (!string.Equals(typeFilter, MarkerTypeFilterAllMarkers, StringComparison.OrdinalIgnoreCase) &&
            _hiddenAiMarkerTypes.Remove(typeFilter))
        {
            SavePersistedMarkerVisibility();
        }

        LoadObservationsInbox();
        RefreshAiMarkersOverlay();
    }

    private void DeleteMarkerSet(SmartAiMarkerSet set)
    {
        if (_currentJob == null)
            return;

        MessageBoxResult answer = MessageBox.Show(
            $"Delete marker set '{set.Name}'?\n\nThis only removes the saved set file. Marker JSON and crop evidence stay in AI_Context.",
            "Delete Marker Set",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        bool deleted = SmartContextStore.DeleteAiMarkerSet(_currentJob, set.Id);
        TxtStatus.Text = deleted
            ? $"Deleted marker set '{set.Name}'."
            : "Marker set JSON was already missing.";
    }

    private static void SelectComboValue(ComboBox combo, string value)
    {
        foreach (object item in combo.Items)
        {
            if (string.Equals(item?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.Items.Add(value);
        combo.SelectedItem = value;
    }

    private void ExportMarkersContext(bool openAfterExport)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before exporting marker context.";
            return;
        }

        List<SmartAiMarker> markers = LoadMarkersForCurrentFilter(includeHiddenTypes: false);
        if (markers.Count == 0)
        {
            TxtStatus.Text = "No visible AI markers match the current filters.";
            MessageBox.Show(
                "No visible AI markers match the current type/sample filters.",
                "Export Marker Context",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string path = SmartContextStore.ExportAiMarkersContext(
            _currentJob,
            markers,
            _hiddenAiMarkerTypes.ToList(),
            SelectedMarkerTypeFilterForMarkers(),
            SelectedMarkerSampleFilter());

        string relativePath = Path.GetRelativePath(_currentJob.RootPath, path);
        TxtStatus.Text = $"Exported {markers.Count} AI markers -> {relativePath}";
        if (openAfterExport)
            OpenJsonFile(path, "AI marker context export is missing.");
    }

    private List<SmartAiMarker> LoadMarkersForCurrentFilter(bool includeHiddenTypes)
    {
        if (_currentJob == null)
            return [];

        return SmartContextStore.LoadAiMarkers(_currentJob)
            .Where(MarkerMatchesCurrentFilters)
            .Where(marker => includeHiddenTypes || !_hiddenAiMarkerTypes.Contains(marker.Type))
            .OrderBy(marker => marker.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.SampleKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.Page, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.CreatedAtUtc)
            .ToList();
    }

    private bool MarkerMatchesCurrentFilters(SmartAiMarker marker)
    {
        string typeFilter = SelectedMarkerTypeFilter();
        bool typeMatches =
            string.Equals(typeFilter, MarkerTypeFilterAllInbox, StringComparison.Ordinal) ||
            string.Equals(typeFilter, MarkerTypeFilterAllMarkers, StringComparison.Ordinal) ||
            string.Equals(marker.Type, typeFilter, StringComparison.OrdinalIgnoreCase);
        if (!typeMatches)
            return false;

        string sampleFilter = SelectedMarkerSampleFilter();
        return string.Equals(sampleFilter, MarkerSampleFilterAny, StringComparison.Ordinal) ||
               string.Equals(marker.SampleKind, sampleFilter, StringComparison.OrdinalIgnoreCase);
    }

    private string SelectedMarkerTypeFilterForMarkers()
    {
        string typeFilter = SelectedMarkerTypeFilter();
        return string.Equals(typeFilter, MarkerTypeFilterAllInbox, StringComparison.Ordinal)
            ? MarkerTypeFilterAllMarkers
            : typeFilter;
    }

    private string DefaultMarkerSetName(IReadOnlyCollection<SmartAiMarker> markers)
    {
        string typeFilter = SelectedMarkerTypeFilterForMarkers();
        string sampleFilter = SelectedMarkerSampleFilter();
        string typeLabel = string.Equals(typeFilter, MarkerTypeFilterAllMarkers, StringComparison.Ordinal)
            ? "All Markers"
            : typeFilter;
        if (!string.Equals(sampleFilter, MarkerSampleFilterAny, StringComparison.Ordinal))
            typeLabel += $" - {sampleFilter}";
        return $"{typeLabel} ({markers.Count})";
    }

    private MarkerSetInput? ShowMarkerSetDialog(string defaultName, int markerCount)
    {
        var win = new Window
        {
            Title = "Create Marker Set",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Markers: {markerCount}   Type: {SelectedMarkerTypeFilterForMarkers()}   Sample: {SelectedMarkerSampleFilter()}",
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.Normal,
        });

        panel.Children.Add(new TextBlock { Text = "Set name", Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new TextBox { Text = defaultName, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock { Text = "Description / use", Margin = new Thickness(0, 0, 0, 4) });
        var descriptionBox = new TextBox
        {
            Height = 76,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(descriptionBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var ok = new Button { Content = "Save", Width = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 78, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        win.Content = panel;
        MarkerSetInput? result = null;
        ok.Click += (_, _) =>
        {
            string name = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Marker set name is required.", "Create Marker Set",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            result = new MarkerSetInput(name, descriptionBox.Text.Trim());
            win.DialogResult = true;
        };
        win.Loaded += (_, _) => nameBox.Focus();

        return win.ShowDialog() == true ? result : null;
    }

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
            PageInfo? page = SmartTakeoffsJobStore.TryReadPage(folder);
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

    private bool CanOpenAiRequestFile(ObservationDisplayItem item) =>
        _currentJob != null && File.Exists(AiRequestPath(item));

    private bool CanAddManualAiResponse(ObservationDisplayItem item) =>
        _currentJob != null && SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id) != null;

    private bool CanRunAiRequest(ObservationDisplayItem item)
    {
        if (_currentJob == null || _isRunningAiRequest)
            return false;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        return request != null && IsRunnableAiStatus(request.Status);
    }

    private bool CanApplySheetMetadataResponse(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request == null ||
            !string.Equals(request.Type, "pdf_sheet_metadata_fallback", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        SmartAiResponse? response = SmartContextStore.LoadAiResponse(_currentJob, request.Id);
        return response != null &&
               string.Equals(response.Status, "done", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(response.OutputText) &&
               TryResolveRequestPage(request, out _);
    }

    private void ApplySheetMetadataResponse(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request == null || !TryResolveRequestPage(request, out PageInfo? page) || page == null)
        {
            TxtStatus.Text = "Could not resolve the page for this sheet metadata response.";
            return;
        }

        SmartAiResponse? response = SmartContextStore.LoadAiResponse(_currentJob, request.Id);
        if (response == null)
        {
            TxtStatus.Text = "No AI response exists for this sheet metadata request.";
            return;
        }

        if (!PdfSheetMetadataService.TryBuildMetadataFromFallbackResponse(page, request, response, out PdfSheetMetadata metadata, out string error, _currentJob))
        {
            MessageBox.Show(error, "Apply Sheet Metadata Response", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SmartTakeoffsJobStore.WriteSourcePdfMetadata(page.FolderPath, metadata);
        var result = new PdfMetadataPageResult(page, true, metadata, "");
        var rows = BuildPdfMetadataPreviewRows([result], defaultRename: true, defaultScale: true).ToList();
        var dialog = new PdfMetadataPreviewDialog(rows, "Apply Sheet Metadata Response")
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPdfMetadataResults(_currentJob, [result], dialog.Rows);
    }

    private bool TryResolveRequestPage(SmartAiRequest request, out PageInfo? page)
    {
        page = null;
        if (_currentJob == null)
            return false;

        if (!string.IsNullOrWhiteSpace(request.PageFolder))
        {
            string folder = Path.IsPathFullyQualified(request.PageFolder)
                ? request.PageFolder
                : Path.GetFullPath(Path.Combine(_currentJob.RootPath, request.PageFolder));
            page = SmartTakeoffsJobStore.TryReadPage(folder);
            if (page != null)
                return true;
        }

        page = FindPageByName(request.Page);
        return page != null;
    }

    private async Task RunSelectedOrNextAiRequestAsync()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before running AI.";
            return;
        }

        if (_isRunningAiRequest)
        {
            TxtStatus.Text = "AI request is already running.";
            return;
        }

        SmartAiRequest? request = null;
        if (SelectedObservationDisplayItem() is { } selected)
        {
            SmartAiRequest? selectedRequest = SmartContextStore.LoadAiRequest(_currentJob, selected.Observation.Id);
            if (selectedRequest != null && IsRunnableAiStatus(selectedRequest.Status))
                request = selectedRequest;
        }

        request ??= SmartContextStore.LoadAiRequests(_currentJob)
            .FirstOrDefault(candidate => IsRunnableAiStatus(candidate.Status));

        if (request == null)
        {
            TxtStatus.Text = "No pending AI request to run.";
            return;
        }

        await RunAiRequestAsync(request);
    }

    private async Task RunAiRequestAsync(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request == null)
        {
            TxtStatus.Text = "No AI request JSON exists for this Inbox entry.";
            return;
        }

        await RunAiRequestAsync(request);
    }

    private async Task RunAiRequestAsync(SmartAiRequest request)
    {
        if (_currentJob == null)
            return;

        if (_isRunningAiRequest)
        {
            TxtStatus.Text = "AI request is already running.";
            return;
        }

        string apiKey = ReadOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            TxtStatus.Text = "Set OPENAI_API_KEY in Windows environment, then run AI again.";
            return;
        }

        string model = AppSettingsStore.ResolveOpenAiModel(_settings);
        _isRunningAiRequest = true;
        try
        {
            request.Status = "running";
            SmartContextStore.SaveAiRequest(_currentJob, request);
            TxtStatus.Text = $"Running AI request {request.Id}...";
            LoadObservationsInbox();

            SmartAiRunResult result = await OpenAiRequestRunner.RunAsync(
                _currentJob,
                request,
                apiKey,
                model,
                CancellationToken.None);

            if (result.Success)
            {
                SmartAiResponse response = SmartContextStore.SaveAiResponse(
                    _currentJob,
                    request,
                    "done",
                    result.OutputText,
                    "",
                    "openai",
                    result.Model,
                    result.ProviderResponseId,
                    result.RawResponsePath);
                SmartContextStore.SaveAiActionDraftFromResponse(_currentJob, request, response);
                TxtStatus.Text = $"AI response saved for {request.Id}.";
            }
            else
            {
                SmartAiResponse response = SmartContextStore.SaveAiResponse(
                    _currentJob,
                    request,
                    "failed",
                    "",
                    result.Error,
                    "openai",
                    result.Model,
                    result.ProviderResponseId,
                    result.RawResponsePath);
                SmartContextStore.SaveAiActionDraftFromResponse(_currentJob, request, response);
                TxtStatus.Text = $"AI request failed: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            SmartAiResponse response = SmartContextStore.SaveAiResponse(
                _currentJob,
                request,
                "failed",
                "",
                ex.Message,
                "openai",
                model,
                "",
                "");
            SmartContextStore.SaveAiActionDraftFromResponse(_currentJob, request, response);
            TxtStatus.Text = $"AI request failed: {ex.Message}";
        }
        finally
        {
            _isRunningAiRequest = false;
            LoadObservationsInbox();
        }
    }

    private static string ReadOpenAiApiKey()
    {
        return AppSettingsStore.ReadOpenAiApiKey();
    }

    private static bool IsRunnableAiStatus(string status) =>
        string.IsNullOrWhiteSpace(status) ||
        status.Equals("pending", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("failed", StringComparison.OrdinalIgnoreCase);

    private bool CanOpenLayerManifest(ObservationDisplayItem item) =>
        TryGetLayerManifestPath(item, out _);

    private void OpenLayerManifest(ObservationDisplayItem item)
    {
        if (!TryGetLayerManifestPath(item, out string path))
        {
            TxtStatus.Text = "Layer JSON is missing for this Inbox entry.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    private bool TryGetLayerManifestPath(ObservationDisplayItem item, out string path)
    {
        path = "";
        if (_currentJob == null)
            return false;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request != null && !string.IsNullOrWhiteSpace(request.LayerManifestPath))
        {
            path = Path.IsPathFullyQualified(request.LayerManifestPath)
                ? request.LayerManifestPath
                : Path.GetFullPath(Path.Combine(_currentJob.RootPath, request.LayerManifestPath));
            return File.Exists(path);
        }

        PageInfo? page = FindPageByName(item.Page);
        if (page == null)
            return false;

        path = SmartTakeoffsJobStore.PageLayersJsonPath(page.FolderPath);
        return File.Exists(path);
    }

    private void OpenAiRequestFile(ObservationDisplayItem item)
    {
        string path = AiRequestPath(item);
        if (!File.Exists(path))
        {
            TxtStatus.Text = "AI request JSON is missing.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    private bool CanOpenAiResponseFile(ObservationDisplayItem item) =>
        _currentJob != null && File.Exists(AiResponsePath(item));

    private bool CanOpenAiActionDraftFile(ObservationDisplayItem item) =>
        _currentJob != null && File.Exists(AiActionDraftPath(item));

    private bool CanPreviewAiActionDraft(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiActionDraft? draft = SmartContextStore.LoadAiActionDraft(_currentJob, item.Observation.Id);
        return draft?.Actions.Any(action => action.Points.Count > 0) == true;
    }

    private bool CanApplyAiActionDraft(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiActionDraft? draft = SmartContextStore.LoadAiActionDraft(_currentJob, item.Observation.Id);
        return draft != null &&
               !string.Equals(draft.Status, "applied", StringComparison.OrdinalIgnoreCase) &&
               draft.Actions.Any(action => ValidActionPointCount(action) > 0);
    }

    private bool CanReviewAiActionDraft(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiActionDraft? draft = SmartContextStore.LoadAiActionDraft(_currentJob, item.Observation.Id);
        return draft != null &&
               !string.Equals(draft.Status, "applied", StringComparison.OrdinalIgnoreCase) &&
               draft.Actions.Count > 0;
    }

    private void OpenAiResponseFile(ObservationDisplayItem item)
    {
        string path = AiResponsePath(item);
        if (!File.Exists(path))
        {
            TxtStatus.Text = "AI response JSON is missing.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    private void OpenAiActionDraftFile(ObservationDisplayItem item)
    {
        string path = AiActionDraftPath(item);
        if (!File.Exists(path))
        {
            TxtStatus.Text = "AI action draft JSON is missing.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    private void PreviewAiActionDraft(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiActionDraft? draft = SmartContextStore.LoadAiActionDraft(_currentJob, item.Observation.Id);
        if (draft == null)
        {
            TxtStatus.Text = "AI action draft JSON is missing.";
            return;
        }

        int actionCount = draft.Actions.Count(action => action.Points.Count > 0);
        if (actionCount == 0)
        {
            TxtStatus.Text = "AI action draft has no preview points.";
            return;
        }

        string pageName = !string.IsNullOrWhiteSpace(draft.Page) ? draft.Page : item.Page;
        PageInfo? page = FindPageByName(pageName) ?? FindPageByName(item.Page);
        if (page != null)
        {
            pageName = page.Name;
            SelectPageByFolder(page.FolderPath);
        }

        string previewPageName = pageName;
        Dispatcher.InvokeAsync(() => _viewport.ShowAiActionDraftPreview(draft, previewPageName));
        TxtStatus.Text = $"Previewing {actionCount} AI action draft(s) on {previewPageName}.";
    }

    private void ClearAiActionDraftPreview()
    {
        _viewport.ClearAiActionDraftPreview();
        TxtStatus.Text = "AI action preview cleared.";
    }

    private void ApplyAiActionDraft(ObservationDisplayItem item)
    {
        ReviewAiActionDraft(item);
    }

    private void ReviewAiActionDraft(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiActionDraft? draft = SmartContextStore.LoadAiActionDraft(_currentJob, item.Observation.Id);
        if (draft == null)
        {
            TxtStatus.Text = "AI action draft JSON is missing.";
            return;
        }

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        bool isRoofRecognition = IsRoofRecognitionRequest(request);
        var targets = isRoofRecognition
            ? BuildRoofRecognitionTargetOptions()
            : BuildAiActionTargetOptions();
        var rows = BuildAiActionReviewRows(draft, item, targets);
        if (rows.Count == 0)
        {
            TxtStatus.Text = "AI action draft has no actions to review.";
            return;
        }

        var dialog = new AiActionReviewDialog(
            rows,
            targets,
            indices => PreviewAiActionDraftActions(draft, item, indices),
            isRoofRecognition ? "Review Auto Roof Candidates" : "Review Action Draft",
            isRoofRecognition ? "Roof Marker" : "Target Takeoff",
            isRoofRecognition ? "Create Markers" : "Apply Accepted",
            isRoofRecognition
                ? "Select at least one valid roof marker candidate before creating markers."
                : "Select at least one valid action with a target takeoff item before applying.")
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
            return;

        var acceptedRows = dialog.AcceptedRows.ToList();
        draft.AcceptedActionIndices = dialog.AcceptedIndices.ToList();
        draft.RejectedActionIndices = dialog.RejectedIndices.ToList();
        draft.ReviewedAtUtc = DateTime.UtcNow.ToString("O");
        draft.Status = acceptedRows.Count > 0 ? "reviewed" : "reviewed_no_actions";
        SmartContextStore.SaveAiActionDraft(_currentJob, draft);
        RecordMarkerCandidateFeedback(item, draft, rows);

        if (acceptedRows.Count == 0)
        {
            LoadObservationsInbox();
            TxtStatus.Text = "Saved AI action draft review; no valid accepted actions were applied.";
            return;
        }

        if (isRoofRecognition)
            ApplyRoofRecognitionMarkerDraft(item, draft, acceptedRows);
        else
            ApplyReviewedAiActionDraft(item, draft, acceptedRows);
    }

    private static bool IsRoofRecognitionRequest(SmartAiRequest? request) =>
        request != null &&
        string.Equals(request.Type, "roof_recognition_request", StringComparison.OrdinalIgnoreCase);

    private void RecordMarkerCandidateFeedback(
        ObservationDisplayItem item,
        SmartAiActionDraft draft,
        IReadOnlyList<AiActionReviewRow> rows)
    {
        if (_currentJob == null || rows.Count == 0)
            return;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request == null ||
            !string.Equals(request.Type, "find_similar_marker_request", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string sourceMarkerId = ExtractSourceMarkerId(request.MeasurementSummary);
        if (string.IsNullOrWhiteSpace(sourceMarkerId))
            sourceMarkerId = ExtractSourceMarkerId(request.Prompt);
        SmartAiMarker? sourceMarker = string.IsNullOrWhiteSpace(sourceMarkerId)
            ? null
            : SmartContextStore.LoadAiMarker(_currentJob, sourceMarkerId);

        foreach (AiActionReviewRow row in rows)
        {
            SmartAiAction action = row.Action;
            AiActionTargetOption? target = row.Target;
            bool accepted = row.Accepted;
            SmartLearningStore.AppendMarkerFeedback(
                _currentJob,
                new SmartMarkerFeedbackRecord
                {
                    RequestId = request.Id,
                    ResponseId = draft.ResponseId,
                    DraftId = draft.Id,
                    SourceMarkerId = sourceMarkerId,
                    SourceMarkerType = sourceMarker?.Type ?? "",
                    SourceMarkerSampleKind = sourceMarker?.SampleKind ?? "",
                    Outcome = accepted ? "accepted" : "rejected",
                    Applied = accepted && row.CanApply && target != null,
                    ActionIndex = row.Index,
                    ActionType = action.Type.Trim(),
                    Label = string.IsNullOrWhiteSpace(action.Label) ? row.Label : action.Label.Trim(),
                    Page = row.Page,
                    MeasurementType = row.MeasurementType,
                    Confidence = action.Confidence,
                    Points = action.Points
                        .Select(point => new SmartAiActionPoint { X = point.X, Y = point.Y })
                        .ToList(),
                    Notes = action.Notes.Trim(),
                    TargetId = target?.Id ?? "",
                    TargetName = target?.Name ?? "",
                    TargetMeasurementType = target?.MeasurementType ?? "",
                    TargetCreatesNewItem = target?.CreatesNewItem == true,
                });
        }
    }

    private static string ExtractSourceMarkerId(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            const string label = "Source marker id:";
            if (trimmed.Equals(label, StringComparison.OrdinalIgnoreCase))
                return i + 1 < lines.Length ? lines[i + 1].Trim() : "";

            if (trimmed.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                return trimmed[label.Length..].Trim();
        }

        return "";
    }

    private IReadOnlyList<AiActionReviewRow> BuildAiActionReviewRows(
        SmartAiActionDraft draft,
        ObservationDisplayItem item,
        IReadOnlyList<AiActionTargetOption> targets)
    {
        var rows = new List<AiActionReviewRow>();
        for (int i = 0; i < draft.Actions.Count; i++)
        {
            SmartAiAction action = draft.Actions[i];
            string measurementType = NormalizeAiActionMeasurementType(action);
            PageInfo? page = ResolveAiActionPage(action, draft, item);
            List<SKPoint> points = ActionPoints(action);
            bool hasPage = page != null;
            bool hasGeometry = HasValidMeasurementGeometry(measurementType, points);
            string status = hasPage && hasGeometry
                ? "Ready"
                : !hasPage
                    ? "No valid page"
                    : AiActionGeometryStatus(measurementType, points.Count);
            rows.Add(AiActionReviewRow.FromAction(
                i,
                action,
                AiActionPageLabel(page, action, draft, item),
                hasPage && hasGeometry,
                status,
                DefaultAiActionTarget(measurementType, targets)));
        }

        return rows;
    }

    private IReadOnlyList<AiActionTargetOption> BuildAiActionTargetOptions()
    {
        var options = new List<AiActionTargetOption>();
        foreach (string measurementType in new[] { "line", "area", "point" })
        {
            options.Add(new AiActionTargetOption
            {
                Id = $"new:{measurementType}",
                Name = $"New AI {MeasurementTypeTitle(measurementType)} Item",
                MeasurementType = measurementType,
                CreatesNewItem = true,
            });
        }

        options.AddRange(_takeoffItems
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                string measurementType = SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType);
                return new AiActionTargetOption
                {
                    Id = item.Id,
                    Name = $"{item.Name} ({MeasurementTypeTitle(measurementType)})",
                    MeasurementType = measurementType,
                    Item = item,
                };
            }));

        return options;
    }

    private static IReadOnlyList<AiActionTargetOption> BuildRoofRecognitionTargetOptions() =>
    [
        new AiActionTargetOption
        {
            Id = "roof-marker:line",
            Name = "Create Line Roof Marker",
            MeasurementType = "line",
            CreatesNewItem = true,
        },
        new AiActionTargetOption
        {
            Id = "roof-marker:point",
            Name = "Create Point Roof Marker",
            MeasurementType = "point",
            CreatesNewItem = true,
        },
    ];

    private AiActionTargetOption? DefaultAiActionTarget(
        string measurementType,
        IReadOnlyList<AiActionTargetOption> targets)
    {
        if (_activeItem != null)
        {
            string activeType = SmartTakeoffsJobStore.NormalizeMeasurementType(_activeItem.MeasurementType);
            if (string.Equals(activeType, measurementType, StringComparison.OrdinalIgnoreCase))
            {
                AiActionTargetOption? active = targets.FirstOrDefault(target =>
                    target.Item != null && ReferenceEquals(target.Item, _activeItem));
                if (active != null)
                    return active;
            }
        }

        return targets.FirstOrDefault(target =>
                   target.Item != null &&
                   string.Equals(target.MeasurementType, measurementType, StringComparison.OrdinalIgnoreCase)) ??
               targets.FirstOrDefault(target =>
                   target.CreatesNewItem &&
                   string.Equals(target.MeasurementType, measurementType, StringComparison.OrdinalIgnoreCase));
    }

    private static string AiActionGeometryStatus(string measurementType, int pointCount) =>
        measurementType switch
        {
            "point" => "Needs at least 1 point",
            "area" => $"Needs 3+ points; has {pointCount}",
            _ => $"Needs 2+ points; has {pointCount}",
        };

    private static string AiActionPageLabel(
        PageInfo? page,
        SmartAiAction action,
        SmartAiActionDraft draft,
        ObservationDisplayItem item)
    {
        if (page != null)
            return page.Name;
        if (!string.IsNullOrWhiteSpace(action.Page))
            return action.Page;
        if (!string.IsNullOrWhiteSpace(draft.Page))
            return draft.Page;
        if (!string.IsNullOrWhiteSpace(item.Page))
            return item.Page;
        return "(missing)";
    }

    private void PreviewAiActionDraftActions(
        SmartAiActionDraft source,
        ObservationDisplayItem item,
        IReadOnlyList<int> indices)
    {
        if (indices.Count == 0)
        {
            TxtStatus.Text = "Select accepted AI actions before previewing.";
            return;
        }

        var actions = indices
            .Where(index => index >= 0 && index < source.Actions.Count)
            .Select(index => source.Actions[index])
            .Where(action => action.Points.Count > 0)
            .ToList();
        if (actions.Count == 0)
        {
            TxtStatus.Text = "Selected AI actions have no preview points.";
            return;
        }

        PageInfo? page = actions
            .Select(action => ResolveAiActionPage(action, source, item))
            .FirstOrDefault(candidate => candidate != null);
        string pageName = page?.Name ?? AiActionPageLabel(null, actions[0], source, item);
        if (page != null)
            SelectPageByFolder(page.FolderPath);

        var previewDraft = new SmartAiActionDraft
        {
            Id = source.Id,
            RequestId = source.RequestId,
            ResponseId = source.ResponseId,
            ProjectId = source.ProjectId,
            Page = source.Page,
            Status = source.Status,
            Summary = source.Summary,
            RawText = source.RawText,
            Actions = actions,
            CreatedAtUtc = source.CreatedAtUtc,
            UpdatedAtUtc = source.UpdatedAtUtc,
        };

        Dispatcher.InvokeAsync(() => _viewport.ShowAiActionDraftPreview(previewDraft, pageName));
        TxtStatus.Text = $"Previewing {actions.Count} selected AI action(s) on {pageName}.";
    }

    private void ApplyReviewedAiActionDraft(
        ObservationDisplayItem item,
        SmartAiActionDraft draft,
        IReadOnlyList<AiActionReviewRow> acceptedRows)
    {
        if (_currentJob == null)
            return;

        var createdByType = new Dictionary<string, TakeoffItem>(StringComparer.OrdinalIgnoreCase);
        var appliedIds = new List<string>();
        var appliedIndices = new List<int>();
        var touchedItems = new HashSet<TakeoffItem>();
        PageInfo? lastPage = null;

        try
        {
            foreach (AiActionReviewRow row in acceptedRows)
            {
                SmartAiAction action = row.Action;
                PageInfo? page = ResolveAiActionPage(action, draft, item);
                if (page == null)
                    continue;

                string measurementType = NormalizeAiActionMeasurementType(action);
                List<SKPoint> points = ActionPoints(action);
                if (!HasValidMeasurementGeometry(measurementType, points))
                    continue;

                TakeoffItem? target = ResolveReviewedAiActionTarget(row, action, measurementType, createdByType);
                if (target == null)
                    continue;

                EnsureTakeoffItemFolder(target);

                var measurement = new Measurement
                {
                    Name = AiActionMeasurementName(action, target),
                    Notes = AiActionMeasurementNotes(action, draft),
                    MType = measurementType,
                    Points = points,
                    Color = target.Color,
                    PageFolder = page.FolderPath,
                    TakeoffFolder = target.FolderPath,
                    ScaleMetersPerPt = page.ScaleMetersPerPt > 0 ? page.ScaleMetersPerPt : _viewport.ScaleMetersPerPt,
                };

                target.Measurements.Add(measurement);
                appliedIds.Add(measurement.Id);
                appliedIndices.Add(row.Index);
                touchedItems.Add(target);
                lastPage = page;
            }

            if (appliedIds.Count == 0)
            {
                draft.Status = "reviewed_no_actions";
                SmartContextStore.SaveAiActionDraft(_currentJob, draft);
                TxtStatus.Text = "AI action draft review saved, but no accepted action matched a valid page, target, and geometry.";
                return;
            }

            draft.Status = "applied";
            draft.AppliedMeasurementIds = draft.AppliedMeasurementIds
                .Concat(appliedIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            draft.AppliedActionIndices = draft.AppliedActionIndices
                .Concat(appliedIndices)
                .Distinct()
                .OrderBy(index => index)
                .ToList();
            SmartContextStore.SaveAiActionDraft(_currentJob, draft);

            foreach (TakeoffItem target in touchedItems)
            {
                SmartTakeoffsJobStore.SaveTakeoffItem(target);
                RefreshTreeItem(target);
            }

            _viewport.ClearAiActionDraftPreview();
            _viewport.SetMeasurements(_takeoffItems.SelectMany(takeoff => takeoff.Measurements));
            if (lastPage != null)
                SelectPageByFolder(lastPage.FolderPath);
            if (touchedItems.LastOrDefault() is { } lastItem)
                SelectTakeoffItem(lastItem);

            RefreshEstimateTable();
            RefreshPagesTakeoffIndicators();
            ApplyTakeoffPageHighlights();
            UpdateTotalDisplay();
            LoadObservationsInbox();
            TxtStatus.Text = $"Applied {appliedIds.Count} AI drafted measurement(s).";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot apply AI action draft:\n{ex.Message}", "Apply AI Action Draft",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyRoofRecognitionMarkerDraft(
        ObservationDisplayItem item,
        SmartAiActionDraft draft,
        IReadOnlyList<AiActionReviewRow> acceptedRows)
    {
        if (_currentJob == null)
            return;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        var existingMarkers = SmartContextStore.LoadAiMarkers(_currentJob).ToList();
        var createdIds = new List<string>();
        var appliedIndices = new List<int>();
        PageInfo? lastPage = null;
        int skippedDuplicates = 0;

        try
        {
            foreach (AiActionReviewRow row in acceptedRows)
            {
                SmartAiAction action = row.Action;
                PageInfo? page = ResolveAiActionPage(action, draft, item);
                if (page == null)
                    continue;

                string measurementType = NormalizeAiActionMeasurementType(action);
                List<SKPoint> points = ActionPoints(action);
                if (!HasValidMeasurementGeometry(measurementType, points))
                    continue;

                string markerType = ResolveRoofRecognitionMarkerType(action);
                List<SKPoint> markerPoints = RoofRecognitionMarkerPoints(markerType, measurementType, points);
                if (markerPoints.Count == 0)
                    continue;

                string cropPath = request?.CropPath ?? "";
                SKRect cropRect = CandidateCropRect(points);
                if (TrySavePageCrop(
                        page,
                        cropRect,
                        "roof_marker_candidate",
                        $"{markerType}_{row.Index + 1}",
                        out string savedCropPath,
                        out SKRect savedCropRect,
                        out _))
                {
                    cropPath = savedCropPath;
                    cropRect = savedCropRect;
                }

                int markerPointIndex = 0;
                foreach (SKPoint point in markerPoints)
                {
                    markerPointIndex++;
                    if (HasNearbyRoofMarkerDuplicate(existingMarkers, page, markerType, point))
                    {
                        skippedDuplicates++;
                        continue;
                    }

                    string value = RoofRecognitionMarkerValue(action, markerType);
                    string note = RoofRecognitionMarkerNote(action, draft, row, markerPointIndex, markerPoints.Count);
                    string details = BuildRoofRecognitionMarkerObservationDetails(
                        request,
                        action,
                        markerType,
                        value,
                        note,
                        cropPath,
                        cropRect,
                        point);

                    SmartObservation observation = SmartContextStore.AddObservation(
                        _currentJob,
                        page,
                        "ai_marker",
                        details);

                    SmartAiMarker marker = SmartContextStore.SaveAiMarker(
                        _currentJob,
                        page,
                        observation,
                        markerType,
                        "positive",
                        value,
                        note,
                        cropPath,
                        point.X,
                        point.Y,
                        cropRect.Left,
                        cropRect.Top,
                        cropRect.Right,
                        cropRect.Bottom);

                    existingMarkers.Add(marker);
                    createdIds.Add(marker.Id);
                    appliedIndices.Add(row.Index);
                    lastPage = page;
                }
            }

            if (createdIds.Count == 0)
            {
                draft.Status = "reviewed_no_actions";
                SmartContextStore.SaveAiActionDraft(_currentJob, draft);
                LoadObservationsInbox();
                TxtStatus.Text = skippedDuplicates > 0
                    ? $"Auto Roof review saved; {skippedDuplicates} duplicate marker candidate(s) were skipped."
                    : "Auto Roof review saved, but no accepted candidate could be converted to a marker.";
                return;
            }

            draft.Status = "applied";
            draft.AppliedActionIndices = draft.AppliedActionIndices
                .Concat(appliedIndices)
                .Distinct()
                .OrderBy(index => index)
                .ToList();
            SmartContextStore.SaveAiActionDraft(_currentJob, draft);

            _viewport.ClearAiActionDraftPreview();
            if (lastPage != null)
                SelectPageByFolder(lastPage.FolderPath);
            RefreshAiMarkersOverlay();
            RefreshMassingDraftPanel();
            LoadObservationsInbox();
            TxtStatus.Text = skippedDuplicates > 0
                ? $"Created {createdIds.Count} reviewed roof marker(s); skipped {skippedDuplicates} duplicate(s). Run Build 3D Draft to update roof guides."
                : $"Created {createdIds.Count} reviewed roof marker(s). Run Build 3D Draft to update roof guides.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Cannot create Auto Roof markers:\n{ex.Message}",
                "Auto Roof",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private string BuildRoofRecognitionMarkerObservationDetails(
        SmartAiRequest? request,
        SmartAiAction action,
        string markerType,
        string value,
        string note,
        string cropPath,
        SKRect cropRect,
        SKPoint point)
    {
        var sb = new StringBuilder();
        sb.AppendLine("AI marker saved from Auto Roof review.");
        sb.AppendLine();
        sb.AppendLine("Marker:");
        sb.AppendLine($"- Type: {markerType}");
        sb.AppendLine("- Sample kind: positive");
        if (!string.IsNullOrWhiteSpace(value))
            sb.AppendLine($"- Value: {value}");
        if (!string.IsNullOrWhiteSpace(note))
            sb.AppendLine($"- Note: {note}");
        sb.AppendLine();
        sb.AppendLine("Source action:");
        if (request != null)
            sb.AppendLine($"- Request: {request.Id}");
        sb.AppendLine($"- Action type: {action.Type}");
        sb.AppendLine($"- Label: {action.Label}");
        if (action.Confidence > 0)
            sb.AppendLine($"- Confidence: {action.Confidence:P0}");
        if (!string.IsNullOrWhiteSpace(action.Notes))
            sb.AppendLine($"- AI notes: {action.Notes.Trim()}");
        sb.AppendLine();
        sb.AppendLine("Context:");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "- PDF point: {0:F1}, {1:F1}", point.X, point.Y));
        sb.AppendLine($"- AI crop: {cropPath}");
        sb.AppendLine($"- PDF crop: {FormatPdfRect(cropRect)}");
        return sb.ToString();
    }

    private bool HasNearbyRoofMarkerDuplicate(
        IReadOnlyList<SmartAiMarker> existingMarkers,
        PageInfo page,
        string markerType,
        SKPoint point)
    {
        if (_currentJob == null)
            return false;

        foreach (SmartAiMarker marker in existingMarkers)
        {
            if (!AiMarkerTypeEquals(marker, markerType) ||
                !MarkerBelongsToPage(marker, page, _currentJob))
            {
                continue;
            }

            float dx = marker.PdfPoint.X - point.X;
            float dy = marker.PdfPoint.Y - point.Y;
            if ((dx * dx) + (dy * dy) <= RoofRecognitionMarkerDuplicateTolerancePt * RoofRecognitionMarkerDuplicateTolerancePt)
                return true;
        }

        return false;
    }

    private static List<SKPoint> RoofRecognitionMarkerPoints(
        string markerType,
        string measurementType,
        IReadOnlyList<SKPoint> points)
    {
        if (points.Count == 0)
            return [];

        if (string.Equals(markerType, "roof_note", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(measurementType, "point", StringComparison.OrdinalIgnoreCase))
        {
            return [points[0]];
        }

        return DistinctRoofRecognitionPoints(points)
            .Take(8)
            .ToList();
    }

    private static IEnumerable<SKPoint> DistinctRoofRecognitionPoints(IReadOnlyList<SKPoint> points)
    {
        var unique = new List<SKPoint>();
        foreach (SKPoint point in points)
        {
            if (unique.Any(existing =>
                    Math.Abs(existing.X - point.X) < 1f &&
                    Math.Abs(existing.Y - point.Y) < 1f))
            {
                continue;
            }

            unique.Add(point);
            yield return point;
        }
    }

    private static string ResolveRoofRecognitionMarkerType(SmartAiAction action)
    {
        string exact = action.Type.Trim();
        if (RoofRecognitionMarkerTypes.Any(type => string.Equals(type, exact, StringComparison.OrdinalIgnoreCase)))
            return RoofRecognitionMarkerTypes.First(type => string.Equals(type, exact, StringComparison.OrdinalIgnoreCase));

        string text = $"{action.Type} {action.Label} {action.Notes}".ToLowerInvariant();
        foreach (string markerType in RoofRecognitionMarkerTypes)
        {
            if (text.Contains(markerType, StringComparison.OrdinalIgnoreCase))
                return markerType;
        }

        if (text.Contains("valley", StringComparison.OrdinalIgnoreCase))
            return "valley_sample";
        if (text.Contains("high", StringComparison.OrdinalIgnoreCase) && text.Contains("edge", StringComparison.OrdinalIgnoreCase))
            return "roof_high_edge";
        if (text.Contains("low", StringComparison.OrdinalIgnoreCase) && text.Contains("edge", StringComparison.OrdinalIgnoreCase))
            return "roof_low_edge";
        if (text.Contains("overhang", StringComparison.OrdinalIgnoreCase) || text.Contains("eave", StringComparison.OrdinalIgnoreCase))
            return "overhang_sample";
        if (text.Contains("ridge", StringComparison.OrdinalIgnoreCase))
            return "ridge_sample";
        if (text.Contains("edge", StringComparison.OrdinalIgnoreCase))
            return "roof_edge_sample";
        return "roof_note";
    }

    private static string RoofRecognitionMarkerValue(SmartAiAction action, string markerType)
    {
        string label = action.Label.Trim();
        if (!string.IsNullOrWhiteSpace(label) &&
            !string.Equals(label, markerType, StringComparison.OrdinalIgnoreCase))
        {
            return label;
        }

        return "";
    }

    private static string RoofRecognitionMarkerNote(
        SmartAiAction action,
        SmartAiActionDraft draft,
        AiActionReviewRow row,
        int markerPointIndex,
        int markerPointCount)
    {
        var lines = new List<string> { "Created from reviewed Auto Roof candidate." };
        if (!string.IsNullOrWhiteSpace(draft.RequestId))
            lines.Add($"Request: {draft.RequestId}");
        lines.Add($"Action index: {row.Index}");
        if (markerPointCount > 1)
            lines.Add($"Marker point: {markerPointIndex} of {markerPointCount}");
        if (action.Confidence > 0)
            lines.Add($"Confidence: {action.Confidence:P0}");
        if (!string.IsNullOrWhiteSpace(action.Notes))
            lines.Add(action.Notes.Trim());
        return string.Join(Environment.NewLine, lines);
    }

    private TakeoffItem? ResolveReviewedAiActionTarget(
        AiActionReviewRow row,
        SmartAiAction action,
        string measurementType,
        Dictionary<string, TakeoffItem> createdByType)
    {
        AiActionTargetOption? option = row.Target;
        if (option == null ||
            !string.Equals(option.MeasurementType, measurementType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (option.Item != null)
        {
            string targetType = SmartTakeoffsJobStore.NormalizeMeasurementType(option.Item.MeasurementType);
            return string.Equals(targetType, measurementType, StringComparison.OrdinalIgnoreCase)
                ? option.Item
                : null;
        }

        if (!option.CreatesNewItem)
            return null;

        if (createdByType.TryGetValue(measurementType, out TakeoffItem? existing))
            return existing;

        string name = AiActionTakeoffName(action, measurementType);
        var created = CreateUniqueTakeoffItem(name, "#00BCD4", measurementType, NewTakeoffItemParentFolder());
        _takeoffItems.Add(created);
        var parent = FindTakeoffTreeItemByFolder(Path.GetDirectoryName(created.FolderPath) ?? "") ?? (ItemsControl)TakeoffsTree;
        var tvi = AddTakeoffTreeItem(created, parent);
        if (parent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;
        tvi.IsSelected = true;
        createdByType[measurementType] = created;
        return created;
    }

    private TakeoffItem ResolveAiActionTakeoffItem(
        SmartAiAction action,
        string measurementType,
        Dictionary<string, TakeoffItem> createdByType)
    {
        if (_activeItem != null &&
            string.Equals(
                SmartTakeoffsJobStore.NormalizeMeasurementType(_activeItem.MeasurementType),
                measurementType,
                StringComparison.OrdinalIgnoreCase))
        {
            return _activeItem;
        }

        if (createdByType.TryGetValue(measurementType, out TakeoffItem? existing))
            return existing;

        string name = AiActionTakeoffName(action, measurementType);
        var created = CreateUniqueTakeoffItem(name, "#00BCD4", measurementType, NewTakeoffItemParentFolder());
        _takeoffItems.Add(created);
        var parent = FindTakeoffTreeItemByFolder(Path.GetDirectoryName(created.FolderPath) ?? "") ?? (ItemsControl)TakeoffsTree;
        var tvi = AddTakeoffTreeItem(created, parent);
        if (parent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;
        tvi.IsSelected = true;
        createdByType[measurementType] = created;
        return created;
    }

    private PageInfo? ResolveAiActionPage(SmartAiAction action, SmartAiActionDraft draft, ObservationDisplayItem item)
    {
        if (FindPageByName(action.Page) is { } actionPage)
            return actionPage;
        if (FindPageByName(draft.Page) is { } draftPage)
            return draftPage;
        if (FindPageByName(item.Page) is { } observationPage)
            return observationPage;
        return _currentPage;
    }

    private static string NormalizeAiActionMeasurementType(SmartAiAction action)
    {
        string value = action.MeasurementType;
        if (string.IsNullOrWhiteSpace(value))
        {
            value = action.Type.Contains("area", StringComparison.OrdinalIgnoreCase)
                ? "area"
                : action.Type.Contains("point", StringComparison.OrdinalIgnoreCase)
                    ? "point"
                    : "line";
        }

        return SmartTakeoffsJobStore.NormalizeMeasurementType(value.Trim().ToLowerInvariant());
    }

    private static List<SKPoint> ActionPoints(SmartAiAction action) =>
        action.Points
            .Select(point => new SKPoint(point.X, point.Y))
            .Where(point => !float.IsNaN(point.X) && !float.IsNaN(point.Y))
            .ToList();

    private static int ValidActionPointCount(SmartAiAction action) =>
        HasValidMeasurementGeometry(NormalizeAiActionMeasurementType(action), ActionPoints(action))
            ? action.Points.Count
            : 0;

    private static bool HasValidMeasurementGeometry(string measurementType, IReadOnlyList<SKPoint> points) =>
        measurementType switch
        {
            "point" => points.Count >= 1,
            "area" => points.Count >= 3,
            _ => points.Count >= 2,
        };

    private static string AiActionTakeoffName(SmartAiAction action, string measurementType)
    {
        string label = string.IsNullOrWhiteSpace(action.Label)
            ? MeasurementTypeTitle(measurementType)
            : action.Label.Trim();
        return $"AI {label}";
    }

    private static string AiActionMeasurementName(SmartAiAction action, TakeoffItem target)
    {
        if (!string.IsNullOrWhiteSpace(action.Label))
            return action.Label.Trim();
        return DefaultSectionName(target, new Measurement { PageFolder = "" }, target.Measurements.Count);
    }

    private static string AiActionMeasurementNotes(SmartAiAction action, SmartAiActionDraft draft)
    {
        var lines = new List<string> { "Created from AI action draft." };
        if (!string.IsNullOrWhiteSpace(draft.RequestId))
            lines.Add($"Request: {draft.RequestId}");
        if (!string.IsNullOrWhiteSpace(action.Type))
            lines.Add($"Action: {action.Type}");
        if (action.Confidence > 0)
            lines.Add($"Confidence: {action.Confidence:P0}");
        if (!string.IsNullOrWhiteSpace(action.Notes))
            lines.Add(action.Notes.Trim());
        return string.Join(Environment.NewLine, lines);
    }

    private void AddManualAiResponse(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request == null)
        {
            TxtStatus.Text = "No AI request JSON exists for this Inbox entry.";
            return;
        }

        SmartAiResponse? existing = SmartContextStore.LoadAiResponse(_currentJob, request.Id);
        string initial = existing?.OutputText ?? "";
        string? responseText = ShowMultilineInputDialog(
            $"AI response for {item.TypeShort}\nPage: {item.Page}\nRequest: {request.Id}",
            initial,
            "AI Response");
        if (string.IsNullOrWhiteSpace(responseText))
            return;

        SmartAiResponse response = SmartContextStore.SaveAiResponse(_currentJob, request, "done", responseText, "");
        SmartContextStore.SaveAiActionDraftFromResponse(_currentJob, request, response);
        TxtStatus.Text = $"Saved AI response for {request.Id}.";
        LoadObservationsInbox();
    }

    private string AiRequestPath(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return "";

        return Path.Combine(_currentJob.AIContextRoot, "requests", $"{item.Observation.Id}.json");
    }

    private string AiMarkerPath(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return "";

        return SmartContextStore.AiMarkerPath(_currentJob, item.Observation.Id);
    }

    private string AiResponsePath(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return "";

        return Path.Combine(_currentJob.AIContextRoot, "responses", $"{item.Observation.Id}.json");
    }

    private string AiActionDraftPath(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return "";

        return SmartContextStore.AiActionDraftPath(_currentJob, item.Observation.Id);
    }

    private PageInfo? FindPageByName(string pageName)
    {
        if (string.IsNullOrWhiteSpace(pageName))
            return null;

        foreach (TreeViewItem item in PagesTree.Items)
        {
            if (FindPageByName(item, pageName) is { } page)
                return page;
        }

        return null;
    }

    private static PageInfo? FindPageByName(TreeViewItem item, string pageName)
    {
        if (item.Tag is PageInfo page &&
            string.Equals(page.Name, pageName, StringComparison.OrdinalIgnoreCase))
        {
            return page;
        }

        foreach (TreeViewItem child in item.Items)
        {
            if (FindPageByName(child, pageName) is { } found)
                return found;
        }

        return null;
    }

    private void ShowObservationDetailsDialog(SmartObservation observation)
    {
        var display = new ObservationDisplayItem(observation);
        var win = new Window
        {
            Title = $"{display.TypeShort} - {observation.Id}",
            Width = 680,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Owner = this,
        };

        var panel = new DockPanel { Margin = new Thickness(10) };
        var header = new TextBlock
        {
            Text = $"{display.TypeShort} | Page: {(string.IsNullOrWhiteSpace(display.Page) ? "Unassigned" : display.Page)} | {display.TimeDisplay}",
            FontWeight = FontWeights.Normal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var goToPage = new Button { Content = "Go to Page", MinWidth = 90, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanGoToObservationPage(display) };
        var openCrop = new Button { Content = "Open Crop", MinWidth = 90, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanOpenObservationCrop(display) };
        var openRequest = new Button { Content = "Request JSON", MinWidth = 105, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanOpenAiRequestFile(display) };
        var runAi = new Button { Content = "Run AI", MinWidth = 82, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanRunAiRequest(display) };
        var addResponse = new Button { Content = "AI Response", MinWidth = 100, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanAddManualAiResponse(display) };
        var openContext = new Button { Content = "Project Context", MinWidth = 110, Margin = new Thickness(0, 0, 6, 0), IsEnabled = _currentJob != null };
        var close = new Button { Content = "Close", Width = 78, IsCancel = true };
        TextBox? detailsText = null;
        goToPage.Click += (_, _) => GoToObservationPage(display);
        openCrop.Click += (_, _) => OpenObservationCrop(display);
        openRequest.Click += (_, _) => OpenAiRequestFile(display);
        runAi.Click += async (_, _) =>
        {
            await RunAiRequestAsync(display);
            if (detailsText != null)
                detailsText.Text = ObservationDetailsText(observation);
        };
        addResponse.Click += (_, _) => AddManualAiResponse(display);
        openContext.Click += (_, _) => OpenProjectContextMarkdown();
        close.Click += (_, _) => win.Close();
        buttons.Children.Add(goToPage);
        buttons.Children.Add(openCrop);
        buttons.Children.Add(openRequest);
        buttons.Children.Add(runAi);
        buttons.Children.Add(addResponse);
        buttons.Children.Add(openContext);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        var text = new TextBox
        {
            Text = ObservationDetailsText(observation),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        detailsText = text;
        panel.Children.Add(text);

        win.Content = panel;
        win.Loaded += (_, _) => text.Focus();
        win.ShowDialog();
    }

    private string ObservationDetailsText(SmartObservation observation)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Id: {observation.Id}");
        sb.AppendLine($"Type: {observation.Type}");
        sb.AppendLine($"Page: {observation.Page}");
        sb.AppendLine($"Created UTC: {observation.CreatedAtUtc}");

        if (_currentJob != null && SmartContextStore.LoadAiMarker(_currentJob, observation.Id) is { } marker)
        {
            sb.AppendLine();
            sb.AppendLine("AI Marker");
            sb.AppendLine($"Marker JSON: {SmartContextStore.AiMarkerPath(_currentJob, marker.Id)}");
            sb.AppendLine($"Type: {marker.Type}");
            sb.AppendLine($"Sample kind: {marker.SampleKind}");
            sb.AppendLine($"PDF point: {marker.PdfPoint.X:F1}, {marker.PdfPoint.Y:F1}");
            if (!string.IsNullOrWhiteSpace(marker.Value))
                sb.AppendLine($"Value: {marker.Value}");
            if (!string.IsNullOrWhiteSpace(marker.Note))
                sb.AppendLine($"Note: {marker.Note}");
            if (marker.LayerCount > 0)
                sb.AppendLine($"Layers: {marker.LayerCount}");
        }

        if (_currentJob != null && SmartContextStore.LoadAiRequest(_currentJob, observation.Id) is { } request)
        {
            sb.AppendLine();
            sb.AppendLine("AI Request");
            sb.AppendLine($"Status: {request.Status}");
            sb.AppendLine($"Request JSON: {Path.Combine(_currentJob.AIContextRoot, "requests", $"{request.Id}.json")}");
            if (!string.IsNullOrWhiteSpace(request.CropPath))
                sb.AppendLine($"Crop: {request.CropPath}");
            if (request.LayerCount > 0 || !string.IsNullOrWhiteSpace(request.LayerManifestPath))
            {
                string layerSummary = request.LayerCount == 1 ? "1 layer" : $"{request.LayerCount} layers";
                if (!string.IsNullOrWhiteSpace(request.LayerManifestPath))
                    sb.AppendLine($"Layers: {layerSummary} ({request.LayerManifestPath})");
                else
                    sb.AppendLine($"Layers: {layerSummary}");
            }

            if (SmartContextStore.LoadAiResponse(_currentJob, request.Id) is { } response)
            {
                sb.AppendLine();
                sb.AppendLine("AI Response");
                sb.AppendLine($"Status: {response.Status}");
                if (!string.IsNullOrWhiteSpace(response.OutputText))
                    sb.AppendLine(response.OutputText);
                if (!string.IsNullOrWhiteSpace(response.Error))
                    sb.AppendLine(response.Error);
                if (!string.IsNullOrWhiteSpace(response.Model))
                    sb.AppendLine($"Model: {response.Model}");
                if (!string.IsNullOrWhiteSpace(response.RawResponsePath))
                    sb.AppendLine($"Raw response: {response.RawResponsePath}");
            }

            if (SmartContextStore.LoadAiActionDraft(_currentJob, request.Id) is { } draft)
            {
                sb.AppendLine();
                sb.AppendLine("AI Action Draft");
                sb.AppendLine($"Status: {draft.Status}");
                sb.AppendLine($"Actions: {draft.Actions.Count}");
                sb.AppendLine($"Draft JSON: {SmartContextStore.AiActionDraftPath(_currentJob, request.Id)}");
                if (!string.IsNullOrWhiteSpace(draft.Summary))
                    sb.AppendLine($"Summary: {draft.Summary}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(observation.Text);
        return sb.ToString();
    }
}
