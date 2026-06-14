using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

// Offline "count similar symbols": box one symbol -> SimilarSymbolMatcher
// finds every look-alike on the page raster -> the matches become ordinary
// count measurements. Optional checkbox queues an online AI double-check
// through the regular AI Inbox pipeline.
public partial class MainWindow
{
    private const float SimilarCountDuplicateTolerancePdf = 4f;
    private const int SimilarCountReviewKeyQuantumPx = 4;

    private SimilarCountDialog? _similarCountDialog;

    private void BtnSimilarCount_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null || _currentPage == null)
        {
            TxtStatus.Text = "Count similar: open a job and a sheet first.";
            return;
        }

        if (_similarCountDialog != null)
        {
            _similarCountDialog.Activate();
            TxtStatus.Text = "Count similar review is already open.";
            return;
        }

        _viewport.BeginSimilarCountSelection();
    }

    private void OnSimilarCountSelectionCompleted(ViewportSimilarCountRequest request)
    {
        if (_currentJob == null || _currentPage == null)
            return;

        if (!_viewport.TryCreateSimilarCountSession(
                request.PdfRect, out SimilarSymbolMatchSession? session, out float bitmapScale, out string error) ||
            session == null)
        {
            TxtStatus.Text = $"Count similar: {error}";
            MessageBox.Show($"Count similar symbols:\n{error}", "Count Similar",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var lastMatches = new List<SimilarSymbolMatch>();
        var excludedIndexes = new HashSet<int>();
        var alreadyCountedIndexes = new HashSet<int>();
        var manualReviewStatesByCenter = new Dictionary<string, bool>(StringComparer.Ordinal);
        TakeoffItem? destinationItem = CurrentSimilarCountDestinationItem();
        string destinationName = SimilarCountDestinationName(destinationItem);
        OurPlaneCoreJob reviewJob = _currentJob;
        PageInfo reviewPage = _currentPage;
        SimilarCountDialog? dialog = null;

        bool IsReviewJobCurrent() =>
            _currentJob != null &&
            string.Equals(
                NormalizePathForCompare(_currentJob.RootPath),
                NormalizePathForCompare(reviewJob.RootPath),
                StringComparison.OrdinalIgnoreCase);

        SKPoint MatchCenterPdf(SimilarSymbolMatch match) =>
            new(match.CenterX / bitmapScale, match.CenterY / bitmapScale);

        IReadOnlyList<ViewportSimilarCountPreviewMarker> BuildPreviewMarkers() =>
            lastMatches
                .Select((match, index) => new ViewportSimilarCountPreviewMarker(
                    MatchCenterPdf(match),
                    Included: !excludedIndexes.Contains(index) && !alreadyCountedIndexes.Contains(index),
                    match.Score,
                    match.RotationDegrees,
                    match.Mirrored,
                    AlreadyCounted: alreadyCountedIndexes.Contains(index),
                    TemplateCoverage: match.TemplateCoverage,
                    WindowPrecision: match.WindowPrecision,
                    InkRatio: match.InkRatio,
                    ProfileScore: match.ProfileScore,
                    ProjectionScore: match.ProjectionScore,
                    UsedFocusedScore: match.UsedFocusedScore))
                .ToList();

        IReadOnlyList<SKPoint> IncludedCenters() =>
            lastMatches
                .Where((_, index) => !excludedIndexes.Contains(index) && !alreadyCountedIndexes.Contains(index))
                .Select(MatchCenterPdf)
                .ToList();

        SimilarCountScanResult BuildReviewResult()
        {
            int total = lastMatches.Count;
            int included = lastMatches
                .Where((_, index) =>
                    !excludedIndexes.Contains(index) &&
                    !alreadyCountedIndexes.Contains(index))
                .Count();
            if (included == 0)
            {
                return new SimilarCountScanResult(
                    0,
                    total,
                    0f,
                    0f,
                    WeakSimilarMatchCount(),
                    alreadyCountedIndexes.Count,
                    SimilarCountLimitSummary());
            }

            var scores = lastMatches
                .Where((_, index) =>
                    !excludedIndexes.Contains(index) &&
                    !alreadyCountedIndexes.Contains(index))
                .Select(match => match.Score)
                .ToList();
            return new SimilarCountScanResult(
                included,
                total,
                scores.Min(),
                scores.Max(),
                WeakSimilarMatchCount(),
                alreadyCountedIndexes.Count,
                SimilarCountLimitSummary());
        }

        static bool IsWeakSimilarMatch(SimilarSymbolMatch match) =>
            match.Score > 0f && match.Score < (float)AppSettingsStore.SimilarCountThresholdDefault;

        string SimilarCountLimitSummary()
        {
            var limits = lastMatches
                .Select((match, index) => new { Match = match, Index = index })
                .Where(item => !alreadyCountedIndexes.Contains(item.Index) && IsWeakSimilarMatch(item.Match))
                .Select(item => SimilarCountLimitLabel(item.Match))
                .GroupBy(label => label, StringComparer.Ordinal)
                .Select(group => new { Label = group.Key, Count = group.Count() })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Label, StringComparer.Ordinal)
                .ToList();
            if (limits.Count == 0)
                return "";

            int weakTotal = limits.Sum(item => item.Count);
            var top = limits[0];
            return $"Weak limit: {top.Label} {top.Count}/{weakTotal}";
        }

        static string SimilarCountLimitLabel(SimilarSymbolMatch match)
        {
            float layoutScore = Math.Min(match.ProfileScore, match.ProjectionScore);
            (string Label, float Score) limit = ("coverage", match.TemplateCoverage);
            if (match.WindowPrecision < limit.Score)
                limit = ("precision", match.WindowPrecision);
            if (match.InkRatio < limit.Score)
                limit = ("ink", match.InkRatio);
            if (layoutScore < limit.Score)
                limit = ("layout", layoutScore);
            return limit.Label;
        }

        int WeakSimilarMatchCount() =>
            lastMatches
                .Where((match, index) => !alreadyCountedIndexes.Contains(index) && IsWeakSimilarMatch(match))
                .Count();

        void ExcludeWeakSimilarMatches()
        {
            for (int i = 0; i < lastMatches.Count; i++)
            {
                if (IsWeakSimilarMatch(lastMatches[i]))
                    excludedIndexes.Add(i);
            }
        }

        void ExcludeAlreadyCountedSimilarMatches()
        {
            alreadyCountedIndexes.Clear();
            for (int i = 0; i < lastMatches.Count; i++)
            {
                if (destinationItem != null &&
                    IsSimilarCountDuplicateCenter(destinationItem, request.PageFolder, MatchCenterPdf(lastMatches[i])))
                {
                    alreadyCountedIndexes.Add(i);
                    excludedIndexes.Add(i);
                }
            }
        }

        void ApplyManualSimilarReviewChoices()
        {
            for (int i = 0; i < lastMatches.Count; i++)
            {
                if (alreadyCountedIndexes.Contains(i))
                    continue;
                if (!manualReviewStatesByCenter.TryGetValue(SimilarReviewKey(lastMatches[i]), out bool include))
                    continue;

                if (include)
                    excludedIndexes.Remove(i);
                else
                    excludedIndexes.Add(i);
            }
        }

        void RememberCurrentSimilarReviewChoices(bool include)
        {
            for (int i = 0; i < lastMatches.Count; i++)
            {
                if (!alreadyCountedIndexes.Contains(i))
                    manualReviewStatesByCenter[SimilarReviewKey(lastMatches[i])] = include;
            }
        }

        void ClearManualSimilarReviewChoices() => manualReviewStatesByCenter.Clear();

        void ApplyDefaultSimilarReviewExclusions()
        {
            excludedIndexes.Clear();
            ExcludeWeakSimilarMatches();
            ExcludeAlreadyCountedSimilarMatches();
            ApplyManualSimilarReviewChoices();
        }

        void RefreshPreviewReview()
        {
            _viewport.SetSimilarCountPreviewMarkers(BuildPreviewMarkers(), request.PageFolder);
            SimilarCountScanResult result = BuildReviewResult();
            dialog?.SetReviewCounts(
                result.Included,
                result.Total,
                result.MinScore,
                result.MaxScore,
                result.WeakCount,
                result.AlreadyCountedCount,
                result.LimitSummary);
        }

        void ToggleSimilarPreviewMarker(int index)
        {
            if (index < 0 || index >= lastMatches.Count)
                return;
            if (alreadyCountedIndexes.Contains(index))
            {
                TxtStatus.Text = "Count similar review: this marker is already counted in the destination takeoff.";
                return;
            }

            bool include = excludedIndexes.Contains(index);
            manualReviewStatesByCenter[SimilarReviewKey(lastMatches[index])] = include;
            if (include)
                excludedIndexes.Remove(index);
            else
                excludedIndexes.Add(index);

            RefreshPreviewReview();
            SimilarCountScanResult result = BuildReviewResult();
            TxtStatus.Text = SimilarReviewStatus(result);
        }

        void IncludeAllPreviewMarkers(object? sender, EventArgs e)
        {
            RememberCurrentSimilarReviewChoices(include: true);
            ApplyDefaultSimilarReviewExclusions();
            RefreshPreviewReview();
            SimilarCountScanResult result = BuildReviewResult();
            TxtStatus.Text = SimilarReviewStatus(result);
        }

        void KeepOnlyStrongPreviewMarkers(object? sender, EventArgs e)
        {
            ClearManualSimilarReviewChoices();
            ApplyDefaultSimilarReviewExclusions();
            RefreshPreviewReview();
            SimilarCountScanResult result = BuildReviewResult();
            TxtStatus.Text = SimilarReviewStatus(result);
        }

        async Task<SimilarCountScanResult> ScanAsync(
            float threshold,
            bool rotations,
            bool mirrored,
            CancellationToken cancellationToken)
        {
            List<SimilarSymbolMatch> matches = await Task.Run(
                () => session.FindMatches(threshold, rotations, mirrored, cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lastMatches.Clear();
            lastMatches.AddRange(matches);
            ApplyDefaultSimilarReviewExclusions();
            _viewport.SetSimilarCountPreviewMarkers(BuildPreviewMarkers(), request.PageFolder);
            return BuildReviewResult();
        }

        dialog = new SimilarCountDialog(
            ScanAsync,
            (float)_settings.SimilarCountThreshold,
            _settings.SimilarCountRotations,
            _settings.SimilarCountMirrored,
            destinationName,
            aiAvailable: !string.IsNullOrWhiteSpace(ReadOpenAiApiKey()),
            templateWarning: session.TemplateWarning)
        {
            Owner = this,
        };

        void CleanupSimilarDialog()
        {
            _viewport.SimilarCountPreviewMarkerToggled -= ToggleSimilarPreviewMarker;
            if (dialog != null)
            {
                dialog.IncludeAllRequested -= IncludeAllPreviewMarkers;
                dialog.StrongOnlyRequested -= KeepOnlyStrongPreviewMarkers;
            }
            _viewport.SetSimilarCountPreviewMarkers(null);
            if (ReferenceEquals(_similarCountDialog, dialog))
                _similarCountDialog = null;
        }

        dialog.Accepted += (_, _) =>
        {
            _settings.SimilarCountThreshold = dialog.Threshold;
            _settings.SimilarCountRotations = dialog.IncludeRotations;
            _settings.SimilarCountMirrored = dialog.IncludeMirrored;
            SaveAppSettings();

            if (!IsReviewJobCurrent())
            {
                TxtStatus.Text = "Count similar: original job changed; review was not added.";
                return;
            }

            IReadOnlyList<SKPoint> included = IncludedCenters();
            if (included.Count == 0)
            {
                TxtStatus.Text = "Count similar: nothing to add.";
                return;
            }

            int added = AddSimilarCountMeasurements(request, included, destinationItem);
            if (added > 0 && dialog.QueueAiDoubleCheck)
                QueueSimilarCountAiRequest(reviewJob, reviewPage, request, added, destinationName);
        };
        dialog.Cancelled += (_, _) =>
        {
            TxtStatus.Text = "Count similar cancelled.";
        };
        dialog.Closed += (_, _) => CleanupSimilarDialog();

        _viewport.SimilarCountPreviewMarkerToggled += ToggleSimilarPreviewMarker;
        dialog.IncludeAllRequested += IncludeAllPreviewMarkers;
        dialog.StrongOnlyRequested += KeepOnlyStrongPreviewMarkers;
        _similarCountDialog = dialog;
        dialog.Show();
        dialog.Activate();
        TxtStatus.Text = $"Count similar review for {destinationName}: click preview markers on the sheet to exclude or include them.";
    }

    private static string SimilarReviewStatus(SimilarCountScanResult result)
    {
        string status = $"Count similar review: {result.Included}/{result.NewCandidateCount} new marker(s) ready";
        if (result.WeakCount > 0)
            status += $", {result.WeakCount} weak";
        if (result.AlreadyCountedCount > 0)
            status += $", {result.AlreadyCountedCount} already counted";
        if (!string.IsNullOrWhiteSpace(result.LimitSummary))
            status += $", {result.LimitSummary}";
        return status + ".";
    }

    private static string SimilarReviewKey(SimilarSymbolMatch match) =>
        SimilarReviewKeyPart(match.CenterX) + ":" + SimilarReviewKeyPart(match.CenterY);

    private static string SimilarReviewKeyPart(int coordinate)
    {
        int quantized = (int)MathF.Round(coordinate / (float)SimilarCountReviewKeyQuantumPx) *
            SimilarCountReviewKeyQuantumPx;
        return quantized.ToString(CultureInfo.InvariantCulture);
    }

    private int AddSimilarCountMeasurements(
        ViewportSimilarCountRequest request,
        IReadOnlyList<SKPoint> centers,
        TakeoffItem? destinationItem)
    {
        TakeoffItem? item = ResolveSimilarCountDestinationItem(destinationItem);
        if (item == null)
        {
            string parent = NewTakeoffItemParentFolderForUserCreate();
            TakeoffItem created = CreateUniqueTakeoffItem(
                "Similar Count",
                RandomTakeoffColor(_viewport.ActiveColor),
                "point",
                parent);
            ApplyNewCountSymbolToItemIfNeeded(created, "point");
            LoadTakeoffsForJob();
            item = _takeoffItems.FirstOrDefault(t =>
                string.Equals(t.FolderPath, created.FolderPath, StringComparison.OrdinalIgnoreCase)) ?? created;
            _activeItem = item;
            _viewport.ActiveColor = item.Color;
            _viewport.ActiveTakeoffFolder = item.FolderPath;
            _viewport.ActiveCountSymbol = item.CountSymbol;
            SelectTakeoffNodeByFolder(item.FolderPath);
        }

        var newCenters = centers
            .Where(center => !IsSimilarCountDuplicateCenter(item, request.PageFolder, center))
            .ToList();
        if (newCenters.Count == 0)
        {
            TxtStatus.Text = $"Count similar: all reviewed marker(s) are already counted in {item.Name}.";
            return 0;
        }

        var generated = new List<Measurement>(newCenters.Count);
        foreach (SKPoint center in newCenters)
        {
            generated.Add(new Measurement
            {
                MType = "point",
                Points = [center],
                Color = item.Color,
                CountSymbol = CountDisplaySymbol.Normalize(item.CountSymbol),
                PageFolder = request.PageFolder,
                TakeoffFolder = item.FolderPath,
                ScaleMetersPerPt = request.ScaleMetersPerPt,
            });
        }

        foreach (Measurement measurement in generated)
            item.Measurements.Add(measurement);
        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        bool scannedSheetIsOpen = IsSamePageFolder(_currentPage?.FolderPath, request.PageFolder);
        _viewport.AddGeneratedMeasurements(generated);
        if (scannedSheetIsOpen)
            _viewport.SelectMeasurements(generated);
        RefreshTreeItem(item);
        QueueTakeoffAutosave(item);
        using (UsePageMeasurementLookup())
        {
            RefreshTakeoffRowVisualsForItems(new[] { item });
            RefreshPageTakeoffIndicatorsForFolder(request.PageFolder);
            RefreshSheetLegend();
        }
        UpdateTotalDisplay();
        int skipped = centers.Count - generated.Count;
        TxtStatus.Text = SimilarCountAddedStatus(generated.Count, skipped, item.Name, scannedSheetIsOpen);
        return generated.Count;
    }

    private static string SimilarCountAddedStatus(int added, int skipped, string itemName, bool scannedSheetIsOpen)
    {
        string review = scannedSheetIsOpen
            ? " They stay selected for review."
            : " Open the scanned sheet to review them.";
        string count = skipped > 0
            ? $"added {added} new marker(s) to {itemName}; skipped {skipped} already counted."
            : $"added {added} marker(s) to {itemName}.";
        return "Count similar: " + count + review;
    }

    private TakeoffItem? CurrentSimilarCountDestinationItem()
    {
        if (_activeItem == null ||
            OurPlaneCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType) != "point")
        {
            return null;
        }

        return _activeItem;
    }

    private TakeoffItem? ResolveSimilarCountDestinationItem(TakeoffItem? destinationItem)
    {
        if (destinationItem == null ||
            OurPlaneCoreJobStore.NormalizeMeasurementType(destinationItem.MeasurementType) != "point")
        {
            return null;
        }

        return _takeoffItems.FirstOrDefault(item =>
            string.Equals(item.FolderPath, destinationItem.FolderPath, StringComparison.OrdinalIgnoreCase)) ??
            destinationItem;
    }

    private static string SimilarCountDestinationName(TakeoffItem? destinationItem)
    {
        string name = destinationItem?.Name?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(name) ? "Similar Count" : name;
    }

    private static bool IsSimilarCountDuplicateCenter(TakeoffItem item, string pageFolder, SKPoint center)
    {
        float toleranceSq = SimilarCountDuplicateTolerancePdf * SimilarCountDuplicateTolerancePdf;
        foreach (Measurement measurement in item.Measurements)
        {
            if (OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) != "point" ||
                measurement.Points.Count == 0 ||
                !string.Equals(measurement.PageFolder ?? "", pageFolder ?? "", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SKPoint existing = measurement.Points[0];
            float dx = existing.X - center.X;
            float dy = existing.Y - center.Y;
            if (dx * dx + dy * dy <= toleranceSq)
                return true;
        }

        return false;
    }

    private void QueueSimilarCountAiRequest(
        OurPlaneCoreJob reviewJob,
        PageInfo reviewPage,
        ViewportSimilarCountRequest request,
        int offlineCount,
        string destinationName)
    {
        if (_currentJob == null ||
            _currentPage == null ||
            !string.Equals(
                NormalizePathForCompare(_currentJob.RootPath),
                NormalizePathForCompare(reviewJob.RootPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!IsSamePageFolder(_currentPage.FolderPath, request.PageFolder))
        {
            TxtStatus.Text = $"Count similar: {offlineCount} marker(s) added; AI double-check skipped because the scanned sheet is not open.";
            return;
        }

        try
        {
            PageInfo page = OurPlaneCoreJobStore.TryReadPage(request.PageFolder) ?? reviewPage;
            var contextRequest = new ViewportContextRequest(
                0,
                0,
                request.PdfRect.MidX,
                request.PdfRect.MidY,
                request.PageFolder,
                null);
            if (!TrySaveAiCrop(contextRequest, "similar_count", out string cropPath, out SKRect cropRect, out string error,
                    request.PdfRect))
            {
                TxtStatus.Text = $"AI double-check skipped: {error}";
                return;
            }

            string prompt =
                "The attached crop shows ONE instance of a plan symbol. The app's offline matcher counted " +
                offlineCount.ToString(CultureInfo.InvariantCulture) +
                $" occurrences of this symbol on sheet '{page.Name}'. " +
                "Describe what the symbol most likely is and note anything that could make this count unreliable " +
                "(similar-looking symbols, rotated/mirrored variants, legend entries that should be excluded).";
            string details =
                "Similar-count AI double-check requested." + Environment.NewLine + Environment.NewLine +
                "Context:" + Environment.NewLine +
                $"- Page: {page.Name}" + Environment.NewLine +
                $"- Destination takeoff: {destinationName}" + Environment.NewLine +
                $"- Offline match count: {offlineCount}" + Environment.NewLine +
                $"- AI crop: {cropPath}" + Environment.NewLine +
                $"- PDF crop: {FormatPdfRect(cropRect)}" + Environment.NewLine;

            SmartObservation observation = SmartContextStore.AddObservation(
                reviewJob, page, "similar_count_request", details);
            SmartContextStore.AddAiRequest(
                reviewJob, page, observation, "similar_count_request", prompt, cropPath, "");
            LoadObservationsInbox();
            TxtStatus.Text = $"Count similar: {offlineCount} marker(s) added; AI double-check queued in the AI Inbox.";
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Similar count AI request failed to queue.");
            TxtStatus.Text = $"AI double-check skipped: {ex.Message}";
        }
    }
}
