using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace OurPlaneCore;

public partial class MainWindow
{
    // ── Sort A/S ─────────────────────────────────────────────────────────
    private CheckBox? _asDash;
    private readonly ObservableCollection<ArchStructRule> _asRules = [];
    private DataGrid? _asGrid;
    private TextBlock? _asStatus;

    // ── Sort D/Sec/WT ────────────────────────────────────────────────────
    private ListBox? _topBox;
    private ListBox? _detBox;
    private readonly ObservableCollection<FolderPlanNode> _topOrder = [];
    private readonly ObservableCollection<FolderPlanNode> _detOrder = [];
    private readonly ObservableCollection<SuffixRule> _sxRules = [];
    private DataGrid? _sxGrid;
    private TextBlock? _sxStatus;

    private static readonly string[] AsKinds = ["FirstLetter", "FileKeyword"];
    private static readonly string[] AsTargets = ["Arch", "Struct", "Others"];
    private static readonly string[] SuffixTargets =
        ["top", "details struct", "details arch", "finish", "shear walls", "units", "sections", "--------others"];

    private void PersistPageSort(bool job)
    {
        SyncPageSortFromUi();
        if (job)
        {
            if (_currentJob == null) { TxtStatus.Text = "Open a job to save a per-job override."; return; }
            SettingsPresetStore.SaveJobPageSortOverride(_currentJob, _psConfig);
            TxtStatus.Text = "Saved as this job's page-sort rules (overrides global).";
        }
        else
        {
            SettingsPresetStore.SaveGlobalPageSort(_psConfig);
            TxtStatus.Text = "Saved as global default page-sort rules.";
        }
        PageSortRulesService.Install(_psConfig);
    }

    private void InstallWorkingPageSort() => PageSortRulesService.Install(_psConfig);

    private void SyncPageSortFromUi()
    {
        if (_asDash != null)
            _psConfig.ArchStructDashToOthers = _asDash.IsChecked == true;
        _psConfig.ArchStructRules = _asRules
            .Where(r => !string.IsNullOrWhiteSpace(r.Match))
            .Select(r => r.Clone())
            .ToList();
        _psConfig.SuffixTopOrder = _topOrder
            .Select(n => n.Name.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0)
            .ToList();
        _psConfig.SuffixDetectionOrder = _detOrder
            .Select(n => n.Name.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0)
            .ToList();
        _psConfig.SuffixRules = _sxRules
            .Where(r => !string.IsNullOrWhiteSpace(r.Suffix) && !string.IsNullOrWhiteSpace(r.Target))
            .Select(r => r.Clone())
            .ToList();
    }

