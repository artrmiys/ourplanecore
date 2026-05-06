using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void BtnAutoPageFolders_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Auto Page Folders",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string baseFolder = CurrentPagesFolderTarget();
        AutoCreatePageFolders(baseFolder);
    }

    private void AutoCreatePageFolders(string baseFolder)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
            return;

        string mode = ResolveFolderTemplateMode();
        string modeLabel = FolderTemplateModeLabel(mode);
        string preview = PlanSwiftFolderTemplateService.PreviewNames(
            PlanSwiftFolderTemplateService.PageFolderNames(mode));
        string baseName = OurPlaneCoreJobStore.DisplayName(baseFolder);
        var confirm = MessageBox.Show(
            $"Create standard {modeLabel} page folders under '{baseName}'?\n\n{preview}\n\nExisting folders will be skipped.",
            "Auto Page Folders",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            FolderTemplateResult result = PlanSwiftFolderTemplateService.CreatePageFolders(baseFolder, mode);
            ReloadPagesTree(baseFolder);
            TxtStatus.Text = $"Page folders ({modeLabel}): created {result.Created}, skipped {result.Skipped}, errors {result.Errors}.";
            ShowFolderTemplateErrors("Auto Page Folders", result);
        }
        catch (Exception ex)
        {
            ShowOperationError("Auto Page Folders", ex);
        }
    }

    private void BtnSortPagesArchStruct_Click(object sender, RoutedEventArgs e)
    {
        SortPagesIntoArchStruct();
    }

    private void BtnSortPagesSuffix_Click(object sender, RoutedEventArgs e)
    {
        SortPagesBySuffix();
    }

    private void BtnRepairMeasurementPageLinks_Click(object sender, RoutedEventArgs e)
    {
        RepairMeasurementPageLinks();
    }

    private void RepairMeasurementPageLinks()
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Repair Measurement Links",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int repaired = RepairMeasurementPageFolderReferences();
        _lastMeasurementPageFolderRepairCount = repaired;
        _viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
        RefreshPagesTakeoffIndicators();
        RefreshEstimateTable();
        RefreshAllTotals();
        TxtStatus.Text = BuildMeasurementRepairStatus(
            repaired > 0
                ? "Repair Links completed"
                : "Repair Links: all resolvable measurement page links already match current pages");
    }

    private void SortPagesIntoArchStruct()
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Sort A/S Pages",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            string imported = OurPlaneCoreJobStore.EnsureFolder(_currentJob.PagesRoot, "00. imported");
            string arch = OurPlaneCoreJobStore.EnsureFolder(imported, "Arch");
            string struc = OurPlaneCoreJobStore.EnsureFolder(imported, "Struct");
            string others = OurPlaneCoreJobStore.EnsureFolder(_currentJob.PagesRoot, "--------others");

            IReadOnlyList<PageInfo> pages = CollectPagesUnder(_currentJob.PagesRoot)
                .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            int movedArch = 0;
            int movedStruct = 0;
            int movedOthers = 0;
            int skipped = 0;
            bool reloadActiveTab = false;
            string? selectAfter = null;

            foreach (PageInfo page in pages)
            {
                string target = ClassifyArchStructPageTarget(page, arch, struc, others);
                if (string.IsNullOrWhiteSpace(target))
                {
                    skipped++;
                    continue;
                }

                string parent = Path.GetDirectoryName(page.FolderPath) ?? "";
                if (string.Equals(parent, target, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                string oldPath = page.FolderPath;
                string movedPath = OurPlaneCoreJobStore.MoveNode(oldPath, target);
                reloadActiveTab = UpdatePageReferencesForMovedPath(oldPath, movedPath) || reloadActiveTab;
                selectAfter ??= movedPath;

                if (string.Equals(target, arch, StringComparison.OrdinalIgnoreCase))
                    movedArch++;
                else if (string.Equals(target, struc, StringComparison.OrdinalIgnoreCase))
                    movedStruct++;
                else
                    movedOthers++;
            }

            OurPlaneCoreJobStore.SortChildren(arch, descending: false);
            OurPlaneCoreJobStore.SortChildren(struc, descending: false);
            OurPlaneCoreJobStore.SortChildren(others, descending: false);
            ReloadPagesTree(selectAfter ?? imported);
            ReloadActivePageTabAfterPathChange(reloadActiveTab);
            TxtStatus.Text = $"Sort A/S: Arch {movedArch}, Struct {movedStruct}, Others {movedOthers}, skipped {skipped}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort A/S Pages", ex);
        }
    }

    private static string ClassifyArchStructPageTarget(PageInfo page, string arch, string struc, string others)
    {
        string name = (page.Name ?? "").Trim();
        if (name.EndsWith("-", StringComparison.Ordinal))
            return others;

        char first = name.FirstOrDefault(char.IsLetter);
        if (first == 'A' || first == 'a')
            return arch;
        if (first == 'S' || first == 's')
            return struc;

        string sourceName = Path.GetFileName(page.PdfPath);
        if (sourceName.Contains("struct", StringComparison.OrdinalIgnoreCase))
            return struc;
        if (sourceName.Contains("arch", StringComparison.OrdinalIgnoreCase))
            return arch;

        return "";
    }

    private void SortPagesBySuffix()
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Sort D/Sec/WT Pages",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            string detailsStruct = EnsurePagesRootFolder("details struct");
            string detailsArch = EnsurePagesRootFolder("details arch");
            string units = EnsurePagesRootFolder("units");
            string sections = EnsurePagesRootFolder("sections");

            IReadOnlyList<PageInfo> pages = CollectPagesUnder(_currentJob.PagesRoot)
                .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            int movedTop = 0;
            int movedDetailsStruct = 0;
            int movedDetailsArch = 0;
            int movedUnits = 0;
            int movedSections = 0;
            int skipped = 0;
            bool reloadActiveTab = false;
            string? selectAfter = null;

            foreach (PageInfo page in pages)
            {
                string target = ClassifySuffixPageTarget(page, detailsStruct, detailsArch, units, sections);
                if (string.IsNullOrWhiteSpace(target))
                {
                    skipped++;
                    continue;
                }

                string parent = Path.GetDirectoryName(page.FolderPath) ?? "";
                if (string.Equals(parent, target, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                string oldPath = page.FolderPath;
                string movedPath = OurPlaneCoreJobStore.MoveNode(oldPath, target);
                reloadActiveTab = UpdatePageReferencesForMovedPath(oldPath, movedPath) || reloadActiveTab;
                selectAfter ??= movedPath;

                if (string.Equals(target, _currentJob.PagesRoot, StringComparison.OrdinalIgnoreCase))
                    movedTop++;
                else if (string.Equals(target, detailsStruct, StringComparison.OrdinalIgnoreCase))
                    movedDetailsStruct++;
                else if (string.Equals(target, detailsArch, StringComparison.OrdinalIgnoreCase))
                    movedDetailsArch++;
                else if (string.Equals(target, units, StringComparison.OrdinalIgnoreCase))
                    movedUnits++;
                else if (string.Equals(target, sections, StringComparison.OrdinalIgnoreCase))
                    movedSections++;
            }

            OurPlaneCoreJobStore.SortChildren(detailsStruct, descending: false);
            OurPlaneCoreJobStore.SortChildren(detailsArch, descending: false);
            OurPlaneCoreJobStore.SortChildren(units, descending: false);
            OurPlaneCoreJobStore.SortChildren(sections, descending: false);
            int reorderedTop = ReorderRootSuffixPagesToTop(_currentJob.PagesRoot);

            ReloadPagesTree(selectAfter ?? _currentJob.PagesRoot);
            ReloadActivePageTabAfterPathChange(reloadActiveTab);
            TxtStatus.Text =
                $"Sort D/Sec/WT: top {movedTop}, details struct {movedDetailsStruct}, details arch {movedDetailsArch}, " +
                $"units {movedUnits}, sections {movedSections}, reordered {reorderedTop}, skipped {skipped}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort D/Sec/WT Pages", ex);
        }
    }

    private string EnsurePagesRootFolder(string displayName)
    {
        if (_currentJob == null)
            return "";

        foreach (string child in OurPlaneCoreJobStore.GetOrderedChildDirectories(_currentJob.PagesRoot))
        {
            if (!OurPlaneCoreJobStore.IsPageFolder(child) &&
                string.Equals(OurPlaneCoreJobStore.DisplayName(child), displayName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return OurPlaneCoreJobStore.EnsureFolder(_currentJob.PagesRoot, displayName);
    }

    private string ClassifySuffixPageTarget(
        PageInfo page,
        string detailsStruct,
        string detailsArch,
        string units,
        string sections)
    {
        if (_currentJob == null)
            return "";

        (string suffix, char first) = DetectPageSuffixSortInfo(page);
        if (PageSuffixTopOrder.Contains(suffix, StringComparer.OrdinalIgnoreCase))
            return _currentJob.PagesRoot;
        if (string.Equals(suffix, "d", StringComparison.OrdinalIgnoreCase) && first == 's')
            return detailsStruct;
        if (string.Equals(suffix, "d", StringComparison.OrdinalIgnoreCase) && first == 'a')
            return detailsArch;
        if (string.Equals(suffix, "u", StringComparison.OrdinalIgnoreCase))
            return units;
        if (string.Equals(suffix, "sec", StringComparison.OrdinalIgnoreCase))
            return sections;
        return "";
    }

    private static (string Suffix, char First) DetectPageSuffixSortInfo(PageInfo page)
    {
        string suffix = AutoSortSuffixFromName(page.Name);
        char first = AutoSortFirstLetter(page.Name);
        PdfSheetMetadata? metadata = null;

        if (string.IsNullOrWhiteSpace(suffix) || first is not ('a' or 's'))
        {
            metadata = OurPlaneCoreJobStore.ReadSourcePdfMetadata(page.FolderPath);
        }

        if (string.IsNullOrWhiteSpace(suffix) && !string.IsNullOrWhiteSpace(metadata?.Suffix))
            suffix = metadata.Suffix.Trim().ToLowerInvariant();

        if (first is not ('a' or 's') && metadata != null)
        {
            string metadataName = $"{metadata.SheetLabel} {metadata.EffectiveSheetKey}";
            first = AutoSortFirstLetter(metadataName);
        }

        return (suffix, first);
    }

    private int ReorderRootSuffixPagesToTop(string pagesRoot)
    {
        var children = OurPlaneCoreJobStore.GetOrderedChildDirectories(pagesRoot).ToList();
        var topPages = new List<string>();
        foreach (string suffix in PageSuffixTopOrder)
        {
            topPages.AddRange(children.Where(child =>
                OurPlaneCoreJobStore.TryReadPage(child) is { } childPage &&
                string.Equals(DetectPageSuffixSortInfo(childPage).Suffix, suffix, StringComparison.OrdinalIgnoreCase)));
        }

        if (topPages.Count == 0)
            return 0;

        var topSet = topPages
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ordered = topPages
            .Concat(children.Where(child => !topSet.Contains(NormalizePath(child))))
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
            OurPlaneCoreJobStore.SetOrderIndex(ordered[i], i);
        return topPages.Count;
    }

    private static char AutoSortFirstLetter(string name)
    {
        foreach (char ch in (name ?? "").Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                return ch;
        }
        return '\0';
    }

    private static string AutoSortSuffixFromName(string name)
    {
        string raw = (name ?? "").Trim().ToLowerInvariant().TrimEnd(' ', '.', '_', '-');
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        string tokenText = Regex.Replace(raw, @"[\s._-]+", " ").Trim();
        foreach (string suffix in PageSuffixDetectionOrder)
        {
            if (Regex.IsMatch(tokenText, $@"(?:^| ){Regex.Escape(suffix)}$"))
                return suffix;
        }

        string compact = Regex.Replace(raw, @"[\s._-]+", "");
        foreach (string suffix in PageSuffixDetectionOrder)
        {
            if (!compact.EndsWith(suffix, StringComparison.Ordinal))
                continue;

            int previousIndex = compact.Length - suffix.Length - 1;
            char previous = previousIndex >= 0 ? compact[previousIndex] : '\0';
            if (previous == '\0' || char.IsDigit(previous))
                return suffix;
        }

        return "";
    }

    private string CurrentPagesFolderTarget()
    {
        if (_currentJob == null)
            return "";

        if (PagesTree.SelectedItem is TreeViewItem { Tag: PageFolderNode folder })
            return folder.FolderPath;

        if (PagesTree.SelectedItem is TreeViewItem { Tag: PageInfo page })
            return Path.GetDirectoryName(page.FolderPath) ?? _currentJob.PagesRoot;

        return _currentJob.PagesRoot;
    }

    private string ResolveFolderTemplateMode() =>
        _currentJob == null
            ? NormalizeFolderTemplateMode(_settings.FolderTemplateMode) switch
            {
                "EWP" => "EWP",
                _ => "COM",
            }
            : PlanSwiftFolderTemplateService.ResolveMode(_currentJob, _settings.FolderTemplateMode);

    private string FolderTemplateModeLabel(string resolvedMode)
    {
        string requested = NormalizeFolderTemplateMode(_settings.FolderTemplateMode);
        return requested == "AUTO" ? $"Auto -> {resolvedMode}" : requested;
    }

    private static string NormalizeFolderTemplateMode(string? mode) =>
        (mode ?? "AUTO").Trim().ToUpperInvariant() switch
        {
            "COM" => "COM",
            "EWP" => "EWP",
            _ => "AUTO",
        };
}
