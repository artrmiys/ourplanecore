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
    // 3D wall state, roof move controls, build commands, model load/save, and bounds.

    private readonly List<ThreeDWallSegment> _threeDWallElements = [];
    private readonly List<ThreeDFloorSlab> _threeDFloorSlabs = [];
    private readonly List<ThreeDFloorLevel> _threeDFloorLevels = [];
    private readonly Dictionary<GeometryModel3D, ThreeDWallSegment> _threeDWallHitMap = [];
    private readonly Dictionary<GeometryModel3D, ThreeDFloorSlab> _threeDFloorSlabHitMap = [];
    private TabItem? _threeDTab;
    private ThreeDWallSegment? _selectedThreeDWall;
    private ThreeDFloorSlab? _selectedThreeDFloorSlab;
    private string _threeDModelSource = "";
    private bool _threeDRoofMoveModeEnabled;
    private double _threeDSceneCenterX;
    private double _threeDSceneCenterZ;
    private bool _threeDSceneCenterValid;
    private double _threeDViewerPivotY;

    private void ToggleThreeDRoofMoveMode()
    {
        if (!_threeDRoofMoveModeEnabled &&
            !RequireModule(ModuleId.ThreeD, "Move 3D roof"))
        {
            return;
        }

        if (!_threeDRoofMoveModeEnabled && !EnsureThreeDEditable("move the 3D roof"))
            return;

        _threeDRoofMoveModeEnabled = !_threeDRoofMoveModeEnabled;
        TxtStatus.Text = _threeDRoofMoveModeEnabled
            ? "3D Move Roof: drag in the viewer to slide the roof over the walls. Toggle off when aligned."
            : "3D Move Roof off.";
        UpdateThreeDEditor();
    }

    private void ResetThreeDRoofOffset()
    {
        if (!RequireModule(ModuleId.ThreeD, "Reset 3D roof position"))
            return;

        if (!EnsureThreeDEditable("reset the 3D roof position"))
            return;

        ThreeDRoofPlacement? placement = ActiveThreeDRoofPlacement();
        if (placement == null)
        {
            TxtStatus.Text = "3D Move Roof: no roof is selected.";
            return;
        }

        if (placement.OffsetXFeet == 0 && placement.OffsetYFeet == 0 && placement.OffsetZFeet == 0)
        {
            TxtStatus.Text = "3D Move Roof: roof is already at its generated position.";
            return;
        }

        placement.OffsetXFeet = 0;
        placement.OffsetYFeet = 0;
        placement.OffsetZFeet = 0;
        RenderThreeDWallModel(fitCamera: false);
        UpdateThreeDRoofMoveControls();
        SaveCurrentThreeDModel();
        TxtStatus.Text = $"3D Move Roof: {placement.Label} position reset to generated alignment.";
        LogThreeD($"Roof placement offset reset: {placement.Label}.");
    }

    // Slide the roof on the ground plane (X/Z) from a screen drag, using the
    // camera basis so the roof tracks the cursor regardless of view angle.
    private void NudgeThreeDRoofOffsetFromDrag(PerspectiveCamera camera, Vector delta, double distance)
    {
        ThreeDRoofPlacement? placement = ActiveThreeDRoofPlacement();
        if (placement == null || !ThreeDEditingAllowed)
            return;

        Vector3D look = camera.LookDirection;
        if (look.LengthSquared < 1e-6)
            look = new Vector3D(0, 0, -1);
        look.Normalize();
        Vector3D up = camera.UpDirection;
        if (up.LengthSquared < 1e-6)
            up = new Vector3D(0, 1, 0);
        up.Normalize();
        Vector3D right = Vector3D.CrossProduct(look, up);
        if (right.LengthSquared < 1e-6)
            right = new Vector3D(1, 0, 0);
        right.Normalize();
        Vector3D forward = new(look.X, 0, look.Z);
        if (forward.LengthSquared < 1e-6)
            forward = new Vector3D(0, 0, -1);
        forward.Normalize();

        double scale = Math.Clamp(distance * 0.0025, 0.01, 2.5);
        Vector3D move = right * (delta.X * scale) - forward * (delta.Y * scale);
        placement.OffsetXFeet += move.X;
        placement.OffsetZFeet += move.Z;
        RenderThreeDWallModel(fitCamera: false);
        UpdateThreeDRoofMoveControls();
    }

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
        if (!IsModuleEnabled(ModuleId.ThreeD))
            return false;

        return TakeoffItemsFor3D(anchor).Any(item =>
            item.Measurements.Any(measurement =>
                OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "line" &&
                measurement.Points.Count >= 2));
    }

    private void Build3DWallsFromTakeoffSelection(TreeViewItem? anchor, bool switchTo3DTab)
    {
        if (!RequireModule(ModuleId.ThreeD, "Build 3D walls"))
            return;

        if (!EnsureThreeDEditable("build 3D walls"))
            return;

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
        if (!RequireModule(ModuleId.ThreeD, "Auto-build 3D model"))
            return;

        if (!EnsureThreeDEditable("auto-build the 3D model"))
            return;

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
        if (!IsModuleEnabled(ModuleId.ThreeD))
        {
            ApplyThreeDModel(null);
            BuildCleanThreeDViewerScene();
            return;
        }

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
        _threeDRoofPlacements.Clear();
        _threeDRoofRafterSettings.Clear();
        _threeDWallHitMap.Clear();
        _threeDFloorSlabHitMap.Clear();
        ClearThreeDMeshIssueLogKeys();
        _selectedThreeDWall = null;
        _selectedThreeDFloorSlab = null;
        _threeDModelSource = model?.Source ?? "";

        if (model == null)
        {
            RefreshThreeDRoofGuideOverlay();
            RefreshThreeDRoofSelector();
            UpdateThreeDEditor();
            return;
        }

        _threeDWallElements.AddRange(model.Walls);
        _threeDFloorSlabs.AddRange(model.Slabs);
        _threeDFloorLevels.AddRange(model.Levels);
        _threeDRoofGuides.AddRange(model.RoofGuides);
        _threeDRoofPlanes.AddRange(model.RoofPlanes);
        _threeDRoofIssues.AddRange(model.RoofIssues);
        _threeDRoofPlacements.AddRange(model.RoofPlacements.Select(CloneThreeDRoofPlacement));
        _threeDRoofRafterSettings.AddRange(model.RoofRafters);
        NormalizeThreeDRoofGroupsInMemory();
        _activeThreeDRoofGroupId = _threeDRoofPlacements.FirstOrDefault()?.RoofGroupId ?? "";
        RebuildPersistedGeneratedRoofIfNeeded();
        ReflowThreeDModelLevels();
        RefreshThreeDRoofGuideOverlay();
        RefreshThreeDRoofSelector();
        LogThreeD($"Loaded 3D model: {_threeDWallElements.Count} wall segment(s), {_threeDFloorSlabs.Count} slab(s), {_threeDRoofGuides.Count} roof edge(s), {_threeDRoofIssues.Count} roof issue(s).");
        UpdateThreeDEditor();
    }

    private void RebuildPersistedGeneratedRoofIfNeeded()
    {
        string active = _activeThreeDRoofGroupId;
        int rebuiltCount = 0;
        foreach (string groupId in CurrentRoofGroupIds().ToList())
        {
            bool hasGeneratedRoof =
                _threeDRoofPlanes.Any(plane => SameRoofGroup(plane.RoofGroupId, groupId)) ||
                _threeDRoofGuides.Any(guide =>
                    SameRoofGroup(guide.RoofGroupId, groupId) &&
                    string.Equals(guide.Status, ThreeDRoofPreviewBuilder.GeneratedSeamStatus, StringComparison.OrdinalIgnoreCase));
            if (!hasGeneratedRoof ||
                !_threeDFloorSlabs.Any(slab => IsRoofSlab(slab) && SameRoofGroup(slab.RoofGroupId, groupId)) ||
                !_threeDRoofGuides.Any(guide => SameRoofGroup(guide.RoofGroupId, groupId)))
            {
                continue;
            }

            ThreeDRoofBuildResult rebuilt = ThreeDRoofBuildService.Build(CurrentThreeDModelForRoofGroup(groupId));
            if (rebuilt.PlaneBuildBlocked || rebuilt.Planes.Count == 0)
                continue;

            ReplaceActiveRoofBuildResult(groupId, rebuilt);
            rebuiltCount++;
        }

        _activeThreeDRoofGroupId = active;
        if (rebuiltCount > 0)
            LogThreeD($"Regenerated persisted roof group(s): {rebuiltCount}.");
    }

    private ThreeDWallModel CurrentThreeDModel()
    {
        NormalizeThreeDRoofGroupsInMemory();
        ThreeDRoofPlacement? activePlacement = ActiveThreeDRoofPlacement();
        return new ThreeDWallModel
        {
            Source = string.IsNullOrWhiteSpace(_threeDModelSource) ? "viewer" : _threeDModelSource,
            Levels = _threeDFloorLevels.ToList(),
            Walls = _threeDWallElements.ToList(),
            Slabs = _threeDFloorSlabs.ToList(),
            RoofGuides = SnapshotThreeDRoofGuides(),
            RoofPlanes = SnapshotThreeDRoofPlanes(),
            RoofIssues = SnapshotThreeDRoofIssues(),
            RoofPlacements = _threeDRoofPlacements.Select(CloneThreeDRoofPlacement).ToList(),
            RoofRafters = _threeDRoofRafterSettings.ToList(),
            RoofOffsetXFeet = activePlacement?.OffsetXFeet ?? 0,
            RoofOffsetYFeet = activePlacement?.OffsetYFeet ?? 0,
            RoofOffsetZFeet = activePlacement?.OffsetZFeet ?? 0,
        };
    }

    private void SaveCurrentThreeDModel(bool allowEmpty = false)
    {
        if (!IsCurrentJobWritable)
        {
            AppLog.Warn("Blocked a 3D model save because the current job is read-only.");
            return;
        }

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
            return OurPlanCoreJobStore.TryReadPage(measurement.PageFolder)?.ScaleMetersPerPt ?? 0;

        return _currentPage?.ScaleMetersPerPt > 0 ? _currentPage.ScaleMetersPerPt : _viewport.ScaleMetersPerPt;
    }
}
