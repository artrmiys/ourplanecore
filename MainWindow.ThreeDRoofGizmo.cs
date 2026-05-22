using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OurPlaneCore;

public partial class MainWindow
{
    private enum ThreeDRoofMoveAxis
    {
        None,
        X,
        Y,
        Z,
    }

    private readonly Dictionary<GeometryModel3D, string> _threeDRoofMeshHitMap = [];
    private readonly Dictionary<GeometryModel3D, ThreeDRoofMoveAxis> _threeDRoofGizmoHitMap = [];
    private ThreeDRoofMoveAxis _threeDRoofDragAxis = ThreeDRoofMoveAxis.None;
    private Point? _threeDRoofGizmoDragStart;
    private PerspectiveCamera? _threeDRoofGizmoDragCamera;
    private double _threeDRoofGizmoDragDistance;

    private bool IsThreeDRoofGizmoDragging => _threeDRoofDragAxis != ThreeDRoofMoveAxis.None;

    private void ClearThreeDRoofSceneHitMaps()
    {
        _threeDRoofMeshHitMap.Clear();
        _threeDRoofGizmoHitMap.Clear();
    }

    private void RegisterThreeDRoofMeshHit(GeometryModel3D model, string groupId)
    {
        if (!string.IsNullOrWhiteSpace(groupId))
            _threeDRoofMeshHitMap[model] = groupId;
    }

    private void RegisterThreeDRoofGizmoHit(GeometryModel3D model, ThreeDRoofMoveAxis axis)
    {
        if (axis != ThreeDRoofMoveAxis.None)
            _threeDRoofGizmoHitMap[model] = axis;
    }

    private bool TryBeginThreeDRoofGizmoDrag(Viewport3D viewport, Point point, PerspectiveCamera camera, double distance)
    {
        HitTestResult? hit = VisualTreeHelper.HitTest(viewport, point);
        if (hit is not RayMeshGeometry3DHitTestResult ray ||
            ray.ModelHit is not GeometryModel3D model ||
            !_threeDRoofGizmoHitMap.TryGetValue(model, out ThreeDRoofMoveAxis axis))
        {
            return false;
        }

        ThreeDRoofPlacement? placement = ActiveThreeDRoofPlacement();
        if (placement == null)
            return false;

        _threeDRoofDragAxis = axis;
        _threeDRoofGizmoDragStart = point;
        _threeDRoofGizmoDragCamera = camera;
        _threeDRoofGizmoDragDistance = distance;
        Mouse.OverrideCursor = Cursors.SizeAll;
        TxtStatus.Text = $"3D Roof: dragging {placement.Label} on {axis}-axis.";
        return true;
    }

    private void DragThreeDRoofGizmoTo(Point current)
    {
        if (_threeDRoofDragAxis == ThreeDRoofMoveAxis.None ||
            _threeDRoofGizmoDragStart == null ||
            _threeDRoofGizmoDragCamera == null)
        {
            return;
        }

        Vector delta = current - _threeDRoofGizmoDragStart.Value;
        _threeDRoofGizmoDragStart = current;
        NudgeThreeDRoofOffsetOnAxis(_threeDRoofDragAxis, _threeDRoofGizmoDragCamera, delta, _threeDRoofGizmoDragDistance);
    }

    private void EndThreeDRoofGizmoDrag(bool save)
    {
        if (_threeDRoofDragAxis == ThreeDRoofMoveAxis.None)
            return;

        ThreeDRoofPlacement? placement = ActiveThreeDRoofPlacement();
        if (save && placement != null)
        {
            SaveCurrentThreeDModel();
            LogThreeD($"Roof gizmo moved: {placement.Label}, X {placement.OffsetXFeet:0.##} ft, Y {placement.OffsetYFeet:0.##} ft, Z {placement.OffsetZFeet:0.##} ft.");
        }

        _threeDRoofDragAxis = ThreeDRoofMoveAxis.None;
        _threeDRoofGizmoDragStart = null;
        _threeDRoofGizmoDragCamera = null;
        _threeDRoofGizmoDragDistance = 0;
        Mouse.OverrideCursor = null;
        UpdateThreeDRoofMoveControls();
    }

