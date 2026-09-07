using OurPlanCore;
using SkiaSharp;
using System.Text.Json;

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

    public static void CropProfileTemplatesRoundTripIndependently()
    {
        WithTempJob("Crop Profile Round Trip", job =>
        {
            PdfSheetMetadataCropService.SaveTemplate(job, TemplateWithLeft(11));
            PdfSheetMetadataCropService.SaveTemplate(
                job,
                PdfSheetMetadataCropProfile.Architectural,
                TemplateWithLeft(22));
            PdfSheetMetadataCropService.SaveTemplate(
                job,
                PdfSheetMetadataCropProfile.Structural,
                TemplateWithLeft(33));

            AssertEqual(
                "sheet_metadata_crop_template.json",
                Path.GetFileName(PdfSheetMetadataCropService.TemplatePath(job)),
                "legacy default path");
            AssertEqual(
                "sheet_metadata_crop_template_a.json",
                Path.GetFileName(PdfSheetMetadataCropService.TemplatePath(job, PdfSheetMetadataCropProfile.Architectural)),
                "architectural path");
            AssertEqual(
                "sheet_metadata_crop_template_s.json",
                Path.GetFileName(PdfSheetMetadataCropService.TemplatePath(job, PdfSheetMetadataCropProfile.Structural)),
                "structural path");
            AssertTemplateLeft(11, PdfSheetMetadataCropService.LoadJobTemplate(job), "legacy default load");
            AssertTemplateLeft(
                22,
                PdfSheetMetadataCropService.LoadExactJobTemplate(job, PdfSheetMetadataCropProfile.Architectural),
                "exact architectural load");
            AssertTemplateLeft(
                33,
                PdfSheetMetadataCropService.LoadExactJobTemplate(job, PdfSheetMetadataCropProfile.Structural),
                "exact structural load");
            AssertTrue(
                PdfSheetMetadataCropService.HasDedicatedTemplate(job, PdfSheetMetadataCropProfile.Architectural),
                "architectural profile should be dedicated");

            PdfSheetMetadataCropCatalog catalog = PdfSheetMetadataCropService.LoadCatalog(job);
            AssertTemplateLeft(11, catalog.TemplateFor(PdfSheetMetadataCropProfile.Default), "catalog default");
            AssertTemplateLeft(22, catalog.TemplateFor(PdfSheetMetadataCropProfile.Architectural), "catalog architectural");
            AssertTemplateLeft(33, catalog.TemplateFor(PdfSheetMetadataCropProfile.Structural), "catalog structural");
            string catalogJson = JsonSerializer.Serialize(catalog);
            AssertTrue(catalogJson.Contains("\"schema_version\"", StringComparison.Ordinal), "catalog schema field");
            AssertTrue(catalogJson.Contains("\"architectural\"", StringComparison.Ordinal), "catalog architectural field");
            AssertTrue(catalogJson.Contains("\"structural\"", StringComparison.Ordinal), "catalog structural field");

            AssertTrue(PdfSheetMetadataCropService.ClearAllJobTemplates(job), "all profile templates should clear");
            AssertTrue(!File.Exists(PdfSheetMetadataCropService.TemplatePath(job)), "default file should clear");
            AssertTrue(
                !File.Exists(PdfSheetMetadataCropService.TemplatePath(job, PdfSheetMetadataCropProfile.Architectural)),
                "architectural file should clear");
            AssertTrue(
                !File.Exists(PdfSheetMetadataCropService.TemplatePath(job, PdfSheetMetadataCropProfile.Structural)),
                "structural file should clear");
        });
    }

    public static void CropProfileResolutionUsesExactThenDefaultFallback()
    {
        string globalDefaultPath = PdfSheetMetadataCropService.GlobalTemplatePath();
        string globalArchitecturalPath = PdfSheetMetadataCropService.GlobalTemplatePath(
            PdfSheetMetadataCropProfile.Architectural);
        try
        {
            DeleteIfPresent(globalDefaultPath);
            DeleteIfPresent(globalArchitecturalPath);
            PdfSheetMetadataCropService.SaveGlobalTemplate(TemplateWithLeft(40));

            WithTempJob("Crop Profile Resolution", job =>
            {
                AssertTemplateLeft(
                    40,
                    PdfSheetMetadataCropService.LoadTemplate(job, PdfSheetMetadataCropProfile.Architectural),
                    "global default fallback");

                PdfSheetMetadataCropService.SaveTemplate(job, TemplateWithLeft(30));
                AssertTemplateLeft(
                    30,
                    PdfSheetMetadataCropService.LoadTemplate(job, PdfSheetMetadataCropProfile.Architectural),
                    "job default fallback");

                PdfSheetMetadataCropService.SaveGlobalTemplate(
                    PdfSheetMetadataCropProfile.Architectural,
                    TemplateWithLeft(20));
                AssertTemplateLeft(
                    20,
                    PdfSheetMetadataCropService.LoadTemplate(job, PdfSheetMetadataCropProfile.Architectural),
                    "exact global before job default");

                PdfSheetMetadataCropService.SaveTemplate(
                    job,
                    PdfSheetMetadataCropProfile.Architectural,
                    TemplateWithLeft(10));
                AssertTemplateLeft(
                    10,
                    PdfSheetMetadataCropService.LoadTemplate(job, PdfSheetMetadataCropProfile.Architectural),
                    "exact job before exact global");
                AssertTemplateLeft(
                    30,
                    PdfSheetMetadataCropService.LoadTemplate(job, PdfSheetMetadataCropProfile.Structural),
                    "structural falls back to job default");

                PdfSheetMetadataCropCatalog catalog = PdfSheetMetadataCropService.LoadCatalog(job);
                AssertTemplateLeft(30, catalog.Default, "resolved catalog default");
                AssertTemplateLeft(10, catalog.Architectural, "resolved catalog architectural");
                AssertTemplateLeft(30, catalog.Structural, "resolved catalog structural fallback");
            });
        }
        finally
        {
            DeleteIfPresent(globalDefaultPath);
            DeleteIfPresent(globalArchitecturalPath);
        }
    }

    public static void CropProfileResolverPrioritizesMetadataAndUsesPageHeuristics()
    {
        var structuralMetadata = new PdfSheetMetadata
        {
            SheetLabel = "S2.01",
            SheetKey = "A9.99",
        };
        AssertEqual(
            PdfSheetMetadataCropProfile.Structural.ToString(),
            PdfSheetMetadataCropService.ResolveProfile(
                structuralMetadata,
                "A1.04 Floor Plan",
                @"C:\Plans\Architectural.pdf").ToString(),
            "metadata label should win");

        var architecturalMetadata = new PdfSheetMetadata { SheetKey = "A1.04" };
        AssertEqual(
            PdfSheetMetadataCropProfile.Architectural.ToString(),
            PdfSheetMetadataCropService.ResolveProfile(
                architecturalMetadata,
                "S1.01 Foundation",
                @"C:\Plans\Structural.pdf").ToString(),
            "metadata key should win over page and PDF names");

        AssertEqual(
            PdfSheetMetadataCropProfile.Structural.ToString(),
            PdfSheetMetadataCropService.ResolveProfile(
                null,
                "S3.10 Roof Framing Plan",
                @"C:\Plans\Architectural.pdf").ToString(),
            "page sheet key heuristic");
        AssertEqual(
            PdfSheetMetadataCropProfile.Structural.ToString(),
            PdfSheetMetadataCropService.ResolveProfile(
                null,
                "Second Floor Framing Plan",
                @"C:\Plans\Architectural.pdf").ToString(),
            "structural word wins within page name");
        AssertEqual(
            PdfSheetMetadataCropProfile.Architectural.ToString(),
            PdfSheetMetadataCropService.ResolveProfile(
                null,
                "Page 14",
                @"C:\Plans\Architectural Floor Plans.pdf").ToString(),
            "architectural PDF filename heuristic");
        AssertEqual(
            PdfSheetMetadataCropProfile.Structural.ToString(),
            PdfSheetMetadataCropService.ResolveProfile(
                null,
                "Page 14",
                @"C:\Plans\885 Westminster Pricing Set updated Structurals.pdf").ToString(),
            "plural Structurals PDF filename heuristic");
        AssertEqual(
            PdfSheetMetadataCropProfile.Default.ToString(),
            PdfSheetMetadataCropService.ResolveProfile(null, "Page 14", @"C:\Plans\Permit Set.pdf").ToString(),
            "unknown discipline fallback");
        AssertEqual(
            "Architectural (A)",
            PdfSheetMetadataCropService.ProfileDisplayName(PdfSheetMetadataCropProfile.Architectural),
            "architectural display name");
        AssertEqual(
            "Structural (S)",
            PdfSheetMetadataCropService.ProfileDisplayName(PdfSheetMetadataCropProfile.Structural),
            "structural display name");
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

    private static PdfSheetMetadataCropTemplate TemplateWithLeft(float left) =>
        new()
        {
            SourcePageName = $"Sheet {left:0}",
            SheetNumberRect = PdfSheetMetadataCropService.RegionFromRect(
                new SKRect(left, 10, left + 20, 30)),
            ScaleRect = PdfSheetMetadataCropService.RegionFromRect(
                new SKRect(left, 40, left + 20, 60)),
        };

    private static void AssertTemplateLeft(
        float expected,
        PdfSheetMetadataCropTemplate? template,
        string message)
    {
        if (template == null)
            throw new InvalidOperationException($"{message}: template missing");
        AssertEqual(expected.ToString("0"), template.SheetNumberRect.Left.ToString("0"), message);
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
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
