using OurPlanCore;
using SkiaSharp;

internal static class JoistExtraModelTests
{
    public static void CursorClipSelectsOnlyTheFilledLocalInterval()
    {
        Measurement area = Area(directionDegrees: 0);
        area.Holes.Add(
        [
            new SKPoint(4, 2),
            new SKPoint(6, 2),
            new SKPoint(6, 8),
            new SKPoint(4, 8),
        ]);

        AssertTrue(
            JoistTakeoffCalculator.TryClipExtraJoist(area, new SKPoint(2, 5), out JoistExtraSegment left),
            "left filled interval should clip");
        AssertPoint(new SKPoint(0, 5), left.Start, "left start");
        AssertPoint(new SKPoint(4, 5), left.End, "left end");

        AssertTrue(
            JoistTakeoffCalculator.TryClipExtraJoist(area, new SKPoint(8, 5), out JoistExtraSegment right),
            "right filled interval should clip");
        AssertPoint(new SKPoint(6, 5), right.Start, "right start");
        AssertPoint(new SKPoint(10, 5), right.End, "right end");

        AssertFalse(
            JoistTakeoffCalculator.TryClipExtraJoist(area, new SKPoint(5, 5), out _),
            "cursor inside a hole must be rejected");
        AssertFalse(
            JoistTakeoffCalculator.TryClipExtraJoist(area, new SKPoint(12, 5), out _),
            "cursor outside the area must be rejected");
    }

    public static void AreaCutHoleSplitsExtraJoistIntoFilledPieces()
    {
        JoistExtraSegment source = Extra("cut-source", 0, 5, 10, 5);
        var geometry = new AreaBooleanGeometry(
            Rectangle(0, 0, 10, 10),
            [Rectangle(4, 2, 6, 8)]);

        List<JoistExtraSegment> pieces = JoistTakeoffCalculator
            .ClipExtraJoistsToAreaGeometries([source], [geometry])
            .Single();

        AssertEqual(2, pieces.Count, "hole should split one extra into two filled pieces");
        AssertPoint(new SKPoint(0, 5), pieces[0].Start, "left cut piece start");
        AssertPoint(new SKPoint(4, 5), pieces[0].End, "left cut piece end");
        AssertPoint(new SKPoint(6, 5), pieces[1].Start, "right cut piece start");
        AssertPoint(new SKPoint(10, 5), pieces[1].End, "right cut piece end");
        AssertEqual(2, pieces.Select(piece => piece.Id).Distinct(StringComparer.Ordinal).Count(), "split pieces need unique ids");
        AssertTrue(pieces.Any(piece => piece.Id == source.Id), "one split piece should retain the source id");
    }

    public static void AreaCutHoleTangentDoesNotTrimExtraJoist()
    {
        JoistExtraSegment source = Extra("tangent-source", 0, 5, 10, 5);
        var geometry = new AreaBooleanGeometry(
            Rectangle(0, 0, 10, 10),
            [[new SKPoint(4, 4), new SKPoint(6, 4), new SKPoint(5, 5)]]);

        JoistExtraSegment retained = JoistTakeoffCalculator
            .ClipExtraJoistsToAreaGeometries([source], [geometry])
            .Single()
            .Single();

        AssertExtra(
            retained,
            "tangent-source",
            new SKPoint(0, 5),
            new SKPoint(10, 5),
            "hole tangent");
    }

    public static void AreaThroughCutDistributesExtraJoistAcrossSegments()
    {
        JoistExtraSegment source = Extra("through-cut-source", 0, 5, 10, 5);
        AreaBooleanGeometry[] geometries =
        [
            new AreaBooleanGeometry(Rectangle(0, 0, 4, 10), []),
            new AreaBooleanGeometry(Rectangle(6, 0, 10, 10), []),
        ];

        IReadOnlyList<List<JoistExtraSegment>> byArea =
            JoistTakeoffCalculator.ClipExtraJoistsToAreaGeometries([source], geometries);

        AssertEqual(1, byArea[0].Count, "left area should receive one clipped extra");
        AssertEqual(1, byArea[1].Count, "right area should receive one clipped extra");
        AssertPoint(new SKPoint(0, 5), byArea[0][0].Start, "left segment start");
        AssertPoint(new SKPoint(4, 5), byArea[0][0].End, "left segment end");
        AssertPoint(new SKPoint(6, 5), byArea[1][0].Start, "right segment start");
        AssertPoint(new SKPoint(10, 5), byArea[1][0].End, "right segment end");
        AssertFalse(byArea[0][0].Id == byArea[1][0].Id, "child area pieces need unique ids");
    }

