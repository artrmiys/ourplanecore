using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OurPlanCore;

public partial class MainWindow
{
    private readonly List<ThreeDRoofPlacement> _threeDRoofPlacements = [];
    private string _activeThreeDRoofGroupId = "";
    private ComboBox? _threeDRoofSelector;
    private Slider? _threeDRoofMoveXSlider;
    private Slider? _threeDRoofMoveYSlider;
    private Slider? _threeDRoofMoveZSlider;
    private TextBlock? _threeDRoofMoveText;
    private Button? _threeDRoofDeleteButton;
    private bool _updatingThreeDRoofMoveUi;

    private static bool IsRoofSlab(ThreeDFloorSlab slab) =>
        string.Equals(slab.LevelKey, "roof", StringComparison.OrdinalIgnoreCase) ||
        slab.GroupKey.StartsWith(ThreeDRoofFootprintBuildService.GeneratedSlabGroupPrefix, StringComparison.OrdinalIgnoreCase);

    private static bool SameRoofGroup(string? left, string? right) =>
        string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);

    private string ActiveThreeDRoofGroupId()
    {
        NormalizeThreeDRoofGroupsInMemory();
        if (!string.IsNullOrWhiteSpace(_activeThreeDRoofGroupId) &&
            HasThreeDRoofGroup(_activeThreeDRoofGroupId))
        {
            return _activeThreeDRoofGroupId;
        }

        _activeThreeDRoofGroupId = _selectedThreeDRoofGuideIds
            .Select(id => _threeDRoofGuides.FirstOrDefault(guide => string.Equals(guide.Id, id, StringComparison.Ordinal)))
            .Where(guide => guide != null)
            .Select(guide => guide!.RoofGroupId)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)) ??
            _threeDRoofPlacements.FirstOrDefault()?.RoofGroupId ??
            "";
        return _activeThreeDRoofGroupId;
    }

    private bool HasThreeDRoofGroup(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            return false;

        return _threeDRoofPlacements.Any(placement => SameRoofGroup(placement.RoofGroupId, groupId)) ||
               _threeDFloorSlabs.Any(slab => IsRoofSlab(slab) && SameRoofGroup(slab.RoofGroupId, groupId)) ||
               _threeDRoofGuides.Any(guide => SameRoofGroup(guide.RoofGroupId, groupId)) ||
               _threeDRoofPlanes.Any(plane => SameRoofGroup(plane.RoofGroupId, groupId)) ||
               _threeDRoofIssues.Any(issue => SameRoofGroup(issue.RoofGroupId, groupId));
    }

    private ThreeDRoofPlacement? ActiveThreeDRoofPlacement()
    {
        string groupId = ActiveThreeDRoofGroupId();
        if (string.IsNullOrWhiteSpace(groupId))
            return null;

        return EnsureThreeDRoofPlacement(groupId, RoofGroupLabel(groupId));
    }

    private ThreeDRoofPlacement EnsureThreeDRoofPlacement(string groupId, string label)
    {
        ThreeDRoofPlacement? placement = _threeDRoofPlacements.FirstOrDefault(candidate =>
            SameRoofGroup(candidate.RoofGroupId, groupId));
        if (placement != null)
        {
            if (!string.IsNullOrWhiteSpace(label))
                placement.Label = label;
            return placement;
        }

        placement = new ThreeDRoofPlacement
        {
            RoofGroupId = groupId,
            Label = string.IsNullOrWhiteSpace(label) ? "Roof" : label,
        };
        _threeDRoofPlacements.Add(placement);
        return placement;
    }

    private string CreateThreeDRoofGroup(string sourceLabel)
    {
        string groupId = Guid.NewGuid().ToString("N");
        string label = NextThreeDRoofLabel(sourceLabel);
        EnsureThreeDRoofPlacement(groupId, label);
        _activeThreeDRoofGroupId = groupId;
        return groupId;
    }

    private string NextThreeDRoofLabel(string sourceLabel)
    {
        int number = _threeDRoofPlacements.Count + 1;
        string clean = string.IsNullOrWhiteSpace(sourceLabel) ? "" : sourceLabel.Trim();
        return string.IsNullOrWhiteSpace(clean)
            ? $"Roof {number}"
            : $"Roof {number} - {clean}";
    }

    private string RoofGroupLabel(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            return "";

        string label = _threeDRoofPlacements.FirstOrDefault(placement =>
            SameRoofGroup(placement.RoofGroupId, groupId))?.Label ?? "";
        if (!string.IsNullOrWhiteSpace(label))
            return label;

        return _threeDFloorSlabs.FirstOrDefault(slab =>
                IsRoofSlab(slab) && SameRoofGroup(slab.RoofGroupId, groupId))?.RoofGroupLabel ??
            _threeDRoofGuides.FirstOrDefault(guide =>
                SameRoofGroup(guide.RoofGroupId, groupId))?.RoofGroupLabel ??
            "Roof";
    }

    private void SetActiveThreeDRoofGroup(string groupId, bool render = true)
    {
        _activeThreeDRoofGroupId = groupId ?? "";
        if (!string.IsNullOrWhiteSpace(_activeThreeDRoofGroupId))
            EnsureThreeDRoofPlacement(_activeThreeDRoofGroupId, RoofGroupLabel(_activeThreeDRoofGroupId));
        PruneThreeDRoofGuideSelection();
        RefreshThreeDRoofSelector();
        UpdateThreeDRoofMoveControls();
        UpdateThreeDEditor();
        if (render)
            RenderThreeDWallModel(fitCamera: false);
    }

    private void NormalizeThreeDRoofGroupsInMemory()
    {
        bool hasAnyRoof =
            _threeDFloorSlabs.Any(IsRoofSlab) ||
            _threeDRoofGuides.Count > 0 ||
            _threeDRoofPlanes.Count > 0 ||
            _threeDRoofIssues.Count > 0;
        if (!hasAnyRoof)
        {
            _activeThreeDRoofGroupId = "";
            _threeDRoofPlacements.Clear();
            return;
        }

        string? fallback = _threeDRoofPlacements.FirstOrDefault()?.RoofGroupId;
        if (string.IsNullOrWhiteSpace(fallback))
            fallback = _threeDRoofGuides.FirstOrDefault(guide => !string.IsNullOrWhiteSpace(guide.RoofGroupId))?.RoofGroupId;
        if (string.IsNullOrWhiteSpace(fallback))
            fallback = _threeDFloorSlabs.FirstOrDefault(slab => IsRoofSlab(slab) && !string.IsNullOrWhiteSpace(slab.RoofGroupId))?.RoofGroupId;
        if (string.IsNullOrWhiteSpace(fallback))
            fallback = "roof-" + Guid.NewGuid().ToString("N");
        string fallbackGroup = fallback ?? "roof-" + Guid.NewGuid().ToString("N");

        string fallbackLabel = _threeDRoofPlacements.FirstOrDefault(placement =>
                SameRoofGroup(placement.RoofGroupId, fallbackGroup))?.Label ??
            _threeDFloorSlabs.FirstOrDefault(IsRoofSlab)?.RoofGroupLabel ??
            _threeDFloorSlabs.FirstOrDefault(IsRoofSlab)?.Label ??
            "Roof 1";

        foreach (ThreeDFloorSlab slab in _threeDFloorSlabs.Where(IsRoofSlab))
        {
            if (string.IsNullOrWhiteSpace(slab.RoofGroupId))
                slab.RoofGroupId = fallbackGroup;
            if (string.IsNullOrWhiteSpace(slab.RoofGroupLabel))
                slab.RoofGroupLabel = fallbackLabel;
        }

        foreach (ThreeDRoofGuide guide in _threeDRoofGuides)
            SetRoofGroupIfMissing(guide, fallbackGroup, fallbackLabel);
        foreach (ThreeDRoofPlane plane in _threeDRoofPlanes)
            SetRoofGroupIfMissing(plane, fallbackGroup, fallbackLabel);
        foreach (ThreeDRoofIssue issue in _threeDRoofIssues)
            SetRoofGroupIfMissing(issue, fallbackGroup, fallbackLabel);

        foreach (string groupId in CurrentRoofGroupIds())
            EnsureThreeDRoofPlacement(groupId, RoofGroupLabel(groupId));
    }

    private IEnumerable<string> CurrentRoofGroupIds() =>
        _threeDFloorSlabs.Where(IsRoofSlab).Select(slab => slab.RoofGroupId)
            .Concat(_threeDRoofGuides.Select(guide => guide.RoofGroupId))
            .Concat(_threeDRoofPlanes.Select(plane => plane.RoofGroupId))
            .Concat(_threeDRoofIssues.Select(issue => issue.RoofGroupId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static void SetRoofGroupIfMissing(ThreeDRoofGuide guide, string groupId, string label)
    {
        if (string.IsNullOrWhiteSpace(guide.RoofGroupId))
            guide.RoofGroupId = groupId;
        if (string.IsNullOrWhiteSpace(guide.RoofGroupLabel))
            guide.RoofGroupLabel = label;
    }

    private static void SetRoofGroupIfMissing(ThreeDRoofPlane plane, string groupId, string label)
    {
        if (string.IsNullOrWhiteSpace(plane.RoofGroupId))
            plane.RoofGroupId = groupId;
        if (string.IsNullOrWhiteSpace(plane.RoofGroupLabel))
            plane.RoofGroupLabel = label;
    }

    private static void SetRoofGroupIfMissing(ThreeDRoofIssue issue, string groupId, string label)
    {
        if (string.IsNullOrWhiteSpace(issue.RoofGroupId))
            issue.RoofGroupId = groupId;
        if (string.IsNullOrWhiteSpace(issue.RoofGroupLabel))
            issue.RoofGroupLabel = label;
    }

    private void AssignRoofGroup(ThreeDRoofFootprintBuildResult result, string groupId, string label)
    {
        foreach (ThreeDFloorSlab slab in result.Slabs)
        {
            slab.RoofGroupId = groupId;
            slab.RoofGroupLabel = label;
        }

        foreach (ThreeDRoofGuide guide in result.Guides)
            SetRoofGroup(guide, groupId, label);
    }

    private static void SetRoofGroup(ThreeDRoofGuide guide, string groupId, string label)
    {
        guide.RoofGroupId = groupId;
        guide.RoofGroupLabel = label;
    }

    private static void SetRoofGroup(ThreeDRoofPlane plane, string groupId, string label)
    {
        plane.RoofGroupId = groupId;
        plane.RoofGroupLabel = label;
    }

    private static void SetRoofGroup(ThreeDRoofIssue issue, string groupId, string label)
    {
        issue.RoofGroupId = groupId;
        issue.RoofGroupLabel = label;
    }

    private void AddGeneratedRoofFootprints(ThreeDRoofFootprintBuildResult result, string sourceLabel)
    {
        string groupId = CreateThreeDRoofGroup(sourceLabel);
        string label = RoofGroupLabel(groupId);
        AssignRoofGroup(result, groupId, label);

        _threeDFloorSlabs.AddRange(result.Slabs);
        _threeDRoofGuides.AddRange(result.Guides);
        _selectedThreeDRoofGuideIds.Clear();
        foreach (ThreeDRoofGuide guide in result.Guides.Where(IsSelectableThreeDRoofBaseGuide))
            _selectedThreeDRoofGuideIds.Add(guide.Id);
        _selectedThreeDRoofGuideId = _selectedThreeDRoofGuideIds.FirstOrDefault() ?? "";

        RefreshThreeDRoofGuideOverlay();
        RefreshThreeDRoofSelector();
        UpdateThreeDRoofMoveControls();
    }

    private void ReplaceActiveRoofBuildResult(string groupId, ThreeDRoofBuildResult build)
    {
        string label = RoofGroupLabel(groupId);
        foreach (ThreeDRoofGuide guide in build.Guides)
            SetRoofGroup(guide, groupId, label);
        foreach (ThreeDRoofPlane plane in build.Planes)
            SetRoofGroup(plane, groupId, label);
        foreach (ThreeDRoofIssue issue in build.Issues)
            SetRoofGroup(issue, groupId, label);

        _threeDRoofGuides.RemoveAll(guide => SameRoofGroup(guide.RoofGroupId, groupId));
        _threeDRoofPlanes.RemoveAll(plane => SameRoofGroup(plane.RoofGroupId, groupId));
        _threeDRoofIssues.RemoveAll(issue => SameRoofGroup(issue.RoofGroupId, groupId));

        _threeDRoofGuides.AddRange(build.Guides);
        _threeDRoofPlanes.AddRange(build.Planes);
        _threeDRoofIssues.AddRange(build.Issues);
        PruneThreeDRoofGuideSelection();
    }

    private ThreeDWallModel CurrentThreeDModelForRoofGroup(string groupId)
    {
        ThreeDWallModel model = CurrentThreeDModel();
        model.Slabs = model.Slabs
            .Where(slab => !IsRoofSlab(slab) || SameRoofGroup(slab.RoofGroupId, groupId))
            .ToList();
        model.RoofGuides = model.RoofGuides
            .Where(guide => SameRoofGroup(guide.RoofGroupId, groupId))
            .ToList();
        model.RoofPlanes = model.RoofPlanes
            .Where(plane => SameRoofGroup(plane.RoofGroupId, groupId))
            .ToList();
        model.RoofIssues = model.RoofIssues
            .Where(issue => SameRoofGroup(issue.RoofGroupId, groupId))
            .ToList();
        return model;
    }

    private Border BuildThreeDRoofPlacementGroup()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        stack.Children.Add(new TextBlock
        {
            Text = "Roof Mesh",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });

        _threeDRoofSelector = new ComboBox
        {
            MinHeight = 22,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = "Select which generated roof mesh/base is active for pitch, move, reset, or delete",
        };
        _threeDRoofSelector.SelectionChanged += ThreeDRoofSelector_SelectionChanged;
        stack.Children.Add(_threeDRoofSelector);

        _threeDRoofMoveText = new TextBlock
        {
            Text = "No roof selected",
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _threeDRoofMoveText.SetResourceReference(Control.ForegroundProperty, "SecondaryForegroundBrush");
        stack.Children.Add(_threeDRoofMoveText);

        _threeDRoofMoveXSlider = RegisterThreeDMutationControl(RoofMoveSlider("Move roof on X, feet"));
        _threeDRoofMoveYSlider = RegisterThreeDMutationControl(RoofMoveSlider("Move roof up/down, feet"));
        _threeDRoofMoveZSlider = RegisterThreeDMutationControl(RoofMoveSlider("Move roof on Z, feet"));
        stack.Children.Add(RoofMoveRow("X", _threeDRoofMoveXSlider));
        stack.Children.Add(RoofMoveRow("Y", _threeDRoofMoveYSlider));
        stack.Children.Add(RoofMoveRow("Z", _threeDRoofMoveZSlider));

        var actions = new UniformGrid { Columns = 2, Margin = new Thickness(0, 4, 0, 0) };
        actions.Children.Add(ThreeDSideButton("Reset Pos", "Move the selected roof back to its generated position", ResetThreeDRoofOffset));
        _threeDRoofDeleteButton = ThreeDSideButton("Delete Roof", "Delete the selected roof base, edge setup, and generated mesh", DeleteActiveThreeDRoof);
        actions.Children.Add(_threeDRoofDeleteButton);
        stack.Children.Add(actions);

        var border = new Border
        {
            Margin = new Thickness(0, 6, 0, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 6, 0, 0),
            Child = stack,
        };
        border.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        return border;
    }

    private Slider RoofMoveSlider(string tooltip)
    {
        var slider = new Slider
        {
            Minimum = -500,
            Maximum = 500,
            SmallChange = 0.25,
            LargeChange = 5,
            TickFrequency = 5,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(0, 0, 0, 2),
            ToolTip = tooltip,
            Style = TryFindResource("RibbonSlider") as Style,
        };
        slider.ValueChanged += ThreeDRoofMoveSlider_ValueChanged;
        slider.PreviewMouseDown += (_, _) => BeginThreeDRoofPlacementDragSnapshot();
        slider.PreviewKeyDown += (_, _) => BeginThreeDRoofPlacementDragSnapshot();
        slider.PreviewMouseUp += ThreeDRoofMoveSlider_Commit;
        slider.KeyUp += ThreeDRoofMoveSlider_Commit;
        return slider;
    }

    private static Grid RoofMoveRow(string label, Slider slider)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var caption = new TextBlock
        {
            Text = label,
            Width = 14,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(caption, 0);
        Grid.SetColumn(slider, 1);
        grid.Children.Add(caption);
        grid.Children.Add(slider);
        return grid;
    }

    private void RefreshThreeDRoofSelector()
    {
        if (_threeDRoofSelector == null)
            return;

        NormalizeThreeDRoofGroupsInMemory();
        _updatingThreeDRoofMoveUi = true;
        try
        {
            _threeDRoofSelector.Items.Clear();
            foreach (ThreeDRoofPlacement placement in _threeDRoofPlacements
                         .Where(placement => HasThreeDRoofGroup(placement.RoofGroupId))
                         .OrderBy(placement => placement.Label, StringComparer.OrdinalIgnoreCase))
            {
                _threeDRoofSelector.Items.Add(new ComboBoxItem
                {
                    Content = placement.Label,
                    Tag = placement.RoofGroupId,
                });
            }

            string active = ActiveThreeDRoofGroupId();
            foreach (ComboBoxItem item in _threeDRoofSelector.Items.OfType<ComboBoxItem>())
            {
                if (!SameRoofGroup(item.Tag?.ToString(), active))
                    continue;

                _threeDRoofSelector.SelectedItem = item;
                break;
            }
        }
        finally
        {
            _updatingThreeDRoofMoveUi = false;
        }
    }

    private void ThreeDRoofSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsModuleEnabled(ModuleId.ThreeD) ||
            _updatingThreeDRoofMoveUi ||
            _threeDRoofSelector?.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string groupId)
        {
            return;
        }

        _selectedThreeDRoofGuideIds.Clear();
        _selectedThreeDRoofGuideId = "";
        SetActiveThreeDRoofGroup(groupId);
        TxtStatus.Text = $"3D Roof: selected {RoofGroupLabel(groupId)}.";
        HideThreeDRoofPitchPopup();
    }

    private void UpdateThreeDRoofMoveControls()
    {
        if (_threeDRoofMoveXSlider == null ||
            _threeDRoofMoveYSlider == null ||
            _threeDRoofMoveZSlider == null ||
            _threeDRoofMoveText == null)
        {
            return;
        }

        ThreeDRoofPlacement? placement = ActiveThreeDRoofPlacement();
        bool hasRoof = placement != null;
        _updatingThreeDRoofMoveUi = true;
        try
        {
            bool canMove = hasRoof && ThreeDEditingAllowed;
            _threeDRoofMoveXSlider.IsEnabled = canMove;
            _threeDRoofMoveYSlider.IsEnabled = canMove;
            _threeDRoofMoveZSlider.IsEnabled = canMove;
            if (_threeDRoofDeleteButton != null)
                _threeDRoofDeleteButton.IsEnabled = canMove;
            _threeDRoofMoveXSlider.Value = hasRoof ? placement!.OffsetXFeet : 0;
            _threeDRoofMoveYSlider.Value = hasRoof ? placement!.OffsetYFeet : 0;
            _threeDRoofMoveZSlider.Value = hasRoof ? placement!.OffsetZFeet : 0;
            _threeDRoofMoveText.Text = hasRoof
                ? $"{placement!.Label}: X {placement.OffsetXFeet:0.##} ft | Y {placement.OffsetYFeet:0.##} ft | Z {placement.OffsetZFeet:0.##} ft"
                : "No roof selected";
        }
        finally
        {
            _updatingThreeDRoofMoveUi = false;
        }
    }

    private void ThreeDRoofMoveSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsModuleEnabled(ModuleId.ThreeD) || _updatingThreeDRoofMoveUi)
            return;

        if (!ThreeDEditingAllowed)
        {
            RestoreThreeDRoofPlacementDragSnapshot();
            UpdateThreeDRoofMoveControls();
            return;
        }

        ThreeDRoofPlacement? placement = ActiveThreeDRoofPlacement();
        if (placement == null ||
            _threeDRoofMoveXSlider == null ||
            _threeDRoofMoveYSlider == null ||
            _threeDRoofMoveZSlider == null)
        {
            return;
        }

        placement.OffsetXFeet = _threeDRoofMoveXSlider.Value;
        placement.OffsetYFeet = _threeDRoofMoveYSlider.Value;
        placement.OffsetZFeet = _threeDRoofMoveZSlider.Value;
        RenderThreeDWallModel(fitCamera: false);
        UpdateThreeDRoofMoveControls();
    }

    private void ThreeDRoofMoveSlider_Commit(object sender, RoutedEventArgs e)
    {
        if (!IsModuleEnabled(ModuleId.ThreeD))
            return;

        if (!EnsureThreeDEditable("move the 3D roof"))
        {
            RestoreThreeDRoofPlacementDragSnapshot();
            UpdateThreeDRoofMoveControls();
            return;
        }

        ThreeDRoofPlacement? placement = ActiveThreeDRoofPlacement();
        if (placement == null)
            return;

        SaveCurrentThreeDModel();
        CompleteThreeDRoofPlacementDragSnapshot();
        LogThreeD($"Roof moved: {placement.Label}, X {placement.OffsetXFeet:0.##} ft, Y {placement.OffsetYFeet:0.##} ft, Z {placement.OffsetZFeet:0.##} ft.");
    }

    private (double X, double Y, double Z) RoofOffsetFor(string groupId)
    {
        ThreeDRoofPlacement? placement = _threeDRoofPlacements.FirstOrDefault(candidate =>
            SameRoofGroup(candidate.RoofGroupId, groupId));
        return placement == null
            ? (0, 0, 0)
            : (placement.OffsetXFeet, placement.OffsetYFeet, placement.OffsetZFeet);
    }

    private List<ThreeDRoofPlane> RenderedThreeDRoofPlanes()
    {
        NormalizeThreeDRoofGroupsInMemory();
        return _threeDRoofPlanes
            .Select(plane =>
            {
                (double ox, double oy, double oz) = RoofOffsetFor(plane.RoofGroupId);
                ThreeDRoofPlane clone = CloneThreeDRoofPlane(plane);
                clone.Points = clone.Points
                    .Select(point => new ThreeDRoofVertex
                    {
                        XFeet = point.XFeet + ox,
                        YFeet = point.YFeet + oy,
                        ZFeet = point.ZFeet + oz,
                    })
                    .ToList();
                return clone;
            })
            .ToList();
    }

    private static ThreeDRoofPlacement CloneThreeDRoofPlacement(ThreeDRoofPlacement placement) =>
        new()
        {
            RoofGroupId = placement.RoofGroupId,
            Label = placement.Label,
            OffsetXFeet = placement.OffsetXFeet,
            OffsetYFeet = placement.OffsetYFeet,
            OffsetZFeet = placement.OffsetZFeet,
        };

    private void DeleteActiveThreeDRoof()
    {
        if (!RequireModule(ModuleId.ThreeD, "Delete 3D roof"))
            return;

        if (!EnsureThreeDEditable("delete the 3D roof"))
            return;

        string groupId = ActiveThreeDRoofGroupId();
        if (string.IsNullOrWhiteSpace(groupId))
        {
            TxtStatus.Text = "3D Roof: no roof selected to delete.";
            return;
        }

        string label = RoofGroupLabel(groupId);
        _threeDFloorSlabs.RemoveAll(slab => IsRoofSlab(slab) && SameRoofGroup(slab.RoofGroupId, groupId));
        _threeDRoofGuides.RemoveAll(guide => SameRoofGroup(guide.RoofGroupId, groupId));
        _threeDRoofPlanes.RemoveAll(plane => SameRoofGroup(plane.RoofGroupId, groupId));
        _threeDRoofIssues.RemoveAll(issue => SameRoofGroup(issue.RoofGroupId, groupId));
        _threeDRoofPlacements.RemoveAll(placement => SameRoofGroup(placement.RoofGroupId, groupId));
        _selectedThreeDRoofGuideIds.Clear();
        _selectedThreeDRoofGuideId = "";
        _activeThreeDRoofGroupId = _threeDRoofPlacements.FirstOrDefault()?.RoofGroupId ?? "";
        SetThreeDRoofEdgeSelectMode(enabled: false);
        RefreshThreeDRoofGuideOverlay();
        RefreshThreeDRoofSelector();
        RenderThreeDWallModel(fitCamera: false);
        SaveCurrentThreeDModel(allowEmpty: true);
        HideThreeDRoofPitchPopup();
        TxtStatus.Text = $"3D Roof: deleted {label}.";
        LogThreeD($"Deleted roof group: {label}.");
    }
}
