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
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private const int SheetManagerAutoRasterDpi = 0;
    private bool _sheetManagerEditableColumnsConfigured;
    private bool _updatingSheetManagerBulkEdit;
    private bool _sheetManagerRefreshPendingAfterEdit;
    private CancellationTokenSource? _sheetManagerRasterPrepareCts;
    private IDisposable? _sheetManagerRasterWriteActivity;
    private CancellationTokenSource? _sheetManagerAnalysisCts;
    private string _sheetManagerRasterBackgroundLabel = "Prepare";

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

        if (TryGetWorkspaceModule(tab.Tag?.ToString(), out ModuleId module) && !IsModuleEnabled(module))
        {
            WorkspaceTabs.SelectedItem = MainViewWorkspaceTab;
            TxtStatus.Text = $"{ModuleFeatureCatalog.Definition(module).Name} is disabled in Settings > Modules.";
            return;
        }

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
        var textBox = new FrameworkElementFactory(typeof(PdfMetadataTextBox));
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
        textBox.AddHandler(TextBox.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(SheetManagerTextBox_LostKeyboardFocus));
        textBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(SheetManagerTextBox_TextChanged));
        return new DataTemplate { VisualTree = textBox };
    }

    private static void SheetManagerTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox ||
            Window.GetWindow(textBox) is not MainWindow owner ||
            !owner._sheetManagerRefreshPendingAfterEdit)
        {
            return;
        }

        owner._sheetManagerRefreshPendingAfterEdit = false;
        owner.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!owner.IsSheetManagerTextEditActive())
                owner.RefreshSheetManager();
        }), System.Windows.Threading.DispatcherPriority.Background);
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

        int selectionStart = textBox.SelectionStart;
        int selectionLength = textBox.SelectionLength;
        string value = textBox.Text;

        owner.MarkSheetManagerTextRowForApply(editedRow, bindingPath, value);
        owner.ApplySheetManagerTextToSelectedRows(editedRow, bindingPath, value);
        owner.RestoreSheetManagerTextSelection(textBox, value, selectionStart, selectionLength);
    }

    private void RestoreSheetManagerTextSelection(TextBox textBox, string value, int selectionStart, int selectionLength)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!textBox.IsKeyboardFocusWithin ||
                !string.Equals(textBox.Text, value, StringComparison.Ordinal))
            {
                return;
            }

            int start = Math.Clamp(selectionStart, 0, textBox.Text.Length);
            int length = Math.Clamp(selectionLength, 0, textBox.Text.Length - start);
            textBox.Select(start, length);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void MarkSheetManagerTextRowForApply(PdfMetadataPreviewRow row, string bindingPath, string value)
    {
        if (_updatingSheetManagerBulkEdit)
            return;

        if (string.Equals(bindingPath, nameof(PdfMetadataPreviewRow.ProposedPageName), StringComparison.Ordinal))
        {
            row.ApplyRename = ShouldApplySheetManagerRename(row, value);
        }
        else if (string.Equals(bindingPath, nameof(PdfMetadataPreviewRow.ProposedScale), StringComparison.Ordinal))
        {
            row.ApplyScale = ShouldApplySheetManagerScale(row, value);
        }
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
                if (ReferenceEquals(row, editedRow))
                    continue;

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
        if (!IsModuleEnabled(ModuleId.SheetManager))
            return;

        ConfigureSheetManagerEditableColumns();

        if (IsSheetManagerTextEditActive())
        {
            _sheetManagerRefreshPendingAfterEdit = true;
            TxtStatus.Text = "Sheet Manager: refresh queued until the current edit is finished.";
            return;
        }

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
            PdfSheetMetadata? metadata = OurPlanCoreJobStore.ReadSourcePdfMetadata(page.FolderPath);
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

    private bool IsSheetManagerTextEditActive() =>
        SheetManagerGrid.IsKeyboardFocusWithin &&
        Keyboard.FocusedElement is TextBox textBox &&
        textBox.DataContext is PdfMetadataPreviewRow &&
        textBox.Tag is string bindingPath &&
        IsSheetManagerEditableBinding(bindingPath);

    private static bool IsSheetManagerEditableBinding(string bindingPath) =>
        string.Equals(bindingPath, nameof(PdfMetadataPreviewRow.ProposedPageName), StringComparison.Ordinal) ||
        string.Equals(bindingPath, nameof(PdfMetadataPreviewRow.ProposedScale), StringComparison.Ordinal);

    private async Task AnalyzeSheetManagerAsync(bool defaultRename, bool defaultScale)
    {
        if (!IsModuleEnabled(ModuleId.SheetManager))
            return;

        ConfigureSheetManagerEditableColumns();

        if (_currentJob == null)
            return;
        if (!EnsureCurrentJobWritable("analyze sheets in Sheet Manager"))
            return;

        IReadOnlyList<PageInfo> pages = SelectedSheetManagerPages();
        if (pages.Count == 0)
            pages = CollectPagesUnder(_currentJob.PagesRoot).ToList();
        if (pages.Count == 0)
        {
            PostStatusInfo("No PDF pages found in Sheet Manager.");
            return;
        }

        SaveCurrentPageScale();
        TxtStatus.Text = $"Sheet Manager analyzing {pages.Count} sheet(s)...";

        OurPlanCoreJob job = _currentJob;
        bool persistDuringAnalysis = SheetMetadataRulesService.Active.ImportPolicy ==
                                     SheetMetadataImportPolicy.LegacyAutoApply;
        _sheetManagerAnalysisCts?.Cancel();
        using var analysisCts = new CancellationTokenSource();
        _sheetManagerAnalysisCts = analysisCts;
        List<PdfMetadataPageResult> results;
        try
        {
            using (ShowBusyOverlay($"Sheet Manager analyzing {pages.Count} sheet(s)..."))
            {
                await WaitForBusyOverlayRenderAsync();
                if (!EnsureExpectedJobWritable(job, "analyze sheets in Sheet Manager"))
                    return;
                results = await Task.Run(
                    () => AnalyzePdfPages(job, pages, persistDuringAnalysis, analysisCts.Token),
                    analysisCts.Token);
            }
        }
        catch (OperationCanceledException) when (analysisCts.IsCancellationRequested)
        {
            TxtStatus.Text = "Sheet Manager analysis cancelled.";
            return;
        }
        finally
        {
            if (ReferenceEquals(_sheetManagerAnalysisCts, analysisCts))
                _sheetManagerAnalysisCts = null;
        }
        if (!EnsureExpectedJobWritable(job, "show Sheet Manager analysis results"))
            return;

        if (!IsModuleEnabled(ModuleId.SheetManager))
            return;

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

    private void CancelActiveSheetManagerWorkForModuleDisable()
    {
        _sheetManagerAnalysisCts?.Cancel();
        _sheetManagerRasterPrepareCts?.Cancel();
    }

    private IReadOnlyList<PageInfo> SelectedSheetManagerPages()
    {
        var pages = new List<PageInfo>();
        foreach (PdfMetadataPreviewRow row in SheetManagerGrid.SelectedItems.OfType<PdfMetadataPreviewRow>())
        {
            if (OurPlanCoreJobStore.TryReadPage(row.PageFolder) is { } page)
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
        if (!EnsureCurrentJobWritable("apply Sheet Manager name and scale changes"))
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
            OurPlanCoreJobStore.TryReadPage(row.PageFolder) is not { } page ||
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
        if (!RequireModule(ModuleId.DetachedSheets, "Detach sheets"))
            return;
        OpenPagesInDetachedWindows(SelectedSheetManagerPagesForOpen(), false, "Sheet Manager");
    }

    private void BtnSheetManagerTileSecondMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.DetachedSheets, "Tile sheets"))
            return;
        OpenPagesInDetachedWindows(SelectedSheetManagerPagesForOpen(), true, "Sheet Manager");
    }

    private IReadOnlyList<PageInfo> SelectedSheetManagerPagesForOpen()
    {
        IReadOnlyList<PageInfo> selected = SelectedSheetManagerPages();
        if (selected.Count > 0)
            return selected;

        if (SheetManagerGrid.SelectedItem is PdfMetadataPreviewRow row &&
            OurPlanCoreJobStore.TryReadPage(row.PageFolder) is { } page)
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

    private async void BtnSheetManagerRasterFirstOn_Click(object sender, RoutedEventArgs e)
    {
        if (TryBlockSheetManagerRasterCommandDuringPrepare("Raster First On"))
            return;

        await RunAsyncUiHandler(
            () => SetSheetManagerRasterFirstAsync(SelectedSheetManagerPages(), enabled: true),
            "Raster First On failed.",
            "Sheet Manager Raster");
    }

    private async void BtnSheetManagerRasterFirstOff_Click(object sender, RoutedEventArgs e)
    {
        if (TryBlockSheetManagerRasterCommandDuringPrepare("Raster First Off"))
            return;

        await RunAsyncUiHandler(
            () => SetSheetManagerRasterFirstAsync(SelectedSheetManagerPages(), enabled: false),
            "Raster First Off failed.",
            "Sheet Manager Raster");
    }

    private async void BtnSheetManagerCompactRaster_Click(object sender, RoutedEventArgs e)
    {
        if (TryBlockSheetManagerRasterCommandDuringPrepare("Clean Raster"))
            return;

        await RunAsyncUiHandler(
            () => CompactSheetManagerRasterCacheAsync(SelectedSheetManagerPages()),
            "Clean Raster failed.",
            "Sheet Manager Raster");
    }

    private void BtnSheetManagerPrepareRaster_Click(object sender, RoutedEventArgs e)
    {
        if (_sheetManagerRasterPrepareCts != null)
        {
            TxtStatus.Text = $"Sheet Manager Raster {_sheetManagerRasterBackgroundLabel} is already running.";
            return;
        }

        IReadOnlyList<PageInfo> pages = SelectedSheetManagerPages();
        if (pages.Count == 0)
        {
            TxtStatus.Text = "Sheet Manager Raster Prepare: no sheets selected.";
            return;
        }

        int rasterDpi = SelectedSheetManagerRasterDpi();
        string rasterFormat = SelectedSheetManagerRasterFormat();
        string rasterDpiLabel = SheetManagerRasterSelectionLabel(rasterDpi, rasterFormat);
        if (!TryBeginSheetManagerRasterOperation(
                "Prepare",
                "preparing raster cache",
                out CancellationTokenSource cts))
        {
            return;
        }
        TxtStatus.Text = $"Sheet Manager Raster Prepare {rasterDpiLabel}: queued {pages.Count} sheet(s).";
        _ = PrepareSheetManagerRasterCacheInBackgroundAsync(pages.ToList(), rasterDpi, rasterFormat, cts);
    }

    private void BtnSheetManagerCancelRaster_Click(object sender, RoutedEventArgs e)
    {
        if (_sheetManagerRasterPrepareCts == null)
        {
            TxtStatus.Text = "Sheet Manager Raster Prepare: nothing to cancel.";
            return;
        }

        _sheetManagerRasterPrepareCts.Cancel();
        TxtStatus.Text = $"Sheet Manager Raster {_sheetManagerRasterBackgroundLabel}: cancelling after the current sheet...";
    }

    private void RefreshSheetManagerRasterRow(PdfMetadataPreviewRow row)
    {
        if (IsSheetManagerTextEditActive())
        {
            _sheetManagerRefreshPendingAfterEdit = true;
            return;
        }

        if (OurPlanCoreJobStore.TryReadPage(row.PageFolder) is { } refreshedPage)
            row.RasterStatus = RasterSheetCacheService.DisplayStatus(refreshedPage);
    }

    private bool RefreshSheetManagerRasterRows(IReadOnlyList<PageInfo> pages)
    {
        if (IsSheetManagerTextEditActive())
        {
            _sheetManagerRefreshPendingAfterEdit = true;
            return true;
        }

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
                OurPlanCoreJobStore.TryReadPage(row.PageFolder) is not { } refreshedPage)
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

        TxtStatus.Text = $"Sheet Manager Raster {_sheetManagerRasterBackgroundLabel} is running. Cancel it before {command}.";
        return true;
    }

    private bool TryBeginSheetManagerRasterOperation(
        string operationLabel,
        string command,
        out CancellationTokenSource cts)
    {
        if (_sheetManagerRasterPrepareCts != null)
        {
            TxtStatus.Text =
                $"Sheet Manager Raster {_sheetManagerRasterBackgroundLabel} is running. Cancel it before {command}.";
            cts = null!;
            return false;
        }

        IDisposable? writeActivity = JobFileWriteActivity.TryBeginBackgroundWriteForProjectPath(
            _currentJob?.RootPath ?? "");
        if (writeActivity == null)
        {
            TxtStatus.Text = $"A project checkpoint is active. Retry {command} when it finishes.";
            cts = null!;
            return false;
        }

        cts = new CancellationTokenSource();
        _sheetManagerRasterWriteActivity = writeActivity;
        _sheetManagerRasterPrepareCts = cts;
        _sheetManagerRasterBackgroundLabel = operationLabel;
        SetSheetManagerRasterPrepareRunning(true);
        return true;
    }

    private void FinishSheetManagerRasterOperation(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_sheetManagerRasterPrepareCts, cts))
        {
            _sheetManagerRasterPrepareCts = null;
            _sheetManagerRasterBackgroundLabel = "Prepare";
            SetSheetManagerRasterPrepareRunning(false);
            _sheetManagerRasterWriteActivity?.Dispose();
            _sheetManagerRasterWriteActivity = null;
        }

        cts.Dispose();
    }

    private void SetSheetManagerRasterPrepareRunning(bool running)
    {
        if (SheetManagerRasterPresetControls != null)
            SheetManagerRasterPresetControls.IsEnabled = !running && SheetManagerGrid.SelectedItems.Count > 0;
        if (SheetManagerRasterOptionsButton != null)
            SheetManagerRasterOptionsButton.IsEnabled = SheetManagerGrid.SelectedItems.Count > 0;
        if (SheetManagerPrepareRasterButton != null)
            SheetManagerPrepareRasterButton.IsEnabled = !running;
        if (SheetManagerRasterFirstOnButton != null)
            SheetManagerRasterFirstOnButton.IsEnabled = !running;
        if (SheetManagerRasterFirstOffButton != null)
            SheetManagerRasterFirstOffButton.IsEnabled = !running;
        if (SheetManagerCleanRasterButton != null)
            SheetManagerCleanRasterButton.IsEnabled = !running;
        if (SheetManagerRasterFormatBox != null)
            SheetManagerRasterFormatBox.IsEnabled = !running;
        if (SheetManagerCancelRasterButton != null)
            SheetManagerCancelRasterButton.IsEnabled = running;
    }

    private int SelectedSheetManagerRasterDpi()
        => _sheetManagerLastRasterDpi;

    private string SelectedSheetManagerRasterFormat()
    {
        if (SheetManagerRasterFormatBox?.SelectedItem is ComboBoxItem item)
            return RasterSheetCacheService.NormalizeReadableRasterFormat(item.Tag?.ToString() ?? item.Content?.ToString() ?? "");

        return "";
    }

    private static string SheetManagerRasterDpiLabel(int rasterDpi) =>
        rasterDpi <= SheetManagerAutoRasterDpi
            ? "Auto"
            : $"{rasterDpi.ToString(CultureInfo.InvariantCulture)} DPI";

    private static string SheetManagerRasterFormatLabel(string rasterFormat) =>
        RasterSheetCacheService.NormalizeReadableRasterFormat(rasterFormat) switch
        {
            RasterSheetCacheService.PngRasterFormat => "PNG",
            RasterSheetCacheService.WebpRasterFormat => "WebP",
            _ => ""
        };

    private static string SheetManagerRasterSelectionLabel(int rasterDpi, string rasterFormat)
    {
        string dpi = SheetManagerRasterDpiLabel(rasterDpi);
        string format = SheetManagerRasterFormatLabel(rasterFormat);
        return string.IsNullOrWhiteSpace(format)
            ? dpi
            : $"{dpi} {format}";
    }

    private static int EffectiveSheetManagerRasterDpi(PageInfo page, int rasterDpi)
    {
        if (rasterDpi > SheetManagerAutoRasterDpi)
            return rasterDpi;

        int bestReadyDpi = RasterSheetCacheService.BestReadyReadableRasterDpi(page);
        return bestReadyDpi > 0
            ? bestReadyDpi
            : RasterSheetCacheService.DefaultRasterDpi;
    }

    private static string SheetManagerRasterDpiProgressLabel(int rasterDpi, int effectiveDpi, string rasterFormat = "")
    {
        string dpi = rasterDpi <= SheetManagerAutoRasterDpi
            ? $"Auto->{effectiveDpi.ToString(CultureInfo.InvariantCulture)} DPI"
            : $"{effectiveDpi.ToString(CultureInfo.InvariantCulture)} DPI";
        string format = SheetManagerRasterFormatLabel(rasterFormat);
        return string.IsNullOrWhiteSpace(format)
            ? dpi
            : $"{dpi} {format}";
    }

    private async Task PrepareSheetManagerRasterCacheInBackgroundAsync(
        IReadOnlyList<PageInfo> pages,
        int rasterDpi,
        string rasterFormat,
        CancellationTokenSource cts)
    {
        int built = 0;
        int reused = 0;
        int failed = 0;
        bool cancelled = false;
        string rasterDpiLabel = SheetManagerRasterSelectionLabel(rasterDpi, rasterFormat);

        try
        {
            for (int i = 0; i < pages.Count; i++)
            {
                cts.Token.ThrowIfCancellationRequested();

                PageInfo page = pages[i];
                int effectiveDpi = EffectiveSheetManagerRasterDpi(page, rasterDpi);
                float renderScale = RasterSheetCacheService.RasterDpiToRenderScale(effectiveDpi);
                TxtStatus.Text = $"Sheet Manager Raster Prepare {SheetManagerRasterDpiProgressLabel(rasterDpi, effectiveDpi, rasterFormat)} {i + 1}/{pages.Count}: {page.Name}";

                RasterSheetBuildResult result;
                try
                {
                    result = await Task.Run(
                        () => PrepareAndWarmSheetManagerRaster(page, renderScale, rasterFormat),
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
                    RefreshSheetManagerRasterBackgroundPage(page, refreshSheetManager: true, reloadCurrentPage: false);
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
            FinishSheetManagerRasterOperation(cts);
            InvalidatePagePreviewPrefetchCache();
            if (!RefreshSheetManagerRasterRows(pages))
                RefreshSheetManager();
            RefreshPageTreePageSnapshots(pages);
            TxtStatus.Text = cancelled
                ? $"Sheet Manager Raster Prepare {rasterDpiLabel} cancelled: built {built}, reused {reused}, failed {failed}."
                : $"Sheet Manager Raster Prepare {rasterDpiLabel} done: built {built}, reused {reused}, failed {failed}.";
        }
    }

    private async Task CompactSheetManagerRasterCacheAsync(IReadOnlyList<PageInfo> pages)
    {
        if (pages.Count == 0)
        {
            TxtStatus.Text = "Sheet Manager Raster Cleanup: no sheets selected.";
            return;
        }

        if (_sheetManagerRasterPrepareCts != null)
        {
            TxtStatus.Text = "Sheet Manager Raster Cleanup: another raster background job is already running.";
            return;
        }

        if (!TryBeginSheetManagerRasterOperation(
                "Cleanup",
                "cleaning raster cache",
                out CancellationTokenSource cts))
        {
            return;
        }
        TxtStatus.Text = $"Sheet Manager Raster Cleanup: queued {pages.Count} sheet(s).";
        _ = CompactSheetManagerRasterCacheInBackgroundAsync(pages.ToList(), cts);
        await Task.CompletedTask;
    }

    private async Task CompactSheetManagerRasterCacheInBackgroundAsync(
        IReadOnlyList<PageInfo> pages,
        CancellationTokenSource cts)
    {
        int cleaned = 0;
        int failed = 0;
        int deletedFiles = 0;
        long deletedBytes = 0;
        bool cancelled = false;

        try
        {
            for (int i = 0; i < pages.Count; i++)
            {
                cts.Token.ThrowIfCancellationRequested();

                PageInfo page = pages[i];
                TxtStatus.Text = $"Sheet Manager Raster Cleanup {i + 1}/{pages.Count}: {page.Name}";

                RasterSheetCacheCompactResult result;
                try
                {
                    result = await Task.Run(
                        () => RasterSheetCacheService.CompactCache(page),
                        cts.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    AppLog.Warn(ex, $"Raster cache compact crashed for '{page.Name}'");
                    continue;
                }

                deletedFiles += result.DeletedFiles;
                deletedBytes += result.DeletedBytes;
                if (result.Ok)
                {
                    if (result.DeletedFiles > 0)
                        cleaned++;
                    if (result.DeletedFiles > 0)
                        RefreshSheetManagerRasterBackgroundPage(page, refreshSheetManager: true, reloadCurrentPage: false);
                }
                else
                {
                    failed++;
                    AppLog.Warn($"Raster cache compact failed for '{page.Name}': {result.Error}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            FinishSheetManagerRasterOperation(cts);
            InvalidatePagePreviewPrefetchCache();
            if (!RefreshSheetManagerRasterRows(pages))
                RefreshSheetManager();
            RefreshPageTreePageSnapshots(pages);
            TxtStatus.Text = cancelled
                ? $"Sheet Manager Raster Cleanup cancelled: cleaned {cleaned}, deleted {deletedFiles} file(s), freed {FormatRasterCacheBytes(deletedBytes)}, failed {failed}."
                : $"Sheet Manager Raster Cleanup done: cleaned {cleaned}, deleted {deletedFiles} file(s), freed {FormatRasterCacheBytes(deletedBytes)}, failed {failed}.";
        }
    }

    private async Task SetSheetManagerRasterEnabledAsync(
        IReadOnlyList<PageInfo> pages,
        bool enabled,
        int rasterDpi = RasterSheetCacheService.DefaultRasterDpi,
        string rasterFormat = "",
        bool refreshSheetManager = true,
        bool pinRequestedDpi = false)
    {
        if (pages.Count == 0)
        {
            TxtStatus.Text = "Sheet Manager Raster: no sheets selected.";
            return;
        }

        string operationLabel = enabled ? "On" : "PDF";
        if (!TryBeginSheetManagerRasterOperation(
                operationLabel,
                enabled ? "changing the raster DPI" : "switching to PDF",
                out CancellationTokenSource cts))
        {
            return;
        }

        bool operationTransferredToBackground = false;
        try
        {
            if (!enabled)
            {
                await SetSheetManagerRasterOffFastAsync(pages, refreshSheetManager, cts.Token);
                return;
            }

            SheetManagerRasterReadyBatch readyBatch = await EnableSheetManagerReadyRasterPagesAsync(
                pages,
                rasterDpi,
                rasterFormat,
                pinRequestedDpi,
                cts.Token);
            bool fastRowsRefreshed = true;
            if (readyBatch.FastPages.Count > 0)
            {
                InvalidatePagePreviewPrefetchCache();
                if (refreshSheetManager)
                    fastRowsRefreshed = RefreshSheetManagerRasterRows(readyBatch.FastPages);
                RefreshPageTreePageSnapshots(readyBatch.FastPages);
                ReloadCurrentPageIfRasterChanged(readyBatch.FastPages);
            }

            string rasterDpiLabel = SheetManagerRasterSelectionLabel(rasterDpi, rasterFormat);
            if (readyBatch.MissingPages.Count == 0)
            {
                if (refreshSheetManager && readyBatch.FastPages.Count > 0 && !fastRowsRefreshed)
                    RefreshSheetManager();
                TxtStatus.Text = $"Sheet Manager Raster On {rasterDpiLabel}: ready {readyBatch.Ready}, source {readyBatch.Source}, already {readyBatch.Already}, failed {readyBatch.Failed}.";
                return;
            }

            QueueSheetManagerRasterOnMissingInBackground(
                pages.ToList(),
                readyBatch,
                rasterDpi,
                rasterFormat,
                refreshSheetManager,
                pinRequestedDpi,
                cts);
            operationTransferredToBackground = true;
        }
        catch (OperationCanceledException)
        {
            InvalidatePagePreviewPrefetchCache();
            if (refreshSheetManager && !RefreshSheetManagerRasterRows(pages))
                RefreshSheetManager();
            RefreshPageTreePageSnapshots(pages);
            ReloadCurrentPageIfRasterChanged(pages);
            TxtStatus.Text = $"Sheet Manager Raster {operationLabel} cancelled.";
        }
        finally
        {
            if (!operationTransferredToBackground)
                FinishSheetManagerRasterOperation(cts);
        }
    }

    private void QueueSheetManagerRasterOnMissingInBackground(
        IReadOnlyList<PageInfo> pages,
        SheetManagerRasterReadyBatch readyBatch,
        int rasterDpi,
        string rasterFormat,
        bool refreshSheetManager,
        bool pinRequestedDpi,
        CancellationTokenSource cts)
    {
        string rasterDpiLabel = SheetManagerRasterSelectionLabel(rasterDpi, rasterFormat);
        TxtStatus.Text =
            $"Sheet Manager Raster On {rasterDpiLabel}: ready {readyBatch.Ready}, queued {readyBatch.MissingPages.Count} missing sheet(s).";
        _ = EnableMissingSheetManagerRasterOnInBackgroundAsync(
            pages,
            readyBatch,
            rasterDpi,
            rasterFormat,
            refreshSheetManager,
            pinRequestedDpi,
            cts);
    }

    private async Task EnableMissingSheetManagerRasterOnInBackgroundAsync(
        IReadOnlyList<PageInfo> pages,
        SheetManagerRasterReadyBatch readyBatch,
        int rasterDpi,
        string rasterFormat,
        bool refreshSheetManager,
        bool pinRequestedDpi,
        CancellationTokenSource cts)
    {
        int changed = readyBatch.Ready + readyBatch.Source;
        int built = 0;
        int reused = readyBatch.Ready;
        int already = readyBatch.Already;
        int failed = readyBatch.Failed;
        bool cancelled = false;
        string rasterDpiLabel = SheetManagerRasterSelectionLabel(rasterDpi, rasterFormat);
        IReadOnlyList<PageInfo> buildPages = readyBatch.MissingPages;

        try
        {
            for (int i = 0; i < buildPages.Count; i++)
            {
                cts.Token.ThrowIfCancellationRequested();

                PageInfo page = buildPages[i];
                int effectiveDpi = EffectiveSheetManagerRasterDpi(page, rasterDpi);
                float renderScale = RasterSheetCacheService.RasterDpiToRenderScale(effectiveDpi);
                TxtStatus.Text = $"Sheet Manager Raster On {SheetManagerRasterDpiProgressLabel(rasterDpi, effectiveDpi, rasterFormat)} {i + 1}/{buildPages.Count}: {page.Name}";

                RasterSheetBuildResult build;
                try
                {
                    build = await Task.Run(
                        () => BuildAndWarmSheetManagerRaster(
                            page,
                            renderScale,
                            rasterFormat,
                            allowPinnedDpiChange: true,
                            pinnedDpiOverride: pinRequestedDpi ? effectiveDpi : 0),
                        cts.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    AppLog.Warn(ex, $"Raster cache on build crashed for '{page.Name}'");
                    continue;
                }

                if (build.Ok)
                {
                    if (build.Reused)
                        reused++;
                    else
                        built++;
                    changed++;
                    RefreshSheetManagerRasterBackgroundPage(page, refreshSheetManager, reloadCurrentPage: true);
                }
                else
                {
                    failed++;
                    AppLog.Warn($"Raster cache on build failed for '{page.Name}': {build.Error}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            FinishSheetManagerRasterOperation(cts);
            InvalidatePagePreviewPrefetchCache();
            if (refreshSheetManager)
            {
                if (!RefreshSheetManagerRasterRows(pages))
                    RefreshSheetManager();
            }
            RefreshPageTreePageSnapshots(pages);
            ReloadCurrentPageIfRasterChanged(pages);
            TxtStatus.Text = cancelled
                ? $"Sheet Manager Raster On {rasterDpiLabel} cancelled: changed {changed}, built {built}, reused {reused}, already {already}, failed {failed}."
                : $"Sheet Manager Raster On {rasterDpiLabel} done: changed {changed}, built {built}, reused {reused}, already {already}, failed {failed}.";
        }
    }

    private async Task<SheetManagerRasterReadyBatch> EnableSheetManagerReadyRasterPagesAsync(
        IReadOnlyList<PageInfo> pages,
        int rasterDpi,
        string rasterFormat,
        bool pinRequestedDpi,
        CancellationToken cancellationToken)
    {
        string rasterDpiLabel = SheetManagerRasterSelectionLabel(rasterDpi, rasterFormat);
        TxtStatus.Text = $"Sheet Manager Raster On {rasterDpiLabel}: checking ready cache for {pages.Count} sheet(s)...";
        return await Task.Run(() =>
        {
            var plans = new List<(PageInfo Page, float RenderScale, bool SourceImage)>();
            var missingPages = new List<PageInfo>();
            foreach (PageInfo page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PageInfo currentPage = OurPlanCoreJobStore.TryReadPage(page.FolderPath) ?? page;
                if (RasterSheetCacheService.IsSourceImageRasterProfile(currentPage.RasterSheet))
                {
                    plans.Add((currentPage, 0f, true));
                    continue;
                }

                int effectiveDpi = EffectiveSheetManagerRasterDpi(currentPage, rasterDpi);
                float renderScale = RasterSheetCacheService.RasterDpiToRenderScale(effectiveDpi);
                if (!RasterSheetCacheService.HasReadyReadableRaster(currentPage, renderScale, rasterFormat))
                {
                    missingPages.Add(currentPage);
                    continue;
                }

                plans.Add((currentPage, renderScale, false));
            }

            var fastPages = new List<PageInfo>();
            int readyCount = 0;
            int sourceCount = 0;
            int alreadyCount = 0;
            int failedCount = 0;
            foreach (var plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (plan.SourceImage)
                {
                    if (RasterSheetCacheService.TrySetEnabledAndPinnedDpi(
                            plan.Page,
                            enabled: true,
                            pinnedDpi: 0,
                            out string error,
                            out bool toggled))
                    {
                        if (toggled)
                        {
                            if (OurPlanCoreJobStore.TryReadPage(plan.Page.FolderPath) is { } refreshedPage)
                                PdfViewport.WarmRasterSheetBitmapCache(refreshedPage);
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

                int effectiveDpi = RasterSheetCacheService.RenderScaleToDpi(plan.RenderScale);
                if (RasterSheetCacheService.TryEnableReadyReadableRaster(
                        plan.Page,
                        plan.RenderScale,
                        out RasterSheetBuildResult result,
                        rasterFormat,
                        allowPinnedDpiChange: true,
                        pinnedDpiOverride: pinRequestedDpi ? effectiveDpi : 0))
                {
                    WarmSheetManagerRasterBitmap(plan.Page, result);
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
        }, cancellationToken);
    }

    private static RasterSheetBuildResult BuildAndWarmSheetManagerRaster(
        PageInfo page,
        float renderScale,
        string rasterFormat = "",
        bool allowPinnedDpiChange = false,
        int? pinnedDpiOverride = null)
    {
        RasterSheetBuildResult result = RasterSheetCacheService.BuildAndEnable(
            page,
            renderScale,
            rasterFormat,
            allowPinnedDpiChange,
            pinnedDpiOverride);
        WarmSheetManagerRasterBitmap(page, result);
        return result;
    }

    private static RasterSheetBuildResult PrepareAndWarmSheetManagerRaster(PageInfo page, float renderScale, string rasterFormat = "")
    {
        RasterSheetBuildResult result = RasterSheetCacheService.BuildCachePreservingEnabled(page, renderScale, rasterFormat);
        WarmSheetManagerRasterBitmap(page, result);
        return result;
    }

    private static void WarmSheetManagerRasterBitmap(PageInfo page, RasterSheetBuildResult result)
    {
        if (result.Ok && result.Source != null)
            PdfViewport.WarmRasterSheetBitmapCache(page, result.Source);
    }

    private async Task SetSheetManagerRasterFirstAsync(IReadOnlyList<PageInfo> pages, bool enabled)
    {
        if (pages.Count == 0)
        {
            TxtStatus.Text = enabled
                ? "Sheet Manager Raster First On: no sheets selected."
                : "Sheet Manager Raster First Off: no sheets selected.";
            return;
        }

        string mode = enabled ? "On" : "Off";
        if (!TryBeginSheetManagerRasterOperation(
                $"First {mode}",
                $"switching Raster First {mode}",
                out CancellationTokenSource cts))
        {
            return;
        }

        try
        {
            TxtStatus.Text = $"Sheet Manager Raster First {mode}: updating {pages.Count} sheet(s)...";
            (int changed, int already, int failed) = await Task.Run(() =>
            {
                int changedCount = 0;
                int alreadyCount = 0;
                int failedCount = 0;
                foreach (PageInfo page in pages)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    if (RasterSheetCacheService.TrySetUseAsPageOpenRaster(page, enabled, out string error, out bool toggled))
                    {
                        if (toggled)
                            changedCount++;
                        else
                            alreadyCount++;
                    }
                    else
                    {
                        failedCount++;
                        AppLog.Warn($"Raster First toggle failed for '{page.Name}': {error}");
                    }
                }

                return (changedCount, alreadyCount, failedCount);
            }, cts.Token);

            InvalidatePagePreviewPrefetchCache();
            if (!RefreshSheetManagerRasterRows(pages))
                RefreshSheetManager();
            RefreshPageTreePageSnapshots(pages);
            ReloadCurrentPageIfRasterChanged(pages);
            TxtStatus.Text = $"Sheet Manager Raster First {mode}: changed {changed}, already {already}, failed {failed}.";
        }
        catch (OperationCanceledException)
        {
            InvalidatePagePreviewPrefetchCache();
            if (!RefreshSheetManagerRasterRows(pages))
                RefreshSheetManager();
            RefreshPageTreePageSnapshots(pages);
            ReloadCurrentPageIfRasterChanged(pages);
            TxtStatus.Text = $"Sheet Manager Raster First {mode} cancelled.";
        }
        finally
        {
            FinishSheetManagerRasterOperation(cts);
        }
    }

    private async Task SetSheetManagerRasterOffFastAsync(
        IReadOnlyList<PageInfo> pages,
        bool refreshSheetManager,
        CancellationToken cancellationToken)
    {
        TxtStatus.Text = $"Sheet Manager Raster Off: updating {pages.Count} sheet(s)...";
        (int changed, int already, int failed) = await Task.Run(() =>
        {
            int changedCount = 0;
            int alreadyCount = 0;
            int failedCount = 0;
            foreach (PageInfo page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (RasterSheetCacheService.TrySetEnabledAndPinnedDpi(
                        page,
                        enabled: false,
                        pinnedDpi: 0,
                        out string error,
                        out bool toggled))
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
        }, cancellationToken);

        InvalidatePagePreviewPrefetchCache();
        if (refreshSheetManager)
        {
            if (!RefreshSheetManagerRasterRows(pages))
                RefreshSheetManager();
        }
        RefreshPageTreePageSnapshots(pages);
        ReloadCurrentPageIfRasterChanged(pages);
        TxtStatus.Text = $"Sheet Manager Raster Off: changed {changed}, already {already}, failed {failed}.";
    }

    private void RefreshSheetManagerRasterBackgroundPage(PageInfo page, bool refreshSheetManager, bool reloadCurrentPage)
    {
        InvalidatePagePreviewPrefetchCache();
        if (refreshSheetManager && !RefreshSheetManagerRasterRows([page]))
            RefreshSheetManager();
        RefreshPageTreePageSnapshots([page]);
        if (reloadCurrentPage)
            ReloadCurrentPageIfRasterChanged([page]);
    }

    private void ReloadCurrentPageIfRasterChanged(IReadOnlyList<PageInfo> changedPages)
    {
        if (_currentPage == null ||
            !changedPages.Any(page => IsSamePageFolder(page.FolderPath, _currentPage.FolderPath)) ||
            OurPlanCoreJobStore.TryReadPage(_currentPage.FolderPath) is not { } refreshedPage)
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
        if (!IsModuleEnabled(ModuleId.TakeoffManager))
            return;

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
        if (!IsModuleEnabled(ModuleId.Ai))
            return;

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
