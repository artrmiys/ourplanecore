using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OurPlaneCore;

public partial class MainWindow
{
    private readonly List<ThreeDWallSegment> _threeDWallElements = [];
    private readonly List<ThreeDFloorSlab> _threeDFloorSlabs = [];
    private readonly List<ThreeDFloorLevel> _threeDFloorLevels = [];
    private readonly Dictionary<GeometryModel3D, ThreeDWallSegment> _threeDWallHitMap = [];
    private readonly Dictionary<GeometryModel3D, ThreeDFloorSlab> _threeDFloorSlabHitMap = [];
    private TabItem? _threeDTab;
    private ThreeDWallSegment? _selectedThreeDWall;
    private ThreeDFloorSlab? _selectedThreeDFloorSlab;
    private string _threeDModelSource = "";

    private void Btn3dBuildWalls_Click(object sender, RoutedEventArgs e)
    {
        Build3DWallsFromTakeoffSelection(TakeoffsTree.SelectedItem as TreeViewItem, switchTo3DTab: false);
    }

    private void Btn3dAutoBuildWalls_Click(object sender, RoutedEventArgs e)
    {
        BuildAuto3DWallsFromTakeoffs(switchTo3DTab: false);
    }

    private bool CanBuild3DWallsFromTakeoffSelection(TreeViewItem? anchor)
    {
        return TakeoffItemsFor3D(anchor).Any(item =>
            item.Measurements.Any(measurement =>
                OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "line" &&
                measurement.Points.Count >= 2));
    }

