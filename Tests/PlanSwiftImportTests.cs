using OurPlaneCore;
using SkiaSharp;
using Docnet.Core;
using Docnet.Core.Models;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

internal static class PlanSwiftImportTests
{
    private const string PageGuid = "11111111-1111-1111-1111-111111111111";
    private const string ImageGuid = "22222222-2222-2222-2222-222222222222";
    private const string UnusedPageGuid = "33333333-3333-3333-3333-333333333333";
    private const string UnusedImageGuid = "44444444-4444-4444-4444-444444444444";
    private const string DeckAreaSectionGuid = "55555555-5555-5555-5555-555555555555";
    private const string DeckSecondAreaSectionGuid = "66666666-6666-6666-6666-666666666666";

    public static void ImportCreatesJobPagesAndMeasurements()
    {
        WithTempParent(parent =>
        {
            string sourceJob = Path.Combine(parent, "PlanSwift Job");
            string destinationParent = Path.Combine(parent, "imported");
            CreateSyntheticPlanSwiftJob(sourceJob);

            PlanSwiftImportResult result = PlanSwiftProjectImporter.Import(new PlanSwiftImportOptions
            {
                SourceJobPath = sourceJob,
                DestinationParentPath = destinationParent,
            });

            AssertEqual("1", result.PagesImported.ToString(), "imported page count");
            AssertEqual("1", result.TakeoffItemsImported.ToString(), "imported takeoff item count");
            AssertEqual("1", result.MeasurementsImported.ToString(), "imported measurement count");
            AssertTrue(File.Exists(Path.Combine(result.DestinationJobPath, "import_reports", "planswift_import_report.md")), "report written");

            OurPlaneCoreJob job = OurPlaneCoreJobStore.LoadJob(result.DestinationJobPath);
            PageInfo page = CollectPages(job.PagesRoot).Single();
            AssertTrue(File.Exists(page.PdfPath), "converted page pdf exists");
            AssertPlanSwiftImageRasterCache(page, expectedWidthPt: 120, expectedHeightPt: 80, minBitmapScale: 0.99);
            AssertClose(0.03048, page.ScaleMetersPerPt, "page scale from PlanSwift ScaleX", tolerance: 0.00001);

            TakeoffItem item = OurPlaneCoreJobStore.LoadTakeoffItems(job).Single();
            AssertEqual("Walls", item.Name, "takeoff item name");
            AssertEqual("line", item.MeasurementType, "measurement type");
            AssertEqual("#FF0000", item.Color, "PlanSwift color");

            Measurement measurement = item.Measurements.Single();
            AssertEqual(page.FolderPath, measurement.PageFolder, "measurement page binding");
            AssertEqual("2", measurement.Points.Count.ToString(), "measurement points");
            AssertClose(0.3048, measurement.Value(0), "one foot line converts to meters");
        });
    }

    public static void ImportNormalizesOversizedRasterPageWithoutChangingMeasurements()
    {
        WithTempParent(parent =>
        {
            string sourceJob = Path.Combine(parent, "PlanSwift Job");
            string destinationParent = Path.Combine(parent, "imported");
            CreateSyntheticPlanSwiftJob(sourceJob);
            string imagePath = Path.Combine(sourceJob, "Pages", "Sheets", "A100", $"{{{ImageGuid}}}.png");
            WriteSyntheticImage(imagePath, width: 5400, height: 3600, dpiX: 72, dpiY: 72);

            PlanSwiftImportResult result = PlanSwiftProjectImporter.Import(new PlanSwiftImportOptions
            {
                SourceJobPath = sourceJob,
                DestinationParentPath = destinationParent,
            });

            AssertTrue(
                result.Messages.Any(message => message.Contains("normalized to 36 x 24 in", StringComparison.OrdinalIgnoreCase)),
                "oversized default-DPI page normalization should be reported");

            OurPlaneCoreJob job = OurPlaneCoreJobStore.LoadJob(result.DestinationJobPath);
            PageInfo page = CollectPages(job.PagesRoot).Single();
            (int pdfWidth, int pdfHeight) = ReadPdfPageSize(page.PdfPath);
            AssertClose(2592, pdfWidth, "normalized PDF width should be 36 in");
            AssertClose(1728, pdfHeight, "normalized PDF height should be 24 in");
            AssertPlanSwiftImageRasterCache(page, expectedWidthPt: 2592, expectedHeightPt: 1728, minBitmapScale: 2.0, expectOverview: true);

            Measurement measurement = OurPlaneCoreJobStore.LoadTakeoffItems(job).Single().Measurements.Single();
            AssertClose(4.8, measurement.Points[1].X, "measurement x coordinate should be transformed to PDF points");
            AssertClose(0.0635, measurement.ScaleMetersPerPt, "measurement scale should compensate for coordinate transform");
            AssertClose(0.3048, measurement.Value(0), "transformed one foot line should keep original measured value");
            AssertPlanSwiftImagePdfUsesHighQualitySampling();
            AssertExistingSourceImageRasterOverviewUpgrade(page);
        });
    }

    public static void ImportSkipsPlanSwiftPagesWithoutTakeoffs()
    {
        WithTempParent(parent =>
        {
            string sourceJob = Path.Combine(parent, "PlanSwift Job");
            string destinationParent = Path.Combine(parent, "imported");
            CreateSyntheticPlanSwiftJob(sourceJob);
            AddUnusedPlanSwiftPage(sourceJob);

            PlanSwiftImportResult result = PlanSwiftProjectImporter.Import(new PlanSwiftImportOptions
            {
                SourceJobPath = sourceJob,
                DestinationParentPath = destinationParent,
                ConvertPageImages = false,
            });

            AssertEqual("1", result.PagesImported.ToString(), "only pages with takeoffs should import");
            AssertTrue(
                result.Messages.Any(message => message.Contains("with no measured takeoff geometry", StringComparison.OrdinalIgnoreCase)),
                "unused PlanSwift page skip should be reported");

            OurPlaneCoreJob job = OurPlaneCoreJobStore.LoadJob(result.DestinationJobPath);
            PageInfo page = CollectPages(job.PagesRoot).Single();
            AssertEqual("A100", page.Name, "unused page should not be created in pages tree");
        });
    }

