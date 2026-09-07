using OurPlanCore;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

internal static class PlanSwiftImportVisibilityTests
{
    private const string PageGuid = "10101010-1010-1010-1010-101010101010";
    private const string ImageGuid = "20202020-2020-2020-2020-202020202020";
    private const string VisibleLinearSectionGuid = "30303030-3030-3030-3030-303030303030";
    private const string HiddenLinearSectionGuid = "40404040-4040-4040-4040-404040404040";
    private const string VisibleAreaSectionGuid = "A1111111-1111-1111-1111-111111111111";
    private const string HiddenAreaSectionGuid = "B2222222-2222-2222-2222-222222222222";
    private const string HiddenInvalidAreaSectionGuid = "C3333333-3333-3333-3333-333333333333";
    private const string VisibleAreaWithHiddenHoleGuid = "D4444444-4444-4444-4444-444444444444";
    private const string VisibleAreaHoleGuid = "F6666666-6666-6666-6666-666666666666";
    private const string HiddenAreaHoleGuid = "A7777777-7777-7777-7777-777777777777";

    public static void ImportPreservesHiddenPlanSwiftSectionsAndHoles()
    {
        WithTempParent(parent =>
        {
            string sourceJob = Path.Combine(parent, "PlanSwift Job");
            string destinationParent = Path.Combine(parent, "imported");
            CreateVisibilityFixture(sourceJob);

            PlanSwiftProjectManifest manifest = PlanSwiftProjectScanner.Scan(sourceJob);
            AssertVisibilityManifest(manifest);

            PlanSwiftImportResult result = PlanSwiftProjectImporter.Import(new PlanSwiftImportOptions
            {
                SourceJobPath = sourceJob,
                DestinationParentPath = destinationParent,
                ConvertPageImages = false,
            });

            AssertImportedVisibility(result);
        });
    }

    private static void AssertVisibilityManifest(PlanSwiftProjectManifest manifest)
    {
        AssertEqual("1", manifest.Pages.Count.ToString(), "fixture page count");
        AssertEqual("2", manifest.TakeoffItems.Count.ToString(), "fixture takeoff item count");

        PlanSwiftTakeoffItemRecord areas = manifest.TakeoffItems.Single(item => item.Name == "Visibility Area");
        AssertEqual("3", areas.Sections.Count.ToString(), "manifest should keep all usable area sections");
        AssertFalse(
            areas.Sections.Single(section => section.Guid == HiddenAreaSectionGuid).Visible,
            "manifest should retain hidden whole-area visibility");
        AssertFalse(
            areas.Sections.Any(section => section.Guid == HiddenInvalidAreaSectionGuid),
            "hidden unusable area placeholder should be rejected");
        AssertEqual(
            "1",
            areas.Sections.Single(section => section.Guid == VisibleAreaSectionGuid).Holes.Count.ToString(),
            "visible subtract hole should remain attached to its area");
        AssertEqual(
            "1",
            areas.Sections.Single(section => section.Guid == VisibleAreaWithHiddenHoleGuid).Holes.Count.ToString(),
            "hidden subtract hole should remain attached for quantity parity");
        AssertTrue(
            manifest.Warnings.Any(message => message.Contains("Hidden Invalid Area", StringComparison.Ordinal)),
            "hidden unusable area placeholder should be reported");

        PlanSwiftTakeoffItemRecord lines = manifest.TakeoffItems.Single(item => item.Name == "Walls");
        AssertEqual("2", lines.Sections.Count.ToString(), "manifest should keep hidden linear sections");
        AssertFalse(
            lines.Sections.Single(section => section.Guid == HiddenLinearSectionGuid).Visible,
            "manifest should retain hidden linear visibility");
    }

    private static void AssertImportedVisibility(PlanSwiftImportResult result)
    {
        AssertEqual("1", result.PagesImported.ToString(), "imported page count");
        AssertEqual("2", result.TakeoffItemsImported.ToString(), "imported takeoff item count");
        AssertEqual("5", result.MeasurementsImported.ToString(), "hidden usable sections should import");

        OurPlanCoreJob job = OurPlanCoreJobStore.LoadJob(result.DestinationJobPath);
        PageInfo page = CollectPages(job.PagesRoot).Single();
        AssertClose(
            0.0846666666667,
            page.ScaleMetersPerPt,
            "page scale should compensate for 200 DPI image coordinates",
            tolerance: 0.000001);

        IReadOnlyList<TakeoffItem> items = OurPlanCoreJobStore.LoadTakeoffItems(job);
        TakeoffItem areas = items.Single(item => item.Name == "Visibility Area");
        AssertImportedAreas(areas, page);

        TakeoffItem lines = items.Single(item => item.Name == "Walls");
        AssertImportedLines(lines, page);
        AssertPersistedHiddenMeasurements(page);
    }

