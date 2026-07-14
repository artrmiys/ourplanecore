using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OurPlanCore;

public partial class MainWindow
{
    private static readonly TimeSpan GlobalShortcutSequenceTimeout = TimeSpan.FromMilliseconds(900);
    private string _globalShortcutSequence = "";
    private DateTime _globalShortcutSequenceAtUtc = DateTime.MinValue;

    private void MainWindow_GlobalPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat || e.Handled)
            return;

        Key key = KeyboardShortcutKeys.EffectiveKey(e);

        // F1 toggles the shortcuts overlay; Esc closes it (works regardless of focus).
        if (key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            ToggleShortcutsOverlay();
            e.Handled = true;
            return;
        }
        if (key == Key.Escape && ShortcutsOverlay is { Visibility: Visibility.Visible })
        {
            ShortcutsOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        if (ShouldSkipTakeoffShortcut(e.OriginalSource as DependencyObject))
            return;

        if (HandleGlobalModifiedShortcut(key))
        {
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            ClearGlobalShortcutSequence();
            return;
        }

        if (HandleGlobalShortcutSequence(key))
        {
            e.Handled = true;
            return;
        }

        switch (key)
        {
            case Key.F4:
                SetSelectedPagesScaleFromShortcut();
                e.Handled = true;
                break;
            case Key.F5:
                BtnFloatingPageSetup_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                CollapseProjectTreeDisplaysWithStatus();
                e.Handled = true;
                break;
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

    private bool HandleGlobalShortcutSequence(Key key)
    {
        string token = KeyboardShortcutKeys.SequenceToken(key);
        if (string.IsNullOrEmpty(token))
        {
            ClearGlobalShortcutSequence();
            return false;
        }

        DateTime now = DateTime.UtcNow;
        string previous = now - _globalShortcutSequenceAtUtc <= GlobalShortcutSequenceTimeout
            ? _globalShortcutSequence
            : "";
        string sequence = previous + token;

        if (string.Equals(sequence, "bk", StringComparison.Ordinal))
        {
            ClearGlobalShortcutSequence();
            AddBookmarkFromShortcut();
            return true;
        }

        if ("bk".StartsWith(sequence, StringComparison.Ordinal))
        {
            _globalShortcutSequence = sequence;
            _globalShortcutSequenceAtUtc = now;
            return false;
        }

        ClearGlobalShortcutSequence();
        return false;
    }

    private void ClearGlobalShortcutSequence()
    {
        _globalShortcutSequence = "";
        _globalShortcutSequenceAtUtc = DateTime.MinValue;
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
                case Key.M:
                    MergeSelectedMeasurementsToPromptedTakeoff();
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
                case Key.M:
                    SplitSelectedMeasurementsToNewTakeoff();
                    return true;
                case Key.P:
                    ShowCommandPalette();
                    return true;
            }
        }

        return false;
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
