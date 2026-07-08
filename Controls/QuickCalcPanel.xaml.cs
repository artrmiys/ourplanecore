using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace OurPlaneCore.Controls;

/// <summary>
/// Slide-out calculator panel: a plain calculator on top (collapsed by default)
/// and three independent feet-inches-twelfths rows below, each showing the
/// result as decimal feet with two decimals.
/// </summary>
public partial class QuickCalcPanel : UserControl
{
    public event EventHandler? CloseRequested;

    private const int FeetGroupCount = 3;

    // Simple calculator state (immediate execution, like a pocket calculator).
    private double _accumulator;
    private string _pendingOperator = "";
    private bool _startNewEntry = true;

    public QuickCalcPanel()
    {
        InitializeComponent();
        for (int i = 1; i <= FeetGroupCount; i++)
            FeetGroupsHost.Children.Add(BuildFeetGroup(i));
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    // ---- Simple calculator -------------------------------------------------

    private void CalcButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
            return;

        switch (key)
        {
            case "C":
                _accumulator = 0;
                _pendingOperator = "";
                _startNewEntry = true;
                CalcDisplay.Text = "0";
                break;
            case "B":
                if (!_startNewEntry && CalcDisplay.Text.Length > 0)
                {
                    CalcDisplay.Text = CalcDisplay.Text.Length > 1
                        ? CalcDisplay.Text[..^1]
                        : "0";
                }
                break;
            case "N":
                if (double.TryParse(CalcDisplay.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double negated))
                    CalcDisplay.Text = FormatCalcValue(-negated);
                break;
            case ".":
                if (_startNewEntry)
                {
                    CalcDisplay.Text = "0.";
                    _startNewEntry = false;
                }
                else if (!CalcDisplay.Text.Contains('.'))
                {
                    CalcDisplay.Text += ".";
                }
                break;
            case "+" or "-" or "*" or "/":
                ApplyPendingOperator();
                _pendingOperator = key;
                _startNewEntry = true;
                break;
            case "=":
                ApplyPendingOperator();
                _pendingOperator = "";
                _startNewEntry = true;
                break;
            default:
                if (key.Length == 1 && char.IsDigit(key[0]))
                {
                    CalcDisplay.Text = _startNewEntry || CalcDisplay.Text == "0"
                        ? key
                        : CalcDisplay.Text + key;
                    _startNewEntry = false;
                }
                break;
        }
    }

    private void ApplyPendingOperator()
    {
        if (!double.TryParse(CalcDisplay.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double entry))
            entry = 0;

        if (_pendingOperator.Length == 0)
        {
            _accumulator = entry;
            return;
        }

        _accumulator = _pendingOperator switch
        {
            "+" => _accumulator + entry,
            "-" => _accumulator - entry,
            "*" => _accumulator * entry,
            "/" => Math.Abs(entry) < 1e-12 ? double.NaN : _accumulator / entry,
            _ => entry,
        };
        CalcDisplay.Text = FormatCalcValue(_accumulator);
    }

    private static string FormatCalcValue(double value) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? "Error"
            : value.ToString("0.########", CultureInfo.InvariantCulture);

    // ---- Feet + inches groups ----------------------------------------------

    private sealed record FeetOperandBoxes(TextBox Feet, TextBox Inches, TextBox Twelfths);

    private UIElement BuildFeetGroup(int index)
    {
        var result = new TextBlock
        {
            Text = "= 0.00 ft",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(2, 4, 0, 0),
        };
        result.SetResourceReference(TextBlock.ForegroundProperty, "ControlForegroundBrush");

        var opCombo = new ComboBox
        {
            Width = 46,
            FontSize = 12,
            Margin = new Thickness(2, 3, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        foreach (string op in new[] { "+", "−", "×", "÷" })
            opCombo.Items.Add(new ComboBoxItem { Content = op });
        opCombo.SelectedIndex = 0;

        FeetOperandBoxes first = BuildOperandBoxes();
        FeetOperandBoxes second = BuildOperandBoxes();

        void Recalculate(object? _, EventArgs __) =>
            result.Text = FormatGroupResult(first, second, opCombo);

        foreach (TextBox box in new[] { first.Feet, first.Inches, first.Twelfths, second.Feet, second.Inches, second.Twelfths })
            box.TextChanged += (s, e) => Recalculate(s, e);
        opCombo.SelectionChanged += (s, e) => Recalculate(s, e);

        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };
        panel.Children.Add(BuildGroupHeader(index));
        panel.Children.Add(BuildOperandRow(first));
        panel.Children.Add(opCombo);
        panel.Children.Add(BuildOperandRow(second));
        panel.Children.Add(result);

        var frame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 5, 7, 7),
            Margin = new Thickness(0, 0, 0, 6),
            Child = panel,
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        return frame;
    }

    private static UIElement BuildGroupHeader(int index)
    {
        var header = new DockPanel { Margin = new Thickness(2, 0, 0, 2) };
        var number = new TextBlock
        {
            Text = index.ToString(CultureInfo.InvariantCulture),
            FontWeight = FontWeights.SemiBold,
            FontSize = 10.5,
        };
        number.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        var caption = new TextBlock
        {
            Text = "ft · in · /12",
            FontSize = 10.5,
            Margin = new Thickness(8, 0, 0, 0),
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        header.Children.Add(number);
        header.Children.Add(caption);
        return header;
    }

    private static FeetOperandBoxes BuildOperandBoxes() =>
        new(BuildValueBox("ft"), BuildValueBox("in"), BuildValueBox("/12"));

    private static TextBox BuildValueBox(string tip)
    {
        var box = new TextBox
        {
            Width = 52,
            FontSize = 12.5,
            Padding = new Thickness(3, 2, 3, 2),
            Margin = new Thickness(2),
            TextAlignment = TextAlignment.Right,
            ToolTip = tip,
        };
        // Select-all on focus so a value can be retyped in one keystroke.
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        return box;
    }

    private static UIElement BuildOperandRow(FeetOperandBoxes boxes)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(boxes.Feet);
        row.Children.Add(boxes.Inches);
        row.Children.Add(boxes.Twelfths);
        return row;
    }

    private static string FormatGroupResult(FeetOperandBoxes first, FeetOperandBoxes second, ComboBox opCombo)
    {
        double left = OperandFeet(first);
        double right = OperandFeet(second);
        string op = (opCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "+";

        double value = op switch
        {
            "−" => left - right,
            "×" => left * right,
            "÷" => Math.Abs(right) < 1e-12 ? double.NaN : left / right,
            _ => left + right,
        };

        if (double.IsNaN(value) || double.IsInfinity(value))
            return "= —";

        string unit = op switch
        {
            "×" => " sq ft",
            "÷" => "",
            _ => " ft",
        };
        return $"= {value.ToString("0.00", CultureInfo.InvariantCulture)}{unit}";
    }

    private static double OperandFeet(FeetOperandBoxes boxes) =>
        ParseBox(boxes.Feet) + ParseBox(boxes.Inches) / 12.0 + ParseBox(boxes.Twelfths) / 144.0;

    private static double ParseBox(TextBox box)
    {
        string text = (box.Text ?? "").Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : 0;
    }
}