    private static void AssertImportedAreas(TakeoffItem areas, PageInfo page)
    {
        AssertEqual("3", areas.Measurements.Count.ToString(), "invalid hidden area should not import");
        AssertFalse(
            areas.Measurements.Any(measurement => measurement.Id == HiddenInvalidAreaSectionGuid),
            "invalid hidden area GUID should not become a measurement");

        Measurement visibleArea = areas.Measurements.Single(measurement => measurement.Id == VisibleAreaSectionGuid);
        Measurement hiddenArea = areas.Measurements.Single(measurement => measurement.Id == HiddenAreaSectionGuid);
        Measurement areaWithHiddenHole = areas.Measurements.Single(
            measurement => measurement.Id == VisibleAreaWithHiddenHoleGuid);

        AssertEqual("1", visibleArea.Holes.Count.ToString(), "visible area hole count");
        AssertEqual("1", areaWithHiddenHole.Holes.Count.ToString(), "hidden hole should affect parent quantity");
        AssertClose(
            7.2,
            visibleArea.Points[1].X,
            "area x coordinate should use 72/200 image normalization",
            tolerance: 0.001);
        AssertClose(
            0.0846666666667,
            hiddenArea.ScaleMetersPerPt,
            "hidden area should keep normalized page scale",
            tolerance: 0.000001);
        AssertTrue(
            areas.Measurements.All(measurement => measurement.PageFolder == page.FolderPath),
            "all area sections should bind to the imported page");

        double sourceScaleMetersPerPoint = 0.3048 / 10.0;
        double expectedSourceAreaPoints = (400 - 16) + 100 + (400 - 25);
        double expectedAreaMetersSquared =
            expectedSourceAreaPoints * sourceScaleMetersPerPoint * sourceScaleMetersPerPoint;
        AssertClose(
            expectedAreaMetersSquared,
            areas.Total(0),
            "area total should include hidden section and hidden hole",
            tolerance: 0.00001);
    }

    private static void AssertImportedLines(TakeoffItem lines, PageInfo page)
    {
        AssertEqual("2", lines.Measurements.Count.ToString(), "visible and hidden lines should import");
        Measurement hiddenLine = lines.Measurements.Single(
            measurement => measurement.Id == HiddenLinearSectionGuid);
        AssertEqual(page.FolderPath, hiddenLine.PageFolder, "hidden line page binding");
        AssertClose(
            3.6,
            hiddenLine.Points[1].X,
            "hidden line x coordinate should use 72/200 image normalization",
            tolerance: 0.001);
        AssertClose(
            0.3048,
            hiddenLine.Value(0),
            "hidden line quantity should be preserved",
            tolerance: 0.00001);
    }

    private static void AssertPersistedHiddenMeasurements(PageInfo page)
    {
        PageInfo reloadedPage = OurPlanCoreJobStore.TryReadPage(page.FolderPath)
            ?? throw new InvalidOperationException("Imported visibility page was not readable.");
        string[] expectedHiddenIds = [HiddenAreaSectionGuid, HiddenLinearSectionGuid];
        AssertEqual(
            JoinIds(expectedHiddenIds),
            JoinIds(reloadedPage.HiddenMeasurements),
            "reloaded page should hide only hidden whole-section measurements");
        AssertFalse(
            reloadedPage.HiddenMeasurements.Contains(HiddenAreaHoleGuid, StringComparer.OrdinalIgnoreCase),
            "hidden hole GUID must not be stored as a whole measurement ID");
        AssertFalse(
            reloadedPage.HiddenMeasurements.Contains(HiddenInvalidAreaSectionGuid, StringComparer.OrdinalIgnoreCase),
            "invalid hidden section GUID must not be persisted");

        string sourceJsonPath = Path.Combine(page.FolderPath, "source.json");
        using JsonDocument sourceJson = JsonDocument.Parse(File.ReadAllText(sourceJsonPath));
        string[] persistedHiddenIds = sourceJson.RootElement
            .GetProperty("hidden_measurements")
            .EnumerateArray()
            .Select(value => value.GetString() ?? "")
            .ToArray();
        AssertEqual(
            JoinIds(expectedHiddenIds),
            JoinIds(persistedHiddenIds),
            "source.json should contain only hidden whole-section IDs");
    }

