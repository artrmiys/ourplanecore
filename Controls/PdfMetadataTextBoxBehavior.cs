using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OurPlaneCore.Controls;

public static class PdfMetadataTextBoxBehavior
{
    public static void AttachCaretOnClick(FrameworkElementFactory textBox)
    {
        textBox.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(TextBox_PreviewMouseLeftButtonDown));
        textBox.AddHandler(
            TextBox.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(TextBox_GotKeyboardFocus));
    }

    private static void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        bool shouldOwnFirstClick =
            !textBox.IsKeyboardFocusWithin ||
            IsWholeTextSelected(textBox);
        if (!shouldOwnFirstClick)
            return;

        e.Handled = true;
        SelectOwningRowForPlainClick(textBox);
        textBox.Focus();

        int caret = CharacterIndexFromMouse(textBox, e);
        textBox.Select(caret, 0);
        QueueClearWholeSelection(textBox, caret);
    }

    private static void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
            QueueClearWholeSelection(textBox, textBox.CaretIndex);
    }

    private static void SelectOwningRowForPlainClick(TextBox textBox)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
            return;

        DataGridRow? row = FindAncestor<DataGridRow>(textBox);
        if (row == null || row.IsSelected)
            return;

        if (FindAncestor<DataGrid>(row) is { } grid)
            grid.SelectedItem = row.Item;
        row.IsSelected = true;
    }

    private static int CharacterIndexFromMouse(TextBox textBox, MouseButtonEventArgs e)
    {
        Point point = e.GetPosition(textBox);
        int index = textBox.GetCharacterIndexFromPoint(point, snapToText: true);
        if (index < 0)
            index = point.X <= 0 ? 0 : textBox.Text.Length;

        return Math.Clamp(index, 0, textBox.Text.Length);
    }

    private static void QueueClearWholeSelection(TextBox textBox, int caret)
    {
        ClearWholeSelection(textBox, caret);
        textBox.Dispatcher.BeginInvoke(
            new Action(() => ClearWholeSelection(textBox, caret)),
            DispatcherPriority.Input);
        textBox.Dispatcher.BeginInvoke(
            new Action(() => ClearWholeSelection(textBox, caret)),
            DispatcherPriority.Background);
        textBox.Dispatcher.BeginInvoke(
            new Action(() => ClearWholeSelection(textBox, caret)),
            DispatcherPriority.ContextIdle);
    }

    private static void ClearWholeSelection(TextBox textBox, int caret)
    {
        if (!textBox.IsKeyboardFocusWithin || !IsWholeTextSelected(textBox))
            return;

        int safeCaret = Math.Clamp(caret, 0, textBox.Text.Length);
        textBox.Select(safeCaret, 0);
    }

    private static bool IsWholeTextSelected(TextBox textBox) =>
        textBox.Text.Length > 0 &&
        textBox.SelectionStart == 0 &&
        textBox.SelectionLength >= textBox.Text.Length;

    private static T? FindAncestor<T>(DependencyObject node)
        where T : DependencyObject
    {
        DependencyObject? current = node;
        while (current != null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
