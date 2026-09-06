using System.Windows;
using System.Windows.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private KeyboardShortcutSettingsDialog? _keyboardShortcutDialog;

    private FrameworkElement BuildKeyboardShortcutsPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(Header("Keyboard Shortcuts"));
        panel.Children.Add(new TextBlock { Text = "Assign or change keys for tools, editing, pages, takeoffs, menus and workspace controls. Original shortcuts remain the defaults.",
            TextWrapping = TextWrapping.Wrap, MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 12) });
        panel.Children.Add(MgrButton("Open Keyboard Shortcuts...", (_, _) => ShowKeyboardShortcutSettings(), primary: true));
        return panel;
    }

    private void ShowKeyboardShortcutSettings()
    {
        if (_keyboardShortcutDialog != null)
        {
            _keyboardShortcutDialog.Show(); _keyboardShortcutDialog.Activate(); return;
        }
        if (!_customShortcutSettingsLoaded) LoadCustomKeyboardShortcuts();
        RefreshKeyboardCommandCatalog();
        var dialog = new KeyboardShortcutSettingsDialog(_keyboardCommands.Values, _customShortcuts,
            _currentJob != null && IsCurrentJobWritable, SaveKeyboardShortcutSettings, KeyboardShortcutStore.IssueSummary(_currentJob)) { Owner = this };
        _keyboardShortcutDialog = dialog;
        dialog.Closed += (_, _) =>
        {
            _keyboardShortcutDialog = null;
            _keyboardCommandPicker = null;
            System.Windows.Input.InputManager.Current.PreProcessInput -= KeyboardCommandPickInput;
        };
        dialog.PickCommandRequested += () =>
        {
            dialog.Hide();
            BeginKeyboardCommandPick(command =>
            {
                dialog.SelectPickedCommand(command);
                dialog.Owner = _keyboardPickedWindow is { IsVisible: true } owner ? owner : this;
                dialog.Show(); dialog.Activate();
            });
            Activate();
        };
        dialog.RecoveryRequested += () =>
        {
            var recovery = new KeyboardShortcutRecoveryDialog(_currentJob) { Owner = dialog };
            recovery.ShowDialog();
            LoadCustomKeyboardShortcuts();
            dialog.ReloadRecoveredConfiguration(_customShortcuts, KeyboardShortcutStore.IssueSummary(_currentJob));
        };
        dialog.Show();
    }

    private string SaveKeyboardShortcutSettings(KeyboardShortcutConfiguration config, string scope)
    {
        config.Validate();
        if (scope != "inherit") config.ValidateConflicts(_keyboardCommands.Values);
        if (scope == "global") KeyboardShortcutStore.Save(KeyboardShortcutStore.GlobalPath, config);
        else
        {
            if (_currentJob == null || !EnsureCurrentJobWritable("save keyboard shortcuts")) return "Open a writable job first.";
            KeyboardShortcutStore.Save(KeyboardShortcutStore.JobPath(_currentJob), scope == "inherit"
                ? new KeyboardShortcutConfiguration { InheritGlobal = true } : config);
        }
        LoadCustomKeyboardShortcuts();
        ClearGlobalShortcutSequence(); _customShortcutSequence = "";
        return scope == "global" ? "Global shortcuts saved. A saved job override takes priority." :
            scope == "inherit" ? "This job now uses the global shortcuts." : "Job shortcuts saved and applied.";
    }
}
