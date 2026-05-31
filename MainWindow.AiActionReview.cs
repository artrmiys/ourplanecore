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
using OurPlaneCore.Controls;
using SkiaSharp;


namespace OurPlaneCore;

public partial class MainWindow
{
    // AI action review rows, preview, apply, and roof-marker drafting.

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
                string measurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
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
            string activeType = OurPlaneCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType);
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
            "point" => "Needs at least 1 count mark",
            "area" => $"Needs 3+ vertices; has {pointCount}",
            _ => $"Needs 2+ vertices; has {pointCount}",
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
                OurPlaneCoreJobStore.SaveTakeoffItem(target);
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
}
