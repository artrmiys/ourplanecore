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
            TxtStatus.Text = "Build a 3D draft before reviewing roof geometry.";
            MessageBox.Show(
                "No 3D massing draft exists yet. Run Build 3D Draft first.",
                "Review Roof",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
            TxtStatus.Text = "Build a 3D draft before reviewing openings.";
            MessageBox.Show(
                "No 3D massing draft exists yet. Run Build 3D Draft first.",
                "Review Openings",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (draft.Openings.Count == 0)
        {
            TxtStatus.Text = "No projected openings are available for review.";
            MessageBox.Show(
                "No projected door/window/opening markers were found in the current 3D draft.",
                "Review Openings",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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

    private void BuildMassingDraftFromMarkers()
    {
        if (StopLegacy3DMassingWorkflow("Build 3D Draft"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before building a 3D draft.";
            return;
        }

        try
        {
            SmartMassingDraft draft = SmartMassingDraftService.SaveDraftFromMarkers(_currentJob);
            string path = SmartMassingDraftService.ModelPath(_currentJob);
            RefreshMassingDraftPanel(draft, path);
            if (_rightWorkspaceTabs != null && _massingTab != null)
                _rightWorkspaceTabs.SelectedItem = _massingTab;

            string summary = BuildMassingDraftSummary(draft, path);
            TxtStatus.Text = $"Saved 3D massing draft -> {Path.GetRelativePath(_currentJob.RootPath, path)}";
            MessageBox.Show(summary, "Build 3D Draft", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowOperationError("Build 3D Draft", ex);
        }
    }

    private void BuildMassingDraftFromWallTakeoffs()
    {
        if (StopLegacy3DMassingWorkflow("3D From Takeoffs"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before building a 3D draft from takeoffs.";
            return;
        }

        double currentLevelSpacing = _settings.MassingLevelSpacingFeet > 0
            ? _settings.MassingLevelSpacingFeet
            : SmartMassingDraftService.DefaultLevelSpacingFeet;
        string? rawLevelSpacing = ShowInputDialog(
            "Default level spacing and roof step, feet (1st=0, 2nd=+spacing, roof=last+spacing):",
            currentLevelSpacing.ToString("G", CultureInfo.InvariantCulture),
            "3D From Takeoffs");
        if (rawLevelSpacing == null)
            return;
        if (!double.TryParse(rawLevelSpacing.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out double levelSpacingFeet) ||
            levelSpacingFeet <= 0 ||
            levelSpacingFeet > 40)
        {
            MessageBox.Show("Enter a level spacing value between 1 and 40 feet.", "3D From Takeoffs", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _settings.MassingLevelSpacingFeet = levelSpacingFeet;
            SaveAppSettings();

            SmartMassingDraft draft = SmartMassingDraftService.SaveDraftFromWallTakeoffs(_currentJob, levelSpacingFeet);
            string path = SmartMassingDraftService.ModelPath(_currentJob);
            RefreshMassingDraftPanel(draft, path);
            if (_rightWorkspaceTabs != null && _massingTab != null)
                _rightWorkspaceTabs.SelectedItem = _massingTab;

            string summary = BuildMassingDraftSummary(draft, path);
            TxtStatus.Text = $"Saved 3D draft from takeoffs -> {Path.GetRelativePath(_currentJob.RootPath, path)}";
            MessageBox.Show(summary, "3D From Takeoffs", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowOperationError("3D From Takeoffs", ex);
        }
    }

    private async Task BuildMassingDraftFromAiSortedTakeoffsAsync()
    {
        if (StopLegacy3DMassingWorkflow("AI 3D Sort"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before running AI 3D Sort.";
            return;
        }

        string apiKey = AppSettingsStore.ReadOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            TxtStatus.Text = "OPENAI_API_KEY is missing. Open AI Settings before running AI 3D Sort.";
            MessageBox.Show(
                "OPENAI_API_KEY is missing. Save it in AI Settings first.",
                "AI 3D Sort",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        double currentLevelSpacing = _settings.MassingLevelSpacingFeet > 0
            ? _settings.MassingLevelSpacingFeet
            : SmartMassingDraftService.DefaultLevelSpacingFeet;
        string? rawLevelSpacing = ShowInputDialog(
            "Default level spacing and roof step, feet (OpenAI sorts roles/levels only):",
            currentLevelSpacing.ToString("G", CultureInfo.InvariantCulture),
            "AI 3D Sort");
        if (rawLevelSpacing == null)
            return;
        if (!double.TryParse(rawLevelSpacing.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out double levelSpacingFeet) ||
            levelSpacingFeet <= 0 ||
            levelSpacingFeet > 40)
        {
            MessageBox.Show("Enter a level spacing value between 1 and 40 feet.", "AI 3D Sort", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _settings.MassingLevelSpacingFeet = levelSpacingFeet;
            SaveAppSettings();
            string model = AppSettingsStore.ResolveOpenAiModel(_settings);
            TxtStatus.Text = $"AI 3D Sort running with {model}...";

            SmartMassingAiTakeoffBuildResult result;
            using (ShowBusyOverlay($"AI 3D Sort running with {model}..."))
            {
                await WaitForBusyOverlayRenderAsync();
                result = await SmartMassingTakeoffAiPlanner.BuildDraftAsync(
                    _currentJob,
                    levelSpacingFeet,
                    apiKey,
                    model,
                    CancellationToken.None);
            }
            if (!result.Success || result.Draft == null)
            {
                TxtStatus.Text = $"AI 3D Sort failed: {result.Error}";
                MessageBox.Show(
                    $"AI 3D Sort failed:\n{result.Error}\n\nInput: {result.InputPath}\nRaw: {result.RawResponsePath}",
                    "AI 3D Sort",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string path = SmartMassingDraftService.ModelPath(_currentJob);
            RefreshMassingDraftPanel(result.Draft, path);
            Refresh3dManagerSummary();
            if (_rightWorkspaceTabs != null && _massingTab != null)
                _rightWorkspaceTabs.SelectedItem = _massingTab;

            string summary = BuildMassingDraftSummary(result.Draft, path);
            TxtStatus.Text = $"AI 3D Sort saved draft -> {Path.GetRelativePath(_currentJob.RootPath, path)}";
            MessageBox.Show(
                $"{summary}\nAI sort files:\n- Input: {result.InputPath}\n- Plan: {result.PlanPath}\n- Raw: {result.RawResponsePath}",
                "AI 3D Sort",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowOperationError("AI 3D Sort", ex);
        }
    }

    private void OpenMassingDraftJson()
    {
        if (StopLegacy3DMassingWorkflow("Open 3D JSON"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before opening the 3D draft.";
            return;
        }

        OpenJsonFile(SmartMassingDraftService.ModelPath(_currentJob), "3D massing draft JSON is missing.");
    }

    private void OpenMassing3DWindow()
    {
        if (StopLegacy3DMassingWorkflow("Open 3D Window"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before opening the 3D viewport.";
            return;
        }

        try
        {
            SmartMassingDraft? draft = _currentMassingDraft;
            string path = SmartMassingDraftService.ModelPath(_currentJob);
            if (draft == null && File.Exists(path))
                draft = SmartMassingDraftService.LoadDraft(_currentJob);

            IReadOnlyList<SmartAiMarker> markers = SmartContextStore.LoadAiMarkers(_currentJob);
            if (draft == null && markers.Count > 0)
                draft = SmartMassingDraftService.BuildDraftFromMarkers(_currentJob);
            if (draft != null)
                SmartMassingDraftService.RefreshDerivedGeometry(draft);

            var window = new Massing3DWindow(_currentJob, draft, markers)
            {
                Owner = this,
            };
            window.Show();

            int footprintPoints = draft?.Footprints.Sum(footprint => footprint.Points.Count) ?? 0;
            TxtStatus.Text = $"Opened 3D viewport window with {markers.Count} marker(s) and {footprintPoints} footprint point(s).";
        }
        catch (Exception ex)
        {
            ShowOperationError("Open 3D Viewport", ex);
        }
    }

    private void RefreshMassingDraftPanel(SmartMassingDraft? draft = null, string? path = null)
    {
        if (_massingDraftTextBox == null)
            return;

        if (_currentJob == null)
        {
            _currentMassingDraft = null;
            _massingDraftTextBox.Text = "Open a job, then build a 3D massing draft from reviewed AI markers.";
            if (_massingOpenDraftButton != null)
                _massingOpenDraftButton.IsEnabled = false;
            if (_massingReviewRoofButton != null)
                _massingReviewRoofButton.IsEnabled = false;
            if (_massingReviewOpeningsButton != null)
                _massingReviewOpeningsButton.IsEnabled = false;
            if (_massingAcceptDraftButton != null)
                _massingAcceptDraftButton.IsEnabled = false;
            DrawMassingPreview(null);
            RefreshMassing3DPreview(null);
            RefreshMassingMarkerRows(null);
            return;
        }

        path ??= SmartMassingDraftService.ModelPath(_currentJob);
        if (draft == null && File.Exists(path))
        {
            try
            {
                draft = SmartMassingDraftService.LoadDraft(_currentJob);
            }
            catch
            {
                draft = null;
            }
        }

        bool exists = File.Exists(path);
        if (_massingOpenDraftButton != null)
            _massingOpenDraftButton.IsEnabled = exists;
        if (_massingReviewRoofButton != null)
            _massingReviewRoofButton.IsEnabled = exists && draft != null;
        if (_massingReviewOpeningsButton != null)
            _massingReviewOpeningsButton.IsEnabled = exists && draft != null && draft.Openings.Count > 0;
        if (_massingAcceptDraftButton != null)
            _massingAcceptDraftButton.IsEnabled = exists && draft != null && draft.Footprints.Any(footprint => footprint.Points.Count >= 3);

        _currentMassingDraft = draft;
        if (draft != null)
            SmartMassingDraftService.RefreshDerivedGeometry(draft);
        _massingDraftTextBox.Text = draft == null
            ? $"No 3D massing draft exists yet.\n\nTarget path:\n{path}\n\nUse Build 3D Draft after placing exterior_corner markers."
            : BuildMassingDraftSummary(draft, path);
        DrawMassingPreview(draft);
        RefreshMassing3DPreview(draft);
        RefreshMassingMarkerRows(draft);
    }

    private void RefreshMassing3DPreview(SmartMassingDraft? draft)
    {
        if (_massingViewport3D == null)
            return;

        _massingViewport3D.Children.Clear();
        _massing3DObjectInfo.Clear();

        List<SmartMassingFootprint> footprints = draft?.Footprints
            .Where(footprint => footprint.Points.Count >= 3)
            .OrderBy(footprint => SmartMassingDraftService.DisplayBaseElevation(draft, footprint))
            .ThenBy(footprint => footprint.Level)
            .ToList() ?? [];
        if (draft == null || footprints.Count == 0)
        {
            if (_massingViewportStatusText != null)
                _massingViewportStatusText.Text = draft == null
                    ? "Build a draft to preview the 3D shell."
                    : "Draft has no footprint loop for 3D preview.";
            return;
        }

        SmartMassingDraftService.RefreshDerivedGeometry(draft);
        if (!TryGetMassing3DBounds(draft, out double minX, out double maxX, out double minY, out double maxY, out double maxZ))
        {
            if (_massingViewportStatusText != null)
                _massingViewportStatusText.Text = "Draft bounds are not valid for 3D preview.";
            return;
        }

        double centerX = (minX + maxX) / 2;
        double centerY = (minY + maxY) / 2;
        double spanX = Math.Max(0.001, maxX - minX);
        double spanY = Math.Max(0.001, maxY - minY);
        _massing3DSceneRadius = Math.Max(Math.Max(spanX, spanY), Math.Max(maxZ, 1));
        _massing3DTarget = new Point3D(0, Math.Max(maxZ, 1) / 2, 0);

        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(92, 92, 92)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(245, 245, 245), new Vector3D(-0.45, -0.8, -0.35)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(130, 160, 190), new Vector3D(0.65, -0.35, 0.55)));

        foreach (SmartMassingFootprint footprint in footprints)
        {
            AddMassingFootprint3D(group, draft, footprint, centerX, centerY);
            AddMassingMarkerPins3D(group, draft, footprint, centerX, centerY);
        }
        AddMassingRoofPlanes3D(group, draft, centerX, centerY);
        AddMassingOpenings3D(group, draft, centerX, centerY);

        _massingViewport3D.Children.Add(new ModelVisual3D { Content = group });
        EnsureMassingCamera();
        FitMassing3DView(resetAngles: false);

        if (_massingViewportStatusText != null)
        {
            int wallCount = footprints.Sum(footprint => footprint.Points.Count);
            int roofPlanes = draft.Roof.Planes.Count(plane => !string.Equals(plane.Status, "rejected", StringComparison.OrdinalIgnoreCase));
            int openings = draft.Openings.Count(opening => !string.Equals(opening.Status, "rejected", StringComparison.OrdinalIgnoreCase));
            _massingViewportStatusText.Text = $"3D shell | levels: {footprints.Count} | walls: {wallCount} | roof planes: {roofPlanes} | openings: {openings} | roof: {draft.Roof.Type} ({draft.Roof.Status})";
        }
    }

    private void AddMassingFootprint3D(
        Model3DGroup group,
        SmartMassingDraft draft,
        SmartMassingFootprint footprint,
        double centerX,
        double centerY)
    {
        double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
        double wallTopZ = SmartMassingDraftService.DisplayWallTopElevation(draft, footprint);
        var floor = footprint.Points
            .Select(point => ToMassing3DPoint(point.X, point.Y, baseZ, centerX, centerY))
            .ToList();
        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                $"floor_level_{footprint.Level}",
                "floor",
                $"Level {footprint.Level} floor/footprint",
                footprint.SourceMarkerIds,
                "Floor cap generated from exterior corner markers."),
            floor,
            Color.FromRgb(82, 91, 102),
            0.42);

        for (int i = 0; i < footprint.Points.Count; i++)
        {
            SmartMassingPoint start = footprint.Points[i];
            SmartMassingPoint end = footprint.Points[(i + 1) % footprint.Points.Count];
            var sourceIds = new[]
                {
                    start.SourceMarkerId,
                    end.SourceMarkerId,
                }
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            AddMassingSurface(
                group,
                new Massing3DObjectInfo(
                    $"wall_level_{footprint.Level}_{i + 1}",
                    "wall",
                    $"Level {footprint.Level} wall {i + 1}",
                    sourceIds,
                    "Wall face generated by extruding adjacent footprint points."),
                [
                    ToMassing3DPoint(start.X, start.Y, baseZ, centerX, centerY),
                    ToMassing3DPoint(end.X, end.Y, baseZ, centerX, centerY),
                    ToMassing3DPoint(end.X, end.Y, wallTopZ, centerX, centerY),
                    ToMassing3DPoint(start.X, start.Y, wallTopZ, centerX, centerY),
                ],
                Color.FromRgb(148, 163, 184),
                0.72);
        }
    }

    private void AddMassingRoofPlanes3D(
        Model3DGroup group,
        SmartMassingDraft draft,
        double centerX,
        double centerY)
    {
        foreach (SmartMassingPlane plane in draft.Roof.Planes)
        {
            if (string.Equals(plane.Status, "rejected", StringComparison.OrdinalIgnoreCase) ||
                plane.Points.Count < 3)
            {
                continue;
            }

            Color color = plane.Kind.Contains("candidate", StringComparison.OrdinalIgnoreCase)
                ? Color.FromRgb(245, 158, 11)
                : Color.FromRgb(71, 123, 156);
            double opacity = plane.Status == "reviewed" ? 0.86 : 0.68;
            AddMassingSurface(
                group,
                new Massing3DObjectInfo(
                    plane.Id,
                    plane.Kind,
                    plane.Label,
                    plane.SourceMarkerIds,
                    plane.Notes),
                plane.Points.Select(point => ToMassing3DPoint(point.X, point.Y, point.Z, centerX, centerY)).ToList(),
                color,
                opacity);
        }
    }

    private void AddMassingOpenings3D(
        Model3DGroup group,
        SmartMassingDraft draft,
        double centerX,
        double centerY)
    {
        foreach (SmartMassingOpening opening in draft.Openings)
        {
            SmartMassingFootprint? footprint = FootprintForMassingOpening(draft, opening);
            if (string.Equals(opening.Status, "rejected", StringComparison.OrdinalIgnoreCase) ||
                footprint == null ||
                opening.WallIndex < 0 ||
                opening.WallIndex >= footprint.Points.Count ||
                opening.Width <= 0 ||
                opening.Height <= 0)
            {
                continue;
            }

            SmartMassingPoint start = footprint.Points[opening.WallIndex];
            SmartMassingPoint end = footprint.Points[(opening.WallIndex + 1) % footprint.Points.Count];
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.0001)
                continue;

            double ux = dx / length;
            double uy = dy / length;
            double halfW = Math.Min(opening.Width / 2.0, length / 2.0);
            double halfH = opening.Height / 2.0;
            double zMin = Math.Max(0.03, opening.Center.Z - halfH);
            double zMax = opening.Center.Z + halfH;
            double x1 = opening.Center.X - ux * halfW;
            double y1 = opening.Center.Y - uy * halfW;
            double x2 = opening.Center.X + ux * halfW;
            double y2 = opening.Center.Y + uy * halfW;

            Color color = opening.Type switch
            {
                "door" => Color.FromRgb(250, 204, 21),
                "window" => Color.FromRgb(34, 211, 238),
                _ => Color.FromRgb(168, 85, 247),
            };
            AddMassingSurface(
                group,
                new Massing3DObjectInfo(
                    $"opening_{opening.SourceMarkerId}",
                    opening.Type,
                    $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(opening.Type)} projection",
                    string.IsNullOrWhiteSpace(opening.SourceMarkerId) ? [] : [opening.SourceMarkerId],
                    opening.Notes),
                [
                    ToMassing3DPoint(x1, y1, zMin, centerX, centerY),
                    ToMassing3DPoint(x2, y2, zMin, centerX, centerY),
                    ToMassing3DPoint(x2, y2, zMax, centerX, centerY),
                    ToMassing3DPoint(x1, y1, zMax, centerX, centerY),
                ],
                color,
                0.92);
        }
    }

    private void AddMassingMarkerPins3D(
        Model3DGroup group,
        SmartMassingDraft draft,
        SmartMassingFootprint footprint,
        double centerX,
        double centerY)
    {
        double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
        double wallHeight = SmartMassingDraftService.DisplayWallHeight(draft, footprint);
        foreach (SmartMassingPoint point in footprint.Points)
        {
            if (string.IsNullOrWhiteSpace(point.SourceMarkerId))
                continue;
            AddMassingPin3D(
                group,
                $"pin_{point.SourceMarkerId}",
                "marker_pin",
                "Exterior corner marker",
                point.SourceMarkerId,
                ToMassing3DPoint(point.X, point.Y, baseZ, centerX, centerY),
                Color.FromRgb(56, 189, 248),
                wallHeight);
        }

        foreach (SmartMassingOpening opening in draft.Openings.Where(opening => opening.Level == footprint.Level))
        {
            if (string.IsNullOrWhiteSpace(opening.SourceMarkerId))
                continue;
            AddMassingPin3D(
                group,
                $"pin_{opening.SourceMarkerId}",
                "opening_pin",
                $"{opening.Type} marker",
                opening.SourceMarkerId,
                ToMassing3DPoint(opening.Center.X, opening.Center.Y, opening.Center.Z, centerX, centerY),
                Color.FromRgb(244, 114, 182),
                wallHeight);
        }
    }

    private void AddMassingPin3D(
        Model3DGroup group,
        string id,
        string kind,
        string label,
        string sourceMarkerId,
        Point3D center,
        Color color,
        double modelHeight)
    {
        double size = Math.Max(0.45, Math.Min(_massing3DSceneRadius * 0.045, Math.Max(0.55, modelHeight * 0.095)));
        var points = new List<Point3D>
        {
            new(center.X - size, center.Y, center.Z - size),
            new(center.X + size, center.Y, center.Z - size),
            new(center.X + size, center.Y, center.Z + size),
            new(center.X - size, center.Y, center.Z + size),
            new(center.X, center.Y + size * 2.8, center.Z),
        };

        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                id,
                kind,
                label,
                string.IsNullOrWhiteSpace(sourceMarkerId) ? [] : [sourceMarkerId],
                "3D source marker pin."),
            [points[0], points[1], points[4]],
            color,
            0.95);
        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                $"{id}_b",
                kind,
                label,
                string.IsNullOrWhiteSpace(sourceMarkerId) ? [] : [sourceMarkerId],
                "3D source marker pin."),
            [points[1], points[2], points[4]],
            color,
            0.95);
        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                $"{id}_c",
                kind,
                label,
                string.IsNullOrWhiteSpace(sourceMarkerId) ? [] : [sourceMarkerId],
                "3D source marker pin."),
            [points[2], points[3], points[4]],
            color,
            0.95);
        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                $"{id}_d",
                kind,
                label,
                string.IsNullOrWhiteSpace(sourceMarkerId) ? [] : [sourceMarkerId],
                "3D source marker pin."),
            [points[3], points[0], points[4]],
            color,
            0.95);
    }

    private void AddMassingSurface(
        Model3DGroup group,
        Massing3DObjectInfo info,
        IReadOnlyList<Point3D> points,
        Color color,
        double opacity)
    {
        if (points.Count < 3)
            return;

        var mesh = new MeshGeometry3D();
        foreach (Point3D point in points)
            mesh.Positions.Add(point);
        for (int i = 1; i < points.Count - 1; i++)
        {
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 1);
        }

        bool selected = IsSelectedMassing3DObject(info);
        var brush = new SolidColorBrush(selected ? Color.FromRgb(255, 183, 77) : color)
        {
            Opacity = selected ? Math.Min(1.0, opacity + 0.1) : opacity,
        };
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material)
        {
            BackMaterial = material,
        };
        _massing3DObjectInfo[model] = info;
        group.Children.Add(model);
    }

    private bool IsSelectedMassing3DObject(Massing3DObjectInfo info)
    {
        if (!string.IsNullOrWhiteSpace(_selectedMassing3DObjectId) &&
            string.Equals(_selectedMassing3DObjectId, info.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string selectedMarkerId = _selectedMassingMarkerId;
        if (string.IsNullOrWhiteSpace(selectedMarkerId))
            selectedMarkerId = SelectedMassingMarkerRow()?.MarkerId ?? "";
        return !string.IsNullOrWhiteSpace(selectedMarkerId) &&
               info.SourceMarkerIds.Contains(selectedMarkerId, StringComparer.OrdinalIgnoreCase);
    }

    private static Point3D ToMassing3DPoint(double x, double y, double z, double centerX, double centerY) =>
        new(x - centerX, z, y - centerY);

    private static bool TryGetMassing3DBounds(
        SmartMassingDraft draft,
        out double minX,
        out double maxX,
        out double minY,
        out double maxY,
        out double maxZ)
    {
        var vertices = new List<SmartMassingVertex>();
        foreach (SmartMassingFootprint footprint in draft.Footprints.Where(footprint => footprint.Points.Count >= 3))
        {
            double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
            double wallTopZ = SmartMassingDraftService.DisplayWallTopElevation(draft, footprint);
            vertices.AddRange(footprint.Points.Select(point => new SmartMassingVertex { X = point.X, Y = point.Y, Z = baseZ }));
            vertices.AddRange(footprint.Points.Select(point => new SmartMassingVertex { X = point.X, Y = point.Y, Z = wallTopZ }));
        }
        vertices.AddRange(draft.Roof.Planes.SelectMany(plane => plane.Points));
        vertices.AddRange(draft.Openings.Select(opening => opening.Center));

        vertices = vertices
            .Where(point =>
                !double.IsNaN(point.X) &&
                !double.IsNaN(point.Y) &&
                !double.IsNaN(point.Z))
            .ToList();
        if (vertices.Count == 0)
        {
            minX = maxX = minY = maxY = maxZ = 0;
            return false;
        }

        minX = vertices.Min(point => point.X);
        maxX = vertices.Max(point => point.X);
        minY = vertices.Min(point => point.Y);
        maxY = vertices.Max(point => point.Y);
        maxZ = Math.Max(1, vertices.Max(point => point.Z));
        return maxX > minX && maxY > minY;
    }

    private static SmartMassingFootprint? FootprintForMassingOpening(SmartMassingDraft draft, SmartMassingOpening opening)
    {
        SmartMassingFootprint? exact = draft.Footprints
            .Where(footprint => footprint.Points.Count >= 3)
            .FirstOrDefault(footprint => footprint.Level == opening.Level);
        return exact ?? draft.Footprints
            .Where(footprint => footprint.Points.Count >= 3)
            .OrderByDescending(footprint => SmartMassingDraftService.DisplayWallTopElevation(draft, footprint))
            .ThenByDescending(footprint => footprint.Level)
            .FirstOrDefault();
    }

    private void EnsureMassingCamera()
    {
        if (_massingViewport3D == null)
            return;

        _massingCamera3D ??= new PerspectiveCamera
        {
            FieldOfView = 42,
            UpDirection = new Vector3D(0, 1, 0),
        };
        _massingViewport3D.Camera = _massingCamera3D;
    }

    private void FitMassing3DView(bool resetAngles)
    {
        if (_massingCamera3D == null)
            EnsureMassingCamera();
        if (_massingCamera3D == null)
            return;

        if (resetAngles)
        {
            _massing3DYaw = -38;
            _massing3DPitch = 28;
        }

        _massing3DDistance = Math.Max(12, _massing3DSceneRadius * 2.65);
        UpdateMassing3DCamera();
    }

    private void SetMassing3DView(double yaw, double pitch)
    {
        _massing3DYaw = yaw;
        _massing3DPitch = Math.Clamp(pitch, -8, 88);
        FitMassing3DView(resetAngles: false);
    }

    private void UpdateMassing3DCamera()
    {
        if (_massingCamera3D == null)
            return;

        double yaw = _massing3DYaw * Math.PI / 180.0;
        double pitch = _massing3DPitch * Math.PI / 180.0;
        double horizontal = _massing3DDistance * Math.Cos(pitch);
        var position = new Point3D(
            _massing3DTarget.X + horizontal * Math.Sin(yaw),
            _massing3DTarget.Y + _massing3DDistance * Math.Sin(pitch),
            _massing3DTarget.Z + horizontal * Math.Cos(yaw));

        _massingCamera3D.Position = position;
        _massingCamera3D.LookDirection = _massing3DTarget - position;
        _massingCamera3D.UpDirection = new Vector3D(0, 1, 0);
    }

    private void MassingViewport3D_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_massingViewport3D == null)
            return;

        Point position = e.GetPosition(_massingViewport3D);
        _massing3DDragStart = position;
        _massing3DMouseDown = position;
        _massing3DMouseMoved = false;
        _massingViewport3D.CaptureMouse();
    }

    private void MassingViewport3D_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_massingViewport3D != null && _massing3DMouseDown != null && !_massing3DMouseMoved)
            TrySelectMassing3DObject(e.GetPosition(_massingViewport3D));

        _massing3DDragStart = null;
        _massing3DMouseDown = null;
        _massingViewport3D?.ReleaseMouseCapture();
    }

    private void MassingViewport3D_MouseMove(object sender, MouseEventArgs e)
    {
        if (_massingViewport3D == null || _massing3DDragStart == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point current = e.GetPosition(_massingViewport3D);
        Vector delta = current - _massing3DDragStart.Value;
        if (delta.Length > 2.5)
            _massing3DMouseMoved = true;
        _massing3DDragStart = current;
        _massing3DYaw += delta.X * 0.45;
        _massing3DPitch = Math.Clamp(_massing3DPitch - delta.Y * 0.35, -8, 88);
        UpdateMassing3DCamera();
    }

    private void TrySelectMassing3DObject(Point point)
    {
        if (_massingViewport3D == null)
            return;

        Massing3DObjectInfo? selected = null;
        VisualTreeHelper.HitTest(
            _massingViewport3D,
            null,
            result =>
            {
                if (result is RayHitTestResult ray &&
                    ray.ModelHit is GeometryModel3D model &&
                    _massing3DObjectInfo.TryGetValue(model, out Massing3DObjectInfo? info))
                {
                    selected = info;
                    return HitTestResultBehavior.Stop;
                }

                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(point));

        if (selected != null)
            SelectMassing3DObject(selected);
    }

    private void SelectMassing3DObject(Massing3DObjectInfo info)
    {
        _selectedMassing3DObjectId = info.Id;
        SelectFirstMassingMarker(info.SourceMarkerIds);
        RefreshMassing3DPreview(_currentMassingDraft);

        string sources = info.SourceMarkerIds.Count == 0
            ? "no source marker"
            : string.Join(", ", info.SourceMarkerIds);
        if (_massingViewportStatusText != null)
        {
            _massingViewportStatusText.Text =
                $"Selected: {info.Label} ({info.Kind}) | source: {sources}" +
                (string.IsNullOrWhiteSpace(info.Notes) ? "" : $" | {info.Notes}");
        }

        TxtStatus.Text = info.SourceMarkerIds.Count == 0
            ? $"Selected 3D {info.Kind}: {info.Label}."
            : $"Selected 3D {info.Kind}: {info.Label}; source marker selected in 3D Massing table.";
    }

    private void SelectFirstMassingMarker(IReadOnlyList<string> sourceMarkerIds)
    {
        if (_massingMarkerList == null || sourceMarkerIds.Count == 0)
            return;

        foreach (string markerId in sourceMarkerIds)
            if (SelectMassingMarkerById(markerId))
                return;
    }

    private void MassingViewport3D_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_massingCamera3D == null)
            return;

        double factor = e.Delta > 0 ? 0.88 : 1.14;
        _massing3DDistance = Math.Clamp(_massing3DDistance * factor, 4, Math.Max(20, _massing3DSceneRadius * 16));
        UpdateMassing3DCamera();
    }

    private void DrawMassingPreview(SmartMassingDraft? draft)
    {
        if (_massingPreviewCanvas == null)
            return;

        _massingPreviewCanvas.Children.Clear();

        List<SmartMassingPoint> points = draft?.Footprints
            .SelectMany(footprint => footprint.Points)
            .Where(point => !double.IsNaN(point.X) && !double.IsNaN(point.Y))
            .ToList() ?? [];

        if (draft == null || points.Count == 0)
        {
            if (_massingPreviewStatusText != null)
            {
                _massingPreviewStatusText.Text = draft == null
                    ? "Build a draft to preview the footprint."
                    : "Draft has no footprint points yet.";
            }
            return;
        }

        double width = _massingPreviewCanvas.ActualWidth > 40 ? _massingPreviewCanvas.ActualWidth : 280;
        double height = _massingPreviewCanvas.ActualHeight > 40 ? _massingPreviewCanvas.ActualHeight : 160;
        double minX = points.Min(point => point.X);
        double maxX = points.Max(point => point.X);
        double minY = points.Min(point => point.Y);
        double maxY = points.Max(point => point.Y);
        if (Math.Abs(maxX - minX) < 0.001)
        {
            minX -= 1;
            maxX += 1;
        }
        if (Math.Abs(maxY - minY) < 0.001)
        {
            minY -= 1;
            maxY += 1;
        }

        const double margin = 20;
        double scale = Math.Min((width - margin * 2) / (maxX - minX), (height - margin * 2) / (maxY - minY));
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            scale = 1;

        Point Project(SmartMassingPoint point)
        {
            double x = margin + (point.X - minX) * scale;
            double y = height - margin - (point.Y - minY) * scale;
            return new Point(x, y);
        }

        Brush footprintFill = new SolidColorBrush(Color.FromArgb(42, 96, 165, 250));
        Brush footprintStroke = new SolidColorBrush(Color.FromRgb(96, 165, 250));
        Brush selectedStroke = new SolidColorBrush(Color.FromRgb(255, 183, 77));
        string selectedMarkerId = _selectedMassingMarkerId;
        if (string.IsNullOrWhiteSpace(selectedMarkerId))
            selectedMarkerId = SelectedMassingMarkerRow()?.MarkerId ?? "";

        List<SmartMassingFootprint> previewFootprints = draft.Footprints
            .Where(footprint => footprint.Points.Count > 0)
            .ToList();

        foreach (SmartMassingFootprint footprint in previewFootprints)
        {
            var polygon = new System.Windows.Shapes.Polygon
            {
                Fill = footprintFill,
                Stroke = footprintStroke,
                StrokeThickness = 1.5,
                Points = new PointCollection(footprint.Points.Select(Project)),
                ToolTip = $"{footprint.Id}: level {footprint.Level}, base {footprint.BaseElevation:F2} {footprint.BaseElevationUnits}, height {footprint.Height:F2} {footprint.HeightUnits}, {footprint.Points.Count} points",
            };
            _massingPreviewCanvas.Children.Add(polygon);
        }

        DrawMassingRoofGuides(draft.Roof.Guides, Project, selectedMarkerId, selectedStroke);

        foreach (SmartMassingFootprint footprint in previewFootprints)
        {
            for (int i = 0; i < footprint.Points.Count; i++)
            {
                SmartMassingPoint point = footprint.Points[i];
                bool selected = !string.IsNullOrWhiteSpace(point.SourceMarkerId) &&
                    string.Equals(point.SourceMarkerId, selectedMarkerId, StringComparison.OrdinalIgnoreCase);
                AddMassingPreviewPoint(Project(point), i + 1, point.SourceMarkerId, selected, selectedStroke);
            }
        }

        if (_massingPreviewStatusText != null)
        {
            string roof = string.IsNullOrWhiteSpace(draft.Roof.Pitch)
                ? draft.Roof.Type
                : $"{draft.Roof.Type} {draft.Roof.Pitch}";
            _massingPreviewStatusText.Text =
                $"{points.Count} footprint pts | {draft.Units} | roof: {roof} ({draft.Roof.Status}) | roof guides: {draft.Roof.Guides.Count} | questions: {draft.UnresolvedQuestions.Count}";
        }
    }

    private void DrawMassingRoofGuides(
        IReadOnlyList<SmartMassingRoofGuide> guides,
        Func<SmartMassingPoint, Point> project,
        string selectedMarkerId,
        Brush selectedStroke)
    {
        if (_massingPreviewCanvas == null || guides.Count == 0)
            return;

        foreach (SmartMassingRoofGuide guide in guides)
        {
            if (string.Equals(guide.Status, "rejected", StringComparison.OrdinalIgnoreCase))
                continue;

            List<Point> points = guide.Points
                .Where(point => !double.IsNaN(point.X) && !double.IsNaN(point.Y))
                .Select(project)
                .ToList();
            if (points.Count == 0)
                continue;

            bool selected = !string.IsNullOrWhiteSpace(selectedMarkerId) &&
                guide.SourceMarkerIds.Contains(selectedMarkerId, StringComparer.OrdinalIgnoreCase);
            Brush stroke = selected ? selectedStroke : new SolidColorBrush(Color.FromRgb(255, 183, 77));
            Brush fill = new SolidColorBrush(Color.FromArgb(32, 255, 183, 77));

            if (guide.Kind is "eave_outline" or "cap" && points.Count >= 3)
            {
                var polygon = new System.Windows.Shapes.Polygon
                {
                    Points = new PointCollection(points),
                    Fill = guide.Kind == "cap" ? fill : Brushes.Transparent,
                    Stroke = stroke,
                    StrokeThickness = selected ? 2.2 : 1.2,
                    StrokeDashArray = new DoubleCollection { 5, 4 },
                    ToolTip = RoofGuideTooltip(guide),
                };
                _massingPreviewCanvas.Children.Add(polygon);
                AddMassingGuideLabel(points[0], guide.Kind == "cap" ? "roof cap" : "eave", stroke, selected);
                continue;
            }

            if (guide.Kind == "slope_arrow" && points.Count >= 2)
            {
                AddMassingGuideLine(points[0], points[^1], stroke, selected, RoofGuideTooltip(guide), dashed: false);
                AddMassingArrowHead(points[^2], points[^1], stroke);
                AddMassingGuideLabel(Midpoint(points[0], points[^1]), "slope", stroke, selected);
                continue;
            }

            if (points.Count >= 2)
            {
                AddMassingGuideLine(points[0], points[^1], stroke, selected, RoofGuideTooltip(guide), dashed: true);
                string label = guide.Kind switch
                {
                    "hip_ridge" => "hip ridge",
                    "axis_candidate" => "roof axis",
                    "valley" => "valley",
                    "roof_edge" => "roof edge",
                    "high_edge" => "high edge",
                    "low_edge" => "low edge",
                    "overhang" => "overhang",
                    _ => "ridge",
                };
                AddMassingGuideLabel(Midpoint(points[0], points[^1]), label, stroke, selected);
            }
        }
    }

    private void AddMassingGuideLine(Point start, Point end, Brush stroke, bool selected, string tooltip, bool dashed)
    {
        if (_massingPreviewCanvas == null)
            return;

        var line = new System.Windows.Shapes.Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = stroke,
            StrokeThickness = selected ? 2.4 : 1.8,
            ToolTip = tooltip,
        };
        if (dashed)
            line.StrokeDashArray = new DoubleCollection { 6, 4 };
        _massingPreviewCanvas.Children.Add(line);
    }

    private void AddMassingArrowHead(Point start, Point end, Brush fill)
    {
        if (_massingPreviewCanvas == null)
            return;

        double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        const double size = 8;
        Point p1 = end;
        Point p2 = new(
            end.X - Math.Cos(angle - Math.PI / 6) * size,
            end.Y - Math.Sin(angle - Math.PI / 6) * size);
        Point p3 = new(
            end.X - Math.Cos(angle + Math.PI / 6) * size,
            end.Y - Math.Sin(angle + Math.PI / 6) * size);

        var head = new System.Windows.Shapes.Polygon
        {
            Points = new PointCollection { p1, p2, p3 },
            Fill = fill,
            Stroke = fill,
            StrokeThickness = 1,
        };
        _massingPreviewCanvas.Children.Add(head);
    }

    private void AddMassingGuideLabel(Point point, string text, Brush foreground, bool selected)
    {
        if (_massingPreviewCanvas == null)
            return;

        var label = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.Normal,
            Foreground = foreground,
            Background = new SolidColorBrush(Color.FromArgb(170, 20, 20, 20)),
            Padding = new Thickness(3, 1, 3, 1),
        };
        Canvas.SetLeft(label, point.X + 6);
        Canvas.SetTop(label, point.Y + 4);
        _massingPreviewCanvas.Children.Add(label);
    }

    private static Point Midpoint(Point first, Point second) =>
        new((first.X + second.X) / 2, (first.Y + second.Y) / 2);

    private static string RoofGuideTooltip(SmartMassingRoofGuide guide) =>
        $"{guide.Label}\nStatus: {guide.Status}\nKind: {guide.Kind}\nConfidence: {guide.Confidence:P0}\n{guide.Notes}".Trim();

    private void AddMassingPreviewPoint(Point point, int index, string sourceMarkerId, bool selected, Brush selectedStroke)
    {
        if (_massingPreviewCanvas == null)
            return;

        double radius = selected ? 5.5 : 3.8;
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = selected ? selectedStroke : new SolidColorBrush(Color.FromRgb(96, 165, 250)),
            Stroke = selected ? Brushes.White : new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
            StrokeThickness = selected ? 1.5 : 1,
            ToolTip = string.IsNullOrWhiteSpace(sourceMarkerId)
                ? $"Footprint point {index}"
                : $"Footprint point {index}: {sourceMarkerId}",
        };
        if (!string.IsNullOrWhiteSpace(sourceMarkerId))
        {
            dot.Cursor = Cursors.Hand;
            dot.MouseLeftButtonDown += (_, e) =>
            {
                SelectMassingMarkerById(sourceMarkerId);
                e.Handled = true;
            };
        }
        Canvas.SetLeft(dot, point.X - radius);
        Canvas.SetTop(dot, point.Y - radius);
        _massingPreviewCanvas.Children.Add(dot);

        var label = new TextBlock
        {
            Text = index.ToString(CultureInfo.InvariantCulture),
            FontSize = 10,
            FontWeight = FontWeights.Normal,
            Foreground = selected ? selectedStroke : PreviewForegroundBrush(),
        };
        if (!string.IsNullOrWhiteSpace(sourceMarkerId))
        {
            label.Cursor = Cursors.Hand;
            label.MouseLeftButtonDown += (_, e) =>
            {
                SelectMassingMarkerById(sourceMarkerId);
                e.Handled = true;
            };
        }
        Canvas.SetLeft(label, point.X + 6);
        Canvas.SetTop(label, point.Y - 10);
        _massingPreviewCanvas.Children.Add(label);
    }

    private Brush PreviewForegroundBrush() =>
        TryFindResource("ControlForegroundBrush") as Brush ?? Brushes.White;

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

    private static string BuildMassingDraftSummary(SmartMassingDraft draft, string path)
    {
        int footprintCount = draft.Footprints.Count;
        int footprintPoints = draft.Footprints.Sum(footprint => footprint.Points.Count);
        string roofSummary = string.IsNullOrWhiteSpace(draft.Roof.Pitch)
            ? $"{draft.Roof.Type} ({draft.Roof.Confidence:P0})"
            : $"{draft.Roof.Type}, pitch {draft.Roof.Pitch} ({draft.Roof.Confidence:P0})";

        var sb = new StringBuilder();
        sb.AppendLine("3D Massing Draft");
        sb.AppendLine($"Path: {path}");
        sb.AppendLine($"Status: {draft.Status}");
        sb.AppendLine($"Units: {draft.Units}");
        sb.AppendLine($"Generated UTC: {draft.GeneratedAtUtc}");
        if (!string.IsNullOrWhiteSpace(draft.ReviewedAtUtc))
            sb.AppendLine($"Reviewed UTC: {draft.ReviewedAtUtc}");
        if (!string.IsNullOrWhiteSpace(draft.ReviewNotes))
            sb.AppendLine($"Review notes: {draft.ReviewNotes}");
        sb.AppendLine();
        sb.AppendLine("Summary");
        sb.AppendLine($"- Footprints: {footprintCount}");
        sb.AppendLine($"- Footprint points: {footprintPoints}");
        sb.AppendLine($"- Openings: {draft.Openings.Count}");
        sb.AppendLine($"- Roof: {roofSummary}");
        sb.AppendLine($"- Roof planes: {draft.Roof.Planes.Count}");
        sb.AppendLine($"- Assumptions: {draft.Assumptions.Count}");
        sb.AppendLine($"- Unresolved questions: {draft.UnresolvedQuestions.Count}");
        sb.AppendLine();
        sb.AppendLine("Build System");
        if (string.Equals(draft.Status, "draft_from_takeoffs", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("- Source: measured takeoff Area/Line items under sqfts, walls, areas, floors, or slabs.");
            sb.AppendLine("- Levels: floor labels and level folders become separate footprints.");
            sb.AppendLine("- Walls: each footprint edge extrudes vertically by the resolved/default wall height.");
            sb.AppendLine("- Roof: top footprint creates eave outline and candidate roof-axis guides until roof markers/review refine it.");
        }
        else
        {
            sb.AppendLine("- Source: reviewed AI markers and their crop/JSON evidence.");
            sb.AppendLine("- Levels: exterior corner markers become footprint levels.");
            sb.AppendLine("- Walls: each footprint edge extrudes vertically by reviewed/default wall height.");
            sb.AppendLine("- Roof: roof markers create reviewable eave, ridge, hip, valley, edge, or slope guides.");
        }

        foreach (SmartMassingFootprint footprint in draft.Footprints)
        {
            sb.AppendLine();
            sb.AppendLine($"Footprint {footprint.Id}");
            sb.AppendLine($"- Level: {footprint.Level}");
            sb.AppendLine($"- Page: {footprint.Page}");
            sb.AppendLine($"- Base elevation: {footprint.BaseElevation:F2} {footprint.BaseElevationUnits}");
            sb.AppendLine($"- Height: {footprint.Height:F2} {footprint.HeightUnits}");
            sb.AppendLine($"- Confidence: {footprint.Confidence:P0}");
            sb.AppendLine($"- Points: {footprint.Points.Count}");
            foreach (SmartMassingPoint point in footprint.Points)
                sb.AppendLine($"  - {point.X:F3}, {point.Y:F3} ({point.SourceMarkerId})");
        }

        sb.AppendLine();
        sb.AppendLine("Roof");
        sb.AppendLine($"- Status: {draft.Roof.Status}");
        sb.AppendLine($"- Type: {draft.Roof.Type}");
        if (draft.Roof.Elevation > 0)
            sb.AppendLine($"- Elevation: {draft.Roof.Elevation:F2} {draft.Roof.ElevationUnits}");
        sb.AppendLine($"- Pitch: {(string.IsNullOrWhiteSpace(draft.Roof.Pitch) ? "unknown" : draft.Roof.Pitch)}");
        sb.AppendLine($"- Confidence: {draft.Roof.Confidence:P0}");
        if (!string.IsNullOrWhiteSpace(draft.Roof.ReviewedAtUtc))
            sb.AppendLine($"- Reviewed UTC: {draft.Roof.ReviewedAtUtc}");
        sb.AppendLine($"- Guides: {draft.Roof.Guides.Count}");
        sb.AppendLine($"- Planes: {draft.Roof.Planes.Count}");
        foreach (SmartMassingRoofGuide guide in draft.Roof.Guides)
        {
            sb.AppendLine($"  - {guide.Kind}: {guide.Label} ({guide.Status}, {guide.Points.Count} pts, {guide.Confidence:P0})");
            if (!string.IsNullOrWhiteSpace(guide.Notes))
                sb.AppendLine($"    {guide.Notes}");
        }
        foreach (SmartMassingPlane plane in draft.Roof.Planes)
        {
            sb.AppendLine($"  - plane {plane.Kind}: {plane.Label} ({plane.Status}, {plane.Points.Count} pts, {plane.Confidence:P0})");
            if (!string.IsNullOrWhiteSpace(plane.Notes))
                sb.AppendLine($"    {plane.Notes}");
        }
        if (!string.IsNullOrWhiteSpace(draft.Roof.Notes))
            sb.AppendLine($"- Notes: {draft.Roof.Notes}");
        if (!string.IsNullOrWhiteSpace(draft.Roof.ReviewNotes))
            sb.AppendLine($"- Review notes: {draft.Roof.ReviewNotes}");

        sb.AppendLine();
        sb.AppendLine("Openings");
        if (draft.Openings.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (SmartMassingOpening opening in draft.Openings)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "- {0}: {1} ({2}, wall {3}, center {4:0.###}/{5:0.###}/{6:0.###}, {7:0.###} x {8:0.###}, {9:P0})",
                    opening.Type,
                    opening.SourceMarkerId,
                    opening.Status,
                    opening.WallIndex,
                    opening.Center.X,
                    opening.Center.Y,
                    opening.Center.Z,
                    opening.Width,
                    opening.Height,
                    opening.Confidence));
                if (!string.IsNullOrWhiteSpace(opening.Notes))
                    sb.AppendLine($"  {opening.Notes}");
            }
        }

        AppendMassingList(sb, "Assumptions", draft.Assumptions);
        AppendMassingList(sb, "Unresolved Questions", draft.UnresolvedQuestions);
        AppendMassingList(sb, "Source Markers", draft.SourceMarkerIds);
        return sb.ToString();
    }

    private static void AppendMassingList(StringBuilder sb, string title, IReadOnlyList<string> items)
    {
        sb.AppendLine();
        sb.AppendLine(title);
        if (items.Count == 0)
        {
            sb.AppendLine("- none");
            return;
        }

        foreach (string item in items)
            sb.AppendLine($"- {item}");
    }
}
