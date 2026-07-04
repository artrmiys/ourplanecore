using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OurPlaneCore;

// Vertical quick-command strips on both sides of the PDF viewport.
// Buttons execute command-palette ids; the gear button at the bottom of each
// strip opens SideStripConfigDialog so the user can pick any commands.
public partial class MainWindow
{
    private const string SideStripSeparatorId = "-";

    private static readonly string[] LeftStripDefaultCommands =
    [
        "pages.openTabs",
        "pages.detach",
        "pages.tileM2",
        SideStripSeparatorId,
        "pages.nameScaleSetup",
        SideStripSeparatorId,
        "pages.sortArchStruct",
        "pages.sortSuffix",
        "pages.autoFolders",
        SideStripSeparatorId,
        "pages.collapseTree",
        "pages.expandTree",
    ];

    private static readonly string[] RightStripDefaultCommands =
    [
        "takeoffs.newItem",
        "takeoffs.newFolder",
        SideStripSeparatorId,
        "takeoffs.activeRecord",
        "takeoffs.activeFind",
        "takeoffs.activeProperties",
        SideStripSeparatorId,
        "edit.mergeMeasurements",
        "edit.splitMeasurements",
        SideStripSeparatorId,
        "takeoffs.autoTree",
        "takeoffs.fromPages",
        SideStripSeparatorId,
        "file.exportCurrentExcel",
    ];

    private static readonly Dictionary<string, string> SideStripIconKeys = new()
    {
        ["pages.openTabs"] = "IconOpenTabs",
        ["pages.detach"] = "IconDetachWin",
        ["pages.tileM2"] = "IconTileGrid",
        ["pages.nameScaleSetup"] = "IconRename",
        ["pages.newFolder"] = "IconFolderPlus",
        ["pages.autoFolders"] = "IconFolderAuto",
        ["pages.collapseTree"] = "IconCollapseAll",
        ["pages.expandTree"] = "IconExpandAll",
        ["pages.blankSheet"] = "IconBlankSheet",
        ["pages.autoName"] = "IconTag",
        ["pages.autoScale"] = "IconRuler",
        ["takeoffs.collapseTree"] = "IconCollapseAll",
        ["takeoffs.expandTree"] = "IconExpandAll",
        ["takeoffs.newItem"] = "IconBoxPlus",
        ["takeoffs.newFolder"] = "IconFolderPlus",
        ["takeoffs.activeRecord"] = "IconRecordDot",
        ["takeoffs.activeFind"] = "IconTarget",
        ["takeoffs.activeProperties"] = "IconSliders",
        ["edit.mergeMeasurements"] = "IconMerge",
        ["edit.splitMeasurements"] = "IconSplit",
        ["edit.copyMeasurements"] = "IconCopy",
        ["takeoffs.autoTree"] = "IconTreeAuto",
        ["takeoffs.fromPages"] = "IconPagesToTree",
        ["file.open"] = "IconFolderOpen",
        ["file.importPdf"] = "IconImport",
        ["file.exportPdf"] = "IconExport",
        ["view.fit"] = "IconLevel",
        ["tool.ruler"] = "IconRuler",
        ["tool.note"] = "IconNote",
    };

    private static readonly Dictionary<string, string> SideStripTextGlyphs = new()
    {
        ["pages.sortArchStruct"] = "A/S",
        ["pages.sortSuffix"] = "D/S",
        ["file.exportCurrentExcel"] = "XLS",
        ["file.exportExcel"] = "XLS",
        ["file.exportCsv"] = "CSV",
        ["file.exportTxt"] = "TXT",
        ["takeoffs.roofBase"] = "RF",
    };

    private void BuildSideCommandStrips()
    {
        RebuildSideCommandStrip(leftSide: true);
        RebuildSideCommandStrip(leftSide: false);
    }