    private static void CreateVisibilityFixture(string root)
    {
        Directory.CreateDirectory(root);
        WriteFolderData(root, "PlanSwift Job");

        string pagesRoot = Path.Combine(root, "Pages");
        string sheetsFolder = Path.Combine(pagesRoot, "Sheets");
        string pageFolder = Path.Combine(sheetsFolder, "A100");
        WriteFolderData(pagesRoot, "Pages");
        WriteFolderData(sheetsFolder, "Sheets");
        WritePageData(pageFolder);
        WriteSyntheticImage(
            Path.Combine(pageFolder, $"{{{ImageGuid}}}.png"),
            width: 120,
            height: 80,
            dpiX: 200,
            dpiY: 200);

        string takeoffRoot = Path.Combine(root, "Takeoff");
        WriteFolderData(takeoffRoot, "Takeoff");
        WriteLineFixture(takeoffRoot);
        WriteAreaFixture(takeoffRoot);
    }

    private static void WriteLineFixture(string takeoffRoot)
    {
        string itemFolder = Path.Combine(takeoffRoot, "Walls");
        WriteTakeoffData(itemFolder, "Linear", "Walls", "16711680", orderIndex: "1");
        WriteLinearSection(
            Path.Combine(itemFolder, "Visible Section"),
            "Visible Section",
            VisibleLinearSectionGuid,
            visible: true,
            x1: 0,
            y1: 0,
            x2: 10,
            y2: 0,
            orderIndex: 1);
        WriteLinearSection(
            Path.Combine(itemFolder, "Hidden Section"),
            "Hidden Section",
            HiddenLinearSectionGuid,
            visible: false,
            x1: 0,
            y1: 20,
            x2: 10,
            y2: 20,
            orderIndex: 2);
    }

    private static void WriteAreaFixture(string takeoffRoot)
    {
        string itemFolder = Path.Combine(takeoffRoot, "Visibility Area");
        WriteTakeoffData(itemFolder, "Area", "Visibility Area", "255", orderIndex: "2");

        string visibleArea = Path.Combine(itemFolder, "Visible Area");
        WriteAreaSection(
            visibleArea,
            "Visible Area",
            VisibleAreaSectionGuid,
            visible: true,
            orderIndex: 1,
            points: [(0, 0), (20, 0), (20, 20), (0, 20)]);
        WriteAreaHole(
            Path.Combine(visibleArea, "Visible Hole"),
            "Visible Hole",
            VisibleAreaHoleGuid,
            visible: true,
            x1: 2,
            y1: 2,
            x2: 6,
            y2: 6);

        WriteAreaSection(
            Path.Combine(itemFolder, "Hidden Area"),
            "Hidden Area",
            HiddenAreaSectionGuid,
            visible: false,
            orderIndex: 2,
            points: [(30, 0), (40, 0), (40, 10), (30, 10)]);
        WriteAreaSection(
            Path.Combine(itemFolder, "Hidden Invalid Area"),
            "Hidden Invalid Area",
            HiddenInvalidAreaSectionGuid,
            visible: false,
            orderIndex: 3,
            points: [(-1, -1), (-1, -1), (-1, -1), (-1, -1)],
            boxMode: "4 Point");

        string areaWithHiddenHole = Path.Combine(itemFolder, "Area With Hidden Hole");
        WriteAreaSection(
            areaWithHiddenHole,
            "Area With Hidden Hole",
            VisibleAreaWithHiddenHoleGuid,
            visible: true,
            orderIndex: 4,
            points: [(50, 0), (70, 0), (70, 20), (50, 20)]);
        WriteAreaHole(
            Path.Combine(areaWithHiddenHole, "Hidden Hole"),
            "Hidden Hole",
            HiddenAreaHoleGuid,
            visible: false,
            x1: 55,
            y1: 5,
            x2: 60,
            y2: 10);
    }

    private static void WriteFolderData(string folder, string name) =>
        WriteDataXml(
            folder,
            new XElement(
                "Item",
                new XAttribute("Class", "Folder"),
                new XAttribute("Name", name),
                new XAttribute("GUID", Guid.NewGuid().ToString("B").ToUpperInvariant()),
                new XElement(
                    "Properties",
                    Prop("Name", name),
                    Prop("Type", "Folder"))));

    private static void WritePageData(string folder) =>
        WriteDataXml(
            folder,
            new XElement(
                "Item",
                new XAttribute("Class", "Page"),
                new XAttribute("Name", "A100"),
                new XAttribute("GUID", $"{{{PageGuid}}}"),
                new XElement(
                    "Properties",
                    Prop("Name", "A100"),
                    Prop("Type", ".PNG Page"),
                    new XElement(
                        "Property",
                        new XAttribute("Class", "Large Image"),
                        new XAttribute("Name", "Image"),
                        new XAttribute("GUID", $"{{{ImageGuid}}}")),
                    Prop("ScaleX", "10"),
                    Prop("ScaleY", "10"),
                    Prop("Scale Units", "FT"))));

