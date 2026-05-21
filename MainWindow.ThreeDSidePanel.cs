using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    private CheckBox? _threeDRoofDefinesSlopeCheck;
    private TextBox? _threeDRoofEdgePitchBox;
    private TextBox? _threeDRoofEdgeOverhangBox;
    private Button? _threeDRoofApplyEdgeButton;
    private Border? _threeDRoofEdgeGroup;
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
        toolbar.Children.Add(ThreeDSideButton("Roof Base", "Create a separate roof base layer from selected area measurements or RF/roof takeoffs", () =>
            BuildRoofFromRfAreas(TakeoffsTree.SelectedItem as TreeViewItem, switchTo3DTab: false)));
        toolbar.Children.Add(ThreeDSideButton("Select Edge", "Toggle roof base edge selection on the sheet", ToggleThreeDRoofEdgeSelectMode));
        toolbar.Children.Add(new TextBlock
        {
            Text = "Default",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 3, 4),
        });
        _threeDRoofPitchBox = new TextBox
        {
            Text = "6/12",
            Width = 56,
            MinHeight = 22,
            FontSize = 11,
            Margin = new Thickness(0, 0, 4, 4),
            ToolTip = "Default pitch for Auto Roof and new slope edges, for example 6/12, 4, or 0.333",
        };
        _threeDRoofPitchBox.KeyDown += (_, e) =>
        {
            if (KeyboardShortcutKeys.EffectiveKey(e) != Key.Enter)
                return;

            GenerateThreeDRoofFromUi();
            e.Handled = true;
        };
        toolbar.Children.Add(_threeDRoofPitchBox);
        toolbar.Children.Add(ThreeDSideButton("Generate Roof", "Build ridge/hip/valley from the saved per-edge roof pitches", GenerateThreeDRoofFromUi));
        toolbar.Children.Add(ThreeDSideButton("Move Roof", "Toggle move mode, then drag in the 3D viewer to slide the roof over the walls when they were drawn from different sheets", ToggleThreeDRoofMoveMode));
        toolbar.Children.Add(ThreeDSideButton("Reset Pos", "Move the roof back to its generated position", ResetThreeDRoofOffset));
        toolbar.Children.Add(ThreeDSideButton("Roof Qty", "Show roof takeoff quantities: sloped/plan area, ridge, hip, valley and eave lengths", ShowThreeDRoofQuantities));
        toolbar.Children.Add(ThreeDSideButton("Roof Takeoff", "Create reviewable takeoff items (eave/ridge/hip/valley lines + plan area) from the generated roof", CreateRoofTakeoffFromGenerated));
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

        editor.Children.Add(BuildThreeDRoofEdgeGroup());
        editor.Children.Add(BuildThreeDRoofPlacementGroup());

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

    // Revit-style roof edge property block: select edge(s) on the sheet, save
    // Defines Slope / Pitch / Overhang per edge, then Generate Roof.
    private Border BuildThreeDRoofEdgeGroup()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        stack.Children.Add(new TextBlock
        {
            Text = "Roof Edge",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });

        _threeDRoofDefinesSlopeCheck = new CheckBox
        {
            Content = "Defines Slope (eave)",
            IsThreeState = true,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = "When on, this edge slopes a roof plane at the pitch below; off makes it a flat rake/gable edge",
        };
        stack.Children.Add(_threeDRoofDefinesSlopeCheck);

        var inputs = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };
        _threeDRoofEdgePitchBox = ThreeDEditBox("Pitch rise/run, e.g. 6/12, 4, or 0.333");
        _threeDRoofEdgeOverhangBox = ThreeDEditBox("Overhang beyond the wall, in inches");
        _threeDRoofEdgePitchBox.KeyDown += ThreeDRoofEdgeField_KeyDown;
        _threeDRoofEdgeOverhangBox.KeyDown += ThreeDRoofEdgeField_KeyDown;
        inputs.Children.Add(LabeledField("Pitch", _threeDRoofEdgePitchBox));
        inputs.Children.Add(LabeledField("Overhang in", _threeDRoofEdgeOverhangBox));
        stack.Children.Add(inputs);

        _threeDRoofApplyEdgeButton = ThreeDSideButton(
            "Save Edge(s)",
            "Write Defines Slope / Pitch / Overhang to the selected roof edge(s); Generate Roof builds after all edges are set",
            ApplyThreeDRoofEdgePropertiesFromPanel);
        stack.Children.Add(_threeDRoofApplyEdgeButton);

        _threeDRoofEdgeGroup = new Border
        {
            Margin = new Thickness(0, 6, 0, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 6, 0, 0),
            Child = stack,
        };
        _threeDRoofEdgeGroup.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        return _threeDRoofEdgeGroup;
    }

    private void ThreeDRoofEdgeField_KeyDown(object sender, KeyEventArgs e)
    {
        if (KeyboardShortcutKeys.EffectiveKey(e) != Key.Enter)
            return;

        ApplyThreeDRoofEdgePropertiesFromPanel();
        e.Handled = true;
    }

    private static StackPanel LabeledField(string label, UIElement field)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        var caption = new TextBlock { Text = label, FontSize = 10 };
        caption.SetResourceReference(Control.ForegroundProperty, "SecondaryForegroundBrush");
        panel.Children.Add(caption);
        panel.Children.Add(field);
        return panel;
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

        UpdateThreeDRoofEdgeGroup(selectedRoofEdgeCount);
        UpdateThreeDRoofMoveControls();
    }

    private void UpdateThreeDRoofEdgeGroup(int selectedRoofEdgeCount)
    {
        if (_threeDRoofEdgeGroup == null ||
            _threeDRoofDefinesSlopeCheck == null ||
            _threeDRoofEdgePitchBox == null ||
            _threeDRoofEdgeOverhangBox == null ||
            _threeDRoofApplyEdgeButton == null)
        {
            return;
        }

        IReadOnlyList<ThreeDRoofGuide> guides = SelectedThreeDRoofGuides();
        bool any = guides.Count > 0;
        _threeDRoofEdgeGroup.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _threeDRoofDefinesSlopeCheck.IsEnabled = any;
        _threeDRoofEdgePitchBox.IsEnabled = any;
        _threeDRoofEdgeOverhangBox.IsEnabled = any;
        _threeDRoofApplyEdgeButton.IsEnabled = any;
        if (!any)
            return;

        // Mixed selections show indeterminate / blank so Apply only writes what
        // the user actually touched.
        bool allSlope = guides.All(g => g.DefinesSlope);
        bool noneSlope = guides.All(g => !g.DefinesSlope);
        _threeDRoofDefinesSlopeCheck.IsChecked = allSlope ? true : noneSlope ? false : null;

        if (allSlope)
        {
            double firstPitch = guides[0].PitchRisePerFoot > 0 ? guides[0].PitchRisePerFoot : ThreeDRoofPreviewBuilder.DefaultPitchRisePerFoot;
            bool samePitch = guides.All(g => Math.Abs((g.PitchRisePerFoot > 0 ? g.PitchRisePerFoot : ThreeDRoofPreviewBuilder.DefaultPitchRisePerFoot) - firstPitch) < 0.0001);
            _threeDRoofEdgePitchBox.Text = samePitch ? RoofPitchText.Format(firstPitch) : "";
        }
        else
        {
            _threeDRoofEdgePitchBox.Text = "";
        }

        double firstOverhang = guides[0].OverhangFeet;
        bool sameOverhang = guides.All(g => Math.Abs(g.OverhangFeet - firstOverhang) < 0.0001);
        _threeDRoofEdgeOverhangBox.Text = sameOverhang
            ? (firstOverhang * 12.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
            : "";

        _ = selectedRoofEdgeCount;
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
