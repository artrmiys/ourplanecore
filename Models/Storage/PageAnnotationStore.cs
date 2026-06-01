using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SkiaSharp;

namespace OurPlaneCore;

internal static class PageAnnotationStore
{
    public static List<PageAnnotation> LoadPageAnnotations(string pageFolder)
    {
        string path = PageAnnotationsJsonPath(pageFolder);
        if (!File.Exists(path)) return [];

        try
        {
            var dtos = JsonSerializer.Deserialize<List<PageAnnotationDto>>(File.ReadAllText(path)) ?? [];
            return dtos.Select(dto => new PageAnnotation
            {
                Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                Kind = NormalizePageAnnotationKind(dto.Kind),
                Text = dto.Text ?? "",
                Color = string.IsNullOrWhiteSpace(dto.Color) ? "#1565C0" : dto.Color,
                StrokeWidth = NormalizeStrokeWidth(dto.StrokeWidth),
                PageFolder = pageFolder,
                ScaleMetersPerPt = dto.ScaleMetersPerPt,
                Hidden = dto.Hidden,
                Points = dto.PointsPdf.Select(p => new SKPoint(p.X, p.Y)).ToList(),
            }).ToList();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            OurPlaneCoreJobStore.QuarantineCorruptJson(path, "LoadPageAnnotations", ex);
            return [];
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"LoadPageAnnotations failed for {path}");
            return [];
        }
    }

    public static void SavePageAnnotations(string pageFolder, IEnumerable<PageAnnotation> annotations)
    {
        Directory.CreateDirectory(pageFolder);
        var dtos = annotations.Select(annotation => new PageAnnotationDto
        {
            Id = annotation.Id,
            Kind = NormalizePageAnnotationKind(annotation.Kind),
            Text = annotation.Text ?? "",
            Color = string.IsNullOrWhiteSpace(annotation.Color) ? "#1565C0" : annotation.Color,
            StrokeWidth = NormalizeStrokeWidth(annotation.StrokeWidth),
            PageFolder = pageFolder,
            ScaleMetersPerPt = annotation.ScaleMetersPerPt,
            Hidden = annotation.Hidden,
            PointsPdf = annotation.Points.Select(p => new PointDto(p.X, p.Y)).ToList(),
        }).ToList();

        try
        {
            IoUtil.WriteAllTextAtomic(
                PageAnnotationsJsonPath(pageFolder),
                JsonSerializer.Serialize(dtos, OurPlaneCoreJobStore.JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(PageAnnotationsJsonPath(pageFolder))}': {ex.Message}", ex);
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
            "arrow" => "arrow",
            "rectangle" or "rect" or "box" => "rectangle",
            "cloud" or "calloutcloud" or "callout_cloud" => "cloud",
            "area" or "highlight" or "fill" => "area",
            "note" or "text" => "note",
            _ => "line",
        };
    }

    private static double NormalizeStrokeWidth(double value) =>
        value is >= 0.75 and <= 12.0 ? value : 5.0;
}
