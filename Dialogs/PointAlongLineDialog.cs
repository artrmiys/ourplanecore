using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace OurPlanCore.Dialogs;

public sealed class PointAlongLineDialog : Window
{
    public string TakeoffName { get; private set; } = "Line Count Points";
    public double SpacingInches { get; private set; } = 16;
    public bool IncludeEndPoint { get; private set; } = true;

    public PointAlongLineDialog(string defaultTakeoffName, double defaultSpacingInches)
    {
        Title = "Create Count Points Along Line(s)";
        Width = 340;
        Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(14) };
        Content = panel;

        panel.Children.Add(new TextBlock { Text = "Count takeoff name" });
        var nameBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(defaultTakeoffName) ? "Line Count Points" : defaultTakeoffName.Trim(),
            Margin = new Thickness(0, 4, 0, 10),
        };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock { Text = "Spacing, inches" });
        var spacingBox = new TextBox
        {
            Text = FormatSpacing(defaultSpacingInches),
            Margin = new Thickness(0, 4, 0, 10),
        };
        panel.Children.Add(spacingBox);

        var includeEndBox = new CheckBox
        {
            Content = "Include line end point",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 14),
        };
        panel.Children.Add(includeEndBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        panel.Children.Add(buttons);

        var ok = new Button { Content = "Create", IsDefault = true, MinWidth = 78, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 78 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        ok.Click += (_, _) =>
        {
            if (!TryReadSpacing(spacingBox, out double spacing))
                return;

            TakeoffName = string.IsNullOrWhiteSpace(nameBox.Text) ? "Line Count Points" : nameBox.Text.Trim();
            SpacingInches = spacing;
            IncludeEndPoint = includeEndBox.IsChecked == true;
            DialogResult = true;
        };
    }

    private static bool TryReadSpacing(TextBox textBox, out double spacing)
    {
        string raw = (textBox.Text ?? "").Trim();
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out spacing) ||
            !double.IsFinite(spacing) ||
            spacing <= 0)
        {
            MessageBox.Show("Spacing must be a positive number of inches.", "Create Count Points",
                MessageBoxButton.OK, MessageBoxImage.Information);
            textBox.Focus();
            textBox.SelectAll();
            return false;
        }

        return true;
    }

    private static string FormatSpacing(double value)
    {
        double spacing = double.IsFinite(value) && value > 0 ? value : 16;
        return spacing.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
