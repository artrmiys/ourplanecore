using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OurPlanCore.Controls;

public partial class LiveInputRibbonSettings : UserControl
{
    public LiveInputRibbonSettings()
    {
        InitializeComponent();
    }

    public event RoutedPropertyChangedEventHandler<double>? SizeValueChanged;

    public event RoutedPropertyChangedEventHandler<double>? OpacityValueChanged;

    public event RoutedEventHandler? SliderCommit;

    public event KeyEventHandler? SizeTextKeyDown;

    public event RoutedEventHandler? SizeTextLostFocus;

    public event KeyEventHandler? OpacityTextKeyDown;

    public event RoutedEventHandler? OpacityTextLostFocus;

    public double SizeValue
    {
        get => SizeSlider.Value;
        set => SizeSlider.Value = value;
    }

    public double OpacityValue
    {
        get => OpacitySlider.Value;
        set => OpacitySlider.Value = value;
    }

    public string SizeText
    {
        get => SizeTextBox.Text;
        set => SizeTextBox.Text = value;
    }

    public string OpacityText
    {
        get => OpacityTextBox.Text;
        set => OpacityTextBox.Text = value;
    }

    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        SizeValueChanged?.Invoke(sender, e);

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        OpacityValueChanged?.Invoke(sender, e);

    private void Slider_Commit(object sender, RoutedEventArgs e) =>
        SliderCommit?.Invoke(sender, e);

    private void SizeTextBox_KeyDown(object sender, KeyEventArgs e) =>
        SizeTextKeyDown?.Invoke(sender, e);

    private void SizeTextBox_LostFocus(object sender, RoutedEventArgs e) =>
        SizeTextLostFocus?.Invoke(sender, e);

    private void OpacityTextBox_KeyDown(object sender, KeyEventArgs e) =>
        OpacityTextKeyDown?.Invoke(sender, e);

    private void OpacityTextBox_LostFocus(object sender, RoutedEventArgs e) =>
        OpacityTextLostFocus?.Invoke(sender, e);
}
