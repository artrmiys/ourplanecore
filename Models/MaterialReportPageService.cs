using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlanCore;

public sealed record MaterialReportPageResult(
    MaterialExtractionResult Extraction,
    IReadOnlyList<PageInfo> ReportPages,
    string ReportPdfPath,
    string RowsCsvPath,
    string SummaryCsvPath);

public sealed record MaterialReportDisplaySection(
    string Title,
    IReadOnlyList<string> Lines);

public static class MaterialReportPageService
{
    private const float PageWidth = 792f;
    private const float PageHeight = 612f;
    private const float Margin = 42f;
    private const float BodySize = 9.5f;
    private const float SmallSize = 8.2f;

    public static async Task<MaterialReportPageResult> CreateReportPagesAsync(
        OurPlanCoreJob job,
        IReadOnlyList<PageInfo> sourcePages,
        string destinationFolder,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> pdfPaths = MaterialExtractionService.UniqueSourcePdfs(sourcePages);
        MaterialExtractionRunResult extraction = await MaterialExtractionService
            .ExtractAsync(job, pdfPaths, cancellationToken)
            .ConfigureAwait(false);

        string reportPdfPath = Path.Combine(MaterialExtractionService.OutputFolder(job), "materials_report.pdf");
        int pageCount = WriteReportPdf(extraction.Result, reportPdfPath);
        string reportNoteText = BuildReportNoteText(extraction.Result);
        string reportNoteTextPath = Path.Combine(MaterialExtractionService.OutputFolder(job), "materials_report_note.txt");
        File.WriteAllText(reportNoteTextPath, reportNoteText);
        string[] pageNames = Enumerable.Range(1, pageCount)
            .Select(index => index == 1 ? "Materials Report" : $"Materials Report {index.ToString(CultureInfo.InvariantCulture)}")
            .ToArray();

        IReadOnlyList<PageInfo> reportPages = OurPlanCoreJobStore.ImportPdf(
            job,
            reportPdfPath,
            pageNames,
            destinationFolder);
        if (reportPages.Count > 0)
            PageAnnotationStore.SavePageAnnotations(
                reportPages[0].FolderPath,
                [BuildCopyableReportNoteAnnotation(reportPages[0], reportNoteText)]);

        return new MaterialReportPageResult(
            extraction.Result,
            reportPages,
            reportPdfPath,
            extraction.RowsCsvPath,
            extraction.SummaryCsvPath);
    }

    public static int WriteReportPdf(MaterialExtractionResult result, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using SKDocument document = SKDocument.CreatePdf(stream);
        using var writer = new ReportPdfWriter(document, result);
        writer.Write();
        return writer.PageCount;
    }

