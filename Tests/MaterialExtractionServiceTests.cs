using OurPlaneCore;

internal static class MaterialExtractionServiceTests
{
    public static void WritesRowsAndSummaryCsvs()
    {
        string outputFolder = Path.Combine(Path.GetTempPath(), "opc_material_tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = new MaterialExtractionResult
            {
                Rows =
                [
                    new MaterialExtractionRow
                    {
                        Category = "Walls",
                        MaterialFamily = "Studs",
                        Item = "2x6 wood studs",
                        Size = "2x6",
                        Sheet = "A101",
                        PdfPage = 4,
                        PdfFile = "plans.pdf",
                        SourceType = "text",
                        Confidence = 0.82,
                        RawText = "2x6 wood studs",
                    },
                    new MaterialExtractionRow
                    {
                        Category = "Walls",
                        MaterialFamily = "Studs",
                        Item = "2x6 wood studs at exterior walls",
                        Size = "2x6",
                        Sheet = "A102",
                        PdfPage = 5,
                        PdfFile = "plans.pdf",
                        SourceType = "schedule",
                        Confidence = 0.90,
                        ReviewFlags = ["schedule_reconstructed_from_text"],
                        RawText = "2x6 wood studs at exterior walls",
                    },
                ],
            };

            (string rowsCsv, string summaryCsv) = MaterialExtractionService.WriteReviewCsvs(result, outputFolder);

            AssertTrue(File.Exists(rowsCsv), "rows csv should exist");
            AssertTrue(File.Exists(summaryCsv), "summary csv should exist");
            string rowsText = File.ReadAllText(rowsCsv);
            string summaryText = File.ReadAllText(summaryCsv);
            AssertTrue(rowsText.Contains("2x6 wood studs", StringComparison.Ordinal), "rows csv keeps evidence text");
            AssertTrue(summaryText.Contains("Walls,Studs,2x6", StringComparison.Ordinal), "summary csv groups material");
            AssertTrue(summaryText.Contains(",2,", StringComparison.Ordinal), "summary csv evidence count");
        }
        finally
        {
            TryDeleteDirectory(outputFolder);
        }
    }

    public static void WritesMaterialReportPdf()
    {
        string outputFolder = Path.Combine(Path.GetTempPath(), "opc_material_report_tests", Guid.NewGuid().ToString("N"));
        string pdfPath = Path.Combine(outputFolder, "materials_report.pdf");
        try
        {
            var result = new MaterialExtractionResult
            {
                JobName = "Material Report Test",
                InputFiles = [new MaterialInputFile { PdfName = "plans.pdf", TotalPages = 12 }],
                Stats = new MaterialExtractionStats { PagesRead = 12, PagesOcr = 1 },
                Quality = new MaterialQualitySummary
                {
                    RowsTotal = 2,
                    HighConfidenceRows = 2,
                    TakeoffReadyRows = 1,
                    ReviewRows = 1,
                    SchedulesTotal = 1,
                    PdfPlumberAvailable = true,
                    OcrAvailable = true,
                },
                Schedules = [new MaterialSchedule { Title = "Beam Schedule" }],
                Rows =
                [
                    new MaterialExtractionRow
                    {
                        Category = "Framing",
                        MaterialFamily = "Beams / Headers",
                        Size = "2x10",
                        PdfFile = "plans.pdf",
                        PdfPage = 2,
                        Sheet = "S101",
                        SourceType = "schedule",
                        Confidence = 0.90,
                        RawText = "2x10 header",
                    },
                ],
            };

            int pageCount = MaterialReportPageService.WriteReportPdf(result, pdfPath);

            AssertTrue(pageCount == 1, "report pdf should contain only the clean report page for this small result");
            AssertTrue(File.Exists(pdfPath), "report pdf should exist");
            using FileStream stream = File.OpenRead(pdfPath);
            byte[] header = new byte[5];
            _ = stream.Read(header, 0, header.Length);
            AssertTrue(System.Text.Encoding.ASCII.GetString(header) == "%PDF-", "report pdf header");
        }
        finally
        {
            TryDeleteDirectory(outputFolder);
        }
    }

