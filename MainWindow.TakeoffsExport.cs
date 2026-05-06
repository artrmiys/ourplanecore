using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before exporting.";
            return;
        }

        if (_takeoffItems.Count == 0 || _takeoffItems.All(i => i.Measurements.Count == 0))
        {
            TxtStatus.Text = "No measurements to export.";
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export Takeoff CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"{SafeFileName(_currentJob.Name)}_takeoffs.csv",
            InitialDirectory = _currentJob.RootPath,
            AddExtension = true,
            DefaultExt = ".csv",
        };

        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            SaveCurrentPageScale();
            File.WriteAllText(dlg.FileName, BuildTakeoffCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            TxtStatus.Text = $"Exported CSV -> {dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnExportTxt_Click(object sender, RoutedEventArgs e)
    {
        ExportPlanSwiftTakeoffs("txt");
    }

    private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
    {
        ExportPlanSwiftTakeoffs("xlsx");
    }

    private void ExportPlanSwiftTakeoffs(string format)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before exporting.";
            return;
        }

        var rows = BuildPlanSwiftExportRows();
        if (rows.Count == 0)
        {
            TxtStatus.Text = "No measured takeoffs to export.";
            return;
        }

        bool excel = string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase);
        var dlg = new SaveFileDialog
        {
            Title = excel ? "Export Takeoffs Excel" : "Export Takeoffs TXT",
            Filter = excel ? "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*" : "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"{SafeFileName(_currentJob.Name)}_takeoffs.{(excel ? "xlsx" : "txt")}",
            InitialDirectory = _currentJob.RootPath,
            AddExtension = true,
            DefaultExt = excel ? ".xlsx" : ".txt",
        };

        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            SaveCurrentPageScale();
            if (excel)
            {
                int written = PlanSwiftTakeoffExporter.WriteXlsx(dlg.FileName, rows);
                TxtStatus.Text = $"Exported Excel ({written} row(s)) -> {dlg.FileName}";
            }
            else
            {
                PlanSwiftTakeoffExporter.WriteTxt(dlg.FileName, rows);
                TxtStatus.Text = $"Exported TXT ({rows.Count} row(s)) -> {dlg.FileName}";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", excel ? "Export Excel" : "Export TXT",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private IReadOnlyList<PlanSwiftExportRow> BuildPlanSwiftExportRows()
    {
        if (_currentJob == null)
            return [];

        IReadOnlyList<string> roots = SelectedTakeoffExportRoots();
        return PlanSwiftTakeoffExporter.BuildRows(_currentJob, _takeoffItems, roots, _viewport.UnitMode);
    }

    private IReadOnlyList<string> SelectedTakeoffExportRoots()
    {
        if (_currentJob == null)
            return [];

        if (TakeoffsTree.SelectedItem is TreeViewItem anchor)
        {
            var entries = GetSelectedTakeoffEntries(anchor);
            if (entries.Count > 0)
                return entries.Select(entry => entry.SourcePath).ToList();

            return anchor.Tag switch
            {
                TakeoffItem item when Directory.Exists(item.FolderPath) => [item.FolderPath],
                TakeoffFolderNode folder when Directory.Exists(folder.FolderPath) => [folder.FolderPath],
                _ => [_currentJob.TakeoffsRoot],
            };
        }

        return [_currentJob.TakeoffsRoot];
    }

    private string BuildTakeoffCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("RowType,Item,MeasurementType,Total,MeasurementCount,UnitPrice,Cost,MeasurementId,SectionName,SectionNotes,SectionIndex,MeasurementValue,MeasurementLabel,ScaleMetersPerPt,PageFolder,TakeoffFolder");

        foreach (var item in _takeoffItems.Where(i => i.Measurements.Count > 0))
        {
            string itemTypes = item.IsJoistArea
                ? "joist"
                : string.Join("+", item.Measurements.Select(m => m.MType).Distinct(StringComparer.OrdinalIgnoreCase));
            AppendCsvRow(sb,
                "ItemTotal",
                item.Name,
                itemTypes,
                ExportItemTotalText(item),
                item.Measurements.Count.ToString(),
                item.UnitPrice.ToString("G17", CultureInfo.InvariantCulture),
                CostText(item),
                "",
                "",
                "",
                "",
                "",
                "",
                "",
                "",
                item.FolderPath);

            for (int i = 0; i < item.Measurements.Count; i++)
            {
                Measurement measurement = item.Measurements[i];
                AppendCsvRow(sb,
                    "Measurement",
                    item.Name,
                    measurement.JoistEnabled ? "joist" : measurement.MType,
                    "",
                    "",
                    "",
                    "",
                    measurement.Id,
                    SectionDisplayName(item, measurement, i),
                    measurement.Notes,
                    (i + 1).ToString(CultureInfo.InvariantCulture),
                    measurement.Value(_viewport.ScaleMetersPerPt).ToString("G17", CultureInfo.InvariantCulture),
                    measurement.Label(_viewport.ScaleMetersPerPt, _viewport.UnitMode),
                    measurement.ScaleMetersPerPt.ToString("G17", CultureInfo.InvariantCulture),
                    measurement.PageFolder,
                    measurement.TakeoffFolder);
            }
        }

        return sb.ToString();
    }

    private string ExportItemTotalText(TakeoffItem item)
    {
        if (!item.IsJoistArea)
            return item.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode);

        string labelText = PlanSwiftTakeoffExporter.JoistLabelText(
            item,
            _viewport.ScaleMetersPerPt,
            _viewport.UnitMode);
        return string.IsNullOrWhiteSpace(labelText)
            ? item.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode)
            : labelText;
    }

    private static void AppendCsvRow(StringBuilder sb, params string[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(CsvEscape(values[i]));
        }
        sb.AppendLine();
    }

    private static string CsvEscape(string value)
    {
        value ??= "";
        if (!value.Contains('"') && !value.Contains(',') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string SafeFileName(string value)
    {
        string name = string.IsNullOrWhiteSpace(value) ? "takeoffs" : value.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
