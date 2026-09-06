using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OurPlanCore;
using SkiaSharp;

internal static partial class CustomShortcutUiSmokeHarness
{
    private static void CheckRowAndStaleSurfaceSafety(MainWindow main)
    {
        int called = 0;
        var rowButton = new Button { Name = "OldRowAction", Content = "Row action" };
        var row = new ListBoxItem { Content = rowButton };
        rowButton.Click += (_, _) => called++;
        Call(main, "InvokeKeyboardSurface", rowButton);
        Check(called == 0, "A recycled data row's captured command must not execute.");
        var oldButton = new Button { Name = "UnattachedStaleAction", Content = "Stale action" };
        KeyboardCommandDefinition old = (KeyboardCommandDefinition)Call(main, "RegisterKeyboardSurface", oldButton)!;
        Call(main, "RefreshKeyboardCommandCatalog");
        Check(!Get<Dictionary<string, WeakReference<FrameworkElement>>>(main, "_keyboardSurfaceTargets").ContainsKey(old.Id), "A refresh must discard old UI object references.");
        GC.KeepAlive(row);
    }

    private static async Task CheckModalBoundary(MainWindow main, Measurement source)
    {
        int invoked = 0;
        var button = new Button { Name = "DialogAction", Content = "Apply selected dialog action", IsDefault = true, Margin = new Thickness(12) };
        var text = new TextBox { Name = "DialogText", Text = "Keep text editing local", Margin = new Thickness(12) };
        var panel = new StackPanel(); panel.Children.Add(button); panel.Children.Add(text);
        var modal = new Window { Title = "Shortcut dialog scope QA", Owner = main, Width = 420, Height = 180, Content = panel };
        button.Click += (_, _) => invoked++;
        _ = main.Dispatcher.BeginInvoke(new Action(() => modal.ShowDialog()));
        await WaitForVisual(button);
        var command = (KeyboardCommandDefinition)Call(main, "RegisterKeyboardSurface", button)!;
        var config = Get<KeyboardShortcutConfiguration>(main, "_customShortcuts");
        Check(command.Id.StartsWith("surface:window:", StringComparison.Ordinal), "Dialog command must have a window scope.");
        config.Overrides[command.Id] = ["F9"];
        try
        {
            SKPoint[] mainBefore = source.Points.ToArray();
            Press(button, Key.F9); await Wait(() => invoked == 1);
            Press(button, Key.F10); await Task.Delay(60);
            Check(source.Points.SequenceEqual(mainBefore), "A modal dialog must never execute hidden main-window mirroring.");
            Press(text, Key.F9); await Task.Delay(60);
            Check(invoked == 1, "Modal text editors must retain their input.");
            foreach (Key key in new[] { Key.Enter, Key.Escape })
            {
                var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(modal), 0, key)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = button };
                Check(!(bool)Call(main, "HandleCustomKeyboardShortcut", modal, input)!, "Native dialog " + key + " must remain unhandled.");
            }
            var differentDialog = new Window { Owner = main, Title = "Different dialog kind" };
            var other = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(modal), 0, Key.F9)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = button };
            Check(!(bool)Call(main, "HandleCustomKeyboardShortcut", differentDialog, other)!, "A generic Window with a different title cannot replay the command.");
            differentDialog.Close();
        }
        finally { config.Overrides.Remove(command.Id); modal.Close(); }
    }

    private static async Task CheckVisibleSettingsRecovery(MainWindow main, string root)
    {
        string path = KeyboardShortcutStore.GlobalPath;
        byte[] before = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Call(main, "LoadCustomKeyboardShortcuts");
        Check(DataFileReader.IsProtected(path), "Global read failure must remain protected after the lock is released.");
        Call(main, "ShowKeyboardShortcutSettings");
        var editor = Get<KeyboardShortcutSettingsDialog>(main, "_keyboardShortcutDialog");
        Check(Get<TextBlock>(editor, "_recoveryWarning").Text.Contains("Global", StringComparison.Ordinal), "Global settings read error must be visible in the editor.");
        await Capture(editor, Path.Combine(root, "shortcut-recovery-warning.png"));
        var completed = new TaskCompletionSource();
        _ = main.Dispatcher.BeginInvoke(new Action(async () =>
        {
            KeyboardShortcutRecoveryDialog? recovery = null;
            try
            {
                await Wait(() => Application.Current.Windows.OfType<KeyboardShortcutRecoveryDialog>().Any());
                recovery = Application.Current.Windows.OfType<KeyboardShortcutRecoveryDialog>().Single();
                await Capture(recovery, Path.Combine(root, "shortcut-recovery-dialog.png"));
                Click(recovery, "Retry reading");
                Check(recovery.Changed, "The real Retry button must recover the file.");
                completed.SetResult();
            }
            catch (Exception ex) { completed.SetException(ex); }
            finally { recovery?.Close(); }
        }));
        Click(editor, "Recover settings...");
        await completed.Task;
        Check(!DataFileReader.IsProtected(path), "Retry must clear protection explicitly.");
        Check(before.SequenceEqual(File.ReadAllBytes(path)), "Global shortcut recovery must preserve every original byte.");
        Check(Get<TextBlock>(editor, "_recoveryWarning").Visibility == Visibility.Collapsed, "Recovered settings no longer show a stale error.");
        editor.Close();
    }
}
