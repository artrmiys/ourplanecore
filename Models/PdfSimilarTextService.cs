using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace OurPlaneCore;

public sealed record PdfSimilarTextMatch(string Text, SKRect Rect, SKPoint Center);

public sealed class PdfSimilarTextResult
{
    public string Query { get; init; } = "";
    public IReadOnlyList<PdfSimilarTextMatch> Matches { get; init; } = [];
}

public static class PdfSimilarTextService
{
    public static bool TryFindSimilarTextByQuery(
        string pdfPath,
        int pageIndex,
        string query,
        out PdfSimilarTextResult result,
        out string error)
    {
        result = new PdfSimilarTextResult();
        error = "";

        string cleanQuery = CleanQuery(query);
        if (string.IsNullOrWhiteSpace(pdfPath) ||
            !File.Exists(pdfPath) ||
            pageIndex < 0 ||
            cleanQuery.Length < 2)
        {
            return false;
        }

        try
        {
            var request = new PdfSimilarTextRequest
            {
                Pdf = pdfPath,
                Page = pageIndex,
                Query = cleanQuery,
            };

            if (!PdfLayerRenderService.TryInvokeHelper(
                    "similartext",
                    request,
                    out PdfSimilarTextResponse? response,
                    out error))
            {
                return false;
            }

            if (response == null || !response.Ok)
            {
                error = response?.Error ?? "PyMuPDF did not return a similar text response.";
                return false;
            }

            result = BuildResult(response);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Similar text query scan failed for {pdfPath} page {pageIndex + 1}");
            error = ex.Message;
            return false;
        }
    }

    public static bool TryFindSimilarText(
        string pdfPath,
        int pageIndex,
        SKRect pdfRect,
        bool preferNearestRepeatedText,
        SKPoint? anchorPdf,
        bool nearbyRepeatedTextFallback,
        out PdfSimilarTextResult result,
        out string error)
    {
        result = new PdfSimilarTextResult();
        error = "";

        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath) || pageIndex < 0)
            return false;

        try
        {
            var request = new PdfSimilarTextRequest
            {
                Pdf = pdfPath,
                Page = pageIndex,
                Rect = PdfSimilarTextRect.From(pdfRect),
                PreferNearestRepeatedText = preferNearestRepeatedText,
                NearbyRepeatedTextFallback = nearbyRepeatedTextFallback,
                Anchor = anchorPdf is { } anchor ? PdfSimilarTextPoint.From(anchor) : null,
            };

            if (!PdfLayerRenderService.TryInvokeHelper(
                    "similartext",
                    request,
                    out PdfSimilarTextResponse? response,
                    out error))
            {
                return false;
            }

            if (response == null || !response.Ok)
            {
                error = response?.Error ?? "PyMuPDF did not return a similar text response.";
                return false;
            }

            result = BuildResult(response);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Similar text scan failed for {pdfPath} page {pageIndex + 1}");
            error = ex.Message;
            return false;
        }
    }

    private static PdfSimilarTextResult BuildResult(PdfSimilarTextResponse response) =>
        new()
        {
            Query = response.Query ?? "",
            Matches = response.Matches
                .Where(match =>
                    IsFinite(match.X0) &&
                    IsFinite(match.Y0) &&
                    IsFinite(match.X1) &&
                    IsFinite(match.Y1))
                .Select(match =>
                {
                    var rect = new SKRect(match.X0, match.Y0, match.X1, match.Y1);
                    return new PdfSimilarTextMatch(
                        match.Text ?? "",
                        rect,
                        new SKPoint((rect.Left + rect.Right) / 2f, (rect.Top + rect.Bottom) / 2f));
                })
                .ToList(),
        };

    private static string CleanQuery(string query) =>
        new((query ?? "")
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);

    private sealed class PdfSimilarTextRequest
    {
        public string Pdf { get; init; } = "";
        public int Page { get; init; }
        public PdfSimilarTextRect Rect { get; init; } = new();
        public bool PreferNearestRepeatedText { get; init; }
        public bool NearbyRepeatedTextFallback { get; init; }
        public PdfSimilarTextPoint? Anchor { get; init; }
        public string Query { get; init; } = "";
    }

    private sealed class PdfSimilarTextRect
    {
        [JsonPropertyName("x0")]
        public float X0 { get; init; }

        [JsonPropertyName("y0")]
        public float Y0 { get; init; }

        [JsonPropertyName("x1")]
        public float X1 { get; init; }

        [JsonPropertyName("y1")]
        public float Y1 { get; init; }

        public static PdfSimilarTextRect From(SKRect rect) =>
            new()
            {
                X0 = Math.Min(rect.Left, rect.Right),
                Y0 = Math.Min(rect.Top, rect.Bottom),
                X1 = Math.Max(rect.Left, rect.Right),
                Y1 = Math.Max(rect.Top, rect.Bottom),
            };
    }

    private sealed class PdfSimilarTextPoint
    {
        [JsonPropertyName("x")]
        public float X { get; init; }

        [JsonPropertyName("y")]
        public float Y { get; init; }

        public static PdfSimilarTextPoint From(SKPoint point) =>
            new()
            {
                X = point.X,
                Y = point.Y,
            };
    }

    private sealed class PdfSimilarTextResponse
    {
        public bool Ok { get; init; }
        public string Error { get; init; } = "";
        public string Query { get; init; } = "";
        public List<PdfSimilarTextMatchDto> Matches { get; init; } = [];
    }

    private sealed class PdfSimilarTextMatchDto
    {
        public string Text { get; init; } = "";

        [JsonPropertyName("x0")]
        public float X0 { get; init; }

        [JsonPropertyName("y0")]
        public float Y0 { get; init; }

        [JsonPropertyName("x1")]
        public float X1 { get; init; }

        [JsonPropertyName("y1")]
        public float Y1 { get; init; }
    }
}
