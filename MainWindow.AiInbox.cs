using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SkiaSharp;

namespace SmartTakeoffs;

public partial class MainWindow
{
    // ── AI Inbox ─────────────────────────────────────────────────────────────

    private void BtnToggleInbox_Click(object sender, RoutedEventArgs e)
    {
        if (_inboxExpanded)
        {
            _inboxExpandedHeight = InboxRow.ActualHeight > 30 ? InboxRow.ActualHeight : _inboxExpandedHeight;
            InboxRow.Height        = new GridLength(30);
            InboxSplitterRow.Height = new GridLength(0);
            TxtInboxToggle.Text    = "+";
        }
        else
        {
            InboxRow.Height        = new GridLength(_inboxExpandedHeight);
            InboxSplitterRow.Height = new GridLength(4);
            TxtInboxToggle.Text    = "-";
        }
        _inboxExpanded = !_inboxExpanded;
    }

    private void BtnInboxMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement target)
            return;

        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
        };

        menu.Items.Add(MakeMenuItem("Run New Bookmarks", _currentJob != null, () => BtnRunNewBookmarks_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Retry Failed Bookmarks", _currentJob != null, () => BtnRetryFailedBookmarks_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Create Marker Set", _currentJob != null, () => BtnCreateMarkerSet_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Manage Marker Sets...", CanManageMarkerSets(), () => BtnManageMarkerSets_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Export Marker Context", _currentJob != null, () => BtnExportMarkers_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Build 3D Draft", _currentJob != null, () => BtnBuildMassingDraft_Click(sender, new RoutedEventArgs())));

        menu.IsOpen = true;
    }

    private void LoadObservationsInbox()
    {
        ObservationsListView.Items.Clear();

        if (_currentJob == null)
        {
            TxtInboxCount.Text    = "0";
            InboxBadge.Visibility = Visibility.Collapsed;
            return;
        }

        string obsPath = Path.Combine(_currentJob.AIContextRoot, "observations.jsonl");
        if (!File.Exists(obsPath))
        {
            TxtInboxCount.Text    = "0";
            InboxBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var list = new List<SmartObservation>();
        foreach (string line in File.ReadLines(obsPath, System.Text.Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var obs = JsonSerializer.Deserialize<SmartObservation>(line);
                if (obs != null) list.Add(obs);
            }
            catch (JsonException) { /* skip malformed lines */ }
        }

        IReadOnlyList<SmartMarkerFeedbackRecord> markerFeedback = SmartLearningStore.LoadProjectMarkerFeedback(_currentJob);
        var displayItems = new List<ObservationDisplayItem>();
        foreach (var obs in list.OrderByDescending(o => o.CreatedAtUtc))
        {
            SmartAiMarker? marker = null;
            string markerQuality = "";
            if (string.Equals(obs.Type, "ai_marker", StringComparison.OrdinalIgnoreCase))
            {
                marker = SmartContextStore.LoadAiMarker(_currentJob, obs.Id);
                if (marker == null)
                    continue;
                markerQuality = MarkerQualityPreview(marker, markerFeedback);
            }

            if (!ShouldShowInboxObservation(marker))
                continue;

            displayItems.Add(new ObservationDisplayItem(obs, ObservationStatusPrefix(obs), marker, markerQuality));
        }

        foreach (ObservationDisplayItem item in displayItems)
            ObservationsListView.Items.Add(item);

        int count = displayItems.Count;
        TxtInboxCount.Text    = count.ToString();
        InboxBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool ShouldShowInboxObservation(SmartAiMarker? marker)
    {
        string typeFilter = SelectedMarkerTypeFilter();
        string sampleFilter = SelectedMarkerSampleFilter();

        if (marker == null)
        {
            return string.Equals(typeFilter, MarkerTypeFilterAllInbox, StringComparison.Ordinal) &&
                   string.Equals(sampleFilter, MarkerSampleFilterAny, StringComparison.Ordinal);
        }

        return MarkerMatchesCurrentFilters(marker);
    }

    private static string MarkerQualityPreview(
        SmartAiMarker marker,
        IReadOnlyList<SmartMarkerFeedbackRecord> feedback)
    {
        if (feedback.Count == 0)
            return "";

        var relevant = feedback
            .Where(record =>
                string.Equals(record.SourceMarkerId, marker.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(record.SourceMarkerType, marker.Type, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (relevant.Count == 0)
            return "";

        int accepted = relevant.Count(record => string.Equals(record.Outcome, "accepted", StringComparison.OrdinalIgnoreCase));
        int rejected = relevant.Count(record => string.Equals(record.Outcome, "rejected", StringComparison.OrdinalIgnoreCase));
        int applied = relevant.Count(record => record.Applied);
        var confidence = relevant
            .Where(record => record.Confidence > 0)
            .Select(record => record.Confidence)
            .ToList();
        string average = confidence.Count == 0
            ? ""
            : $" avg {confidence.Average():P0}";
        return $"fb A{accepted}/R{rejected}/appl{applied}{average}";
    }

    private string SelectedMarkerTypeFilter() =>
        ComboMarkerTypeFilter.SelectedItem?.ToString() ?? MarkerTypeFilterAllInbox;

    private string SelectedMarkerSampleFilter() =>
        ComboMarkerSampleFilter.SelectedItem?.ToString() ?? MarkerSampleFilterAny;

    private string ObservationStatusPrefix(SmartObservation observation)
    {
        if (_currentJob == null)
            return "";

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, observation.Id);
        if (request == null)
            return "";

        return $"[{request.Status}] ";
    }

    private ContextMenu BuildObservationsContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();
            ObservationDisplayItem? selected = SelectedObservationDisplayItem();
            if (selected == null)
            {
                menu.Items.Add(new MenuItem { Header = "No AI Inbox entry selected", IsEnabled = false });
                menu.Items.Add(new Separator());
                menu.Items.Add(MakeMenuItem("Refresh Inbox", true, LoadObservationsInbox));
                return;
            }

            menu.Items.Add(MakeMenuItem("Open Details", true, () => ShowObservationDetailsDialog(selected.Observation)));
            menu.Items.Add(MakeMenuItem("Go to Page", CanGoToObservationPage(selected), () => GoToObservationPage(selected)));
            menu.Items.Add(MakeMenuItem("Run AI Request", CanRunAiRequest(selected), async () => await RunAiRequestAsync(selected)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeSubmenu(
                "Crop",
                MakeMenuItem("Open Crop", CanOpenObservationCrop(selected), () => OpenObservationCrop(selected)),
                MakeMenuItem("Open Crop Folder", CanOpenObservationCrop(selected), () => OpenObservationCropFolder(selected)),
                MakeMenuItem("Bookmark Crop For Batch AI", CanBookmarkObservationCrop(selected), () => BookmarkObservationCrop(selected)),
                MakeMenuItem("Open Bookmark JSON", CanOpenCropBookmarkFile(selected), () => OpenCropBookmarkFile(selected))));
            menu.Items.Add(MakeSubmenu(
                "Marker",
                MakeMenuItem("Edit Marker", CanEditAiMarker(selected), () => EditAiMarker(selected)),
                MakeMenuItem("Delete Marker", CanEditAiMarker(selected), () => DeleteAiMarker(selected)),
                MakeMenuItem("Find Similar From Marker", CanFindSimilarFromMarker(selected), () => QueueFindSimilarFromMarker(selected)),
                MakeMenuItem("Create Marker Set From Filter", _currentJob != null, CreateMarkerSetFromCurrentFilter),
                MakeMenuItem("Manage Marker Sets...", CanManageMarkerSets(), ManageMarkerSets),
                MakeMenuItem("Export Marker Context", _currentJob != null, () => ExportMarkersContext(openAfterExport: true)),
                MakeMenuItem("Hide This Marker Type", CanHideAiMarkerType(selected), () => HideAiMarkerType(selected)),
                MakeMenuItem("Show All Marker Types", _hiddenAiMarkerTypes.Count > 0, ShowAllMarkerTypes)));
            menu.Items.Add(MakeSubmenu(
                "AI Response",
                MakeMenuItem("Add Manual AI Response", CanAddManualAiResponse(selected), () => AddManualAiResponse(selected)),
                MakeMenuItem("Preview Action Draft", CanPreviewAiActionDraft(selected), () => PreviewAiActionDraft(selected)),
                MakeMenuItem("Review Action Draft", CanReviewAiActionDraft(selected), () => ReviewAiActionDraft(selected)),
                MakeMenuItem("Apply Sheet Metadata Response", CanApplySheetMetadataResponse(selected), () => ApplySheetMetadataResponse(selected)),
                MakeMenuItem("Clear Action Preview", _currentJob != null, ClearAiActionDraftPreview)));
            menu.Items.Add(MakeSubmenu(
                "Files",
                MakeMenuItem("Open Request JSON", CanOpenAiRequestFile(selected), () => OpenAiRequestFile(selected)),
                MakeMenuItem("Open Layer JSON", CanOpenLayerManifest(selected), () => OpenLayerManifest(selected)),
                MakeMenuItem("Open Response JSON", CanOpenAiResponseFile(selected), () => OpenAiResponseFile(selected)),
                MakeMenuItem("Open Action Draft JSON", CanOpenAiActionDraftFile(selected), () => OpenAiActionDraftFile(selected)),
                MakeMenuItem("Open Marker JSON", CanOpenAiMarkerFile(selected), () => OpenAiMarkerFile(selected))));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Open Project Context", _currentJob != null, OpenProjectContextMarkdown));
            menu.Items.Add(MakeMenuItem("Refresh Inbox", true, LoadObservationsInbox));
        };
        return menu;
    }

    private void ObservationsListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedInboxObservation();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            LoadObservationsInbox();
            e.Handled = true;
        }
    }

    private ObservationDisplayItem? SelectedObservationDisplayItem() =>
        ObservationsListView.SelectedItem as ObservationDisplayItem;

    private void OpenSelectedInboxObservation()
    {
        if (SelectedObservationDisplayItem() is { } selected)
            ShowObservationDetailsDialog(selected.Observation);
    }

    private async void BtnRunAi_Click(object sender, RoutedEventArgs e)
    {
        await RunSelectedOrNextAiRequestAsync();
    }

    private async void BtnRunNewBookmarks_Click(object sender, RoutedEventArgs e)
    {
        await RunNewCropBookmarksAsync();
    }

    private async void BtnRetryFailedBookmarks_Click(object sender, RoutedEventArgs e)
    {
        await RetryFailedCropBookmarksAsync();
    }

    private bool CanBookmarkObservationCrop(ObservationDisplayItem item)
    {
        if (_currentJob == null || !CanOpenObservationCrop(item))
            return false;

        return SmartContextStore.FindCropBookmarkByObservation(_currentJob, item.Observation.Id) == null;
    }

    private void BookmarkObservationCrop(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id);
        string cropPath = !string.IsNullOrWhiteSpace(marker?.CropPath)
            ? marker.CropPath
            : item.CropRelativePath;
        if (string.IsNullOrWhiteSpace(cropPath))
        {
            TxtStatus.Text = "This Inbox entry has no crop to bookmark.";
            return;
        }

        if (SmartContextStore.FindCropBookmarkByObservation(_currentJob, item.Observation.Id) is { } existing)
        {
            TxtStatus.Text = $"Crop is already bookmarked as {existing.Id}.";
            return;
        }

        string pageFolder = marker?.PageFolder ?? ResolveObservationPageFolder(item.Observation);
        var bookmark = new SmartAiCropBookmark
        {
            SourceObservationId = item.Observation.Id,
            SourceMarkerId = marker?.Id ?? "",
            Page = marker?.Page ?? item.Page,
            PageFolder = pageFolder,
            Type = marker != null ? $"marker:{marker.Type}" : item.Observation.Type,
            CropPath = cropPath,
            Prompt = BuildCropBookmarkPrompt(item.Observation, marker),
            Status = "new",
        };

        SmartContextStore.SaveCropBookmark(_currentJob, bookmark);
        TxtStatus.Text = $"Bookmarked crop {bookmark.Id}; Run New will send it to OpenAI.";
    }

    private async Task RunNewCropBookmarksAsync()
    {
        await RunCropBookmarksAsync(
            "new",
            "No new crop bookmarks to send.",
            "new crop bookmarks");
    }

    private async Task RetryFailedCropBookmarksAsync()
    {
        await RunCropBookmarksAsync(
            "failed",
            "No failed crop bookmarks to retry.",
            "failed crop bookmarks");
    }

    private async Task RunCropBookmarksAsync(string statusFilter, string emptyMessage, string operationLabel)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before running crop bookmarks.";
            return;
        }

        if (_isRunningAiRequest)
        {
            TxtStatus.Text = "AI request is already running.";
            return;
        }

        string apiKey = ReadOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            TxtStatus.Text = "Set OPENAI_API_KEY in Windows environment, then run crop bookmarks.";
            return;
        }

        var bookmarks = SmartContextStore.LoadCropBookmarks(_currentJob)
            .Where(bookmark => string.Equals(bookmark.Status, statusFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(bookmark => bookmark.CreatedAtUtc)
            .ToList();
        if (bookmarks.Count == 0)
        {
            TxtStatus.Text = emptyMessage;
            return;
        }

        int done = 0;
        int failed = 0;
        int generated = 0;
        int skippedCandidates = 0;
        foreach (SmartAiCropBookmark bookmark in bookmarks)
        {
            if (!CropBookmarkFileExists(bookmark))
            {
                bookmark.Status = "failed";
                bookmark.ResultSummary = "Crop file is missing.";
                bookmark.ProcessedAtUtc = DateTime.UtcNow.ToString("O");
                SmartContextStore.SaveCropBookmark(_currentJob, bookmark);
                failed++;
                continue;
            }

            SmartAiRequest request = EnsureCropBookmarkRequest(bookmark);
            bookmark.RequestId = request.Id;
            bookmark.Status = "running";
            SmartContextStore.SaveCropBookmark(_currentJob, bookmark);

            await RunAiRequestAsync(request);

            SmartAiResponse? response = SmartContextStore.LoadAiResponse(_currentJob, request.Id);
            if (response != null && string.Equals(response.Status, "done", StringComparison.OrdinalIgnoreCase))
            {
                bookmark.Status = "done";
                bookmark.ResponseId = response.Id;
                bookmark.ResultSummary = FirstLineOrFallback(response.OutputText, "OpenAI response saved.");
                done++;
            }
            else
            {
                bookmark.Status = "failed";
                bookmark.ResponseId = response?.Id ?? "";
                bookmark.ResultSummary = FirstLineOrFallback(response?.Error ?? "", "OpenAI request failed.");
                failed++;
            }

            if (SmartContextStore.LoadAiActionDraft(_currentJob, request.Id) is { } draft)
            {
                bookmark.ActionDraftId = draft.Id;
                int skipped = 0;
                if (string.Equals(bookmark.Status, "done", StringComparison.OrdinalIgnoreCase))
                    generated += CreateCropBookmarksFromAiCandidates(bookmark, draft, out skipped);
                skippedCandidates += skipped;
            }
            bookmark.ProcessedAtUtc = DateTime.UtcNow.ToString("O");
            SmartContextStore.SaveCropBookmark(_currentJob, bookmark);
        }

        LoadObservationsInbox();
        string generatedSummary = generated > 0 || skippedCandidates > 0
            ? $", {generated} auto-new, {skippedCandidates} candidates skipped"
            : "";
        TxtStatus.Text = $"Processed {operationLabel}: {done} done, {failed} failed, {bookmarks.Count} total{generatedSummary}.";
    }

    private int CreateCropBookmarksFromAiCandidates(
        SmartAiCropBookmark sourceBookmark,
        SmartAiActionDraft draft,
        out int skipped)
    {
        skipped = 0;
        if (_currentJob == null || draft.Actions.Count == 0)
            return 0;

        if (sourceBookmark.CandidateDepth >= MaxAutoCropBookmarkDepth)
        {
            skipped += draft.Actions.Count(action => action.Points.Count > 0);
            return 0;
        }

        int created = 0;
        var existingBookmarks = SmartContextStore.LoadCropBookmarks(_currentJob).ToList();
        for (int i = 0; i < draft.Actions.Count; i++)
        {
            SmartAiAction action = draft.Actions[i];
            List<SKPoint> points = ActionPoints(action);
            if (points.Count == 0 ||
                !TryActionPointCenter(points, out SKPoint center) ||
                !TryResolveActionCandidatePage(action, draft, sourceBookmark, out PageInfo? page) ||
                page == null)
            {
                skipped++;
                continue;
            }

            string candidateKey = CropBookmarkCandidateKey(page, center);
            if (HasNearbyCropBookmarkDuplicate(existingBookmarks, page, center, candidateKey))
            {
                skipped++;
                continue;
            }

            if (!TrySaveActionCandidateCrop(page, points, action, i, out string cropPath, out SKRect cropRect, out string error))
            {
                skipped++;
                continue;
            }

            var bookmark = new SmartAiCropBookmark
            {
                SourceMarkerId = sourceBookmark.SourceMarkerId,
                SourceActionDraftId = draft.Id,
                SourceActionIndex = i,
                Page = page.Name,
                PageFolder = Path.GetRelativePath(_currentJob.RootPath, page.FolderPath),
                Type = AutoCropBookmarkType(action),
                CropPath = cropPath,
                Prompt = BuildAutoCropBookmarkPrompt(sourceBookmark, draft, action, i, cropRect),
                Status = "new",
                AutoCreated = true,
                CandidateDepth = sourceBookmark.CandidateDepth + 1,
                CandidateKey = candidateKey,
                CandidateCenter = new SmartAiActionPoint { X = center.X, Y = center.Y },
                CandidatePoints = points.Select(point => new SmartAiActionPoint { X = point.X, Y = point.Y }).ToList(),
                ResultSummary = "Auto-created from AI action candidate.",
            };

            SmartContextStore.SaveCropBookmark(_currentJob, bookmark);
            existingBookmarks.Add(bookmark);
            created++;
        }

        return created;
    }

    private bool TryResolveActionCandidatePage(
        SmartAiAction action,
        SmartAiActionDraft draft,
        SmartAiCropBookmark sourceBookmark,
        out PageInfo? page)
    {
        page = FindPageByName(action.Page) ??
               FindPageByName(draft.Page) ??
               ResolveBookmarkPage(sourceBookmark);
        return page != null;
    }

    private string CropBookmarkCandidateKey(PageInfo page, SKPoint center)
    {
        string pageKey = _currentJob == null
            ? page.Name
            : Path.GetRelativePath(_currentJob.RootPath, page.FolderPath);
        int bucketX = (int)Math.Round(center.X / AutoCropBookmarkDuplicateTolerancePt);
        int bucketY = (int)Math.Round(center.Y / AutoCropBookmarkDuplicateTolerancePt);
        return $"{pageKey}|{bucketX}|{bucketY}";
    }

    private bool HasNearbyCropBookmarkDuplicate(
        IReadOnlyList<SmartAiCropBookmark> bookmarks,
        PageInfo page,
        SKPoint center,
        string candidateKey)
    {
        foreach (SmartAiCropBookmark bookmark in bookmarks)
        {
            if (!CropBookmarkBelongsToPage(bookmark, page))
                continue;

            if (!string.IsNullOrWhiteSpace(candidateKey) &&
                string.Equals(bookmark.CandidateKey, candidateKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!TryGetCropBookmarkCenter(bookmark, out SKPoint existingCenter))
                continue;

            float dx = existingCenter.X - center.X;
            float dy = existingCenter.Y - center.Y;
            if ((dx * dx) + (dy * dy) <= AutoCropBookmarkDuplicateTolerancePt * AutoCropBookmarkDuplicateTolerancePt)
                return true;
        }

        return false;
    }

    private bool CropBookmarkBelongsToPage(SmartAiCropBookmark bookmark, PageInfo page)
    {
        if (_currentJob != null && !string.IsNullOrWhiteSpace(bookmark.PageFolder))
        {
            string bookmarkFolder = Path.IsPathFullyQualified(bookmark.PageFolder)
                ? bookmark.PageFolder
                : Path.Combine(_currentJob.RootPath, bookmark.PageFolder);
            if (string.Equals(NormalizePath(bookmarkFolder), NormalizePath(page.FolderPath), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return !string.IsNullOrWhiteSpace(bookmark.Page) &&
               string.Equals(bookmark.Page, page.Name, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetCropBookmarkCenter(SmartAiCropBookmark bookmark, out SKPoint center)
    {
        center = default;
        if (TryActionPointCenter(bookmark.CandidatePoints.Select(point => new SKPoint(point.X, point.Y)).ToList(), out center))
            return true;

        if (_currentJob != null &&
            !string.IsNullOrWhiteSpace(bookmark.SourceMarkerId) &&
            SmartContextStore.LoadAiMarker(_currentJob, bookmark.SourceMarkerId) is { } marker)
        {
            center = new SKPoint(marker.PdfPoint.X, marker.PdfPoint.Y);
            return true;
        }

        return false;
    }

    private bool TrySaveActionCandidateCrop(
        PageInfo page,
        IReadOnlyList<SKPoint> points,
        SmartAiAction action,
        int actionIndex,
        out string relativePath,
        out SKRect cropPdfRect,
        out string error)
    {
        relativePath = "";
        cropPdfRect = SKRect.Empty;
        error = "";

        SKRect requested = CandidateCropRect(points);
        return TrySavePageCrop(
            page,
            requested,
            "candidate",
            $"{action.Label}_{actionIndex + 1}",
            out relativePath,
            out cropPdfRect,
            out error);
    }

    private bool TrySavePageCrop(
        PageInfo page,
        SKRect requested,
        string type,
        string tag,
        out string relativePath,
        out SKRect cropPdfRect,
        out string error)
    {
        relativePath = "";
        cropPdfRect = SKRect.Empty;
        error = "";

        if (_currentJob == null)
        {
            error = "No current job.";
            return false;
        }

        if (!PdfLayerRenderService.TryRender(
                page.PdfPath,
                page.PdfPage,
                1.50,
                new Dictionary<int, bool>(),
                [],
                page.PdfLayersCached ? page.PdfLayers : null,
                out PdfLayerRenderResult render,
                out error))
        {
            return false;
        }

        using SKBitmap? bitmap = SKBitmap.Decode(render.ImageBytes);
        if (bitmap == null || render.WidthPt <= 0 || render.HeightPt <= 0)
        {
            error = "Rendered PDF image could not be decoded.";
            return false;
        }

        float left = Math.Clamp(requested.Left, 0, render.WidthPt);
        float top = Math.Clamp(requested.Top, 0, render.HeightPt);
        float right = Math.Clamp(requested.Right, 0, render.WidthPt);
        float bottom = Math.Clamp(requested.Bottom, 0, render.HeightPt);
        if (right - left < 1 || bottom - top < 1)
        {
            error = "Requested crop is outside the PDF page.";
            return false;
        }

        float scaleX = bitmap.Width / render.WidthPt;
        float scaleY = bitmap.Height / render.HeightPt;
        int srcLeft = Math.Clamp((int)Math.Floor(left * scaleX), 0, bitmap.Width - 1);
        int srcTop = Math.Clamp((int)Math.Floor(top * scaleY), 0, bitmap.Height - 1);
        int srcRight = Math.Clamp((int)Math.Ceiling(right * scaleX), srcLeft + 1, bitmap.Width);
        int srcBottom = Math.Clamp((int)Math.Ceiling(bottom * scaleY), srcTop + 1, bitmap.Height);

        using var crop = new SKBitmap(srcRight - srcLeft, srcBottom - srcTop);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(
                bitmap,
                new SKRectI(srcLeft, srcTop, srcRight, srcBottom),
                new SKRect(0, 0, crop.Width, crop.Height));
        }

        string fileName =
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{SafeFileNamePart(type)}_{SafeFileNamePart(page.Name)}_{SafeFileNamePart(tag)}.png";
        string cropPath = Path.Combine(_currentJob.AIContextRoot, "crops", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(cropPath) ?? _currentJob.AIContextRoot);
        using SKData data = crop.Encode(SKEncodedImageFormat.Png, 95);
        using FileStream stream = File.Create(cropPath);
        data.SaveTo(stream);

        cropPdfRect = new SKRect(
            srcLeft / scaleX,
            srcTop / scaleY,
            srcRight / scaleX,
            srcBottom / scaleY);
        relativePath = Path.GetRelativePath(_currentJob.AIContextRoot, cropPath);
        return true;
    }

    private static SKRect CandidateCropRect(IReadOnlyList<SKPoint> points)
    {
        SKRect bounds = PointsBounds(points);
        float centerX = (bounds.Left + bounds.Right) / 2f;
        float centerY = (bounds.Top + bounds.Bottom) / 2f;
        float width = Math.Max(bounds.Width + AutoCropBookmarkPaddingPt * 2, AutoCropBookmarkMinSizePt);
        float height = Math.Max(bounds.Height + AutoCropBookmarkPaddingPt * 2, AutoCropBookmarkMinSizePt);
        return SKRect.Create(centerX - width / 2f, centerY - height / 2f, width, height);
    }

    private static SKRect PointsBounds(IReadOnlyList<SKPoint> points)
    {
        float left = points.Min(point => point.X);
        float top = points.Min(point => point.Y);
        float right = points.Max(point => point.X);
        float bottom = points.Max(point => point.Y);
        return new SKRect(left, top, right, bottom);
    }

    private static bool TryActionPointCenter(IReadOnlyList<SKPoint> points, out SKPoint center)
    {
        center = default;
        var validPoints = points
            .Where(point => !float.IsNaN(point.X) && !float.IsNaN(point.Y))
            .ToList();
        if (validPoints.Count == 0)
            return false;

        SKRect bounds = PointsBounds(validPoints);
        center = new SKPoint((bounds.Left + bounds.Right) / 2f, (bounds.Top + bounds.Bottom) / 2f);
        return true;
    }

    private static string AutoCropBookmarkType(SmartAiAction action)
    {
        string type = string.IsNullOrWhiteSpace(action.Type)
            ? action.MeasurementType
            : action.Type;
        return string.IsNullOrWhiteSpace(type) ? "ai_candidate" : $"candidate:{type.Trim()}";
    }

    private static string BuildAutoCropBookmarkPrompt(
        SmartAiCropBookmark sourceBookmark,
        SmartAiActionDraft draft,
        SmartAiAction action,
        int actionIndex,
        SKRect cropRect)
    {
        var sb = new StringBuilder();
        sb.AppendLine("This crop was auto-created from an AI-discovered action candidate.");
        sb.AppendLine("Review this single crop as a new bookmark. Do not rediscover the source crop or suggest the same candidate again.");
        sb.AppendLine("If this crop confirms a distinct takeoff-relevant candidate, return a JSON action draft with page and points.");
        sb.AppendLine("If it does not confirm a distinct candidate, return JSON with an empty actions array and a short summary.");
        sb.AppendLine();
        sb.AppendLine("Source:");
        sb.AppendLine($"- Source bookmark: {sourceBookmark.Id}");
        sb.AppendLine($"- Source action draft: {draft.Id}");
        sb.AppendLine($"- Source action index: {actionIndex}");
        sb.AppendLine($"- Page: {action.Page}");
        sb.AppendLine($"- Type: {action.Type}");
        sb.AppendLine($"- Label: {action.Label}");
        if (action.Confidence > 0)
            sb.AppendLine($"- Confidence: {action.Confidence:P0}");
        if (!string.IsNullOrWhiteSpace(action.Notes))
            sb.AppendLine($"- Notes: {action.Notes}");
        sb.AppendLine($"- Candidate crop: {FormatPdfRect(cropRect)}");
        sb.AppendLine("- Candidate points:");
        foreach (SmartAiActionPoint point in action.Points)
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - x={0:F1}, y={1:F1}", point.X, point.Y));
        return sb.ToString();
    }

    private SmartAiRequest EnsureCropBookmarkRequest(SmartAiCropBookmark bookmark)
    {
        if (_currentJob == null)
            throw new InvalidOperationException("Open a job before running crop bookmarks.");

        if (!string.IsNullOrWhiteSpace(bookmark.RequestId) &&
            SmartContextStore.LoadAiRequest(_currentJob, bookmark.RequestId) is { } existing)
        {
            return existing;
        }

        PageInfo? page = ResolveBookmarkPage(bookmark);
        string details =
            "Crop bookmark AI request.\n\n" +
            "Bookmark:\n" +
            $"- Id: {bookmark.Id}\n" +
            $"- Type: {bookmark.Type}\n" +
            $"- Source observation: {bookmark.SourceObservationId}\n" +
            $"- Source marker: {bookmark.SourceMarkerId}\n" +
            $"- AI crop: {bookmark.CropPath}\n\n" +
            bookmark.Prompt;

        SmartObservation observation = SmartContextStore.AddObservation(
            _currentJob,
            page,
            "crop_bookmark_request",
            details);

        return SmartContextStore.AddAiRequest(
            _currentJob,
            page,
            observation,
            "crop_bookmark_request",
            bookmark.Prompt,
            bookmark.CropPath,
            "");
    }

    private PageInfo? ResolveBookmarkPage(SmartAiCropBookmark bookmark)
    {
        if (_currentJob == null)
            return null;

        if (!string.IsNullOrWhiteSpace(bookmark.PageFolder))
        {
            string folder = Path.IsPathFullyQualified(bookmark.PageFolder)
                ? bookmark.PageFolder
                : Path.GetFullPath(Path.Combine(_currentJob.RootPath, bookmark.PageFolder));
            PageInfo? page = SmartTakeoffsJobStore.TryReadPage(folder);
            if (page != null)
                return page;
        }

        return FindPageByName(bookmark.Page);
    }

    private bool CropBookmarkFileExists(SmartAiCropBookmark bookmark)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(bookmark.CropPath))
            return false;

        string path = Path.IsPathFullyQualified(bookmark.CropPath)
            ? bookmark.CropPath
            : Path.GetFullPath(Path.Combine(_currentJob.AIContextRoot, bookmark.CropPath));
        return File.Exists(path);
    }

    private string ResolveObservationPageFolder(SmartObservation observation)
    {
        if (_currentJob == null)
            return "";

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, observation.Id);
        if (request != null && !string.IsNullOrWhiteSpace(request.PageFolder))
            return request.PageFolder;

        PageInfo? page = FindPageByName(observation.Page);
        return page == null ? "" : Path.GetRelativePath(_currentJob.RootPath, page.FolderPath);
    }

    private string BuildCropBookmarkPrompt(SmartObservation observation, SmartAiMarker? marker)
    {
        var sb = new StringBuilder();
        sb.AppendLine("This crop was bookmarked during manual plan review.");
        sb.AppendLine("Analyze it once in the batch pass and record whether it contains takeoff-relevant evidence.");
        sb.AppendLine("If this looks like a reusable detection example, describe what should be searched next.");
        if (marker != null)
        {
            sb.AppendLine();
            sb.AppendLine("Source marker:");
            sb.AppendLine($"- Type: {marker.Type}");
            sb.AppendLine($"- Sample kind: {marker.SampleKind}");
            if (!string.IsNullOrWhiteSpace(marker.Value))
                sb.AppendLine($"- Value: {marker.Value}");
            if (!string.IsNullOrWhiteSpace(marker.Note))
                sb.AppendLine($"- Note: {marker.Note}");
        }

        sb.AppendLine();
        sb.AppendLine("Source observation:");
        sb.AppendLine(observation.Text);
        return sb.ToString();
    }

    private static string FirstLineOrFallback(string text, string fallback)
    {
        string first = (text ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(first) ? fallback : first;
    }

    private void BtnCreateMarkerSet_Click(object sender, RoutedEventArgs e)
    {
        CreateMarkerSetFromCurrentFilter();
    }

    private void BtnManageMarkerSets_Click(object sender, RoutedEventArgs e)
    {
        ManageMarkerSets();
    }

    private void BtnExportMarkers_Click(object sender, RoutedEventArgs e)
    {
        ExportMarkersContext(openAfterExport: true);
    }
}
