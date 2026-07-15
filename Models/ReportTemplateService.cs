using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace OurPlanCore;

public sealed record ReportTemplateColumn(string Letter, string BindingPath, string Header, double Width);

public sealed record ReportTemplateSheet(
    string TemplatePath,
    string SheetName,
    IReadOnlyList<ReportTemplateColumn> Columns,
    IReadOnlyList<ReportBuilderRow> Rows);

public sealed class ReportBuilderRow : INotifyPropertyChanged
{
    private readonly Dictionary<string, string> _cells = new(StringComparer.OrdinalIgnoreCase);

    public event PropertyChangedEventHandler? PropertyChanged;

    public int RowNumber { get; init; }
    public string RowKind { get; init; } = "Data";
    public string RowLabel => RowNumber.ToString();

    public string A { get => GetCell("A"); set => SetCell("A", value); }
    public string B { get => GetCell("B"); set => SetCell("B", value); }
    public string C { get => GetCell("C"); set => SetCell("C", value); }
    public string D { get => GetCell("D"); set => SetCell("D", value); }
    public string E { get => GetCell("E"); set => SetCell("E", value); }
    public string F { get => GetCell("F"); set => SetCell("F", value); }
    public string G { get => GetCell("G"); set => SetCell("G", value); }
    public string H { get => GetCell("H"); set => SetCell("H", value); }
    public string I { get => GetCell("I"); set => SetCell("I", value); }
    public string J { get => GetCell("J"); set => SetCell("J", value); }
    public string K { get => GetCell("K"); set => SetCell("K", value); }
    public string L { get => GetCell("L"); set => SetCell("L", value); }
    public string M { get => GetCell("M"); set => SetCell("M", value); }
    public string N { get => GetCell("N"); set => SetCell("N", value); }
    public string O { get => GetCell("O"); set => SetCell("O", value); }
    public string P { get => GetCell("P"); set => SetCell("P", value); }
    public string Q { get => GetCell("Q"); set => SetCell("Q", value); }
    public string R { get => GetCell("R"); set => SetCell("R", value); }
    public string S { get => GetCell("S"); set => SetCell("S", value); }
    public string T { get => GetCell("T"); set => SetCell("T", value); }
    public string U { get => GetCell("U"); set => SetCell("U", value); }
    public string V { get => GetCell("V"); set => SetCell("V", value); }
    public string W { get => GetCell("W"); set => SetCell("W", value); }
    public string X { get => GetCell("X"); set => SetCell("X", value); }
    public string Y { get => GetCell("Y"); set => SetCell("Y", value); }
    public string Z { get => GetCell("Z"); set => SetCell("Z", value); }
    public string AA { get => GetCell("AA"); set => SetCell("AA", value); }
    public string AB { get => GetCell("AB"); set => SetCell("AB", value); }

    public IReadOnlyDictionary<string, string> Cells => _cells;

    public void SetInitialCell(string column, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _cells[column] = Clean(value);
    }

    public void SetCellValue(string column, string value) =>
        SetCell(column, value, column.ToUpperInvariant());

    private string GetCell(string column) =>
        _cells.TryGetValue(column, out string? value) ? value : "";

    private void SetCell(string column, string value, [CallerMemberName] string? propertyName = null)
    {
        string clean = Clean(value);
        if (string.Equals(GetCell(column), clean, StringComparison.Ordinal))
            return;

        if (string.IsNullOrWhiteSpace(clean))
            _cells.Remove(column);
        else
            _cells[column] = clean;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string Clean(string value) =>
        string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}

public static class ReportTemplateService
{
    public const string DetailedFrameListSheetName = "Detailed Frame List";
    public const string DefaultTemplateFileName = "TemplateCom.xlsm";

    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace WorkbookRelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly string[] VisibleColumns =
    [
        "A", "B", "C", "D", "E", "F", "G", "H", "I",
        "J", "K", "L", "M", "N", "O", "P", "Q", "R",
        "S", "T", "U", "V", "W", "X", "Y", "Z", "AA", "AB"
    ];

    public static string DefaultTemplatePath()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return ResolveDefaultTemplatePath(AppContext.BaseDirectory, desktop);
    }

    public static string ResolveDefaultTemplatePath(string appBaseDirectory, string desktopDirectory)
    {
        string packagedPath = Path.Combine(appBaseDirectory, DefaultTemplateFileName);
        string currentDevelopmentPath = Path.Combine(desktopDirectory, "Python", "1.macros", DefaultTemplateFileName);
        string legacyPath = Path.Combine(
            desktopDirectory,
            "03_Excel_Templates_Macros",
            "Templates",
            DefaultTemplateFileName);

        return new[] { packagedPath, currentDevelopmentPath, legacyPath }
            .FirstOrDefault(File.Exists) ?? packagedPath;
    }

    public static ReportTemplateSheet LoadDefaultTemplate() =>
        LoadTemplate(DefaultTemplatePath(), DetailedFrameListSheetName);

