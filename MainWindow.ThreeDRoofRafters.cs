using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OurPlanCore;

// Rafters on 3D roof faces: pick individual faces in the viewer or rafter the
// whole active roof at once. Settings persist per roof group in the 3D model
// JSON; picked faces persist as plan anchors so they survive plane rebuilds.
public partial class MainWindow
{
    private readonly List<ThreeDRoofRafterSettings> _threeDRoofRafterSettings = [];
    private readonly Dictionary<GeometryModel3D, string> _threeDRoofFaceHitMap = [];
    private bool _threeDRafterFaceMode;

    private static readonly double[] RafterSpacingOptions = [12, 16, 19.2, 24];

    private void InitializeRafterControls()
    {
        CmbRafterSpacing.ItemsSource = RafterSpacingOptions.Select(s => $"{s:0.#}\"").ToList();
        CmbRafterSpacing.SelectedIndex = 1;
        CmbRafterSize.ItemsSource = ThreeDRoofRafterMembers.All;
        CmbRafterSize.SelectedItem = "2x10";
    }

    private ThreeDRoofRafterSettings RafterSettingsForGroup(string roofGroupId, bool create)
    {
        ThreeDRoofRafterSettings? existing = _threeDRoofRafterSettings.FirstOrDefault(settings =>
            string.Equals(settings.RoofGroupId, roofGroupId, StringComparison.OrdinalIgnoreCase));
        if (existing != null || !create)
            return existing ?? new ThreeDRoofRafterSettings { RoofGroupId = roofGroupId };

        var created = new ThreeDRoofRafterSettings { RoofGroupId = roofGroupId };
        ApplyRafterControlSelection(created);
        _threeDRoofRafterSettings.Add(created);
        return created;
    }

    private void ApplyRafterControlSelection(ThreeDRoofRafterSettings settings)
    {
        if (CmbRafterSpacing.SelectedIndex >= 0 && CmbRafterSpacing.SelectedIndex < RafterSpacingOptions.Length)
            settings.SpacingInches = RafterSpacingOptions[CmbRafterSpacing.SelectedIndex];
        if (CmbRafterSize.SelectedItem is string size && !string.IsNullOrWhiteSpace(size))
            settings.MemberSize = size;
    }

    private void RegisterThreeDRoofFaceHit(GeometryModel3D model, ThreeDRoofPlane plane)
    {
        if (ThreeDRoofRafterService.IsEnvelopeFace(plane))
            _threeDRoofFaceHitMap[model] = plane.Id;
    }

    private void ClearThreeDRoofFaceHitMap() =>
        _threeDRoofFaceHitMap.Clear();

    // Every active layout for rendering/summary: resolved fresh from current
    // planes so plane rebuilds and setting edits stay consistent.
    private List<(ThreeDRoofRafterSettings Settings, ThreeDRoofRafterPlaneLayout Layout)> CurrentRafterLayouts()
    {
        var results = new List<(ThreeDRoofRafterSettings, ThreeDRoofRafterPlaneLayout)>();
        foreach (ThreeDRoofRafterSettings settings in _threeDRoofRafterSettings)
        {
            if (!settings.IsActive)
                continue;

            foreach (ThreeDRoofPlane plane in ThreeDRoofRafterService.ResolvePlanes(settings, _threeDRoofPlanes))
            {
                ThreeDRoofRafterPlaneLayout? layout = ThreeDRoofRafterService.ComputeForPlane(plane, settings);
                if (layout != null)
                    results.Add((settings, layout));
            }
        }

        return results;
    }

    private bool IsRafterFace(ThreeDRoofPlane plane)
    {
        ThreeDRoofRafterSettings settings = RafterSettingsForGroup(plane.RoofGroupId, create: false);
        return settings.IsActive &&
               ThreeDRoofRafterService.ResolvePlanes(settings, [plane]).Count > 0;
    }