    public static IReadOnlyList<MaterialReportDisplaySection> BuildFirstPageSections(MaterialExtractionResult result)
    {
        string[] orderedTitles =
        [
            "Wall sheathing",
            "Floor sheathing",
            "Roof sheathing",
        ];

        var linesBySection = orderedTitles.ToDictionary(
            title => title,
            _ => new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (MaterialExtractionRow row in result.Rows)
        {
            if (!ShouldIncludeOnFirstPage(row))
                continue;

            string material = FormatMaterialDescription(row);
            if (string.IsNullOrWhiteSpace(material))
                continue;

            string section = ClassifyDisplaySection(row);
            if (!linesBySection.ContainsKey(section))
                continue;

            string line = FormatMaterialScheduleLine(row, material);
            string sortKey = $"{DisplayPageSortKey(row)}|{row.SectionRef}|{material}|{line}";
            linesBySection[section].TryAdd(sortKey, line);
        }

        return orderedTitles
            .Select(title => new MaterialReportDisplaySection(title, linesBySection[title].Values.ToList()))
            .ToList();
    }

    public static string BuildReportNoteText(MaterialExtractionResult result)
    {
        var lines = new List<string>
        {
            "Material Takeoff",
        };
        if (!string.IsNullOrWhiteSpace(result.JobName))
            lines.Add($"Job: {CleanInline(result.JobName)}");
        lines.Add("");

        bool wroteMaterials = false;
        foreach (MaterialReportDisplaySection section in BuildFirstPageSections(result).Where(section => section.Lines.Count > 0))
        {
            wroteMaterials = true;
            lines.Add($"{section.Title}:");
            lines.AddRange(section.Lines);
            lines.Add("");
        }

        IReadOnlyList<MaterialReportDisplaySection> scheduleLegends = BuildScheduleLegendSections(result);
        if (scheduleLegends.Count > 0)
        {
            lines.Add("Schedule legends:");
            foreach (MaterialReportDisplaySection section in scheduleLegends)
            {
                lines.Add($"{section.Title}:");
                lines.AddRange(section.Lines);
                lines.Add("");
            }
        }

        if (!wroteMaterials && scheduleLegends.Count == 0)
            lines.Add("No clean sheathing material rows found.");

        return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
    }

    public static PageAnnotation BuildCopyableReportNoteAnnotation(PageInfo reportPage, string noteText)
    {
        return new PageAnnotation
        {
            Kind = "note",
            Text = noteText.Trim(),
            Color = "#F9A825",
            PageFolder = reportPage.FolderPath,
            ScaleMetersPerPt = reportPage.ScaleMetersPerPt,
            Points =
            [
                new SKPoint(30, 30),
                new SKPoint(PageWidth - 30, 30),
                new SKPoint(PageWidth - 30, PageHeight - 30),
                new SKPoint(30, PageHeight - 30),
            ],
        };
    }

    public static IReadOnlyList<MaterialReportDisplaySection> BuildScheduleLegendSections(MaterialExtractionResult result)
    {
        var sections = new List<MaterialReportDisplaySection>();
        foreach (MaterialSchedule schedule in result.Schedules
                     .OrderBy(schedule => schedule.Title ?? schedule.ScheduleType)
                     .ThenBy(schedule => schedule.Sheet)
                     .ThenBy(schedule => schedule.PdfPage))
        {
            var lines = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (MaterialScheduleRow row in schedule.Rows)
            {
                if (!ShouldIncludeScheduleLegendRow(row))
                    continue;

                string line = FormatScheduleLegendLine(row);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string mark = CleanInline(row.Mark ?? row.Type ?? "");
                string sortKey = $"{mark}|{line}";
                lines.TryAdd(sortKey, line);
            }

            if (lines.Count == 0)
                continue;

            sections.Add(new MaterialReportDisplaySection(
                FormatScheduleLegendTitle(schedule),
                lines.Values.ToList()));
        }

        return sections;
    }

    private sealed class ReportPdfWriter : IDisposable
    {
        private readonly SKDocument _document;
        private readonly MaterialExtractionResult _result;
        private readonly SKPaint _titlePaint;
        private readonly SKPaint _headerPaint;
        private readonly SKPaint _bodyPaint;
        private readonly SKPaint _smallPaint;
        private SKCanvas? _canvas;
        private float _y;

        public ReportPdfWriter(SKDocument document, MaterialExtractionResult result)
        {
            _document = document;
            _result = result;
            SKTypeface typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
            _titlePaint = Paint(18, true, typeface);
            _headerPaint = Paint(12, true, typeface);
            _bodyPaint = Paint(BodySize, false, typeface);
            _smallPaint = Paint(SmallSize, false, typeface);
        }

        public int PageCount { get; private set; }

        public void Write()
        {
            BeginPage();
            WriteFirstPageMaterials();
            EndPage();
        }

        public void Dispose()
        {
            _titlePaint.Dispose();
            _headerPaint.Dispose();
            _bodyPaint.Dispose();
            _smallPaint.Dispose();
        }

        private void WriteFirstPageMaterials()
        {
            Line("Material Takeoff", _titlePaint, 18);
            Line($"Job: {_result.JobName ?? ""}", _smallPaint);
            Space(8);

            bool wroteMaterials = false;
            foreach (MaterialReportDisplaySection section in BuildFirstPageSections(_result).Where(section => section.Lines.Count > 0))
            {
                wroteMaterials = true;
                Header($"{section.Title}:");
                foreach (string line in section.Lines)
                    Line(line, _bodyPaint, 3);
                Space(5);
            }

            IReadOnlyList<MaterialReportDisplaySection> scheduleLegends = BuildScheduleLegendSections(_result);
            if (scheduleLegends.Count == 0)
            {
                if (!wroteMaterials)
                    Line("No clean sheathing material rows found.", _bodyPaint, 3);
                return;
            }

            Space(4);
            Header("Schedule legends");
            foreach (MaterialReportDisplaySection section in scheduleLegends)
            {
                Header($"{section.Title}:");
                foreach (string line in section.Lines)
                    Line(line, _bodyPaint, 3);
                Space(5);
            }
        }

        private void WriteQuality()
        {
            MaterialQualitySummary quality = _result.Quality;
            Header("Quality");
            Line(
                $"Rows {_result.Rows.Count} | Ready {quality.TakeoffReadyRows} | High {quality.HighConfidenceRows} | " +
                $"Review {quality.ReviewRows} | Schedules {_result.Schedules.Count} | OCR pages {_result.Stats.PagesOcr}",
                _bodyPaint);
            Line(
                $"Tables: {(quality.PdfPlumberAvailable ? "on" : "fallback")} | " +
                $"OCR: {(quality.OcrAvailable ? "on" : "off")} | Blank without OCR: {quality.BlankPagesWithoutOcr}",
                _smallPaint);
            Space(8);
        }

        private void WriteInputs()
        {
            Header("Input PDFs");
            foreach (MaterialInputFile input in _result.InputFiles)
                Line($"- {input.PdfName} ({input.TotalPages} pages)", _smallPaint);
            Space(8);
        }

        private void WriteSchedules()
        {
            Header("Detected Schedules");
            var schedules = _result.Schedules
                .GroupBy(schedule => schedule.Title ?? schedule.ScheduleType ?? "Marked Table")
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (schedules.Count == 0)
            {
                Line("- none", _smallPaint);
                Space(8);
                return;
            }

            foreach (var group in schedules)
                Line($"- {group.Key}: {group.Count()} page(s)", _smallPaint);
            Space(8);
        }

        private void WriteMaterials()
        {
            Header("Material Summary");
            IReadOnlyList<MaterialSummaryRow> summaries = MaterialExtractionService.BuildSummaryRows(_result)
                .OrderBy(row => row.Category)
                .ThenBy(row => row.MaterialFamily)
                .ThenBy(row => row.Size)
                .ToList();
            if (summaries.Count == 0)
            {
                Line("- no material rows found", _bodyPaint);
                return;
            }

            string currentCategory = "";
            foreach (MaterialSummaryRow row in summaries)
            {
                string category = row.Category ?? "Uncategorized";
                if (!string.Equals(currentCategory, category, StringComparison.Ordinal))
                {
                    currentCategory = category;
                    Space(3);
                    Header(category);
                }

                string material = row.MaterialFamily ?? "Unclassified";
                string size = string.IsNullOrWhiteSpace(row.Size) ? "" : $" | size {row.Size}";
                string treatment = string.IsNullOrWhiteSpace(row.Treatment) ? "" : $" | {row.Treatment}";
                string sheets = row.Sheets.Count == 0 ? "" : $" | sheets {string.Join(", ", row.Sheets.Take(8))}";
                string flags = row.ReviewFlags.Count == 0 ? "" : $" | review {string.Join(", ", row.ReviewFlags.Take(3))}";
                Line($"- {material}{size}{treatment} | evidence {row.EvidenceCount}{sheets}{flags}", _smallPaint);
            }
        }

        private void Header(string text)
        {
            EnsureSpace(22);
            Line(text, _headerPaint, 16);
        }

        private void Line(string text, SKPaint paint, float extraAfter = 3)
        {
            foreach (string line in Wrap(text, paint, PageWidth - Margin * 2))
            {
                EnsureSpace(paint.TextSize * 1.35f + extraAfter);
                _canvas!.DrawText(line, Margin, _y, paint);
                _y += paint.TextSize * 1.25f;
            }
            _y += extraAfter;
        }

        private void Space(float amount)
        {
            EnsureSpace(amount);
            _y += amount;
        }

        private void EnsureSpace(float needed)
        {
            if (_canvas == null || _y + needed <= PageHeight - Margin)
                return;

            EndPage();
            BeginPage();
        }

        private void BeginPage()
        {
            _canvas = _document.BeginPage(PageWidth, PageHeight);
            PageCount++;
            _canvas.Clear(SKColors.White);
            _y = Margin;
        }

        private void EndPage()
        {
            if (_canvas == null)
                return;

            _document.EndPage();
            _canvas = null;
        }

        private static SKPaint Paint(float size, bool bold, SKTypeface typeface) => new()
        {
            Color = SKColors.Black,
            IsAntialias = true,
            TextSize = size,
            Typeface = typeface,
            FakeBoldText = bold,
        };

        private static IReadOnlyList<string> Wrap(string text, SKPaint paint, float maxWidth)
        {
            var lines = new List<string>();
            foreach (string sourceLine in (text ?? "").Split('\n'))
            {
                string current = "";
                foreach (string word in sourceLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string candidate = current.Length == 0 ? word : $"{current} {word}";
                    if (paint.MeasureText(candidate) <= maxWidth || current.Length == 0)
                    {
                        current = candidate;
                        continue;
                    }

                    lines.Add(current);
                    current = word;
                }
                lines.Add(current);
            }
            return lines;
        }
    }

    private static string ClassifyDisplaySection(MaterialExtractionRow row)
    {
        string text = SearchText(row);
        bool wall = ContainsAny(text, "WALL", "EXTERIOR", "SHEAR");
        bool floor = ContainsAny(text, "FLOOR", "SUBFLOOR", "T&G", "T & G", "TONGUE AND GROOVE");
        bool roof = ContainsAny(text, "ROOF", "RAFTER", "TRUSS", "DENSROCK");
        bool sheathing = ContainsAny(text, "SHEATHING", "PLY", "PLYWOOD", "OSB", "CDX", "ZIP", "DENSGLASS", "DENSGLAS");
        bool insulation = ContainsAny(text, "INSULATION");

        if (floor || EqualsAny(row.MaterialFamily, "Subfloor"))
            return "Floor sheathing";
        if (roof && (sheathing || insulation || ContainsAny(text, "COVER BOARD")))
            return "Roof sheathing";
        if (wall && (sheathing || insulation || ContainsAny(text, "WRB", "TYVEK", "WEATHER BARRIER", "FLASHING")))
            return "Wall sheathing";
        if (sheathing || insulation)
            return "Wall sheathing";
        if (ContainsAny(text, "SIDING", "HARDIE", "FIBER CEMENT", "CLADDING", "CLAPBOARD"))
            return "Siding / exterior finish";
        if (ContainsAny(text, "TRIM", "SOFFIT", "FASCIA", "FRIEZE", "WRB", "TYVEK", "FLASHING", "VAPOR BARRIER"))
            return "Exterior trim / WRB / flashing";
        if (ContainsAny(text, "SHINGLE", "FELT", "EPDM", "MEMBRANE", "STANDING SEAM", "ICE AND WATER"))
            return "Roofing";
        if (ContainsAny(text, "BEAM", "HEADER", "JOIST", "RAFTER", "BLOCKING", "STUD", "PLATE", "POST", "COLUMN", "LVL", "PSL", "LSL"))
            return "Framing / blocking";
        if (ContainsAny(text, "HANGER", "HOLDOWN", "FASTENER", "SCREW", "BOLT", "ANCHOR", "LUS", "HUC", "HDU", "SDS", "SDWS"))
            return "Hardware / fasteners";
        if (ContainsAny(text, "GYPSUM", "GWB", "DRYWALL", "TYPE X"))
            return "Interior / drywall";

        return "Other materials";
    }

    private static bool ShouldIncludeOnFirstPage(MaterialExtractionRow row)
    {
        string text = SearchText(row);
        if (row.Confidence > 0 && row.Confidence < 0.65)
            return false;

        if (row.ReviewFlags.Any(flag => flag.Contains("general_note", StringComparison.OrdinalIgnoreCase)) &&
            !HasConcreteMaterialEvidence(row))
        {
            return false;
        }

        string section = ClassifyDisplaySection(row);
        if (section is "Wall sheathing" or "Floor sheathing" or "Roof sheathing")
            return HasConcreteMaterialEvidence(row) && HasSpecificMaterialDescription(row);

        return false;
    }

    private static string FormatMaterialScheduleLine(MaterialExtractionRow row, string material)
    {
        string page = DisplayPage(row);
        string detail = CleanInline(row.SectionRef ?? "");
        if (string.IsNullOrWhiteSpace(detail))
            detail = CleanInline(row.PageRef ?? "");

        string detailPart = string.IsNullOrWhiteSpace(detail) ? "" : $" - detail {detail}";
        return $"{page}{detailPart} - {material}";
    }

    private static string DisplayPage(MaterialExtractionRow row)
    {
        string sheet = CleanInline(row.Sheet ?? "");
        if (!string.IsNullOrWhiteSpace(sheet))
            return $"page {sheet}";

        if (row.PdfPage > 0)
            return $"pdf page {row.PdfPage.ToString(CultureInfo.InvariantCulture)}";

        return "page unknown";
    }

    private static string DisplayPageSortKey(MaterialExtractionRow row)
    {
        string sheet = CleanInline(row.Sheet ?? "");
        if (!string.IsNullOrWhiteSpace(sheet))
            return sheet;

        return row.PdfPage.ToString("0000", CultureInfo.InvariantCulture);
    }

    private static string FormatMaterialDescription(MaterialExtractionRow row)
    {
        string family = FriendlyFamily(row);
        string dimension = CleanInline(row.Thickness ?? "");
        if (string.IsNullOrWhiteSpace(dimension))
            dimension = CleanInline(row.Size ?? "");

        if (!string.IsNullOrWhiteSpace(dimension) &&
            !string.IsNullOrWhiteSpace(family) &&
            !DescriptionContains(family, dimension))
        {
            return string.IsNullOrWhiteSpace(family) ? dimension : $"{dimension} {family}";
        }

        if (!string.IsNullOrWhiteSpace(family))
            return family;

        return "";
    }

    private static string FormatScheduleLegendTitle(MaterialSchedule schedule)
    {
        string title = CleanInline(schedule.Title ?? schedule.ScheduleType ?? "Marked schedule");
        if (title.Length == 0)
            title = "Marked schedule";

        title = ToSentenceCase(title);
        string sheet = CleanInline(schedule.Sheet ?? "");
        if (!string.IsNullOrWhiteSpace(sheet))
            return $"{title} - page {sheet}";
        if (schedule.PdfPage > 0)
            return $"{title} - pdf page {schedule.PdfPage.ToString(CultureInfo.InvariantCulture)}";

        return title;
    }

    private static string FormatScheduleLegendLine(MaterialScheduleRow row)
    {
        string mark = CleanInline(row.Mark ?? row.Type ?? "");
        string description = FormatScheduleLegendDescription(row, mark);
        if (string.IsNullOrWhiteSpace(mark))
            return "";
        if (string.IsNullOrWhiteSpace(description))
            return "";

        return $"{mark} - {description}";
    }

    private static bool ShouldIncludeScheduleLegendRow(MaterialScheduleRow row)
    {
        string mark = CleanInline(row.Mark ?? row.Type ?? "");
        if (string.IsNullOrWhiteSpace(mark))
            return false;

        if (!string.IsNullOrWhiteSpace(row.Qty) ||
            !string.IsNullOrWhiteSpace(row.Size) ||
            !string.IsNullOrWhiteSpace(row.Material))
        {
            return true;
        }

        return IsUsefulScheduleDescription(row.Item ?? "") ||
               IsUsefulScheduleDescription(row.RawText);
    }

    private static string FormatScheduleLegendDescription(MaterialScheduleRow row, string mark)
    {
        var pieces = new List<string>();
        string qty = FormatScheduleQty(row.Qty);
        if (!string.IsNullOrWhiteSpace(qty))
            pieces.Add(qty);

        string size = CleanInline(row.Size ?? "");
        if (!string.IsNullOrWhiteSpace(size))
            pieces.Add(size);

        string material = CleanInline(row.Material ?? "");
        if (!string.IsNullOrWhiteSpace(material) && !pieces.Any(piece => DescriptionContains(piece, material)))
            pieces.Add(material);

        string description = CleanInline(string.Join(" ", pieces));
        if (!string.IsNullOrWhiteSpace(description))
            return description;

        string item = CleanInline(row.Item ?? "");
        if (!string.IsNullOrWhiteSpace(item) &&
            !string.Equals(item, mark, StringComparison.OrdinalIgnoreCase) &&
            IsUsefulScheduleDescription(item))
        {
            return item;
        }

        string raw = CleanInline(row.RawText);
        if (!string.IsNullOrWhiteSpace(mark) && raw.StartsWith(mark, StringComparison.OrdinalIgnoreCase))
            raw = CleanInline(raw[mark.Length..].TrimStart('-', ':', '|', ' '));

        return IsUsefulScheduleDescription(raw) ? raw : "";
    }

    private static bool IsUsefulScheduleDescription(string value)
    {
        string clean = CleanInline(value ?? "");
        if (string.IsNullOrWhiteSpace(clean))
            return false;

        string text = clean.ToUpperInvariant();
        if (ContainsAny(text, "GENERAL NOTE", "SEE ", "REFER ", "VERIFY", "CONTRACTOR", "TYPICAL", "TYP."))
            return false;

        return HasConcreteMaterialToken(text) || HasSizeToken(text);
    }

    private static string FormatScheduleQty(string? value)
    {
        string qty = CleanInline(value ?? "");
        if (string.IsNullOrWhiteSpace(qty))
            return "";

        if (qty.StartsWith("(", StringComparison.Ordinal) && qty.EndsWith(")", StringComparison.Ordinal))
            return qty;

        return $"({qty})";
    }

    private static string FriendlyFamily(MaterialExtractionRow row)
    {
        string family = CleanInline(row.MaterialFamily ?? row.Material ?? "");
        string text = SearchText(row);

        if (ContainsAny(text, "CDX"))
            return "CDX Ply";
        if (ContainsAny(text, "ZIP"))
            return "Zip";
        if (ContainsAny(text, "OSB"))
            return "OSB";
        if (ContainsAny(text, "PLYWOOD") || ContainsDelimitedToken(text, "PLY"))
            return "Ply";
        if (ContainsAny(text, "T&G", "T & G", "TONGUE AND GROOVE"))
            return "TG";
        if (ContainsAny(text, "INSULATION"))
            return "Insulation";
        if (family.Equals("Plywood", StringComparison.OrdinalIgnoreCase) ||
            family.Equals("CDX Plywood", StringComparison.OrdinalIgnoreCase))
            return "Ply";
        if (family.Equals("ZIP System", StringComparison.OrdinalIgnoreCase))
            return "Zip";
        if (family.Equals("Subfloor", StringComparison.OrdinalIgnoreCase))
            return "Subfloor";
        if (!string.IsNullOrWhiteSpace(family))
            return family;

        return "";
    }

    private static bool HasSpecificMaterialDescription(MaterialExtractionRow row)
    {
        string family = FriendlyFamily(row);
        if (string.IsNullOrWhiteSpace(family))
            return false;

        string dimension = CleanInline(row.Thickness ?? "");
        if (string.IsNullOrWhiteSpace(dimension))
            dimension = CleanInline(row.Size ?? "");

        return !string.IsNullOrWhiteSpace(dimension) || HasConcreteMaterialEvidence(row);
    }

    private static string SearchText(MaterialExtractionRow row)
    {
        return string.Join(
                " ",
                row.Category,
                row.Subcategory,
                row.MaterialFamily,
                row.Material,
                row.Item,
                row.RawText,
                row.ScheduleRef,
                row.SectionRef)
            .ToUpperInvariant();
    }

    private static bool DescriptionContains(string value, string part)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(part))
            return false;

