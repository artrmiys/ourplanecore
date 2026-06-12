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
    // 3D viewer hit selection, summary, quantities, editor apply, and level reflow.

    private void SelectThreeDViewerWallAt(Viewport3D viewport, Point point)
    {
        if (_threeDWallHitMap.Count == 0 && _threeDFloorSlabHitMap.Count == 0 && _threeDRoofMeshHitMap.Count == 0)
            return;

        // Rafter face-pick mode claims roof-face clicks before group selection.
        if (TryToggleRafterFaceAt(viewport, point))
            return;

        if (TrySelectThreeDRoofMeshAt(viewport, point))
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
        string roofQtyText = ThreeDRoofQuantitiesText();
        ThreeDRoofPlacement? selectedRoof = ActiveThreeDRoofPlacement();
        string selected = _selectedThreeDWall != null
            ? $"Selected: {_selectedThreeDWall.Label} | H {_selectedThreeDWall.HeightFeet:F1} ft | W {_selectedThreeDWall.ThicknessInches:F0} in | base {_selectedThreeDWall.BaseElevationFeet:F1}"
            : _selectedThreeDFloorSlab != null
                ? $"Selected slab: {_selectedThreeDFloorSlab.Label} | elev {_selectedThreeDFloorSlab.ElevationFeet:F1} ft"
                : selectedRoof != null
                    ? $"Selected roof: {selectedRoof.Label} | X {selectedRoof.OffsetXFeet:F1} ft | Y {selectedRoof.OffsetYFeet:F1} ft | Z {selectedRoof.OffsetZFeet:F1} ft | drag colored axis arrows to move"
                    : "Click a wall to edit its height/thickness or apply the values to its group.";
        string body = string.Join("\n", groups);
        return $"3D model: {_threeDWallElements.Count} wall segment(s), {_threeDFloorSlabs.Count} slab(s)\n{levelText}\n{slabText}\n{roofText}\n{roofQtyText}\n{body}\n{selected}";
    }

    private ThreeDRoofQuantities CurrentThreeDRoofQuantities(string? roofGroupId = null)
    {
        if (string.IsNullOrWhiteSpace(roofGroupId))
            return ThreeDRoofQuantities.Compute(_threeDRoofPlanes, _threeDRoofGuides);

        return ThreeDRoofQuantities.Compute(
            _threeDRoofPlanes.Where(plane => SameRoofGroup(plane.RoofGroupId, roofGroupId)),
            _threeDRoofGuides.Where(guide => SameRoofGroup(guide.RoofGroupId, roofGroupId)));
    }

    private string ThreeDRoofQuantitiesText(string? roofGroupId = null)
    {
        ThreeDRoofQuantities q = CurrentThreeDRoofQuantities(roofGroupId);
        if (!q.HasRoof)
            return "";

        return string.Format(
            CultureInfo.InvariantCulture,
            "Roof qty: {0:F0} SF sloped ({1:F0} SF plan) | ridge {2:F1} ft | hip {3:F1} ft | valley {4:F1} ft | eave {5:F1} ft",
            q.SlopedAreaSqFt, q.PlanAreaSqFt, q.RidgeLengthFeet, q.HipLengthFeet, q.ValleyLengthFeet, q.EaveLengthFeet);
    }

    private void ShowThreeDRoofQuantities()
    {
        string groupId = ActiveThreeDRoofGroupId();
        ThreeDRoofQuantities q = CurrentThreeDRoofQuantities(groupId);
        if (!q.HasRoof)
        {
            TxtStatus.Text = "3D Roof Qty: generate a roof first (Roof Base -> set eave pitch).";
            return;
        }

        string text = ThreeDRoofQuantitiesText(groupId);
        string label = string.IsNullOrWhiteSpace(groupId) ? "" : $"{RoofGroupLabel(groupId)}: ";
        TxtStatus.Text = "3D " + label + text;
        LogThreeD(label + text);
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
}
