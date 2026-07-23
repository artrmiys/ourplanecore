using OurPlanCore;
using SkiaSharp;

internal static class CutRegionSelectionRegressionTests
{
    public static void EnclosingMarqueeFindsCutoutWithoutSelectingParentBoundary()
    {
        Measurement parent = Area(
            "facade",
            Rect(0, 0, 200, 120),
            Rect(70, 35, 110, 75));
        var marquee = new SKRect(65, 30, 115, 80);

        IReadOnlyList<CutRegionRef> hits =
            CutRegionSelectionService.FindInMarquee([parent], marquee, selectTouched: false);

        AssertEqual(1, hits.Count, "the enclosed hole must be a first-class marquee hit");
        AssertTrue(ReferenceEquals(parent, hits[0].Parent), "the hit must retain its parent identity");
        AssertFalse(
            CutRegionSelectionService.OuterBoundaryIntersectsRect(parent.Points, marquee),
            "a marquee around the window must not turn the enclosing facade Area into the selected object");
    }

    public static void MixedTransformKeepsRelativeGeometryAndLeavesParentContourFixed()
    {
        Measurement parent = Area(
            "facade",
            Rect(0, 0, 200, 120),
            Rect(70, 35, 110, 75));
        var line = new Measurement
        {
            Id = "trim",
            MType = "line",
            Points = [new SKPoint(72, 37), new SKPoint(108, 73)],
        };
        List<SKPoint> originalOuter = parent.Points.ToList();
        SKPoint originalLine = line.Points[0];
        SKPoint originalHole = parent.Holes[0][0];
        var offset = new SKPoint(45, 18);

        CutRegionSelectionService.ApplyGeometryTransform(
            [line],
            [new CutRegionRef(parent, 0)],
            point => new SKPoint(point.X + offset.X, point.Y + offset.Y));

        AssertPoint(originalOuter[0], parent.Points[0], "parent Area outer contour");
        AssertPoint(
            new SKPoint(originalLine.X + offset.X, originalLine.Y + offset.Y),
            line.Points[0],
            "selected Line");
        AssertPoint(
            new SKPoint(originalHole.X + offset.X, originalHole.Y + offset.Y),
            parent.Holes[0][0],
            "selected cutout");
        AssertPoint(
            new SKPoint(
                originalLine.X - originalHole.X,
                originalLine.Y - originalHole.Y),
            new SKPoint(
                line.Points[0].X - parent.Holes[0][0].X,
                line.Points[0].Y - parent.Holes[0][0].Y),
            "mixed-bundle relative offset");
    }

    public static void MixedRotateUsesOnePivotAndLeavesParentContourFixed()
    {
        var pivot = new SKPoint(100, 75);
        AssertMixedTransform(
            "rotate",
            point => new SKPoint(
                pivot.X - (point.Y - pivot.Y),
                pivot.Y + (point.X - pivot.X)));
    }

    public static void MixedMirrorsUseOnePivotAndLeaveParentContourFixed()
    {
        var pivot = new SKPoint(100, 75);
        AssertMixedTransform(
            "horizontal mirror",
            point => new SKPoint(2 * pivot.X - point.X, point.Y));
        AssertMixedTransform(
            "vertical mirror",
            point => new SKPoint(point.X, 2 * pivot.Y - point.Y));
    }

    public static void MixedScaleUsesOnePivotAndLeavesParentContourFixed()
    {
        var pivot = new SKPoint(100, 75);
        const float factor = 1.5f;
        AssertMixedTransform(
            "scale",
            point => new SKPoint(
                pivot.X + (point.X - pivot.X) * factor,
                pivot.Y + (point.Y - pivot.Y) * factor));
    }

