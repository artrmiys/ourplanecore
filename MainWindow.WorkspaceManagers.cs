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
            case "AiManager":
                RefreshAiManager();
                break;
            case "3DManager":
                Refresh3dManagerSummary();
                break;
        }
    }

    private void BtnSheetManagerRefresh_Click(object sender, RoutedEventArgs e) => RefreshSheetManager();
    private async void BtnSheetManagerAnalyze_Click(object sender, RoutedEventArgs e) => await AnalyzeSheetManagerAsync(defaultRename: false, defaultScale: false);
    private async void BtnSheetManagerAutoName_Click(object sender, RoutedEventArgs e) => await AnalyzeSheetManagerAsync(defaultRename: true, defaultScale: false);
    private async void BtnSheetManagerAutoScale_Click(object sender, RoutedEventArgs e) => await AnalyzeSheetManagerAsync(defaultRename: false, defaultScale: true);
    private async void BtnSheetManagerAutoNameScale_Click(object sender, RoutedEventArgs e) => await AnalyzeSheetManagerAsync(defaultRename: true, defaultScale: true);

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
        return new DataTemplate { VisualTree = textBox };
    }

    private static void SheetManagerTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
            textBox.SelectAll();
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

        _sheetManagerMetadataResults = results;
        var rows = BuildPdfMetadataPreviewRows(results, defaultRename, defaultScale).ToList();
        rows.AddRange(results
            .Where(result => !result.Ok)
            .Select(result => new PdfMetadataPreviewRow
            {
                PageFolder = result.Page.FolderPath,
                CurrentPageName = result.Page.Name,
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
            if (!row.ApplyRename &&
                !string.IsNullOrWhiteSpace(row.ProposedPageName) &&
                !string.Equals(row.ProposedPageName.Trim(), row.CurrentPageName, StringComparison.OrdinalIgnoreCase))
            {
                row.ApplyRename = true;
            }

            if (row.ApplyScale ||
                string.IsNullOrWhiteSpace(row.ProposedScale) ||
                string.Equals(row.ProposedScale.Trim(), "skip", StringComparison.OrdinalIgnoreCase) ||
                OurPlaneCoreJobStore.TryReadPage(row.PageFolder) is not { } page)
            {
                continue;
            }

            string currentScale = SheetManagerScaleText(page.ScaleMetersPerPt);
            if (!string.Equals(row.ProposedScale.Trim(), currentScale, StringComparison.OrdinalIgnoreCase))
                row.ApplyScale = true;
        }
    }

    private void BtnSheetManagerOpenSheet_Click(object sender, RoutedEventArgs e)
    {
        if (SheetManagerGrid.SelectedItem is not PdfMetadataPreviewRow row)
            return;

        SelectPageByFolder(row.PageFolder);
        WorkspaceTabs.SelectedIndex = 0;
    }

    private void BtnSheetManagerOpenJson_Click(object sender, RoutedEventArgs e)
    {
        if (SheetManagerGrid.SelectedItem is PdfMetadataPreviewRow row)
            OpenSourcePdfMetadata(row.PageFolder);
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

        SetActiveTakeoffTarget(FindTakeoffTreeItem(item), item);
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
        if (SelectedAiManagerItem() is { } item && CanRunAiRequest(item))
            await RunAiRequestAsync(item);
    }

    private void Btn3dManagerBuildFromTakeoffs_Click(object sender, RoutedEventArgs e)
    {
        BuildMassingDraftFromWallTakeoffs();
        Refresh3dManagerSummary();
    }

    private void Btn3dManagerOpenWindow_Click(object sender, RoutedEventArgs e) => OpenMassing3DWindow();
    private void Btn3dManagerOpenJson_Click(object sender, RoutedEventArgs e) => OpenMassingDraftJson();

    private void Refresh3dManagerSummary()
    {
        if (_currentJob == null)
        {
            Txt3dManagerSummary.Text = "Open a job to use the 3D manager.";
            return;
        }

        try
        {
            string path = SmartMassingDraftService.ModelPath(_currentJob);
            SmartMassingDraft? draft = _currentMassingDraft;
            if (draft == null && File.Exists(path))
                draft = SmartMassingDraftService.LoadDraft(_currentJob);
            Txt3dManagerSummary.Text = draft == null
                ? $"No 3D draft found at {Path.GetRelativePath(_currentJob.RootPath, path)}."
                : BuildMassingDraftSummary(draft, path);
        }
        catch (Exception ex)
        {
            Txt3dManagerSummary.Text = ex.Message;
        }
    }
}
