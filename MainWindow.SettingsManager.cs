using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private bool _settingsBuilt;
    private ListBox? _settingsCategoryList;
    private ContentControl _settingsHost = new();
    private readonly Dictionary<string, FrameworkElement> _settingsPanels = new();

    // From Pages working state
    private FolderPlan _fromPagesPlan = new();
    private ObservableCollection<FolderPlanNode> _fromPagesTop = [];
    private TreeView? _fromPagesSubTree;
    private ComboBox? _fromPagesPreset;
    private TextBlock? _fromPagesStatus;

    // Auto rename/scale working state
    private ComboBox? _rulesScope;
    private ObservableCollection<LearnedRuleRow> _ruleRows = [];
    private DataGrid? _rulesGrid;

    private static readonly string[] SettingsCategories =
    [
        "From Pages",
        "Sort & Grouping",
        "Auto Rename / Scale",
        "Defaults",
    ];

    private void RefreshSettingsManager()
    {
        if (!_settingsBuilt)
            BuildSettingsManager();

        // Re-resolve data for the visible category.
        string cat = (_settingsCategoryList?.SelectedItem as string) ?? SettingsCategories[0];
        ShowSettingsCategory(cat);
    }

    private void BuildSettingsManager()
    {
        _settingsBuilt = true;
        SettingsManagerRoot.Children.Clear();

        var bar = new Border { Style = TryFindResource("ManagerToolbarBand") as Style };
        var barPanel = new WrapPanel { Style = TryFindResource("ManagerToolbar") as Style };
        barPanel.Children.Add(new TextBlock { Text = "Settings", Style = TryFindResource("ManagerGroupLabel") as Style });
        barPanel.Children.Add(new TextBlock
        {
            Text = "Rules & defaults — preview, edit, presets (global, override per job).",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
            FontSize = 12,
        });
        bar.Child = barPanel;
        DockPanel.SetDock(bar, Dock.Top);
        SettingsManagerRoot.Children.Add(bar);

        _settingsCategoryList = new ListBox
        {
            Width = 170,
            BorderThickness = new Thickness(0, 0, 1, 0),
            BorderBrush = TryFindResource("ControlBorderBrush") as Brush,
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 0, 0, 0),
        };
        foreach (string c in SettingsCategories)
            _settingsCategoryList.Items.Add(c);
        _settingsCategoryList.SelectionChanged += (_, _) =>
        {
            if (_settingsCategoryList.SelectedItem is string s)
                ShowSettingsCategory(s);
        };
        DockPanel.SetDock(_settingsCategoryList, Dock.Left);
        SettingsManagerRoot.Children.Add(_settingsCategoryList);

        _settingsHost = new ContentControl { Margin = new Thickness(10, 6, 6, 6) };
        SettingsManagerRoot.Children.Add(_settingsHost);

        _settingsCategoryList.SelectedIndex = 0;
    }

    private void ShowSettingsCategory(string category)
    {
        if (!_settingsPanels.TryGetValue(category, out FrameworkElement? panel))
        {
            panel = category switch
            {
                "From Pages" => BuildFromPagesPanel(),
                "Sort & Grouping" => BuildSortPanel(),
                "Auto Rename / Scale" => BuildRulesPanel(),
                _ => BuildDefaultsPanel(),
            };
            _settingsPanels[category] = panel;
        }

        _settingsHost.Content = panel;

        if (category == "From Pages") LoadFromPagesPlan();
        else if (category == "Auto Rename / Scale") LoadRuleRows();
    }

    // ── helpers ──────────────────────────────────────────────────────────
    private Button MgrButton(string text, RoutedEventHandler onClick, bool primary = false)
    {
        var b = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 6, 4),
            Style = TryFindResource(primary ? "ManagerPrimaryButton" : "ManagerButton") as Style,
        };
        b.Click += onClick;
        return b;
    }

    private static StackPanel HBar() =>
        new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

    private string? PromptText(string title, string initial)
    {
        var win = new Window
        {
            Title = title,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
        };
        var root = new StackPanel { Margin = new Thickness(14) };
        var box = new TextBox { Text = initial, FontSize = 13, Padding = new Thickness(5, 3, 5, 3) };
        root.Children.Add(box);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "OK", MinWidth = 76, IsDefault = true, Margin = new Thickness(0, 0, 6, 0), Style = TryFindResource("ManagerPrimaryButton") as Style };
        var cancel = new Button { Content = "Cancel", MinWidth = 76, IsCancel = true, Style = TryFindResource("ManagerButton") as Style };
        bool okClicked = false;
        ok.Click += (_, _) => { okClicked = true; win.Close(); };
        cancel.Click += (_, _) => win.Close();
        row.Children.Add(ok);
        row.Children.Add(cancel);
        root.Children.Add(row);
        win.Content = root;
        box.Focus();
        box.SelectAll();
        win.ShowDialog();
        string result = box.Text.Trim();
        return okClicked && result.Length > 0 ? result : null;
    }

    // ── From Pages ───────────────────────────────────────────────────────
    private FrameworkElement BuildFromPagesPanel()
    {
        var root = new DockPanel();

        var top = HBar();
        top.Children.Add(new TextBlock { Text = "Mode:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        foreach (string m in new[] { "AUTO", "COM", "EWP" })
        {
            var rb = new RadioButton
            {
                Content = m,
                GroupName = "FromPagesMode",
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = m,
            };
            rb.Checked += (_, _) =>
            {
                if (rb.Tag is string mm && _currentJob != null)
                {
                    _fromPagesPlan.Mode = PlanSwiftFolderTemplateService.ResolveMode(_currentJob, mm);
                    _fromPagesPlan.SubTree = PlanSwiftFolderTemplateService.DefaultSubTree(_fromPagesPlan.Mode);
                    BindFromPages();
                }
            };
            top.Children.Add(rb);
        }
        _fromPagesPreset = new ComboBox { Width = 150, Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        top.Children.Add(new TextBlock { Text = "Preset:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) });
        top.Children.Add(_fromPagesPreset);
        top.Children.Add(MgrButton("Load", (_, _) => LoadSelectedPreset()));
        top.Children.Add(MgrButton("Save as preset", (_, _) => SaveFromPagesPreset()));
        top.Children.Add(MgrButton("Reset to default", (_, _) => ResetFromPagesDefault()));
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var actions = HBar();
        actions.Children.Add(MgrButton("Apply — create these folders", (_, _) => ApplyFromPages(), primary: true));
        actions.Children.Add(MgrButton("Save as this job's plan", (_, _) => SaveJobPlan()));
        actions.Children.Add(MgrButton("Save as global default", (_, _) => SaveGlobalPlan()));
        actions.Children.Add(MgrButton("Reload top from Pages", (_, _) => ReloadTopFromPages()));
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        _fromPagesStatus = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
            FontSize = 12,
        };
        DockPanel.SetDock(_fromPagesStatus, Dock.Top);
        root.Children.Add(_fromPagesStatus);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });

        // Left: top folders
        var leftDock = new DockPanel();
        leftDock.Children.Add(Header("Top folders (created under Takeoffs)"));
        var topBtns = HBar();
        topBtns.Children.Add(MgrButton("Add", (_, _) => AddTopFolder()));
        topBtns.Children.Add(MgrButton("Rename", (_, _) => RenameTop()));
        topBtns.Children.Add(MgrButton("Remove", (_, _) => RemoveTop()));
        DockPanel.SetDock(topBtns, Dock.Bottom);
        leftDock.Children.Add(topBtns);
        _fromPagesTopList = new ListBox
        {
            ItemsSource = _fromPagesTop,
            DisplayMemberPath = nameof(FolderPlanNode.Name),
        };
        leftDock.Children.Add(_fromPagesTopList);
        Grid.SetColumn(leftDock, 0);
        grid.Children.Add(leftDock);

        // Right: sub-tree
        var rightDock = new DockPanel();
        rightDock.Children.Add(Header("Sub-tree created under EACH top folder"));
        var subBtns = HBar();
        subBtns.Children.Add(MgrButton("Add root", (_, _) => AddSub(root: true)));
        subBtns.Children.Add(MgrButton("Add child", (_, _) => AddSub(root: false)));
        subBtns.Children.Add(MgrButton("Rename", (_, _) => RenameSub()));
        subBtns.Children.Add(MgrButton("Remove", (_, _) => RemoveSub()));
        DockPanel.SetDock(subBtns, Dock.Bottom);
        rightDock.Children.Add(subBtns);
        _fromPagesSubTree = new TreeView { BorderThickness = new Thickness(1), BorderBrush = TryFindResource("ControlBorderBrush") as Brush };
        rightDock.Children.Add(_fromPagesSubTree);
        Grid.SetColumn(rightDock, 2);
        grid.Children.Add(rightDock);

        root.Children.Add(grid);
        return root;
    }

    private ListBox? _fromPagesTopList;

    private static TextBlock Header(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4),
        };
        DockPanel.SetDock(tb, Dock.Top);
        return tb;
    }

    private void LoadFromPagesPlan()
    {
        if (_currentJob == null)
        {
            if (_fromPagesStatus != null) _fromPagesStatus.Text = "Open a job to edit the From Pages plan.";
            _fromPagesPlan = new FolderPlan();
            _fromPagesTop.Clear();
            if (_fromPagesSubTree != null) _fromPagesSubTree.Items.Clear();
            return;
        }

        _fromPagesPlan = SettingsPresetStore.ResolveFromPagesPlan(_currentJob);
        RefreshPresetCombo();
        BindFromPages();
    }

    private void RefreshPresetCombo()
    {
        if (_fromPagesPreset == null) return;
        _fromPagesPreset.Items.Clear();
        foreach (FolderPlan p in SettingsPresetStore.LoadFromPagesPresets().Presets)
            _fromPagesPreset.Items.Add(p.Name);
    }

    private void BindFromPages()
    {
        _fromPagesTop.Clear();
        foreach (string n in _fromPagesPlan.TopFolders)
            _fromPagesTop.Add(new FolderPlanNode { Name = n });

        if (_fromPagesSubTree != null)
        {
            _fromPagesSubTree.Items.Clear();
            foreach (FolderPlanNode node in _fromPagesPlan.SubTree)
                _fromPagesSubTree.Items.Add(BuildTreeItem(node));
        }

        if (_fromPagesStatus != null)
            _fromPagesStatus.Text =
                $"Mode {_fromPagesPlan.Mode}. {_fromPagesPlan.TopFolders.Count} top folder(s); " +
                $"{CountNodes(_fromPagesPlan.SubTree)} sub-folder(s) created under each. Apply creates exactly this.";
    }

    private static int CountNodes(IReadOnlyList<FolderPlanNode> nodes) =>
        nodes.Count + nodes.Sum(n => CountNodes(n.Children));

    private static TreeViewItem BuildTreeItem(FolderPlanNode node)
    {
        var item = new TreeViewItem { Header = node.Name, Tag = node, IsExpanded = true };
        foreach (FolderPlanNode c in node.Children)
            item.Items.Add(BuildTreeItem(c));
        return item;
    }

    private void SyncTopToPlan() =>
        _fromPagesPlan.TopFolders = _fromPagesTop.Select(n => n.Name).Where(s => s.Length > 0).ToList();

    private void AddTopFolder()
    {
        string? name = PromptText("Add top folder", "New Folder");
        if (name == null) return;
        _fromPagesTop.Add(new FolderPlanNode { Name = name });
        SyncTopToPlan();
        BindFromPages();
    }

    private void RenameTop()
    {
        if (_fromPagesTopList?.SelectedItem is not FolderPlanNode sel) return;
        string? name = PromptText("Rename folder", sel.Name);
        if (name == null) return;
        sel.Name = name;
        SyncTopToPlan();
        BindFromPages();
    }

    private void RemoveTop()
    {
        if (_fromPagesTopList?.SelectedItem is not FolderPlanNode sel) return;
        _fromPagesTop.Remove(sel);
        SyncTopToPlan();
        BindFromPages();
    }

    private void ReloadTopFromPages()
    {
        if (_currentJob == null) return;
        _fromPagesPlan.TopFolders = PlanSwiftFolderTemplateService.CollectCapsGroupNames(_currentJob).ToList();
        BindFromPages();
    }

    private FolderPlanNode? SelectedSubNode() =>
        (_fromPagesSubTree?.SelectedItem as TreeViewItem)?.Tag as FolderPlanNode;

    private void AddSub(bool root)
    {
        string? name = PromptText(root ? "Add root sub-folder" : "Add child sub-folder", "New Folder");
        if (name == null) return;
        var node = new FolderPlanNode { Name = name };
        if (root || SelectedSubNode() is not { } parent)
            _fromPagesPlan.SubTree.Add(node);
        else
            parent.Children.Add(node);
        BindFromPages();
    }

    private void RenameSub()
    {
        if (SelectedSubNode() is not { } sel) return;
        string? name = PromptText("Rename sub-folder", sel.Name);
        if (name == null) return;
        sel.Name = name;
        BindFromPages();
    }

    private void RemoveSub()
    {
        if (SelectedSubNode() is not { } sel) return;
        RemoveNode(_fromPagesPlan.SubTree, sel);
        BindFromPages();
    }

    private static bool RemoveNode(List<FolderPlanNode> nodes, FolderPlanNode target)
    {
        if (nodes.Remove(target)) return true;
        foreach (FolderPlanNode n in nodes)
            if (RemoveNode(n.Children, target)) return true;
        return false;
    }

    private void ResetFromPagesDefault()
    {
        if (_currentJob == null) return;
        _fromPagesPlan = PlanSwiftFolderTemplateService.BuildDefaultPlan(_currentJob);
        BindFromPages();
        TxtStatus.Text = "From Pages plan reset to default.";
    }

    private void SaveFromPagesPreset()
    {
        SyncTopToPlan();
        string? name = PromptText("Preset name", _fromPagesPlan.Name == "Default" ? "My Preset" : _fromPagesPlan.Name);
        if (name == null) return;
        FolderPlanPresets presets = SettingsPresetStore.LoadFromPagesPresets();
        presets.Presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        FolderPlan clone = _fromPagesPlan.Clone();
        clone.Name = name;
        presets.Presets.Add(clone);
        SettingsPresetStore.SaveFromPagesPresets(presets);
        RefreshPresetCombo();
        TxtStatus.Text = $"Saved From Pages preset '{name}'.";
    }

    private void LoadSelectedPreset()
    {
        if (_fromPagesPreset?.SelectedItem is not string name) return;
        FolderPlan? p = SettingsPresetStore.LoadFromPagesPresets().Presets
            .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (p == null) return;
        _fromPagesPlan = p.Clone();
        if (_currentJob != null && _fromPagesPlan.TopFolders.Count == 0)
            _fromPagesPlan.TopFolders = PlanSwiftFolderTemplateService.CollectCapsGroupNames(_currentJob).ToList();
        BindFromPages();
        TxtStatus.Text = $"Loaded preset '{name}'.";
    }

    private void SaveJobPlan()
    {
        if (_currentJob == null) return;
        SyncTopToPlan();
        SettingsPresetStore.SaveJobFromPagesPlan(_currentJob, _fromPagesPlan.Clone());
        TxtStatus.Text = "Saved as this job's From Pages plan (overrides global).";
    }

    private void SaveGlobalPlan()
    {
        SyncTopToPlan();
        SettingsPresetStore.SaveGlobalActiveFromPagesPlan(_fromPagesPlan.Clone());
        TxtStatus.Text = "Saved as global default From Pages plan.";
    }

    private void ApplyFromPages()
    {
        if (_currentJob == null) return;
        SyncTopToPlan();
        if (_fromPagesPlan.TopFolders.Count == 0)
        {
            MessageBox.Show("No top folders to create. Add some or Reload from Pages.", "From Pages",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Create {_fromPagesPlan.TopFolders.Count} top folder(s), each with {CountNodes(_fromPagesPlan.SubTree)} sub-folder(s), under Takeoffs?\nExisting folders are skipped.",
            "From Pages", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            FlushTakeoffAutosaves();
            CapsTakeoffFolderResult r = PlanSwiftFolderTemplateService.CreateFoldersFromPlan(
                _currentJob.TakeoffsRoot, _fromPagesPlan);
            LoadTakeoffsForJob();
            TxtStatus.Text =
                $"From Pages applied: top created {r.TopCreated}, skipped {r.TopSkipped}, " +
                $"sub created {r.SubCreated}, skipped {r.SubSkipped}, errors {r.Errors}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("From Pages", ex);
        }
    }

    // ── Sort & Grouping ──────────────────────────────────────────────────
    private FrameworkElement BuildSortPanel()
    {
        var root = new StackPanel { Margin = new Thickness(2) };
        root.Children.Add(Header("Takeoff tree sort order (default)"));
        var az = new RadioButton { Content = "A → Z", GroupName = "SortDir", Margin = new Thickness(0, 4, 0, 4), IsChecked = !_settings.TakeoffSortDescending };
        var za = new RadioButton { Content = "Z → A", GroupName = "SortDir", Margin = new Thickness(0, 0, 0, 8), IsChecked = _settings.TakeoffSortDescending };
        az.Checked += (_, _) => { _settings.TakeoffSortDescending = false; SaveAppSettings(); };
        za.Checked += (_, _) => { _settings.TakeoffSortDescending = true; SaveAppSettings(); };
        root.Children.Add(az);
        root.Children.Add(za);
        var row = HBar();
        row.Children.Add(MgrButton("Apply to whole tree now", (_, _) => ApplySortNow(), primary: true));
        root.Children.Add(row);
        root.Children.Add(new TextBlock
        {
            Text = "Default direction is global and is used when sorting folders. 'Apply' sorts the Takeoffs root and its sub-folders now.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
        });
        return root;
    }

    private void ApplySortNow()
    {
        if (_currentJob == null) return;
        bool desc = _settings.TakeoffSortDescending;
        SortTakeoffChildren(_currentJob.TakeoffsRoot, desc);
        foreach (string dir in System.IO.Directory.GetDirectories(_currentJob.TakeoffsRoot))
            SortTakeoffChildren(dir, desc);
        TxtStatus.Text = $"Sorted Takeoffs tree {(desc ? "Z→A" : "A→Z")}.";
    }

    // ── Auto Rename / Scale ──────────────────────────────────────────────
    private FrameworkElement BuildRulesPanel()
    {
        var root = new DockPanel();
        var top = HBar();
        top.Children.Add(new TextBlock { Text = "Scope:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _rulesScope = new ComboBox { Width = 150, VerticalAlignment = VerticalAlignment.Center };
        _rulesScope.Items.Add("Global");
        _rulesScope.Items.Add("This job");
        _rulesScope.SelectedIndex = 0;
        _rulesScope.SelectionChanged += (_, _) => LoadRuleRows();
        top.Children.Add(_rulesScope);
        top.Children.Add(MgrButton("Enable all", (_, _) => { foreach (var r in _ruleRows) r.Enabled = true; _rulesGrid?.Items.Refresh(); }));
        top.Children.Add(MgrButton("Disable selected", (_, _) => SetSelRules(false)));
        top.Children.Add(MgrButton("Enable selected", (_, _) => SetSelRules(true)));
        top.Children.Add(MgrButton("Delete selected", (_, _) => DeleteSelRules()));
        top.Children.Add(MgrButton("Save", (_, _) => SaveRuleRows(), primary: true));
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        _rulesGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            ItemsSource = _ruleRows,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
        };
        _rulesGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "On",
            Binding = new Binding(nameof(LearnedRuleRow.Enabled)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
        });
        void Col(string h, string p, double w) => _rulesGrid.Columns.Add(new DataGridTextColumn { Header = h, Binding = new Binding(p), Width = w, IsReadOnly = true });
        Col("Token", nameof(LearnedRuleRow.TitleToken), 150);
        Col("Suffix (rename)", nameof(LearnedRuleRow.Suffix), 90);
        Col("Skip scale", nameof(LearnedRuleRow.SkipScale), 70);
        Col("Scale", nameof(LearnedRuleRow.ScaleText), 120);
        Col("Support", nameof(LearnedRuleRow.Support), 70);
        Col("Confidence", nameof(LearnedRuleRow.Confidence), 90);
        Col("Kind", nameof(LearnedRuleRow.Kind), 150);
        root.Children.Add(_rulesGrid);
        return root;
    }

    private void LoadRuleRows()
    {
        _ruleRows.Clear();
        SmartLearnedRuleSet set = GetRuleSetForScope();
        foreach (var r in set.Rules
                     .OrderByDescending(r => r.Enabled)
                     .ThenByDescending(r => r.Support)
                     .ThenBy(r => r.TitleToken))
        {
            _ruleRows.Add(LearnedRuleRow.FromRule(r));
        }
        _rulesGrid?.Items.Refresh();
    }

    private SmartLearnedRuleSet GetRuleSetForScope()
    {
        bool job = _rulesScope?.SelectedIndex == 1;
        if (job && _currentJob != null)
            return SmartLearningStore.LoadProjectLearnedRules(_currentJob);
        return SmartLearningStore.LoadGlobalLearnedRules();
    }

    private void SetSelRules(bool enabled)
    {
        if (_rulesGrid == null) return;
        foreach (var r in _rulesGrid.SelectedItems.OfType<LearnedRuleRow>())
            r.Enabled = enabled;
        _rulesGrid.Items.Refresh();
    }

    private void DeleteSelRules()
    {
        if (_rulesGrid == null) return;
        foreach (var r in _rulesGrid.SelectedItems.OfType<LearnedRuleRow>().ToList())
            _ruleRows.Remove(r);
        _rulesGrid.Items.Refresh();
    }

    private void SaveRuleRows()
    {
        var set = new SmartLearnedRuleSet { Rules = _ruleRows.Select(r => r.ToRule()).ToList() };
        bool job = _rulesScope?.SelectedIndex == 1;
        if (job)
        {
            if (_currentJob == null) { TxtStatus.Text = "Open a job to save job rules."; return; }
            SmartLearningStore.SaveProjectLearnedRules(_currentJob, set);
            TxtStatus.Text = "Saved job Auto Rename/Scale rules.";
        }
        else
        {
            SmartLearningStore.SaveGlobalLearnedRules(set);
            TxtStatus.Text = "Saved global Auto Rename/Scale rules.";
        }
    }

    // ── Defaults ─────────────────────────────────────────────────────────
    private FrameworkElement BuildDefaultsPanel()
    {
        var root = new StackPanel { Margin = new Thickness(2) };
        root.Children.Add(Header("Display & export defaults"));
        root.Children.Add(new TextBlock
        {
            Text = "Viewport and PDF export appearance defaults live on the top ribbon tabs.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 8),
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
        });
        var row = HBar();
        row.Children.Add(MgrButton("Open Viewport defaults", (_, _) => SelectTopTab("Viewport")));
        row.Children.Add(MgrButton("Open PDF Output defaults", (_, _) => SelectTopTab("PDF Output")));
        root.Children.Add(row);
        return root;
    }

    private void SelectTopTab(string header)
    {
        foreach (object o in TopMainTabs.Items)
            if (o is TabItem t && string.Equals(t.Header?.ToString(), header, StringComparison.Ordinal))
            {
                TopMainTabs.SelectedItem = t;
                return;
            }
    }
}
