using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace OurPlanCore;

internal sealed class ProjectDataRecoveryDialog : Window
{
    private readonly string _root;
    private readonly ListBox _files = new() { MinHeight = 160 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    public bool Changed { get; private set; }

    public ProjectDataRecoveryDialog(string root)
    {
        _root = root;
        Title = "Project Data Recovery";
        Width = 790; Height = 370;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(16) };
        var info = new TextBlock { Text = "These files are protected from overwrite. Retry after access is restored, or select a known-good JSON copy. Restored bytes are preserved in a recovery backup. Close this window to reload the project.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(info, Dock.Top); panel.Children.Add(info);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        AddButton(buttons, "Retry reading", () => Recover(false));
        AddButton(buttons, "Restore from copy...", () => Recover(true));
        AddButton(buttons, "Open file folder", OpenFolder);
        AddButton(buttons, "Close", Close);
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        DockPanel.SetDock(_status, Dock.Bottom); panel.Children.Add(_status);
        _files.DisplayMemberPath = nameof(RecoveryRow.Label);
        panel.Children.Add(_files); Content = panel;
        Refresh();
    }

    private void Refresh()
    {
        _files.ItemsSource = DataFileReader.Issues(_root).Select(issue => new RecoveryRow(issue.Path,
            $"{issue.State}: {Path.GetRelativePath(_root, issue.Path)} — {issue.Error}")).ToArray();
        if (_files.Items.Count > 0) _files.SelectedIndex = 0;
    }

    private void Recover(bool restore)
    {
        if (_files.SelectedItem is not RecoveryRow row) return;
        try
        {
            string? copy = null;
            if (restore)
            {
                var picker = new OpenFileDialog { Title = "Select a known-good copy of " + Path.GetFileName(row.Path), Filter = "JSON and recovery files|*.json;*.corrupt-*;*.recovered-*;*.before-repair-*|All files|*.*" };
                if (picker.ShowDialog(this) != true) return;
                copy = picker.FileName;
            }
            DataFileReader.RestoreOrRetry(row.Path, copy);
            Changed = true;
            _status.Text = "Verified. The project will reload when this window closes.";
            Refresh();
        }
        catch (Exception ex) { _status.Text = "Still protected: " + ex.Message; }
    }

    private void OpenFolder()
    {
        if (_files.SelectedItem is not RecoveryRow row) return;
        string folder = SafeJobPathResolver.ResolveInside(_root, Path.GetDirectoryName(row.Path)!, _root);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = true });
    }

    private static void AddButton(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }

    private sealed record RecoveryRow(string Path, string Label);
}
