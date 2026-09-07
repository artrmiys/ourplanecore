using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private async void BtnAutoRenamePdf_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            async () =>
            {
                if (GetSelectedPdfAutomationTarget("Auto Name") is { } item)
                    await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: false);
            },
            "Auto Name failed.",
            "Auto Name");
    }

    private async void BtnAutoScalePdf_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            async () =>
            {
                if (GetSelectedPdfAutomationTarget("Auto Scale") is { } item)
                    await AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: true);
            },
            "Auto Scale failed.",
            "Auto Scale");
    }

    private async void BtnAutoRenameScalePdf_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            async () =>
            {
                if (GetSelectedPdfAutomationTarget("Auto Name + Scale") is { } item)
                    await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: true);
            },
            "Auto Name + Scale failed.",
            "Auto Name + Scale");
    }

    private TreeViewItem? GetSelectedPdfAutomationTarget(string title)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before PDF automation.";
            return null;
        }

        if (PagesTree.SelectedItem is TreeViewItem selected &&
            (selected.Tag is PageInfo || selected.Tag is PageFolderNode))
        {
            return selected;
        }

        if (Directory.Exists(_currentJob.PagesRoot))
            return CreateHiddenPagesRootItem();

        PostStatusInfo("No PDF pages found.");
        return null;
    }

    private async Task AnalyzePdfMetadataAsync(
        TreeViewItem item,
        bool applyRename,
        bool applyScale,
        bool forceReanalyze = false)
    {
        if (_currentJob == null)
            return;
        if (!EnsureCurrentJobWritable("analyze and apply PDF metadata"))
            return;

        var pages = GetPagesForMetadata(item).ToList();
        if (pages.Count == 0)
        {
            PostStatusInfo("No PDF pages found in this selection.");
            return;
        }

        OurPlanCoreJob job = _currentJob;
        SaveCurrentPageScale();
        await OfferPdfMetadataGuidanceIfNeededAsync(job, pages, "PDF metadata");
        if (!EnsureExpectedJobWritable(job, "analyze PDF metadata"))
            return;

        TxtStatus.Text = $"Analyzing PDF metadata for {pages.Count} page(s)...";
        bool persistBeforeReview = (!applyRename && !applyScale) ||
                                   SheetMetadataRulesService.Active.ImportPolicy == SheetMetadataImportPolicy.LegacyAutoApply;
        List<PdfMetadataPageResult> results;
        using (ShowBusyOverlay($"Analyzing PDF metadata for {pages.Count} page(s)..."))
        {
            await WaitForBusyOverlayRenderAsync();
            if (!EnsureExpectedJobWritable(job, "analyze PDF metadata"))
                return;
            results = await Task.Run(() => AnalyzePdfPages(
                job, pages, persistBeforeReview, forceReanalyze: forceReanalyze));
        }
        if (!EnsureExpectedJobWritable(job, "review PDF metadata results"))
            return;

        int okCount = results.Count(result => result.Ok);
        int failCount = results.Count - okCount;
        string operationTitle = applyRename && applyScale
            ? "Auto Rename + Scale from PDF"
            : applyRename
                ? "Auto Rename from PDF"
                : applyScale
                    ? "Auto Scale from PDF"
                    : "Analyze PDF Metadata";

        if (!applyRename && !applyScale)
        {
            ReloadPagesTree(pages[0].FolderPath);
            string message = BuildPdfMetadataSummary(results, includeApplyPreview: false);
            MessageBox.Show(message, "Analyze PDF Metadata", MessageBoxButton.OK,
                            failCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            TxtStatus.Text = $"PDF metadata analyzed: {okCount} OK, {failCount} failed.";
            return;
        }

        string preview = BuildPdfMetadataSummary(results, includeApplyPreview: true);
        if (okCount == 0)
        {
            MessageBox.Show(preview, operationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtStatus.Text = $"PDF metadata analyze failed for {failCount} page(s).";
            return;
        }

        var rows = BuildPdfMetadataPreviewRows(results, applyRename, applyScale).ToList();
        var dialog = new PdfMetadataPreviewDialog(rows, operationTitle)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            TxtStatus.Text = $"PDF metadata analyzed: {okCount} OK, apply cancelled.";
            return;
        }
        if (!EnsureExpectedJobWritable(job, "apply PDF metadata results", showDialog: true))
            return;

        ApplyPdfMetadataResults(job, results, dialog.Rows);
    }

    private List<PdfMetadataPageResult> AnalyzePdfPages(
        OurPlanCoreJob job,
        IReadOnlyList<PageInfo> pages,
        bool persistMetadata,
        CancellationToken cancellationToken = default,
        bool forceReanalyze = false)
    {
        IReadOnlyList<PdfSheetMetadataAnalysisItem> analyzed = PdfSheetMetadataService.AnalyzePages(
            job,
            pages,
            persistMetadata,
            forceReanalyze,
            cancellationToken,
            progress => Dispatcher.BeginInvoke(() =>
            {
                string cached = progress.CacheHits > 0 ? $", {progress.CacheHits} cached" : "";
                TxtStatus.Text =
                    $"Analyzing sheets: {progress.Completed}/{progress.Total}{cached} — {progress.CurrentPdf}";
            }));
        return analyzed
            .Select(item => new PdfMetadataPageResult(item.Page, item.Ok, item.Metadata, item.Error))
            .ToList();
    }

    private PdfMetadataApplySummary ApplyPdfMetadataResults(
        OurPlanCoreJob job,
        IReadOnlyList<PdfMetadataPageResult> results,
        IReadOnlyList<PdfMetadataPreviewRow> rows,
        bool reviewedByUser = true)
    {
        if (!IsExpectedJobWritable(job))
        {
            TxtStatus.Text = "PDF metadata was not applied because the originating job changed or became read-only.";
            return new PdfMetadataApplySummary(0, 0, rows.Count);
        }

        int renamed = 0;
        int scaled = 0;
        int failed = 0;
        bool reloadActiveTab = false;
        string? selectAfter = null;
        var resultsByFolder = results
            .Where(result => result.Ok && result.Metadata != null)
            .GroupBy(result => NormalizePath(result.Page.FolderPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (PdfMetadataPreviewRow row in rows.Where(row =>
                     row.ApplyRename ||
                     row.ScaleAction != PdfMetadataScaleAction.Keep ||
                     (reviewedByUser && row.ReviewScale)))
        {
            if (!IsExpectedJobWritable(job))
            {
                failed++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.PageFolder))
            {
                failed++;
                continue;
            }

            resultsByFolder.TryGetValue(NormalizePath(row.PageFolder), out PdfMetadataPageResult? result);
            PageInfo? sourcePage = result?.Page ?? OurPlanCoreJobStore.TryReadPage(row.PageFolder);
            if (sourcePage == null)
            {
                failed++;
                continue;
            }

            PdfSheetMetadata metadata = result?.Metadata
                ?? OurPlanCoreJobStore.ReadSourcePdfMetadata(sourcePage.FolderPath)
                ?? CreateManualSheetMetadata(sourcePage);

            string currentPath = sourcePage.FolderPath;
            string finalName = OurPlanCoreJobStore.DisplayName(currentPath);
            double finalScale = sourcePage.ScaleMetersPerPt;
            SmartSheetLearningDecision detected = PdfSheetMetadataService.DetectedDecision(metadata);

            try
            {
                if (row.ScaleAction == PdfMetadataScaleAction.Set)
                {
                    if (TryApplySheetManagerScale(sourcePage, metadata, row.ProposedScale, clear: false, out finalScale))
                    {
                        scaled++;
                        if (reviewedByUser &&
                            !PdfSheetMetadataPolicy.SameScale(detected.ScaleMetersPerPt, finalScale))
                        {
                            metadata.ScaleSource = "manual-review";
                            metadata.ScaleConfidence = "high";
                            metadata.ScaleEvidence = $"reviewed value '{row.ProposedScale.Trim()}'";
                            metadata.SuffixScalePolicy = "allow";
                        }
                    }
                    else
                    {
                        failed++;
                        metadata.Warnings.Add($"scale not applied: '{row.ProposedScale}'");
                    }
                }
                else if (row.ScaleAction == PdfMetadataScaleAction.Clear)
                {
                    if (TryApplySheetManagerScale(sourcePage, metadata, "", clear: true, out finalScale))
                    {
                        scaled++;
                        if (reviewedByUser)
                        {
                            metadata.ScaleSource = "manual-review";
                            metadata.ScaleConfidence = "high";
                            metadata.ScaleEvidence = "scale explicitly cleared in metadata review";
                            metadata.SuffixScalePolicy = "skip";
                        }
                    }
                    else
                        failed++;
                }
                else if (reviewedByUser && row.ReviewScale)
                {
                    PdfSheetMetadataPolicy.PersistKeptScaleDecision(metadata, finalScale);
                }

                if (row.ApplyRename)
                {
                    string proposedName = string.IsNullOrWhiteSpace(row.ProposedPageName)
                        ? metadata.ProposedPageName()
                        : row.ProposedPageName.Trim();
                    metadata.RenameCandidate = proposedName;

                    if (!string.IsNullOrWhiteSpace(proposedName) &&
                        !string.Equals(proposedName, finalName, StringComparison.OrdinalIgnoreCase))
                    {
                        string renamedPath = OurPlanCoreJobStore.RenamePageAllowDuplicateName(currentPath, proposedName);
                        reloadActiveTab = UpdatePageReferencesForMovedPath(currentPath, renamedPath) || reloadActiveTab;
                        currentPath = renamedPath;
                        finalName = OurPlanCoreJobStore.DisplayName(renamedPath);
                        renamed++;
                    }

                    SmartSheetLearningDecision reviewedName = PdfSheetMetadataService.FinalDecision(
                        sourcePage,
                        metadata,
                        finalName,
                        finalScale);
                    metadata.RenameCandidate = finalName;
                    metadata.Suffix = reviewedName.Suffix;
                    if (!string.Equals(detected.Suffix, reviewedName.Suffix, StringComparison.OrdinalIgnoreCase))
                        metadata.SuffixScalePolicy = "";
                    if (reviewedByUser &&
                        !string.Equals(detected.PageName, finalName, StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.TitleSource = "manual-review";
                        metadata.TitleConfidence = "high";
                        metadata.TitleEvidence = $"reviewed page name '{finalName}'";
                        metadata.SuffixSource = "manual-review";
                        metadata.SuffixConfidence = "high";
                        metadata.SuffixEvidence = $"reviewed suffix '{reviewedName.Suffix}'";
                    }
                }
                else if (SheetMetadataRulesService.Active.PreserveExistingManualSuffix)
                {
                    string safeName = PdfSheetMetadataPolicy.BuildSafeProposedPageName(
                        finalName,
                        metadata,
                        SheetMetadataRulesService.Active);
                    string safeSuffix = PdfSheetMetadataPolicy.ExtractSuffix(safeName, metadata.EffectiveSheetKey);
                    if (!string.IsNullOrWhiteSpace(safeSuffix))
                        metadata.Suffix = safeSuffix;
                }

                metadata.GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(metadata.Source))
                    metadata.Source = "manual";

                OurPlanCoreJobStore.WriteSourcePdfMetadata(currentPath, metadata);
                if (reviewedByUser && OurPlanCoreJobStore.TryReadPage(currentPath) is { } finalPage)
                {
                    var finalDecision = PdfSheetMetadataService.FinalDecision(finalPage, metadata, finalName, finalScale);
                    string nameOutcome = LearningTextOutcome(
                        row.ApplyRename,
                        detected.PageName,
                        finalDecision.PageName);
                    string suffixOutcome = LearningTextOutcome(
                        row.ApplyRename,
                        detected.Suffix,
                        finalDecision.Suffix);
                    string scaleOutcome = LearningScaleOutcome(
                        row.ReviewScale || row.ScaleAction != PdfMetadataScaleAction.Keep,
                        detected.ScaleMetersPerPt,
                        finalDecision.ScaleMetersPerPt);
                    string outcome = nameOutcome == "corrected" ||
                                     suffixOutcome == "corrected" ||
                                     scaleOutcome == "corrected"
                        ? "corrected"
                        : "accepted";
                    SmartLearningStore.AppendSheetFeedback(
                        job,
                        finalPage,
                        PdfSheetMetadataService.BuildLearningRecord(
                            sourcePage,
                            metadata,
                            outcome,
                            "User applied PDF metadata preview.",
                            finalDecision,
                            detected,
                            nameOutcome,
                            suffixOutcome,
                            scaleOutcome));
                }

                selectAfter ??= currentPath;
            }
            catch (Exception ex)
            {
                failed++;
                if (reviewedByUser)
                {
                    SmartLearningStore.AppendSheetFeedback(
                        job,
                        sourcePage,
                        PdfSheetMetadataService.BuildLearningRecord(
                            sourcePage,
                            metadata,
                            "failed_apply",
                            ex.Message,
                            detected: detected));
                }
            }
        }

        ReloadPagesTree();
        ReloadActivePageTabAfterPathChange(reloadActiveTab);
        if (!string.IsNullOrWhiteSpace(selectAfter))
        {
            if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, selectAfter))
                SelectPageTreeNodeSilently(selectAfter);
            else
                SelectPageByFolder(selectAfter);
        }

        TxtStatus.Text = $"PDF metadata applied: {renamed} renamed, {scaled} scaled, {failed} failed.";
        return new PdfMetadataApplySummary(renamed, scaled, failed);
    }

    private static PdfSheetMetadata CreateManualSheetMetadata(PageInfo page) =>
        new()
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Source = "manual",
            PdfPath = page.PdfPath,
            PageIndex = page.PdfPage,
            PageNumber = page.PdfPage + 1,
            SheetLabel = page.Name,
            RenameCandidate = page.Name,
            Confidence = "manual",
        };

    private bool TryApplySheetManagerScale(
        PageInfo page,
        PdfSheetMetadata metadata,
        string scaleText,
        bool clear,
        out double scaleMetersPerPt)
    {
        scaleMetersPerPt = 0;
        string cleanScale = (scaleText ?? "").Trim();
        if (!clear &&
            (string.IsNullOrWhiteSpace(cleanScale) ||
             string.Equals(cleanScale, "skip", StringComparison.OrdinalIgnoreCase) ||
             !PdfSheetMetadataService.TryParseScaleMetersPerPt(cleanScale, out scaleMetersPerPt)))
        {
            return false;
        }

        if (!ApplyMetadataScaleToPage(page, scaleMetersPerPt, clear))
            return false;

        string displayScale = clear
            ? ""
            : PdfSheetMetadataPolicy.AppliedScaleText(cleanScale, scaleMetersPerPt);
        metadata.SkipScale = clear;
        metadata.SkipReason = clear ? "manual-review: scale cleared" : "";
        metadata.SelectedScaleText = string.IsNullOrWhiteSpace(displayScale) ? cleanScale : displayScale;
        metadata.ScaleText = metadata.SelectedScaleText;
        metadata.SelectedScaleRatio = scaleMetersPerPt > 0
            ? scaleMetersPerPt / ViewportConstants.PdfPointMeters
            : 0;
        metadata.SelectedScaleMetersPerPt = scaleMetersPerPt;
        return true;
    }

    private static string LearningTextOutcome(bool reviewed, string detected, string final) =>
        !reviewed
            ? "not_reviewed"
            : string.Equals(
                PdfSheetMetadataService.VisibleSheetDisplayName(detected),
                PdfSheetMetadataService.VisibleSheetDisplayName(final),
                StringComparison.OrdinalIgnoreCase)
                ? "accepted"
                : "corrected";

    private static string LearningScaleOutcome(
        bool reviewed,
        double detected,
        double final) =>
        !reviewed
            ? "not_reviewed"
            : PdfSheetMetadataPolicy.SameScale(detected, final)
                ? "accepted"
                : "corrected";


    private IEnumerable<PdfMetadataPreviewRow> BuildPdfMetadataPreviewRows(
        IReadOnlyList<PdfMetadataPageResult> results,
        bool defaultRename,
        bool defaultScale,
        IReadOnlyDictionary<string, IReadOnlyList<int>>? readyRasterDpisByPageFolder = null,
        bool highConfidenceOnly = false)
    {
        SheetMetadataConfig config = SheetMetadataRulesService.Active;
        bool legacyDefaults = config.ImportPolicy == SheetMetadataImportPolicy.LegacyAutoApply &&
                              !highConfidenceOnly;
        var candidates = results
            .Where(result => result.Ok && result.Metadata != null)
            .Select(result => new
            {
                Result = result,
                Metadata = result.Metadata!,
                ProposedName = PdfSheetMetadataPolicy.BuildSafeProposedPageName(
                    result.Page.Name,
                    result.Metadata!,
                    config,
                    PdfSheetMetadataPolicy.IsExistingNameMarkedManual(result.Page)),
            })
            .ToList();
        var duplicateNames = candidates
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ProposedName))
            .GroupBy(entry => NormalizePathForCompare(entry.ProposedName), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in candidates)
        {
            PdfMetadataPageResult result = entry.Result;
            PdfSheetMetadata metadata = entry.Metadata;
            string proposedName = PdfSheetMetadataService.VisibleSheetDisplayName(entry.ProposedName);
            bool duplicateName = duplicateNames.Contains(NormalizePathForCompare(proposedName));
            if (duplicateName)
            {
                metadata.Warnings.Add("duplicate sheet number; hidden folder/source identity will disambiguate");
            }
            bool canRename = !string.IsNullOrWhiteSpace(proposedName) &&
                             !string.Equals(
                                 proposedName,
                                 PdfSheetMetadataService.VisibleSheetDisplayName(result.Page.Name),
                                 StringComparison.OrdinalIgnoreCase);
            bool nameConflict = HasPageNameConflict(result.Page.FolderPath, proposedName);
            bool canScale = metadata.CanApplyScale();
            bool exactScaleSet = PdfSheetMetadataPolicy.IsExactScaleOverrideSet(metadata);
            bool exactScaleClear = PdfSheetMetadataPolicy.IsExactScaleOverrideClear(metadata);
            bool renameMeetsPolicy = PdfSheetMetadataPolicy.CanAutoRename(
                result.Page,
                metadata,
                config,
                proposedName);
            bool scaleMeetsPolicy = exactScaleSet || exactScaleClear || (legacyDefaults
                ? canScale && (!config.PreserveExistingManualScale || result.Page.ScaleMetersPerPt <= 0)
                : PdfSheetMetadataPolicy.IsExactScaleAutoApplyCandidate(result.Page, metadata, config));
            if (highConfidenceOnly)
            {
                renameMeetsPolicy = renameMeetsPolicy &&
                                    PdfSheetMetadataPolicy.IsTrustedHighConfidenceRename(metadata);
                scaleMeetsPolicy = scaleMeetsPolicy &&
                                   PdfSheetMetadataPolicy.IsTrustedHighConfidenceScale(metadata);
            }
            SmartSheetLearningSignal learning = SmartLearningStore.BuildSheetMetadataSignal(metadata);
            bool learnedConflict = string.Equals(learning.Confidence, "learned-conflict", StringComparison.OrdinalIgnoreCase);
            var warnings = metadata.Warnings.ToList();
            if (nameConflict)
                warnings.Add("same page name allowed; folder path will be uniqued");
            if (!string.IsNullOrWhiteSpace(learning.Warning))
                warnings.Add(learning.Warning);
            if (defaultScale && canScale && !scaleMeetsPolicy)
                warnings.Add(result.Page.ScaleMetersPerPt > 0 && config.PreserveExistingManualScale
                    ? "existing scale preserved; choose Set explicitly to replace it"
                    : "scale requires explicit review; only high-confidence title-block/index scales are preselected");

            string displayedSuffix = PdfSheetMetadataPolicy.ExtractSuffix(
                proposedName,
                metadata.EffectiveSheetKey);

            yield return new PdfMetadataPreviewRow
            {
                PageFolder = result.Page.FolderPath,
                CurrentPageName = PdfSheetMetadataService.VisibleSheetDisplayName(result.Page.Name),
                CurrentScale = PdfSheetMetadataPolicy.ScaleDisplay(result.Page.ScaleMetersPerPt),
                SheetLabel = metadata.SheetLabel,
                SheetTitle = metadata.SheetTitle,
                ProposedPageName = proposedName,
                Suffix = displayedSuffix,
                ProposedScale = canScale
                    ? PdfSheetMetadataPolicy.ReviewScaleText(metadata)
                    : metadata.SkipScale ? "skip" : "",
                Source = metadata.Source,
                Confidence = learning.Confidence,
                TitleSource = metadata.TitleSource,
                TitleConfidence = metadata.TitleConfidence,
                TitleEvidence = metadata.TitleEvidence,
                SuffixSource = metadata.SuffixSource,
                SuffixConfidence = metadata.SuffixConfidence,
                SuffixEvidence = metadata.SuffixEvidence,
                ScaleSource = metadata.ScaleSource,
                ScaleConfidence = metadata.ScaleConfidence,
                ScaleEvidence = string.IsNullOrWhiteSpace(metadata.ScaleEvidence)
                    ? metadata.SkipReason
                    : metadata.ScaleEvidence,
                CanSetScale = canScale,
                ReviewScale = defaultScale,
                RasterStatus = RasterSheetCacheService.DisplayStatus(result.Page, readyRasterDpisByPageFolder),
                Reason = PdfMetadataDecisionReason(metadata, learning, canRename, canScale, nameConflict, learnedConflict),
                Warnings = string.Join("; ", warnings),
                ApplyRename = defaultRename && canRename && renameMeetsPolicy &&
                              (!learnedConflict ||
                               PdfSheetMetadataPolicy.IsExactRenameOverride(metadata) ||
                               PdfSheetMetadataPolicy.IsExactSuffixOverride(metadata)),
                ScaleAction = defaultScale && scaleMeetsPolicy &&
                              (!learnedConflict || exactScaleSet || exactScaleClear)
                    ? exactScaleClear ? PdfMetadataScaleAction.Clear : PdfMetadataScaleAction.Set
                    : PdfMetadataScaleAction.Keep,
            };
        }
    }

    private static string PdfMetadataDecisionReason(
        PdfSheetMetadata metadata,
        SmartSheetLearningSignal learning,
        bool canRename,
        bool canScale,
        bool nameConflict,
        bool learnedConflict)
    {
        var parts = new List<string>();
        string key = metadata.EffectiveSheetKey;
        if (canRename)
        {
            string suffix = string.IsNullOrWhiteSpace(metadata.Suffix) ? "no suffix" : $"suffix {metadata.Suffix}";
            parts.Add($"name from {metadata.Source}: {metadata.SheetLabel} / {key} / {suffix}");
            if (nameConflict)
                parts.Add("duplicate page name allowed; folder path will be unique");
        }
        else if (nameConflict)
        {
            parts.Add("rename unchanged; same page name is allowed when needed");
        }
        else
        {
            parts.Add("rename unchanged");
        }

        if (metadata.SkipScale)
            parts.Add("scale skipped by metadata");
        else if (canScale)
            parts.Add($"scale from '{metadata.EffectiveScaleText}'");
        else
            parts.Add("no usable scale detected");

        if (learnedConflict)
            parts.Add("learned-rule conflict blocks auto apply");
        else if (learning.SupportingRecords > 0)
            parts.Add($"learning support {learning.SupportingRecords}, conflicts {learning.ConflictingRecords}");

        return string.Join(" | ", parts);
    }

    private static bool HasPageNameConflict(string pageFolder, string proposedName)
    {
        if (string.IsNullOrWhiteSpace(proposedName))
            return false;

        string parent = Path.GetDirectoryName(pageFolder) ?? "";
        if (string.IsNullOrWhiteSpace(parent))
            return false;

        string target = Path.Combine(parent, OurPlanCoreJobStore.SanitizeName(proposedName, 120));
        return !string.Equals(NormalizePath(pageFolder), NormalizePath(target), StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(target);
    }

    private string BuildPdfMetadataSummary(IReadOnlyList<PdfMetadataPageResult> results, bool includeApplyPreview)
    {
        var sb = new StringBuilder();
        int okCount = results.Count(result => result.Ok);
        int failCount = results.Count - okCount;
        sb.AppendLine($"Pages: {results.Count}, OK: {okCount}, Failed: {failCount}");
        sb.AppendLine();

        foreach (var result in results.Take(30))
        {
            if (!result.Ok || result.Metadata == null)
            {
                sb.AppendLine($"- {result.Page.Name}: failed - {result.Error}");
                continue;
            }

            PdfSheetMetadata metadata = result.Metadata;
            string proposed = metadata.ProposedPageName();
            string scale = metadata.CanApplyScale()
                ? metadata.EffectiveScaleText
                : metadata.SkipScale
                    ? "skip scale"
                    : "no scale";
            string warnings = metadata.Warnings.Count > 0
                ? $" [{string.Join("; ", metadata.Warnings.Take(2))}]"
                : "";
            SmartSheetLearningSignal learning = SmartLearningStore.BuildSheetMetadataSignal(metadata);
            string reason = PdfMetadataDecisionReason(
                metadata,
                learning,
                canRename: !string.IsNullOrWhiteSpace(proposed) &&
                           !string.Equals(proposed, result.Page.Name, StringComparison.OrdinalIgnoreCase),
                canScale: metadata.CanApplyScale(),
                nameConflict: HasPageNameConflict(result.Page.FolderPath, proposed),
                learnedConflict: string.Equals(learning.Confidence, "learned-conflict", StringComparison.OrdinalIgnoreCase));

            sb.AppendLine(includeApplyPreview
                ? $"- {result.Page.Name} -> {proposed}; {scale}; {reason}{warnings}"
                : $"- {result.Page.Name}: {metadata.SheetLabel} {metadata.SheetTitle}; {proposed}; {scale}; {reason}{warnings}");
        }

        if (results.Count > 30)
            sb.AppendLine($"...and {results.Count - 30} more page(s).");

        return sb.ToString().TrimEnd();
    }

    private IReadOnlyList<PageInfo> GetPagesForMetadata(TreeViewItem item)
    {
        if (_currentJob == null)
            return [];

        var paths = new List<string>();
        if (IsRootPagesNode(item))
        {
            paths.Add(_currentJob.PagesRoot);
        }
        else
        {
            var entries = GetSelectedPageEntries(item);
            if (entries.Count > 0)
                paths.AddRange(entries.Select(entry => entry.SourcePath));
            else if (GetPagesNodePath(item) is { } path)
                paths.Add(path);
        }

        return paths
            .SelectMany(CollectPagesUnder)
            .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(page => page.FolderPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void OpenSourcePdfMetadata(string pageFolder)
    {
        string path = OurPlanCoreJobStore.SourcePdfMetadataPath(pageFolder);
        if (!File.Exists(path))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }
}