    public static void ImportAllOptionKeepsPlanSwiftPagesWithoutTakeoffs()
    {
        WithTempParent(parent =>
        {
            string sourceJob = Path.Combine(parent, "PlanSwift Job");
            string destinationParent = Path.Combine(parent, "imported");
            CreateSyntheticPlanSwiftJob(sourceJob);
            AddUnusedPlanSwiftPage(sourceJob);
            AddEmptyTakeoffFolder(sourceJob);

            PlanSwiftImportResult result = PlanSwiftProjectImporter.Import(new PlanSwiftImportOptions
            {
                SourceJobPath = sourceJob,
                DestinationParentPath = destinationParent,
                ConvertPageImages = false,
                ImportAllSheetsAndTakeoffFolders = true,
            });

            AssertEqual("2", result.PagesImported.ToString(), "all PlanSwift pages should import");
            AssertFalse(
                result.Messages.Any(message => message.Contains("with no measured takeoff geometry", StringComparison.OrdinalIgnoreCase)),
                "all PlanSwift pages mode should not report unused pages as skipped");

            OurPlaneCoreJob job = OurPlaneCoreJobStore.LoadJob(result.DestinationJobPath);
            IReadOnlyList<PageInfo> pages = CollectPages(job.PagesRoot);
            AssertTrue(pages.Any(page => page.Name == "A100"), "measured page should be imported");
            AssertTrue(pages.Any(page => page.Name == "A101"), "unused page should be imported");
            AssertTrue(Directory.Exists(Path.Combine(job.TakeoffsRoot, "Empty Folder")), "empty PlanSwift takeoff folder should be imported");
        });
    }


    public static void ImportPreservesPlanSwiftHolesBoxAndContainers()
    {
        WithTempParent(parent =>
        {
            string sourceJob = Path.Combine(parent, "PlanSwift Job");
            string destinationParent = Path.Combine(parent, "imported");
            CreateSyntheticPlanSwiftJob(sourceJob);
            AddAreaWithSubtractHole(sourceJob);
            AddNestedItemContainer(sourceJob);

            PlanSwiftImportResult result = PlanSwiftProjectImporter.Import(new PlanSwiftImportOptions
            {
                SourceJobPath = sourceJob,
                DestinationParentPath = destinationParent,
                ConvertPageImages = false,
            });

            AssertEqual("1", result.PagesImported.ToString(), "imported page count");
            AssertEqual("3", result.TakeoffItemsImported.ToString(), "imported takeoff item count");
            AssertEqual("3", result.MeasurementsImported.ToString(), "imported measurement count");

            OurPlaneCoreJob job = OurPlaneCoreJobStore.LoadJob(result.DestinationJobPath);
            IReadOnlyList<TakeoffItem> items = OurPlaneCoreJobStore.LoadTakeoffItems(job);
            TakeoffItem deck = items.Single(item => item.Name == "Deck Area");
            Measurement deckMeasurement = deck.Measurements.Single();
            AssertEqual("4", deckMeasurement.Points.Count.ToString(), "area box points expanded to rectangle");
            AssertEqual("1", deckMeasurement.Holes.Count.ToString(), "area subtract section imported as hole");
            AssertEqual("4", deckMeasurement.Holes[0].Count.ToString(), "area subtract box expanded to rectangle");

            string assemblyFolder = Path.Combine(job.TakeoffsRoot, "Assembly");
            AssertTrue(Directory.Exists(assemblyFolder), "item container imported as visible folder");
            AssertTrue(OurPlaneCoreJobStore.TryReadTakeoffItem(assemblyFolder) == null, "item container should not hide nested items");

            TakeoffItem nested = items.Single(item => item.Name == "Nested Line");
            AssertEqual("5", nested.Measurements.Single().Points.Count.ToString(), "closed box line appends closing point");
        });
    }

    public static void ImportPreservesSegmentsAndSourceMetadata()
    {
        WithTempParent(parent =>
        {
            string sourceJob = Path.Combine(parent, "PlanSwift Job");
            string destinationParent = Path.Combine(parent, "imported");
            CreateSyntheticPlanSwiftJob(sourceJob);
            AddAreaWithSubtractHole(sourceJob);
            AddSegmentAndSourceMetadata(sourceJob);

            PlanSwiftProjectManifest manifest = PlanSwiftProjectScanner.Scan(sourceJob);
            AssertEqual("1", manifest.Segments.Count.ToString(), "segment count");
            AssertEqual("2", manifest.Segments.Single().Sections.Count.ToString(), "segment section count");
            AssertEqual("1", manifest.EstimateItems.Count.ToString(), "estimate item count");
            AssertEqual("1", manifest.Notes.Count.ToString(), "note count");
            AssertTrue(
                manifest.TakeoffClassCounts.Any(count => count.ClassName == "Segment Section" && count.Count == 2),
                "class audit includes segment sections");

            PlanSwiftImportResult result = PlanSwiftProjectImporter.Import(new PlanSwiftImportOptions
            {
                SourceJobPath = sourceJob,
                DestinationParentPath = destinationParent,
                ConvertPageImages = false,
            });

            AssertEqual("1", result.PagesImported.ToString(), "imported page count");
            AssertEqual("2", result.TakeoffItemsImported.ToString(), "imported takeoff item count");
            AssertEqual("2", result.MeasurementsImported.ToString(), "imported measurement count");

            OurPlaneCoreJob job = OurPlaneCoreJobStore.LoadJob(result.DestinationJobPath);
            IReadOnlyList<TakeoffItem> items = OurPlaneCoreJobStore.LoadTakeoffItems(job);
            AssertTrue(!items.Any(item => item.Name == "Deck Area - PlanSwift segments"), "segment line item should not be created");
            TakeoffItem deckArea = items.Single(item => item.Name == "Deck Area");
            AssertTrue(deckArea.IsJoistArea, "deck area becomes joist area");
            AssertEqual("#00FF00", deckArea.Color, "joist area uses segment color");
            AssertFalse(deckArea.JoistAddEndJoist, "imported joist area skips end joist");
            AssertClose(0, deckArea.JoistDirectionDegrees, "segment direction applied to joist area", tolerance: 0.001);
            AssertClose(12, deckArea.JoistSpacingInches, "segment spacing applied as joist O.C.", tolerance: 0.001);
            Measurement deckMeasurement = deckArea.Measurements.Single();
            AssertEqual("#00FF00", deckMeasurement.Color, "joist area section uses segment color");
            AssertTrue(deckMeasurement.JoistEnabled, "area measurement joist enabled");
            AssertTrue(deckMeasurement.JoistDirectionLocked, "area measurement direction locked");
            AssertFalse(deckMeasurement.JoistAddEndJoist, "area measurement skips end joist");
            AssertClose(12, deckMeasurement.JoistSpacingInches, "area measurement spacing");
            AssertTrue(deckArea.Notes.Contains("Imported PlanSwift Segment as joist area direction", StringComparison.Ordinal), "segment source note kept on area");

            string sourceMetadataPath = Path.Combine(result.DestinationJobPath, "import_reports", "planswift_source_metadata.json");
            AssertTrue(File.Exists(sourceMetadataPath), "source metadata sidecar written");
            string sourceMetadata = File.ReadAllText(sourceMetadataPath);
            AssertTrue(sourceMetadata.Contains("Deck material", StringComparison.Ordinal), "estimate item preserved");
            AssertTrue(sourceMetadata.Contains("Installer note", StringComparison.Ordinal), "note preserved");
        });
    }