    public static void AreaCutTrimsTouchedExtraAndPreservesUntouchedExtra()
    {
        JoistExtraSegment touched = Extra("touched", 0, 2, 10, 2);
        JoistExtraSegment untouched = Extra("untouched", 1, 4, 3, 4);
        JoistExtraSegment removed = Extra("removed", 9, 6, 10, 6);
        var geometry = new AreaBooleanGeometry(Rectangle(0, 0, 8, 10), []);

        List<JoistExtraSegment> pieces = JoistTakeoffCalculator
            .ClipExtraJoistsToAreaGeometries([touched, untouched, removed], [geometry])
            .Single();

        AssertEqual(2, pieces.Count, "fully cut extra should be removed");
        JoistExtraSegment clippedTouched = pieces.Single(piece => piece.Id == "touched");
        AssertPoint(new SKPoint(0, 2), clippedTouched.Start, "edge-trimmed extra start");
        AssertPoint(new SKPoint(8, 2), clippedTouched.End, "edge-trimmed extra end");
        AssertExtra(
            pieces.Single(piece => piece.Id == "untouched"),
            "untouched",
            new SKPoint(1, 4),
            new SKPoint(3, 4),
            "untouched extra");
    }

    public static void ExtraJoistsJoinTotalsButStayInTheirOwnLabelGroup()
    {
        Measurement area = Area(directionDegrees: 0);
        area.JoistSpacingInches = 120;
        area.ExtraJoists.Add(new JoistExtraSegment
        {
            Id = "extra-a",
            Start = new SKPoint(0, 5),
            End = new SKPoint(10, 5),
        });

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(area, 0);

        AssertEqual(3, layout.Count, "two regular joists plus one extra");
        AssertEqual(2, layout.CandidateLineCount, "candidate count stays regular-only");
        AssertEqual(1, layout.Segments.Count(segment => segment.IsExtra), "extra classification");
        AssertClose(30, layout.TotalLengthMeters / 0.3048, "combined order length in feet");

        string[] lines = area.Label(0, UnitMode.Imperial).Split('\n');
        int separator = Array.IndexOf(lines, "Extra");
        AssertTrue(separator > 0, "label should include the Extra separator");
        AssertTrue(lines.Take(separator).Any(line => line.StartsWith("2 pcs @", StringComparison.Ordinal)), "regular group before Extra");
        AssertTrue(lines.Skip(separator + 1).Any(line => line.StartsWith("1 pc @", StringComparison.Ordinal)), "extra group after Extra");
    }

    public static void ExtraJoistUsesTheAreaPitchAndOrderRounding()
    {
        Measurement area = Area(directionDegrees: 0);
        area.JoistPitch = "6:12";
        area.JoistLengthRounding = JoistTakeoffCalculator.RoundingNearestEvenFoot;
        area.ExtraJoists.Add(Extra("pitched-extra", 0, 5, 7, 5));

        JoistSegment extra = JoistTakeoffCalculator.Calculate(area, 0)
            .Segments
            .Single(segment => segment.IsExtra);

        AssertClose(7, extra.FlatLengthMeters / 0.3048, "extra flat length");
        AssertClose(7 * Math.Sqrt(1.25), extra.RawLengthMeters / 0.3048, "extra pitched raw length");
        AssertClose(8, extra.OrderLengthFeet, "extra order length rounds per segment");
    }