    public static void MaterialReportFirstPageUsesPageDetailFormat()
    {
        var result = new MaterialExtractionResult
        {
            JobName = "Report Format Test",
            Rows =
            [
                new MaterialExtractionRow
                {
                    Category = "Walls",
                    Subcategory = "Sheathing",
                    MaterialFamily = "CDX Plywood",
                    Thickness = "1/2\"",
                    Sheet = "A101",
                    SectionRef = "3/A101",
                    RawText = "1/2\" CDX plywood wall sheathing detail 3/A101",
                },
                new MaterialExtractionRow
                {
                    Category = "Walls",
                    Subcategory = "Sheathing",
                    MaterialFamily = "ZIP System",
                    Thickness = "7/16\"",
                    Sheet = "S102",
                    SectionRef = "3/S102",
                    RawText = "7/16\" Zip wall sheathing detail 3/S102",
                },
                new MaterialExtractionRow
                {
                    Category = "Insulation",
                    MaterialFamily = "Insulation",
                    Thickness = "1 1/2\"",
                    Sheet = "S102",
                    SectionRef = "3/S102",
                    RawText = "1 1/2\" Insulation at wall detail 3/S102",
                },
                new MaterialExtractionRow
                {
                    Category = "Framing",
                    MaterialFamily = "Subfloor",
                    Thickness = "3/4\"",
                    RawText = "3/4\" T&G subfloor",
                },
                new MaterialExtractionRow
                {
                    Category = "Walls",
                    Subcategory = "Sheathing",
                    MaterialFamily = "Plywood",
                    Thickness = "1/2\"",
                    Sheet = "A201",
                    SectionRef = "2/A201",
                    RawText = "1/2\" sheathing",
                },
            ],
        };

        IReadOnlyList<MaterialReportDisplaySection> sections = MaterialReportPageService.BuildFirstPageSections(result);
        MaterialReportDisplaySection wall = sections.First(section => section.Title == "Wall sheathing");
        MaterialReportDisplaySection floor = sections.First(section => section.Title == "Floor sheathing");

        AssertTrue(wall.Lines.Contains("page A101 - detail 3/A101 - 1/2\" CDX Ply"), "wall CDX line");
        AssertTrue(wall.Lines.Contains("page S102 - detail 3/S102 - 7/16\" Zip"), "wall Zip line");
        AssertTrue(wall.Lines.Contains("page S102 - detail 3/S102 - 1 1/2\" Insulation"), "wall insulation line");
        AssertTrue(!wall.Lines.Any(line => line.Contains("A201", StringComparison.Ordinal)), "generic sheathing line should be skipped");
        AssertTrue(floor.Lines.Contains("pdf page 0 - 3/4\" TG") || floor.Lines.Contains("page unknown - 3/4\" TG"), "floor TG line");
    }

    public static void MaterialReportBuildsScheduleLegends()
    {
        var result = new MaterialExtractionResult
        {
            Schedules =
            [
                new MaterialSchedule
                {
                    Title = "BEAM SCHEDULE",
                    Sheet = "S102",
                    Rows =
                    [
                        new MaterialScheduleRow { Mark = "H1", Qty = "3", Size = "2x8" },
                        new MaterialScheduleRow { Mark = "R2", Size = "2x10" },
                        new MaterialScheduleRow { Mark = "B1", Qty = "2", Size = "1 3/4 x 11 7/8", Material = "LVL" },
                        new MaterialScheduleRow { RawText = "see general structural notes" },
                        new MaterialScheduleRow { Mark = "N1", RawText = "general note verify field dimensions" },
                    ],
                },
            ],
        };

        IReadOnlyList<MaterialReportDisplaySection> legends = MaterialReportPageService.BuildScheduleLegendSections(result);

        AssertTrue(legends.Count == 1, "one schedule legend");
        AssertTrue(legends[0].Title == "Beam schedule - page S102", "schedule title");
        AssertTrue(legends[0].Lines.Contains("H1 - (3) 2x8"), "H1 legend line");
        AssertTrue(legends[0].Lines.Contains("R2 - 2x10"), "R2 legend line");
        AssertTrue(legends[0].Lines.Contains("B1 - (2) 1 3/4 x 11 7/8 LVL"), "B1 legend line");
        AssertTrue(legends[0].Lines.Count == 3, "no-mark and general-note schedule rows should be skipped");
    }

