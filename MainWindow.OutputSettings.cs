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
    private CheckBox? _chkOutputPdfJoistLabels;
    private CheckBox? _chkOutputPdfCountLabels;
    private TextBox? _txtOutputPdfStroke;
    private TextBox? _txtOutputPdfPoint;
    private TextBox? _txtOutputPdfLabel;
    private TextBox? _txtOutputPdfLegend;
    private TextBox? _txtOutputPdfHeader;
    private TextBox? _txtOutputPdfAreaEdge;
    private TextBox? _txtOutputPdfAreaFill;
    private Slider? _sldOutputPdfStroke;
    private Slider? _sldOutputPdfPoint;
    private Slider? _sldOutputPdfLabel;
    private Slider? _sldOutputPdfLegend;
    private Slider? _sldOutputPdfHeader;
    private Slider? _sldOutputPdfAreaEdge;
    private Slider? _sldOutputPdfAreaFill;
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
            Margin = new Thickness(6, 1, 6, 1),
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

        _chkOutputPdfMeasurements = OutputCheckBox("Meas", "Default: include measurements in PDF export.");
        _chkOutputPdfMarkups = OutputCheckBox("Markups", "Default: include markups in PDF export.");
        _chkOutputPdfLegend = OutputCheckBox("Legend", "Default: include the legend overlay in PDF export.");
        _chkOutputPdfLabels = OutputCheckBox("All", "Master toggle for all exported measurement value labels.");
        _chkOutputPdfLineLabels = OutputCheckBox("Line", "Show exported line labels.");
        _chkOutputPdfAreaLabels = OutputCheckBox("Area", "Show exported area labels.");
        _chkOutputPdfJoistLabels = OutputCheckBox("Joist", "Show exported joist segment labels (separate from the Area label).");
        _chkOutputPdfCountLabels = OutputCheckBox("Count", "Show exported count labels.");

        _txtOutputPdfStroke = OutputScaleBox("pdfStroke", "PDF export line thickness multiplier.");
        _txtOutputPdfPoint = OutputScaleBox("pdfPoint", "PDF export point marker size multiplier.");
        _txtOutputPdfAreaEdge = OutputScaleBox("pdfAreaEdge", "PDF export area edge thickness 0.25 - 4.");
        _txtOutputPdfAreaFill = OutputScaleBox("pdfAreaFill", "PDF export area fill opacity 0 - 100 %.");
        _txtOutputPdfLabel = OutputScaleBox("pdfLabel", "PDF export measurement label size multiplier.");
        _txtOutputPdfLegend = OutputScaleBox("pdfLegend", "PDF export legend size multiplier.");
        _txtOutputPdfHeader = OutputScaleBox("pdfHeader", "PDF export scale / sheet-size header multiplier.");
        _sldOutputPdfStroke = OutputSlider("pdfStroke", 0.25, max, "PDF export line thickness 0.25 - 10");
        _sldOutputPdfPoint = OutputSlider("pdfPoint", 0.25, max, "PDF export point size 0.25 - 10");
        _sldOutputPdfAreaEdge = OutputSlider("pdfAreaEdge", 0.25, 4.0, "PDF export area edge thickness 0.25 - 4");
        _sldOutputPdfAreaFill = OutputSlider("pdfAreaFill", 0, 100, "PDF export area fill opacity 0 - 100 %");
        _sldOutputPdfLabel = OutputSlider("pdfLabel", 0.50, max, "PDF export label size 0.5 - 10");
        _sldOutputPdfLegend = OutputSlider("pdfLegend", 0.25, max, "PDF export legend size 0.25 - 10");
        _sldOutputPdfHeader = OutputSlider("pdfHeader", 0.25, max, "PDF export header size 0.25 - 10");

        // ── GROUP: LINES & AREA (2 columns x 2 rows, same as Viewport) ──
        var laColA = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 10, 0) };
        laColA.Children.Add(OutputScaleRow("Line", _sldOutputPdfStroke, _txtOutputPdfStroke));
        laColA.Children.Add(OutputScaleRow("Point", _sldOutputPdfPoint, _txtOutputPdfPoint));
        var laColB = new StackPanel { Orientation = Orientation.Vertical };
        laColB.Children.Add(OutputScaleRow("Edge", _sldOutputPdfAreaEdge, _txtOutputPdfAreaEdge));
        laColB.Children.Add(OutputScaleRow("Fill", _sldOutputPdfAreaFill, _txtOutputPdfAreaFill));
        var linesArea = new StackPanel { Orientation = Orientation.Horizontal };
        linesArea.Children.Add(laColA);
        linesArea.Children.Add(laColB);
        root.Children.Add(RibbonGroupContainer("LINES & AREA", linesArea));

        root.Children.Add(RibbonSep());

        // ── GROUP: LABELS (checkbox row + Size row, same as Viewport) ──
        var lblRow = OutputHRow();
        lblRow.Children.Add(_chkOutputPdfLabels);
        lblRow.Children.Add(_chkOutputPdfLineLabels);
        lblRow.Children.Add(_chkOutputPdfAreaLabels);
        lblRow.Children.Add(_chkOutputPdfJoistLabels);
        lblRow.Children.Add(_chkOutputPdfCountLabels);
        var labels = new StackPanel { Orientation = Orientation.Vertical };
        labels.Children.Add(lblRow);
        labels.Children.Add(OutputScaleRow("Size", _sldOutputPdfLabel, _txtOutputPdfLabel));
        root.Children.Add(RibbonGroupContainer("LABELS", labels));

        root.Children.Add(RibbonSep());

        // ── GROUP: OVERLAYS (Legend / Header sizes, same as Viewport) ──
        var overlays = new StackPanel { Orientation = Orientation.Vertical };
        overlays.Children.Add(OutputScaleRow("Legend", _sldOutputPdfLegend, _txtOutputPdfLegend));
        overlays.Children.Add(OutputScaleRow("Header", _sldOutputPdfHeader, _txtOutputPdfHeader));
        root.Children.Add(RibbonGroupContainer("OVERLAYS", overlays));

        root.Children.Add(RibbonSep());

        // ── GROUP: INCLUDE (export content toggles; PDF analog of UNITS & VIEW) ──
        var incRow1 = OutputHRow();
        incRow1.Children.Add(_chkOutputPdfMeasurements);
        incRow1.Children.Add(_chkOutputPdfMarkups);
        var incRow2 = OutputHRow();
        incRow2.Children.Add(_chkOutputPdfLegend);
        var include = new StackPanel { Orientation = Orientation.Vertical };
        include.Children.Add(incRow1);
        include.Children.Add(incRow2);
        root.Children.Add(RibbonGroupContainer("INCLUDE", include));

        return root;
    }

    private static StackPanel OutputHRow() =>
        new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };

    private FrameworkElement RibbonGroupContainer(string label, UIElement content)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var body = new StackPanel { Style = TryFindResource("RibbonGroupBody") as Style };
        body.Children.Add(content);
        sp.Children.Add(body);
        var border = new Border
        {
            Style = TryFindResource("RibbonGroupCap") as Style,
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
        Style = TryFindResource("RibbonGroupSep") as Style,
    };

    private FrameworkElement OutputScaleRow(string text, Slider slider, TextBox box)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
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
            case "pdfAreaEdge":
                _settings.PdfExportAreaEdgeScale = v;
                SetScaleText(_txtOutputPdfAreaEdge, v);
                break;
            case "pdfAreaFill":
                _settings.PdfExportAreaFillOpacity = Math.Clamp(v / 100.0, 0.0, 1.0);
                if (_txtOutputPdfAreaFill != null)
                    _txtOutputPdfAreaFill.Text = Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
                _outputScaleDirty = true;
                TxtStatus.Text = $"Output {OutputScaleLabel(key)}: {Math.Round(v):0}%.";
                return;
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
        _settings.PdfExportShowJoistLabels = _chkOutputPdfJoistLabels?.IsChecked == true;
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
        (double min, double max) = key switch
        {
            "pdfLabel" => (0.50, AppSettingsStore.PdfExportScaleMax),
            "pdfAreaEdge" => (0.25, 4.0),
            "pdfAreaFill" => (0.0, 100.0),
            _ => (0.25, AppSettingsStore.PdfExportScaleMax),
        };

        string raw = box.Text.Trim().TrimEnd('%').Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < min ||
            scale > max)
        {
            SyncOutputSettingsControls();
            TxtStatus.Text = key == "pdfAreaFill"
                ? "Area fill opacity must be 0 - 100 (%)."
                : $"Output scale must be {min:0.##} - {max:0.##}.";
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
            case "pdfAreaEdge":
                _settings.PdfExportAreaEdgeScale = scale;
                break;
            case "pdfAreaFill":
                _settings.PdfExportAreaFillOpacity = Math.Clamp(scale / 100.0, 0.0, 1.0);
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
        TxtStatus.Text = key == "pdfAreaFill"
            ? $"Output {OutputScaleLabel(key)}: {scale:0}%."
            : $"Output {OutputScaleLabel(key)}: {scale:0.##}x.";
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
            _chkOutputPdfJoistLabels!.IsChecked = _settings.PdfExportShowJoistLabels;
            _chkOutputPdfCountLabels!.IsChecked = _settings.PdfExportShowCountLabels;
            SetScaleText(_txtOutputPdfStroke, _settings.PdfExportMeasurementStrokeScale);
            SetScaleText(_txtOutputPdfPoint, _settings.PdfExportPointSizeScale);
            SetScaleText(_txtOutputPdfAreaEdge, _settings.PdfExportAreaEdgeScale);
            if (_txtOutputPdfAreaFill != null)
                _txtOutputPdfAreaFill.Text = Math.Round(_settings.PdfExportAreaFillOpacity * 100.0).ToString("0", CultureInfo.InvariantCulture);
            SetScaleText(_txtOutputPdfLabel, _settings.PdfExportMeasurementLabelScale);
            SetScaleText(_txtOutputPdfLegend, _settings.PdfExportSheetLegendScale);
            SetScaleText(_txtOutputPdfHeader, _settings.PdfExportSheetHeaderScale);
            SetSlider(_sldOutputPdfStroke, _settings.PdfExportMeasurementStrokeScale);
            SetSlider(_sldOutputPdfPoint, _settings.PdfExportPointSizeScale);
            SetSlider(_sldOutputPdfAreaEdge, _settings.PdfExportAreaEdgeScale);
            SetSlider(_sldOutputPdfAreaFill, Math.Round(_settings.PdfExportAreaFillOpacity * 100.0));
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
        "pdfStroke" => "PDF line thickness",
        "pdfPoint" => "PDF point",
        "pdfAreaEdge" => "PDF area edge",
        "pdfAreaFill" => "PDF area fill",
        "pdfLabel" => "PDF label",
        "pdfLegend" => "PDF legend",
        "pdfHeader" => "PDF header",
        _ => "scale",
    };
}