    private void NudgeThreeDRoofOffsetOnAxis(
        ThreeDRoofMoveAxis axis,
        PerspectiveCamera camera,
        Vector delta,
        double distance)
    {
        ThreeDRoofPlacement? placement = ActiveThreeDRoofPlacement();
        if (placement == null)
            return;

        double pixels = ProjectScreenDeltaToWorldAxis(camera, axis, delta);
        if (Math.Abs(pixels) < 0.001)
            return;

        // Lower drag sensitivity: the roof should slide noticeably slower than
        // the cursor so it can be nudged into place precisely. (Was 0.0035 /
        // clamp 0.02..3.5, which moved the roof too fast per pixel.)
        double scale = Math.Clamp(distance * 0.0018, 0.01, 1.6);
        double amount = pixels * scale;
        switch (axis)
        {
            case ThreeDRoofMoveAxis.X:
                placement.OffsetXFeet += amount;
                break;
            case ThreeDRoofMoveAxis.Y:
                placement.OffsetYFeet += amount;
                break;
            case ThreeDRoofMoveAxis.Z:
                placement.OffsetZFeet += amount;
                break;
        }

        RenderThreeDWallModel(fitCamera: false);
        UpdateThreeDRoofMoveControls();
    }

    private static double ProjectScreenDeltaToWorldAxis(PerspectiveCamera camera, ThreeDRoofMoveAxis axis, Vector delta)
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

        Vector3D worldAxis = axis switch
        {
            ThreeDRoofMoveAxis.Y => new Vector3D(0, 1, 0),
            ThreeDRoofMoveAxis.Z => new Vector3D(0, 0, 1),
            _ => new Vector3D(1, 0, 0),
        };

        double sx = Vector3D.DotProduct(worldAxis, right);
        double sy = -Vector3D.DotProduct(worldAxis, up);
        double length = Math.Sqrt(sx * sx + sy * sy);
        if (length < 0.001)
            return 0;

