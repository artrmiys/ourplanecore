using OurPlanCore;

internal static class SampleJobGuideTests
{
    public static void CreatesGuidePagesScreenshotsAndTakeoffs()
    {
        string root = Path.Combine(Path.GetTempPath(), "onc_sample_guide_tests", Guid.NewGuid().ToString("N"));
        try
        {
            OurPlanCoreJob job = SampleJobService.CreateSampleJob(root);
            string guideFolder = Path.Combine(job.PagesRoot, "00. Guide");
            string guideReadme = Path.Combine(job.AIContextRoot, "guide", "README.md");
            string screenshots = Path.Combine(job.AIContextRoot, "guide", "screenshots");

            AssertTrue(Directory.Exists(guideFolder), "sample job should create a 00. Guide pages folder");
            AssertTrue(
                Directory.EnumerateDirectories(guideFolder).Count(path => OurPlanCoreJobStore.IsPageFolder(path)) >= 7,
                "sample guide should import guide pages plus A101 Sample Plan");
            AssertTrue(
                File.Exists(guideReadme) &&
                File.ReadAllText(guideReadme).Contains("Takeoffs panel: R refresh", StringComparison.Ordinal),
                "sample job should write a button-map README under AI_Context/guide");
            AssertTrue(
                Directory.Exists(screenshots) &&
                Directory.EnumerateFiles(screenshots, "*.png").Count() >= 5,
                "sample job should write generated screenshot PNGs");

            IReadOnlyList<TakeoffItem> items = OurPlanCoreJobStore.LoadTakeoffItems(job);
            AssertTrue(items.Any(item => item.IsJoistArea), "sample job should include a joist area takeoff");
            AssertTrue(
                items.Any(item => item.Name.Contains("Eave", StringComparison.OrdinalIgnoreCase)) &&
                items.Any(item => item.Name.Contains("Rake", StringComparison.OrdinalIgnoreCase)),
                "sample job should include roof guide takeoffs");
            AssertTrue(
                items.Any(item => item.Measurements.Any(measurement => measurement.PageFolder.Contains("A101", StringComparison.OrdinalIgnoreCase))),
                "sample measurements should link to A101 Sample Plan");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
