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
using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    // ── Pages tree ────────────────────────────────────────────────────────────

    private void ReloadPagesTree(string? selectPath = null, bool selectSilently = false)
    {
        InvalidatePagePreviewPrefetchCache();
        ResetPageTreeItemIndex();
        PagesTree.Items.Clear();
        _pageTakeoffMultiSelection.Clear();
        _pageTakeoffRangeAnchorKey = null;
        ClearDirtyPageTakeoffIndicators();
        if (_currentJob == null)
        {
            _expandedPageTreePaths.Clear();
            return;
        }

        using (UsePageMeasurementLookup())
        {
            // FillPagesTree already builds every page header and its takeoff
            // child nodes. RefreshPagesTakeoffIndicators() used to run right
            // here and rebuild all of them a second time (identical output),
            // doubling the page-tree build cost on every job open / reload.
            // ClearDirtyPageTakeoffIndicators() + ApplyPagesMultiSelectionVisuals()
            // (its only other effects) already run below.
            FillPagesTree(PagesTree.Items, _currentJob.PagesRoot);
        }
        RebuildPageTreeItemIndex();
        ClearDirtyPageTakeoffIndicators();
        RestoreExpandedTreeState(PagesTree, _expandedPageTreePaths, GetPagesNodePath);

        if (!string.IsNullOrWhiteSpace(selectPath))
        {
            if (selectSilently)
                SelectPageTreeNodeSilently(selectPath);
            else
                SelectNodeByFolder(selectPath);
        }
        PrunePagesMultiSelection();
        ApplyPagesMultiSelectionVisuals();
        ApplyPagesTreeSearchFilter();
    }

    private int FillPagesTree(ItemCollection items, string folder)
    {
        if (!Directory.Exists(folder))
            return 0;

        int pageCount = 0;
        foreach (string dir in OurPlanCoreJobStore.GetOrderedChildDirectories(folder))
        {
            PageInfo? page = OurPlanCoreJobStore.TryReadPage(dir);
            if (page != null)
            {
                var pageItem = new TreeViewItem
                {
                    Header = BuildPageHeader(page),
                    Tag = page,
                    IsExpanded = false,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                RebuildPageTakeoffNodes(pageItem, page);
                items.Add(pageItem);
                pageCount++;
                continue;
            }

            string name = OurPlanCoreJobStore.ReadName(dir) ?? Path.GetFileName(dir);
            var folderNode = new PageFolderNode { Name = name, FolderPath = dir };
            var tvi = new TreeViewItem
            {
                Header = $"📁 {name}",
                Tag = folderNode,
                IsExpanded = false,
            };
            items.Add(tvi);
            int childPageCount = FillPagesTree(tvi.Items, dir);
            tvi.Header = BuildPageFolderHeader(folderNode, childPageCount);
            pageCount += childPageCount;
        }

        return pageCount;
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

    private DockPanel BuildPageHeader(PageInfo page)
    {
        var panel = new DockPanel
        {
            Background = Brushes.Transparent,
            LastChildFill = true,
        };

        if (HasCurrentPageMeasurements(page))
        {
            FrameworkElement visibilityDot = BuildPageMeasurementVisibilityDot(page);
            DockPanel.SetDock(visibilityDot, Dock.Right);
            panel.Children.Add(visibilityDot);
        }

        var nameRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameRow.Children.Add(new TextBlock
        {
            Text = $"  {PdfSheetMetadataService.VisibleSheetDisplayName(page.Name)}",
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (page.ScaleMetersPerPt <= 0)
        {
            nameRow.Children.Add(new TextBlock
            {
                Text = "  unscaled",
                Foreground = Brushes.Firebrick,
                FontSize = 10,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        panel.Children.Add(nameRow);
        return panel;
    }

    private StackPanel BuildPageFolderHeader(PageFolderNode folder, int pageCount)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = Brushes.Transparent,
        };
        panel.Children.Add(CreateFolderTreeIcon(pageCount > 0));
        panel.Children.Add(new TextBlock
        {
            Text = folder.Name,
            FontWeight = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = pageCount == 1 ? "  1 sheet" : $"  {pageCount} sheets",
            Foreground = Brushes.Gray,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });
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
        bool rebuiltAny = false;
        bool reloadNeeded = false;
        using (UsePageMeasurementLookup())
        {
            foreach (TreeViewItem item in EnumeratePageTreeItems().ToList())
            {
                if (item.Tag is PageTakeoffNode or PageOverlayNode)
                    continue;

                string? path = GetPagesNodePath(item);
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (TryRefreshPageTreeItemFromStore(item, path, out _))
                {
                    rebuiltAny = true;
                    continue;
                }

                if (item.Tag is PageInfo)
                    reloadNeeded = true;
            }
        }

        if (reloadNeeded)
        {
            ReloadPagesTree();
            return;
        }

        if (rebuiltAny)
            RebuildPageTreeItemIndex();
        ClearDirtyPageTakeoffIndicators();
        ApplyPagesMultiSelectionVisuals();
    }

    private void RefreshPageTakeoffIndicatorsForFolder(string? pageFolder)
    {
        if (string.IsNullOrWhiteSpace(pageFolder))
        {
            RefreshPagesTakeoffIndicators();
            return;
        }

        using (UsePageMeasurementLookup())
        {
            if (FindPageTreeItemByFolder(pageFolder) is not { } item ||
                !TryRefreshPageTreeItemFromStore(item, pageFolder, out string refreshedKey))
            {
                ReloadPagesTree(pageFolder, selectSilently: true);
                return;
            }

            RebuildPageTreeItemIndex();
            RefreshPageTreeRowsByFolderKeys(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                refreshedKey,
            });
        }
    }

    private void RefreshPageTakeoffIndicatorsForFolders(IEnumerable<string> pageFolders)
    {
        var folders = pageFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .GroupBy(NormalizePathForCompare, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (folders.Count == 0)
            return;

        var refreshed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (UsePageMeasurementLookup())
        {
            foreach (string folder in folders)
            {
                if (FindPageTreeItemByFolder(folder) is not { } item ||
                    !TryRefreshPageTreeItemFromStore(item, folder, out string refreshedKey))
                {
                    ReloadPagesTree(folder, selectSilently: true);
                    return;
                }

                refreshed.Add(refreshedKey);
            }
        }

        RebuildPageTreeItemIndex();
        RefreshPageTreeRowsByFolderKeys(refreshed);
    }

    private void RefreshPageTreePageSnapshots(IReadOnlyList<PageInfo> pages)
    {
        if (pages.Count == 0)
            return;

        var refreshed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool reloadNeeded = false;
        using (UsePageMeasurementLookup())
        {
            foreach (PageInfo page in pages)
            {
                string key = NormalizePathForCompare(page.FolderPath);
                if (string.IsNullOrWhiteSpace(key) ||
                    refreshed.Contains(key) ||
                    FindPageTreeItemByFolderKeyIndexed(key) is not { } item)
                {
                    continue;
                }

                if (TryRefreshPageTreeItemFromStore(item, page.FolderPath, out string refreshedKey))
                    refreshed.Add(refreshedKey);
                else
                    reloadNeeded = true;
            }
        }

        if (reloadNeeded)
        {
            ReloadPagesTree();
            return;
        }

        if (refreshed.Count == 0)
            return;

        RebuildPageTreeItemIndex();
        RefreshPageTreeRowsByFolderKeys(refreshed);
    }

    private bool TryRefreshPageTreeItemFromStore(TreeViewItem item, string pageFolder, out string refreshedKey)
    {
        refreshedKey = "";
        if (OurPlanCoreJobStore.TryReadPage(pageFolder) is not { } refreshedPage)
            return false;

        bool wasExpanded = item.IsExpanded;
        item.Tag = refreshedPage;
        item.Header = BuildPageHeader(refreshedPage);
        RebuildPageTakeoffNodes(item, refreshedPage);
        item.IsExpanded = wasExpanded;
        refreshedKey = NormalizePathForCompare(refreshedPage.FolderPath);
        return !string.IsNullOrWhiteSpace(refreshedKey);
    }

    private void RefreshPageTakeoffIndicatorsForActiveChange(
        string? previousActiveTakeoffFolder,
        IReadOnlyList<TakeoffItem> selectedTakeoffs)
    {
        var pageFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddPageFoldersForTakeoffFolder(previousActiveTakeoffFolder, pageFolders);
        foreach (TakeoffItem takeoff in selectedTakeoffs)
            AddPageFoldersForTakeoff(takeoff, pageFolders);

        if (pageFolders.Count == 0)
        {
            return;
        }

        RefreshKnownPageTakeoffIndicatorsForFolders(pageFolders);
    }

    private void RefreshKnownPageTakeoffIndicatorsForFolders(IEnumerable<string> pageFolders)
    {
        var folders = pageFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .GroupBy(NormalizePathForCompare, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (folders.Count == 0)
            return;

        var refreshed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool rebuiltAny = false;
        using (UsePageMeasurementLookup())
        {
            foreach (string folder in folders)
            {
                string key = NormalizePathForCompare(folder);
                if (FindPageTreeItemByFolderKeyIndexed(key) is not { } item)
                    continue;

                if (TryRefreshPageTreeItemFromStore(item, folder, out string refreshedKey))
                {
                    rebuiltAny = true;
                    refreshed.Add(refreshedKey);
                }
                else
                {
                    refreshed.Add(key);
                }
            }
        }

        if (refreshed.Count == 0)
            return;

        if (rebuiltAny)
            RebuildPageTreeItemIndex();

        RefreshPageTreeRowsByFolderKeys(refreshed);
    }

    private void AddPageFoldersForTakeoffFolder(string? takeoffFolder, HashSet<string> pageFolders)
    {
        if (string.IsNullOrWhiteSpace(takeoffFolder))
            return;

        TakeoffItem? item = _takeoffItems.FirstOrDefault(candidate =>
            string.Equals(candidate.FolderPath, takeoffFolder, StringComparison.OrdinalIgnoreCase));
        AddPageFoldersForTakeoff(item, pageFolders);
    }

    private static void AddPageFoldersForTakeoff(TakeoffItem? takeoff, HashSet<string> pageFolders)
    {
        if (takeoff == null)
            return;

        foreach (Measurement measurement in takeoff.Measurements)
        {
            if (!string.IsNullOrWhiteSpace(measurement.PageFolder))
                pageFolders.Add(measurement.PageFolder);
        }
    }

    private void PagesTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingPageTreeSelection)
            return;

        if (e.NewValue is TreeViewItem { Tag: PageInfo page })
        {
            TryRefreshDirtyPageTakeoffIndicator(page.FolderPath);
            if (!TryShowPageInFocusedDetachedSheet(page))
                OpenPageInActiveTab(page);
        }
        else if (e.NewValue is TreeViewItem { Tag: PageTakeoffNode node })
        {
            SelectLinkedPageTakeoff(node);
        }
        else if (e.NewValue is TreeViewItem { Tag: PageOverlayNode overlay })
        {
            ShowSheetOverlayProperties(overlay.Page);
        }
    }

    private string GetSelectedImportFolder()
    {
        if (_currentJob == null)
            throw new InvalidOperationException("No job is open.");

        return OurPlanCoreJobStore.DefaultImportFolder(_currentJob);
    }

    private void SelectNodeByFolder(string folderPath)
    {
        WithTreeExpansionTrackingSuppressed(() =>
        {
            if (FindPageTreeItemByFolder(folderPath) is not { } item)
                return;

            ExpandTreeItemAndAncestors(item);
            item.IsSelected = true;
            item.BringIntoView();
        });
    }

    private TreeViewItem? FindPageTreeItemByFolder(string folderPath)
        => FindPageTreeItemByFolderIndexed(folderPath);

    private void SelectPageByFolder(string folderPath)
    {
        bool selected = false;
        WithTreeExpansionTrackingSuppressed(() =>
        {
            foreach (TreeViewItem item in PagesTree.Items)
            {
                if (SelectPageByFolder(item, folderPath))
                {
                    selected = true;
                    return;
                }
            }
        });

        if (selected || OurPlanCoreJobStore.IsPageFolder(folderPath))
            OpenPageByFolder(folderPath);
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
        if (FindPagesTreeItemFromSource(e.OriginalSource as DependencyObject) is not { } item)
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
            item.Focus();
            item.IsSelected = true;
        }
        finally
        {
            _syncingPageTreeSelection = false;
        }
    }

    private bool HasActivePagesMultiSelection() => _pagesMultiSelection.Count > 1;

    private void SyncPageTreeNodeForViewportOpen(string pageFolder)
    {
        if (HasActivePagesMultiSelection())
        {
            ApplyPagesMultiSelectionVisuals();
            return;
        }

        if (PagesTree.SelectedItem is TreeViewItem { Tag: PageInfo selectedPage } &&
            IsSamePageFolder(selectedPage.FolderPath, pageFolder))
        {
            ApplyPagesMultiSelectionVisuals();
            return;
        }

        SelectPageTreeNodeSilently(pageFolder);
    }

    private void PagesTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TryBeginPagesTreeMarqueeSelection(e))
            return;

        _pagesDragStart = e.GetPosition(PagesTree);
        _pagesDragItem = null;
        _pagesDragArmed = false;
        if (FindPagesTreeItemFromSource(e.OriginalSource as DependencyObject) is not { } item)
            return;

        _pagesDragItem = item;
        _pagesDragArmed = CanArmPagesTreeDrag(item, e.OriginalSource as DependencyObject);
        if (FindAncestor<ToggleButton>(e.OriginalSource as DependencyObject) != null)
            return;

        if (item.Tag is PageInfo visibilityPage &&
            IsPageMeasurementVisibilityToggleSource(e.OriginalSource as DependencyObject))
        {
            TogglePageMeasurementVisibilitySnapshot(visibilityPage);
            e.Handled = true;
            return;
        }

        if (item.Tag is PageTakeoffNode visibilityTakeoff &&
            IsPageTakeoffVisibilityToggleSource(e.OriginalSource as DependencyObject))
        {
            TogglePageTakeoffVisibility(visibilityTakeoff.Page, visibilityTakeoff.Takeoff);
            e.Handled = true;
            return;
        }

        if (item.Tag is PageTakeoffNode pageTakeoff)
        {
            HandlePageTakeoffNodeMultiSelect(item, pageTakeoff, e);
            return;
        }

        if (item.Tag is PageOverlayNode overlay)
        {
            _pagesMultiSelection.Clear();
            _pageTakeoffMultiSelection.Clear();
            SelectPagesTreeItemSilently(item);
            ApplyPagesMultiSelectionVisuals();
            if (IsPageOverlayVisibilityToggleSource(e.OriginalSource as DependencyObject))
                TogglePageOverlayVisibility(overlay.Page);
            else
                ShowSheetOverlayProperties(overlay.Page);
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
        SelectPageTreeItemAndOpenIfPage(item);
    }

    private void SelectPageTreeItemAndOpenIfPage(TreeViewItem item)
    {
        if (item.Tag is not PageInfo page)
            return;

        SelectPagesTreeItemSilently(item);
        TryRefreshDirtyPageTakeoffIndicator(page.FolderPath);
        if (TryShowPageInFocusedDetachedSheet(page))
            return;

        OpenPageInActiveTab(page);
    }

    private bool CanArmPagesTreeDrag(TreeViewItem item, DependencyObject? source)
    {
        if (FindAncestor<ToggleButton>(source) != null)
            return false;
        if (IsPageMeasurementVisibilityToggleSource(source))
            return false;
        if (IsPageTakeoffVisibilityToggleSource(source))
            return false;
        if (IsPageOverlayVisibilityToggleSource(source))
            return false;
        if (item.Tag is PageTakeoffNode)
            return true;

        string? path = GetPagesNodePath(item);
        return path != null &&
               !IsRootPagesNode(item) &&
               Directory.Exists(path) &&
               IsPathInsidePagesRoot(path, allowRoot: false);
    }

    // ContainerFromElement(PagesTree, source) resolves nested sheet headers to
    // their parent folder, so prefer the nearest visual TreeViewItem first.
    private TreeViewItem? FindPagesTreeItemFromSource(DependencyObject? source) =>
        FindAncestor<TreeViewItem>(source) ??
        ItemsControl.ContainerFromElement(PagesTree, source) as TreeViewItem;

    private static bool IsPageOverlayVisibilityToggleSource(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is FrameworkElement { Tag: PageOverlayVisibilityToggleTag })
                return true;
            if (source is TreeViewItem)
                return false;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static bool IsPageTakeoffVisibilityToggleSource(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is FrameworkElement { Tag: PageTakeoffVisibilityToggleTag })
                return true;
            if (source is TreeViewItem)
                return false;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
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
            SyncTakeoffsTreeSelectionFromPageTakeoffs(node, fallbackToAnchor: false);
            return;
        }

        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            bool additive = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            SelectPageTakeoffRange(_pageTakeoffRangeAnchorKey, key, node.Page.FolderPath, additive);
            _pageTakeoffRangeAnchorKey = key;
            SelectPagesTreeItemSilently(item);
            ApplyPagesMultiSelectionVisuals();
            SyncTakeoffsTreeSelectionFromPageTakeoffs(node, fallbackToAnchor: false);
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
            SyncTakeoffsTreeSelectionFromPageTakeoffs(node, fallbackToAnchor: false);
            Dispatcher.InvokeAsync(() => SelectSelectedPageTakeoffMeasurementsOnCanvas(node));
            e.Handled = true;
            return;
        }

        _pageTakeoffMultiSelection.Clear();
        _pageTakeoffMultiSelection.Add(key);
        _pageTakeoffRangeAnchorKey = key;
        ApplyPagesMultiSelectionVisuals();
        SyncTakeoffsTreeSelectionFromPageTakeoffs(node, fallbackToAnchor: true);
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

}
