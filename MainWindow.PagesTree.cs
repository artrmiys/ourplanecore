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

    private void RebuildPageTakeoffNodes(TreeViewItem pageItem, PageInfo page)
    {
        pageItem.Items.Clear();
        IReadOnlyList<TakeoffItem> orderedTakeoffs = OrderedTakeoffsForPage(page);
        var addedTakeoffs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int legendIndex = 0;
        foreach (TakeoffItem takeoff in orderedTakeoffs)
        {
            if (!addedTakeoffs.Add(TakeoffLegendOrderKey(takeoff)))
                continue;

            var node = new PageTakeoffNode(page, takeoff);
            var child = new TreeViewItem
            {
                Header = BuildPageTakeoffHeader(page, takeoff, legendIndex),
                Tag = node,
            };
            child.ContextMenu = BuildPageTakeoffContextMenu(node);
            pageItem.Items.Add(child);
            legendIndex++;
        }

        if (!string.IsNullOrWhiteSpace(page.OverlayPageFolder))
            pageItem.Items.Add(CreatePageOverlayTreeItem(page));
    }

    private TreeViewItem CreatePageOverlayTreeItem(PageInfo page)
    {
        string overlayName = OverlayPageName(page);
        var node = new PageOverlayNode(page, overlayName);
        return new TreeViewItem
        {
            Header = BuildPageOverlayHeader(page, overlayName),
            Tag = node,
            ContextMenu = BuildPageOverlayContextMenu(node),
        };
    }

    private FrameworkElement BuildPageOverlayHeader(PageInfo page, string overlayName)
    {
        var secondaryBrush = (Brush)Application.Current.Resources["SecondaryForegroundBrush"]
            ?? new SolidColorBrush(Color.FromRgb(128, 128, 128));
        Brush swatchBrush = BrushFromHex(page.OverlayColor, Brushes.Gray);

        var dock = new DockPanel { LastChildFill = true };
        var transform = new TextBlock
        {
            Text = $"{page.OverlayScale:0.###}x  {page.OverlayOffsetXPt:0.#},{page.OverlayOffsetYPt:0.#}",
            Foreground = secondaryBrush,
            FontSize = 10,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
            Margin = new Thickness(8, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            MinWidth = 76,
        };
        DockPanel.SetDock(transform, Dock.Right);
        dock.Children.Add(transform);

        var colorBox = new Border
        {
            Width = 12,
            Height = 12,
            Background = swatchBrush,
            BorderBrush = secondaryBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(28, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var nameRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameRow.Children.Add(colorBox);
        nameRow.Children.Add(new TextBlock
        {
            Text = $"Overlay: {overlayName}",
            FontWeight = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        dock.Children.Add(nameRow);
        dock.ToolTip = "Sheet overlay. Right-click to move, scale, recolor, or clear.";
        return dock;
    }

    private FrameworkElement BuildPageTakeoffHeader(PageInfo page, TakeoffItem takeoff, int legendIndex)
    {
        bool isActive = IsActivePageTakeoff(page, takeoff);
        var secondaryBrush = (Brush)Application.Current.Resources["SecondaryForegroundBrush"]
            ?? new SolidColorBrush(Color.FromRgb(128, 128, 128));
        Brush swatchBrush = BrushFromHex(takeoff.Color, Brushes.Gray);

        var dock = new DockPanel { LastChildFill = true };

        var pageMeasurements = MeasurementsForTakeoffOnPage(takeoff, page.FolderPath).ToList();
        if (pageMeasurements.Count > 0)
        {
            var qty = new TextBlock
            {
                Text              = SheetLegendQuantityText(takeoff, pageMeasurements),
                Foreground        = secondaryBrush,
                FontSize          = 10,
                FontFamily        = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
                Margin            = new Thickness(8, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment     = TextAlignment.Right,
                MinWidth          = 56,
            };
            DockPanel.SetDock(qty, Dock.Right);
            dock.Children.Add(qty);
        }

        var indexText = new TextBlock
        {
            Text              = $"{legendIndex + 1}.",
            Width             = 22,
            TextAlignment     = TextAlignment.Right,
            Foreground        = secondaryBrush,
            FontSize          = 10,
            Margin            = new Thickness(2, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var swatchHost = BuildTakeoffSwatchGlyph(takeoff, swatchBrush, isActive ? 16 : 14);
        swatchHost.Margin = new Thickness(0, 0, 6, 0);

        var nameRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameRow.Children.Add(indexText);
        nameRow.Children.Add(swatchHost);
        nameRow.Children.Add(new TextBlock
        {
            Text              = takeoff.Name,
            FontWeight        = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
        });
        dock.Children.Add(nameRow);

        dock.ToolTip =
            $"Legend position: {legendIndex + 1}" + Environment.NewLine +
            "Linked to the real Takeoffs item. Use Move Up/Down here only to change this sheet's legend order.";
        return dock;
    }

    private IEnumerable<TakeoffItem> TakeoffsForPage(string pageFolder) =>
        _takeoffItems
            .Where(item => item.Measurements.Any(m =>
                IsSamePageFolder(m.PageFolder, pageFolder)))
            .GroupBy(TakeoffLegendOrderKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());

    private IEnumerable<Measurement> MeasurementsForTakeoffOnPage(TakeoffItem item, string pageFolder) =>
        item.Measurements.Where(measurement => IsSamePageFolder(measurement.PageFolder, pageFolder));

    private IReadOnlyList<TakeoffItem> OrderedTakeoffsForPage(PageInfo page)
    {
        var takeoffs = TakeoffsForPage(page.FolderPath).ToList();
        if (takeoffs.Count <= 1)
            return takeoffs;

        var byKey = takeoffs
            .GroupBy(TakeoffLegendOrderKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<TakeoffItem>();

        foreach (string storedKey in page.LegendTakeoffOrder.Select(NormalizeTakeoffLegendOrderKey))
        {
            if (string.IsNullOrWhiteSpace(storedKey) || !byKey.TryGetValue(storedKey, out TakeoffItem? takeoff))
                continue;
            if (!used.Add(storedKey))
                continue;

            ordered.Add(takeoff);
        }

        ordered.AddRange(takeoffs
            .Where(takeoff => !used.Contains(TakeoffLegendOrderKey(takeoff)))
            .OrderBy(takeoff => MeasurementTypeTitle(takeoff.MeasurementType), StringComparer.OrdinalIgnoreCase)
            .ThenBy(takeoff => takeoff.Name, StringComparer.OrdinalIgnoreCase));

        return ordered;
    }

    private string TakeoffLegendOrderKey(TakeoffItem item) =>
        NormalizeTakeoffLegendOrderKey(item.FolderPath);

    private string NormalizeTakeoffLegendOrderKey(string value)
    {
        string clean = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return "";

        if (_currentJob != null && Path.IsPathFullyQualified(clean))
        {
            string full = NormalizePath(clean);
            if (OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, full))
                clean = Path.GetRelativePath(_currentJob.TakeoffsRoot, full);
        }

        return clean.Replace('\\', '/').Trim('/');
    }

    private void SavePageLegendOrder(PageInfo page, IReadOnlyList<TakeoffItem> orderedTakeoffs)
    {
        var order = orderedTakeoffs
            .Select(TakeoffLegendOrderKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        page.LegendTakeoffOrder = order;
        if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
            _currentPage.LegendTakeoffOrder = order.ToList();
        OurPlaneCoreJobStore.SavePageLegendTakeoffOrder(page.FolderPath, order);
    }

    private void RebasePageLegendTakeoffOrderReferences(string oldPath, string newPath)
    {
        RebaseExpandedTreePaths(_expandedTakeoffTreePaths, oldPath, newPath);

        if (_currentJob == null)
            return;

        string oldKey = NormalizeTakeoffLegendOrderKey(oldPath);
        string newKey = NormalizeTakeoffLegendOrderKey(newPath);
        if (string.IsNullOrWhiteSpace(oldKey) ||
            string.IsNullOrWhiteSpace(newKey) ||
            string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (PageInfo page in CollectPagesUnder(_currentJob.PagesRoot))
        {
            if (page.LegendTakeoffOrder.Count == 0)
                continue;

            var updated = page.LegendTakeoffOrder
                .Select(key => RebaseTakeoffLegendOrderKey(key, oldKey, newKey))
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool changed = updated.Count != page.LegendTakeoffOrder.Count ||
                           updated.Where((key, index) => !string.Equals(
                               key,
                               NormalizeTakeoffLegendOrderKey(page.LegendTakeoffOrder[index]),
                               StringComparison.OrdinalIgnoreCase)).Any();
            if (!changed)
                continue;

            page.LegendTakeoffOrder = updated;
            OurPlaneCoreJobStore.SavePageLegendTakeoffOrder(page.FolderPath, updated);
            if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
                _currentPage.LegendTakeoffOrder = updated.ToList();
        }
    }

    private string RebaseTakeoffLegendOrderKey(string key, string oldPrefix, string newPrefix)
    {
        string clean = NormalizeTakeoffLegendOrderKey(key);
        if (string.Equals(clean, oldPrefix, StringComparison.OrdinalIgnoreCase))
            return newPrefix;

        string prefix = oldPrefix.TrimEnd('/') + "/";
        if (clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return newPrefix.TrimEnd('/') + clean[(prefix.Length - 1)..];

        return clean;
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

    private void RemovePageTabsForAffectedPath(string affectedPath)
    {
        bool changed = false;
        for (int i = _pageTabs.Count - 1; i >= 0; i--)
        {
            PageTabState tab = _pageTabs[i];
            if (!OurPlaneCoreJobStore.IsSameOrDescendant(affectedPath, tab.PageFolder))
                continue;

            if (ReferenceEquals(tab, _activePageTab))
                _activePageTab = null;
            _pageTabs.RemoveAt(i);
            changed = true;
        }

        if (changed)
            RefreshPageTabs(_activePageTab);
    }

    private bool UpdatePageReferencesForMovedPath(string oldPath, string newPath)
    {
        string oldFull = NormalizePath(oldPath);
        string newFull = NormalizePath(newPath);
        RebaseExpandedTreePaths(_expandedPageTreePaths, oldFull, newFull);
        bool activeAffected = _currentPage != null &&
                              OurPlaneCoreJobStore.IsSameOrDescendant(oldFull, _currentPage.FolderPath);
        bool tabsChanged = false;
        bool measurementsChanged = RebaseMeasurementPageFolderReferences(oldFull, newFull);

        foreach (PageTabState tab in _pageTabs)
        {
            if (!OurPlaneCoreJobStore.IsSameOrDescendant(oldFull, tab.PageFolder))
                continue;

            tab.PageFolder = RebaseDescendantPath(oldFull, newFull, tab.PageFolder);
            if (OurPlaneCoreJobStore.TryReadPage(tab.PageFolder) is { } page)
                tab.PageName = page.Name;
            tabsChanged = true;
        }

        if (!string.IsNullOrWhiteSpace(_settings.LastPageFolder) &&
            OurPlaneCoreJobStore.IsSameOrDescendant(oldFull, _settings.LastPageFolder))
        {
            _settings.LastPageFolder = RebaseDescendantPath(oldFull, newFull, _settings.LastPageFolder);
            SaveAppSettings();
        }

        if (activeAffected)
        {
            _currentPage = null;
            _currentPdfPath = "";
        }

        if (tabsChanged)
            RefreshPageTabs(_activePageTab);
        if (measurementsChanged)
        {
            _viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
            RefreshPagesTakeoffIndicators();
            RefreshEstimateTable();
        }

        return activeAffected;
    }

    private bool RebaseMeasurementPageFolderReferences(string oldFull, string newFull)
    {
        if (_currentJob == null)
            return false;

        bool changed = false;
        foreach (TakeoffItem item in _takeoffItems)
        {
            bool itemChanged = false;
            foreach (Measurement measurement in item.Measurements)
            {
                if (string.IsNullOrWhiteSpace(measurement.PageFolder))
                    continue;

                string current = NormalizePageReferencePath(measurement.PageFolder);
                if (!OurPlaneCoreJobStore.IsSameOrDescendant(oldFull, current))
                    continue;

                measurement.PageFolder = RebaseDescendantPath(oldFull, newFull, current);
                changed = true;
                itemChanged = true;
            }

            if (itemChanged)
                OurPlaneCoreJobStore.SaveTakeoffItem(item);
        }

        return changed;
    }

    private void ReloadActivePageTabAfterPathChange(bool shouldReload)
    {
        if (!shouldReload || _activePageTab == null)
            return;

        if (Directory.Exists(_activePageTab.PageFolder))
        {
            LoadPageFromTab(_activePageTab);
            return;
        }

        _activePageTab = null;
        RefreshPageTabs(null);
        _viewport.ClearPage();
        TxtStatusPage.Text = "—";
    }

    private static string RebaseDescendantPath(string oldRoot, string newRoot, string path)
    {
        string relative = Path.GetRelativePath(oldRoot, NormalizePath(path));
        return relative == "."
            ? newRoot
            : Path.GetFullPath(Path.Combine(newRoot, relative));
    }

    private string NormalizePageReferencePath(string path)
    {
        if (_currentJob != null && !Path.IsPathFullyQualified(path))
            path = Path.Combine(_currentJob.RootPath, path);

        return NormalizePath(path);
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

    private void PagesTree_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pagesDragStart == null || e.LeftButton != MouseButtonState.Pressed)
            return;
        if (PagesTree.SelectedItem is not TreeViewItem item)
            return;
        if (IsRootPagesNode(item))
            return;

        Point pos = e.GetPosition(PagesTree);
        if (Math.Abs(pos.X - _pagesDragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _pagesDragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (item.Tag is PageTakeoffNode pageTakeoff)
        {
            var takeoffFolders = SelectedPageTakeoffNodes(pageTakeoff, fallbackToAnchor: true)
                .Select(node => node.Takeoff.FolderPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (takeoffFolders.Count == 0)
                return;

            var legendPayload = new PageTakeoffLegendDrag(pageTakeoff.Page.FolderPath, takeoffFolders);
            DoPagesDragDrop(legendPayload, DragDropEffects.Move);
            return;
        }

        var entries = GetSelectedPageEntries(item);
        if (entries.Count == 0)
            return;

        var payload = new PagesClipboard(entries, PagesClipboardMode.Cut);
        DoPagesDragDrop(payload, DragDropEffects.Move | DragDropEffects.Copy);
    }

    private void DoPagesDragDrop(object payload, DragDropEffects effects)
    {
        try
        {
            DragDrop.DoDragDrop(PagesTree, payload, effects);
        }
        finally
        {
            _pagesDragStart = null;
            FlushPendingPagesTreeDropRefresh();
        }
    }

    private void PagesTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(typeof(PageTakeoffLegendDrag)) is PageTakeoffLegendDrag legendDrag)
        {
            TreeViewItem? legendTargetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (CanDropPageTakeoffLegend(legendDrag, legendTargetItem))
            {
                e.Effects = DragDropEffects.Move;
                UpdatePageTakeoffLegendDropCue(legendDrag, legendTargetItem!, e.GetPosition(legendTargetItem));
            }
            else
            {
                ClearPageTakeoffLegendDropCue();
            }
            ClearPagesPositionDropCue();
            e.Handled = true;
            return;
        }

        ClearPageTakeoffLegendDropCue();
        if (e.Data.GetData(typeof(PagesClipboard)) is not PagesClipboard payload)
        {
            ClearPagesPositionDropCue();
            e.Handled = true;
            return;
        }

        TreeViewItem? targetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        string? targetFolder = targetItem == null ? _currentJob?.PagesRoot : GetPasteTargetFolder(targetItem);
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;

        if (TryGetPagesPositionDropCue(payload, targetItem, copy, e, out bool after, out bool canDropPosition, out string positionStatus))
        {
            UpdatePagesPositionDropCue(targetItem, after, canDropPosition, positionStatus);
            if (canDropPosition)
                e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        ClearPagesPositionDropCue();
        if (CanDropInto(payload, targetFolder, copy ? PagesClipboardMode.Copy : PagesClipboardMode.Cut))
            e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
    }

    private void PagesTree_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PageTakeoffLegendDrag)) is PageTakeoffLegendDrag legendDrag)
        {
            TreeViewItem? legendTargetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (legendTargetItem != null)
                DropPageTakeoffLegend(legendDrag, legendTargetItem, e.GetPosition(legendTargetItem));
            ClearPageTakeoffLegendDropCue();
            ClearPagesPositionDropCue();
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(typeof(PagesClipboard)) is not PagesClipboard payload)
        {
            ClearPagesPositionDropCue();
            return;
        }

        TreeViewItem? targetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        PagesClipboardMode mode = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey
            ? PagesClipboardMode.Copy
            : PagesClipboardMode.Cut;

        if (TryGetPagesPositionDropCue(payload, targetItem, mode == PagesClipboardMode.Copy, e, out bool after, out bool canDropPosition, out _) &&
            canDropPosition &&
            targetItem != null)
        {
            DropPagesPosition(payload, targetItem, after);
            ClearPagesPositionDropCue();
            e.Handled = true;
            return;
        }

        ClearPagesPositionDropCue();
        string? targetFolder = targetItem == null ? _currentJob?.PagesRoot : GetPasteTargetFolder(targetItem);
        if (!CanDropInto(payload, targetFolder, mode))
            return;

        RunDrop(payload, targetFolder!, mode);
        e.Handled = true;
    }

    private void PagesTree_DragLeave(object sender, DragEventArgs e)
    {
        if (!PagesTree.IsMouseOver)
        {
            ClearPageTakeoffLegendDropCue();
            ClearPagesPositionDropCue();
        }
    }

    private void PagesTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (PagesTree.SelectedItem is not TreeViewItem item) return;

        if (item.Tag is PageTakeoffNode node)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
            {
                MovePageTakeoffLegendNodes(node, -1);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
            {
                MovePageTakeoffLegendNodes(node, 1);
                e.Handled = true;
            }
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            CopyCutPagesNode(item, PagesClipboardMode.Copy);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.X)
        {
            CopyCutPagesNode(item, PagesClipboardMode.Cut);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            PasteIntoSelectedTarget(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D)
        {
            DuplicatePageNode(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
        {
            MovePagesNodes(item, -1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
        {
            MovePagesNodes(item, 1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
        {
            DeletePagesNode(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.F2)
        {
            RenamePagesNode(item);
            e.Handled = true;
        }
    }

    private ContextMenu BuildPagesContextMenu(TreeViewItem item)
    {
        var menu = new ContextMenu();

        if (item.Tag is PageFolderNode folder)
        {
            int selectedCount = PageSelectionCount(item);
            bool isRoot = folder.IsRoot;
            bool canPaste = CanPasteInto(folder.FolderPath);
            bool hasChildren = Directory.Exists(folder.FolderPath) &&
                               Directory.EnumerateDirectories(folder.FolderPath).Any();

            menu.Items.Add(MakeMenuItem("New Folder", true, () => NewPageFolder(item)));
            menu.Items.Add(MakeMenuItem("Rename Folder", !isRoot && selectedCount <= 1, () => RenamePagesNode(item)));
            menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Delete Selected" : "Delete Folder", !isRoot || selectedCount > 1, () => DeletePagesNode(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeSubmenu(
                "Clipboard",
                MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Folder", !isRoot || selectedCount > 1, () => CopyCutPagesNode(item, PagesClipboardMode.Copy)),
                MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Folder", !isRoot || selectedCount > 1, () => CopyCutPagesNode(item, PagesClipboardMode.Cut)),
                MakeMenuItem("Paste Into Folder", canPaste, () => PasteIntoSelectedTarget(item))));
            menu.Items.Add(MakeSubmenu(
                "Organize",
                MakeMenuItem("Auto Create Folders", true, () => AutoCreatePageFolders(folder.FolderPath)),
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up", CanMovePagesNodes(item, -1), () => MovePagesNodes(item, -1)),
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down", CanMovePagesNodes(item, 1), () => MovePagesNodes(item, 1)),
                MakeMenuItem("Sort Children A-Z", hasChildren, () => SortFolderChildren(item, descending: false)),
                MakeMenuItem("Sort Children Z-A", hasChildren, () => SortFolderChildren(item, descending: true)),
                MakeMenuItem("Sort A/S into Arch/Struct", true, SortPagesIntoArchStruct),
                MakeMenuItem("Sort D/Sec/WT by Suffix", true, SortPagesBySuffix),
                MakeMenuItem("Repair Measurement Links", true, RepairMeasurementPageLinks)));
            menu.Items.Add(MakeSubmenu(
                "PDF Metadata",
                MakeMenuItem("Analyze PDF Metadata", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: false)),
                MakeMenuItem("Auto Rename from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: false)),
                MakeMenuItem("Auto Scale from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: true)),
                MakeMenuItem("Auto Rename + Scale from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: true)),
                MakeMenuItem("Queue GPT Metadata Fallback", true, () => QueuePdfMetadataFallback(item))));
            menu.Items.Add(MakeSubmenu(
                "Learning",
                MakeMenuItem("Capture Final Learning Snapshot", true, () => CaptureFinalLearningSnapshot(item)),
                MakeMenuItem("Review Project Learned Rules...", true, ReviewProjectLearnedRules),
                MakeMenuItem("Review Global Learned Rules...", true, ReviewLearnedRules)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Open in Explorer", true, () => OpenFolderInExplorer(folder.FolderPath)));
        }
        else if (item.Tag is PageInfo page)
        {
            int selectedCount = PageSelectionCount(item);
            string parent = Path.GetDirectoryName(page.FolderPath) ?? "";
            menu.Items.Add(MakeMenuItem("Open in New Tab", true, () => OpenPageInNewTab(page)));
            menu.Items.Add(BuildSheetOverlayMenu(page));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Rename Page", selectedCount <= 1, () => RenamePagesNode(item)));
            menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Delete Selected" : "Delete Page", true, () => DeletePagesNode(item)));
            menu.Items.Add(MakeMenuItem("Duplicate Page", selectedCount <= 1, () => DuplicatePageNode(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeSubmenu(
                "Clipboard",
                MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Page", true, () => CopyCutPagesNode(item, PagesClipboardMode.Copy)),
                MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Page", true, () => CopyCutPagesNode(item, PagesClipboardMode.Cut)),
                MakeMenuItem("Paste Into Parent Folder", CanPasteInto(parent), () => PasteIntoSelectedTarget(item))));
            menu.Items.Add(MakeSubmenu(
                "Organize",
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up", CanMovePagesNodes(item, -1), () => MovePagesNodes(item, -1)),
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down", CanMovePagesNodes(item, 1), () => MovePagesNodes(item, 1)),
                MakeMenuItem("Move to Folder...", selectedCount <= 1, () => MovePageToFolder(item)),
                MakeMenuItem("Sort Sheet Legend A-Z", CanSortPageLegend(page), () => SortPageLegendByName(page)),
                MakeMenuItem("Reset Sheet Legend Order", HasCustomPageLegendOrder(page), () => ResetPageLegendOrder(page)),
                MakeMenuItem("Sort A/S into Arch/Struct", true, SortPagesIntoArchStruct),
                MakeMenuItem("Sort D/Sec/WT by Suffix", true, SortPagesBySuffix),
                MakeMenuItem("Repair Measurement Links", true, RepairMeasurementPageLinks)));
            menu.Items.Add(MakeSubmenu(
                "PDF Metadata",
                MakeMenuItem("Analyze PDF Metadata", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: false)),
                MakeMenuItem("Auto Rename from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: false)),
                MakeMenuItem("Auto Scale from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: true)),
                MakeMenuItem("Auto Rename + Scale from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: true)),
                MakeMenuItem("Queue GPT Metadata Fallback", true, () => QueuePdfMetadataFallback(item)),
                MakeMenuItem("Open source_pdf.json", File.Exists(OurPlaneCoreJobStore.SourcePdfMetadataPath(page.FolderPath)), () => OpenSourcePdfMetadata(page.FolderPath))));
            menu.Items.Add(MakeSubmenu(
                "Learning",
                MakeMenuItem("Capture Final Learning Snapshot", true, () => CaptureFinalLearningSnapshot(item)),
                MakeMenuItem("Review Project Learned Rules...", true, ReviewProjectLearnedRules),
                MakeMenuItem("Review Global Learned Rules...", true, ReviewLearnedRules)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Open Page Folder in Explorer", true, () => OpenFolderInExplorer(page.FolderPath)));
        }
        else if (item.Tag is PageTakeoffNode node)
        {
            menu = BuildPageTakeoffContextMenu(node);
        }
        else if (item.Tag is PageOverlayNode overlay)
        {
            menu = BuildPageOverlayContextMenu(overlay);
        }

        return menu;
    }

    private ContextMenu BuildPageTakeoffContextMenu(PageTakeoffNode node)
    {
        var menu = new ContextMenu();
        int selectedCount = SelectedPageTakeoffContextCount(node);
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Select {selectedCount} Linked Takeoffs" : "Select Linked Takeoff",
            true,
            () => SelectLinkedPageTakeoff(node)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Move {selectedCount} Up in Legend" : "Move Up in Legend",
            CanMovePageTakeoffLegendNodes(node, -1),
            () => MovePageTakeoffLegendNodes(node, -1)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Move {selectedCount} Down in Legend" : "Move Down in Legend",
            CanMovePageTakeoffLegendNodes(node, 1),
            () => MovePageTakeoffLegendNodes(node, 1)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Sort Sheet Legend A-Z", CanSortPageLegend(node.Page), () => SortPageLegendByName(node.Page, node.Takeoff.FolderPath)));
        menu.Items.Add(MakeMenuItem("Reset Sheet Legend Order", HasCustomPageLegendOrder(node.Page), () => ResetPageLegendOrder(node.Page, node.Takeoff.FolderPath)));
        return menu;
    }

    private int SelectedPageTakeoffContextCount(PageTakeoffNode anchor) =>
        SelectedPageTakeoffNodes(anchor, fallbackToAnchor: true).Count;

    private void SelectLinkedPageTakeoff(PageTakeoffNode node)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, node.Page.FolderPath))
            OpenPageInActiveTab(node.Page);

        var selectedNodes = SelectedPageTakeoffNodes(node, fallbackToAnchor: true);
        ActivateTakeoffItem(node.Takeoff);
        SelectTakeoffItemSilently(node.Takeoff);
        RefreshActiveTakeoffVisuals();
        UpdateTotalDisplay();
        Dispatcher.InvokeAsync(() => SelectPageTakeoffMeasurementsOnCanvas(selectedNodes, node.Page));
        if (selectedNodes.Count <= 1)
            TxtStatus.Text = $"Linked takeoff selected for {node.Page.Name}: {node.Takeoff.Name}.";
    }

    private void SelectPageTakeoffMeasurementsOnCanvas(PageTakeoffNode node)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, node.Page.FolderPath))
            return;

        SelectTakeoffMeasurementsOnCanvas(node.Takeoff, node.Page.FolderPath, node.Page.Name);
    }

    private void SelectSelectedPageTakeoffMeasurementsOnCanvas(PageTakeoffNode anchor)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, anchor.Page.FolderPath))
            OpenPageInActiveTab(anchor.Page);

        var selectedNodes = SelectedPageTakeoffNodes(anchor, fallbackToAnchor: false);
        SelectPageTakeoffMeasurementsOnCanvas(selectedNodes, anchor.Page);
    }

    private List<PageTakeoffNode> SelectedPageTakeoffNodes(PageTakeoffNode anchor, bool fallbackToAnchor)
    {
        string anchorKey = PageTakeoffSelectionKey(anchor);
        IEnumerable<string> keys = _pageTakeoffMultiSelection.Contains(anchorKey)
            ? _pageTakeoffMultiSelection
            : fallbackToAnchor
                ? [anchorKey]
                : Enumerable.Empty<string>();

        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        if (keySet.Count == 0)
            return [];

        return EnumeratePageTreeItems()
            .Select(item => item.Tag as PageTakeoffNode)
            .Where(node => node != null &&
                           IsSamePageFolder(node.Page.FolderPath, anchor.Page.FolderPath) &&
                           keySet.Contains(PageTakeoffSelectionKey(node)))
            .Select(node => node!)
            .ToList();
    }

    private void SelectPageTakeoffMeasurementsOnCanvas(IReadOnlyList<PageTakeoffNode> nodes, PageInfo page)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
            return;

        var measurements = nodes
            .SelectMany(node => MeasurementsForTakeoffOnPage(node.Takeoff, page.FolderPath))
            .Distinct()
            .ToList();

        _syncingViewportSelectionFromTakeoffItem = true;
        try
        {
            _viewport.SelectMeasurements(measurements);
        }
        finally
        {
            _syncingViewportSelectionFromTakeoffItem = false;
        }

        if (measurements.Count == 0)
            TxtStatus.Text = $"No selected takeoff measurements on {page.Name}.";
        else if (nodes.Count <= 1)
            TxtStatus.Text = measurements.Count == 1
                ? $"Selected {nodes[0].Takeoff.Name} measurement on {page.Name}."
                : $"Selected {measurements.Count} {nodes[0].Takeoff.Name} measurements on {page.Name}.";
        else
            TxtStatus.Text = $"Selected {measurements.Count} measurements from {nodes.Count} linked takeoffs on {page.Name}.";
    }

    private void SelectTakeoffMeasurementsOnCanvas(TakeoffItem item, string pageFolder, string pageName)
    {
        var pageMeasurements = MeasurementsForTakeoffOnPage(item, pageFolder).ToList();
        if (pageMeasurements.Count == 0)
            return;

        _syncingViewportSelectionFromTakeoffItem = true;
        try
        {
            _viewport.SelectMeasurements(pageMeasurements);
        }
        finally
        {
            _syncingViewportSelectionFromTakeoffItem = false;
        }

        TxtStatus.Text = pageMeasurements.Count == 1
            ? $"Selected {item.Name} measurement on {pageName}."
            : $"Selected {pageMeasurements.Count} {item.Name} measurements on {pageName}.";
    }

    private void SelectTakeoffSelectionMeasurementsOnCurrentPage(TreeViewItem? anchor)
    {
        if (_currentPage == null || anchor == null || anchor.Tag is TakeoffMeasurementNode)
            return;

        var selectedItems = TakeoffItemsForSelection(anchor);
        if (selectedItems.Count == 0)
            return;

        var pageMeasurements = selectedItems
            .SelectMany(item => MeasurementsForTakeoffOnPage(item, _currentPage.FolderPath))
            .Distinct()
            .ToList();

        _syncingViewportSelectionFromTakeoffItem = true;
        try
        {
            _viewport.SelectMeasurements(pageMeasurements);
        }
        finally
        {
            _syncingViewportSelectionFromTakeoffItem = false;
        }

        if (pageMeasurements.Count == 0)
        {
            TxtStatus.Text = $"No selected takeoff measurements on {_currentPage.Name}.";
        }
        else if (selectedItems.Count == 1)
        {
            TxtStatus.Text = pageMeasurements.Count == 1
                ? $"Selected {selectedItems[0].Name} measurement on {_currentPage.Name}."
                : $"Selected {pageMeasurements.Count} {selectedItems[0].Name} measurements on {_currentPage.Name}.";
        }
        else
        {
            TxtStatus.Text = $"Selected {pageMeasurements.Count} measurements from {selectedItems.Count} takeoffs on {_currentPage.Name}.";
        }
    }

    private bool CanMovePageTakeoffLegendNode(PageTakeoffNode node, int offset)
    {
        IReadOnlyList<TakeoffItem> ordered = OrderedTakeoffsForPage(node.Page);
        int index = IndexOfTakeoff(ordered, node.Takeoff);
        int target = index + offset;
        return index >= 0 && target >= 0 && target < ordered.Count;
    }

    private bool CanMovePageTakeoffLegendNodes(PageTakeoffNode anchor, int offset)
    {
        var selectedNodes = SelectedPageTakeoffNodes(anchor, fallbackToAnchor: true);
        return CanMovePageTakeoffLegendNodes(selectedNodes, anchor.Page, offset);
    }

    private bool CanMovePageTakeoffLegendNodes(IReadOnlyList<PageTakeoffNode> selectedNodes, PageInfo page, int offset)
    {
        if (offset == 0 || selectedNodes.Count == 0)
            return false;

        var ordered = OrderedTakeoffsForPage(page).ToList();
        if (ordered.Count <= 1)
            return false;

        var selectedKeys = selectedNodes
            .Select(node => TakeoffLegendOrderKey(node.Takeoff))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedKeys.Count == 0 || selectedKeys.Count >= ordered.Count)
            return false;

        if (offset < 0)
        {
            for (int i = 1; i < ordered.Count; i++)
            {
                if (selectedKeys.Contains(TakeoffLegendOrderKey(ordered[i])) &&
                    !selectedKeys.Contains(TakeoffLegendOrderKey(ordered[i - 1])))
                    return true;
            }
        }
        else
        {
            for (int i = 0; i < ordered.Count - 1; i++)
            {
                if (selectedKeys.Contains(TakeoffLegendOrderKey(ordered[i])) &&
                    !selectedKeys.Contains(TakeoffLegendOrderKey(ordered[i + 1])))
                    return true;
            }
        }

        return false;
    }

    private void MovePageTakeoffLegendNode(PageTakeoffNode node, int offset)
    {
        var ordered = OrderedTakeoffsForPage(node.Page).ToList();
        int index = IndexOfTakeoff(ordered, node.Takeoff);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= ordered.Count)
            return;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        SavePageLegendOrder(node.Page, ordered);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectPageTakeoffNodeSilently(node.Page.FolderPath, node.Takeoff.FolderPath);
        TxtStatus.Text = offset < 0
            ? $"Moved {node.Takeoff.Name} up in {node.Page.Name} legend."
            : $"Moved {node.Takeoff.Name} down in {node.Page.Name} legend.";
    }

    private void MovePageTakeoffLegendNodes(PageTakeoffNode anchor, int offset)
    {
        var selectedNodes = SelectedPageTakeoffNodes(anchor, fallbackToAnchor: true);
        if (selectedNodes.Count <= 1)
        {
            MovePageTakeoffLegendNode(anchor, offset);
            return;
        }

        if (!CanMovePageTakeoffLegendNodes(selectedNodes, anchor.Page, offset))
            return;

        var ordered = OrderedTakeoffsForPage(anchor.Page).ToList();
        var selectedKeys = selectedNodes
            .Select(node => TakeoffLegendOrderKey(node.Takeoff))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (offset < 0)
        {
            for (int i = 1; i < ordered.Count; i++)
            {
                string currentKey = TakeoffLegendOrderKey(ordered[i]);
                string previousKey = TakeoffLegendOrderKey(ordered[i - 1]);
                if (selectedKeys.Contains(currentKey) && !selectedKeys.Contains(previousKey))
                    (ordered[i - 1], ordered[i]) = (ordered[i], ordered[i - 1]);
            }
        }
        else
        {
            for (int i = ordered.Count - 2; i >= 0; i--)
            {
                string currentKey = TakeoffLegendOrderKey(ordered[i]);
                string nextKey = TakeoffLegendOrderKey(ordered[i + 1]);
                if (selectedKeys.Contains(currentKey) && !selectedKeys.Contains(nextKey))
                    (ordered[i], ordered[i + 1]) = (ordered[i + 1], ordered[i]);
            }
        }

        SavePageLegendOrder(anchor.Page, ordered);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectPageTakeoffNodeSilently(anchor.Page.FolderPath, anchor.Takeoff.FolderPath);
        ApplyPagesMultiSelectionVisuals();
        TxtStatus.Text = offset < 0
            ? $"Moved {selectedNodes.Count} linked takeoffs up in {anchor.Page.Name} legend."
            : $"Moved {selectedNodes.Count} linked takeoffs down in {anchor.Page.Name} legend.";
    }

    private bool CanSortPageLegend(PageInfo page) =>
        TakeoffsForPage(page.FolderPath).Skip(1).Any();

    private static bool HasCustomPageLegendOrder(PageInfo page) =>
        page.LegendTakeoffOrder.Count > 0;

    private void SortPageLegendByName(PageInfo page, string? selectTakeoffFolder = null)
    {
        var ordered = TakeoffsForPage(page.FolderPath)
            .OrderBy(takeoff => takeoff.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(takeoff => MeasurementTypeTitle(takeoff.MeasurementType), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ordered.Count <= 1)
            return;

        SavePageLegendOrder(page, ordered);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectLegendOrderResult(page, selectTakeoffFolder);
        TxtStatus.Text = $"Sorted {page.Name} legend A-Z.";
    }

    private void ResetPageLegendOrder(PageInfo page, string? selectTakeoffFolder = null)
    {
        page.LegendTakeoffOrder = [];
        if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
            _currentPage.LegendTakeoffOrder = [];
        OurPlaneCoreJobStore.SavePageLegendTakeoffOrder(page.FolderPath, []);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectLegendOrderResult(page, selectTakeoffFolder);
        TxtStatus.Text = $"Reset {page.Name} legend order.";
    }

    private void SelectLegendOrderResult(PageInfo page, string? selectTakeoffFolder)
    {
        if (string.IsNullOrWhiteSpace(selectTakeoffFolder))
            return;

        SelectPageTakeoffNodeSilently(page.FolderPath, selectTakeoffFolder);
    }

    private bool CanDropPageTakeoffLegend(PageTakeoffLegendDrag drag, TreeViewItem? targetItem)
    {
        if (targetItem?.Tag is not PageTakeoffNode targetNode)
            return false;
        if (!IsSamePageFolder(drag.PageFolder, targetNode.Page.FolderPath))
            return false;

        var draggedFolders = drag.TakeoffFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (draggedFolders.Count == 0)
            return false;
        if (draggedFolders.Contains(targetNode.Takeoff.FolderPath, StringComparer.OrdinalIgnoreCase))
            return false;

        IReadOnlyList<TakeoffItem> ordered = OrderedTakeoffsForPage(targetNode.Page);
        return draggedFolders.All(folder => IndexOfTakeoffByFolder(ordered, folder) >= 0) &&
               IndexOfTakeoff(ordered, targetNode.Takeoff) >= 0;
    }

    private void UpdatePageTakeoffLegendDropCue(PageTakeoffLegendDrag drag, TreeViewItem targetItem, Point targetPosition)
    {
        bool dropAfter = IsPageTakeoffLegendDropAfter(targetItem, targetPosition);
        if (ReferenceEquals(_pageTakeoffLegendDropTarget, targetItem) &&
            _pageTakeoffLegendDropAfter == dropAfter)
        {
            return;
        }

        _pageTakeoffLegendDropTarget = targetItem;
        _pageTakeoffLegendDropAfter = dropAfter;
        ApplyPagesMultiSelectionVisuals();
        if (targetItem.Tag is PageTakeoffNode node)
        {
            IReadOnlyList<TakeoffItem> ordered = OrderedTakeoffsForPage(node.Page);
            int targetIndex = IndexOfTakeoff(ordered, node.Takeoff);
            int insertPosition = Math.Clamp(targetIndex + (dropAfter ? 2 : 1), 1, Math.Max(1, ordered.Count));
            string countText = drag.TakeoffFolders.Count == 1 ? "1 linked takeoff" : $"{drag.TakeoffFolders.Count} linked takeoffs";
            TxtStatus.Text = $"Drop {countText} {(dropAfter ? "below" : "above")} {node.Takeoff.Name} | {node.Page.Name} legend position {insertPosition}.";
        }
    }

    private void ClearPageTakeoffLegendDropCue()
    {
        if (_pageTakeoffLegendDropTarget == null)
            return;

        _pageTakeoffLegendDropTarget = null;
        ApplyPagesMultiSelectionVisuals();
    }

    private static bool IsPageTakeoffLegendDropAfter(TreeViewItem targetItem, Point targetPosition) =>
        targetPosition.Y > Math.Max(1.0, targetItem.ActualHeight) / 2.0;

    private void DropPageTakeoffLegend(PageTakeoffLegendDrag drag, TreeViewItem targetItem, Point targetPosition)
    {
        if (!CanDropPageTakeoffLegend(drag, targetItem) ||
            targetItem.Tag is not PageTakeoffNode targetNode)
        {
            return;
        }

        var draggedKeys = drag.TakeoffFolders
            .Select(NormalizeTakeoffLegendOrderKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (draggedKeys.Count == 0)
            return;

        var ordered = OrderedTakeoffsForPage(targetNode.Page).ToList();
        var moved = ordered
            .Where(takeoff => draggedKeys.Contains(TakeoffLegendOrderKey(takeoff)))
            .ToList();
        if (moved.Count == 0)
            return;

        ordered.RemoveAll(takeoff => draggedKeys.Contains(TakeoffLegendOrderKey(takeoff)));
        int targetIndex = IndexOfTakeoff(ordered, targetNode.Takeoff);
        if (targetIndex < 0)
            return;

        bool insertAfter = IsPageTakeoffLegendDropAfter(targetItem, targetPosition);
        int insertIndex = targetIndex + (insertAfter ? 1 : 0);
        insertIndex = Math.Clamp(insertIndex, 0, ordered.Count);
        ordered.InsertRange(insertIndex, moved);

        SavePageLegendOrder(targetNode.Page, ordered);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectPageTakeoffNodeSilently(targetNode.Page.FolderPath, moved[0].FolderPath);
        ApplyPagesMultiSelectionVisuals();
        int firstPosition = insertIndex + 1;
        TxtStatus.Text = moved.Count == 1
            ? $"Moved {moved[0].Name} to {targetNode.Page.Name} legend position {firstPosition}."
            : $"Moved {moved.Count} linked takeoffs to {targetNode.Page.Name} legend positions {firstPosition}-{firstPosition + moved.Count - 1}.";
    }

    private bool TryGetPagesPositionDropCue(
        PagesClipboard payload,
        TreeViewItem? targetItem,
        bool copy,
        DragEventArgs e,
        out bool after,
        out bool canDrop,
        out string status)
    {
        after = false;
        canDrop = false;
        status = "";

        if (copy || _currentJob == null || payload.Entries.Count == 0 || targetItem == null)
            return false;

        string? targetPath = GetPagesNodePath(targetItem);
        if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
            return false;

        Point targetPoint = e.GetPosition(targetItem);
        bool dropOutOfTargetFolder = targetItem.Tag is PageFolderNode && AreDirectPageChildren(payload, targetPath);
        if (targetItem.Tag is PageFolderNode && !dropOutOfTargetFolder && !IsPagesPositionEdgeDrop(targetItem, targetPoint))
            return false;

        after = dropOutOfTargetFolder || IsPagesPositionDropAfter(targetItem, targetPoint);
        canDrop = CanDropPagesToPosition(payload, targetPath, after);
        string targetName = OurPlaneCoreJobStore.DisplayName(targetPath);
        string position = after ? "below" : "above";
        status = canDrop
            ? dropOutOfTargetFolder
                ? $"Move {payload.Entries.Count} page/folder item(s) out below {targetName}."
                : $"Move {payload.Entries.Count} page/folder item(s) {position} {targetName}."
            : "Cannot reorder here. Drop on a sibling position in the same Pages folder.";
        return true;
    }

    private static bool AreDirectPageChildren(PagesClipboard payload, string folderPath)
    {
        if (payload.Entries.Count == 0)
            return false;

        return payload.Entries.All(entry =>
            string.Equals(
                Path.GetDirectoryName(entry.SourcePath) ?? "",
                folderPath,
                StringComparison.OrdinalIgnoreCase));
    }

    private bool CanDropPagesToPosition(PagesClipboard payload, string targetPath, bool after)
    {
        if (_currentJob == null || payload.Entries.Count == 0 || string.IsNullOrWhiteSpace(targetPath))
            return false;

        string targetParent = Path.GetDirectoryName(targetPath) ?? "";
        if (string.IsNullOrWhiteSpace(targetParent) ||
            !IsPathInsidePagesRoot(targetParent) ||
            !Directory.Exists(targetParent))
        {
            return false;
        }

        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Any(path => string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (paths.All(path => string.Equals(Path.GetDirectoryName(path) ?? "", targetParent, StringComparison.OrdinalIgnoreCase)))
            return OurPlaneCoreJobStore.CanMoveSiblingsToPosition(paths, targetPath, after);

        return CanDropInto(payload, targetParent, PagesClipboardMode.Cut);
    }

    private static bool IsPagesPositionDropAfter(TreeViewItem item, Point targetPoint) =>
        targetPoint.Y >= PagesNodeHeaderDropHeight(item) / 2.0;

    private static bool IsPagesPositionEdgeDrop(TreeViewItem item, Point targetPoint)
    {
        double height = PagesNodeHeaderDropHeight(item);
        if (targetPoint.Y < 0 || targetPoint.Y > height)
            return false;

        double edge = Math.Min(5.0, Math.Max(3.0, height * 0.18));
        return targetPoint.Y <= edge || targetPoint.Y >= height - edge;
    }

    private static double PagesNodeHeaderDropHeight(TreeViewItem item)
    {
        double itemHeight = Math.Max(1.0, item.ActualHeight);
        if (item.Header is FrameworkElement header && header.ActualHeight > 0)
            return Math.Min(itemHeight, Math.Max(18.0, header.ActualHeight + 6.0));

        return Math.Min(itemHeight, 28.0);
    }

    private void UpdatePagesPositionDropCue(TreeViewItem? targetItem, bool after, bool canDrop, string status)
    {
        if (ReferenceEquals(_pagesPositionDropTarget, targetItem) &&
            _pagesPositionDropAfter == after &&
            _pagesPositionDropAllowed == canDrop &&
            string.Equals(_pagesPositionDropStatus, status, StringComparison.Ordinal))
        {
            return;
        }

        _pagesPositionDropTarget = targetItem;
        _pagesPositionDropAfter = after;
        _pagesPositionDropAllowed = canDrop;
        _pagesPositionDropStatus = status;
        ApplyPagesMultiSelectionVisuals();
        if (!string.IsNullOrWhiteSpace(status))
            TxtStatus.Text = status;
    }

    private void ClearPagesPositionDropCue()
    {
        if (_pagesPositionDropTarget == null && string.IsNullOrEmpty(_pagesPositionDropStatus))
            return;

        _pagesPositionDropTarget = null;
        _pagesPositionDropAfter = false;
        _pagesPositionDropAllowed = false;
        _pagesPositionDropStatus = "";
        ApplyPagesMultiSelectionVisuals();
    }

    private void DropPagesPosition(PagesClipboard payload, TreeViewItem targetItem, bool after)
    {
        string? targetPath = GetPagesNodePath(targetItem);
        if (string.IsNullOrWhiteSpace(targetPath))
            return;

        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Count == 0)
            return;

        try
        {
            string targetParent = Path.GetDirectoryName(targetPath) ?? "";
            if (string.IsNullOrWhiteSpace(targetParent))
                return;

            var changed = new List<string>();
            bool reloadActiveTab = false;
            if (paths.All(path => string.Equals(Path.GetDirectoryName(path) ?? "", targetParent, StringComparison.OrdinalIgnoreCase)))
            {
                if (!OurPlaneCoreJobStore.MoveSiblingsToPosition(paths, targetPath, after))
                    return;
                changed.AddRange(paths);
            }
            else
            {
                foreach (var entry in payload.Entries)
                {
                    if (string.Equals(Path.GetDirectoryName(entry.SourcePath) ?? "", targetParent, StringComparison.OrdinalIgnoreCase))
                    {
                        changed.Add(entry.SourcePath);
                        continue;
                    }

                    if (!CanDropInto(new PagesClipboard([entry], PagesClipboardMode.Cut), targetParent, PagesClipboardMode.Cut))
                        continue;

                    string changedPath = OurPlaneCoreJobStore.MoveNode(entry.SourcePath, targetParent);
                    reloadActiveTab = UpdatePageReferencesForMovedPath(entry.SourcePath, changedPath) || reloadActiveTab;
                    changed.Add(changedPath);
                }

                if (changed.Count == 0 ||
                    !OurPlaneCoreJobStore.MoveSiblingsToPosition(changed, targetPath, after))
                {
                    return;
                }

                _pagesClipboard = null;
            }

            _pagesMultiSelection.Clear();
            foreach (string changedPath in changed)
                _pagesMultiSelection.Add(changedPath);

            string selectPath = changed[0];
            QueuePagesTreeDropRefresh(selectPath, reloadActiveTab);
            TxtStatus.Text = changed.Count == 1
                ? $"Moved page/folder {(after ? "below" : "above")} {OurPlaneCoreJobStore.DisplayName(targetPath)}."
                : $"Moved {changed.Count} page/folder items {(after ? "below" : "above")} {OurPlaneCoreJobStore.DisplayName(targetPath)}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Reorder Pages", ex);
        }
    }

    private void QueuePagesTreeDropRefresh(string selectPath, bool reloadActiveTab)
    {
        _pendingPagesTreeDropRefreshPath = selectPath;
        _pendingPagesTreeDropReloadActiveTab = _pendingPagesTreeDropReloadActiveTab || reloadActiveTab;
        int version = ++_pendingPagesTreeDropRefreshVersion;

        Dispatcher.InvokeAsync(() =>
        {
            if (version == _pendingPagesTreeDropRefreshVersion)
                FlushPendingPagesTreeDropRefresh();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void FlushPendingPagesTreeDropRefresh()
    {
        if (string.IsNullOrWhiteSpace(_pendingPagesTreeDropRefreshPath))
            return;

        string selectPath = _pendingPagesTreeDropRefreshPath;
        bool reloadActiveTab = _pendingPagesTreeDropReloadActiveTab;
        _pendingPagesTreeDropRefreshPath = null;
        _pendingPagesTreeDropReloadActiveTab = false;

        ClearPagesPositionDropCue();
        ReloadPagesTree(selectPath);
        ReloadActivePageTabAfterPathChange(reloadActiveTab);
        PagesTree.Items.Refresh();
        PagesTree.UpdateLayout();
        RevealPageNodeAfterDrop(selectPath);
    }

    private void RevealPageNodeAfterDrop(string folderPath)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!Directory.Exists(folderPath))
                return;

            SelectPageTreeNodeSilently(folderPath);
            if (FindPageTreeItemByFolder(folderPath) is { } item)
                BringPageTreeItemIntoCenteredView(item);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private static int IndexOfTakeoff(IReadOnlyList<TakeoffItem> takeoffs, TakeoffItem target)
    {
        for (int i = 0; i < takeoffs.Count; i++)
        {
            if (string.Equals(takeoffs[i].FolderPath, target.FolderPath, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static int IndexOfTakeoffByFolder(IReadOnlyList<TakeoffItem> takeoffs, string folderPath)
    {
        for (int i = 0; i < takeoffs.Count; i++)
        {
            if (string.Equals(takeoffs[i].FolderPath, folderPath, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void SelectPageTakeoffNodeSilently(string pageFolder, string takeoffFolder)
    {
        if (FindPageTakeoffTreeItem(pageFolder, takeoffFolder) is not { } item)
            return;

        _syncingPageTreeSelection = true;
        try
        {
            ExpandTreeItemAndAncestorsWithoutTracking(item);
            item.IsSelected = true;
            ApplyPagesMultiSelectionVisuals();
            BringPageTreeItemIntoCenteredView(item);
        }
        finally
        {
            _syncingPageTreeSelection = false;
        }
    }

    private TreeViewItem? FindPageTakeoffTreeItem(string pageFolder, string takeoffFolder)
    {
        foreach (TreeViewItem item in EnumeratePageTreeItems())
        {
            if (item.Tag is not PageTakeoffNode node)
                continue;

            if (IsSamePageFolder(node.Page.FolderPath, pageFolder) &&
                string.Equals(node.Takeoff.FolderPath, takeoffFolder, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
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

    private static MenuItem MakeMenuItem(string header, bool isEnabled, Action action)
    {
        var item = new MenuItem { Header = header, IsEnabled = isEnabled };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem MakeSubmenu(string header, params object[] children)
    {
        var item = new MenuItem { Header = header };
        foreach (object child in children)
            item.Items.Add(child);
        return item;
    }

    private void NewPageFolder(TreeViewItem item)
    {
        if (item.Tag is not PageFolderNode folder || !IsPathInsidePagesRoot(folder.FolderPath))
            return;

        string? name = ShowInputDialog("Folder name:", "New Folder", "New Folder");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            string created = OurPlaneCoreJobStore.CreateFolder(folder.FolderPath, name);
            ReloadPagesTree(created);
            TxtStatus.Text = $"Created folder: {OurPlaneCoreJobStore.DisplayName(created)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("New Folder", ex);
        }
    }

    private void RenamePagesNode(TreeViewItem item)
    {
        if (IsRootPagesNode(item)) return;
        string? path = GetPagesNodePath(item);
        if (path == null || !IsPathInsidePagesRoot(path, allowRoot: false)) return;

        string currentName = OurPlaneCoreJobStore.DisplayName(path);
        string? name = ShowInputDialog("New name:", currentName, item.Tag is PageInfo ? "Rename Page" : "Rename Folder");
        if (string.IsNullOrWhiteSpace(name) || name == currentName) return;

        try
        {
            string renamed = item.Tag is PageInfo
                ? OurPlaneCoreJobStore.RenamePageAllowDuplicateName(path, name)
                : OurPlaneCoreJobStore.RenameNode(path, name);
            bool reloadActiveTab = UpdatePageReferencesForMovedPath(path, renamed);
            ReloadPagesTree(renamed);
            ReloadActivePageTabAfterPathChange(reloadActiveTab);
            TxtStatus.Text = $"Renamed to: {OurPlaneCoreJobStore.DisplayName(renamed)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Rename", ex);
        }
    }

    private void DeletePagesNode(TreeViewItem item)
    {
        var entries = GetSelectedPageEntries(item);
        if (entries.Count == 0) return;

        string message;
        if (entries.Count == 1)
        {
            string path = entries[0].SourcePath;
            bool isPage = entries[0].IsPage;
            bool hasChildren = Directory.EnumerateFileSystemEntries(path).Any();
            string name = OurPlaneCoreJobStore.DisplayName(path);
            message = isPage
                ? $"Delete page '{name}'?"
                : hasChildren
                    ? $"Delete folder '{name}' and everything inside it?"
                    : $"Delete empty folder '{name}'?";
        }
        else
        {
            message = $"Delete {entries.Count} selected page/folder item(s)?";
        }

        var result = MessageBox.Show(message, "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var deletedNames = entries.Select(e => OurPlaneCoreJobStore.DisplayName(e.SourcePath)).ToList();
        var parents = entries
            .Select(e => Path.GetDirectoryName(e.SourcePath) ?? _currentJob?.PagesRoot ?? "")
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        string? selectAfter = parents.FirstOrDefault() ?? _currentJob?.PagesRoot;
        try
        {
            foreach (var entry in entries)
                ClearCurrentPageIfAffected(entry.SourcePath);

            foreach (var entry in entries)
                DeleteDirectoryToRecycle(entry.SourcePath);

            if (_pagesClipboard != null && entries.Any(e =>
                    _pagesClipboard.Entries.Any(c =>
                        OurPlaneCoreJobStore.IsSameOrDescendant(e.SourcePath, c.SourcePath))))
                _pagesClipboard = null;

            foreach (string parent in parents.Where(Directory.Exists))
                OurPlaneCoreJobStore.NormalizeOrder(parent);
            _pagesMultiSelection.Clear();
            ReloadPagesTree(selectAfter);
            TxtStatus.Text = entries.Count == 1
                ? $"Deleted: {deletedNames[0]}"
                : $"Deleted {entries.Count} items.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Delete", ex);
        }
    }

    private void CopyCutPagesNode(TreeViewItem item, PagesClipboardMode mode)
    {
        var entries = GetSelectedPageEntries(item);
        if (entries.Count == 0) return;

        _pagesClipboard = new PagesClipboard(entries, mode);
        string verb = mode == PagesClipboardMode.Copy ? "Copied" : "Cut";
        TxtStatus.Text = entries.Count == 1
            ? $"{verb}: {OurPlaneCoreJobStore.DisplayName(entries[0].SourcePath)}"
            : $"{verb} {entries.Count} items.";
    }

    private void PasteIntoSelectedTarget(TreeViewItem item)
    {
        string? targetFolder = GetPasteTargetFolder(item);
        if (targetFolder == null) return;
        PasteIntoFolder(targetFolder);
    }

    private void PasteIntoFolder(string targetFolder)
    {
        if (_pagesClipboard == null || !CanPasteInto(targetFolder)) return;

        RunDrop(_pagesClipboard, targetFolder, _pagesClipboard.Mode);
    }

    private void RunDrop(PagesClipboard payload, string targetFolder, PagesClipboardMode mode)
    {
        bool wasCut = mode == PagesClipboardMode.Cut;

        try
        {
            var pastedItems = new List<string>();
            bool reloadActiveTab = false;
            foreach (var entry in payload.Entries)
            {
                string source = entry.SourcePath;
                if (!Directory.Exists(source))
                    continue;
                if (!CanDropInto(new PagesClipboard([entry], mode), targetFolder, mode))
                    continue;

                string pasted;
                if (wasCut)
                {
                    pasted = OurPlaneCoreJobStore.MoveNode(source, targetFolder);
                    reloadActiveTab = UpdatePageReferencesForMovedPath(source, pasted) || reloadActiveTab;
                }
                else
                {
                    pasted = OurPlaneCoreJobStore.CopyNode(source, targetFolder);
                }

                pastedItems.Add(pasted);
            }

            if (wasCut)
                _pagesClipboard = null;
            if (pastedItems.Count == 0)
                return;

            _pagesMultiSelection.Clear();
            foreach (string pasted in pastedItems)
                _pagesMultiSelection.Add(pasted);
            QueuePagesTreeDropRefresh(pastedItems[0], reloadActiveTab);
            TxtStatus.Text = pastedItems.Count == 1
                ? $"{(wasCut ? "Moved" : "Pasted")}: {OurPlaneCoreJobStore.DisplayName(pastedItems[0])}"
                : $"{(wasCut ? "Moved" : "Pasted")} {pastedItems.Count} items.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Paste", ex);
        }
    }

    private void DuplicatePageNode(TreeViewItem item)
    {
        if (item.Tag is not PageInfo page || !IsPathInsidePagesRoot(page.FolderPath, allowRoot: false))
            return;

        try
        {
            string duplicated = OurPlaneCoreJobStore.DuplicatePage(page.FolderPath);
            ReloadPagesTree(duplicated);
            TxtStatus.Text = $"Duplicated page: {OurPlaneCoreJobStore.DisplayName(duplicated)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Duplicate Page", ex);
        }
    }

    private void MovePagesNode(TreeViewItem item, int offset)
    {
        MovePagesNodes(item, offset);
    }

    private bool CanMovePagesNodes(TreeViewItem item, int offset)
    {
        var paths = GetSelectedPageEntries(item)
            .Select(entry => entry.SourcePath)
            .ToList();
        return OurPlaneCoreJobStore.CanMoveSiblings(paths, offset);
    }

    private void MovePagesNodes(TreeViewItem item, int offset)
    {
        if (IsRootPagesNode(item)) return;
        string? path = GetPagesNodePath(item);
        if (path == null || !IsPathInsidePagesRoot(path, allowRoot: false)) return;

        var entries = GetSelectedPageEntries(item);
        var paths = entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Count == 0)
            return;

        try
        {
            if (OurPlaneCoreJobStore.MoveSiblings(paths, offset))
            {
                _pagesMultiSelection.Clear();
                foreach (string selectedPath in paths)
                    _pagesMultiSelection.Add(selectedPath);

                ReloadPagesTree(paths[0]);
                TxtStatus.Text = paths.Count == 1
                    ? (offset < 0 ? "Moved up." : "Moved down.")
                    : (offset < 0 ? $"Moved {paths.Count} page/folder items up." : $"Moved {paths.Count} page/folder items down.");
            }
        }
        catch (Exception ex)
        {
            ShowOperationError(offset < 0 ? "Move Up" : "Move Down", ex);
        }
    }

    private void SortFolderChildren(TreeViewItem item, bool descending)
    {
        if (item.Tag is not PageFolderNode folder || !IsPathInsidePagesRoot(folder.FolderPath))
            return;

        try
        {
            OurPlaneCoreJobStore.SortChildren(folder.FolderPath, descending);
            ReloadPagesTree(folder.FolderPath);
            TxtStatus.Text = descending ? "Sorted children Z-A." : "Sorted children A-Z.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort Children", ex);
        }
    }

    private void MovePageToFolder(TreeViewItem item)
    {
        if (item.Tag is not PageInfo page || _currentJob == null)
            return;

        string? target = SelectFolder("Select destination folder inside Pages", _currentJob.PagesRoot);
        if (target == null) return;
        target = Path.GetFullPath(target);

        if (!IsPathInsidePagesRoot(target) || OurPlaneCoreJobStore.IsPageFolder(target))
        {
            MessageBox.Show("Choose a folder inside the current job's Pages tree.",
                            "Move to Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.Equals(Path.GetDirectoryName(page.FolderPath), target, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            string moved = OurPlaneCoreJobStore.MoveNode(page.FolderPath, target);
            bool reloadActiveTab = UpdatePageReferencesForMovedPath(page.FolderPath, moved);
            ReloadPagesTree(moved);
            ReloadActivePageTabAfterPathChange(reloadActiveTab);
            TxtStatus.Text = $"Moved page to: {OurPlaneCoreJobStore.DisplayName(target)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Move to Folder", ex);
        }
    }

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

    private async void BtnQueuePdfMetadataFallback_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            async () =>
            {
                if (await TryRunSheetManagerPdfMetadataFallbackAsync())
                    return;

                if (GetSelectedPdfAutomationTarget("AI Fill") is { } item)
                    QueuePdfMetadataFallback(item);
            },
            "AI Fill failed.",
            "AI Fill");
    }

    private async Task<bool> TryRunSheetManagerPdfMetadataFallbackAsync()
    {
        if (WorkspaceTabs.SelectedItem is not TabItem tab ||
            !string.Equals(tab.Tag?.ToString(), "SheetManager", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before PDF automation.";
            return true;
        }

        IReadOnlyList<PageInfo> pages = SheetManagerAiFillPages();

        if (pages.Count == 0)
        {
            MessageBox.Show("No PDF pages found in Sheet Manager.", "AI Fill",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        if (string.IsNullOrWhiteSpace(ReadOpenAiApiKey()))
        {
            MessageBox.Show(
                "Set OPENAI_API_KEY in Windows environment or OpenAI Settings, then run AI Fill again.",
                "AI Fill",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            TxtStatus.Text = "AI Fill needs OPENAI_API_KEY before it can run GPT metadata.";
            return true;
        }

        SheetManagerGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SheetManagerGrid.CommitEdit(DataGridEditingUnit.Row, true);

        PdfMetadataFallbackQueueResult queueResult = QueuePdfMetadataFallback(pages, showMessage: false);
        IReadOnlyList<SmartAiRequest> requests = RunnableMetadataFallbackRequests(_currentJob, pages);

        if (requests.Count == 0)
        {
            ShowSheetManagerAiFillNoRequests(queueResult);
            return true;
        }

        TxtStatus.Text = $"AI Fill running GPT metadata for {requests.Count} sheet(s)...";
        var runResult = await RunAndSaveSheetMetadataFallbackRequestsAsync(requests, queueResult.Errors);

        RefreshSheetManager();

        string summary =
            $"AI Fill complete. Queued: {queueResult.Queued}. Ran: {runResult.Ran}. " +
            $"Updated metadata: {runResult.Saved}. Skipped: {queueResult.Skipped}. " +
            $"Failed: {runResult.Errors.Count}.";
        ShowSheetManagerAiFillErrors(summary, runResult.Errors);

        TxtStatus.Text = summary;
        return true;
    }

    private IReadOnlyList<PageInfo> SheetManagerAiFillPages()
    {
        IReadOnlyList<PageInfo> pages = SelectedSheetManagerPages();
        if (pages.Count > 0)
            return pages;

        return SheetManagerRows()
            .Select(row => OurPlaneCoreJobStore.TryReadPage(row.PageFolder))
            .Where(page => page != null)
            .Cast<PageInfo>()
            .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static IReadOnlyList<SmartAiRequest> RunnableMetadataFallbackRequests(
        OurPlaneCoreJob job,
        IReadOnlyList<PageInfo> pages) =>
        MetadataFallbackRequestsForPages(job, pages)
            .Where(request => MetadataFallbackRequestStillNeeded(job, request, pages))
            .Where(request => IsRunnableAiStatus(request.Status) || HasDoneAiResponse(job, request))
            .GroupBy(request => request.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

    private void ShowSheetManagerAiFillNoRequests(PdfMetadataFallbackQueueResult queueResult)
    {
        string message = BuildPdfMetadataFallbackQueueMessage(queueResult);
        MessageBox.Show(
            message.Length == 0 ? "No GPT metadata fallback was needed for the selected sheets." : message,
            "AI Fill",
            MessageBoxButton.OK,
            queueResult.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        TxtStatus.Text = queueResult.Queued > 0
            ? "AI Fill queued metadata fallback, but no runnable request was found."
            : "AI Fill: no GPT metadata fallback needed.";
    }

    private async Task<(int Ran, int Saved, List<string> Errors)> RunAndSaveSheetMetadataFallbackRequestsAsync(
        IReadOnlyList<SmartAiRequest> requests,
        IEnumerable<string> initialErrors)
    {
        int ran = 0;
        int saved = 0;
        var errors = new List<string>(initialErrors);
        OurPlaneCoreJob job = _currentJob!;

        foreach (SmartAiRequest request in requests)
        {
            SmartAiRequest current = SmartContextStore.LoadAiRequest(job, request.Id) ?? request;
            if (IsRunnableAiStatus(current.Status))
            {
                string statusBeforeRun = current.Status;
                await RunAiRequestAsync(current);
                current = SmartContextStore.LoadAiRequest(job, request.Id) ?? current;
                if (!string.Equals(statusBeforeRun, current.Status, StringComparison.OrdinalIgnoreCase) ||
                    HasDoneAiResponse(job, current))
                {
                    ran++;
                }
            }

            if (TrySaveSheetMetadataFromFallbackResponse(current, out _, out string error))
                saved++;
            else if (!string.IsNullOrWhiteSpace(error))
                errors.Add($"{current.Page}: {error}");
        }

        return (ran, saved, errors);
    }

    private static void ShowSheetManagerAiFillErrors(string summary, IReadOnlyList<string> errors)
    {
        if (errors.Count == 0)
            return;

        MessageBox.Show(
            summary + Environment.NewLine + string.Join(Environment.NewLine, errors.Take(6)),
            "AI Fill",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
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

    private sealed class PdfMetadataFallbackQueueResult
    {
        public int Queued { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; } = [];
        public List<SmartAiRequest> Requests { get; } = [];
    }

    private void QueuePdfMetadataFallback(TreeViewItem item)
    {
        if (_currentJob == null)
            return;

        var pages = GetPagesForMetadata(item).ToList();
        if (pages.Count == 0)
            return;

        QueuePdfMetadataFallback(pages);
    }

    private PdfMetadataFallbackQueueResult QueuePdfMetadataFallback(IReadOnlyList<PageInfo> pages, bool showMessage = true)
    {
        var result = new PdfMetadataFallbackQueueResult();
        if (_currentJob == null || pages.Count == 0)
            return result;

        foreach (PageInfo page in pages)
        {
            try
            {
                PdfSheetMetadata? metadata = OurPlaneCoreJobStore.ReadSourcePdfMetadata(page.FolderPath);
                if (metadata == null)
                {
                    PdfSheetMetadataService.TryAnalyzeAndSave(_currentJob, page, out metadata, out _);
                }

                if (!PdfSheetMetadataService.NeedsFallback(metadata))
                {
                    result.Skipped++;
                    continue;
                }

                if (HasExistingMetadataFallbackRequest(_currentJob, page))
                {
                    result.Skipped++;
                    continue;
                }

                string cropsRoot = Path.Combine(_currentJob.AIContextRoot, "crops");
                string fileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_sheetmeta_{SafeFileNamePart(page.Name)}.png";
                string cropPath = Path.Combine(cropsRoot, fileName);
                if (!PdfSheetMetadataService.TrySaveFallbackCrop(page, cropPath, out SKRect cropRect, out string error))
                {
                    result.Failed++;
                    result.Errors.Add($"{page.Name}: {error}");
                    continue;
                }

                string relativeCrop = Path.GetRelativePath(_currentJob.AIContextRoot, cropPath);
                string observationText =
                    "GPT fallback requested for PDF sheet metadata." + Environment.NewLine +
                    $"- AI crop: {relativeCrop}" + Environment.NewLine +
                    $"- PDF crop: {FormatPdfRect(cropRect)}" + Environment.NewLine +
                    $"- Page folder: {Path.GetRelativePath(_currentJob.RootPath, page.FolderPath)}" + Environment.NewLine +
                    $"- Current page name: {page.Name}" + Environment.NewLine +
                    $"- Current PDF source: {page.PdfPath}" + Environment.NewLine +
                    $"- Current PDF page: {page.PdfPage + 1}" + Environment.NewLine;

                if (metadata != null)
                {
                    observationText +=
                        "- Deterministic metadata JSON:" + Environment.NewLine +
                        JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }) +
                        Environment.NewLine;
                }

                SmartObservation observation = SmartContextStore.AddObservation(
                    _currentJob,
                    page,
                    "pdf_sheet_metadata_fallback",
                    observationText);
                SmartAiRequest request = SmartContextStore.AddAiRequest(
                    _currentJob,
                    page,
                    observation,
                    "pdf_sheet_metadata_fallback",
                    BuildPdfMetadataFallbackPrompt(page, metadata),
                    relativeCrop,
                    "Read the sheet title block crop and return sheet metadata JSON only.");
                result.Requests.Add(request);
                result.Queued++;
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"{page.Name}: {ex.Message}");
            }
        }

        LoadObservationsInbox();
        if (showMessage)
        {
            MessageBox.Show(
                BuildPdfMetadataFallbackQueueMessage(result),
                "Queue GPT Metadata Fallback",
                MessageBoxButton.OK,
                result.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        TxtStatus.Text = $"Queued GPT metadata fallback for {result.Queued} page(s).";
        return result;
    }

    private static string BuildPdfMetadataFallbackQueueMessage(PdfMetadataFallbackQueueResult result)
    {
        string message = $"Queued: {result.Queued}. Skipped: {result.Skipped}. Failed: {result.Failed}.";
        if (result.Errors.Count > 0)
            message += Environment.NewLine + string.Join(Environment.NewLine, result.Errors.Take(5));
        return message;
    }

    private static IReadOnlyList<SmartAiRequest> MetadataFallbackRequestsForPages(
        OurPlaneCoreJob job,
        IReadOnlyList<PageInfo> pages) =>
        SmartContextStore.LoadAiRequests(job)
            .Where(request => string.Equals(request.Type, "pdf_sheet_metadata_fallback", StringComparison.OrdinalIgnoreCase))
            .Where(request => pages.Any(page => MetadataFallbackRequestMatchesPage(job, request, page)))
            .OrderBy(request => request.CreatedAtUtc, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool HasDoneAiResponse(OurPlaneCoreJob job, SmartAiRequest request)
    {
        SmartAiResponse? response = SmartContextStore.LoadAiResponse(job, request.Id);
        return response != null &&
               string.Equals(response.Status, "done", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(response.OutputText);
    }

    private static bool MetadataFallbackRequestStillNeeded(
        OurPlaneCoreJob job,
        SmartAiRequest request,
        IReadOnlyList<PageInfo> pages) =>
        pages.Any(page =>
            MetadataFallbackRequestMatchesPage(job, request, page) &&
            PdfSheetMetadataService.NeedsFallback(OurPlaneCoreJobStore.ReadSourcePdfMetadata(page.FolderPath)));

    private static bool MetadataFallbackRequestMatchesPage(OurPlaneCoreJob job, SmartAiRequest request, PageInfo page)
    {
        string pageFolder = NormalizePath(page.FolderPath);
        string relativePageFolder = Path.GetRelativePath(job.RootPath, page.FolderPath);
        if (!string.IsNullOrWhiteSpace(request.PageFolder))
        {
            string requestFolder = Path.IsPathFullyQualified(request.PageFolder)
                ? NormalizePath(request.PageFolder)
                : NormalizePath(Path.Combine(job.RootPath, request.PageFolder));

            if (string.Equals(requestFolder, pageFolder, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.PageFolder, relativePageFolder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return string.Equals(request.Page, page.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExistingMetadataFallbackRequest(OurPlaneCoreJob job, PageInfo page)
    {
        string pageFolder = NormalizePath(page.FolderPath);
        string relativePageFolder = Path.GetRelativePath(job.RootPath, page.FolderPath);
        foreach (SmartAiRequest request in SmartContextStore.LoadAiRequests(job))
        {
            if (!string.Equals(request.Type, "pdf_sheet_metadata_fallback", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(request.Status, "failed", StringComparison.OrdinalIgnoreCase))
                continue;

            string requestFolder = "";
            if (!string.IsNullOrWhiteSpace(request.PageFolder))
            {
                requestFolder = Path.IsPathFullyQualified(request.PageFolder)
                    ? NormalizePath(request.PageFolder)
                    : NormalizePath(Path.Combine(job.RootPath, request.PageFolder));
            }

            if (string.Equals(requestFolder, pageFolder, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.PageFolder, relativePageFolder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildPdfMetadataFallbackPrompt(PageInfo page, PdfSheetMetadata? metadata)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Read this construction sheet title-block crop.");
        sb.AppendLine("Return one fenced JSON block only. Do not include prose outside JSON.");
        sb.AppendLine();
        sb.AppendLine("Required JSON shape:");
        sb.AppendLine("{");
        sb.AppendLine("  \"sheet_label\": \"S-100\",");
        sb.AppendLine("  \"sheet_key\": \"s100\",");
        sb.AppendLine("  \"sheet_title\": \"FOUNDATION PLAN\",");
        sb.AppendLine("  \"suffix\": \"f\",");
        sb.AppendLine("  \"skip_scale\": false,");
        sb.AppendLine("  \"selected_scale_text\": \"1/8\\\" = 1'0\\\"\",");
        sb.AppendLine("  \"confidence\": \"gpt-image-high | gpt-image-medium | gpt-image-low\",");
        sb.AppendLine("  \"warnings\": []");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Naming suffix rules:");
        sb.AppendLine("notes=n skip, schedules=sc skip, details=d skip, foundation=f, first=1st, second=2nd, third=3rd, fourth=4th, roof=rf, sections=sec, unit plans=u, wall/partition types=wt, floor types=ft.");
        sb.AppendLine("Sections must be scale eligible. Details/notes/schedules must skip scale.");
        sb.AppendLine("Allowed scales: 1/32\", 3/64\", 1/16\", 3/32\", 1/10\", 1/8\", 3/16\", 1/4\", 3/8\", 1/2\", 3/4\", 1\", 1-1/2\", 3\" = 1'0\", and 1\" = 1\".");
        sb.AppendLine("If unsure, leave fields empty and add a warning.");
        sb.AppendLine();
        sb.AppendLine($"Current page name: {page.Name}");
        if (metadata != null)
        {
            sb.AppendLine("Deterministic PDF metadata before fallback:");
            sb.AppendLine(JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        }

        return sb.ToString();
    }

    private bool CanPasteInto(string? targetFolder)
    {
        return _pagesClipboard != null && CanDropInto(_pagesClipboard, targetFolder, _pagesClipboard.Mode);
    }

    private bool CanDropInto(PagesClipboard payload, string? targetFolder, PagesClipboardMode mode)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(targetFolder))
            return false;
        if (payload.Entries.Count == 0 || !Directory.Exists(targetFolder))
            return false;
        if (!IsPathInsidePagesRoot(targetFolder) || OurPlaneCoreJobStore.IsPageFolder(targetFolder))
            return false;

        bool hasMovableEntry = false;
        foreach (var entry in payload.Entries)
        {
            if (!Directory.Exists(entry.SourcePath))
                return false;
            if (!IsPathInsidePagesRoot(entry.SourcePath, allowRoot: false))
                return false;
            if (OurPlaneCoreJobStore.IsSameOrDescendant(entry.SourcePath, targetFolder))
                return false;

            if (mode == PagesClipboardMode.Cut)
            {
                string sourceParent = Path.GetDirectoryName(entry.SourcePath) ?? "";
                if (!string.Equals(sourceParent, targetFolder, StringComparison.OrdinalIgnoreCase))
                    hasMovableEntry = true;
            }
        }

        if (mode == PagesClipboardMode.Cut && !hasMovableEntry)
            return false;

        return true;
    }

    private string? GetPasteTargetFolder(TreeViewItem item)
    {
        return item.Tag switch
        {
            PageFolderNode folder => folder.FolderPath,
            PageInfo page => Path.GetDirectoryName(page.FolderPath),
            _ => null,
        };
    }

    private static string? GetPagesNodePath(TreeViewItem item)
    {
        return item.Tag switch
        {
            PageFolderNode folder => folder.FolderPath,
            PageInfo page => page.FolderPath,
            _ => null,
        };
    }

    private static string PageTakeoffSelectionKey(PageTakeoffNode node) =>
        $"{NormalizePath(node.Page.FolderPath)}|{NormalizePath(node.Takeoff.FolderPath)}";

    private static string? GetPageTakeoffSelectionKey(TreeViewItem item) =>
        item.Tag is PageTakeoffNode node ? PageTakeoffSelectionKey(node) : null;

    private void SelectPagesRange(string? anchorPath, string targetPath, bool additive)
    {
        var candidates = EnumerateVisibleTreeItems(PagesTree)
            .Where(item => !IsRootPagesNode(item))
            .Select(item => (Item: item, Key: GetPagesNodePath(item)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => (entry.Item, Key: entry.Key!))
            .ToList();

        SelectRangeKeys(candidates, anchorPath, targetPath, _pagesMultiSelection, additive);
    }

    private void SelectPageTakeoffRange(string? anchorKey, string targetKey, string pageFolder, bool additive)
    {
        var candidates = EnumerateVisibleTreeItems(PagesTree)
            .Where(item => item.Tag is PageTakeoffNode node &&
                           IsSamePageFolder(node.Page.FolderPath, pageFolder))
            .Select(item => (Item: item, Key: GetPageTakeoffSelectionKey(item)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => (entry.Item, Key: entry.Key!))
            .ToList();

        SelectRangeKeys(candidates, anchorKey, targetKey, _pageTakeoffMultiSelection, additive);
    }

    private static void SelectRangeKeys(
        IReadOnlyList<(TreeViewItem Item, string Key)> candidates,
        string? anchorKey,
        string targetKey,
        HashSet<string> selection,
        bool additive)
    {
        int targetIndex = FindRangeKeyIndex(candidates, targetKey);
        if (targetIndex < 0)
            return;

        int anchorIndex = string.IsNullOrWhiteSpace(anchorKey)
            ? -1
            : FindRangeKeyIndex(candidates, anchorKey);
        if (anchorIndex < 0)
            anchorIndex = targetIndex;

        if (!additive)
            selection.Clear();

        int start = Math.Min(anchorIndex, targetIndex);
        int end = Math.Max(anchorIndex, targetIndex);
        for (int i = start; i <= end; i++)
            selection.Add(candidates[i].Key);
    }

    private static int FindRangeKeyIndex(IReadOnlyList<(TreeViewItem Item, string Key)> candidates, string key)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(candidates[i].Key, key, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private IReadOnlyList<PagesClipboardEntry> GetSelectedPageEntries(TreeViewItem anchor)
    {
        string? anchorPath = GetPagesNodePath(anchor);
        if (anchorPath == null || IsRootPagesNode(anchor))
            return [];

        IEnumerable<string> paths = _pagesMultiSelection.Contains(anchorPath)
            ? _pagesMultiSelection
            : [anchorPath];

        var entries = paths
            .Where(path => IsPathInsidePagesRoot(path, allowRoot: false))
            .Where(Directory.Exists)
            .Select(path => new PagesClipboardEntry(path, OurPlaneCoreJobStore.IsPageFolder(path)))
            .ToList();

        return NormalizeSelectedEntries(entries);
    }

    private int PageSelectionCount(TreeViewItem anchor) =>
        GetSelectedPageEntries(anchor).Count;

    private static IReadOnlyList<PagesClipboardEntry> NormalizeSelectedEntries(
        IReadOnlyList<PagesClipboardEntry> entries)
    {
        var distinct = entries
            .GroupBy(e => NormalizePath(e.SourcePath), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => NormalizePath(e.SourcePath).Length)
            .ToList();

        var result = new List<PagesClipboardEntry>();
        foreach (var entry in distinct)
        {
            if (result.Any(parent => OurPlaneCoreJobStore.IsSameOrDescendant(parent.SourcePath, entry.SourcePath)))
                continue;
            result.Add(entry);
        }

        return result;
    }

    private void PrunePagesMultiSelection()
    {
        _pagesMultiSelection.RemoveWhere(path =>
            !Directory.Exists(path) || !IsPathInsidePagesRoot(path, allowRoot: false));
    }

    private void ApplyPagesMultiSelectionVisuals()
    {
        Brush? brushOrNull(string key) => Application.Current.Resources[key] as Brush;
        Brush dropOk = brushOrNull("RowDropOkBrush") ?? new SolidColorBrush(Color.FromRgb(204, 245, 218));
        Brush activeLinked = brushOrNull("RowSelectionBrush") ?? new SolidColorBrush(Color.FromRgb(204, 229, 255));
        Brush multiLinked = brushOrNull("RowMultiSelectBrush") ?? new SolidColorBrush(Color.FromRgb(205, 226, 255));
        Brush measuredByActive = brushOrNull("RowActiveBrush") ?? new SolidColorBrush(Color.FromRgb(255, 236, 190));
        Brush rowFg = brushOrNull("RowFlagForegroundBrush") ?? Brushes.Black;

        foreach (TreeViewItem item in EnumeratePageTreeItems())
        {
            string? path = GetPagesNodePath(item);
            bool selected = path != null && !IsRootPagesNode(item) && _pagesMultiSelection.Contains(path);
            string? pageTakeoffKey = GetPageTakeoffSelectionKey(item);
            bool linkedSelected = pageTakeoffKey != null && _pageTakeoffMultiSelection.Contains(pageTakeoffKey);
            item.ClearValue(Control.BorderBrushProperty);
            item.ClearValue(Control.BorderThicknessProperty);

            if (ReferenceEquals(item, _pagesPositionDropTarget))
            {
                item.ClearValue(Control.BackgroundProperty);
                item.ClearValue(Control.ForegroundProperty);
                item.FontWeight = FontWeights.Normal;
                item.BorderBrush = _pagesPositionDropAllowed ? Brushes.SeaGreen : Brushes.IndianRed;
                item.BorderThickness = _pagesPositionDropAfter
                    ? new Thickness(0, 0, 0, 2)
                    : new Thickness(0, 2, 0, 0);
            }
            else if (ReferenceEquals(item, _pageTakeoffLegendDropTarget))
            {
                item.Background = dropOk;
                item.Foreground = rowFg;
                item.FontWeight = FontWeights.Normal;
                item.BorderBrush = Brushes.SeaGreen;
                item.BorderThickness = _pageTakeoffLegendDropAfter
                    ? new Thickness(0, 0, 0, 2)
                    : new Thickness(0, 2, 0, 0);
            }
            else if (IsActivePageTakeoffNode(item))
            {
                item.Background = activeLinked;
                item.Foreground = rowFg;
                item.FontWeight = FontWeights.Normal;
            }
            else if (selected || linkedSelected)
            {
                item.Background = multiLinked;
                item.Foreground = rowFg;
                item.ClearValue(Control.FontWeightProperty);
            }
            else if (IsPageMeasuredByActiveTakeoff(item))
            {
                item.Background = measuredByActive;
                item.Foreground = rowFg;
                item.ClearValue(Control.FontWeightProperty);
            }
            else
            {
                item.ClearValue(Control.BackgroundProperty);
                item.ClearValue(Control.ForegroundProperty);
                item.ClearValue(Control.FontWeightProperty);
            }
        }
    }

    private IEnumerable<TreeViewItem> EnumeratePageTreeItems()
    {
        foreach (TreeViewItem root in PagesTree.Items)
        {
            foreach (TreeViewItem item in EnumeratePageTreeItems(root))
                yield return item;
        }
    }

    private static IEnumerable<TreeViewItem> EnumeratePageTreeItems(TreeViewItem item)
    {
        yield return item;
        foreach (TreeViewItem child in item.Items)
        {
            foreach (TreeViewItem nested in EnumeratePageTreeItems(child))
                yield return nested;
        }
    }

    private static IEnumerable<TreeViewItem> EnumerateVisibleTreeItems(ItemsControl parent)
    {
        foreach (TreeViewItem child in parent.Items.OfType<TreeViewItem>())
        {
            yield return child;
            if (!child.IsExpanded)
                continue;

            foreach (TreeViewItem nested in EnumerateVisibleTreeItems(child))
                yield return nested;
        }
    }

    private static bool IsRootPagesNode(TreeViewItem item) =>
        item.Tag is PageFolderNode { IsRoot: true };

    private bool IsPathInsidePagesRoot(string path, bool allowRoot = true)
    {
        if (_currentJob == null) return false;
        string root = NormalizePath(_currentJob.PagesRoot);
        string full = NormalizePath(path);
        if (allowRoot && string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            return true;
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private void ClearCurrentPageIfAffected(string affectedPath)
    {
        RemovePageTabsForAffectedPath(affectedPath);

        if (_currentPage == null) return;
        if (!OurPlaneCoreJobStore.IsSameOrDescendant(affectedPath, _currentPage.FolderPath))
            return;

        _currentPage = null;
        _currentPdfPath = "";
        _takeoffItems.Clear();
        _activeItem = null;
        TakeoffsTree.Items.Clear();
        _viewport.ClearPage();
    }

    private static void DeleteDirectoryToRecycle(string path)
    {
        FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    private static void OpenFolderInExplorer(string folder)
    {
        if (!Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true,
        });
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsSamePageFolder(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(NormalizePathForCompare(left), NormalizePathForCompare(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePathForCompare(string path)
    {
        string trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0)
            return "";

        try
        {
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return trimmed;
        }
    }

    private void ShowOperationError(string operation, Exception ex)
    {
        MessageBox.Show(ex.Message, operation, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowFolderTemplateErrors(string title, FolderTemplateResult result) =>
        ShowFolderTemplateErrors(title, result.ErrorMessages);

    private void ShowFolderTemplateErrors(string title, IReadOnlyList<string> errors)
    {
        if (errors.Count == 0)
            return;

        string message = string.Join(Environment.NewLine, errors.Take(8));
        if (errors.Count > 8)
            message += Environment.NewLine + $"...and {errors.Count - 8} more.";
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
