using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace OurPlaneCore;

public static partial class PlanSwiftProjectImporter
{
    private static void ImportPages(
        PlanSwiftImportOptions options,
        PlanSwiftProjectManifest manifest,
        OurPlaneCoreJob job,
        string pagesRoot,
        string tempRoot,
        Dictionary<string, ImportedPlanSwiftPage> pageByGuid,
        List<string> messages,
        ref int importedPages)
    {
        HashSet<string> takeoffPageGuids = BuildTakeoffPageGuidSet(manifest);
        IReadOnlyList<PlanSwiftPageRecord> pagesToImport = options.ImportAllSheetsAndTakeoffFolders
            ? manifest.Pages
            : manifest.Pages
                .Where(page => PageHasTakeoffGeometry(page, takeoffPageGuids))
                .ToList();

        int skippedPages = manifest.Pages.Count - pagesToImport.Count;
        if (!options.ImportAllSheetsAndTakeoffFolders && skippedPages > 0)
        {
            messages.Add(
                $"Skipped {skippedPages.ToString(CultureInfo.InvariantCulture)} PlanSwift page(s) " +
                "with no measured takeoff geometry.");
        }

        IEnumerable<PlanSwiftPageRecord> pages = Limit(pagesToImport, options.MaxPages);
        foreach (PlanSwiftPageRecord page in pages)
        {
            string parent = EnsureRelativeFolder(pagesRoot, page.ParentRelativeFolder);
            string tempPdf = Path.Combine(tempRoot, "pages", $"{Guid.NewGuid():N}.pdf");
            PlanSwiftPageNormalization? imageNormalization = null;
            if (File.Exists(page.ImagePath))
                PlanSwiftPagePdfWriter.TryReadImagePageNormalization(page.ImagePath, out imageNormalization);

            PlanSwiftPageNormalization normalization;
            try
            {
                if (options.ConvertPageImages && File.Exists(page.ImagePath))
                    normalization = PlanSwiftPagePdfWriter.WriteImagePagePdf(page.ImagePath, tempPdf);
                else
                    normalization = PlanSwiftPagePdfWriter.WritePlaceholderPdf(
                        tempPdf,
                        $"PlanSwift page image missing: {page.Name}",
                        imageNormalization);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                messages.Add($"Page '{page.Name}' image conversion failed; blank PDF was created. {ex.Message}");
                normalization = PlanSwiftPagePdfWriter.WritePlaceholderPdf(
                    tempPdf,
                    $"PlanSwift page image failed: {page.Name}",
                    imageNormalization);
            }

            if (!string.IsNullOrWhiteSpace(normalization.Message))
                messages.Add($"Page '{page.Name}': {normalization.Message}");
            if (!PlanSwiftGeometryConverter.HasUniformPageNormalization(normalization))
            {
                messages.Add(
                    $"Page '{page.Name}' image has non-uniform coordinate scale " +
                    $"{normalization.CoordinateScaleX:G6}/{normalization.CoordinateScaleY:G6}; measurement scale uses X scale.");
            }

            double rawScale = PlanSwiftGeometryConverter.ScaleMetersPerPoint(page.ScaleX, page.ScaleUnits);
            double scale = PlanSwiftGeometryConverter.AdjustScaleForPageNormalization(rawScale, normalization);
            if (page.ScaleX > 0 && page.ScaleY > 0 && Math.Abs(page.ScaleX - page.ScaleY) > 0.001)
                messages.Add($"Page '{page.Name}' has different ScaleX/ScaleY values: {page.ScaleX:G17}/{page.ScaleY:G17}.");

            PageInfo imported = OurPlaneCoreJobStore.CreatePageFromPdf(
                job,
                tempPdf,
                page.Name,
                parent,
                pdfPage: 0,
                scaleMetersPerPt: scale);
            if (page.OrderIndex > 0)
                OurPlaneCoreJobStore.SetOrderIndex(imported.FolderPath, page.OrderIndex);
            if (options.ConvertPageImages && File.Exists(page.ImagePath))
            {
                RasterSheetBuildResult raster = RasterSheetCacheService.BuildFromImageAndEnable(
                    imported,
                    page.ImagePath,
                    normalization.WidthPt,
                    normalization.HeightPt);
                if (!raster.Ok)
                    messages.Add($"Page '{page.Name}' image raster cache skipped: {raster.Error}");
            }
            importedPages++;

            if (!string.IsNullOrWhiteSpace(page.Guid))
                pageByGuid[page.Guid] = new ImportedPlanSwiftPage(imported, normalization);
        }
    }

    private static HashSet<string> BuildTakeoffPageGuidSet(PlanSwiftProjectManifest manifest)
    {
        var pageGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PlanSwiftSectionRecord section in manifest.TakeoffItems.SelectMany(item => item.Sections))
            AddPageGuid(pageGuids, section.PageGuid);
        foreach (PlanSwiftSectionRecord section in manifest.Segments.SelectMany(segment => segment.Sections))
            AddPageGuid(pageGuids, section.PageGuid);

        return pageGuids;
    }

    private static void AddPageGuid(HashSet<string> pageGuids, string pageGuid)
    {
        string normalized = PlanSwiftXml.NormalizeGuid(pageGuid);
        if (!string.IsNullOrWhiteSpace(normalized))
            pageGuids.Add(normalized);
    }

    private static bool PageHasTakeoffGeometry(PlanSwiftPageRecord page, HashSet<string> takeoffPageGuids)
    {
        string normalized = PlanSwiftXml.NormalizeGuid(page.Guid);
        return !string.IsNullOrWhiteSpace(normalized) && takeoffPageGuids.Contains(normalized);
    }
}