    public static void PlanSwiftExportPlacesAllRegularBlocksBeforeOneExtraBlock()
    {
        TakeoffItem item = JoistItem();
        Measurement first = Area(directionDegrees: 0, joistType: "2x10");
        Measurement second = Area(directionDegrees: 90, joistType: "2x10");
        first.ExtraJoists.Add(Extra("extra-1", 0, 4, 10, 4));
        second.ExtraJoists.Add(Extra("extra-2", 4, 0, 4, 10));
        item.Measurements.Add(first);
        item.Measurements.Add(second);

        IReadOnlyList<string> lines = PlanSwiftTakeoffExporter.JoistLabelLines(item, 0, UnitMode.Imperial);
        int separator = lines.ToList().FindIndex(line => line == "Extra");

        AssertEqual(1, lines.Count(line => line == "Extra"), "single Extra separator");
        AssertTrue(separator > 0, "Extra separator follows normal blocks");
        AssertEqual(2, lines.Take(separator).Count(line => line.StartsWith("2x10 ", StringComparison.Ordinal)), "both area headers precede Extra");
        AssertFalse(lines.Skip(separator + 1).Any(line => line.StartsWith("2x10 ", StringComparison.Ordinal)), "no normal area header after Extra");
        AssertTrue(lines.Skip(separator + 1).Any(line => line.StartsWith("2 pcs @", StringComparison.Ordinal)), "extras aggregate after separator");
    }

    public static void AddEndJoistAppliesPerAreaWithoutOverwritingDirections()
    {
        TakeoffItem item = JoistItem();
        item.JoistAddEndJoist = true;
        Measurement horizontal = Area(directionDegrees: 0);
        Measurement vertical = Area(directionDegrees: 90);
        item.Measurements.Add(horizontal);
        item.Measurements.Add(vertical);

        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        JoistLayoutSummary summary = JoistTakeoffCalculator.Summarize(item.Measurements, 0);

        AssertTrue(horizontal.JoistAddEndJoist && vertical.JoistAddEndJoist, "item end-joist option reaches every area");
        AssertClose(0, horizontal.JoistDirectionDegrees, "first saved direction remains unchanged");
        AssertClose(90, vertical.JoistDirectionDegrees, "second saved direction remains unchanged");
        AssertEqual(4, summary.Count, "each area receives its own far-edge joist");
    }

    public static void PerAreaEdgeOverridesSurviveRefreshAndControlBothBoundaries()
    {
        TakeoffItem item = JoistItem();
        item.JoistAddEndJoist = false;
        Measurement area = Area(directionDegrees: 0);
        area.JoistStartEdgeEnabled = false;
        area.JoistEndEdgeEnabled = true;
        area.JoistEdgeOverridesSet = true;
        item.Measurements.Add(area);

        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        (bool start, bool end) = JoistTakeoffCalculator.ResolveEdgeJoists(area);
        AssertFalse(start, "local start-edge override survives refresh");
        AssertTrue(end, "local end-edge override survives refresh");
        AssertEqual(1, JoistTakeoffCalculator.Calculate(area, 0).Count, "far edge alone produces one boundary joist");

        area.JoistStartEdgeEnabled = true;
        AssertEqual(2, JoistTakeoffCalculator.Calculate(area, 0).Count, "both checked boundaries produce two joists");
        area.JoistEndEdgeEnabled = false;
        AssertEqual(1, JoistTakeoffCalculator.Calculate(area, 0).Count, "start edge alone is the default one-side layout");
        area.JoistStartEdgeEnabled = false;
        AssertEqual(0, JoistTakeoffCalculator.Calculate(area, 0).Count, "both unchecked boundaries remove both edge joists");
    }

    public static void SlightlySkewedAreaEdgesStillProduceBoundaryJoists()
    {
        Measurement area = Area(directionDegrees: 0);
        area.Points =
        [
            new SKPoint(0, 0),
            new SKPoint(20, 0),
            new SKPoint(20, 10.2f),
            new SKPoint(0, 10),
        ];
        area.JoistEdgeOverridesSet = true;

        area.JoistStartEdgeEnabled = false;
        area.JoistEndEdgeEnabled = false;
        JoistLayoutResult regularLayout = JoistTakeoffCalculator.Calculate(area, 0);
        int regularCount = regularLayout.Count;

        area.JoistStartEdgeEnabled = true;
        JoistLayoutResult startLayout = JoistTakeoffCalculator.Calculate(area, 0);
        AssertEqual(regularCount + 1, startLayout.Count, "flat start boundary remains usable");
        AssertClose(
            regularLayout.Segments[0].FlatLengthMeters,
            startLayout.Segments[0].FlatLengthMeters,
            "start boundary copies the nearest regular joist length");

        area.JoistStartEdgeEnabled = false;
        area.JoistEndEdgeEnabled = true;
        JoistLayoutResult endLayout = JoistTakeoffCalculator.Calculate(area, 0);
        AssertEqual(regularCount + 1, endLayout.Count, "slightly skewed far boundary anchors a full joist");
        AssertClose(
            regularLayout.Segments[^1].FlatLengthMeters,
            endLayout.Segments[^1].FlatLengthMeters,
            "far boundary copies the nearest regular joist length");

        area.JoistStartEdgeEnabled = true;
        AssertEqual(regularCount + 2, JoistTakeoffCalculator.Calculate(area, 0).Count, "both skewed Area edge options remain independent");
    }