        return value.Contains(part, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsDelimitedToken(string text, string token)
    {
        int index = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            bool before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            int afterIndex = index + token.Length;
            bool after = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);
            if (before && after)
                return true;

            index = text.IndexOf(token, index + token.Length, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool HasSizeToken(string text)
    {
        string compact = text.Replace(" ", "", StringComparison.Ordinal);
        if (compact.Any(char.IsDigit) && compact.Contains('X'))
            return true;

        return ContainsAny(text, "\"", " IN ", " FT ", "MM", "GA");
    }

    private static bool HasConcreteMaterialToken(string text)
    {
        return ContainsAny(
            text,
            "CDX",
            "ZIP",
            "OSB",
            "PLYWOOD",
            "PLY",
            "T&G",
            "T & G",
            "TONGUE AND GROOVE",
            "INSULATION",
            "DENSGLASS",
            "DENSGLAS",
            "DENSROCK",
            "TYVEK",
            "WRB",
            "HARDIE",
            "FIBER CEMENT",
            "LVL",
            "PSL",
            "LSL",
            "GLULAM",
            "LUS",
            "HUC",
            "HDU",
            "SDS",
            "SDWS");
    }

    private static bool HasConcreteMaterialEvidence(MaterialExtractionRow row)
    {
        string evidence = string.Join(" ", row.Item, row.RawText, row.ScheduleRef, row.SectionRef).ToUpperInvariant();
        return HasConcreteMaterialToken(evidence);
    }

    private static bool EqualsAny(string? value, params string[] candidates)
    {
        return candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string CleanInline(string value)
    {
        return string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ToSentenceCase(string value)
    {
        string lower = value.ToLower(CultureInfo.InvariantCulture);
        return lower.Length == 0 ? lower : char.ToUpperInvariant(lower[0]) + lower[1..];
    }
}
