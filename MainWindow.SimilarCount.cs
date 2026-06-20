using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private const int SimilarCountMaxSweepSheets = 80;
    private const float SimilarCountTextFallbackPaddingMinPdf = 6f;
    private const float SimilarCountTextFallbackPaddingMaxPdf = 24f;
    private const float SimilarCountTextFallbackPaddingRatio = 0.75f;
    private const float SimilarCountWeakTextCandidateScore = 0.50f;
    private const float SimilarCountNearestTextFallbackPaddingRatio = 0.35f;
    private const float SimilarCountNearestTextFallbackPaddingMinPdf = 8f;
    private const float SimilarCountNearestTextFallbackPaddingMaxPdf = 48f;
    private const int SimilarCountReviewReadinessRetryLimit = 24;
    private static readonly TimeSpan SimilarCountReviewReadinessRetryDelay = TimeSpan.FromMilliseconds(500);

    private SimilarCountDialog? _similarCountDialog;
    private int _similarCountReviewStartSerial;

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

    private void OnSimilarCountSelectionCompleted(ViewportSimilarCountRequest request) =>
        StartSimilarCountReview(request);

    private void StartSimilarCountReview(ViewportSimilarCountRequest request)
    {
        int serial = ++_similarCountReviewStartSerial;
        StartSimilarCountReview(request, serial, 0);
    }

    private void StartSimilarCountReview(
        ViewportSimilarCountRequest request,
        int startSerial,
        int readinessRetryCount)
    {
        if (_currentJob == null || _currentPage == null)
            return;

        if (_similarCountDialog != null)
        {
            _similarCountDialog.Activate();
            TxtStatus.Text = "Count similar review is already open.";
            return;
        }

        PdfSimilarTextResult? textResult = TryFindSimilarCountText(request);
        PdfSimilarTextMatch? textAnchor = textResult == null
            ? null
            : SelectSimilarTextAnchor(textResult, request);
        bool usedTextTemplateFallback = false;
        if (!_viewport.TryCreateSimilarCountSession(
                request.PdfRect, out SimilarSymbolMatchSession? session, out float bitmapScale, out string error) ||
            session == null)
        {
            if (ShouldRetrySimilarCountReviewReadiness(error, readinessRetryCount))
            {
                TxtStatus.Text = SimilarCountReviewReadinessRetryStatus(error, readinessRetryCount);
                _ = RetryStartSimilarCountReviewAsync(request, startSerial, readinessRetryCount + 1);
                return;
            }

            string originalError = error;
            if (request.UseTextCandidateRasterMatches && textAnchor != null)
            {
                ViewportSimilarCountRequest fallbackRequest = SimilarCountTextTemplateFallbackRequest(request, textAnchor);
                if (_viewport.TryCreateSimilarCountSession(
                        fallbackRequest.PdfRect,
                        out SimilarSymbolMatchSession? fallbackSession,
                        out float fallbackBitmapScale,
                        out string fallbackError) &&
                    fallbackSession != null)
                {
                    request = fallbackRequest;
                    session = fallbackSession;
                    bitmapScale = fallbackBitmapScale;
                    usedTextTemplateFallback = true;
                    textAnchor = SelectSimilarTextAnchor(textResult!, request) ?? textAnchor;
                    AppLog.Info(
                        $"Similar count text-template fallback used; originalError='{originalError}'; query='{textResult!.Query}'; page='{request.PageFolder}'");
                }
                else
                {
                    error = string.IsNullOrWhiteSpace(fallbackError)
                        ? originalError
                        : $"{originalError} Text mark fallback also failed: {fallbackError}";
                }
            }

            if (session == null)
            {
                TxtStatus.Text = $"Count similar: {error}";
                MessageBox.Show($"Count similar symbols:\n{error}", "Count Similar",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        var lastMatches = new List<SimilarSymbolMatch>();
        var excludedIndexes = new HashSet<int>();
        var alreadyCountedIndexes = new HashSet<int>();
        var manualReviewStatesByCenter = new Dictionary<string, bool>(StringComparer.Ordinal);
        var otherSheetSweeps = new List<SimilarSheetSweep>();
        string otherSheetSweepKey = "";       // "rotations|mirrored" the sweep cache was built for
        bool otherSheetEnabled = false;       // whether the last scan included other sheets
        int otherSheetSkippedOverLimit = 0;
        bool textCandidateReviewFallbackActive = false;
        bool includeTextCandidateReviewMatchesByDefault = false;
        float currentThreshold = (float)AppSettingsStore.SimilarCountThresholdDefault;
        OurPlaneCoreJob reviewJob = _currentJob;
        PageInfo reviewPage = _currentPage;
        TakeoffItem? destinationItem = RequestedSimilarCountDestinationItem(request);
        bool canRenameDestination = destinationItem == null;
        string destinationName = SimilarCountDestinationName(destinationItem, request, textResult);
        IReadOnlyList<SimilarSymbolMatch> textMatches = textResult == null
            ? []
            : textResult.Matches
                .Select(match => TextSimilarMatch(match.Center, bitmapScale))
                .ToList();
        string templateWarning = SimilarCountTemplateWarning(
            session.TemplateWarning,
            textResult,
            request,
            usedTextTemplateFallback);
        SimilarCountDialog? dialog = null;

        bool IsReviewJobCurrent() =>
            _currentJob != null &&
            string.Equals(
                NormalizePathForCompare(_currentJob.RootPath),
                NormalizePathForCompare(reviewJob.RootPath),
                StringComparison.OrdinalIgnoreCase);

        SKPoint matchMarkerOffsetPdf = SimilarCountMarkerOffset(request);

        SKPoint MatchCenterPdf(SimilarSymbolMatch match) =>
            new(match.CenterX / bitmapScale, match.CenterY / bitmapScale);

        SKPoint AddMarkerOffset(SKPoint rawCenter) =>
            new(rawCenter.X + matchMarkerOffsetPdf.X, rawCenter.Y + matchMarkerOffsetPdf.Y);

        SKPoint ReviewCenterPdf(SimilarSymbolMatch match) =>
            AddMarkerOffset(MatchCenterPdf(match));

        IReadOnlyList<ViewportSimilarCountPreviewMarker> BuildPreviewMarkers() =>
            lastMatches
                .Select((match, index) => new ViewportSimilarCountPreviewMarker(
                    ReviewCenterPdf(match),
                    Included: !excludedIndexes.Contains(index) && !alreadyCountedIndexes.Contains(index),
                    match.Score,
                    match.RotationDegrees,
                    match.Mirrored,
                    match.ScalePercent,
                    AlreadyCounted: alreadyCountedIndexes.Contains(index),
                    TemplateCoverage: match.TemplateCoverage,
                    WindowPrecision: match.WindowPrecision,
                    InkRatio: match.InkRatio,
                    ProfileScore: match.ProfileScore,
                    ProjectionScore: match.ProjectionScore,
                    StrokeScore: match.StrokeScore,
                    UsedFocusedScore: match.UsedFocusedScore))
                .ToList();

        IReadOnlyList<SKPoint> IncludedCenters() =>
            lastMatches
                .Where((_, index) => !excludedIndexes.Contains(index) && !alreadyCountedIndexes.Contains(index))
                .Select(ReviewCenterPdf)
                .ToList();

        // Above-threshold, not-already-counted hits on other sheets, grouped by
        // sheet. Sweeps are cached at the loose floor; this re-filters them to
        // the current threshold so the slider stays instant.
        List<(SimilarSheetSweep Sweep, List<SKPoint> Centers)> OtherSheetAdditions(float threshold)
        {
            var additions = new List<(SimilarSheetSweep, List<SKPoint>)>();
            if (!otherSheetEnabled)
                return additions;

            foreach (SimilarSheetSweep sweep in otherSheetSweeps)
            {
                var centers = new List<SKPoint>();
                foreach ((SKPoint center, float score) in sweep.Hits)
                {
                    if (score < threshold)
                        continue;
                    SKPoint markerCenter = AddMarkerOffset(center);
                    if (destinationItem != null &&
                        IsSimilarCountDuplicateCenter(destinationItem, sweep.PageFolder, markerCenter))
                        continue;
                    centers.Add(markerCenter);
                }
                if (centers.Count > 0)
                    additions.Add((sweep, centers));
            }

            return additions;
        }

        string OtherSheetSummary()
        {
            if (!otherSheetEnabled || otherSheetSweepKey.Length == 0)
                return "";

            List<(SimilarSheetSweep Sweep, List<SKPoint> Centers)> additions = OtherSheetAdditions(currentThreshold);
            int markers = additions.Sum(addition => addition.Centers.Count);
            string skipped = otherSheetSkippedOverLimit > 0
                ? $" {otherSheetSkippedOverLimit} sheet(s) skipped over scan limit."
                : "";
            return markers == 0
                ? $"Other sheets: no new matches.{skipped}"
                : $"Other sheets: +{markers} on {additions.Count} sheet(s) added on Add.{skipped}";
        }

        SimilarCountScanResult BuildReviewResult()
        {
            int total = lastMatches.Count;
            List<(SimilarSheetSweep Sweep, List<SKPoint> Centers)> otherAdditions = OtherSheetAdditions(currentThreshold);
            int otherNew = otherAdditions.Sum(addition => addition.Centers.Count);
            string otherSummary = OtherSheetSummary();
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
                    SimilarCountLimitSummary(),
                    otherNew,
                    otherAdditions.Count,
                    otherSummary);
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
                SimilarCountLimitSummary(),
                otherNew,
                otherAdditions.Count,
                otherSummary);
        }

        static bool IsWeakSimilarMatch(SimilarSymbolMatch match) =>
            match.Score > 0f && match.Score < (float)AppSettingsStore.SimilarCountThresholdDefault;

        string SimilarCountLimitSummary()
        {
            if (textCandidateReviewFallbackActive)
            {
                return includeTextCandidateReviewMatchesByDefault
                    ? "Text-only candidates included; review before Add"
                    : "Text-only candidates need manual review";
            }

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
            float layoutScore = Math.Min(Math.Min(match.ProfileScore, match.ProjectionScore), match.StrokeScore);
            (string Label, float Score) limit = ("coverage", match.TemplateCoverage);
            if (match.WindowPrecision < limit.Score)
                limit = ("precision", match.WindowPrecision);
            if (match.InkRatio < limit.Score)
                limit = ("ink", match.InkRatio);
            if (match.StrokeScore < limit.Score)
                limit = ("stroke", match.StrokeScore);
            if (layoutScore < limit.Score)
                limit = ("layout", layoutScore);
            return limit.Label;
        }

        int WeakSimilarMatchCount() =>
            lastMatches
                .Where((match, index) => !alreadyCountedIndexes.Contains(index) && IsWeakSimilarMatch(match))
                .Count();

        void ExcludeWeakSimilarMatches(bool includeTextCandidatesByDefault)
        {
            for (int i = 0; i < lastMatches.Count; i++)
            {
                if (IsWeakSimilarMatch(lastMatches[i]) &&
                    !(includeTextCandidatesByDefault && textCandidateReviewFallbackActive))
                {
                    excludedIndexes.Add(i);
                }
            }
        }

        void ExcludeAlreadyCountedSimilarMatches()
        {
            alreadyCountedIndexes.Clear();
            for (int i = 0; i < lastMatches.Count; i++)
            {
                if (destinationItem != null &&
                    IsSimilarCountAlreadyCounted(destinationItem, request, ReviewCenterPdf(lastMatches[i])))
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
            ExcludeWeakSimilarMatches(includeTextCandidateReviewMatchesByDefault);
            ExcludeAlreadyCountedSimilarMatches();
            ApplyManualSimilarReviewChoices();
        }

        void ApplyStrongSimilarReviewExclusions()
        {
            excludedIndexes.Clear();
            ExcludeWeakSimilarMatches(includeTextCandidatesByDefault: false);
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
                result.LimitSummary,
                result.OtherSheetNewCount,
                result.OtherSheetCount,
                result.OtherSheetSummary);
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
            ApplyStrongSimilarReviewExclusions();
            RefreshPreviewReview();
            SimilarCountScanResult result = BuildReviewResult();
            TxtStatus.Text = SimilarReviewStatus(result);
        }

        async Task EnsureOtherSheetSweepAsync(bool rotations, bool mirrored, CancellationToken cancellationToken)
        {
            string key = $"{rotations}|{mirrored}";
            if (string.Equals(otherSheetSweepKey, key, StringComparison.Ordinal))
                return;

            TxtStatus.Text = "Count similar: scanning other sheets...";
            (List<SimilarSheetSweep> sweeps, int skipped) = await SweepOtherSimilarSheetsAsync(
                reviewJob,
                request,
                textResult,
                textAnchor,
                session,
                bitmapScale,
                rotations,
                mirrored,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            otherSheetSweeps.Clear();
            otherSheetSweeps.AddRange(sweeps);
            otherSheetSkippedOverLimit = skipped;
            otherSheetSweepKey = key;
        }

        async Task<SimilarCountScanResult> ScanAsync(
            float threshold,
            bool rotations,
            bool mirrored,
            bool allSheets,
            CancellationToken cancellationToken)
        {
            var scanWatch = Stopwatch.StartNew();
            try
            {
                textCandidateReviewFallbackActive = false;
                includeTextCandidateReviewMatchesByDefault = false;
                AppLog.Info(
                    $"Similar count scan started; page='{request.PageFolder}'; bitmapScale={bitmapScale:0.###}; downsample={session.DownsampleFactor}; search={session.SearchWidth}x{session.SearchHeight}; threshold={threshold:0.###}; rotations={rotations}; mirrored={mirrored}; allSheets={allSheets}");
                List<SimilarSymbolMatch> matches;
                if (request.AllowExactTextMatches && textResult != null && textMatches.Count > 0)
                {
                    matches = textMatches.ToList();
                    AppLog.Info(
                        $"Similar count text matches applied; query='{textResult.Query}'; matches={matches.Count}; page='{request.PageFolder}'");
                }
                else if (request.UseTextCandidateRasterMatches &&
                         textResult != null &&
                         textAnchor != null &&
                         textResult.Matches.Count > 1)
                {
                    matches = await Task.Run(
                        () => FindTextCandidateRasterMatches(
                            session,
                            textResult,
                            textAnchor,
                            request,
                            bitmapScale,
                            threshold,
                            rotations,
                            mirrored,
                            cancellationToken),
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    AppLog.Info(
                        $"Similar count text-raster candidates applied; query='{textResult.Query}'; textCandidates={textResult.Matches.Count}; verifiedMatches={matches.Count}; page='{request.PageFolder}'");
                    if (TextRasterMatchesLeaveNoNewMarkers(matches))
                    {
                        matches = BuildWeakTextCandidateReviewMatches(
                            textResult,
                            textAnchor,
                            request,
                            bitmapScale);
                        textCandidateReviewFallbackActive = true;
                        includeTextCandidateReviewMatchesByDefault = request.IncludeTextCandidatesByDefault;
                        AppLog.Info(
                            $"Similar count text-raster produced no new verified markers; showing weak text-only review candidates instead; query='{textResult.Query}'; textCandidates={textResult.Matches.Count}; reviewCandidates={matches.Count}; page='{request.PageFolder}'");
                    }
                    else
                    {
                        int weakAdded = AppendUnverifiedTextCandidateReviewMatches(
                            matches,
                            BuildWeakTextCandidateReviewMatches(
                                textResult,
                                textAnchor,
                                request,
                                bitmapScale),
                            bitmapScale);
                        if (weakAdded > 0)
                        {
                            textCandidateReviewFallbackActive = true;
                            includeTextCandidateReviewMatchesByDefault = request.IncludeTextCandidatesByDefault;
                            AppLog.Info(
                                $"Similar count text-raster added weak unverified text review candidates; query='{textResult.Query}'; textCandidates={textResult.Matches.Count}; verifiedMatches={matches.Count - weakAdded}; weakAdded={weakAdded}; reviewCandidates={matches.Count}; page='{request.PageFolder}'");
                        }
                    }
                }
                else
                {
                    matches = await Task.Run(
                        () => session.FindMatches(threshold, rotations, mirrored, cancellationToken),
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                scanWatch.Stop();
                AppLog.Info(
                    $"Similar count scan completed; elapsedMs={scanWatch.ElapsedMilliseconds}; matches={matches.Count}; downsample={session.DownsampleFactor}; searchPixels={session.SearchPixels}; page='{request.PageFolder}'");
                lastMatches.Clear();
                lastMatches.AddRange(matches);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                scanWatch.Stop();
                AppLog.Info(
                    $"Similar count scan cancelled; elapsedMs={scanWatch.ElapsedMilliseconds}; downsample={session.DownsampleFactor}; searchPixels={session.SearchPixels}; page='{request.PageFolder}'");
                throw;
            }
            ApplyDefaultSimilarReviewExclusions();
            _viewport.SetSimilarCountPreviewMarkers(BuildPreviewMarkers(), request.PageFolder);

            currentThreshold = threshold;
            otherSheetEnabled = allSheets;
            if (allSheets)
                await EnsureOtherSheetSweepAsync(rotations, mirrored, cancellationToken);

            return BuildReviewResult();
        }

        bool TextRasterMatchesLeaveNoNewMarkers(IReadOnlyList<SimilarSymbolMatch> matches)
        {
            if (matches.Count == 0)
                return true;

            if (destinationItem == null)
                return false;

            return matches.All(match =>
                IsSimilarCountAlreadyCounted(destinationItem, request, ReviewCenterPdf(match)));
        }

        dialog = new SimilarCountDialog(
            scan: ScanAsync,
            initialThreshold: (float)AppSettingsStore.SimilarCountThresholdDefault,
            initialRotations: request.InitialIncludeRotations,
            initialMirrored: request.InitialIncludeMirrored,
            initialAllSheets: false,
            destinationName: destinationName,
            aiAvailable: !string.IsNullOrWhiteSpace(ReadOpenAiApiKey()),
            templateWarning: templateWarning,
            allowDestinationNameEdit: canRenameDestination)
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
            // Similar tuning is intentionally session-only; restoring loose,
            // rotated, mirrored, or all-sheets scans makes the next review open
            // straight into a slow/high-noise scan.
            _settings.SimilarCountThreshold = AppSettingsStore.SimilarCountThresholdDefault;
            _settings.SimilarCountRotations = false;
            _settings.SimilarCountMirrored = false;
            _settings.SimilarCountAllSheets = false;
            SaveAppSettings();

            if (!IsReviewJobCurrent())
            {
                TxtStatus.Text = "Count similar: original job changed; review was not added.";
                return;
            }

            IReadOnlyList<SKPoint> included = IncludedCenters();
            List<(SimilarSheetSweep Sweep, List<SKPoint> Centers)> offSheet = dialog.IncludeAllSheets
                ? OtherSheetAdditions(dialog.Threshold)
                : [];
            if (included.Count == 0 && offSheet.Count == 0)
            {
                TxtStatus.Text = "Count similar: nothing to add.";
                return;
            }

            int added = 0;
            TakeoffItem? addedItem = null;
            string acceptedDestinationName = dialog.DestinationName;
            if (included.Count > 0)
            {
                added = AddSimilarCountMeasurements(
                    request,
                    included,
                    destinationItem,
                    acceptedDestinationName,
                    out addedItem);
                if (added > 0 && dialog.QueueAiDoubleCheck)
                    QueueSimilarCountAiRequest(reviewJob, reviewPage, request, added, acceptedDestinationName);
            }

            if (offSheet.Count > 0)
            {
                TakeoffItem offItem = ResolveOrCreateSimilarCountItem(
                    addedItem ?? destinationItem,
                    acceptedDestinationName);
                (int offSheets, int offMarkers) = AddOtherSheetSimilarCounts(offItem, offSheet);
                if (offMarkers > 0)
                    TxtStatus.Text = $"{TxtStatus.Text} Plus {offMarkers} marker(s) on {offSheets} other sheet(s).".Trim();
            }
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

    private async Task RetryStartSimilarCountReviewAsync(
        ViewportSimilarCountRequest request,
        int startSerial,
        int readinessRetryCount)
    {
        await Task.Delay(SimilarCountReviewReadinessRetryDelay);
        if (startSerial != _similarCountReviewStartSerial || _similarCountDialog != null)
            return;

        if (_currentJob == null || _currentPage == null)
            return;

        if (!IsSamePageFolder(_currentPage.FolderPath, request.PageFolder))
        {
            TxtStatus.Text = "Count similar: scanned sheet changed before the review raster was ready.";
            return;
        }

        StartSimilarCountReview(request, startSerial, readinessRetryCount);
    }

    private static bool ShouldRetrySimilarCountReviewReadiness(string error, int readinessRetryCount) =>
        readinessRetryCount < SimilarCountReviewReadinessRetryLimit &&
        IsSimilarCountReviewReadinessError(error);

    private static bool IsSimilarCountReviewReadinessError(string error) =>
        !string.IsNullOrWhiteSpace(error) &&
        (error.Contains("sheet is rendering", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("sheet is sharpening", StringComparison.OrdinalIgnoreCase));

    private static string SimilarCountReviewReadinessRetryStatus(string error, int readinessRetryCount)
    {
        string clean = (error ?? "").Replace(
            " Selection will start automatically.",
            "",
            StringComparison.OrdinalIgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(clean))
            clean = "Count similar: sheet raster is preparing.";

        return $"{clean} Retrying Similar review {readinessRetryCount + 1}/{SimilarCountReviewReadinessRetryLimit}.";
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

    private static PdfSimilarTextResult? TryFindSimilarCountText(ViewportSimilarCountRequest request)
    {
        if (!request.AllowExactTextMatches && !request.UseTextCandidateRasterMatches)
            return null;

        if (string.IsNullOrWhiteSpace(request.PdfPath) || request.PdfPageIndex < 0)
            return null;

        SKRect searchRect = SimilarCountTextSearchRect(request);
        SKPoint anchor = request.TemplateAnchorPdf ?? RectCenter(searchRect);
        if (!PdfSimilarTextService.TryFindSimilarText(
                request.PdfPath,
                request.PdfPageIndex,
                searchRect,
                request.PreferNearestRepeatedText,
                anchor,
                out PdfSimilarTextResult result,
                out string error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                AppLog.Info($"Similar count text scan unavailable; {error}");
            return null;
        }

        if (IsUsableSimilarTextResult(result))
            return result;

        if (request.PreferNearestRepeatedText || !request.AllowExactTextMatches)
            return null;

        if (!PdfSimilarTextService.TryFindSimilarText(
                request.PdfPath,
                request.PdfPageIndex,
                searchRect,
                preferNearestRepeatedText: true,
                anchor,
                out PdfSimilarTextResult nearestResult,
                out string nearestError))
        {
            if (!string.IsNullOrWhiteSpace(nearestError))
                AppLog.Info($"Similar count nearest text fallback unavailable; {nearestError}");
            return null;
        }

        if (!IsUsableSimilarTextResult(nearestResult) ||
            !NearestRepeatedTextFallbackLooksIntentional(nearestResult, searchRect, anchor))
        {
            return null;
        }

        AppLog.Info(
            $"Similar count nearest repeated text fallback used; query='{nearestResult.Query}'; matches={nearestResult.Matches.Count}; page='{request.PageFolder}'");
        return nearestResult;
    }

    private static bool IsUsableSimilarTextResult(PdfSimilarTextResult result) =>
        !string.IsNullOrWhiteSpace(result.Query) && result.Matches.Count >= 2;

    private static bool NearestRepeatedTextFallbackLooksIntentional(
        PdfSimilarTextResult result,
        SKRect searchRect,
        SKPoint anchor)
    {
        PdfSimilarTextMatch? selectedAnchor = result.Matches
            .Where(match => RectsIntersect(match.Rect, searchRect))
            .OrderBy(match => DistanceSquared(match.Center, anchor))
            .FirstOrDefault();
        if (selectedAnchor == null)
            return false;

        float side = Math.Max(Math.Abs(searchRect.Width), Math.Abs(searchRect.Height));
        float tolerance = Math.Clamp(
            side * SimilarCountNearestTextFallbackPaddingRatio,
            SimilarCountNearestTextFallbackPaddingMinPdf,
            SimilarCountNearestTextFallbackPaddingMaxPdf);
        return DistanceSquared(selectedAnchor.Center, anchor) <= tolerance * tolerance;
    }

    private static ViewportSimilarCountRequest SimilarCountTextTemplateFallbackRequest(
        ViewportSimilarCountRequest request,
        PdfSimilarTextMatch textAnchor)
    {
        float side = Math.Max(textAnchor.Rect.Width, textAnchor.Rect.Height);
        float padding = !float.IsFinite(side) || side <= 0f
            ? SimilarCountTextFallbackPaddingMinPdf
            : Math.Clamp(
                side * SimilarCountTextFallbackPaddingRatio,
                SimilarCountTextFallbackPaddingMinPdf,
                SimilarCountTextFallbackPaddingMaxPdf);

        return request with
        {
            PdfRect = PadRect(textAnchor.Rect, padding),
            TemplateAnchorPdf = textAnchor.Center,
        };
    }

    private static List<SimilarSymbolMatch> FindTextCandidateRasterMatches(
        SimilarSymbolMatchSession session,
        PdfSimilarTextResult textResult,
        PdfSimilarTextMatch textAnchor,
        ViewportSimilarCountRequest request,
        float bitmapScale,
        float threshold,
        bool rotations,
        bool mirrored,
        CancellationToken cancellationToken)
    {
        SKPoint anchor = request.TemplateAnchorPdf ?? RectCenter(request.PdfRect);
        SKPoint textOffset = new(textAnchor.Center.X - anchor.X, textAnchor.Center.Y - anchor.Y);
        var candidateCenters = textResult.Matches
            .Select(match => new SKPoint(
                (match.Center.X - textOffset.X) * bitmapScale,
                (match.Center.Y - textOffset.Y) * bitmapScale))
            .ToList();
        int radiusPixels = SimilarCountTextCandidateSearchRadiusPixels(request, bitmapScale);
        return session.FindMatchesNearCenters(
            candidateCenters,
            radiusPixels,
            threshold,
            rotations,
            mirrored,
            cancellationToken);
    }

    private static List<SimilarSymbolMatch> BuildWeakTextCandidateReviewMatches(
        PdfSimilarTextResult textResult,
        PdfSimilarTextMatch textAnchor,
        ViewportSimilarCountRequest request,
        float bitmapScale)
    {
        SKPoint anchor = request.TemplateAnchorPdf ?? RectCenter(request.PdfRect);
        SKPoint textOffset = new(textAnchor.Center.X - anchor.X, textAnchor.Center.Y - anchor.Y);
        return textResult.Matches
            .Select(match => new SKPoint(match.Center.X - textOffset.X, match.Center.Y - textOffset.Y))
            .Select(center => TextSimilarMatch(center, bitmapScale, SimilarCountWeakTextCandidateScore))
            .ToList();
    }

    private static int AppendUnverifiedTextCandidateReviewMatches(
        List<SimilarSymbolMatch> matches,
        IReadOnlyList<SimilarSymbolMatch> textCandidates,
        float bitmapScale)
    {
        float duplicateTolerancePx = Math.Max(10f, SimilarCountDuplicateTolerancePdf * bitmapScale);
        float duplicateToleranceSq = duplicateTolerancePx * duplicateTolerancePx;
        int added = 0;

        foreach (SimilarSymbolMatch candidate in textCandidates)
        {
            if (matches.Any(match => DistanceSquaredPixels(match, candidate) <= duplicateToleranceSq))
                continue;

            matches.Add(candidate);
            added++;
        }

        return added;
    }

    private static float DistanceSquaredPixels(SimilarSymbolMatch left, SimilarSymbolMatch right)
    {
        float dx = left.CenterX - right.CenterX;
        float dy = left.CenterY - right.CenterY;
        return dx * dx + dy * dy;
    }

    private static PdfSimilarTextMatch? SelectSimilarTextAnchor(
        PdfSimilarTextResult result,
        ViewportSimilarCountRequest request)
    {
        SKRect searchRect = SimilarCountTextSearchRect(request);
        SKPoint anchor = request.TemplateAnchorPdf ?? RectCenter(request.PdfRect);
        return result.Matches
            .Where(match => RectsIntersect(match.Rect, searchRect))
            .OrderBy(match => DistanceSquared(match.Center, anchor))
            .FirstOrDefault();
    }

    private static SimilarSymbolMatch TextSimilarMatch(
        SKPoint centerPdf,
        float bitmapScale,
        float score = 1f)
    {
        int centerX = (int)MathF.Round(centerPdf.X * bitmapScale);
        int centerY = (int)MathF.Round(centerPdf.Y * bitmapScale);
        score = Math.Clamp(score, 0f, 1f);
        return new SimilarSymbolMatch(
            centerX,
            centerY,
            score,
            0,
            TemplateCoverage: score,
            WindowPrecision: score,
            InkRatio: score,
            ProfileScore: score,
            ProjectionScore: score,
            StrokeScore: score);
    }

    private static string SimilarCountTemplateWarning(
        string rasterWarning,
        PdfSimilarTextResult? textResult,
        ViewportSimilarCountRequest request,
        bool usedTextTemplateFallback)
    {
        if (textResult == null)
            return rasterWarning;

        string textWarning = request.UseTextCandidateRasterMatches && !request.AllowExactTextMatches
            ? $"PDF text candidates: {textResult.Query} ({textResult.Matches.Count}); raster verified."
            : $"PDF text exact match: {textResult.Query} ({textResult.Matches.Count} found).";
        if (usedTextTemplateFallback)
            textWarning += " Template fell back to the text mark because the measured box had no matchable linework.";
        return string.IsNullOrWhiteSpace(rasterWarning)
            ? textWarning
            : rasterWarning + " " + textWarning;
    }

    private static SKPoint SimilarCountMarkerOffset(ViewportSimilarCountRequest request)
    {
        if (request.TemplateAnchorPdf is not { } anchor ||
            request.MarkerCenterPdf is not { } marker)
        {
            return default;
        }

        return new SKPoint(marker.X - anchor.X, marker.Y - anchor.Y);
    }

    private static SKRect SimilarCountTextSearchRect(ViewportSimilarCountRequest request) =>
        request.TextSearchRectPdf ?? request.PdfRect;

    private static int SimilarCountTextCandidateSearchRadiusPixels(
        ViewportSimilarCountRequest request,
        float bitmapScale)
    {
        float radius = 0f;
        if (request.TextCandidateSearchRadiusPdf > 0f && float.IsFinite(request.TextCandidateSearchRadiusPdf))
            radius = request.TextCandidateSearchRadiusPdf * bitmapScale;
        else
        {
            float templateSide = Math.Max(request.PdfRect.Width, request.PdfRect.Height);
            radius = templateSide * bitmapScale * 0.18f;
        }

        radius = Math.Clamp(radius, 14f, 384f);
        return (int)MathF.Ceiling(radius);
    }

    private static SKPoint RectCenter(SKRect rect) =>
        new((rect.Left + rect.Right) / 2f, (rect.Top + rect.Bottom) / 2f);

    private static float DistanceSquared(SKPoint left, SKPoint right)
    {
        float dx = left.X - right.X;
        float dy = left.Y - right.Y;
        return dx * dx + dy * dy;
    }

    private static bool RectsIntersect(SKRect left, SKRect right) =>
        left.Left <= right.Right &&
        left.Right >= right.Left &&
        left.Top <= right.Bottom &&
        left.Bottom >= right.Top;

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
        TakeoffItem? destinationItem,
        string newItemName,
        out TakeoffItem item)
    {
        TakeoffItem resolvedItem = ResolveOrCreateSimilarCountItem(destinationItem, newItemName);
        item = resolvedItem;

        var newCenters = centers
            .Where(center => !IsSimilarCountAlreadyCounted(resolvedItem, request, center))
            .ToList();
        if (newCenters.Count == 0)
        {
            TxtStatus.Text = $"Count similar: all reviewed marker(s) are already counted in {resolvedItem.Name}.";
            return 0;
        }

        var generated = new List<Measurement>(newCenters.Count);
        foreach (SKPoint center in newCenters)
        {
            generated.Add(new Measurement
            {
                MType = "point",
                Points = [center],
                Color = resolvedItem.Color,
                CountSymbol = CountDisplaySymbol.Normalize(resolvedItem.CountSymbol),
                PageFolder = request.PageFolder,
                TakeoffFolder = resolvedItem.FolderPath,
                ScaleMetersPerPt = request.ScaleMetersPerPt,
            });
        }

        foreach (Measurement measurement in generated)
            resolvedItem.Measurements.Add(measurement);
        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(resolvedItem);
        bool scannedSheetIsOpen = IsSamePageFolder(_currentPage?.FolderPath, request.PageFolder);
        if (scannedSheetIsOpen)
        {
            _viewport.AddGeneratedMeasurements(generated);
            _viewport.SelectMeasurements(generated);
        }
        RefreshTreeItem(resolvedItem);
        QueueTakeoffAutosave(resolvedItem);
        using (UsePageMeasurementLookup())
        {
            RefreshTakeoffRowVisualsForItems(new[] { resolvedItem });
            RefreshPageTakeoffIndicatorsForFolder(request.PageFolder);
            RefreshSheetLegend();
        }
        UpdateTotalDisplay();
        int skipped = centers.Count - generated.Count;
        TxtStatus.Text = SimilarCountAddedStatus(generated.Count, skipped, resolvedItem.Name, scannedSheetIsOpen);
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

    // Resolve the live destination point takeoff, or create a fresh
    // "Similar Count" item when there is no suitable active one. Shared by the
    // current-sheet add and the all-sheets add so both target the same item.
    private TakeoffItem ResolveOrCreateSimilarCountItem(TakeoffItem? destinationItem, string newItemName)
    {
        TakeoffItem? item = ResolveSimilarCountDestinationItem(destinationItem);
        if (item != null)
            return item;

        string parent = NewTakeoffItemParentFolderForUserCreate();
        string cleanName = string.IsNullOrWhiteSpace(newItemName)
            ? "Similar Count"
            : newItemName.Trim();
        TakeoffItem created = CreateUniqueTakeoffItem(
            cleanName,
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
        return item;
    }

    private TakeoffItem? RequestedSimilarCountDestinationItem(ViewportSimilarCountRequest request) =>
        string.IsNullOrWhiteSpace(request.DestinationTakeoffFolderPath)
            ? null
            : _takeoffItems.FirstOrDefault(item =>
                string.Equals(item.FolderPath, request.DestinationTakeoffFolderPath, StringComparison.OrdinalIgnoreCase));

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

    private static string SimilarCountDestinationName(
        TakeoffItem? destinationItem,
        ViewportSimilarCountRequest request,
        PdfSimilarTextResult? textResult)
    {
        string name = destinationItem?.Name?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(name))
            return name;
        if (!string.IsNullOrWhiteSpace(request.DefaultDestinationName))
            return request.DefaultDestinationName.Trim();
        if (request.AllowExactTextMatches && !string.IsNullOrWhiteSpace(textResult?.Query))
            return textResult.Query.Trim();
        return "Similar Count";
    }

    private static bool IsSimilarCountDuplicateCenter(TakeoffItem item, string pageFolder, SKPoint center)
    {
        foreach (Measurement measurement in item.Measurements)
        {
            if (OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) != "point" ||
                measurement.Points.Count == 0 ||
                !string.Equals(measurement.PageFolder ?? "", pageFolder ?? "", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsSimilarCountDuplicatePoint(measurement.Points[0], center))
                return true;
        }

        return false;
    }

    private static bool IsSimilarCountAlreadyCounted(
        TakeoffItem item,
        ViewportSimilarCountRequest request,
        SKPoint center) =>
        IsSimilarCountDuplicateCenter(item, request.PageFolder, center) ||
        IsSimilarCountDuplicateCenter(request.AlreadyCountedCentersPdf, center);

    private static bool IsSimilarCountDuplicateCenter(IReadOnlyList<SKPoint>? centers, SKPoint center)
    {
        if (centers == null)
            return false;

        foreach (SKPoint existing in centers)
        {
            if (IsSimilarCountDuplicatePoint(existing, center))
                return true;
        }

        return false;
    }

    private static bool IsSimilarCountDuplicatePoint(SKPoint existing, SKPoint center)
    {
        float dx = existing.X - center.X;
        float dy = existing.Y - center.Y;
        return dx * dx + dy * dy <= SimilarCountDuplicateTolerancePdf * SimilarCountDuplicateTolerancePdf;
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
