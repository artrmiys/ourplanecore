using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
    }

    private FrameworkElement BuildPdfOutputGroup()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0) };
        panel.Children.Add(OutputLabel("PDF Export", "Defaults for exported PDF sheets."));

        _chkOutputPdfMeasurements = OutputCheckBox("Meas", "Default: include measurements in PDF export.");
        _chkOutputPdfMarkups = OutputCheckBox("Markups", "Default: include markups in PDF export.");
        _chkOutputPdfLegend = OutputCheckBox("Legend", "Default: include legend in PDF export.");
        _chkOutputPdfLabels = OutputCheckBox("Labels", "Show exported measurement value labels.");
        _chkOutputPdfLineLabels = OutputCheckBox("Line", "Show exported line labels.");
        _chkOutputPdfAreaLabels = OutputCheckBox("Area", "Show exported area labels.");
        _chkOutputPdfCountLabels = OutputCheckBox("Count", "Show exported count labels.");
        panel.Children.Add(_chkOutputPdfMeasurements);
        panel.Children.Add(_chkOutputPdfMarkups);
        panel.Children.Add(_chkOutputPdfLegend);
        panel.Children.Add(_chkOutputPdfLabels);
        panel.Children.Add(_chkOutputPdfLineLabels);
        panel.Children.Add(_chkOutputPdfAreaLabels);
        panel.Children.Add(_chkOutputPdfCountLabels);

        panel.Children.Add(OutputLabel("Stroke", "Exported measurement line thickness."));
        _txtOutputPdfStroke = OutputScaleBox("pdfStroke", "PDF export measurement line thickness multiplier.");
        panel.Children.Add(_txtOutputPdfStroke);

        panel.Children.Add(OutputLabel("Point", "Exported point marker size."));
        _txtOutputPdfPoint = OutputScaleBox("pdfPoint", "PDF export point marker size multiplier.");
        panel.Children.Add(_txtOutputPdfPoint);

        panel.Children.Add(OutputLabel("Label", "Exported measurement label size."));
        _txtOutputPdfLabel = OutputScaleBox("pdfLabel", "PDF export measurement label size multiplier.");
        panel.Children.Add(_txtOutputPdfLabel);

        panel.Children.Add(OutputLabel("Leg.", "Exported legend size."));
        _txtOutputPdfLegend = OutputScaleBox("pdfLegend", "PDF export legend size multiplier.");
        panel.Children.Add(_txtOutputPdfLegend);

        panel.Children.Add(OutputLabel("Hdr.", "Exported scale / sheet-size header size."));
        _txtOutputPdfHeader = OutputScaleBox("pdfHeader", "PDF export scale / sheet-size header multiplier.");
        panel.Children.Add(_txtOutputPdfHeader);

        return panel;
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
            Width = 38,
            Height = 22,
            FontSize = 11,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = key,
            ToolTip = tooltip,
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
