using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OurPlanCore;

public enum PageBookmarkSaveMode
{
    View,
    CropImage,
}

public sealed class PageBookmarkDialog : Window
{
    private readonly TextBox _nameBox;
    private readonly RadioButton? _viewMode;
    private readonly RadioButton? _cropImageMode;

    public string BookmarkName { get; private set; } = "";
    public PageBookmarkSaveMode SaveMode { get; private set; } = PageBookmarkSaveMode.View;

    public PageBookmarkDialog(
        string title,
        string initialName,
        bool showSaveMode = false,
        PageBookmarkSaveMode initialSaveMode = PageBookmarkSaveMode.View)
    {
        Title = title;
        Width = 360;
        Height = showSaveMode ? 190 : 150;
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

        if (showSaveMode)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Save",
                Margin = new Thickness(0, 10, 0, 4),
            });

            var modeRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
            };
            _viewMode = new RadioButton
            {
                Content = "View",
                GroupName = "BookmarkSaveMode",
                Margin = new Thickness(0, 0, 16, 0),
                IsChecked = initialSaveMode != PageBookmarkSaveMode.CropImage,
            };
            _cropImageMode = new RadioButton
            {
                Content = "Crop image",
                GroupName = "BookmarkSaveMode",
                IsChecked = initialSaveMode == PageBookmarkSaveMode.CropImage,
            };
            modeRow.Children.Add(_viewMode);
            modeRow.Children.Add(_cropImageMode);
            stack.Children.Add(modeRow);
        }

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
        SaveMode = _cropImageMode?.IsChecked == true
            ? PageBookmarkSaveMode.CropImage
            : PageBookmarkSaveMode.View;
        DialogResult = true;
    }
}
