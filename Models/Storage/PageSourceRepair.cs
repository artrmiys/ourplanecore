using System;
using System.Collections.Generic;
using System.IO;

namespace OurPlaneCore;

internal static class PageSourceRepair
{
    public static bool TryRepairFromMetadata(string pageFolder, out SourceInfo src)
    {
        src = new SourceInfo();
        if (!Directory.Exists(pageFolder) ||
            !string.Equals(OurPlaneCoreJobStore.ReadClass(pageFolder), "Page", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        PdfSheetMetadata? metadata = OurPlaneCoreJobStore.ReadSourcePdfMetadata(pageFolder);
        if (metadata == null ||
            string.IsNullOrWhiteSpace(metadata.PdfPath) ||
            !TryResolvePdfPath(pageFolder, metadata.PdfPath, out string pdfAbsPath))
        {
            return false;
        }

        double scaleMetersPerPt = ScaleMetersPerPtFromMetadata(metadata);
        try
        {
            SourceInfo repaired = BuildSource(pageFolder, pdfAbsPath, metadata, scaleMetersPerPt);
            IoUtil.WriteAllTextAtomic(
                Path.Combine(pageFolder, "source.json"),
                System.Text.Json.JsonSerializer.Serialize(repaired, OurPlaneCoreJobStore.JsonOptions));
            if (metadata.Layers.Count > 0)
                TrySaveLayerCache(pageFolder, metadata.Layers);

            src = OurPlaneCoreJobStore.ReadSource(pageFolder) ?? new SourceInfo();
            bool ok = !string.IsNullOrWhiteSpace(src.Pdf);
            if (ok)
                AppLog.Warn($"Repaired missing page source.json from source_pdf.json for '{pageFolder}'.");
            return ok;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Failed to repair page source.json from source_pdf.json for '{pageFolder}'");
            return false;
        }
    }

    private static void TrySaveLayerCache(string pageFolder, IReadOnlyList<PdfLayerInfo> layers)
    {
        try
        {
            OurPlaneCoreJobStore.SavePageLayerCache(pageFolder, layers);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Failed to save repaired page layer cache for '{pageFolder}'");
        }
    }

    private static SourceInfo BuildSource(
        string pageFolder,
        string pdfAbsPath,
        PdfSheetMetadata metadata,
        double scaleMetersPerPt) =>
        new()
        {
            Pdf = Path.GetRelativePath(pageFolder, pdfAbsPath),
            Page = Math.Max(0, metadata.PageIndex),
            ScaleMetersPerPt = Math.Max(0, scaleMetersPerPt),
            PdfLayersCached = metadata.Layers.Count > 0,
            PdfLayers = metadata.Layers,
        };

    private static double ScaleMetersPerPtFromMetadata(PdfSheetMetadata metadata)
    {
        if (metadata.SelectedScaleMetersPerPt > 0)
            return metadata.SelectedScaleMetersPerPt;

        return !string.IsNullOrWhiteSpace(metadata.EffectiveScaleText) &&
               PdfSheetMetadataService.TryParseScaleMetersPerPt(metadata.EffectiveScaleText, out double parsedScale)
            ? parsedScale
            : 0;
    }

    private static bool TryResolvePdfPath(string pageFolder, string metadataPdfPath, out string pdfAbsPath)
    {
        pdfAbsPath = "";
        try
        {
            string direct = Path.IsPathRooted(metadataPdfPath)
                ? Path.GetFullPath(metadataPdfPath)
                : Path.GetFullPath(Path.Combine(pageFolder, metadataPdfPath));
            if (File.Exists(direct))
            {
                pdfAbsPath = direct;
                return true;
            }

            string fileName = Path.GetFileName(metadataPdfPath);
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            foreach (string sourceRoot in CandidateSourceRoots(pageFolder))
            {
                string? match = Directory.EnumerateFiles(sourceRoot, fileName, SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(match) && File.Exists(match))
                {
                    pdfAbsPath = Path.GetFullPath(match);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Failed to resolve repair PDF path '{metadataPdfPath}' for '{pageFolder}'");
        }

        return false;
    }

    private static IEnumerable<string> CandidateSourceRoots(string pageFolder)
    {
        string? current = pageFolder;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string sources = Path.Combine(current, "sources");
            if (Directory.Exists(sources))
                yield return sources;

            string? parent = Directory.GetParent(current)?.FullName;
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                yield break;
            current = parent;
        }
    }
}
