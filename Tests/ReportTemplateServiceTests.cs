using OurPlanCore;
using System.IO.Compression;
using System.Xml.Linq;

internal static class ReportTemplateServiceTests
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace WorkbookRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static void LoadsSyntheticDetailedFrameList()
    {
        string path = CreateWorkbook();
        try
        {
            ReportTemplateSheet sheet = ReportTemplateService.LoadTemplate(path);
            AssertEqual("Detailed Frame List", sheet.SheetName, "sheet name");
            AssertEqual(28, sheet.Columns.Count, "visible columns");
            AssertTrue(sheet.Rows.Count >= 4, "rows loaded");
            AssertEqual("Header", sheet.Rows[0].RowKind, "row 1 kind");
            ReportBuilderRow tableHeader = sheet.Rows.First(row => row.RowNumber == 14);
            AssertEqual("TableHeader", tableHeader.RowKind, "row 14 kind");
            AssertEqual("Description", tableHeader.A, "header value");
            ReportBuilderRow input = sheet.Rows.First(row => row.RowNumber == 17);
            AssertEqual("InputBlock", input.RowKind, "yellow row kind");
            AssertEqual("Studs Ext", input.A, "input row value");
        }
        finally
        {
            TryDeleteFile(path);
        }
    }

    public static void LoadsLocalTemplateIfPresent()
    {
        string path = ReportTemplateService.DefaultTemplatePath();
        if (!File.Exists(path))
            return;

        ReportTemplateSheet sheet = ReportTemplateService.LoadTemplate(path);
        AssertTrue(sheet.Rows.Count > 100, "local template rows");
        AssertTrue(sheet.Columns.Any(column => column.Letter == "A"), "local template A column");
        AssertTrue(sheet.Rows.Any(row => string.Equals(row.A, "Description", StringComparison.OrdinalIgnoreCase)), "local template description row");
    }

    public static void PrefersTemplateBesidePackagedExecutable()
    {
        string root = Path.Combine(Path.GetTempPath(), "onc_report_template_resolution_" + Guid.NewGuid().ToString("N"));
        string appBase = Path.Combine(root, "app");
        string desktop = Path.Combine(root, "desktop");
        string packagedPath = Path.Combine(appBase, ReportTemplateService.DefaultTemplateFileName);
        string developmentPath = Path.Combine(desktop, "Python", "1.macros", ReportTemplateService.DefaultTemplateFileName);
        try
        {
            Directory.CreateDirectory(appBase);
            Directory.CreateDirectory(Path.GetDirectoryName(developmentPath)!);
            File.WriteAllText(packagedPath, "packaged");
            File.WriteAllText(developmentPath, "development");

            string resolved = ReportTemplateService.ResolveDefaultTemplatePath(appBase, desktop);

            AssertEqual(Path.GetFullPath(packagedPath), Path.GetFullPath(resolved), "packaged template path");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    public static void FallsBackToCurrentDevelopmentTemplate()
    {
        string root = Path.Combine(Path.GetTempPath(), "onc_report_template_fallback_" + Guid.NewGuid().ToString("N"));
        string appBase = Path.Combine(root, "app");
        string desktop = Path.Combine(root, "desktop");
        string developmentPath = Path.Combine(desktop, "Python", "1.macros", ReportTemplateService.DefaultTemplateFileName);
        try
        {
            Directory.CreateDirectory(appBase);
            Directory.CreateDirectory(Path.GetDirectoryName(developmentPath)!);
            File.WriteAllText(developmentPath, "development");

            string resolved = ReportTemplateService.ResolveDefaultTemplatePath(appBase, desktop);

            AssertEqual(Path.GetFullPath(developmentPath), Path.GetFullPath(resolved), "development template path");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    public static void AppliesA3WallBlockLikeMacro()
    {
        List<ReportBuilderRow> rows = Enumerable.Range(1, 60)
            .Select(rowNumber => new ReportBuilderRow { RowNumber = rowNumber })
            .ToList();
        SetSource(rows, 1, "1", "");
        SetSource(rows, 2, "corners", "22");
        SetSource(rows, 3, "ext 2x6 9.00", "207,38");
        SetSource(rows, 4, "corr 2x6 9.00", "212,14");
        SetSource(rows, 5, "dem 2x6 9.00", "168,43");

        IReadOnlyList<ReportWallSourceRow> sourceRows = ReportWallsBlockService.SourceRowsFromReportRows(rows.Take(5));
        ReportWallBlockResult result = ReportWallsBlockService.ApplyAllGroups(rows, sourceRows);

        AssertEqual(1, result.FloorGroups, "floor groups");
        AssertEqual(5, result.SourceRows, "source rows");
        AssertEqual("22", rows[37].Q, "corners target Q38");
        AssertEqual("9,00", rows[39].O, "x target O40");
        AssertEqual("207,38", rows[39].P, "ext 2x6 target P40");
        AssertEqual("212,14", rows[39].T, "corr 2x6 target T40");
        AssertEqual("168,43", rows[39].W, "dem 2x6 target W40");
    }

    private static string CreateWorkbook()
    {
        string path = Path.Combine(Path.GetTempPath(), "onc_report_template_" + Guid.NewGuid().ToString("N") + ".xlsx");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteXml(archive, "xl/workbook.xml", WorkbookXml());
        WriteXml(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml());
        WriteXml(archive, "xl/styles.xml", StylesXml());
        WriteXml(archive, "xl/worksheets/sheet1.xml", WorksheetXml());
        return path;
    }

    private static void SetSource(IReadOnlyList<ReportBuilderRow> rows, int rowNumber, string label, string value)
    {
        ReportBuilderRow row = rows[rowNumber - 1];
        row.SetCellValue("J", label);
        row.SetCellValue("K", value);
    }

    private static XDocument WorkbookXml() =>
        new(
            new XElement(SpreadsheetNs + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", WorkbookRelNs.NamespaceName),
                new XElement(SpreadsheetNs + "sheets",
                    new XElement(SpreadsheetNs + "sheet",
                        new XAttribute("name", "Detailed Frame List"),
                        new XAttribute("sheetId", "1"),
                        new XAttribute(WorkbookRelNs + "id", "rId1")))));

    private static XDocument WorkbookRelationshipsXml() =>
        new(
            new XElement(PackageRelNs + "Relationships",
                new XElement(PackageRelNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml"))));

    private static XDocument StylesXml() =>
        new(
            new XElement(SpreadsheetNs + "styleSheet",
                new XElement(SpreadsheetNs + "fills",
                    new XAttribute("count", "2"),
                    new XElement(SpreadsheetNs + "fill", new XElement(SpreadsheetNs + "patternFill")),
                    new XElement(SpreadsheetNs + "fill",
                        new XElement(SpreadsheetNs + "patternFill",
                            new XAttribute("patternType", "solid"),
                            new XElement(SpreadsheetNs + "fgColor", new XAttribute("rgb", "FFFFF2CC"))))),
                new XElement(SpreadsheetNs + "cellXfs",
                    new XAttribute("count", "2"),
                    new XElement(SpreadsheetNs + "xf", new XAttribute("fillId", "0")),
                    new XElement(SpreadsheetNs + "xf", new XAttribute("fillId", "1")))));

    private static XDocument WorksheetXml() =>
        new(
            new XElement(SpreadsheetNs + "worksheet",
                new XElement(SpreadsheetNs + "cols",
                    Column(1, 25.8),
                    Column(2, 20.7),
                    Column(3, 12.5),
                    Column(4, 11),
                    Column(5, 10.2),
                    Column(6, 10.5),
                    Column(7, 8.7),
                    Column(8, 4.7),
                    Column(10, 12.7),
                    Column(11, 6.7),
                    Column(12, 6.7)),
                new XElement(SpreadsheetNs + "sheetData",
                    Row(1, ("A", "Detailed Material List", 0)),
                    Row(10, ("A", "Header", 0)),
                    Row(14, ("A", "Description", 0), ("B", "Size", 0), ("C", "Quantities", 0)),
                    Row(17, ("A", "Studs Ext", 1), ("B", "2x6 pc", 1), ("C", "0", 1)))));

    private static XElement Column(int index, double width) =>
        new(
            SpreadsheetNs + "col",
            new XAttribute("min", index),
            new XAttribute("max", index),
            new XAttribute("width", width.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static XElement Row(int rowNumber, params (string Column, string Value, int Style)[] cells) =>
        new(
            SpreadsheetNs + "row",
            new XAttribute("r", rowNumber),
            cells.Select(cell => InlineStringCell($"{cell.Column}{rowNumber}", cell.Value, cell.Style)));

    private static XElement InlineStringCell(string reference, string value, int style) =>
        new(
            SpreadsheetNs + "c",
            new XAttribute("r", reference),
            new XAttribute("t", "inlineStr"),
            new XAttribute("s", style),
            new XElement(SpreadsheetNs + "is", new XElement(SpreadsheetNs + "t", value)));

    private static void WriteXml(ZipArchive archive, string path, XDocument document)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using Stream stream = entry.Open();
        document.Save(stream);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
