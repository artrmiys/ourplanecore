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
            PostStatusInfo("Open or create a job before creating page folders.");
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

    private void SortPagesAlphabetically(bool descending)
    {
        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before sorting pages.");
            return;
        }

        try
        {
            string scopeFolder = CurrentPagesFolderTarget();
            if (string.IsNullOrWhiteSpace(scopeFolder) ||
                !Directory.Exists(scopeFolder) ||
                !IsPathInsidePagesRoot(scopeFolder))
            {
                scopeFolder = _currentJob.PagesRoot;
            }

            OurPlaneCoreJobStore.SortChildren(scopeFolder, descending);
            ReloadPagesTree(scopeFolder);
            string scopeLabel = string.Equals(
                NormalizePath(scopeFolder),
                NormalizePath(_currentJob.PagesRoot),
                StringComparison.OrdinalIgnoreCase)
                    ? "Pages root"
                    : OurPlaneCoreJobStore.DisplayName(scopeFolder);
            TxtStatus.Text = $"Sorted {scopeLabel} {(descending ? "Z-A" : "A-Z")}.";
        }
        catch (Exception ex)
        {
            ShowOperationError(descending ? "Sort Pages Z-A" : "Sort Pages A-Z", ex);
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
            PostStatusInfo("Open or create a job before repairing measurement links.");
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
            PostStatusInfo("Open or create a job before sorting A/S pages.");
            return;
        }

        try
        {
            string imported = OurPlaneCoreJobStore.EnsureFolder(_currentJob.PagesRoot, "00. imported");
            string arch = OurPlaneCoreJobStore.EnsureFolder(imported, "Arch");
            string struc = OurPlaneCoreJobStore.EnsureFolder(imported, "Struct");
            string others = OurPlaneCoreJobStore.EnsureFolder(_currentJob.PagesRoot, "--------others");
            SortPagesIntoArchStructScope(
                _currentJob.PagesRoot,
                arch,
                struc,
                others,
                imported,
                "");
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort A/S Pages", ex);
        }
    }

    private void SortPagesIntoArchStruct(string scopeFolder)
    {
        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before sorting A/S pages.");
            return;
        }

        if (string.IsNullOrWhiteSpace(scopeFolder) ||
            !Directory.Exists(scopeFolder) ||
            !IsPathInsidePagesRoot(scopeFolder))
        {
            return;
        }

        try
        {
            string arch = EnsurePagesChildFolder(scopeFolder, "Arch");
            string struc = EnsurePagesChildFolder(scopeFolder, "Struct");
            string others = EnsurePagesChildFolder(scopeFolder, "--------others");
            string scopeLabel = $" in {OurPlaneCoreJobStore.DisplayName(scopeFolder)}";
            SortPagesIntoArchStructScope(
                scopeFolder,
                arch,
                struc,
                others,
                scopeFolder,
                scopeLabel);
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort A/S Pages", ex);
        }
    }

    private void SortPagesIntoArchStructScope(
        string scopeFolder,
        string arch,
        string struc,
        string others,
        string fallbackSelection,
        string statusScopeLabel)
    {
        IReadOnlyList<PageInfo> pages = CollectPagesUnder(scopeFolder)
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
        ReloadPagesTree(selectAfter ?? fallbackSelection);
        ReloadActivePageTabAfterPathChange(reloadActiveTab);
        TxtStatus.Text = $"Sort A/S{statusScopeLabel}: Arch {movedArch}, Struct {movedStruct}, Others {movedOthers}, skipped {skipped}.";
    }

    private static string ClassifyArchStructPageTarget(PageInfo page, string arch, string struc, string others)
    {
        PageSortConfig cfg = PageSortRulesService.Active;
        string name = (page.Name ?? "").Trim();
        if (cfg.ArchStructDashToOthers && name.EndsWith("-", StringComparison.Ordinal))
            return others;

        string Map(string target) => target switch
        {
            "Struct" => struc,
            "Others" => others,
            _ => arch,
        };

        char first = char.ToLowerInvariant(name.FirstOrDefault(char.IsLetter));
        foreach (ArchStructRule rule in cfg.ArchStructRules)
        {
            if (!string.Equals(rule.Kind, "FirstLetter", StringComparison.OrdinalIgnoreCase))
                continue;
            string m = (rule.Match ?? "").Trim();
            if (m.Length > 0 && char.ToLowerInvariant(m[0]) == first && first != '\0')
                return Map(rule.Target);
        }

        string sourceName = Path.GetFileName(page.PdfPath);
        foreach (ArchStructRule rule in cfg.ArchStructRules)
        {
            if (!string.Equals(rule.Kind, "FileKeyword", StringComparison.OrdinalIgnoreCase))
                continue;
            string m = (rule.Match ?? "").Trim();
            if (m.Length > 0 && sourceName.Contains(m, StringComparison.OrdinalIgnoreCase))
                return Map(rule.Target);
        }

        return "";
    }

    private void SortPagesBySuffix()
    {
        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before sorting D/Sec/WT pages.");
            return;
        }

        try
        {
            string scopeFolder = CurrentSelectedPagesFolderOrRoot(out bool scopedToSelectedFolder);
            SortPagesBySuffix(scopeFolder, scopedToSelectedFolder);
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort D/Sec/WT Pages", ex);
        }
    }

    private void SortPagesBySuffix(string scopeFolder)
    {
        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before sorting D/Sec/WT pages.");
            return;
        }

        if (string.IsNullOrWhiteSpace(scopeFolder) ||
            !Directory.Exists(scopeFolder) ||
            !IsPathInsidePagesRoot(scopeFolder))
        {
            return;
        }

        try
        {
            SortPagesBySuffix(scopeFolder, scopedToSelectedFolder: true);
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort D/Sec/WT Pages", ex);
        }
    }

    private void SortPagesBySuffix(string scopeFolder, bool scopedToSelectedFolder)
    {
        PageSortConfig cfg = PageSortRulesService.Active;

        // Pre-create each distinct destination folder referenced by the rules
        // (defaults reproduce the old "details struct / arch, units, sections").
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string EnsureChild(string name)
        {
            if (targets.TryGetValue(name, out string? p) && !string.IsNullOrEmpty(p))
                return p;
            string ensured = EnsurePagesChildFolder(scopeFolder, name);
            targets[name] = ensured;
            return ensured;
        }

        foreach (string name in cfg.SuffixTargetFolderNames())
            EnsureChild(name);

        IReadOnlyList<PageInfo> pages = CollectPagesUnder(scopeFolder)
            .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        int movedTop = 0;
        var movedByFolder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        bool reloadActiveTab = false;
        string? selectAfter = null;

        foreach (PageInfo page in pages)
        {
            string target = ClassifySuffixPageTarget(page, scopeFolder, EnsureChild);
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

            if (string.Equals(target, scopeFolder, StringComparison.OrdinalIgnoreCase))
                movedTop++;
            else
                movedByFolder[target] = movedByFolder.GetValueOrDefault(target) + 1;
        }

        foreach (string folder in targets.Values.Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.OrdinalIgnoreCase))
            OurPlaneCoreJobStore.SortChildren(folder, descending: false);
        int reorderedTop = ReorderSuffixPagesToTop(scopeFolder);

        ReloadPagesTree(selectAfter ?? scopeFolder);
        ReloadActivePageTabAfterPathChange(reloadActiveTab);
        string scopeLabel = scopedToSelectedFolder
            ? $" in {OurPlaneCoreJobStore.DisplayName(scopeFolder)}"
            : "";
        string perFolder = string.Join(", ", movedByFolder
            .OrderBy(kv => OurPlaneCoreJobStore.DisplayName(kv.Key), StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{OurPlaneCoreJobStore.DisplayName(kv.Key)} {kv.Value}"));
        TxtStatus.Text =
            $"Sort D/Sec/WT{scopeLabel}: top {movedTop}" +
            (perFolder.Length > 0 ? $", {perFolder}" : "") +
            $", reordered {reorderedTop}, skipped {skipped}.";
    }

    private string CurrentSelectedPagesFolderOrRoot(out bool scopedToSelectedFolder)
    {
        scopedToSelectedFolder = false;
        if (_currentJob == null)
            return "";

        if (PagesTree.SelectedItem is TreeViewItem { Tag: PageFolderNode folder } &&
            !string.IsNullOrWhiteSpace(folder.FolderPath) &&
            IsPathInsidePagesRoot(folder.FolderPath))
        {
            scopedToSelectedFolder = true;
            return folder.FolderPath;
        }

        return _currentJob.PagesRoot;
    }

    private string EnsurePagesChildFolder(string parentFolder, string displayName)
    {
        if (_currentJob == null ||
            string.IsNullOrWhiteSpace(parentFolder) ||
            !Directory.Exists(parentFolder) ||
            !IsPathInsidePagesRoot(parentFolder))
        {
            return "";
        }

        foreach (string child in OurPlaneCoreJobStore.GetOrderedChildDirectories(parentFolder))
        {
            if (!OurPlaneCoreJobStore.IsPageFolder(child) &&
                string.Equals(OurPlaneCoreJobStore.DisplayName(child), displayName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return OurPlaneCoreJobStore.EnsureFolder(parentFolder, displayName);
    }

    private string ClassifySuffixPageTarget(
        PageInfo page,
        string topFolder,
        Func<string, string> ensureChild)
    {
        if (_currentJob == null)
            return "";

        PageSortConfig cfg = PageSortRulesService.Active;
        (string suffix, char first) = DetectPageSuffixSortInfo(page);
        if (cfg.SuffixTopOrder.Contains(suffix, StringComparer.OrdinalIgnoreCase))
            return topFolder;

        foreach (SuffixRule rule in cfg.SuffixRules)
        {
            if (!string.Equals(rule.Suffix, suffix, StringComparison.OrdinalIgnoreCase))
                continue;
            string fl = (rule.FirstLetter ?? "").Trim();
            if (fl.Length > 0 && char.ToLowerInvariant(fl[0]) != first)
                continue;
            string target = (rule.Target ?? "").Trim();
            if (target.Length == 0)
                continue;
            return string.Equals(target, "top", StringComparison.OrdinalIgnoreCase)
                ? topFolder
                : ensureChild(target);
        }

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

    private int ReorderSuffixPagesToTop(string parentFolder)
    {
        var children = OurPlaneCoreJobStore.GetOrderedChildDirectories(parentFolder).ToList();
        var topPages = new List<string>();
        foreach (string suffix in PageSortRulesService.Active.SuffixTopOrder)
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
        foreach (string suffix in PageSortRulesService.Active.SuffixDetectionOrder)
        {
            if (Regex.IsMatch(tokenText, $@"(?:^| ){Regex.Escape(suffix)}$"))
                return suffix;
        }

        string compact = Regex.Replace(raw, @"[\s._-]+", "");
        foreach (string suffix in PageSortRulesService.Active.SuffixDetectionOrder)
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
