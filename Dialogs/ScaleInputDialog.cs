using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartTakeoffs.Controls;

/// <summary>
/// Minimal dialog: shows a prompt and accepts a decimal number.
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

        var entry = new TextBox { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
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
            if (double.TryParse(entry.Text.Replace(',', '.'), NumberStyles.Any,
                                CultureInfo.InvariantCulture, out double v) && v > 0)
            {
                Value        = v;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Enter a positive number (metres).", "Invalid input",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        Loaded += (_, _) => entry.Focus();
    }
}
