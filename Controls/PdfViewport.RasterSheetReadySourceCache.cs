using System;
using System.Collections.Generic;
using System.IO;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private const int RasterSheetReadySourceCacheMaxEntries = 2048;
    private static readonly object RasterSheetReadySourceCacheGate = new();
    private static readonly Dictionary<string, RasterSheetSource> RasterSheetReadySourceCache = new(StringComparer.OrdinalIgnoreCase);

    private static void RememberReadyRasterSheetSource(PageInfo page, int targetDpi, RasterSheetSource? source)
    {
        if (source == null)
            return;

        string key = RasterSheetReadySourceCacheKey(page.FolderPath, page.PdfPath, page.PdfPage, targetDpi);
        if (string.IsNullOrWhiteSpace(key) ||
            !IsRememberedReadyRasterSheetSourceUsable(page, targetDpi, source))
        {
            return;
        }

        lock (RasterSheetReadySourceCacheGate)
        {
            if (RasterSheetReadySourceCache.Count >= RasterSheetReadySourceCacheMaxEntries)
                RasterSheetReadySourceCache.Clear();

            RasterSheetReadySourceCache[key] = source.Clone();
        }
    }

    private static void RememberReadyRasterSheetSource(
        string pageFolder,
        string pdfPath,
        int pdfIndex,
        RasterSheetSource? source)
    {
        if (source == null)
            return;

        int targetDpi = RasterSheetCacheService.RenderScaleToDpi(source.RenderScale);
        string key = RasterSheetReadySourceCacheKey(pageFolder, pdfPath, pdfIndex, targetDpi);
        if (string.IsNullOrWhiteSpace(key) ||
            targetDpi <= 0 ||
            RasterSheetCacheService.IsSourceImageRaster(source))
        {
            return;
        }

        lock (RasterSheetReadySourceCacheGate)
        {
            if (RasterSheetReadySourceCache.Count >= RasterSheetReadySourceCacheMaxEntries)
                RasterSheetReadySourceCache.Clear();

            RasterSheetReadySourceCache[key] = source.Clone();
        }
    }

    private static bool TryGetRememberedReadyRasterSheetSource(
        PageInfo page,
        int targetDpi,
        out RasterSheetSource? source)
    {
        source = null;
        string key = RasterSheetReadySourceCacheKey(page.FolderPath, page.PdfPath, page.PdfPage, targetDpi);
        if (string.IsNullOrWhiteSpace(key))
            return false;

        lock (RasterSheetReadySourceCacheGate)
        {
            if (!RasterSheetReadySourceCache.TryGetValue(key, out RasterSheetSource? remembered))
                return false;

            if (!IsRememberedReadyRasterSheetSourceUsable(page, targetDpi, remembered))
            {
                RasterSheetReadySourceCache.Remove(key);
                return false;
            }

            source = remembered.Clone();
            return true;
        }
    }

    private static bool IsRememberedReadyRasterSheetSourceUsable(
        PageInfo page,
        int targetDpi,
        RasterSheetSource source)
    {
        if (targetDpi <= 0 ||
            string.IsNullOrWhiteSpace(source.Image) ||
            RasterSheetCacheService.IsSourceImageRaster(source) ||
            RasterSheetCacheService.RenderScaleToDpi(source.RenderScale) != targetDpi)
        {
            return false;
        }

        RasterSheetSource? pageSource = page.RasterSheet;
        if (pageSource != null)
        {
            if (pageSource.PdfLastWriteUtcTicks > 0 &&
                source.PdfLastWriteUtcTicks > 0 &&
                pageSource.PdfLastWriteUtcTicks != source.PdfLastWriteUtcTicks)
            {
                return false;
            }

            if (pageSource.PdfLength > 0 &&
                source.PdfLength > 0 &&
                pageSource.PdfLength != source.PdfLength)
            {
                return false;
            }
        }

        return true;
    }

    private static string RasterSheetReadySourceCacheKey(
        string pageFolder,
        string pdfPath,
        int pdfIndex,
        int targetDpi)
    {
        if (string.IsNullOrWhiteSpace(pageFolder) ||
            string.IsNullOrWhiteSpace(pdfPath) ||
            pdfIndex < 0 ||
            targetDpi <= 0)
        {
            return "";
        }

        return $"{StableFullPath(pageFolder)}|{StableFullPath(pdfPath)}|{pdfIndex}|dpi:{targetDpi}";
    }

    private static string StableFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }
}