    public static void MeasurementsAndLegacyProjectFileRoundTripExtras()
    {
        WithTempFolder(root =>
        {
            string takeoffFolder = Path.Combine(root, "Takeoffs", "Joists");
            Directory.CreateDirectory(takeoffFolder);
            Measurement source = Area(directionDegrees: 0);
            source.JoistStartEdgeEnabled = false;
            source.JoistEndEdgeEnabled = true;
            source.JoistEdgeOverridesSet = true;
            source.ExtraJoists.Add(Extra("stable-extra-id", 1, 2, 9, 2));

            TakeoffStore.SaveMeasurements(takeoffFolder, [source]);
            Measurement stored = TakeoffStore.LoadMeasurements(takeoffFolder).Single();
            AssertExtra(stored.ExtraJoists.Single(), "stable-extra-id", new SKPoint(1, 2), new SKPoint(9, 2), "measurements.json");
            AssertFalse(stored.JoistStartEdgeEnabled, "measurements.json start-edge override");
            AssertTrue(stored.JoistEndEdgeEnabled && stored.JoistEdgeOverridesSet, "measurements.json end-edge override");

            string pdfPath = Path.Combine(root, "plans.pdf");
            File.WriteAllText(pdfPath, "%PDF-1.4");
            TakeoffItem item = JoistItem();
            item.Measurements.Add(source);
            ProjectFile.Save(pdfPath, source.ScaleMetersPerPt, UnitMode.Imperial, [item]);
            var restored = ProjectFile.Restore(pdfPath);
            Measurement legacy = restored.items.Single().Measurements.Single();
            AssertExtra(legacy.ExtraJoists.Single(), "stable-extra-id", new SKPoint(1, 2), new SKPoint(9, 2), "legacy project file");
        });
    }

    public static void AreaCoalescePreservesAndDeduplicatesExtras()
    {
        TakeoffItem sourceItem = JoistItem("source");
        TakeoffItem targetItem = JoistItem("target");
        Measurement moved = Area(directionDegrees: 0);
        moved.Points = Rectangle(5, 0, 15, 10);
        moved.ExtraJoists.Add(Extra("shared", 5, 2, 15, 2));
        moved.ExtraJoists.Add(Extra("moved-only", 5, 4, 15, 4));
        Measurement existing = Area(directionDegrees: 0);
        existing.ExtraJoists.Add(Extra("shared", 0, 2, 10, 2));
        sourceItem.Measurements.Add(moved);
        targetItem.Measurements.Add(existing);

        MeasurementMoveResult result = MeasurementMergeSplitService.MoveMeasurementsToTakeoff(
            [sourceItem, targetItem],
            [moved],
            targetItem);

        Measurement survivor = result.SelectedMeasurements.Single();
        AssertEqual(2, survivor.ExtraJoists.Count, "union keeps unique extras from both areas");
        AssertEqual(1, survivor.ExtraJoists.Count(extra => extra.Id == "shared"), "duplicate stable id is coalesced");
        AssertTrue(survivor.ExtraJoists.Any(extra => extra.Id == "moved-only"), "removed area extra survives union");
    }

