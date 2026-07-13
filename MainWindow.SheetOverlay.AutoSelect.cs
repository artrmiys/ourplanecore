using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private const int MaxSheetOverlayAutoSelectCandidates = 160;

    private async void AutoSelectAndFitSheetOverlay(
        PageInfo page,
        bool replaceExistingOverlay,
        bool skipCurrentOverlay = false)
    {
        if (!RequireModule(ModuleId.SheetOverlay, "Auto Fit Sheet Overlay"))
            return;

        PageInfo? targetPage = OurPlanCoreJobStore.TryReadPage(page.FolderPath);
        if (targetPage == null)
        {
            TxtStatus.Text = "Overlay auto select: sheet source is missing.";
            return;
        }

        OurPlanCoreJob? job = _currentJob;
        if (job == null)
        {
            TxtStatus.Text = "Overlay auto select: open a job before searching sheets.";
            return;
        }

        TxtStatus.Text = skipCurrentOverlay
            ? "Overlay auto select: trying the next matching sheet..."
            : replaceExistingOverlay
            ? "Overlay auto select: reselecting the best matching sheet..."
            : "Overlay auto select: searching job sheets for matching plan geometry...";

        try
        {
            SheetOverlayAutoFitCandidateSearch search = await System.Threading.Tasks.Task.Run(() =>
                FindSheetOverlayAutoFitCandidate(
                    job,
                    targetPage,
                    skipCurrentOverlay ? targetPage.OverlayPageFolder : ""));
            if (!search.Ok)
            {
                TxtStatus.Text = search.Error;
                return;
            }

            ApplySheetOverlayAutoSelectedFit(targetPage, search, replaceExistingOverlay, skipCurrentOverlay);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Sheet overlay auto select failed for {targetPage.Name}");
            TxtStatus.Text = $"Overlay auto select failed: {ex.Message}";
        }
    }

    private async void ChooseSheetOverlayAutoSelectCandidate(PageInfo page)
    {
        if (!RequireModule(ModuleId.SheetOverlay, "Choose Sheet Overlay"))
            return;

        PageInfo? targetPage = OurPlanCoreJobStore.TryReadPage(page.FolderPath);
        if (targetPage == null)
        {
            TxtStatus.Text = "Overlay candidate chooser: sheet source is missing.";
            return;
        }

        OurPlanCoreJob? job = _currentJob;
        if (job == null)
        {
            TxtStatus.Text = "Overlay candidate chooser: open a job before searching sheets.";
            return;
        }

        TxtStatus.Text = "Overlay candidate chooser: ranking matching plan geometry...";
        try
        {
            SheetOverlayAutoFitCandidateSearch search = await System.Threading.Tasks.Task.Run(() =>
                FindSheetOverlayAutoFitCandidate(job, targetPage, includeReviewCandidates: true));
            if (!search.Ok)
            {
                TxtStatus.Text = search.Error;
                return;
            }

            var dialog = new SheetOverlayCandidateDialog(
                targetPage.Name,
                search.TopMatches,
                targetPage.OverlayPageFolder)
            {
                Owner = this,
            };
            if (dialog.ShowDialog() != true || dialog.SelectedMatch == null)
            {
                TxtStatus.Text = "Overlay candidate selection cancelled.";
                return;
            }

            if (!TrySelectSheetOverlayAutoFitCandidateSearch(
                    search,
                    dialog.SelectedMatch,
                    out SheetOverlayAutoFitCandidateSearch selectedSearch,
                    out string error))
            {
                TxtStatus.Text = error;
                return;
            }

            ApplySheetOverlayAutoSelectedFit(
                targetPage,
                selectedSearch,
                replaceExistingOverlay: true,
                statusLabel: "Selected overlay candidate");
            HandleSheetOverlayCandidatePostAction(targetPage, selectedSearch, dialog.SelectedAction);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Sheet overlay candidate chooser failed for {targetPage.Name}");
            TxtStatus.Text = $"Overlay candidate chooser failed: {ex.Message}";
        }
    }

    private void HandleSheetOverlayCandidatePostAction(
        PageInfo targetPage,
        SheetOverlayAutoFitCandidateSearch selectedSearch,
        SheetOverlayCandidateAction action)
    {
        if (action == SheetOverlayCandidateAction.UseSelected || selectedSearch.OverlayPage == null)
            return;

        PageInfo latestTarget = OurPlanCoreJobStore.TryReadPage(targetPage.FolderPath) ?? targetPage;
        if (!SameFolder(latestTarget.OverlayPageFolder, selectedSearch.OverlayPage.FolderPath))
            return;

        if (action == SheetOverlayCandidateAction.OpenTargetSheet)
        {
            OpenSheetOverlayTarget(latestTarget);
            return;
        }

        if (action == SheetOverlayCandidateAction.OpenOverlaySheet)
        {
            OpenSheetOverlaySource(latestTarget);
            return;
        }

        if (action == SheetOverlayCandidateAction.EditTransform)
        {
            EditSheetOverlayTransform(latestTarget);
            return;
        }

        if (action == SheetOverlayCandidateAction.EditByPoints)
            BeginSheetOverlayPointEditWhenReady(latestTarget);
    }

    private void OpenSheetOverlayTarget(PageInfo page)
    {
        PageInfo latest = ReadLatestSheetOverlayPage(page);
        if (string.IsNullOrWhiteSpace(latest.OverlayPageFolder))
        {
            TxtStatus.Text = "Set a sheet overlay before reviewing it.";
            return;
        }

        OpenPageInActiveTab(latest);
        TxtStatus.Text = $"Opened target sheet with overlay: {latest.Name}.";
    }

    private void ApplySheetOverlayAutoSelectedFit(
        PageInfo targetPage,
        SheetOverlayAutoFitCandidateSearch search,
        bool replaceExistingOverlay = false,
        bool skipCurrentOverlay = false,
        string statusLabel = "")
    {
        if (search.OverlayPage == null || search.Read == null)
        {
            TxtStatus.Text = search.Error;
            return;
        }

        PageInfo? latestTarget = OurPlanCoreJobStore.TryReadPage(targetPage.FolderPath);
        if (latestTarget == null)
        {
            TxtStatus.Text = "Overlay auto fit skipped: target sheet changed while searching.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(latestTarget.OverlayPageFolder) &&
            !SameFolder(latestTarget.OverlayPageFolder, search.OverlayPage.FolderPath) &&
            !replaceExistingOverlay)
        {
            TxtStatus.Text = "Overlay auto fit skipped: overlay changed while searching.";
            return;
        }

        if (replaceExistingOverlay &&
            !string.IsNullOrWhiteSpace(latestTarget.OverlayPageFolder) &&
            !SameFolder(latestTarget.OverlayPageFolder, search.OverlayPage.FolderPath))
        {
            ClearReciprocalSheetOverlay(latestTarget);
        }

        OurPlanCoreJobStore.SavePageOverlay(
            latestTarget.FolderPath,
            search.OverlayPage.FolderPath,
            SheetOverlaySaveColor(latestTarget),
            SheetOverlaySaveOpacity(latestTarget));
        OurPlanCoreJobStore.SavePageOverlayVisibility(latestTarget.FolderPath, true);

        PageInfo selectedTarget = OurPlanCoreJobStore.TryReadPage(latestTarget.FolderPath) ?? latestTarget;
        ClearReciprocalSheetOverlay(selectedTarget);
        string alternatives = BuildSheetOverlayAutoSelectAlternativesSummary(search.TopMatches);
        AppLog.Info(
            $"Sheet overlay auto select chose overlay; base='{selectedTarget.FolderPath}'; overlay='{search.OverlayPage.FolderPath}'; " +
            $"candidates={search.ComparableCount}/{search.CandidateCount}; confidence={search.Fit.Confidence:0.###}; " +
            $"matched={search.Fit.MatchedSamples}/{search.Fit.SampleCount}; method='{search.Fit.Method}'; alternatives='{alternatives}'");
        ApplySheetOverlayAutoFitResult(
            selectedTarget,
            search.OverlayPage,
            search.Read,
            search.Fit,
            $"{(string.IsNullOrWhiteSpace(statusLabel) ? skipCurrentOverlay ? "Next overlay candidate" : "Auto-selected overlay" : statusLabel)}: {search.OverlayPage.Name} " +
            $"({search.ComparableCount}/{search.CandidateCount} sheets compared; {alternatives}). ");
    }

    private static SheetOverlayAutoFitCandidateSearch FindSheetOverlayAutoFitCandidate(
        OurPlanCoreJob job,
        PageInfo targetPage,
        string nextAfterOverlayFolder = "",
        bool includeReviewCandidates = false)
    {
        SheetOverlayAutoFitSnapRead baseRead = ReadSheetOverlayAutoFitCandidateSnap(targetPage);
        if (!baseRead.Ok)
        {
            return SheetOverlayAutoFitCandidateSearch.Failed(
                $"Overlay auto fit: base sheet geometry unavailable. {baseRead.Error}");
        }

        List<SheetOverlayAutoFitCandidatePage> pages = BuildSheetOverlayAutoFitCandidatePages(
            job,
            targetPage).ToList();
        var inputs = new List<SheetOverlayAutoFitCandidateInput>();
        var readsByFolder = new Dictionary<string, SheetOverlayAutoFitSnapRead>(StringComparer.OrdinalIgnoreCase);
        int tried = 0;

        foreach (SheetOverlayAutoFitCandidatePage candidate in pages)
        {
            tried++;
            SheetOverlayAutoFitSnapRead overlayRead = ReadSheetOverlayAutoFitCandidateSnap(candidate.Page);
            if (!overlayRead.Ok)
                continue;

            string key = SheetOverlayFolderKey(candidate.Page.FolderPath);
            readsByFolder[key] = overlayRead;
            inputs.Add(new SheetOverlayAutoFitCandidateInput(
                candidate.Page,
                overlayRead.Snap,
                overlayRead.Source,
                candidate.SearchRank));
        }

        if (inputs.Count == 0)
        {
            return SheetOverlayAutoFitCandidateSearch.Failed(
                string.IsNullOrWhiteSpace(nextAfterOverlayFolder)
                    ? $"Overlay auto fit: no comparable sheet geometry found in {tried} candidate sheets."
                    : $"Overlay auto fit: no alternate comparable sheet geometry found in {tried} candidate sheets.");
        }

        IReadOnlyList<SheetOverlayAutoFitCandidateMatch> topMatches;
        bool ranked = includeReviewCandidates
            ? SheetOverlayAutoFitCandidateSearchService.TryRankReviewCandidates(baseRead.Snap, inputs, out topMatches)
            : SheetOverlayAutoFitCandidateSearchService.TryFindBest(
                baseRead.Snap,
                inputs,
                out _,
                out topMatches);
        if (!ranked)
        {
            return SheetOverlayAutoFitCandidateSearch.Failed(
                string.IsNullOrWhiteSpace(nextAfterOverlayFolder)
                    ? includeReviewCandidates
                        ? $"Overlay auto fit: no reviewable similar sheet matched {inputs.Count}/{tried} candidate sheets."
                        : $"Overlay auto fit: no similar sheet matched {inputs.Count}/{tried} candidate sheets closely enough."
                    : $"Overlay auto fit: no alternate similar sheet matched {inputs.Count}/{tried} candidate sheets closely enough.");
        }

        SheetOverlayAutoFitCandidateMatch match = topMatches[0];

        if (!string.IsNullOrWhiteSpace(nextAfterOverlayFolder) &&
            !SheetOverlayAutoFitCandidateSearchService.TrySelectNextMatch(topMatches, nextAfterOverlayFolder, out match))
        {
            return SheetOverlayAutoFitCandidateSearch.Failed(
                $"Overlay auto fit: no alternate similar sheet matched {inputs.Count}/{tried} candidate sheets closely enough.");
        }

        if (!readsByFolder.TryGetValue(SheetOverlayFolderKey(match.Page.FolderPath), out SheetOverlayAutoFitSnapRead? selectedRead) ||
            selectedRead == null)
        {
            return SheetOverlayAutoFitCandidateSearch.Failed(
                "Overlay auto fit: selected sheet geometry changed while ranking candidates.");
        }

        return SheetOverlayAutoFitCandidateSearch.Success(
            match.Page,
            SheetOverlayAutoFitReadResult.Success(baseRead, selectedRead),
            match.Fit,
            topMatches,
            inputs.Count,
            tried,
            baseRead,
            new Dictionary<string, SheetOverlayAutoFitSnapRead>(readsByFolder, StringComparer.OrdinalIgnoreCase));
    }

    private static bool TrySelectSheetOverlayAutoFitCandidateSearch(
        SheetOverlayAutoFitCandidateSearch search,
        SheetOverlayAutoFitCandidateMatch match,
        out SheetOverlayAutoFitCandidateSearch selected,
        out string error)
    {
        selected = search;
        error = "";
        if (search.BaseRead == null)
        {
            error = "Overlay candidate chooser: base sheet geometry is no longer available.";
            return false;
        }

        if (!search.CandidateReads.TryGetValue(
                SheetOverlayFolderKey(match.Page.FolderPath),
                out SheetOverlayAutoFitSnapRead? selectedRead) ||
            selectedRead == null)
        {
            error = "Overlay candidate chooser: selected sheet geometry changed while reviewing candidates.";
            return false;
        }

        selected = search with
        {
            OverlayPage = match.Page,
            Read = SheetOverlayAutoFitReadResult.Success(search.BaseRead, selectedRead),
            Fit = match.Fit,
        };
        return true;
    }

    private static SheetOverlayAutoFitSnapRead ReadSheetOverlayAutoFitCandidateSnap(PageInfo page)
    {
        SheetOverlayAutoFitSnapRead read = ReadSheetOverlayAutoFitSnap(page);
        if (read.Ok)
            return read;

        SheetOverlayAutoFitSnapRead rasterRead = ReadSheetOverlayAutoFitRasterSnap(page);
        return rasterRead.Ok ? rasterRead : read;
    }

    private static IEnumerable<SheetOverlayAutoFitCandidatePage> BuildSheetOverlayAutoFitCandidatePages(
        OurPlanCoreJob job,
        PageInfo targetPage)
    {
        List<PageInfo> pages = CollectSheetOverlayAutoFitPages(job.PagesRoot).ToList();
        int targetIndex = pages.FindIndex(page => SameFolder(page.FolderPath, targetPage.FolderPath));

        return pages
            .Select((page, index) => new SheetOverlayAutoFitCandidatePage(
                page,
                BuildSheetOverlayAutoFitSearchRank(targetPage, targetIndex, page, index)))
            .Where(candidate => !SameFolder(candidate.Page.FolderPath, targetPage.FolderPath))
            .OrderBy(candidate => candidate.SearchRank)
            .ThenBy(candidate => candidate.Page.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSheetOverlayAutoSelectCandidates);
    }

    private static IEnumerable<PageInfo> CollectSheetOverlayAutoFitPages(string folder)
    {
        if (!Directory.Exists(folder))
            yield break;

        if (OurPlanCoreJobStore.TryReadPage(folder) is { } page)
        {
            yield return page;
            yield break;
        }

        foreach (string child in OurPlanCoreJobStore.GetOrderedChildDirectories(folder))
        {
            foreach (PageInfo childPage in CollectSheetOverlayAutoFitPages(child))
                yield return childPage;
        }
    }

    private static int BuildSheetOverlayAutoFitSearchRank(
        PageInfo targetPage,
        int targetIndex,
        PageInfo candidatePage,
        int candidateIndex)
    {
        string targetParent = Path.GetDirectoryName(targetPage.FolderPath) ?? "";
        string candidateParent = Path.GetDirectoryName(candidatePage.FolderPath) ?? "";
        int parentPenalty = SamePath(targetParent, candidateParent) ? 0 : 10_000;
        int pdfPenalty = SamePath(targetPage.PdfPath, candidatePage.PdfPath) ? 0 : 2_000;
        int indexPenalty = targetIndex >= 0
            ? Math.Abs(candidateIndex - targetIndex)
            : candidateIndex;

        return parentPenalty + pdfPenalty + Math.Min(indexPenalty, 999);
    }

    private static string SheetOverlaySaveColor(PageInfo page) =>
        string.IsNullOrWhiteSpace(page.OverlayColor)
            ? DefaultSheetOverlayColor
            : page.OverlayColor;

    private static double SheetOverlaySaveOpacity(PageInfo page) =>
        EffectiveSheetOverlayOpacity(page.OverlayOpacity);

    private static string SheetOverlayFolderKey(string folder)
    {
        try
        {
            return Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return folder;
        }
    }

    private static bool SamePath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            string normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string BuildSheetOverlayAutoSelectAlternativesSummary(
        IReadOnlyList<SheetOverlayAutoFitCandidateMatch> topMatches)
    {
        IReadOnlyList<string> rows = topMatches
            .Take(3)
            .Select(match => $"{match.Page.Name} {match.Fit.Confidence * 100:0}%")
            .ToList();

        if (rows.Count == 0)
            return "no ranked alternatives";

        string summary = $"top matches: {string.Join(", ", rows)}";
        return HasCloseSheetOverlayAutoSelectAlternative(topMatches)
            ? $"{summary}; close alternative needs review"
            : summary;
    }

    private static bool HasCloseSheetOverlayAutoSelectAlternative(
        IReadOnlyList<SheetOverlayAutoFitCandidateMatch> topMatches) =>
        topMatches.Count > 1 &&
        topMatches[0].Fit.Confidence - topMatches[1].Fit.Confidence <= 0.05;

    private sealed record SheetOverlayAutoFitCandidateSearch(
        bool Ok,
        PageInfo? OverlayPage,
        SheetOverlayAutoFitReadResult? Read,
        SheetOverlayAutoFitResult Fit,
        string Error,
        IReadOnlyList<SheetOverlayAutoFitCandidateMatch> TopMatches,
        int ComparableCount,
        int CandidateCount,
        SheetOverlayAutoFitSnapRead? BaseRead,
        IReadOnlyDictionary<string, SheetOverlayAutoFitSnapRead> CandidateReads)
    {
        public static SheetOverlayAutoFitCandidateSearch Success(
            PageInfo overlayPage,
            SheetOverlayAutoFitReadResult read,
            SheetOverlayAutoFitResult fit,
            IReadOnlyList<SheetOverlayAutoFitCandidateMatch> topMatches,
            int comparableCount,
            int candidateCount,
            SheetOverlayAutoFitSnapRead baseRead,
            IReadOnlyDictionary<string, SheetOverlayAutoFitSnapRead> candidateReads) =>
            new(true, overlayPage, read, fit, "", topMatches, comparableCount, candidateCount, baseRead, candidateReads);

        public static SheetOverlayAutoFitCandidateSearch Failed(string error) =>
            new(false, null, null, FailedFit(error), error, [], 0, 0, null, new Dictionary<string, SheetOverlayAutoFitSnapRead>());
    }

    private sealed record SheetOverlayAutoFitCandidatePage(PageInfo Page, int SearchRank);
}
