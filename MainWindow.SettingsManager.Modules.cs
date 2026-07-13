using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OurPlaneCore;

public partial class MainWindow
{
    private ModuleFeatureConfig _moduleDraft = ModuleFeatureConfig.BuildDefault();
    private readonly Dictionary<ModuleId, CheckBox> _moduleCheckBoxes = [];
    private TextBlock? _moduleSettingsSourceText;
    private ComboBox? _modulePresetBox;

    private FrameworkElement BuildModulesPanel()
    {
        var root = new DockPanel { Margin = new Thickness(2) };

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        _modulePresetBox = new ComboBox
        {
            Width = 150,
            Margin = new Thickness(0, 0, 6, 4),
            ItemsSource = new[] { "Full (current)", "No AI / 3D", "Takeoff Only", "Minimal" },
            SelectedIndex = 0,
        };
        actions.Children.Add(_modulePresetBox);
        actions.Children.Add(MgrButton("Load preset", (_, _) => LoadSelectedModulePreset()));
        actions.Children.Add(MgrButton("Reset", (_, _) => ResetModuleDraft()));
        actions.Children.Add(MgrButton("Apply", (_, _) => ApplyModuleDraft(), primary: true));
        actions.Children.Add(MgrButton("Save global", (_, _) => SaveGlobalModuleDraft()));
        actions.Children.Add(MgrButton("Save for this job", (_, _) => SaveJobModuleDraft()));
        actions.Children.Add(MgrButton("Clear job override", (_, _) => ClearJobModuleOverride()));
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        _moduleSettingsSourceText = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
        };
        DockPanel.SetDock(_moduleSettingsSourceText, Dock.Top);
        root.Children.Add(_moduleSettingsSourceText);

        var stack = new StackPanel();
        _moduleCheckBoxes.Clear();
        foreach (IGrouping<string, ModuleFeatureDefinition> group in
                 ModuleFeatureCatalog.All.GroupBy(definition => definition.Group))
        {
            stack.Children.Add(Header(group.Key));
            foreach (ModuleFeatureDefinition definition in group)
            {
                var box = new CheckBox
                {
                    Content = definition.Name,
                    IsChecked = _moduleDraft.IsEnabled(definition.Id),
                    Margin = new Thickness(0, 2, 0, 0),
                    FontSize = 12,
                    ToolTip = definition.Description,
                };
                box.Checked += (_, _) => _moduleDraft.SetEnabled(definition.Id, true);
                box.Unchecked += (_, _) => _moduleDraft.SetEnabled(definition.Id, false);
                _moduleCheckBoxes[definition.Id] = box;
                stack.Children.Add(box);
                stack.Children.Add(new TextBlock
                {
                    Text = definition.Description,
                    Margin = new Thickness(22, 0, 8, 4),
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
                });
            }
        }

        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack,
        });
        BindModulesSettings();
        return root;
    }

    private void BindModulesSettings()
    {
        ModuleFeatureConfig persisted = ModuleFeatureStore.Resolve(_currentJob);
        bool liveOnly = !ModuleConfigsEqual(_moduleFeatures, persisted);
        _moduleDraft = _moduleFeatures.Clone();
        SyncModuleCheckBoxes();
        if (_moduleSettingsSourceText != null)
        {
            string source = liveOnly
                ? "Live Apply (not saved)"
                : _currentJob != null && ModuleFeatureStore.LoadJobOverride(_currentJob) != null
                    ? $"This job override: {_currentJob.Name}"
                    : ModuleFeatureStore.LoadGlobal() != null
                        ? "Global module preset"
                        : "Built-in defaults (current app behavior)";
            _moduleSettingsSourceText.Text =
                $"Effective source: {source}. Off hides the module everywhere and blocks execution; saved project data is preserved.";
        }
    }

    private void SyncModuleCheckBoxes()
    {
        foreach ((ModuleId id, CheckBox box) in _moduleCheckBoxes)
            box.IsChecked = _moduleDraft.IsEnabled(id);
    }

    private static bool ModuleConfigsEqual(ModuleFeatureConfig left, ModuleFeatureConfig right) =>
        ModuleFeatureCatalog.All.All(definition =>
            left.IsEnabled(definition.Id) == right.IsEnabled(definition.Id));

    private void ResetModuleDraft()
    {
        _moduleDraft = ModuleFeatureConfig.BuildDefault();
        SyncModuleCheckBoxes();
        TxtStatus.Text = "Module draft reset to current built-in behavior; press Apply or Save.";
    }

    private void LoadSelectedModulePreset()
    {
        string preset = _modulePresetBox?.SelectedItem?.ToString() ?? "Full (current)";
        _moduleDraft = ModuleFeatureConfig.BuildDefault();
        if (preset == "No AI / 3D")
        {
            _moduleDraft.SetEnabled(ModuleId.Ai, false);
            _moduleDraft.SetEnabled(ModuleId.ThreeD, false);
        }
        else if (preset == "Takeoff Only")
        {
            DisableModuleDraft(
                ModuleId.Annotations, ModuleId.SheetOverlay, ModuleId.ReportBuilder,
                ModuleId.Materials, ModuleId.Ai, ModuleId.ThreeD,
                ModuleId.Bookmarks, ModuleId.QuickCalculator, ModuleId.DetachedSheets);
        }
        else if (preset == "Minimal")
        {
            foreach (ModuleFeatureDefinition definition in ModuleFeatureCatalog.All)
                _moduleDraft.SetEnabled(definition.Id, false);
        }
        SyncModuleCheckBoxes();
        TxtStatus.Text = $"Loaded module preset: {preset}; press Apply or Save.";
    }

    private void DisableModuleDraft(params ModuleId[] modules)
    {
        foreach (ModuleId module in modules)
            _moduleDraft.SetEnabled(module, false);
    }

    private void ApplyModuleDraft()
    {
        _moduleFeatures = _moduleDraft.Clone();
        ApplyModuleAvailability(announce: true);
        BindModulesSettings();
    }

    private void SaveGlobalModuleDraft()
    {
        ModuleFeatureStore.SaveGlobal(_moduleDraft);
        ReloadModuleFeatures();
        BindModulesSettings();
        TxtStatus.Text = "Saved global module settings.";
    }

    private void SaveJobModuleDraft()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before saving a job module override.";
            return;
        }

        ModuleFeatureStore.SaveJobOverride(_currentJob, _moduleDraft);
        ReloadModuleFeatures();
        BindModulesSettings();
        TxtStatus.Text = $"Saved module settings for {_currentJob.Name}.";
    }

    private void ClearJobModuleOverride()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before clearing a job module override.";
            return;
        }

        ModuleFeatureStore.ClearJobOverride(_currentJob);
        ReloadModuleFeatures();
        BindModulesSettings();
        TxtStatus.Text = "Cleared this job's module override; global/default settings now apply.";
    }
}
