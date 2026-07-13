using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    private Border? _threeDRoofPitchPopup;
    private TextBlock? _threeDRoofPitchPopupLabel;
    private TextBox? _threeDRoofPitchPopupBox;

    private void ShowThreeDRoofPitchPopup()
    {
        if (_currentPage == null)
        {
            HideThreeDRoofPitchPopup();
            return;
        }

        IReadOnlyList<ThreeDRoofGuide> guides = SelectedThreeDRoofGuides()
            .Where(guide => IsSamePageFolder(guide.PageFolder, _currentPage.FolderPath))
            .ToList();
        if (guides.Count == 0 || !TryRoofPitchPopupAnchor(guides, out SKPoint anchorPdf))
        {
            HideThreeDRoofPitchPopup();
            return;
        }

        EnsureThreeDRoofPitchPopup();
        if (_threeDRoofPitchPopup == null || _threeDRoofPitchPopupBox == null || _threeDRoofPitchPopupLabel == null)
            return;

        Point anchor = _viewport.PdfToViewPoint(anchorPdf.X, anchorPdf.Y);
        double left = Math.Clamp(anchor.X + 14, 6, Math.Max(6, ViewportSurfaceHost.ActualWidth - 150));
        double top = Math.Clamp(anchor.Y - 18, 6, Math.Max(6, ViewportSurfaceHost.ActualHeight - 42));
        _threeDRoofPitchPopup.Margin = new Thickness(left, top, 0, 0);
        _threeDRoofPitchPopup.Visibility = Visibility.Visible;
        Panel.SetZIndex(_threeDRoofPitchPopup, 20);

        _threeDRoofPitchPopupLabel.Text = guides.Count == 1 ? "Pitch" : $"Pitch x{guides.Count}";
        _threeDRoofPitchPopupBox.Text = PopupPitchText(guides);
        _threeDRoofPitchPopupBox.SelectAll();
        _threeDRoofPitchPopupBox.Focus();
    }

    private void HideThreeDRoofPitchPopup()
    {
        if (_threeDRoofPitchPopup != null)
            _threeDRoofPitchPopup.Visibility = Visibility.Collapsed;
    }

    private void EnsureThreeDRoofPitchPopup()
    {
        if (_threeDRoofPitchPopup != null)
            return;

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0),
        };
        _threeDRoofPitchPopupLabel = new TextBlock
        {
            Text = "Pitch",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        };
        _threeDRoofPitchPopupBox = new TextBox
        {
            Width = 64,
            MinHeight = 22,
            FontSize = 11,
            Padding = new Thickness(3, 1, 3, 1),
            ToolTip = "Enter pitch for selected roof edge(s), for example 6/12, 4, or 0.333",
        };
        _threeDRoofPitchPopupBox.KeyDown += ThreeDRoofPitchPopupBox_KeyDown;
        panel.Children.Add(_threeDRoofPitchPopupLabel);
        panel.Children.Add(_threeDRoofPitchPopupBox);

        _threeDRoofPitchPopup = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(6, 4, 6, 4),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = panel,
        };
        _threeDRoofPitchPopup.SetResourceReference(Border.BackgroundProperty, "ControlBackgroundBrush");
        _threeDRoofPitchPopup.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        ViewportSurfaceHost.Children.Add(_threeDRoofPitchPopup);
    }

    private void ThreeDRoofPitchPopupBox_KeyDown(object sender, KeyEventArgs e)
    {
        Key key = KeyboardShortcutKeys.EffectiveKey(e);
        if (key == Key.Escape)
        {
            HideThreeDRoofPitchPopup();
            e.Handled = true;
            return;
        }

        if (key != Key.Enter)
            return;

        ApplyThreeDRoofPitchFromPopup();
        e.Handled = true;
    }

    private void ApplyThreeDRoofPitchFromPopup()
    {
        IReadOnlyList<ThreeDRoofGuide> guides = SelectedThreeDRoofGuides();
        if (guides.Count == 0 || _threeDRoofPitchPopupBox == null)
        {
            HideThreeDRoofPitchPopup();
            return;
        }

        string text = _threeDRoofPitchPopupBox.Text.Trim();
        if (!RoofPitchText.TryParse(text, out double pitch))
        {
            TxtStatus.Text = $"3D Roof: invalid edge pitch '{text}'. Use 6/12, 4, or 0.333.";
            _threeDRoofPitchPopupBox.SelectAll();
            return;
        }

        ApplyPitchToRoofGuides(guides, pitch);
        _threeDRoofPitchPopupBox.Text = RoofPitchText.Format(pitch);
        _threeDRoofPitchPopupBox.SelectAll();
        TxtStatus.Text = guides.Count == 1
            ? $"3D Roof: saved pitch {PitchLabel(pitch)} on {guides[0].Label}. Generate Roof when ready."
            : $"3D Roof: saved pitch {PitchLabel(pitch)} on {guides.Count} selected edge(s). Generate Roof when ready.";
        LogThreeD($"Roof edge pitch popup saved: {guides.Count} selected edge(s), {PitchLabel(pitch)}.");
    }

    private void ApplyPitchToRoofGuides(IReadOnlyList<ThreeDRoofGuide> guides, double pitch)
    {
        foreach (ThreeDRoofGuide guide in guides)
        {
            guide.Kind = ThreeDRoofGuideKinds.Eave;
            guide.Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Eave);
            guide.DefinesSlope = true;
            guide.PitchRisePerFoot = pitch;
            guide.Label = RelabelRoofGuide(guide);
        }

        InvalidateGeneratedThreeDRoofAfterEdgeEdit();
        UpdateThreeDEditor();
    }

    private static string PopupPitchText(IReadOnlyList<ThreeDRoofGuide> guides)
    {
        if (guides.Count == 0)
            return "";

        double first = guides[0].PitchRisePerFoot > 0
            ? guides[0].PitchRisePerFoot
            : ThreeDRoofPreviewBuilder.DefaultPitchRisePerFoot;
        bool same = guides.All(guide =>
        {
            double pitch = guide.PitchRisePerFoot > 0
                ? guide.PitchRisePerFoot
                : ThreeDRoofPreviewBuilder.DefaultPitchRisePerFoot;
            return Math.Abs(pitch - first) < 0.0001;
        });
        return same ? RoofPitchText.Format(first) : "";
    }

    private static bool TryRoofPitchPopupAnchor(IReadOnlyList<ThreeDRoofGuide> guides, out SKPoint anchor)
    {
        anchor = default;
        List<SKPoint> points = guides
            .Where(guide => guide.Points.Count >= 2)
            .Select(guide =>
            {
                ThreeDRoofGuidePoint a = guide.Points[0];
                ThreeDRoofGuidePoint b = guide.Points[^1];
                return new SKPoint((float)((a.PdfX + b.PdfX) / 2.0), (float)((a.PdfY + b.PdfY) / 2.0));
            })
            .ToList();
        if (points.Count == 0)
            return false;

        anchor = new SKPoint(
            points.Average(point => point.X),
            points.Average(point => point.Y));
        return true;
    }
}
