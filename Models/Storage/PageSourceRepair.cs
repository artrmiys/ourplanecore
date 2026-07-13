using System;
using System.Collections.Generic;
using System.IO;

namespace OurPlanCore;

internal static class PageSourceRepair
{
    public static bool TryRepairFromMetadata(string pageFolder, out SourceInfo src)
    {
        src = new SourceInfo();
        if (!Directory.Exists(pageFolder) ||
            !string.Equals(OurPlanCoreJobStore.ReadClass(pageFolder), "Page", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        PdfSheetMetadata? metadata = OurPlanCoreJobStore.ReadSourcePdfMetadata(pageFolder);
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
                System.Text.Json.JsonSerializer.Serialize(repaired, OurPlanCoreJobStore.JsonOptions));
            if (metadata.Layers.Count > 0)
                TrySaveLayerCache(pageFolder, metadata.Layers);

            src = OurPlanCoreJobStore.ReadSource(pageFolder) ?? new SourceInfo();
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
            OurPlanCoreJobStore.SavePageLayerCache(pageFolder, layers);
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
        double scaleMetersPerPt)
    {
        SourceInfo src = new()
        {
            Pdf = Path.GetRelativePath(pageFolder, pdfAbsPath),
            Page = Math.Max(0, metadata.PageIndex),
            ScaleMetersPerPt = Math.Max(0, scaleMetersPerPt),
            PdfLayersCached = metadata.Layers.Count > 0,
            PdfLayers = metadata.Layers,
        };
        RestoreReciprocalOverlay(pageFolder, src);
        return src;
    }

    private static void RestoreReciprocalOverlay(string pageFolder, SourceInfo repaired)
    {
        if (!TryFindReciprocalOverlay(pageFolder, out string overlayPageFolder, out SourceInfo overlaySource))
            return;

        SheetOverlayTransformValues transform = SheetOverlayReciprocalService.Invert(
            overlaySource.OverlayOffsetXPt,
            overlaySource.OverlayOffsetYPt,
            overlaySource.OverlayScale,
            overlaySource.OverlayRotationDegrees);
        repaired.OverlayPageFolder = Path.GetRelativePath(pageFolder, overlayPageFolder);
        repaired.OverlayVisible = overlaySource.OverlayVisible;
        repaired.OverlayColor = overlaySource.OverlayColor;
        repaired.OverlayOpacity = overlaySource.OverlayOpacity;
        repaired.OverlayOffsetXPt = transform.OffsetXPt;
        repaired.OverlayOffsetYPt = transform.OffsetYPt;
        repaired.OverlayScale = transform.OverlayScale;
        repaired.OverlayRotationDegrees = transform.OverlayRotationDegrees;
    }

    private static bool TryFindReciprocalOverlay(
        string pageFolder,
        out string overlayPageFolder,
        out SourceInfo overlaySource)
    {
        overlayPageFolder = "";
        overlaySource = new SourceInfo();
        string? pagesRoot = FindPagesRoot(pageFolder);
        if (string.IsNullOrWhiteSpace(pagesRoot) || !Directory.Exists(pagesRoot))
            return false;

        foreach (string candidate in OurPlanCoreJobStore.EnumerateSelfAndDescendants(pagesRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (SheetOverlayReciprocalService.SameFolder(candidate, pageFolder) ||
                !string.Equals(OurPlanCoreJobStore.ReadClass(candidate), "Page", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SourceInfo? source = OurPlanCoreJobStore.ReadSource(candidate);
            if (source == null || string.IsNullOrWhiteSpace(source.OverlayPageFolder))
                continue;

            string target = ResolvePageReference(candidate, source.OverlayPageFolder);
            if (!SheetOverlayReciprocalService.SameFolder(target, pageFolder))
                continue;

            overlayPageFolder = candidate;
            overlaySource = source;
            return true;
        }

        return false;
    }

    private static string? FindPagesRoot(string pageFolder)
    {
        string? current = Path.GetFullPath(pageFolder);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (string.Equals(Path.GetFileName(current), "Pages", StringComparison.OrdinalIgnoreCase))
                return current;

            string? parent = Directory.GetParent(current)?.FullName;
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                return null;
            current = parent;
        }

        return null;
    }

    private static string ResolvePageReference(string pageFolder, string path)
    {
        try
        {
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(pageFolder, path));
        }
        catch
        {
            return path;
        }
    }

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