    public static void PasteKeepsSourceParentOnlyWhileCutoutStillFits()
    {
        Measurement source = Area("source", Rect(0, 0, 100, 100));
        Measurement destination = Area("destination", Rect(150, 0, 250, 100));
        IReadOnlyList<SKPoint> original = Rect(20, 20, 40, 40);

        CutRegionPasteTargetResult sameWindow = CutRegionSelectionService.ResolvePasteTarget(
            original,
            source.Id,
            explicitTarget: destination,
            [source, destination]);
        AssertTrue(ReferenceEquals(source, sameWindow.Target), "source parent must win while geometry remains inside it");

        IReadOnlyList<SKPoint> moved = original
            .Select(point => new SKPoint(point.X + 150, point.Y))
            .ToList();
        CutRegionPasteTargetResult otherWindow = CutRegionSelectionService.ResolvePasteTarget(
            moved,
            source.Id,
            explicitTarget: destination,
            [source, destination]);
        AssertTrue(
            ReferenceEquals(destination, otherWindow.Target),
            "an explicitly selected containing Area must receive a cutout moved outside its source parent");
    }

    public static void AmbiguousPasteDoesNotChooseOrMutateAnArea()
    {
        Measurement first = Area("first", Rect(0, 0, 200, 200));
        Measurement second = Area("second", Rect(0, 0, 200, 200));
        IReadOnlyList<SKPoint> cutout = Rect(50, 50, 80, 80);
        int firstHoleCount = first.Holes.Count;
        int secondHoleCount = second.Holes.Count;

        CutRegionPasteTargetResult result = CutRegionSelectionService.ResolvePasteTarget(
            cutout,
            sourceParentId: "missing",
            explicitTarget: null,
            [first, second]);

        AssertFalse(result.Success, "an ambiguous paste must not choose an Area");
        AssertTrue(
            result.Error.Contains("More than one Area", StringComparison.Ordinal),
            "an ambiguous paste must explain that the destination Area must be selected");
        AssertEqual(firstHoleCount, first.Holes.Count, "first Area must remain untouched");
        AssertEqual(secondHoleCount, second.Holes.Count, "second Area must remain untouched");
    }

    public static void AmbiguousBundlePreflightHasZeroMutations()
    {
        Measurement first = Area("first", Rect(0, 0, 200, 200));
        Measurement second = Area("second", Rect(0, 0, 200, 200));
        CutRegionClipboardTemplate[] templates =
        [
            new("missing", Rect(50, 50, 80, 80)),
        ];
        int firstHoleCount = first.Holes.Count;
        int secondHoleCount = second.Holes.Count;

        bool success = CutRegionSelectionService.TryResolvePasteBundle(
            templates,
            offset: default,
            explicitTarget: null,
            [first, second],
            excluded: null,
            out var reservations,
            out string error);

        AssertFalse(success, "ambiguous containing Areas must fail the whole preflight");
        AssertEqual(0, reservations.Count, "an ambiguous preflight must reserve nothing");
        AssertTrue(
            error.Contains("More than one Area", StringComparison.Ordinal),
            "ambiguity must produce an actionable status");
        AssertEqual(firstHoleCount, first.Holes.Count, "first candidate must remain untouched");
        AssertEqual(secondHoleCount, second.Holes.Count, "second candidate must remain untouched");
    }

    public static void OneCutoutFailureCancelsTheEntireBundle()
    {
        Measurement baseArea = Area("base", Rect(0, 0, 100, 100));
        CutRegionClipboardTemplate[] templates =
        [
            new("missing-a", Rect(10, 10, 25, 25)),
            new("missing-b", Rect(150, 150, 165, 165)),
        ];
        int before = baseArea.Holes.Count;

        bool success = CutRegionSelectionService.TryResolvePasteBundle(
            templates,
            offset: default,
            explicitTarget: null,
            [baseArea],
            excluded: null,
            out var reservations,
            out _);

        AssertFalse(success, "one unresolved cutout must cancel the complete bundle");
        AssertEqual(0, reservations.Count, "a partially resolved bundle must discard earlier reservations");
        AssertEqual(before, baseArea.Holes.Count, "preflight failure must leave the valid target untouched");
    }

    public static void ExplicitSelectedAreaResolvesTheWholeAmbiguousBundle()
    {
        Measurement selected = Area("selected", Rect(0, 0, 200, 200));
        Measurement overlapping = Area("overlapping", Rect(0, 0, 200, 200));
        CutRegionClipboardTemplate[] templates =
        [
            new("missing-a", Rect(20, 20, 35, 35)),
            new("missing-b", Rect(60, 60, 85, 85)),
        ];

        bool success = CutRegionSelectionService.TryResolvePasteBundle(
            templates,
            offset: default,
            explicitTarget: selected,
            [selected, overlapping],
            excluded: null,
            out var reservations,
            out string error);

        AssertTrue(success, $"explicit target should resolve the bundle: {error}");
        AssertEqual(2, reservations.Count, "every template must receive a reservation");
        AssertTrue(
            reservations.All(reservation => ReferenceEquals(selected, reservation.Target)),
            "all templates must reserve the explicitly selected Area");
    }

