using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OurPlaneCore;

public partial class MainWindow
{
    private const int ShortcutSequenceTimeoutMs = 450;
    private System.Windows.Threading.DispatcherTimer? _shortcutSequenceTimer;
    private string _shortcutSequence = "";

    private void MainWindow_GlobalPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat ||
            e.Handled ||
            ShouldSkipTakeoffShortcut(e.OriginalSource as DependencyObject))
        {
            return;
        }

        Key key = KeyboardShortcutKeys.EffectiveKey(e);
        if (HandleGlobalModifiedShortcut(key))
        {
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
            return;

        if (HandleShortcutSequenceKey(key))
        {
            e.Handled = true;
            return;
        }

        switch (key)
        {
            case Key.Space:
                BtnActiveTakeoffRecord_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.T:
                BtnNewItem_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    private bool HandleGlobalModifiedShortcut(Key key)
    {
        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.O:
                    BtnOpen_Click(this, new RoutedEventArgs());
                    return true;
                case Key.S:
                    BtnSave_Click(this, new RoutedEventArgs());
                    return true;
            }
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            switch (key)
            {
                case Key.O:
                    ShowRecentJobPicker();
                    return true;
                case Key.P:
                    ShowCommandPalette();
                    return true;
            }
        }

        return false;
    }

    private bool HandleShortcutSequenceKey(Key key)
    {
        string token = KeyboardShortcutKeys.SequenceToken(key);
        if (string.IsNullOrWhiteSpace(token))
        {
            FlushShortcutSequence();
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_shortcutSequence))
        {
            string sequence = _shortcutSequence + token;
            if (string.Equals(sequence, "bk", StringComparison.Ordinal))
            {
                ClearShortcutSequence();
                AddBookmarkFromShortcut();
                return true;
            }

            FlushShortcutSequence();
        }

        if (string.Equals(token, "b", StringComparison.Ordinal))
        {
            BeginShortcutSequence(token);
            TxtStatus.Text = "Shortcut B: press K to name a Bookmark, or wait for Box.";
            return true;
        }

        return false;
    }

    private void BeginShortcutSequence(string token)
    {
        _shortcutSequence = token;
        _shortcutSequenceTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ShortcutSequenceTimeoutMs),
        };
        _shortcutSequenceTimer.Tick -= ShortcutSequenceTimer_Tick;
        _shortcutSequenceTimer.Tick += ShortcutSequenceTimer_Tick;
        _shortcutSequenceTimer.Stop();
        _shortcutSequenceTimer.Start();
    }

    private void ShortcutSequenceTimer_Tick(object? sender, EventArgs e)
    {
        FlushShortcutSequence();
    }

    private void FlushShortcutSequence()
    {
        string sequence = _shortcutSequence;
        ClearShortcutSequence();

        if (string.Equals(sequence, "b", StringComparison.Ordinal))
            SetTool("drawrect");
    }

    private void ClearShortcutSequence()
    {
        _shortcutSequenceTimer?.Stop();
        _shortcutSequence = "";
    }

    private static bool ShouldSkipTakeoffShortcut(DependencyObject? source)
    {
        for (DependencyObject? current = source; current != null; current = GetShortcutParent(current))
        {
            if (current is TextBoxBase or PasswordBox or ComboBox or MenuItem)
                return true;
        }

        return false;
    }

    private static DependencyObject? GetShortcutParent(DependencyObject current)
    {
        if (current is Visual or Visual3D)
            return VisualTreeHelper.GetParent(current);

        return LogicalTreeHelper.GetParent(current);
    }
}
