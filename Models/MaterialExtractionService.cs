using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OurPlaneCore;

public sealed record MaterialExtractionRunResult(
    MaterialExtractionResult Result,
    string JsonPath,
    string RowsCsvPath,
    string SummaryCsvPath,
    string OutputFolder,
    string StandardOutput,
    string StandardError);

public static class MaterialExtractionService
{
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromMinutes(10);

    public static string OutputFolder(OurPlaneCoreJob job) =>
        Path.Combine(job.AIContextRoot, "materials");

    public static string LatestJsonPath(OurPlaneCoreJob job) =>
        Path.Combine(OutputFolder(job), "materials_unique_by_page.json");

    public static string LatestRowsCsvPath(OurPlaneCoreJob job) =>
        Path.Combine(OutputFolder(job), "materials_rows.csv");

    public static string LatestSummaryCsvPath(OurPlaneCoreJob job) =>
        Path.Combine(OutputFolder(job), "materials_summary.csv");

    public static IReadOnlyList<string> UniqueSourcePdfs(IEnumerable<PageInfo> pages) =>
        pages
            .Where(page => !IsGeneratedMaterialsReportPage(page))
            .Select(page => page.PdfPath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .GroupBy(NormalizePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static bool IsGeneratedMaterialsReportPage(PageInfo page) =>
        page.Name.StartsWith("Materials Report", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(page.PdfPath).StartsWith("materials_report", StringComparison.OrdinalIgnoreCase);

    public static MaterialExtractionResult? TryLoadLatest(OurPlaneCoreJob job)
    {
        string path = LatestJsonPath(job);
        if (!File.Exists(path))
            return null;

        return Load(path);
    }

    public static MaterialExtractionResult Load(string jsonPath)
    {
        var result = JsonSerializer.Deserialize<MaterialExtractionResult>(
            File.ReadAllText(jsonPath, Encoding.UTF8),
            OurPlaneCoreJobStore.JsonOptions);
        return result ?? new MaterialExtractionResult();
    }

    public static async Task<MaterialExtractionRunResult> ExtractAsync(
        OurPlaneCoreJob job,
        IReadOnlyList<string> pdfPaths,
        CancellationToken cancellationToken = default)
    {
        if (pdfPaths.Count == 0)
            throw new InvalidOperationException("No source PDF files were found in the current job.");

        string helperPath = ResolveHelperPath();
        if (helperPath.Length == 0)
            throw new FileNotFoundException("Material extractor helper was not found.", "Tools\\material_extractor.py");

        string outputFolder = OutputFolder(job);
        Directory.CreateDirectory(outputFolder);

        string jsonPath = LatestJsonPath(job);
        string pythonExecutable = BundledPythonRuntime.ResolveExecutable();
        var psi = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        BundledPythonRuntime.ConfigureEnvironment(psi, pythonExecutable);
        psi.ArgumentList.Add(helperPath);
        foreach (string pdfPath in pdfPaths)
            psi.ArgumentList.Add(pdfPath);
        psi.ArgumentList.Add("--output");
        psi.ArgumentList.Add(jsonPath);
        psi.ArgumentList.Add("--mode");
        psi.ArgumentList.Add("unique_by_page");
        psi.ArgumentList.Add("--job-name");
        psi.ArgumentList.Add(job.Name);

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Could not start python.");

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ExtractionTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("Material extraction timed out.", ex);
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException($"Material extraction failed: {detail.Trim()}");
        }
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException("Material extractor finished without writing JSON.", jsonPath);

        MaterialExtractionResult result = Load(jsonPath);
        (string rowsCsvPath, string summaryCsvPath) = WriteReviewCsvs(result, outputFolder);
        return new MaterialExtractionRunResult(result, jsonPath, rowsCsvPath, summaryCsvPath, outputFolder, stdout, stderr);
    }

    public static (string RowsCsvPath, string SummaryCsvPath) WriteReviewCsvs(
        MaterialExtractionResult result,
        string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        string rowsCsvPath = Path.Combine(outputFolder, "materials_rows.csv");
        string summaryCsvPath = Path.Combine(outputFolder, "materials_summary.csv");
        WriteRowsCsv(result.Rows, rowsCsvPath);
        WriteSummaryCsv(BuildSummaryRows(result), summaryCsvPath);
        return (rowsCsvPath, summaryCsvPath);
    }

    public static IReadOnlyList<MaterialSummaryRow> BuildSummaryRows(MaterialExtractionResult result)
    {
        if (result.MaterialSummaries.Count > 0)
            return result.MaterialSummaries;

        return result.Rows
            .GroupBy(row => new
            {
                row.Category,
                row.MaterialFamily,
                row.Size,
                row.Thickness,
                row.Grade,
                row.Treatment,
                row.Unit,
            })
            .Select(group => new MaterialSummaryRow
            {
                Category = group.Key.Category,
                MaterialFamily = group.Key.MaterialFamily,
                Size = group.Key.Size,
                Thickness = group.Key.Thickness,
                Grade = group.Key.Grade,
                Treatment = group.Key.Treatment,
                Unit = group.Key.Unit,
                EvidenceCount = group.Count(),
                PdfFiles = group.Select(row => row.PdfFile).Where(value => value.Length > 0).Distinct().Order().ToList(),
                Sheets = group.Select(row => row.Sheet ?? "").Where(value => value.Length > 0).Distinct().Order().ToList(),
                Pages = group.Select(row => $"{row.PdfFile}:{row.PdfPage}").Distinct().Order().ToList(),
                ReviewFlags = group.SelectMany(row => row.ReviewFlags).Distinct().Order().ToList(),
                Example = group.Select(row => row.RawText).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            })
            .OrderBy(row => row.Category)
            .ThenBy(row => row.MaterialFamily)
            .ThenBy(row => row.Size)
            .ToList();
    }

    private static void WriteRowsCsv(IEnumerable<MaterialExtractionRow> rows, string path)
    {
        string[] headers =
        [
            "category",
            "material_family",
            "item",
            "size",
            "thickness",
            "grade",
            "treatment",
            "qty",
            "unit",
            "sheet",
            "pdf_page",
            "page_ref",
            "section_ref",
            "schedule_ref",
            "source_type",
            "confidence",
            "review_flags",
            "pdf_file",
            "raw_text",
        ];

        WriteCsv(
            path,
            headers,
            rows.Select(row => new[]
            {
                row.Category,
                row.MaterialFamily ?? "",
                row.Item ?? "",
                row.Size ?? "",
                row.Thickness ?? "",
                row.Grade ?? "",
                row.Treatment ?? "",
                row.Qty ?? "",
                row.Unit ?? "",
                row.Sheet ?? "",
                row.PdfPage.ToString(CultureInfo.InvariantCulture),
                row.PageRef ?? "",
                row.SectionRef ?? "",
                row.ScheduleRef ?? "",
                row.SourceType,
                row.Confidence.ToString("0.00", CultureInfo.InvariantCulture),
                string.Join("; ", row.ReviewFlags),
                row.PdfFile,
                row.RawText,
            }));
    }

    private static void WriteSummaryCsv(IEnumerable<MaterialSummaryRow> rows, string path)
    {
        string[] headers =
        [
            "category",
            "material_family",
            "size",
            "thickness",
            "grade",
            "treatment",
            "unit",
            "evidence_count",
            "sheets",
            "pdf_files",
            "review_flags",
            "example",
        ];

        WriteCsv(
            path,
            headers,
            rows.Select(row => new[]
            {
                row.Category ?? "",
                row.MaterialFamily ?? "",
                row.Size ?? "",
                row.Thickness ?? "",
                row.Grade ?? "",
                row.Treatment ?? "",
                row.Unit ?? "",
                row.EvidenceCount.ToString(CultureInfo.InvariantCulture),
                string.Join("; ", row.Sheets),
                string.Join("; ", row.PdfFiles),
                string.Join("; ", row.ReviewFlags),
                row.Example ?? "",
            }));
    }

    private static void WriteCsv(string path, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(CsvCell)));
        foreach (IReadOnlyList<string> row in rows)
            sb.AppendLine(string.Join(",", row.Select(CsvCell)));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string CsvCell(string? value)
    {
        string text = value ?? "";
        bool quote = text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r');
        text = text.Replace("\"", "\"\"");
        return quote ? $"\"{text}\"" : text;
    }

    private static string ResolveHelperPath()
    {
        return BundledToolPathResolver.ResolveFile(
            Path.Combine("Tools", "material_extractor.py"),
            [
                "material_extractor.py",
                Path.Combine("..", "..", "..", "Tools", "material_extractor.py"),
            ]);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }
}
