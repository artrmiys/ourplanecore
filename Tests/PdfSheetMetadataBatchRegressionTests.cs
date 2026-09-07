using OurPlanCore;

internal static class PdfSheetMetadataBatchRegressionTests
{
    public static void FailedBatchStopsWithoutPerPageRetry()
    {
        string source = File.ReadAllText(RepoFile("Models/PdfSheetMetadataService.Batch.cs"));
        string failureMethod = Slice(
            source,
            "private static void RecordChunkFailure(",
            "private sealed class SheetMetaBatchRequest");

        AssertContains(failureMethod, "page, false, null, error, false", "failed page result");
        AssertDoesNotContain(failureMethod, "TryAnalyzePage", "per-page metadata retry");
        AssertDoesNotContain(source, "AnalyzeChunkFallback", "legacy unbounded fallback method");
    }

    public static void BatchIsBoundedAndCarriesProfileCatalog()
    {
        string batch = File.ReadAllText(RepoFile("Models/PdfSheetMetadataService.Batch.cs"));
        string single = File.ReadAllText(RepoFile("Models/PdfSheetMetadataService.cs"));
        string helper = File.ReadAllText(RepoFile("Tools/pdf_layers_helper.py"));

        AssertContains(batch, "SheetMetadataBatchChunkSize = 4", "bounded chunk size");
        AssertContains(batch, "CropTemplates = cropTemplates", "batch profile catalog request");
        AssertContains(single, "CropTemplates = job == null ? null", "single-page profile catalog request");
        AssertContains(helper, "\"crop_templates\": req.get(\"crop_templates\")", "batch helper catalog forwarding");
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (start < 0 || end <= start)
            throw new InvalidOperationException($"Could not locate source slice: {startMarker}");
        return source[start..end];
    }

    private static string RepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ourplancore.csproj")))
                return Path.Combine(directory.FullName, relativePath);
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the OurPlanCore repository root.");
    }

    private static void AssertContains(string source, string expected, string message)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: missing '{expected}'.");
    }

    private static void AssertDoesNotContain(string source, string unexpected, string message)
    {
        if (source.Contains(unexpected, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: found forbidden '{unexpected}'.");
    }
}
