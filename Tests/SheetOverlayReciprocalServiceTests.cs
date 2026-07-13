using OurPlanCore;

internal static class SheetOverlayReciprocalServiceTests
{
    public static void InvertsTransformRoundTrip()
    {
        double offsetX = 42.5;
        double offsetY = -18.25;
        double scale = 1.35;
        double rotation = 7.5;
        (double X, double Y) overlayPoint = (175.0, 88.0);

        (double X, double Y) basePoint = Transform(overlayPoint.X, overlayPoint.Y, offsetX, offsetY, scale, rotation);
        SheetOverlayTransformValues inverse = SheetOverlayReciprocalService.Invert(offsetX, offsetY, scale, rotation);
        (double X, double Y) roundTrip = Transform(
            basePoint.X,
            basePoint.Y,
            inverse.OffsetXPt,
            inverse.OffsetYPt,
            inverse.OverlayScale,
            inverse.OverlayRotationDegrees);

        AssertClose(overlayPoint.X, roundTrip.X, "reciprocal x");
        AssertClose(overlayPoint.Y, roundTrip.Y, "reciprocal y");
        AssertClose(1.0 / scale, inverse.OverlayScale, "reciprocal scale");
        AssertClose(-rotation, inverse.OverlayRotationDegrees, "reciprocal rotation");
    }

    public static void WritesOnlyEmptyOrExistingReciprocalTargets()
    {
        string baseFolder = TestPath("Base");
        PageInfo empty = Page("Overlay", "");
        PageInfo reciprocal = Page("Overlay", baseFolder);
        PageInfo unrelated = Page("Overlay", TestPath("Other"));

        AssertTrue(
            SheetOverlayReciprocalService.ShouldWriteReciprocal(empty, baseFolder),
            "empty overlay page should accept reciprocal compare state");
        AssertTrue(
            SheetOverlayReciprocalService.ShouldWriteReciprocal(reciprocal, baseFolder),
            "existing reciprocal page should be updated");
        AssertFalse(
            SheetOverlayReciprocalService.ShouldWriteReciprocal(unrelated, baseFolder),
            "an unrelated overlay compare should not be overwritten");
    }

    public static void SyncWritesAndClearsReciprocalSource()
    {
        WithTempJob("Sheet Overlay Reciprocal", job =>
        {
            PageInfo basePage = CreatePageItem(job, "S101");
            PageInfo overlayPage = CreatePageItem(job, "S102");
            OurPlanCoreJobStore.SavePageOverlay(basePage.FolderPath, overlayPage.FolderPath, "#1E88E5", 0.73);
            OurPlanCoreJobStore.SavePageOverlayTransform(basePage.FolderPath, 42.5, -18.25, 1.35, 7.5);
            OurPlanCoreJobStore.SavePageOverlayVisibility(basePage.FolderPath, true);

            PageInfo latestBase = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
                ?? throw new InvalidOperationException("base page missing");
            bool synced = SheetOverlayReciprocalService.TrySync(latestBase, out string reciprocalFolder);
            PageInfo reciprocalPage = OurPlanCoreJobStore.TryReadPage(overlayPage.FolderPath)
                ?? throw new InvalidOperationException("reciprocal page missing");
            SheetOverlayTransformValues expected = SheetOverlayReciprocalService.Invert(
                latestBase.OverlayOffsetXPt,
                latestBase.OverlayOffsetYPt,
                latestBase.OverlayScale,
                latestBase.OverlayRotationDegrees);

            AssertTrue(synced, "reciprocal sync should write an empty overlay target");
            AssertEqual(overlayPage.FolderPath, reciprocalFolder, "reciprocal folder");
            AssertEqual(basePage.FolderPath, reciprocalPage.OverlayPageFolder, "reciprocal overlay page");
            AssertEqual("#1E88E5", reciprocalPage.OverlayColor, "reciprocal overlay color");
            AssertClose(0.73, reciprocalPage.OverlayOpacity, "reciprocal opacity");
            AssertClose(expected.OffsetXPt, reciprocalPage.OverlayOffsetXPt, "reciprocal offset x");
            AssertClose(expected.OffsetYPt, reciprocalPage.OverlayOffsetYPt, "reciprocal offset y");
            AssertClose(expected.OverlayScale, reciprocalPage.OverlayScale, "reciprocal scale");
            AssertClose(expected.OverlayRotationDegrees, reciprocalPage.OverlayRotationDegrees, "reciprocal rotation");

            bool cleared = SheetOverlayReciprocalService.TryClear(latestBase, out string clearedFolder);
            PageInfo clearedPage = OurPlanCoreJobStore.TryReadPage(overlayPage.FolderPath)
                ?? throw new InvalidOperationException("cleared reciprocal page missing");

            AssertTrue(cleared, "reciprocal clear should remove the target that points back to the base page");
            AssertEqual(overlayPage.FolderPath, clearedFolder, "cleared reciprocal folder");
            AssertEqual("", clearedPage.OverlayPageFolder, "cleared reciprocal overlay");
        });
    }

    private static PageInfo Page(string name, string overlayPageFolder) =>
        new()
        {
            Name = name,
            FolderPath = TestPath(name),
            OverlayPageFolder = overlayPageFolder,
        };

    private static string TestPath(string leaf) =>
        Path.Combine(Path.GetTempPath(), "onc_overlay_reciprocal_tests", leaf);

    private static (double X, double Y) Transform(
        double x,
        double y,
        double offsetX,
        double offsetY,
        double scale,
        double rotationDegrees)
    {
        double radians = rotationDegrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double scaledX = x * scale;
        double scaledY = y * scale;
        return (
            offsetX + scaledX * cos - scaledY * sin,
            offsetY + scaledX * sin + scaledY * cos);
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
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static void AssertClose(double expected, double actual, string message)
    {
        if (Math.Abs(expected - actual) > 0.0001)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static PageInfo CreatePageItem(OurPlanCoreJob job, string name) =>
        OurPlanCoreJobStore.CreatePageFromPdf(job, CreateSourcePdf(job), name, job.PagesRoot);

    private static string CreateSourcePdf(OurPlanCoreJob job)
    {
        string sourcePdf = Path.Combine(job.RootPath, "source.pdf");
        if (!File.Exists(sourcePdf))
            File.WriteAllText(sourcePdf, "%PDF-1.4 test");
        return sourcePdf;
    }

    private static void WithTempJob(string name, Action<OurPlanCoreJob> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "onc_overlay_reciprocal_jobs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(root, name);
            action(job);
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
            // Best-effort cleanup for temp files that may still be scanned by the OS.
        }
    }
}
