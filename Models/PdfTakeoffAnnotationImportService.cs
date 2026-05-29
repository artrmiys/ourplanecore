using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlaneCore;

public sealed class PdfTakeoffAnnotationImportResult
{
    public string PdfPath { get; init; } = "";
    public int PageCount { get; init; }
    public int TotalMeasurements { get; init; }
    public IReadOnlyList<PdfTakeoffAnnotationPage> Pages { get; init; } = [];
}

public sealed class PdfTakeoffAnnotationPage
{
    public int PageIndex { get; init; }
    public float WidthPt { get; init; }
    public float HeightPt { get; init; }
    public double ScaleMPerPt { get; init; }
    public IReadOnlyList<PdfTakeoffAnnotationMeasurement> Measurements { get; init; } = [];
}

public sealed class PdfTakeoffAnnotationMeasurement
{
    public string Type { get; init; } = "line";
    public string Role { get; init; } = "takeoff";
    public string Color { get; init; } = "#E52237";
    public IReadOnlyList<SKPoint> Points { get; init; } = [];
    public double ScaleMPerPt { get; init; }
    public string Content { get; init; } = "";
    public string Subject { get; init; } = "";
    public string AnnotationId { get; init; } = "";
    public string SourceSubtype { get; init; } = "";
}

public static class PdfTakeoffAnnotationImportService
{
    public static Task<(bool Ok, PdfTakeoffAnnotationImportResult Result, string Error)> TryReadAsync(string pdfPath) =>
        Task.Run(() =>
        {
            bool ok = TryRead(pdfPath, out PdfTakeoffAnnotationImportResult result, out string error);
            return (ok, result, error);
        });

    public static bool TryRead(
        string pdfPath,
        out PdfTakeoffAnnotationImportResult result,
        out string error)
    {
        result = new PdfTakeoffAnnotationImportResult();
        error = "";

        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            error = "PDF file was not found.";
            return false;
        }

