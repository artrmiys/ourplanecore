using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    // ── Pages tree ────────────────────────────────────────────────────────────

    private void ReloadPagesTree(string? selectPath = null)
    {
        PagesTree.Items.Clear();
        _pageTakeoffMultiSelection.Clear();
        _pageTakeoffRangeAnchorKey = null;
        if (_currentJob == null)
        {
            _expandedPageTreePaths.Clear();
            return;
        }

        FillPagesTree(PagesTree.Items, _currentJob.PagesRoot);
        RefreshPagesTakeoffIndicators();
        RestoreExpandedTreeState(PagesTree, _expandedPageTreePaths, GetPagesNodePath);

        if (!string.IsNullOrWhiteSpace(selectPath))
            SelectNodeByFolder(selectPath);
        PrunePagesMultiSelection();
        ApplyPagesMultiSelectionVisuals();
    }

    private void FillPagesTree(ItemCollection items, string folder)
    {
        if (!Directory.Exists(folder)) return;

        foreach (string dir in OurPlaneCoreJobStore.GetOrderedChildDirectories(folder))
        {
            PageInfo? page = OurPlaneCoreJobStore.TryReadPage(dir);
            if (page != null)
            {
                var pageItem = new TreeViewItem
                {
                    Header = BuildPageHeader(page),
                    Tag = page,
                    IsExpanded = false,
                };
                RebuildPageTakeoffNodes(pageItem, page);
                items.Add(pageItem);
                continue;
            }

            string name = OurPlaneCoreJobStore.ReadName(dir) ?? Path.GetFileName(dir);
            var folderNode = new PageFolderNode { Name = name, FolderPath = dir };
            var tvi = new TreeViewItem
            {
                Header = $"📁 {name}",
                Tag = folderNode,
                IsExpanded = false,
            };
            items.Add(tvi);
            FillPagesTree(tvi.Items, dir);
        }
    }

    private TreeViewItem CreateHiddenPagesRootItem() =>
        new()
        {
            Header = "Pages",
            Tag = new PageFolderNode
            {
                Name = "Pages",
                FolderPath = _currentJob?.PagesRoot ?? "",
                IsRoot = true,
            },
            IsExpanded = true,
        };

    private StackPanel BuildPageHeader(PageInfo page)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = $"  {page.Name}",
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (page.ScaleMetersPerPt <= 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "  unscaled",
                Foreground = Brushes.Firebrick,
                FontSize = 10,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        return panel;
    }

    private void SelectPageTreeNodeSilently(string pageFolder)
    {
        _syncingPageTreeSelection = true;
        try
        {
            SelectNodeByFolder(pageFolder);
        }
        finally
        {
            _syncingPageTreeSelection = false;
        }
    }

    private bool IsPageMeasuredByActiveTakeoff(TreeViewItem item) =>
        _activeItem != null &&
        item.Tag is PageInfo page &&
        _activeItem.Measurements.Any(m =>
            IsSamePageFolder(m.PageFolder, page.FolderPath));

    private bool IsActivePageTakeoffNode(TreeViewItem item) =>
        item.Tag is PageTakeoffNode node &&
        IsActivePageTakeoff(node.Page, node.Takeoff);

    private bool IsActivePageTakeoff(PageInfo page, TakeoffItem takeoff) =>
        _activeItem != null &&
        string.Equals(_activeItem.FolderPath, takeoff.FolderPath, StringComparison.OrdinalIgnoreCase) &&
        takeoff.Measurements.Any(measurement => IsSamePageFolder(measurement.PageFolder, page.FolderPath));

    private void RefreshPagesTakeoffIndicators()
    {
        foreach (TreeViewItem item in EnumeratePageTreeItems().ToList())
        {
            if (item.Tag is PageInfo page)
            {
                bool wasExpanded = item.IsExpanded;
                item.Header = BuildPageHeader(page);
                RebuildPageTakeoffNodes(item, page);
                item.IsExpanded = wasExpanded;
            }
        }
        ApplyPagesMultiSelectionVisuals();
    }

    private void PagesTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingPageTreeSelection)
            return;

        if (e.NewValue is TreeViewItem { Tag: PageInfo page })
        {
            OpenPageInActiveTab(page);
        }
        else if (e.NewValue is TreeViewItem { Tag: PageTakeoffNode node })
        {
            SelectLinkedPageTakeoff(node);
        }
        else if (e.NewValue is TreeViewItem { Tag: PageOverlayNode overlay })
        {
            TxtStatus.Text = $"Sheet overlay on {overlay.Page.Name}: {overlay.OverlayName}.";
        }
    }

    private string GetSelectedImportFolder()
    {
        if (_currentJob == null)
            throw new InvalidOperationException("No job is open.");

        if (PagesTree.SelectedItem is TreeViewItem tvi)
        {
            if (tvi.Tag is PageFolderNode folder)
                return folder.FolderPath;
            if (tvi.Tag is PageInfo page)
                return Path.GetDirectoryName(page.FolderPath) ?? _currentJob.PagesRoot;
        }

        return OurPlaneCoreJobStore.DefaultImportFolder(_currentJob);
    }

    private void SelectNodeByFolder(string folderPath)
    {
        WithTreeExpansionTrackingSuppressed(() =>
        {
            foreach (TreeViewItem item in PagesTree.Items)
            {
                if (SelectNodeByFolder(item, folderPath))
                    return;
            }
        });
    }

    private TreeViewItem? FindPageTreeItemByFolder(string folderPath)
    {
        foreach (TreeViewItem item in EnumeratePageTreeItems())
        {
            string? itemPath = GetPagesNodePath(item);
            if (itemPath != null && IsSamePageFolder(itemPath, folderPath))
                return item;
        }

        return null;
    }

    private static bool SelectNodeByFolder(TreeViewItem item, string folderPath)
    {
        string? itemPath = GetPagesNodePath(item);
        if (itemPath != null &&
            IsSamePageFolder(itemPath, folderPath))
        {
            item.IsSelected = true;
            item.BringIntoView();
            return true;
        }

        foreach (TreeViewItem child in item.Items)
        {
            if (SelectNodeByFolder(child, folderPath))
            {
                item.IsExpanded = true;
                return true;
            }
        }

        return false;
    }

    private void SelectPageByFolder(string folderPath)
    {
        WithTreeExpansionTrackingSuppressed(() =>
        {
            foreach (TreeViewItem item in PagesTree.Items)
            {
                if (SelectPageByFolder(item, folderPath))
                    return;
            }
        });
    }

    private static bool SelectPageByFolder(TreeViewItem item, string folderPath)
    {
        if (item.Tag is PageInfo page &&
            IsSamePageFolder(page.FolderPath, folderPath))
        {
            item.IsSelected = true;
            item.BringIntoView();
            return true;
        }

        foreach (TreeViewItem child in item.Items)
        {
            if (SelectPageByFolder(child, folderPath))
            {
                item.IsExpanded = true;
                return true;
            }
        }

        return false;
    }

    private void PagesTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { } item)
            return;

        if (item.Tag is PageTakeoffNode pageTakeoff)
        {
            string key = PageTakeoffSelectionKey(pageTakeoff);
            if (!_pageTakeoffMultiSelection.Contains(key))
            {
                _pageTakeoffMultiSelection.Clear();
                _pageTakeoffMultiSelection.Add(key);
                _pageTakeoffRangeAnchorKey = key;
                _pagesMultiSelection.Clear();
                ApplyPagesMultiSelectionVisuals();
            }

            OpenPagesTreeContextMenu(item, BuildPageTakeoffContextMenu(pageTakeoff));
            e.Handled = true;
            return;
        }

        if (item.Tag is PageOverlayNode pageOverlay)
        {
            _pagesMultiSelection.Clear();
            _pageTakeoffMultiSelection.Clear();
            ApplyPagesMultiSelectionVisuals();
            OpenPagesTreeContextMenu(item, BuildPageOverlayContextMenu(pageOverlay));
            e.Handled = true;
            return;
        }

        string? path = GetPagesNodePath(item);
        if (path == null)
        {
            _pagesMultiSelection.Clear();
            _pageTakeoffMultiSelection.Clear();
            ApplyPagesMultiSelectionVisuals();
        }
        else if (!_pagesMultiSelection.Contains(path))
        {
            _pagesMultiSelection.Clear();
            _pageTakeoffMultiSelection.Clear();
            if (!IsRootPagesNode(item))
                _pagesMultiSelection.Add(path);
            _pagesRangeAnchorPath = path;
            ApplyPagesMultiSelectionVisuals();
        }

        OpenPagesTreeContextMenu(item, BuildPagesContextMenu(item));
        e.Handled = true;
    }

    private void OpenPagesTreeContextMenu(TreeViewItem item, ContextMenu menu)
    {
        _syncingPageTreeSelection = true;
        try
        {
            item.Focus();
            item.IsSelected = true;
        }
        finally
        {
            _syncingPageTreeSelection = false;
        }

        item.ContextMenu = menu;
        menu.PlacementTarget = item;
        menu.IsOpen = true;
    }

    private void SelectPagesTreeItemSilently(TreeViewItem item)
    {
        _syncingPageTreeSelection = true;
        try
        {
            item.IsSelected = true;
        }
        finally
        {
            _syncingPageTreeSelection = false;
        }
    }

    private void PagesTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pagesDragStart = e.GetPosition(PagesTree);
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { } item)
            return;

        if (item.Tag is PageTakeoffNode pageTakeoff)
        {
            HandlePageTakeoffNodeMultiSelect(item, pageTakeoff, e);
            return;
        }

        if (item.Tag is PageOverlayNode)
        {
            _pagesMultiSelection.Clear();
            _pageTakeoffMultiSelection.Clear();
            ApplyPagesMultiSelectionVisuals();
            TxtStatus.Text = "Sheet overlay selected. Right-click it to move, scale, recolor, or clear.";
            e.Handled = true;
            return;
        }

        string? path = GetPagesNodePath(item);
        if (path == null)
        {
            _pagesMultiSelection.Clear();
            _pageTakeoffMultiSelection.Clear();
            ApplyPagesMultiSelectionVisuals();
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None &&
            _pagesMultiSelection.Count > 1 &&
            _pagesMultiSelection.Contains(path))
        {
            item.IsSelected = true;
            ApplyPagesMultiSelectionVisuals();
            return;
        }

        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift && !IsRootPagesNode(item))
        {
            bool additive = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            SelectPagesRange(_pagesRangeAnchorPath, path, additive);
            _pagesRangeAnchorPath = path;
            _pageTakeoffMultiSelection.Clear();
            item.IsSelected = true;
            ApplyPagesMultiSelectionVisuals();
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control && !IsRootPagesNode(item))
        {
            if (!_pagesMultiSelection.Add(path))
                _pagesMultiSelection.Remove(path);
            _pagesRangeAnchorPath = path;
            _pageTakeoffMultiSelection.Clear();
            item.IsSelected = true;
            ApplyPagesMultiSelectionVisuals();
            e.Handled = true;
            return;
        }

        _pagesMultiSelection.Clear();
        if (!IsRootPagesNode(item))
            _pagesMultiSelection.Add(path);
        _pagesRangeAnchorPath = path;
        _pageTakeoffMultiSelection.Clear();
        ApplyPagesMultiSelectionVisuals();
    }

    private void HandlePageTakeoffNodeMultiSelect(TreeViewItem item, PageTakeoffNode node, MouseButtonEventArgs e)
    {
        string key = PageTakeoffSelectionKey(node);
        ModifierKeys modifiers = Keyboard.Modifiers;
        _pagesMultiSelection.Clear();

        if (modifiers == ModifierKeys.None &&
            _pageTakeoffMultiSelection.Count > 1 &&
            _pageTakeoffMultiSelection.Contains(key))
        {
            SelectPagesTreeItemSilently(item);
            ApplyPagesMultiSelectionVisuals();
            return;
        }

        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            bool additive = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            SelectPageTakeoffRange(_pageTakeoffRangeAnchorKey, key, node.Page.FolderPath, additive);
            _pageTakeoffRangeAnchorKey = key;
            SelectPagesTreeItemSilently(item);
            ApplyPagesMultiSelectionVisuals();
            Dispatcher.InvokeAsync(() => SelectSelectedPageTakeoffMeasurementsOnCanvas(node));
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (!_pageTakeoffMultiSelection.Add(key))
                _pageTakeoffMultiSelection.Remove(key);
            _pageTakeoffRangeAnchorKey = key;
            SelectPagesTreeItemSilently(item);
            ApplyPagesMultiSelectionVisuals();
            Dispatcher.InvokeAsync(() => SelectSelectedPageTakeoffMeasurementsOnCanvas(node));
            e.Handled = true;
            return;
        }

        _pageTakeoffMultiSelection.Clear();
        _pageTakeoffMultiSelection.Add(key);
        _pageTakeoffRangeAnchorKey = key;
        ApplyPagesMultiSelectionVisuals();
    }

    private static void ExpandTreeItemAndAncestors(TreeViewItem item)
    {
        item.IsExpanded = true;
        ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(item);
        while (parent is TreeViewItem parentItem)
        {
            parentItem.IsExpanded = true;
            parent = ItemsControl.ItemsControlFromItemContainer(parentItem);
        }
    }

    private void TreeView_RequestBringIntoViewKeepLeft(object sender, RequestBringIntoViewEventArgs e)
    {
        if (sender is not TreeView tree)
            return;

        Dispatcher.InvokeAsync(() =>
        {
            foreach (ScrollViewer scrollViewer in FindVisualChildren<ScrollViewer>(tree))
                scrollViewer.ScrollToHorizontalOffset(0);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void BringPageTreeItemIntoCenteredView(TreeViewItem item)
    {
        item.BringIntoView();
        Dispatcher.InvokeAsync(() =>
        {
            ScrollViewer? scrollViewer = FindVisualChildren<ScrollViewer>(PagesTree).FirstOrDefault();
            if (scrollViewer == null || scrollViewer.ViewportHeight <= 0)
                return;

            Point top;
            try
            {
                top = item.TranslatePoint(new Point(0, 0), scrollViewer);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            double itemHeight = item.ActualHeight > 0 ? item.ActualHeight : 22.0;
            double offset = scrollViewer.VerticalOffset + top.Y - ((scrollViewer.ViewportHeight - itemHeight) / 2.0);
            scrollViewer.ScrollToVerticalOffset(Math.Max(0, offset));
            scrollViewer.ScrollToHorizontalOffset(0);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

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
        List<PdfMetadataPageResult> results = await Task.Run(() =>
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

    private void ApplyPdfMetadataResults(
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

    private IEnumerable<PageInfo> CollectPagesUnder(string path)
    {
        if (!Directory.Exists(path))
            yield break;

        if (OurPlaneCoreJobStore.TryReadPage(path) is { } page)
        {
            yield return page;
            yield break;
        }

        foreach (string child in OurPlaneCoreJobStore.GetOrderedChildDirectories(path))
        {
            foreach (PageInfo pageInfo in CollectPagesUnder(child))
                yield return pageInfo;
        }
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

    private void CaptureFinalLearningSnapshot(TreeViewItem item)
    {
        if (_currentJob == null)
            return;

        var pages = GetPagesForMetadata(item);
        if (pages.Count == 0)
            return;

        foreach (PageInfo page in pages)
            SmartLearningStore.CaptureManualPageState(_currentJob, page, "End-of-project/manual learning snapshot.");
        SmartSheetLearningSummary summary = SmartLearningStore.SaveProjectSummary(_currentJob);

        MessageBox.Show(
            $"Captured {pages.Count} page state(s)." + Environment.NewLine +
            $"Learning records in this project: {summary.RecordCount}.",
            "Capture Final Learning Snapshot",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        TxtStatus.Text = $"Captured final learning snapshot for {pages.Count} page(s).";
    }

    private void ReviewLearnedRules()
    {
        if (_currentJob != null)
            SmartLearningStore.EnsureLearningStore(_currentJob);

        SmartLearnedRuleSet rules = SmartLearningStore.LoadGlobalLearnedRules();
        if (rules.Rules.Count == 0)
        {
            MessageBox.Show(
                "No learned rules yet. Capture a final learning snapshot after reviewed projects to generate rules.",
                "Review Learned Rules",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new LearnedRulesDialog(rules, "Review Global Learned Rules")
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        SmartLearningStore.SaveGlobalLearnedRules(dialog.RuleSet);
        int enabled = dialog.RuleSet.Rules.Count(rule => rule.Enabled);
        TxtStatus.Text = $"Saved learned rules: {enabled} enabled, {dialog.RuleSet.Rules.Count - enabled} disabled.";
    }

    private void ReviewProjectLearnedRules()
    {
        if (_currentJob == null)
            return;

        SmartLearningStore.EnsureLearningStore(_currentJob);
        SmartLearnedRuleSet rules = SmartLearningStore.LoadProjectLearnedRules(_currentJob);
        if (rules.Rules.Count == 0)
        {
            MessageBox.Show(
                "No project learned rules yet. Capture a final learning snapshot for this project to generate rules.",
                "Review Project Learned Rules",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new LearnedRulesDialog(rules, "Review Project Learned Rules")
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        SmartLearningStore.SaveProjectLearnedRules(_currentJob, dialog.RuleSet);
        int enabled = dialog.RuleSet.Rules.Count(rule => rule.Enabled);
        TxtStatus.Text = $"Saved project learned rules: {enabled} enabled, {dialog.RuleSet.Rules.Count - enabled} disabled.";
    }

}
