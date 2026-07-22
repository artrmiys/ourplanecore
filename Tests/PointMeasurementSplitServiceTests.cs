using OurPlanCore;
using SkiaSharp;

internal static class PointMeasurementSplitServiceTests
{
    public static void MainWindowPointSplitWiringCapturesMarkersAndStopsRecordBeforeMutation()
    {
        string mergeSplit = ReadRepoFile("MainWindow.MeasurementMergeSplit.cs");
        string viewportMenu = ReadRepoFile("MainWindow.ViewportContextMenu.cs");
        string viewportInput = ReadRepoFile(Path.Combine("Controls", "PdfViewport.Input.cs"));
        string viewportCommands = ReadRepoFile(Path.Combine("Controls", "PdfViewport.ViewCommands.cs"));
        string commandPalette = ReadRepoFile("MainWindow.CommandPalette.cs");
        string mainWindowXaml = ReadRepoFile("MainWindow.xaml");
        int staleSelectionGuard = mergeSplit.IndexOf(
            "if (pointSelectionRequested && selectedPointIndices.Count == 0)",
            StringComparison.Ordinal);
        int recordGuard = mergeSplit.IndexOf("if (IsRecordTool(_activeTool))", StringComparison.Ordinal);
        int selectTool = recordGuard < 0
            ? -1
            : mergeSplit.IndexOf("SetTool(\"select\")", recordGuard, StringComparison.Ordinal);
        int targetMutation = selectTool < 0
            ? -1
            : mergeSplit.IndexOf("CreateSplitTargetTakeoff(", selectTool, StringComparison.Ordinal);

        AssertTrue(
            mergeSplit.Contains("_viewport.GetSelectedPointVertexSelections()", StringComparison.Ordinal),
            "toolbar and shortcut Split should capture selected Count vertices");
        AssertTrue(
            viewportMenu.Contains("request.PointVertexIndex", StringComparison.Ordinal) &&
            viewportMenu.Contains("explicitPointSelection: pointSelections", StringComparison.Ordinal),
            "right-click Split should pass the clicked or selected Count markers");
        AssertTrue(
            viewportInput.Contains("_rightClickPointVertexIndex", StringComparison.Ordinal) &&
            viewportInput.Contains("contextPointVertexIndex", StringComparison.Ordinal) &&
            viewportCommands.Contains("ActiveMeasurementVertexIndices(measurement)", StringComparison.Ordinal),
            "viewport should capture and expose the exact selected Count marker indices");
        AssertTrue(
            mergeSplit.Contains("selected.Where(selectedPointIndices.ContainsKey)", StringComparison.Ordinal) &&
            mergeSplit.Contains("SourceItemsForMeasurements(effectiveSelection)", StringComparison.Ordinal) &&
            mergeSplit.Contains("sourceItems, effectiveSelection", StringComparison.Ordinal),
            "Point Split target metadata should come only from measurements with selected markers");
        AssertTrue(
            staleSelectionGuard >= 0 && staleSelectionGuard < targetMutation,
            "stale marker selection should stop before a target takeoff is created");
        AssertTrue(
            recordGuard >= 0 && selectTool > recordGuard && targetMutation > selectTool,
            "Split should leave Record mode before creating or mutating the new takeoff");
        AssertTrue(
            mergeSplit.Contains("PointMeasurementSplitService.MoveMeasurementsToTakeoff(", StringComparison.Ordinal),
            "Point Split should use the marker-aware service");
        AssertTrue(
            mergeSplit.Contains("Split Selected Count Marks", StringComparison.Ordinal) &&
            mergeSplit.Contains("CountMarkLabel", StringComparison.Ordinal),
            "Point Split dialog and status should use Count-mark wording");
        AssertTrue(
            commandPalette.Contains("measurements or Count marks", StringComparison.Ordinal) &&
            mainWindowXaml.Contains("measurements or Count marks", StringComparison.Ordinal),
            "shared Split surfaces should explain Count-mark behavior");
    }

