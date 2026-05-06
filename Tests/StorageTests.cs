using OurPlaneCore;
using SkiaSharp;
using System.Xml.Linq;

internal static class StorageTests
{
    public static void JobLayoutCreateAndLoadEnsuresBaseFolders()
    {
        WithTempParent(parent =>
        {
            OurPlaneCoreJob created = OurPlaneCoreJobStore.CreateJob(parent, "Layout Job");
            OurPlaneCoreJob loaded = OurPlaneCoreJobStore.LoadJob(created.RootPath);

            AssertEqual("Layout Job", loaded.Name, "loaded job name");
            AssertTrue(File.Exists(Path.Combine(created.RootPath, "Data.xml")), "root Data.xml should exist");
            AssertTrue(Directory.Exists(Path.Combine(created.RootPath, "sources")), "sources folder should exist");
            AssertTrue(Directory.Exists(Path.Combine(created.PagesRoot, "00. imported", "Arch")), "Arch import folder should exist");
            AssertTrue(Directory.Exists(Path.Combine(created.PagesRoot, "00. imported", "Struct")), "Struct import folder should exist");
            AssertTrue(Directory.Exists(Path.Combine(created.PagesRoot, "--------others")), "others folder should exist");
            AssertTrue(Directory.Exists(created.TakeoffsRoot), "takeoffs root should exist");

            string expectedImport = Path.Combine(created.PagesRoot, "00. imported", "Arch");
            AssertEqual(FullPath(expectedImport), FullPath(OurPlaneCoreJobStore.DefaultImportFolder(loaded)), "default import folder");
        });
    }

    public static void PageImportWritesLayerManifestAndMetadata()
    {
        WithTempJob("Page Import", job =>
        {
            string sourcePdf = CreateSourcePdf(job, "plans.pdf");
            var layers = new List<PdfLayerInfo>
            {
                new() { Number = 2, Name = "Walls", IsOn = true },
                new() { Number = 1, Name = "Grid", IsOn = false },
            };
            var cache = new Dictionary<int, IReadOnlyList<PdfLayerInfo>> { [0] = layers };

            IReadOnlyList<PageInfo> pages = OurPlaneCoreJobStore.ImportPdf(
                job,
                sourcePdf,
                ["A101", "A102"],
                job.PagesRoot,
                cache);

            AssertEqual("2", pages.Count.ToString(), "imported page count");
            AssertTrue(pages[0].PdfLayersCached, "first page should use cached layers");
            AssertFalse(pages[1].PdfLayersCached, "second page should not be cached");
            SourceInfo source = OurPlaneCoreJobStore.ReadSource(pages[0].FolderPath)
                ?? throw new InvalidOperationException("source missing");
            AssertFalse(Path.IsPathRooted(source.Pdf), "stored pdf path should be relative");

            PageLayerManifest manifest = OurPlaneCoreJobStore.ReadPageLayerManifest(pages[0].FolderPath)
                ?? throw new InvalidOperationException("layer manifest missing");
            AssertEqual("2", manifest.LayerCount.ToString(), "manifest layer count");
            AssertEqual("1", manifest.Layers[0].Number.ToString(), "manifest layers sorted by number");
            AssertTrue(OurPlaneCoreJobStore.ReadPageLayerManifest(pages[1].FolderPath) == null, "uncached page has no layer manifest");

            var metadata = new PdfSheetMetadata
            {
                GeneratedAtUtc = "keep-existing",
                SheetLabel = "A101",
                SheetKey = "a101",
                SelectedScaleMetersPerPt = 0.25,
                Layers = layers,
            };
            OurPlaneCoreJobStore.WriteSourcePdfMetadata(pages[0].FolderPath, metadata);
            PdfSheetMetadata reloaded = OurPlaneCoreJobStore.ReadSourcePdfMetadata(pages[0].FolderPath)
                ?? throw new InvalidOperationException("source metadata missing");

            AssertEqual("keep-existing", reloaded.GeneratedAtUtc, "metadata timestamp should not be rewritten");
            AssertEqual("A101", reloaded.SheetLabel, "metadata sheet label");
            AssertClose(0.25, reloaded.SelectedScaleMetersPerPt, "metadata selected scale");
            AssertEqual("2", reloaded.Layers.Count.ToString(), "metadata layers");
        });
    }

