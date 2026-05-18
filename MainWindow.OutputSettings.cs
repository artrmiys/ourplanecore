using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlaneCore;

public partial class MainWindow
{
    private CheckBox? _chkOutputPdfMeasurements;
    private CheckBox? _chkOutputPdfMarkups;
    private CheckBox? _chkOutputPdfLegend;
    private CheckBox? _chkOutputPdfLabels;
    private CheckBox? _chkOutputPdfLineLabels;
    private CheckBox? _chkOutputPdfAreaLabels;
    private CheckBox? _chkOutputPdfCountLabels;
    private TextBox? _txtOutputPdfStroke;
    private TextBox? _txtOutputPdfPoint;
    private TextBox? _txtOutputPdfLabel;
    private TextBox? _txtOutputPdfLegend;
    private TextBox? _txtOutputPdfHeader;
    private Slider? _sldOutputPdfStroke;
    private Slider? _sldOutputPdfPoint;
    private Slider? _sldOutputPdfLabel;
    private Slider? _sldOutputPdfLegend;
    private Slider? _sldOutputPdfHeader;
    private bool _outputUiReady;
    private bool _outputScaleDirty;

    private void InstallOutputSettingsTab()
    {
        var tab = new TabItem { Header = "PDF Output" };
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 3, 6, 3),
        };

        panel.Children.Add(BuildPdfOutputGroup());

        scroll.Content = panel;
        tab.Content = scroll;
        TopMainTabs.Items.Insert(Math.Min(TopMainTabs.Items.Count, 2), tab);
        SyncOutputSettingsControls();
        _outputUiReady = true;
    }

    private FrameworkElement BuildPdfOutputGroup()
    {
        double max = AppSettingsStore.PdfExportScaleMax;
        var root = new StackPanel { Orientation = Orientation.Horizontal };

        // ── INCLUDE group (2 rows of checkboxes) ──
        _chkOutputPdfMeasurements = OutputCheckBox("Meas", "Default: include measurements in PDF export.");
        _chkOutputPdfMarkups = OutputCheckBox("Markups", "Default: include markups in PDF export.");
        _chkOutputPdfLegend = OutputCheckBox("Legend", "Default: include legend in PDF export.");
        _chkOutputPdfLabels = OutputCheckBox("Labels", "Show exported measurement value labels.");
        _chkOutputPdfLineLabels = OutputCheckBox("Line", "Show exported line labels.");
        _chkOutputPdfAreaLabels = OutputCheckBox("Area", "Show exported area labels.");
        _chkOutputPdfCountLabels = OutputCheckBox("Count", "Show exported count labels.");

        var incRow1 = OutputHRow();
        incRow1.Children.Add(_chkOutputPdfMeasurements);
        incRow1.Children.Add(_chkOutputPdfMarkups);
        incRow1.Children.Add(_chkOutputPdfLegend);
        var incRow2 = OutputHRow();
        incRow2.Children.Add(_chkOutputPdfLabels);
        incRow2.Children.Add(_chkOutputPdfLineLabels);
        incRow2.Children.Add(_chkOutputPdfAreaLabels);
        incRow2.Children.Add(_chkOutputPdfCountLabels);
        var incStack = new StackPanel { Orientation = Orientation.Vertical };
        incStack.Children.Add(incRow1);
        incStack.Children.Add(incRow2);
        root.Children.Add(RibbonGroupContainer("PDF INCLUDE", incStack));

        root.Children.Add(RibbonSep());

        // ── EXPORT SIZE group (3 columns x up to 2 rows) ──
        _txtOutputPdfStroke = OutputScaleBox("pdfStroke", "PDF export measurement line thickness multiplier.");
        _txtOutputPdfPoint = OutputScaleBox("pdfPoint", "PDF export point marker size multiplier.");
        _txtOutputPdfLabel = OutputScaleBox("pdfLabel", "PDF export measurement label size multiplier.");
        _txtOutputPdfLegend = OutputScaleBox("pdfLegend", "PDF export legend size multiplier.");
        _txtOutputPdfHeader = OutputScaleBox("pdfHeader", "PDF export scale / sheet-size header multiplier.");
        _sldOutputPdfStroke = OutputSlider("pdfStroke", 0.25, max, "PDF export line thickness 0.25 - 10");
        _sldOutputPdfPoint = OutputSlider("pdfPoint", 0.25, max, "PDF export point size 0.25 - 10");
        _sldOutputPdfLabel = OutputSlider("pdfLabel", 0.50, max, "PDF export label size 0.5 - 10");
        _sldOutputPdfLegend = OutputSlider("pdfLegend", 0.25, max, "PDF export legend size 0.25 - 10");
        _sldOutputPdfHeader = OutputSlider("pdfHeader", 0.25, max, "PDF export header size 0.25 - 10");

        var col1 = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 10, 0) };
        col1.Children.Add(OutputScaleRow("Stroke", _sldOutputPdfStroke, _txtOutputPdfStroke));
        col1.Children.Add(OutputScaleRow("Point", _sldOutputPdfPoint, _txtOutputPdfPoint));
        var col2 = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 10, 0) };
        col2.Children.Add(OutputScaleRow("Label", _sldOutputPdfLabel, _txtOutputPdfLabel));
        col2.Children.Add(OutputScaleRow("Legend", _sldOutputPdfLegend, _txtOutputPdfLegend));
        var col3 = new StackPanel { Orientation = Orientation.Vertical };
        col3.Children.Add(OutputScaleRow("Header", _sldOutputPdfHeader, _txtOutputPdfHeader));
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
        sizeRow.Children.Add(col1);
        sizeRow.Children.Add(col2);
        sizeRow.Children.Add(col3);
        root.Children.Add(RibbonGroupContainer("PDF EXPORT SIZE", sizeRow));

        return root;
    }

    private static StackPanel OutputHRow() =>
        new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };

    private FrameworkElement RibbonGroupContainer(string label, UIElement content)
    {
        var sp = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 4, 0) };
        sp.Children.Add(content);
        var border = new Border
        {
            BorderBrush = TryFindResource("ControlBorderBrush") as Brush,
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        border.Child = new TextBlock
        {
            Text = label,
            Style = TryFindResource("RibbonGroupLabel") as Style,
        };
        sp.Children.Add(border);
        return sp;
    }

    private Border RibbonSep() => new()
    {
        Width = 1,
        Background = TryFindResource("ControlBorderBrush") as Brush,
        Margin = new Thickness(6, 3, 6, 3),
    };

    private FrameworkElement OutputScaleRow(string text, Slider slider, TextBox box)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lbl = new TextBlock { Text = text, Style = TryFindResource("RibbonRowLabel") as Style };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(box, 2);
        g.Children.Add(lbl);
        g.Children.Add(slider);
        g.Children.Add(box);
        return g;
    }

    private Slider OutputSlider(string key, double min, double max, string tooltip)
    {
        var s = new Slider
        {
            Minimum = min,
            Maximum = max,
            SmallChange = 0.25,
            LargeChange = 0.5,
            Tag = key,
            ToolTip = tooltip,
            Style = TryFindResource("RibbonSlider") as Style,
        };
        s.ValueChanged += OutputSlider_ValueChanged;
        s.PreviewMouseUp += OutputSlider_Commit;
        s.KeyUp += OutputSlider_Commit;
        return s;
    }

    private void OutputSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_outputUiReady || _isApplyingSettings || sender is not Slider s)
            return;

        string key = s.Tag as string ?? "";
        double v = Math.Round(e.NewValue, 2);
        switch (key)
        {
            case "pdfStroke":
                _settings.PdfExportMeasurementStrokeScale = v;
                SetScaleText(_txtOutputPdfStroke, v);
                break;
            case "pdfPoint":
                _settings.PdfExportPointSizeScale = v;
                SetScaleText(_txtOutputPdfPoint, v);
                break;
            case "pdfLabel":
                _settings.PdfExportMeasurementLabelScale = v;
                SetScaleText(_txtOutputPdfLabel, v);
                break;
            case "pdfLegend":
                _settings.PdfExportSheetLegendScale = v;
                SetScaleText(_txtOutputPdfLegend, v);
                break;
            case "pdfHeader":
                _settings.PdfExportSheetHeaderScale = v;
                SetScaleText(_txtOutputPdfHeader, v);
                break;
            default:
                return;
        }

        _outputScaleDirty = true;
        TxtStatus.Text = $"Output {OutputScaleLabel(key)}: {v:0.##}x.";
    }

    private void OutputSlider_Commit(object sender, RoutedEventArgs e)
    {
        if (!_outputUiReady || !_outputScaleDirty)
            return;

        _outputScaleDirty = false;
        ApplyOutputSettings();
    }

    private CheckBox OutputCheckBox(string content, string tooltip)
    {
        var box = new CheckBox
        {
            Content = content,
            ToolTip = tooltip,
            Style = TryFindResource("TopCommandCheckBox") as Style,
        };
        box.Click += OutputSetting_Click;
        return box;
    }

    private TextBox OutputScaleBox(string key, string tooltip)
    {
        var box = new TextBox
        {
            Tag = key,
            ToolTip = tooltip,
            Style = TryFindResource("RibbonValue") as Style,
        };
        box.KeyDown += OutputScaleBox_KeyDown;
        box.LostFocus += OutputScaleBox_LostFocus;
        return box;
    }

    private TextBlock OutputLabel(string text, string tooltip)
    {
        return new TextBlock
        {
            Text = text,
            ToolTip = tooltip,
            Style = TryFindResource("TopCommandLabel") as Style,
            Margin = new Thickness(0, 0, 4, 0),
        };
    }

    private Separator BuildTopSeparator() =>
        new() { Margin = new Thickness(0, 0, 0, 0) };

    private void OutputSetting_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
            return;

        _settings.PdfExportIncludeMeasurements = _chkOutputPdfMeasurements?.IsChecked == true;
        _settings.PdfExportIncludeAnnotations = _chkOutputPdfMarkups?.IsChecked == true;
        _settings.PdfExportShowSheetLegend = _chkOutputPdfLegend?.IsChecked == true;
        _settings.PdfExportShowMeasurementLabels = _chkOutputPdfLabels?.IsChecked == true;
        _settings.PdfExportShowLineLabels = _chkOutputPdfLineLabels?.IsChecked == true;
        _settings.PdfExportShowAreaLabels = _chkOutputPdfAreaLabels?.IsChecked == true;
        _settings.PdfExportShowCountLabels = _chkOutputPdfCountLabels?.IsChecked == true;

        ApplyOutputSettings();
        TxtStatus.Text = "Output settings saved.";
    }

    private void OutputScaleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return) || sender is not TextBox box)
            return;

        ApplyOutputScaleBox(box);
        e.Handled = true;
    }

    private void OutputScaleBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            ApplyOutputScaleBox(box);
    }

    private void ApplyOutputScaleBox(TextBox box)
    {
        if (_isApplyingSettings)
            return;

        string key = box.Tag as string ?? "";
        (double min, double max) = key.Equals("pdfLabel", StringComparison.OrdinalIgnoreCase)
            ? (0.50, AppSettingsStore.PdfExportScaleMax)
            : (0.25, AppSettingsStore.PdfExportScaleMax);

        string raw = box.Text.Trim().Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < min ||
            scale > max)
        {
            SyncOutputSettingsControls();
            TxtStatus.Text = $"Output scale must be {min:0.##} - {max:0.##}.";
            return;
        }

        switch (key)
        {
            case "pdfStroke":
                _settings.PdfExportMeasurementStrokeScale = scale;
                break;
            case "pdfPoint":
                _settings.PdfExportPointSizeScale = scale;
                break;
            case "pdfLabel":
                _settings.PdfExportMeasurementLabelScale = scale;
                break;
            case "pdfLegend":
                _settings.PdfExportSheetLegendScale = scale;
                break;
            case "pdfHeader":
                _settings.PdfExportSheetHeaderScale = scale;
                break;
            default:
                return;
        }

        ApplyOutputSettings();
        TxtStatus.Text = $"Output {OutputScaleLabel(key)}: {scale:0.##}x.";
    }

    private void ApplyOutputSettings()
    {
        AppSettingsStore.NormalizeOutputSettings(_settings);
        SyncOutputSettingsControls();
        SaveAppSettings();
    }

    private void SyncOutputSettingsControls()
    {
        if (_chkOutputPdfMeasurements == null)
            return;

        bool wasApplying = _isApplyingSettings;
        _isApplyingSettings = true;
        try
        {
            AppSettingsStore.NormalizeOutputSettings(_settings);

            _chkOutputPdfMeasurements.IsChecked = _settings.PdfExportIncludeMeasurements;
            _chkOutputPdfMarkups!.IsChecked = _settings.PdfExportIncludeAnnotations;
            _chkOutputPdfLegend!.IsChecked = _settings.PdfExportShowSheetLegend;
            _chkOutputPdfLabels!.IsChecked = _settings.PdfExportShowMeasurementLabels;
            _chkOutputPdfLineLabels!.IsChecked = _settings.PdfExportShowLineLabels;
            _chkOutputPdfAreaLabels!.IsChecked = _settings.PdfExportShowAreaLabels;
            _chkOutputPdfCountLabels!.IsChecked = _settings.PdfExportShowCountLabels;
            SetScaleText(_txtOutputPdfStroke, _settings.PdfExportMeasurementStrokeScale);
            SetScaleText(_txtOutputPdfPoint, _settings.PdfExportPointSizeScale);
            SetScaleText(_txtOutputPdfLabel, _settings.PdfExportMeasurementLabelScale);
            SetScaleText(_txtOutputPdfLegend, _settings.PdfExportSheetLegendScale);
            SetScaleText(_txtOutputPdfHeader, _settings.PdfExportSheetHeaderScale);
            SetSlider(_sldOutputPdfStroke, _settings.PdfExportMeasurementStrokeScale);
            SetSlider(_sldOutputPdfPoint, _settings.PdfExportPointSizeScale);
            SetSlider(_sldOutputPdfLabel, _settings.PdfExportMeasurementLabelScale);
            SetSlider(_sldOutputPdfLegend, _settings.PdfExportSheetLegendScale);
            SetSlider(_sldOutputPdfHeader, _settings.PdfExportSheetHeaderScale);
        }
        finally
        {
            _isApplyingSettings = wasApplying;
        }
    }

    private static void SetScaleText(TextBox? box, double value)
    {
        if (box != null)
            box.Text = value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static void SetSlider(Slider? slider, double value)
    {
        if (slider == null)
            return;

        slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
    }

    private static string OutputScaleLabel(string key) => key switch
    {
        "pdfStroke" => "PDF stroke",
        "pdfPoint" => "PDF point",
        "pdfLabel" => "PDF label",
        "pdfLegend" => "PDF legend",
        "pdfHeader" => "PDF header",
        _ => "scale",
    };
}
