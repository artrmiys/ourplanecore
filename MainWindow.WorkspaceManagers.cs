using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    private bool _sheetManagerEditableColumnsConfigured;
    private bool _updatingSheetManagerBulkEdit;

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

        var results = new List<PdfMetadataPageResult>();
        var rows = new List<PdfMetadataPreviewRow>();
        foreach (PageInfo page in CollectPagesUnder(_currentJob.PagesRoot))
        {
            PdfSheetMetadata? metadata = OurPlaneCoreJobStore.ReadSourcePdfMetadata(page.FolderPath);
            if (metadata != null)
            {
                var result = new PdfMetadataPageResult(page, true, metadata, "");
                results.Add(result);
                rows.AddRange(BuildPdfMetadataPreviewRows([result], defaultRename: false, defaultScale: false));
                continue;
            }

            rows.Add(new PdfMetadataPreviewRow
            {
                PageFolder = page.FolderPath,
                CurrentPageName = page.Name,
                ProposedPageName = page.Name,
                ProposedScale = SheetManagerScaleText(page.ScaleMetersPerPt),
                RasterStatus = RasterSheetCacheService.DisplayStatus(page),
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
        var rows = BuildPdfMetadataPreviewRows(results, defaultRename, defaultScale).ToList();
        rows.AddRange(results
            .Where(result => !result.Ok)
            .Select(result => new PdfMetadataPreviewRow
            {
                PageFolder = result.Page.FolderPath,
                CurrentPageName = result.Page.Name,
                RasterStatus = RasterSheetCacheService.DisplayStatus(result.Page),
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
        await RunAsyncUiHandler(
            () => BuildSheetManagerRasterCacheAsync(SelectedSheetManagerPagesForRaster()),
            "Build Raster failed.",
            "Sheet Manager Raster");
    }

    private async void BtnSheetManagerRasterOn_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            () => SetSheetManagerRasterEnabledAsync(SelectedSheetManagerPagesForRaster(), enabled: true),
            "Raster On failed.",
            "Sheet Manager Raster");
    }

    private async void BtnSheetManagerRasterOff_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            () => SetSheetManagerRasterEnabledAsync(SelectedSheetManagerPagesForRaster(), enabled: false),
            "Raster Off failed.",
            "Sheet Manager Raster");
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

    private async Task BuildSheetManagerRasterCacheAsync(IReadOnlyList<PageInfo> pages)
    {
        if (pages.Count == 0)
        {
            TxtStatus.Text = "Sheet Manager Raster: no sheets selected.";
            return;
        }

        int ok = 0;
        int failed = 0;
        using (ShowBusyOverlay($"Building raster cache for {pages.Count} sheet(s)..."))
        {
            await WaitForBusyOverlayRenderAsync();
            for (int i = 0; i < pages.Count; i++)
            {
                PageInfo page = pages[i];
                BusyOverlayText.Text = $"Raster {i + 1}/{pages.Count}: {page.Name}";
                RasterSheetBuildResult result = await Task.Run(() => RasterSheetCacheService.BuildAndEnable(page));
                if (result.Ok)
                    ok++;
                else
                {
                    failed++;
                    AppLog.Warn($"Raster cache build failed for '{page.Name}': {result.Error}");
                }
            }
        }

        InvalidatePagePreviewPrefetchCache();
        RefreshSheetManager();
        ReloadCurrentPageIfRasterChanged(pages);
        TxtStatus.Text = $"Sheet Manager Raster: built {ok}, failed {failed}.";
    }

    private async Task SetSheetManagerRasterEnabledAsync(IReadOnlyList<PageInfo> pages, bool enabled)
    {
        if (pages.Count == 0)
        {
            TxtStatus.Text = "Sheet Manager Raster: no sheets selected.";
            return;
        }

        int changed = 0;
        int built = 0;
        int failed = 0;
        using (ShowBusyOverlay($"{(enabled ? "Enabling" : "Disabling")} raster for {pages.Count} sheet(s)..."))
        {
            await WaitForBusyOverlayRenderAsync();
            for (int i = 0; i < pages.Count; i++)
            {
                PageInfo page = pages[i];
                BusyOverlayText.Text = $"{(enabled ? "Raster On" : "Raster Off")} {i + 1}/{pages.Count}: {page.Name}";
                if (enabled && (page.RasterSheet == null || string.IsNullOrWhiteSpace(page.RasterSheet.Image)))
                {
                    RasterSheetBuildResult build = await Task.Run(() => RasterSheetCacheService.BuildAndEnable(page));
                    if (build.Ok)
                    {
                        built++;
                        changed++;
                    }
                    else
                    {
                        failed++;
                        AppLog.Warn($"Raster cache build failed for '{page.Name}': {build.Error}");
                    }
                    continue;
                }

                if (RasterSheetCacheService.TrySetEnabled(page, enabled, out string error))
                    changed++;
                else
                {
                    failed++;
                    AppLog.Warn($"Raster cache toggle failed for '{page.Name}': {error}");
                }
            }
        }

        InvalidatePagePreviewPrefetchCache();
        RefreshSheetManager();
        ReloadCurrentPageIfRasterChanged(pages);
        TxtStatus.Text = enabled
            ? $"Sheet Manager Raster On: changed {changed}, built {built}, failed {failed}."
            : $"Sheet Manager Raster Off: changed {changed}, failed {failed}.";
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
