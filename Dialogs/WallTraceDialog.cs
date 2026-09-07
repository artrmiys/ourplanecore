using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace OurPlanCore.Controls;

public sealed class WallTraceDialog : Window
{
    public string TakeoffName { get; private set; } = "Walls";
    public double MinThicknessInches { get; private set; } = 3;
    public double MaxThicknessInches { get; private set; } = 13;
    public double MinWallLengthFeet { get; private set; } = 1;
    public bool IncludePerimeterWalls { get; private set; }
    public double PerimeterOffsetFeet { get; private set; } = 1;
    public bool DarkFillOnly { get; private set; }
    public bool AllowRoughImageOnlyTrace { get; private set; }

    /// <summary>Manual dark/light fill luminance cutoff; null lets the tracer pick it per sheet.</summary>
    public double? DarkFillCutoff { get; private set; }

    public WallTraceDialog(string defaultTakeoffName)
    {
        Title = "Trace Walls Inside Area";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(12) };

        panel.Children.Add(new TextBlock { Text = "New Line takeoff:", Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(defaultTakeoffName) ? "Walls" : defaultTakeoffName.Trim(),
        };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock
        {
            Text = "Wall thickness (distance between the two face lines):",
            Margin = new Thickness(0, 12, 0, 4),
            TextWrapping = TextWrapping.Wrap,
        });
        var minBox = new TextBox { Text = "3", Width = 64, Margin = new Thickness(0, 0, 4, 0) };
        var maxBox = new TextBox { Text = "13", Width = 64, Margin = new Thickness(0, 0, 4, 0) };
        panel.Children.Add(BuildValueRow("Min", minBox, "in"));
        panel.Children.Add(BuildValueRow("Max", maxBox, "in"));

        panel.Children.Add(new TextBlock
        {
            Text = "Ignore walls shorter than:",
            Margin = new Thickness(0, 12, 0, 4),
        });
        var minLenBox = new TextBox { Text = "1", Width = 64, Margin = new Thickness(0, 0, 4, 0) };
        panel.Children.Add(BuildValueRow("Length", minLenBox, "ft"));

        var darkFillBox = new CheckBox
        {
            Content = "Only solid (dark) filled walls — demising / corridor / rated",
            IsChecked = false,
            Margin = new Thickness(0, 12, 0, 0),
        };
        panel.Children.Add(darkFillBox);

        // "auto" = the tracer clusters the sheet's fill luminances itself;
        // a number 0..1 pins the dark/light cutoff for unusual plans.
        var darkCutoffBox = new TextBox { Text = "auto", Width = 64, Margin = new Thickness(0, 0, 4, 0) };
        FrameworkElement darkCutoffRow = BuildValueRow("Dark cutoff", darkCutoffBox, "0-1 or auto");
        darkCutoffRow.Margin = new Thickness(18, 4, 0, 0);
        darkCutoffRow.IsEnabled = false;
        panel.Children.Add(darkCutoffRow);
        darkFillBox.Checked += (_, _) => darkCutoffRow.IsEnabled = true;
        darkFillBox.Unchecked += (_, _) => darkCutoffRow.IsEnabled = false;

        var perimeterBox = new CheckBox
        {
            Content = "Include perimeter walls on the area edge",
            IsChecked = false,
            Margin = new Thickness(0, 8, 0, 0),
        };
        panel.Children.Add(perimeterBox);

        var perimeterOffsetBox = new TextBox { Text = "1", Width = 64, Margin = new Thickness(0, 0, 4, 0) };
        FrameworkElement perimeterOffsetRow = BuildValueRow("Edge offset", perimeterOffsetBox, "ft");
        perimeterOffsetRow.Margin = new Thickness(18, 4, 0, 0);
        panel.Children.Add(perimeterOffsetRow);
        perimeterOffsetRow.IsEnabled = true;
        perimeterBox.Checked += (_, _) => perimeterOffsetRow.IsEnabled = false;
        perimeterBox.Unchecked += (_, _) => perimeterOffsetRow.IsEnabled = true;

        var roughImageBox = new CheckBox
        {
            Content = "Allow rough image-only trace (may catch text)",
            IsChecked = false,
            Margin = new Thickness(0, 10, 0, 0),
        };
        panel.Children.Add(roughImageBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "Trace", Width = 70, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 70, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;

        ok.Click += (_, _) =>
        {
            if (!TryReadValue(minBox, "Min thickness", out double min) ||
                !TryReadValue(maxBox, "Max thickness", out double max) ||
                !TryReadValue(minLenBox, "Minimum wall length", out double minLen) ||
                !TryReadValue(perimeterOffsetBox, "Edge offset", out double perimeterOffset))
            {
                return;
            }

            double? darkCutoff = null;
            string cutoffRaw = darkCutoffBox.Text.Trim().Replace(',', '.');
            if (darkFillBox.IsChecked == true &&
                cutoffRaw.Length > 0 &&
                !string.Equals(cutoffRaw, "auto", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(cutoffRaw, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double cutoffValue) ||
                    cutoffValue <= 0 || cutoffValue >= 1)
                {
                    MessageBox.Show("Dark cutoff must be a number between 0 and 1, or \"auto\".", Title,
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    darkCutoffBox.Focus();
                    darkCutoffBox.SelectAll();
                    return;
                }

                darkCutoff = cutoffValue;
            }

            if (max <= min)
            {
                MessageBox.Show("Max thickness must be larger than min thickness.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                maxBox.Focus();
                maxBox.SelectAll();
                return;
            }

            TakeoffName = string.IsNullOrWhiteSpace(nameBox.Text) ? "Walls" : nameBox.Text.Trim();
            MinThicknessInches = min;
            MaxThicknessInches = max;
            MinWallLengthFeet = minLen;
            IncludePerimeterWalls = perimeterBox.IsChecked == true;
            PerimeterOffsetFeet = perimeterOffset;
            DarkFillOnly = darkFillBox.IsChecked == true;
            DarkFillCutoff = darkCutoff;
            AllowRoughImageOnlyTrace = roughImageBox.IsChecked == true;
            DialogResult = true;
        };

        Loaded += (_, _) =>
        {
            nameBox.Focus();
            nameBox.SelectAll();
        };
    }

    private static FrameworkElement BuildValueRow(string label, TextBox valueBox, string unit)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0), LastChildFill = false };
        var labelBlock = new TextBlock
        {
            Text = label,
            Width = 64,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(labelBlock, Dock.Left);
        row.Children.Add(labelBlock);

        DockPanel.SetDock(valueBox, Dock.Left);
        row.Children.Add(valueBox);

        row.Children.Add(new TextBlock
        {
            Text = unit,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
        });
        return row;
    }

    private static bool TryReadValue(TextBox textBox, string label, out double value)
    {
        string raw = textBox.Text.Trim().Replace(',', '.');
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            !double.IsFinite(value) ||
            value <= 0)
        {
            MessageBox.Show($"{label} must be a positive number.", "Trace Walls Inside Area",
                MessageBoxButton.OK, MessageBoxImage.Information);
            textBox.Focus();
            textBox.SelectAll();
            return false;
        }

        return true;
    }
}
