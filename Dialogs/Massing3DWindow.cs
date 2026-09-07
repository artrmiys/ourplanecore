using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;

namespace OurPlanCore.Controls;

public sealed partial class Massing3DWindow : Window
{
    private readonly OurPlanCoreJob _job;
    private SmartMassingDraft? _draft;
    private IReadOnlyList<SmartAiMarker> _markers;
    private readonly Viewport3D _viewport;
    private readonly PerspectiveCamera _camera;
    private readonly TextBlock _statusText;
    private readonly ListView _markerList;
    private readonly CheckBox _showMarkersBox;
    private readonly Dictionary<GeometryModel3D, Massing3DHitInfo> _hitInfo = [];
    private readonly Dictionary<string, Point3D> _markerScenePoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MarkerDraftPoint> _draftMarkerPoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SmartAiMarker> _markersById = new(StringComparer.OrdinalIgnoreCase);

    private Point3D _target = new(0, 3, 0);
    private double _sceneRadius = 12;
    private double _distance = 28;
    private double _yaw = -38;
    private double _pitch = 28;
    private Point? _dragStart;
    private Point? _mouseDown;
    private bool _mouseMoved;
    private bool _syncingSelection;
    private string _selectedMarkerId = "";

    public Massing3DWindow(
        OurPlanCoreJob job,
        SmartMassingDraft? draft,
        IReadOnlyList<SmartAiMarker> markers)
    {
        _job = job;
        _draft = draft;
        _markers = markers
            .Where(marker => !string.Equals(marker.SampleKind, "ignore", StringComparison.OrdinalIgnoreCase))
            .OrderBy(marker => marker.Page, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (SmartAiMarker marker in _markers)
        {
            if (!string.IsNullOrWhiteSpace(marker.Id))
                _markersById[marker.Id] = marker;
        }

        Title = string.IsNullOrWhiteSpace(job.Name)
            ? "3D Viewport"
            : $"3D Viewport - {job.Name}";
        Width = 1180;
        Height = 780;
        MinWidth = 760;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        _camera = new PerspectiveCamera
        {
            FieldOfView = 42,
            UpDirection = new Vector3D(0, 1, 0),
        };

        var root = new DockPanel { Margin = new Thickness(10) };
        Content = root;

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var title = new TextBlock
        {
            Text = "3D viewport",
            FontSize = 16,
            FontWeight = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetResourceReference(Control.ForegroundProperty, "ControlForegroundBrush");
        top.Children.Add(title);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        buttons.Children.Add(ViewButton("Fit", "Fit model and markers", () => FitView(resetAngles: false)));
        buttons.Children.Add(ViewButton("Iso", "Isometric view", () => SetView(-38, 28)));
        buttons.Children.Add(ViewButton("Top", "Top view", () => SetView(0, 88)));
        buttons.Children.Add(ViewButton("Front", "Front view", () => SetView(0, 12)));

        _showMarkersBox = new CheckBox
        {
            Content = "Markers",
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = true,
            ToolTip = "Show saved AI marker points in the 3D scene",
        };
        _showMarkersBox.Checked += (_, _) => RenderScene(preserveCamera: true);
        _showMarkersBox.Unchecked += (_, _) => RenderScene(preserveCamera: true);
        buttons.Children.Add(_showMarkersBox);

        _statusText = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        _statusText.SetResourceReference(Control.ForegroundProperty, "SecondaryForegroundBrush");
        DockPanel.SetDock(_statusText, Dock.Top);
        root.Children.Add(_statusText);

        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        root.Children.Add(mainGrid);

        _viewport = new Viewport3D
        {
            Camera = _camera,
            ClipToBounds = true,
        };
        _viewport.MouseLeftButtonDown += Viewport_MouseLeftButtonDown;
        _viewport.MouseLeftButtonUp += Viewport_MouseLeftButtonUp;
        _viewport.MouseMove += Viewport_MouseMove;
        _viewport.MouseWheel += Viewport_MouseWheel;

        var viewportBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = _viewport,
        };
        viewportBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBackgroundBrush");
        viewportBorder.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        mainGrid.Children.Add(viewportBorder);

        var side = new DockPanel { Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(side, 1);
        mainGrid.Children.Add(side);

        var markerTitle = new TextBlock
        {
            Text = "Source markers",
            FontWeight = FontWeights.Normal,
            Margin = new Thickness(0, 0, 0, 6),
        };
        markerTitle.SetResourceReference(Control.ForegroundProperty, "ControlForegroundBrush");
        DockPanel.SetDock(markerTitle, Dock.Top);
        side.Children.Add(markerTitle);

        _markerList = new ListView
        {
            MinWidth = 240,
            SelectionMode = SelectionMode.Single,
        };
        _markerList.SelectionChanged += MarkerList_SelectionChanged;
        var gridView = new GridView();
        gridView.Columns.Add(new GridViewColumn { Header = "Type", DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Marker3DRow.Type)), Width = 110 });
        gridView.Columns.Add(new GridViewColumn { Header = "Page", DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Marker3DRow.Page)), Width = 80 });
        gridView.Columns.Add(new GridViewColumn { Header = "3D", DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Marker3DRow.ScenePoint)), Width = 70 });
        _markerList.View = gridView;
        side.Children.Add(_markerList);

        RenderScene(preserveCamera: false);
    }

    private Button ViewButton(string text, string toolTip, Action action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = toolTip,
        };
        button.Click += (_, _) => action();
        return button;
    }
}
