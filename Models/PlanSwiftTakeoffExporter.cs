using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace OurPlaneCore;

public enum PlanSwiftExportRowKind
{
    Header,
    Item,
    Note,
    Blank,
}

public sealed record PlanSwiftExportRow(PlanSwiftExportRowKind Kind, string Name, string Value = "", string Unit = "");

public static class PlanSwiftTakeoffExporter
{
    private const int HeaderLineLength = 60;
    private const int ExcelStartRow = 10;
    private const int ExcelStartColumn = 10; // J
    private static readonly string[] HiddenImportNotePrefixes =
    [
        "Imported from PlanSwift:",
        "Imported from PlanSwift Segment Section:",
        "Imported generated PlanSwift Segment geometry from ",
        "Imported from PDF takeoff:",
        "Imported from PDF takeoff annotations:",
        "PDF page:",
        "Annotation:",
        "Subtype:",
        "Content:",
    ];

    public static IReadOnlyList<PlanSwiftExportRow> BuildRows(
        OurPlaneCoreJob job,
        IReadOnlyList<TakeoffItem> takeoffItems,
        IReadOnlyList<string> selectedRoots,
        UnitMode unitMode)
    {
        var itemByFolder = takeoffItems
            .Where(item => !string.IsNullOrWhiteSpace(item.FolderPath))
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var roots = NormalizeRoots(job, selectedRoots);
        var rows = new List<PlanSwiftExportRow>();
        if (roots.Count > 0 && roots.All(root => TryGetItem(itemByFolder, root, out _)))
        {
            EmitSelectedItemGroups(
                rows,
                job,
                roots
                    .Select(root => itemByFolder[NormalizePath(root)])
                    .ToList(),
                unitMode);
        }
        else
        {
            foreach (string root in roots)
            {
                if (TryGetItem(itemByFolder, root, out TakeoffItem? item))
                {
                    EmitSingleItem(rows, job, item!, unitMode);
                    continue;
                }

                ProcessFolder(rows, job, root, itemByFolder, unitMode, isRoot: IsSamePath(root, job.TakeoffsRoot));
            }
        }

        while (rows.Count > 0 && rows[^1].Kind == PlanSwiftExportRowKind.Blank)
            rows.RemoveAt(rows.Count - 1);

        return rows;
    }

    public static void WriteTxt(string path, IReadOnlyList<PlanSwiftExportRow> rows)
    {
        var sb = new StringBuilder();
        string separator = new('=', HeaderLineLength);
        foreach (PlanSwiftExportRow row in rows)
        {
            switch (row.Kind)
            {
                case PlanSwiftExportRowKind.Header:
                    sb.AppendLine(separator);
                    sb.AppendLine(SanitizeCell(row.Name));
                    sb.AppendLine(separator);
                    break;
                case PlanSwiftExportRowKind.Item:
                    sb.AppendLine($"{SanitizeCell(row.Name)}\t{SanitizeCell(row.Value)}\t{SanitizeCell(row.Unit)}");
                    break;
                case PlanSwiftExportRowKind.Note:
                    sb.AppendLine(SanitizeCell(row.Name));
                    break;
                case PlanSwiftExportRowKind.Blank:
                    sb.AppendLine();
                    break;
            }
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static int WriteXlsx(string path, IReadOnlyList<PlanSwiftExportRow> rows)
    {
        if (File.Exists(path))
            File.Delete(path);

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml",
            """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
</Types>
""");
        WriteEntry(archive, "_rels/.rels",
            """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""");
        WriteEntry(archive, "xl/workbook.xml",
            """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
    <sheet name="Takeoffs" sheetId="1" r:id="rId1"/>
  </sheets>
</workbook>
""");
        WriteEntry(archive, "xl/_rels/workbook.xml.rels",
            """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
</Relationships>
""");
        WriteEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(rows));

        return rows.Count(row => row.Kind != PlanSwiftExportRowKind.Blank);
    }

    private static void EmitSingleItem(List<PlanSwiftExportRow> rows, OurPlaneCoreJob job, TakeoffItem item, UnitMode unitMode)
    {
        string parent = Path.GetDirectoryName(item.FolderPath) ?? job.TakeoffsRoot;
        rows.Add(new PlanSwiftExportRow(PlanSwiftExportRowKind.Header, GroupTitle(job, parent)));
        EmitItem(rows, item, unitMode);
        rows.Add(new PlanSwiftExportRow(PlanSwiftExportRowKind.Blank, ""));
    }

