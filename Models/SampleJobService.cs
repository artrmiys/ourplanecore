using System;
using System.Globalization;
using System.IO;
using SkiaSharp;

namespace OurPlaneCore;

public static class SampleJobService
{
    private const double SampleScaleMetersPerPt = ViewportConstants.PdfPointMeters * 96.0;

    public static string DefaultJobsRoot
    {
        get
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrWhiteSpace(documents)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OurPlaneCore Jobs")
                : Path.Combine(documents, "OurPlaneCore Jobs");
        }
    }

    public static OurPlaneCoreJob CreateSampleJob(string parentDir)
    {
        Directory.CreateDirectory(parentDir);
        string jobName = UniqueJobName(parentDir, "OurPlaneCore Guide Sample");
        OurPlaneCoreJob job = OurPlaneCoreJobStore.CreateJob(parentDir, jobName);

        string tempPdf = Path.Combine(Path.GetTempPath(), $"ourplanecore_guide_sample_{Guid.NewGuid():N}.pdf");
        try
        {
            SampleJobGuideBuilder.WriteGuidePdf(tempPdf);
            string guideFolder = OurPlaneCoreJobStore.CreateFolder(job.PagesRoot, SampleJobGuideBuilder.GuideFolderName);
            IReadOnlyList<PageInfo> pages = OurPlaneCoreJobStore.ImportPdf(
                job,
                tempPdf,
                SampleJobGuideBuilder.PageNames,
                guideFolder);

            PageInfo planPage = pages[^1];
            planPage.ScaleMetersPerPt = SampleScaleMetersPerPt;
            OurPlaneCoreJobStore.SavePageScale(planPage.FolderPath, SampleScaleMetersPerPt);

            CreateSampleTakeoffs(job, planPage);
            CreateSampleAnnotations(planPage);
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

        return OurPlaneCoreJobStore.LoadJob(job.RootPath);
    }

    private static void CreateSampleTakeoffs(OurPlaneCoreJob job, PageInfo page)
    {
        string sqftsFolder = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
        string wallsFolder = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "walls");
        string openingsFolder = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "openings");
        string joistsFolder = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "joists");
        string roofFolder = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "rf");

        CreateMeasuredTakeoff(
            job,
            sqftsFolder,
            page,
            "1F Floor Area",
            "#4CAF50",
            "area",
            6.25,
            "Sample area takeoff for the main plan footprint.",
            null,
            new Measurement
            {
                Name = "Main footprint",
                Points =
                [
                    new SKPoint(120, 140),
                    new SKPoint(672, 140),
                    new SKPoint(672, 470),
                    new SKPoint(120, 470),
                ],
            });

        CreateMeasuredTakeoff(
            job,
            wallsFolder,
            page,
            "Exterior Walls",
            "#2196F3",
            "line",
            14.50,
            "Sample line takeoff around the exterior wall outline.",
            null,
            new Measurement
            {
                Name = "Exterior wall loop",
                Points =
                [
                    new SKPoint(120, 140),
                    new SKPoint(672, 140),
                    new SKPoint(672, 470),
                    new SKPoint(120, 470),
                    new SKPoint(120, 140),
                ],
            });

        CreateMeasuredTakeoff(
            job,
            openingsFolder,
            page,
            "Doors",
            "#FF9800",
            "point",
            350.0,
            "Sample Count takeoff for door openings.",
            item => item.CountSymbol = CountDisplaySymbol.Cross,
            new Measurement
            {
                Name = "Door count",
                Points =
                [
                    new SKPoint(392, 140),
                    new SKPoint(672, 305),
                    new SKPoint(396, 470),
                ],
            });

        CreateMeasuredTakeoff(
            job,
            openingsFolder,
            page,
            "Windows",
            "#7E57C2",
            "point",
            285.0,
            "Sample Count takeoff for window openings.",
            item => item.CountSymbol = CountDisplaySymbol.Square,
            new Measurement
            {
                Name = "Window count",
                Points =
                [
                    new SKPoint(120, 230),
                    new SKPoint(258, 470),
                    new SKPoint(534, 140),
                    new SKPoint(672, 410),
                ],
            });

        CreateMeasuredTakeoff(
            job,
            joistsFolder,
            page,
            "2x10 Joists 16 OC",
            "#009688",
            "area",
            3.95,
            "Sample joist area with a locked direction.",
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
                Name = "Office joists",
                JoistDirectionLocked = true,
                JoistDirectionDegrees = 0,
                Points =
                [
                    new SKPoint(120, 305),
                    new SKPoint(396, 305),
                    new SKPoint(396, 470),
                    new SKPoint(120, 470),
                ],
            });

        CreateMeasuredTakeoff(
            job,
            roofFolder,
            page,
            "RF Roof Area",
            "#8BC34A",
            "area",
            7.10,
            "Sample roof area footprint for 3D roof experiments.",
            null,
            new Measurement
            {
                Name = "Roof footprint",
                Points =
                [
                    new SKPoint(98, 120),
                    new SKPoint(694, 120),
                    new SKPoint(694, 492),
                    new SKPoint(98, 492),
                ],
            });

        CreateMeasuredTakeoff(
            job,
            roofFolder,
            page,
            "Front Eave Guide",
            "#E53935",
            "line",
            0,
            "Measured roof guide: eave edge for review before roof generation.",
            null,
            new Measurement
            {
                Name = "Front eave",
                Points =
                [
                    new SKPoint(120, 140),
                    new SKPoint(672, 140),
                ],
            });

        CreateMeasuredTakeoff(
            job,
            roofFolder,
            page,
            "Left Rake Guide",
            "#C2185B",
            "line",
            0,
            "Measured roof guide: rake edge for review before roof generation.",
            null,
            new Measurement
            {
                Name = "Left rake",
                Points =
                [
                    new SKPoint(120, 140),
                    new SKPoint(396, 78),
                    new SKPoint(672, 140),
                ],
            });
    }

    private static TakeoffItem CreateMeasuredTakeoff(
        OurPlaneCoreJob job,
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
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, parentFolder, name, color, measurementType);
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

        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        return item;
    }

    private static void CreateSampleAnnotations(PageInfo page)
    {
        OurPlaneCoreJobStore.SavePageAnnotations(
            page.FolderPath,
            [
                new PageAnnotation
                {
                    Kind = "note",
                    Text = "Start here: select the sample takeoffs, then try Record, Export, Takeoff Manager, and 3D.",
                    Color = "#1565C0",
                    PageFolder = page.FolderPath,
                    ScaleMetersPerPt = SampleScaleMetersPerPt,
                    Points = [new SKPoint(90, 100)],
                },
                new PageAnnotation
                {
                    Kind = "dimension",
                    Text = "Sample dimension",
                    Color = "#455A64",
                    StrokeWidth = 1.6,
                    PageFolder = page.FolderPath,
                    ScaleMetersPerPt = SampleScaleMetersPerPt,
                    Points =
                    [
                        new SKPoint(120, 504),
                        new SKPoint(672, 504),
                    ],
                },
                new PageAnnotation
                {
                    Kind = "rectangle",
                    Text = "Try Box mode here",
                    Color = "#9C27B0",
                    StrokeWidth = 1.6,
                    PageFolder = page.FolderPath,
                    ScaleMetersPerPt = SampleScaleMetersPerPt,
                    Points =
                    [
                        new SKPoint(404, 150),
                        new SKPoint(524, 150),
                        new SKPoint(524, 298),
                        new SKPoint(404, 298),
                    ],
                },
            ]);
    }

    private static string UniqueJobName(string parentDir, string baseName)
    {
        string candidate = baseName;
        int index = 2;
        while (Directory.Exists(Path.Combine(parentDir, OurPlaneCoreJobStore.SanitizeName(candidate, 120))))
        {
            candidate = $"{baseName} {index.ToString(CultureInfo.InvariantCulture)}";
            index++;
        }

        return candidate;
    }
}