    private void RebuildSideCommandStrip(bool leftSide)
    {
        var host = leftSide ? LeftCommandStripHost : RightCommandStripHost;
        if (host == null)
            return;

        var catalog = BuildSideStripCommandCatalog()
            .ToDictionary(info => info.Id, info => info, StringComparer.OrdinalIgnoreCase);

        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (string id in GetSideStripCommandIds(leftSide))
        {
            if (id == SideStripSeparatorId)
            {
                var separator = new Border { Height = 1, Margin = new Thickness(8, 5, 8, 5) };
                separator.SetResourceReference(Border.BackgroundProperty, "ControlBorderBrush");
                stack.Children.Add(separator);
                continue;
            }

            catalog.TryGetValue(id, out var info);
            string title = info?.Title ?? id;
            string tooltip = string.IsNullOrWhiteSpace(info?.Shortcut) ? title : $"{title} ({info.Shortcut})";
            if (!string.IsNullOrWhiteSpace(info?.Description))
                tooltip += "\n" + info.Description;

            var button = new Button
            {
                Style = (Style)FindResource("TopCommandButton"),
                Width = 18,
                Height = 18,
                Margin = new Thickness(4, 2, 4, 0),
                Padding = new Thickness(0),
                ToolTip = tooltip,
                Tag = id,
                Content = BuildSideStripButtonIcon(id, title),
            };
            string commandId = id;
            button.Click += (_, _) => ExecuteSideStripCommand(commandId);
            stack.Children.Add(button);
        }

        var gear = new Button
        {
            Style = (Style)FindResource("TopCommandButton"),
            Width = 18,
            Height = 18,
            Margin = new Thickness(4, 4, 4, 6),
            Padding = new Thickness(0),
            ToolTip = leftSide
                ? "Configure the left command strip"
                : "Configure the right command strip",
            Content = BuildSideStripIconPath("IconGear"),
        };
        gear.Click += (_, _) => ConfigureSideStrip(leftSide);

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(gear, Dock.Bottom);
        dock.Children.Add(gear);
        dock.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack,
        });

        host.Child = dock;
    }

    private List<string> GetSideStripCommandIds(bool leftSide)
    {
        var stored = leftSide ? _settings.LeftStripCommands : _settings.RightStripCommands;
        if (stored is { Count: > 0 })
            return [.. stored];
        return [.. leftSide ? LeftStripDefaultCommands : RightStripDefaultCommands];
    }

    private FrameworkElement BuildSideStripButtonIcon(string id, string title)
    {
        if (SideStripTextGlyphs.TryGetValue(id, out string? glyph))
            return BuildSideStripTextGlyph(glyph);

        if (SideStripIconKeys.TryGetValue(id, out string? iconKey) &&
            TryFindResource(iconKey) is Geometry)
        {
            return BuildSideStripIconPath(iconKey);
        }

        return BuildSideStripTextGlyph(SideStripInitials(title));
    }

    private Path BuildSideStripIconPath(string iconKey)
    {
        return new Path
        {
            Data = (Geometry)FindResource(iconKey),
            Style = (Style)FindResource("RibbonIcon"),
            Width = 10,
            Height = 10,
            StrokeThickness = 1.1,
            Margin = new Thickness(0),
        };
    }

    private static TextBlock BuildSideStripTextGlyph(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 6.5,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static string SideStripInitials(string title)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => char.IsLetterOrDigit(word[0]))
            .ToList();
        if (words.Count == 0)
            return "?";
        return string.Concat(words.Take(3).Select(word => char.ToUpperInvariant(word[0])));
    }

    private IReadOnlyList<SideStripCommandInfo> BuildSideStripCommandCatalog()
    {
        var items = new List<SideStripCommandInfo>
        {
            new("pages.openTabs", "Open Selected in Tabs", "Pages", "", "Open the selected Pages sheets as tabs."),
            new("pages.detach", "Detach to Windows", "Pages", "", "Open the selected Pages sheets in detached windows."),
            new("pages.tileM2", "Tile on Monitor 2", "Pages", "", "Tile the selected sheets on monitor 2."),
            new("pages.nameScaleSetup", "Name / Scale Setup", "Pages", "", "Open the floating page name / scale setup window."),
            new("pages.newFolder", "New Page Folder", "Pages", "", "Create a new folder under the selected Pages folder."),
            new("pages.collapseTree", "Collapse Pages Tree", "Pages", "", "Collapse all Pages nodes."),
            new("pages.expandTree", "Expand Pages Tree", "Pages", "", "Expand all Pages nodes."),
            new("takeoffs.collapseTree", "Collapse Takeoffs Tree", "Takeoffs", "", "Collapse all Takeoffs nodes."),
            new("takeoffs.expandTree", "Expand Takeoffs Tree", "Takeoffs", "", "Expand all Takeoffs nodes."),
            new("takeoffs.roofBase", "Roof Base from Area", "Takeoffs", "", "Create a 3D roof base from the selected area takeoff."),
        };

        try
        {
            foreach (var item in BuildCommandPaletteItems())
                items.Add(new SideStripCommandInfo(item.Id, item.Title, item.Group, item.Shortcut, item.Description));
        }
        catch
        {
            // Palette items need live viewport/job state; if that is not ready
            // yet the strip still works with the built-in entries above.
        }

        return items;
    }

    private void ExecuteSideStripCommand(string id)
    {
        switch (id)
        {
            case "pages.openTabs": BtnPagesOpenTabs_Click(this, new RoutedEventArgs()); break;
            case "pages.detach": BtnPagesDetach_Click(this, new RoutedEventArgs()); break;
            case "pages.tileM2": BtnPagesTileSecondMonitor_Click(this, new RoutedEventArgs()); break;
            case "pages.nameScaleSetup": BtnFloatingPageSetup_Click(this, new RoutedEventArgs()); break;
            case "pages.newFolder": BtnNewPageFolder_Click(this, new RoutedEventArgs()); break;
            case "pages.collapseTree": BtnCollapsePagesTree_Click(this, new RoutedEventArgs()); break;
            case "pages.expandTree": BtnExpandPagesTree_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.collapseTree": BtnCollapseTakeoffsTree_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.expandTree": BtnExpandTakeoffsTree_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.roofBase": Btn3dRfRoof_Click(this, new RoutedEventArgs()); break;
            default: ExecuteCommandPaletteItem(id); break;
        }
    }

    private void ConfigureSideStrip(bool leftSide)
    {
        var dialog = new SideStripConfigDialog(
            leftSide ? "Left Command Strip" : "Right Command Strip",
            BuildSideStripCommandCatalog(),
            GetSideStripCommandIds(leftSide),
            leftSide ? LeftStripDefaultCommands : RightStripDefaultCommands)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        if (leftSide)
            _settings.LeftStripCommands = [.. dialog.SelectedIds];
        else
            _settings.RightStripCommands = [.. dialog.SelectedIds];
        SaveAppSettings();
        RebuildSideCommandStrip(leftSide);
    }
}
