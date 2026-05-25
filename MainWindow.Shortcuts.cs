using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OurPlaneCore;

public partial class MainWindow
{
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