    public static void WholeSinglePointMovePreservesIdentityAndMetadata()
    {
        TakeoffItem source = CountTakeoff(
            "Source Count",
            @"C:\job\Takeoffs\Source",
            "#112233",
            CountDisplaySymbol.Circle);
        TakeoffItem target = CountTakeoff(
            "Target Count",
            @"C:\job\Takeoffs\Target",
            "#22AAFF",
            CountDisplaySymbol.Square);
        Measurement measurement = CountMeasurement(
            id: "single-count-id",
            name: "Window W1",
            notes: "keep this note",
            source.FolderPath,
            @"C:\job\Pages\A101",
            scaleMetersPerPt: 0.125,
            [new SKPoint(11, 17)]);
        source.Measurements.Add(measurement);

        MeasurementMoveResult result = PointMeasurementSplitService.MoveMeasurementsToTakeoff(
            [source, target],
            [measurement],
            selectedPointIndices: null,
            target);

        AssertEqual(0, source.Measurements.Count, "source should lose the whole one-point measurement");
        AssertEqual(1, target.Measurements.Count, "target should receive the one-point measurement");
        AssertTrue(ReferenceEquals(measurement, target.Measurements[0]), "whole move should preserve the measurement object");
        AssertEqual("single-count-id", measurement.Id, "whole move should preserve id");
        AssertEqual("Window W1", measurement.Name, "whole move should preserve name");
        AssertEqual("keep this note", measurement.Notes, "whole move should preserve notes");
        AssertEqual(@"C:\job\Pages\A101", measurement.PageFolder, "whole move should preserve page");
        AssertClose(0.125, measurement.ScaleMetersPerPt, "whole move should preserve scale");
        AssertEqual(target.FolderPath, measurement.TakeoffFolder, "whole move should use target folder");
        AssertEqual(target.Color, measurement.Color, "whole move should use target color");
        AssertEqual(target.CountSymbol, measurement.CountSymbol, "whole move should use target symbol");
        AssertEqual(1, result.MovedMeasurements.Count, "whole move result count");
        AssertTrue(ReferenceEquals(measurement, result.SelectedMeasurements.Single()), "whole move result selection");
    }

    public static void WholeMultiPointWithoutVertexMapMovesEntireSection()
    {
        TakeoffItem source = CountTakeoff(
            "Source Count",
            @"C:\job\Takeoffs\Source",
            "#112233",
            CountDisplaySymbol.Circle);
        TakeoffItem target = CountTakeoff(
            "Target Count",
            @"C:\job\Takeoffs\Target",
            "#22AAFF",
            CountDisplaySymbol.Square);
        Measurement measurement = CountMeasurement(
            id: "whole-multi-id",
            name: "Imported section",
            notes: "tree split",
            source.FolderPath,
            @"C:\job\Pages\A102",
            scaleMetersPerPt: 0.125,
            [new SKPoint(1, 1), new SKPoint(2, 2), new SKPoint(3, 3)]);
        source.Measurements.Add(measurement);

        PointMeasurementSplitService.MoveMeasurementsToTakeoff(
            [source, target],
            [measurement],
            selectedPointIndices: null,
            target);

        AssertEqual(0, source.Measurements.Count, "tree-style whole split should empty the source section");
        AssertEqual(1, target.Measurements.Count, "tree-style whole split should create one target section");
        AssertTrue(ReferenceEquals(measurement, target.Measurements[0]), "tree-style whole split should preserve the object");
        AssertPoints(target.Measurements[0].Points, [(1, 1), (2, 2), (3, 3)], "tree-style whole split points");
    }