    public static void ExtraJoistModeContinuesUntilDOrEscapeAndRegularJoistsStayDistinct()
    {
        string shortcuts = ReadRepoFile("MainWindow.Shortcuts.cs");
        string extraCommands = ReadRepoFile("MainWindow.TakeoffsExtraJoists.cs");
        string generation = ReadRepoFile("MainWindow.TakeoffsJoistGeneration.cs");
        string viewportInput = ReadRepoFile(Path.Combine("Controls", "PdfViewport.Input.cs"));
        string viewportExtra = ReadRepoFile(Path.Combine("Controls", "PdfViewport.ExtraJoists.cs"));
        string mainWindowXaml = ReadRepoFile("MainWindow.xaml");

        int toggle = extraCommands.IndexOf(
            "if (_viewport.IsExtraJoistPlacementActive)",
            StringComparison.Ordinal);
        int resolve = extraCommands.IndexOf(
            "if (!TryResolveSelectedExtraJoistTarget",
            StringComparison.Ordinal);
        AssertTrue(
            toggle >= 0 && toggle < resolve &&
            extraCommands.Contains("_viewport.CancelExtraJoistPlacement();", StringComparison.Ordinal),
            "D should stop the active Extra Joists mode before resolving a new target");

        AssertTrue(
            shortcuts.Contains("case Key.D:", StringComparison.Ordinal) &&
            shortcuts.Contains("TryStartExtraJoistShortcut()", StringComparison.Ordinal),
            "D should route through the Extra Joists mode toggle first");
        AssertTrue(
            shortcuts.Contains(
                "key == Key.Escape && _viewport.IsExtraJoistPlacementActive",
                StringComparison.Ordinal),
            "Esc should stop Extra Joists mode regardless of viewport focus");

        int clickStart = viewportExtra.IndexOf(
            "private bool HandleExtraJoistPlacementClick",
            StringComparison.Ordinal);
        int clickEnd = clickStart < 0
            ? -1
            : viewportExtra.IndexOf(
                "private bool IsExtraJoistPlacementTargetCurrent",
                clickStart,
                StringComparison.Ordinal);
        AssertTrue(clickStart >= 0 && clickEnd > clickStart, "Extra Joist click handler should be present");
        string clickHandler = viewportExtra[clickStart..clickEnd];
        AssertFalse(
            clickHandler.Contains("_extraJoistPlacementMeasurement = null", StringComparison.Ordinal),
            "a successful click must not end Extra Joists mode");
        AssertTrue(
            clickHandler.Contains("_extraJoistPlacementPreview = CloneExtraJoist(segment)", StringComparison.Ordinal) &&
            clickHandler.Contains("Click again to add another; D or Esc exits", StringComparison.Ordinal),
            "a successful click should keep the ghost and explain continuous placement");

        AssertTrue(
            generation.Contains("List<Measurement> areas = item.Measurements", StringComparison.Ordinal),
            "regular joist refresh should include every Area in the takeoff");
        foreach (string menuSource in new[]
                 {
                     ReadRepoFile("MainWindow.ViewportContextMenu.cs"),
                     ReadRepoFile("MainWindow.TakeoffSections.cs"),
                     ReadRepoFile("MainWindow.TakeoffsMenus.cs"),
                 })
        {
            AssertTrue(
                menuSource.Contains("Refresh Regular Joists in All Area Segments", StringComparison.Ordinal) &&
                menuSource.Contains("Start Extra Joists Mode (D)", StringComparison.Ordinal),
                "all Joist context menus should distinguish regular refresh from Extra mode");
        }
        AssertFalse(
            mainWindowXaml.Contains("BtnAddJoists", StringComparison.Ordinal) ||
            mainWindowXaml.Contains("BtnAddExtraJoist", StringComparison.Ordinal),
            "Joist commands should not occupy the main toolbar");
        AssertTrue(
            viewportInput.Contains("case Key.D: ToolChanged?.Invoke(\"drawline\")", StringComparison.Ordinal),
            "D should remain the Draw Line fallback when no Joist Area segment is selected");
    }

