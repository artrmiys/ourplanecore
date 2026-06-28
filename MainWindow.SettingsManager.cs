using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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

    // Defaults panel: live mouse-wheel zoom step control.
    private Slider? _defaultsZoomSlider;
    private TextBlock? _defaultsZoomValue;
    private CheckBox? _defaultsTakeoffSectionsBox;
    private bool _defaultsZoomSyncing;

    private FolderTemplateConfig _ftConfig = FolderTemplateConfig.BuildDefault();
    private PageSortConfig _psConfig = PageSortConfig.BuildDefault();

    // Page Folders editor
    private ComboBox? _pfMode;
    private readonly ObservableCollection<FolderPlanNode> _pfList = [];
    private ListBox? _pfListBox;
    private TextBlock? _pfStatus;

    // Auto Tree editor
    private ComboBox? _atMode;
    private TreeView? _atTree;
    private TextBlock? _atStatus;

    // From Pages editor
    private ComboBox? _fpMode;
    private readonly ObservableCollection<FolderPlanNode> _fpTop = [];
    private ListBox? _fpTopBox;
    private TextBlock? _fpStatus;

    // Auto rename/scale
    private ComboBox? _rulesScope;
    private readonly ObservableCollection<LearnedRuleRow> _ruleRows = [];
    private DataGrid? _rulesGrid;

    private static readonly string[] SettingsCategories =
    [
        "Page Folders",
        "Auto Tree",
        "From Pages",
        "Takeoff Templates",
        "Sort A/S",
        "Sort D/Sec/WT",
        "Auto Rename / Scale",
        "Defaults",
    ];

    // Called on job open so edited templates apply app-wide (menus too).
    private void ApplyFolderTemplateProviders()
    {
        SettingsPresetStore.InstallProviders(_currentJob);
        SettingsPresetStore.InstallPageSortProvider(_currentJob);
        _ftConfig = SettingsPresetStore.Resolve(_currentJob).Clone();
        _psConfig = SettingsPresetStore.ResolvePageSort(_currentJob).Clone();
        _takeoffTemplateConfig = TakeoffTemplateStore.ResolveConfig(_currentJob).Clone();
        RefreshTakeoffTemplateEditors();
    }

    private void RefreshSettingsManager()
    {
        if (!_settingsBuilt)
            BuildSettingsManager();
        _ftConfig = SettingsPresetStore.Resolve(_currentJob).Clone();
        _psConfig = SettingsPresetStore.ResolvePageSort(_currentJob).Clone();
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
            Text = "Edit templates & rules (default, presets — global, override per job). Edits apply everywhere.",
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
                "Page Folders" => BuildPageFoldersPanel(),
                "Auto Tree" => BuildAutoTreePanel(),
                "From Pages" => BuildFromPagesPanel(),
                "Takeoff Templates" => BuildTakeoffTemplatesSettingsPanel(),
                "Sort A/S" => BuildArchStructPanel(),
                "Sort D/Sec/WT" => BuildSuffixSortPanel(),
                "Auto Rename / Scale" => BuildRulesPanel(),
                _ => BuildDefaultsPanel(),
            };
            _settingsPanels[category] = panel;
        }

        _settingsHost.Content = panel;

        switch (category)
        {
            case "Page Folders": BindPageFolders(); break;
            case "Auto Tree": BindAutoTree(); break;
            case "From Pages": BindFromPages(); break;
            case "Takeoff Templates": BindTakeoffTemplatesSettings(); break;
            case "Sort A/S": BindArchStruct(); break;
            case "Sort D/Sec/WT": BindSuffixSort(); break;
            case "Auto Rename / Scale": LoadRuleRows(); break;
            case "Defaults": SyncDefaultsZoomControl(); break;
        }
    }

    // ── shared helpers ───────────────────────────────────────────────────
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

    private ComboBox ModeCombo(SelectionChangedEventHandler onChange)
    {
        var cb = new ComboBox { Width = 90, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        cb.Items.Add("COM");
        cb.Items.Add("EWP");
        cb.SelectedIndex = 0;
        cb.SelectionChanged += onChange;
        return cb;
    }

    private static string ModeOf(ComboBox? cb) => (cb?.SelectedItem as string) ?? "COM";

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
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", MinWidth = 76, IsDefault = true, Margin = new Thickness(0, 0, 6, 0), Style = TryFindResource("ManagerPrimaryButton") as Style };
        var cancel = new Button { Content = "Cancel", MinWidth = 76, IsCancel = true, Style = TryFindResource("ManagerButton") as Style };
        bool okay = false;
        ok.Click += (_, _) => { okay = true; win.Close(); };
        cancel.Click += (_, _) => win.Close();
        bar.Children.Add(ok);
        bar.Children.Add(cancel);
        root.Children.Add(bar);
        win.Content = root;
        box.Focus();
        box.SelectAll();
        win.ShowDialog();
        string r = box.Text.Trim();
        return okay && r.Length > 0 ? r : null;
    }

    private void PersistAndInstall(bool job)
    {
        if (job)
        {
            if (_currentJob == null) { TxtStatus.Text = "Open a job to save a per-job override."; return; }
            SettingsPresetStore.SaveJobOverride(_currentJob, _ftConfig);
            TxtStatus.Text = "Saved as this job's template (overrides global).";
        }
        else
        {
            SettingsPresetStore.SaveGlobal(_ftConfig);
            TxtStatus.Text = "Saved as global default template.";
        }
        SettingsPresetStore.InstallProviders(_currentJob);
    }

    private void PersistFolderTemplateEditorChange(string status)
    {
        SettingsPresetStore.SaveGlobal(_ftConfig);
        if (_currentJob != null && SettingsPresetStore.LoadJobOverride(_currentJob) != null)
            SettingsPresetStore.SaveJobOverride(_currentJob, _ftConfig);
        InstallWorkingProviders();
        TxtStatus.Text = status;
    }

    private void InstallWorkingProviders()
    {
        var c = _ftConfig;
        PlanSwiftFolderTemplateService.PageFoldersOverride = m => c.PageFoldersFor(m);
        PlanSwiftFolderTemplateService.TakeoffTreeOverride = m => c.TreeFor(m);
    }

    private static TreeViewItem BuildTreeItem(FolderPlanNode node)
    {
        var item = new TreeViewItem { Header = node.Name, Tag = node, IsExpanded = true };
        foreach (FolderPlanNode c in node.Children)
            item.Items.Add(BuildTreeItem(c));
        return item;
    }

    private static int CountNodes(IReadOnlyList<FolderPlanNode> nodes) =>
        nodes.Count + nodes.Sum(n => CountNodes(n.Children));

    private static bool RemoveNode(List<FolderPlanNode> nodes, FolderPlanNode target)
    {
        if (nodes.Remove(target)) return true;
        foreach (FolderPlanNode n in nodes)
            if (RemoveNode(n.Children, target)) return true;
        return false;
    }

    // ── Page Folders (Pages tree) ────────────────────────────────────────
    private FrameworkElement BuildPageFoldersPanel()
    {
        var root = new DockPanel();
        var top = HBar();
        top.Children.Add(new TextBlock { Text = "Mode:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _pfMode = ModeCombo((_, _) => BindPageFolders());
        top.Children.Add(_pfMode);
        top.Children.Add(MgrButton("Add", (_, _) => { var n = PromptText("Add page folder", "New Folder"); if (n != null) { _pfList.Add(new FolderPlanNode { Name = n }); SyncPageFolders(); PersistFolderTemplateEditorChange($"Page Folders {ModeOf(_pfMode)} saved as global default."); } }));
        top.Children.Add(MgrButton("Rename", (_, _) => { if (_pfListBox?.SelectedItem is FolderPlanNode s) { var n = PromptText("Rename", s.Name); if (n != null) { s.Name = n; SyncPageFolders(); _pfListBox.Items.Refresh(); PersistFolderTemplateEditorChange($"Page Folders {ModeOf(_pfMode)} saved as global default."); } } }));
        top.Children.Add(MgrButton("Remove", (_, _) => { if (_pfListBox?.SelectedItem is FolderPlanNode s) { _pfList.Remove(s); SyncPageFolders(); PersistFolderTemplateEditorChange($"Page Folders {ModeOf(_pfMode)} saved as global default."); } }));
        top.Children.Add(MgrButton("↑", (_, _) => MovePf(-1)));
        top.Children.Add(MgrButton("↓", (_, _) => MovePf(1)));
        top.Children.Add(MgrButton("Reset to default", (_, _) => { _ftConfig.PageFolders[ModeOf(_pfMode)] = PlanSwiftFolderTemplateService.DefaultPageFolders(ModeOf(_pfMode)).ToList(); BindPageFolders(); PersistFolderTemplateEditorChange($"Page Folders {ModeOf(_pfMode)} reset and saved as global default."); }));
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var act = HBar();
        act.Children.Add(MgrButton("Apply — create page folders", (_, _) => ApplyPageFolders(), primary: true));
        act.Children.Add(MgrButton("Save global default", (_, _) => { SyncPageFolders(); PersistAndInstall(false); }));
        act.Children.Add(MgrButton("Save as this job", (_, _) => { SyncPageFolders(); PersistAndInstall(true); }));
        DockPanel.SetDock(act, Dock.Top);
        root.Children.Add(act);

        _pfStatus = StatusLine();
        DockPanel.SetDock(_pfStatus, Dock.Top);
        root.Children.Add(_pfStatus);

        _pfListBox = new ListBox { ItemsSource = _pfList, DisplayMemberPath = nameof(FolderPlanNode.Name) };
        root.Children.Add(_pfListBox);
        return root;
    }

    private void MovePf(int delta)
    {
        if (_pfListBox?.SelectedItem is not FolderPlanNode s) return;
        int i = _pfList.IndexOf(s), j = i + delta;
        if (j < 0 || j >= _pfList.Count) return;
        _pfList.Move(i, j);
        SyncPageFolders();
        PersistFolderTemplateEditorChange($"Page Folders {ModeOf(_pfMode)} order saved as global default.");
        _pfListBox.SelectedIndex = j;
    }

    private void BindPageFolders()
    {
        _pfList.Clear();
        foreach (string n in _ftConfig.PageFoldersFor(ModeOf(_pfMode)))
            _pfList.Add(new FolderPlanNode { Name = n });
        if (_pfStatus != null)
            _pfStatus.Text = $"{_pfList.Count} page folder(s) for mode {ModeOf(_pfMode)}. Apply creates them in the Pages tree.";
    }

    private void SyncPageFolders() =>
        _ftConfig.PageFolders[ModeOf(_pfMode)] = _pfList.Select(n => n.Name).Where(s => s.Length > 0).ToList();

    private void ApplyPageFolders()
    {
        if (_currentJob == null) { MessageBox.Show("Open a job first.", "Page Folders"); return; }
        SyncPageFolders();
        InstallWorkingProviders();
        AutoCreatePageFolders(_currentJob.PagesRoot);
    }

    // ── Auto Tree (Takeoffs tree) ────────────────────────────────────────
    private FrameworkElement BuildAutoTreePanel()
    {
        var root = new DockPanel();
        var top = HBar();
        top.Children.Add(new TextBlock { Text = "Mode:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _atMode = ModeCombo((_, _) => BindAutoTree());
        top.Children.Add(_atMode);
        top.Children.Add(MgrButton("Add root", (_, _) => AddTreeNode(true)));
        top.Children.Add(MgrButton("Add child", (_, _) => AddTreeNode(false)));
        top.Children.Add(MgrButton("Rename", (_, _) => RenameTreeNode()));
        top.Children.Add(MgrButton("Remove", (_, _) => RemoveTreeNode()));
        top.Children.Add(MgrButton("Reset to default", (_, _) => { _ftConfig.TakeoffTree[ModeOf(_atMode)] = PlanSwiftFolderTemplateService.HardcodedSubTree(ModeOf(_atMode)); BindAutoTree(); PersistFolderTemplateEditorChange($"Auto Tree {ModeOf(_atMode)} reset and saved as global default."); }));
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var act = HBar();
        act.Children.Add(MgrButton("Apply — create takeoff tree", (_, _) => ApplyAutoTree(), primary: true));
        act.Children.Add(MgrButton("Save global default", (_, _) => PersistAndInstall(false)));
        act.Children.Add(MgrButton("Save as this job", (_, _) => PersistAndInstall(true)));
        DockPanel.SetDock(act, Dock.Top);
        root.Children.Add(act);

        _atStatus = StatusLine();
        DockPanel.SetDock(_atStatus, Dock.Top);
        root.Children.Add(_atStatus);

        _atTree = new TreeView { BorderThickness = new Thickness(1), BorderBrush = TryFindResource("ControlBorderBrush") as Brush };
        root.Children.Add(_atTree);
        return root;
    }

    private List<FolderPlanNode> AtNodes() =>
        _ftConfig.TakeoffTree.TryGetValue(ModeOf(_atMode), out var v)
            ? v
            : _ftConfig.TakeoffTree[ModeOf(_atMode)] = _ftConfig.TreeFor(ModeOf(_atMode));

    private void BindAutoTree()
    {
        if (_atTree == null) return;
        _atTree.Items.Clear();
        foreach (FolderPlanNode n in AtNodes())
            _atTree.Items.Add(BuildTreeItem(n));
        if (_atStatus != null)
            _atStatus.Text = $"{CountNodes(AtNodes())} folder(s) for mode {ModeOf(_atMode)}. Used by Auto Takeoff Tree and as the From Pages sub-tree.";
    }

    private FolderPlanNode? SelAt() => (_atTree?.SelectedItem as TreeViewItem)?.Tag as FolderPlanNode;

    private void AddTreeNode(bool atRoot)
    {
        string? n = PromptText(atRoot ? "Add root folder" : "Add child folder", "New Folder");
        if (n == null) return;
        var node = new FolderPlanNode { Name = n };
        if (atRoot || SelAt() is not { } p)
            AtNodes().Add(node);
        else
            p.Children.Add(node);
        BindAutoTree();
        PersistFolderTemplateEditorChange($"Auto Tree {ModeOf(_atMode)} saved as global default.");
    }

    private void RenameTreeNode()
    {
        if (SelAt() is not { } s) return;
        string? n = PromptText("Rename folder", s.Name);
        if (n == null) return;
        s.Name = n;
        BindAutoTree();
        PersistFolderTemplateEditorChange($"Auto Tree {ModeOf(_atMode)} saved as global default.");
    }

    private void RemoveTreeNode()
    {
        if (SelAt() is not { } s) return;
        RemoveNode(AtNodes(), s);
        BindAutoTree();
        PersistFolderTemplateEditorChange($"Auto Tree {ModeOf(_atMode)} saved as global default.");
    }

    private void ApplyAutoTree()
    {
        if (_currentJob == null) { MessageBox.Show("Open a job first.", "Auto Tree"); return; }
        InstallWorkingProviders();
        AutoCreateTakeoffTree(_currentJob.TakeoffsRoot);
    }

    // ── From Pages ───────────────────────────────────────────────────────
    private FrameworkElement BuildFromPagesPanel()
    {
        var root = new DockPanel();
        var top = HBar();
        top.Children.Add(new TextBlock { Text = "Mode:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _fpMode = ModeCombo((_, _) => BindFromPages());
        top.Children.Add(_fpMode);
        top.Children.Add(MgrButton("Add", (_, _) => { var n = PromptText("Add top folder", "New Folder"); if (n != null) { _fpTop.Add(new FolderPlanNode { Name = n }); } }));
        top.Children.Add(MgrButton("Rename", (_, _) => { if (_fpTopBox?.SelectedItem is FolderPlanNode s) { var n = PromptText("Rename", s.Name); if (n != null) { s.Name = n; _fpTopBox.Items.Refresh(); } } }));
        top.Children.Add(MgrButton("Remove", (_, _) => { if (_fpTopBox?.SelectedItem is FolderPlanNode s) _fpTop.Remove(s); }));
        top.Children.Add(MgrButton("Reload from Pages", (_, _) => BindFromPages()));
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var act = HBar();
        act.Children.Add(MgrButton("Apply — create from pages", (_, _) => ApplyFromPages(), primary: true));
        DockPanel.SetDock(act, Dock.Top);
        root.Children.Add(act);

        _fpStatus = StatusLine();
        DockPanel.SetDock(_fpStatus, Dock.Top);
        root.Children.Add(_fpStatus);

        _fpTopBox = new ListBox { ItemsSource = _fpTop, DisplayMemberPath = nameof(FolderPlanNode.Name) };
        root.Children.Add(_fpTopBox);
        return root;
    }

    private void BindFromPages()
    {
        _fpTop.Clear();
        if (_currentJob != null)
            foreach (string n in PlanSwiftFolderTemplateService.CollectCapsGroupNames(_currentJob))
                _fpTop.Add(new FolderPlanNode { Name = n });
        if (_fpStatus != null)
            _fpStatus.Text =
                $"{_fpTop.Count} top folder(s) from Pages CAPS names. Each gets the Auto Tree ({ModeOf(_fpMode)}: " +
                $"{CountNodes(_ftConfig.TreeFor(ModeOf(_fpMode)))} folders). Edit the tree in the Auto Tree section.";
    }

    private void ApplyFromPages()
    {
        if (_currentJob == null) { MessageBox.Show("Open a job first.", "From Pages"); return; }
        var top = _fpTop.Select(n => n.Name).Where(s => s.Length > 0).ToList();
        if (top.Count == 0) { MessageBox.Show("No top folders. Reload from Pages or add some.", "From Pages"); return; }

        var confirm = MessageBox.Show(
            $"Create {top.Count} top folder(s), each with the Auto Tree ({ModeOf(_fpMode)}, {CountNodes(_ftConfig.TreeFor(ModeOf(_fpMode)))} folders), under Takeoffs?\nExisting folders are skipped.",
            "From Pages", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            FlushTakeoffAutosaves();
            var plan = new FolderPlan
            {
                Mode = ModeOf(_fpMode),
                TopFolders = top,
                SubTree = _ftConfig.TreeFor(ModeOf(_fpMode)).Select(n => n.Clone()).ToList(),
            };
            CapsTakeoffFolderResult r = PlanSwiftFolderTemplateService.CreateFoldersFromPlan(_currentJob.TakeoffsRoot, plan);
            LoadTakeoffsForJob();
            TxtStatus.Text = $"From Pages: top created {r.TopCreated}, skipped {r.TopSkipped}, sub created {r.SubCreated}, skipped {r.SubSkipped}, errors {r.Errors}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("From Pages", ex);
        }
    }

    private TextBlock StatusLine()
    {
        var tb = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
            FontSize = 12,
        };
        return tb;
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
        bool job = _rulesScope?.SelectedIndex == 1;
        SmartLearnedRuleSet set = job && _currentJob != null
            ? SmartLearningStore.LoadProjectLearnedRules(_currentJob)
            : SmartLearningStore.LoadGlobalLearnedRules();
        foreach (var r in set.Rules.OrderByDescending(r => r.Enabled).ThenByDescending(r => r.Support).ThenBy(r => r.TitleToken))
            _ruleRows.Add(LearnedRuleRow.FromRule(r));
        _rulesGrid?.Items.Refresh();
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

        root.Children.Add(Header("Viewport interaction"));
        root.Children.Add(new TextBlock
        {
            Text = "Mouse-wheel zoom step — how much one wheel notch zooms in/out "
                 + $"({AppSettingsStore.ViewportZoomWheelFactorMin:0.##} - {AppSettingsStore.ViewportZoomWheelFactorMax:0.##}x). "
                 + "Higher = fewer notches to zoom, like PlanSwift.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 6),
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
        });

        double initial = NormalizeZoomWheelFactor(_settings.ViewportZoomWheelFactor);
        var zoomRow = HBar();
        zoomRow.Children.Add(new TextBlock
        {
            Text = "Zoom step (× / notch)",
            VerticalAlignment = VerticalAlignment.Center, Width = 150, FontSize = 12,
        });
        _defaultsZoomSlider = new Slider
        {
            Minimum = AppSettingsStore.ViewportZoomWheelFactorMin,
            Maximum = AppSettingsStore.ViewportZoomWheelFactorMax,
            SmallChange = 0.05, LargeChange = 0.25,
            Width = 220, VerticalAlignment = VerticalAlignment.Center,
            Value = initial,
        };
        _defaultsZoomSlider.ValueChanged += DefaultsZoomSlider_ValueChanged;
        _defaultsZoomSlider.PreviewMouseUp += Slider_CommitSave;
        _defaultsZoomSlider.KeyUp += Slider_CommitSave;
        zoomRow.Children.Add(_defaultsZoomSlider);
        _defaultsZoomValue = new TextBlock
        {
            Text = initial.ToString("0.##", CultureInfo.InvariantCulture) + "x",
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
            MinWidth = 44, FontSize = 12,
        };
        zoomRow.Children.Add(_defaultsZoomValue);
        root.Children.Add(zoomRow);

        var zoomActions = HBar();
        zoomActions.Children.Add(MgrButton(
            $"Reset to default ({AppSettingsStore.ViewportZoomWheelFactorDefault:0.##}x)",
            (_, _) => { SetZoomWheelFactor(AppSettingsStore.ViewportZoomWheelFactorDefault); SyncDefaultsZoomControl(); }));
        root.Children.Add(zoomActions);

        root.Children.Add(Header("Background performance"));
        root.Children.Add(new TextBlock
        {
            Text = "Warm every page of the job in the background after opening it. "
                 + "Pages open instantly once warmed, but on big jobs the sweep keeps the CPU and disk "
                 + "busy for a while and the open sheet can feel blurry longer while you zoom and pan.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 6),
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
        });
        var warmupBox = new CheckBox
        {
            Content = "Warm all job pages in background",
            IsChecked = _settings.BackgroundJobWarmupEnabled,
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 12,
        };
        warmupBox.Checked += (_, _) => { _settings.BackgroundJobWarmupEnabled = true; SaveAppSettings(); };
        warmupBox.Unchecked += (_, _) => { _settings.BackgroundJobWarmupEnabled = false; SaveAppSettings(); };
        root.Children.Add(warmupBox);

        root.Children.Add(Header("Tree display"));
        root.Children.Add(new TextBlock
        {
            Text = "Keep the Takeoffs tree compact by showing only takeoff items. "
                 + "Turn this on when you need the old nested section/count rows for per-segment actions.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 6),
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
        });
        _defaultsTakeoffSectionsBox = new CheckBox
        {
            Content = "Show section/count rows under takeoffs",
            IsChecked = _settings.ShowTakeoffSectionsInTree,
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 12,
        };
        _defaultsTakeoffSectionsBox.Checked += (_, _) => SetTakeoffSectionRowsVisible(true);
        _defaultsTakeoffSectionsBox.Unchecked += (_, _) => SetTakeoffSectionRowsVisible(false);
        root.Children.Add(_defaultsTakeoffSectionsBox);

        root.Children.Add(Header("Display & export defaults"));
        root.Children.Add(new TextBlock
        {
            Text = "More viewport and PDF export appearance defaults live on the top ribbon tabs.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 8),
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
        });
        var row = HBar();
        row.Children.Add(MgrButton("Open Viewport defaults", (_, _) => SelectTopTab("Viewport")));
        row.Children.Add(MgrButton("Open Display defaults", (_, _) => SelectTopTab("Display")));
        row.Children.Add(MgrButton("Open PDF Output defaults", (_, _) => SelectTopTab("PDF Output")));
        root.Children.Add(row);
        return root;
    }

    private void DefaultsZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings || _defaultsZoomSyncing)
            return;

        _settings.ViewportZoomWheelFactor = NormalizeZoomWheelFactor(e.NewValue);
        _viewport.ZoomWheelFactor = _settings.ViewportZoomWheelFactor;
        if (_defaultsZoomValue != null)
            _defaultsZoomValue.Text = _settings.ViewportZoomWheelFactor.ToString("0.##", CultureInfo.InvariantCulture) + "x";
        _viewportScaleDirty = true;
        TxtStatus.Text = $"Mouse-wheel zoom step: {_settings.ViewportZoomWheelFactor:0.##}x per notch.";
    }

    private void SyncDefaultsZoomControl()
    {
        if (_defaultsZoomSlider == null)
            return;

        _defaultsZoomSyncing = true;
        try
        {
            double value = NormalizeZoomWheelFactor(_settings.ViewportZoomWheelFactor);
            _defaultsZoomSlider.Value = value;
            if (_defaultsZoomValue != null)
                _defaultsZoomValue.Text = value.ToString("0.##", CultureInfo.InvariantCulture) + "x";
        }
        finally
        {
            _defaultsZoomSyncing = false;
        }

        if (_defaultsTakeoffSectionsBox != null)
            _defaultsTakeoffSectionsBox.IsChecked = _settings.ShowTakeoffSectionsInTree;
    }

    private void SetTakeoffSectionRowsVisible(bool visible)
    {
        if (_settings.ShowTakeoffSectionsInTree == visible)
            return;

        _settings.ShowTakeoffSectionsInTree = visible;
        SaveAppSettings();
        RefreshTakeoffSectionTreeVisibility();
        TxtStatus.Text = visible
            ? "Takeoffs tree section/count rows shown."
            : "Takeoffs tree section/count rows hidden.";
    }

    private void RefreshTakeoffSectionTreeVisibility()
    {
        var itemNodes = EnumerateTakeoffTreeItems(TakeoffsTree)
            .Where(item => item.Tag is TakeoffItem)
            .ToList();
        foreach (TreeViewItem itemNode in itemNodes)
            if (itemNode.Tag is TakeoffItem item)
                RefreshTakeoffSectionNodes(itemNode, item);

        PruneTakeoffSectionMultiSelection();
        InvalidateTakeoffTreeItemIndex();
        ApplyTakeoffPageHighlights();
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
