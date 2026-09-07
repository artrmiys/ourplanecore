using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace OurPlanCore.Controls;

public enum PdfMetadataScaleAction
{
    Keep,
    Set,
    Clear,
}

public sealed class PdfMetadataPreviewRow : INotifyPropertyChanged
{
    public static IReadOnlyList<PdfMetadataScaleAction> AvailableScaleActions { get; } =
        System.Enum.GetValues<PdfMetadataScaleAction>();

    private string _proposedPageName = "";
    private string _proposedScale = "";
    private string _rasterStatus = "";
    private bool _applyRename;
    private PdfMetadataScaleAction _scaleAction = PdfMetadataScaleAction.Keep;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PageFolder { get; init; } = "";
    public string CurrentPageName { get; init; } = "";
    public string CurrentScale { get; init; } = "";
    public string SheetLabel { get; init; } = "";
    public string SheetTitle { get; init; } = "";
    public string ProposedPageName
    {
        get => _proposedPageName;
        set => SetField(ref _proposedPageName, value ?? "");
    }
    public string Suffix { get; init; } = "";
    public string ProposedScale
    {
        get => _proposedScale;
        set => SetField(ref _proposedScale, value ?? "");
    }
    public string Source { get; init; } = "";
    public string Confidence { get; init; } = "";
    public string TitleSource { get; init; } = "";
    public string TitleConfidence { get; init; } = "";
    public string TitleEvidence { get; init; } = "";
    public string SuffixSource { get; init; } = "";
    public string SuffixConfidence { get; init; } = "";
    public string SuffixEvidence { get; init; } = "";
    public string ScaleSource { get; init; } = "";
    public string ScaleConfidence { get; init; } = "";
    public string ScaleEvidence { get; init; } = "";
    public bool CanSetScale { get; init; }
    public bool ReviewScale { get; init; }
    public string RasterStatus
    {
        get => _rasterStatus;
        set => SetField(ref _rasterStatus, value ?? "");
    }
    public string Reason { get; init; } = "";
    public string Warnings { get; init; } = "";
    public bool ApplyRename
    {
        get => _applyRename;
        set => SetField(ref _applyRename, value);
    }
    public bool ApplyScale
    {
        get => ScaleAction != PdfMetadataScaleAction.Keep;
        set => ScaleAction = value ? PdfMetadataScaleAction.Set : PdfMetadataScaleAction.Keep;
    }
    public PdfMetadataScaleAction ScaleAction
    {
        get => _scaleAction;
        set
        {
            if (EqualityComparer<PdfMetadataScaleAction>.Default.Equals(_scaleAction, value))
                return;

            _scaleAction = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScaleAction)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApplyScale)));
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class PdfMetadataPreviewDialog : Window
{
    public ObservableCollection<PdfMetadataPreviewRow> Rows { get; }

    public PdfMetadataPreviewDialog(IEnumerable<PdfMetadataPreviewRow> rows, string title)
    {
        Rows = new ObservableCollection<PdfMetadataPreviewRow>(rows);

        Title = title;
        Width = 1540;
        Height = 620;
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
        grid.Columns.Add(ScaleActionColumn());
        grid.Columns.Add(TextColumn("Current Name", nameof(PdfMetadataPreviewRow.CurrentPageName), 150));
        grid.Columns.Add(EditableTextColumn("Proposed Name", nameof(PdfMetadataPreviewRow.ProposedPageName), 150));
        grid.Columns.Add(TextColumn("Name Source", nameof(PdfMetadataPreviewRow.TitleSource), 110));
        grid.Columns.Add(TextColumn("Name Confidence", nameof(PdfMetadataPreviewRow.TitleConfidence), 100));
        grid.Columns.Add(TextColumn("Name Evidence", nameof(PdfMetadataPreviewRow.TitleEvidence), 220));
        grid.Columns.Add(TextColumn("Label", nameof(PdfMetadataPreviewRow.SheetLabel), 80));
        grid.Columns.Add(TextColumn("Suffix", nameof(PdfMetadataPreviewRow.Suffix), 70));
        grid.Columns.Add(TextColumn("Suffix Source", nameof(PdfMetadataPreviewRow.SuffixSource), 110));
        grid.Columns.Add(TextColumn("Suffix Confidence", nameof(PdfMetadataPreviewRow.SuffixConfidence), 100));
        grid.Columns.Add(TextColumn("Suffix Evidence", nameof(PdfMetadataPreviewRow.SuffixEvidence), 220));
        grid.Columns.Add(TextColumn("Title", nameof(PdfMetadataPreviewRow.SheetTitle), 230));
        grid.Columns.Add(TextColumn("Current Scale", nameof(PdfMetadataPreviewRow.CurrentScale), 115));
        grid.Columns.Add(EditableTextColumn("Scale", nameof(PdfMetadataPreviewRow.ProposedScale), 120));
        grid.Columns.Add(TextColumn("Scale Source", nameof(PdfMetadataPreviewRow.ScaleSource), 110));
        grid.Columns.Add(TextColumn("Scale Confidence", nameof(PdfMetadataPreviewRow.ScaleConfidence), 100));
        grid.Columns.Add(TextColumn("Scale Evidence", nameof(PdfMetadataPreviewRow.ScaleEvidence), 220));
        grid.Columns.Add(TextColumn("Why", nameof(PdfMetadataPreviewRow.Reason), 320));
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
            foreach (PdfMetadataPreviewRow row in Rows.Where(row => row.CanSetScale))
                row.ScaleAction = PdfMetadataScaleAction.Set;
            grid.Items.Refresh();
        };
        clear.Click += (_, _) =>
        {
            foreach (PdfMetadataPreviewRow row in Rows)
            {
                row.ApplyRename = false;
                row.ScaleAction = PdfMetadataScaleAction.Keep;
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

    private static DataGridComboBoxColumn ScaleActionColumn() =>
        new()
        {
            Header = "Scale Action",
            ItemsSource = System.Enum.GetValues<PdfMetadataScaleAction>(),
            SelectedItemBinding = new Binding(nameof(PdfMetadataPreviewRow.ScaleAction))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            },
            Width = 92,
        };

    private static DataGridTemplateColumn EditableTextColumn(string header, string property, double width) =>
        new()
        {
            Header = header,
            Width = width,
            SortMemberPath = property,
            CellTemplate = EditableTextTemplate(property),
        };

    private static DataTemplate EditableTextTemplate(string property)
    {
        var textBox = new FrameworkElementFactory(typeof(PdfMetadataTextBox));
        textBox.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
        textBox.SetValue(TextBox.PaddingProperty, new Thickness(4, 1, 4, 1));
        textBox.SetValue(TextBox.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        textBox.SetValue(FrameworkElement.TagProperty, property);
        textBox.SetValue(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        textBox.SetValue(FrameworkElement.MinWidthProperty, 70.0);
        textBox.SetBinding(
            TextBox.TextProperty,
            new Binding(property)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
        textBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(PdfMetadataPreviewTextBox_TextChanged));
        return new DataTemplate { VisualTree = textBox };
    }

    private static void PdfMetadataPreviewTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox ||
            !textBox.IsKeyboardFocusWithin ||
            textBox.DataContext is not PdfMetadataPreviewRow row ||
            textBox.Tag is not string property)
        {
            return;
        }

        if (property == nameof(PdfMetadataPreviewRow.ProposedPageName))
        {
            row.ApplyRename =
                !string.IsNullOrWhiteSpace(row.ProposedPageName) &&
                !string.Equals(row.ProposedPageName, row.CurrentPageName, System.StringComparison.OrdinalIgnoreCase);
        }
        else if (property == nameof(PdfMetadataPreviewRow.ProposedScale))
        {
            row.ScaleAction =
                !string.IsNullOrWhiteSpace(row.ProposedScale) &&
                !string.Equals(row.ProposedScale.Trim(), "skip", System.StringComparison.OrdinalIgnoreCase)
                    ? PdfMetadataScaleAction.Set
                    : PdfMetadataScaleAction.Keep;
        }
    }
}
