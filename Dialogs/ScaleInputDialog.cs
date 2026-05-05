using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OurPlaneCore.Controls;

/// <summary>
/// Minimal dialog: shows a prompt and accepts an imperial distance in feet.
/// </summary>
public sealed class ScaleInputDialog : Window
{
    public double Value { get; private set; }

    public ScaleInputDialog(string prompt)
    {
        Title           = "Set Scale";
        Width           = 380;
        SizeToContent   = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode      = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(12) };

        panel.Children.Add(new TextBlock
        {
            Text         = prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 8),
        });

        var entry = new TextBox
        {
            Width = 140,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Feet or feet/inches, e.g. 12, 12'6\", or 12 ft 6 in",
        };
        panel.Children.Add(entry);

        var buttons = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(0, 10, 0, 0),
        };

        var ok     = new Button { Content = "OK",     Width = 70, IsDefault = true,  Margin = new Thickness(0,0,6,0) };
        var cancel = new Button { Content = "Cancel", Width = 70, IsCancel  = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;

        ok.Click += (_, _) =>
        {
            if (TryParseImperialDistanceMeters(entry.Text, out double v) && v > 0)
            {
                Value        = v;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Enter a positive distance in feet, e.g. 12 or 12'6\".", "Invalid input",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        Loaded += (_, _) => entry.Focus();
    }

    private static bool TryParseImperialDistanceMeters(string text, out double meters)
    {
        meters = 0;
        string clean = (text ?? "")
            .Trim()
            .ToLowerInvariant()
            .Replace("feet", "ft")
            .Replace("foot", "ft")
            .Replace("inches", "in")
            .Replace("inch", "in")
            .Replace("\u2019", "'", StringComparison.Ordinal)
            .Replace("\u2018", "'", StringComparison.Ordinal)
            .Replace("\u2032", "'", StringComparison.Ordinal)
            .Replace("\u201d", "\"", StringComparison.Ordinal)
            .Replace("\u201c", "\"", StringComparison.Ordinal)
            .Replace("\u2033", "\"", StringComparison.Ordinal);

        if (string.IsNullOrWhiteSpace(clean))
            return false;

        double feet = 0;
        double inches = 0;

        int footMark = clean.IndexOf('\'');
        if (footMark >= 0)
        {
            if (!TryParseNonNegativeDouble(clean[..footMark], out feet))
                return false;
            clean = clean[(footMark + 1)..].Trim();
        }
        else if (clean.Contains("ft", StringComparison.Ordinal))
        {
            string[] pieces = clean.Split("ft", 2, StringSplitOptions.TrimEntries);
            if (!TryParsePositiveDouble(pieces[0], out feet))
                return false;
            clean = pieces.Length > 1 ? pieces[1].Trim() : "";
        }

        clean = clean
            .Replace("\"", "", StringComparison.Ordinal)
            .Replace("in", "", StringComparison.Ordinal)
            .Trim();

        if (!string.IsNullOrWhiteSpace(clean))
        {
            if (feet > 0 || clean.Contains('/', StringComparison.Ordinal) || clean.Contains('-', StringComparison.Ordinal))
            {
                if (!TryParseInches(clean, out inches))
                    return false;
            }
            else if (!TryParsePositiveDouble(clean, out feet))
            {
                return false;
            }
        }

        double totalFeet = feet + inches / 12.0;
        meters = totalFeet * 0.3048;
        return meters > 0;
    }

    private static bool TryParseInches(string text, out double inches)
    {
        inches = 0;
        string clean = text.Trim();
        if (clean == "0")
            return true;

        if (clean.Contains('-', StringComparison.Ordinal))
        {
            string[] pieces = clean.Split('-', 2, StringSplitOptions.TrimEntries);
            return TryParseNonNegativeDouble(pieces[0], out double whole) &&
                   TryParseFraction(pieces[1], out double fraction) &&
                   (inches = whole + fraction) > 0;
        }

        return clean.Contains('/', StringComparison.Ordinal)
            ? TryParseFraction(clean, out inches)
            : TryParsePositiveDouble(clean, out inches);
    }

    private static bool TryParseFraction(string text, out double value)
    {
        value = 0;
        string[] pieces = text.Split('/', 2, StringSplitOptions.TrimEntries);
        if (pieces.Length != 2 ||
            !double.TryParse(pieces[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double numerator) ||
            !double.TryParse(pieces[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double denominator) ||
            denominator <= 0)
        {
            return false;
        }

        value = numerator / denominator;
        return value > 0;
    }

    private static bool TryParsePositiveDouble(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out value) &&
        value > 0;

    private static bool TryParseNonNegativeDouble(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out value) &&
        value >= 0;
}
