using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SmartTakeoffs.Controls;

public sealed class PdfMetadataPreviewRow
{
    public string PageFolder { get; init; } = "";
    public string CurrentPageName { get; init; } = "";
    public string SheetLabel { get; init; } = "";
    public string SheetTitle { get; init; } = "";
    public string ProposedPageName { get; init; } = "";
    public string Suffix { get; init; } = "";
    public string ProposedScale { get; init; } = "";
    public string Source { get; init; } = "";
    public string Confidence { get; init; } = "";
    public string Warnings { get; init; } = "";
    public bool ApplyRename { get; set; }
    public bool ApplyScale { get; set; }
}

public sealed class PdfMetadataPreviewDialog : Window
{
    public ObservableCollection<PdfMetadataPreviewRow> Rows { get; }

    public PdfMetadataPreviewDialog(IEnumerable<PdfMetadataPreviewRow> rows, string title)
    {
        Rows = new ObservableCollection<PdfMetadataPreviewRow>(rows);

        Title = title;
        Width = 1120;
        Height = 560;
        MinWidth = 860;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        var root = new DockPanel { Margin = new Thickness(12) };

        var summary = new TextBlock
        {
            Text = BuildSummary(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(summary, Dock.Top);
        root.Children.Add(summary);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var renameAll = new Button { Content = "Rename All", MinWidth = 88, Margin = new Thickness(0, 0, 6, 0) };
        var scaleAll = new Button { Content = "Scale All", MinWidth = 78, Margin = new Thickness(0, 0, 6, 0) };
        var clear = new Button { Content = "Clear", MinWidth = 66, Margin = new Thickness(0, 0, 18, 0) };
        var apply = new Button { Content = "Apply", MinWidth = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 78, IsCancel = true };
        buttons.Children.Add(renameAll);
        buttons.Children.Add(scaleAll);
        buttons.Children.Add(clear);
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = false,
            ItemsSource = Rows,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
        };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Rename", Binding = new Binding(nameof(PdfMetadataPreviewRow.ApplyRename)) });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Scale", Binding = new Binding(nameof(PdfMetadataPreviewRow.ApplyScale)) });
        grid.Columns.Add(TextColumn("Current Page", nameof(PdfMetadataPreviewRow.CurrentPageName), 150));
        grid.Columns.Add(TextColumn("Proposed Name", nameof(PdfMetadataPreviewRow.ProposedPageName), 140));
        grid.Columns.Add(TextColumn("Scale", nameof(PdfMetadataPreviewRow.ProposedScale), 120));
        grid.Columns.Add(TextColumn("Label", nameof(PdfMetadataPreviewRow.SheetLabel), 80));
        grid.Columns.Add(TextColumn("Suffix", nameof(PdfMetadataPreviewRow.Suffix), 60));
        grid.Columns.Add(TextColumn("Title", nameof(PdfMetadataPreviewRow.SheetTitle), 230));
        grid.Columns.Add(TextColumn("Source", nameof(PdfMetadataPreviewRow.Source), 90));
        grid.Columns.Add(TextColumn("Confidence", nameof(PdfMetadataPreviewRow.Confidence), 110));
        grid.Columns.Add(TextColumn("Warnings", nameof(PdfMetadataPreviewRow.Warnings), 260));
        root.Children.Add(grid);

        renameAll.Click += (_, _) =>
        {
            foreach (PdfMetadataPreviewRow row in Rows)
                row.ApplyRename = true;
            grid.Items.Refresh();
        };
        scaleAll.Click += (_, _) =>
        {
            foreach (PdfMetadataPreviewRow row in Rows.Where(row => row.ProposedScale.Length > 0 && row.ProposedScale != "skip"))
                row.ApplyScale = true;
            grid.Items.Refresh();
        };
        clear.Click += (_, _) =>
        {
            foreach (PdfMetadataPreviewRow row in Rows)
            {
                row.ApplyRename = false;
                row.ApplyScale = false;
            }
            grid.Items.Refresh();
        };
        apply.Click += (_, _) => DialogResult = true;

        Content = root;
    }

    private string BuildSummary()
    {
        int rename = Rows.Count(row => row.ApplyRename);
        int scale = Rows.Count(row => row.ApplyScale);
        int warning = Rows.Count(row => !string.IsNullOrWhiteSpace(row.Warnings));
        return $"{Rows.Count} detected page(s). Rename: {rename}. Scale: {scale}. Warnings: {warning}. Review before applying.";
    }

    private static DataGridTextColumn TextColumn(string header, string property, double width) =>
        new()
        {
            Header = header,
            Binding = new Binding(property),
            Width = width,
            IsReadOnly = true,
        };
}
