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
using OurPlaneCore.Controls;

namespace OurPlaneCore;

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

        MessageBox.Show("No PDF pages found.", title, MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private async Task AnalyzePdfMetadataAsync(TreeViewItem item, bool applyRename, bool applyScale)
    {
        if (_currentJob == null)
            return;

        var pages = GetPagesForMetadata(item).ToList();
        if (pages.Count == 0)
        {
            MessageBox.Show("No PDF pages found in this selection.", "PDF Metadata",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveCurrentPageScale();
        TxtStatus.Text = $"Analyzing PDF metadata for {pages.Count} page(s)...";

        OurPlaneCoreJob job = _currentJob;
        List<PdfMetadataPageResult> results;
        using (ShowBusyOverlay($"Analyzing PDF metadata for {pages.Count} page(s)..."))
        {
            await WaitForBusyOverlayRenderAsync();
            results = await Task.Run(() =>
            {
                var analyzed = new List<PdfMetadataPageResult>();
                foreach (PageInfo page in pages)
                {
                    if (PdfSheetMetadataService.TryAnalyzeAndSave(job, page, out var metadata, out string error))
                        analyzed.Add(new PdfMetadataPageResult(page, true, metadata, ""));
                    else
                        analyzed.Add(new PdfMetadataPageResult(page, false, null, error));
                }

                return analyzed;
            });
        }

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

        ApplyPdfMetadataResults(job, results, dialog.Rows);
    }

    private PdfMetadataApplySummary ApplyPdfMetadataResults(
        OurPlaneCoreJob job,
        IReadOnlyList<PdfMetadataPageResult> results,
        IReadOnlyList<PdfMetadataPreviewRow> rows)
    {
        int renamed = 0;
        int scaled = 0;
        int failed = 0;
        string? selectAfter = null;
        var resultsByFolder = results
            .Where(result => result.Ok && result.Metadata != null)
            .GroupBy(result => NormalizePath(result.Page.FolderPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (PdfMetadataPreviewRow row in rows.Where(row => row.ApplyRename || row.ApplyScale))
        {
            if (string.IsNullOrWhiteSpace(row.PageFolder))
            {
                failed++;
                continue;
            }

            resultsByFolder.TryGetValue(NormalizePath(row.PageFolder), out PdfMetadataPageResult? result);
            PageInfo? sourcePage = result?.Page ?? OurPlaneCoreJobStore.TryReadPage(row.PageFolder);
            if (sourcePage == null)
            {
                failed++;
                continue;
            }

            PdfSheetMetadata metadata = result?.Metadata
                ?? OurPlaneCoreJobStore.ReadSourcePdfMetadata(sourcePage.FolderPath)
                ?? CreateManualSheetMetadata(sourcePage);

            string currentPath = sourcePage.FolderPath;
            string finalName = OurPlaneCoreJobStore.DisplayName(currentPath);
            double finalScale = sourcePage.ScaleMetersPerPt;

            try
            {
                if (row.ApplyScale)
                {
                    if (TryApplySheetManagerScale(currentPath, metadata, row.ProposedScale, out finalScale))
                    {
                        scaled++;
                    }
                    else
                    {
                        failed++;
                        metadata.Warnings.Add($"scale not applied: '{row.ProposedScale}'");
                    }
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
                        string renamedPath = OurPlaneCoreJobStore.RenamePageAllowDuplicateName(currentPath, proposedName);
                        currentPath = renamedPath;
                        finalName = OurPlaneCoreJobStore.DisplayName(renamedPath);
                        renamed++;
                    }
                }

                metadata.GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(metadata.Source))
                    metadata.Source = "manual";

                OurPlaneCoreJobStore.WriteSourcePdfMetadata(currentPath, metadata);
                if (OurPlaneCoreJobStore.TryReadPage(currentPath) is { } finalPage)
                {
                    var finalDecision = PdfSheetMetadataService.FinalDecision(finalPage, metadata, finalName, finalScale);
                    string outcome = (row.ApplyRename && !string.Equals(row.ProposedPageName, finalName, StringComparison.OrdinalIgnoreCase))
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
                            finalDecision));
                }

                selectAfter ??= currentPath;
            }
            catch (Exception ex)
            {
                failed++;
                SmartLearningStore.AppendSheetFeedback(
                    job,
                    sourcePage,
                    PdfSheetMetadataService.BuildLearningRecord(
                        sourcePage,
                        metadata,
                        "failed_apply",
                        ex.Message));
            }
        }

        _currentPage = null;
        _currentPdfPath = "";
        ReloadPagesTree(selectAfter ?? _currentJob?.PagesRoot);
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

    private static bool TryApplySheetManagerScale(
        string pageFolder,
        PdfSheetMetadata metadata,
        string scaleText,
        out double scaleMetersPerPt)
    {
        scaleMetersPerPt = 0;
        string cleanScale = (scaleText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cleanScale) ||
            string.Equals(cleanScale, "skip", StringComparison.OrdinalIgnoreCase) ||
            !PdfSheetMetadataService.TryParseScaleMetersPerPt(cleanScale, out scaleMetersPerPt))
        {
            return false;
        }

        string displayScale = PdfSheetMetadataService.FormatImperialScale(scaleMetersPerPt);
        metadata.SkipScale = false;
        metadata.SelectedScaleText = string.IsNullOrWhiteSpace(displayScale) ? cleanScale : displayScale;
        metadata.ScaleText = metadata.SelectedScaleText;
        metadata.SelectedScaleRatio = scaleMetersPerPt / ViewportConstants.PdfPointMeters;
        metadata.SelectedScaleMetersPerPt = scaleMetersPerPt;
        OurPlaneCoreJobStore.SavePageScale(pageFolder, scaleMetersPerPt);
        return true;
    }

    private IEnumerable<PdfMetadataPreviewRow> BuildPdfMetadataPreviewRows(
        IReadOnlyList<PdfMetadataPageResult> results,
        bool defaultRename,
        bool defaultScale)
    {
        foreach (var result in results.Where(result => result.Ok && result.Metadata != null))
        {
            PdfSheetMetadata metadata = result.Metadata!;
            string proposedName = metadata.ProposedPageName();
            bool canRename = !string.IsNullOrWhiteSpace(proposedName) &&
                             !string.Equals(proposedName, result.Page.Name, StringComparison.OrdinalIgnoreCase);
            bool nameConflict = HasPageNameConflict(result.Page.FolderPath, proposedName);
            bool canScale = metadata.CanApplyScale();
            SmartSheetLearningSignal learning = SmartLearningStore.BuildSheetMetadataSignal(metadata);
            bool learnedConflict = string.Equals(learning.Confidence, "learned-conflict", StringComparison.OrdinalIgnoreCase);
            var warnings = metadata.Warnings.ToList();
            if (nameConflict)
                warnings.Add("same page name allowed; folder path will be uniqued");
            if (!string.IsNullOrWhiteSpace(learning.Warning))
                warnings.Add(learning.Warning);

            yield return new PdfMetadataPreviewRow
            {
                PageFolder = result.Page.FolderPath,
                CurrentPageName = result.Page.Name,
                SheetLabel = metadata.SheetLabel,
                SheetTitle = metadata.SheetTitle,
                ProposedPageName = proposedName,
                Suffix = metadata.Suffix,
                ProposedScale = canScale
                    ? PdfSheetMetadataService.FormatImperialScale(metadata.SelectedScaleMetersPerPt)
                    : metadata.SkipScale ? "skip" : "",
                Source = metadata.Source,
                Confidence = learning.Confidence,
                Reason = PdfMetadataDecisionReason(metadata, learning, canRename, canScale, nameConflict, learnedConflict),
                Warnings = string.Join("; ", warnings),
                ApplyRename = defaultRename && canRename && !learnedConflict,
                ApplyScale = defaultScale && canScale && !learnedConflict,
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

        string target = Path.Combine(parent, OurPlaneCoreJobStore.SanitizeName(proposedName, 120));
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
        string path = OurPlaneCoreJobStore.SourcePdfMetadataPath(pageFolder);
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
