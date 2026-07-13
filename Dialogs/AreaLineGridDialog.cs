using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace OurPlanCore.Controls;

public sealed class AreaLineGridDialog : Window
{
    public string TakeoffName { get; private set; } = "Line Grid";
    public bool IncludeHorizontal { get; private set; } = true;
    public bool IncludeVertical { get; private set; } = true;
    public double HorizontalSpacingInches { get; private set; } = 16;
    public double VerticalSpacingInches { get; private set; } = 16;

    public AreaLineGridDialog(
        string defaultTakeoffName,
        double defaultHorizontalSpacingInches,
        double defaultVerticalSpacingInches)
    {
        Title = "Create Line Grid";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(12) };

        panel.Children.Add(new TextBlock { Text = "New Line takeoff:", Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(defaultTakeoffName) ? "Line Grid" : defaultTakeoffName.Trim(),
        };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock { Text = "Grid:", Margin = new Thickness(0, 12, 0, 4) });
        var horizontalBox = new CheckBox
        {
            Content = "Horizontal",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var horizontalSpacingBox = new TextBox
        {
            Text = FormatSpacing(defaultHorizontalSpacingInches),
            Width = 64,
            Margin = new Thickness(0, 0, 4, 0),
        };
        panel.Children.Add(BuildDirectionRow(horizontalBox, horizontalSpacingBox));

        var verticalBox = new CheckBox
        {
            Content = "Vertical",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var verticalSpacingBox = new TextBox
        {
            Text = FormatSpacing(defaultVerticalSpacingInches),
            Width = 64,
            Margin = new Thickness(0, 0, 4, 0),
        };
        panel.Children.Add(BuildDirectionRow(verticalBox, verticalSpacingBox));

        horizontalBox.Checked += (_, _) => horizontalSpacingBox.IsEnabled = true;
        horizontalBox.Unchecked += (_, _) => horizontalSpacingBox.IsEnabled = false;
        verticalBox.Checked += (_, _) => verticalSpacingBox.IsEnabled = true;
        verticalBox.Unchecked += (_, _) => verticalSpacingBox.IsEnabled = false;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "OK", Width = 70, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 70, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;

        ok.Click += (_, _) =>
        {
            bool includeHorizontal = horizontalBox.IsChecked == true;
            bool includeVertical = verticalBox.IsChecked == true;
            if (!includeHorizontal && !includeVertical)
            {
                MessageBox.Show("Select horizontal, vertical, or both directions.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            double horizontalSpacing = defaultHorizontalSpacingInches;
            double verticalSpacing = defaultVerticalSpacingInches;
            if (includeHorizontal &&
                !TryReadSpacing(horizontalSpacingBox, "Horizontal spacing", out horizontalSpacing))
            {
                return;
            }

            if (includeVertical &&
                !TryReadSpacing(verticalSpacingBox, "Vertical spacing", out verticalSpacing))
            {
                return;
            }

            TakeoffName = string.IsNullOrWhiteSpace(nameBox.Text) ? "Line Grid" : nameBox.Text.Trim();
            IncludeHorizontal = includeHorizontal;
            IncludeVertical = includeVertical;
            HorizontalSpacingInches = horizontalSpacing;
            VerticalSpacingInches = verticalSpacing;
            DialogResult = true;
        };

        Loaded += (_, _) =>
        {
            nameBox.Focus();
            nameBox.SelectAll();
        };
    }

    private static FrameworkElement BuildDirectionRow(CheckBox directionBox, TextBox spacingBox)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0), LastChildFill = false };
        directionBox.Width = 110;
        DockPanel.SetDock(directionBox, Dock.Left);
        row.Children.Add(directionBox);

        var spacingLabel = new TextBlock
        {
            Text = "spacing",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        DockPanel.SetDock(spacingLabel, Dock.Left);
        row.Children.Add(spacingLabel);

        DockPanel.SetDock(spacingBox, Dock.Left);
        row.Children.Add(spacingBox);

        row.Children.Add(new TextBlock
        {
            Text = "in O.C.",
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private static bool TryReadSpacing(TextBox textBox, string label, out double spacing)
    {
        string raw = textBox.Text.Trim().Replace(',', '.');
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out spacing) ||
            !double.IsFinite(spacing) ||
            spacing <= 0)
        {
            MessageBox.Show($"{label} must be a positive number of inches.", "Create Line Grid",
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