    private static void EmitSelectedItemGroups(
        List<PlanSwiftExportRow> rows,
        OurPlaneCoreJob job,
        IReadOnlyList<TakeoffItem> selectedItems,
        UnitMode unitMode)
    {
        var groups = selectedItems
            .Select(item => new
            {
                Parent = Path.GetDirectoryName(item.FolderPath) ?? job.TakeoffsRoot,
                Item = item,
            })
            .GroupBy(entry => NormalizePath(entry.Parent), StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            string parent = group.First().Parent;
            rows.Add(new PlanSwiftExportRow(PlanSwiftExportRowKind.Header, GroupTitle(job, parent)));
            foreach (TakeoffItem item in SortItemsForFolder(job, parent, group.Select(entry => entry.Item).ToList()))
                EmitItem(rows, item, unitMode);
            rows.Add(new PlanSwiftExportRow(PlanSwiftExportRowKind.Blank, ""));
        }
    }

    private static void ProcessFolder(
        List<PlanSwiftExportRow> rows,
        OurPlaneCoreJob job,
        string folder,
        IReadOnlyDictionary<string, TakeoffItem> itemByFolder,
        UnitMode unitMode,
        bool isRoot)
    {
        if (!Directory.Exists(folder) || !FolderHasMeasuredItems(folder, itemByFolder))
            return;

        var children = OurPlaneCoreJobStore.GetOrderedChildDirectories(folder);
        var items = children
            .Select(child => TryGetItem(itemByFolder, child, out TakeoffItem? item) ? item : null)
            .Where(item => item is { Measurements.Count: > 0 })
            .Cast<TakeoffItem>()
            .ToList();

        if (items.Count > 0)
        {
            rows.Add(new PlanSwiftExportRow(PlanSwiftExportRowKind.Header, GroupTitle(job, isRoot ? "" : folder)));
            foreach (TakeoffItem item in SortItemsForFolder(job, folder, items))
                EmitItem(rows, item, unitMode);
            rows.Add(new PlanSwiftExportRow(PlanSwiftExportRowKind.Blank, ""));
        }

        foreach (string child in children)
        {
            if (TryGetItem(itemByFolder, child, out _))
                continue;

            ProcessFolder(rows, job, child, itemByFolder, unitMode, isRoot: false);
        }
    }

    private static void EmitItem(List<PlanSwiftExportRow> rows, TakeoffItem item, UnitMode unitMode)
    {
        var noteLines = ExportNotes(item).ToList();
        if (item.IsJoistArea)
        {
            var joistLabelLines = JoistLabelLines(item, 0, unitMode).ToList();
            if (joistLabelLines.Count > 0)
            {
                rows.Add(new PlanSwiftExportRow(PlanSwiftExportRowKind.Item, item.Name, joistLabelLines[0]));
                foreach (string line in joistLabelLines.Skip(1))
                    rows.Add(string.IsNullOrWhiteSpace(line)
                        ? new PlanSwiftExportRow(PlanSwiftExportRowKind.Blank, "")
                        : new PlanSwiftExportRow(PlanSwiftExportRowKind.Note, line));

                foreach (string line in noteLines)
                    rows.Add(new PlanSwiftExportRow(PlanSwiftExportRowKind.Note, line));

                if (joistLabelLines.Count > 1 || noteLines.Count > 0)
                    rows.Add(new PlanSwiftExportRow(PlanSwiftExportRowKind.Blank, ""));
                return;
            }
        }

        var (value, unit) = QuantityValueAndUnit(item, unitMode);
        rows.Add(new PlanSwiftExportRow(PlanSwiftExportRowKind.Item, item.Name, value, unit));

        foreach (string line in noteLines)
            rows.Add(new PlanSwiftExportRow(PlanSwiftExportRowKind.Note, line));

        if (noteLines.Count > 0)
            rows.Add(new PlanSwiftExportRow(PlanSwiftExportRowKind.Blank, ""));
    }

    public static string CleanExportNotes(string notes) =>
        string.Join(Environment.NewLine, SplitExportNoteLines(notes));

