using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace OurPlaneCore;

public partial class MainWindow
{
    private bool _reportBuilderColumnsConfigured;

    private void BtnReportBuilderReload_Click(object sender, RoutedEventArgs e) => RefreshReportBuilder(forceReload: true);

    private void BtnReportBuilderRefresh_Click(object sender, RoutedEventArgs e) => RefreshReportBuilder(forceReload: false);

    private void BtnReportBuilderApplyWalls_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<ReportBuilderRow> allRows = ReportBuilderRows();
        if (allRows.Count == 0)
        {
            RefreshReportBuilder(forceReload: true);
            allRows = ReportBuilderRows();
        }

        List<ReportBuilderRow> selectedRows = ReportBuilderGrid.SelectedItems
            .OfType<ReportBuilderRow>()
            .Concat(ReportBuilderGrid.SelectedCells
                .Select(cell => cell.Item)
                .OfType<ReportBuilderRow>())
            .Distinct()
            .OrderBy(row => row.RowNumber)
            .ToList();
        if (selectedRows.Count == 0)
        {
            TxtStatus.Text = "Report Builder: select wall source rows in J:K first.";
            return;
        }

        IReadOnlyList<ReportWallSourceRow> sourceRows = ReportWallsBlockService.SourceRowsFromReportRows(selectedRows);
        ReportWallBlockResult result = ReportWallsBlockService.ApplyAllGroups(allRows, sourceRows);
        ReportBuilderGrid.Items.Refresh();
        TxtStatus.Text =
            $"Report Builder walls: groups {result.FloorGroups}, source rows {result.SourceRows}, assigned {result.Assignments}.";
        ShowFolderTemplateErrors("Report Builder Walls", result.Messages);
    }

    private void RefreshReportBuilder(bool forceReload = false)
    {
        if (!forceReload && ReportBuilderGrid.ItemsSource != null)
            return;

        try
        {
            ReportTemplateSheet sheet = ReportTemplateService.LoadDefaultTemplate();
            ConfigureReportBuilderColumns(sheet.Columns);
            ReportBuilderGrid.ItemsSource = sheet.Rows;
            TxtReportBuilderTemplate.Text = Path.GetFileName(sheet.TemplatePath);
            TxtReportBuilderTemplate.ToolTip = sheet.TemplatePath;
            TxtStatus.Text = $"Report Builder: {sheet.SheetName}, {sheet.Rows.Count} row(s).";
        }
        catch (Exception ex)
        {
            ReportBuilderGrid.ItemsSource = Array.Empty<ReportBuilderRow>();
            TxtReportBuilderTemplate.Text = "Template missing";
            TxtReportBuilderTemplate.ToolTip = ReportTemplateService.DefaultTemplatePath();
            TxtStatus.Text = $"Report Builder: {ex.Message}";
        }
    }

    private void ConfigureReportBuilderColumns(IReadOnlyList<ReportTemplateColumn> columns)
    {
        if (_reportBuilderColumnsConfigured)
            return;

        ReportBuilderGrid.Columns.Clear();
        ReportBuilderGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "#",
            Binding = new Binding(nameof(ReportBuilderRow.RowLabel)),
            Width = new DataGridLength(46),
            IsReadOnly = true,
        });

        foreach (ReportTemplateColumn column in columns)
        {
            ReportBuilderGrid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Header,
                Binding = new Binding(column.BindingPath)
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                },
                Width = new DataGridLength(column.Width),
                ElementStyle = ReportBuilderTextBlockStyle(),
                EditingElementStyle = ReportBuilderTextBoxStyle(),
            });
        }

        _reportBuilderColumnsConfigured = true;
    }

    private IReadOnlyList<ReportBuilderRow> ReportBuilderRows() =>
        ReportBuilderGrid.ItemsSource?.OfType<ReportBuilderRow>().ToList() ?? [];

    private static Style ReportBuilderTextBlockStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(4, 2, 4, 2)));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        return style;
    }

    private static Style ReportBuilderTextBoxStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(TextBox.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(TextBox.PaddingProperty, new Thickness(4, 1, 4, 1)));
        style.Setters.Add(new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        return style;
    }
}
