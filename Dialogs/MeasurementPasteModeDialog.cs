using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlanCore;

/// <summary>The two existing paste destinations, with explicit action labels.</summary>
internal sealed class MeasurementPasteModeDialog : Window
{
    public bool CreateNewTakeoffs { get; private set; }

    public MeasurementPasteModeDialog(int count, string sheetName)
    {
        Title = "Paste Measurements";
        Width = 550;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = TryFindResource("WindowBackgroundBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(35, 38, 43));
        Foreground = TryFindResource("PrimaryForegroundBrush") as Brush ?? Brushes.White;
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = $"Paste {count} measurement(s) to {sheetName}", FontSize = 17,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(new TextBlock
        {
            Text = "Same takeoffs reuses the source items, recreating any missing items.\nNew takeoffs creates separate items with the same names and properties.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 18), FontSize = 13,
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button same = ActionButton("Same takeoffs", "PasteSameTakeoffs", () => Complete(false), primary: true);
        same.IsDefault = true;
        actions.Children.Add(same);
        actions.Children.Add(ActionButton("New takeoffs", "PasteNewTakeoffs", () => Complete(true)));
        var cancel = ActionButton("Cancel", "PasteCancel", () => DialogResult = false);
        cancel.IsCancel = true;
        actions.Children.Add(cancel);
        root.Children.Add(actions);
        Content = root;
        Loaded += (_, _) => { same.Focus(); Keyboard.Focus(same); };
    }

    private Button ActionButton(string title, string id, Action action, bool primary = false)
    {
        var button = new Button
        {
            Content = title, MinWidth = 105, Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(6, 0, 0, 0),
            Style = TryFindResource(primary ? "ManagerPrimaryButton" : "ManagerButton") as Style,
        };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => action();
        return button;
    }

    private void Complete(bool createNew)
    {
        CreateNewTakeoffs = createNew;
        DialogResult = true;
    }
}
