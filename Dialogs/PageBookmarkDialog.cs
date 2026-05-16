using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OurPlaneCore;

public sealed class PageBookmarkDialog : Window
{
    private readonly TextBox _nameBox;

    public string BookmarkName { get; private set; } = "";

    public PageBookmarkDialog(string title, string initialName)
    {
        Title = title;
        Width = 360;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var ok = new Button
        {
            Content = "OK",
            Width = 72,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);

        var cancel = new Button
        {
            Content = "Cancel",
            Width = 72,
            IsCancel = true,
        };
        buttons.Children.Add(cancel);

        var stack = new StackPanel();
        root.Children.Add(stack);

        stack.Children.Add(new TextBlock
        {
            Text = "Name",
            Margin = new Thickness(0, 0, 0, 4),
        });

        _nameBox = new TextBox
        {
            Text = initialName,
            MinHeight = 24,
        };
        _nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Accept();
            }
        };
        stack.Children.Add(_nameBox);

        Loaded += (_, _) =>
        {
            _nameBox.Focus();
            _nameBox.SelectAll();
        };
    }

    private void Accept()
    {
        string clean = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            MessageBox.Show(this, "Enter a bookmark name.", "Bookmark", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BookmarkName = clean;
        DialogResult = true;
    }
}
