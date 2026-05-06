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
            Keyboard.Modifiers != ModifierKeys.None ||
            ShouldSkipTakeoffShortcut(e.OriginalSource as DependencyObject))
        {
            return;
        }

        switch (e.Key)
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
