using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OurPlaneCore.Controls;

public enum JobRootLocationKind
{
    Local,
    Cloud,
    Network,
}

public sealed record JobRootDescriptor(
    string Path,
    string DisplayName,
    string KindLabel,
    string StatusLabel,
    bool Exists);

public sealed class JobRootSelectorBar : ScrollViewer
{
    private readonly IReadOnlyList<string> _rootPaths;
    private readonly List<ToggleButton> _buttons = [];

    public event EventHandler? SelectionChanged;

    public string SelectedRootPath { get; private set; } = "";

    public JobRootSelectorBar(IReadOnlyList<string> rootPaths)
    {
        _rootPaths = rootPaths;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        MaxHeight = 82;
        Content = BuildButtonPanel();
        RefreshButtons();
    }

    private WrapPanel BuildButtonPanel()
    {
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };

        panel.Children.Add(CreateRootToggleButton(
            tag: "",
            content: CreateAllRootsButtonContent(),
            toolTip: "Show jobs from every configured folder"));

        foreach (string rootPath in _rootPaths)
        {
            JobRootDescriptor descriptor = DescribeJobRoot(rootPath);
            panel.Children.Add(CreateRootToggleButton(
                tag: NormalizePath(descriptor.Path),
                content: CreateRootButtonContent(descriptor),
                toolTip: descriptor.Path));
        }

        return panel;
    }

    private ToggleButton CreateRootToggleButton(string tag, object content, string toolTip)
    {
        var button = new ToggleButton
        {
            Tag = tag,
            Content = content,
            MinWidth = 124,
            MaxWidth = 220,
            Height = 50,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = toolTip,
        };
        button.SetResourceReference(Control.ForegroundProperty, "ControlForegroundBrush");
        button.SetResourceReference(Control.BackgroundProperty, "ControlBackgroundBrush");
        button.SetResourceReference(Control.BorderBrushProperty, "ControlBorderBrush");
        button.Click += (_, _) =>
        {
            SelectedRootPath = button.Tag as string ?? "";
            RefreshButtons();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        _buttons.Add(button);
        return button;
    }

    private FrameworkElement CreateAllRootsButtonContent()
    {
        var stack = CreateRootButtonStack();
        stack.Children.Add(new TextBlock
        {
            Text = "All Jobs",
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var details = new TextBlock
        {
            Text = $"{_rootPaths.Count} folders",
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        details.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        stack.Children.Add(details);
        return stack;
    }

    private static FrameworkElement CreateRootButtonContent(JobRootDescriptor descriptor)
    {
        var stack = CreateRootButtonStack();
        var row = new DockPanel { LastChildFill = true };
        var kind = new Border
        {
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = descriptor.KindLabel,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
            },
        };
        kind.SetResourceReference(Border.BackgroundProperty, "ControlHoverBackgroundBrush");
        kind.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        kind.BorderThickness = new Thickness(1);
        DockPanel.SetDock(kind, Dock.Right);
        row.Children.Add(kind);
        row.Children.Add(new TextBlock
        {
            Text = descriptor.DisplayName,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(row);

        var details = new TextBlock
        {
            Text = descriptor.Exists
                ? descriptor.Path
                : $"{descriptor.StatusLabel}: {descriptor.Path}",
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        details.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        stack.Children.Add(details);
        return stack;
    }

    private static StackPanel CreateRootButtonStack() =>
        new()
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
        };

    private void RefreshButtons()
    {
        foreach (ToggleButton button in _buttons)
        {
            string tag = button.Tag as string ?? "";
            button.IsChecked = string.Equals(tag, SelectedRootPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static JobRootDescriptor DescribeJobRoot(string rootPath)
    {
        string path = NormalizePath(rootPath);
        JobRootLocationKind kind = ClassifyJobRootPath(path);
        bool exists = Directory.Exists(path);
        return new JobRootDescriptor(
            path,
            BuildJobRootDisplayName(path),
            kind.ToString(),
            exists ? "Ready" : "Missing",
            exists);
    }

    public static JobRootLocationKind ClassifyJobRootPath(string rootPath)
    {
        string path = rootPath.Trim();
        if (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
            return JobRootLocationKind.Network;

        string lower = path.ToLowerInvariant();
        string[] cloudTokens =
        [
            "onedrive",
            "dropbox",
            "google drive",
            "googledrive",
            "icloud",
            "sharepoint",
            "box sync",
            "box drive",
        ];
        return cloudTokens.Any(token => lower.Contains(token, StringComparison.OrdinalIgnoreCase))
            ? JobRootLocationKind.Cloud
            : JobRootLocationKind.Local;
    }

    public static string BuildJobRootDisplayName(string rootPath)
    {
        string path = NormalizePath(rootPath);
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        string? root = Path.GetPathRoot(path);
        return string.IsNullOrWhiteSpace(root)
            ? path
            : root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
