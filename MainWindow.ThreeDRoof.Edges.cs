using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    // 3D roof edge kind, pitch, overhang, and eave takeoff application.

    private void SetSelectedThreeDRoofGuideKind(string kind, bool applyPitch)
    {
        IReadOnlyList<ThreeDRoofGuide> guides = SelectedThreeDRoofGuides();
        if (guides.Count == 0)
        {
            SetThreeDRoofEdgeSelectMode(enabled: true);
            TxtStatus.Text = "3D Roof: select one or more roof base edges first, then apply Edge Pitch.";
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

        InvalidateGeneratedThreeDRoofAfterEdgeEdit();
        if (guides.Count == 1)
        {
            ThreeDRoofGuide guide = guides[0];
            TxtStatus.Text = cleanKind == ThreeDRoofGuideKinds.Eave
                ? $"3D Roof: {guide.Label} saved as Eave, pitch {PitchLabel(guide.PitchRisePerFoot)}. Generate Roof when ready."
                : $"3D Roof: {guide.Label} set to {ThreeDRoofGuideKinds.Title(cleanKind)}.";
        }
        else
        {
            TxtStatus.Text = cleanKind == ThreeDRoofGuideKinds.Eave
                ? $"3D Roof: {guides.Count} selected edge(s) saved as Eave, pitch {PitchLabel(pitch)}. Generate Roof when ready."
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

        InvalidateGeneratedThreeDRoofAfterEdgeEdit();
        TxtStatus.Text = guides.Count == 1
            ? $"3D Roof: saved pitch {PitchLabel(pitch)} on {guides[0].Label}. Generate Roof when ready."
            : $"3D Roof: saved pitch {PitchLabel(pitch)} on {guides.Count} selected edge(s). Generate Roof when ready.";
        LogThreeD($"Roof edge pitch saved: {guides.Count} selected edge(s), {PitchLabel(pitch)}.");
    }

    private void ApplyThreeDRoofPitchToEaves()
    {
        double pitch = ResolveThreeDRoofPitchRisePerFoot();
        string groupId = ActiveThreeDRoofGroupId();
        int changed = 0;
        foreach (ThreeDRoofGuide guide in _threeDRoofGuides
                     .Where(ThreeDRoofPreviewBuilder.IsSlopeDefiningGuide)
                     .Where(guide => SameRoofGroup(guide.RoofGroupId, groupId)))
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

    private void ApplyThreeDRoofEdgeProperties() => ApplyThreeDRoofPitchToSelectedEdges();

    // Revit-style: save only the side-panel fields onto every selected edge.
    // Blank fields stay unchanged so mixed selections are safe; Generate Roof
    // rebuilds after the user finishes assigning all edge pitches.
    private void ApplyThreeDRoofEdgePropertiesFromPanel() =>
        TryApplyPendingThreeDRoofEdgePropertiesFromPanel(showStatus: true);

    private bool TryApplyPendingThreeDRoofEdgePropertiesFromPanel(bool showStatus)
    {
        IReadOnlyList<ThreeDRoofGuide> guides = SelectedThreeDRoofGuides();
        if (guides.Count == 0)
        {
            if (showStatus)
            {
                SetThreeDRoofEdgeSelectMode(enabled: true);
                TxtStatus.Text = "3D Roof: select one or more roof base edges first.";
            }

            return !showStatus;
        }

        bool? definesSlope = _threeDRoofDefinesSlopeCheck?.IsChecked;
        if (!TryReadRoofEdgePitch(out bool hasPitch, out double pitch) ||
            !TryReadRoofEdgeOverhangFeet(out bool hasOverhang, out double overhangFeet))
        {
            return false;
        }

        foreach (ThreeDRoofGuide guide in guides)
        {
            if (definesSlope.HasValue)
            {
                guide.DefinesSlope = definesSlope.Value;
                guide.Kind = definesSlope.Value ? ThreeDRoofGuideKinds.Eave : ThreeDRoofGuideKinds.Rake;
                guide.Color = ThreeDRoofGuideKinds.Color(guide.Kind);
                if (!definesSlope.Value)
                    guide.PitchRisePerFoot = 0;
            }

            if (hasPitch)
            {
                guide.DefinesSlope = true;
                guide.Kind = ThreeDRoofGuideKinds.Eave;
                guide.Color = ThreeDRoofGuideKinds.Color(guide.Kind);
                guide.PitchRisePerFoot = pitch;
            }
            else if (guide.DefinesSlope && guide.PitchRisePerFoot <= 0)
            {
                guide.PitchRisePerFoot = ResolveThreeDRoofPitchRisePerFoot();
            }

            if (hasOverhang)
                guide.OverhangFeet = overhangFeet;

            guide.Label = RelabelRoofGuide(guide);
        }

        InvalidateGeneratedThreeDRoofAfterEdgeEdit();
        if (showStatus)
            TxtStatus.Text = $"3D Roof: saved edge properties to {guides.Count} edge(s). Generate Roof when ready.";
        LogThreeD($"Roof edge properties applied to {guides.Count} edge(s): " +
                  $"slope={(definesSlope.HasValue ? definesSlope.Value.ToString() : "unchanged")}, " +
                  $"pitch={(hasPitch ? PitchLabel(pitch) : "unchanged")}, " +
                  $"overhang={(hasOverhang ? (overhangFeet * 12.0).ToString("0.#") + " in" : "unchanged")}.");
        return true;
    }

    private bool TryReadRoofEdgePitch(out bool hasPitch, out double pitch)
    {
        hasPitch = false;
        pitch = 0;
        string? text = _threeDRoofEdgePitchBox?.Text;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (RoofPitchText.TryParse(text, out pitch))
        {
            hasPitch = true;
            return true;
        }

        TxtStatus.Text = $"3D Roof: invalid edge pitch '{text}'. Use 6/12, 4, or 0.333.";
        return false;
    }

    private bool TryReadRoofEdgeOverhangFeet(out bool hasOverhang, out double overhangFeet)
    {
        hasOverhang = false;
        overhangFeet = 0;
        string? text = _threeDRoofEdgeOverhangBox?.Text;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (!double.TryParse(text.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double inches))
        {
            TxtStatus.Text = $"3D Roof: invalid overhang '{text}'. Use inches, for example 12.";
            return false;
        }

        hasOverhang = true;
        overhangFeet = Math.Max(0, inches) / 12.0;
        return true;
    }

    private void InvalidateGeneratedThreeDRoofAfterEdgeEdit()
    {
        HashSet<string> targetGroups = SelectedThreeDRoofGuides()
            .Select(guide => guide.RoofGroupId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (targetGroups.Count == 0)
        {
            string activeGroupId = ActiveThreeDRoofGroupId();
            if (!string.IsNullOrWhiteSpace(activeGroupId))
                targetGroups.Add(activeGroupId);
        }

        _threeDRoofGuides.RemoveAll(guide =>
            targetGroups.Contains(guide.RoofGroupId) &&
            string.Equals(guide.Status, ThreeDRoofPreviewBuilder.GeneratedSeamStatus, StringComparison.OrdinalIgnoreCase));
        _threeDRoofPlanes.RemoveAll(plane => targetGroups.Contains(plane.RoofGroupId));
        _threeDRoofIssues.RemoveAll(issue => targetGroups.Contains(issue.RoofGroupId));
        PruneThreeDRoofGuideSelection();
        RefreshThreeDRoofGuideOverlay();
        RenderThreeDWallModel(fitCamera: false);
        SaveCurrentThreeDModel();
        UpdateThreeDEditor();
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
}
