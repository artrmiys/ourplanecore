using System;
using System.Globalization;
using System.IO;
using SkiaSharp;

namespace OurPlanCore;

public static class SampleJobService
{
    private const double SampleScaleMetersPerPt = ViewportConstants.PdfPointMeters * 96.0;

    public static string DefaultJobsRoot
    {
        get
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrWhiteSpace(documents)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OurPlanCore Jobs")
                : Path.Combine(documents, "OurPlanCore Jobs");
        }
    }

    public static OurPlanCoreJob CreateSampleJob(string parentDir)
    {
        Directory.CreateDirectory(parentDir);
        string jobName = UniqueJobName(parentDir, "OurPlanCore Guide Sample");
        OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(parentDir, jobName);

        string tempPdf = Path.Combine(Path.GetTempPath(), $"ourplancore_guide_sample_{Guid.NewGuid():N}.pdf");
        try
        {
            SampleJobGuideBuilder.WriteGuidePdf(tempPdf);
            string guideFolder = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, SampleJobGuideBuilder.GuideFolderName);
            IReadOnlyList<PageInfo> pages = OurPlanCoreJobStore.ImportPdf(
                job,
                tempPdf,
                SampleJobGuideBuilder.PageNames,
                guideFolder);

            PageInfo planPage = pages[^1];
            planPage.ScaleMetersPerPt = SampleScaleMetersPerPt;
            OurPlanCoreJobStore.SavePageScale(planPage.FolderPath, SampleScaleMetersPerPt);

            CreateSampleTakeoffs(job, planPage);
            CreateSampleAnnotations(planPage);
            AddSampleObservations(job, planPage);
            WriteSampleMaterials(job, planPage);
            SampleThreeDModelBuilder.BuildAndSave(job, planPage, SampleScaleMetersPerPt);
            SampleJobGuideBuilder.WriteGuideFiles(job);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPdf))
                    File.Delete(tempPdf);
            }
            catch
            {
            }
        }

        return OurPlanCoreJobStore.LoadJob(job.RootPath);
    }

    private static void CreateSampleTakeoffs(OurPlanCoreJob job, PageInfo page)
    {
        var g = SamplePlanGeometry.Instance;

        // Folders. "1st" levels under sqfts/walls let the 3D Auto builder lift walls and slabs.
        string sqftsFolder = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
        string sqfts1st = OurPlanCoreJobStore.CreateTakeoffFolder(job, sqftsFolder, "1st");
        string wallsFolder = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "walls");
        string walls1st = OurPlanCoreJobStore.CreateTakeoffFolder(job, wallsFolder, "1st");
        string openingsFolder = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "openings");
        string framingFolder = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "framing");
        string areasFolder = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "areas");
        string roofFolder = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "rf");

        // AREA - whole floor footprint (feeds the 3D 1st-floor slab).
        CreateMeasuredTakeoff(
            job, sqfts1st, page, "Floor Area", "#4CAF50", "area", 6.25,
            "Area takeoff: whole interior footprint. Drives sqft totals and the 3D 1st-floor slab.",
            null,
            new Measurement { Name = "Interior footprint", Points = [.. g.FloorAreaPolygon] });

        // AREA + CUT - demonstrates a subtracted hole (Area Cut tool).
        CreateMeasuredTakeoff(
            job, areasFolder, page, "Living w/ Cut", "#26A69A", "area", 0,
            "Area Cut demo: a hole is subtracted from the polygon (try the Cut tool, shortcut X).",
            null,
            new Measurement
            {
                Name = "Living minus opening",
                Points = [.. g.LivingRoomPolygon],
                Holes = [[new SKPoint(220, 340), new SKPoint(300, 340), new SKPoint(300, 410), new SKPoint(220, 410)]],
            });

        // LINE with SECTIONS - four exterior wall runs in one item (feeds 3D walls).
        CreateMeasuredTakeoff(
            job, walls1st, page, "Exterior Walls", "#2196F3", "line", 14.50,
            "Line takeoff with four sections (one per wall run), 9 ft tall. Auto-builds the 3D walls.",
            null,
            WallRun("North wall", g, 165, 145, 627, 145),
            WallRun("East wall", g, 627, 145, 627, 467),
            WallRun("South wall", g, 627, 467, 165, 467),
            WallRun("West wall", g, 165, 467, 165, 145));

        // LINE - beam (Beam tool result).
        CreateMeasuredTakeoff(
            job, framingFolder, page, "Ridge Beam", "#5E35B1", "line", 22.0,
            "Line/Beam takeoff: a single structural beam along the building center.",
            null,
            new Measurement { Name = "Ridge beam", Points = [new SKPoint(170, 300), new SKPoint(622, 300)] });

        // JOIST AREA - locked direction (Joist Area tool).
        CreateMeasuredTakeoff(
            job, framingFolder, page, "Floor Joists 2x10 @16", "#009688", "area", 3.95,
            "Joist Area takeoff with a locked direction; total is joist linear feet, not area.",
            item =>
            {
                item.IsJoistTakeoff = true;
                item.JoistType = "2x10";
                item.JoistSpacingInches = 16;
                item.JoistDirectionDegrees = 0;
                item.JoistLengthRounding = JoistTakeoffCalculator.RoundingNearestFoot;
                item.JoistShowLabels = true;
            },
            new Measurement
            {
                Name = "Living joists",
                JoistDirectionLocked = true,
                JoistDirectionDegrees = 0,
                Points = [.. g.LivingRoomPolygon],
            });

        // COUNT - doors, with the Cross symbol, one point per drawn door.
        CreateMeasuredTakeoff(
            job, openingsFolder, page, "Doors", "#FF9800", "point", 350.0,
            "Count takeoff (Cross symbol): one marker on every door in the plan.",
            item => item.CountSymbol = CountDisplaySymbol.Cross,
            new Measurement { Name = "Door count", Points = [.. g.DoorPoints] });

        // COUNT - windows, with the Square symbol.
        CreateMeasuredTakeoff(
            job, openingsFolder, page, "Windows", "#7E57C2", "point", 285.0,
            "Count takeoff (Square symbol): one marker on every window in the plan.",
            item => item.CountSymbol = CountDisplaySymbol.Square,
            new Measurement { Name = "Window count", Points = [.. g.WindowPoints] });

        // AREA - roof footprint (drives the 3D roof base / Generate Roof).
        CreateMeasuredTakeoff(
            job, roofFolder, page, "Roof Area", "#8BC34A", "area", 7.10,
            "Roof footprint area. Use 3D > Roof Base + Generate Roof (or Auto) to build the pitched roof.",
            null,
            new Measurement { Name = "Roof footprint", Points = [.. g.RoofFootprintPolygon] });

        // LINE - roof guide edges for the 3D roof workflow.
        CreateMeasuredTakeoff(
            job, roofFolder, page, "Front Eave Guide", "#E53935", "line", 0,
            "Roof guide: front eave edge. Select it in 3D > Select Edge to set eave pitch.",
            null,
            new Measurement { Name = "Front eave", Points = [new SKPoint(g.OuterL, g.OuterB), new SKPoint(g.OuterR, g.OuterB)] });

        CreateMeasuredTakeoff(
            job, roofFolder, page, "Left Rake Guide", "#C2185B", "line", 0,
            "Roof guide: left rake edge. Mark it as Rake for a gable end.",
            null,
            new Measurement { Name = "Left rake", Points = [new SKPoint(g.OuterL, g.OuterB), new SKPoint(g.OuterL, g.OuterT)] });
    }

    private static Measurement WallRun(string name, SamplePlanGeometry g, float x1, float y1, float x2, float y2) =>
        new() { Name = name, Points = [new SKPoint(x1, y1), new SKPoint(x2, y2)] };

    private static TakeoffItem CreateMeasuredTakeoff(
        OurPlanCoreJob job,
        string parentFolder,
        PageInfo page,
        string name,
        string color,
        string measurementType,
        double unitPrice,
        string notes,
        Action<TakeoffItem>? configure,
        params Measurement[] measurements)
    {
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, parentFolder, name, color, measurementType);
        item.UnitPrice = unitPrice;
        item.Notes = notes;
        configure?.Invoke(item);

        foreach (Measurement measurement in measurements)
        {
            measurement.MType = measurementType;
            measurement.Color = color;
            measurement.PageFolder = page.FolderPath;
            measurement.TakeoffFolder = item.FolderPath;
            measurement.ScaleMetersPerPt = SampleScaleMetersPerPt;
            measurement.CountSymbol = CountDisplaySymbol.Normalize(item.CountSymbol);
            item.Measurements.Add(measurement);
        }

        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        OurPlanCoreJobStore.SaveTakeoffItem(item);
        return item;
    }

    private static void CreateSampleAnnotations(PageInfo page)
    {
        // One clean note demonstrating the Annotation tool, pinned in the left margin so it
        // does not overlap the drawing or the takeoff overlays.
        OurPlanCoreJobStore.SavePageAnnotations(
            page.FolderPath,
            [
                new PageAnnotation
                {
                    Kind = "note",
                    Text = "Start here: select a takeoff on the right, then try Record, Export, Takeoff Manager, and 3D.",
                    Color = "#1565C0",
                    PageFolder = page.FolderPath,
                    ScaleMetersPerPt = SampleScaleMetersPerPt,
                    Points = [new SKPoint(60, 250)],
                },
            ]);
    }

    private static void AddSampleObservations(OurPlanCoreJob job, PageInfo page)
    {
        // Preloaded AI observations so the AI Manager grid and the AI Inbox are not empty -
        // they show what the model produces without requiring a network call or API key.
        SmartContextStore.AddObservation(job, page, "scale",
            "Detected drawing scale 1/8\" = 1'-0\" from the title block.");
        SmartContextStore.AddObservation(job, page, "count",
            "Counted 6 door symbols and 4 window symbols on A101; compare with the Doors / Windows takeoffs.");
        SmartContextStore.AddObservation(job, page, "area",
            "Interior footprint reads about 780 SF across 5 rooms; verify against the Floor Area takeoff.");
        SmartContextStore.AddObservation(job, page, "note",
            "Exterior wall perimeter looks like a clean rectangle - good candidate for Auto 3D walls.");
    }

    private static void WriteSampleMaterials(OurPlanCoreJob job, PageInfo page)
    {
        // Preloaded material extraction example so the Materials tab shows a realistic summary
        // for the sample house without running the Python extractor.
        const string sheet = "A101";
        const string pdf = "A101 Sample Plan";
        int pageNo = 17;

        MaterialExtractionRow Row(string cat, string family, string item, string size, string qty, string unit, double conf, params string[] flags) =>
            new()
            {
                Id = Guid.NewGuid().ToString("N"),
                PdfFile = pdf,
                PdfPage = pageNo,
                Sheet = sheet,
                SourceType = "schedule",
                Category = cat,
                MaterialFamily = family,
                Item = item,
                Size = size,
                Qty = qty,
                Unit = unit,
                PageRef = sheet,
                ScheduleRef = cat == "Openings" ? (family == "Door" ? "Door Schedule" : "Window Schedule") : "",
                RawText = $"{item} {size} - {qty} {unit}",
                Confidence = conf,
                ReviewFlags = [.. flags],
            };

        var result = new MaterialExtractionResult
        {
            JobName = job.Name,
            InputFiles = [new MaterialInputFile { PdfName = pdf, SourcePath = page.PdfPath, TotalPages = pageNo }],
            Rows =
            [
                Row("Framing", "Lumber", "Exterior wall stud", "2x6", "196", "LF", 0.92),
                Row("Framing", "Lumber", "Floor joist", "2x10", "15", "EA", 0.89),
                Row("Framing", "Beam", "Ridge beam", "6x12", "47", "LF", 0.81),
                Row("Sheathing", "Plywood", "Wall sheathing", "1/2\"", "1,860", "SF", 0.85),
                Row("Roofing", "Shingles", "Asphalt shingle (cross-gable roof)", "-", "2,226", "SF", 0.83),
                Row("Openings", "Door", "Door unit", "3'-0\" x 6'-8\"", "6", "EA", 0.90),
                Row("Openings", "Window", "Window unit", "3'-0\" x 4'-0\"", "4", "EA", 0.90),
                Row("Concrete", "Slab", "Floor slab on grade", "4\"", "780", "SF", 0.74, "verify thickness"),
                Row("Insulation", "Batt", "Wall insulation", "R-21", "1,860", "SF", 0.70, "review assembly"),
                Row("Finishes", "Drywall", "Gypsum board", "1/2\"", "2,400", "SF", 0.76),
            ],
            Stats = new MaterialExtractionStats { PagesRead = pageNo, PagesOcr = 0, RowsTotal = 10, SchedulesTotal = 2 },
            Quality = new MaterialQualitySummary
            {
                RowsTotal = 10,
                HighConfidenceRows = 7,
                TakeoffReadyRows = 7,
                ReviewRows = 3,
                SchedulesTotal = 2,
                PdfPlumberAvailable = true,
                OcrAvailable = false,
            },
        };

        string outputFolder = MaterialExtractionService.OutputFolder(job);
        Directory.CreateDirectory(outputFolder);
        File.WriteAllText(
            MaterialExtractionService.LatestJsonPath(job),
            System.Text.Json.JsonSerializer.Serialize(result, OurPlanCoreJobStore.JsonOptions));
        try
        {
            MaterialExtractionService.WriteReviewCsvs(result, outputFolder);
        }
        catch
        {
            // CSV export is a convenience; the JSON drives the Materials grid.
        }
    }

    private static string UniqueJobName(string parentDir, string baseName)
    {
        string candidate = baseName;
        int index = 2;
        while (Directory.Exists(Path.Combine(parentDir, OurPlanCoreJobStore.SanitizeName(candidate, 120))))
        {
            candidate = $"{baseName} {index.ToString(CultureInfo.InvariantCulture)}";
            index++;
        }

        return candidate;
    }
}