    public static void PageCopyAndMovePreserveSourceOverlayAndLayers()
    {
        WithTempJob("Page Copy Move", job =>
        {
            PageInfo basePage = CreatePageItem(job, "S101");
            PageInfo overlayPage = CreatePageItem(job, "S102");
            OurPlaneCoreJobStore.SavePageOverlay(basePage.FolderPath, overlayPage.FolderPath, "#1E88E5", 0.42);
            OurPlaneCoreJobStore.SavePageOverlayTransform(basePage.FolderPath, 12.5, -7.25, 1.2);
            OurPlaneCoreJobStore.SavePageLayerCache(
                basePage.FolderPath,
                [new PdfLayerInfo { Number = 1, Name = "Walls", IsOn = true }]);

            string copyParent = OurPlaneCoreJobStore.CreateFolder(job.PagesRoot, "Copies");
            string copiedPath = OurPlaneCoreJobStore.CopyNode(basePage.FolderPath, copyParent);
            AssertPageSourceState(copiedPath, overlayPage.FolderPath);

            string moveParent = OurPlaneCoreJobStore.CreateFolder(job.PagesRoot, "Moved");
            string movedPath = OurPlaneCoreJobStore.MoveNode(copiedPath, moveParent);
            AssertPageSourceState(movedPath, overlayPage.FolderPath);
        });
    }

    public static void PageCorruptSourceJsonIsQuarantined()
    {
        WithTempJob("Corrupt Source", job =>
        {
            string pageFolder = OurPlaneCoreJobStore.EnsureFolder(job.PagesRoot, "Broken");
            string sourcePath = Path.Combine(pageFolder, "source.json");
            File.WriteAllText(sourcePath, "{ bad json");
            _ = OurPlaneCoreJobStore.DrainCorruptJsonFiles();

            PageInfo? page = OurPlaneCoreJobStore.TryReadPage(pageFolder);
            IReadOnlyList<string> quarantined = OurPlaneCoreJobStore.DrainCorruptJsonFiles();

            AssertTrue(page == null, "corrupt source should not load as page");
            AssertFalse(File.Exists(sourcePath), "corrupt source should be moved away");
            AssertEqual("1", Directory.GetFiles(pageFolder, "source.json.corrupt-*").Length.ToString(), "corrupt file count");
            AssertTrue(quarantined.Any(path => path.Contains("source.json", StringComparison.OrdinalIgnoreCase)), "quarantine report");
        });
    }

    public static void PageAnnotationsSaveLoadNormalizeDefaults()
    {
        WithTempJob("Page Annotations", job =>
        {
            PageInfo page = CreatePageItem(job, "A200");
            var annotation = new PageAnnotation
            {
                Kind = "rect",
                Color = "",
                ScaleMetersPerPt = 0.25,
                Points = [new SKPoint(0, 0), new SKPoint(10, 10)],
            };

            OurPlaneCoreJobStore.SavePageAnnotations(page.FolderPath, [annotation]);
            List<PageAnnotation> loaded = OurPlaneCoreJobStore.LoadPageAnnotations(page.FolderPath);

            AssertEqual("1", loaded.Count.ToString(), "loaded annotation count");
            AssertEqual("rectangle", loaded[0].Kind, "annotation kind normalized");
            AssertEqual("#1565C0", loaded[0].Color, "annotation default color");
            AssertEqual(page.FolderPath, loaded[0].PageFolder, "annotation page default");
            AssertClose(0.25, loaded[0].ScaleMetersPerPt, "annotation scale");
            AssertEqual("dimension", OurPlaneCoreJobStore.NormalizePageAnnotationKind("ruler"), "ruler alias");
        });
    }

    public static void PageCorruptAnnotationsJsonIsQuarantined()
    {
        WithTempJob("Corrupt Annotations", job =>
        {
            PageInfo page = CreatePageItem(job, "A201");
            string path = OurPlaneCoreJobStore.PageAnnotationsJsonPath(page.FolderPath);
            File.WriteAllText(path, "{ bad json");
            _ = OurPlaneCoreJobStore.DrainCorruptJsonFiles();

            List<PageAnnotation> annotations = OurPlaneCoreJobStore.LoadPageAnnotations(page.FolderPath);
            IReadOnlyList<string> quarantined = OurPlaneCoreJobStore.DrainCorruptJsonFiles();

            AssertEqual("0", annotations.Count.ToString(), "corrupt annotations should load empty");
            AssertFalse(File.Exists(path), "corrupt annotations should be moved away");
            AssertEqual("1", Directory.GetFiles(page.FolderPath, "annotations.json.corrupt-*").Length.ToString(), "corrupt annotations count");
            AssertTrue(quarantined.Any(entry => entry.Contains("annotations.json", StringComparison.OrdinalIgnoreCase)), "quarantine report");
        });
    }

