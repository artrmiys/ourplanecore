using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SkiaSharp;

namespace OurPlanCore;

internal static class PageAnnotationStore
{
    public static List<PageAnnotation> LoadPageAnnotations(string pageFolder) =>
        ReadPageAnnotations(pageFolder).Value ?? [];

    internal static DataFileResult<List<PageAnnotation>> ReadPageAnnotations(string pageFolder) =>
        DataFileReader.Read(PageAnnotationsJsonPath(pageFolder), json =>
        {
            var dtos = JsonSerializer.Deserialize<List<PageAnnotationDto>>(json) ?? throw new JsonException("The document contains null instead of an array.");
            return dtos.Select(dto =>
            {
                if (dto == null || dto.PointsPdf == null) throw new JsonException("Invalid annotation entry.");
                string kind = NormalizePageAnnotationKind(dto.Kind);
                return new PageAnnotation
                {
                    Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                    Kind = kind,
                    Text = dto.Text ?? "",
                    Color = string.IsNullOrWhiteSpace(dto.Color) ? DefaultAnnotationColor(kind) : dto.Color,
                    StrokeWidth = NormalizeStrokeWidth(dto.StrokeWidth),
                    PageFolder = pageFolder,
                    ScaleMetersPerPt = dto.ScaleMetersPerPt,
                    Hidden = dto.Hidden,
                    Points = dto.PointsPdf.Select(p => new SKPoint(p.X, p.Y)).ToList(),
                };
            }).ToList();
        });

    public static void SavePageAnnotations(string pageFolder, IEnumerable<PageAnnotation> annotations)
    {
        string path = PageAnnotationsJsonPath(pageFolder);
        JobWriteAccess.Demand(path, "save page annotations");
        var dtos = annotations.Select(annotation =>
        {
            string kind = NormalizePageAnnotationKind(annotation.Kind);
            return new PageAnnotationDto
            {
                Id = annotation.Id,
                Kind = kind,
                Text = annotation.Text ?? "",
                Color = string.IsNullOrWhiteSpace(annotation.Color) ? DefaultAnnotationColor(kind) : annotation.Color,
                StrokeWidth = NormalizeStrokeWidth(annotation.StrokeWidth),
                PageFolder = pageFolder,
                ScaleMetersPerPt = annotation.ScaleMetersPerPt,
                Hidden = annotation.Hidden,
                PointsPdf = annotation.Points.Select(p => new PointDto(p.X, p.Y)).ToList(),
            };
        }).ToList();
        string json = JsonSerializer.Serialize(dtos, OurPlanCoreJobStore.JsonOptions);

        try
        {
            if (File.Exists(path) &&
                string.Equals(File.ReadAllText(path), json, StringComparison.Ordinal))
            {
                return;
            }

            Directory.CreateDirectory(pageFolder);
            IoUtil.WriteAllTextAtomic(path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    public static string PageAnnotationsJsonPath(string pageFolder) =>
        Path.Combine(pageFolder, "annotations.json");

    public static string NormalizePageAnnotationKind(string value)
    {
        string clean = (value ?? "").Trim().ToLowerInvariant();
        return clean switch
        {
            "dimension" or "ruler" => "dimension",
            "pitch" or "slope" or "roofpitch" => "pitch",
            "arrow" => "arrow",
            "rectangle" or "rect" or "box" => "rectangle",
            "cloud" or "calloutcloud" or "callout_cloud" => "cloud",
            "highlight" or "highlighter" => "highlight",
            "area" or "fill" => "area",
            "note" or "text" => "note",
            _ => "line",
        };
    }

    private static double NormalizeStrokeWidth(double value) =>
        value is >= 0.75 and <= 12.0 ? value : 5.0;

    private static string DefaultAnnotationColor(string kind) =>
        kind == "highlight" ? "#FFC107" : "#1565C0";
}
