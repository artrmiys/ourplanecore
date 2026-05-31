using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using OurPlaneCore.Controls;
using SkiaSharp;
using Path = System.IO.Path;

namespace OurPlaneCore;

public partial class MainWindow
{
    // Inline 3D massing preview scene, camera, and object selection.

    private void RefreshMassing3DPreview(SmartMassingDraft? draft)
    {
        if (_massingViewport3D == null)
            return;

        _massingViewport3D.Children.Clear();
        _massing3DObjectInfo.Clear();

        List<SmartMassingFootprint> footprints = draft?.Footprints
            .Where(footprint => footprint.Points.Count >= 3)
            .OrderBy(footprint => SmartMassingDraftService.DisplayBaseElevation(draft, footprint))
            .ThenBy(footprint => footprint.Level)
            .ToList() ?? [];
        if (draft == null || footprints.Count == 0)
        {
            if (_massingViewportStatusText != null)
                _massingViewportStatusText.Text = draft == null
                    ? "Build a draft to preview the 3D shell."
                    : "Draft has no footprint loop for 3D preview.";
            return;
        }

        SmartMassingDraftService.RefreshDerivedGeometry(draft);
        if (!TryGetMassing3DBounds(draft, out double minX, out double maxX, out double minY, out double maxY, out double maxZ))
        {
            if (_massingViewportStatusText != null)
                _massingViewportStatusText.Text = "Draft bounds are not valid for 3D preview.";
            return;
        }

        double centerX = (minX + maxX) / 2;
        double centerY = (minY + maxY) / 2;
        double spanX = Math.Max(0.001, maxX - minX);
        double spanY = Math.Max(0.001, maxY - minY);
        _massing3DSceneRadius = Math.Max(Math.Max(spanX, spanY), Math.Max(maxZ, 1));
        _massing3DTarget = new Point3D(0, Math.Max(maxZ, 1) / 2, 0);

        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(92, 92, 92)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(245, 245, 245), new Vector3D(-0.45, -0.8, -0.35)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(130, 160, 190), new Vector3D(0.65, -0.35, 0.55)));

        foreach (SmartMassingFootprint footprint in footprints)
        {
            AddMassingFootprint3D(group, draft, footprint, centerX, centerY);
            AddMassingMarkerPins3D(group, draft, footprint, centerX, centerY);
        }
        AddMassingRoofPlanes3D(group, draft, centerX, centerY);
        AddMassingOpenings3D(group, draft, centerX, centerY);

        _massingViewport3D.Children.Add(new ModelVisual3D { Content = group });
        EnsureMassingCamera();
        FitMassing3DView(resetAngles: false);

        if (_massingViewportStatusText != null)
        {
            int wallCount = footprints.Sum(footprint => footprint.Points.Count);
            int roofPlanes = draft.Roof.Planes.Count(plane => !string.Equals(plane.Status, "rejected", StringComparison.OrdinalIgnoreCase));
            int openings = draft.Openings.Count(opening => !string.Equals(opening.Status, "rejected", StringComparison.OrdinalIgnoreCase));
            _massingViewportStatusText.Text = $"3D shell | levels: {footprints.Count} | walls: {wallCount} | roof planes: {roofPlanes} | openings: {openings} | roof: {draft.Roof.Type} ({draft.Roof.Status})";
        }
    }

    private void AddMassingFootprint3D(
        Model3DGroup group,
        SmartMassingDraft draft,
        SmartMassingFootprint footprint,
        double centerX,
        double centerY)
    {
        double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
        double wallTopZ = SmartMassingDraftService.DisplayWallTopElevation(draft, footprint);
        var floor = footprint.Points
            .Select(point => ToMassing3DPoint(point.X, point.Y, baseZ, centerX, centerY))
            .ToList();
        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                $"floor_level_{footprint.Level}",
                "floor",
                $"Level {footprint.Level} floor/footprint",
                footprint.SourceMarkerIds,
                "Floor cap generated from exterior corner markers."),
            floor,
            Color.FromRgb(82, 91, 102),
            0.42);

        for (int i = 0; i < footprint.Points.Count; i++)
        {
            SmartMassingPoint start = footprint.Points[i];
            SmartMassingPoint end = footprint.Points[(i + 1) % footprint.Points.Count];
            var sourceIds = new[]
                {
                    start.SourceMarkerId,
                    end.SourceMarkerId,
                }
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            AddMassingSurface(
                group,
                new Massing3DObjectInfo(
                    $"wall_level_{footprint.Level}_{i + 1}",
                    "wall",
                    $"Level {footprint.Level} wall {i + 1}",
                    sourceIds,
                    "Wall face generated by extruding adjacent footprint points."),
                [
                    ToMassing3DPoint(start.X, start.Y, baseZ, centerX, centerY),
                    ToMassing3DPoint(end.X, end.Y, baseZ, centerX, centerY),
                    ToMassing3DPoint(end.X, end.Y, wallTopZ, centerX, centerY),
                    ToMassing3DPoint(start.X, start.Y, wallTopZ, centerX, centerY),
                ],
                Color.FromRgb(148, 163, 184),
                0.72);
        }
    }

    private void AddMassingRoofPlanes3D(
        Model3DGroup group,
        SmartMassingDraft draft,
        double centerX,
        double centerY)
    {
        foreach (SmartMassingPlane plane in draft.Roof.Planes)
        {
            if (string.Equals(plane.Status, "rejected", StringComparison.OrdinalIgnoreCase) ||
                plane.Points.Count < 3)
            {
                continue;
            }

            Color color = plane.Kind.Contains("candidate", StringComparison.OrdinalIgnoreCase)
                ? Color.FromRgb(245, 158, 11)
                : Color.FromRgb(71, 123, 156);
            double opacity = plane.Status == "reviewed" ? 0.86 : 0.68;
            AddMassingSurface(
                group,
                new Massing3DObjectInfo(
                    plane.Id,
                    plane.Kind,
                    plane.Label,
                    plane.SourceMarkerIds,
                    plane.Notes),
                plane.Points.Select(point => ToMassing3DPoint(point.X, point.Y, point.Z, centerX, centerY)).ToList(),
                color,
                opacity);
        }
    }

    private void AddMassingOpenings3D(
        Model3DGroup group,
        SmartMassingDraft draft,
        double centerX,
        double centerY)
    {
        foreach (SmartMassingOpening opening in draft.Openings)
        {
            SmartMassingFootprint? footprint = FootprintForMassingOpening(draft, opening);
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
            AddMassingSurface(
                group,
                new Massing3DObjectInfo(
                    $"opening_{opening.SourceMarkerId}",
                    opening.Type,
                    $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(opening.Type)} projection",
                    string.IsNullOrWhiteSpace(opening.SourceMarkerId) ? [] : [opening.SourceMarkerId],
                    opening.Notes),
                [
                    ToMassing3DPoint(x1, y1, zMin, centerX, centerY),
                    ToMassing3DPoint(x2, y2, zMin, centerX, centerY),
                    ToMassing3DPoint(x2, y2, zMax, centerX, centerY),
                    ToMassing3DPoint(x1, y1, zMax, centerX, centerY),
                ],
                color,
                0.92);
        }
    }

    private void AddMassingMarkerPins3D(
        Model3DGroup group,
        SmartMassingDraft draft,
        SmartMassingFootprint footprint,
        double centerX,
        double centerY)
    {
        double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
        double wallHeight = SmartMassingDraftService.DisplayWallHeight(draft, footprint);
        foreach (SmartMassingPoint point in footprint.Points)
        {
            if (string.IsNullOrWhiteSpace(point.SourceMarkerId))
                continue;
            AddMassingPin3D(
                group,
                $"pin_{point.SourceMarkerId}",
                "marker_pin",
                "Exterior corner marker",
                point.SourceMarkerId,
                ToMassing3DPoint(point.X, point.Y, baseZ, centerX, centerY),
                Color.FromRgb(56, 189, 248),
                wallHeight);
        }

        foreach (SmartMassingOpening opening in draft.Openings.Where(opening => opening.Level == footprint.Level))
        {
            if (string.IsNullOrWhiteSpace(opening.SourceMarkerId))
                continue;
            AddMassingPin3D(
                group,
                $"pin_{opening.SourceMarkerId}",
                "opening_pin",
                $"{opening.Type} marker",
                opening.SourceMarkerId,
                ToMassing3DPoint(opening.Center.X, opening.Center.Y, opening.Center.Z, centerX, centerY),
                Color.FromRgb(244, 114, 182),
                wallHeight);
        }
    }

    private void AddMassingPin3D(
        Model3DGroup group,
        string id,
        string kind,
        string label,
        string sourceMarkerId,
        Point3D center,
        Color color,
        double modelHeight)
    {
        double size = Math.Max(0.45, Math.Min(_massing3DSceneRadius * 0.045, Math.Max(0.55, modelHeight * 0.095)));
        var points = new List<Point3D>
        {
            new(center.X - size, center.Y, center.Z - size),
            new(center.X + size, center.Y, center.Z - size),
            new(center.X + size, center.Y, center.Z + size),
            new(center.X - size, center.Y, center.Z + size),
            new(center.X, center.Y + size * 2.8, center.Z),
        };

        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                id,
                kind,
                label,
                string.IsNullOrWhiteSpace(sourceMarkerId) ? [] : [sourceMarkerId],
                "3D source marker pin."),
            [points[0], points[1], points[4]],
            color,
            0.95);
        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                $"{id}_b",
                kind,
                label,
                string.IsNullOrWhiteSpace(sourceMarkerId) ? [] : [sourceMarkerId],
                "3D source marker pin."),
            [points[1], points[2], points[4]],
            color,
            0.95);
        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                $"{id}_c",
                kind,
                label,
                string.IsNullOrWhiteSpace(sourceMarkerId) ? [] : [sourceMarkerId],
                "3D source marker pin."),
            [points[2], points[3], points[4]],
            color,
            0.95);
        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                $"{id}_d",
                kind,
                label,
                string.IsNullOrWhiteSpace(sourceMarkerId) ? [] : [sourceMarkerId],
                "3D source marker pin."),
            [points[3], points[0], points[4]],
            color,
            0.95);
    }

    private void AddMassingSurface(
        Model3DGroup group,
        Massing3DObjectInfo info,
        IReadOnlyList<Point3D> points,
        Color color,
        double opacity)
    {
        if (points.Count < 3)
            return;

        var mesh = new MeshGeometry3D();
        foreach (Point3D point in points)
            mesh.Positions.Add(point);
        for (int i = 1; i < points.Count - 1; i++)
        {
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 1);
        }

        bool selected = IsSelectedMassing3DObject(info);
        var brush = new SolidColorBrush(selected ? Color.FromRgb(255, 183, 77) : color)
        {
            Opacity = selected ? Math.Min(1.0, opacity + 0.1) : opacity,
        };
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material)
        {
            BackMaterial = material,
        };
        _massing3DObjectInfo[model] = info;
        group.Children.Add(model);
    }

    private bool IsSelectedMassing3DObject(Massing3DObjectInfo info)
    {
        if (!string.IsNullOrWhiteSpace(_selectedMassing3DObjectId) &&
            string.Equals(_selectedMassing3DObjectId, info.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string selectedMarkerId = _selectedMassingMarkerId;
        if (string.IsNullOrWhiteSpace(selectedMarkerId))
            selectedMarkerId = SelectedMassingMarkerRow()?.MarkerId ?? "";
        return !string.IsNullOrWhiteSpace(selectedMarkerId) &&
               info.SourceMarkerIds.Contains(selectedMarkerId, StringComparer.OrdinalIgnoreCase);
    }

    private static Point3D ToMassing3DPoint(double x, double y, double z, double centerX, double centerY) =>
        new(x - centerX, z, y - centerY);

    private static bool TryGetMassing3DBounds(
        SmartMassingDraft draft,
        out double minX,
        out double maxX,
        out double minY,
        out double maxY,
        out double maxZ)
    {
        var vertices = new List<SmartMassingVertex>();
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
            .Where(point =>
                !double.IsNaN(point.X) &&
                !double.IsNaN(point.Y) &&
                !double.IsNaN(point.Z))
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

    private static SmartMassingFootprint? FootprintForMassingOpening(SmartMassingDraft draft, SmartMassingOpening opening)
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

    private void EnsureMassingCamera()
    {
        if (_massingViewport3D == null)
            return;

        _massingCamera3D ??= new PerspectiveCamera
        {
            FieldOfView = 42,
            UpDirection = new Vector3D(0, 1, 0),
        };
        _massingViewport3D.Camera = _massingCamera3D;
    }

    private void FitMassing3DView(bool resetAngles)
    {
        if (_massingCamera3D == null)
            EnsureMassingCamera();
        if (_massingCamera3D == null)
            return;

        if (resetAngles)
        {
            _massing3DYaw = -38;
            _massing3DPitch = 28;
        }

        _massing3DDistance = Math.Max(12, _massing3DSceneRadius * 2.65);
        UpdateMassing3DCamera();
    }

    private void SetMassing3DView(double yaw, double pitch)
    {
        _massing3DYaw = yaw;
        _massing3DPitch = Math.Clamp(pitch, -8, 88);
        FitMassing3DView(resetAngles: false);
    }

    private void UpdateMassing3DCamera()
    {
        if (_massingCamera3D == null)
            return;

        double yaw = _massing3DYaw * Math.PI / 180.0;
        double pitch = _massing3DPitch * Math.PI / 180.0;
        double horizontal = _massing3DDistance * Math.Cos(pitch);
        var position = new Point3D(
            _massing3DTarget.X + horizontal * Math.Sin(yaw),
            _massing3DTarget.Y + _massing3DDistance * Math.Sin(pitch),
            _massing3DTarget.Z + horizontal * Math.Cos(yaw));

        _massingCamera3D.Position = position;
        _massingCamera3D.LookDirection = _massing3DTarget - position;
        _massingCamera3D.UpDirection = new Vector3D(0, 1, 0);
    }

    private void MassingViewport3D_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_massingViewport3D == null)
            return;

        Point position = e.GetPosition(_massingViewport3D);
        _massing3DDragStart = position;
        _massing3DMouseDown = position;
        _massing3DMouseMoved = false;
        _massingViewport3D.CaptureMouse();
    }

    private void MassingViewport3D_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_massingViewport3D != null && _massing3DMouseDown != null && !_massing3DMouseMoved)
            TrySelectMassing3DObject(e.GetPosition(_massingViewport3D));

        _massing3DDragStart = null;
        _massing3DMouseDown = null;
        _massingViewport3D?.ReleaseMouseCapture();
    }

    private void MassingViewport3D_MouseMove(object sender, MouseEventArgs e)
    {
        if (_massingViewport3D == null || _massing3DDragStart == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point current = e.GetPosition(_massingViewport3D);
        Vector delta = current - _massing3DDragStart.Value;
        if (delta.Length > 2.5)
            _massing3DMouseMoved = true;
        _massing3DDragStart = current;
        _massing3DYaw += delta.X * 0.45;
        _massing3DPitch = Math.Clamp(_massing3DPitch - delta.Y * 0.35, -8, 88);
        UpdateMassing3DCamera();
    }

    private void TrySelectMassing3DObject(Point point)
    {
        if (_massingViewport3D == null)
            return;

        Massing3DObjectInfo? selected = null;
        VisualTreeHelper.HitTest(
            _massingViewport3D,
            null,
            result =>
            {
                if (result is RayHitTestResult ray &&
                    ray.ModelHit is GeometryModel3D model &&
                    _massing3DObjectInfo.TryGetValue(model, out Massing3DObjectInfo? info))
                {
                    selected = info;
                    return HitTestResultBehavior.Stop;
                }

                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(point));

        if (selected != null)
            SelectMassing3DObject(selected);
    }

    private void SelectMassing3DObject(Massing3DObjectInfo info)
    {
        _selectedMassing3DObjectId = info.Id;
        SelectFirstMassingMarker(info.SourceMarkerIds);
        RefreshMassing3DPreview(_currentMassingDraft);

        string sources = info.SourceMarkerIds.Count == 0
            ? "no source marker"
            : string.Join(", ", info.SourceMarkerIds);
        if (_massingViewportStatusText != null)
        {
            _massingViewportStatusText.Text =
                $"Selected: {info.Label} ({info.Kind}) | source: {sources}" +
                (string.IsNullOrWhiteSpace(info.Notes) ? "" : $" | {info.Notes}");
        }

        TxtStatus.Text = info.SourceMarkerIds.Count == 0
            ? $"Selected 3D {info.Kind}: {info.Label}."
            : $"Selected 3D {info.Kind}: {info.Label}; source marker selected in 3D Massing table.";
    }

    private void SelectFirstMassingMarker(IReadOnlyList<string> sourceMarkerIds)
    {
        if (_massingMarkerList == null || sourceMarkerIds.Count == 0)
            return;

        foreach (string markerId in sourceMarkerIds)
            if (SelectMassingMarkerById(markerId))
                return;
    }

    private void MassingViewport3D_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_massingCamera3D == null)
            return;

        double factor = e.Delta > 0 ? 0.88 : 1.14;
        _massing3DDistance = Math.Clamp(_massing3DDistance * factor, 4, Math.Max(20, _massing3DSceneRadius * 16));
        UpdateMassing3DCamera();
    }
}
