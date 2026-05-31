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
using OurPlaneCore.Controls;
using SkiaSharp;
using Path = System.IO.Path;

namespace OurPlaneCore;

public partial class MainWindow
{
    // 3D massing source-marker rows, details, navigation, and crop actions.

    private void RefreshMassingMarkerRows(SmartMassingDraft? draft)
    {
        if (_massingMarkerList == null)
            return;

        string selectedMarkerId = _selectedMassingMarkerId;
        if (string.IsNullOrWhiteSpace(selectedMarkerId))
            selectedMarkerId = SelectedMassingMarkerRow()?.MarkerId ?? "";
        _massingMarkerList.Items.Clear();
        if (_currentJob == null || draft == null)
        {
            _selectedMassingMarkerId = "";
            UpdateMassingMarkerActionButtons();
            return;
        }

        MassingMarkerReviewRow? restoreSelection = null;
        foreach (MassingMarkerReviewRow row in BuildMassingMarkerRows(draft))
        {
            _massingMarkerList.Items.Add(row);
            if (!string.IsNullOrWhiteSpace(selectedMarkerId) &&
                string.Equals(row.MarkerId, selectedMarkerId, StringComparison.OrdinalIgnoreCase))
            {
                restoreSelection = row;
            }
        }

        if (restoreSelection != null)
        {
            _massingMarkerList.SelectedItem = restoreSelection;
            _massingMarkerList.ScrollIntoView(restoreSelection);
        }

        UpdateMassingMarkerActionButtons();
    }

