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

    private const int OperandRowsPerGroup = 3;

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

        var operands = new List<FeetOperandBoxes>();
        var operators = new List<ComboBox>();
        for (int row = 0; row < OperandRowsPerGroup; row++)
        {
            operands.Add(BuildOperandBoxes());
            if (row < OperandRowsPerGroup - 1)
                operators.Add(BuildOperatorCombo());
        }

        void Recalculate(object? _, EventArgs __) =>
            result.Text = FormatGroupResult(operands, operators);

        foreach (FeetOperandBoxes boxes in operands)
        {
            foreach (TextBox box in new[] { boxes.Feet, boxes.Inches, boxes.Twelfths })
                box.TextChanged += (s, e) => Recalculate(s, e);
        }
        foreach (ComboBox combo in operators)
            combo.SelectionChanged += (s, e) => Recalculate(s, e);

        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };
        panel.Children.Add(BuildGroupHeader(index));
        for (int row = 0; row < operands.Count; row++)
        {
            panel.Children.Add(BuildOperandRow(operands[row]));
            if (row < operators.Count)
                panel.Children.Add(operators[row]);
        }
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
            Text = "ft · in · frac in",
            FontSize = 10.5,
            Margin = new Thickness(8, 0, 0, 0),
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        header.Children.Add(number);
        header.Children.Add(caption);
        return header;
    }

    private static FeetOperandBoxes BuildOperandBoxes() =>
        new(
            BuildValueBox("feet"),
            BuildValueBox("inches (3, 3.5 or 3 3/8)"),
            BuildValueBox("fractional inches (1/8, 3/8, 3 3/8)"));

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

    private static ComboBox BuildOperatorCombo()
    {
        var combo = new ComboBox
        {
            Width = 46,
            FontSize = 12,
            Margin = new Thickness(2, 3, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        foreach (string op in new[] { "+", "−", "×", "÷" })
            combo.Items.Add(new ComboBoxItem { Content = op });
        combo.SelectedIndex = 0;
        return combo;
    }

    // Folds the used rows left to right: (a op1 b) op2 c. A row whose three
    // boxes are all blank is skipped, so two-value math just leaves row 3 empty.
    private static string FormatGroupResult(IReadOnlyList<FeetOperandBoxes> operands, IReadOnlyList<ComboBox> operators)
    {
        double value = 0;
        bool anyUsed = false;
        bool multiplied = false;
        for (int row = 0; row < operands.Count; row++)
        {
            if (IsOperandEmpty(operands[row]))
                continue;

            double operand = OperandFeet(operands[row]);
            if (!anyUsed)
            {
                value = operand;
                anyUsed = true;
                continue;
            }

            string op = SelectedOperator(operators[row - 1]);
            multiplied |= op is "×" or "÷";
            value = op switch
            {
                "−" => value - operand,
                "×" => value * operand,
                "÷" => Math.Abs(operand) < 1e-12 ? double.NaN : value / operand,
                _ => value + operand,
            };
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
            return "= —";

        string unit = multiplied ? "" : " ft";
        return $"= {value.ToString("0.00", CultureInfo.InvariantCulture)}{unit}";
    }

    private static string SelectedOperator(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "+";

    private static bool IsOperandEmpty(FeetOperandBoxes boxes) =>
        string.IsNullOrWhiteSpace(boxes.Feet.Text) &&
        string.IsNullOrWhiteSpace(boxes.Inches.Text) &&
        string.IsNullOrWhiteSpace(boxes.Twelfths.Text);

    // Inches and fractional inches are both divided by 12; the fraction box
    // takes carpenter-style input like "1/8", "3/8" or "3 3/8".
    private static double OperandFeet(FeetOperandBoxes boxes) =>
        ParseLength(boxes.Feet) + (ParseLength(boxes.Inches) + ParseLength(boxes.Twelfths)) / 12.0;

    private static double ParseLength(TextBox box)
    {
        string text = (box.Text ?? "").Trim().Replace(',', '.');
        if (text.Length == 0)
            return 0;

        double total = 0;
        foreach (string token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            total += ParseLengthToken(token);
        return total;
    }

    private static double ParseLengthToken(string token)
    {
        int slash = token.IndexOf('/');
        if (slash <= 0)
        {
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double plain)
                ? plain
                : 0;
        }

        // Mixed number written with a dash: "3-3/8".
        string whole = "";
        string fraction = token;
        int dash = token.IndexOf('-');
        if (dash > 0 && dash < slash)
        {
            whole = token[..dash];
            fraction = token[(dash + 1)..];
            slash = fraction.IndexOf('/');
        }

        double result = 0;
        if (whole.Length > 0 &&
            double.TryParse(whole, NumberStyles.Float, CultureInfo.InvariantCulture, out double wholeValue))
        {
            result += wholeValue;
        }

        string numeratorText = fraction[..slash];
        string denominatorText = fraction[(slash + 1)..];
        if (double.TryParse(numeratorText, NumberStyles.Float, CultureInfo.InvariantCulture, out double numerator) &&
            double.TryParse(denominatorText, NumberStyles.Float, CultureInfo.InvariantCulture, out double denominator) &&
            Math.Abs(denominator) > 1e-12)
        {
            result += numerator / denominator;
        }

        return result;
    }
}
