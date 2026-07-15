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
    // 3D massing legacy guards, commands, review, and accept workflow.

    private static bool Legacy3DMassingWorkflowEnabled => false;

    private bool StopLegacy3DMassingWorkflow(string operation)
    {
        if (Legacy3DMassingWorkflowEnabled)
            return false;

        TxtStatus.Text = $"{operation}: legacy 3D massing is archived and disabled. Use the 3D viewer tab while this feature is rebuilt.";
        return true;
    }

    private void BtnBuildMassingDraft_Click(object sender, RoutedEventArgs e)
    {
        if (StopLegacy3DMassingWorkflow("Build 3D Draft"))
            return;

        BuildMassingDraftFromMarkers();
    }

    private async void Btn3dManagerAiSortTakeoffs_Click(object sender, RoutedEventArgs e)
    {
        if (StopLegacy3DMassingWorkflow("AI 3D Sort"))
            return;

        await BuildMassingDraftFromAiSortedTakeoffsAsync();
    }

    private void BtnDetectRoof_Click(object sender, RoutedEventArgs e)
    {
        if (StopLegacy3DMassingWorkflow("Auto Roof"))
            return;

        QueueRoofRecognitionRequest();
    }

    private void BtnReviewRoof_Click(object sender, RoutedEventArgs e)
    {
        if (StopLegacy3DMassingWorkflow("Review Roof"))
            return;

        ReviewMassingRoof();
    }

    private void BtnReviewOpenings_Click(object sender, RoutedEventArgs e)
    {
        if (StopLegacy3DMassingWorkflow("Review Openings"))
            return;

        ReviewMassingOpenings();
    }

    private void BtnAcceptMassingDraft_Click(object sender, RoutedEventArgs e)
    {
        if (StopLegacy3DMassingWorkflow("Accept 3D Draft"))
            return;

        AcceptMassingDraft();
    }

    private void AcceptMassingDraft()
    {
        if (!EnsureThreeDEditable("accept the 3D massing draft"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before accepting a 3D draft.";
            return;
        }

        SmartMassingDraft? draft;
        string path = SmartMassingDraftService.ModelPath(_currentJob);
        try
        {
            draft = SmartMassingDraftService.LoadDraft(_currentJob);
        }
        catch (Exception ex)
        {
            ShowOperationError("Accept 3D Draft", ex);
            return;
        }

        if (draft == null)
        {
            TxtStatus.Text = "Build a 3D draft before accepting it.";
            return;
        }

        string warning = string.Equals(draft.Roof.Status, "reviewed", StringComparison.OrdinalIgnoreCase)
            ? "Accept current 3D massing draft as reviewed project context?"
            : "Roof is not marked reviewed yet. Accept current 3D massing draft anyway?";
        if (MessageBox.Show(
                warning + "\n\nThis does not create takeoff quantities.",
                "Accept 3D Draft",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        draft.Status = "reviewed";
        draft.ReviewedAtUtc = DateTime.UtcNow.ToString("O");
        draft.ReviewNotes = "Accepted from the 3D Massing tab as reviewed context. Not a quantity source.";
        AddMassingAssumptionOnce(draft, $"3D draft accepted manually at {draft.ReviewedAtUtc}; use as reviewed AI context, not estimating geometry.");

        try
        {
            SmartMassingDraftService.SaveDraft(_currentJob, draft);
            string snapshotPath = SmartMassingDraftService.SaveSnapshot(_currentJob, draft);
            RefreshMassingDraftPanel(draft, path);
            if (_rightWorkspaceTabs != null && _massingTab != null)
                _rightWorkspaceTabs.SelectedItem = _massingTab;
            TxtStatus.Text =
                $"Accepted 3D draft -> {Path.GetRelativePath(_currentJob.RootPath, path)}; snapshot: {Path.GetRelativePath(_currentJob.RootPath, snapshotPath)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Accept 3D Draft", ex);
        }
    }

    private void ReviewMassingRoof()
    {
        if (!EnsureThreeDEditable("review 3D massing roof geometry"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before reviewing roof geometry.";
            return;
        }

        SmartMassingDraft? draft;
        string path = SmartMassingDraftService.ModelPath(_currentJob);
        try
        {
            draft = SmartMassingDraftService.LoadDraft(_currentJob);
        }
        catch (Exception ex)
        {
            ShowOperationError("Review Roof", ex);
            return;
        }

        if (draft == null)
        {
            PostStatusInfo("No 3D massing draft exists yet. Run Build 3D Draft first.");
            return;
        }

        var dialog = new RoofReviewDialog(draft.Roof)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
            return;

        draft.Roof = dialog.ReviewedRoof;
        draft.Status = "roof_reviewed";
        AddMassingAssumptionOnce(
            draft,
            $"Roof reviewed manually at {draft.Roof.ReviewedAtUtc}. Rebuild from markers may replace this reviewed roof state.");

        try
        {
            SmartMassingDraftService.SaveDraft(_currentJob, draft);
            RefreshMassingDraftPanel(draft, path);
            if (_rightWorkspaceTabs != null && _massingTab != null)
                _rightWorkspaceTabs.SelectedItem = _massingTab;
            TxtStatus.Text = $"Saved reviewed roof -> {Path.GetRelativePath(_currentJob.RootPath, path)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Review Roof", ex);
        }
    }

    private void ReviewMassingOpenings()
    {
        if (!EnsureThreeDEditable("review 3D massing openings"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before reviewing opening projections.";
            return;
        }

        SmartMassingDraft? draft;
        string path = SmartMassingDraftService.ModelPath(_currentJob);
        try
        {
            draft = SmartMassingDraftService.LoadDraft(_currentJob);
        }
        catch (Exception ex)
        {
            ShowOperationError("Review Openings", ex);
            return;
        }

        if (draft == null)
        {
            PostStatusInfo("No 3D massing draft exists yet. Run Build 3D Draft first.");
            return;
        }

        if (draft.Openings.Count == 0)
        {
            PostStatusInfo("No projected door/window/opening markers were found in the current 3D draft.");
            return;
        }

        var dialog = new MassingOpeningsReviewDialog(draft.Openings)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
            return;

        draft.Openings = dialog.ReviewedOpenings;
        draft.Status = "openings_reviewed";
        string reviewedAt = DateTime.UtcNow.ToString("O");
        AddMassingAssumptionOnce(
            draft,
            $"Projected openings reviewed manually at {reviewedAt}. Rebuild from markers may replace this reviewed opening state.");

        try
        {
            SmartMassingDraftService.SaveDraft(_currentJob, draft);
            RefreshMassingDraftPanel(draft, path);
            if (_rightWorkspaceTabs != null && _massingTab != null)
                _rightWorkspaceTabs.SelectedItem = _massingTab;
            int kept = draft.Openings.Count(opening => !string.Equals(opening.Status, "rejected", StringComparison.OrdinalIgnoreCase));
            RecordMassingOpeningFeedback(draft);
            TxtStatus.Text = $"Saved reviewed openings ({kept}/{draft.Openings.Count} kept) -> {Path.GetRelativePath(_currentJob.RootPath, path)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Review Openings", ex);
        }
    }

    private void RecordMassingOpeningFeedback(SmartMassingDraft draft)
    {
        if (_currentJob == null || draft.Openings.Count == 0)
            return;

        Dictionary<string, SmartAiMarker> markersById = SmartContextStore.LoadAiMarkers(_currentJob)
            .Where(marker => !string.IsNullOrWhiteSpace(marker.Id))
            .GroupBy(marker => marker.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < draft.Openings.Count; i++)
        {
            SmartMassingOpening opening = draft.Openings[i];
            bool accepted = !string.Equals(opening.Status, "rejected", StringComparison.OrdinalIgnoreCase);
            markersById.TryGetValue(opening.SourceMarkerId, out SmartAiMarker? marker);

            SmartLearningStore.AppendMarkerFeedback(
                _currentJob,
                new SmartMarkerFeedbackRecord
                {
                    EventType = "3d_opening_projection_review",
                    DraftId = draft.Id,
                    SourceMarkerId = opening.SourceMarkerId,
                    SourceMarkerType = marker?.Type ?? $"{opening.Type}_sample",
                    SourceMarkerSampleKind = marker?.SampleKind ?? "",
                    Outcome = accepted ? "accepted" : "rejected",
                    Applied = accepted,
                    ActionIndex = i,
                    ActionType = "3d_opening_projection",
                    Label = $"{opening.Type} projection on wall {opening.WallIndex}",
                    Page = opening.Page,
                    MeasurementType = "opening",
                    Confidence = opening.Confidence,
                    Points =
                    [
                        new SmartAiActionPoint
                        {
                            X = (float)opening.Center.X,
                            Y = (float)opening.Center.Y,
                        },
                    ],
                    Notes = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} | wall={1}; center={2:0.###},{3:0.###},{4:0.###}; size={5:0.###}x{6:0.###}",
                        opening.Notes.Trim(),
                        opening.WallIndex,
                        opening.Center.X,
                        opening.Center.Y,
                        opening.Center.Z,
                        opening.Width,
                        opening.Height),
                });
        }
    }

    private static void AddMassingAssumptionOnce(SmartMassingDraft draft, string text)
    {
        bool isRoofReview = text.Contains("Roof reviewed manually", StringComparison.OrdinalIgnoreCase);
        bool isAcceptedDraft = text.Contains("3D draft accepted manually", StringComparison.OrdinalIgnoreCase);
        bool isOpeningReview = text.Contains("Projected openings reviewed manually", StringComparison.OrdinalIgnoreCase);
        draft.Assumptions.RemoveAll(item =>
            isRoofReview && item.Contains("Roof reviewed manually", StringComparison.OrdinalIgnoreCase) ||
            isAcceptedDraft && item.Contains("3D draft accepted manually", StringComparison.OrdinalIgnoreCase) ||
            isOpeningReview && item.Contains("Projected openings reviewed manually", StringComparison.OrdinalIgnoreCase));
        draft.Assumptions.Add(text);
    }
}