    // Plumb drop per face for wall trimming: with rafters on, the stored
    // plane is the rafter TOP, so walls stop one member-depth lower.
    private double RafterPlumbDropFor(ThreeDRoofPlane plane)
    {
        if (!IsRafterFace(plane))
            return 0;

        ThreeDRoofRafterSettings settings = RafterSettingsForGroup(plane.RoofGroupId, create: false);
        if (!ThreeDRoofRafterService.TryFitPlane(plane.Points, out double a, out double b, out _))
            return 0;

        double slope = Math.Sqrt(a * a + b * b);
        return ThreeDRoofRafterMembers.DepthInches(settings.MemberSize) / 12.0 * Math.Sqrt(1.0 + slope * slope);
    }

    private bool TryToggleRafterFaceAt(System.Windows.Controls.Viewport3D viewport, Point point)
    {
        if (!IsModuleEnabled(ModuleId.ThreeD) || !_threeDRafterFaceMode)
            return false;

        HitTestResult? hit = VisualTreeHelper.HitTest(viewport, point);
        if (hit is not RayMeshGeometry3DHitTestResult ray ||
            ray.ModelHit is not GeometryModel3D model ||
            !_threeDRoofFaceHitMap.TryGetValue(model, out string? planeId))
        {
            return false;
        }

        ThreeDRoofPlane? plane = _threeDRoofPlanes.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, planeId, StringComparison.Ordinal));
        if (plane == null)
            return false;

        ThreeDRoofRafterSettings settings = RafterSettingsForGroup(plane.RoofGroupId, create: true);
        if (!string.Equals(settings.Mode, ThreeDRoofRafterSettings.ModeFaces, StringComparison.OrdinalIgnoreCase))
        {
            // Switching from off/all to explicit faces starts a fresh pick set.
            settings.Mode = ThreeDRoofRafterSettings.ModeFaces;
            settings.Faces.Clear();
        }

        ThreeDRoofRafterFaceAnchor? inside = settings.Faces.FirstOrDefault(anchor =>
            ThreeDRoofRafterService.PlanContains(plane, anchor.XFeet, anchor.ZFeet));
        if (inside != null)
        {
            settings.Faces.Remove(inside);
        }
        else
        {
            (double x, double z) = ThreeDRoofRafterService.PlanCentroid(plane);
            settings.Faces.Add(new ThreeDRoofRafterFaceAnchor { XFeet = x, ZFeet = z });
        }

        SaveCurrentThreeDModel();
        RenderThreeDWallModel(fitCamera: false);
        ReportRafterSummary(settings);
        return true;
    }

    private void ReportRafterSummary(ThreeDRoofRafterSettings settings)
    {
        var layouts = CurrentRafterLayouts()
            .Where(entry => ReferenceEquals(entry.Settings, settings))
            .Select(entry => entry.Layout)
            .ToList();
        string summary = ThreeDRoofRafterService.FormatSummary(layouts, settings, _viewport.UnitMode);
        TxtStatus.Text = summary;
        LogThreeD(summary);
    }

    private void Btn3dRafterFaces_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.ThreeD, "Pick 3D roof faces"))
            return;

        if (_threeDRoofPlanes.Count == 0)
        {
            TxtStatus.Text = "Rafters: generate the roof first (Roof Base -> Select Edge -> Generate Roof).";
            return;
        }

        _threeDRafterFaceMode = !_threeDRafterFaceMode;
        TxtStatus.Text = _threeDRafterFaceMode
            ? "Rafters: click roof faces in the 3D view to toggle rafters on each. Click Pick Faces again to finish."
            : "Rafters: face picking off.";
    }

    private void Btn3dRafterAll_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.ThreeD, "Build 3D roof rafters"))
            return;

        var groupIds = _threeDRoofPlanes
            .Where(ThreeDRoofRafterService.IsEnvelopeFace)
            .Select(plane => plane.RoofGroupId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (groupIds.Count == 0)
        {
            TxtStatus.Text = "Rafters: generate the roof first (Roof Base -> Select Edge -> Generate Roof).";
            return;
        }

        ThreeDRoofRafterSettings? last = null;
        foreach (string groupId in groupIds)
        {
            ThreeDRoofRafterSettings settings = RafterSettingsForGroup(groupId, create: true);
            ApplyRafterControlSelection(settings);
            settings.Mode = ThreeDRoofRafterSettings.ModeAll;
            settings.Faces.Clear();
            last = settings;
        }

        _threeDRafterFaceMode = false;
        SaveCurrentThreeDModel();
        RenderThreeDWallModel(fitCamera: false);
        if (last != null)
            ReportRafterSummary(last);
    }

    private void Btn3dRafterClear_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.ThreeD, "Clear 3D roof rafters"))
            return;

        if (_threeDRoofRafterSettings.All(settings => !settings.IsActive))
        {
            TxtStatus.Text = "Rafters: nothing to clear.";
            return;
        }

        foreach (ThreeDRoofRafterSettings settings in _threeDRoofRafterSettings)
        {
            settings.Mode = ThreeDRoofRafterSettings.ModeOff;
            settings.Faces.Clear();
        }

        _threeDRafterFaceMode = false;
        SaveCurrentThreeDModel();
        RenderThreeDWallModel(fitCamera: false);
        TxtStatus.Text = "Rafters cleared.";
    }

    private void CmbRafter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized ||
            !IsModuleEnabled(ModuleId.ThreeD) ||
            _threeDRoofRafterSettings.Count == 0)
            return;

        ThreeDRoofRafterSettings? last = null;
        foreach (ThreeDRoofRafterSettings settings in _threeDRoofRafterSettings)
        {
            ApplyRafterControlSelection(settings);
            if (settings.IsActive)
                last = settings;
        }

        SaveCurrentThreeDModel();
        RenderThreeDWallModel(fitCamera: false);
        if (last != null)
            ReportRafterSummary(last);
    }

    // ── 3D rendering ─────────────────────────────────────────────────────────

    private void AddThreeDRoofRafterMeshes(Model3DGroup group, double centerX, double centerZ)
    {
        foreach ((ThreeDRoofRafterSettings settings, ThreeDRoofRafterPlaneLayout layout) in CurrentRafterLayouts())
        {
            (double ox, double oy, double oz) = RoofOffsetFor(layout.RoofGroupId);
            double depthFeet = ThreeDRoofRafterMembers.DepthInches(settings.MemberSize) / 12.0;
            double halfWidth = ThreeDRoofRafterMembers.WidthInches / 24.0;

            // Downward face normal of Y = a*X + b*Z + c.
            double a = layout.PlaneA, b = layout.PlaneB;
            double normalLength = Math.Sqrt(a * a + 1.0 + b * b);
            var down = new Vector3D(a / normalLength, -1.0 / normalLength, b / normalLength);
            // Level (contour) direction across the slope — rafter width axis.
            double slope = Math.Max(1e-6, Math.Sqrt(a * a + b * b));
            var across = new Vector3D(-b / slope, 0, a / slope);

            var material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x8B, 0x5E, 0x3C)));
            foreach (ThreeDRoofRafterBar bar in layout.Bars)
            {
                var start = new Point3D(bar.X1Feet + ox - centerX, bar.Y1Feet + oy, bar.Z1Feet + oz - centerZ);
                var end = new Point3D(bar.X2Feet + ox - centerX, bar.Y2Feet + oy, bar.Z2Feet + oz - centerZ);
                GeometryModel3D model = AddRafterBarMesh(group, start, end, across, down, halfWidth, depthFeet, material);
                RegisterThreeDRoofMeshHit(model, layout.RoofGroupId);
            }
        }
    }

    private static GeometryModel3D AddRafterBarMesh(
        Model3DGroup group,
        Point3D start,
        Point3D end,
        Vector3D across,
        Vector3D down,
        double halfWidth,
        double depthFeet,
        Material material)
    {
        Vector3D w = across * halfWidth;
        Vector3D d = down * depthFeet;
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                start - w, end - w, end + w, start + w,             // top face on the plane
                start - w + d, end - w + d, end + w + d, start + w + d, // underside
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
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        group.Children.Add(model);
        return model;
    }
}
