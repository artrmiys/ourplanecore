using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OurPlaneCore.Controls;

public sealed class PageSetupWindow : Window
{
    private readonly TextBox _pageNameBox;
    private readonly TextBox _scaleBox;
    private readonly TextBlock _pageCounter;
    private readonly TextBlock _statusText;
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private string _pageFolder = "";
    private int _pageIndex = -1;
    private int _selectRequestVersion;

    public event EventHandler? ApplyRequested;
    public event EventHandler<PageSetupNavigateEventArgs>? NavigateRequested;

    public string PageName => _pageNameBox.Text.Trim();
    public string ScaleText => _scaleBox.Text.Trim();

    public PageSetupWindow()
    {
        Title = "Page Setup";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        ShowInTaskbar = false;

        var root = new Border
        {
            Padding = new Thickness(16),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };
        root.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
        root.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        Content = root;

        var panel = new StackPanel();
        root.Child = panel;

        _pageCounter = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 12,
        };
        _pageCounter.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        DockPanel.SetDock(_pageCounter, Dock.Top);
        panel.Children.Add(_pageCounter);

        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(fields);

        AddLabel(fields, "Page", 0);
        _pageNameBox = AddTextBox(fields, 0);

        AddLabel(fields, "Scale", 1);
        _scaleBox = AddTextBox(fields, 1);
        _scaleBox.ToolTip = "Example: 1/8\" = 1'0\" or 1:96";

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        _prevButton = AddButton(buttons, "<", 48, primary: false);
        var okButton = AddButton(buttons, "OK", 76, primary: true);
        _nextButton = AddButton(buttons, ">", 48, primary: false);
        okButton.IsDefault = true;

        _statusText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
        };
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        DockPanel.SetDock(_statusText, Dock.Bottom);
        panel.Children.Add(_statusText);

        _prevButton.Click += (_, _) => NavigateRequested?.Invoke(this, new PageSetupNavigateEventArgs(-1));
        _nextButton.Click += (_, _) => NavigateRequested?.Invoke(this, new PageSetupNavigateEventArgs(1));
        okButton.Click += (_, _) => ApplyRequested?.Invoke(this, EventArgs.Empty);
        PreviewKeyDown += PageSetupWindow_PreviewKeyDown;
        Loaded += (_, _) => SelectPageNameText(force: false);
    }

    public void SetPage(
        string pageName,
        string scaleText,
        int pageIndex,
        int pageCount,
        string pageFolder = "",
        bool selectName = false)
    {
        bool samePage = IsSamePage(pageFolder, pageIndex);
        bool preservePageNameEdit = samePage && _pageNameBox.IsKeyboardFocusWithin;
        bool preserveScaleEdit = samePage && _scaleBox.IsKeyboardFocusWithin;

        if (!preservePageNameEdit)
            _pageNameBox.Text = pageName;
        if (!preserveScaleEdit)
            _scaleBox.Text = scaleText;

        _pageFolder = pageFolder ?? "";
        _pageIndex = pageIndex;
        _pageCounter.Text = pageCount > 0
            ? $"Sheet {Math.Clamp(pageIndex + 1, 1, pageCount)} of {pageCount}"
            : "No sheet";
        _prevButton.IsEnabled = pageIndex > 0;
        _nextButton.IsEnabled = pageIndex >= 0 && pageIndex < pageCount - 1;
        _statusText.Text = "";

        if (selectName && IsVisible && !preservePageNameEdit && !preserveScaleEdit)
            SelectPageNameText(force: true);
    }

    public void ShowStatus(string message) =>
        _statusText.Text = message;

    private static void AddLabel(Grid grid, string text, int row)
    {
        var label = new TextBlock
        {
            Text = text,
            Width = 54,
            FontSize = 12,
            Margin = new Thickness(0, row == 0 ? 0 : 8, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "ControlForegroundBrush");
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);
    }

    private static TextBox AddTextBox(Grid grid, int row)
    {
        var textBox = new TextBox
        {
            Margin = new Thickness(0, row == 0 ? 0 : 8, 0, 0),
            MinWidth = 220,
            FontSize = 12,
            MinHeight = 26,
            Padding = new Thickness(5, 3, 5, 3),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        textBox.SetResourceReference(Control.BackgroundProperty, "ControlBackgroundBrush");
        textBox.SetResourceReference(Control.ForegroundProperty, "ControlForegroundBrush");
        textBox.SetResourceReference(Control.BorderBrushProperty, "ControlBorderBrush");
        Grid.SetRow(textBox, row);
        Grid.SetColumn(textBox, 1);
        grid.Children.Add(textBox);
        return textBox;
    }

    private bool IsSamePage(string pageFolder, int pageIndex)
    {
        if (!string.IsNullOrWhiteSpace(pageFolder) && !string.IsNullOrWhiteSpace(_pageFolder))
            return string.Equals(pageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase);

        return pageIndex >= 0 && _pageIndex >= 0 && pageIndex == _pageIndex;
    }

    private static Button AddButton(Panel panel, string content, double width, bool primary)
    {
        var button = new Button
        {
            Content = content,
            Width = width,
            Margin = new Thickness(4, 0, 0, 0),
            Style = Application.Current?.TryFindResource(primary ? "ManagerPrimaryButton" : "ManagerButton") as Style,
        };
        panel.Children.Add(button);
        return button;
    }

    private void PageSetupWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            if (_pageNameBox.IsKeyboardFocusWithin)
            {
                SelectScaleText(force: true);
            }
            else if (_scaleBox.IsKeyboardFocusWithin)
            {
                NavigateRequested?.Invoke(this, new PageSetupNavigateEventArgs(1));
            }
            else
            {
                SelectPageNameText(force: true);
            }

            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private void SelectPageNameText(bool force)
    {
        int requestVersion = ++_selectRequestVersion;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible || requestVersion != _selectRequestVersion)
                return;

            if (!force && UserAlreadyPlacedTextFocus(_pageNameBox))
                return;

            _pageNameBox.Focus();
            Keyboard.Focus(_pageNameBox);
            _pageNameBox.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void SelectScaleText(bool force)
    {
        int requestVersion = ++_selectRequestVersion;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible || requestVersion != _selectRequestVersion)
                return;

            if (!force && UserAlreadyPlacedTextFocus(_scaleBox))
                return;

            _scaleBox.Focus();
            Keyboard.Focus(_scaleBox);
            _scaleBox.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private bool UserAlreadyPlacedTextFocus(TextBox target)
    {
        TextBox? focusedTextBox =
            _pageNameBox.IsKeyboardFocusWithin ? _pageNameBox :
            _scaleBox.IsKeyboardFocusWithin ? _scaleBox :
            null;
        if (focusedTextBox == null)
            return false;

        if (!ReferenceEquals(focusedTextBox, target))
            return true;

        return !IsWholeTextSelected(focusedTextBox);
    }

    private static bool IsWholeTextSelected(TextBox textBox) =>
        textBox.Text.Length > 0 &&
        textBox.SelectionStart == 0 &&
        textBox.SelectionLength >= textBox.Text.Length;
}

public sealed class PageSetupNavigateEventArgs(int direction) : EventArgs
{
    public int Direction { get; } = direction;
}
