using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlanCore;

public sealed class OverlayColorPaletteDialog : Window
{
    private readonly Slider _red;
    private readonly Slider _green;
    private readonly Slider _blue;
    private readonly TextBox _hex;
    private readonly Border _preview;
    private bool _updating;

    public string SelectedColor { get; private set; }

    public OverlayColorPaletteDialog(string initialColor)
    {
        Title = "Overlay Color";
        Width = 350;
        Height = 360;
        MinWidth = 320;
        MinHeight = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        Color initial = ParseColor(initialColor);
        SelectedColor = ToHex(initial);
        _red = ChannelSlider(initial.R);
        _green = ChannelSlider(initial.G);
        _blue = ChannelSlider(initial.B);
        _hex = new TextBox
        {
            Text = SelectedColor,
            Width = 92,
            Padding = new Thickness(5, 3, 5, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
        };
        _preview = new Border
        {
            Width = 52,
            Height = 30,
            CornerRadius = new CornerRadius(3),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(initial),
        };

        Content = BuildContent();
        _red.ValueChanged += Channel_ValueChanged;
        _green.ValueChanged += Channel_ValueChanged;
        _blue.ValueChanged += Channel_ValueChanged;
        _hex.LostKeyboardFocus += (_, _) => ApplyHex();
        _hex.PreviewKeyDown += Hex_PreviewKeyDown;
    }

    private UIElement BuildContent()
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Choose overlay line color",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 9),
        };
        root.Children.Add(title);

        AddChannelRow(root, 1, "R", _red);
        AddChannelRow(root, 2, "G", _green);
        AddChannelRow(root, 3, "B", _blue);

        var hexRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 8),
        };
        hexRow.Children.Add(new TextBlock
        {
            Text = "HEX",
            Width = 28,
            VerticalAlignment = VerticalAlignment.Center,
        });
        hexRow.Children.Add(_hex);
        hexRow.Children.Add(_preview);
        _preview.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetRow(hexRow, 4);
        root.Children.Add(hexRow);

        var palette = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
        foreach (string color in PaletteColors())
        {
            var button = new Button
            {
                Tag = color,
                Width = 27,
                Height = 24,
                Margin = new Thickness(0, 0, 5, 5),
                Background = new SolidColorBrush(ParseColor(color)),
                BorderThickness = new Thickness(1),
                ToolTip = color,
            };
            button.Click += Palette_Click;
            palette.Children.Add(button);
        }
        Grid.SetRow(palette, 5);
        root.Children.Add(palette);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 76,
            Padding = new Thickness(9, 4, 9, 4),
            Margin = new Thickness(0, 0, 6, 0),
            IsCancel = true,
        };
        var ok = new Button
        {
            Content = "Use Color",
            MinWidth = 88,
            Padding = new Thickness(9, 4, 9, 4),
            IsDefault = true,
        };
        ok.Click += (_, _) =>
        {
            ApplyHex();
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 6);
        root.Children.Add(buttons);
        return root;
    }

    private static void AddChannelRow(Grid root, int row, string label, Slider slider)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        var name = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var value = new TextBlock
        {
            Text = slider.Value.ToString("0", CultureInfo.InvariantCulture),
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
        };
        slider.Tag = value;
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(value, 2);
        grid.Children.Add(name);
        grid.Children.Add(slider);
        grid.Children.Add(value);
        Grid.SetRow(grid, row);
        root.Children.Add(grid);
    }

    private static Slider ChannelSlider(byte value) =>
        new()
        {
            Minimum = 0,
            Maximum = 255,
            Value = value,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
        };

    private void Channel_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider { Tag: TextBlock value })
            value.Text = e.NewValue.ToString("0", CultureInfo.InvariantCulture);
        if (_updating)
            return;
        ApplyColor(Color.FromRgb(
            (byte)Math.Round(_red.Value),
            (byte)Math.Round(_green.Value),
            (byte)Math.Round(_blue.Value)));
    }

    private void Palette_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color })
            SetControls(ParseColor(color));
    }

    private void Hex_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        ApplyHex();
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void ApplyHex()
    {
        Color color = ParseColor(_hex.Text);
        SetControls(color);
    }

    private void SetControls(Color color)
    {
        _updating = true;
        try
        {
            _red.Value = color.R;
            _green.Value = color.G;
            _blue.Value = color.B;
            if (_red.Tag is TextBlock red)
                red.Text = color.R.ToString(CultureInfo.InvariantCulture);
            if (_green.Tag is TextBlock green)
                green.Text = color.G.ToString(CultureInfo.InvariantCulture);
            if (_blue.Tag is TextBlock blue)
                blue.Text = color.B.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _updating = false;
        }
        ApplyColor(color);
    }

    private void ApplyColor(Color color)
    {
        SelectedColor = ToHex(color);
        _hex.Text = SelectedColor;
        _preview.Background = new SolidColorBrush(color);
    }

    private static Color ParseColor(string value)
    {
        try
        {
            object? parsed = ColorConverter.ConvertFromString(
                string.IsNullOrWhiteSpace(value) ? "#E53935" : value.Trim());
            return parsed is Color color ? color : Color.FromRgb(0xE5, 0x39, 0x35);
        }
        catch
        {
            return Color.FromRgb(0xE5, 0x39, 0x35);
        }
    }

    private static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static IReadOnlyList<string> PaletteColors() =>
    [
        "#E53935", "#FB8C00", "#FDD835", "#43A047",
        "#00ACC1", "#1E88E5", "#5E35B1", "#D81B60",
        "#6D4C41", "#546E7A", "#212121", "#9E9E9E",
    ];
}