    private static IEnumerable<string> ExportNotes(TakeoffItem item)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in SplitExportNoteLines(item.Notes))
            if (seen.Add(line))
                yield return line;

        foreach (Measurement measurement in item.Measurements)
            foreach (string line in SplitExportNoteLines(measurement.Notes))
                if (seen.Add(line))
                    yield return line;
    }

    private static IEnumerable<string> SplitExportNoteLines(string notes) =>
        SplitNoteLines(notes).Where(line => !IsHiddenImportNoteLine(line));

    private static bool IsHiddenImportNoteLine(string line) =>
        HiddenImportNotePrefixes.Any(prefix => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> SplitNoteLines(string notes) =>
        (notes ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line));

    public static IReadOnlyList<string> JoistLabelLines(
        TakeoffItem item,
        double fallbackScaleMetersPerPt,
        UnitMode unitMode)
    {
        if (!item.IsJoistArea)
            return [];

        var lines = new List<string>();
        foreach (Measurement measurement in item.Measurements)
        {
            if (OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) != "area" ||
                !measurement.JoistEnabled)
            {
                continue;
            }

            if (lines.Count > 0)
                lines.Add("");

            lines.AddRange(SplitLabelLines(measurement.Label(fallbackScaleMetersPerPt, unitMode)));
        }

        return lines;
    }

    public static string JoistLabelText(
        TakeoffItem item,
        double fallbackScaleMetersPerPt,
        UnitMode unitMode) =>
        string.Join("\n", JoistLabelLines(item, fallbackScaleMetersPerPt, unitMode));

    private static IEnumerable<string> SplitLabelLines(string label) =>
        (label ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static (string Value, string Unit) QuantityValueAndUnit(TakeoffItem item, UnitMode unitMode)
    {
        string type = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double totalMeters = item.Total(0);
        if (item.IsJoistArea)
            return unitMode == UnitMode.Imperial
                ? (FormatExportNumber(totalMeters / 0.3048), "FT")
                : (FormatExportNumber(totalMeters), "M");

        return type switch
        {
            "point" => (item.Measurements.Sum(m => m.Points.Count).ToString(CultureInfo.InvariantCulture), "EA"),
            "area" when unitMode == UnitMode.Imperial => (FormatExportNumber(totalMeters / 0.0929030), "SF"),
            "area" => (FormatExportNumber(totalMeters), "SM"),
            "line" when unitMode == UnitMode.Imperial => (FormatExportNumber(totalMeters / 0.3048), "FT"),
            "line" => (FormatExportNumber(totalMeters), "M"),
            _ => (FormatExportNumber(totalMeters), ""),
        };
    }

    private static string FormatExportNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "";

        string formatted = Math.Abs(value - Math.Round(value)) < 0.005
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
        return formatted.Replace('.', ',');
    }

    private static bool FolderHasMeasuredItems(string folder, IReadOnlyDictionary<string, TakeoffItem> itemByFolder)
    {
        foreach (string child in OurPlaneCoreJobStore.GetOrderedChildDirectories(folder))
        {
            if (TryGetItem(itemByFolder, child, out TakeoffItem? item))
            {
                if (item!.Measurements.Count > 0)
                    return true;
                continue;
            }

            if (FolderHasMeasuredItems(child, itemByFolder))
                return true;
        }

        return false;
    }

    private static IReadOnlyList<string> NormalizeRoots(OurPlaneCoreJob job, IReadOnlyList<string> selectedRoots)
    {
        var rawRoots = selectedRoots.Count == 0 ? [job.TakeoffsRoot] : selectedRoots;
        var valid = rawRoots
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(NormalizePath)
            .Where(path => OurPlaneCoreJobStore.IsSameOrDescendant(job.TakeoffsRoot, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Length)
            .ToList();

        var result = new List<string>();
        foreach (string root in valid)
        {
            if (result.Any(parent => OurPlaneCoreJobStore.IsSameOrDescendant(parent, root)))
                continue;
            result.Add(root);
        }

        return result.Count == 0 ? [job.TakeoffsRoot] : result;
    }

    private static IEnumerable<TakeoffItem> SortItemsForFolder(OurPlaneCoreJob job, string folder, IReadOnlyList<TakeoffItem> items)
    {
        string relative = RelativeTakeoffPath(job, folder);
        if (!IsWallFloorFolder(relative))
            return items;

        return items.OrderBy(WallItemSortKey).ToList();
    }

    private static string GroupTitle(OurPlaneCoreJob job, string folder)
    {
        string relative = string.IsNullOrWhiteSpace(folder) || IsSamePath(folder, job.TakeoffsRoot)
            ? ""
            : RelativeTakeoffPath(job, folder);
        if (string.IsNullOrWhiteSpace(relative))
            return "(root)";

        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2 && string.Equals(parts[0], "walls", StringComparison.OrdinalIgnoreCase))
        {
            string floor = Regex.Replace(parts[1], @"\s+walls\s*$", "", RegexOptions.IgnoreCase).Trim();
            return $"walls - {floor}";
        }

        if (parts.Length >= 2 && string.Equals(parts[0], "framing", StringComparison.OrdinalIgnoreCase))
        {
            string floor = Regex.Replace(parts[1], @"\s+framing\s*$", "", RegexOptions.IgnoreCase).Trim();
            return $"framing - {floor}";
        }

        return string.Join(" - ", parts);
    }

    private static bool IsWallFloorFolder(string relative)
    {
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 && string.Equals(parts[0], "walls", StringComparison.OrdinalIgnoreCase);
    }

    private static (int Group, int Rank, string Name) WallItemSortKey(TakeoffItem item)
    {
        string name = item.Name.Trim();
        string low = name.ToLowerInvariant();
        var tokens = Regex.Matches(low, @"[a-z0-9]+(?:x[0-9]+)?")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (Regex.IsMatch(low, @"^\s*corners?\b", RegexOptions.IgnoreCase))
            return (0, 0, low);
        if (tokens.Contains("ext"))
            return (1, -LastNumber(name), low);
        if ((tokens.Contains("cor") || tokens.Contains("corr")) &&
            !Regex.IsMatch(low, @"^\s*corners?\b", RegexOptions.IgnoreCase))
            return (2, 0, low);
        if (tokens.Contains("dem"))
            return (3, 0, low);

        string[] studs = ["2x4", "2x6", "2x8"];
        for (int i = 0; i < studs.Length; i++)
            if (tokens.Contains(studs[i]))
                return (4, i, low);

        if (tokens.Contains("half"))
            return (5, 0, low);
        return (9, 0, low);
    }

    private static int LastNumber(string name)
    {
        var matches = Regex.Matches(name, @"\d+");
        return matches.Count == 0 ? -1 : int.Parse(matches[^1].Value, CultureInfo.InvariantCulture);
    }

    private static string RelativeTakeoffPath(OurPlaneCoreJob job, string folder)
    {
        try
        {
            return Path.GetRelativePath(job.TakeoffsRoot, folder).Replace('\\', '/');
        }
        catch
        {
            return OurPlaneCoreJobStore.DisplayName(folder);
        }
    }

    private static bool TryGetItem(
        IReadOnlyDictionary<string, TakeoffItem> itemByFolder,
        string folder,
        out TakeoffItem? item) =>
        itemByFolder.TryGetValue(NormalizePath(folder), out item);

    private static string BuildWorksheetXml(IReadOnlyList<PlanSwiftExportRow> rows)
    {
        var sb = new StringBuilder();
        int lastRow = Math.Max(ExcelStartRow, ExcelStartRow + rows.Count - 1);
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.AppendLine($"""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><dimension ref="J{ExcelStartRow}:L{lastRow}"/><sheetViews><sheetView workbookViewId="0"/></sheetViews><sheetFormatPr defaultRowHeight="15"/><sheetData>""");

        int rowIndex = ExcelStartRow;
        foreach (PlanSwiftExportRow row in rows)
        {
            sb.Append($"""<row r="{rowIndex}">""");
            if (row.Kind != PlanSwiftExportRowKind.Blank)
            {
                WriteCell(sb, rowIndex, ExcelStartColumn, row.Name, forceString: row.Kind != PlanSwiftExportRowKind.Item);
                if (row.Kind == PlanSwiftExportRowKind.Item)
                {
                    WriteCell(sb, rowIndex, ExcelStartColumn + 1, row.Value, forceString: false);
                    WriteCell(sb, rowIndex, ExcelStartColumn + 2, row.Unit, forceString: true);
                }
            }

            sb.AppendLine("</row>");
            rowIndex++;
        }

        sb.AppendLine("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void WriteCell(StringBuilder sb, int row, int column, string value, bool forceString)
    {
        string reference = $"{ColumnName(column)}{row}";
        if (!forceString && TryParseExportNumber(value, out double number))
        {
            sb.Append($"""<c r="{reference}"><v>{number.ToString("G17", CultureInfo.InvariantCulture)}</v></c>""");
            return;
        }

        sb.Append($"""<c r="{reference}" t="inlineStr"><is><t>{XmlEscape(value)}</t></is></c>""");
    }

    private static bool TryParseExportNumber(string value, out double number)
    {
        string clean = (value ?? "").Trim().Replace(" ", "", StringComparison.Ordinal).Replace(',', '.');
        return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static string ColumnName(int column)
    {
        var chars = new Stack<char>();
        int value = column;
        while (value > 0)
        {
            value--;
            chars.Push((char)('A' + value % 26));
            value /= 26;
        }
        return new string(chars.ToArray());
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string SanitizeCell(string value) =>
        (value ?? "").Replace('\t', ' ').Trim();

    private static string XmlEscape(string value) =>
        (value ?? "")
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static bool IsSamePath(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
}