    public static ReportTemplateSheet LoadTemplate(string templatePath, string sheetName = DetailedFrameListSheetName)
    {
        if (string.IsNullOrWhiteSpace(templatePath))
            throw new ArgumentException("Template path is empty.", nameof(templatePath));
        if (!File.Exists(templatePath))
            throw new FileNotFoundException("Report template was not found.", templatePath);

        using FileStream stream = File.Open(templatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        IReadOnlyList<string> sharedStrings = ReadSharedStrings(archive);
        Dictionary<int, string> styleFillColors = ReadStyleFillColors(archive);
        var sheet = ResolveSheet(archive, sheetName);
        XDocument worksheet = LoadXml(archive, sheet.Path);
        IReadOnlyDictionary<string, double> widths = ReadColumnWidths(worksheet);
        IReadOnlyList<ReportBuilderRow> rows = ReadRows(worksheet, sharedStrings, styleFillColors);

        return new ReportTemplateSheet(
            templatePath,
            sheet.Name,
            VisibleColumns.Select(column => new ReportTemplateColumn(
                column,
                column,
                column,
                ExcelWidthToWpf(widths.TryGetValue(column, out double width) ? width : 10))).ToList(),
            rows);
    }

    private static IReadOnlyList<ReportBuilderRow> ReadRows(
        XDocument worksheet,
        IReadOnlyList<string> sharedStrings,
        IReadOnlyDictionary<int, string> styleFillColors)
    {
        var rows = new List<ReportBuilderRow>();
        foreach (XElement rowElement in worksheet.Descendants(SpreadsheetNs + "row"))
        {
            int rowNumber = ParseRowNumber(rowElement);
            if (rowNumber <= 0)
                continue;

            var rowCells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var rowFills = new List<string>();
            foreach (XElement cell in rowElement.Elements(SpreadsheetNs + "c"))
            {
                string column = ColumnName(cell.Attribute("r")?.Value);
                if (!VisibleColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
                    continue;

                string value = CellValue(cell, sharedStrings);
                rowCells[column] = value;
                if (int.TryParse(cell.Attribute("s")?.Value, out int styleIndex) &&
                    styleFillColors.TryGetValue(styleIndex, out string? fill))
                {
                    rowFills.Add(fill);
                }
            }

            if (rowCells.Count == 0 && rowNumber > 80)
                continue;

            var reportRow = new ReportBuilderRow
            {
                RowNumber = rowNumber,
                RowKind = RowKind(rowNumber, rowCells, rowFills),
            };
            foreach ((string column, string value) in rowCells)
                reportRow.SetInitialCell(column, value);
            rows.Add(reportRow);
        }

        return rows;
    }

    private static string RowKind(int rowNumber, IReadOnlyDictionary<string, string> cells, IReadOnlyList<string> fills)
    {
        if (rowNumber <= 10)
            return "Header";
        if (rowNumber is >= 11 and <= 14)
            return "TableHeader";
        if (IsSectionRow(cells))
            return "Section";
        if (fills.Any(IsYellowishFill))
            return "InputBlock";
        return "Data";
    }

    private static bool IsSectionRow(IReadOnlyDictionary<string, string> cells)
    {
        string a = cells.TryGetValue("A", out string? value) ? value : "";
        if (string.IsNullOrWhiteSpace(a))
            return false;

        return VisibleColumns
            .Where(column => column != "A")
            .All(column => !cells.TryGetValue(column, out string? other) || string.IsNullOrWhiteSpace(other));
    }

    private static bool IsYellowishFill(string color)
    {
        string clean = (color ?? "").Trim().TrimStart('#').ToUpperInvariant();
        return clean is "FFFFFF00" or "FFFFF2CC" or "FFFFEB9C" or "FFFFE699" or "FFFFFF99" or "FFFFCC00" or "36" or "13";
    }

    private static IReadOnlyDictionary<string, double> ReadColumnWidths(XDocument worksheet)
    {
        var widths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement column in worksheet.Descendants(SpreadsheetNs + "col"))
        {
            if (!int.TryParse(column.Attribute("min")?.Value, out int min) ||
                !int.TryParse(column.Attribute("max")?.Value, out int max) ||
                !double.TryParse(
                    column.Attribute("width")?.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double width))
            {
                continue;
            }

            for (int index = min; index <= max; index++)
            {
                string letter = ColumnNameFromIndex(index);
                if (VisibleColumns.Contains(letter, StringComparer.OrdinalIgnoreCase))
                    widths[letter] = width;
            }
        }

        return widths;
    }

    private static double ExcelWidthToWpf(double width) =>
        Math.Clamp(width * 7.0 + 8.0, 42.0, 230.0);

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null)
            return [];

        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        return document.Root?
            .Elements(SpreadsheetNs + "si")
            .Select(si => string.Concat(si.Descendants(SpreadsheetNs + "t").Select(t => t.Value)))
            .ToList() ?? [];
    }

    private static Dictionary<int, string> ReadStyleFillColors(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/styles.xml");
        if (entry == null)
            return [];

        using Stream stream = entry.Open();
        XDocument styles = XDocument.Load(stream);
        List<string> fills = styles.Root?
            .Element(SpreadsheetNs + "fills")?
            .Elements(SpreadsheetNs + "fill")
            .Select(ReadFillColor)
            .ToList() ?? [];

        var result = new Dictionary<int, string>();
        int styleIndex = 0;
        foreach (XElement xf in styles.Root?
                     .Element(SpreadsheetNs + "cellXfs")?
                     .Elements(SpreadsheetNs + "xf") ?? [])
        {
            if (int.TryParse(xf.Attribute("fillId")?.Value, out int fillId) &&
                fillId >= 0 &&
                fillId < fills.Count)
            {
                result[styleIndex] = fills[fillId];
            }

            styleIndex++;
        }

        return result;
    }

    private static string ReadFillColor(XElement fill)
    {
        XElement? pattern = fill.Element(SpreadsheetNs + "patternFill");
        XElement? fg = pattern?.Element(SpreadsheetNs + "fgColor");
        XElement? bg = pattern?.Element(SpreadsheetNs + "bgColor");
        return fg?.Attribute("rgb")?.Value ??
               bg?.Attribute("rgb")?.Value ??
               fg?.Attribute("indexed")?.Value ??
               bg?.Attribute("indexed")?.Value ??
               "";
    }

    private static (string Name, string Path) ResolveSheet(ZipArchive archive, string requestedSheetName)
    {
        XDocument workbook = LoadXml(archive, "xl/workbook.xml");
        Dictionary<string, string> relationships = ReadWorkbookRelationships(archive);
        var sheets = workbook.Root?
            .Element(SpreadsheetNs + "sheets")?
            .Elements(SpreadsheetNs + "sheet")
            .Select(sheet => new
            {
                Name = sheet.Attribute("name")?.Value ?? "",
                RelationshipId = sheet.Attribute(WorkbookRelationshipNs + "id")?.Value ?? "",
            })
            .Where(sheet => !string.IsNullOrWhiteSpace(sheet.Name) && !string.IsNullOrWhiteSpace(sheet.RelationshipId))
            .ToList() ?? [];

        var selected = sheets.FirstOrDefault(sheet => sheet.Name.Equals(requestedSheetName, StringComparison.OrdinalIgnoreCase)) ??
                       sheets.FirstOrDefault(sheet => sheet.Name.Contains("Detailed", StringComparison.OrdinalIgnoreCase)) ??
                       sheets.FirstOrDefault();
        if (selected == null)
            throw new InvalidDataException("Workbook does not contain any worksheets.");
        if (!relationships.TryGetValue(selected.RelationshipId, out string? target))
            throw new InvalidDataException($"Worksheet relationship '{selected.RelationshipId}' was not found.");

        string path = ResolveWorkbookTarget(target);
        if (archive.GetEntry(path) == null)
            throw new InvalidDataException($"Worksheet file '{path}' was not found in the workbook.");
        return (selected.Name, path);
    }

    private static Dictionary<string, string> ReadWorkbookRelationships(ZipArchive archive)
    {
        XDocument rels = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        return rels.Root?
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(rel => !string.IsNullOrWhiteSpace(rel.Attribute("Id")?.Value))
            .ToDictionary(
                rel => rel.Attribute("Id")!.Value,
                rel => rel.Attribute("Target")?.Value ?? "",
                StringComparer.OrdinalIgnoreCase) ?? [];
    }

    private static string ResolveWorkbookTarget(string target)
    {
        string normalized = (target ?? "").Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : "xl/" + normalized;
    }

    private static string CellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        string type = cell.Attribute("t")?.Value ?? "";
        if (type.Equals("inlineStr", StringComparison.OrdinalIgnoreCase))
            return Clean(string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(t => t.Value)));

        string value = cell.Element(SpreadsheetNs + "v")?.Value ?? "";
        if (type.Equals("s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(value, out int sharedIndex) &&
            sharedIndex >= 0 &&
            sharedIndex < sharedStrings.Count)
        {
            return Clean(sharedStrings[sharedIndex]);
        }

        return Clean(value);
    }

    private static int ParseRowNumber(XElement rowElement) =>
        int.TryParse(rowElement.Attribute("r")?.Value, out int rowNumber) ? rowNumber : 0;

    private static string ColumnName(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
            return "";

        return new string(cellReference.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
    }

    private static string ColumnNameFromIndex(int index)
    {
        string name = "";
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }

        return name;
    }

    private static string Clean(string value) =>
        string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        ZipArchiveEntry? entry = archive.GetEntry(path);
        if (entry == null)
            throw new InvalidDataException($"Workbook part '{path}' was not found.");

        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }
}
