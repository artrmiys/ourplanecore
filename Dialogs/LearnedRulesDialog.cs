using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SmartTakeoffs;

namespace SmartTakeoffs.Controls;

public sealed class LearnedRuleRow
{
    public bool Enabled { get; set; }
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public string TitleToken { get; init; } = "";
    public string Suffix { get; init; } = "";
    public bool SkipScale { get; init; }
    public string ScaleText { get; init; } = "";
    public int Support { get; init; }
    public string Confidence { get; init; } = "";

    public static LearnedRuleRow FromRule(SmartLearnedRule rule) =>
        new()
        {
            Enabled = rule.Enabled,
            Id = rule.Id,
            Kind = rule.Kind,
            TitleToken = rule.TitleToken,
            Suffix = rule.Suffix,
            SkipScale = rule.SkipScale,
            ScaleText = rule.ScaleText,
            Support = rule.Support,
            Confidence = rule.Confidence,
        };

    public SmartLearnedRule ToRule() =>
        new()
        {
            Enabled = Enabled,
            Id = Id,
            Kind = Kind,
            TitleToken = TitleToken,
            Suffix = Suffix,
            SkipScale = SkipScale,
            ScaleText = ScaleText,
            Support = Support,
            Confidence = Confidence,
        };
}

public sealed class LearnedRulesDialog : Window
{
    private readonly SmartLearnedRuleSet _source;
    private readonly TextBlock _summary;

    public ObservableCollection<LearnedRuleRow> Rows { get; }
    public SmartLearnedRuleSet RuleSet { get; private set; }

    public LearnedRulesDialog(SmartLearnedRuleSet rules, string title)
    {
        _source = rules;
        Rows = new ObservableCollection<LearnedRuleRow>(
            rules.Rules
                .OrderByDescending(rule => rule.Enabled)
                .ThenByDescending(rule => rule.Support)
                .ThenBy(rule => rule.TitleToken)
                .Select(LearnedRuleRow.FromRule));
        RuleSet = CloneRuleSet();

        Title = title;
        Width = 980;
        Height = 520;
        MinWidth = 780;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        var root = new DockPanel { Margin = new Thickness(12) };

        _summary = new TextBlock
        {
            Text = BuildSummary(),
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
        var enableSelected = new Button { Content = "Enable Selected", MinWidth = 112, Margin = new Thickness(0, 0, 6, 0) };
        var disableSelected = new Button { Content = "Disable Selected", MinWidth = 116, Margin = new Thickness(0, 0, 6, 0) };
        var enableAll = new Button { Content = "Enable All", MinWidth = 82, Margin = new Thickness(0, 0, 18, 0) };
        var save = new Button { Content = "Save", MinWidth = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 78, IsCancel = true };
        buttons.Children.Add(enableSelected);
        buttons.Children.Add(disableSelected);
        buttons.Children.Add(enableAll);
        buttons.Children.Add(save);
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
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Enabled",
            Binding = new Binding(nameof(LearnedRuleRow.Enabled))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            },
        });
        grid.Columns.Add(TextColumn("Token", nameof(LearnedRuleRow.TitleToken), 130));
        grid.Columns.Add(TextColumn("Suffix", nameof(LearnedRuleRow.Suffix), 70));
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Skip Scale",
            Binding = new Binding(nameof(LearnedRuleRow.SkipScale)),
            IsReadOnly = true,
        });
        grid.Columns.Add(TextColumn("Scale", nameof(LearnedRuleRow.ScaleText), 130));
        grid.Columns.Add(TextColumn("Support", nameof(LearnedRuleRow.Support), 70));
        grid.Columns.Add(TextColumn("Confidence", nameof(LearnedRuleRow.Confidence), 95));
        grid.Columns.Add(TextColumn("Kind", nameof(LearnedRuleRow.Kind), 150));
        grid.Columns.Add(TextColumn("Id", nameof(LearnedRuleRow.Id), 210));
        root.Children.Add(grid);

        enableSelected.Click += (_, _) => SetSelectedEnabled(grid, true);
        disableSelected.Click += (_, _) => SetSelectedEnabled(grid, false);
        enableAll.Click += (_, _) =>
        {
            foreach (LearnedRuleRow row in Rows)
                row.Enabled = true;
            grid.Items.Refresh();
            RefreshSummary();
        };
        save.Click += (_, _) =>
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            RuleSet = CloneRuleSet();
            DialogResult = true;
        };

        Content = root;
    }

    private void SetSelectedEnabled(DataGrid grid, bool enabled)
    {
        foreach (LearnedRuleRow row in grid.SelectedItems.OfType<LearnedRuleRow>())
            row.Enabled = enabled;
        grid.Items.Refresh();
        RefreshSummary();
    }

    private void RefreshSummary() => _summary.Text = BuildSummary();

    private string BuildSummary()
    {
        int enabled = Rows.Count(row => row.Enabled);
        return $"Rules: {Rows.Count}. Enabled: {enabled}. Disabled: {Rows.Count - enabled}.";
    }

    private SmartLearnedRuleSet CloneRuleSet() =>
        new()
        {
            SchemaVersion = _source.SchemaVersion,
            GeneratedAtUtc = _source.GeneratedAtUtc,
            SourceRecordCount = _source.SourceRecordCount,
            Rules = Rows.Select(row => row.ToRule()).ToList(),
        };

    private static DataGridTextColumn TextColumn(string header, string property, double width) =>
        new()
        {
            Header = header,
            Binding = new Binding(property),
            Width = width,
            IsReadOnly = true,
        };
}
