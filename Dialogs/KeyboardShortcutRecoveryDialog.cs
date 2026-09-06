using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace OurPlanCore;

internal sealed class KeyboardShortcutRecoveryDialog : Window
{
    private readonly OurPlanCoreJob? _job;
    private readonly ListBox _files = new() { MinHeight = 100 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    public bool Changed { get; private set; }

    public KeyboardShortcutRecoveryDialog(OurPlanCoreJob? job)
    {
        _job = job; Title = "Recover Keyboard Shortcuts"; Width = 760; Height = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(14) };
        var explanation = new TextBlock { Text = "Unreadable shortcut files are protected from overwrite. Retry after access is restored, restore a preset/copy, or reset to the original keys. Existing bytes and quarantine copies are retained for rollback.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(explanation, Dock.Top); root.Children.Add(explanation);
        var buttons = new WrapPanel();
        Add(buttons, "Retry reading", () => Recover("retry"));
        Add(buttons, "Restore / import...", () => Recover("restore"));
        Add(buttons, "Reset to original keys", () => Recover("reset"));
        Add(buttons, "Close", Close);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        _files.DisplayMemberPath = nameof(Row.Title); root.Children.Add(_files); Content = root;
        Refresh();
    }

    private void Refresh()
    {
        _files.ItemsSource = KeyboardShortcutStore.Issues(_job).Select(issue => new Row(issue.Path,
            (issue.Path == KeyboardShortcutStore.GlobalPath ? "Global" : "This job") + " • " + issue.State + "\n" + issue.Path)).ToArray();
        if (_files.Items.Count > 0) _files.SelectedIndex = 0;
        else _status.Text = "No protected shortcut files remain.";
    }

    private void Recover(string action)
    {
        if (_files.SelectedItem is not Row selected) return;
        try
        {
            string? restore = null;
            if (action == "restore")
            {
                var picker = new OpenFileDialog { Title = "Select shortcut preset or recovery copy", Filter = "Shortcut and recovery files|*.json;*.corrupt-*;*.recovered-*;*.before-repair-*|All files|*.*" };
                if (picker.ShowDialog(this) != true) return;
                restore = picker.FileName;
            }
            if (action == "reset" && MessageBox.Show(this, "Restore the original keyboard shortcuts for this file?\n\nThe old file and recovery copies will be retained.",
                "Reset keyboard shortcuts", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            KeyboardShortcutStore.Recover(selected.Path, restore, reset: action == "reset");
            Changed = true; Refresh();
            _status.Text = "Verified and recovered. Existing copies remain available for rollback.";
        }
        catch (Exception ex) { _status.Text = "Still protected: " + ex.Message; }
    }

    private static void Add(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 7, 4) };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }

    private sealed record Row(string Path, string Title);
}