    public static void InvalidExplicitVertexMapDoesNotExpandToWholeMove()
    {
        TakeoffItem source = CountTakeoff(
            "Source Count",
            @"C:\job\Takeoffs\Source",
            "#112233",
            CountDisplaySymbol.Circle);
        TakeoffItem target = CountTakeoff(
            "Target Count",
            @"C:\job\Takeoffs\Target",
            "#22AAFF",
            CountDisplaySymbol.Square);
        Measurement measurement = CountMeasurement(
            id: "stale-selection-id",
            name: "Stale selection",
            notes: "",
            source.FolderPath,
            @"C:\job\Pages\A103",
            scaleMetersPerPt: 0.125,
            [new SKPoint(1, 1), new SKPoint(2, 2)]);
        source.Measurements.Add(measurement);

        bool threw = false;
        try
        {
            PointMeasurementSplitService.MoveMeasurementsToTakeoff(
                [source, target],
                [measurement],
                new Dictionary<Measurement, IReadOnlyList<int>>
                {
                    [measurement] = [99],
                },
                target);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        AssertTrue(threw, "stale explicit marker indices should stop instead of moving the whole section");
        AssertEqual(1, source.Measurements.Count, "stale explicit marker indices should leave source unchanged");
        AssertEqual(0, target.Measurements.Count, "stale explicit marker indices should leave target unchanged");
    }

    public static void PartialThreePointPartitionPreservesOrderTotalAndMetadata()
    {
        TakeoffItem source = CountTakeoff(
            "Source Count",
            @"C:\job\Takeoffs\Source",
            "#111111",
            CountDisplaySymbol.Circle);
        TakeoffItem target = CountTakeoff(
            "Target Count",
            @"C:\job\Takeoffs\Target",
            "#33BB66",
            CountDisplaySymbol.Diamond);
        Measurement sourceMeasurement = CountMeasurement(
            id: "three-point-source-id",
            name: "Imported windows",
            notes: "PlanSwift section note",
            source.FolderPath,
            @"C:\job\Pages\A202",
            scaleMetersPerPt: 0.0625,
            [
                new SKPoint(10, 1),
                new SKPoint(20, 2),
                new SKPoint(30, 3),
            ]);
        source.Measurements.Add(sourceMeasurement);

        MeasurementMoveResult result = PointMeasurementSplitService.MoveMeasurementsToTakeoff(
            [source, target],
            [sourceMeasurement],
            new Dictionary<Measurement, IReadOnlyList<int>>
            {
                [sourceMeasurement] = [2, 0],
            },
            target);

        AssertEqual(1, source.Measurements.Count, "partial split should retain its source measurement");
        AssertTrue(ReferenceEquals(sourceMeasurement, source.Measurements[0]), "partial split should retain the source object");
        AssertEqual("three-point-source-id", sourceMeasurement.Id, "partial split should retain the source id");
        AssertPoints(sourceMeasurement.Points, [(20, 2)], "remaining source point order");
        AssertEqual(1, target.Measurements.Count, "partial split should create one target measurement");

        Measurement moved = target.Measurements[0];
        AssertFalse(ReferenceEquals(sourceMeasurement, moved), "partial split should create a distinct target object");
        AssertTrue(!string.IsNullOrWhiteSpace(moved.Id), "partial split should assign a target id");
        AssertTrue(!string.Equals(sourceMeasurement.Id, moved.Id, StringComparison.Ordinal), "partial split target id should be new");
        AssertPoints(moved.Points, [(10, 1), (30, 3)], "moved points should keep source order");
        AssertEqual("Imported windows", moved.Name, "partial split should preserve name");
        AssertEqual("PlanSwift section note", moved.Notes, "partial split should preserve notes");
        AssertEqual(@"C:\job\Pages\A202", moved.PageFolder, "partial split should preserve page");
        AssertClose(0.0625, moved.ScaleMetersPerPt, "partial split should preserve scale");
        AssertEqual(target.FolderPath, moved.TakeoffFolder, "partial split target folder");
        AssertEqual(target.Color, moved.Color, "partial split target color");
        AssertEqual(target.CountSymbol, moved.CountSymbol, "partial split target symbol");
        AssertClose(3, source.Total(0) + target.Total(0), "partial split should preserve total count");
        AssertTrue(ReferenceEquals(moved, result.MovedMeasurements.Single()), "result should expose the created target measurement");
        AssertTrue(ReferenceEquals(moved, result.SelectedMeasurements.Single()), "result should select the created target measurement");
    }

    public static void AllSelectedPointsMoveOriginalMeasurement()
    {
        TakeoffItem source = CountTakeoff(
            "Source Count",
            @"C:\job\Takeoffs\Source",
            "#111111",
            CountDisplaySymbol.Circle);
        TakeoffItem target = CountTakeoff(
            "Target Count",
            @"C:\job\Takeoffs\Target",
            "#44CC77",
            CountDisplaySymbol.Star);
        Measurement measurement = CountMeasurement(
            id: "all-selected-id",
            name: "All selected",
            notes: "all points move",
            source.FolderPath,
            @"C:\job\Pages\A303",
            scaleMetersPerPt: 0.25,
            [new SKPoint(1, 1), new SKPoint(2, 2), new SKPoint(3, 3)]);
        source.Measurements.Add(measurement);

        MeasurementMoveResult result = PointMeasurementSplitService.MoveMeasurementsToTakeoff(
            [source, target],
            [measurement],
            new Dictionary<Measurement, IReadOnlyList<int>>
            {
                [measurement] = [2, 0, 1],
            },
            target);

        AssertEqual(0, source.Measurements.Count, "all selected points should remove the source section");
        AssertEqual(1, target.Measurements.Count, "all selected points should produce one target section");
        AssertTrue(ReferenceEquals(measurement, target.Measurements[0]), "all selected points should move the original object");
        AssertEqual("all-selected-id", target.Measurements[0].Id, "all selected points should preserve id");
        AssertPoints(target.Measurements[0].Points, [(1, 1), (2, 2), (3, 3)], "all selected points should keep source order");
        AssertTrue(ReferenceEquals(measurement, result.SelectedMeasurements.Single()), "all selected result selection");
    }

    public static void VertexSubsetTakesPrecedenceOverWholeObjectSelection()
    {
        TakeoffItem source = CountTakeoff(
            "Source Count",
            @"C:\job\Takeoffs\Source",
            "#111111",
            CountDisplaySymbol.Circle);
        TakeoffItem target = CountTakeoff(
            "Target Count",
            @"C:\job\Takeoffs\Target",
            "#55DD88",
            CountDisplaySymbol.Triangle);
        Measurement subsetMeasurement = CountMeasurement(
            id: "subset-id",
            name: "Subset owner",
            notes: "",
            source.FolderPath,
            @"C:\job\Pages\A404",
            scaleMetersPerPt: 0.5,
            [new SKPoint(10, 10), new SKPoint(20, 20), new SKPoint(30, 30)]);
        Measurement wholeObjectOnly = CountMeasurement(
            id: "whole-selection-id",
            name: "Whole selection only",
            notes: "",
            source.FolderPath,
            @"C:\job\Pages\A404",
            scaleMetersPerPt: 0.5,
            [new SKPoint(40, 40)]);
        source.Measurements.Add(subsetMeasurement);
        source.Measurements.Add(wholeObjectOnly);

        PointMeasurementSplitService.MoveMeasurementsToTakeoff(
            [source, target],
            [subsetMeasurement, wholeObjectOnly],
            new Dictionary<Measurement, IReadOnlyList<int>>
            {
                [subsetMeasurement] = [1],
            },
            target);

        AssertEqual(2, source.Measurements.Count, "vertex mode should retain measurements without selected vertices");
        AssertTrue(source.Measurements.Contains(wholeObjectOnly), "whole-object-only selection should not leak into vertex split");
        AssertPoints(subsetMeasurement.Points, [(10, 10), (30, 30)], "vertex subset should leave the other points");
        AssertEqual(1, target.Measurements.Count, "only the selected vertex subset should move");
        AssertPoints(target.Measurements[0].Points, [(20, 20)], "selected vertex should move");
    }

    public static void MultiSourceVertexSplitUpdatesEveryOwner()
    {
        TakeoffItem firstSource = CountTakeoff(
            "First Source",
            @"C:\job\Takeoffs\First",
            "#101010",
            CountDisplaySymbol.Circle);
        TakeoffItem secondSource = CountTakeoff(
            "Second Source",
            @"C:\job\Takeoffs\Second",
            "#202020",
            CountDisplaySymbol.Cross);
        TakeoffItem target = CountTakeoff(
            "Target",
            @"C:\job\Takeoffs\Target",
            "#66EE99",
            CountDisplaySymbol.Ring);
        Measurement first = CountMeasurement(
            id: "first-source-id",
            name: "First points",
            notes: "first note",
            firstSource.FolderPath,
            @"C:\job\Pages\A501",
            scaleMetersPerPt: 0.125,
            [new SKPoint(1, 0), new SKPoint(2, 0)]);
        Measurement second = CountMeasurement(
            id: "second-source-id",
            name: "Second point",
            notes: "second note",
            secondSource.FolderPath,
            @"C:\job\Pages\A502",
            scaleMetersPerPt: 0.25,
            [new SKPoint(3, 0)]);
        firstSource.Measurements.Add(first);
        secondSource.Measurements.Add(second);

        MeasurementMoveResult result = PointMeasurementSplitService.MoveMeasurementsToTakeoff(
            [firstSource, secondSource, target],
            [first, second],
            new Dictionary<Measurement, IReadOnlyList<int>>
            {
                [first] = [1],
                [second] = [0],
            },
            target);

        AssertEqual(1, firstSource.Measurements.Count, "first source should retain its partial section");
        AssertPoints(first.Points, [(1, 0)], "first source remaining point");
        AssertEqual(0, secondSource.Measurements.Count, "second source whole one-point section should move");
        AssertEqual(2, target.Measurements.Count, "target should receive points from both sources");
        AssertTrue(target.Measurements.Any(candidate => ReferenceEquals(candidate, second)), "whole second measurement should preserve identity");
        AssertTrue(target.Measurements.Any(candidate => candidate.Name == "First points" && !ReferenceEquals(candidate, first)), "first source should contribute a partial clone");
        AssertEqual(2, result.SourceItems.Count, "result should include both source takeoffs");
        AssertTrue(result.SourceItems.Contains(firstSource), "result should include first source");
        AssertTrue(result.SourceItems.Contains(secondSource), "result should include second source");
        AssertEqual(3, result.ChangedItems.Count, "both sources and target should be changed");
        AssertEqual(2, result.PageFolders.Count, "result should include both affected pages");
        AssertClose(3, firstSource.Total(0) + secondSource.Total(0) + target.Total(0), "multi-source split should preserve total count");
    }

    public static void PartialSplitPersistsSourceAndTargetRoundTrip()
    {
        string tempParent = Path.Combine(Path.GetTempPath(), "opc_point_split_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempParent);
        try
        {
            OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(tempParent, "Point Split Round Trip");
            TakeoffItem source = OurPlanCoreJobStore.CreateTakeoffItem(
                job,
                job.TakeoffsRoot,
                "Source Count",
                "#224466",
                "point");
            source.UnitPrice = 17.25;
            source.Notes = "source takeoff notes";
            source.CountSymbol = CountDisplaySymbol.Cross;
            TakeoffItem target = OurPlanCoreJobStore.CreateTakeoffItem(
                job,
                job.TakeoffsRoot,
                "Split Count",
                "#55AA77",
                "point");
            target.UnitPrice = source.UnitPrice;
            target.Notes = source.Notes;
            target.CountSymbol = source.CountSymbol;

            Measurement measurement = CountMeasurement(
                id: "persisted-source-id",
                name: "Imported window section",
                notes: "measurement notes",
                source.FolderPath,
                Path.Combine(job.PagesRoot, "A601"),
                scaleMetersPerPt: 0.0625,
                [new SKPoint(1, 2), new SKPoint(3, 4), new SKPoint(5, 6)]);
            measurement.CountSymbol = source.CountSymbol;
            source.Measurements.Add(measurement);

            PointMeasurementSplitService.MoveMeasurementsToTakeoff(
                [source, target],
                [measurement],
                new Dictionary<Measurement, IReadOnlyList<int>>
                {
                    [measurement] = [1],
                },
                target);
            OurPlanCoreJobStore.SaveTakeoffItem(source);
            OurPlanCoreJobStore.SaveTakeoffItem(target);

            TakeoffItem loadedSource = OurPlanCoreJobStore.TryReadTakeoffItem(source.FolderPath)
                ?? throw new InvalidOperationException("persisted source Count was not reloaded");
            TakeoffItem loadedTarget = OurPlanCoreJobStore.TryReadTakeoffItem(target.FolderPath)
                ?? throw new InvalidOperationException("persisted target Count was not reloaded");
            Measurement loadedSourceMeasurement = loadedSource.Measurements.Single();
            Measurement loadedTargetMeasurement = loadedTarget.Measurements.Single();

            AssertEqual("persisted-source-id", loadedSourceMeasurement.Id, "round-trip source id");
            AssertPoints(loadedSourceMeasurement.Points, [(1, 2), (5, 6)], "round-trip source points");
            AssertTrue(
                !string.Equals("persisted-source-id", loadedTargetMeasurement.Id, StringComparison.Ordinal),
                "round-trip target should retain the new id");
            AssertPoints(loadedTargetMeasurement.Points, [(3, 4)], "round-trip target points");
            AssertEqual("Imported window section", loadedTargetMeasurement.Name, "round-trip measurement name");
            AssertEqual("measurement notes", loadedTargetMeasurement.Notes, "round-trip measurement notes");
            AssertEqual(measurement.PageFolder, loadedTargetMeasurement.PageFolder, "round-trip page folder");
            AssertClose(0.0625, loadedTargetMeasurement.ScaleMetersPerPt, "round-trip scale");
            AssertEqual(source.CountSymbol, loadedTarget.CountSymbol, "round-trip target symbol");
            AssertEqual(source.CountSymbol, loadedTargetMeasurement.CountSymbol, "round-trip measurement symbol");
            AssertClose(17.25, loadedTarget.UnitPrice, "round-trip target unit price");
            AssertEqual("source takeoff notes", loadedTarget.Notes, "round-trip target notes");
            AssertClose(3, loadedSource.Total(0) + loadedTarget.Total(0), "round-trip total count");
        }
        finally
        {
            if (Directory.Exists(tempParent))
                Directory.Delete(tempParent, recursive: true);
        }
    }

    private static TakeoffItem CountTakeoff(string name, string folderPath, string color, string symbol) =>
        new()
        {
            Name = name,
            FolderPath = folderPath,
            MeasurementType = "point",
            Color = color,
            CountSymbol = symbol,
        };

    private static Measurement CountMeasurement(
        string id,
        string name,
        string notes,
        string takeoffFolder,
        string pageFolder,
        double scaleMetersPerPt,
        IReadOnlyList<SKPoint> points) =>
        new()
        {
            Id = id,
            Name = name,
            Notes = notes,
            MType = "point",
            Color = "#111111",
            CountSymbol = CountDisplaySymbol.Circle,
            TakeoffFolder = takeoffFolder,
            PageFolder = pageFolder,
            ScaleMetersPerPt = scaleMetersPerPt,
            Points = points.ToList(),
        };

    private static void AssertPoints(
        IReadOnlyList<SKPoint> actual,
        IReadOnlyList<(float X, float Y)> expected,
        string message)
    {
        AssertEqual(expected.Count, actual.Count, $"{message} count");
        for (int i = 0; i < expected.Count; i++)
        {
            AssertClose(expected[i].X, actual[i].X, $"{message} point {i} x");
            AssertClose(expected[i].Y, actual[i].Y, $"{message} point {i} y");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }

    private static void AssertClose(double expected, double actual, string message, double tolerance = 0.000001)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate) && File.Exists(Path.Combine(current.FullName, "ourplancore.csproj")))
                return File.ReadAllText(candidate);
            current = current.Parent;
        }

        throw new FileNotFoundException($"Repository file not found: {relativePath}");
    }
}
