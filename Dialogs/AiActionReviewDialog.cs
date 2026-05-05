using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace OurPlaneCore.Controls;

public sealed class AiActionTargetOption
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string MeasurementType { get; init; } = "";
    public TakeoffItem? Item { get; init; }
    public bool CreatesNewItem { get; init; }

    public override string ToString() => Name;
}

public sealed class AiActionReviewRow
{
    public int Index { get; init; }
    public bool Accepted { get; set; }
    public string Confidence { get; init; } = "";
    public string Type { get; init; } = "";
    public string MeasurementType { get; init; } = "";
    public string Label { get; init; } = "";
    public string Page { get; init; } = "";
    public string Notes { get; init; } = "";
    public string Points { get; init; } = "";
    public string Status { get; init; } = "";
    public bool CanApply { get; init; }
    public AiActionTargetOption? Target { get; set; }
    public SmartAiAction Action { get; init; } = new();

    public static AiActionReviewRow FromAction(
        int index,
        SmartAiAction action,
        string resolvedPage,
        bool canApply,
        string status,
        AiActionTargetOption? target)
    {
        string measurementType = NormalizeMeasurementType(action);
        return new()
        {
            Index = index,
            Accepted = canApply,
            Confidence = action.Confidence > 0 ? action.Confidence.ToString("P0") : "",
            Type = string.IsNullOrWhiteSpace(action.Type) ? measurementType : action.Type.Trim(),
            MeasurementType = measurementType,
            Label = string.IsNullOrWhiteSpace(action.Label) ? action.Type : action.Label.Trim(),
            Page = resolvedPage,
            Notes = action.Notes.Trim(),
            Points = action.Points.Count.ToString(),
            Status = status,
            CanApply = canApply,
            Target = target,
            Action = action,
        };
    }

    private static string NormalizeMeasurementType(SmartAiAction action)
    {
        string value = action.MeasurementType;
        if (string.IsNullOrWhiteSpace(value))
        {
            value = action.Type.Contains("area", StringComparison.OrdinalIgnoreCase)
                ? "area"
                : action.Type.Contains("point", StringComparison.OrdinalIgnoreCase)
                    ? "point"
                    : "line";
        }

        return OurPlaneCoreJobStore.NormalizeMeasurementType(value.Trim().ToLowerInvariant());
    }
}

public sealed class AiActionReviewDialog : Window
{
    private readonly DataGrid _grid;
    private readonly TextBlock _summary;
    private readonly Action<IReadOnlyList<int>> _previewRequested;
    private readonly string _emptyApplyMessage;

    public ObservableCollection<AiActionReviewRow> Rows { get; }
    public IReadOnlyList<AiActionTargetOption> TargetOptions { get; }

    public IReadOnlyList<AiActionReviewRow> AcceptedRows =>
        Rows.Where(row => row.Accepted && row.CanApply && TargetUsable(row)).ToList();

    public IReadOnlyList<int> AcceptedIndices =>
        Rows.Where(row => row.Accepted).Select(row => row.Index).ToList();

    public IReadOnlyList<int> RejectedIndices =>
        Rows.Where(row => !row.Accepted).Select(row => row.Index).ToList();

