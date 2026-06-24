using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OurPlaneCore;

public partial class MainWindow
{
    private const int MaxSheetOverlayAutoSelectCandidates = 48;

    private async void AutoSelectAndFitSheetOverlay(
        PageInfo page,
        bool replaceExistingOverlay,
        bool skipCurrentOverlay = false)
    {
        PageInfo? targetPage = OurPlaneCoreJobStore.TryReadPage(page.FolderPath);
        if (targetPage == null)
        {
            TxtStatus.Text = "Overlay auto select: sheet source is missing.";
            return;
        }

        OurPlaneCoreJob? job = _currentJob;
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

    private void ApplySheetOverlayAutoSelectedFit(
        PageInfo targetPage,
        SheetOverlayAutoFitCandidateSearch search,
        bool replaceExistingOverlay = false,
        bool skipCurrentOverlay = false)
    {
        if (search.OverlayPage == null || search.Read == null)
        {
            TxtStatus.Text = search.Error;
            return;
        }

        PageInfo? latestTarget = OurPlaneCoreJobStore.TryReadPage(targetPage.FolderPath);
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

        OurPlaneCoreJobStore.SavePageOverlay(
            latestTarget.FolderPath,
            search.OverlayPage.FolderPath,
            SheetOverlaySaveColor(latestTarget),
            SheetOverlaySaveOpacity(latestTarget));
        OurPlaneCoreJobStore.SavePageOverlayVisibility(latestTarget.FolderPath, true);

        PageInfo selectedTarget = OurPlaneCoreJobStore.TryReadPage(latestTarget.FolderPath) ?? latestTarget;
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
            $"{(skipCurrentOverlay ? "Next overlay candidate" : "Auto-selected overlay")}: {search.OverlayPage.Name} " +
            $"({search.ComparableCount}/{search.CandidateCount} sheets compared; {alternatives}). ");
    }

    private static SheetOverlayAutoFitCandidateSearch FindSheetOverlayAutoFitCandidate(
        OurPlaneCoreJob job,
        PageInfo targetPage,
        string excludedOverlayFolder = "")
    {
        SheetOverlayAutoFitSnapRead baseRead = ReadSheetOverlayAutoFitCandidateSnap(targetPage);
        if (!baseRead.Ok)
        {
            return SheetOverlayAutoFitCandidateSearch.Failed(
                $"Overlay auto fit: base sheet geometry unavailable. {baseRead.Error}");
        }

        List<SheetOverlayAutoFitCandidatePage> pages = BuildSheetOverlayAutoFitCandidatePages(
            job,
            targetPage,
            excludedOverlayFolder).ToList();
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
                string.IsNullOrWhiteSpace(excludedOverlayFolder)
                    ? $"Overlay auto fit: no comparable sheet geometry found in {tried} candidate sheets."
                    : $"Overlay auto fit: no alternate comparable sheet geometry found in {tried} candidate sheets.");
        }

        if (!SheetOverlayAutoFitCandidateSearchService.TryFindBest(
                baseRead.Snap,
                inputs,
                out SheetOverlayAutoFitCandidateMatch match,
                out IReadOnlyList<SheetOverlayAutoFitCandidateMatch> topMatches))
        {
            return SheetOverlayAutoFitCandidateSearch.Failed(
                string.IsNullOrWhiteSpace(excludedOverlayFolder)
                    ? $"Overlay auto fit: no similar sheet matched {inputs.Count}/{tried} candidate sheets closely enough."
                    : $"Overlay auto fit: no alternate similar sheet matched {inputs.Count}/{tried} candidate sheets closely enough.");
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
            tried);
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
        OurPlaneCoreJob job,
        PageInfo targetPage,
        string excludedOverlayFolder = "")
    {
        List<PageInfo> pages = CollectSheetOverlayAutoFitPages(job.PagesRoot).ToList();
        int targetIndex = pages.FindIndex(page => SameFolder(page.FolderPath, targetPage.FolderPath));

        return pages
            .Select((page, index) => new SheetOverlayAutoFitCandidatePage(
                page,
                BuildSheetOverlayAutoFitSearchRank(targetPage, targetIndex, page, index)))
            .Where(candidate => !SameFolder(candidate.Page.FolderPath, targetPage.FolderPath))
            .Where(candidate => string.IsNullOrWhiteSpace(excludedOverlayFolder) ||
                                !SameFolder(candidate.Page.FolderPath, excludedOverlayFolder))
            .OrderBy(candidate => candidate.SearchRank)
            .ThenBy(candidate => candidate.Page.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSheetOverlayAutoSelectCandidates);
    }

    private static IEnumerable<PageInfo> CollectSheetOverlayAutoFitPages(string folder)
    {
        if (!Directory.Exists(folder))
            yield break;

        if (OurPlaneCoreJobStore.TryReadPage(folder) is { } page)
        {
            yield return page;
            yield break;
        }

        foreach (string child in OurPlaneCoreJobStore.GetOrderedChildDirectories(folder))
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

        return rows.Count == 0
            ? "no ranked alternatives"
            : $"top matches: {string.Join(", ", rows)}";
    }

    private sealed record SheetOverlayAutoFitCandidateSearch(
        bool Ok,
        PageInfo? OverlayPage,
        SheetOverlayAutoFitReadResult? Read,
        SheetOverlayAutoFitResult Fit,
        string Error,
        IReadOnlyList<SheetOverlayAutoFitCandidateMatch> TopMatches,
        int ComparableCount,
        int CandidateCount)
    {
        public static SheetOverlayAutoFitCandidateSearch Success(
            PageInfo overlayPage,
            SheetOverlayAutoFitReadResult read,
            SheetOverlayAutoFitResult fit,
            IReadOnlyList<SheetOverlayAutoFitCandidateMatch> topMatches,
            int comparableCount,
            int candidateCount) =>
            new(true, overlayPage, read, fit, "", topMatches, comparableCount, candidateCount);

        public static SheetOverlayAutoFitCandidateSearch Failed(string error) =>
            new(false, null, null, FailedFit(error), error, [], 0, 0);
    }

    private sealed record SheetOverlayAutoFitCandidatePage(PageInfo Page, int SearchRank);
}
