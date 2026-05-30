using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OurPlaneCore;

public partial class MainWindow
{
    private readonly List<TakeoffTemplate> _takeoffTemplates = new();
    private ListBox? _templateList;
    private TextBlock? _templateStatusText;
    private Button? _templateApplyButton;
    private Button? _templateRenameButton;
    private Button? _templateDeleteButton;
    private ToggleButton? _templatesTabDockToggle;
    private TabItem? _templatesTab;
    private FrameworkElement? _templatePanel;
    private GridLength _templatesDockRowHeight = new(190);
    private bool _syncingTemplatesDockToggle;

    // Right-side bottom dock host (mirrors the Bookmarks dock, but under the Takeoffs tabs).
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

        _templateList = new ListBox
        {
            Margin = new Thickness(0, 4, 0, 0),
            DisplayMemberPath = nameof(TakeoffTemplate.Name),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        _templateList.SelectionChanged += (_, _) => UpdateTemplateButtons();
        _templateList.MouseDoubleClick += (_, _) => ApplySelectedTemplate();

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        toolbar.Children.Add(TemplateButton("Save Current", BtnTemplateSaveCurrent_Click,
            "Save the current Takeoffs folder/item tree as a reusable template"));
        _templateApplyButton = TemplateButton("Apply", BtnTemplateApply_Click,
            "Create the selected template under the current Takeoffs folder");
        _templateRenameButton = TemplateButton("Rename", BtnTemplateRename_Click, "Rename the selected template");
        _templateDeleteButton = TemplateButton("Delete", BtnTemplateDelete_Click, "Delete the selected template");
        toolbar.Children.Add(_templateApplyButton);
        toolbar.Children.Add(_templateRenameButton);
        toolbar.Children.Add(_templateDeleteButton);

        _templateStatusText = new TextBlock
        {
            Text = "No templates yet. Build a Takeoffs tree, then Save Current.",
            FontSize = 10,
            Foreground = System.Windows.Media.Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2),
        };

