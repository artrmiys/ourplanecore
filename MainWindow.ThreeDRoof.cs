using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    private readonly List<ThreeDRoofGuide> _threeDRoofGuides = [];
    private readonly List<ThreeDRoofPlane> _threeDRoofPlanes = [];
    private readonly List<ThreeDRoofIssue> _threeDRoofIssues = [];
    private readonly HashSet<string> _selectedThreeDRoofGuideIds = new(StringComparer.Ordinal);
    private string _selectedThreeDRoofGuideId = "";
    private bool _threeDRoofEdgeSelectModeEnabled;

    private void Btn3dBuildRoof_Click(object sender, RoutedEventArgs e) =>
        BuildThreeDRoofPreview();

    private void Btn3dRfRoof_Click(object sender, RoutedEventArgs e) =>
        BuildRoofFromRfAreas(TakeoffsTree.SelectedItem as TreeViewItem, switchTo3DTab: true);

    private void Btn3dAutoRoof_Click(object sender, RoutedEventArgs e) =>
        BuildAutoThreeDRoof(TakeoffsTree.SelectedItem as TreeViewItem, switchTo3DTab: true);

    private void Btn3dClearRoof_Click(object sender, RoutedEventArgs e) =>
        ClearThreeDRoof();

    private void Btn3dRoofEdges_Click(object sender, RoutedEventArgs e) =>
        ToggleThreeDRoofEdgeSelectMode();

    private void AddViewportThreeDMenuItems(ContextMenu menu, ViewportContextRequest request)
    {
        var roofMenu = new MenuItem { Header = "3D" };
        if (TryFindThreeDRoofGuideAt(request.PdfX, request.PdfY, out ThreeDRoofGuide? roofGuide) &&
            roofGuide != null)
        {
            if (IsRoofGuideMultiSelectModifierActive())
                ToggleThreeDRoofGuideSelection(roofGuide.Id);
            else
                SelectThreeDRoofGuide(roofGuide.Id);
            AddViewportThreeDRoofEdgeMenuItems(roofMenu, roofGuide);
            roofMenu.Items.Add(new Separator());
        }

        roofMenu.Items.Add(MakeMenuItem("Create Roof Base from Areas", _currentJob != null, () =>
            BuildRoofFromRfAreas(TakeoffsTree.SelectedItem as TreeViewItem, switchTo3DTab: true)));
        roofMenu.Items.Add(MakeMenuItem("Auto Roof from RF Areas", _currentJob != null, () =>
            BuildAutoThreeDRoof(TakeoffsTree.SelectedItem as TreeViewItem, switchTo3DTab: true)));
        roofMenu.Items.Add(MakeMenuItem(
            _threeDRoofEdgeSelectModeEnabled ? "Roof Edge Select Off" : "Roof Edge Select",
            _currentJob != null && _threeDRoofGuides.Count > 0,
            ToggleThreeDRoofEdgeSelectMode));
        roofMenu.Items.Add(MakeMenuItem("Draw Eave Segment", _currentJob != null && _threeDRoofGuides.Count > 0, StartThreeDRoofEaveGuideMode));
        roofMenu.Items.Add(MakeMenuItem("Use Selected Eave Lines", _currentJob != null && _threeDRoofGuides.Count > 0, () =>
            ApplySelectedEaveTakeoffsToRoofEdges(TakeoffsTree.SelectedItem as TreeViewItem)));
        roofMenu.Items.Add(MakeMenuItem("Generate Roof", CanBuildThreeDRoofPreview(), BuildThreeDRoofPreview));
        roofMenu.Items.Add(MakeMenuItem(
            "Clear Roof Base",
            HasGeneratedRoofBase() || _threeDRoofGuides.Count > 0 || _threeDRoofPlanes.Count > 0 || _threeDRoofIssues.Count > 0,
            ClearThreeDRoof));
        menu.Items.Add(roofMenu);
        _ = request;
    }

    private void ToggleThreeDRoofEdgeSelectMode() =>
        SetThreeDRoofEdgeSelectMode(!_threeDRoofEdgeSelectModeEnabled);

    private void SetThreeDRoofEdgeSelectMode(bool enabled)
    {
        if (enabled && _threeDRoofGuides.Count == 0)
        {
            TxtStatus.Text = "3D Roof: create Roof Base first, then select its edges.";
            return;
        }

        _threeDRoofEdgeSelectModeEnabled = enabled;
        _viewport.SetThreeDRoofEdgeSelectMode(enabled);
        TxtStatus.Text = enabled
            ? "3D Roof Edge Select: click roof base edges. Ctrl/Shift-click adds more."
            : "3D Roof Edge Select off.";
    }

    private void OnThreeDRoofGuideSelectionRequested(string guideId, bool additive)
    {
        if (string.IsNullOrWhiteSpace(guideId))
        {
            ClearThreeDRoofGuideSelection();
            return;
        }

        if (additive)
            ToggleThreeDRoofGuideSelection(guideId);
        else
            SelectThreeDRoofGuide(guideId);
    }

    private void AddViewportThreeDRoofEdgeMenuItems(MenuItem roofMenu, ThreeDRoofGuide guide)
    {
        string title = ThreeDRoofGuideKinds.Title(guide.Kind);
        int selectedCount = SelectedThreeDRoofGuideCount();
        bool guideSelected = _selectedThreeDRoofGuideIds.Contains(guide.Id);
        string targetLabel = selectedCount > 1
            ? $"{selectedCount} Selected Edges"
            : "Edge";
        roofMenu.Items.Add(new MenuItem
        {
            Header = $"Roof Edge - {title} | {RoofGuidePitchLabel(guide)}",
            IsEnabled = false,
        });
        roofMenu.Items.Add(MakeMenuItem(guideSelected ? "Remove Edge from Selection" : "Add Edge to Selection", true, () =>
            ToggleThreeDRoofGuideSelection(guide.Id)));
        roofMenu.Items.Add(MakeMenuItem("Clear Edge Selection", selectedCount > 0, ClearThreeDRoofGuideSelection));
        roofMenu.Items.Add(new Separator());
        roofMenu.Items.Add(MakeMenuItem($"Set {targetLabel} as Eave + Pitch {PitchLabel(ResolveThreeDRoofPitchRisePerFoot())}", selectedCount > 0, () =>
            SetSelectedThreeDRoofGuideKind(ThreeDRoofGuideKinds.Eave, applyPitch: true)));
        roofMenu.Items.Add(MakeMenuItem($"Set {targetLabel} as Rake", selectedCount > 0, () =>
            SetSelectedThreeDRoofGuideKind(ThreeDRoofGuideKinds.Rake, applyPitch: false)));
    }

    private void BuildRoofFromRfAreas(TreeViewItem? anchor, bool switchTo3DTab)
    {
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

        ReplaceGeneratedRoofFootprints(result);
        _threeDModelSource = "rf_roof";
        if (switchTo3DTab)
            SelectRightWorkspaceTab("3D");

        RenderThreeDWallModel(fitCamera: true);
        SaveCurrentThreeDModel();
        SetThreeDRoofEdgeSelectMode(enabled: true);
        TxtStatus.Text = $"3D Roof Base: created {result.Slabs.Count} roof base layer(s) with {result.Guides.Count} selectable edge(s). Click edges, then set Eave + pitch.";
        LogThreeD($"Roof base created: {result.SourceAreaCount} {sourceLabel} area(s), {result.Slabs.Count} base layer(s), {result.Guides.Count} edge(s) defaulted to Rake, current pitch {PitchLabel(pitch)}.");
    }

    private void BuildAutoThreeDRoof(TreeViewItem? anchor, bool switchTo3DTab)
    {
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

            ReplaceGeneratedRoofFootprints(footprint);
            LogThreeD($"Auto roof base: {footprint.SourceAreaCount} {sourceLabel} area(s), {footprint.Slabs.Count} base layer(s), {footprint.Guides.Count} boundary edge(s).");
        }
        else if (!HasGeneratedRoofBase() && !_threeDFloorSlabs.Any(slab => string.Equals(slab.LevelKey, "roof", StringComparison.OrdinalIgnoreCase)))
        {
            TxtStatus.Text = "3D Auto Roof: select RF/roof area takeoffs first, or create a Roof Base manually.";
            LogThreeD("Auto roof skipped: no RF/roof area takeoffs or saved roof base.");
            return;
        }

        double autoPitch = ResolveThreeDRoofPitchRisePerFoot();
        int measuredEaves = ApplyAvailableEaveTakeoffsToRoofEdges(anchor, autoPitch);
        ThreeDRoofAutoGuideResult auto = ThreeDRoofAutoGuideService.ApplyAutoEaves(_threeDRoofGuides, autoPitch);
        if (auto.EaveGuideCount == 0 && !_threeDRoofGuides.Any(ThreeDRoofPreviewBuilder.IsSlopeDefiningGuide))
        {
            TxtStatus.Text = auto.SkippedManualRegionCount > 0
                ? "3D Auto Roof: roof base has manual eaves, but no auto eave could be selected."
                : "3D Auto Roof: no roof base boundary edges were available for auto eaves.";
            LogThreeD($"Auto roof edge selection skipped: regions {auto.RoofRegionCount}, manual regions {auto.SkippedManualRegionCount}.");
            RenderThreeDWallModel(fitCamera: true);
            SaveCurrentThreeDModel();
            return;
        }

        if (switchTo3DTab)
            SelectRightWorkspaceTab("3D");

        BuildThreeDRoofPreview();
        string detail = auto.Messages.Count == 0 ? "" : " " + string.Join(" ", auto.Messages.Take(2));
        string measured = measuredEaves > 0 ? $" matched {measuredEaves} measured eave edge(s)," : "";
        TxtStatus.Text = $"3D Auto Roof:{measured} selected {auto.EaveGuideCount} auto eave edge(s), pitch {PitchLabel(autoPitch)}, generated {_threeDRoofPlanes.Count} roof mesh face(s).{detail}";
        LogThreeD($"Auto roof: matched {measuredEaves} measured eave edge(s), selected {auto.EaveGuideCount} auto eave edge(s), reset {auto.ResetGuideCount}, generated {_threeDRoofPlanes.Count} roof mesh face(s).");
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
        _currentJob != null &&
        _takeoffItems.Any(item => ThreeDRoofFootprintBuildService.IsRoofFootprintCandidate(_currentJob, item));

    private static bool IsUsableRoofAreaMeasurement(Measurement measurement) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
        measurement.Points.Count >= 3;

    private bool CanBuildRoofBaseFromTakeoffSelection(TreeViewItem? anchor) =>
        _currentJob != null &&
        anchor != null &&
        TakeoffItemsForSelection(anchor).Any(item => ThreeDRoofFootprintBuildService.IsAreaTakeoff(item));

    private void ReplaceGeneratedRoofFootprints(ThreeDRoofFootprintBuildResult result)
    {
        var measurementIds = result.Slabs
            .Select(slab => slab.MeasurementId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _threeDFloorSlabs.RemoveAll(slab =>
            slab.GroupKey.StartsWith(ThreeDRoofFootprintBuildService.GeneratedSlabGroupPrefix, StringComparison.OrdinalIgnoreCase) ||
            measurementIds.Contains(slab.MeasurementId));
        _threeDRoofGuides.RemoveAll(guide =>
            string.Equals(guide.Status, ThreeDRoofFootprintBuildService.GeneratedStatus, StringComparison.OrdinalIgnoreCase));

        _threeDFloorSlabs.AddRange(result.Slabs);
        _threeDRoofGuides.AddRange(result.Guides);
        _threeDRoofPlanes.Clear();
        _threeDRoofIssues.Clear();
        PruneThreeDRoofGuideSelection();
        RefreshThreeDRoofGuideOverlay();
    }

    private void SetSelectedThreeDRoofGuideKind(string kind, bool applyPitch)
    {
        IReadOnlyList<ThreeDRoofGuide> guides = SelectedThreeDRoofGuides();
        if (guides.Count == 0)
        {
            TxtStatus.Text = "3D Roof: select one or more roof edges first.";
            return;
        }

        string cleanKind = ThreeDRoofGuideKinds.Normalize(kind);
        double pitch = ResolveThreeDRoofPitchRisePerFoot();
        bool definesSlope = cleanKind == ThreeDRoofGuideKinds.Eave;
        foreach (ThreeDRoofGuide guide in guides)
        {
            guide.Kind = cleanKind;
            guide.Color = ThreeDRoofGuideKinds.Color(cleanKind);
            guide.DefinesSlope = definesSlope;
            guide.PitchRisePerFoot = applyPitch || definesSlope ? pitch : 0;
            guide.Label = RelabelRoofGuide(guide);
        }

        BuildThreeDRoofPreview();
        if (guides.Count == 1)
        {
            ThreeDRoofGuide guide = guides[0];
            TxtStatus.Text = cleanKind == ThreeDRoofGuideKinds.Eave
                ? $"3D Roof: {guide.Label} set to Eave, pitch {PitchLabel(guide.PitchRisePerFoot)}."
                : $"3D Roof: {guide.Label} set to {ThreeDRoofGuideKinds.Title(cleanKind)}.";
        }
        else
        {
            TxtStatus.Text = cleanKind == ThreeDRoofGuideKinds.Eave
                ? $"3D Roof: {guides.Count} selected edge(s) set to Eave, pitch {PitchLabel(pitch)}."
                : $"3D Roof: {guides.Count} selected edge(s) set to {ThreeDRoofGuideKinds.Title(cleanKind)}.";
        }

        LogThreeD(cleanKind == ThreeDRoofGuideKinds.Eave
            ? $"Roof edges updated: {guides.Count} selected edge(s), kind {cleanKind}, pitch {PitchLabel(pitch)}."
            : $"Roof edges updated: {guides.Count} selected edge(s), kind {cleanKind}.");
    }

    private void ApplyThreeDRoofPitchToSelectedEdges()
    {
        IReadOnlyList<ThreeDRoofGuide> guides = SelectedThreeDRoofGuides();
        if (guides.Count == 0)
        {
            TxtStatus.Text = "3D Roof: select one or more roof edges first.";
            return;
        }

        double pitch = ResolveThreeDRoofPitchRisePerFoot();
        foreach (ThreeDRoofGuide guide in guides)
        {
            guide.Kind = ThreeDRoofGuideKinds.Eave;
            guide.Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Eave);
            guide.DefinesSlope = true;
            guide.PitchRisePerFoot = pitch;
            guide.Label = RelabelRoofGuide(guide);
        }

        BuildThreeDRoofPreview();
        TxtStatus.Text = guides.Count == 1
            ? $"3D Roof: applied pitch {PitchLabel(pitch)} to {guides[0].Label}."
            : $"3D Roof: applied pitch {PitchLabel(pitch)} to {guides.Count} selected edge(s).";
        LogThreeD($"Roof edge pitch applied: {guides.Count} selected edge(s), {PitchLabel(pitch)}.");
    }

    private void ApplyThreeDRoofPitchToEaves()
    {
        double pitch = ResolveThreeDRoofPitchRisePerFoot();
        int changed = 0;
        foreach (ThreeDRoofGuide guide in _threeDRoofGuides.Where(ThreeDRoofPreviewBuilder.IsSlopeDefiningGuide))
        {
            guide.PitchRisePerFoot = pitch;
            changed++;
        }

        if (changed == 0)
        {
            TxtStatus.Text = "3D Roof: no slope-defining edges found to receive pitch.";
            return;
        }

        BuildThreeDRoofPreview();
        TxtStatus.Text = $"3D Roof: applied pitch {PitchLabel(pitch)} to {changed} eave edge(s).";
        LogThreeD($"Roof pitch applied to {changed} eave edge(s): {PitchLabel(pitch)}.");
    }

    // Revit-style per-edge apply: Defines Slope + pitch + overhang on the
    // selected roof base edge(s), then rebuild the U/S envelope.
    private void ApplyThreeDRoofEdgeProperties()
    {
        IReadOnlyList<ThreeDRoofGuide> guides = SelectedThreeDRoofGuides();
        if (guides.Count == 0)
        {
            TxtStatus.Text = "3D Roof: select one or more roof base edges on the sheet first.";
            return;
        }

        bool? checkbox = _threeDRoofDefinesSlopeBox?.IsChecked;
        string pitchText = (_threeDRoofEdgePitchBox?.Text ?? "").Trim();
        string overhangText = (_threeDRoofEdgeOverhangBox?.Text ?? "").Trim();

        bool hasPitch = RoofPitchText.TryParse(pitchText, out double pitch);
        bool hasOverhang = double.TryParse(
            overhangText.Replace(',', '.'),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double overhangInches);

        // Intent, not just the checkbox: entering a pitch means "this is a
        // sloped eave" (you do not pitch a non-slope edge). The checkbox only
        // forces slope OFF when the user explicitly unchecks it with no pitch.
        // null = mixed/untouched and no pitch -> leave each edge unchanged.
        bool? slope = hasPitch ? true
            : checkbox == true ? true
            : checkbox == false ? false
            : null;

        double fallbackPitch = ResolveThreeDRoofPitchRisePerFoot();
        foreach (ThreeDRoofGuide guide in guides)
        {
            if (slope == true)
            {
                guide.DefinesSlope = true;
                guide.Kind = ThreeDRoofGuideKinds.Eave;
                guide.Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Eave);
                guide.PitchRisePerFoot = hasPitch
                    ? pitch
                    : guide.PitchRisePerFoot > 0 ? guide.PitchRisePerFoot : fallbackPitch;
                guide.Label = RelabelRoofGuide(guide);
            }
            else if (slope == false)
            {
                guide.DefinesSlope = false;
                guide.Kind = ThreeDRoofGuideKinds.Rake;
                guide.Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Rake);
                guide.PitchRisePerFoot = 0;
                guide.Label = RelabelRoofGuide(guide);
            }

            if (hasOverhang)
                guide.OverhangFeet = Math.Max(0, overhangInches) / 12.0;
        }

        BuildThreeDRoofPreview();

        string slopePart = slope switch
        {
            true => "Defines Slope on",
            false => "Defines Slope off",
            _ => "slope unchanged",
        };
        string pitchPart = slope == true ? $", pitch {PitchLabel(hasPitch ? pitch : fallbackPitch)}" : "";
        string overhangPart = hasOverhang ? $", overhang {Math.Max(0, overhangInches):0.##} in" : "";
        TxtStatus.Text = $"3D Roof: {guides.Count} edge(s) - {slopePart}{pitchPart}{overhangPart}.";
        LogThreeD($"Roof edge properties applied: {guides.Count} edge(s), {slopePart}{pitchPart}{overhangPart}.");
    }

    private void ApplySelectedEaveTakeoffsToRoofEdges(TreeViewItem? anchor)
    {
        if (_threeDRoofGuides.Count == 0)
        {
            TxtStatus.Text = "3D Roof: create Roof Base before using eave line takeoffs.";
            return;
        }

        IReadOnlyList<Measurement> sources = SelectedRoofLineMeasurements(anchor);
        if (sources.Count == 0)
        {
            TxtStatus.Text = "3D Roof: select one or more eave line takeoffs first.";
            return;
        }

        double pitch = ResolveThreeDRoofPitchRisePerFoot();
        var changed = MarkRoofEdgesFromEaveMeasurements(sources, pitch, "matched_eave_takeoff");

        if (changed.Count == 0)
        {
            TxtStatus.Text = "3D Roof: selected eave lines did not match roof base edges.";
            return;
        }

        _selectedThreeDRoofGuideIds.Clear();
        foreach (string id in changed)
            _selectedThreeDRoofGuideIds.Add(id);
        _selectedThreeDRoofGuideId = changed.FirstOrDefault() ?? "";
        BuildThreeDRoofPreview();
        TxtStatus.Text = $"3D Roof: matched {changed.Count} roof edge(s) from selected eave line takeoff(s), pitch {PitchLabel(pitch)}.";
        LogThreeD($"Selected eave line takeoffs matched {changed.Count} roof edge(s), pitch {PitchLabel(pitch)}.");
    }

    private int ApplyAvailableEaveTakeoffsToRoofEdges(TreeViewItem? anchor, double pitch)
    {
        IReadOnlyList<Measurement> sources = AutoRoofLineMeasurements(anchor);
        if (sources.Count == 0)
            return 0;

        HashSet<string> changed = MarkRoofEdgesFromEaveMeasurements(sources, pitch, "matched_eave_takeoff");
        if (changed.Count > 0)
            LogThreeD($"Auto roof matched {changed.Count} eave edge(s) from saved eave line takeoff(s).");
        return changed.Count;
    }

    private HashSet<string> MarkRoofEdgesFromEaveMeasurements(
        IReadOnlyList<Measurement> sources,
        double pitch,
        string adjustmentStatus)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (Measurement measurement in sources)
        {
            for (int i = 1; i < measurement.Points.Count; i++)
            {
                SKPoint a = measurement.Points[i - 1];
                SKPoint b = measurement.Points[i];
                if (TryFindNearestRoofEdgeForLine(measurement.PageFolder, a, b, out ThreeDRoofGuide? guide) &&
                    guide != null)
                {
                    guide.Kind = ThreeDRoofGuideKinds.Eave;
                    guide.Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Eave);
                    guide.DefinesSlope = true;
                    guide.PitchRisePerFoot = pitch;
                    guide.AdjustmentStatus = adjustmentStatus;
                    guide.AdjustmentMessage = $"Matched from eave line takeoff {measurement.Name}.";
                    guide.Label = RelabelRoofGuide(guide);
                    changed.Add(guide.Id);
                }
            }
        }

        return changed;
    }

    private IReadOnlyList<Measurement> SelectedRoofLineMeasurements(TreeViewItem? anchor)
    {
        List<Measurement> selected = _viewport.GetSelectedMeasurements()
            .Where(IsUsableRoofLineMeasurement)
            .Where(measurement =>
            {
                TakeoffItem? item = FindTakeoffItemForMeasurement(measurement);
                return item == null || IsEaveNamedSource(item.Name) || IsEaveNamedSource(measurement.Name);
            })
            .ToList();
        if (selected.Count > 0)
            return selected;

        return TakeoffItemsForSelection(anchor)
            .Where(item => ThreeDRoofFootprintBuildService.IsLineTakeoff(item))
            .Where(item => IsEaveNamedSource(item.Name))
            .SelectMany(item => item.Measurements.Where(IsUsableRoofLineMeasurement))
            .ToList();
    }

    private IReadOnlyList<Measurement> AutoRoofLineMeasurements(TreeViewItem? anchor)
    {
        List<Measurement> selected = SelectedRoofLineMeasurements(anchor).ToList();
        if (selected.Count > 0)
            return selected;

        return _takeoffItems
            .Where(item => ThreeDRoofFootprintBuildService.IsLineTakeoff(item))
            .Where(item => IsEaveNamedSource(item.Name) || IsEaveNamedSource(TakeoffFolderDisplayName(item)))
            .SelectMany(item => item.Measurements.Where(IsUsableRoofLineMeasurement))
            .ToList();
    }

    private static string TakeoffFolderDisplayName(TakeoffItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FolderPath))
            return "";

        try
        {
            return OurPlaneCoreJobStore.DisplayName(item.FolderPath);
        }
        catch
        {
            return "";
        }
    }

    private static bool IsUsableRoofLineMeasurement(Measurement measurement) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "line" &&
        measurement.Points.Count >= 2;

    private static bool IsEaveNamedSource(string? value)
    {
        string text = (value ?? "").Trim();
        return EaveSourceRegex().IsMatch(text);
    }

    [GeneratedRegex(@"\b(?:eave|eaves|eve)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EaveSourceRegex();

    private bool TryFindNearestRoofEdgeForLine(string pageFolder, SKPoint a, SKPoint b, out ThreeDRoofGuide? guide)
    {
        guide = null;
        IEnumerable<ThreeDRoofGuide> candidates = _threeDRoofGuides;
        if (!string.IsNullOrWhiteSpace(pageFolder))
            candidates = candidates.Where(candidate => IsSamePageFolder(candidate.PageFolder, pageFolder));

        double bestDistance = double.PositiveInfinity;
        ThreeDRoofGuide? best = null;
        foreach (ThreeDRoofGuide candidate in candidates)
        {
            for (int i = 1; i < candidate.Points.Count; i++)
            {
                ThreeDRoofGuidePoint p0 = candidate.Points[i - 1];
                ThreeDRoofGuidePoint p1 = candidate.Points[i];
                double distance = SegmentDistance(a.X, a.Y, b.X, b.Y, p0.PdfX, p0.PdfY, p1.PdfX, p1.PdfY);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
        }

        if (best == null || bestDistance > 12)
            return false;

        guide = best;
        return true;
    }

    private bool CanBuildThreeDRoofPreview() =>
        _currentJob != null &&
        (_threeDRoofGuides.Count > 0 || _threeDFloorSlabs.Count > 0 || _threeDWallElements.Count > 0);

    private void BuildThreeDRoofPreview()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "3D Roof: open a job first.";
            return;
        }

        ThreeDWallModel model = CurrentThreeDModel();
        ThreeDRoofBuildResult build = ThreeDRoofBuildService.Build(model);
        _threeDRoofGuides.Clear();
        _threeDRoofGuides.AddRange(build.Guides);
        PruneThreeDRoofGuideSelection();

        _threeDRoofIssues.Clear();
        _threeDRoofIssues.AddRange(build.Issues);
        RefreshThreeDRoofGuideOverlay();
        UpdateThreeDEditor();

        if (build.PlaneBuildBlocked || build.Planes.Count == 0)
        {
            _threeDRoofPlanes.Clear();
            RenderThreeDWallModel(fitCamera: false);
            string message = build.Messages.LastOrDefault() ?? "Roof preview could not be built.";
            TxtStatus.Text = $"3D Roof: {message}";
            SaveCurrentThreeDModel();
            LogThreeD($"Roof generation blocked: issues {_threeDRoofIssues.Count}. {message}");
            return;
        }

        _threeDRoofPlanes.Clear();
        _threeDRoofPlanes.AddRange(build.Planes);
        RenderThreeDWallModel(fitCamera: true);
        SaveCurrentThreeDModel();
        string summary = string.Join(" ", build.Messages);
        TxtStatus.Text = $"3D Roof: generated {_threeDRoofPlanes.Count} roof mesh face(s) from Eave edge pitch.";
        LogThreeD(string.IsNullOrWhiteSpace(summary)
            ? $"Roof generated: {_threeDRoofPlanes.Count} mesh face(s)."
            : $"Roof generated: {_threeDRoofPlanes.Count} mesh face(s). {summary}");
    }

    private void ClearThreeDRoof()
    {
        bool hasRoofBase = HasGeneratedRoofBase();
        if (!hasRoofBase && _threeDRoofGuides.Count == 0 && _threeDRoofPlanes.Count == 0 && _threeDRoofIssues.Count == 0)
            return;

        _threeDFloorSlabs.RemoveAll(slab =>
            slab.GroupKey.StartsWith(ThreeDRoofFootprintBuildService.GeneratedSlabGroupPrefix, StringComparison.OrdinalIgnoreCase));
        _threeDRoofGuides.Clear();
        _threeDRoofPlanes.Clear();
        _threeDRoofIssues.Clear();
        _selectedThreeDRoofGuideId = "";
        _selectedThreeDRoofGuideIds.Clear();
        SetThreeDRoofEdgeSelectMode(enabled: false);
        RefreshThreeDRoofGuideOverlay();
        RenderThreeDWallModel(fitCamera: false);
        SaveCurrentThreeDModel(allowEmpty: true);
        TxtStatus.Text = "3D Roof: roof base, edge roles, and generated mesh cleared.";
        LogThreeD("Roof base, edge roles, and generated mesh cleared.");
    }

    private bool HasGeneratedRoofBase() =>
        _threeDFloorSlabs.Any(slab =>
            slab.GroupKey.StartsWith(ThreeDRoofFootprintBuildService.GeneratedSlabGroupPrefix, StringComparison.OrdinalIgnoreCase));

    private void RefreshThreeDRoofGuideOverlay()
    {
        if (_currentPage == null)
        {
            _viewport.ClearThreeDRoofGuides();
            return;
        }

        _viewport.SetThreeDRoofGuides(_threeDRoofGuides
            .Where(guide => IsSamePageFolder(guide.PageFolder, _currentPage.FolderPath)));
        _viewport.SetSelectedThreeDRoofGuides(_selectedThreeDRoofGuideIds);
        _viewport.SetThreeDRoofIssues(_threeDRoofIssues
            .Where(issue => IsSamePageFolder(issue.PageFolder, _currentPage.FolderPath)));
    }

    private List<ThreeDRoofGuide> SnapshotThreeDRoofGuides() =>
        _threeDRoofGuides.Select(CloneThreeDRoofGuide).ToList();

    private List<ThreeDRoofPlane> SnapshotThreeDRoofPlanes() =>
        _threeDRoofPlanes.Select(CloneThreeDRoofPlane).ToList();

    private List<ThreeDRoofIssue> SnapshotThreeDRoofIssues() =>
        _threeDRoofIssues.Select(CloneThreeDRoofIssue).ToList();

    private void RestoreThreeDRoofState(
        IEnumerable<ThreeDRoofGuide> guides,
        IEnumerable<ThreeDRoofPlane> planes,
        IEnumerable<ThreeDRoofIssue>? issues = null)
    {
        _threeDRoofGuides.Clear();
        _threeDRoofGuides.AddRange(guides.Select(CloneThreeDRoofGuide));
        _threeDRoofPlanes.Clear();
        _threeDRoofPlanes.AddRange(planes.Select(CloneThreeDRoofPlane));
        _threeDRoofIssues.Clear();
        if (issues != null)
            _threeDRoofIssues.AddRange(issues.Select(CloneThreeDRoofIssue));
        PruneThreeDRoofGuideSelection();

        RefreshThreeDRoofGuideOverlay();
    }

    private double ResolveRoofGuideElevationFeet()
    {
        double wallTop = _threeDWallElements
            .Select(wall => wall.BaseElevationFeet + wall.HeightFeet)
            .DefaultIfEmpty(0)
            .Max();
        double levelTop = _threeDFloorLevels
            .Select(level => level.BaseElevationFeet + level.HeightFeet)
            .DefaultIfEmpty(0)
            .Max();
        double slabTop = _threeDFloorSlabs
            .Select(slab => slab.ElevationFeet + Math.Max(0.02, slab.ThicknessFeet))
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(wallTop, Math.Max(levelTop, slabTop));
    }

    private double ResolveThreeDRoofPitchRisePerFoot() =>
        RoofPitchText.ParseOrDefault(
            _threeDRoofPitchBox?.Text,
            ThreeDRoofPreviewBuilder.DefaultPitchRisePerFoot);

    private bool TryFindThreeDRoofGuideAt(float pdfX, float pdfY, out ThreeDRoofGuide? guide)
    {
        guide = null;
        if (_currentPage == null || _threeDRoofGuides.Count == 0)
            return false;

        double tolerance = Math.Max(3.0, 16.0 / Math.Max(_viewport.CaptureViewState().Zoom, 0.05f));
        ThreeDRoofGuide? bestGuide = null;
        double bestDistance = double.PositiveInfinity;
        foreach (ThreeDRoofGuide candidate in _threeDRoofGuides
                     .Where(candidate => IsSamePageFolder(candidate.PageFolder, _currentPage.FolderPath))
                     .Where(IsSelectableThreeDRoofBaseGuide))
        {
            for (int i = 1; i < candidate.Points.Count; i++)
            {
                ThreeDRoofGuidePoint a = candidate.Points[i - 1];
                ThreeDRoofGuidePoint b = candidate.Points[i];
                double distance = DistanceToSegment(pdfX, pdfY, a.PdfX, a.PdfY, b.PdfX, b.PdfY);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestGuide = candidate;
                }
            }
        }

        if (bestGuide == null || bestDistance > tolerance)
            return false;

        guide = bestGuide;
        return true;
    }

    private void SelectThreeDRoofGuide(string guideId)
    {
        _selectedThreeDRoofGuideIds.Clear();
        _selectedThreeDRoofGuideIds.Add(guideId);
        _selectedThreeDRoofGuideId = guideId;
        RefreshThreeDRoofGuideOverlay();
        UpdateThreeDEditor();
        TxtStatus.Text = "3D Roof: 1 roof edge selected. Ctrl/Shift + right-click adds more edges.";
    }

    private void ToggleThreeDRoofGuideSelection(string guideId)
    {
        if (string.IsNullOrWhiteSpace(guideId))
            return;

        if (_selectedThreeDRoofGuideIds.Contains(guideId))
            _selectedThreeDRoofGuideIds.Remove(guideId);
        else
            _selectedThreeDRoofGuideIds.Add(guideId);

        _selectedThreeDRoofGuideId = _selectedThreeDRoofGuideIds.Contains(guideId)
            ? guideId
            : _selectedThreeDRoofGuideIds.FirstOrDefault() ?? "";
        RefreshThreeDRoofGuideOverlay();
        UpdateThreeDEditor();
        TxtStatus.Text = _selectedThreeDRoofGuideIds.Count == 0
            ? "3D Roof: edge selection cleared."
            : $"3D Roof: {_selectedThreeDRoofGuideIds.Count} roof edge(s) selected.";
    }

    private void ClearThreeDRoofGuideSelection()
    {
        if (_selectedThreeDRoofGuideIds.Count == 0 && string.IsNullOrWhiteSpace(_selectedThreeDRoofGuideId))
            return;

        _selectedThreeDRoofGuideIds.Clear();
        _selectedThreeDRoofGuideId = "";
        RefreshThreeDRoofGuideOverlay();
        UpdateThreeDEditor();
        TxtStatus.Text = "3D Roof: edge selection cleared.";
    }

    private void PruneThreeDRoofGuideSelection()
    {
        var availableIds = _threeDRoofGuides
            .Where(IsSelectableThreeDRoofBaseGuide)
            .Select(guide => guide.Id)
            .ToHashSet(StringComparer.Ordinal);
        _selectedThreeDRoofGuideIds.RemoveWhere(id => !availableIds.Contains(id));
        if (string.IsNullOrWhiteSpace(_selectedThreeDRoofGuideId) ||
            !availableIds.Contains(_selectedThreeDRoofGuideId))
        {
            _selectedThreeDRoofGuideId = _selectedThreeDRoofGuideIds.FirstOrDefault() ?? "";
        }
    }

    private ThreeDRoofGuide? SelectedThreeDRoofGuide() =>
        string.IsNullOrWhiteSpace(_selectedThreeDRoofGuideId)
            ? null
            : _threeDRoofGuides.FirstOrDefault(guide => string.Equals(guide.Id, _selectedThreeDRoofGuideId, StringComparison.Ordinal));

    private IReadOnlyList<ThreeDRoofGuide> SelectedThreeDRoofGuides()
    {
        PruneThreeDRoofGuideSelection();
        if (_selectedThreeDRoofGuideIds.Count == 0)
            return [];

        return _threeDRoofGuides
            .Where(IsSelectableThreeDRoofBaseGuide)
            .Where(guide => _selectedThreeDRoofGuideIds.Contains(guide.Id))
            .ToList();
    }

    private int SelectedThreeDRoofGuideCount()
    {
        PruneThreeDRoofGuideSelection();
        return _selectedThreeDRoofGuideIds.Count;
    }

    private static bool IsRoofGuideMultiSelectModifierActive() =>
        (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None;

    private static bool IsSelectableThreeDRoofBaseGuide(ThreeDRoofGuide guide) =>
        !string.Equals(guide.Status, ThreeDRoofPreviewBuilder.GeneratedSeamStatus, StringComparison.OrdinalIgnoreCase);

    private static string RelabelRoofGuide(ThreeDRoofGuide guide)
    {
        string title = ThreeDRoofGuideKinds.Title(guide.Kind);
        string label = string.IsNullOrWhiteSpace(guide.Label) ? title : guide.Label.Trim();
        foreach (string kind in ThreeDRoofGuideKinds.All)
        {
            string kindTitle = ThreeDRoofGuideKinds.Title(kind);
            if (label.StartsWith(kindTitle, StringComparison.OrdinalIgnoreCase))
                return title + label[kindTitle.Length..];
        }

        string[] parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[^1], out _))
        {
            foreach (string kind in ThreeDRoofGuideKinds.All)
            {
                string kindTitle = ThreeDRoofGuideKinds.Title(kind);
                if (string.Equals(parts[^2], kindTitle, StringComparison.OrdinalIgnoreCase))
                {
                    parts[^2] = title;
                    return string.Join(" ", parts);
                }
            }
        }

        return $"{title} - {label}";
    }

    private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double len2 = dx * dx + dy * dy;
        if (len2 <= 0.000001)
            return Distance(px, py, ax, ay);

        double t = ((px - ax) * dx + (py - ay) * dy) / len2;
        t = Math.Clamp(t, 0, 1);
        return Distance(px, py, ax + dx * t, ay + dy * t);
    }

    private static double SegmentDistance(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        if (SegmentsIntersect(ax, ay, bx, by, cx, cy, dx, dy))
            return 0;

        return Math.Min(
            Math.Min(DistanceToSegment(ax, ay, cx, cy, dx, dy), DistanceToSegment(bx, by, cx, cy, dx, dy)),
            Math.Min(DistanceToSegment(cx, cy, ax, ay, bx, by), DistanceToSegment(dx, dy, ax, ay, bx, by)));
    }

    private static bool SegmentsIntersect(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        double o1 = Cross(ax, ay, bx, by, cx, cy);
        double o2 = Cross(ax, ay, bx, by, dx, dy);
        double o3 = Cross(cx, cy, dx, dy, ax, ay);
        double o4 = Cross(cx, cy, dx, dy, bx, by);
        return o1 > 0 != o2 > 0 && o3 > 0 != o4 > 0;
    }

    private static double Cross(double ax, double ay, double bx, double by, double cx, double cy) =>
        (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

    private static double Distance(double ax, double ay, double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static ThreeDRoofGuide CloneThreeDRoofGuide(ThreeDRoofGuide guide) =>
        new()
        {
            Id = guide.Id,
            Kind = guide.Kind,
            Label = guide.Label,
            PageFolder = guide.PageFolder,
            LevelKey = guide.LevelKey,
            ElevationFeet = guide.ElevationFeet,
            Color = guide.Color,
            Status = guide.Status,
            AdjustmentStatus = guide.AdjustmentStatus,
            AdjustmentMessage = guide.AdjustmentMessage,
            RawPoints = guide.RawPoints.Select(CloneRoofGuidePoint).ToList(),
            Points = guide.Points.Select(CloneRoofGuidePoint).ToList(),
            PitchRisePerFoot = guide.PitchRisePerFoot,
            DefinesSlope = guide.DefinesSlope,
            OverhangFeet = guide.OverhangFeet,
        };

    private static ThreeDRoofPlane CloneThreeDRoofPlane(ThreeDRoofPlane plane) =>
        new()
        {
            Id = plane.Id,
            Kind = plane.Kind,
            Label = plane.Label,
            Color = plane.Color,
            Opacity = plane.Opacity,
            Status = plane.Status,
            Message = plane.Message,
            SourceGuideIds = plane.SourceGuideIds.ToList(),
            Points = plane.Points
                .Select(point => new ThreeDRoofVertex
                {
                    XFeet = point.XFeet,
                    YFeet = point.YFeet,
                    ZFeet = point.ZFeet,
                })
                .ToList(),
        };

    private static ThreeDRoofIssue CloneThreeDRoofIssue(ThreeDRoofIssue issue) =>
        new()
        {
            Id = issue.Id,
            Severity = issue.Severity,
            Kind = issue.Kind,
            Message = issue.Message,
            PageFolder = issue.PageFolder,
            HasPdfPoint = issue.HasPdfPoint,
            PdfX = issue.PdfX,
            PdfY = issue.PdfY,
            XFeet = issue.XFeet,
            YFeet = issue.YFeet,
            ZFeet = issue.ZFeet,
            Color = issue.Color,
            GuideIds = issue.GuideIds.ToList(),
        };

    private static ThreeDRoofGuidePoint CloneRoofGuidePoint(ThreeDRoofGuidePoint point) =>
        new()
        {
            PdfX = point.PdfX,
            PdfY = point.PdfY,
            XFeet = point.XFeet,
            YFeet = point.YFeet,
            ZFeet = point.ZFeet,
        };

    private static string PitchLabel(double pitchRisePerFoot)
    {
        double pitch = pitchRisePerFoot > 0
            ? pitchRisePerFoot
            : ThreeDRoofPreviewBuilder.DefaultPitchRisePerFoot;
        return RoofPitchText.Format(pitch);
    }

    private static string RoofGuidePitchLabel(ThreeDRoofGuide guide) =>
        guide.DefinesSlope
            ? $"pitch {PitchLabel(guide.PitchRisePerFoot)}"
            : "no slope";

}