    // ── Sort A/S panel ───────────────────────────────────────────────────
    private FrameworkElement BuildArchStructPanel()
    {
        var root = new DockPanel();

        var top = HBar();
        top.Children.Add(MgrButton("Add rule", (_, _) =>
        {
            _asRules.Add(new ArchStructRule { Kind = "FileKeyword", Match = "", Target = "Arch" });
            _asGrid?.Items.Refresh();
        }));
        top.Children.Add(MgrButton("Remove selected", (_, _) =>
        {
            foreach (var r in _asGrid?.SelectedItems.OfType<ArchStructRule>().ToList() ?? [])
                _asRules.Remove(r);
        }));
        top.Children.Add(MgrButton("Reset to default", (_, _) =>
        {
            _psConfig.ArchStructDashToOthers = PageSortConfig.BuildDefault().ArchStructDashToOthers;
            _psConfig.ArchStructRules = PageSortConfig.BuildDefault().ArchStructRules;
            BindArchStruct();
        }));
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        _asDash = new CheckBox
        {
            Content = "Page name ends with “-”  →  --------others",
            Margin = new Thickness(0, 0, 0, 8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(_asDash, Dock.Top);
        root.Children.Add(_asDash);

        var act = HBar();
        act.Children.Add(MgrButton("Apply — Sort A/S now", (_, _) =>
        {
            if (_currentJob == null) { MessageBox.Show("Open a job first.", "Sort A/S"); return; }
            SyncPageSortFromUi();
            InstallWorkingPageSort();
            SortPagesIntoArchStruct();
        }, primary: true));
        act.Children.Add(MgrButton("Save global default", (_, _) => PersistPageSort(false)));
        act.Children.Add(MgrButton("Save as this job", (_, _) => PersistPageSort(true)));
        DockPanel.SetDock(act, Dock.Top);
        root.Children.Add(act);

        _asStatus = StatusLine();
        DockPanel.SetDock(_asStatus, Dock.Top);
        root.Children.Add(_asStatus);

        _asGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            ItemsSource = _asRules,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
        };
        _asGrid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Kind",
            Width = 130,
            ItemsSource = AsKinds,
            SelectedItemBinding = new Binding(nameof(ArchStructRule.Kind)),
        });
        _asGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Match (letter / filename keyword)",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            Binding = new Binding(nameof(ArchStructRule.Match)) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
        });
        _asGrid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Target",
            Width = 110,
            ItemsSource = AsTargets,
            SelectedItemBinding = new Binding(nameof(ArchStructRule.Target)),
        });
        root.Children.Add(_asGrid);
        return root;
    }

    private void BindArchStruct()
    {
        if (_asDash != null)
            _asDash.IsChecked = _psConfig.ArchStructDashToOthers;
        _asRules.Clear();
        foreach (var r in _psConfig.ArchStructRules)
            _asRules.Add(r.Clone());
        _asGrid?.Items.Refresh();
        if (_asStatus != null)
            _asStatus.Text =
                "First-letter rules match the page name’s first letter; filename rules search the source PDF name. " +
                "First-letter rules win, then filename rules — top-down. Apply runs Sort A/S on the selected scope.";
    }

    // ── Sort D/Sec/WT panel ──────────────────────────────────────────────
    private FrameworkElement BuildSuffixSortPanel()
    {
        var root = new DockPanel();

        var act = HBar();
        act.Children.Add(MgrButton("Apply — Sort D/Sec/WT now", (_, _) =>
        {
            if (_currentJob == null) { MessageBox.Show("Open a job first.", "Sort D/Sec/WT"); return; }
            SyncPageSortFromUi();
            InstallWorkingPageSort();
            SortPagesBySuffix();
        }, primary: true));
        act.Children.Add(MgrButton("Save global default", (_, _) => PersistPageSort(false)));
        act.Children.Add(MgrButton("Save as this job", (_, _) => PersistPageSort(true)));
        act.Children.Add(MgrButton("Reset all to default", (_, _) =>
        {
            var d = PageSortConfig.BuildDefault();
            _psConfig.SuffixTopOrder = d.SuffixTopOrder;
            _psConfig.SuffixDetectionOrder = d.SuffixDetectionOrder;
            _psConfig.SuffixRules = d.SuffixRules;
            BindSuffixSort();
        }));
        DockPanel.SetDock(act, Dock.Top);
        root.Children.Add(act);

        _sxStatus = StatusLine();
        DockPanel.SetDock(_sxStatus, Dock.Top);
        root.Children.Add(_sxStatus);

        // Two suffix-list editors side by side over the rule grid.
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(168) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _topBox = SuffixListBox(_topOrder);
        var topCol = SuffixListColumn(
            "Top order — these suffixes float to the scope root",
            _topBox, _topOrder);
        Grid.SetColumn(topCol, 0);
        Grid.SetRow(topCol, 0);
        grid.Children.Add(topCol);

        _detBox = SuffixListBox(_detOrder);
        var detCol = SuffixListColumn(
            "Detection order — suffix recognition priority",
            _detBox, _detOrder);
        Grid.SetColumn(detCol, 1);
        Grid.SetRow(detCol, 0);
        grid.Children.Add(detCol);

        var ruleBar = HBar();
        ruleBar.Margin = new Thickness(0, 8, 0, 6);
        ruleBar.Children.Add(new TextBlock
        {
            Text = "Suffix → folder rules",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });
        ruleBar.Children.Add(MgrButton("Add rule", (_, _) =>
        {
            _sxRules.Add(new SuffixRule { Suffix = "", FirstLetter = "", Target = "units" });
            _sxGrid?.Items.Refresh();
        }));
        ruleBar.Children.Add(MgrButton("Remove selected", (_, _) =>
        {
            foreach (var r in _sxGrid?.SelectedItems.OfType<SuffixRule>().ToList() ?? [])
                _sxRules.Remove(r);
        }));
        Grid.SetColumnSpan(ruleBar, 2);
        Grid.SetRow(ruleBar, 1);
        grid.Children.Add(ruleBar);

        _sxGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            ItemsSource = _sxRules,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
        };
        _sxGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Suffix",
            Width = 120,
            Binding = new Binding(nameof(SuffixRule.Suffix)) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
        });
        _sxGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "First letter (blank = any)",
            Width = 150,
            Binding = new Binding(nameof(SuffixRule.FirstLetter)) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
        });
        _sxGrid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Target folder",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            ItemsSource = SuffixTargets,
            SelectedItemBinding = new Binding(nameof(SuffixRule.Target)),
        });
        Grid.SetColumnSpan(_sxGrid, 2);
        Grid.SetRow(_sxGrid, 2);
        grid.Children.Add(_sxGrid);

        root.Children.Add(grid);
        return root;
    }

    private ListBox SuffixListBox(ObservableCollection<FolderPlanNode> src) => new()
    {
        ItemsSource = src,
        DisplayMemberPath = nameof(FolderPlanNode.Name),
        BorderThickness = new Thickness(1),
        BorderBrush = TryFindResource("ControlBorderBrush") as Brush,
        Margin = new Thickness(0, 0, 0, 0),
    };

    private FrameworkElement SuffixListColumn(
        string caption,
        ListBox box,
        ObservableCollection<FolderPlanNode> src)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        var cap = new TextBlock
        {
            Text = caption,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
        };
        DockPanel.SetDock(cap, Dock.Top);
        dock.Children.Add(cap);

        var bar = HBar();
        bar.Children.Add(MgrButton("Add", (_, _) =>
        {
            var n = PromptText("Add suffix", "x");
            if (n != null) src.Add(new FolderPlanNode { Name = n });
        }));
        bar.Children.Add(MgrButton("Rename", (_, _) =>
        {
            if (box.SelectedItem is FolderPlanNode s)
            {
                var n = PromptText("Rename suffix", s.Name);
                if (n != null) { s.Name = n; box.Items.Refresh(); }
            }
        }));
        bar.Children.Add(MgrButton("Remove", (_, _) =>
        {
            if (box.SelectedItem is FolderPlanNode s) src.Remove(s);
        }));
        bar.Children.Add(MgrButton("↑", (_, _) => MoveInList(box, src, -1)));
        bar.Children.Add(MgrButton("↓", (_, _) => MoveInList(box, src, 1)));
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);

        dock.Children.Add(box);
        return dock;
    }

    private static void MoveInList(ListBox box, ObservableCollection<FolderPlanNode> src, int delta)
    {
        if (box.SelectedItem is not FolderPlanNode s) return;
        int i = src.IndexOf(s), j = i + delta;
        if (j < 0 || j >= src.Count) return;
        src.Move(i, j);
        box.SelectedIndex = j;
    }

    private void BindSuffixSort()
    {
        _topOrder.Clear();
        foreach (string s in _psConfig.SuffixTopOrder)
            _topOrder.Add(new FolderPlanNode { Name = s });
        _detOrder.Clear();
        foreach (string s in _psConfig.SuffixDetectionOrder)
            _detOrder.Add(new FolderPlanNode { Name = s });
        _sxRules.Clear();
        foreach (var r in _psConfig.SuffixRules)
            _sxRules.Add(r.Clone());
        _sxGrid?.Items.Refresh();
        if (_sxStatus != null)
            _sxStatus.Text =
                "A page’s suffix is detected by Detection order. If it is in Top order it stays at the scope root; " +
                "otherwise the first matching Suffix→folder rule moves it. Target “top” = scope root. " +
                "Apply runs Sort D/Sec/WT on the selected scope.";
    }
}
