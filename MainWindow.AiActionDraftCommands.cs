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
    // AI action draft file commands, preview, review, and feedback entry points.

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

        path = OurPlanCoreJobStore.PageLayersJsonPath(page.FolderPath);
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

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (IsRoofRecognitionRequest(request))
            return false;

        SmartAiActionDraft? draft = SmartContextStore.LoadAiActionDraft(_currentJob, item.Observation.Id);
        return draft?.Actions.Any(action => action.Points.Count > 0) == true;
    }

    private bool CanApplyAiActionDraft(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (IsRoofRecognitionRequest(request))
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

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (IsRoofRecognitionRequest(request))
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
        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (IsRoofRecognitionRequest(request) && StopLegacy3DMassingWorkflow("Auto Roof preview"))
            return;

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
        if (IsRoofRecognitionRequest(request) && StopLegacy3DMassingWorkflow("Auto Roof review"))
            return;

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
}