    public static void ExtraJoistsAndAreaEdgesAreEditableInViewport()
    {
        string input = ReadRepoFile(Path.Combine("Controls", "PdfViewport.Input.cs"));
        string rendering = ReadRepoFile(Path.Combine("Controls", "PdfViewport.Rendering.cs"));
        string joistRendering = ReadRepoFile(Path.Combine("Controls", "PdfViewport.JoistRendering.cs"));
        string extra = ReadRepoFile(Path.Combine("Controls", "PdfViewport.ExtraJoists.cs"));
        string edges = ReadRepoFile(Path.Combine("Controls", "PdfViewport.JoistEdgeControls.cs"));
        string selection = ReadRepoFile(Path.Combine("Controls", "PdfViewport.SelectionState.cs"));

        AssertTrue(
            input.Contains("TryBeginExtraJoistEdit(pdf)", StringComparison.Ordinal) &&
            input.Contains("TryUpdateExtraJoistDrag", StringComparison.Ordinal) &&
            input.Contains("FinishExtraJoistDrag()", StringComparison.Ordinal),
            "mouse input must select, drag, and finish an existing Extra Joist");
        AssertTrue(
            extra.Contains("TryDeleteSelectedExtraJoist", StringComparison.Ordinal) &&
            extra.Contains("JoistTakeoffCalculator.TryClipExtraJoist(owner, pdf", StringComparison.Ordinal),
            "selected Extra Joists must delete and stay clipped while moving");
        AssertTrue(
            selection.IndexOf("TryDeleteSelectedExtraJoist()", StringComparison.Ordinal) <
            selection.IndexOf("TryDeleteSelectedCutRegions()", StringComparison.Ordinal),
            "Delete must remove the selected Extra Joist before its owning Area");
        AssertTrue(
            input.Contains("TryToggleJoistEdgeControl(pdf)", StringComparison.Ordinal) &&
            rendering.Contains("DrawJoistEdgeControls(canvas)", StringComparison.Ordinal) &&
            edges.Contains("area.JoistEdgeOverridesSet = true", StringComparison.Ordinal) &&
            edges.Contains("RawMeasurementBounds(area)", StringComparison.Ordinal) &&
            edges.Contains("float left = bounds.Left + inset", StringComparison.Ordinal) &&
            edges.Contains("new SKPoint(left, bounds.Top + inset)", StringComparison.Ordinal) &&
            edges.Contains("new SKPoint(left, bounds.Bottom - inset)", StringComparison.Ordinal) &&
            edges.Contains("TopJoistEdgeControlSide(area)", StringComparison.Ordinal) &&
            edges.Contains("OppositeEdgeControlSide(topSide)", StringComparison.Ordinal),
            "every selected Joist Area must use fixed left checkboxes mapped to the visual top/left and bottom/right edges");
        AssertTrue(
            joistRendering.Contains("segment.IsExtra && !selectedExtra", StringComparison.Ordinal) &&
            joistRendering.Contains("extraGlow", StringComparison.Ordinal),
            "unselected Extra Joists must retain a subtle distinguishing glow");
    }

    private static TakeoffItem JoistItem(string name = "Joists") =>
        new()
        {
            Name = name,
            MeasurementType = "area",
            IsJoistTakeoff = true,
            JoistType = "2x10",
            JoistSpacingInches = 120,
            JoistLengthRounding = JoistTakeoffCalculator.RoundingNone,
            JoistAddEndJoist = true,
        };

    private static Measurement Area(double directionDegrees, string joistType = "2x10") =>
        new()
        {
            MType = "area",
            JoistEnabled = true,
            JoistType = joistType,
            JoistDirectionLocked = true,
            JoistDirectionDegrees = directionDegrees,
            JoistSpacingInches = 120,
            JoistLengthRounding = JoistTakeoffCalculator.RoundingNone,
            JoistAddEndJoist = true,
            ScaleMetersPerPt = 0.3048,
            Points = Rectangle(0, 0, 10, 10),
        };

    private static List<SKPoint> Rectangle(float left, float top, float right, float bottom) =>
    [
        new SKPoint(left, top),
        new SKPoint(right, top),
        new SKPoint(right, bottom),
        new SKPoint(left, bottom),
    ];

    private static JoistExtraSegment Extra(string id, float x1, float y1, float x2, float y2) =>
        new()
        {
            Id = id,
            Start = new SKPoint(x1, y1),
            End = new SKPoint(x2, y2),
        };

    private static void AssertExtra(
        JoistExtraSegment extra,
        string id,
        SKPoint start,
        SKPoint end,
        string context)
    {
        AssertEqual(id, extra.Id, $"{context} id");
        AssertPoint(start, extra.Start, $"{context} start");
        AssertPoint(end, extra.End, $"{context} end");
    }

    private static void WithTempFolder(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "ourplancore-extra-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            string projectPath = Path.Combine(current.FullName, "ourplancore.csproj");
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(projectPath) && File.Exists(candidate))
                return File.ReadAllText(candidate);
            current = current.Parent;
        }

        throw new FileNotFoundException($"Repository file not found: {relativePath}");
    }

    private static void AssertPoint(SKPoint expected, SKPoint actual, string message)
    {
        AssertClose(expected.X, actual.X, $"{message} X");
        AssertClose(expected.Y, actual.Y, $"{message} Y");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);

    private static void AssertEqual(int expected, int actual, string message)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
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
}
