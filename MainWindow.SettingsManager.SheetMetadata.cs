using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private ComboBox? _sheetDetectorModeBox;
    private ComboBox? _sheetImportPolicyBox;
    private ComboBox? _sheetRenameConfidenceBox;
    private ComboBox? _sheetSuffixConfidenceBox;
    private ComboBox? _sheetScaleConfidenceBox;
    private CheckBox? _sheetPreserveNameBox;
    private CheckBox? _sheetPreserveSuffixBox;
    private CheckBox? _sheetPreserveScaleBox;
    private CheckBox? _sheetPreserveMultiSuffixBox;
    private CheckBox? _sheetIndexEvidenceBox;
    private CheckBox? _sheetTitleBlockLabelEvidenceBox;
    private CheckBox? _sheetTitleEvidenceBox;
    private CheckBox? _sheetTitleBlockScaleEvidenceBox;
    private CheckBox? _sheetBodyEvidenceBox;
    private CheckBox? _sheetScaleInferenceBox;
    private TextBox? _sheetScaleSuffixesBox;
    private TextBox? _sheetNoScaleSuffixesBox;
    private TextBox? _sheetNoScaleTerminalTokensBox;
    private TextBox? _sheetCompoundSuffixesBox;
    private TextBlock? _sheetMetadataStatus;
    private TextBlock? _sheetDetectorModeNote;
    private FrameworkElement? _sheetSuffixRuleEditor;
    private FrameworkElement? _sheetLabelOverrideEditor;

    private readonly ObservableCollection<SheetSuffixRule> _sheetSuffixRules = [];
    private readonly ObservableCollection<SheetMetadataLabelOverride> _sheetLabelOverrides = [];
    private DataGrid? _sheetSuffixRulesGrid;
    private DataGrid? _sheetLabelOverridesGrid;

    private ComboBox? _rulesScope;
    private readonly ObservableCollection<LearnedRuleRow> _ruleRows = [];
    private DataGrid? _rulesGrid;

    private static readonly SheetMetadataEvidenceField[] SheetEvidenceFields =
        Enum.GetValues<SheetMetadataEvidenceField>();
    private static readonly SheetMetadataEvidenceField?[] SheetExclusionEvidenceFields =
        [null, .. Enum.GetValues<SheetMetadataEvidenceField>().Select(value => (SheetMetadataEvidenceField?)value)];
    private static readonly SheetMetadataMatchKind[] SheetMatchKinds =
        Enum.GetValues<SheetMetadataMatchKind>();
    private static readonly SheetMetadataOverrideAction[] SheetOverrideActions =
        Enum.GetValues<SheetMetadataOverrideAction>();
    private static readonly SheetMetadataConfidence[] SheetConfidenceValues =
        Enum.GetValues<SheetMetadataConfidence>();

    private FrameworkElement BuildRulesPanel()
    {
        var content = new StackPanel { Margin = new Thickness(2) };
        content.Children.Add(BuildSheetMetadataActions());
        content.Children.Add(BuildSheetMetadataPolicyEditor());
        _sheetSuffixRuleEditor = BuildSuffixRuleEditor();
        content.Children.Add(_sheetSuffixRuleEditor);
        _sheetLabelOverrideEditor = BuildLabelOverrideEditor();
        content.Children.Add(_sheetLabelOverrideEditor);
        content.Children.Add(BuildLearnedRulesEditor());
        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private FrameworkElement BuildSheetMetadataActions()
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(MgrButton("Reset", (_, _) => LoadSheetMetadataPreset(SheetMetadataConfig.BuildDefault(), "Reset to built-in legacy defaults.")));
        panel.Children.Add(MgrButton("Legacy", (_, _) => LoadSheetMetadataPreset(SheetMetadataConfig.BuildLegacy(), "Loaded Legacy preset.")));
        panel.Children.Add(MgrButton("Precise v2", (_, _) => LoadSheetMetadataPreset(SheetMetadataConfig.BuildPreciseV2(), "Loaded recommended Precise v2 preset."), primary: true));
        panel.Children.Add(MgrButton("Save Global", (_, _) => SaveSheetMetadataConfig(saveForJob: false)));
        panel.Children.Add(MgrButton("Save This Job", (_, _) => SaveSheetMetadataConfig(saveForJob: true)));
        panel.Children.Add(MgrButton("Clear Job Override", (_, _) => ClearSheetMetadataJobOverride()));
        panel.Children.Add(MgrButton("Apply selected scope...", ApplySheetMetadataSettingsToSelection, primary: true));

        var root = new StackPanel();
        root.Children.Add(Header("Deterministic Auto Name / Scale policy"));
        root.Children.Add(new TextBlock
        {
            Text = "Legacy reproduces current behavior. Precise v2 preserves reviewed values, uses stronger evidence, and previews import results. Save globally or override only this job.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
            Margin = new Thickness(0, 0, 0, 6),
        });
        root.Children.Add(panel);
        _sheetMetadataStatus = StatusLine();
        root.Children.Add(_sheetMetadataStatus);
        return root;
    }

    private FrameworkElement BuildSheetMetadataPolicyEditor()
    {
        var root = new StackPanel { Margin = new Thickness(0, 2, 0, 10) };
        root.Children.Add(Header("Evidence, confidence, and preservation"));
        _sheetDetectorModeBox = AddEnumSetting(root, "Detector engine", Enum.GetValues<SheetMetadataDetectorMode>());
        _sheetDetectorModeBox.SelectionChanged += (_, _) => UpdateSheetMetadataDetectorUiState();
        _sheetDetectorModeNote = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
            Margin = new Thickness(0, 0, 0, 5),
        };
        root.Children.Add(_sheetDetectorModeNote);
        _sheetImportPolicyBox = AddEnumSetting(root, "After PDF import", Enum.GetValues<SheetMetadataImportPolicy>());
        _sheetRenameConfidenceBox = AddEnumSetting(root, "Minimum Name confidence", SheetConfidenceValues);
        _sheetSuffixConfidenceBox = AddEnumSetting(root, "Minimum Suffix confidence", SheetConfidenceValues);
        _sheetScaleConfidenceBox = AddEnumSetting(root, "Minimum Scale confidence", SheetConfidenceValues);

        _sheetIndexEvidenceBox = AddPolicyCheck(root, "Use drawing-list / sheet-index evidence");
        _sheetTitleBlockLabelEvidenceBox = AddPolicyCheck(root, "Use title-block sheet-label evidence");
        _sheetTitleEvidenceBox = AddPolicyCheck(root, "Use title-block page-title evidence");
        _sheetTitleBlockScaleEvidenceBox = AddPolicyCheck(root, "Use title-block scale evidence");
        _sheetBodyEvidenceBox = AddPolicyCheck(root, "Use PDF body text as weaker evidence");
        _sheetScaleInferenceBox = AddPolicyCheck(root, "Allow scale inference when no explicit scale exists");
        _sheetPreserveNameBox = AddPolicyCheck(root, "Preserve existing manually reviewed Name");
        _sheetPreserveSuffixBox = AddPolicyCheck(root, "Preserve existing manually reviewed Suffix");
        _sheetPreserveScaleBox = AddPolicyCheck(root, "Preserve existing manually reviewed Scale");
        _sheetPreserveMultiSuffixBox = AddPolicyCheck(root, "Preserve any existing multi-token suffix (not only catalog entries)");

        _sheetScaleSuffixesBox = AddListSetting(root, "Scale-capable suffixes");
        _sheetNoScaleSuffixesBox = AddListSetting(root, "Never auto-scale suffixes");
        _sheetNoScaleTerminalTokensBox = AddListSetting(root, "Never auto-scale terminal tokens (editable fallback)");
        _sheetCompoundSuffixesBox = AddListSetting(root, "Known compound suffixes");
        return root;
    }

    private ComboBox AddEnumSetting<T>(Panel root, string label, T[] values) where T : struct, Enum
    {
        var row = HBar();
        row.Children.Add(new TextBlock { Text = label, Width = 205, VerticalAlignment = VerticalAlignment.Center });
        var box = new ComboBox { Width = 230, ItemsSource = values, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(box);
        root.Children.Add(row);
        return box;
    }

    private static CheckBox AddPolicyCheck(Panel root, string text)
    {
        var box = new CheckBox { Content = text, Margin = new Thickness(0, 0, 0, 4) };
        root.Children.Add(box);
        return box;
    }

    private static TextBox AddListSetting(Panel root, string label)
    {
        root.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 2) });
        var box = new TextBox
        {
            AcceptsReturn = true,
            Height = 66,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        root.Children.Add(box);
        return box;
    }

    private FrameworkElement BuildSuffixRuleEditor()
    {
        var root = new StackPanel { Margin = new Thickness(0, 2, 0, 12) };
        root.Children.Add(Header("Suffix generation rules (first enabled match wins)"));
        var actions = HBar();
        actions.Children.Add(MgrButton("Add", (_, _) => AddSuffixRule()));
        actions.Children.Add(MgrButton("Delete", (_, _) => DeleteSuffixRules()));
        actions.Children.Add(MgrButton("↑", (_, _) => MoveSuffixRule(-1)));
        actions.Children.Add(MgrButton("↓", (_, _) => MoveSuffixRule(1)));
        actions.Children.Add(MgrButton("Reset Legacy rules", (_, _) => SetSuffixRules(SheetMetadataSuffixCatalog.BuildLegacy())));
        root.Children.Add(actions);

        _sheetSuffixRulesGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            ItemsSource = _sheetSuffixRules,
            Height = 280,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        AddSuffixRuleColumns(_sheetSuffixRulesGrid);
        root.Children.Add(_sheetSuffixRulesGrid);
        return root;
    }

    private static void AddSuffixRuleColumns(DataGrid grid)
    {
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "On", Binding = EditBinding(nameof(SheetSuffixRule.Enabled)), Width = 42 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Rule", Binding = new Binding(nameof(SheetSuffixRule.Id)), IsReadOnly = true, Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Priority", Binding = EditBinding(nameof(SheetSuffixRule.Priority)), Width = 62 });
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "Evidence", ItemsSource = SheetEvidenceFields, SelectedItemBinding = EditBinding(nameof(SheetSuffixRule.EvidenceField)), Width = 105 });
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "Match", ItemsSource = SheetMatchKinds, SelectedItemBinding = EditBinding(nameof(SheetSuffixRule.MatchKind)), Width = 100 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Sheet prefix", Binding = EditBinding(nameof(SheetSuffixRule.SheetPrefix)), Width = 82 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Pattern", Binding = EditBinding(nameof(SheetSuffixRule.Pattern)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Keywords (;)", Binding = EditBinding(nameof(SheetSuffixRule.KeywordsText)), Width = 220 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Exclude (;)", Binding = EditBinding(nameof(SheetSuffixRule.ExcludedKeywordsText)), Width = 150 });
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "Exclude from", ItemsSource = SheetExclusionEvidenceFields, SelectedItemBinding = EditBinding(nameof(SheetSuffixRule.ExclusionEvidenceField)), Width = 105 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Require flags (;)", Binding = EditBinding(nameof(SheetSuffixRule.RequiredFlagsText)), Width = 125 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Min #", Binding = EditBinding(nameof(SheetSuffixRule.MinimumSheetNumber)), Width = 58 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Max #", Binding = EditBinding(nameof(SheetSuffixRule.MaximumSheetNumber)), Width = 58 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Output suffix", Binding = EditBinding(nameof(SheetSuffixRule.OutputSuffix)), Width = 92 });
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "Confidence", ItemsSource = SheetConfidenceValues, SelectedItemBinding = EditBinding(nameof(SheetSuffixRule.Confidence)), Width = 92 });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Skip scale", Binding = EditBinding(nameof(SheetSuffixRule.SkipScale)), Width = 72 });
    }

    private FrameworkElement BuildLabelOverrideEditor()
    {
        var root = new StackPanel { Margin = new Thickness(0, 2, 0, 12) };
        root.Children.Add(Header(
            "Exact sheet-label overrides (specific PDF pattern wins; ties use first row). " +
            "Full page name is final; leave it blank when using Suffix Set/Clear."));
        var actions = HBar();
        actions.Children.Add(MgrButton("Add override", (_, _) => AddLabelOverride()));
        actions.Children.Add(MgrButton("Delete selected", (_, _) => DeleteLabelOverrides()));
        root.Children.Add(actions);

        _sheetLabelOverridesGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            ItemsSource = _sheetLabelOverrides,
            Height = 160,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
        };
        AddLabelOverrideColumns(_sheetLabelOverridesGrid);
        root.Children.Add(_sheetLabelOverridesGrid);
        return root;
    }

    private static void AddLabelOverrideColumns(DataGrid grid)
    {
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "On", Binding = EditBinding(nameof(SheetMetadataLabelOverride.Enabled)), Width = 42 });
        grid.Columns.Add(new DataGridTextColumn { Header = "PDF filename pattern", Binding = EditBinding(nameof(SheetMetadataLabelOverride.SourcePdfPattern)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Sheet label", Binding = EditBinding(nameof(SheetMetadataLabelOverride.SheetLabel)), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Full page name", Binding = EditBinding(nameof(SheetMetadataLabelOverride.OutputPageName)), Width = 155 });
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "Suffix action", ItemsSource = SheetOverrideActions, SelectedItemBinding = EditBinding(nameof(SheetMetadataLabelOverride.SuffixAction)), Width = 95 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Suffix", Binding = EditBinding(nameof(SheetMetadataLabelOverride.OutputSuffix)), Width = 80 });
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "Scale action", ItemsSource = SheetOverrideActions, SelectedItemBinding = EditBinding(nameof(SheetMetadataLabelOverride.ScaleAction)), Width = 95 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Scale", Binding = EditBinding(nameof(SheetMetadataLabelOverride.ScaleText)), Width = 120 });
    }

    private static Binding EditBinding(string property) => new(property)
    {
        Mode = BindingMode.TwoWay,
        UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
    };

    private FrameworkElement BuildLearnedRulesEditor()
    {
        var root = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        root.Children.Add(Header("Learned rules (lower-priority evidence)"));
        var top = HBar();
        _rulesScope = new ComboBox { Width = 130, VerticalAlignment = VerticalAlignment.Center };
        _rulesScope.Items.Add("Global");
        _rulesScope.Items.Add("This job");
        _rulesScope.SelectedIndex = 0;
        _rulesScope.SelectionChanged += (_, _) => LoadRuleRows();
        top.Children.Add(_rulesScope);
        top.Children.Add(MgrButton("Enable all", (_, _) => SetAllRules(true)));
        top.Children.Add(MgrButton("Disable selected", (_, _) => SetSelRules(false)));
        top.Children.Add(MgrButton("Enable selected", (_, _) => SetSelRules(true)));
        top.Children.Add(MgrButton("Delete selected", (_, _) => DeleteSelRules()));
        top.Children.Add(MgrButton("Save learned", (_, _) => SaveRuleRows()));
        root.Children.Add(top);

        _rulesGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            ItemsSource = _ruleRows,
            Height = 230,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
        };
        AddLearnedRuleColumns(_rulesGrid);
        root.Children.Add(_rulesGrid);
        return root;
    }

    private static void AddLearnedRuleColumns(DataGrid grid)
    {
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "On", Binding = EditBinding(nameof(LearnedRuleRow.Enabled)), Width = 42 });
        void Col(string header, string property, double width) => grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property), Width = width, IsReadOnly = true });
        Col("Token", nameof(LearnedRuleRow.TitleToken), 150);
        Col("Suffix", nameof(LearnedRuleRow.Suffix), 80);
        Col("Skip scale", nameof(LearnedRuleRow.SkipScale), 70);
        Col("Scale", nameof(LearnedRuleRow.ScaleText), 110);
        Col("Support", nameof(LearnedRuleRow.Support), 65);
        Col("Confidence", nameof(LearnedRuleRow.Confidence), 85);
        Col("Kind", nameof(LearnedRuleRow.Kind), 140);
    }

    private void BindSheetMetadataSettings()
    {
        SheetMetadataConfig config = SheetMetadataConfig.UpgradeForCurrentSchema(_sheetMetadataConfig);
        _sheetMetadataConfig = config.Clone();
        if (_sheetDetectorModeBox != null) _sheetDetectorModeBox.SelectedItem = config.DetectorMode;
        if (_sheetImportPolicyBox != null) _sheetImportPolicyBox.SelectedItem = config.ImportPolicy;
        if (_sheetRenameConfidenceBox != null) _sheetRenameConfidenceBox.SelectedItem = config.MinimumRenameConfidence;
        if (_sheetSuffixConfidenceBox != null) _sheetSuffixConfidenceBox.SelectedItem = config.MinimumSuffixConfidence;
        if (_sheetScaleConfidenceBox != null) _sheetScaleConfidenceBox.SelectedItem = config.MinimumScaleConfidence;
        if (_sheetPreserveNameBox != null) _sheetPreserveNameBox.IsChecked = config.PreserveExistingManualName;
        if (_sheetPreserveSuffixBox != null) _sheetPreserveSuffixBox.IsChecked = config.PreserveExistingManualSuffix;
        if (_sheetPreserveScaleBox != null) _sheetPreserveScaleBox.IsChecked = config.PreserveExistingManualScale;
        if (_sheetPreserveMultiSuffixBox != null) _sheetPreserveMultiSuffixBox.IsChecked = config.PreserveArbitraryExistingMultiTokenSuffix;
        if (_sheetIndexEvidenceBox != null) _sheetIndexEvidenceBox.IsChecked = config.EnableSheetIndexEvidence;
        if (_sheetTitleBlockLabelEvidenceBox != null) _sheetTitleBlockLabelEvidenceBox.IsChecked = config.EnableTitleBlockLabelEvidence;
        if (_sheetTitleEvidenceBox != null) _sheetTitleEvidenceBox.IsChecked = config.EnableTitleBlockEvidence;
        if (_sheetTitleBlockScaleEvidenceBox != null) _sheetTitleBlockScaleEvidenceBox.IsChecked = config.EnableTitleBlockScaleEvidence;
        if (_sheetBodyEvidenceBox != null) _sheetBodyEvidenceBox.IsChecked = config.EnableBodyEvidence;
        if (_sheetScaleInferenceBox != null) _sheetScaleInferenceBox.IsChecked = config.AllowScaleInference;
        SetListText(_sheetScaleSuffixesBox, config.ScaleCapableSuffixes);
        SetListText(_sheetNoScaleSuffixesBox, config.NoScaleSuffixes);
        SetListText(_sheetNoScaleTerminalTokensBox, config.NoScaleTerminalTokens);
        SetListText(_sheetCompoundSuffixesBox, config.CompoundSuffixes);
        SetSuffixRules(config.SuffixRules);
        SetLabelOverrides(config.SheetLabelOverrides);
        UpdateSheetMetadataDetectorUiState();
        LoadRuleRows();
        ShowSheetMetadataStatus($"Effective: {SheetMetadataEffectiveScope()}, engine {config.DetectorMode}, preset {config.PresetName}, schema {config.SchemaVersion}.");
    }

    private void SyncSheetMetadataFromUi()
    {
        CommitGrid(_sheetSuffixRulesGrid);
        CommitGrid(_sheetLabelOverridesGrid);
        if (_sheetDetectorModeBox?.SelectedItem is SheetMetadataDetectorMode detectorMode) _sheetMetadataConfig.DetectorMode = detectorMode;
        if (_sheetImportPolicyBox?.SelectedItem is SheetMetadataImportPolicy policy) _sheetMetadataConfig.ImportPolicy = policy;
        if (_sheetRenameConfidenceBox?.SelectedItem is SheetMetadataConfidence rename) _sheetMetadataConfig.MinimumRenameConfidence = rename;
        if (_sheetSuffixConfidenceBox?.SelectedItem is SheetMetadataConfidence suffix) _sheetMetadataConfig.MinimumSuffixConfidence = suffix;
        if (_sheetScaleConfidenceBox?.SelectedItem is SheetMetadataConfidence scale) _sheetMetadataConfig.MinimumScaleConfidence = scale;
        _sheetMetadataConfig.PreserveExistingManualName = _sheetPreserveNameBox?.IsChecked == true;
        _sheetMetadataConfig.PreserveExistingManualSuffix = _sheetPreserveSuffixBox?.IsChecked == true;
        _sheetMetadataConfig.PreserveExistingManualScale = _sheetPreserveScaleBox?.IsChecked == true;
        _sheetMetadataConfig.PreserveArbitraryExistingMultiTokenSuffix = _sheetPreserveMultiSuffixBox?.IsChecked == true;
        _sheetMetadataConfig.EnableSheetIndexEvidence = _sheetIndexEvidenceBox?.IsChecked == true;
        _sheetMetadataConfig.EnableTitleBlockLabelEvidence = _sheetTitleBlockLabelEvidenceBox?.IsChecked == true;
        _sheetMetadataConfig.EnableTitleBlockEvidence = _sheetTitleEvidenceBox?.IsChecked == true;
        _sheetMetadataConfig.EnableTitleBlockScaleEvidence = _sheetTitleBlockScaleEvidenceBox?.IsChecked == true;
        _sheetMetadataConfig.EnableBodyEvidence = _sheetBodyEvidenceBox?.IsChecked == true;
        _sheetMetadataConfig.AllowScaleInference = _sheetScaleInferenceBox?.IsChecked == true;
        _sheetMetadataConfig.ScaleCapableSuffixes = ParseList(_sheetScaleSuffixesBox?.Text);
        _sheetMetadataConfig.NoScaleSuffixes = ParseList(_sheetNoScaleSuffixesBox?.Text);
        _sheetMetadataConfig.NoScaleTerminalTokens = ParseList(_sheetNoScaleTerminalTokensBox?.Text);
        _sheetMetadataConfig.CompoundSuffixes = ParseList(_sheetCompoundSuffixesBox?.Text);
        _sheetMetadataConfig.SuffixRules = _sheetSuffixRules.OrderBy(rule => rule.Priority).Select(rule => rule.Clone()).ToList();
        _sheetMetadataConfig.SheetLabelOverrides = _sheetLabelOverrides.Select(item => item.Clone()).ToList();
        _sheetMetadataConfig = SheetMetadataConfig.UpgradeForCurrentSchema(_sheetMetadataConfig);
        if (string.Equals(_sheetMetadataConfig.PresetName, SheetMetadataConfig.LegacyPresetName, StringComparison.OrdinalIgnoreCase) &&
            !_sheetMetadataConfig.HasSameBehaviorAs(SheetMetadataConfig.BuildLegacy()))
        {
            _sheetMetadataConfig.PresetName = _sheetMetadataConfig.DetectorMode == SheetMetadataDetectorMode.Legacy
                ? "Custom Legacy"
                : "Custom Precise v2";
        }
        else if (string.Equals(_sheetMetadataConfig.PresetName, SheetMetadataConfig.PreciseV2PresetName, StringComparison.OrdinalIgnoreCase) &&
                 !_sheetMetadataConfig.HasSameBehaviorAs(SheetMetadataConfig.BuildPreciseV2()))
        {
            _sheetMetadataConfig.PresetName = _sheetMetadataConfig.DetectorMode == SheetMetadataDetectorMode.Legacy
                ? "Custom Legacy"
                : "Custom Precise v2";
        }
    }

    private void LoadSheetMetadataPreset(SheetMetadataConfig config, string status)
    {
        _sheetMetadataConfig = config.Clone();
        BindSheetMetadataSettings();
        ShowSheetMetadataStatus(status + " Save or Apply to activate it.");
    }

    private void SaveSheetMetadataConfig(bool saveForJob)
    {
        SyncSheetMetadataFromUi();
        if (!TryValidateSheetMetadataConfig(out string validationError))
        {
            ShowSheetMetadataStatus(validationError);
            return;
        }
        if (saveForJob)
        {
            if (_currentJob == null) { ShowSheetMetadataStatus("Open a job before saving a job override."); return; }
            SettingsPresetStore.SaveJobSheetMetadataOverride(_currentJob, _sheetMetadataConfig);
        }
        else
        {
            SettingsPresetStore.SaveGlobalSheetMetadata(_sheetMetadataConfig);
        }
        _sheetMetadataConfig = SettingsPresetStore.ResolveSheetMetadata(_currentJob).Clone();
        SettingsPresetStore.InstallSheetMetadataProvider(_currentJob);
        BindSheetMetadataSettings();
        if (!saveForJob && _currentJob != null && SettingsPresetStore.LoadJobSheetMetadataOverride(_currentJob) != null)
            ShowSheetMetadataStatus("Saved global policy. This job's override remains active and is shown above.");
        else
            ShowSheetMetadataStatus(saveForJob ? "Saved this-job sheet metadata override." : "Saved and activated global sheet metadata policy.");
    }

    private void ClearSheetMetadataJobOverride()
    {
        if (_currentJob == null) { ShowSheetMetadataStatus("Open a job before clearing its override."); return; }
        if (!SettingsPresetStore.ClearJobSheetMetadataOverride(_currentJob))
        {
            ShowSheetMetadataStatus("Could not clear this-job override; it remains active.");
            return;
        }
        _sheetMetadataConfig = SettingsPresetStore.ResolveSheetMetadata(_currentJob).Clone();
        SettingsPresetStore.InstallSheetMetadataProvider(_currentJob);
        BindSheetMetadataSettings();
        ShowSheetMetadataStatus("Cleared this-job override; global/default policy is active.");
    }

    private async void ApplySheetMetadataSettingsToSelection(object sender, RoutedEventArgs e)
    {
        SyncSheetMetadataFromUi();
        if (!TryValidateSheetMetadataConfig(out string validationError))
        {
            ShowSheetMetadataStatus(validationError);
            return;
        }
        SheetMetadataConfig previous = SheetMetadataRulesService.Active.Clone();
        SheetMetadataRulesService.Install(_sheetMetadataConfig);
        await RunAsyncUiHandler(
            async () =>
            {
                try
                {
                    if (GetSelectedPdfAutomationTarget("Auto Name + Scale") is { } item)
                        await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: true);
                }
                finally
                {
                    SheetMetadataRulesService.Install(previous);
                }
            },
            "Auto Name + Scale failed.",
            "Auto Name + Scale");
    }

    private void AddSuffixRule()
    {
        int priority = _sheetSuffixRules.Count == 0 ? 10 : _sheetSuffixRules.Max(rule => rule.Priority) + 10;
        _sheetSuffixRules.Add(new SheetSuffixRule { Priority = priority, EvidenceField = SheetMetadataEvidenceField.SheetTitle, MatchKind = SheetMetadataMatchKind.ContainsAny });
        _sheetSuffixRulesGrid?.Items.Refresh();
    }

    private void DeleteSuffixRules()
    {
        foreach (SheetSuffixRule rule in _sheetSuffixRulesGrid?.SelectedItems.OfType<SheetSuffixRule>().ToList() ?? [])
            _sheetSuffixRules.Remove(rule);
    }

    private void MoveSuffixRule(int delta)
    {
        if (_sheetSuffixRulesGrid?.SelectedItem is not SheetSuffixRule rule) return;
        int index = _sheetSuffixRules.IndexOf(rule);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= _sheetSuffixRules.Count) return;
        _sheetSuffixRules.Move(index, target);
        RenumberSuffixRules();
        _sheetSuffixRulesGrid.SelectedItem = rule;
    }

    private void RenumberSuffixRules()
    {
        for (int index = 0; index < _sheetSuffixRules.Count; index++)
            _sheetSuffixRules[index].Priority = (index + 1) * 10;
        _sheetSuffixRulesGrid?.Items.Refresh();
    }

    private void SetSuffixRules(System.Collections.Generic.IEnumerable<SheetSuffixRule>? rules)
    {
        _sheetSuffixRules.Clear();
        foreach (SheetSuffixRule rule in (rules ?? []).OrderBy(rule => rule.Priority))
            _sheetSuffixRules.Add(rule.Clone());
        _sheetSuffixRulesGrid?.Items.Refresh();
    }

    private void AddLabelOverride()
    {
        _sheetLabelOverrides.Add(new SheetMetadataLabelOverride());
        _sheetLabelOverridesGrid?.Items.Refresh();
    }

    private void DeleteLabelOverrides()
    {
        foreach (SheetMetadataLabelOverride item in _sheetLabelOverridesGrid?.SelectedItems.OfType<SheetMetadataLabelOverride>().ToList() ?? [])
            _sheetLabelOverrides.Remove(item);
    }

    private void SetLabelOverrides(System.Collections.Generic.IEnumerable<SheetMetadataLabelOverride>? overrides)
    {
        _sheetLabelOverrides.Clear();
        foreach (SheetMetadataLabelOverride item in overrides ?? [])
            _sheetLabelOverrides.Add(item.Clone());
        _sheetLabelOverridesGrid?.Items.Refresh();
    }

    private void LoadRuleRows()
    {
        _ruleRows.Clear();
        bool job = _rulesScope?.SelectedIndex == 1;
        SmartLearnedRuleSet set = job && _currentJob != null
            ? SmartLearningStore.LoadProjectLearnedRules(_currentJob)
            : SmartLearningStore.LoadGlobalLearnedRules();
        foreach (SmartLearnedRule rule in set.Rules.OrderByDescending(rule => rule.Enabled).ThenByDescending(rule => rule.Support).ThenBy(rule => rule.TitleToken))
            _ruleRows.Add(LearnedRuleRow.FromRule(rule));
        _rulesGrid?.Items.Refresh();
    }

    private void SetAllRules(bool enabled)
    {
        foreach (LearnedRuleRow row in _ruleRows) row.Enabled = enabled;
        _rulesGrid?.Items.Refresh();
    }

    private void SetSelRules(bool enabled)
    {
        if (_rulesGrid == null) return;
        foreach (LearnedRuleRow row in _rulesGrid.SelectedItems.OfType<LearnedRuleRow>()) row.Enabled = enabled;
        _rulesGrid.Items.Refresh();
    }

    private void DeleteSelRules()
    {
        foreach (LearnedRuleRow row in _rulesGrid?.SelectedItems.OfType<LearnedRuleRow>().ToList() ?? [])
            _ruleRows.Remove(row);
    }

    private void SaveRuleRows()
    {
        var set = new SmartLearnedRuleSet { Rules = _ruleRows.Select(row => row.ToRule()).ToList() };
        bool job = _rulesScope?.SelectedIndex == 1;
        if (job)
        {
            if (_currentJob == null) { ShowSheetMetadataStatus("Open a job to save learned job rules."); return; }
            SmartLearningStore.SaveProjectLearnedRules(_currentJob, set);
        }
        else
        {
            SmartLearningStore.SaveGlobalLearnedRules(set);
        }
        ShowSheetMetadataStatus(job ? "Saved learned rules for this job." : "Saved global learned rules.");
    }

    private string SheetMetadataEffectiveScope()
    {
        if (_currentJob != null && SettingsPresetStore.LoadJobSheetMetadataOverride(_currentJob) != null) return "This job override";
        if (SettingsPresetStore.LoadGlobalSheetMetadata() != null) return "Global";
        return "Built-in default";
    }

    private void ShowSheetMetadataStatus(string message)
    {
        if (_sheetMetadataStatus != null) _sheetMetadataStatus.Text = message;
        TxtStatus.Text = message;
    }

    private void UpdateSheetMetadataDetectorUiState()
    {
        bool precise = _sheetDetectorModeBox?.SelectedItem is SheetMetadataDetectorMode.PreciseV2;
        foreach (FrameworkElement? element in new FrameworkElement?[]
        {
            _sheetIndexEvidenceBox,
            _sheetTitleBlockLabelEvidenceBox,
            _sheetTitleEvidenceBox,
            _sheetTitleBlockScaleEvidenceBox,
            _sheetBodyEvidenceBox,
            _sheetScaleInferenceBox,
            _sheetScaleSuffixesBox,
            _sheetNoScaleSuffixesBox,
            _sheetNoScaleTerminalTokensBox,
            _sheetCompoundSuffixesBox,
            _sheetSuffixRuleEditor,
            _sheetLabelOverrideEditor,
        })
        {
            if (element != null)
                element.IsEnabled = precise;
        }

        if (_sheetDetectorModeNote != null)
        {
            _sheetDetectorModeNote.Text = precise
                ? "PreciseV2 uses the editable evidence, suffix, override, and scale rules below."
                : "Legacy detection is fixed for exact compatibility. Import policy and preservation remain editable; switch the engine to PreciseV2 to edit detector rules.";
        }
    }

    private bool TryValidateSheetMetadataConfig(out string error)
    {
        error = "";
        if (_sheetMetadataConfig.DetectorMode != SheetMetadataDetectorMode.PreciseV2)
            return true;

        foreach (SheetSuffixRule rule in _sheetMetadataConfig.SuffixRules.Where(rule => rule.Enabled))
        {
            if (rule.MatchKind != SheetMetadataMatchKind.Regex)
                continue;
            try
            {
                _ = Regex.IsMatch("", rule.Pattern ?? "", RegexOptions.IgnoreCase);
            }
            catch (ArgumentException ex)
            {
                error = $"Rule '{rule.Id}' has an invalid regex: {ex.Message}";
                return false;
            }
        }

        foreach (SheetMetadataLabelOverride item in _sheetMetadataConfig.SheetLabelOverrides.Where(item => item.Enabled))
        {
            if (string.IsNullOrWhiteSpace(item.SheetLabel))
            {
                error = "Every enabled exact override needs a sheet label.";
                return false;
            }
            if (item.SuffixAction == SheetMetadataOverrideAction.Set && string.IsNullOrWhiteSpace(item.OutputSuffix))
            {
                error = $"Override '{item.SheetLabel}' uses Suffix Set but has no suffix. Use Clear for an intentional blank.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(item.OutputPageName) &&
                item.SuffixAction != SheetMetadataOverrideAction.Keep)
            {
                error = $"Override '{item.SheetLabel}' has a final Full page name and Suffix {item.SuffixAction}. " +
                        "Use Suffix Keep, or clear Full page name to apply Set/Clear.";
                return false;
            }
            if (item.ScaleAction == SheetMetadataOverrideAction.Set &&
                !PdfSheetMetadataService.TryParseScaleMetersPerPt(item.ScaleText, out _))
            {
                error = $"Override '{item.SheetLabel}' uses Scale Set but '{item.ScaleText}' is not a supported scale.";
                return false;
            }
        }

        var duplicate = _sheetMetadataConfig.SheetLabelOverrides
            .Where(item => item.Enabled)
            .GroupBy(
                item => $"{(item.SourcePdfPattern ?? "").Trim()}\u001f{(item.SheetLabel ?? "").Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            string[] key = duplicate.Key.Split('\u001f');
            error = $"Duplicate exact override for PDF pattern '{key[0]}' and sheet '{key[1]}'.";
            return false;
        }
        return true;
    }

    private static void SetListText(TextBox? box, System.Collections.Generic.IEnumerable<string> values)
    {
        if (box != null) box.Text = string.Join(Environment.NewLine, values);
    }

    private static System.Collections.Generic.List<string> ParseList(string? text) =>
        SheetSuffixRule.SplitValues(text);

    private static void CommitGrid(DataGrid? grid)
    {
        if (grid == null) return;
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }
}