    public static void ImportJoistSegmentsUseLinkedAreaSectionDirections()
    {
        WithTempParent(parent =>
        {
            string sourceJob = Path.Combine(parent, "PlanSwift Job");
            string destinationParent = Path.Combine(parent, "imported");
            CreateSyntheticPlanSwiftJob(sourceJob);
            AddAreaWithSubtractHole(sourceJob);
            AddSecondDeckAreaSection(sourceJob);
            AddLinkedJoistSegmentsWithBlankColor(sourceJob);

            PlanSwiftImportResult result = PlanSwiftProjectImporter.Import(new PlanSwiftImportOptions
            {
                SourceJobPath = sourceJob,
                DestinationParentPath = destinationParent,
                ConvertPageImages = false,
            });

            AssertEqual("1", result.PagesImported.ToString(), "imported page count");
            OurPlaneCoreJob job = OurPlaneCoreJobStore.LoadJob(result.DestinationJobPath);
            TakeoffItem deckArea = OurPlaneCoreJobStore.LoadTakeoffItems(job).Single(item => item.Name == "Deck Area");
            AssertTrue(deckArea.IsJoistArea, "deck area becomes joist area");
            AssertTrue(!string.Equals("#FFFFFF", deckArea.Color, StringComparison.OrdinalIgnoreCase), "blank segment color becomes stable import color");
            AssertFalse(deckArea.JoistAddEndJoist, "imported linked joist area skips end joist");

            IReadOnlyDictionary<string, Measurement> measurements = deckArea.Measurements
                .ToDictionary(measurement => NormalizeGuid(measurement.Id), StringComparer.OrdinalIgnoreCase);
            Measurement first = measurements[NormalizeGuid(DeckAreaSectionGuid)];
            Measurement second = measurements[NormalizeGuid(DeckSecondAreaSectionGuid)];

            AssertEqual(deckArea.Color, first.Color, "first area section uses segment color");
            AssertEqual(deckArea.Color, second.Color, "second area section uses segment color");
            AssertTrue(first.JoistDirectionLocked, "first area section direction locked");
            AssertTrue(second.JoistDirectionLocked, "second area section direction locked");
            AssertFalse(first.JoistAddEndJoist, "first area section skips end joist");
            AssertFalse(second.JoistAddEndJoist, "second area section skips end joist");
            AssertClose(0, first.JoistDirectionDegrees, "first section direction from linked horizontal segment", tolerance: 0.001);
            AssertClose(90, second.JoistDirectionDegrees, "second section direction from linked vertical segment", tolerance: 0.001);
            AssertClose(24, first.JoistSpacingInches, "first section spacing from joist source properties");
            AssertClose(24, second.JoistSpacingInches, "second section spacing from joist source properties");
        });
    }

