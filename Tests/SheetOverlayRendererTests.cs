using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

internal static class SheetOverlayRendererTests
{
    public static void LongTextExpandsWhenSpaceExists()
    {
        bool shortOk = TryBounds(
            [Entry("Wall")],
            width: 1200,
            height: 280,
            out SKRect shortBounds);
        bool longOk = TryBounds(
            [Entry("Exterior 2x6 framed wall with continuous insulation and structural sheathing")],
            width: 1200,
            height: 280,
            out SKRect longBounds);

        AssertTrue(shortOk && longOk, "both legends should fit the available sheet area");
        AssertTrue(
            longBounds.Width > shortBounds.Width + 40f,
            $"long label should expand the legend instead of staying fixed; short={shortBounds.Width:0.##}, long={longBounds.Width:0.##}");
        AssertTrue(longBounds.Right <= 1192.01f, "expanded legend must stay inside the sheet margin");
    }

    public static void LongTextClampsToVisibleWidth()
    {
        string longName = string.Join(" ", Enumerable.Repeat("VERY-LONG-TAKEOFF-NAME", 12));
        bool ok = TryBounds(
            [Entry(longName)],
            width: 320,
            height: 220,
            out SKRect bounds);

        AssertTrue(ok, "legend should still render when its preferred width exceeds the sheet");
        AssertClose(8f, bounds.Left, "left margin");
        AssertClose(312f, bounds.Right, "right margin");
        AssertClose(304f, bounds.Width, "clamped width");
    }

    public static void MultipleColumnsMeasureTheirOwnContent()
    {
        const string longName =
            "Exterior 2x6 framed wall with continuous insulation and structural sheathing";
        SheetLegendEntry[] shortEntries = Enumerable.Range(0, 6)
            .Select(index => Entry($"Wall {index + 1}"))
            .ToArray();
        SheetLegendEntry[] oneLongColumn = shortEntries.ToArray();
        oneLongColumn[0] = Entry(longName);
        SheetLegendEntry[] twoLongColumns = oneLongColumn.ToArray();
        twoLongColumns[3] = Entry(longName);

        AssertTrue(TryBounds(shortEntries, 1200, 100, out SKRect shortBounds), "short multi-column legend");
        AssertTrue(TryBounds(oneLongColumn, 1200, 100, out SKRect oneLongBounds), "one-long-column legend");
        AssertTrue(TryBounds(twoLongColumns, 1200, 100, out SKRect twoLongBounds), "two-long-column legend");
        AssertTrue(
            oneLongBounds.Width > shortBounds.Width + 40f,
            "one long column should expand independently");
        AssertTrue(
            twoLongBounds.Width > oneLongBounds.Width + 80f,
            "a second long column should add only its own required width");
        AssertTrue(twoLongBounds.Right <= 1192.01f, "multi-column legend must remain inside the sheet");
    }

    public static void DenseLongEntriesPreferFewerReadableColumns()
    {
        const string longName =
            "Exterior 2x6 framed wall with continuous insulation and structural sheathing";
        SheetLegendEntry[] entries = Enumerable.Range(0, 20)
            .Select(_ => Entry(longName))
            .ToArray();

        AssertTrue(TryBounds(entries, 800, 300, out SKRect bounds), "dense long legend should render");
        AssertTrue(
            bounds.Width < 650f,
            $"dense legend should compress rows before splitting long labels into clipped columns; width={bounds.Width:0.##}");
    }

    public static void DenseEntriesStayClippedToBounds()
    {
        SheetLegendEntry[] entries = Enumerable.Range(0, 50)
            .Select(index => Entry($"Dense takeoff {index + 1}"))
            .ToArray();
        const int width = 320;
        const int height = 220;
        AssertTrue(TryBounds(entries, width, height, out SKRect expected), "dense legend bounds");

        SKRect rendered = RenderBounds(entries, width, height, "TopLeft");
        AssertTrue(rendered.Left >= expected.Left - 1.1f, "dense rendering must stay inside the left edge");
        AssertTrue(rendered.Top >= expected.Top - 1.1f, "dense rendering must stay inside the top edge");
        AssertTrue(rendered.Right <= expected.Right + 1.1f, "dense rendering must stay inside the right edge");
        AssertTrue(rendered.Bottom <= expected.Bottom + 1.1f, "dense rendering must stay inside the bottom edge");
    }

    public static void HitBoundsMatchRenderedLayout()
    {
        SheetLegendEntry[] entries =
        [
            Entry(
                "Exterior 2x6 framed wall with continuous insulation",
                "1,248 LF",
                ["Level 1 - Type A fire-rated assembly"]),
            Entry("Interior partitions", "836 LF"),
        ];
        const int width = 760;
        const int height = 260;
        bool ok = SheetOverlayRenderer.TryGetLegendBounds(
            entries,
            0,
            0,
            width,
            height,
            0,
            0,
            width,
            height,
            "BottomRight",
            1f,
            out SKRect expected);
        AssertTrue(ok, "legend bounds should be available for the rendering test");

        SKRect rendered = RenderBounds(entries, width, height, "BottomRight");
        AssertClose(expected.Left, rendered.Left, "rendered left edge", 1.1f);
        AssertClose(expected.Top, rendered.Top, "rendered top edge", 1.1f);
        AssertClose(expected.Right, rendered.Right, "rendered right edge", 1.1f);
        AssertClose(expected.Bottom, rendered.Bottom, "rendered bottom edge", 1.1f);
    }

    private static SheetLegendEntry Entry(
        string name,
        string quantity = "120 LF",
        IReadOnlyList<string>? details = null) =>
        new("#D44A4A", name, quantity, "Line", "LF", details ?? []);

    private static bool TryBounds(
        IReadOnlyList<SheetLegendEntry> entries,
        float width,
        float height,
        out SKRect bounds) =>
        SheetOverlayRenderer.TryGetLegendBounds(
            entries,
            0,
            0,
            width,
            height,
            0,
            0,
            width,
            height,
            "TopLeft",
            1f,
            out bounds);

    private static SKRect RenderBounds(
        IReadOnlyList<SheetLegendEntry> entries,
        int width,
        int height,
        string anchor)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        SheetOverlayRenderer.DrawLegend(
            canvas,
            entries,
            0,
            0,
            width,
            height,
            0,
            0,
            width,
            height,
            anchor,
            1f);
        canvas.Flush();
        return FindRenderedBounds(bitmap);
    }

    private static SKRect FindRenderedBounds(SKBitmap bitmap)
    {
        int minX = bitmap.Width;
        int minY = bitmap.Height;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        AssertTrue(maxX >= minX && maxY >= minY, "legend renderer should produce visible pixels");
        return new SKRect(minX, minY, maxX + 1, maxY + 1);
    }

    private static void AssertClose(float expected, float actual, string label, float tolerance = 0.01f)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{label}: expected {expected:0.###}, got {actual:0.###}");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