    public static void TakeoffSaveWritesCountersAndReloadsFallbackScale()
    {
        WithTempJob("Takeoff Save", job =>
        {
            PageInfo page = CreatePageItem(job, "A100");
            OurPlaneCoreJobStore.SavePageScale(page.FolderPath, 0.5);
            TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Walls", "#FF0000", "line");
            item.Measurements.Add(new Measurement
            {
                MType = "line",
                PageFolder = page.FolderPath,
                Points = [new SKPoint(0, 0), new SKPoint(4, 0)],
            });
            item.Measurements.Add(new Measurement
            {
                MType = "point",
                PageFolder = page.FolderPath,
                Points = [new SKPoint(1, 1)],
            });

            OurPlaneCoreJobStore.SaveTakeoffItem(item);
            TakeoffItem loaded = OurPlaneCoreJobStore.TryReadTakeoffItem(item.FolderPath)
                ?? throw new InvalidOperationException("takeoff item missing");

            AssertEqual("2", ReadDataProperty(item.FolderPath, "MeasurementCount"), "measurement count property");
            AssertEqual("1", ReadDataProperty(item.FolderPath, "MeasuredPageCount"), "measured page count property");
            AssertEqual("2", loaded.Measurements.Count.ToString(), "loaded measurement count");
            AssertClose(0.5, loaded.Measurements[0].ScaleMetersPerPt, "fallback page scale");
        });
    }

    public static void TakeoffCorruptMeasurementsJsonIsQuarantined()
    {
        WithTempJob("Corrupt Measurements", job =>
        {
            TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Bad", "#FF0000", "line");
            string path = Path.Combine(item.FolderPath, "measurements.json");
            File.WriteAllText(path, "{ bad json");
            _ = OurPlaneCoreJobStore.DrainCorruptJsonFiles();

            List<Measurement> measurements = OurPlaneCoreJobStore.LoadMeasurements(item.FolderPath);
            IReadOnlyList<string> quarantined = OurPlaneCoreJobStore.DrainCorruptJsonFiles();

            AssertEqual("0", measurements.Count.ToString(), "corrupt measurements should load empty");
            AssertFalse(File.Exists(path), "corrupt measurements should be moved away");
            AssertEqual("1", Directory.GetFiles(item.FolderPath, "measurements.json.corrupt-*").Length.ToString(), "corrupt measurements count");
            AssertTrue(quarantined.Any(entry => entry.Contains("measurements.json", StringComparison.OrdinalIgnoreCase)), "quarantine report");
        });
    }

    private static void AssertPageSourceState(string pageFolder, string overlayPageFolder)
    {
        PageInfo page = OurPlaneCoreJobStore.TryReadPage(pageFolder)
            ?? throw new InvalidOperationException("page missing");
        PageLayerManifest manifest = OurPlaneCoreJobStore.ReadPageLayerManifest(pageFolder)
            ?? throw new InvalidOperationException("layer manifest missing");

        AssertTrue(File.Exists(page.PdfPath), "copied page pdf should resolve");
        AssertEqual(overlayPageFolder, page.OverlayPageFolder, "overlay page path should survive rewrite");
        AssertClose(12.5, page.OverlayOffsetXPt, "overlay x should survive rewrite");
        AssertClose(-7.25, page.OverlayOffsetYPt, "overlay y should survive rewrite");
        AssertClose(1.2, page.OverlayScale, "overlay scale should survive rewrite");
        AssertEqual("1", manifest.LayerCount.ToString(), "layer manifest should survive rewrite");
    }

    private static PageInfo CreatePageItem(OurPlaneCoreJob job, string name) =>
        OurPlaneCoreJobStore.CreatePageFromPdf(job, CreateSourcePdf(job, "source.pdf"), name, job.PagesRoot);

    private static string CreateSourcePdf(OurPlaneCoreJob job, string fileName)
    {
        string sourcePdf = Path.Combine(job.RootPath, fileName);
        if (!File.Exists(sourcePdf))
            File.WriteAllText(sourcePdf, "%PDF-1.4 test");
        return sourcePdf;
    }

    private static void WithTempJob(string name, Action<OurPlaneCoreJob> action)
    {
        WithTempParent(parent =>
        {
            OurPlaneCoreJob job = OurPlaneCoreJobStore.CreateJob(parent, name);
            action(job);
        });
    }

    private static void WithTempParent(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "opc_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string FullPath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string ReadDataProperty(string folder, string propertyName)
    {
        string path = Path.Combine(folder, "Data.xml");
        XElement root = XDocument.Load(path).Root
            ?? throw new InvalidOperationException("missing Data.xml root");
        return root.Element("Properties")?
            .Elements("Property")
            .FirstOrDefault(prop => string.Equals((string?)prop.Attribute("Name"), propertyName, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("Value")
            ?.Value ?? "";
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }

    private static void AssertClose(double expected, double actual, string message, double tolerance = 0.000001)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
