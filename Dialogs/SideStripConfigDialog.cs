using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OurPlaneCore;

public sealed record SideStripCommandInfo(
    string Id,
    string Title,
    string Group,
    string Shortcut,
    string Description);

// Editor for one viewport side command strip: pick any commands from the
// catalog (same registry as the command palette), order them, add separators.
public sealed class SideStripConfigDialog : Window
{
    private const string SeparatorId = "-";
    private const string SeparatorDisplay = "──────────";

    private readonly IReadOnlyList<SideStripCommandInfo> _catalog;
    private readonly IReadOnlyList<string> _defaultIds;
    private readonly TextBox _searchBox;
    private readonly ListBox _availableList;
    private readonly ListBox _currentList;

    public List<string> SelectedIds { get; private set; } = [];

    public SideStripConfigDialog(
        string title,
        IReadOnlyList<SideStripCommandInfo> catalog,
        IReadOnlyList<string> currentIds,
        IReadOnlyList<string> defaultIds)
    {
        _catalog = catalog;
        _defaultIds = defaultIds;

        Title = title;
        Width = 640;
        Height = 520;
        MinWidth = 540;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

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

        var reset = new Button
        {
            Content = "Reset to Default",
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(0, 0, 12, 0),
        };
        reset.Click += (_, _) => LoadCurrent(_defaultIds);
        buttons.Children.Add(reset);

        var ok = new Button
        {
            Content = "OK",
            Width = 72,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);

        buttons.Children.Add(new Button
        {
            Content = "Cancel",
            Width = 72,
            IsCancel = true,
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(grid);

        // Left: all commands with search.
        var leftPanel = new DockPanel();
        Grid.SetColumn(leftPanel, 0);
        grid.Children.Add(leftPanel);

        var leftHeader = new TextBlock
        {
            Text = "All commands",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        DockPanel.SetDock(leftHeader, Dock.Top);
        leftPanel.Children.Add(leftHeader);

        _searchBox = new TextBox
        {
            MinHeight = 22,
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = "Filter commands by name or group",
        };
        _searchBox.TextChanged += (_, _) => FillAvailable();
        DockPanel.SetDock(_searchBox, Dock.Top);
        leftPanel.Children.Add(_searchBox);

        _availableList = new ListBox();
        _availableList.MouseDoubleClick += (_, _) => AddSelected();
        leftPanel.Children.Add(_availableList);

        // Middle: transfer / ordering buttons.
        var middle = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
        };
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);

        middle.Children.Add(MiddleButton("Add  →", "Add the selected command to the strip", AddSelected));
        middle.Children.Add(MiddleButton("Separator", "Add a separator line to the strip", AddSeparator));
        middle.Children.Add(MiddleButton("←  Remove", "Remove the selected entry from the strip", RemoveSelected));
        middle.Children.Add(MiddleButton("Up", "Move the selected entry up", () => MoveSelected(-1)));
        middle.Children.Add(MiddleButton("Down", "Move the selected entry down", () => MoveSelected(1)));

        // Right: current strip layout.
        var rightPanel = new DockPanel();
        Grid.SetColumn(rightPanel, 2);
        grid.Children.Add(rightPanel);

        var rightHeader = new TextBlock
        {
            Text = "Strip layout (top → bottom)",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        DockPanel.SetDock(rightHeader, Dock.Top);
        rightPanel.Children.Add(rightHeader);

        _currentList = new ListBox();
        _currentList.MouseDoubleClick += (_, _) => RemoveSelected();
        rightPanel.Children.Add(_currentList);

        FillAvailable();
        LoadCurrent(currentIds);
    }

    private static Button MiddleButton(string text, string tooltip, Action action)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 86,
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 6),
            ToolTip = tooltip,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void FillAvailable()
    {
        string filter = _searchBox.Text.Trim();
        var items = _catalog
            .OrderBy(info => info.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(info => info.Title, StringComparer.OrdinalIgnoreCase)
            .Where(info => filter.Length == 0 ||
                info.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                info.Group.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                info.Id.Contains(filter, StringComparison.OrdinalIgnoreCase));

        _availableList.Items.Clear();
        foreach (var info in items)
        {
            _availableList.Items.Add(new ListBoxItem
            {
                Content = $"{info.Group} · {info.Title}" +
                          (string.IsNullOrWhiteSpace(info.Shortcut) ? "" : $"  ({info.Shortcut})"),
                Tag = info.Id,
                ToolTip = string.IsNullOrWhiteSpace(info.Description) ? null : info.Description,
            });
        }
    }

    private void LoadCurrent(IEnumerable<string> ids)
    {
        _currentList.Items.Clear();
        foreach (string id in ids)
            _currentList.Items.Add(MakeCurrentItem(id));
    }

    private ListBoxItem MakeCurrentItem(string id)
    {
        if (id == SeparatorId)
            return new ListBoxItem { Content = SeparatorDisplay, Tag = SeparatorId };

        var info = _catalog.FirstOrDefault(entry =>
            string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        return new ListBoxItem
        {
            Content = info == null ? id : $"{info.Group} · {info.Title}",
            Tag = id,
            ToolTip = info?.Description,
        };
    }

    private void AddSelected()
    {
        if (_availableList.SelectedItem is not ListBoxItem { Tag: string id })
            return;

        bool alreadyPresent = _currentList.Items.OfType<ListBoxItem>()
            .Any(item => item.Tag is string existing &&
                 string.Equals(existing, id, StringComparison.OrdinalIgnoreCase));
        if (alreadyPresent)
            return;

        int index = _currentList.SelectedIndex >= 0 ? _currentList.SelectedIndex + 1 : _currentList.Items.Count;
        _currentList.Items.Insert(index, MakeCurrentItem(id));
        _currentList.SelectedIndex = index;
    }

    private void AddSeparator()
    {
        int index = _currentList.SelectedIndex >= 0 ? _currentList.SelectedIndex + 1 : _currentList.Items.Count;
        _currentList.Items.Insert(index, MakeCurrentItem(SeparatorId));
        _currentList.SelectedIndex = index;
    }

    private void RemoveSelected()
    {
        int index = _currentList.SelectedIndex;
        if (index < 0)
            return;
        _currentList.Items.RemoveAt(index);
        if (_currentList.Items.Count > 0)
            _currentList.SelectedIndex = Math.Min(index, _currentList.Items.Count - 1);
    }

    private void MoveSelected(int delta)
    {
        int index = _currentList.SelectedIndex;
        if (index < 0)
            return;
        int target = index + delta;
        if (target < 0 || target >= _currentList.Items.Count)
            return;

        var item = _currentList.Items[index];
        _currentList.Items.RemoveAt(index);
        _currentList.Items.Insert(target, item);
        _currentList.SelectedIndex = target;
    }

    private void Accept()
    {
        SelectedIds = _currentList.Items.OfType<ListBoxItem>()
            .Select(item => item.Tag as string)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();
        DialogResult = true;
    }
}
