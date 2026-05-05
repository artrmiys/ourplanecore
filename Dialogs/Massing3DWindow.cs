using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OurPlaneCore.Controls;

public sealed class Massing3DWindow : Window
{
    private readonly OurPlaneCoreJob _job;
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
        OurPlaneCoreJob job,
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

    private void RenderScene(bool preserveCamera)
    {
        _viewport.Children.Clear();
        _hitInfo.Clear();
        _markerScenePoints.Clear();
        _draftMarkerPoints.Clear();

        SmartMassingDraft? draft = _draft;
        if (draft != null)
            SmartMassingDraftService.RefreshDerivedGeometry(draft);

        List<SmartMassingFootprint> footprints = draft?.Footprints
            .Where(candidate => candidate.Points.Count >= 3)
            .OrderBy(candidate => SmartMassingDraftService.DisplayBaseElevation(draft, candidate))
            .ThenBy(candidate => candidate.Level)
            .ToList() ?? [];
        MassingSceneFrame frame = BuildSceneFrame(draft, footprints);

        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(88, 88, 88)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(245, 245, 245), new Vector3D(-0.45, -0.85, -0.35)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(135, 165, 195), new Vector3D(0.65, -0.35, 0.55)));

        var bounds = new MassingBounds();
        if (draft != null && footprints.Count > 0)
        {
            foreach (SmartMassingFootprint footprint in footprints)
                AddFootprint(group, bounds, draft, footprint, frame);
            AddRoofPlanes(group, bounds, draft, frame);
            AddOpenings(group, bounds, draft, frame);
            IndexDraftMarkerPoints(draft, footprints, frame);
        }

        BuildMarkerScenePoints(draft, footprints, frame);
        if (_showMarkersBox.IsChecked == true)
            AddMarkers(group, bounds);

        _viewport.Children.Add(new ModelVisual3D { Content = group });

        if (bounds.IsValid)
        {
            _target = bounds.Center;
            _sceneRadius = Math.Max(4, bounds.Radius);
        }
        else
        {
            _target = new Point3D(0, 2, 0);
            _sceneRadius = 8;
        }

        RefreshMarkerRows();
        if (preserveCamera)
            UpdateCamera();
        else
            FitView(resetAngles: true);

        UpdateStatus(draft, footprints);
    }

    private MassingSceneFrame BuildSceneFrame(SmartMassingDraft? draft, IReadOnlyList<SmartMassingFootprint> footprints)
    {
        if (draft != null && footprints.Count > 0 && TryGetDraftBounds(draft, out double minX, out double maxX, out double minY, out double maxY, out double maxZ))
        {
            double span = Math.Max(Math.Max(maxX - minX, maxY - minY), Math.Max(maxZ, 1));
            return new MassingSceneFrame(
                (minX + maxX) / 2,
                (minY + maxY) / 2,
                span,
                PdfCenterX: _markers.Count == 0 ? 0 : _markers.Average(marker => marker.PdfPoint.X),
                PdfCenterY: _markers.Count == 0 ? 0 : _markers.Average(marker => marker.PdfPoint.Y),
                PdfScale: EstimatePdfScale(draft, footprints, span));
        }

        double minPdfX = _markers.Count == 0 ? -100 : _markers.Min(marker => (double)marker.PdfPoint.X);
        double maxPdfX = _markers.Count == 0 ? 100 : _markers.Max(marker => (double)marker.PdfPoint.X);
        double minPdfY = _markers.Count == 0 ? -100 : _markers.Min(marker => (double)marker.PdfPoint.Y);
        double maxPdfY = _markers.Count == 0 ? 100 : _markers.Max(marker => (double)marker.PdfPoint.Y);
        double pdfSpan = Math.Max(Math.Max(maxPdfX - minPdfX, maxPdfY - minPdfY), 1);
        double targetSpan = 28;
        return new MassingSceneFrame(
            0,
            0,
            targetSpan,
            (minPdfX + maxPdfX) / 2,
            (minPdfY + maxPdfY) / 2,
            targetSpan / pdfSpan);
    }

    private void AddFootprint(
        Model3DGroup group,
        MassingBounds bounds,
        SmartMassingDraft draft,
        SmartMassingFootprint footprint,
        MassingSceneFrame frame)
    {
        double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
        double wallTop = SmartMassingDraftService.DisplayWallTopElevation(draft, footprint);
        AddSurface(
            group,
            bounds,
            $"floor_level_{footprint.Level}",
            $"Level {footprint.Level} floor",
            footprint.SourceMarkerIds,
            footprint.Points.Select(point => ToScenePoint(point.X, point.Y, baseZ, frame)).ToList(),
            Color.FromRgb(82, 91, 102),
            0.42);

        for (int i = 0; i < footprint.Points.Count; i++)
        {
            SmartMassingPoint start = footprint.Points[i];
            SmartMassingPoint end = footprint.Points[(i + 1) % footprint.Points.Count];
            List<string> sourceIds = new[] { start.SourceMarkerId, end.SourceMarkerId }
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            AddSurface(
                group,
                bounds,
                $"wall_level_{footprint.Level}_{i + 1}",
                $"Level {footprint.Level} wall {i + 1}",
                sourceIds,
                [
                    ToScenePoint(start.X, start.Y, baseZ, frame),
                    ToScenePoint(end.X, end.Y, baseZ, frame),
                    ToScenePoint(end.X, end.Y, wallTop, frame),
                    ToScenePoint(start.X, start.Y, wallTop, frame),
                ],
                Color.FromRgb(148, 163, 184),
                0.72);
        }
    }

    private void AddRoofPlanes(
        Model3DGroup group,
        MassingBounds bounds,
        SmartMassingDraft draft,
        MassingSceneFrame frame)
    {
        foreach (SmartMassingPlane plane in draft.Roof.Planes)
        {
            if (plane.Points.Count < 3 || string.Equals(plane.Status, "rejected", StringComparison.OrdinalIgnoreCase))
                continue;

            Color color = plane.Kind.Contains("candidate", StringComparison.OrdinalIgnoreCase)
                ? Color.FromRgb(245, 158, 11)
                : Color.FromRgb(71, 123, 156);
            AddSurface(
                group,
                bounds,
                plane.Id,
                string.IsNullOrWhiteSpace(plane.Label) ? plane.Kind : plane.Label,
                plane.SourceMarkerIds,
                plane.Points.Select(point => ToScenePoint(point.X, point.Y, point.Z, frame)).ToList(),
                color,
                plane.Status == "reviewed" ? 0.86 : 0.68);
        }
    }

    private void AddOpenings(
        Model3DGroup group,
        MassingBounds bounds,
        SmartMassingDraft draft,
        MassingSceneFrame frame)
    {
        foreach (SmartMassingOpening opening in draft.Openings)
        {
            SmartMassingFootprint? footprint = FootprintForOpening(draft, opening);
            if (string.Equals(opening.Status, "rejected", StringComparison.OrdinalIgnoreCase) ||
                footprint == null ||
                opening.WallIndex < 0 ||
                opening.WallIndex >= footprint.Points.Count ||
                opening.Width <= 0 ||
                opening.Height <= 0)
            {
                continue;
            }

            SmartMassingPoint start = footprint.Points[opening.WallIndex];
            SmartMassingPoint end = footprint.Points[(opening.WallIndex + 1) % footprint.Points.Count];
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.0001)
                continue;

            double ux = dx / length;
            double uy = dy / length;
            double halfW = Math.Min(opening.Width / 2.0, length / 2.0);
            double halfH = opening.Height / 2.0;
            double zMin = Math.Max(0.03, opening.Center.Z - halfH);
            double zMax = opening.Center.Z + halfH;
            double x1 = opening.Center.X - ux * halfW;
            double y1 = opening.Center.Y - uy * halfW;
            double x2 = opening.Center.X + ux * halfW;
            double y2 = opening.Center.Y + uy * halfW;
            Color color = opening.Type switch
            {
                "door" => Color.FromRgb(250, 204, 21),
                "window" => Color.FromRgb(34, 211, 238),
                _ => Color.FromRgb(168, 85, 247),
            };
            AddSurface(
                group,
                bounds,
                $"opening_{opening.SourceMarkerId}",
                string.IsNullOrWhiteSpace(opening.Type) ? "Opening" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(opening.Type),
                string.IsNullOrWhiteSpace(opening.SourceMarkerId) ? [] : [opening.SourceMarkerId],
                [
                    ToScenePoint(x1, y1, zMin, frame),
                    ToScenePoint(x2, y2, zMin, frame),
                    ToScenePoint(x2, y2, zMax, frame),
                    ToScenePoint(x1, y1, zMax, frame),
                ],
                color,
                0.92);
        }
    }

    private void AddSurface(
        Model3DGroup group,
        MassingBounds bounds,
        string id,
        string label,
        IReadOnlyList<string> sourceMarkerIds,
        IReadOnlyList<Point3D> points,
        Color color,
        double opacity)
    {
        if (points.Count < 3)
            return;

        var mesh = new MeshGeometry3D();
        foreach (Point3D point in points)
        {
            mesh.Positions.Add(point);
            bounds.Include(point);
        }

        for (int i = 1; i < points.Count - 1; i++)
        {
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 1);
        }

        bool selected = sourceMarkerIds.Any(id => string.Equals(id, _selectedMarkerId, StringComparison.OrdinalIgnoreCase));
        var brush = new SolidColorBrush(selected ? Color.FromRgb(255, 183, 77) : color)
        {
            Opacity = selected ? Math.Min(1, opacity + 0.12) : opacity,
        };
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material)
        {
            BackMaterial = material,
        };
        _hitInfo[model] = new Massing3DHitInfo(id, label, sourceMarkerIds.FirstOrDefault() ?? "");
        group.Children.Add(model);
    }

    private void IndexDraftMarkerPoints(SmartMassingDraft draft, IReadOnlyList<SmartMassingFootprint> footprints, MassingSceneFrame frame)
    {
        foreach (SmartMassingFootprint footprint in footprints)
        {
            double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
            foreach (SmartMassingPoint point in footprint.Points)
            {
                if (!string.IsNullOrWhiteSpace(point.SourceMarkerId))
                    _draftMarkerPoints[point.SourceMarkerId] = new MarkerDraftPoint(point.X, point.Y, baseZ);
            }
        }

        double roofZ = footprints.Count == 0
            ? 0
            : footprints.Max(footprint => SmartMassingDraftService.DisplayWallTopElevation(draft, footprint));
        foreach (SmartMassingRoofGuide guide in draft.Roof.Guides)
        {
            foreach (SmartMassingPoint point in guide.Points)
            {
                if (!string.IsNullOrWhiteSpace(point.SourceMarkerId))
                    _draftMarkerPoints.TryAdd(point.SourceMarkerId, new MarkerDraftPoint(point.X, point.Y, roofZ));
            }
        }

        foreach (SmartMassingPlane plane in draft.Roof.Planes)
        {
            foreach (SmartMassingVertex point in plane.Points)
            {
                if (!string.IsNullOrWhiteSpace(point.SourceMarkerId))
                    _draftMarkerPoints[point.SourceMarkerId] = new MarkerDraftPoint(point.X, point.Y, point.Z);
            }
        }

        foreach (SmartMassingOpening opening in draft.Openings)
        {
            if (!string.IsNullOrWhiteSpace(opening.SourceMarkerId))
                _draftMarkerPoints[opening.SourceMarkerId] = new MarkerDraftPoint(opening.Center.X, opening.Center.Y, opening.Center.Z);
        }

        foreach ((string markerId, MarkerDraftPoint point) in _draftMarkerPoints)
            _markerScenePoints[markerId] = ToScenePoint(point.X, point.Y, point.Z, frame);
    }

    private void BuildMarkerScenePoints(SmartMassingDraft? draft, IReadOnlyList<SmartMassingFootprint> footprints, MassingSceneFrame frame)
    {
        MarkerDraftPoint? originDraft = null;
        SmartAiMarker? originMarker = null;
        foreach ((string markerId, MarkerDraftPoint draftPoint) in _draftMarkerPoints)
        {
            if (!_markersById.TryGetValue(markerId, out SmartAiMarker? marker))
                continue;

            originDraft = draftPoint;
            originMarker = marker;
            break;
        }

        double wallTop = draft != null && footprints.Count > 0
            ? footprints.Max(footprint => SmartMassingDraftService.DisplayWallTopElevation(draft, footprint))
            : Math.Max(4, frame.ModelSpan * 0.24);

        foreach (SmartAiMarker marker in _markers)
        {
            if (string.IsNullOrWhiteSpace(marker.Id) || _markerScenePoints.ContainsKey(marker.Id))
                continue;

            double x;
            double y;
            if (originDraft != null && originMarker != null && frame.PdfScale > 0)
            {
                x = originDraft.Value.X + (marker.PdfPoint.X - originMarker.PdfPoint.X) * frame.PdfScale;
                y = originDraft.Value.Y + (marker.PdfPoint.Y - originMarker.PdfPoint.Y) * frame.PdfScale;
            }
            else
            {
                x = frame.CenterX + (marker.PdfPoint.X - frame.PdfCenterX) * frame.PdfScale;
                y = frame.CenterY + (marker.PdfPoint.Y - frame.PdfCenterY) * frame.PdfScale;
            }

            double z = MarkerHeight(marker, wallTop);
            _markerScenePoints[marker.Id] = ToScenePoint(x, y, z, frame);
        }
    }

    private void AddMarkers(Model3DGroup group, MassingBounds bounds)
    {
        double size = Math.Max(0.22, Math.Min(_sceneRadius * 0.035, 0.9));
        foreach (SmartAiMarker marker in _markers)
        {
            if (string.IsNullOrWhiteSpace(marker.Id) || !_markerScenePoints.TryGetValue(marker.Id, out Point3D point))
                continue;

            bool selected = string.Equals(marker.Id, _selectedMarkerId, StringComparison.OrdinalIgnoreCase);
            AddBox(
                group,
                bounds,
                point,
                selected ? size * 1.75 : size,
                selected ? Color.FromRgb(255, 183, 77) : MarkerColor(marker),
                selected ? 1.0 : 0.96,
                new Massing3DHitInfo(marker.Id, MarkerLabel(marker), marker.Id));
        }
    }

    private void AddBox(
        Model3DGroup group,
        MassingBounds bounds,
        Point3D center,
        double size,
        Color color,
        double opacity,
        Massing3DHitInfo info)
    {
        double h = size / 2;
        Point3D[] p =
        [
            new(center.X - h, center.Y - h, center.Z - h),
            new(center.X + h, center.Y - h, center.Z - h),
            new(center.X + h, center.Y + h, center.Z - h),
            new(center.X - h, center.Y + h, center.Z - h),
            new(center.X - h, center.Y - h, center.Z + h),
            new(center.X + h, center.Y - h, center.Z + h),
            new(center.X + h, center.Y + h, center.Z + h),
            new(center.X - h, center.Y + h, center.Z + h),
        ];
        int[] triangles =
        [
            0, 1, 2, 0, 2, 3,
            4, 6, 5, 4, 7, 6,
            0, 4, 5, 0, 5, 1,
            3, 2, 6, 3, 6, 7,
            1, 5, 6, 1, 6, 2,
            0, 3, 7, 0, 7, 4,
        ];

        var mesh = new MeshGeometry3D();
        foreach (Point3D point in p)
        {
            mesh.Positions.Add(point);
            bounds.Include(point);
        }

        foreach (int index in triangles)
            mesh.TriangleIndices.Add(index);

        var brush = new SolidColorBrush(color) { Opacity = opacity };
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material)
        {
            BackMaterial = material,
        };
        _hitInfo[model] = info;
        group.Children.Add(model);
    }

    private void RefreshMarkerRows()
    {
        _syncingSelection = true;
        try
        {
            var rows = _markers
                .Select(marker =>
                {
                    _markerScenePoints.TryGetValue(marker.Id, out Point3D point);
                    return new Marker3DRow(
                        marker.Id,
                        MarkerLabel(marker),
                        string.IsNullOrWhiteSpace(marker.Type) ? "(type)" : marker.Type,
                        string.IsNullOrWhiteSpace(marker.Page) ? "(page)" : marker.Page,
                        _markerScenePoints.ContainsKey(marker.Id)
                            ? $"{point.X:F1},{point.Y:F1},{point.Z:F1}"
                            : "-");
                })
                .ToList();

            _markerList.ItemsSource = rows;
            if (!string.IsNullOrWhiteSpace(_selectedMarkerId))
            {
                Marker3DRow? selected = rows.FirstOrDefault(row => string.Equals(row.MarkerId, _selectedMarkerId, StringComparison.OrdinalIgnoreCase));
                if (selected != null)
                    _markerList.SelectedItem = selected;
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void MarkerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || _markerList.SelectedItem is not Marker3DRow row)
            return;

        _selectedMarkerId = row.MarkerId;
        RenderScene(preserveCamera: true);
        _statusText.Text = $"Selected marker: {row.Label} | page: {row.Page} | scene: {row.ScenePoint}.";
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point position = e.GetPosition(_viewport);
        _dragStart = position;
        _mouseDown = position;
        _mouseMoved = false;
        _viewport.CaptureMouse();
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mouseDown != null && !_mouseMoved)
            TrySelectSceneObject(e.GetPosition(_viewport));

        _dragStart = null;
        _mouseDown = null;
        _viewport.ReleaseMouseCapture();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point current = e.GetPosition(_viewport);
        Vector delta = current - _dragStart.Value;
        if (delta.Length > 2.5)
            _mouseMoved = true;
        _dragStart = current;
        _yaw += delta.X * 0.45;
        _pitch = Math.Clamp(_pitch - delta.Y * 0.35, -8, 88);
        UpdateCamera();
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 0.88 : 1.14;
        _distance = Math.Clamp(_distance * factor, 4, Math.Max(20, _sceneRadius * 18));
        UpdateCamera();
    }

    private void TrySelectSceneObject(Point point)
    {
        Massing3DHitInfo? selected = null;
        VisualTreeHelper.HitTest(
            _viewport,
            null,
            result =>
            {
                if (result is RayHitTestResult ray &&
                    ray.ModelHit is GeometryModel3D model &&
                    _hitInfo.TryGetValue(model, out Massing3DHitInfo? info))
                {
                    selected = info;
                    return HitTestResultBehavior.Stop;
                }

                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(point));

        if (selected == null)
            return;

        if (!string.IsNullOrWhiteSpace(selected.SourceMarkerId))
            _selectedMarkerId = selected.SourceMarkerId;

        RenderScene(preserveCamera: true);
        _statusText.Text = string.IsNullOrWhiteSpace(selected.SourceMarkerId)
            ? $"Selected: {selected.Label}."
            : $"Selected: {selected.Label} | source marker: {selected.SourceMarkerId}.";
    }

    private void FitView(bool resetAngles)
    {
        if (resetAngles)
        {
            _yaw = -38;
            _pitch = 28;
        }

        _distance = Math.Max(10, _sceneRadius * 2.75);
        UpdateCamera();
    }

    private void SetView(double yaw, double pitch)
    {
        _yaw = yaw;
        _pitch = Math.Clamp(pitch, -8, 88);
        FitView(resetAngles: false);
    }

    private void UpdateCamera()
    {
        double yaw = _yaw * Math.PI / 180.0;
        double pitch = _pitch * Math.PI / 180.0;
        double horizontal = _distance * Math.Cos(pitch);
        var position = new Point3D(
            _target.X + horizontal * Math.Sin(yaw),
            _target.Y + _distance * Math.Sin(pitch),
            _target.Z + horizontal * Math.Cos(yaw));
        _camera.Position = position;
        _camera.LookDirection = _target - position;
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }

    private void UpdateStatus(SmartMassingDraft? draft, IReadOnlyList<SmartMassingFootprint> footprints)
    {
        int walls = footprints.Sum(footprint => footprint.Points.Count);
        int planes = draft?.Roof.Planes.Count(plane => !string.Equals(plane.Status, "rejected", StringComparison.OrdinalIgnoreCase)) ?? 0;
        int openings = draft?.Openings.Count(opening => !string.Equals(opening.Status, "rejected", StringComparison.OrdinalIgnoreCase)) ?? 0;
        string source = draft == null
            ? "marker-only preview"
            : string.IsNullOrWhiteSpace(draft.Status) ? "draft" : draft.Status;
        _statusText.Text = $"Scene: {source} | levels: {footprints.Count} | walls: {walls} | roof planes: {planes} | openings: {openings} | markers: {_markerScenePoints.Count}/{_markers.Count}. Drag to orbit, wheel to zoom, click marker/object to select.";
    }

    private double EstimatePdfScale(SmartMassingDraft draft, IReadOnlyList<SmartMassingFootprint> footprints, double fallbackSpan)
    {
        var pairs = footprints.SelectMany(footprint => footprint.Points)
            .Where(point => !string.IsNullOrWhiteSpace(point.SourceMarkerId) && _markersById.ContainsKey(point.SourceMarkerId))
            .Select(point => new
            {
                Draft = point,
                Marker = _markersById[point.SourceMarkerId],
            })
            .ToList();

        List<double> scales = [];
        for (int i = 0; i < pairs.Count; i++)
        {
            for (int j = i + 1; j < pairs.Count; j++)
            {
                double dx = pairs[i].Draft.X - pairs[j].Draft.X;
                double dy = pairs[i].Draft.Y - pairs[j].Draft.Y;
                double modelDistance = Math.Sqrt(dx * dx + dy * dy);
                double pdx = pairs[i].Marker.PdfPoint.X - pairs[j].Marker.PdfPoint.X;
                double pdy = pairs[i].Marker.PdfPoint.Y - pairs[j].Marker.PdfPoint.Y;
                double pdfDistance = Math.Sqrt(pdx * pdx + pdy * pdy);
                if (modelDistance > 0.0001 && pdfDistance > 0.0001)
                    scales.Add(modelDistance / pdfDistance);
            }
        }

        if (scales.Count > 0)
        {
            scales.Sort();
            return scales[scales.Count / 2];
        }

        double minPdfX = _markers.Count == 0 ? -100 : _markers.Min(marker => (double)marker.PdfPoint.X);
        double maxPdfX = _markers.Count == 0 ? 100 : _markers.Max(marker => (double)marker.PdfPoint.X);
        double minPdfY = _markers.Count == 0 ? -100 : _markers.Min(marker => (double)marker.PdfPoint.Y);
        double maxPdfY = _markers.Count == 0 ? 100 : _markers.Max(marker => (double)marker.PdfPoint.Y);
        double pdfSpan = Math.Max(Math.Max(maxPdfX - minPdfX, maxPdfY - minPdfY), 1);
        return Math.Max(0.0001, fallbackSpan / pdfSpan);
    }

    private static bool TryGetDraftBounds(
        SmartMassingDraft draft,
        out double minX,
        out double maxX,
        out double minY,
        out double maxY,
        out double maxZ)
    {
        List<SmartMassingVertex> vertices = [];
        foreach (SmartMassingFootprint footprint in draft.Footprints.Where(footprint => footprint.Points.Count >= 3))
        {
            double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
            double wallTopZ = SmartMassingDraftService.DisplayWallTopElevation(draft, footprint);
            vertices.AddRange(footprint.Points.Select(point => new SmartMassingVertex { X = point.X, Y = point.Y, Z = baseZ }));
            vertices.AddRange(footprint.Points.Select(point => new SmartMassingVertex { X = point.X, Y = point.Y, Z = wallTopZ }));
        }
        vertices.AddRange(draft.Roof.Planes.SelectMany(plane => plane.Points));
        vertices.AddRange(draft.Openings.Select(opening => opening.Center));
        vertices = vertices
            .Where(point => !double.IsNaN(point.X) && !double.IsNaN(point.Y) && !double.IsNaN(point.Z))
            .ToList();
        if (vertices.Count == 0)
        {
            minX = maxX = minY = maxY = maxZ = 0;
            return false;
        }

        minX = vertices.Min(point => point.X);
        maxX = vertices.Max(point => point.X);
        minY = vertices.Min(point => point.Y);
        maxY = vertices.Max(point => point.Y);
        maxZ = Math.Max(1, vertices.Max(point => point.Z));
        return maxX > minX && maxY > minY;
    }

    private static SmartMassingFootprint? FootprintForOpening(SmartMassingDraft draft, SmartMassingOpening opening)
    {
        SmartMassingFootprint? exact = draft.Footprints
            .Where(footprint => footprint.Points.Count >= 3)
            .FirstOrDefault(footprint => footprint.Level == opening.Level);
        return exact ?? draft.Footprints
            .Where(footprint => footprint.Points.Count >= 3)
            .OrderByDescending(footprint => SmartMassingDraftService.DisplayWallTopElevation(draft, footprint))
            .ThenByDescending(footprint => footprint.Level)
            .FirstOrDefault();
    }

    private static Point3D ToScenePoint(double x, double y, double z, MassingSceneFrame frame) =>
        new(x - frame.CenterX, z, y - frame.CenterY);

    private static double MarkerHeight(SmartAiMarker marker, double wallTop)
    {
        string type = marker.Type ?? "";
        if (type.Contains("height", StringComparison.OrdinalIgnoreCase))
            return wallTop;
        if (type.Contains("roof", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("ridge", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("valley", StringComparison.OrdinalIgnoreCase))
        {
            return wallTop + Math.Max(0.35, wallTop * 0.08);
        }
        if (type.Contains("window", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("door", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("opening", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(0.5, wallTop * 0.48);
        }

        return 0.18;
    }

    private static Color MarkerColor(SmartAiMarker marker)
    {
        string type = marker.Type ?? "";
        if (type.Contains("corner", StringComparison.OrdinalIgnoreCase))
            return Color.FromRgb(34, 197, 94);
        if (type.Contains("height", StringComparison.OrdinalIgnoreCase))
            return Color.FromRgb(250, 204, 21);
        if (type.Contains("roof", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("ridge", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("valley", StringComparison.OrdinalIgnoreCase))
        {
            return Color.FromRgb(56, 189, 248);
        }
        if (type.Contains("window", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("door", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("opening", StringComparison.OrdinalIgnoreCase))
        {
            return Color.FromRgb(244, 114, 182);
        }

        return Color.FromRgb(45, 212, 191);
    }

    private static string MarkerLabel(SmartAiMarker marker)
    {
        string type = string.IsNullOrWhiteSpace(marker.Type) ? "marker" : marker.Type;
        return string.IsNullOrWhiteSpace(marker.Value)
            ? $"{type} {marker.Id}"
            : $"{type}: {marker.Value}";
    }

    private readonly record struct MarkerDraftPoint(double X, double Y, double Z);
    private readonly record struct MassingSceneFrame(double CenterX, double CenterY, double ModelSpan, double PdfCenterX, double PdfCenterY, double PdfScale);
    private sealed record Massing3DHitInfo(string Id, string Label, string SourceMarkerId);
    private sealed record Marker3DRow(string MarkerId, string Label, string Type, string Page, string ScenePoint);

    private sealed class MassingBounds
    {
        private double _minX = double.PositiveInfinity;
        private double _maxX = double.NegativeInfinity;
        private double _minY = double.PositiveInfinity;
        private double _maxY = double.NegativeInfinity;
        private double _minZ = double.PositiveInfinity;
        private double _maxZ = double.NegativeInfinity;

        public bool IsValid => !double.IsInfinity(_minX) && _maxX >= _minX && _maxY >= _minY && _maxZ >= _minZ;

        public Point3D Center => IsValid
            ? new((_minX + _maxX) / 2, (_minY + _maxY) / 2, (_minZ + _maxZ) / 2)
            : new Point3D(0, 2, 0);

        public double Radius
        {
            get
            {
                if (!IsValid)
                    return 8;

                double dx = _maxX - _minX;
                double dy = _maxY - _minY;
                double dz = _maxZ - _minZ;
                return Math.Max(4, Math.Sqrt(dx * dx + dy * dy + dz * dz) / 2);
            }
        }

        public void Include(Point3D point)
        {
            if (double.IsNaN(point.X) || double.IsNaN(point.Y) || double.IsNaN(point.Z))
                return;

            _minX = Math.Min(_minX, point.X);
            _maxX = Math.Max(_maxX, point.X);
            _minY = Math.Min(_minY, point.Y);
            _maxY = Math.Max(_maxY, point.Y);
            _minZ = Math.Min(_minZ, point.Z);
            _maxZ = Math.Max(_maxZ, point.Z);
        }
    }
}
