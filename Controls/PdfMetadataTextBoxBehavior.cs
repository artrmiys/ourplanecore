using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OurPlaneCore.Controls;

public sealed class PdfMetadataTextBox : TextBox
{
    private bool _clearingSelection;
    private int _protectedCaret;
    private DateTime _protectWholeSelectionUntilUtc;

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        bool shouldOwnFirstClick =
            !IsKeyboardFocusWithin ||
            IsWholeTextSelected();
        if (shouldOwnFirstClick)
        {
            e.Handled = true;
            int caret = CharacterIndexFromMouse(e);
            SelectOwningRowForPlainClick();
            Focus();
            ProtectCaret(caret);
            return;
        }

        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        ProtectCaret(CaretIndex);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            _protectWholeSelectionUntilUtc = DateTime.MinValue;

        base.OnPreviewKeyDown(e);
    }

    protected override void OnSelectionChanged(RoutedEventArgs e)
    {
        base.OnSelectionChanged(e);
        if (_clearingSelection ||
            !IsKeyboardFocusWithin ||
            DateTime.UtcNow > _protectWholeSelectionUntilUtc ||
            !IsWholeTextSelected())
        {
            return;
        }

        ClearWholeSelection(_protectedCaret);
    }

    private void SelectOwningRowForPlainClick()
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
            return;

        DataGridRow? row = FindAncestor<DataGridRow>(this);
        if (row == null || row.IsSelected)
            return;

        if (FindAncestor<DataGrid>(row) is { } grid)
            grid.SelectedItem = row.Item;
        row.IsSelected = true;
    }

    private int CharacterIndexFromMouse(MouseButtonEventArgs e)
    {
        Point point = e.GetPosition(this);
        int index = GetCharacterIndexFromPoint(point, snapToText: true);
        if (index < 0)
            index = point.X <= 0 ? 0 : Text.Length;

        return Math.Clamp(index, 0, Text.Length);
    }

    private void ProtectCaret(int caret)
    {
        _protectedCaret = Math.Clamp(caret, 0, Text.Length);
        _protectWholeSelectionUntilUtc = DateTime.UtcNow.AddSeconds(1);
        ClearWholeSelection(_protectedCaret);
        Dispatcher.BeginInvoke(
            new Action(() => ClearWholeSelection(_protectedCaret)),
            DispatcherPriority.Input);
        Dispatcher.BeginInvoke(
            new Action(() => ClearWholeSelection(_protectedCaret)),
            DispatcherPriority.Background);
        Dispatcher.BeginInvoke(
            new Action(() => ClearWholeSelection(_protectedCaret)),
            DispatcherPriority.ContextIdle);
    }

    private void ClearWholeSelection(int caret)
    {
        if (!IsKeyboardFocusWithin || !IsWholeTextSelected())
            return;

        _clearingSelection = true;
        try
        {
            int safeCaret = Math.Clamp(caret, 0, Text.Length);
            Select(safeCaret, 0);
        }
        finally
        {
            _clearingSelection = false;
        }
    }

    private bool IsWholeTextSelected() =>
        Text.Length > 0 &&
        SelectionStart == 0 &&
        SelectionLength >= Text.Length;

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
