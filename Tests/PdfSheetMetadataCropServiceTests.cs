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
                SheetTitleRect = PdfSheetMetadataCropService.RegionFromRect(new SKRect(360, 690, 505, 745)),
                ScaleRect = PdfSheetMetadataCropService.RegionFromRect(new SKRect(420, 650, 600, 680)),
            };

            PdfSheetMetadataCropService.SaveTemplate(job, template);
            PdfSheetMetadataCropTemplate loaded = PdfSheetMetadataCropService.LoadTemplate(job)
                ?? throw new InvalidOperationException("template missing");

            AssertEqual("A101", loaded.SourcePageName, "source page");
            AssertEqual("612", loaded.PageWidthPt.ToString("0"), "page width");
            AssertEqual("510", loaded.SheetNumberRect.Left.ToString("0"), "sheet number left");
            AssertEqual("360", loaded.SheetTitleRect.Left.ToString("0"), "sheet title left");
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

        var titleOnly = new PdfSheetMetadataCropTemplate
        {
            SheetTitleRect = PdfSheetMetadataCropService.RegionFromRect(new SKRect(2, 3, 30, 40)),
        };
        AssertTrue(PdfSheetMetadataCropService.HasUsableTemplate(titleOnly), "title region should be usable");

        var scaleOnly = new PdfSheetMetadataCropTemplate
        {
            ScaleRect = PdfSheetMetadataCropService.RegionFromRect(new SKRect(4, 5, 40, 50)),
        };
        AssertTrue(PdfSheetMetadataCropService.HasUsableTemplate(scaleOnly), "scale region should be usable");
    }

    public static void CropTemplateResolvesJobOverrideThenGlobal()
    {
        string globalPath = PdfSheetMetadataCropService.GlobalTemplatePath();
        try
        {
            if (File.Exists(globalPath))
                File.Delete(globalPath);
            var global = new PdfSheetMetadataCropTemplate
            {
                SheetTitleRect = PdfSheetMetadataCropService.RegionFromRect(new SKRect(9, 10, 40, 50)),
            };
            PdfSheetMetadataCropService.SaveGlobalTemplate(global);

            WithTempJob("Crop Resolution", job =>
            {
                AssertEqual("9", PdfSheetMetadataCropService.LoadTemplate(job)!.SheetTitleRect.Left.ToString("0"), "global fallback");
                var local = new PdfSheetMetadataCropTemplate
                {
                    SheetNumberRect = PdfSheetMetadataCropService.RegionFromRect(new SKRect(1, 2, 20, 30)),
                };
                PdfSheetMetadataCropService.SaveTemplate(job, local);
                AssertEqual("1", PdfSheetMetadataCropService.LoadTemplate(job)!.SheetNumberRect.Left.ToString("0"), "job override");
                AssertTrue(PdfSheetMetadataCropService.ClearJobTemplate(job), "job override should clear");
                AssertEqual("9", PdfSheetMetadataCropService.LoadTemplate(job)!.SheetTitleRect.Left.ToString("0"), "global after reset");
            });
        }
        finally
        {
            if (File.Exists(globalPath))
                File.Delete(globalPath);
        }
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