    public static void ConcaveBoundaryRejectsAnEdgeThatLeavesAndReenters()
    {
        List<SKPoint> concaveOuter =
        [
            new SKPoint(0, 0),
            new SKPoint(100, 0),
            new SKPoint(100, 100),
            new SKPoint(47, 100),
            new SKPoint(47, 40),
            new SKPoint(43, 40),
            new SKPoint(43, 100),
            new SKPoint(0, 100),
        ];
        IReadOnlyList<SKPoint> crossingCutout = Rect(30, 70, 70, 80);

        AssertFalse(
            CutRegionSelectionService.FitsInsideOuterBoundary(crossingCutout, concaveOuter),
            "an edge crossing a narrow concave notch must fail exact containment");
    }

    public static void PastedOverlayAreaIsExcludedFromBaseAreaResolution()
    {
        Measurement baseArea = Area("base", Rect(0, 0, 200, 200));
        Measurement pastedOverlay = Area("overlay", Rect(40, 40, 100, 100));
        IReadOnlyList<SKPoint> cutout = Rect(55, 55, 75, 75);

        CutRegionPasteTargetResult result = CutRegionSelectionService.ResolvePasteTarget(
            cutout,
            sourceParentId: "missing",
            explicitTarget: null,
            [baseArea, pastedOverlay],
            new HashSet<Measurement> { pastedOverlay });

        AssertTrue(
            ReferenceEquals(baseArea, result.Target),
            "the independently pasted Area overlay must not steal the cutout from the existing base Area");
    }

    public static void MixedPastePreflightRunsBeforeAnyMeasurementMutation()
    {
        string mainWindowClipboard = ReadRepoFile("MainWindow.MeasurementClipboard.cs");
        int preflight = mainWindowClipboard.IndexOf(
            "TryPreflightPendingMixedCutRegionPaste",
            StringComparison.Ordinal);
        int reservationValidation = mainWindowClipboard.IndexOf(
            "ValidatePendingMixedCutRegionPasteReservation",
            Math.Max(preflight, 0),
            StringComparison.Ordinal);
        int measurementLoop = mainWindowClipboard.IndexOf(
            "foreach (MeasurementClipboardEntry entry",
            Math.Max(reservationValidation, 0),
            StringComparison.Ordinal);
        AssertTrue(
            preflight >= 0 &&
            reservationValidation > preflight &&
            measurementLoop > reservationValidation,
            "mixed cutout targets must be reserved and revalidated before takeoff measurements are created");

        string cutoutClipboard = ReadRepoFile("Controls/PdfViewport.CutRegionClipboard.cs");
        AssertTrue(
            cutoutClipboard.Contains("_reservedMixedCutRegionPaste", StringComparison.Ordinal) &&
            cutoutClipboard.Contains("TryResolvePasteBundle(", StringComparison.Ordinal),
            "completion must consume the all-or-none preflight reservation");
    }

