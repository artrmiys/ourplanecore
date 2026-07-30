using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace OurPlanCore;

public partial class MainWindow
{
    private async void BtnExportCsv_Click(object sender, RoutedEventArgs e)
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
            using (ShowBusyOverlay("Exporting CSV..."))
            {
                await WaitForBusyOverlayRenderAsync();
                SaveCurrentPageScale();
                File.WriteAllText(dlg.FileName, BuildTakeoffCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            TxtStatus.Text = $"Exported CSV -> {dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnExportTxt_Click(object sender, RoutedEventArgs e)
    {
        await ExportPlanSwiftTakeoffsAsync("txt", exportWholeJob: true);
    }

    private async void BtnExportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.ExcelIntegration, "Export Excel"))
            return;
        await ExportPlanSwiftTakeoffsAsync("xlsx", exportWholeJob: false);
    }

    private async void BtnExportCurrentExcel_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.ExcelIntegration, "Export to Current Excel"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before exporting to Excel.";
            return;
        }

        IReadOnlyList<string> roots = SelectedCurrentExcelExportRoots();
        if (roots.Count == 0)
        {
            TxtStatus.Text = "Select a takeoff folder or item before exporting to current Excel.";
            return;
        }

        IReadOnlyList<PlanSwiftExportRow> rows;
        using (ShowBusyOverlay("Preparing active Excel export..."))
        {
            await WaitForBusyOverlayRenderAsync();
            SaveCurrentPageScale();
            rows = PlanSwiftTakeoffExporter.BuildRows(_currentJob, _takeoffItems, roots, _viewport.UnitMode);
        }
        if (rows.Count == 0)
        {
            TxtStatus.Text = "Selected takeoff folder has no measured rows to export.";
            return;
        }

        ActiveExcelExportResult result;
        using (ShowBusyOverlay("Writing to active Excel..."))
        {
            await WaitForBusyOverlayRenderAsync();
            result = ActiveExcelTakeoffExportService.ExportRows(rows);
        }
        TxtStatus.Text = result.Message;
        if (!result.Success)
        {
            MessageBox.Show(result.Message, "Export to Current Excel", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RefreshTakeoffManager();
    }

    private async Task ExportPlanSwiftTakeoffsAsync(string format, bool exportWholeJob)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before exporting.";
            return;
        }

        var rows = BuildPlanSwiftExportRows(exportWholeJob);
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
            using (ShowBusyOverlay(excel ? "Exporting Excel..." : "Exporting TXT..."))
            {
                await WaitForBusyOverlayRenderAsync();
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
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", excel ? "Export Excel" : "Export TXT",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private IReadOnlyList<PlanSwiftExportRow> BuildPlanSwiftExportRows(bool exportWholeJob)
    {
        if (_currentJob == null)
            return [];

        IReadOnlyList<string> roots = exportWholeJob
            ? [_currentJob.TakeoffsRoot]
            : SelectedTakeoffExportRoots();
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

    private IReadOnlyList<string> SelectedCurrentExcelExportRoots()
    {
        if (_currentJob == null)
            return [];

        IReadOnlyList<string> managerRoots = SelectedTakeoffManagerExportRoots();
        if (managerRoots.Count > 0)
            return managerRoots;

        if (TakeoffsTree.SelectedItem is not TreeViewItem anchor)
            return [];

        var entries = GetSelectedTakeoffEntries(anchor);
        if (entries.Count > 0)
            return entries.Select(entry => entry.SourcePath).ToList();

        return TakeoffTreeAnchorExportRoots(anchor);
    }

    private IReadOnlyList<string> SelectedExcelAllExportRoots()
    {
        if (_currentJob == null)
            return [];

        if (TakeoffsTree.SelectedItem is TreeViewItem anchor)
            return TakeoffTreeAnchorExportRoots(anchor);

        return SelectedTakeoffManagerExportRoots();
    }

    private IReadOnlyList<string> SelectedTakeoffManagerExportRoots()
    {
        if (WorkspaceTabs.SelectedItem is not TabItem { Tag: not null } tab ||
            !string.Equals(
                tab.Tag.ToString(),
                "TakeoffManager",
                StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return TakeoffManagerGrid.SelectedItems
            .OfType<TakeoffManagerRow>()
            .Select(row => row.Item.FolderPath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> TakeoffTreeAnchorExportRoots(TreeViewItem anchor)
    {
        string? path = anchor.Tag switch
        {
            TakeoffItem item => item.FolderPath,
            TakeoffMeasurementNode node => node.Item.FolderPath,
            TakeoffFolderNode folder => folder.FolderPath,
            _ => null,
        };

        return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)
            ? [path]
            : [];
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
                    PlanSwiftTakeoffExporter.CleanExportNotes(measurement.Notes),
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