    public AiActionReviewDialog(
        IEnumerable<AiActionReviewRow> rows,
        IReadOnlyList<AiActionTargetOption> targetOptions,
        Action<IReadOnlyList<int>> previewRequested,
        string title,
        string targetHeader = "Target Takeoff",
        string applyLabel = "Apply Accepted",
        string emptyApplyMessage = "Select at least one valid action with a target takeoff item before applying.")
    {
        Rows = new ObservableCollection<AiActionReviewRow>(rows);
        TargetOptions = targetOptions;
        _previewRequested = previewRequested;
        _emptyApplyMessage = emptyApplyMessage;

        Title = title;
        Width = 1160;
        Height = 560;
        MinWidth = 900;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        var root = new DockPanel { Margin = new Thickness(12) };

        _summary = new TextBlock
        {
            Text = BuildSummary(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(_summary, Dock.Top);
        root.Children.Add(_summary);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var acceptSelected = new Button { Content = "Accept Selected", MinWidth = 110, Margin = new Thickness(0, 0, 6, 0) };
        var rejectSelected = new Button { Content = "Reject Selected", MinWidth = 108, Margin = new Thickness(0, 0, 6, 0) };
        var acceptAllValid = new Button { Content = "Accept Valid", MinWidth = 92, Margin = new Thickness(0, 0, 6, 0) };
        var rejectAll = new Button { Content = "Reject All", MinWidth = 78, Margin = new Thickness(0, 0, 18, 0) };
        var preview = new Button { Content = "Preview Selected", MinWidth = 118, Margin = new Thickness(0, 0, 18, 0) };
        var apply = new Button { Content = applyLabel, MinWidth = 112, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 78, IsCancel = true };
        buttons.Children.Add(acceptSelected);
        buttons.Children.Add(rejectSelected);
        buttons.Children.Add(acceptAllValid);
        buttons.Children.Add(rejectAll);
        buttons.Children.Add(preview);
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = false,
            ItemsSource = Rows,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
        };
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Accept",
            Binding = new Binding(nameof(AiActionReviewRow.Accepted))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            },
        });
        _grid.Columns.Add(TextColumn("#", nameof(AiActionReviewRow.Index), 48));
        _grid.Columns.Add(TextColumn("Confidence", nameof(AiActionReviewRow.Confidence), 92));
        _grid.Columns.Add(TextColumn("Type", nameof(AiActionReviewRow.Type), 120));
        _grid.Columns.Add(TextColumn("Measure", nameof(AiActionReviewRow.MeasurementType), 80));
        _grid.Columns.Add(TextColumn("Label", nameof(AiActionReviewRow.Label), 150));
        _grid.Columns.Add(TextColumn("Page", nameof(AiActionReviewRow.Page), 105));
        _grid.Columns.Add(TextColumn("Points", nameof(AiActionReviewRow.Points), 58));
        _grid.Columns.Add(TextColumn("Status", nameof(AiActionReviewRow.Status), 170));
        _grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = targetHeader,
            ItemsSource = TargetOptions,
            SelectedItemBinding = new Binding(nameof(AiActionReviewRow.Target))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            },
            Width = 190,
        });
        _grid.Columns.Add(TextColumn("Notes", nameof(AiActionReviewRow.Notes), 260));
        root.Children.Add(_grid);

        acceptSelected.Click += (_, _) => SetSelectedAccepted(true);
        rejectSelected.Click += (_, _) => SetSelectedAccepted(false);
        acceptAllValid.Click += (_, _) =>
        {
            foreach (AiActionReviewRow row in Rows)
                row.Accepted = row.CanApply && TargetUsable(row);
            RefreshGrid();
        };
        rejectAll.Click += (_, _) =>
        {
            foreach (AiActionReviewRow row in Rows)
                row.Accepted = false;
            RefreshGrid();
        };
        preview.Click += (_, _) =>
        {
            CommitGridEdits();
            var selectedIndices = _grid.SelectedItems
                .OfType<AiActionReviewRow>()
                .Select(row => row.Index)
                .ToList();
            _previewRequested(selectedIndices.Count > 0
                ? selectedIndices
                : AcceptedRows.Select(row => row.Index).ToList());
        };
        apply.Click += (_, _) =>
        {
            CommitGridEdits();
            if (AcceptedRows.Count == 0)
            {
                MessageBox.Show(
                    _emptyApplyMessage,
                    "Review Action Draft",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
        };

        Content = root;
    }

    private void SetSelectedAccepted(bool accepted)
    {
        CommitGridEdits();
        foreach (AiActionReviewRow row in _grid.SelectedItems.OfType<AiActionReviewRow>())
            row.Accepted = accepted && row.CanApply && TargetUsable(row);
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        _grid.Items.Refresh();
        _summary.Text = BuildSummary();
    }

    private void CommitGridEdits()
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell, true);
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private string BuildSummary()
    {
        int valid = Rows.Count(row => row.CanApply);
        int accepted = Rows.Count(row => row.Accepted && row.CanApply && TargetUsable(row));
        int invalid = Rows.Count - valid;
        int missingTarget = Rows.Count(row => row.Accepted && row.CanApply && row.Target == null);
        int targetMismatch = Rows.Count(row => row.Accepted && row.CanApply && row.Target != null && !TargetUsable(row));
        return $"Actions: {Rows.Count}. Valid: {valid}. Accepted to apply: {accepted}. Invalid: {invalid}. Missing target: {missingTarget}. Target mismatch: {targetMismatch}.";
    }

    private static bool TargetUsable(AiActionReviewRow row) =>
        row.Target != null &&
        (row.Target.Item != null || row.Target.CreatesNewItem) &&
        string.Equals(row.Target.MeasurementType, row.MeasurementType, StringComparison.OrdinalIgnoreCase);

    private static DataGridTextColumn TextColumn(string header, string property, double width) =>
        new()
        {
            Header = header,
            Binding = new Binding(property),
            Width = width,
            IsReadOnly = true,
        };
}
