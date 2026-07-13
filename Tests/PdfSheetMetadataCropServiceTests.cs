using OurPlanCore;
using SkiaSharp;

internal static class PdfSheetMetadataCropServiceTests
{
    public static void CropTemplateSaveLoadRoundTrips()
    {
        WithTempJob("Crop Template", job =>
        {
            var template = new PdfSheetMetadataCropTemplate
            {
                SourcePageName = "A101",
                SourcePageFolder = Path.Combine(job.PagesRoot, "A101"),
                PageWidthPt = 612,
                PageHeightPt = 792,
                SheetNumberRect = PdfSheetMetadataCropService.RegionFromRect(new SKRect(510, 690, 600, 745)),
                ScaleRect = PdfSheetMetadataCropService.RegionFromRect(new SKRect(420, 650, 600, 680)),
            };

            PdfSheetMetadataCropService.SaveTemplate(job, template);
            PdfSheetMetadataCropTemplate loaded = PdfSheetMetadataCropService.LoadTemplate(job)
                ?? throw new InvalidOperationException("template missing");

            AssertEqual("A101", loaded.SourcePageName, "source page");
            AssertEqual("612", loaded.PageWidthPt.ToString("0"), "page width");
            AssertEqual("510", loaded.SheetNumberRect.Left.ToString("0"), "sheet number left");
            AssertEqual("680", loaded.ScaleRect.Bottom.ToString("0"), "scale bottom");
            AssertTrue(PdfSheetMetadataCropService.HasUsableTemplate(loaded), "loaded template should be usable");
        });
    }

    public static void CropTemplateUsableWhenEitherRegionExists()
    {
        AssertTrue(!PdfSheetMetadataCropService.HasUsableTemplate(null), "null template should not be usable");
        AssertTrue(!PdfSheetMetadataCropService.HasUsableTemplate(new PdfSheetMetadataCropTemplate()), "empty template should not be usable");

        var sheetOnly = new PdfSheetMetadataCropTemplate
        {
            SheetNumberRect = PdfSheetMetadataCropService.RegionFromRect(new SKRect(1, 2, 20, 30)),
        };
        AssertTrue(PdfSheetMetadataCropService.HasUsableTemplate(sheetOnly), "sheet region should be usable");

        var scaleOnly = new PdfSheetMetadataCropTemplate
        {
            ScaleRect = PdfSheetMetadataCropService.RegionFromRect(new SKRect(4, 5, 40, 50)),
        };
        AssertTrue(PdfSheetMetadataCropService.HasUsableTemplate(scaleOnly), "scale region should be usable");
    }

    private static void WithTempJob(string name, Action<OurPlanCoreJob> action)
    {
        string parent = Path.Combine(Path.GetTempPath(), $"ourplancore-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(parent);
        try
        {
            OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(parent, name);
            action(job);
        }
        finally
        {
            try
            {
                Directory.Delete(parent, recursive: true);
            }
            catch
            {
                // Test cleanup should not mask the real assertion failure.
            }
        }
    }

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
