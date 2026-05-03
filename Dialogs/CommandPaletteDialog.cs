using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartTakeoffs.Controls;

public sealed record CommandPaletteItem(
    string Id,
    string Title,
    string Group,
    string Shortcut,
    string Description,
    bool CanExecute,
    string DisabledReason = "")
{
    public string DisplayText =>
        string.IsNullOrWhiteSpace(Shortcut)
            ? $"{Title}  -  {Group}{DisabledSuffix}"
            : $"{Title}  -  {Shortcut}  -  {Group}{DisabledSuffix}";

    public string SearchText => $"{Title} {Group} {Shortcut} {Description}";

    private string DisabledSuffix => CanExecute ? "" : "  (unavailable)";
}

public sealed class CommandPaletteDialog : Window
{
    private readonly IReadOnlyList<CommandPaletteItem> _items;
    private readonly TextBox _searchBox;
    private readonly ListBox _list;
    private readonly TextBlock _details;
    private readonly Button _runButton;

    public string SelectedCommandId { get; private set; } = "";

    public CommandPaletteDialog(IEnumerable<CommandPaletteItem> items)
    {
        _items = items.OrderBy(i => i.Group).ThenBy(i => i.Title).ToList();

        Title = "Command Palette";
        Width = 680;
        Height = 480;
        MinWidth = 520;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;

        _searchBox = new TextBox
        {
            MinHeight = 28,
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "Search commands",
        };
        DockPanel.SetDock(_searchBox, Dock.Top);
        root.Children.Add(_searchBox);

        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        _details = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        _details.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        footer.Children.Add(_details);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);

        _runButton = new Button
        {
            Content = "Run",
            MinWidth = 78,
            IsDefault = true,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 78,
            IsCancel = true,
        };
        buttons.Children.Add(_runButton);
        buttons.Children.Add(cancelButton);

        _list = new ListBox
        {
            DisplayMemberPath = nameof(CommandPaletteItem.DisplayText),
        };
        root.Children.Add(_list);

        _searchBox.TextChanged += (_, _) => ApplyFilter();
        _searchBox.PreviewKeyDown += SearchBox_PreviewKeyDown;
        _list.SelectionChanged += (_, _) => UpdateDetails();
        _list.MouseDoubleClick += (_, _) => AcceptSelected();
        _runButton.Click += (_, _) => AcceptSelected();
        Loaded += (_, _) =>
        {
            ApplyFilter();
            _searchBox.Focus();
        };
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            AcceptSelected();
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Down or Key.Up) || _list.Items.Count == 0)
            return;

        int current = _list.SelectedIndex < 0 ? 0 : _list.SelectedIndex;
        int next = e.Key == Key.Down
            ? Math.Min(_list.Items.Count - 1, current + 1)
            : Math.Max(0, current - 1);
        _list.SelectedIndex = next;
        _list.ScrollIntoView(_list.SelectedItem);
        e.Handled = true;
    }

    private void ApplyFilter()
    {
        string query = _searchBox.Text.Trim();
        var visible = string.IsNullOrWhiteSpace(query)
            ? _items
            : _items
                .Where(item => item.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        _list.ItemsSource = visible;
        _list.SelectedIndex = visible.Count > 0 ? 0 : -1;
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (_list.SelectedItem is not CommandPaletteItem item)
        {
            _details.Text = "No matching commands.";
            _runButton.IsEnabled = false;
            return;
        }

        string prefix = item.CanExecute
            ? item.Description
            : $"{item.Description} Unavailable: {item.DisabledReason}";
        _details.Text = prefix;
        _runButton.IsEnabled = item.CanExecute;
    }

    private void AcceptSelected()
    {
        if (_list.SelectedItem is not CommandPaletteItem item || !item.CanExecute)
            return;

        SelectedCommandId = item.Id;
        DialogResult = true;
    }
}
