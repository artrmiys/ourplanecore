using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    // Generated roof preview, snapshots, overlay sync, and guide selection.

    private bool TryFindNearestRoofEdgeForLine(string pageFolder, SKPoint a, SKPoint b, out ThreeDRoofGuide? guide)
    {
        guide = null;
        IEnumerable<ThreeDRoofGuide> candidates = _threeDRoofGuides;
        string activeGroupId = ActiveThreeDRoofGroupId();
        if (!string.IsNullOrWhiteSpace(activeGroupId))
            candidates = candidates.Where(candidate => SameRoofGroup(candidate.RoofGroupId, activeGroupId));
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
        IsModuleEnabled(ModuleId.ThreeD) &&
        IsCurrentJobWritable &&
        (_threeDRoofGuides.Count > 0 || _threeDFloorSlabs.Count > 0 || _threeDWallElements.Count > 0);

    private void BuildThreeDRoofPreview()
    {
        if (!RequireModule(ModuleId.ThreeD, "Generate 3D roof"))
            return;

        if (!EnsureThreeDEditable("generate the 3D roof"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "3D Roof: open a job first.";
            return;
        }

        string groupId = ActiveThreeDRoofGroupId();
        if (string.IsNullOrWhiteSpace(groupId))
        {
            TxtStatus.Text = "3D Roof: create or select a roof base first.";
            return;
        }

        ThreeDWallModel model = CurrentThreeDModelForRoofGroup(groupId);
        ThreeDRoofBuildResult build = ThreeDRoofBuildService.Build(model);
        ReplaceActiveRoofBuildResult(groupId, build);
        PruneThreeDRoofGuideSelection();

        RefreshThreeDRoofGuideOverlay();
        UpdateThreeDEditor();

        if (build.PlaneBuildBlocked || build.Planes.Count == 0)
        {
            RenderThreeDWallModel(fitCamera: false);
            string message = build.Messages.LastOrDefault() ?? "Roof preview could not be built.";
            TxtStatus.Text = $"3D Roof: {RoofGroupLabel(groupId)}: {message}";
            SaveCurrentThreeDModel();
            LogThreeD($"Roof generation blocked: {RoofGroupLabel(groupId)}, issues {build.Issues.Count}. {message}");
            return;
        }

        RenderThreeDWallModel(fitCamera: true);
        SaveCurrentThreeDModel();
        string summary = string.Join(" ", build.Messages);
        int activePlaneCount = _threeDRoofPlanes.Count(plane => SameRoofGroup(plane.RoofGroupId, groupId));
        TxtStatus.Text = $"3D Roof: generated {activePlaneCount} roof mesh face(s) for {RoofGroupLabel(groupId)} from Eave edge pitch.";
        string qty = ThreeDRoofQuantitiesText(groupId);
        LogThreeD(string.IsNullOrWhiteSpace(summary)
            ? $"Roof generated: {RoofGroupLabel(groupId)}, {activePlaneCount} mesh face(s)."
            : $"Roof generated: {RoofGroupLabel(groupId)}, {activePlaneCount} mesh face(s). {summary}");
        if (!string.IsNullOrWhiteSpace(qty))
            LogThreeD(qty);
    }

    private void ClearThreeDRoof()
    {
        DeleteActiveThreeDRoof();
    }

    private bool HasGeneratedRoofBase() =>
        _threeDFloorSlabs.Any(slab =>
            slab.GroupKey.StartsWith(ThreeDRoofFootprintBuildService.GeneratedSlabGroupPrefix, StringComparison.OrdinalIgnoreCase));

    private void RefreshThreeDRoofGuideOverlay()
    {
        if (_currentPage == null)
        {
            _viewport.ClearThreeDRoofGuides();
            HideThreeDRoofPitchPopup();
            return;
        }

        // Only the editable base edges (eave/rake) belong on the plan. The
        // auto-generated ridge/hip/valley seams are generation output - drawing
        // them here just clutters the top-down view with helper lines (the user
        // can't edit them, and their lengths still live on as takeoff data).
        _viewport.SetThreeDRoofGuides(_threeDRoofGuides
            .Where(guide => IsSamePageFolder(guide.PageFolder, _currentPage.FolderPath))
            .Where(IsSelectableThreeDRoofBaseGuide));
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
        ThreeDRoofGuide? guide = _threeDRoofGuides.FirstOrDefault(candidate => string.Equals(candidate.Id, guideId, StringComparison.Ordinal));
        if (guide != null && !string.IsNullOrWhiteSpace(guide.RoofGroupId))
            SetActiveThreeDRoofGroup(guide.RoofGroupId, render: false);
        _selectedThreeDRoofGuideIds.Clear();
        _selectedThreeDRoofGuideIds.Add(guideId);
        _selectedThreeDRoofGuideId = guideId;
        RefreshThreeDRoofGuideOverlay();
        UpdateThreeDEditor();
        ShowThreeDRoofPitchPopup();
        TxtStatus.Text = "3D Roof: 1 roof edge selected. Ctrl/Shift + right-click adds more edges.";
    }

    private void ToggleThreeDRoofGuideSelection(string guideId)
    {
        if (string.IsNullOrWhiteSpace(guideId))
            return;

        ThreeDRoofGuide? guide = _threeDRoofGuides.FirstOrDefault(candidate => string.Equals(candidate.Id, guideId, StringComparison.Ordinal));
        if (guide != null && !string.IsNullOrWhiteSpace(guide.RoofGroupId))
        {
            string activeGroupId = ActiveThreeDRoofGroupId();
            if (!string.IsNullOrWhiteSpace(activeGroupId) && !SameRoofGroup(activeGroupId, guide.RoofGroupId))
            {
                _selectedThreeDRoofGuideIds.Clear();
                _selectedThreeDRoofGuideId = "";
            }

            SetActiveThreeDRoofGroup(guide.RoofGroupId, render: false);
        }

        if (_selectedThreeDRoofGuideIds.Contains(guideId))
            _selectedThreeDRoofGuideIds.Remove(guideId);
        else
            _selectedThreeDRoofGuideIds.Add(guideId);

        _selectedThreeDRoofGuideId = _selectedThreeDRoofGuideIds.Contains(guideId)
            ? guideId
            : _selectedThreeDRoofGuideIds.FirstOrDefault() ?? "";
        RefreshThreeDRoofGuideOverlay();
        UpdateThreeDEditor();
        ShowThreeDRoofPitchPopup();
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
        HideThreeDRoofPitchPopup();
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
}