    public static void MaterialReportBuildsCopyableNoteAnnotation()
    {
        var result = new MaterialExtractionResult
        {
            JobName = "Copy Note Test",
            Rows =
            [
                new MaterialExtractionRow
                {
                    Category = "Walls",
                    MaterialFamily = "CDX Plywood",
                    Thickness = "1/2\"",
                    Sheet = "A101",
                    SectionRef = "3/A101",
                    RawText = "1/2\" CDX plywood wall sheathing",
                },
            ],
            Schedules =
            [
                new MaterialSchedule
                {
                    Title = "BEAM SCHEDULE",
                    Sheet = "S102",
                    Rows = [new MaterialScheduleRow { Mark = "H1", Qty = "3", Size = "2x8" }],
                },
            ],
        };

        string text = MaterialReportPageService.BuildReportNoteText(result);
        var page = new PageInfo { Name = "Materials Report", FolderPath = "page-folder", ScaleMetersPerPt = 0.001 };
        PageAnnotation annotation = MaterialReportPageService.BuildCopyableReportNoteAnnotation(page, text);

        AssertTrue(annotation.Kind == "note", "report annotation should be a note");
        AssertTrue(annotation.Text.Contains("Wall sheathing:", StringComparison.Ordinal), "note has wall section");
        AssertTrue(!annotation.Text.Contains("Floor sheathing:", StringComparison.Ordinal), "note skips empty material sections");
        AssertTrue(annotation.Text.Contains("page A101 - detail 3/A101 - 1/2\" CDX Ply", StringComparison.Ordinal), "note has material line");
        AssertTrue(annotation.Text.Contains("Beam schedule - page S102:", StringComparison.Ordinal), "note has schedule title");
        AssertTrue(annotation.Text.Contains("H1 - (3) 2x8", StringComparison.Ordinal), "note has schedule row");
        AssertTrue(annotation.Points.Count == 4, "note rectangle should have four corners");
    }

    public static void UniqueSourcePdfsSkipsGeneratedMaterialReports()
    {
        string root = Path.Combine(Path.GetTempPath(), "opc_material_filter_tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string sourcePdf = Path.Combine(root, "plans.pdf");
            string reportPdf = Path.Combine(root, "materials_report.pdf");
            File.WriteAllText(sourcePdf, "%PDF-1.4 source");
            File.WriteAllText(reportPdf, "%PDF-1.4 report");

            IReadOnlyList<string> pdfs = MaterialExtractionService.UniqueSourcePdfs(
                [
                    new PageInfo { Name = "A101", PdfPath = sourcePdf },
                    new PageInfo { Name = "Materials Report", PdfPath = reportPdf },
                ]);

            AssertTrue(pdfs.Count == 1, "generated material report should be skipped");
            AssertTrue(pdfs[0].EndsWith("plans.pdf", StringComparison.OrdinalIgnoreCase), "source pdf should remain");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    public static void BundledToolResolverFindsExtractedNestedFiles()
    {
        string? previousRoot = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
        string root = Path.Combine(Path.GetTempPath(), "opc_bundle_tool_tests", Guid.NewGuid().ToString("N"));
        try
        {
            string extractedTools = Path.Combine(root, "ourplanecore", "bundlehash", "Tools");
            Directory.CreateDirectory(extractedTools);
            string helperPath = Path.Combine(extractedTools, "extracted_only_probe.py");
            File.WriteAllText(helperPath, "# test helper");
            Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", root);

            string resolved = BundledToolPathResolver.ResolveFile(Path.Combine("Tools", "extracted_only_probe.py"));

            AssertTrue(
                string.Equals(Path.GetFullPath(helperPath), resolved, StringComparison.OrdinalIgnoreCase),
                "resolver should find files extracted from a single-file bundle");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", previousRoot);
            TryDeleteDirectory(root);
        }
    }

    public static void ProjectPublishesToolsAsSidecarContent()
    {
        string project = File.ReadAllText("ourplanecore.csproj");

        AssertTrue(
            project.Contains("Update=\"Tools\\**\\*\"", StringComparison.Ordinal) &&
            project.Contains("ExcludeFromSingleFile=\"true\"", StringComparison.Ordinal),
            "published package must keep PyMuPDF helper/python files as sidecar content next to the compressed exe");
    }

    public static void BundledPythonRuntimeResolvesPackagedPython()
    {
        string pythonExecutable = BundledPythonRuntime.ResolveExecutable();

        AssertTrue(
            pythonExecutable.Equals("python", StringComparison.OrdinalIgnoreCase) || File.Exists(pythonExecutable),
            "python runtime should resolve to packaged python or system fallback");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