        try
        {
            var request = new PdfTakeoffAnnotationRequest { Pdf = pdfPath };
            if (!PdfLayerRenderService.TryInvokeHelper(
                    "pdftakeoffs",
                    request,
                    out PdfTakeoffAnnotationResponse? response,
                    out error))
            {
                return false;
            }

            if (response == null || !response.Ok)
            {
                error = response?.Error ?? "PyMuPDF did not return PDF takeoff annotations.";
                return false;
            }

            result = new PdfTakeoffAnnotationImportResult
            {
                PdfPath = response.PdfPath,
                PageCount = response.PageCount,
                TotalMeasurements = response.TotalMeasurements,
                Pages = response.Pages
                    .Select(NormalizePage)
                    .ToList(),
            };
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"PDF takeoff annotation import scan failed for {pdfPath}");
            error = ex.Message;
            return false;
        }
    }

    public static Task<(bool Ok, PdfTakeoffCleanCopyResult Result, string Error)> TryCreateCleanCopyAsync(
        string pdfPath,
        string outputPath) =>
        Task.Run(() =>
        {
            bool ok = TryCreateCleanCopy(pdfPath, outputPath, out PdfTakeoffCleanCopyResult result, out string error);
            return (ok, result, error);
        });

    public static bool TryCreateCleanCopy(
        string pdfPath,
        string outputPath,
        out PdfTakeoffCleanCopyResult result,
        out string error)
    {
        result = new PdfTakeoffCleanCopyResult();
        error = "";

        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            error = "PDF file was not found.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            error = "Clean PDF output path is empty.";
            return false;
        }

        try
        {
            var request = new PdfTakeoffCleanCopyRequest { Pdf = pdfPath, Output = outputPath };
            if (!PdfLayerRenderService.TryInvokeHelper(
                    "pdftakeoffclean",
                    request,
                    out PdfTakeoffCleanCopyResponse? response,
                    out error))
            {
                return false;
            }

            if (response == null || !response.Ok)
            {
                error = response?.Error ?? "PyMuPDF did not create a clean PDF copy.";
                return false;
            }

            result = new PdfTakeoffCleanCopyResult
            {
                PdfPath = response.PdfPath,
                OutputPath = response.OutputPath,
                RemovedAnnotations = response.RemovedAnnotations,
            };
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"PDF takeoff clean-copy failed for {pdfPath}");
            error = ex.Message;
            return false;
        }
    }

    private static PdfTakeoffAnnotationPage NormalizePage(PdfTakeoffAnnotationPageDto page) =>
        new()
        {
            PageIndex = page.PageIndex,
            WidthPt = page.WidthPt,
            HeightPt = page.HeightPt,
            ScaleMPerPt = page.ScaleMPerPt,
            Measurements = page.Measurements
                .Select(NormalizeMeasurement)
                .Where(measurement => HasEnoughPoints(measurement.Type, measurement.Points))
                .ToList(),
        };

    private static PdfTakeoffAnnotationMeasurement NormalizeMeasurement(PdfTakeoffAnnotationMeasurementDto measurement)
    {
        string type = OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.Type);
        return new PdfTakeoffAnnotationMeasurement
        {
            Type = type,
            Role = NormalizeRole(measurement.Role),
            Color = NormalizeHexColor(measurement.Color),
            Points = measurement.Points.Select(point => new SKPoint(point.X, point.Y)).ToList(),
            ScaleMPerPt = measurement.ScaleMPerPt > 0 ? measurement.ScaleMPerPt : 0,
            Content = measurement.Content ?? "",
            Subject = measurement.Subject ?? "",
            AnnotationId = measurement.AnnotationId ?? "",
            SourceSubtype = measurement.SourceSubtype ?? "",
        };
    }

    private static string NormalizeRole(string? value)
    {
        string clean = (value ?? "").Trim().ToLowerInvariant();
        return clean == "dimension" ? "dimension" : "takeoff";
    }

    private static bool HasEnoughPoints(string type, IReadOnlyList<SKPoint> points) =>
        type switch
        {
            "point" => points.Count >= 1,
            "area" => points.Count >= 3,
            _ => points.Count >= 2,
        };

    private static string NormalizeHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "#E52237";

        string clean = value.Trim();
        if (!clean.StartsWith("#", StringComparison.Ordinal))
            clean = "#" + clean;
        if (clean.Length != 7)
            return "#E52237";

        for (int i = 1; i < clean.Length; i++)
        {
            char c = clean[i];
            if (!char.IsAsciiHexDigit(c))
                return "#E52237";
        }

        return clean.ToUpperInvariant();
    }

    private sealed class PdfTakeoffAnnotationRequest
    {
        public string Pdf { get; init; } = "";
    }

    private sealed class PdfTakeoffCleanCopyRequest
    {
        public string Pdf { get; init; } = "";
        public string Output { get; init; } = "";
    }

    public sealed class PdfTakeoffCleanCopyResult
    {
        public string PdfPath { get; init; } = "";
        public string OutputPath { get; init; } = "";
        public int RemovedAnnotations { get; init; }
    }

    private sealed class PdfTakeoffAnnotationResponse
    {
        public bool Ok { get; init; }
        public string Error { get; init; } = "";
        public string PdfPath { get; init; } = "";
        public int PageCount { get; init; }
        public int TotalMeasurements { get; init; }
        public List<PdfTakeoffAnnotationPageDto> Pages { get; init; } = [];
    }

    private sealed class PdfTakeoffAnnotationPageDto
    {
        public int PageIndex { get; init; }
        public float WidthPt { get; init; }
        public float HeightPt { get; init; }
        public double ScaleMPerPt { get; init; }
        public List<PdfTakeoffAnnotationMeasurementDto> Measurements { get; init; } = [];
    }

    private sealed class PdfTakeoffAnnotationMeasurementDto
    {
        public string Type { get; init; } = "line";
        public string Role { get; init; } = "takeoff";
        public string Color { get; init; } = "#E52237";
        public double ScaleMPerPt { get; init; }
        public string Content { get; init; } = "";
        public string Subject { get; init; } = "";
        public string AnnotationId { get; init; } = "";
        public string SourceSubtype { get; init; } = "";
        public List<PdfTakeoffAnnotationPointDto> Points { get; init; } = [];
    }

    private sealed class PdfTakeoffCleanCopyResponse
    {
        public bool Ok { get; init; }
        public string Error { get; init; } = "";
        public string PdfPath { get; init; } = "";
        public string OutputPath { get; init; } = "";
        public int RemovedAnnotations { get; init; }
    }

    private sealed class PdfTakeoffAnnotationPointDto
    {
        public float X { get; init; }
        public float Y { get; init; }
    }
}
