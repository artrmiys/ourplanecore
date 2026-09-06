using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;

namespace OurPlanCore;

internal sealed class KeyboardShortcutSettingsDialog : Window
{
    private readonly Dictionary<string, KeyboardCommandDefinition> _commands;
    private KeyboardShortcutConfiguration _draft;
    private readonly TextBox _search = new() { MinWidth = 230, Margin = new Thickness(0, 0, 8, 6) };
    private readonly ComboBox _category = new() { MinWidth = 210, Margin = new Thickness(0, 0, 8, 6) };
    private readonly CheckBox _modifiedOnly = new() { Content = "Changed only", VerticalAlignment = VerticalAlignment.Center };
    private readonly DataGrid _grid = new() { IsReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Single,
        CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, MinHeight = 180 };
    private readonly TextBox _gesture = new() { IsReadOnly = true, MinWidth = 210, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Click here, then press the desired keys." };
    private readonly CheckBox _sequence = new() { Content = "Sequence", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 7), MinHeight = 36 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 5) };
    private readonly TextBlock _recoveryWarning = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
    private string _capturedGesture = "";
    private readonly Func<KeyboardShortcutConfiguration, string, string> _save;
    public event Action? PickCommandRequested;
    public event Action? RecoveryRequested;

    public KeyboardShortcutSettingsDialog(IEnumerable<KeyboardCommandDefinition> commands,
        KeyboardShortcutConfiguration configuration, bool hasWritableJob,
        Func<KeyboardShortcutConfiguration, string, string> save, string recoveryWarning = "")
    {
        _commands = commands.ToDictionary(command => command.Id, StringComparer.Ordinal);
        _draft = configuration.Clone(); _save = save;
        Title = "Keyboard Shortcuts";
        Width = 1050; Height = 710; MinWidth = 780; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "ControlForegroundBrush");
        var root = new DockPanel { Margin = new Thickness(14) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "Keyboard Shortcuts", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) });
        top.Children.Add(new TextBlock { Text = "Select a command, click New shortcut and press the keys. Click Assign, then Save global default or Save for this job. Existing keys stay unchanged until you save.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 9) });
        var filters = new WrapPanel();
        filters.Children.Add(_search); filters.Children.Add(_category); filters.Children.Add(_modifiedOnly);
        AddButton(filters, "Choose command in app...", () => PickCommandRequested?.Invoke());
        top.Children.Add(filters); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        _recoveryWarning.Text = recoveryWarning;
        _recoveryWarning.Visibility = string.IsNullOrWhiteSpace(recoveryWarning) ? Visibility.Collapsed : Visibility.Visible;
        top.Children.Add(_recoveryWarning);
        _search.ToolTip = "Search command, category, key or control name";
        _search.TextChanged += (_, _) => RefreshRows();
        _category.SelectionChanged += (_, _) => RefreshRows();
        _modifiedOnly.Click += (_, _) => RefreshRows();
        var bottom = new StackPanel();
        bottom.Children.Add(_details);
        var binding = new WrapPanel { Margin = new Thickness(0, 4, 0, 5) };
        binding.Children.Add(new TextBlock { Text = "New shortcut:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 5) });
        binding.Children.Add(_gesture); binding.Children.Add(_sequence);
        AddButton(binding, "Assign", AssignCaptured);
        AddButton(binding, "Clear capture", () => { _capturedGesture = ""; _gesture.Text = ""; _gesture.Focus(); });
        AddButton(binding, "Remove keys", RemoveBinding);
        AddButton(binding, "Reset command", ResetCommand);
        bottom.Children.Add(binding);
        bottom.Children.Add(new TextBlock { Text = "Text entry and mouse modifiers (Ctrl/Shift/Alt + click/drag) keep their normal behavior. A command with no key can still be assigned. Context menu commands require the corresponding selection.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 3, 0, 7) });
        var presets = new WrapPanel();
        AddButton(presets, "Reset all to original", () => { _draft = KeyboardShortcutConfiguration.BuildDefault(); RefreshRows(); });
        AddButton(presets, "Import preset...", ImportPreset);
        AddButton(presets, "Export preset...", ExportPreset);
        AddButton(presets, "Recover settings...", () => RecoveryRequested?.Invoke());
        bottom.Children.Add(presets);
        bottom.Children.Add(_status);
        var saves = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        AddButton(saves, "Save global default", () => Save("global"));
        AddButton(saves, "Save for this job", () => Save("job"), hasWritableJob);
        AddButton(saves, "Use global for this job", () => Save("inherit"), hasWritableJob);
        AddButton(saves, "Close", Close);
        bottom.Children.Add(saves);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        AddColumn("Command", nameof(CommandRow.Title), 2.7);
        AddColumn("Category", nameof(CommandRow.Category), 1.4);
        AddColumn("Keys", nameof(CommandRow.Keys), 1.2);
        AddColumn("Original keys", nameof(CommandRow.Defaults), 1.2);
        AddColumn("Context", nameof(CommandRow.Context), 0.9);
        _grid.SelectionChanged += (_, _) => UpdateSelection();
        root.Children.Add(_grid); Content = root;
        _gesture.PreviewKeyDown += CaptureKey;
        RefreshCategories(); RefreshRows();
        Loaded += (_, _) => _search.Focus();
    }

    public void SelectPickedCommand(KeyboardCommandDefinition command)
    {
        _commands[command.Id] = command;
        if (command.Id.StartsWith("surface:", StringComparison.Ordinal) && !_draft.PickedCommands.Any(existing => existing.Id == command.Id))
            _draft.PickedCommands.Add(command);
        _search.Text = ""; _category.SelectedIndex = 0; _modifiedOnly.IsChecked = false;
        RefreshCategories(); RefreshRows(command.Id);
        _gesture.Focus();
    }

    public void ReloadRecoveredConfiguration(KeyboardShortcutConfiguration config, string warning)
    {
        _draft = config.Clone();
        _recoveryWarning.Text = warning;
        _recoveryWarning.Visibility = string.IsNullOrWhiteSpace(warning) ? Visibility.Collapsed : Visibility.Visible;
        RefreshRows();
        _status.Text = "Recovered settings loaded. Unsaved edits can be assigned again.";
    }

    private void AddColumn(string header, string path, double width) => _grid.Columns.Add(new DataGridTextColumn
    { Header = header, Binding = new Binding(path), Width = new DataGridLength(width, DataGridLengthUnitType.Star) });

    private static void AddButton(Panel panel, string text, Action action, bool enabled = true)
    {
        var button = new Button { Content = text, IsEnabled = enabled, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 5) };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }

    private void RefreshCategories()
    {
        string? selected = _category.SelectedItem as string;
        _category.ItemsSource = new[] { "All categories" }.Concat(_commands.Values.Select(command => command.Category).Distinct().Order()).ToArray();
        _category.SelectedItem = selected ?? "All categories";
    }

    private void RefreshRows(string? select = null)
    {
        select ??= (_grid.SelectedItem as CommandRow)?.Id;
        string query = _search.Text.Trim();
        string category = _category.SelectedItem as string ?? "All categories";
        _grid.ItemsSource = _commands.Values.Where(command =>
            (category == "All categories" || command.Category == category) &&
            (_modifiedOnly.IsChecked != true || _draft.Overrides.ContainsKey(command.Id)) &&
            (query.Length == 0 || $"{command.Title} {command.Category} {command.Id} {string.Join(" ", _draft.Effective(command))}"
                .Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(command => command.Category).ThenBy(command => command.Title)
            .Select(command => new CommandRow(command.Id, command.Title, command.Category,
                Display(_draft.Effective(command)), Display(command.Defaults), command.Context.ToString())).ToList();
        _grid.SelectedItem = _grid.Items.OfType<CommandRow>().FirstOrDefault(row => row.Id == select);
        if (_grid.SelectedItem is { } selected)
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (!ReferenceEquals(_grid.SelectedItem, selected)) return;
                _grid.UpdateLayout(); _grid.ScrollIntoView(selected);
            }));
        _status.Text = $"{_grid.Items.Count} commands shown; {_draft.Overrides.Count} changed commands. Changes are not saved yet.";
    }

    private static string Display(IEnumerable<string> keys) => string.Join(" / ", keys.Select(KeyboardShortcutGesture.Display));
    private KeyboardCommandDefinition? Selected => _grid.SelectedItem is CommandRow row ? _commands[row.Id] : null;

    private void UpdateSelection()
    {
        KeyboardCommandDefinition? command = Selected;
        _details.Text = command == null ? "Select a command." : $"{command.Title} • {command.Context}\n{command.Description}";
        _capturedGesture = ""; _gesture.Text = "";
    }

    private void CaptureKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (e.IsRepeat) return;
        string value = KeyboardShortcutGesture.FromKey(KeyboardShortcutKeys.EffectiveKey(e), Keyboard.Modifiers);
        if (!KeyboardShortcutGesture.TryNormalize(value, out string normalized, out string error)) { _status.Text = error; return; }
        if (_sequence.IsChecked == true && _capturedGesture.Length > 0 && _capturedGesture.Split(',').Length < 3)
            _capturedGesture += ", " + normalized;
        else _capturedGesture = normalized;
        _gesture.Text = KeyboardShortcutGesture.Display(_capturedGesture);
        if (Selected is { } command)
        {
            var conflicts = _draft.FindConflicts(command, _capturedGesture, _commands.Values);
            _status.Text = conflicts.Count == 0 ? "Ready to assign." : "Already used by: " + string.Join(", ", conflicts.Select(item => item.Title));
        }
    }

    private void AssignCaptured()
    {
        if (Selected is not { } command || !KeyboardShortcutGesture.TryNormalize(_capturedGesture, out string gesture, out _)) return;
        var conflicts = _draft.FindConflicts(command, gesture, _commands.Values);
        if (conflicts.Count > 0)
        {
            if (MessageBox.Show(this, "Replace the conflicting keys for:\n\n" + string.Join("\n", conflicts.Select(item => item.Title + " (" + item.Context + ")")),
                "Shortcut conflict", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            foreach (KeyboardCommandDefinition conflict in conflicts)
                _draft.Overrides[conflict.Id] = _draft.Effective(conflict).Where(key => !KeyboardShortcutGesture.Conflicts(key, gesture)).ToList();
        }
        _draft.Overrides[command.Id] = [gesture];
        if (command.Id.StartsWith("surface:", StringComparison.Ordinal) && !_draft.PickedCommands.Any(existing => existing.Id == command.Id))
            _draft.PickedCommands.Add(command);
        RefreshRows(command.Id);
    }

    private void RemoveBinding()
    {
        if (Selected is not { } command) return;
        _draft.Overrides[command.Id] = [];
        RefreshRows(command.Id);
    }

    private void ResetCommand()
    {
        if (Selected is not { } command) return;
        _draft.Overrides.Remove(command.Id); RefreshRows(command.Id);
    }

    private void Save(string scope)
    {
        try { _status.Text = _save(_draft.Clone(), scope); }
        catch (Exception ex) { _status.Text = "Could not save: " + ex.Message; }
    }

    private void ImportPreset()
    {
        var picker = new OpenFileDialog { Title = "Import keyboard shortcut preset", Filter = "Keyboard shortcuts|*.json" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            _draft = KeyboardShortcutStore.Parse(File.ReadAllText(picker.FileName));
            foreach (var command in _draft.PickedCommands) _commands.TryAdd(command.Id, command);
            RefreshCategories(); RefreshRows();
        }
        catch (Exception ex) { _status.Text = "Invalid preset: " + ex.Message; }
    }

    private void ExportPreset()
    {
        var picker = new SaveFileDialog { Title = "Export keyboard shortcut preset", Filter = "Keyboard shortcuts|*.json", FileName = "keyboard-shortcuts.json" };
        if (picker.ShowDialog(this) != true) return;
        try { IoUtil.WriteAllTextAtomic(picker.FileName, KeyboardShortcutStore.Export(_draft)); _status.Text = "Preset exported."; }
        catch (Exception ex) { _status.Text = "Could not export: " + ex.Message; }
    }

    private sealed record CommandRow(string Id, string Title, string Category, string Keys, string Defaults, string Context);
}
