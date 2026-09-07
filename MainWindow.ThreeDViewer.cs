using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OurPlanCore;

public partial class MainWindow
{
    private Point? _threeDViewerDragStart;
    private Point? _threeDViewerMouseDownPoint;
    private Point? _threeDViewerPanStart;
    private bool _threeDViewerMouseMoved;
    private double _threeDViewerYaw = -38;
    private double _threeDViewerPitch = 28;
    private double _threeDViewerDistance = 28;
    private Point? _threeDSideViewerDragStart;
    private Point? _threeDSideViewerMouseDownPoint;
    private Point? _threeDSideViewerPanStart;
    private bool _threeDSideViewerMouseMoved;
    private double _threeDSideViewerYaw = -38;
    private double _threeDSideViewerPitch = 28;
    private double _threeDSideViewerDistance = 28;
    private Point3D _threeDViewerTarget = new(0, 0, 0);
    private Point3D _threeDSideViewerTarget = new(0, 0, 0);
    private double _threeDViewerSceneRadius = 12;
    private Viewport3D? _threeDSideViewport;
    private Grid? _threeDSideViewportHost;
    private PerspectiveCamera? _threeDSideCamera;
    private TextBlock? _threeDSideSummaryText;

    // Studio backdrop for the 3D viewports, theme-aware: a light gradient in
    // the light theme and a deep one in dark, so the shaded model always pops
    // against the background instead of washing out. Used by both the main and
    // side viewports and refreshed from ApplyTheme.
    private static LinearGradientBrush ThreeDViewportBackdropBrush(bool dark)
    {
        var stops = dark
            ? new GradientStopCollection
            {
                new(Color.FromRgb(0x2A, 0x2F, 0x38), 0),
                new(Color.FromRgb(0x1A, 0x1E, 0x25), 0.55),
                new(Color.FromRgb(0x0E, 0x11, 0x16), 1),
            }
            : new GradientStopCollection
            {
                new(Color.FromRgb(0xF7, 0xF8, 0xFA), 0),
                new(Color.FromRgb(0xE3, 0xE7, 0xEE), 0.62),
                new(Color.FromRgb(0xD2, 0xD8, 0xE1), 1),
            };
        var brush = new LinearGradientBrush(stops, new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
    }

    private void ApplyThreeDViewportTheme(bool dark)
    {
        LinearGradientBrush backdrop = ThreeDViewportBackdropBrush(dark);
        if (ThreeDViewerViewportHost != null)
            ThreeDViewerViewportHost.Background = backdrop;
        if (_threeDSideViewportHost != null)
            _threeDSideViewportHost.Background = backdrop;
    }

    private void InitializeThreeDViewer()
    {
        BuildCleanThreeDViewerScene();
        SetThreeDViewerView(-38, 28, 28);
    }

    private void RefreshThreeDViewer()
    {
        if (!IsModuleEnabled(ModuleId.ThreeD))
            return;

        if (_threeDWallElements.Count > 0 ||
            _threeDFloorSlabs.Count > 0 ||
            _threeDRoofGuides.Count > 0 ||
            _threeDRoofPlanes.Count > 0 ||
            _threeDRoofIssues.Count > 0)
        {
            RenderThreeDWallModel(fitCamera: false);
            return;
        }

        if (ThreeDViewerViewport.Children.Count == 0)
            BuildCleanThreeDViewerScene();

        Txt3dManagerSummary.Text = "3D viewer ready. Use Auto for walls, Wall for selected lines, or Roof Base for selected areas.";
        if (_threeDSideSummaryText != null)
            _threeDSideSummaryText.Text = Txt3dManagerSummary.Text;
        UpdateThreeDEditor();
    }

    private void Btn3dViewerFit_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.ThreeD, "Use 3D viewer"))
            return;

        _threeDViewerTarget = new Point3D(0, _threeDViewerPivotY, 0);
        SetThreeDViewerView(_threeDViewerYaw, _threeDViewerPitch, ThreeDViewerFitDistance());
    }
    private void Btn3dViewerIso_Click(object sender, RoutedEventArgs e)
    {
        if (RequireModule(ModuleId.ThreeD, "Use 3D viewer"))
            SetThreeDViewerView(-38, 28, ThreeDViewerFitDistance());
    }

    private void Btn3dViewerTop_Click(object sender, RoutedEventArgs e)
    {
        if (RequireModule(ModuleId.ThreeD, "Use 3D viewer"))
            SetThreeDViewerView(0, 86, ThreeDViewerFitDistance());
    }

    private void Btn3dViewerFront_Click(object sender, RoutedEventArgs e)
    {
        if (RequireModule(ModuleId.ThreeD, "Use 3D viewer"))
            SetThreeDViewerView(0, 12, ThreeDViewerFitDistance());
    }

    private void Btn3dViewerReset_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.ThreeD, "Reset 3D viewer"))
            return;

        if (_threeDWallElements.Count > 0 || _threeDFloorSlabs.Count > 0)
        {
            RenderThreeDWallModel(fitCamera: true);
            TxtStatus.Text = "3D viewer camera reset.";
            return;
        }

        BuildCleanThreeDViewerScene();
        SetThreeDViewerView(-38, 28, ThreeDViewerFitDistance());
        Txt3dManagerSummary.Text = "3D viewer reset. Use Auto for walls, Wall for selected lines, or Roof Base for selected areas.";
    }

    private void ThreeDViewerViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point point = e.GetPosition(ThreeDViewerViewport);
        _threeDViewerDragStart = point;
        _threeDViewerMouseDownPoint = point;
        _threeDViewerMouseMoved = false;
        if (TryBeginThreeDRoofGizmoDrag(ThreeDViewerViewport, point, ThreeDViewerCamera, _threeDViewerDistance))
            _threeDViewerMouseDownPoint = null;
        else if (_threeDRoofMoveModeEnabled && ThreeDEditingAllowed)
            BeginThreeDRoofPlacementDragSnapshot();
        CaptureThreeDInput(sender);
    }

    private void ThreeDViewerViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Point point = e.GetPosition(ThreeDViewerViewport);
        if (IsThreeDRoofGizmoDragging)
        {
            EndThreeDRoofGizmoDrag(_threeDViewerMouseMoved);
        }
        else if (_threeDRoofMoveModeEnabled)
        {
            if (_threeDViewerMouseMoved && EnsureThreeDEditable("move the 3D roof"))
            {
                SaveCurrentThreeDModel();
                CompleteThreeDRoofPlacementDragSnapshot();
            }
            else if (_threeDViewerMouseMoved)
            {
                RestoreThreeDRoofPlacementDragSnapshot();
            }
            else
            {
                CompleteThreeDRoofPlacementDragSnapshot();
            }
        }
        else if (!_threeDViewerMouseMoved && _threeDViewerMouseDownPoint != null)
        {
            SelectThreeDViewerWallAt(ThreeDViewerViewport, point);
        }

        _threeDViewerDragStart = null;
        _threeDViewerMouseDownPoint = null;
        _threeDViewerMouseMoved = false;
        ReleaseThreeDInput(sender);
    }

    private void ThreeDViewerViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_threeDViewerPanStart != null && e.RightButton == MouseButtonState.Pressed)
        {
            ThreeDViewerViewport_MouseRightMove(e.GetPosition(ThreeDViewerViewport));
            return;
        }

        if (_threeDViewerDragStart == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point current = e.GetPosition(ThreeDViewerViewport);
        Vector delta = current - _threeDViewerDragStart.Value;
        if (Math.Abs(delta.X) > 1 || Math.Abs(delta.Y) > 1)
            _threeDViewerMouseMoved = true;
        _threeDViewerDragStart = current;

        if (IsThreeDRoofGizmoDragging)
        {
            DragThreeDRoofGizmoTo(current);
            return;
        }

        if (_threeDRoofMoveModeEnabled)
        {
            if (!ThreeDEditingAllowed)
            {
                RestoreThreeDRoofPlacementDragSnapshot();
                _threeDRoofMoveModeEnabled = false;
                return;
            }
            NudgeThreeDRoofOffsetFromDrag(ThreeDViewerCamera, delta, _threeDViewerDistance);
            return;
        }

        SetThreeDViewerView(
            _threeDViewerYaw + delta.X * 0.35,
            Math.Clamp(_threeDViewerPitch - delta.Y * 0.25, -85, 86),
            _threeDViewerDistance);
    }

    private void ThreeDViewerViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double nextDistance = _threeDViewerDistance * (e.Delta > 0 ? 0.9 : 1.1);
        SetThreeDViewerView(_threeDViewerYaw, _threeDViewerPitch, nextDistance);
    }

    private void ThreeDViewerViewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _threeDViewerDragStart = null;
        _threeDViewerMouseDownPoint = null;
        _threeDViewerPanStart = e.GetPosition(ThreeDViewerViewport);
        CaptureThreeDInput(sender);
    }

    private void ThreeDViewerViewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _threeDViewerPanStart = null;
        ReleaseThreeDInput(sender);
    }

    private void ThreeDViewerViewport_MouseRightMove(Point current)
    {
        if (_threeDViewerPanStart == null)
            return;

        Vector delta = current - _threeDViewerPanStart.Value;
        _threeDViewerPanStart = current;
        _threeDViewerTarget = PanThreeDTarget(ThreeDViewerCamera, _threeDViewerTarget, delta, _threeDViewerDistance);
        SetThreeDViewerView(_threeDViewerYaw, _threeDViewerPitch, _threeDViewerDistance);
    }

    private void SetThreeDViewerView(double yaw, double pitch, double distance)
    {
        _threeDViewerYaw = yaw;
        _threeDViewerPitch = Math.Clamp(pitch, -85, 86);
        _threeDViewerDistance = Math.Clamp(distance, 6, 900);

        ApplyThreeDViewerCamera(ThreeDViewerCamera, _threeDViewerTarget, _threeDViewerYaw, _threeDViewerPitch, _threeDViewerDistance);
    }

    private void SetThreeDSideViewerView(double yaw, double pitch, double distance)
    {
        _threeDSideViewerYaw = yaw;
        _threeDSideViewerPitch = Math.Clamp(pitch, -85, 86);
        _threeDSideViewerDistance = Math.Clamp(distance, 6, 900);
        if (_threeDSideCamera != null)
            ApplyThreeDViewerCamera(_threeDSideCamera, _threeDSideViewerTarget, _threeDSideViewerYaw, _threeDSideViewerPitch, _threeDSideViewerDistance);
    }

    private static void ApplyThreeDViewerCamera(PerspectiveCamera camera, Point3D target, double yaw, double pitch, double distance)
    {
        double yawRad = yaw * Math.PI / 180.0;
        double pitchRad = pitch * Math.PI / 180.0;
        double cosPitch = Math.Cos(pitchRad);
        double x = distance * cosPitch * Math.Sin(yawRad);
        double y = distance * Math.Sin(pitchRad);
        double z = distance * cosPitch * Math.Cos(yawRad);

        camera.Position = new Point3D(target.X + x, target.Y + y, target.Z + z);
        camera.LookDirection = new Vector3D(
            target.X - camera.Position.X,
            target.Y - camera.Position.Y,
            target.Z - camera.Position.Z);
        camera.UpDirection = new Vector3D(0, 1, 0);
    }

    private static Point3D PanThreeDTarget(PerspectiveCamera camera, Point3D target, Vector delta, double distance)
    {
        Vector3D look = camera.LookDirection;
        if (look.LengthSquared < 0.000001)
            look = new Vector3D(0, 0, -1);
        look.Normalize();

        Vector3D up = camera.UpDirection;
        if (up.LengthSquared < 0.000001)
            up = new Vector3D(0, 1, 0);
        up.Normalize();

        Vector3D right = Vector3D.CrossProduct(look, up);
        if (right.LengthSquared < 0.000001)
            right = new Vector3D(1, 0, 0);
        right.Normalize();

        double scale = Math.Clamp(distance * 0.0025, 0.01, 2.5);
        Vector3D move = right;
        move *= -delta.X * scale;
        Vector3D vertical = up;
        vertical *= delta.Y * scale;
        move += vertical;
        return target + move;
    }

    private void BuildCleanThreeDViewerScene()
    {
        _threeDViewerSceneRadius = 12;
        _threeDWallHitMap.Clear();
        _threeDFloorSlabHitMap.Clear();
        ClearThreeDRoofSceneHitMaps();

        ThreeDViewerViewport.Children.Clear();
        ThreeDViewerViewport.Children.Add(new ModelVisual3D { Content = CreateCleanThreeDViewerSceneGroup() });
        if (_threeDSideViewport != null)
        {
            _threeDSideViewport.Children.Clear();
            _threeDSideViewport.Children.Add(new ModelVisual3D { Content = CreateCleanThreeDViewerSceneGroup() });
        }
    }

    private static Model3DGroup CreateCleanThreeDViewerSceneGroup()
    {
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(82, 88, 96)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(245, 247, 250), new Vector3D(-0.45, -0.85, -0.35)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(138, 165, 190), new Vector3D(0.65, -0.35, 0.55)));

        for (int i = -5; i <= 5; i++)
        {
            byte shade = i == 0 ? (byte)116 : (byte)74;
            AddThreeDViewerBox(group, new Point3D(i, -0.03, 0), 0.025, 0.025, 10, Color.FromRgb(shade, shade, shade), 0.9);
            AddThreeDViewerBox(group, new Point3D(0, -0.03, i), 10, 0.025, 0.025, Color.FromRgb(shade, shade, shade), 0.9);
        }

        AddThreeDViewerBox(group, new Point3D(2.6, 0.04, 0), 5.2, 0.08, 0.08, Color.FromRgb(220, 72, 72), 1);
        AddThreeDViewerBox(group, new Point3D(0, 1.6, 0), 0.08, 3.2, 0.08, Color.FromRgb(72, 170, 100), 1);
        AddThreeDViewerBox(group, new Point3D(0, 0.04, 2.6), 0.08, 0.08, 5.2, Color.FromRgb(72, 132, 220), 1);
        AddThreeDViewerBox(group, new Point3D(0, 0.45, 0), 1.2, 0.9, 1.2, Color.FromRgb(148, 163, 184), 0.45);
        return group;
    }

    private double ThreeDViewerFitDistance() =>
        Math.Clamp(_threeDViewerSceneRadius * 2.35, 10, 900);

    private void ThreeDSideViewerViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_threeDSideViewport == null)
            return;

        Point point = e.GetPosition(_threeDSideViewport);
        _threeDSideViewerDragStart = point;
        _threeDSideViewerMouseDownPoint = point;
        _threeDSideViewerMouseMoved = false;
        if (_threeDSideCamera != null &&
            TryBeginThreeDRoofGizmoDrag(_threeDSideViewport, point, _threeDSideCamera, _threeDSideViewerDistance))
        {
            _threeDSideViewerMouseDownPoint = null;
        }
        else if (_threeDRoofMoveModeEnabled && ThreeDEditingAllowed)
        {
            BeginThreeDRoofPlacementDragSnapshot();
        }
        CaptureThreeDInput(sender);
    }

    private void ThreeDSideViewerViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_threeDSideViewport == null)
            return;

        Point point = e.GetPosition(_threeDSideViewport);
        if (IsThreeDRoofGizmoDragging)
        {
            EndThreeDRoofGizmoDrag(_threeDSideViewerMouseMoved);
        }
        else if (_threeDRoofMoveModeEnabled)
        {
            if (_threeDSideViewerMouseMoved && EnsureThreeDEditable("move the 3D roof"))
            {
                SaveCurrentThreeDModel();
                CompleteThreeDRoofPlacementDragSnapshot();
            }
            else if (_threeDSideViewerMouseMoved)
            {
                RestoreThreeDRoofPlacementDragSnapshot();
            }
            else
            {
                CompleteThreeDRoofPlacementDragSnapshot();
            }
        }
        else if (!_threeDSideViewerMouseMoved && _threeDSideViewerMouseDownPoint != null)
        {
            SelectThreeDViewerWallAt(_threeDSideViewport, point);
        }

        _threeDSideViewerDragStart = null;
        _threeDSideViewerMouseDownPoint = null;
        _threeDSideViewerMouseMoved = false;
        ReleaseThreeDInput(sender);
    }

    private void ThreeDSideViewerViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_threeDSideViewport != null &&
            _threeDSideViewerPanStart != null &&
            e.RightButton == MouseButtonState.Pressed)
        {
            ThreeDSideViewerViewport_MouseRightMove(e.GetPosition(_threeDSideViewport));
            return;
        }

        if (_threeDSideViewport == null ||
            _threeDSideViewerDragStart == null ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point current = e.GetPosition(_threeDSideViewport);
        Vector delta = current - _threeDSideViewerDragStart.Value;
        if (Math.Abs(delta.X) > 1 || Math.Abs(delta.Y) > 1)
            _threeDSideViewerMouseMoved = true;
        _threeDSideViewerDragStart = current;
        if (IsThreeDRoofGizmoDragging)
        {
            DragThreeDRoofGizmoTo(current);
            return;
        }

        if (_threeDRoofMoveModeEnabled && _threeDSideCamera != null)
        {
            if (!ThreeDEditingAllowed)
            {
                RestoreThreeDRoofPlacementDragSnapshot();
                _threeDRoofMoveModeEnabled = false;
                return;
            }
            NudgeThreeDRoofOffsetFromDrag(_threeDSideCamera, delta, _threeDSideViewerDistance);
            return;
        }

        SetThreeDSideViewerView(
            _threeDSideViewerYaw + delta.X * 0.35,
            Math.Clamp(_threeDSideViewerPitch - delta.Y * 0.25, -85, 86),
            _threeDSideViewerDistance);
    }

    private void ThreeDSideViewerViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double nextDistance = _threeDSideViewerDistance * (e.Delta > 0 ? 0.9 : 1.1);
        SetThreeDSideViewerView(_threeDSideViewerYaw, _threeDSideViewerPitch, nextDistance);
    }

    private void ThreeDSideViewerViewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_threeDSideViewport == null)
            return;

        _threeDSideViewerDragStart = null;
        _threeDSideViewerMouseDownPoint = null;
        _threeDSideViewerPanStart = e.GetPosition(_threeDSideViewport);
        CaptureThreeDInput(sender);
    }

    private void ThreeDSideViewerViewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _threeDSideViewerPanStart = null;
        ReleaseThreeDInput(sender);
    }

    private void ThreeDSideViewerViewport_MouseRightMove(Point current)
    {
        if (_threeDSideViewport == null || _threeDSideCamera == null || _threeDSideViewerPanStart == null)
            return;

        Vector delta = current - _threeDSideViewerPanStart.Value;
        _threeDSideViewerPanStart = current;
        _threeDSideViewerTarget = PanThreeDTarget(_threeDSideCamera, _threeDSideViewerTarget, delta, _threeDSideViewerDistance);
        SetThreeDSideViewerView(_threeDSideViewerYaw, _threeDSideViewerPitch, _threeDSideViewerDistance);
    }

    private static void CaptureThreeDInput(object sender)
    {
        if (sender is UIElement element)
            element.CaptureMouse();
    }

    private static void ReleaseThreeDInput(object sender)
    {
        if (sender is UIElement element)
            element.ReleaseMouseCapture();
    }

    private static GeometryModel3D AddThreeDViewerBox(
        Model3DGroup group,
        Point3D center,
        double width,
        double height,
        double depth,
        Color color,
        double opacity)
    {
        double x = width / 2;
        double y = height / 2;
        double z = depth / 2;
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new(center.X - x, center.Y - y, center.Z - z),
                new(center.X + x, center.Y - y, center.Z - z),
                new(center.X + x, center.Y + y, center.Z - z),
                new(center.X - x, center.Y + y, center.Z - z),
                new(center.X - x, center.Y - y, center.Z + z),
                new(center.X + x, center.Y - y, center.Z + z),
                new(center.X + x, center.Y + y, center.Z + z),
                new(center.X - x, center.Y + y, center.Z + z),
            },
            TriangleIndices = new Int32Collection
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6,
                3, 0, 4, 3, 4, 7,
            },
        };

        var brush = new SolidColorBrush(color)
        {
            Opacity = opacity,
        };
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        group.Children.Add(model);
        return model;
    }
}
