using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OurPlaneCore;

public sealed class MaterialExtractionResult
{
    [JsonPropertyName("job_name")]
    public string? JobName { get; set; }

    [JsonPropertyName("input_files")]
    public List<MaterialInputFile> InputFiles { get; set; } = [];

    [JsonPropertyName("rows")]
    public List<MaterialExtractionRow> Rows { get; set; } = [];

    [JsonPropertyName("material_summaries")]
    public List<MaterialSummaryRow> MaterialSummaries { get; set; } = [];

    [JsonPropertyName("schedules")]
    public List<MaterialSchedule> Schedules { get; set; } = [];

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];

    [JsonPropertyName("quality")]
    public MaterialQualitySummary Quality { get; set; } = new();

    [JsonPropertyName("stats")]
    public MaterialExtractionStats Stats { get; set; } = new();
}

public sealed class MaterialInputFile
{
    [JsonPropertyName("pdf_name")]
    public string PdfName { get; set; } = "";

    [JsonPropertyName("source_path")]
    public string SourcePath { get; set; } = "";

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }
}

public sealed class MaterialExtractionStats
{
    [JsonPropertyName("pages_read")]
    public int PagesRead { get; set; }

    [JsonPropertyName("pages_ocr")]
    public int PagesOcr { get; set; }

    [JsonPropertyName("rows_total")]
    public int RowsTotal { get; set; }

    [JsonPropertyName("schedules_total")]
    public int SchedulesTotal { get; set; }
}

public sealed class MaterialQualitySummary
{
    [JsonPropertyName("rows_total")]
    public int RowsTotal { get; set; }

    [JsonPropertyName("high_confidence_rows")]
    public int HighConfidenceRows { get; set; }

    [JsonPropertyName("takeoff_ready_rows")]
    public int TakeoffReadyRows { get; set; }

    [JsonPropertyName("review_rows")]
    public int ReviewRows { get; set; }

    [JsonPropertyName("schedules_total")]
    public int SchedulesTotal { get; set; }

    [JsonPropertyName("blank_pages_without_ocr")]
    public int BlankPagesWithoutOcr { get; set; }

    [JsonPropertyName("pdfplumber_available")]
    public bool PdfPlumberAvailable { get; set; }

    [JsonPropertyName("ocr_available")]
    public bool OcrAvailable { get; set; }
}

public sealed class MaterialExtractionRow
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("pdf_file")]
    public string PdfFile { get; set; } = "";

    [JsonPropertyName("source_path")]
    public string SourcePath { get; set; } = "";

    [JsonPropertyName("pdf_page")]
    public int PdfPage { get; set; }

    [JsonPropertyName("sheet")]
    public string? Sheet { get; set; }

    [JsonPropertyName("discipline")]
    public string? Discipline { get; set; }

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("subcategory")]
    public string? Subcategory { get; set; }

    [JsonPropertyName("material_family")]
    public string? MaterialFamily { get; set; }

    [JsonPropertyName("item")]
    public string? Item { get; set; }

    [JsonPropertyName("material")]
    public string? Material { get; set; }

    [JsonPropertyName("size")]
    public string? Size { get; set; }

    [JsonPropertyName("thickness")]
    public string? Thickness { get; set; }

    [JsonPropertyName("grade")]
    public string? Grade { get; set; }

    [JsonPropertyName("treatment")]
    public string? Treatment { get; set; }

    [JsonPropertyName("qty")]
    public string? Qty { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("page_ref")]
    public string? PageRef { get; set; }

    [JsonPropertyName("section_ref")]
    public string? SectionRef { get; set; }

    [JsonPropertyName("schedule_ref")]
    public string? ScheduleRef { get; set; }

    [JsonPropertyName("raw_text")]
    public string RawText { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("review_flags")]
    public List<string> ReviewFlags { get; set; } = [];

    [JsonPropertyName("downstream_scope")]
    public List<string> DownstreamScope { get; set; } = [];
}

public sealed class MaterialSummaryRow
{
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("material_family")]
    public string? MaterialFamily { get; set; }

    [JsonPropertyName("size")]
    public string? Size { get; set; }

    [JsonPropertyName("thickness")]
    public string? Thickness { get; set; }

    [JsonPropertyName("grade")]
    public string? Grade { get; set; }

    [JsonPropertyName("treatment")]
    public string? Treatment { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("evidence_count")]
    public int EvidenceCount { get; set; }

    [JsonPropertyName("pdf_files")]
    public List<string> PdfFiles { get; set; } = [];

    [JsonPropertyName("sheets")]
    public List<string> Sheets { get; set; } = [];

    [JsonPropertyName("pages")]
    public List<string> Pages { get; set; } = [];

    [JsonPropertyName("review_flags")]
    public List<string> ReviewFlags { get; set; } = [];

    [JsonPropertyName("example")]
    public string? Example { get; set; }
}

public sealed class MaterialSchedule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("pdf_file")]
    public string PdfFile { get; set; } = "";

    [JsonPropertyName("pdf_page")]
    public int PdfPage { get; set; }

    [JsonPropertyName("sheet")]
    public string? Sheet { get; set; }

    [JsonPropertyName("schedule_type")]
    public string? ScheduleType { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("source_type")]
    public string? SourceType { get; set; }

    [JsonPropertyName("columns")]
    public List<string> Columns { get; set; } = [];

    [JsonPropertyName("rows")]
    public List<MaterialScheduleRow> Rows { get; set; } = [];

    [JsonPropertyName("review_flags")]
    public List<string> ReviewFlags { get; set; } = [];
}

public sealed class MaterialScheduleRow
{
    [JsonPropertyName("mark")]
    public string? Mark { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("item")]
    public string? Item { get; set; }

    [JsonPropertyName("size")]
    public string? Size { get; set; }

    [JsonPropertyName("material")]
    public string? Material { get; set; }

    [JsonPropertyName("qty")]
    public string? Qty { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("spacing")]
    public string? Spacing { get; set; }

    [JsonPropertyName("length")]
    public string? Length { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("raw_text")]
    public string RawText { get; set; } = "";

    [JsonPropertyName("raw_cells")]
    public Dictionary<string, string> RawCells { get; set; } = [];

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("review_flags")]
    public List<string> ReviewFlags { get; set; } = [];
}
