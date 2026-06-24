using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private bool ShowSheetOverlayTransformDialog(
        PageInfo page,
        out double offsetXPt,
        out double offsetYPt,
        out double overlayScale,
        out double overlayRotationDegrees)
    {
        offsetXPt = page.OverlayOffsetXPt;
        offsetYPt = page.OverlayOffsetYPt;
        overlayScale = page.OverlayScale;
        overlayRotationDegrees = page.OverlayRotationDegrees;

        var dialog = new Window
        {
            Title = "Sheet Overlay Transform",
            Owner = this,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Overlay: {OverlayPageName(page)}",
            Margin = new Thickness(0, 0, 0, 10),
        });

        AddLabeledTextBox(panel, "X offset (pt):", FormatOverlayNumber(page.OverlayOffsetXPt), out TextBox xBox);
        AddLabeledTextBox(panel, "Y offset (pt):", FormatOverlayNumber(page.OverlayOffsetYPt), out TextBox yBox);
        AddLabeledTextBox(panel, "Scale:", FormatOverlayNumber(page.OverlayScale), out TextBox scaleBox);
        AddLabeledTextBox(panel, "Rotation (deg):", FormatOverlayNumber(page.OverlayRotationDegrees), out TextBox rotationBox);
        AddTransformAdjustmentButtons(panel, xBox, yBox, scaleBox, rotationBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "OK", Width = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 78, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        double resultX = offsetXPt;
        double resultY = offsetYPt;
        double resultScale = overlayScale;
        double resultRotation = overlayRotationDegrees;
        ok.Click += (_, _) =>
        {
            if (!TryParseOverlayNumber(xBox.Text, out resultX) ||
                !TryParseOverlayNumber(yBox.Text, out resultY) ||
                !TryParseOverlayNumber(scaleBox.Text, out resultScale) ||
                !TryParseOverlayNumber(rotationBox.Text, out resultRotation) ||
                resultScale <= 0)
            {
                MessageBox.Show(
                    "Enter numeric X, Y, Rotation, and positive Scale values.",
                    "Sheet Overlay Transform",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) => { xBox.Focus(); xBox.SelectAll(); };

        if (dialog.ShowDialog() != true)
            return false;

        offsetXPt = resultX;
        offsetYPt = resultY;
        overlayScale = NormalizeSheetOverlayTransformScale(resultScale);
        overlayRotationDegrees = NormalizeOverlayRotationDegrees(resultRotation);
        return true;
    }

    private static void AddLabeledTextBox(Panel panel, string label, string value, out TextBox box)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        box = new TextBox
        {
            Text = value,
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(box);
    }

    private static void AddTransformAdjustmentButtons(
        Panel panel,
        TextBox xBox,
        TextBox yBox,
        TextBox scaleBox,
        TextBox rotationBox)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        AddTransformAdjustmentButton(row, "X -1", () => AdjustOverlayNumber(xBox, value => value - 1));
        AddTransformAdjustmentButton(row, "X +1", () => AdjustOverlayNumber(xBox, value => value + 1));
        AddTransformAdjustmentButton(row, "Y -1", () => AdjustOverlayNumber(yBox, value => value - 1));
        AddTransformAdjustmentButton(row, "Y +1", () => AdjustOverlayNumber(yBox, value => value + 1));
        AddTransformAdjustmentButton(row, "Scale -1%", () => AdjustOverlayNumber(
            scaleBox,
            value => NormalizeSheetOverlayTransformScale(value / 1.01)));
        AddTransformAdjustmentButton(row, "Scale +1%", () => AdjustOverlayNumber(
            scaleBox,
            value => NormalizeSheetOverlayTransformScale(value * 1.01)));
        AddTransformAdjustmentButton(row, "Rot -0.25", () => AdjustOverlayNumber(
            rotationBox,
            value => NormalizeOverlayRotationDegrees(value - 0.25)));
        AddTransformAdjustmentButton(row, "Rot +0.25", () => AdjustOverlayNumber(
            rotationBox,
            value => NormalizeOverlayRotationDegrees(value + 0.25)));
        AddTransformAdjustmentButton(row, "Reset", () =>
        {
            xBox.Text = "0";
            yBox.Text = "0";
            scaleBox.Text = "1";
            rotationBox.Text = "0";
            xBox.Focus();
            xBox.SelectAll();
        });
        panel.Children.Add(row);
    }

    private static void AddTransformAdjustmentButton(Panel panel, string label, Action apply)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 68,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(6, 2, 6, 2),
        };
        button.Click += (_, _) => apply();
        panel.Children.Add(button);
    }

    private static void AdjustOverlayNumber(TextBox box, Func<double, double> adjust)
    {
        if (!TryParseOverlayNumber(box.Text, out double value))
            return;

        box.Text = FormatOverlayNumber(adjust(value));
        box.Focus();
        box.SelectAll();
    }

    private static bool TryParseOverlayNumber(string value, out double result) =>
        double.TryParse(
            (value ?? "").Replace(",", ".", StringComparison.Ordinal),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);

    private static string FormatOverlayNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static double NormalizeSheetOverlayTransformScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return 1.0;

        return Math.Clamp(scale, 0.05, 20.0);
    }

    private static double NormalizeOverlayRotationDegrees(double degrees)
    {
        if (double.IsNaN(degrees) || double.IsInfinity(degrees))
            return 0;

        double normalized = degrees % 360.0;
        if (normalized > 180.0)
            normalized -= 360.0;
        if (normalized <= -180.0)
            normalized += 360.0;
        return normalized;
    }

    private static string OverlayPageName(PageInfo page)
    {
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder))
            return "none";

        return OurPlaneCoreJobStore.TryReadPage(page.OverlayPageFolder)?.Name
            ?? OurPlaneCoreJobStore.DisplayName(page.OverlayPageFolder);
    }
}
