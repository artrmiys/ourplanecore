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

    public static void MeasurementsAndLegacyProjectFileRoundTripExtras()
    {
        WithTempFolder(root =>
        {
            string takeoffFolder = Path.Combine(root, "Takeoffs", "Joists");
            Directory.CreateDirectory(takeoffFolder);
            Measurement source = Area(directionDegrees: 0);
            source.ExtraJoists.Add(Extra("stable-extra-id", 1, 2, 9, 2));

            TakeoffStore.SaveMeasurements(takeoffFolder, [source]);
            Measurement stored = TakeoffStore.LoadMeasurements(takeoffFolder).Single();
            AssertExtra(stored.ExtraJoists.Single(), "stable-extra-id", new SKPoint(1, 2), new SKPoint(9, 2), "measurements.json");

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