    private static void WriteTakeoffData(
        string folder,
        string className,
        string name,
        string color,
        string orderIndex) =>
        WriteDataXml(
            folder,
            new XElement(
                "Item",
                new XAttribute("Class", className),
                new XAttribute("Name", name),
                new XAttribute("GUID", Guid.NewGuid().ToString("B").ToUpperInvariant()),
                new XElement(
                    "Properties",
                    Prop("Name", name),
                    Prop("Type", className),
                    Prop("Color", color),
                    Prop("OrderIndex", orderIndex),
                    Prop("Qty", "[Takeoff]"))));

    private static void WriteLinearSection(
        string folder,
        string name,
        string guid,
        bool visible,
        double x1,
        double y1,
        double x2,
        double y2,
        int orderIndex)
    {
        string digitizerData = PointsXml([(x1, y1), (x2, y2)]);
        WriteDataXml(
            folder,
            new XElement(
                "Item",
                new XAttribute("Class", "Linear Section"),
                new XAttribute("Name", name),
                new XAttribute("GUID", $"{{{guid}}}"),
                new XElement(
                    "Properties",
                    Prop("Name", name),
                    Prop("Type", "Linear Section"),
                    Prop("PageGUID", $"{{{PageGuid}}}"),
                    Prop("Visible", visible.ToString()),
                    Prop("OrderIndex", orderIndex.ToString()),
                    Prop("DigitizerData", digitizerData))));
    }

    private static void WriteAreaSection(
        string folder,
        string name,
        string guid,
        bool visible,
        int orderIndex,
        IReadOnlyList<(double X, double Y)> points,
        string boxMode = "None")
    {
        WriteDataXml(
            folder,
            new XElement(
                "Item",
                new XAttribute("Class", "Area Section"),
                new XAttribute("Name", name),
                new XAttribute("GUID", $"{{{guid}}}"),
                new XElement(
                    "Properties",
                    Prop("Name", name),
                    Prop("Type", "Area Section"),
                    Prop("PageGUID", $"{{{PageGuid}}}"),
                    Prop("Visible", visible.ToString()),
                    Prop("OrderIndex", orderIndex.ToString()),
                    Prop("Box Mode", boxMode),
                    Prop("DigitizerData", PointsXml(points)))));
    }

    private static void WriteAreaHole(
        string folder,
        string name,
        string guid,
        bool visible,
        double x1,
        double y1,
        double x2,
        double y2)
    {
        WriteDataXml(
            folder,
            new XElement(
                "Item",
                new XAttribute("Class", "Area Subtract Section"),
                new XAttribute("Name", name),
                new XAttribute("GUID", $"{{{guid}}}"),
                new XElement(
                    "Properties",
                    Prop("Name", name),
                    Prop("Type", "Area Subtract Section"),
                    Prop("PageGUID", $"{{{PageGuid}}}"),
                    Prop("Visible", visible.ToString()),
                    Prop("Box Mode", "4 Point"),
                    Prop("DigitizerData", PointsXml([(x1, y1), (x2, y2)])))));
    }

    private static string PointsXml(IReadOnlyList<(double X, double Y)> points)
    {
        string pointElements = string.Concat(
            points.Select(point =>
                FormattableString.Invariant(
                    $"<Point X=\"{point.X}\" Y=\"{point.Y}\" PointType=\"Normal\"/>")));
        return $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Points>{pointElements}</Points>";
    }

    private static XElement Prop(string name, string value) =>
        new("Property", new XAttribute("Name", name), value);

    private static void WriteDataXml(string folder, XElement root)
    {
        Directory.CreateDirectory(folder);
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        document.Save(Path.Combine(folder, "Data.xml"));
    }

    private static void WriteSyntheticImage(
        string path,
        int width,
        int height,
        double dpiX,
        double dpiY)
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

    private static IReadOnlyList<PageInfo> CollectPages(string root)
    {
        var pages = new List<PageInfo>();
        foreach (string folder in EnumerateSelfAndDescendants(root))
        {
            PageInfo? page = OurPlanCoreJobStore.TryReadPage(folder);
            if (page != null)
                pages.Add(page);
        }

        return pages;
    }

    private static IEnumerable<string> EnumerateSelfAndDescendants(string root)
    {
        yield return root;
        foreach (string folder in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            yield return folder;
    }

    private static string JoinIds(IEnumerable<string> ids) =>
        string.Join(
            ",",
            ids.Where(id => !string.IsNullOrWhiteSpace(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase));

    private static void WithTempParent(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "onc_tests", Guid.NewGuid().ToString("N"));
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

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }

    private static void AssertClose(
        double expected,
        double actual,
        string message,
        double tolerance = 0.000001)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
