using System;
using System.Windows;
using System.Windows.Controls;

namespace OurPlanCore.Controls;

public partial class ExcelFramingLegendPanel : UserControl
{
    public event EventHandler? CloseRequested;
    public event EventHandler? SaveRequested;

    public ExcelFramingLegendPanel()
    {
        InitializeComponent();
    }

    public string LegendText
    {
        get => LegendTextBox.Text ?? "";
        set => LegendTextBox.Text = value ?? "";
    }

    public void SetStatus(string text) =>
        StatusText.Text = text ?? "";

    private void BtnClose_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void BtnSave_Click(object sender, RoutedEventArgs e) =>
        SaveRequested?.Invoke(this, EventArgs.Empty);

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        LegendTextBox.Clear();
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }
}
