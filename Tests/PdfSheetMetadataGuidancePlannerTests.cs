using OurPlanCore;

internal static class PdfSheetMetadataGuidancePlannerTests
{
    public static void ChoosesStableMiddlePageForEachProfile()
    {
        IReadOnlyList<PageInfo> pages =
        [
            Page("A1.01"),
            Page("S1.01"),
            Page("A1.02"),
            Page("S1.02"),
            Page("A1.03"),
            Page("S1.03"),
            Page("S1.04"),
        ];

        IReadOnlyList<PdfSheetMetadataGuidancePlanItem> plan =
            PdfSheetMetadataGuidancePlanner.Build(pages, _ => false);

        AssertEqual(2, plan.Count, "profile count");
        AssertItem(
            plan[0],
            PdfSheetMetadataCropProfile.Architectural,
            "A1.02",
            3,
            "architectural midpoint");
        AssertItem(
            plan[1],
            PdfSheetMetadataCropProfile.Structural,
            "S1.03",
            4,
            "structural upper midpoint");
    }

    public static void KeepsArchitecturalAndStructuralProfilesSeparate()
    {
        IReadOnlyList<PageInfo> pages =
        [
            Page("A2.01 Floor Plan"),
            Page("Page 8", "Permit Set.pdf"),
            Page("S2.01 Foundation Plan"),
        ];

        IReadOnlyList<PdfSheetMetadataGuidancePlanItem> plan =
            PdfSheetMetadataGuidancePlanner.Build(pages, _ => false);

        AssertEqual(3, plan.Count, "A/S plus unresolved profile count");
        AssertItem(
            plan[0],
            PdfSheetMetadataCropProfile.Architectural,
            "A2.01 Floor Plan",
            1,
            "architectural group");
        AssertItem(
            plan[1],
            PdfSheetMetadataCropProfile.Structural,
            "S2.01 Foundation Plan",
            1,
            "structural group");
        AssertItem(
            plan[2],
            PdfSheetMetadataCropProfile.Default,
            "Page 8",
            1,
            "unresolved group remains bounded instead of being discarded");
    }

    public static void UsesDefaultOnlyWhenNoDisciplineGroupExists()
    {
        IReadOnlyList<PageInfo> pages =
        [
            Page("Page 1"),
            Page("Page 2"),
            Page("Page 3"),
            Page("Page 4"),
        ];

        IReadOnlyList<PdfSheetMetadataGuidancePlanItem> plan =
            PdfSheetMetadataGuidancePlanner.Build(
                pages,
                profile => profile == PdfSheetMetadataCropProfile.Default);

        AssertEqual(4, plan.Count, "an exact Default still needs bounded A/S validation samples");
        AssertItem(
            plan[0],
            PdfSheetMetadataCropProfile.Default,
            "Page 2",
            4,
            "default lower-third sample");
        AssertItem(
            plan[1],
            PdfSheetMetadataCropProfile.Default,
            "Page 3",
            4,
            "default upper-third sample");
        AssertItem(
            plan[2],
            PdfSheetMetadataCropProfile.Default,
            "Page 1",
            4,
            "default far sample");
        AssertItem(
            plan[3],
            PdfSheetMetadataCropProfile.Default,
            "Page 4",
            4,
            "default final far sample");
    }

    public static void SkipsProfilesWithDedicatedTemplates()
    {
        IReadOnlyList<PageInfo> pages =
        [
            Page("A3.01"),
            Page("S3.01"),
        ];

        IReadOnlyList<PdfSheetMetadataGuidancePlanItem> plan =
            PdfSheetMetadataGuidancePlanner.Build(
                pages,
                profile => profile == PdfSheetMetadataCropProfile.Architectural);

        AssertEqual(1, plan.Count, "remaining profile count");
        AssertItem(
            plan[0],
            PdfSheetMetadataCropProfile.Structural,
            "S3.01",
            1,
            "remaining structural profile");
    }

    private static PageInfo Page(string name, string pdfName = "Permit Set.pdf") =>
        new()
        {
            Name = name,
            PdfPath = Path.Combine(@"C:\Plans", pdfName),
        };

    private static void AssertItem(
        PdfSheetMetadataGuidancePlanItem item,
        PdfSheetMetadataCropProfile expectedProfile,
        string expectedPageName,
        int expectedPageCount,
        string message)
    {
        if (item.Profile != expectedProfile ||
            !string.Equals(item.SamplePage.Name, expectedPageName, StringComparison.Ordinal) ||
            item.PageCount != expectedPageCount)
        {
            throw new InvalidOperationException(
                $"{message}: expected {expectedProfile}/{expectedPageName}/{expectedPageCount}, " +
                $"got {item.Profile}/{item.SamplePage.Name}/{item.PageCount}");
        }
    }

    private static void AssertEqual(int expected, int actual, string message)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
