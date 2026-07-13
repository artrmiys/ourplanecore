using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    // 3D roof base construction from selected, auto, and RF area sources.

    private void BuildRoofFromRfAreas(TreeViewItem? anchor, bool switchTo3DTab)
    {
        if (!RequireModule(ModuleId.ThreeD, "Build 3D roof base"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "3D Roof Base: open a job first.";
            return;
        }

        IReadOnlyList<ThreeDRoofFootprintSource> sources = RoofFootprintSources(anchor, out string sourceLabel);
        if (sources.Count == 0)
        {
            TxtStatus.Text = "3D Roof Base: select area measurements or RF/roof area takeoffs first.";
            LogThreeD("Roof base skipped: no selected area measurements or RF/roof area takeoffs were found.");
            return;
        }

        double pitch = ResolveThreeDRoofPitchRisePerFoot();
        ThreeDRoofFootprintBuildResult result = ThreeDRoofFootprintBuildService.Build(
            sources,
            ResolveThreeDWallScale,
            ResolveRoofGuideElevationFeet(),
            pitch);
        if (result.Slabs.Count == 0)
        {
            TxtStatus.Text = result.SkippedNoScaleMeasurements > 0
                ? "3D Roof Base: selected roof areas need sheet scale before base generation."
                : "3D Roof Base: selected sources do not contain usable area measurements.";
            LogThreeD($"Roof base skipped: areas {sources.Count}, no usable base, no-scale {result.SkippedNoScaleMeasurements}, short {result.SkippedShortAreas}.");
            return;
        }

        AddGeneratedRoofFootprints(result, sourceLabel);
        _threeDModelSource = "rf_roof";
        if (switchTo3DTab)
            SelectRightWorkspaceTab("3D");

        RenderThreeDWallModel(fitCamera: true);
        SaveCurrentThreeDModel();
        SetThreeDRoofEdgeSelectMode(enabled: true);
        TxtStatus.Text = $"3D Roof Base: created {RoofGroupLabel(ActiveThreeDRoofGroupId())}, {result.Slabs.Count} base layer(s), {result.Guides.Count} selectable edge(s). Select slope edges and apply pitch.";
        LogThreeD($"Roof base created: {RoofGroupLabel(ActiveThreeDRoofGroupId())}, {result.SourceAreaCount} {sourceLabel} area(s), {result.Slabs.Count} base layer(s), {result.Guides.Count} edge(s) defaulted to Rake, current pitch {PitchLabel(pitch)}.");
    }

    private void BuildAutoThreeDRoof(TreeViewItem? anchor, bool switchTo3DTab)
    {
        if (!RequireModule(ModuleId.ThreeD, "Auto-build 3D roof"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "3D Auto Roof: open a job first.";
            return;
        }

        IReadOnlyList<ThreeDRoofFootprintSource> sources = RoofFootprintSources(anchor, out string sourceLabel);
        if (sources.Count > 0)
        {
            double pitch = ResolveThreeDRoofPitchRisePerFoot();
            ThreeDRoofFootprintBuildResult footprint = ThreeDRoofFootprintBuildService.Build(
                sources,
                ResolveThreeDWallScale,
                ResolveRoofGuideElevationFeet(),
                pitch);
            if (footprint.Slabs.Count == 0)
            {
                TxtStatus.Text = footprint.SkippedNoScaleMeasurements > 0
                    ? "3D Auto Roof: RF/roof areas need sheet scale before roof generation."
                    : "3D Auto Roof: RF/roof sources do not contain usable area measurements.";
                LogThreeD($"Auto roof skipped: sources {sources.Count}, no usable base, no-scale {footprint.SkippedNoScaleMeasurements}, short {footprint.SkippedShortAreas}.");
                return;
            }

            AddGeneratedRoofFootprints(footprint, sourceLabel);
            LogThreeD($"Auto roof base: {footprint.SourceAreaCount} {sourceLabel} area(s), {footprint.Slabs.Count} base layer(s), {footprint.Guides.Count} boundary edge(s).");
        }
        else if (!HasGeneratedRoofBase() && !_threeDFloorSlabs.Any(slab => string.Equals(slab.LevelKey, "roof", StringComparison.OrdinalIgnoreCase)))
        {
            TxtStatus.Text = "3D Auto Roof: select RF/roof area takeoffs first, or create a Roof Base manually.";
            LogThreeD("Auto roof skipped: no RF/roof area takeoffs or saved roof base.");
            return;
        }

        double autoPitch = ResolveThreeDRoofPitchRisePerFoot();
        // Revit-style one-click default: full hip - every boundary edge slopes
        // at the panel pitch. Robust for any footprint; the user can later
        // flip individual edges to Rake for a gable end.
        int eaves = MarkAllRoofBoundaryEdgesAsEave(autoPitch);
        if (eaves == 0)
        {
            TxtStatus.Text = "3D Auto Roof: no roof base boundary edges were available. Use Roof Base / select a roof area first.";
            LogThreeD("Auto roof skipped: roof base produced no editable boundary edges.");
            RenderThreeDWallModel(fitCamera: true);
            SaveCurrentThreeDModel();
            return;
        }

        if (switchTo3DTab)
            SelectRightWorkspaceTab("3D");

        BuildThreeDRoofPreview();
        string activeGroupId = ActiveThreeDRoofGroupId();
        int activePlaneCount = _threeDRoofPlanes.Count(plane => SameRoofGroup(plane.RoofGroupId, activeGroupId));
        TxtStatus.Text = $"3D Auto Roof: {RoofGroupLabel(activeGroupId)}, full hip from {eaves} eave edge(s), pitch {PitchLabel(autoPitch)}, generated {activePlaneCount} roof mesh face(s).";
        LogThreeD($"Auto roof: {RoofGroupLabel(activeGroupId)}, full hip, {eaves} eave edge(s), pitch {PitchLabel(autoPitch)}, generated {activePlaneCount} roof mesh face(s).");
    }

    // Every editable roof base boundary edge becomes a slope-defining eave at
    // the given pitch (full hip). Generated seams are left untouched.
    private int MarkAllRoofBoundaryEdgesAsEave(double pitch)
    {
        int count = 0;
        string groupId = ActiveThreeDRoofGroupId();
        foreach (ThreeDRoofGuide guide in _threeDRoofGuides
                     .Where(IsSelectableThreeDRoofBaseGuide)
                     .Where(guide => SameRoofGroup(guide.RoofGroupId, groupId)))
        {
            guide.Kind = ThreeDRoofGuideKinds.Eave;
            guide.Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Eave);
            guide.DefinesSlope = true;
            guide.PitchRisePerFoot = pitch;
            guide.Label = RelabelRoofGuide(guide);
            count++;
        }

        return count;
    }

    private IReadOnlyList<ThreeDRoofFootprintSource> RoofFootprintSources(TreeViewItem? anchor, out string sourceLabel)
    {
        IReadOnlyList<Measurement> selectedAreas = _viewport.GetSelectedMeasurements()
            .Where(IsUsableRoofAreaMeasurement)
            .ToList();
        if (selectedAreas.Count > 0)
        {
            sourceLabel = "selected";
            return selectedAreas
                .Select(measurement => new ThreeDRoofFootprintSource
                {
                    Measurement = measurement,
                    Item = FindTakeoffItemForMeasurement(measurement),
                })
                .ToList();
        }

        IReadOnlyList<TakeoffItem> selectedAreaItems = TakeoffItemsForSelection(anchor)
            .Where(item => ThreeDRoofFootprintBuildService.IsAreaTakeoff(item))
            .ToList();
        if (selectedAreaItems.Count > 0)
        {
            sourceLabel = "selected takeoff";
            return ThreeDRoofFootprintBuildService.SourcesFromItems(selectedAreaItems).ToList();
        }

        sourceLabel = "RF/roof";
        return ThreeDRoofFootprintBuildService.SourcesFromItems(
                _takeoffItems.Where(item => ThreeDRoofFootprintBuildService.IsRoofFootprintCandidate(_currentJob!, item)))
            .ToList();
    }

    private bool HasAutoRoofSourceForCurrentJob() =>
        IsModuleEnabled(ModuleId.ThreeD) &&
        _currentJob != null &&
        _takeoffItems.Any(item => ThreeDRoofFootprintBuildService.IsRoofFootprintCandidate(_currentJob, item));

    private static bool IsUsableRoofAreaMeasurement(Measurement measurement) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
        measurement.Points.Count >= 3;

    private bool CanBuildRoofBaseFromTakeoffSelection(TreeViewItem? anchor) =>
        IsModuleEnabled(ModuleId.ThreeD) &&
        _currentJob != null &&
        anchor != null &&
        TakeoffItemsForSelection(anchor).Any(item => ThreeDRoofFootprintBuildService.IsAreaTakeoff(item));
}
