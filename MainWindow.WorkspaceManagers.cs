using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private const int SheetManagerAutoRasterDpi = 0;
    private bool _sheetManagerEditableColumnsConfigured;
    private bool _updatingSheetManagerBulkEdit;
    private CancellationTokenSource? _sheetManagerRasterPrepareCts;

    private sealed record SheetManagerRasterReadyBatch(
        IReadOnlyList<PageInfo> FastPages,
        IReadOnlyList<PageInfo> MissingPages,
        int Ready,
        int Source,
        int Already,
        int Failed);

    private void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, WorkspaceTabs))
            return;

        RefreshActiveWorkspaceTab();
    }

    private void RefreshActiveWorkspaceTab()
    {
        if (WorkspaceTabs.SelectedItem is not TabItem tab)
            return;

        switch (tab.Tag?.ToString())
        {
            case "SheetManager":
                RefreshSheetManager();
                break;
            case "TakeoffManager":
                RefreshTakeoffManager();
                break;
            case "ReportBuilder":
                RefreshReportBuilder();
                break;
            case "MaterialsManager":
                RefreshMaterialsManager();
                break;
            case "AiManager":
                RefreshAiManager();
                break;
            case "3DManager":
                RefreshThreeDViewer();
                break;
            case "SettingsManager":
                RefreshSettingsManager();
                break;
        }
    }

    private void BtnSheetManagerRefresh_Click(object sender, RoutedEventArgs e) => RefreshSheetManager();

    private async void BtnSheetManagerAnalyze_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            () => AnalyzeSheetManagerAsync(defaultRename: false, defaultScale: false),
            "Sheet Manager analysis failed.",
            "Sheet Manager");
    }

    private async void BtnSheetManagerAutoName_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            () => AnalyzeSheetManagerAsync(defaultRename: true, defaultScale: false),
            "Sheet Manager Auto Name failed.",
            "Sheet Manager");
    }

    private async void BtnSheetManagerAutoScale_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            () => AnalyzeSheetManagerAsync(defaultRename: false, defaultScale: true),
            "Sheet Manager Auto Scale failed.",
            "Sheet Manager");
    }

    private async void BtnSheetManagerAutoNameScale_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            () => AnalyzeSheetManagerAsync(defaultRename: true, defaultScale: true),
            "Sheet Manager Auto Name + Scale failed.",
            "Sheet Manager");
    }

    private void ConfigureSheetManagerEditableColumns()
    {
        if (_sheetManagerEditableColumnsConfigured)
            return;

        ReplaceSheetManagerTextColumn("Proposed Name", nameof(PdfMetadataPreviewRow.ProposedPageName), 160);
        ReplaceSheetManagerTextColumn("Scale", nameof(PdfMetadataPreviewRow.ProposedScale), 120);
        _sheetManagerEditableColumnsConfigured = true;
    }

    private void ReplaceSheetManagerTextColumn(string header, string bindingPath, double width)
    {
        for (int index = 0; index < SheetManagerGrid.Columns.Count; index++)
        {
            DataGridColumn column = SheetManagerGrid.Columns[index];
            if (column is not DataGridTextColumn ||
                !string.Equals(column.Header?.ToString(), header, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var editorColumn = new DataGridTemplateColumn
            {
                Header = header,
                Width = new DataGridLength(width),
                SortMemberPath = bindingPath,
                CellTemplate = CreateSheetManagerTextBoxTemplate(bindingPath),
            };

            SheetManagerGrid.Columns.RemoveAt(index);
            SheetManagerGrid.Columns.Insert(index, editorColumn);
            return;
        }
    }

    private static DataTemplate CreateSheetManagerTextBoxTemplate(string bindingPath)
    {
        var textBox = new FrameworkElementFactory(typeof(TextBox));
        textBox.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
        textBox.SetValue(TextBox.PaddingProperty, new Thickness(4, 1, 4, 1));
        textBox.SetValue(TextBox.BackgroundProperty, Brushes.Transparent);
        textBox.SetValue(FrameworkElement.TagProperty, bindingPath);
        textBox.SetValue(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        textBox.SetValue(FrameworkElement.MinWidthProperty, 70.0);
        textBox.SetBinding(
            TextBox.TextProperty,
            new Binding(bindingPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
        textBox.AddHandler(UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(SheetManagerTextBox_GotKeyboardFocus));
        textBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(SheetManagerTextBox_TextChanged));
        return new DataTemplate { VisualTree = textBox };
    }

    private static void SheetManagerTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
            textBox.SelectAll();
    }

    private static void SheetManagerTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox ||
            textBox.DataContext is not PdfMetadataPreviewRow editedRow ||
            textBox.Tag is not string bindingPath ||
            Window.GetWindow(textBox) is not MainWindow owner)
        {
            return;
        }

        owner.ApplySheetManagerTextToSelectedRows(editedRow, bindingPath, textBox.Text);
    }

    private void ApplySheetManagerTextToSelectedRows(PdfMetadataPreviewRow editedRow, string bindingPath, string value)
    {
        if (_updatingSheetManagerBulkEdit ||
            SheetManagerGrid.SelectedItems.Count <= 1 ||
            !SheetManagerGrid.SelectedItems.Contains(editedRow))
        {
            return;
        }

        var selectedRows = SheetManagerGrid.SelectedItems
            .OfType<PdfMetadataPreviewRow>()
            .ToList();
        if (selectedRows.Count <= 1)
            return;

        _updatingSheetManagerBulkEdit = true;
        try
        {
            foreach (PdfMetadataPreviewRow row in selectedRows)
            {
                if (string.Equals(bindingPath, nameof(PdfMetadataPreviewRow.ProposedPageName), StringComparison.Ordinal))
                {
                    row.ProposedPageName = value;
                    row.ApplyRename = ShouldApplySheetManagerRename(row, value);
                }
                else if (string.Equals(bindingPath, nameof(PdfMetadataPreviewRow.ProposedScale), StringComparison.Ordinal))
                {
                    row.ProposedScale = value;
                    row.ApplyScale = ShouldApplySheetManagerScale(row, value);
                }
            }
        }
        finally
        {
            _updatingSheetManagerBulkEdit = false;
        }

        string field = string.Equals(bindingPath, nameof(PdfMetadataPreviewRow.ProposedPageName), StringComparison.Ordinal)
            ? "name"
            : "scale";
        TxtStatus.Text = $"Sheet Manager: {field} copied to {selectedRows.Count} selected row(s).";
    }

    private void RefreshSheetManager()
    {
        ConfigureSheetManagerEditableColumns();

        if (_currentJob == null)
        {
            _sheetManagerMetadataResults = [];
            SheetManagerGrid.ItemsSource = Array.Empty<PdfMetadataPreviewRow>();
            return;
        }

        List<PageInfo> pages = CollectPagesUnder(_currentJob.PagesRoot).ToList();
        IReadOnlyDictionary<string, IReadOnlyList<int>> readyRasterDpisByPageFolder =
            RasterSheetCacheService.ReadyReadableRasterDpisByPageFolder(pages);
        var results = new List<PdfMetadataPageResult>();
        foreach (PageInfo page in pages)
        {
            PdfSheetMetadata? metadata = OurPlaneCoreJobStore.ReadSourcePdfMetadata(page.FolderPath);
            if (metadata != null)
                results.Add(new PdfMetadataPageResult(page, true, metadata, ""));
        }

        Dictionary<string, PdfMetadataPreviewRow> metadataRowsByFolder = BuildPdfMetadataPreviewRows(
                results,
                defaultRename: false,
                defaultScale: false,
                readyRasterDpisByPageFolder)
            .ToDictionary(row => row.PageFolder, StringComparer.OrdinalIgnoreCase);

        var rows = new List<PdfMetadataPreviewRow>();
        foreach (PageInfo page in pages)
        {
            if (metadataRowsByFolder.TryGetValue(page.FolderPath, out PdfMetadataPreviewRow? metadataRow))
                rows.Add(metadataRow);
            else
                rows.Add(new PdfMetadataPreviewRow
                {
                    PageFolder = page.FolderPath,
                    CurrentPageName = page.Name,
                    ProposedPageName = page.Name,
                    ProposedScale = SheetManagerScaleText(page.ScaleMetersPerPt),
                    RasterStatus = RasterSheetCacheService.DisplayStatus(page, readyRasterDpisByPageFolder),
                    Reason = "No saved PDF metadata. Click Analyze / Auto Name / Auto Scale.",
                    Confidence = page.ScaleMetersPerPt > 0 ? "scale-set" : "",
                });
        }

        _sheetManagerMetadataResults = results;
        SheetManagerGrid.ItemsSource = rows;
        TxtStatus.Text = $"Sheet Manager: {rows.Count} sheet(s).";
    }

    private async Task AnalyzeSheetManagerAsync(bool defaultRename, bool defaultScale)
    {
        ConfigureSheetManagerEditableColumns();

        if (_currentJob == null)
            return;

        IReadOnlyList<PageInfo> pages = SelectedSheetManagerPages();
        if (pages.Count == 0)
            pages = CollectPagesUnder(_currentJob.PagesRoot).ToList();
        if (pages.Count == 0)
        {
            MessageBox.Show("No PDF pages found.", "Sheet Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveCurrentPageScale();
        TxtStatus.Text = $"Sheet Manager analyzing {pages.Count} sheet(s)...";

        OurPlaneCoreJob job = _currentJob;
        List<PdfMetadataPageResult> results;
        using (ShowBusyOverlay($"Sheet Manager analyzing {pages.Count} sheet(s)..."))
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

        _sheetManagerMetadataResults = results;
        IReadOnlyDictionary<string, IReadOnlyList<int>> readyRasterDpisByPageFolder =
            RasterSheetCacheService.ReadyReadableRasterDpisByPageFolder(results.Select(result => result.Page));
        var rows = BuildPdfMetadataPreviewRows(
                results,
                defaultRename,
                defaultScale,
                readyRasterDpisByPageFolder)
            .ToList();
        rows.AddRange(results
            .Where(result => !result.Ok)
            .Select(result => new PdfMetadataPreviewRow
            {
                PageFolder = result.Page.FolderPath,
                CurrentPageName = result.Page.Name,
                RasterStatus = RasterSheetCacheService.DisplayStatus(result.Page, readyRasterDpisByPageFolder),
                Reason = result.Error,
                Warnings = result.Error,
            }));

        SheetManagerGrid.ItemsSource = rows;
        TxtStatus.Text = $"Sheet Manager analyzed: {results.Count(result => result.Ok)} OK, {results.Count(result => !result.Ok)} failed.";
    }

    private IReadOnlyList<PageInfo> SelectedSheetManagerPages()
    {
        var pages = new List<PageInfo>();
        foreach (PdfMetadataPreviewRow row in SheetManagerGrid.SelectedItems.OfType<PdfMetadataPreviewRow>())
        {
            if (OurPlaneCoreJobStore.TryReadPage(row.PageFolder) is { } page)
                pages.Add(page);
        }

        return pages;
    }

    private List<PdfMetadataPreviewRow> SheetManagerRows() =>
        SheetManagerGrid.ItemsSource?.OfType<PdfMetadataPreviewRow>().ToList() ?? [];

    private void BtnSheetManagerApplyChecked_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
            return;

        SheetManagerGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SheetManagerGrid.CommitEdit(DataGridEditingUnit.Row, true);
        List<PdfMetadataPreviewRow> rows = SheetManagerRows();
        MarkEditedSheetManagerRowsForApply(rows);
        if (!rows.Any(row => row.ApplyRename || row.ApplyScale))
        {
            TxtStatus.Text = "Sheet Manager: no Rename/Scale rows are checked.";
            return;
        }

        ApplyPdfMetadataResults(_currentJob, _sheetManagerMetadataResults, rows);
        RefreshSheetManager();
    }

    private void MarkEditedSheetManagerRowsForApply(IReadOnlyList<PdfMetadataPreviewRow> rows)
    {
        foreach (PdfMetadataPreviewRow row in rows)
        {
            if (!row.ApplyRename && ShouldApplySheetManagerRename(row, row.ProposedPageName))
            {
                row.ApplyRename = true;
            }

            if (row.ApplyScale ||
                !ShouldApplySheetManagerScale(row, row.ProposedScale))
            {
                continue;
            }

            row.ApplyScale = true;
        }
    }

    private static bool ShouldApplySheetManagerRename(PdfMetadataPreviewRow row, string proposedName) =>
        !string.IsNullOrWhiteSpace(proposedName) &&
        !string.Equals(proposedName.Trim(), row.CurrentPageName, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldApplySheetManagerScale(PdfMetadataPreviewRow row, string proposedScale)
    {
        string clean = (proposedScale ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean) ||
            string.Equals(clean, "skip", StringComparison.OrdinalIgnoreCase) ||
            OurPlaneCoreJobStore.TryReadPage(row.PageFolder) is not { } page ||
            !PdfSheetMetadataService.TryParseScaleMetersPerPt(clean, out double scaleMetersPerPt))
        {
            return false;
        }

        string currentScale = SheetManagerScaleText(page.ScaleMetersPerPt);
        string normalizedScale = SheetManagerScaleText(scaleMetersPerPt);
        return !string.Equals(clean, currentScale, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(normalizedScale, currentScale, StringComparison.OrdinalIgnoreCase);
    }

    private void BtnSheetManagerOpenSheet_Click(object sender, RoutedEventArgs e)
    {
        if (SheetManagerGrid.SelectedItem is not PdfMetadataPreviewRow row)
            return;

        SelectPageByFolder(row.PageFolder);
        WorkspaceTabs.SelectedIndex = 0;
    }

    private void BtnSheetManagerOpenTabs_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<PageInfo> pages = SelectedSheetManagerPagesForOpen();
        OpenPagesInNewTabs(pages, "Sheet Manager");
        if (pages.Count > 0)
            WorkspaceTabs.SelectedIndex = 0;
    }

    private void BtnSheetManagerDetach_Click(object sender, RoutedEventArgs e)
    {
        OpenPagesInDetachedWindows(SelectedSheetManagerPagesForOpen(), false, "Sheet Manager");
    }

    private void BtnSheetManagerTileSecondMonitor_Click(object sender, RoutedEventArgs e)
    {
        OpenPagesInDetachedWindows(SelectedSheetManagerPagesForOpen(), true, "Sheet Manager");
    }

    private IReadOnlyList<PageInfo> SelectedSheetManagerPagesForOpen()
    {
        IReadOnlyList<PageInfo> selected = SelectedSheetManagerPages();
        if (selected.Count > 0)
            return selected;

        if (SheetManagerGrid.SelectedItem is PdfMetadataPreviewRow row &&
            OurPlaneCoreJobStore.TryReadPage(row.PageFolder) is { } page)
        {
            return [page];
        }

        return [];
    }

    private void BtnSheetManagerOpenJson_Click(object sender, RoutedEventArgs e)
    {
        if (SheetManagerGrid.SelectedItem is PdfMetadataPreviewRow row)
            OpenSourcePdfMetadata(row.PageFolder);
    }

    private async void BtnSheetManagerBuildRaster_Click(object sender, RoutedEventArgs e)
    {
        if (TryBlockSheetManagerRasterCommandDuringPrepare("Build Raster"))
            return;

        int rasterDpi = SelectedSheetManagerRasterDpi();
        await RunAsyncUiHandler(
            () => BuildSheetManagerRasterCacheAsync(SelectedSheetManagerPagesForRaster(), rasterDpi),
            "Build Raster failed.",
            "Sheet Manager Raster");
    }

    private async void BtnSheetManagerRasterOn_Click(object sender, RoutedEventArgs e)
    {
        if (TryBlockSheetManagerRasterCommandDuringPrepare("Raster On"))
            return;

        int rasterDpi = SelectedSheetManagerRasterDpi();
        await RunAsyncUiHandler(
            () => SetSheetManagerRasterEnabledAsync(SelectedSheetManagerPagesForRaster(), enabled: true, rasterDpi),
            "Raster On failed.",
            "Sheet Manager Raster");
    }

    private async void BtnSheetManagerRasterOff_Click(object sender, RoutedEventArgs e)
    {
        if (TryBlockSheetManagerRasterCommandDuringPrepare("Raster Off"))
            return;

        await RunAsyncUiHandler(
            () => SetSheetManagerRasterEnabledAsync(SelectedSheetManagerPagesForRaster(), enabled: false),
            "Raster Off failed.",
            "Sheet Manager Raster");
    }

    private async void BtnSheetManagerCompactRaster_Click(object sender, RoutedEventArgs e)
    {
        if (TryBlockSheetManagerRasterCommandDuringPrepare("Clean PNGs"))
            return;

        await RunAsyncUiHandler(
            () => CompactSheetManagerRasterCacheAsync(SelectedSheetManagerPagesForRaster()),
            "Clean PNGs failed.",
            "Sheet Manager Raster");
    }

    private void BtnSheetManagerPrepareRaster_Click(object sender, RoutedEventArgs e)
    {
        if (_sheetManagerRasterPrepareCts != null)
        {
            TxtStatus.Text = "Sheet Manager Raster Prepare is already running.";
            return;
        }

        IReadOnlyList<PageInfo> pages = SelectedSheetManagerPagesForRaster();
        if (pages.Count == 0)
        {
            TxtStatus.Text = "Sheet Manager Raster Prepare: no sheets selected.";
            return;
        }

        int rasterDpi = SelectedSheetManagerRasterDpi();
        string rasterDpiLabel = SheetManagerRasterDpiLabel(rasterDpi);
        var cts = new CancellationTokenSource();
        _sheetManagerRasterPrepareCts = cts;
        SetSheetManagerRasterPrepareRunning(true);
        TxtStatus.Text = $"Sheet Manager Raster Prepare {rasterDpiLabel}: queued {pages.Count} sheet(s).";
        _ = PrepareSheetManagerRasterCacheInBackgroundAsync(pages.ToList(), rasterDpi, cts);
    }

    private void BtnSheetManagerCancelRaster_Click(object sender, RoutedEventArgs e)
    {
        if (_sheetManagerRasterPrepareCts == null)
        {
            TxtStatus.Text = "Sheet Manager Raster Prepare: nothing to cancel.";
            return;
        }

        _sheetManagerRasterPrepareCts.Cancel();
        TxtStatus.Text = "Sheet Manager Raster Prepare: cancelling after the current sheet...";
    }

    private async void BtnSheetManagerRowRasterPdf_Click(object sender, RoutedEventArgs e)
    {
        if (TryBlockSheetManagerRasterCommandDuringPrepare("row PDF raster toggle") ||
            SheetManagerRowFromButton(sender) is not { } row ||
            SheetManagerPageFromRow(row) is not { } page)
        {
            return;
        }

        await RunAsyncUiHandler(
            () => SetSheetManagerRasterRowEnabledAsync(row, page, enabled: false),
            "Sheet row PDF failed.",
            "Sheet Manager Raster");
    }

    private async void BtnSheetManagerRowRasterAuto_Click(object sender, RoutedEventArgs e) =>
        await ApplySheetManagerRowRasterDpiAsync(sender, SheetManagerAutoRasterDpi);

    private async void BtnSheetManagerRowRaster200_Click(object sender, RoutedEventArgs e) =>
        await ApplySheetManagerRowRasterDpiAsync(sender, 200);

    private async void BtnSheetManagerRowRaster300_Click(object sender, RoutedEventArgs e) =>
        await ApplySheetManagerRowRasterDpiAsync(sender, 300);

    private async void BtnSheetManagerRowRaster400_Click(object sender, RoutedEventArgs e) =>
        await ApplySheetManagerRowRasterDpiAsync(sender, 400);

    private async Task ApplySheetManagerRowRasterDpiAsync(object sender, int rasterDpi)
    {
        string rasterDpiLabel = SheetManagerRasterDpiLabel(rasterDpi);
        if (TryBlockSheetManagerRasterCommandDuringPrepare($"row {rasterDpiLabel} raster") ||
            SheetManagerRowFromButton(sender) is not { } row ||
            SheetManagerPageFromRow(row) is not { } page)
        {
            return;
        }

        await RunAsyncUiHandler(
            () => SetSheetManagerRasterRowEnabledAsync(row, page, enabled: true, rasterDpi),
            $"Sheet row {rasterDpiLabel} failed.",
            "Sheet Manager Raster");
    }

    private async Task SetSheetManagerRasterRowEnabledAsync(
        PdfMetadataPreviewRow row,
        PageInfo page,
        bool enabled,
        int rasterDpi = RasterSheetCacheService.DefaultRasterDpi)
    {
        bool changed = false;
        bool reused = false;
        int effectiveDpi = EffectiveSheetManagerRasterDpi(page, rasterDpi);
        string rasterDpiLabel = SheetManagerRasterDpiLabel(rasterDpi);

        if (enabled && !RasterSheetCacheService.IsSourceImageRasterProfile(page.RasterSheet))
        {
            float renderScale = RasterSheetCacheService.RasterDpiToRenderScale(effectiveDpi);
            TxtStatus.Text = $"Sheet Manager Raster Row {SheetManagerRasterDpiProgressLabel(rasterDpi, effectiveDpi)}: {page.Name}";
            RasterSheetBuildResult build = await Task.Run(() => RasterSheetCacheService.BuildAndEnable(page, renderScale));
            if (!build.Ok)
            {
                AppLog.Warn($"Raster cache row build failed for '{page.Name}': {build.Error}");
                TxtStatus.Text = $"Sheet Manager Raster Row {rasterDpiLabel}: failed for {page.Name}.";
                RefreshSheetManagerRasterRow(row);
                return;
            }

            changed = true;
            reused = build.Reused;
        }
        else if (RasterSheetCacheService.TrySetEnabled(page, enabled, out string error, out bool toggled))
        {
            changed = toggled;
        }
        else
        {
            AppLog.Warn($"Raster cache row toggle failed for '{page.Name}': {error}");
            TxtStatus.Text = enabled
                ? $"Sheet Manager Raster Row {rasterDpiLabel}: failed for {page.Name}."
                : $"Sheet Manager Raster Row PDF: failed for {page.Name}.";
            RefreshSheetManagerRasterRow(row);
            return;
        }

        InvalidatePagePreviewPrefetchCache();
        ReloadCurrentPageIfRasterChanged([page]);
        RefreshSheetManagerRasterRow(row);
        TxtStatus.Text = enabled
            ? $"Sheet Manager Raster Row {rasterDpiLabel}: {(reused ? "reused" : changed ? "changed" : "already")} {page.Name}."
            : $"Sheet Manager Raster Row PDF: {(changed ? "changed" : "already")} {page.Name}.";
    }

    private PdfMetadataPreviewRow? SheetManagerRowFromButton(object sender)
    {
        if (sender is FrameworkElement { DataContext: PdfMetadataPreviewRow row })
            return row;

        TxtStatus.Text = "Sheet Manager Raster: sheet row is not available.";
        return null;
    }

    private PageInfo? SheetManagerPageFromRow(PdfMetadataPreviewRow row)
    {
        if (OurPlaneCoreJobStore.TryReadPage(row.PageFolder) is { } page)
            return page;

        TxtStatus.Text = "Sheet Manager Raster: sheet row page is not available.";
        return null;
    }

    private void RefreshSheetManagerRasterRow(PdfMetadataPreviewRow row)
    {
        if (OurPlaneCoreJobStore.TryReadPage(row.PageFolder) is { } refreshedPage)
            row.RasterStatus = RasterSheetCacheService.DisplayStatus(refreshedPage);
    }

    private bool RefreshSheetManagerRasterRows(IReadOnlyList<PageInfo> pages)
    {
        if (pages.Count == 0)
            return false;

        List<PdfMetadataPreviewRow> rows = SheetManagerRows();
        if (rows.Count == 0)
            return false;

        var targetPageFolders = pages
            .Select(page => NormalizePathForCompare(page.FolderPath))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (targetPageFolders.Count == 0)
            return false;

        List<PdfMetadataPreviewRow> targetRows = rows
            .Where(row => targetPageFolders.Contains(NormalizePathForCompare(row.PageFolder)))
            .ToList();
        if (targetRows.Count == 0)
            return false;

        var refreshedPagesByFolder = new Dictionary<string, PageInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (PdfMetadataPreviewRow row in targetRows)
        {
            string key = NormalizePathForCompare(row.PageFolder);
            if (refreshedPagesByFolder.ContainsKey(key) ||
                OurPlaneCoreJobStore.TryReadPage(row.PageFolder) is not { } refreshedPage)
            {
                continue;
            }

            refreshedPagesByFolder[key] = refreshedPage;
        }

        if (refreshedPagesByFolder.Count == 0)
            return false;

        IReadOnlyDictionary<string, IReadOnlyList<int>> readyRasterDpisByPageFolder =
            RasterSheetCacheService.ReadyReadableRasterDpisByPageFolder(refreshedPagesByFolder.Values);
        foreach (PdfMetadataPreviewRow row in targetRows)
        {
            string key = NormalizePathForCompare(row.PageFolder);
            if (refreshedPagesByFolder.TryGetValue(key, out PageInfo? refreshedPage))
                row.RasterStatus = RasterSheetCacheService.DisplayStatus(refreshedPage, readyRasterDpisByPageFolder);
        }

        return true;
    }

    private bool TryBlockSheetManagerRasterCommandDuringPrepare(string command)
    {
        if (_sheetManagerRasterPrepareCts == null)
            return false;

        TxtStatus.Text = $"Sheet Manager Raster Prepare is running. Cancel it before {command}.";
        return true;
    }

    private void SetSheetManagerRasterPrepareRunning(bool running)
    {
        if (SheetManagerPrepareRasterButton != null)
            SheetManagerPrepareRasterButton.IsEnabled = !running;
        if (SheetManagerCancelRasterButton != null)
            SheetManagerCancelRasterButton.IsEnabled = running;
    }

    private IReadOnlyList<PageInfo> SelectedSheetManagerPagesForRaster()
    {
        IReadOnlyList<PageInfo> selected = SelectedSheetManagerPages();
        if (selected.Count > 0)
            return selected;

        return _currentJob == null
            ? []
            : CollectPagesUnder(_currentJob.PagesRoot).ToList();
    }

    private int SelectedSheetManagerRasterDpi()
    {
        if (SheetManagerRasterDpiBox?.SelectedItem is ComboBoxItem item)
        {
            string raw = item.Tag?.ToString() ?? item.Content?.ToString() ?? "";
            if (string.Equals(raw.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
                return SheetManagerAutoRasterDpi;

            string digits = new(raw.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dpi))
                return Math.Clamp(dpi, 72, RasterSheetCacheService.MaxRasterDpi);
        }

        return RasterSheetCacheService.DefaultRasterDpi;
    }

    private static string SheetManagerRasterDpiLabel(int rasterDpi) =>
        rasterDpi <= SheetManagerAutoRasterDpi
            ? "Auto"
            : $"{rasterDpi.ToString(CultureInfo.InvariantCulture)} DPI";

    private static int EffectiveSheetManagerRasterDpi(PageInfo page, int rasterDpi)
    {
        if (rasterDpi > SheetManagerAutoRasterDpi)
            return rasterDpi;

        int bestReadyDpi = RasterSheetCacheService.BestReadyReadableRasterDpi(page);
        return bestReadyDpi > 0
            ? bestReadyDpi
            : RasterSheetCacheService.DefaultRasterDpi;
    }

    private static string SheetManagerRasterDpiProgressLabel(int rasterDpi, int effectiveDpi) =>
        rasterDpi <= SheetManagerAutoRasterDpi
            ? $"Auto->{effectiveDpi.ToString(CultureInfo.InvariantCulture)} DPI"
            : $"{effectiveDpi.ToString(CultureInfo.InvariantCulture)} DPI";

    private async Task BuildSheetManagerRasterCacheAsync(IReadOnlyList<PageInfo> pages, int rasterDpi)
    {
        if (pages.Count == 0)
        {
            TxtStatus.Text = "Sheet Manager Raster: no sheets selected.";
            return;
        }

        int built = 0;
        int reused = 0;
        int failed = 0;
        string rasterDpiLabel = SheetManagerRasterDpiLabel(rasterDpi);
        using (ShowBusyOverlay($"Building {rasterDpiLabel} raster cache for {pages.Count} sheet(s)..."))
        {
            await WaitForBusyOverlayRenderAsync();
            for (int i = 0; i < pages.Count; i++)
            {
                PageInfo page = pages[i];
                int effectiveDpi = EffectiveSheetManagerRasterDpi(page, rasterDpi);
                float renderScale = RasterSheetCacheService.RasterDpiToRenderScale(effectiveDpi);
                BusyOverlayText.Text = $"Raster {SheetManagerRasterDpiProgressLabel(rasterDpi, effectiveDpi)} {i + 1}/{pages.Count}: {page.Name}";
                RasterSheetBuildResult result = await Task.Run(() => RasterSheetCacheService.BuildAndEnable(page, renderScale));
                if (result.Ok)
                {
                    if (result.Reused)
                        reused++;
                    else
                        built++;
                }
                else
                {
                    failed++;
                    AppLog.Warn($"Raster cache build failed for '{page.Name}': {result.Error}");
                }
            }
        }

        InvalidatePagePreviewPrefetchCache();
        if (!RefreshSheetManagerRasterRows(pages))
            RefreshSheetManager();
        ReloadCurrentPageIfRasterChanged(pages);
        TxtStatus.Text = $"Sheet Manager Raster {rasterDpiLabel}: built {built}, reused {reused}, failed {failed}.";
    }

    private async Task PrepareSheetManagerRasterCacheInBackgroundAsync(
        IReadOnlyList<PageInfo> pages,
        int rasterDpi,
        CancellationTokenSource cts)
    {
        int built = 0;
        int reused = 0;
        int failed = 0;
        bool cancelled = false;
        string rasterDpiLabel = SheetManagerRasterDpiLabel(rasterDpi);

        try
        {
            for (int i = 0; i < pages.Count; i++)
            {
                cts.Token.ThrowIfCancellationRequested();

                PageInfo page = pages[i];
                int effectiveDpi = EffectiveSheetManagerRasterDpi(page, rasterDpi);
                float renderScale = RasterSheetCacheService.RasterDpiToRenderScale(effectiveDpi);
                TxtStatus.Text = $"Sheet Manager Raster Prepare {SheetManagerRasterDpiProgressLabel(rasterDpi, effectiveDpi)} {i + 1}/{pages.Count}: {page.Name}";

                RasterSheetBuildResult result;
                try
                {
                    result = await Task.Run(
                        () => RasterSheetCacheService.BuildCachePreservingEnabled(page, renderScale),
                        cts.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    AppLog.Warn(ex, $"Raster cache prepare crashed for '{page.Name}'");
                    continue;
                }

                if (result.Ok)
                {
                    if (result.Reused)
                        reused++;
                    else
                        built++;
                }
                else
                {
                    failed++;
                    AppLog.Warn($"Raster cache prepare failed for '{page.Name}': {result.Error}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            if (ReferenceEquals(_sheetManagerRasterPrepareCts, cts))
            {
                _sheetManagerRasterPrepareCts = null;
                SetSheetManagerRasterPrepareRunning(false);
            }

            cts.Dispose();
            InvalidatePagePreviewPrefetchCache();
            if (!RefreshSheetManagerRasterRows(pages))
                RefreshSheetManager();
            TxtStatus.Text = cancelled
                ? $"Sheet Manager Raster Prepare {rasterDpiLabel} cancelled: built {built}, reused {reused}, failed {failed}."
                : $"Sheet Manager Raster Prepare {rasterDpiLabel} done: built {built}, reused {reused}, failed {failed}.";
        }
    }

    private async Task CompactSheetManagerRasterCacheAsync(IReadOnlyList<PageInfo> pages)
    {
        if (pages.Count == 0)
        {
            TxtStatus.Text = "Sheet Manager Raster Clean PNGs: no sheets selected.";
            return;
        }

        int cleaned = 0;
        int failed = 0;
        int deletedFiles = 0;
        long deletedBytes = 0;
        using (ShowBusyOverlay($"Cleaning raster PNG cache for {pages.Count} sheet(s)..."))
        {
            await WaitForBusyOverlayRenderAsync();
            for (int i = 0; i < pages.Count; i++)
            {
                PageInfo page = pages[i];
                BusyOverlayText.Text = $"Clean PNGs {i + 1}/{pages.Count}: {page.Name}";
                RasterSheetCacheCompactResult result = await Task.Run(() => RasterSheetCacheService.CompactCache(page));
                deletedFiles += result.DeletedFiles;
                deletedBytes += result.DeletedBytes;
                if (result.Ok)
                {
                    if (result.DeletedFiles > 0)
                        cleaned++;
                }
                else
                {
                    failed++;
                    AppLog.Warn($"Raster cache compact failed for '{page.Name}': {result.Error}");
                }
            }
        }

        if (!RefreshSheetManagerRasterRows(pages))
            RefreshSheetManager();
        TxtStatus.Text =
            $"Sheet Manager Raster Clean PNGs: cleaned {cleaned}, deleted {deletedFiles} file(s), freed {FormatRasterCacheBytes(deletedBytes)}, failed {failed}.";
    }

    private async Task SetSheetManagerRasterEnabledAsync(
        IReadOnlyList<PageInfo> pages,
        bool enabled,
        int rasterDpi = RasterSheetCacheService.DefaultRasterDpi,
        bool refreshSheetManager = true)
    {
        if (pages.Count == 0)
        {
            TxtStatus.Text = "Sheet Manager Raster: no sheets selected.";
            return;
        }

        if (!enabled)
        {
            await SetSheetManagerRasterOffFastAsync(pages, refreshSheetManager);
            return;
        }

        SheetManagerRasterReadyBatch readyBatch = await EnableSheetManagerReadyRasterPagesAsync(pages, rasterDpi);
        bool fastRowsRefreshed = true;
        if (readyBatch.FastPages.Count > 0)
        {
            InvalidatePagePreviewPrefetchCache();
            if (refreshSheetManager)
                fastRowsRefreshed = RefreshSheetManagerRasterRows(readyBatch.FastPages);
            ReloadCurrentPageIfRasterChanged(readyBatch.FastPages);
        }

        string rasterDpiLabel = SheetManagerRasterDpiLabel(rasterDpi);
        if (readyBatch.MissingPages.Count == 0)
        {
            if (refreshSheetManager && readyBatch.FastPages.Count > 0 && !fastRowsRefreshed)
                RefreshSheetManager();
            TxtStatus.Text = $"Sheet Manager Raster On {rasterDpiLabel}: ready {readyBatch.Ready}, source {readyBatch.Source}, already {readyBatch.Already}, failed {readyBatch.Failed}.";
            return;
        }

        int changed = readyBatch.Ready + readyBatch.Source;
        int built = 0;
        int reused = readyBatch.Ready;
        int already = readyBatch.Already;
        int failed = readyBatch.Failed;
        IReadOnlyList<PageInfo> buildPages = readyBatch.MissingPages;
        string busyText = $"Building missing {rasterDpiLabel} raster for {buildPages.Count} of {pages.Count} sheet(s)...";
        using (ShowBusyOverlay(busyText))
        {
            await WaitForBusyOverlayRenderAsync();
            for (int i = 0; i < buildPages.Count; i++)
            {
                PageInfo page = buildPages[i];
                int effectiveDpi = EffectiveSheetManagerRasterDpi(page, rasterDpi);
                float renderScale = RasterSheetCacheService.RasterDpiToRenderScale(effectiveDpi);
                BusyOverlayText.Text = $"Raster On {SheetManagerRasterDpiProgressLabel(rasterDpi, effectiveDpi)} {i + 1}/{buildPages.Count}: {page.Name}";
                RasterSheetBuildResult build = await Task.Run(() => RasterSheetCacheService.BuildAndEnable(page, renderScale));
                if (build.Ok)
                {
                    if (build.Reused)
                        reused++;
                    else
                        built++;
                    changed++;
                }
                else
                {
                    failed++;
                    AppLog.Warn($"Raster cache build failed for '{page.Name}': {build.Error}");
                }
            }
        }

        InvalidatePagePreviewPrefetchCache();
        if (refreshSheetManager)
        {
            if (!RefreshSheetManagerRasterRows(pages))
                RefreshSheetManager();
        }
        ReloadCurrentPageIfRasterChanged(pages);
        TxtStatus.Text = $"Sheet Manager Raster On {rasterDpiLabel}: changed {changed}, built {built}, reused {reused}, already {already}, failed {failed}.";
    }

    private async Task<SheetManagerRasterReadyBatch> EnableSheetManagerReadyRasterPagesAsync(
        IReadOnlyList<PageInfo> pages,
        int rasterDpi)
    {
        string rasterDpiLabel = SheetManagerRasterDpiLabel(rasterDpi);
        TxtStatus.Text = $"Sheet Manager Raster On {rasterDpiLabel}: checking ready cache for {pages.Count} sheet(s)...";
        return await Task.Run(() =>
        {
            var plans = new List<(PageInfo Page, float RenderScale, bool SourceImage)>();
            var missingPages = new List<PageInfo>();
            foreach (PageInfo page in pages)
            {
                if (RasterSheetCacheService.IsSourceImageRasterProfile(page.RasterSheet))
                {
                    plans.Add((page, 0f, true));
                    continue;
                }

                int effectiveDpi = EffectiveSheetManagerRasterDpi(page, rasterDpi);
                float renderScale = RasterSheetCacheService.RasterDpiToRenderScale(effectiveDpi);
                if (!RasterSheetCacheService.HasReadyReadableRaster(page, renderScale))
                {
                    missingPages.Add(page);
                    continue;
                }

                plans.Add((page, renderScale, false));
            }

            var fastPages = new List<PageInfo>();
            int readyCount = 0;
            int sourceCount = 0;
            int alreadyCount = 0;
            int failedCount = 0;
            foreach (var plan in plans)
            {
                if (plan.SourceImage)
                {
                    if (RasterSheetCacheService.TrySetEnabled(plan.Page, enabled: true, out string error, out bool toggled))
                    {
                        if (toggled)
                        {
                            sourceCount++;
                            fastPages.Add(plan.Page);
                        }
                        else
                        {
                            alreadyCount++;
                        }
                    }
                    else
                    {
                        failedCount++;
                        AppLog.Warn($"Raster cache toggle failed for '{plan.Page.Name}': {error}");
                    }

                    continue;
                }

                if (RasterSheetCacheService.TryEnableReadyReadableRaster(
                        plan.Page,
                        plan.RenderScale,
                        out RasterSheetBuildResult result))
                {
                    readyCount++;
                    fastPages.Add(plan.Page);
                }
                else
                {
                    failedCount++;
                    AppLog.Warn($"Ready raster cache enable failed for '{plan.Page.Name}': {result.Error}");
                }
            }

            return new SheetManagerRasterReadyBatch(
                fastPages,
                missingPages,
                readyCount,
                sourceCount,
                alreadyCount,
                failedCount);
        });
    }

    private async Task SetSheetManagerRasterOffFastAsync(
        IReadOnlyList<PageInfo> pages,
        bool refreshSheetManager)
    {
        TxtStatus.Text = $"Sheet Manager Raster Off: updating {pages.Count} sheet(s)...";
        (int changed, int already, int failed) = await Task.Run(() =>
        {
            int changedCount = 0;
            int alreadyCount = 0;
            int failedCount = 0;
            foreach (PageInfo page in pages)
            {
                if (RasterSheetCacheService.TrySetEnabled(page, enabled: false, out string error, out bool toggled))
                {
                    if (toggled)
                        changedCount++;
                    else
                        alreadyCount++;
                }
                else
                {
                    failedCount++;
                    AppLog.Warn($"Raster cache toggle failed for '{page.Name}': {error}");
                }
            }

            return (changedCount, alreadyCount, failedCount);
        });

        InvalidatePagePreviewPrefetchCache();
        if (refreshSheetManager)
        {
            if (!RefreshSheetManagerRasterRows(pages))
                RefreshSheetManager();
        }
        ReloadCurrentPageIfRasterChanged(pages);
        TxtStatus.Text = $"Sheet Manager Raster Off: changed {changed}, already {already}, failed {failed}.";
    }

    private void ReloadCurrentPageIfRasterChanged(IReadOnlyList<PageInfo> changedPages)
    {
        if (_currentPage == null ||
            !changedPages.Any(page => IsSamePageFolder(page.FolderPath, _currentPage.FolderPath)) ||
            OurPlaneCoreJobStore.TryReadPage(_currentPage.FolderPath) is not { } refreshedPage)
        {
            return;
        }

        LoadPageIntoViewport(refreshedPage, _viewport.CaptureViewState());
    }

    private static string FormatRasterCacheBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";

        string[] units = ["KB", "MB", "GB"];
        double value = bytes / 1024.0;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024.0;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static string SheetManagerScaleText(double scaleMetersPerPt)
        => PdfSheetMetadataService.FormatImperialScale(scaleMetersPerPt);

    private void BtnTakeoffManagerRefresh_Click(object sender, RoutedEventArgs e) => RefreshTakeoffManager();

    private void RefreshTakeoffManager()
    {
        TakeoffManagerGrid.ItemsSource = _takeoffItems
            .Select(item => new TakeoffManagerRow(
                item.Name,
                TakeoffTypeDisplay(item),
                item.Measurements.Count.ToString(CultureInfo.InvariantCulture),
                item.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode),
                TakeoffUnitText(item),
                UnitPriceText(item),
                CostText(item),
                item.Notes,
                _currentJob == null ? item.FolderPath : Path.GetRelativePath(_currentJob.RootPath, item.FolderPath),
                item))
            .ToList();
        TxtStatus.Text = $"Takeoff Manager: {_takeoffItems.Count} item(s).";
    }

    private TakeoffManagerRow? SelectedTakeoffManagerRow() =>
        TakeoffManagerGrid.SelectedItem as TakeoffManagerRow;

    private void BtnTakeoffManagerSetActive_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTakeoffManagerRow() is not { Item: { } item })
            return;

        if (SetActiveTakeoffTarget(FindTakeoffTreeItem(item), item))
            WorkspaceTabs.SelectedIndex = 0;
    }

    private void BtnTakeoffManagerProperties_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTakeoffManagerRow() is not { Item: { } item } ||
            FindTakeoffTreeItem(item) is not { } tvi)
            return;

        EditTakeoffItemProperties(tvi, item);
        RefreshTakeoffManager();
    }

    private void BtnTakeoffManagerOpenEstimating_Click(object sender, RoutedEventArgs e) => OpenEstimatingWindow();

    private void BtnAiManagerRefresh_Click(object sender, RoutedEventArgs e) => RefreshAiManager();

    private void RefreshAiManager()
    {
        LoadObservationsInbox();
        AiManagerGrid.ItemsSource = ObservationsListView.Items.OfType<ObservationDisplayItem>().ToList();
        TxtStatus.Text = $"AI Manager: {AiManagerGrid.Items.Count} item(s).";
    }

    private ObservationDisplayItem? SelectedAiManagerItem() =>
        AiManagerGrid.SelectedItem as ObservationDisplayItem;

    private void BtnAiManagerOpenDetails_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAiManagerItem() is { } item)
            ShowObservationDetailsDialog(item.Observation);
    }

    private void BtnAiManagerGoToPage_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAiManagerItem() is { } item && CanGoToObservationPage(item))
        {
            GoToObservationPage(item);
            WorkspaceTabs.SelectedIndex = 0;
        }
    }

    private async void BtnAiManagerRunAi_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            async () =>
            {
                if (SelectedAiManagerItem() is { } item && CanRunAiRequest(item))
                    await RunAiRequestAsync(item);
            },
            "AI Manager request failed.",
            "AI Manager");
    }

    private void Btn3dManagerBuildFromTakeoffs_Click(object sender, RoutedEventArgs e)
    {
        StopLegacy3DMassingWorkflow("3D From Takeoffs");
    }

    private void Btn3dManagerOpenWindow_Click(object sender, RoutedEventArgs e) => StopLegacy3DMassingWorkflow("Open 3D Window");
    private void Btn3dManagerOpenJson_Click(object sender, RoutedEventArgs e) => StopLegacy3DMassingWorkflow("Open 3D JSON");

    private void Refresh3dManagerSummary()
    {
        RefreshThreeDViewer();
    }
}