    public static void ImportIntoCurrentJobUsesPlanSwiftBuckets()
    {
        WithTempParent(parent =>
        {
            string sourceJob = Path.Combine(parent, "PlanSwift Job");
            CreateSyntheticPlanSwiftJob(sourceJob);

            OurPlaneCoreJob currentJob = OurPlaneCoreJobStore.CreateJob(parent, "Current OPC Job");
            string existingPdf = Path.Combine(parent, "existing.pdf");
            WriteTestPdf(existingPdf);
            PageInfo existingPage = OurPlaneCoreJobStore.CreatePageFromPdf(
                currentJob,
                existingPdf,
                "Existing Page",
                currentJob.PagesRoot,
                pdfPage: 0,
                scaleMetersPerPt: 0.25);
            TakeoffItem existingItem = OurPlaneCoreJobStore.CreateTakeoffItem(
                currentJob,
                currentJob.TakeoffsRoot,
                "Existing Walls",
                "#444444",
                "line");
            existingItem.Measurements.Add(new Measurement
            {
                MType = "line",
                Color = existingItem.Color,
                PageFolder = existingPage.FolderPath,
                ScaleMetersPerPt = existingPage.ScaleMetersPerPt,
                Points = [new SKPoint(0, 0), new SKPoint(2, 0)],
            });
            OurPlaneCoreJobStore.SaveTakeoffItem(existingItem);

            PlanSwiftImportResult result = PlanSwiftProjectImporter.Import(new PlanSwiftImportOptions
            {
                SourceJobPath = sourceJob,
                DestinationJobPath = currentJob.RootPath,
                ConvertPageImages = false,
            });

            AssertEqual(currentJob.RootPath, result.DestinationJobPath, "current job remains import destination");
            AssertEqual("1", result.PagesImported.ToString(), "imported page count");
            AssertEqual("1", result.TakeoffItemsImported.ToString(), "imported item count");
            AssertEqual("1", result.MeasurementsImported.ToString(), "imported measurement count");

            string pageBucket = Path.Combine(currentJob.PagesRoot, PlanSwiftImportOptions.DefaultCurrentJobImportFolderName);
            string takeoffBucket = Path.Combine(currentJob.TakeoffsRoot, PlanSwiftImportOptions.DefaultCurrentJobImportFolderName);
            AssertTrue(Directory.Exists(pageBucket), "page import bucket exists");
            AssertTrue(Directory.Exists(takeoffBucket), "takeoff import bucket exists");

            OurPlaneCoreJob reloaded = OurPlaneCoreJobStore.LoadJob(currentJob.RootPath);
            IReadOnlyList<PageInfo> pages = CollectPages(reloaded.PagesRoot);
            AssertTrue(pages.Any(page => page.Name == "Existing Page" && !page.FolderPath.StartsWith(pageBucket, StringComparison.OrdinalIgnoreCase)), "existing page stays outside PlanSwift bucket");
            PageInfo importedPage = pages.Single(page => page.Name == "A100");
            AssertTrue(importedPage.FolderPath.StartsWith(pageBucket, StringComparison.OrdinalIgnoreCase), "imported page lives under 01. planswift page bucket");

            IReadOnlyList<TakeoffItem> items = OurPlaneCoreJobStore.LoadTakeoffItems(reloaded);
            AssertTrue(items.Any(item => item.Name == "Existing Walls" && !item.FolderPath.StartsWith(takeoffBucket, StringComparison.OrdinalIgnoreCase)), "existing takeoff stays outside PlanSwift bucket");
            TakeoffItem importedItem = items.Single(item => item.Name == "Walls");
            AssertTrue(importedItem.FolderPath.StartsWith(takeoffBucket, StringComparison.OrdinalIgnoreCase), "imported takeoff lives under 01. planswift takeoff bucket");
            Measurement importedMeasurement = importedItem.Measurements.Single();
            AssertEqual(importedPage.FolderPath, importedMeasurement.PageFolder, "imported measurement page binding uses imported bucket page");
            AssertEqual(importedItem.FolderPath, importedMeasurement.TakeoffFolder, "imported measurement takeoff binding uses imported bucket item");
        });
    }

    public static void ImportCopiesExistingOurPlaneCoreJobTakeoffs()
    {
        WithTempParent(parent =>
        {
            OurPlaneCoreJob sourceJob = OurPlaneCoreJobStore.CreateJob(parent, "Existing OPC Job");
            string sourcePdf = Path.Combine(parent, "source.pdf");
            WriteTestPdf(sourcePdf);
            PageInfo page = OurPlaneCoreJobStore.CreatePageFromPdf(
                sourceJob,
                sourcePdf,
                "A100",
                sourceJob.PagesRoot,
                pdfPage: 0,
                scaleMetersPerPt: 0.5);
            TakeoffItem sourceItem = OurPlaneCoreJobStore.CreateTakeoffItem(
                sourceJob,
                sourceJob.TakeoffsRoot,
                "Copied Walls",
                "#00AA00",
                "line");
            sourceItem.Measurements.Add(new Measurement
            {
                MType = "line",
                Color = sourceItem.Color,
                PageFolder = page.FolderPath,
                ScaleMetersPerPt = page.ScaleMetersPerPt,
                Points = [new SKPoint(1, 1), new SKPoint(5, 1)],
            });
            OurPlaneCoreJobStore.SaveTakeoffItem(sourceItem);

            PlanSwiftProjectManifest manifest = PlanSwiftProjectScanner.Scan(sourceJob.RootPath);
            AssertEqual(PlanSwiftSourceFormats.OurPlaneCore, manifest.SourceFormat, "source format");
            AssertEqual("1", manifest.TakeoffItems.Count.ToString(), "existing takeoff item scan count");
            AssertEqual("1", manifest.TakeoffItems.Sum(item => item.Sections.Count).ToString(), "existing measurement scan count");

            string destinationParent = Path.Combine(parent, "imported");
            PlanSwiftImportResult result = PlanSwiftProjectImporter.Import(new PlanSwiftImportOptions
            {
                SourceJobPath = sourceJob.RootPath,
                DestinationParentPath = destinationParent,
            });

            AssertEqual("1", result.PagesImported.ToString(), "copied page count");
            AssertEqual("1", result.TakeoffItemsImported.ToString(), "copied item count");
            AssertEqual("1", result.MeasurementsImported.ToString(), "copied measurement count");

            OurPlaneCoreJob importedJob = OurPlaneCoreJobStore.LoadJob(result.DestinationJobPath);
            TakeoffItem importedItem = OurPlaneCoreJobStore.LoadTakeoffItems(importedJob).Single();
            Measurement importedMeasurement = importedItem.Measurements.Single();
            PageInfo importedPage = CollectPages(importedJob.PagesRoot).Single(page => page.Name == "A100");
            AssertEqual("Copied Walls", importedItem.Name, "copied item name");
            AssertTrue(importedMeasurement.PageFolder.StartsWith(importedJob.RootPath, StringComparison.OrdinalIgnoreCase), "page binding rebased to copied job");
            AssertEqual(importedPage.FolderPath, importedMeasurement.PageFolder, "rebased measurement page");
            AssertTrue(File.Exists(importedPage.PdfPath), "copied source pdf resolves");
        });
    }

    private static void CreateSyntheticPlanSwiftJob(string root)
    {
        Directory.CreateDirectory(root);
        WriteFolderData(root, "PlanSwift Job");

        string pagesRoot = Path.Combine(root, "Pages");
        string sheets = Path.Combine(pagesRoot, "Sheets");
        string pageFolder = Path.Combine(sheets, "A100");
        Directory.CreateDirectory(pageFolder);
        WriteFolderData(pagesRoot, "Pages");
        WriteFolderData(sheets, "Sheets");
        WritePageData(pageFolder);
        WriteSyntheticImage(Path.Combine(pageFolder, $"{{{ImageGuid}}}.png"));

        string takeoffRoot = Path.Combine(root, "Takeoff");
        string itemFolder = Path.Combine(takeoffRoot, "Walls");
        string sectionFolder = Path.Combine(itemFolder, "Section");
        Directory.CreateDirectory(sectionFolder);
        WriteFolderData(takeoffRoot, "Takeoff");
        WriteTakeoffData(itemFolder);
        WriteSectionData(sectionFolder);
    }

