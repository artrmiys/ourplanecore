using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OurPlanCore;

public partial class MainWindow
{
    // 3D wall, slab, roof guide, roof plane, and issue rendering.

    private void RenderThreeDWallModel(bool fitCamera)
    {
        _threeDWallHitMap.Clear();
        _threeDFloorSlabHitMap.Clear();
        ClearThreeDRoofSceneHitMaps();
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
        // While a roof is being dragged, keep the scene center fixed so the
        // building and grid stay put and only the roof slides. Recentering on
        // the moving roof's bounds makes the whole background appear to drift.
        bool roofDragging = _threeDRoofMoveModeEnabled || IsThreeDRoofGizmoDragging;
        double centerX, centerZ;
        if (roofDragging && _threeDSceneCenterValid)
        {
            centerX = _threeDSceneCenterX;
            centerZ = _threeDSceneCenterZ;
        }
        else
        {
            centerX = (minX + maxX) / 2;
            centerZ = (minZ + maxZ) / 2;
            _threeDSceneCenterX = centerX;
            _threeDSceneCenterZ = centerZ;
            _threeDSceneCenterValid = true;
        }
        double spanX = Math.Max(1, maxX - minX);
        double spanZ = Math.Max(1, maxZ - minZ);
        _threeDViewerSceneRadius = Math.Max(Math.Max(spanX, spanZ), Math.Max(maxY, 8));
        // Orbit around the object's body, lifted a bit above the roof top so
        // the model rotates about a point just over it rather than its base.
        if (!roofDragging)
            _threeDViewerPivotY = maxY * 0.6;

        ThreeDViewerViewport.Children.Clear();
        ThreeDViewerViewport.Children.Add(new ModelVisual3D { Content = BuildThreeDWallModelGroup(centerX, centerZ) });
        if (_threeDSideViewport != null)
        {
            _threeDSideViewport.Children.Clear();
            _threeDSideViewport.Children.Add(new ModelVisual3D { Content = BuildThreeDWallModelGroup(centerX, centerZ) });
        }

        if (fitCamera)
        {
            _threeDViewerTarget = new Point3D(0, _threeDViewerPivotY, 0);
            _threeDSideViewerTarget = new Point3D(0, _threeDViewerPivotY, 0);
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
            (double ox, double oy, double oz) = IsRoofSlab(slab) ? RoofOffsetFor(slab.RoofGroupId) : (0, 0, 0);
            xs.AddRange(slab.Points.Select(point => point.XFeet + ox));
            zs.AddRange(slab.Points.Select(point => point.ZFeet + oz));
            maxY = Math.Max(maxY, slab.ElevationFeet + oy + Math.Max(0.02, slab.ThicknessFeet));
        }

        foreach (ThreeDRoofGuide guide in _threeDRoofGuides)
        {
            (double ox, double oy, double oz) = RoofOffsetFor(guide.RoofGroupId);
            xs.AddRange(guide.Points.Select(point => point.XFeet + ox));
            zs.AddRange(guide.Points.Select(point => point.ZFeet + oz));
            maxY = Math.Max(maxY, guide.ElevationFeet + oy + 0.25);
        }

        foreach (ThreeDRoofPlane plane in _threeDRoofPlanes)
        foreach (ThreeDRoofVertex point in plane.Points)
        {
            (double ox, double oy, double oz) = RoofOffsetFor(plane.RoofGroupId);
            xs.Add(point.XFeet + ox);
            zs.Add(point.ZFeet + oz);
            maxY = Math.Max(maxY, point.YFeet + oy);
        }

        foreach (ThreeDRoofIssue issue in _threeDRoofIssues)
        {
            (double ox, double oy, double oz) = RoofOffsetFor(issue.RoofGroupId);
            xs.Add(issue.XFeet + ox);
            zs.Add(issue.ZFeet + oz);
            maxY = Math.Max(maxY, issue.YFeet + oy + 0.5);
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

        ThreeDRoofSurface roofSurface = ThreeDRoofSurface.Build(RenderedThreeDRoofPlanes(), RafterPlumbDropFor);

        foreach (ThreeDFloorSlab slab in _threeDFloorSlabs)
            AddThreeDFloorSlabMesh(group, slab, centerX, centerZ);

        foreach (ThreeDWallSegment wall in _threeDWallElements)
            AddThreeDWallMesh(group, wall, centerX, centerZ, roofSurface);

        ThreeDRoofRenderBoundaryEdges roofBoundaryEdges = ThreeDRoofRenderBoundaryEdges.Build(_threeDRoofPlanes);
        foreach (ThreeDRoofPlane plane in _threeDRoofPlanes)
            AddThreeDRoofPlaneMesh(group, plane, centerX, centerZ, roofBoundaryEdges);
        AddThreeDRoofRafterMeshes(group, centerX, centerZ);

        // Once a roof group has built plane geometry, the editable base guides
        // (eave/rake bars) just clutter the surfaces. But the generated
        // ridge/hip/valley seams should read as one clean pronounced crease
        // along where the slopes meet (the interior plane-intersection edges are
        // otherwise cancelled, so the crease would look torn). Draw seams as a
        // crisp edge on the crease; keep full guide bars only for groups not yet
        // built into a mesh.
        foreach (ThreeDRoofGuide guide in _threeDRoofGuides)
        {
            bool groupHasPlanes = _threeDRoofPlanes.Any(plane => SameRoofGroup(plane.RoofGroupId, guide.RoofGroupId));
            if (groupHasPlanes)
            {
                if (IsGeneratedRoofSeamGuide(guide))
                    AddThreeDRoofSeamEdge(group, guide, centerX, centerZ);
                continue;
            }
            AddThreeDRoofGuideMesh(group, guide, centerX, centerZ);
        }

        foreach (ThreeDRoofIssue issue in _threeDRoofIssues)
            AddThreeDRoofIssueMarker(group, issue, centerX, centerZ);
        AddThreeDRoofMoveGizmo(group, centerX, centerZ);
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

    private void AddThreeDWallMesh(Model3DGroup group, ThreeDWallSegment wall, double centerX, double centerZ, ThreeDRoofSurface roofSurface)
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
        Color color = selected ? Color.FromRgb(245, 158, 11) : ToCleanMeshTint(ParseWallColor(wall.Color));
        double opacity = selected ? 0.94 : 0.8;

        // Top-of-structure walls follow the roof underside: a flat eave wall
        // gets clipped to the eave, a gable wall rises into a triangle up to
        // the ridge. Lower-floor walls (well below the eave) keep a flat top.
        var mesh = WallReachesRoof(roofSurface, topY)
            ? BuildRoofFollowingWallMesh(wall, centerX, centerZ, sx, sz, dx, dz, length, nx, nz, halfThickness, baseY, topY, roofSurface)
            : BuildFlatWallMesh(sx, sz, ex, ez, nx, nz, halfThickness, baseY, topY);

        var brush = new SolidColorBrush(color) { Opacity = opacity };
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        _threeDWallHitMap[model] = wall;
        group.Children.Add(model);
    }

    private bool WallReachesRoof(ThreeDRoofSurface roofSurface, double wallTopY) =>
        roofSurface.HasFaces && wallTopY >= roofSurface.BaseElevationFeet - 1.0;

    private static MeshGeometry3D BuildFlatWallMesh(
        double sx, double sz, double ex, double ez,
        double nx, double nz, double halfThickness, double baseY, double topY) =>
        new()
        {
            Positions = new Point3DCollection
            {
                new(sx + nx * halfThickness, baseY, sz + nz * halfThickness),
                new(ex + nx * halfThickness, baseY, ez + nz * halfThickness),
                new(ex - nx * halfThickness, baseY, ez - nz * halfThickness),
                new(sx - nx * halfThickness, baseY, sz - nz * halfThickness),
                new(sx + nx * halfThickness, topY, sz + nz * halfThickness),
                new(ex + nx * halfThickness, topY, ez + nz * halfThickness),
                new(ex - nx * halfThickness, topY, ez - nz * halfThickness),
                new(sx - nx * halfThickness, topY, sz - nz * halfThickness),
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

    // A wall whose top profile follows the roof underside along its length.
    // Sampled densely so a ridge crossing the wall reads as a gable peak.
    private MeshGeometry3D BuildRoofFollowingWallMesh(
        ThreeDWallSegment wall, double centerX, double centerZ,
        double sx, double sz, double dx, double dz, double length,
        double nx, double nz, double halfThickness, double baseY, double plateTopY,
        ThreeDRoofSurface roofSurface)
    {
        int samples = Math.Clamp((int)Math.Ceiling(length / 1.0), 2, 96);
        var positions = new Point3DCollection();
        var topYs = new double[samples + 1];
        for (int i = 0; i <= samples; i++)
        {
            double t = (double)i / samples;
            double cx = sx + dx * t;
            double cz = sz + dz * t;
            // Query in world feet (surface built before centering), backing out
            // the roof placement offset so trimming follows the moved roof.
            double? roofY = roofSurface.HeightAt(cx + centerX, cz + centerZ);
            double top = roofY.HasValue ? roofY.Value : plateTopY;
            // Never below the plate (no gaps under eaves); never below base.
            topYs[i] = Math.Max(baseY + 0.05, Math.Max(plateTopY, top));

            positions.Add(new Point3D(cx + nx * halfThickness, baseY, cz + nz * halfThickness)); // side+ bottom
            positions.Add(new Point3D(cx + nx * halfThickness, topYs[i], cz + nz * halfThickness)); // side+ top
            positions.Add(new Point3D(cx - nx * halfThickness, baseY, cz - nz * halfThickness)); // side- bottom
            positions.Add(new Point3D(cx - nx * halfThickness, topYs[i], cz - nz * halfThickness)); // side- top
        }

        var indices = new Int32Collection();
        void Quad(int a, int b, int c, int d)
        {
            indices.Add(a); indices.Add(b); indices.Add(c);
            indices.Add(a); indices.Add(c); indices.Add(d);
        }

        for (int i = 0; i < samples; i++)
        {
            int p = i * 4;
            int q = (i + 1) * 4;
            Quad(p + 0, p + 1, q + 1, q + 0); // + side
            Quad(p + 2, p + 3, q + 3, q + 2); // - side
            Quad(p + 1, p + 3, q + 3, q + 1); // top ridge strip
            Quad(p + 0, p + 2, q + 2, q + 0); // bottom
        }

        int last = samples * 4;
        Quad(0, 1, 3, 2);                     // start cap
        Quad(last + 0, last + 1, last + 3, last + 2); // end cap

        _ = wall;
        return new MeshGeometry3D { Positions = positions, TriangleIndices = indices };
    }

    private void AddThreeDFloorSlabMesh(Model3DGroup group, ThreeDFloorSlab slab, double centerX, double centerZ)
    {
        if (IsRoofSlab(slab) &&
            _threeDRoofPlanes.Any(plane => SameRoofGroup(plane.RoofGroupId, slab.RoofGroupId)))
        {
            return;
        }

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

        (double ox, double oy, double oz) = IsRoofSlab(slab) ? RoofOffsetFor(slab.RoofGroupId) : (0, 0, 0);
        double thickness = Math.Clamp(slab.ThicknessFeet, 0.03, 2.0);
        double bottomY = Math.Max(0, slab.ElevationFeet + oy);
        double topY = bottomY + thickness;
        bool selected = _selectedThreeDFloorSlab != null &&
                        string.Equals(_selectedThreeDFloorSlab.Id, slab.Id, StringComparison.Ordinal);
        bool selectedRoof = IsRoofSlab(slab) && SameRoofGroup(slab.RoofGroupId, ActiveThreeDRoofGroupId());
        Color color = selected
            ? Color.FromRgb(59, 130, 246)
            : selectedRoof
                ? Color.FromRgb(245, 158, 11)
                : ToCleanMeshTint(ParseWallColor(slab.Color));

        var positions = new Point3DCollection();
        foreach (ThreeDPoint point in triangulation.Points)
            positions.Add(new Point3D(point.XFeet + ox - centerX, bottomY, point.ZFeet + oz - centerZ));
        foreach (ThreeDPoint point in triangulation.Points)
            positions.Add(new Point3D(point.XFeet + ox - centerX, topY, point.ZFeet + oz - centerZ));

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
        var brush = new SolidColorBrush(color) { Opacity = selected || selectedRoof ? 0.7 : (IsRoofSlab(slab) ? 0.62 : 0.34) };
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        if (IsRoofSlab(slab))
            RegisterThreeDRoofMeshHit(model, slab.RoofGroupId);
        else
            _threeDFloorSlabHitMap[model] = slab;
        group.Children.Add(model);
    }

    private static bool IsGeneratedRoofSeamGuide(ThreeDRoofGuide guide) =>
        string.Equals(guide.Status, ThreeDRoofPreviewBuilder.GeneratedSeamStatus, StringComparison.OrdinalIgnoreCase);

    // Draw a generated ridge/hip/valley as a single crisp edge sitting right on
    // the crease (the seam guide already merges collinear pieces into one clean
    // line). Sits a hair above the surface to avoid z-fighting with the two
    // slope faces, and uses the seam's kind color so the crease reads clearly.
    private void AddThreeDRoofSeamEdge(Model3DGroup group, ThreeDRoofGuide guide, double centerX, double centerZ)
    {
        if (guide.Points.Count < 2)
            return;

        Color color = ParseWallColor(string.IsNullOrWhiteSpace(guide.Color)
            ? ThreeDRoofGuideKinds.Color(guide.Kind)
            : guide.Color);
        (double ox, double oy, double oz) = RoofOffsetFor(guide.RoofGroupId);
        for (int i = 1; i < guide.Points.Count; i++)
        {
            ThreeDRoofGuidePoint a = guide.Points[i - 1];
            ThreeDRoofGuidePoint b = guide.Points[i];
            var va = new ThreeDRoofVertex { XFeet = a.XFeet, YFeet = a.YFeet, ZFeet = a.ZFeet };
            var vb = new ThreeDRoofVertex { XFeet = b.XFeet, YFeet = b.YFeet, ZFeet = b.ZFeet };
            GeometryModel3D edge = AddThreeDRoofPlaneEdgeMesh(
                group, va, vb, ox - centerX, oy + 0.05, oz - centerZ, color, 0.05);
            RegisterThreeDRoofMeshHit(edge, guide.RoofGroupId);
        }
    }

    private void AddThreeDRoofGuideMesh(Model3DGroup group, ThreeDRoofGuide guide, double centerX, double centerZ)
    {
        if (guide.Points.Count < 2)
            return;

        Color color = ParseWallColor(string.IsNullOrWhiteSpace(guide.Color)
            ? ThreeDRoofGuideKinds.Color(guide.Kind)
            : guide.Color);
        (double ox, double oy, double oz) = RoofOffsetFor(guide.RoofGroupId);
        for (int i = 1; i < guide.Points.Count; i++)
        {
            ThreeDRoofGuidePoint a = guide.Points[i - 1];
            ThreeDRoofGuidePoint b = guide.Points[i];
            double startY = RoofGuidePointRenderY(guide, a) + oy;
            double endY = RoofGuidePointRenderY(guide, b) + oy;
            AddThreeDGuideSegmentMesh(
                group,
                a.XFeet + ox - centerX,
                a.ZFeet + oz - centerZ,
                b.XFeet + ox - centerX,
                b.ZFeet + oz - centerZ,
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

    private void AddThreeDRoofPlaneMesh(
        Model3DGroup group,
        ThreeDRoofPlane plane,
        double centerX,
        double centerZ,
        ThreeDRoofRenderBoundaryEdges roofBoundaryEdges)
    {
        if (plane.Points.Count < 3)
            return;

        List<ThreeDRoofVertex> renderPoints = plane.Points.ToList();
        if (ProjectedRoofArea(renderPoints) < -0.0001)
            renderPoints.Reverse();

        (double ox, double oy, double oz) = RoofOffsetFor(plane.RoofGroupId);
        int n = renderPoints.Count;
        var positions = new Point3DCollection();
        foreach (ThreeDRoofVertex p in renderPoints)
            positions.Add(new Point3D(p.XFeet + ox - centerX, p.YFeet + oy, p.ZFeet + oz - centerZ));

        var mesh = new MeshGeometry3D { Positions = positions };
        if (!TryAddProjectedRoofTriangles(mesh, renderPoints))
            AddFanRoofTriangles(mesh, n);
        // One flat normal for the whole face so every triangle shades
        // identically. Otherwise per-triangle normals make the triangulation
        // diagonals read as faint lines across the middle of the plane.
        ApplyFlatFaceNormals(mesh);

        bool selectedRoof = SameRoofGroup(plane.RoofGroupId, ActiveThreeDRoofGroupId());
        // Slopes render in their source takeoff's color so editing that color is
        // visible on the model; the active roof reads a touch more vivid.
        // Directional light (not a neutral tint) carries the form difference
        // between adjacent faces.
        string? takeoffColorHex = ResolveRoofGroupTakeoffColor(plane.RoofGroupId);
        Color planeColor = ToVisibleRoofColor(
            ParseWallColor(string.IsNullOrWhiteSpace(takeoffColorHex) ? plane.Color : takeoffColorHex),
            selectedRoof);
        bool rafterFace = IsRafterFace(plane);
        if (rafterFace)
        {
            // Faces with rafters read warm so the picked set is obvious.
            planeColor = Color.FromRgb(
                (byte)Math.Min(255, planeColor.R + 45),
                (byte)Math.Max(0, planeColor.G - 5),
                (byte)Math.Max(0, planeColor.B - 35));
        }
        var brush = new SolidColorBrush(planeColor)
        {
            Opacity = rafterFace ? 0.82 : selectedRoof ? 1.0 : 0.96,
        };
        Material material = CreateRoofFaceMaterial(brush);
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        RegisterThreeDRoofMeshHit(model, plane.RoofGroupId);
        RegisterThreeDRoofFaceHit(model, plane);
        group.Children.Add(model);

        // Outline only the outer roof boundary. Interior plane intersections
        // should read from geometry/shading, not from extra bars laid over the
        // surface.
        Color edgeColor = selectedRoof ? Color.FromRgb(245, 158, 11) : Color.FromRgb(38, 50, 64);
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            bool boundary = roofBoundaryEdges.IsBoundary(plane.RoofGroupId, renderPoints[i], renderPoints[next]);
            if (!boundary)
                continue;

            GeometryModel3D edge = AddThreeDRoofPlaneEdgeMesh(
                group,
                renderPoints[i],
                renderPoints[next],
                ox - centerX,
                oy + 0.035,
                oz - centerZ,
                edgeColor,
                0.06);
            RegisterThreeDRoofMeshHit(edge, plane.RoofGroupId);
        }
    }

    private static GeometryModel3D AddThreeDRoofPlaneEdgeMesh(
        Model3DGroup group,
        ThreeDRoofVertex a,
        ThreeDRoofVertex b,
        double offsetX,
        double offsetY,
        double offsetZ,
        Color color,
        double halfWidth)
    {
        double sx = a.XFeet + offsetX;
        double sz = a.ZFeet + offsetZ;
        double ex = b.XFeet + offsetX;
        double ez = b.ZFeet + offsetZ;
        double dx = ex - sx;
        double dz = ez - sz;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length <= 0.001)
            return new GeometryModel3D();

        double nx = -dz / length;
        double nz = dx / length;
        double halfHeight = Math.Max(0.016, halfWidth * 0.45);
        double sy = a.YFeet + offsetY;
        double ey = b.YFeet + offsetY;
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new(sx + nx * halfWidth, sy - halfHeight, sz + nz * halfWidth),
                new(ex + nx * halfWidth, ey - halfHeight, ez + nz * halfWidth),
                new(ex - nx * halfWidth, ey - halfHeight, ez - nz * halfWidth),
                new(sx - nx * halfWidth, sy - halfHeight, sz - nz * halfWidth),
                new(sx + nx * halfWidth, sy + halfHeight, sz + nz * halfWidth),
                new(ex + nx * halfWidth, ey + halfHeight, ez + nz * halfWidth),
                new(ex - nx * halfWidth, ey + halfHeight, ez - nz * halfWidth),
                new(sx - nx * halfWidth, sy + halfHeight, sz - nz * halfWidth),
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
        var brush = new SolidColorBrush(color) { Opacity = 0.88 };
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        group.Children.Add(model);
        return model;
    }

    // Assign a single averaged (Newell) normal to every vertex so the mesh
    // shades as one flat plane and the triangulation diagonals disappear.
    private static void ApplyFlatFaceNormals(MeshGeometry3D mesh)
    {
        Point3DCollection p = mesh.Positions;
        if (p.Count < 3)
            return;

        double nx = 0, ny = 0, nz = 0;
        for (int i = 0; i < p.Count; i++)
        {
            Point3D a = p[i];
            Point3D b = p[(i + 1) % p.Count];
            nx += (a.Y - b.Y) * (a.Z + b.Z);
            ny += (a.Z - b.Z) * (a.X + b.X);
            nz += (a.X - b.X) * (a.Y + b.Y);
        }

        var normal = new Vector3D(nx, ny, nz);
        if (normal.LengthSquared < 1e-9)
            normal = new Vector3D(0, 1, 0);
        normal.Normalize();
        if (normal.Y < -0.0001)
            normal *= -1;

        var normals = new Vector3DCollection(p.Count);
        for (int i = 0; i < p.Count; i++)
            normals.Add(normal);
        mesh.Normals = normals;
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
        (double ox, double oy, double oz) = RoofOffsetFor(issue.RoofGroupId);
        double y = Math.Max(0.2, issue.YFeet + oy);
        AddThreeDViewerBox(
            group,
            new Point3D(issue.XFeet + ox - centerX, y, issue.ZFeet + oz - centerZ),
            0.8,
            0.8,
            0.8,
            color,
            issue.Severity == "error" ? 0.96 : 0.84);
        AddThreeDViewerBox(
            group,
            new Point3D(issue.XFeet + ox - centerX, Math.Max(0.05, y / 2.0), issue.ZFeet + oz - centerZ),
            0.08,
            Math.Max(0.1, y),
            0.08,
            color,
            0.7);
    }
}