    private void Build3DWallsFromTakeoffSelection(TreeViewItem? anchor, bool switchTo3DTab)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before building 3D walls.";
            return;
        }

        IReadOnlyList<TakeoffItem> items = TakeoffItemsFor3D(anchor);
        if (items.Count == 0)
        {
            TxtStatus.Text = "Select one or more line takeoffs before building 3D walls.";
            return;
        }

        List<ThreeDRoofGuide> roofGuides = SnapshotThreeDRoofGuides();
        List<ThreeDRoofPlane> roofPlanes = SnapshotThreeDRoofPlanes();
        List<ThreeDRoofIssue> roofIssues = SnapshotThreeDRoofIssues();
        ThreeDWallBuildResult result = ThreeDWallTakeoffBuilder.BuildWalls(items, _currentJob, ResolveThreeDWallScale);
        if (result.Walls.Count == 0)
        {
            TxtStatus.Text = result.SkippedNoScaleMeasurements > 0
                ? "3D Wall: selected line takeoffs need sheet scale before they can be built."
                : "3D Wall: selected takeoffs do not contain usable line measurements.";
            return;
        }

        _threeDWallElements.Clear();
        _threeDWallElements.AddRange(result.Walls);
        _threeDFloorSlabs.Clear();
        _threeDFloorLevels.Clear();
        RestoreThreeDRoofState(roofGuides, roofPlanes, roofIssues);
        _selectedThreeDWall = null;
        _selectedThreeDFloorSlab = null;
        _threeDModelSource = "selected_takeoffs";

        if (switchTo3DTab)
            SelectRightWorkspaceTab("3D");

        RenderThreeDWallModel(fitCamera: true);
        SaveCurrentThreeDModel();
        string skipped = result.SkippedNoScaleMeasurements > 0
            ? $" {result.SkippedNoScaleMeasurements} measurement(s) skipped: no scale."
            : "";
        TxtStatus.Text = $"3D Wall: built {result.Walls.Count} wall segment(s) from {items.Count} takeoff(s).{skipped}";
        LogThreeD($"Wall build: {result.Walls.Count} segment(s) from {items.Count} selected takeoff(s).{skipped}");
    }

    private void BuildAuto3DWallsFromTakeoffs(bool switchTo3DTab)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before auto-building 3D walls.";
            return;
        }

        ThreeDWallAutoBuildResult result = ThreeDWallAutoBuilder.Build(_currentJob, ResolveThreeDWallScale);
        if (result.Model.Walls.Count == 0 && result.Model.Slabs.Count == 0)
        {
            TxtStatus.Text = "3D Auto: no usable wall folders or sqft areas were found. Use folders like walls/1st and sqfts/1st.";
            return;
        }

        result.Model.RoofGuides.AddRange(SnapshotThreeDRoofGuides());
        result.Model.RoofPlanes.AddRange(SnapshotThreeDRoofPlanes());
        result.Model.RoofIssues.AddRange(SnapshotThreeDRoofIssues());
        ApplyThreeDModel(result.Model);
        if (switchTo3DTab)
            SelectRightWorkspaceTab("3D");

        RenderThreeDWallModel(fitCamera: true);
        SaveCurrentThreeDModel();
        string skipped = result.SkippedNoScaleMeasurements > 0
            ? $" {result.SkippedNoScaleMeasurements} measurement(s) skipped: no scale."
            : "";
        TxtStatus.Text = $"3D Auto: built {_threeDWallElements.Count} wall segment(s), {_threeDFloorSlabs.Count} slab(s), {_threeDFloorLevels.Count} level(s).{skipped}";
        LogThreeD($"Auto build: {_threeDWallElements.Count} wall segment(s), {_threeDFloorSlabs.Count} slab(s), {_threeDFloorLevels.Count} level(s).{skipped}");
        if (HasAutoRoofSourceForCurrentJob())
            BuildAutoThreeDRoof(anchor: null, switchTo3DTab: false);
    }

    private IReadOnlyList<TakeoffItem> TakeoffItemsFor3D(TreeViewItem? anchor)
    {
        if (anchor != null)
            return TakeoffItemsForSelection(anchor);

        return _activeItem != null ? [_activeItem] : [];
    }

    private void LoadThreeDModelForCurrentJob()
    {
        if (_currentJob == null)
        {
            ApplyThreeDModel(null);
            return;
        }

        ApplyThreeDModel(ThreeDModelStore.Load(_currentJob));
        RenderThreeDWallModel(fitCamera:
            _threeDWallElements.Count > 0 ||
            _threeDFloorSlabs.Count > 0 ||
            _threeDRoofGuides.Count > 0 ||
            _threeDRoofPlanes.Count > 0 ||
            _threeDRoofIssues.Count > 0);
    }

    private void ApplyThreeDModel(ThreeDWallModel? model)
    {
        _threeDWallElements.Clear();
        _threeDFloorSlabs.Clear();
        _threeDFloorLevels.Clear();
        _threeDRoofGuides.Clear();
        _threeDRoofPlanes.Clear();
        _threeDRoofIssues.Clear();
        _threeDWallHitMap.Clear();
        _threeDFloorSlabHitMap.Clear();
        ClearThreeDMeshIssueLogKeys();
        _selectedThreeDWall = null;
        _selectedThreeDFloorSlab = null;
        _threeDModelSource = model?.Source ?? "";

        if (model == null)
        {
            RefreshThreeDRoofGuideOverlay();
            UpdateThreeDEditor();
            return;
        }

        _threeDWallElements.AddRange(model.Walls);
        _threeDFloorSlabs.AddRange(model.Slabs);
        _threeDFloorLevels.AddRange(model.Levels);
        _threeDRoofGuides.AddRange(model.RoofGuides);
        _threeDRoofPlanes.AddRange(model.RoofPlanes);
        _threeDRoofIssues.AddRange(model.RoofIssues);
        ReflowThreeDModelLevels();
        RefreshThreeDRoofGuideOverlay();
        LogThreeD($"Loaded 3D model: {_threeDWallElements.Count} wall segment(s), {_threeDFloorSlabs.Count} slab(s), {_threeDRoofGuides.Count} roof edge(s), {_threeDRoofIssues.Count} roof issue(s).");
        UpdateThreeDEditor();
    }

    private ThreeDWallModel CurrentThreeDModel() =>
        new()
        {
            Source = string.IsNullOrWhiteSpace(_threeDModelSource) ? "viewer" : _threeDModelSource,
            Levels = _threeDFloorLevels.ToList(),
            Walls = _threeDWallElements.ToList(),
            Slabs = _threeDFloorSlabs.ToList(),
            RoofGuides = SnapshotThreeDRoofGuides(),
            RoofPlanes = SnapshotThreeDRoofPlanes(),
            RoofIssues = SnapshotThreeDRoofIssues(),
        };

    private void SaveCurrentThreeDModel(bool allowEmpty = false)
    {
        if (_currentJob == null ||
            (!allowEmpty &&
             _threeDWallElements.Count == 0 &&
             _threeDFloorSlabs.Count == 0 &&
             _threeDRoofGuides.Count == 0 &&
             _threeDRoofPlanes.Count == 0 &&
             _threeDRoofIssues.Count == 0))
        {
            return;
        }

        ThreeDModelStore.Save(_currentJob, CurrentThreeDModel());
        LogThreeD($"Saved 3D model -> {System.IO.Path.GetRelativePath(_currentJob.RootPath, ThreeDModelStore.ModelPath(_currentJob))}.");
    }

    private double ResolveThreeDWallScale(Measurement measurement)
    {
        if (measurement.ScaleMetersPerPt > 0)
            return measurement.ScaleMetersPerPt;

        if (!string.IsNullOrWhiteSpace(measurement.PageFolder))
            return OurPlaneCoreJobStore.TryReadPage(measurement.PageFolder)?.ScaleMetersPerPt ?? 0;

        return _currentPage?.ScaleMetersPerPt > 0 ? _currentPage.ScaleMetersPerPt : _viewport.ScaleMetersPerPt;
    }

    private void RenderThreeDWallModel(bool fitCamera)
    {
        _threeDWallHitMap.Clear();
        _threeDFloorSlabHitMap.Clear();
        if (_threeDWallElements.Count == 0 &&
            _threeDFloorSlabs.Count == 0 &&
            _threeDRoofGuides.Count == 0 &&
            _threeDRoofPlanes.Count == 0 &&
            _threeDRoofIssues.Count == 0)
        {
            BuildCleanThreeDViewerScene();
            string emptySummary = "3D viewer ready. Use Auto for walls/slabs, Wall for selected lines, or Roof Base for selected areas.";
            Txt3dManagerSummary.Text = emptySummary;
            if (_threeDSideSummaryText != null)
                _threeDSideSummaryText.Text = emptySummary;
            UpdateThreeDEditor();
            return;
        }

        (double minX, double maxX, double minZ, double maxZ, double maxY) = ThreeDModelBounds();
        double centerX = (minX + maxX) / 2;
        double centerZ = (minZ + maxZ) / 2;
        double spanX = Math.Max(1, maxX - minX);
        double spanZ = Math.Max(1, maxZ - minZ);
        _threeDViewerSceneRadius = Math.Max(Math.Max(spanX, spanZ), Math.Max(maxY, 8));

        ThreeDViewerViewport.Children.Clear();
        ThreeDViewerViewport.Children.Add(new ModelVisual3D { Content = BuildThreeDWallModelGroup(centerX, centerZ) });
        if (_threeDSideViewport != null)
        {
            _threeDSideViewport.Children.Clear();
            _threeDSideViewport.Children.Add(new ModelVisual3D { Content = BuildThreeDWallModelGroup(centerX, centerZ) });
        }

        if (fitCamera)
        {
            _threeDViewerTarget = new Point3D(0, 0, 0);
            _threeDSideViewerTarget = new Point3D(0, 0, 0);
            SetThreeDViewerView(-38, 28, ThreeDViewerFitDistance());
            SetThreeDSideViewerView(-38, 28, ThreeDViewerFitDistance());
        }

        string summary = ThreeDWallSummaryText();
        Txt3dManagerSummary.Text = summary;
        if (_threeDSideSummaryText != null)
            _threeDSideSummaryText.Text = summary;
        UpdateThreeDEditor();
    }

    private (double MinX, double MaxX, double MinZ, double MaxZ, double MaxY) ThreeDModelBounds()
    {
        var xs = new List<double>();
        var zs = new List<double>();
        double maxY = 1;

        foreach (ThreeDWallSegment wall in _threeDWallElements)
        {
            xs.Add(wall.StartXFeet);
            xs.Add(wall.EndXFeet);
            zs.Add(wall.StartZFeet);
            zs.Add(wall.EndZFeet);
            maxY = Math.Max(maxY, wall.BaseElevationFeet + wall.HeightFeet);
        }

        foreach (ThreeDFloorSlab slab in _threeDFloorSlabs)
        {
            xs.AddRange(slab.Points.Select(point => point.XFeet));
            zs.AddRange(slab.Points.Select(point => point.ZFeet));
            maxY = Math.Max(maxY, slab.ElevationFeet + Math.Max(0.02, slab.ThicknessFeet));
        }

        foreach (ThreeDRoofGuide guide in _threeDRoofGuides)
        {
            xs.AddRange(guide.Points.Select(point => point.XFeet));
            zs.AddRange(guide.Points.Select(point => point.ZFeet));
            maxY = Math.Max(maxY, guide.ElevationFeet + 0.25);
        }

        foreach (ThreeDRoofPlane plane in _threeDRoofPlanes)
        foreach (ThreeDRoofVertex point in plane.Points)
        {
            xs.Add(point.XFeet);
            zs.Add(point.ZFeet);
            maxY = Math.Max(maxY, point.YFeet);
        }

        foreach (ThreeDRoofIssue issue in _threeDRoofIssues)
        {
            xs.Add(issue.XFeet);
            zs.Add(issue.ZFeet);
            maxY = Math.Max(maxY, issue.YFeet + 0.5);
        }

        return (xs.Min(), xs.Max(), zs.Min(), zs.Max(), maxY);
    }

    private Model3DGroup BuildThreeDWallModelGroup(double centerX, double centerZ)
    {
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(76, 82, 90)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(245, 247, 250), new Vector3D(-0.45, -0.85, -0.35)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(130, 160, 190), new Vector3D(0.65, -0.35, 0.55)));
        AddThreeDWallGrid(group, centerX, centerZ);

        foreach (ThreeDFloorSlab slab in _threeDFloorSlabs)
            AddThreeDFloorSlabMesh(group, slab, centerX, centerZ);

        foreach (ThreeDWallSegment wall in _threeDWallElements)
            AddThreeDWallMesh(group, wall, centerX, centerZ);

        foreach (ThreeDRoofPlane plane in _threeDRoofPlanes)
            AddThreeDRoofPlaneMesh(group, plane, centerX, centerZ);

        foreach (ThreeDRoofGuide guide in _threeDRoofGuides)
            AddThreeDRoofGuideMesh(group, guide, centerX, centerZ);

        foreach (ThreeDRoofIssue issue in _threeDRoofIssues)
            AddThreeDRoofIssueMarker(group, issue, centerX, centerZ);
        return group;
    }

    private void AddThreeDWallGrid(Model3DGroup group, double centerX, double centerZ)
    {
        double gridRadius = Math.Ceiling(_threeDViewerSceneRadius / 10.0) * 10.0 + 10.0;
        for (double i = -gridRadius; i <= gridRadius; i += 10)
        {
            byte shade = Math.Abs(i) < 0.001 ? (byte)118 : (byte)78;
            AddThreeDViewerBox(group, new Point3D(i, -0.03, 0), 0.035, 0.035, gridRadius * 2, Color.FromRgb(shade, shade, shade), 0.85);
            AddThreeDViewerBox(group, new Point3D(0, -0.03, i), gridRadius * 2, 0.035, 0.035, Color.FromRgb(shade, shade, shade), 0.85);
        }

        _ = centerX;
        _ = centerZ;
    }

    private void AddThreeDWallMesh(Model3DGroup group, ThreeDWallSegment wall, double centerX, double centerZ)
    {
        double sx = wall.StartXFeet - centerX;
        double sz = wall.StartZFeet - centerZ;
        double ex = wall.EndXFeet - centerX;
        double ez = wall.EndZFeet - centerZ;
        double dx = ex - sx;
        double dz = ez - sz;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length <= 0.001)
            return;

        double nx = -dz / length;
        double nz = dx / length;
        double halfThickness = Math.Max(0.08, wall.ThicknessInches / 12.0 / 2.0);
        double height = Math.Max(0.1, wall.HeightFeet);
        double baseY = Math.Max(0, wall.BaseElevationFeet);
        double topY = baseY + height;
        bool selected = _selectedThreeDWall != null && string.Equals(_selectedThreeDWall.Id, wall.Id, StringComparison.Ordinal);
        Color color = selected ? Color.FromRgb(245, 158, 11) : ParseWallColor(wall.Color);
        double opacity = selected ? 0.94 : 0.76;

        var points = new Point3DCollection
        {
            new(sx + nx * halfThickness, baseY, sz + nz * halfThickness),
            new(ex + nx * halfThickness, baseY, ez + nz * halfThickness),
            new(ex - nx * halfThickness, baseY, ez - nz * halfThickness),
            new(sx - nx * halfThickness, baseY, sz - nz * halfThickness),
            new(sx + nx * halfThickness, topY, sz + nz * halfThickness),
            new(ex + nx * halfThickness, topY, ez + nz * halfThickness),
            new(ex - nx * halfThickness, topY, ez - nz * halfThickness),
            new(sx - nx * halfThickness, topY, sz - nz * halfThickness),
        };

        var mesh = new MeshGeometry3D
        {
            Positions = points,
            TriangleIndices = new Int32Collection
            {
                0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6,
                0, 4, 5, 0, 5, 1,
                1, 5, 6, 1, 6, 2,
                2, 6, 7, 2, 7, 3,
                3, 7, 4, 3, 4, 0,
            },
        };

        var brush = new SolidColorBrush(color) { Opacity = opacity };
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        _threeDWallHitMap[model] = wall;
        group.Children.Add(model);
    }

    private void AddThreeDFloorSlabMesh(Model3DGroup group, ThreeDFloorSlab slab, double centerX, double centerZ)
    {
        ThreeDPolygonTriangulation triangulation = ThreeDPolygonTriangulator.Triangulate(slab.Points);
        if (!triangulation.Success)
        {
            LogThreeDOnce(
                $"slab:{slab.Id}:{triangulation.Message}",
                $"Slab skipped: {slab.Label} - {triangulation.Message}.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(triangulation.Message))
        {
            LogThreeDOnce(
                $"slab:{slab.Id}:{triangulation.Message}",
                $"Slab cleaned: {slab.Label} - {triangulation.Message}.");
        }

        double thickness = Math.Clamp(slab.ThicknessFeet, 0.03, 2.0);
        double bottomY = Math.Max(0, slab.ElevationFeet);
        double topY = bottomY + thickness;
        bool selected = _selectedThreeDFloorSlab != null &&
                        string.Equals(_selectedThreeDFloorSlab.Id, slab.Id, StringComparison.Ordinal);
        Color color = selected ? Color.FromRgb(59, 130, 246) : ParseWallColor(slab.Color);

        var positions = new Point3DCollection();
        foreach (ThreeDPoint point in triangulation.Points)
            positions.Add(new Point3D(point.XFeet - centerX, bottomY, point.ZFeet - centerZ));
        foreach (ThreeDPoint point in triangulation.Points)
            positions.Add(new Point3D(point.XFeet - centerX, topY, point.ZFeet - centerZ));

        int n = triangulation.Points.Count;
        var indices = new Int32Collection();
        for (int i = 0; i < triangulation.TriangleIndices.Count; i += 3)
        {
            int a = triangulation.TriangleIndices[i];
            int b = triangulation.TriangleIndices[i + 1];
            int c = triangulation.TriangleIndices[i + 2];
            indices.Add(n + a);
            indices.Add(n + b);
            indices.Add(n + c);
            indices.Add(a);
            indices.Add(c);
            indices.Add(b);
        }

        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            indices.Add(i);
            indices.Add(n + i);
            indices.Add(n + next);
            indices.Add(i);
            indices.Add(n + next);
            indices.Add(next);
        }

        var mesh = new MeshGeometry3D
        {
            Positions = positions,
            TriangleIndices = indices,
        };
        var brush = new SolidColorBrush(color) { Opacity = selected ? 0.58 : 0.34 };
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        _threeDFloorSlabHitMap[model] = slab;
        group.Children.Add(model);
    }

    private void AddThreeDRoofGuideMesh(Model3DGroup group, ThreeDRoofGuide guide, double centerX, double centerZ)
    {
        if (guide.Points.Count < 2)
            return;

        Color color = ParseWallColor(string.IsNullOrWhiteSpace(guide.Color)
            ? ThreeDRoofGuideKinds.Color(guide.Kind)
            : guide.Color);
        for (int i = 1; i < guide.Points.Count; i++)
        {
            ThreeDRoofGuidePoint a = guide.Points[i - 1];
            ThreeDRoofGuidePoint b = guide.Points[i];
            double startY = RoofGuidePointRenderY(guide, a);
            double endY = RoofGuidePointRenderY(guide, b);
            AddThreeDGuideSegmentMesh(
                group,
                a.XFeet - centerX,
                a.ZFeet - centerZ,
                b.XFeet - centerX,
                b.ZFeet - centerZ,
                startY,
                endY,
                color);
        }
    }

    private static double RoofGuidePointRenderY(ThreeDRoofGuide guide, ThreeDRoofGuidePoint point) =>
        Math.Max(0.08, (point.YFeet > 0.0001 ? point.YFeet : guide.ElevationFeet) + 0.18);

    private static void AddThreeDGuideSegmentMesh(
        Model3DGroup group,
        double sx,
        double sz,
        double ex,
        double ez,
        double startY,
        double endY,
        Color color)
    {
        double dx = ex - sx;
        double dz = ez - sz;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length <= 0.001)
            return;

        double nx = -dz / length;
        double nz = dx / length;
        double halfWidth = 0.12;
        double halfHeight = 0.04;
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new(sx + nx * halfWidth, startY - halfHeight, sz + nz * halfWidth),
                new(ex + nx * halfWidth, endY - halfHeight, ez + nz * halfWidth),
                new(ex - nx * halfWidth, endY - halfHeight, ez - nz * halfWidth),
                new(sx - nx * halfWidth, startY - halfHeight, sz - nz * halfWidth),
                new(sx + nx * halfWidth, startY + halfHeight, sz + nz * halfWidth),
                new(ex + nx * halfWidth, endY + halfHeight, ez + nz * halfWidth),
                new(ex - nx * halfWidth, endY + halfHeight, ez - nz * halfWidth),
                new(sx - nx * halfWidth, startY + halfHeight, sz - nz * halfWidth),
            },
            TriangleIndices = new Int32Collection
            {
                0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6,
                0, 4, 5, 0, 5, 1,
                1, 5, 6, 1, 6, 2,
                2, 6, 7, 2, 7, 3,
                3, 7, 4, 3, 4, 0,
            },
        };
        var brush = new SolidColorBrush(color) { Opacity = 0.96 };
        var material = new DiffuseMaterial(brush);
        group.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
    }

    private void AddThreeDRoofPlaneMesh(Model3DGroup group, ThreeDRoofPlane plane, double centerX, double centerZ)
    {
        if (plane.Points.Count < 3)
            return;

        List<ThreeDRoofVertex> renderPoints = plane.Points.ToList();
        if (ProjectedRoofArea(renderPoints) < -0.0001)
            renderPoints.Reverse();

        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection(renderPoints
                .Select(point => new Point3D(point.XFeet - centerX, point.YFeet, point.ZFeet - centerZ))),
        };

        if (!TryAddProjectedRoofTriangles(mesh, renderPoints))
            AddFanRoofTriangles(mesh, renderPoints.Count);

        var brush = new SolidColorBrush(ParseWallColor(plane.Color))
        {
            Opacity = Math.Clamp(plane.Opacity, 0.12, 0.88),
        };
        var material = new DiffuseMaterial(brush);
        group.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
    }

    private static bool TryAddProjectedRoofTriangles(MeshGeometry3D mesh, IReadOnlyList<ThreeDRoofVertex> points)
    {
        if (Math.Abs(ProjectedRoofArea(points)) < 0.0001)
            return false;

        ThreeDPolygonTriangulation triangulation = ThreeDPolygonTriangulator.Triangulate(points
            .Select(point => new ThreeDPoint { XFeet = point.XFeet, ZFeet = point.ZFeet })
            .ToList());
        if (!triangulation.Success || triangulation.Points.Count != points.Count)
            return false;

        for (int i = 0; i < triangulation.TriangleIndices.Count; i += 3)
        {
            int a = triangulation.TriangleIndices[i];
            int b = triangulation.TriangleIndices[i + 1];
            int c = triangulation.TriangleIndices[i + 2];
            mesh.TriangleIndices.Add(a);
            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(c);
            mesh.TriangleIndices.Add(a);
            mesh.TriangleIndices.Add(c);
            mesh.TriangleIndices.Add(b);
        }

        return mesh.TriangleIndices.Count > 0;
    }

    private static void AddFanRoofTriangles(MeshGeometry3D mesh, int pointCount)
    {
        for (int i = 1; i < pointCount - 1; i++)
        {
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add(i);
        }
    }

    private static double ProjectedRoofArea(IReadOnlyList<ThreeDRoofVertex> points)
    {
        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            ThreeDRoofVertex a = points[i];
            ThreeDRoofVertex b = points[(i + 1) % points.Count];
            area += a.XFeet * b.ZFeet - b.XFeet * a.ZFeet;
        }

        return area / 2.0;
    }

    private void AddThreeDRoofIssueMarker(Model3DGroup group, ThreeDRoofIssue issue, double centerX, double centerZ)
    {
        Color color = ParseWallColor(string.IsNullOrWhiteSpace(issue.Color) ? "#DC2626" : issue.Color);
        double y = Math.Max(0.2, issue.YFeet);
        AddThreeDViewerBox(
            group,
            new Point3D(issue.XFeet - centerX, y, issue.ZFeet - centerZ),
            0.8,
            0.8,
            0.8,
            color,
            issue.Severity == "error" ? 0.96 : 0.84);
        AddThreeDViewerBox(
            group,
            new Point3D(issue.XFeet - centerX, Math.Max(0.05, y / 2.0), issue.ZFeet - centerZ),
            0.08,
            Math.Max(0.1, y),
            0.08,
            color,
            0.7);
    }

    private void SelectThreeDViewerWallAt(Viewport3D viewport, Point point)
    {
        if (_threeDWallHitMap.Count == 0 && _threeDFloorSlabHitMap.Count == 0)
            return;

        HitTestResult? hit = VisualTreeHelper.HitTest(viewport, point);
        if (hit is RayMeshGeometry3DHitTestResult ray &&
            ray.ModelHit is GeometryModel3D model &&
            _threeDWallHitMap.TryGetValue(model, out ThreeDWallSegment? wall))
        {
            _selectedThreeDWall = wall;
            _selectedThreeDFloorSlab = null;
            RenderThreeDWallModel(fitCamera: false);
            TxtStatus.Text = $"3D Wall selected: {wall.Label}, {wall.HeightFeet:F1} ft high, {wall.ThicknessInches:F0} in wide, base {wall.BaseElevationFeet:F1} ft.";
            return;
        }

        if (hit is RayMeshGeometry3DHitTestResult slabRay &&
            slabRay.ModelHit is GeometryModel3D slabModel &&
            _threeDFloorSlabHitMap.TryGetValue(slabModel, out ThreeDFloorSlab? slab))
        {
            _selectedThreeDWall = null;
            _selectedThreeDFloorSlab = slab;
            RenderThreeDWallModel(fitCamera: false);
            TxtStatus.Text = $"3D slab selected: {slab.Label}, elevation {slab.ElevationFeet:F1} ft.";
            return;
        }

        _selectedThreeDWall = null;
        _selectedThreeDFloorSlab = null;
        RenderThreeDWallModel(fitCamera: false);
    }

    private string ThreeDWallSummaryText()
    {
        var groups = _threeDWallElements
            .GroupBy(wall => wall.GroupKey)
            .Select(group =>
            {
                ThreeDWallSegment first = group.First();
                double length = group.Sum(WallLengthFeet);
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1} segment(s), {2:F1} ft LFT, {3:F1} ft high, {4:F0} in wide",
                    first.Label,
                    group.Count(),
                    length,
                    first.HeightFeet,
                    first.ThicknessInches);
            });

        string levelText = _threeDFloorLevels.Count == 0
            ? ""
            : "Levels: " + string.Join(", ", _threeDFloorLevels
                .OrderBy(level => level.Ordinal)
                .Select(level => $"{level.Label} base {level.BaseElevationFeet:F1} h {level.HeightFeet:F1}"));
        string slabText = _threeDFloorSlabs.Count == 0
            ? ""
            : $"Slabs: {_threeDFloorSlabs.Count}";
        string roofText = _threeDRoofGuides.Count == 0 && _threeDRoofPlanes.Count == 0 && _threeDRoofIssues.Count == 0
            ? ""
            : $"Roof: {_threeDRoofGuides.Count} edge(s), {_threeDRoofPlanes.Count} mesh face(s), {_threeDRoofIssues.Count} issue(s)";
        string selected = _selectedThreeDWall != null
            ? $"Selected: {_selectedThreeDWall.Label} | H {_selectedThreeDWall.HeightFeet:F1} ft | W {_selectedThreeDWall.ThicknessInches:F0} in | base {_selectedThreeDWall.BaseElevationFeet:F1}"
            : _selectedThreeDFloorSlab != null
                ? $"Selected slab: {_selectedThreeDFloorSlab.Label} | elev {_selectedThreeDFloorSlab.ElevationFeet:F1} ft"
                : "Click a wall to edit its height/thickness or apply the values to its group.";
        string body = string.Join("\n", groups);
        return $"3D model: {_threeDWallElements.Count} wall segment(s), {_threeDFloorSlabs.Count} slab(s)\n{levelText}\n{slabText}\n{roofText}\n{body}\n{selected}";
    }

    private void ApplyThreeDWallEditor(bool applyGroup)
    {
        if (_selectedThreeDWall == null)
            return;

        if (!TryReadThreeDWallEditor(out double heightFeet, out double thicknessInches))
            return;

        IEnumerable<ThreeDWallSegment> targets = applyGroup
            ? _threeDWallElements.Where(wall => string.Equals(wall.GroupKey, _selectedThreeDWall.GroupKey, StringComparison.Ordinal))
            : [_selectedThreeDWall!];
        int changed = 0;
        foreach (ThreeDWallSegment wall in targets)
        {
            wall.HeightFeet = heightFeet;
            wall.ThicknessInches = thicknessInches;
            changed++;
        }

        ReflowThreeDModelLevels();
        RenderThreeDWallModel(fitCamera: false);
        SaveCurrentThreeDModel();
        TxtStatus.Text = applyGroup
            ? $"3D Wall: updated {changed} segment(s) in group."
            : "3D Wall: updated selected segment.";
    }

    private bool TryReadThreeDWallEditor(out double heightFeet, out double thicknessInches)
    {
        heightFeet = 0;
        thicknessInches = 0;
        if (!TryParsePositiveThreeDValue(_threeDHeightBox?.Text, 0.1, 80, out heightFeet) ||
            !TryParsePositiveThreeDValue(_threeDThicknessBox?.Text, 0.5, 48, out thicknessInches))
        {
            TxtStatus.Text = "3D Wall: enter valid height in feet and width in inches.";
            return false;
        }

        return true;
    }

    private static bool TryParsePositiveThreeDValue(string? text, double min, double max, out double value)
    {
        string clean = (text ?? "").Trim().Replace(',', '.');
        return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               value >= min &&
               value <= max;
    }

    private void ReflowThreeDModelLevels()
    {
        if (_threeDWallElements.Count == 0 ||
            _threeDWallElements.All(wall => string.IsNullOrWhiteSpace(wall.LevelKey)))
        {
            return;
        }

        var levelHeights = _threeDWallElements
            .Select(wall => TryParseThreeDLevelOrdinal(wall.LevelKey, out int ordinal)
                ? new { Wall = wall, Ordinal = ordinal }
                : null)
            .Where(entry => entry != null)
            .GroupBy(entry => entry!.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Max(entry => entry!.Wall.HeightFeet));
        if (levelHeights.Count == 0)
            return;

        double defaultHeight = levelHeights.Values.Max();
        int maxOrdinal = levelHeights.Keys.Max();
        var baseByLevel = new Dictionary<int, double>();
        double currentBase = 0;
        for (int ordinal = 1; ordinal <= maxOrdinal; ordinal++)
        {
            baseByLevel[ordinal] = currentBase;
            currentBase += levelHeights.TryGetValue(ordinal, out double height)
                ? height
                : defaultHeight;
        }

        foreach (ThreeDWallSegment wall in _threeDWallElements)
        {
            if (TryParseThreeDLevelOrdinal(wall.LevelKey, out int ordinal) &&
                baseByLevel.TryGetValue(ordinal, out double baseElevation))
            {
                wall.BaseElevationFeet = baseElevation;
            }
        }

        foreach (ThreeDFloorSlab slab in _threeDFloorSlabs)
        {
            if (TryParseThreeDLevelOrdinal(slab.LevelKey, out int ordinal))
                slab.ElevationFeet = baseByLevel.TryGetValue(ordinal, out double baseElevation)
                    ? baseElevation
                    : Math.Max(0, ordinal - 1) * defaultHeight;
        }

        _threeDFloorLevels.Clear();
        foreach (int ordinal in levelHeights.Keys.OrderBy(key => key))
        {
            _threeDFloorLevels.Add(new ThreeDFloorLevel
            {
                Label = ThreeDLevelLabel(ordinal),
                Ordinal = ordinal,
                BaseElevationFeet = baseByLevel[ordinal],
                HeightFeet = levelHeights[ordinal],
            });
        }
    }

    private static bool TryParseThreeDLevelOrdinal(string text, out int ordinal)
    {
        ordinal = 0;
        string clean = (text ?? "").Trim().ToLowerInvariant();
        if (clean == "basement")
        {
            ordinal = 0;
            return true;
        }

        Match match = Regex.Match(clean, @"\b(?<n>\d+)");
        return match.Success && int.TryParse(match.Groups["n"].Value, out ordinal);
    }

    private static string ThreeDLevelLabel(int ordinal) =>
        ordinal switch
        {
            0 => "basement",
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{ordinal}th",
        };

    private static double WallLengthFeet(ThreeDWallSegment wall)
    {
        double dx = wall.EndXFeet - wall.StartXFeet;
        double dz = wall.EndZFeet - wall.StartZFeet;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private static Color ParseWallColor(string hex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color color)
                return color;
        }
        catch
        {
        }

        return Color.FromRgb(120, 144, 156);
    }
}
