using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OurPlaneCore;

public partial class MainWindow
{
    private TextBlock? _threeDEditSelectionText;
    private TextBox? _threeDHeightBox;
    private TextBox? _threeDThicknessBox;
    private TextBox? _threeDRoofPitchBox;
    private Button? _threeDApplyWallButton;
    private Button? _threeDApplyGroupButton;
    private CheckBox? _threeDRoofDefinesSlopeBox;
    private TextBox? _threeDRoofEdgePitchBox;
    private TextBox? _threeDRoofEdgeOverhangBox;
    private Button? _threeDApplyRoofEdgeButton;
    private DockPanel BuildThreeDSidePanel()
    {
        var panel = new DockPanel { Margin = new Thickness(2) };
        panel.SetResourceReference(Panel.BackgroundProperty, "PanelBackgroundBrush");

        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4),
        };
        DockPanel.SetDock(toolbar, Dock.Top);
        toolbar.Children.Add(ThreeDSideButton("Auto", "Auto-build 3D walls, sqft slabs, and RF/roof areas when available", () =>
            BuildAuto3DWallsFromTakeoffs(switchTo3DTab: false)));
        toolbar.Children.Add(ThreeDSideButton("Wall", "Build wall prisms from selected line takeoffs", () =>
            Build3DWallsFromTakeoffSelection(TakeoffsTree.SelectedItem as TreeViewItem, switchTo3DTab: false)));
        toolbar.Children.Add(ThreeDSideButton("Roof Base", "Create a separate roof base layer from selected area measurements or RF/roof takeoffs", () =>
            BuildRoofFromRfAreas(TakeoffsTree.SelectedItem as TreeViewItem, switchTo3DTab: false)));
        toolbar.Children.Add(ThreeDSideButton("Auto Roof", "Create roof base from RF/roof areas, auto-select eaves, and generate a preview", () =>
            BuildAutoThreeDRoof(TakeoffsTree.SelectedItem as TreeViewItem, switchTo3DTab: false)));
        toolbar.Children.Add(ThreeDSideButton("Edges", "Toggle roof base edge selection on the sheet", ToggleThreeDRoofEdgeSelectMode));
        toolbar.Children.Add(ThreeDSideButton("Generate", "Generate roof mesh from roof base Eave edges and pitch", BuildThreeDRoofPreview));
        toolbar.Children.Add(ThreeDSideButton("Clear Roof", "Clear roof base, edge roles, and generated mesh", ClearThreeDRoof));
        _threeDRoofPitchBox = new TextBox
        {
            Text = "6/12",
            Width = 48,
            MinHeight = 22,
            FontSize = 11,
            Margin = new Thickness(0, 0, 4, 4),
            ToolTip = "Pitch for new slope-defining eave edges, for example 6/12, 4, or 0.333",
        };
        toolbar.Children.Add(_threeDRoofPitchBox);
        toolbar.Children.Add(ThreeDSideButton("Sel Pitch", "Apply this pitch to the selected roof edge(s) and mark them as Eave", ApplyThreeDRoofPitchToSelectedEdges));
        toolbar.Children.Add(ThreeDSideButton("All Pitch", "Apply this pitch to all saved eave edges and rebuild the roof surface", ApplyThreeDRoofPitchToEaves));
        toolbar.Children.Add(ThreeDSideButton("Sel Eave", "Mark the selected roof edge(s) as Eave with the current pitch", () =>
            SetSelectedThreeDRoofGuideKind(ThreeDRoofGuideKinds.Eave, applyPitch: true)));
        toolbar.Children.Add(ThreeDSideButton("Draw Eave", "Draw an approximate eave segment; it snaps to and splits the nearest roof base edge", StartThreeDRoofEaveGuideMode));
        toolbar.Children.Add(ThreeDSideButton("Use Eave", "Match selected eave line takeoffs to roof base edges", () =>
            ApplySelectedEaveTakeoffsToRoofEdges(TakeoffsTree.SelectedItem as TreeViewItem)));
        toolbar.Children.Add(ThreeDSideButton("Sel Rake", "Mark the selected roof edge(s) as Rake with no slope contribution", () =>
            SetSelectedThreeDRoofGuideKind(ThreeDRoofGuideKinds.Rake, applyPitch: false)));
        toolbar.Children.Add(ThreeDSideButton("Clear Sel", "Clear selected roof edges", ClearThreeDRoofGuideSelection));
        toolbar.Children.Add(ThreeDSideButton("Fit", "Fit the side 3D view", () =>
        {
            _threeDSideViewerTarget = new Point3D(0, 0, 0);
            SetThreeDSideViewerView(_threeDSideViewerYaw, _threeDSideViewerPitch, ThreeDViewerFitDistance());
        }));
        toolbar.Children.Add(ThreeDSideButton("Iso", "Side 3D isometric view", () =>
            SetThreeDSideViewerView(-38, 28, ThreeDViewerFitDistance())));
        toolbar.Children.Add(ThreeDSideButton("Top", "Side 3D top view", () =>
            SetThreeDSideViewerView(0, 86, ThreeDViewerFitDistance())));
        toolbar.Children.Add(ThreeDSideButton("Front", "Side 3D front view", () =>
            SetThreeDSideViewerView(0, 12, ThreeDViewerFitDistance())));
        toolbar.Children.Add(ThreeDSideButton("Full", "Open the shared 3D workspace tab", () =>
            SelectWorkspaceTab("3DManager")));
        panel.Children.Add(toolbar);

        _threeDSideSummaryText = new TextBlock
        {
            Text = "3D viewer ready. Select takeoff lines for Wall, or area takeoffs for Roof Base.",
            Margin = new Thickness(4, 0, 4, 4),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 70,
        };
        _threeDSideSummaryText.SetResourceReference(Control.ForegroundProperty, "SecondaryForegroundBrush");
        DockPanel.SetDock(_threeDSideSummaryText, Dock.Top);
        panel.Children.Add(_threeDSideSummaryText);

        Border editor = BuildThreeDEditorPanel();
        DockPanel.SetDock(editor, Dock.Top);
        panel.Children.Add(editor);

        Border logPanel = BuildThreeDLogPanel();
        DockPanel.SetDock(logPanel, Dock.Bottom);
        panel.Children.Add(logPanel);

        var viewportGrid = new Grid
        {
            Background = Brushes.Transparent,
        };
        _threeDSideCamera = new PerspectiveCamera
        {
            Position = new Point3D(18, 14, 18),
            LookDirection = new Vector3D(-18, -10, -18),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 42,
        };
        _threeDSideViewport = new Viewport3D
        {
            ClipToBounds = true,
            Camera = _threeDSideCamera,
        };
        viewportGrid.MouseLeftButtonDown += ThreeDSideViewerViewport_MouseLeftButtonDown;
        viewportGrid.MouseLeftButtonUp += ThreeDSideViewerViewport_MouseLeftButtonUp;
        viewportGrid.MouseRightButtonDown += ThreeDSideViewerViewport_MouseRightButtonDown;
        viewportGrid.MouseRightButtonUp += ThreeDSideViewerViewport_MouseRightButtonUp;
        viewportGrid.MouseMove += ThreeDSideViewerViewport_MouseMove;
        viewportGrid.MouseWheel += ThreeDSideViewerViewport_MouseWheel;
        _threeDSideViewport.Children.Add(new ModelVisual3D { Content = CreateCleanThreeDViewerSceneGroup() });
        SetThreeDSideViewerView(-38, 28, ThreeDViewerFitDistance());
        viewportGrid.Children.Add(_threeDSideViewport);

        var border = new Border
        {
            Margin = new Thickness(0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = viewportGrid,
        };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        panel.Children.Add(border);

        return panel;
    }

    private Border BuildThreeDEditorPanel()
    {
        var editor = new StackPanel { Margin = new Thickness(4, 0, 4, 6) };
        _threeDEditSelectionText = new TextBlock
        {
            Text = "No 3D wall selected",
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _threeDEditSelectionText.SetResourceReference(Control.ForegroundProperty, "SecondaryForegroundBrush");
        editor.Children.Add(_threeDEditSelectionText);

        var inputs = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };
        _threeDHeightBox = ThreeDEditBox("Height ft");
        _threeDThicknessBox = ThreeDEditBox("Width in");
        inputs.Children.Add(_threeDHeightBox);
        inputs.Children.Add(_threeDThicknessBox);
        editor.Children.Add(inputs);

        var actions = new UniformGrid { Columns = 2 };
        _threeDApplyWallButton = ThreeDSideButton("Apply Wall", "Apply height and width to selected 3D wall segment", () => ApplyThreeDWallEditor(applyGroup: false));
        _threeDApplyGroupButton = ThreeDSideButton("Apply Group", "Apply height and width to all segments from this takeoff", () => ApplyThreeDWallEditor(applyGroup: true));
        actions.Children.Add(_threeDApplyWallButton);
        actions.Children.Add(_threeDApplyGroupButton);
        editor.Children.Add(actions);

        editor.Children.Add(BuildThreeDRoofEdgeEditor());

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Child = editor,
        };
        border.SetResourceReference(Border.BackgroundProperty, "ControlBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        UpdateThreeDEditor();
        return border;
    }

    // Revit-style per-edge roof properties. Edges are selected on the PDF
    // sheet (Roof Edge Select); their slope/pitch/overhang are edited here.
    private Border BuildThreeDRoofEdgeEditor()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        var header = new TextBlock
        {
            Text = "Roof Edge",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        header.SetResourceReference(Control.ForegroundProperty, "SecondaryForegroundBrush");
        stack.Children.Add(header);

        _threeDRoofDefinesSlopeBox = new CheckBox
        {
            Content = "Defines Slope",
            FontSize = 11,
            IsThreeState = true,
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = "When on, this roof base edge contributes a slope plane (Revit \"Defines Slope\").",
        };
        _threeDRoofDefinesSlopeBox.Click += (_, _) =>
        {
            // A user click resolves the indeterminate (mixed) state to on.
            if (_threeDRoofDefinesSlopeBox.IsChecked == null)
                _threeDRoofDefinesSlopeBox.IsChecked = true;
        };
        stack.Children.Add(_threeDRoofDefinesSlopeBox);

        var inputs = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };
        _threeDRoofEdgePitchBox = ThreeDEditBox("Pitch as rise/run, e.g. 6/12");
        _threeDRoofEdgeOverhangBox = ThreeDEditBox("Overhang in inches (recorded; projection geometry in a later pass)");
        inputs.Children.Add(_threeDRoofEdgePitchBox);
        inputs.Children.Add(_threeDRoofEdgeOverhangBox);
        stack.Children.Add(inputs);

        _threeDApplyRoofEdgeButton = ThreeDSideButton(
            "Apply Edge",
            "Apply Defines Slope, pitch, and overhang to the selected roof edge(s) and rebuild the roof",
            ApplyThreeDRoofEdgeProperties);
        stack.Children.Add(_threeDApplyRoofEdgeButton);

        var border = new Border
        {
            Margin = new Thickness(0, 4, 0, 0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Child = stack,
        };
        border.SetResourceReference(Border.BackgroundProperty, "ControlBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        return border;
    }

    private Border BuildThreeDLogPanel()
    {
        _threeDLogBox = new TextBox
        {
            Text = "No 3D log messages yet.",
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MinHeight = 58,
            MaxHeight = 86,
            FontSize = 10,
            Padding = new Thickness(4),
            BorderThickness = new Thickness(0),
        };
        _threeDLogBox.SetResourceReference(Control.BackgroundProperty, "SurfaceBackgroundBrush");
        _threeDLogBox.SetResourceReference(Control.ForegroundProperty, "SecondaryForegroundBrush");
        RefreshThreeDLogBox();

        var border = new Border
        {
            Margin = new Thickness(0, 6, 0, 0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = _threeDLogBox,
        };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        return border;
    }

    private static TextBox ThreeDEditBox(string tooltip) =>
        new()
        {
            Margin = new Thickness(0, 0, 4, 0),
            MinHeight = 22,
            FontSize = 11,
            ToolTip = tooltip,
        };

    private void UpdateThreeDEditor()
    {
        if (_threeDEditSelectionText == null ||
            _threeDHeightBox == null ||
            _threeDThicknessBox == null ||
            _threeDApplyWallButton == null ||
            _threeDApplyGroupButton == null)
        {
            return;
        }

        bool hasWall = _selectedThreeDWall != null;
        ThreeDRoofGuide? selectedRoofEdge = SelectedThreeDRoofGuide();
        int selectedRoofEdgeCount = SelectedThreeDRoofGuideCount();
        _threeDEditSelectionText.Text = hasWall
            ? $"{_selectedThreeDWall!.Label} | {_selectedThreeDWall.LevelKey} | base {_selectedThreeDWall.BaseElevationFeet:F1} ft"
            : _selectedThreeDFloorSlab != null
                ? $"{_selectedThreeDFloorSlab.Label} slab | elev {_selectedThreeDFloorSlab.ElevationFeet:F1} ft"
                : selectedRoofEdgeCount > 1
                    ? $"{selectedRoofEdgeCount} roof edges selected"
                    : selectedRoofEdge != null
                    ? $"{selectedRoofEdge.Label} | {ThreeDRoofGuideKinds.Title(selectedRoofEdge.Kind)} | {RoofGuidePitchLabel(selectedRoofEdge)}"
                    : "No 3D wall selected";
        _threeDHeightBox.Text = hasWall ? _selectedThreeDWall!.HeightFeet.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "";
        _threeDThicknessBox.Text = hasWall ? _selectedThreeDWall!.ThicknessInches.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "";
        _threeDHeightBox.IsEnabled = hasWall;
        _threeDThicknessBox.IsEnabled = hasWall;
        _threeDApplyWallButton.IsEnabled = hasWall;
        _threeDApplyGroupButton.IsEnabled = hasWall;

        UpdateThreeDRoofEdgeEditor(SelectedThreeDRoofGuides());
    }

    private void UpdateThreeDRoofEdgeEditor(IReadOnlyList<ThreeDRoofGuide> guides)
    {
        if (_threeDRoofDefinesSlopeBox == null ||
            _threeDRoofEdgePitchBox == null ||
            _threeDRoofEdgeOverhangBox == null ||
            _threeDApplyRoofEdgeButton == null)
        {
            return;
        }

        bool hasSelection = guides.Count > 0;
        _threeDRoofDefinesSlopeBox.IsEnabled = hasSelection;
        _threeDRoofEdgePitchBox.IsEnabled = hasSelection;
        _threeDRoofEdgeOverhangBox.IsEnabled = hasSelection;
        _threeDApplyRoofEdgeButton.IsEnabled = hasSelection;

        if (!hasSelection)
        {
            _threeDRoofDefinesSlopeBox.IsChecked = false;
            _threeDRoofEdgePitchBox.Text = "";
            _threeDRoofEdgeOverhangBox.Text = "";
            return;
        }

        bool allSlope = guides.All(guide => guide.DefinesSlope);
        bool noneSlope = guides.All(guide => !guide.DefinesSlope);
        _threeDRoofDefinesSlopeBox.IsChecked = allSlope ? true : noneSlope ? false : null;

        double firstPitch = guides[0].PitchRisePerFoot;
        bool samePitch = guides.All(guide => Math.Abs(guide.PitchRisePerFoot - firstPitch) < 0.0005);
        _threeDRoofEdgePitchBox.Text = samePitch && firstPitch > 0
            ? RoofPitchText.Format(firstPitch)
            : "";

        double firstOverhangIn = guides[0].OverhangFeet * 12.0;
        bool sameOverhang = guides.All(guide => Math.Abs(guide.OverhangFeet * 12.0 - firstOverhangIn) < 0.05);
        _threeDRoofEdgeOverhangBox.Text = sameOverhang
            ? firstOverhangIn.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "";
    }

    private static Button ThreeDSideButton(string text, string tooltip, Action action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 4, 4),
            MinWidth = 40,
            FontSize = 11,
            ToolTip = tooltip,
        };
        button.Click += (_, _) => action();
        return button;
    }
}