        var panel = new DockPanel { Margin = new Thickness(4) };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_templateStatusText, Dock.Top);
        panel.Children.Add(toolbar);
        panel.Children.Add(_templateStatusText);
        panel.Children.Add(_templateList);
        _templatePanel = panel;

        _templatesTab = new TabItem
        {
            Header = BuildTemplatesTabHeader(),
            Content = panel,
        };
        _rightWorkspaceTabs.Items.Add(_templatesTab);

        InstallTemplatesRightDock();

        _takeoffTemplates.Clear();
        _takeoffTemplates.AddRange(TakeoffTemplateStore.Load());
        RefreshTemplateList();
    }

    // Wraps the right-side workspace TabControl in a Grid so Templates can pop out
    // into a resizable panel docked at the bottom (the Bookmarks-below-Pages pattern).
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

    private Button TemplateButton(string content, RoutedEventHandler handler, string tooltip)
    {
        var button = new Button
        {
            Content = content,
            ToolTip = tooltip,
            Margin = new Thickness(0, 0, 4, 4),
            Padding = new Thickness(7, 3, 7, 3),
            FontSize = 11,
        };
        button.Click += handler;
        return button;
    }

    private void RefreshTemplateList(string? selectId = null)
    {
        if (_templateList == null)
            return;

        _templateList.ItemsSource = null;
        _templateList.ItemsSource = _takeoffTemplates;

        if (selectId != null)
            _templateList.SelectedItem = _takeoffTemplates.FirstOrDefault(t => t.Id == selectId);

        if (_templateStatusText != null)
        {
            _templateStatusText.Text = _takeoffTemplates.Count == 0
                ? "No templates yet. Build a Takeoffs tree, then Save Current."
                : $"{_takeoffTemplates.Count} template(s). Select a Takeoffs folder, pick one, Apply.";
        }
        UpdateTemplateButtons();
    }

    private void UpdateTemplateButtons()
    {
        bool hasSelection = _templateList?.SelectedItem is TakeoffTemplate;
        if (_templateApplyButton != null) _templateApplyButton.IsEnabled = hasSelection;
        if (_templateRenameButton != null) _templateRenameButton.IsEnabled = hasSelection;
        if (_templateDeleteButton != null) _templateDeleteButton.IsEnabled = hasSelection;
    }

    private void BtnTemplateSaveCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Save Template",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        List<TakeoffTemplateNode> roots = CaptureTakeoffTreeNodes(_currentJob.TakeoffsRoot);
        if (roots.Count == 0)
        {
            MessageBox.Show("The current Takeoffs tree is empty — nothing to save.", "Save Template",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? name = ShowInputDialog("Template name:", "Save Template", "My Template");
        if (string.IsNullOrWhiteSpace(name))
            return;

        var template = new TakeoffTemplate { Name = name.Trim(), Roots = roots };
        _takeoffTemplates.Add(template);
        TakeoffTemplateStore.Save(_takeoffTemplates);
        RefreshTemplateList(template.Id);
        TxtStatus.Text = $"Saved template '{template.Name}' ({CountTemplateItems(roots)} items).";
    }

    private void BtnTemplateApply_Click(object sender, RoutedEventArgs e) => ApplySelectedTemplate();

    private void ApplySelectedTemplate()
    {
        if (_templateList?.SelectedItem is not TakeoffTemplate template)
            return;
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Apply Template",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string parentFolder = CurrentTakeoffParentFolder();
        var confirm = MessageBox.Show(
            $"Create the '{template.Name}' tree ({CountTemplateItems(template.Roots)} items) under the selected Takeoffs folder?",
            "Apply Template",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            int created = 0;
            foreach (var node in template.Roots)
                created += ApplyTemplateNode(node, parentFolder);
            LoadTakeoffsForJob();
            SelectTakeoffNodeByFolder(parentFolder);
            TxtStatus.Text = $"Applied template '{template.Name}': created {created} folders/items.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Apply Template", ex);
        }
    }

    private int ApplyTemplateNode(TakeoffTemplateNode node, string parentFolder)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(node.Name))
            return 0;

        if (node.IsFolder)
        {
            string folderPath = OurPlaneCoreJobStore.CreateTakeoffFolder(_currentJob, parentFolder, node.Name);
            int count = 1;
            foreach (var child in node.Children)
                count += ApplyTemplateNode(child, folderPath);
            return count;
        }

        string color = string.IsNullOrWhiteSpace(node.Color) ? RandomTakeoffColor(_viewport.ActiveColor) : node.Color!;
        string type = string.IsNullOrWhiteSpace(node.MeasurementType) ? "line" : node.MeasurementType!;
        OurPlaneCoreJobStore.CreateTakeoffItem(_currentJob, parentFolder, node.Name, color, type);
        return 1;
    }

    private List<TakeoffTemplateNode> CaptureTakeoffTreeNodes(string baseFolder)
    {
        var roots = new List<TakeoffTemplateNode>();
        if (string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
            return roots;

        foreach (string dir in Directory.GetDirectories(baseFolder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var node = CaptureNode(dir);
            if (node != null)
                roots.Add(node);
        }
        return roots;
    }

    private TakeoffTemplateNode? CaptureNode(string dirPath)
    {
        TakeoffItem? item = _takeoffItems.FirstOrDefault(t =>
            string.Equals(t.FolderPath, dirPath, StringComparison.OrdinalIgnoreCase));

        var node = new TakeoffTemplateNode { Name = OurPlaneCoreJobStore.DisplayName(dirPath) };
        if (item != null)
        {
            node.IsFolder = false;
            node.MeasurementType = item.MeasurementType;
            node.Color = item.Color;
            return node;
        }

        node.IsFolder = true;
        foreach (string child in Directory.GetDirectories(dirPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var childNode = CaptureNode(child);
            if (childNode != null)
                node.Children.Add(childNode);
        }
        return node;
    }

    private static int CountTemplateItems(List<TakeoffTemplateNode> nodes)
    {
        int count = 0;
        foreach (var node in nodes)
        {
            count++;
            count += CountTemplateItems(node.Children);
        }
        return count;
    }

    private void BtnTemplateRename_Click(object sender, RoutedEventArgs e)
    {
        if (_templateList?.SelectedItem is not TakeoffTemplate template)
            return;

        string? name = ShowInputDialog("Template name:", "Rename Template", template.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;

        template.Name = name.Trim();
        TakeoffTemplateStore.Save(_takeoffTemplates);
        RefreshTemplateList(template.Id);
    }

    private void BtnTemplateDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_templateList?.SelectedItem is not TakeoffTemplate template)
            return;

        var confirm = MessageBox.Show(
            $"Delete template '{template.Name}'?",
            "Delete Template",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        _takeoffTemplates.Remove(template);
        TakeoffTemplateStore.Save(_takeoffTemplates);
        RefreshTemplateList();
    }
}