    private IReadOnlyList<MassingMarkerReviewRow> BuildMassingMarkerRows(SmartMassingDraft draft)
    {
        if (_currentJob == null)
            return [];

        var roles = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var draftPoints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var orderedIds = new List<string>();

        void AddMarkerRole(string markerId, string role)
        {
            if (string.IsNullOrWhiteSpace(markerId))
                return;

            markerId = markerId.Trim();
            if (!roles.TryGetValue(markerId, out SortedSet<string>? markerRoles))
            {
                markerRoles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                roles[markerId] = markerRoles;
            }

            markerRoles.Add(role);
            if (!orderedIds.Contains(markerId, StringComparer.OrdinalIgnoreCase))
                orderedIds.Add(markerId);
        }

        foreach (SmartMassingFootprint footprint in draft.Footprints)
        {
            foreach (string markerId in footprint.SourceMarkerIds)
                AddMarkerRole(markerId, $"Level {footprint.Level} footprint");
            double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
            foreach (SmartMassingPoint point in footprint.Points)
            {
                AddMarkerRole(point.SourceMarkerId, $"Level {footprint.Level} corner");
                draftPoints[point.SourceMarkerId] = $"{point.X:F2}, {point.Y:F2}, z {baseZ:F2}";
            }
        }

        foreach (string markerId in draft.Roof.SourceMarkerIds)
            AddMarkerRole(markerId, "Roof");
        foreach (SmartMassingOpening opening in draft.Openings)
            AddMarkerRole(opening.SourceMarkerId, string.IsNullOrWhiteSpace(opening.Type) ? "Opening" : opening.Type);
        foreach (string markerId in draft.SourceMarkerIds)
            AddMarkerRole(markerId, "Source");

        return orderedIds
            .Select(markerId =>
            {
                SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, markerId);
                bool isTakeoffSource = IsMassingTakeoffSourceId(markerId);
                string role = roles.TryGetValue(markerId, out SortedSet<string>? markerRoles)
                    ? string.Join(", ", markerRoles)
                    : "Source";
                string status = marker == null
                    ? isTakeoffSource ? "takeoff" : "missing"
                    : _hiddenAiMarkerTypes.Contains(marker.Type)
                        ? "hidden"
                        : marker.SampleKind;

                return new MassingMarkerReviewRow
                {
                    MarkerId = markerId,
                    Role = role,
                    Type = marker?.Type ?? (isTakeoffSource ? "takeoff" : ""),
                    Page = marker?.Page ?? (isTakeoffSource ? "measured takeoff" : ""),
                    PdfPoint = marker == null ? "" : $"{marker.PdfPoint.X:F0}, {marker.PdfPoint.Y:F0}",
                    DraftPoint = draftPoints.TryGetValue(markerId, out string? draftPoint) ? draftPoint : "",
                    Status = status,
                    Marker = marker,
                    HasCrop = marker != null && File.Exists(ResolveAiContextPath(marker.CropPath)),
                };
            })
            .ToList();
    }

    private void UpdateMassingMarkerActionButtons()
    {
        MassingMarkerReviewRow? row = SelectedMassingMarkerRow();
        if (row != null)
            _selectedMassingMarkerId = row.MarkerId;
        else if (_massingMarkerList?.Items.Count == 0)
            _selectedMassingMarkerId = "";
        bool hasMarker = row?.Marker != null;
        if (_massingJumpMarkerButton != null)
            _massingJumpMarkerButton.IsEnabled = hasMarker;
        if (_massingOpenMarkerButton != null)
            _massingOpenMarkerButton.IsEnabled = hasMarker;
        if (_massingOpenMarkerCropButton != null)
            _massingOpenMarkerCropButton.IsEnabled = row?.HasCrop == true;
        UpdateMassingMarkerDetails(row);
        DrawMassingPreview(_currentMassingDraft);
        RefreshMassing3DPreview(_currentMassingDraft);
    }

    private void UpdateMassingMarkerDetails(MassingMarkerReviewRow? row)
    {
        if (_massingMarkerDetailsTextBox == null)
            return;

        if (row == null)
        {
            _massingMarkerDetailsTextBox.Text = "Select a source marker to inspect the evidence behind the draft.";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Marker: {row.MarkerId}");
        sb.AppendLine($"Role: {row.Role}");
        sb.AppendLine($"Status: {row.Status}");
        if (!string.IsNullOrWhiteSpace(row.DraftPoint))
            sb.AppendLine($"Draft point: {row.DraftPoint}");

        if (row.Marker is not { } marker)
        {
            sb.AppendLine(IsMassingTakeoffSourceId(row.MarkerId)
                ? "Source is a measured takeoff, not an AI marker JSON file. Use the Takeoffs tree or sheet legend to review the source measurement."
                : "Marker JSON is missing, so this draft source needs review.");
            _massingMarkerDetailsTextBox.Text = sb.ToString();
            return;
        }

        sb.AppendLine($"Type: {marker.Type}");
        sb.AppendLine($"Sample: {marker.SampleKind}");
        sb.AppendLine($"Page: {marker.Page}");
        sb.AppendLine($"PDF point: {marker.PdfPoint.X:F1}, {marker.PdfPoint.Y:F1}");
        if (marker.PdfRect.Right > marker.PdfRect.Left && marker.PdfRect.Bottom > marker.PdfRect.Top)
            sb.AppendLine($"PDF rect: {marker.PdfRect.Left:F1}, {marker.PdfRect.Top:F1}, {marker.PdfRect.Right:F1}, {marker.PdfRect.Bottom:F1}");
        if (!string.IsNullOrWhiteSpace(marker.Value))
            sb.AppendLine($"Value: {marker.Value}");
        if (!string.IsNullOrWhiteSpace(marker.Note))
            sb.AppendLine($"Note: {marker.Note}");
        if (!string.IsNullOrWhiteSpace(marker.CropPath))
        {
            string cropPath = ResolveAiContextPath(marker.CropPath);
            sb.AppendLine(File.Exists(cropPath)
                ? $"Crop: {marker.CropPath}"
                : $"Crop missing: {marker.CropPath}");
        }
        if (_currentJob != null)
            sb.AppendLine($"JSON: {SmartContextStore.AiMarkerPath(_currentJob, marker.Id)}");

        _massingMarkerDetailsTextBox.Text = sb.ToString();
    }

    private MassingMarkerReviewRow? SelectedMassingMarkerRow() =>
        _massingMarkerList?.SelectedItem as MassingMarkerReviewRow;

    private static bool IsMassingTakeoffSourceId(string sourceId) =>
        sourceId.StartsWith("takeoff:", StringComparison.OrdinalIgnoreCase);

    private bool SelectMassingMarkerById(string markerId)
    {
        if (_massingMarkerList == null || string.IsNullOrWhiteSpace(markerId))
            return false;

        foreach (object item in _massingMarkerList.Items)
        {
            if (item is not MassingMarkerReviewRow row ||
                !string.Equals(row.MarkerId, markerId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _selectedMassingMarkerId = row.MarkerId;
            _massingMarkerList.SelectedItem = row;
            _massingMarkerList.ScrollIntoView(row);
            UpdateMassingMarkerActionButtons();
            return true;
        }

        return false;
    }

    private void JumpToSelectedMassingMarker()
    {
        if (_currentJob == null || SelectedMassingMarkerRow()?.Marker is not { } marker)
            return;

        PageInfo? page = ResolveMassingMarkerPage(marker);
        if (page == null)
        {
            TxtStatus.Text = $"Source marker page is missing for {marker.Id}.";
            return;
        }

        SelectPageByFolder(page.FolderPath);
        Dispatcher.BeginInvoke(() =>
        {
            _viewport.FocusPdfPoint(marker.PdfPoint.X, marker.PdfPoint.Y);
        }, System.Windows.Threading.DispatcherPriority.Background);
        TxtStatus.Text = $"Opened source marker {marker.Id} on {page.Name} at PDF {marker.PdfPoint.X:F1}, {marker.PdfPoint.Y:F1}.";
    }

    private void OpenSelectedMassingMarkerJson()
    {
        if (_currentJob == null || SelectedMassingMarkerRow()?.Marker is not { } marker)
            return;

        OpenJsonFile(SmartContextStore.AiMarkerPath(_currentJob, marker.Id), "AI marker JSON is missing.");
    }

    private void OpenSelectedMassingMarkerCrop()
    {
        if (_currentJob == null || SelectedMassingMarkerRow()?.Marker is not { } marker)
            return;

        string path = ResolveAiContextPath(marker.CropPath);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            TxtStatus.Text = "AI marker crop file is missing.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    private PageInfo? ResolveMassingMarkerPage(SmartAiMarker marker)
    {
        if (_currentJob == null)
            return null;

        if (!string.IsNullOrWhiteSpace(marker.PageFolder))
        {
            string folder = Path.IsPathFullyQualified(marker.PageFolder)
                ? marker.PageFolder
                : Path.GetFullPath(Path.Combine(_currentJob.RootPath, marker.PageFolder));
            PageInfo? page = OurPlaneCoreJobStore.TryReadPage(folder);
            if (page != null)
                return page;
        }

        return FindPageByName(marker.Page);
    }

    private string ResolveAiContextPath(string path)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(path))
            return "";

        return Path.IsPathFullyQualified(path)
            ? path
            : Path.GetFullPath(Path.Combine(_currentJob.AIContextRoot, path));
    }
}
