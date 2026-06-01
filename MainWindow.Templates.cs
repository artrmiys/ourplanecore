using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private TakeoffTemplateConfig _takeoffTemplateConfig = TakeoffTemplateConfig.BuildDefault();
    private TreeView? _templateTree;
    private TreeView? _settingsTemplateTree;
    private TextBlock? _templateStatusText;
    private TextBlock? _settingsTemplateStatusText;
    private ToggleButton? _templatesTabDockToggle;
    private TabItem? _templatesTab;
    private FrameworkElement? _templatePanel;
    private GridLength _templatesDockRowHeight = new(190);
    private bool _syncingTemplatesDockToggle;

    private Grid? _templatesRightHostGrid;
    private GridSplitter? _templatesRightSplitter;
    private DockPanel? _templatesRightDock;
    private ContentControl? _templatesRightDockHost;
    private RowDefinition? _templatesRightSplitterRow;
    private RowDefinition? _templatesRightDockRow;
    private ToggleButton? _templatesDockReturnToggle;

    private void InitializeTemplatesTab()
    {
        if (_rightWorkspaceTabs == null)
            return;

        _templatePanel = BuildTakeoffTemplateEditorPanel(settingsMode: false);
        _templatesTab = new TabItem
        {
            Header = BuildTemplatesTabHeader(),
            Content = _templatePanel,
        };
        _rightWorkspaceTabs.Items.Add(_templatesTab);

        InstallTemplatesRightDock();
        ReloadTakeoffTemplateConfig();
    }

    private void ReloadTakeoffTemplateConfig()
    {
        _takeoffTemplateConfig = TakeoffTemplateStore.ResolveConfig(_currentJob).Clone();
        RefreshTakeoffTemplateEditors();
    }

    private FrameworkElement BuildTakeoffTemplateEditorPanel(bool settingsMode)
    {
        var tree = new TreeView
        {
            Margin = new Thickness(0, 4, 0, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = TryFindResource("ControlBorderBrush") as Brush,
        };
        tree.SelectedItemChanged += (_, _) => UpdateTakeoffTemplateEditorButtons();

        TextBlock status = TemplateStatusText();
        if (settingsMode)
        {
            _settingsTemplateTree = tree;
            _settingsTemplateStatusText = status;
        }
        else
        {
            _templateTree = tree;
            _templateStatusText = status;
        }

        FrameworkElement toolbar = BuildTakeoffTemplateToolbar(tree, settingsMode);

        var panel = new DockPanel { Margin = new Thickness(settingsMode ? 2 : 4) };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(status, Dock.Top);
        panel.Children.Add(toolbar);
        panel.Children.Add(status);
        panel.Children.Add(tree);
        return panel;
    }

    private static TextBlock TemplateStatusText() =>
        new()
        {
            Text = "Template presets are loading.",
            FontSize = 11,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2),
        };

    private FrameworkElement BuildTakeoffTemplateToolbar(TreeView tree, bool settingsMode)
    {
        var toolbar = new StackPanel { Margin = new Thickness(0, 0, 0, 3) };
        var createRow = TemplateToolbarRow();
        var editRow = TemplateToolbarRow();

        if (!settingsMode)
            createRow.Children.Add(TemplateButton("Create", (_, _) => CreateTakeoffFromTemplateSelection(tree), "Create a new takeoff item from the selected template item", primary: true, minWidth: 48));
        createRow.Children.Add(TemplateButton("Folder", (_, _) => AddTemplateFolder(tree), "Add a folder to the template tree"));
        createRow.Children.Add(TemplateButton("Line", (_, _) => AddTemplateItem(tree, "line"), "Add a Line preset"));
        createRow.Children.Add(TemplateButton("Area", (_, _) => AddTemplateItem(tree, "area"), "Add an Area preset"));
        createRow.Children.Add(TemplateButton("Count", (_, _) => AddTemplateItem(tree, "point"), "Add a Count preset"));

        editRow.Children.Add(TemplateButton("Edit", (_, _) => EditTemplateNode(tree), "Edit the selected template node"));
        editRow.Children.Add(TemplateButton("Dup", (_, _) => DuplicateTemplateNode(tree), "Duplicate the selected template node"));
        editRow.Children.Add(TemplateButton("Del", (_, _) => DeleteTemplateNode(tree), "Delete the selected template node"));
        editRow.Children.Add(TemplateButton("Reset", (_, _) => ResetTakeoffTemplateToDefault(), "Reset this template tree to the built-in presets"));

        toolbar.Children.Add(createRow);
        toolbar.Children.Add(editRow);

        if (settingsMode)
        {
            var saveRow = TemplateToolbarRow();
            saveRow.Children.Add(TemplateButton("Global", (_, _) => SaveTakeoffTemplateGlobal(), "Save this template tree as the global default", primary: true, minWidth: 48));
            saveRow.Children.Add(TemplateButton("Job", (_, _) => SaveTakeoffTemplateJob(), "Save this template tree as a per-job override"));
            saveRow.Children.Add(TemplateButton("Clear", (_, _) => ClearTakeoffTemplateJobOverride(), "Return this job to the global/default template tree"));
            toolbar.Children.Add(saveRow);
        }

        return toolbar;
    }

    private static WrapPanel TemplateToolbarRow() =>
        new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 1),
        };

    private Button TemplateButton(string content, RoutedEventHandler handler, string tooltip, bool primary = false, double minWidth = 40)
    {
        var button = new Button
        {
            Content = content,
            ToolTip = tooltip,
            Height = 20,
            MinWidth = minWidth,
            Margin = new Thickness(0, 0, 3, 2),
            Padding = new Thickness(4, 1, 4, 1),
            FontSize = 10,
            Style = TryFindResource(primary ? "ManagerPrimaryButton" : "ManagerButton") as Style,
        };
        button.Click += handler;
        return button;
    }

    private FrameworkElement BuildTemplatesTabHeader()
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(new TextBlock
        {
            Text = "Templates",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        });

        _templatesTabDockToggle = CreateTemplatesDockToggle("Pop Templates out into a panel docked below Takeoffs");
        header.Children.Add(_templatesTabDockToggle);
        return header;
    }

    private ToggleButton CreateTemplatesDockToggle(string tooltip)
    {
        var toggle = new ToggleButton
        {
            Style = (Style)FindResource("BookmarkDockToggleButton"),
            ToolTip = tooltip,
        };
        toggle.Checked += TemplatesDockToggle_Changed;
        toggle.Unchecked += TemplatesDockToggle_Changed;
        return toggle;
    }

    private void InstallTemplatesRightDock()
    {
        if (_rightWorkspaceTabs == null || _rightWorkspaceTabs.Parent is not Panel hostPanel)
            return;

        int idx = hostPanel.Children.IndexOf(_rightWorkspaceTabs);
        hostPanel.Children.Remove(_rightWorkspaceTabs);

        _templatesRightSplitterRow = new RowDefinition { Height = new GridLength(0) };
        _templatesRightDockRow = new RowDefinition { Height = new GridLength(0), MinHeight = 0 };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(_templatesRightSplitterRow);
        grid.RowDefinitions.Add(_templatesRightDockRow);

        Grid.SetRow(_rightWorkspaceTabs, 0);
        grid.Children.Add(_rightWorkspaceTabs);

        _templatesRightSplitter = new GridSplitter
        {
            Height = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            Visibility = Visibility.Collapsed,
        };
        _templatesRightSplitter.SetResourceReference(Control.BackgroundProperty, "SplitterBrush");
        Grid.SetRow(_templatesRightSplitter, 1);
        grid.Children.Add(_templatesRightSplitter);

        _templatesRightDock = new DockPanel { MinHeight = 120, Visibility = Visibility.Collapsed };
        _templatesRightDock.SetResourceReference(Panel.BackgroundProperty, "PanelBackgroundBrush");

        var headerBorder = new Border { Style = (Style)FindResource("PanelHeaderBorder") };
        DockPanel.SetDock(headerBorder, Dock.Top);
        var headerDock = new DockPanel { LastChildFill = true };
        _templatesDockReturnToggle = new ToggleButton
        {
            Style = (Style)FindResource("BookmarkDockToggleButton"),
            Margin = new Thickness(2, 0, 0, 0),
            IsChecked = true,
            ToolTip = "Return Templates to the tab list",
        };
        _templatesDockReturnToggle.Checked += TemplatesDockToggle_Changed;
        _templatesDockReturnToggle.Unchecked += TemplatesDockToggle_Changed;
        DockPanel.SetDock(_templatesDockReturnToggle, Dock.Right);
        headerDock.Children.Add(_templatesDockReturnToggle);
        headerDock.Children.Add(new TextBlock
        {
            Text = "Templates",
            Style = (Style)FindResource("PanelHeader"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        headerBorder.Child = headerDock;
        _templatesRightDock.Children.Add(headerBorder);

        _templatesRightDockHost = new ContentControl();
        _templatesRightDock.Children.Add(_templatesRightDockHost);

        Grid.SetRow(_templatesRightDock, 2);
        grid.Children.Add(_templatesRightDock);

        _templatesRightHostGrid = grid;
        hostPanel.Children.Insert(idx, grid);
    }

    private void TemplatesDockToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingTemplatesDockToggle || sender is not ToggleButton toggle)
            return;

        ApplyTemplatesDockMode(toggle.IsChecked == true);
    }

    private void ApplyTemplatesDockMode(bool docked)
    {
        if (_templatesTab == null || _templatePanel == null || _rightWorkspaceTabs == null
            || _templatesRightDock == null || _templatesRightDockHost == null
            || _templatesRightSplitter == null || _templatesRightSplitterRow == null
            || _templatesRightDockRow == null)
            return;

        SetTemplatesDockToggleState(docked);

        if (docked)
        {
            if (_rightWorkspaceTabs.SelectedItem == _templatesTab)
                _rightWorkspaceTabs.SelectedIndex = 0;

            if (_rightWorkspaceTabs.Items.Contains(_templatesTab))
            {
                _templatesTab.Content = null;
                _rightWorkspaceTabs.Items.Remove(_templatesTab);
            }

            _templatesRightDockHost.Content = _templatePanel;
            _templatesRightSplitter.Visibility = Visibility.Visible;
            _templatesRightDock.Visibility = Visibility.Visible;
            _templatesRightSplitterRow.Height = new GridLength(4);
            _templatesRightDockRow.MinHeight = 120;
            _templatesRightDockRow.Height = _templatesDockRowHeight.Value > 0
                ? _templatesDockRowHeight
                : new GridLength(190);
            TxtStatus.Text = "Templates docked below Takeoffs.";
            return;
        }

        if (_templatesRightDockRow.ActualHeight >= 80)
            _templatesDockRowHeight = new GridLength(_templatesRightDockRow.ActualHeight);

        _templatesRightDockHost.Content = null;
        if (!_rightWorkspaceTabs.Items.Contains(_templatesTab))
        {
            _templatesTab.Content = _templatePanel;
            _rightWorkspaceTabs.Items.Add(_templatesTab);
        }
        _rightWorkspaceTabs.SelectedItem = _templatesTab;

        _templatesRightDock.Visibility = Visibility.Collapsed;
        _templatesRightSplitter.Visibility = Visibility.Collapsed;
        _templatesRightSplitterRow.Height = new GridLength(0);
        _templatesRightDockRow.MinHeight = 0;
        _templatesRightDockRow.Height = new GridLength(0);
        TxtStatus.Text = "Templates returned to the Takeoffs tabs.";
    }

    private void SetTemplatesDockToggleState(bool docked)
    {
        _syncingTemplatesDockToggle = true;
        try
        {
            if (_templatesTabDockToggle != null)
                _templatesTabDockToggle.IsChecked = docked;
            if (_templatesDockReturnToggle != null)
                _templatesDockReturnToggle.IsChecked = docked;
        }
        finally
        {
            _syncingTemplatesDockToggle = false;
        }
    }

    private void RefreshTakeoffTemplateEditors(string? selectedNodeId = null)
    {
        RefreshTakeoffTemplateTree(_templateTree, selectedNodeId);
        RefreshTakeoffTemplateTree(_settingsTemplateTree, selectedNodeId);
        RefreshTakeoffTemplateStatus();
        UpdateTakeoffTemplateEditorButtons();
    }

    private void RefreshTakeoffTemplateTree(TreeView? tree, string? selectedNodeId)
    {
        if (tree == null)
            return;

        tree.Items.Clear();
        bool allowCreate = ReferenceEquals(tree, _templateTree);
        foreach (TakeoffTemplateNode root in TemplateRoots())
            tree.Items.Add(BuildTemplateTreeItem(root, tree, allowCreate));

        if (!string.IsNullOrWhiteSpace(selectedNodeId))
            SelectTemplateTreeItemById(tree, selectedNodeId);
    }

    private List<TakeoffTemplateNode> TemplateRoots() =>
        _takeoffTemplateConfig.Template.Roots;

    private TreeViewItem BuildTemplateTreeItem(TakeoffTemplateNode node, TreeView ownerTree, bool allowCreate)
    {
        var item = new TreeViewItem
        {
            Header = BuildTemplateNodeHeader(node),
            Tag = node,
            IsExpanded = true,
        };
        item.MouseDoubleClick += (_, e) =>
        {
            if (allowCreate && !node.IsFolder)
            {
                CreateTakeoffFromTemplateNode(node);
                e.Handled = true;
            }
        };
        item.ContextMenu = BuildTemplateNodeContextMenu(node, ownerTree, allowCreate);
        foreach (TakeoffTemplateNode child in node.Children)
            item.Items.Add(BuildTemplateTreeItem(child, ownerTree, allowCreate));
        return item;
    }

    private FrameworkElement BuildTemplateNodeHeader(TakeoffTemplateNode node)
    {
        var panel = new DockPanel { LastChildFill = true };
        if (node.IsFolder)
        {
            panel.Children.Add(new TextBlock
            {
                Text = node.Name,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return panel;
        }

        var swatch = new Border
        {
            Width = 12,
            Height = 12,
            Margin = new Thickness(0, 0, 6, 0),
            Background = BrushFromHex(node.Color, Brushes.Gray),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(swatch, Dock.Left);
        panel.Children.Add(swatch);

        var type = new TextBlock
        {
            Text = MeasurementTypeTitle(node.MeasurementType),
            FontSize = 10,
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(type, Dock.Right);
        panel.Children.Add(type);

        panel.Children.Add(new TextBlock
        {
            Text = node.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return panel;
    }

    private ContextMenu BuildTemplateNodeContextMenu(TakeoffTemplateNode node, TreeView ownerTree, bool allowCreate)
    {
        var menu = new ContextMenu();
        if (allowCreate && !node.IsFolder)
            menu.Items.Add(TemplateMenuItem("Create New Takeoff", () => CreateTakeoffFromTemplateNode(node)));
        menu.Items.Add(TemplateMenuItem("Add Folder", () => AddTemplateFolder(ownerTree)));
        menu.Items.Add(TemplateMenuItem("Add Line", () => AddTemplateItem(ownerTree, "line")));
        menu.Items.Add(TemplateMenuItem("Add Area", () => AddTemplateItem(ownerTree, "area")));
        menu.Items.Add(TemplateMenuItem("Add Count", () => AddTemplateItem(ownerTree, "point")));
        menu.Items.Add(new Separator());
        menu.Items.Add(TemplateMenuItem("Edit", () => EditTemplateNodeByReference(node)));
        menu.Items.Add(TemplateMenuItem("Duplicate", () => DuplicateTemplateNodeByReference(node)));
        menu.Items.Add(TemplateMenuItem("Delete", () => DeleteTemplateNodeByReference(node)));
        return menu;
    }

    private static MenuItem TemplateMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void RefreshTakeoffTemplateStatus()
    {
        int itemCount = CountTemplateItems(TemplateRoots(), countFolders: false);
        int folderCount = CountTemplateItems(TemplateRoots(), countFolders: true) - itemCount;
        string scope = _currentJob != null && TakeoffTemplateStore.LoadJobOverride(_currentJob) != null
            ? "this job override"
            : "global/default";
        string text = $"{folderCount} folder(s), {itemCount} preset item(s). Edits save to {scope}. Double-click an item to create a new takeoff.";
        if (_templateStatusText != null)
            _templateStatusText.Text = text;
        if (_settingsTemplateStatusText != null)
            _settingsTemplateStatusText.Text = text;
    }

    private void UpdateTakeoffTemplateEditorButtons()
    {
        // Toolbars are intentionally always enabled. Commands report a short
        // status if the current selection cannot be used for that operation.
    }

    private static int CountTemplateItems(IEnumerable<TakeoffTemplateNode> nodes, bool countFolders)
    {
        int count = 0;
        foreach (TakeoffTemplateNode node in nodes)
        {
            if (countFolders || !node.IsFolder)
                count++;
            count += CountTemplateItems(node.Children, countFolders);
        }
        return count;
    }

    private TakeoffTemplateNode? SelectedTemplateNode(TreeView? tree) =>
        (tree?.SelectedItem as TreeViewItem)?.Tag as TakeoffTemplateNode;

    private void AddTemplateFolder(TreeView? tree)
    {
        string? name = ShowInputDialog("Folder name:", "New Folder", "Add Template Folder");
        if (string.IsNullOrWhiteSpace(name))
            return;

        var node = new TakeoffTemplateNode
        {
            Name = name.Trim(),
            IsFolder = true,
        };
        InsertTemplateNode(tree, node);
        PersistTakeoffTemplateEditorChange($"Template folder added: {node.Name}.", node.Id);
    }

    private void AddTemplateItem(TreeView? tree, string measurementType)
    {
        string type = OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType);
        var dialog = new NewItemDialog(
            type,
            DefaultTemplateItemName(type),
            lockType: true,
            defaultColor: RandomTakeoffColor(_viewport.ActiveColor),
            defaultCountSymbol: _newCountSymbol)
        {
            Owner = this,
            Title = "Add Template Preset",
        };
        if (dialog.ShowDialog() != true)
            return;

        var node = new TakeoffTemplateNode
        {
            Name = dialog.ItemName,
            IsFolder = false,
            MeasurementType = dialog.ItemType,
            Color = dialog.ItemColor,
            CountSymbol = CountDisplaySymbol.Normalize(dialog.ItemCountSymbol),
        };
        InsertTemplateNode(tree, node);
        PersistTakeoffTemplateEditorChange($"Template preset added: {node.Name}.", node.Id);
    }

    private static string DefaultTemplateItemName(string measurementType) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "area" => "Area Preset",
            "point" => "Count Preset",
            _ => "Line Preset",
        };

    private void InsertTemplateNode(TreeView? tree, TakeoffTemplateNode node)
    {
        TakeoffTemplateNode? selected = SelectedTemplateNode(tree);
        if (selected == null)
        {
            TemplateRoots().Add(node);
            return;
        }

        if (selected.IsFolder)
        {
            selected.Children.Add(node);
            return;
        }

        if (TryFindTemplateParentList(selected, out List<TakeoffTemplateNode> siblings, out _))
        {
            int index = siblings.IndexOf(selected);
            siblings.Insert(index >= 0 ? index + 1 : siblings.Count, node);
            return;
        }

        TemplateRoots().Add(node);
    }

    private void EditTemplateNode(TreeView? tree)
    {
        TakeoffTemplateNode? node = SelectedTemplateNode(tree);
        if (node == null)
        {
            TxtStatus.Text = "Select a template folder or preset first.";
            return;
        }

        EditTemplateNodeByReference(node);
    }

    private void EditTemplateNodeByReference(TakeoffTemplateNode node)
    {
        if (node.IsFolder)
        {
            string? name = ShowInputDialog("Folder name:", node.Name, "Edit Template Folder");
            if (string.IsNullOrWhiteSpace(name))
                return;
            node.Name = name.Trim();
            PersistTakeoffTemplateEditorChange($"Template folder updated: {node.Name}.", node.Id);
            return;
        }

        var dialog = new NewItemDialog(
            node.MeasurementType,
            node.Name,
            lockType: false,
            defaultColor: node.Color,
            defaultCountSymbol: node.CountSymbol)
        {
            Owner = this,
            Title = "Edit Template Preset",
        };
        if (dialog.ShowDialog() != true)
            return;

        node.Name = dialog.ItemName;
        node.MeasurementType = dialog.ItemType;
        node.Color = dialog.ItemColor;
        node.CountSymbol = CountDisplaySymbol.Normalize(dialog.ItemCountSymbol);
        PersistTakeoffTemplateEditorChange($"Template preset updated: {node.Name}.", node.Id);
    }

    private void DuplicateTemplateNode(TreeView? tree)
    {
        TakeoffTemplateNode? node = SelectedTemplateNode(tree);
        if (node == null)
        {
            TxtStatus.Text = "Select a template node first.";
            return;
        }

        DuplicateTemplateNodeByReference(node);
    }

    private void DuplicateTemplateNodeByReference(TakeoffTemplateNode node)
    {
        if (!TryFindTemplateParentList(node, out List<TakeoffTemplateNode> siblings, out _))
            return;

        TakeoffTemplateNode copy = node.Clone();
        ReassignTemplateNodeIds(copy);
        int index = siblings.IndexOf(node);
        siblings.Insert(index >= 0 ? index + 1 : siblings.Count, copy);
        PersistTakeoffTemplateEditorChange($"Template node duplicated: {copy.Name}.", copy.Id);
    }

    private static void ReassignTemplateNodeIds(TakeoffTemplateNode node)
    {
        node.Id = Guid.NewGuid().ToString("N");
        foreach (TakeoffTemplateNode child in node.Children)
            ReassignTemplateNodeIds(child);
    }

    private void DeleteTemplateNode(TreeView? tree)
    {
        TakeoffTemplateNode? node = SelectedTemplateNode(tree);
        if (node == null)
        {
            TxtStatus.Text = "Select a template node first.";
            return;
        }

        DeleteTemplateNodeByReference(node);
    }

    private void DeleteTemplateNodeByReference(TakeoffTemplateNode node)
    {
        var confirm = MessageBox.Show(
            $"Delete template node '{node.Name}'?",
            "Delete Template Node",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        if (TryFindTemplateParentList(node, out List<TakeoffTemplateNode> siblings, out _))
            siblings.Remove(node);
        PersistTakeoffTemplateEditorChange($"Template node deleted: {node.Name}.");
    }

    private bool TryFindTemplateParentList(
        TakeoffTemplateNode target,
        out List<TakeoffTemplateNode> siblings,
        out TakeoffTemplateNode? parent)
    {
        return TryFindTemplateParentList(TemplateRoots(), null, target, out siblings, out parent);
    }

    private static bool TryFindTemplateParentList(
        List<TakeoffTemplateNode> nodes,
        TakeoffTemplateNode? currentParent,
        TakeoffTemplateNode target,
        out List<TakeoffTemplateNode> siblings,
        out TakeoffTemplateNode? parent)
    {
        if (nodes.Contains(target))
        {
            siblings = nodes;
            parent = currentParent;
            return true;
        }

        foreach (TakeoffTemplateNode node in nodes)
        {
            if (TryFindTemplateParentList(node.Children, node, target, out siblings, out parent))
                return true;
        }

        siblings = [];
        parent = null;
        return false;
    }

    private void ResetTakeoffTemplateToDefault()
    {
        var confirm = MessageBox.Show(
            "Reset takeoff templates to the built-in preset tree?",
            "Reset Takeoff Templates",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        _takeoffTemplateConfig = TakeoffTemplateConfig.BuildDefault();
        PersistTakeoffTemplateEditorChange("Takeoff templates reset to built-in defaults.");
    }

    private void PersistTakeoffTemplateEditorChange(string status, string? selectedNodeId = null)
    {
        if (_currentJob != null && TakeoffTemplateStore.LoadJobOverride(_currentJob) != null)
            TakeoffTemplateStore.SaveJobOverride(_currentJob, _takeoffTemplateConfig);
        else
            TakeoffTemplateStore.SaveGlobalConfig(_takeoffTemplateConfig);

        RefreshTakeoffTemplateEditors(selectedNodeId);
        TxtStatus.Text = status;
    }

    private void SaveTakeoffTemplateGlobal()
    {
        TakeoffTemplateStore.SaveGlobalConfig(_takeoffTemplateConfig);
        RefreshTakeoffTemplateEditors();
        TxtStatus.Text = "Saved takeoff templates as global default.";
    }

    private void SaveTakeoffTemplateJob()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job to save a per-job template override.";
            return;
        }

        TakeoffTemplateStore.SaveJobOverride(_currentJob, _takeoffTemplateConfig);
        RefreshTakeoffTemplateEditors();
        TxtStatus.Text = "Saved takeoff templates as this job's override.";
    }

    private void ClearTakeoffTemplateJobOverride()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job to clear a per-job template override.";
            return;
        }

        TakeoffTemplateStore.ClearJobOverride(_currentJob);
        ReloadTakeoffTemplateConfig();
        TxtStatus.Text = "Cleared this job's takeoff template override.";
    }

    private void SelectTemplateTreeItemById(TreeView tree, string nodeId)
    {
        foreach (TreeViewItem item in tree.Items.OfType<TreeViewItem>())
        {
            if (SelectTemplateTreeItemById(item, nodeId))
                return;
        }
    }

    private static bool SelectTemplateTreeItemById(TreeViewItem item, string nodeId)
    {
        if (item.Tag is TakeoffTemplateNode node &&
            string.Equals(node.Id, nodeId, StringComparison.OrdinalIgnoreCase))
        {
            item.IsSelected = true;
            item.BringIntoView();
            return true;
        }

        foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
        {
            if (SelectTemplateTreeItemById(child, nodeId))
            {
                item.IsExpanded = true;
                return true;
            }
        }

        return false;
    }

    private FrameworkElement BuildTakeoffTemplatesSettingsPanel() =>
        BuildTakeoffTemplateEditorPanel(settingsMode: true);

    private void BindTakeoffTemplatesSettings()
    {
        RefreshTakeoffTemplateEditors();
    }
}
