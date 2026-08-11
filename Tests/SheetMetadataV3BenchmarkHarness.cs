using System.Diagnostics;
using System.Text.Json;
using OurPlanCore;

internal static class SheetMetadataV3BenchmarkHarness
{
    public static int Run(string[] args)
    {
        if (args.Length < 3 || !File.Exists(args[1]) || !int.TryParse(args[2], out int pageCount) || pageCount <= 0)
        {
            Console.Error.WriteLine("Usage: sheetmetadata-v3-benchmark <pdf> <page-count>");
            return 2;
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "ourplancore-sheetmeta-v3-benchmark", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        SheetMetadataConfig previous = SheetMetadataRulesService.Active.Clone();
        try
        {
            OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(tempRoot, "benchmark");
            var pages = new List<PageInfo>();
            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                string folder = Path.Combine(job.PagesRoot, $"Page {pageIndex + 1}");
                Directory.CreateDirectory(folder);
                pages.Add(new PageInfo
                {
                    Name = $"Page {pageIndex + 1}",
                    FolderPath = folder,
                    PdfPath = Path.GetFullPath(args[1]),
                    PdfPage = pageIndex,
                });
            }

            SheetMetadataRulesService.Install(SheetMetadataConfig.BuildIdealV3());
            var firstWatch = Stopwatch.StartNew();
            IReadOnlyList<PdfSheetMetadataAnalysisItem> first = PdfSheetMetadataService.AnalyzePages(
                job, pages, persistMetadata: false);
            firstWatch.Stop();

            var repeatWatch = Stopwatch.StartNew();
            IReadOnlyList<PdfSheetMetadataAnalysisItem> repeat = PdfSheetMetadataService.AnalyzePages(
                job, pages, persistMetadata: false);
            repeatWatch.Stop();

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                pages = pageCount,
                first_ms = firstWatch.ElapsedMilliseconds,
                repeat_ms = repeatWatch.ElapsedMilliseconds,
                first_ok = first.Count(item => item.Ok),
                repeat_ok = repeat.Count(item => item.Ok),
                repeat_cache_hits = repeat.Count(item => item.FromCache),
                labels = repeat.Count(item => !string.IsNullOrWhiteSpace(item.Metadata?.SheetLabel)),
                titles = repeat.Count(item => !string.IsNullOrWhiteSpace(item.Metadata?.SheetTitle)),
                lowercase_names = repeat.Count(item =>
                    item.Metadata != null &&
                    string.Equals(item.Metadata.ProposedPageName(), item.Metadata.ProposedPageName().ToLowerInvariant(), StringComparison.Ordinal)),
            }, new JsonSerializerOptions { WriteIndented = true }));
            return first.All(item => item.Ok) && repeat.All(item => item.Ok) ? 0 : 1;
        }
        finally
        {
            SheetMetadataRulesService.Install(previous);
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Benchmark cleanup must not hide the measured result.
            }
        }
    }
}
