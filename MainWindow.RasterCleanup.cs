using System;
using System.Diagnostics;
using System.Linq;

namespace OurPlanCore;

public partial class MainWindow
{
    private void RunRasterCacheCleanupOnClose()
    {
        if (!_settings.AutoCleanRasterCacheOnClose)
            return;

        if (_currentJob == null)
            return;

        if (_sheetManagerRasterPrepareCts != null)
        {
            AppLog.Info("Raster cache auto-clean on close skipped because a raster background job is running.");
            return;
        }

        try
        {
            PageInfo[] pages = CollectPagesUnder(_currentJob.PagesRoot).ToArray();
            if (pages.Length == 0)
                return;

            var stopwatch = Stopwatch.StartNew();
            int cleaned = 0;
            int failed = 0;
            int deletedFiles = 0;
            long deletedBytes = 0;

            foreach (PageInfo page in pages)
            {
                try
                {
                    RasterSheetCacheCompactResult result = RasterSheetCacheService.CompactCache(page);
                    deletedFiles += result.DeletedFiles;
                    deletedBytes += result.DeletedBytes;

                    if (result.Ok)
                    {
                        if (result.DeletedFiles > 0)
                            cleaned++;
                    }
                    else
                    {
                        failed++;
                        AppLog.Warn($"Raster cache auto-clean failed for '{page.Name}': {result.Error}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    AppLog.Warn(ex, $"Raster cache auto-clean crashed for '{page.Name}'");
                }
            }

            AppLog.Info(
                $"Raster cache auto-clean on close: checked {pages.Length} sheet(s), cleaned {cleaned}, " +
                $"deleted {deletedFiles} file(s), freed {FormatRasterCacheBytes(deletedBytes)}, " +
                $"failed {failed}, elapsed {stopwatch.ElapsedMilliseconds}ms.");
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Raster cache auto-clean on close skipped.");
        }
    }
}
