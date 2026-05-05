using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SmartTakeoffs;

public partial class MainWindow
{
    private DockPanel BuildMassingDraftPanel()
    {
        var panel = new DockPanel { Margin = new Thickness(2) };
        panel.SetResourceReference(Panel.BackgroundProperty, "PanelBackgroundBrush");

        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 2),
        };
        DockPanel.SetDock(buttons, Dock.Top);

        var build = new Button
        {
            Content = "Build 3D Draft",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 4),
            ToolTip = "Build AI_Context/3d_massing/model.json from current AI markers",
        };
        build.Click += BtnBuildMassingDraft_Click;
        buttons.Children.Add(build);

        var buildFromWalls = new Button
        {
            Content = "3D From Takeoffs",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 4),
            ToolTip = "Build 3D draft from Walls/Areas/Sqft level Line/Area measurements",
        };
        buildFromWalls.Click += (_, _) => BuildMassingDraftFromWallTakeoffs();
        buttons.Children.Add(buildFromWalls);

        var open3DWindow = new Button
        {
            Content = "3D Window",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 4),
            ToolTip = "Open a separate orbitable 3D viewport with saved marker points",
        };
        open3DWindow.Click += (_, _) => OpenMassing3DWindow();
        buttons.Children.Add(open3DWindow);

        var detectRoof = new Button
        {
            Content = "Auto Roof",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 4),
            ToolTip = "Queue reviewable AI roof marker candidates from the active sheet",
        };
        detectRoof.Click += BtnDetectRoof_Click;
        buttons.Children.Add(detectRoof);

        _massingReviewRoofButton = new Button
        {
            Content = "Review Roof",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 4),
            IsEnabled = false,
            ToolTip = "Review and save roof type, pitch, notes, and guide points before accepting roof geometry",
        };
        _massingReviewRoofButton.Click += BtnReviewRoof_Click;
        buttons.Children.Add(_massingReviewRoofButton);

        _massingReviewOpeningsButton = new Button
        {
            Content = "Review Openings",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 4),
            IsEnabled = false,
            ToolTip = "Review projected door/window/opening markers before accepting the 3D draft",
        };
        _massingReviewOpeningsButton.Click += BtnReviewOpenings_Click;
        buttons.Children.Add(_massingReviewOpeningsButton);

        _massingAcceptDraftButton = new Button
        {
            Content = "Accept 3D",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 4),
            IsEnabled = false,
            ToolTip = "Mark the current 3D massing draft as reviewed project context",
        };
        _massingAcceptDraftButton.Click += BtnAcceptMassingDraft_Click;
        buttons.Children.Add(_massingAcceptDraftButton);

        _massingOpenDraftButton = new Button
        {
            Content = "Open JSON",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 4),
            IsEnabled = false,
            ToolTip = "Open AI_Context/3d_massing/model.json",
        };
        _massingOpenDraftButton.Click += (_, _) => OpenMassingDraftJson();
        buttons.Children.Add(_massingOpenDraftButton);

        buttons.Children.Add(MassingViewButton("Fit", "Fit 3D preview to model", () => FitMassing3DView(resetAngles: false)));
        buttons.Children.Add(MassingViewButton("Iso", "Isometric 3D preview", () => SetMassing3DView(-38, 28)));
        buttons.Children.Add(MassingViewButton("Top", "Top 3D preview", () => SetMassing3DView(0, 88)));
        buttons.Children.Add(MassingViewButton("Front", "Front 3D preview", () => SetMassing3DView(0, 12)));
        panel.Children.Add(buttons);

        var previewGrid = new Grid();
        _massingPreviewCanvas = new Canvas
        {
            ClipToBounds = true,
        };
        _massingPreviewCanvas.SetResourceReference(Panel.BackgroundProperty, "SurfaceBackgroundBrush");
        _massingPreviewCanvas.SizeChanged += (_, _) => DrawMassingPreview(_currentMassingDraft);
        previewGrid.Children.Add(_massingPreviewCanvas);

        _massingPreviewStatusText = new TextBlock
        {
            Text = "Build a draft to preview the footprint.",
            Margin = new Thickness(8, 6, 8, 6),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _massingPreviewStatusText.SetResourceReference(Control.ForegroundProperty, "SecondaryForegroundBrush");
        previewGrid.Children.Add(_massingPreviewStatusText);

        var previewBorder = new Border
        {
            Height = 170,
            Margin = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = previewGrid,
        };
        previewBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBackgroundBrush");
        previewBorder.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        DockPanel.SetDock(previewBorder, Dock.Top);
        panel.Children.Add(previewBorder);

        var viewportGrid = new Grid();
        _massingViewport3D = new Viewport3D
        {
            ClipToBounds = true,
        };
        _massingViewport3D.MouseLeftButtonDown += MassingViewport3D_MouseLeftButtonDown;
        _massingViewport3D.MouseLeftButtonUp += MassingViewport3D_MouseLeftButtonUp;
        _massingViewport3D.MouseMove += MassingViewport3D_MouseMove;
        _massingViewport3D.MouseWheel += MassingViewport3D_MouseWheel;
        viewportGrid.Children.Add(_massingViewport3D);

        _massingViewportStatusText = new TextBlock
        {
            Text = "Build a draft to preview the 3D shell.",
            Margin = new Thickness(8, 6, 8, 6),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _massingViewportStatusText.SetResourceReference(Control.ForegroundProperty, "SecondaryForegroundBrush");
        viewportGrid.Children.Add(_massingViewportStatusText);

        var viewportBorder = new Border
        {
            Height = 220,
            Margin = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = viewportGrid,
        };
        viewportBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBackgroundBrush");
        viewportBorder.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        DockPanel.SetDock(viewportBorder, Dock.Top);
        panel.Children.Add(viewportBorder);

        var markerLabel = new TextBlock
        {
            Text = "Source markers",
            FontWeight = FontWeights.Normal,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 3),
        };
        markerLabel.SetResourceReference(Control.ForegroundProperty, "ControlForegroundBrush");
        DockPanel.SetDock(markerLabel, Dock.Top);
        panel.Children.Add(markerLabel);

        _massingMarkerList = new ListView
        {
            Height = 118,
            MinHeight = 72,
            Margin = new Thickness(0, 0, 0, 4),
            FontSize = 11,
            View = new GridView
            {
                Columns =
                {
                    new GridViewColumn { Header = "Role", Width = 72, DisplayMemberBinding = new Binding(nameof(MassingMarkerReviewRow.Role)) },
                    new GridViewColumn { Header = "Type", Width = 118, DisplayMemberBinding = new Binding(nameof(MassingMarkerReviewRow.Type)) },
                    new GridViewColumn { Header = "Page", Width = 72, DisplayMemberBinding = new Binding(nameof(MassingMarkerReviewRow.Page)) },
                    new GridViewColumn { Header = "Point", Width = 88, DisplayMemberBinding = new Binding(nameof(MassingMarkerReviewRow.PdfPoint)) },
                    new GridViewColumn { Header = "Draft", Width = 88, DisplayMemberBinding = new Binding(nameof(MassingMarkerReviewRow.DraftPoint)) },
                    new GridViewColumn { Header = "Status", Width = 80, DisplayMemberBinding = new Binding(nameof(MassingMarkerReviewRow.Status)) },
                },
            },
        };
        _massingMarkerList.SetResourceReference(Control.BackgroundProperty, "SurfaceBackgroundBrush");
        _massingMarkerList.SetResourceReference(Control.ForegroundProperty, "ControlForegroundBrush");
        _massingMarkerList.SelectionChanged += (_, _) => UpdateMassingMarkerActionButtons();
        _massingMarkerList.MouseDoubleClick += (_, _) => JumpToSelectedMassingMarker();
        DockPanel.SetDock(_massingMarkerList, Dock.Top);
        panel.Children.Add(_massingMarkerList);

        var markerButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4),
        };
        DockPanel.SetDock(markerButtons, Dock.Top);

        _massingJumpMarkerButton = new Button
        {
            Content = "Jump",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 0),
            IsEnabled = false,
            ToolTip = "Open the source marker sheet",
        };
        _massingJumpMarkerButton.Click += (_, _) => JumpToSelectedMassingMarker();
        markerButtons.Children.Add(_massingJumpMarkerButton);

        _massingOpenMarkerButton = new Button
        {
            Content = "Marker JSON",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 0),
            IsEnabled = false,
            ToolTip = "Open the selected source marker JSON",
        };
        _massingOpenMarkerButton.Click += (_, _) => OpenSelectedMassingMarkerJson();
        markerButtons.Children.Add(_massingOpenMarkerButton);

        _massingOpenMarkerCropButton = new Button
        {
            Content = "Crop",
            Padding = new Thickness(6, 2, 6, 2),
            IsEnabled = false,
            ToolTip = "Open the selected source marker crop",
        };
        _massingOpenMarkerCropButton.Click += (_, _) => OpenSelectedMassingMarkerCrop();
        markerButtons.Children.Add(_massingOpenMarkerCropButton);
        panel.Children.Add(markerButtons);

        _massingMarkerDetailsTextBox = new TextBox
        {
            Height = 86,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6),
            Text = "Select a source marker to inspect the evidence behind the draft.",
        };
        _massingMarkerDetailsTextBox.SetResourceReference(Control.BackgroundProperty, "SurfaceBackgroundBrush");
        _massingMarkerDetailsTextBox.SetResourceReference(Control.ForegroundProperty, "ControlForegroundBrush");
        _massingMarkerDetailsTextBox.SetResourceReference(Control.BorderBrushProperty, "ControlBorderBrush");
        DockPanel.SetDock(_massingMarkerDetailsTextBox, Dock.Top);
        panel.Children.Add(_massingMarkerDetailsTextBox);

        _massingDraftTextBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
        };
        _massingDraftTextBox.SetResourceReference(Control.BackgroundProperty, "SurfaceBackgroundBrush");
        _massingDraftTextBox.SetResourceReference(Control.ForegroundProperty, "ControlForegroundBrush");
        _massingDraftTextBox.SetResourceReference(Control.BorderBrushProperty, "ControlBorderBrush");
        panel.Children.Add(_massingDraftTextBox);

        return panel;
    }

    private static Button MassingViewButton(string text, string tooltip, Action action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 4),
            ToolTip = tooltip,
        };
        button.Click += (_, _) => action();
        return button;
    }
}
