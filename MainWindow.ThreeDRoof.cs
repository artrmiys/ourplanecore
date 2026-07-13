using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    // 3D roof state, toolbar commands, viewport context menu, and edge-select mode.

    private readonly List<ThreeDRoofGuide> _threeDRoofGuides = [];
    private readonly List<ThreeDRoofPlane> _threeDRoofPlanes = [];
    private readonly List<ThreeDRoofIssue> _threeDRoofIssues = [];
    private readonly HashSet<string> _selectedThreeDRoofGuideIds = new(StringComparer.Ordinal);
    private string _selectedThreeDRoofGuideId = "";
    private bool _threeDRoofEdgeSelectModeEnabled;

    private void Btn3dBuildRoof_Click(object sender, RoutedEventArgs e) =>
        GenerateThreeDRoofFromUi();

    private void GenerateThreeDRoofFromUi()
    {
        if (!RequireModule(ModuleId.ThreeD, "Generate 3D roof"))
            return;

        if (!TryApplyPendingThreeDRoofEdgePropertiesFromPanel(showStatus: false))
            return;

        BuildThreeDRoofPreview();
    }

    private void Btn3dRfRoof_Click(object sender, RoutedEventArgs e) =>
        BuildRoofFromRfAreas(TakeoffsTree.SelectedItem as TreeViewItem, switchTo3DTab: true);

    private void Btn3dAutoRoof_Click(object sender, RoutedEventArgs e) =>
        BuildAutoThreeDRoof(TakeoffsTree.SelectedItem as TreeViewItem, switchTo3DTab: true);

    private void Btn3dClearRoof_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.ThreeD, "Clear 3D roof"))
            return;

        ClearThreeDRoof();
    }

    private void Btn3dRoofEdges_Click(object sender, RoutedEventArgs e) =>
        ToggleThreeDRoofEdgeSelectMode();

    private void AddViewportThreeDMenuItems(ContextMenu menu, ViewportContextRequest request)
    {
        if (!IsModuleEnabled(ModuleId.ThreeD))
            return;

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
        roofMenu.Items.Add(MakeMenuItem(
            _threeDRoofEdgeSelectModeEnabled ? "Roof Edge Select Off" : "Roof Edge Select",
            _currentJob != null && _threeDRoofGuides.Count > 0,
            ToggleThreeDRoofEdgeSelectMode));
        roofMenu.Items.Add(MakeMenuItem(
            "Clear Roof Base",
            HasGeneratedRoofBase() || _threeDRoofGuides.Count > 0 || _threeDRoofPlanes.Count > 0 || _threeDRoofIssues.Count > 0,
            ClearThreeDRoof));
        menu.Items.Add(roofMenu);
        _ = request;
    }

    private void ToggleThreeDRoofEdgeSelectMode()
    {
        if (!_threeDRoofEdgeSelectModeEnabled &&
            !RequireModule(ModuleId.ThreeD, "Select 3D roof edges"))
        {
            return;
        }

        SetThreeDRoofEdgeSelectMode(!_threeDRoofEdgeSelectModeEnabled);
    }

    private void SetThreeDRoofEdgeSelectMode(bool enabled)
    {
        if (enabled && _threeDRoofGuides.Count == 0)
        {
            TxtStatus.Text = "3D Roof: create Roof Base first, then select its edges.";
            return;
        }

        _threeDRoofEdgeSelectModeEnabled = enabled;
        _viewport.SetThreeDRoofEdgeSelectMode(enabled);
        if (!enabled)
            HideThreeDRoofPitchPopup();
        TxtStatus.Text = enabled
            ? "3D Roof Edge Select: click a roof base edge, set its pitch in Roof Edge, then Generate Roof. Ctrl/Shift-click adds more."
            : "3D Roof Edge Select off.";
    }

    private void OnThreeDRoofGuideSelectionRequested(string guideId, bool additive)
    {
        if (!IsModuleEnabled(ModuleId.ThreeD))
            return;

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
}