        return (delta.X * sx + delta.Y * sy) / length;
    }

    private bool TrySelectThreeDRoofMeshAt(Viewport3D viewport, Point point)
    {
        HitTestResult? hit = VisualTreeHelper.HitTest(viewport, point);
        if (hit is not RayMeshGeometry3DHitTestResult ray ||
            ray.ModelHit is not GeometryModel3D model)
        {
            return false;
        }

        if (_threeDRoofGizmoHitMap.ContainsKey(model))
            return true;

        if (!_threeDRoofMeshHitMap.TryGetValue(model, out string? groupId) ||
            string.IsNullOrWhiteSpace(groupId))
        {
            return false;
        }

        _selectedThreeDWall = null;
        _selectedThreeDFloorSlab = null;
        SetActiveThreeDRoofGroup(groupId);
        ThreeDRoofPlacement? placement = ActiveThreeDRoofPlacement();
        TxtStatus.Text = placement == null
            ? $"3D Roof selected: {RoofGroupLabel(groupId)}."
            : $"3D Roof selected: {placement.Label}. Drag red X, green Y, or blue Z arrow to move it.";
        return true;
    }

    private void AddThreeDRoofMoveGizmo(Model3DGroup group, double centerX, double centerZ)
    {
        string groupId = ActiveThreeDRoofGroupId();
        if (string.IsNullOrWhiteSpace(groupId) ||
            !TryThreeDRoofGroupBounds(groupId, out ThreeDRoofVisualBounds bounds))
        {
            return;
        }

        double span = Math.Max(Math.Max(bounds.MaxX - bounds.MinX, bounds.MaxZ - bounds.MinZ), Math.Max(1, bounds.MaxY - bounds.MinY));
        double length = Math.Clamp(span * 0.28, 2.0, 10.0);
        double thickness = Math.Clamp(length * 0.045, 0.08, 0.32);
        double handle = Math.Clamp(length * 0.14, 0.28, 0.95);
        var origin = new Point3D(
            (bounds.MinX + bounds.MaxX) / 2.0 - centerX,
            bounds.MaxY + Math.Clamp(span * 0.07, 0.55, 2.5),
            (bounds.MinZ + bounds.MaxZ) / 2.0 - centerZ);

        GeometryModel3D hub = AddThreeDViewerBox(group, origin, handle * 0.9, handle * 0.9, handle * 0.9, Color.FromRgb(245, 245, 245), 0.96);
        RegisterThreeDRoofGizmoHit(hub, ThreeDRoofMoveAxis.X);
        AddThreeDRoofGizmoAxis(group, origin, ThreeDRoofMoveAxis.X, new Vector3D(1, 0, 0), length, thickness, handle, Color.FromRgb(239, 68, 68));
        AddThreeDRoofGizmoAxis(group, origin, ThreeDRoofMoveAxis.Y, new Vector3D(0, 1, 0), length, thickness, handle, Color.FromRgb(34, 197, 94));
        AddThreeDRoofGizmoAxis(group, origin, ThreeDRoofMoveAxis.Z, new Vector3D(0, 0, 1), length, thickness, handle, Color.FromRgb(59, 130, 246));
    }

    private void AddThreeDRoofGizmoAxis(
        Model3DGroup group,
        Point3D origin,
        ThreeDRoofMoveAxis axis,
        Vector3D direction,
        double length,
        double thickness,
        double handle,
        Color color)
    {
        Point3D shaftCenter = origin + direction * (length / 2.0);
        double width = axis == ThreeDRoofMoveAxis.X ? length : thickness;
        double height = axis == ThreeDRoofMoveAxis.Y ? length : thickness;
        double depth = axis == ThreeDRoofMoveAxis.Z ? length : thickness;
        GeometryModel3D shaft = AddThreeDViewerBox(group, shaftCenter, width, height, depth, color, 0.98);
        RegisterThreeDRoofGizmoHit(shaft, axis);

        Point3D handleCenter = origin + direction * length;
        GeometryModel3D arrow = AddThreeDViewerBox(group, handleCenter, handle, handle, handle, color, 1.0);
        RegisterThreeDRoofGizmoHit(arrow, axis);
    }

    private bool TryThreeDRoofGroupBounds(string groupId, out ThreeDRoofVisualBounds bounds)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        var zs = new List<double>();

        foreach (ThreeDFloorSlab slab in _threeDFloorSlabs.Where(slab => IsRoofSlab(slab) && SameRoofGroup(slab.RoofGroupId, groupId)))
        {
            (double ox, double oy, double oz) = RoofOffsetFor(slab.RoofGroupId);
            double topY = slab.ElevationFeet + oy + Math.Max(0.02, slab.ThicknessFeet);
            foreach (ThreeDPoint point in slab.Points)
            {
                xs.Add(point.XFeet + ox);
                ys.Add(topY);
                zs.Add(point.ZFeet + oz);
            }
        }

        foreach (ThreeDRoofPlane plane in _threeDRoofPlanes.Where(plane => SameRoofGroup(plane.RoofGroupId, groupId)))
        {
            (double ox, double oy, double oz) = RoofOffsetFor(plane.RoofGroupId);
            foreach (ThreeDRoofVertex point in plane.Points)
            {
                xs.Add(point.XFeet + ox);
                ys.Add(point.YFeet + oy);
                zs.Add(point.ZFeet + oz);
            }
        }

        foreach (ThreeDRoofGuide guide in _threeDRoofGuides.Where(guide => SameRoofGroup(guide.RoofGroupId, groupId)))
        {
            (double ox, double oy, double oz) = RoofOffsetFor(guide.RoofGroupId);
            foreach (ThreeDRoofGuidePoint point in guide.Points)
            {
                xs.Add(point.XFeet + ox);
                ys.Add(RoofGuidePointRenderY(guide, point) + oy);
                zs.Add(point.ZFeet + oz);
            }
        }

        if (xs.Count == 0 || ys.Count == 0 || zs.Count == 0)
        {
            bounds = default;
            return false;
        }

        bounds = new ThreeDRoofVisualBounds(xs.Min(), xs.Max(), ys.Min(), ys.Max(), zs.Min(), zs.Max());
        return true;
    }

    private readonly record struct ThreeDRoofVisualBounds(
        double MinX,
        double MaxX,
        double MinY,
        double MaxY,
        double MinZ,
        double MaxZ);
}