    private static void AddAreaWithSubtractHole(string root)
    {
        string areasFolder = Path.Combine(root, "Takeoff", "Areas");
        string itemFolder = Path.Combine(areasFolder, "Deck Area");
        string sectionFolder = Path.Combine(itemFolder, "Section");
        string subtractFolder = Path.Combine(sectionFolder, "Subtract Section");
        Directory.CreateDirectory(subtractFolder);
        WriteFolderData(areasFolder, "Areas");
        WriteAreaData(itemFolder);
        WriteAreaSectionData(sectionFolder);
        WriteAreaSubtractSectionData(subtractFolder);
    }

    private static void AddUnusedPlanSwiftPage(string root)
    {
        string pageFolder = Path.Combine(root, "Pages", "Sheets", "A101");
        Directory.CreateDirectory(pageFolder);
        WritePageData(pageFolder, "A101", UnusedPageGuid, UnusedImageGuid);
        WriteSyntheticImage(Path.Combine(pageFolder, $"{{{UnusedImageGuid}}}.png"));
    }

    private static void AddEmptyTakeoffFolder(string root)
    {
        string folder = Path.Combine(root, "Takeoff", "Empty Folder");
        Directory.CreateDirectory(folder);
        WriteFolderData(folder, "Empty Folder");
    }

    private static void AddNestedItemContainer(string root)
    {
        string container = Path.Combine(root, "Takeoff", "Assembly");
        string nested = Path.Combine(container, "Nested Line");
        string section = Path.Combine(nested, "Section");
        Directory.CreateDirectory(section);
        WriteContainerLineData(container);
        WriteNestedLineData(nested);
        WriteBoxLineSectionData(section);
    }

    private static void AddSegmentAndSourceMetadata(string root)
    {
        string areaItem = Path.Combine(root, "Takeoff", "Areas", "Deck Area");
        string segment = Path.Combine(areaItem, "Deck Segment");
        string firstSection = Path.Combine(segment, "Deck Segment-1");
        string secondSection = Path.Combine(segment, "Deck Segment-2");
        string materialItem = Path.Combine(segment, "Deck material");
        string note = Path.Combine(segment, "Installer note");

        Directory.CreateDirectory(secondSection);
        WriteSegmentData(segment);
        WriteSegmentSectionData(firstSection, "Deck Segment-1", "0", "0", "5", "0", "1");
        WriteSegmentSectionData(secondSection, "Deck Segment-2", "0", "10", "5", "10", "2");
        WriteEstimateItemData(materialItem);
        WriteNoteData(note);
    }

    private static void AddSecondDeckAreaSection(string root)
    {
        string secondSection = Path.Combine(root, "Takeoff", "Areas", "Deck Area", "Section 2");
        WriteAreaSectionData(
            secondSection,
            "Area Section 2",
            DeckSecondAreaSectionGuid,
            "20",
            "20",
            "30",
            "30",
            "2");
    }

    private static void AddLinkedJoistSegmentsWithBlankColor(string root)
    {
        string areaItem = Path.Combine(root, "Takeoff", "Areas", "Deck Area");
        string segment = Path.Combine(areaItem, "Linked Joist Segment");
        string firstSection = Path.Combine(segment, "Linked Segment-1");
        string secondSection = Path.Combine(segment, "Linked Segment-2");

        Directory.CreateDirectory(secondSection);
        WriteSegmentData(segment, color: "536870911", spacing: "24");
        WriteSegmentSectionData(firstSection, "Linked Segment-1", "0", "0", "10", "0", "1", DeckAreaSectionGuid);
        WriteSegmentSectionData(secondSection, "Linked Segment-2", "20", "20", "20", "30", "2", DeckSecondAreaSectionGuid);
    }

    private static void WriteFolderData(string folder, string name) =>
        WriteDataXml(
            folder,
            new XElement("Item",
                new XAttribute("Class", "Folder"),
                new XAttribute("Name", name),
                new XAttribute("GUID", Guid.NewGuid().ToString("B").ToUpperInvariant()),
                new XElement("Properties", Prop("Name", name), Prop("Type", "Folder"))));

    private static void WritePageData(
        string folder,
        string name = "A100",
        string pageGuid = PageGuid,
        string imageGuid = ImageGuid) =>
        WriteDataXml(
            folder,
            new XElement("Item",
                new XAttribute("Class", "Page"),
                new XAttribute("Name", name),
                new XAttribute("GUID", $"{{{pageGuid}}}"),
                new XElement("Properties",
                    Prop("Name", name),
                    Prop("Type", ".PNG Page"),
                    new XElement("Property",
                        new XAttribute("Class", "Large Image"),
                        new XAttribute("Name", "Image"),
                        new XAttribute("GUID", $"{{{imageGuid}}}")),
                    Prop("ScaleX", "10"),
                    Prop("ScaleY", "10"),
                    Prop("Scale Units", "FT"))));

    private static void WriteTakeoffData(string folder) =>
        WriteDataXml(
            folder,
            new XElement("Item",
                new XAttribute("Class", "Linear"),
                new XAttribute("Name", "Walls"),
                new XAttribute("GUID", Guid.NewGuid().ToString("B").ToUpperInvariant()),
                new XElement("Properties",
                    Prop("Name", "Walls"),
                    Prop("Type", "Linear"),
                    Prop("Color", "16711680"))));

    private static void WriteAreaData(string folder) =>
        WriteDataXml(
            folder,
            new XElement("Item",
                new XAttribute("Class", "Area"),
                new XAttribute("Name", "Deck Area"),
                new XAttribute("GUID", Guid.NewGuid().ToString("B").ToUpperInvariant()),
                new XElement("Properties",
                    Prop("Name", "Deck Area"),
                    Prop("Type", "Area"),
                    Prop("Color", "255"),
                    Prop("OrderIndex", "2"))));