    public static void MixedPasteRollsBackEveryUncommittedMutation()
    {
        string mainWindowClipboard = ReadRepoFile("MainWindow.MeasurementClipboard.cs");
        AssertTrue(
            mainWindowClipboard.Contains("bool pasteCommitted = false;", StringComparison.Ordinal) &&
            mainWindowClipboard.Contains("if (!pasteCommitted)", StringComparison.Ordinal) &&
            mainWindowClipboard.Contains("RollBackUncommittedMeasurementPaste(", StringComparison.Ordinal) &&
            mainWindowClipboard.Contains("MoveUncommittedTakeoffFoldersToRecovery(", StringComparison.Ordinal) &&
            mainWindowClipboard.Contains("CreateTakeoffUndoTrashRoot(", StringComparison.Ordinal) &&
            mainWindowClipboard.Contains("QueueTakeoffAutosave(changedItems);", StringComparison.Ordinal),
            "measurement, tree, and provisional takeoff-folder mutations must roll back before bundle commit");

        int complete = mainWindowClipboard.IndexOf(
            "CompletePendingMixedCutRegionPaste(",
            StringComparison.Ordinal);
        int committed = mainWindowClipboard.IndexOf(
            "pasteCommitted = true;",
            Math.Max(complete, 0),
            StringComparison.Ordinal);
        int autosave = mainWindowClipboard.IndexOf(
            "QueueTakeoffAutosave(changedItems);",
            Math.Max(committed, 0),
            StringComparison.Ordinal);
        AssertTrue(
            complete >= 0 && committed > complete && autosave > committed,
            "autosave must start only after measurements and cutouts commit as one bundle");

        string cutoutClipboard = ReadRepoFile("Controls/PdfViewport.CutRegionClipboard.cs");
        AssertTrue(
            cutoutClipboard.Contains("throw new InvalidOperationException(", StringComparison.Ordinal) &&
            cutoutClipboard.Contains("Mixed paste reservation was lost before", StringComparison.Ordinal) &&
            cutoutClipboard.Contains("target.Holes = CloneHoles(holes);", StringComparison.Ordinal) &&
            cutoutClipboard.Contains("int undoDepthBefore = _undoStack.Count;", StringComparison.Ordinal) &&
            cutoutClipboard.Contains("_undoStack.RemoveRange(", StringComparison.Ordinal),
            "lost reservations and cutout callback/undo failures must not leave a partial bundle");
    }

    private static Measurement Area(
        string id,
        List<SKPoint> outer,
        params List<SKPoint>[] holes) =>
        new()
        {
            Id = id,
            MType = "area",
            Points = outer,
            Holes = holes.Select(hole => hole.ToList()).ToList(),
        };

    private static List<SKPoint> Rect(float left, float top, float right, float bottom) =>
    [
        new SKPoint(left, top),
        new SKPoint(right, top),
        new SKPoint(right, bottom),
        new SKPoint(left, bottom),
    ];

    private static void AssertMixedTransform(string label, Func<SKPoint, SKPoint> transform)
    {
        Measurement parent = Area(
            "facade",
            Rect(0, 0, 200, 120),
            Rect(70, 50, 90, 70));
        var line = new Measurement
        {
            Id = "trim",
            MType = "line",
            Points = [new SKPoint(110, 80), new SKPoint(130, 100)],
        };
        List<SKPoint> originalOuter = parent.Points.ToList();
        SKPoint originalHole = parent.Holes[0][0];
        SKPoint originalLine = line.Points[0];

        CutRegionSelectionService.ApplyGeometryTransform(
            [line],
            [new CutRegionRef(parent, 0)],
            transform);

        for (int i = 0; i < originalOuter.Count; i++)
            AssertPoint(originalOuter[i], parent.Points[i], $"{label} parent Area outer contour");

        AssertPoint(transform(originalHole), parent.Holes[0][0], $"{label} selected cutout");
        AssertPoint(transform(originalLine), line.Points[0], $"{label} selected Line");
        SKPoint expectedRelative = RelativeVector(transform(originalHole), transform(originalLine));
        SKPoint actualRelative = RelativeVector(parent.Holes[0][0], line.Points[0]);
        AssertPoint(expectedRelative, actualRelative, $"{label} mixed-bundle relative layout");
    }

    private static SKPoint RelativeVector(SKPoint from, SKPoint to) =>
        new(to.X - from.X, to.Y - from.Y);

    private static void AssertPoint(SKPoint expected, SKPoint actual, string label)
    {
        const float tolerance = 0.0001f;
        if (Math.Abs(expected.X - actual.X) > tolerance ||
            Math.Abs(expected.Y - actual.Y) > tolerance)
        {
            throw new InvalidOperationException(
                $"{label}: expected ({expected.X}, {expected.Y}), got ({actual.X}, {actual.Y}).");
        }
    }

    private static void AssertEqual(int expected, int actual, string label)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    private static string RepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ourplancore.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("OurPlanCore repository root was not found.");
    }
}
