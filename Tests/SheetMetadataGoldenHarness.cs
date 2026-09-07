using System.Text.Json;
using Docnet.Core;
using Docnet.Core.Models;
using OurPlanCore;

internal static class SheetMetadataGoldenHarness
{
    public static int Run(string[] args)
    {
        bool summaryOnly = args.Any(arg => string.Equals(arg, "--summary-only", StringComparison.OrdinalIgnoreCase));
        string[] pdfs = args.Skip(1).Where(arg => !arg.StartsWith("--", StringComparison.Ordinal) && File.Exists(arg)).ToArray();
        if (pdfs.Length == 0)
        {
            Console.Error.WriteLine("Usage: sheetmetadata-golden <pdf> [<pdf> ...]");
            return 2;
        }

        SheetMetadataConfig previous = SheetMetadataRulesService.Active.Clone();
        SheetMetadataRulesService.Install(SheetMetadataConfig.BuildPreciseV2());
        int failures = 0;
        try
        {
            foreach (string pdf in pdfs)
                failures += AnalyzePdf(Path.GetFullPath(pdf), summaryOnly);
        }
        finally
        {
            SheetMetadataRulesService.Install(previous);
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "summary",
            pdf_count = pdfs.Length,
            failures,
        }));
        return failures == 0 ? 0 : 1;
    }

    private static int AnalyzePdf(string pdf, bool summaryOnly)
    {
        int pageCount;
        try
        {
            using var reader = DocLib.Instance.GetDocReader(pdf, new PageDimensions(1.0));
            pageCount = reader.GetPageCount();
        }
        catch (Exception ex)
        {
            WriteError(pdf, -1, ex.Message);
            return 1;
        }

        int failures = 0;
        int blankLabels = 0;
        int blankTitles = 0;
        int blankSuffixes = 0;
        int scaleCandidates = 0;
        int skippedScales = 0;
        var actualSuffixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string nonJobFolder = Path.Combine(Path.GetTempPath(), "ourplancore-sheetmeta-readonly", Guid.NewGuid().ToString("N"));
        for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var page = new PageInfo
            {
                Name = $"page-{pageIndex + 1}",
                FolderPath = Path.Combine(nonJobFolder, $"page-{pageIndex + 1}"),
                PdfPath = pdf,
                PdfPage = pageIndex,
            };
            if (!PdfSheetMetadataService.TryAnalyzePage(page, out PdfSheetMetadata metadata, out string error))
            {
                WriteError(pdf, pageIndex, error);
                failures++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(metadata.SheetLabel)) blankLabels++;
            if (string.IsNullOrWhiteSpace(metadata.SheetTitle)) blankTitles++;
            if (string.IsNullOrWhiteSpace(metadata.Suffix)) blankSuffixes++;
            if (metadata.SelectedScaleMetersPerPt > 0) scaleCandidates++;
            if (metadata.SkipScale) skippedScales++;
            actualSuffixes[metadata.EffectiveSheetKey] = metadata.Suffix;
            if (!summaryOnly)
                Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "page",
                pdf = Path.GetFileName(pdf),
                page = pageIndex + 1,
                label = metadata.SheetLabel,
                key = metadata.EffectiveSheetKey,
                title = metadata.SheetTitle,
                suffix = metadata.Suffix,
                rename = metadata.ProposedPageName(),
                title_source = metadata.TitleSource,
                title_confidence = metadata.TitleConfidence,
                suffix_source = metadata.SuffixSource,
                suffix_confidence = metadata.SuffixConfidence,
                scale = metadata.EffectiveScaleText,
                scale_source = metadata.ScaleSource,
                scale_confidence = metadata.ScaleConfidence,
                skip_scale = metadata.SkipScale,
                detector = metadata.DetectorVersion,
                config = metadata.DetectorConfigFingerprint,
            }));
        }

        failures += ValidateExpectedSuffixes(pdf, actualSuffixes);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "pdf_summary",
            pdf = Path.GetFileName(pdf),
            pages = pageCount,
            failures,
            blank_labels = blankLabels,
            blank_titles = blankTitles,
            blank_suffixes = blankSuffixes,
            scale_candidates = scaleCandidates,
            skipped_scales = skippedScales,
        }));
        return failures;
    }

    private static int ValidateExpectedSuffixes(string pdf, IReadOnlyDictionary<string, string> actual)
    {
        IReadOnlyDictionary<string, string> expected = ExpectedSuffixes(pdf);
        int failures = 0;
        foreach ((string key, string suffix) in expected)
        {
            if (!actual.TryGetValue(key, out string? detected))
            {
                WriteGoldenFailure(pdf, key, suffix, "(missing sheet)");
                failures++;
            }
            else if (!string.Equals(suffix, detected, StringComparison.OrdinalIgnoreCase))
            {
                WriteGoldenFailure(pdf, key, suffix, detected);
                failures++;
            }
        }
        return failures;
    }

    private static IReadOnlyDictionary<string, string> ExpectedSuffixes(string pdf)
    {
        string name = Path.GetFileName(pdf);
        if (name.Contains("Croton Point - Archs", StringComparison.OrdinalIgnoreCase))
        {
            return Pairs(
                "a100.00:b", "a101.00:1st", "a102.00:2nd", "a103.00:3rd", "a104.00:4th", "a105.00:5th",
                "a106.00:rf", "a111.00:1st rcp", "a201.00:el", "a301.00:sec", "a310.00:sec", "a401.00:d",
                "a411.00:f", "a501.00:sc", "a502.00:u", "a510.00:u", "a511.00:u", "a520.00:u", "a601.00:d",
                "a610.00:d", "a701.00:d", "a702.00:f", "a801.00:sc", "a802.00:el", "a803.00:sc", "a901.00:");
        }

        string[] structural =
        [
            "s200:f", "s201:2nd", "s202:3rd", "s203:rf", "s500:f d", "s510:wd d", "s511:wd d", "s512:wd d",
            "s900:n", "s901:n", "s902:shw",
        ];
        if (name.Contains("Avenue_STRUCT", StringComparison.OrdinalIgnoreCase))
            return Pairs(structural);
        if (name.Contains("Construction_Bid_Set", StringComparison.OrdinalIgnoreCase))
        {
            return Pairs(
            [
                "g000:n", "g001:n", "g002:wt", "g003:fr n", "g004:df", "g005:wt", "a200:u sc", "a201:fl pl",
                "a202:1st", "a203:2nd", "a204:3rd", "a205:wt pl", "a250:rf", "a400:el", "a500:elev sec",
                "a510:str sec", "a600:d sec", "a700:jamb d", "a800:u", "a900:f", "a1000:w d sc", "a1100:sc",
                "a1101:f", "a1102:f", "a1103:f", "a1104:f", .. structural,
            ]);
        }
        if (name.Contains("Structural Permit", StringComparison.OrdinalIgnoreCase))
            return Pairs("s5.1:d", "s6.1:d", "s7.1:d");
        return new Dictionary<string, string>();
    }

    private static IReadOnlyDictionary<string, string> Pairs(params string[] pairs) =>
        pairs.Select(pair => pair.Split(':', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : "", StringComparer.OrdinalIgnoreCase);

    private static void WriteGoldenFailure(string pdf, string key, string expected, string actual) =>
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "golden_mismatch",
            pdf = Path.GetFileName(pdf),
            key,
            expected,
            actual,
        }));

    private static void WriteError(string pdf, int pageIndex, string error) =>
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "error",
            pdf = Path.GetFileName(pdf),
            page = pageIndex + 1,
            error,
        }));
}