    private static void WriteContainerLineData(string folder) =>
        WriteDataXml(
            folder,
            new XElement("Item",
                new XAttribute("Class", "Linear"),
                new XAttribute("Name", "Assembly"),
                new XAttribute("GUID", Guid.NewGuid().ToString("B").ToUpperInvariant()),
                new XElement("Properties",
                    Prop("Name", "Assembly"),
                    Prop("Type", "Linear"),
                    Prop("Color", "65280"),
                    Prop("OrderIndex", "3"))));

    private static void WriteNestedLineData(string folder) =>
        WriteDataXml(
            folder,
            new XElement("Item",
                new XAttribute("Class", "Linear"),
                new XAttribute("Name", "Nested Line"),
                new XAttribute("GUID", Guid.NewGuid().ToString("B").ToUpperInvariant()),
                new XElement("Properties",
                    Prop("Name", "Nested Line"),
                    Prop("Type", "Linear"),
                    Prop("Color", "65280"),
                    Prop("OrderIndex", "1"))));

    private static void WriteSectionData(string folder)
    {
        string digitizerData =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Points><Point X="0" Y="0" PointType="Normal"/><Point X="10" Y="0" PointType="Normal"/></Points>
            """;

        WriteDataXml(
            folder,
            new XElement("Item",
                new XAttribute("Class", "Linear Section"),
                new XAttribute("Name", "Section"),
                new XAttribute("GUID", Guid.NewGuid().ToString("B").ToUpperInvariant()),
                new XElement("Properties",
                    Prop("Name", "Section"),
                    Prop("Type", "Linear Section"),
                    Prop("PageGUID", $"{{{PageGuid}}}"),
                    Prop("Visible", "True"),
                    Prop("DigitizerData", digitizerData))));
    }

    private static void WriteAreaSectionData(
        string folder,
        string name = "Area Section",
        string guid = DeckAreaSectionGuid,
        string x1 = "0",
        string y1 = "0",
        string x2 = "10",
        string y2 = "5",
        string orderIndex = "1")
    {
        string digitizerData =
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Points><Point X="{x1}" Y="{y1}" PointType="Normal"/><Point X="{x2}" Y="{y2}" PointType="Normal"/></Points>
            """;

        WriteDataXml(
            folder,
            new XElement("Item",
                new XAttribute("Class", "Area Section"),
                new XAttribute("Name", name),
                new XAttribute("GUID", $"{{{guid}}}"),
                new XElement("Properties",
                    Prop("Name", name),
                    Prop("Type", "Area Section"),
                    Prop("PageGUID", $"{{{PageGuid}}}"),
                    Prop("Visible", "True"),
                    Prop("OrderIndex", orderIndex),
                    Prop("Box Mode", "4 Point"),
                    Prop("DigitizerData", digitizerData))));
    }

    private static void WriteAreaSubtractSectionData(string folder)
    {
        string digitizerData =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Points><Point X="2" Y="1" PointType="Normal"/><Point X="4" Y="3" PointType="Normal"/></Points>
            """;

        WriteDataXml(
            folder,
            new XElement("Item",
                new XAttribute("Class", "Area Subtract Section"),
                new XAttribute("Name", "Subtract Section"),
                new XAttribute("GUID", Guid.NewGuid().ToString("B").ToUpperInvariant()),
                new XElement("Properties",
                    Prop("Name", "Subtract Section"),
                    Prop("Type", "Area Subtract Section"),
                    Prop("PageGUID", $"{{{PageGuid}}}"),
                    Prop("Visible", "True"),
                    Prop("Box Mode", "4 Point"),
                    Prop("DigitizerData", digitizerData))));
    }

    private static void WriteBoxLineSectionData(string folder)
    {
        string digitizerData =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Points><Point X="20" Y="20" PointType="Normal"/><Point X="25" Y="22" PointType="Normal"/></Points>
            """;

        WriteDataXml(
            folder,
            new XElement("Item",
                new XAttribute("Class", "Linear Section"),
                new XAttribute("Name", "Box Line"),
                new XAttribute("GUID", Guid.NewGuid().ToString("B").ToUpperInvariant()),
                new XElement("Properties",
                    Prop("Name", "Box Line"),
                    Prop("Type", "Linear Section"),
                    Prop("PageGUID", $"{{{PageGuid}}}"),
                    Prop("Visible", "True"),
                    Prop("Box Mode", "4 Point"),
                    Prop("Closed", "True"),
                    Prop("DigitizerData", digitizerData))));
    }

    private static void WriteSegmentData(string folder, string color = "65280", string spacing = "") =>
        WriteDataXml(
            folder,
            new XElement("Item",
                new XAttribute("Class", "Segment"),
                new XAttribute("Name", "Deck Segment"),
                new XAttribute("GUID", Guid.NewGuid().ToString("B").ToUpperInvariant()),
                new XElement("Properties",
                    Prop("Name", "Deck Segment"),
                    Prop("Type", "Joist Segment"),
                    Prop("Color", color),
                    Prop("O.C. Spacing", spacing),
                    Prop("Qty", "[Takeoff]"),
                    Prop("Default", "[Joist Length]"),
                    Prop("Joist Length", "9"),
                    Prop("Pitch", "0"))));

