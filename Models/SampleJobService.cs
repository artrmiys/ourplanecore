using System;
using System.Globalization;
using System.IO;
using System.Text;
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
        string jobName = UniqueJobName(parentDir, "Sample Job");
        OurPlaneCoreJob job = OurPlaneCoreJobStore.CreateJob(parentDir, jobName);

        string tempPdf = Path.Combine(Path.GetTempPath(), $"ourplanecore_sample_{Guid.NewGuid():N}.pdf");
        try
        {
            WriteSamplePdf(tempPdf);
            string importFolder = OurPlaneCoreJobStore.DefaultImportFolder(job);
            PageInfo page = OurPlaneCoreJobStore.CreatePageFromPdf(
                job,
                tempPdf,
                "A101 Sample Plan",
                importFolder,
                pdfPage: 0,
                scaleMetersPerPt: SampleScaleMetersPerPt);

            CreateSampleTakeoffs(job, page);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPdf))
                    File.Delete(tempPdf);
            }
            catch { }
        }

        return OurPlaneCoreJobStore.LoadJob(job.RootPath);
    }

    private static void CreateSampleTakeoffs(OurPlaneCoreJob job, PageInfo page)
    {
        string generalFolder = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "GENERAL");
        string openingsFolder = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "OPENINGS");

        TakeoffItem exterior = OurPlaneCoreJobStore.CreateTakeoffItem(
            job,
            generalFolder,
            "Sample Exterior Walls",
            "#2196F3",
            "line");
        exterior.UnitPrice = 14.50;
        exterior.Notes = "Sample line takeoff around the exterior wall outline.";
        exterior.Measurements.Add(new Measurement
        {
            Name = "Exterior wall loop",
            MType = "line",
            Color = exterior.Color,
            PageFolder = page.FolderPath,
            TakeoffFolder = exterior.FolderPath,
            ScaleMetersPerPt = SampleScaleMetersPerPt,
            Points =
            [
                new SKPoint(120, 140),
                new SKPoint(672, 140),
                new SKPoint(672, 470),
                new SKPoint(120, 470),
                new SKPoint(120, 140),
            ],
        });
        OurPlaneCoreJobStore.SaveTakeoffItem(exterior);

        TakeoffItem floor = OurPlaneCoreJobStore.CreateTakeoffItem(
            job,
            generalFolder,
            "Sample Floor Area",
            "#4CAF50",
            "area");
        floor.UnitPrice = 6.25;
        floor.Notes = "Sample area takeoff for the main plan footprint.";
        floor.Measurements.Add(new Measurement
        {
            Name = "Main footprint",
            MType = "area",
            Color = floor.Color,
            PageFolder = page.FolderPath,
            TakeoffFolder = floor.FolderPath,
            ScaleMetersPerPt = SampleScaleMetersPerPt,
            Points =
            [
                new SKPoint(120, 140),
                new SKPoint(672, 140),
                new SKPoint(672, 470),
                new SKPoint(120, 470),
            ],
        });
        OurPlaneCoreJobStore.SaveTakeoffItem(floor);

        TakeoffItem doors = OurPlaneCoreJobStore.CreateTakeoffItem(
            job,
            openingsFolder,
            "Sample Doors",
            "#FF9800",
            "point");
        doors.UnitPrice = 350.0;
        doors.Notes = "Sample count takeoff for door openings.";
        doors.Measurements.Add(new Measurement
        {
            Name = "Door count",
            MType = "point",
            Color = doors.Color,
            PageFolder = page.FolderPath,
            TakeoffFolder = doors.FolderPath,
            ScaleMetersPerPt = SampleScaleMetersPerPt,
            Points =
            [
                new SKPoint(392, 140),
                new SKPoint(672, 305),
                new SKPoint(396, 470),
            ],
        });
        OurPlaneCoreJobStore.SaveTakeoffItem(doors);
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

    private static void WriteSamplePdf(string path)
    {
        string contents = """
            q
            1 1 1 rg 0 0 792 612 re f
            0.12 0.14 0.16 RG 2 w
            120 140 m 672 140 l 672 470 l 120 470 l h S
            0.55 0.55 0.55 RG 1 w
            120 305 m 672 305 l S
            396 140 m 396 470 l S
            258 305 m 258 470 l S
            534 140 m 534 305 l S
            0.10 0.35 0.70 RG 4 w
            392 140 m 430 140 l S
            672 305 m 672 345 l S
            396 470 m 438 470 l S
            0 0 0 rg
            BT /F1 22 Tf 72 552 Td (OurPlaneCore Sample Job) Tj ET
            BT /F1 12 Tf 72 528 Td (Use Select, Ctrl+Shift+P, Ctrl+Shift+O, Snap, Ortho, and Estimating.) Tj ET
            BT /F1 11 Tf 130 485 Td (Office) Tj ET
            BT /F1 11 Tf 410 485 Td (Open Area) Tj ET
            BT /F1 11 Tf 130 285 Td (Storage) Tj ET
            BT /F1 11 Tf 410 285 Td (Shop) Tj ET
            BT /F1 10 Tf 120 110 Td (Scale in app: 1/8 in = 1 ft. Sample takeoffs are preloaded.) Tj ET
            Q
            """;

        byte[] contentBytes = Encoding.ASCII.GetBytes(contents.Replace("\r\n", "\n"));
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 792 612] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {contentBytes.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n{contents.Replace("\r\n", "\n")}\nendstream",
        };

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        WriteAscii(stream, "%PDF-1.4\n");
        var offsets = new long[objects.Length + 1];
        for (int i = 0; i < objects.Length; i++)
        {
            offsets[i + 1] = stream.Position;
            WriteAscii(stream, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        long xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {objects.Length + 1}\n");
        WriteAscii(stream, "0000000000 65535 f \n");
        for (int i = 1; i < offsets.Length; i++)
            WriteAscii(stream, $"{offsets[i].ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        WriteAscii(stream, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
    }

    private static void WriteAscii(Stream stream, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