    private static void WriteSegmentSectionData(
        string folder,
        string name,
        string x1,
        string y1,
        string x2,
        string y2,
        string orderIndex,
        string areaSectionGuid = "")
    {
        string digitizerData =
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Points><Point X="{x1}" Y="{y1}" PointType="Normal"/><Point X="{x2}" Y="{y2}" PointType="Normal"/></Points>
            """;

        WriteDataXml(
            folder,
            new XElement("Item",
                new XAttribute("Class", "Segment Section"),
                new XAttribute("Name", name),
                new XAttribute("GUID", Guid.NewGuid().ToString("B").ToUpperInvariant()),
                new XElement("Properties",
                    Prop("Name", name),
                    Prop("Type", "Joist Line"),
                    Prop("PageGUID", $"{{{PageGuid}}}"),
                    Prop("Visible", "True"),
                    Prop("OrderIndex", orderIndex),
                    Prop("Area Section", string.IsNullOrWhiteSpace(areaSectionGuid) ? "" : $"{{{areaSectionGuid}}}"),
                    Prop("Section Link", string.IsNullOrWhiteSpace(areaSectionGuid) ? "" : $"{{{areaSectionGuid}}}"),
                    Prop("DigitizerData", digitizerData))));
    }

    private static void WriteEstimateItemData(string folder) =>
        WriteDataXml(
            folder,
            new XElement("Item",
                new XAttribute("Class", "Item"),
                new XAttribute("Name", "Deck material"),
                new XAttribute("GUID", Guid.NewGuid().ToString("B").ToUpperInvariant()),
                new XElement("Properties",
                    Prop("Name", "Deck material"),
                    Prop("Type", "Joist Material"),
                    Prop("Qty", "[Joist Qty]"),
                    Prop("Joist Length", "9"),
                    Prop("Joist Qty", "2"))));

    private static void WriteNoteData(string folder) =>
        WriteDataXml(
            folder,
            new XElement("Item",
                new XAttribute("Class", "Note"),
                new XAttribute("Name", "Installer note"),
                new XAttribute("GUID", Guid.NewGuid().ToString("B").ToUpperInvariant()),
                new XElement("Properties",
                    Prop("Name", "Installer note"),
                    Prop("Type", "Note"),
                    Prop("Description", "Preserve this note"))));

    private static XElement Prop(string name, string value) =>
        new("Property", new XAttribute("Name", name), value);

    private static void WriteDataXml(string folder, XElement root)
    {
        Directory.CreateDirectory(folder);
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        doc.Save(Path.Combine(folder, "Data.xml"));
    }

    private static void WriteSyntheticImage(
        string path,
        int width = 120,
        int height = 80,
        double dpiX = 72.0,
        double dpiY = 72.0)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = ((y * width) + x) * 4;
                bool line = Math.Abs(y - (x * (height - 1.0) / Math.Max(1, width - 1))) <= 1.0;
                byte value = line ? (byte)0 : (byte)255;
                pixels[index] = value;
                pixels[index + 1] = value;
                pixels[index + 2] = value;
                pixels[index + 3] = 255;
            }
        }

        BitmapSource source = BitmapSource.Create(
            width,
            height,
            dpiX,
            dpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void WriteTestPdf(string path)
    {
        using FileStream stream = File.Create(path);
        using SKDocument document = SKDocument.CreatePdf(stream);
        SKCanvas canvas = document.BeginPage(200, 120);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black, StrokeWidth = 2 };
        canvas.DrawLine(10, 10, 190, 110, paint);
        document.EndPage();
        document.Close();
    }

    private static IReadOnlyList<PageInfo> CollectPages(string root)
    {
        var pages = new List<PageInfo>();
        foreach (string folder in EnumerateSelfAndDescendants(root))
            if (OurPlaneCoreJobStore.TryReadPage(folder) is { } page)
                pages.Add(page);
        return pages;
    }

    private static (int Width, int Height) ReadPdfPageSize(string path)
    {
        using var docReader = DocLib.Instance.GetDocReader(path, new PageDimensions(1.0));
        using var pageReader = docReader.GetPageReader(0);
        return (pageReader.GetPageWidth(), pageReader.GetPageHeight());
    }

    private static void AssertPlanSwiftImagePdfUsesHighQualitySampling()
    {
        string repoRoot = FindRepoRoot();
        string writer = File.ReadAllText(Path.Combine(repoRoot, "Models", "Import", "PlanSwiftPagePdfWriter.cs"));

        AssertTrue(
            writer.Contains("CreateImagePagePdfPaint", StringComparison.Ordinal) &&
            writer.Contains("FilterQuality = SKFilterQuality.High", StringComparison.Ordinal) &&
            writer.Contains("IsAntialias = true", StringComparison.Ordinal) &&
            writer.Contains("canvas.DrawBitmap(", StringComparison.Ordinal) &&
            writer.Contains("imagePaint", StringComparison.Ordinal),
            "PlanSwift PNG/TIF page import should embed image pages with explicit high-quality sampling");
    }

    private static void AssertPlanSwiftImageRasterCache(
        PageInfo page,
        double expectedWidthPt,
        double expectedHeightPt,
        double minBitmapScale,
        bool expectOverview = false)
    {
        AssertTrue(page.RasterSheet != null, "PlanSwift image import should persist a raster sheet cache");
        RasterSheetSource raster = page.RasterSheet!;
        AssertEqual(
            RasterSheetCacheService.SourceImageRasterProfile,
            raster.RenderProfile,
            "PlanSwift image raster cache profile");
        AssertTrue(
            raster.Image.EndsWith(Path.Combine(RasterSheetCacheService.CacheFolderName, RasterSheetCacheService.WorkingImageName), StringComparison.OrdinalIgnoreCase),
            "PlanSwift image raster cache should live beside the page");
        if (expectOverview)
        {
            AssertTrue(
                raster.OverviewImage.EndsWith(Path.Combine(RasterSheetCacheService.CacheFolderName, RasterSheetCacheService.OverviewImageName), StringComparison.OrdinalIgnoreCase),
                "large PlanSwift image raster cache should persist a bounded overview image beside the full working raster");
            AssertTrue(
                raster.OverviewRenderScale > 0 && raster.OverviewRenderScale < raster.RenderScale,
                "large PlanSwift image overview should be lower resolution than the full working raster");
            AssertTrue(
                RasterSheetCacheService.HasSourceImageOverview(raster),
                "large PlanSwift image raster should advertise an overview cache for fast page open");
        }
        else
        {
            AssertTrue(string.IsNullOrWhiteSpace(raster.OverviewImage), "small PlanSwift image raster should not write a duplicate overview image");
        }
        AssertClose(expectedWidthPt, raster.WidthPt, "PlanSwift image raster cache width", tolerance: 0.05);
        AssertClose(expectedHeightPt, raster.HeightPt, "PlanSwift image raster cache height", tolerance: 0.05);
        AssertTrue(raster.RenderScale >= minBitmapScale, "PlanSwift image raster cache should keep source pixels for readable zoom");
        AssertTrue(string.IsNullOrWhiteSpace(raster.SnapIndex), "PlanSwift image raster cache should not run PDF snap indexing during import");
        if (expectOverview)
        {
            AssertFalse(
                RasterSheetCacheService.ShouldUseSourceImageRasterForFastOpen(raster),
                "oversized PlanSwift image raster cache should reserve low-zoom opens for the bounded overview instead of full source pixels");
        }
        else
        {
            AssertTrue(
                RasterSheetCacheService.ShouldUseSourceImageRasterForFastOpen(raster),
                "small PlanSwift image raster cache should be eligible for direct full source-image open");
        }

        AssertTrue(
            RasterSheetCacheService.DisplayStatus(page).Contains("+image", StringComparison.Ordinal),
            "PlanSwift image raster cache should be visible as an image-backed raster status");
        AssertTrue(
            RasterSheetCacheService.TryReadReady(
                page.FolderPath,
                page.PdfPath,
                raster,
                out RasterSheetBitmapResult bitmap,
                out string reason),
            reason);
        try
        {
            AssertClose(expectedWidthPt, bitmap.WidthPt, "readable image raster width", tolerance: 0.05);
            AssertClose(expectedHeightPt, bitmap.HeightPt, "readable image raster height", tolerance: 0.05);
            AssertTrue(bitmap.BitmapScale >= minBitmapScale, "readable image raster should decode at source pixel scale");
        }
        finally
        {
            bitmap.Bitmap.Dispose();
        }

        if (expectOverview)
        {
            AssertTrue(
                RasterSheetCacheService.TryReadOverviewReady(
                    page.FolderPath,
                    page.PdfPath,
                    raster,
                    out RasterSheetBitmapResult overview,
                    out string overviewReason),
                overviewReason);
            try
            {
                long overviewPixels = (long)overview.Bitmap.Width * overview.Bitmap.Height;
                AssertTrue(
                    overviewPixels <= RasterSheetCacheService.SourceImageOverviewMaxPixels,
                    "large PlanSwift image overview should stay inside the browsing overview pixel budget");
                AssertTrue(
                    overview.BitmapScale > 0 && overview.BitmapScale < raster.RenderScale,
                    "overview raster should be lighter than the full source image raster");
            }
            finally
            {
                overview.Bitmap.Dispose();
            }
        }
        else
        {
            AssertFalse(
                RasterSheetCacheService.ShouldBuildSourceImageOverview(
                    page.FolderPath,
                    page.PdfPath,
                    raster,
                    out _),
                "small PlanSwift image raster should not queue a duplicate overview build");
        }
    }

    private static void AssertExistingSourceImageRasterOverviewUpgrade(PageInfo page)
    {
        RasterSheetSource legacy = page.RasterSheet!.Clone();
        legacy.OverviewImage = "";
        legacy.OverviewRenderScale = 0;
        OurPlaneCoreJobStore.SavePageRasterSheet(page.FolderPath, legacy);

        PageInfo legacyPage = OurPlaneCoreJobStore.TryReadPage(page.FolderPath)
            ?? throw new InvalidOperationException("Legacy raster page source was not readable.");
        AssertTrue(
            RasterSheetCacheService.ShouldBuildSourceImageOverview(
                legacyPage.FolderPath,
                legacyPage.PdfPath,
                legacyPage.RasterSheet,
                out string reason),
            reason);

        RasterSheetBuildResult upgrade = RasterSheetCacheService.BuildOverviewForExistingSourceImageRaster(legacyPage);
        AssertTrue(upgrade.Ok, upgrade.Error);

        PageInfo upgradedPage = OurPlaneCoreJobStore.TryReadPage(page.FolderPath)
            ?? throw new InvalidOperationException("Upgraded raster page source was not readable.");
        RasterSheetSource upgraded = upgradedPage.RasterSheet!;
        AssertTrue(
            RasterSheetCacheService.HasSourceImageOverview(upgraded),
            "existing oversized source-image raster should be upgraded with overview metadata");
        RasterSheetSource lowQualityOverview = upgraded.Clone();
        lowQualityOverview.OverviewRenderScale = upgraded.OverviewRenderScale * 0.5;
        OurPlaneCoreJobStore.SavePageRasterSheet(upgradedPage.FolderPath, lowQualityOverview);
        PageInfo lowQualityOverviewPage = OurPlaneCoreJobStore.TryReadPage(upgradedPage.FolderPath)
            ?? throw new InvalidOperationException("Low-quality overview page source was not readable.");
        AssertTrue(
            RasterSheetCacheService.ShouldBuildSourceImageOverview(
                lowQualityOverviewPage.FolderPath,
                lowQualityOverviewPage.PdfPath,
                lowQualityOverviewPage.RasterSheet,
                out string lowQualityReason),
            lowQualityReason);
        OurPlaneCoreJobStore.SavePageRasterSheet(upgradedPage.FolderPath, upgraded);
        AssertTrue(
            RasterSheetCacheService.TryReadOverviewReady(
                upgradedPage.FolderPath,
                upgradedPage.PdfPath,
                upgraded,
                out RasterSheetBitmapResult overview,
                out string overviewReason),
            overviewReason);
        try
        {
            long overviewPixels = (long)overview.Bitmap.Width * overview.Bitmap.Height;
            AssertTrue(
                overviewPixels <= RasterSheetCacheService.SourceImageOverviewMaxPixels,
                "upgraded existing source-image overview should stay inside the browsing overview pixel budget");
        }
        finally
        {
            overview.Bitmap.Dispose();
        }
    }

    private static IEnumerable<string> EnumerateSelfAndDescendants(string root)
    {
        yield return root;
        foreach (string folder in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            yield return folder;
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
            TryDeleteDirectory(root);
        }
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

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition)
            throw new InvalidOperationException(message);
    }

    private static string FindRepoRoot()
    {
        string dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "ourplanecore.csproj")))
                return dir;

            string? parent = Directory.GetParent(dir)?.FullName;
            if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                break;
            dir = parent ?? "";
        }

        throw new DirectoryNotFoundException("Could not locate ourplanecore repo root.");
    }

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

    private static string NormalizeGuid(string value) =>
        (value ?? "").Trim().Trim('{', '}').ToUpperInvariant();
}
