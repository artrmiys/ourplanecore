using OurPlaneCore;

internal static class TakeoffTemplateTests
{
    public static void DefaultsIncludeFramingLinePresets()
    {
        TakeoffTemplateConfig config = TakeoffTemplateConfig.BuildDefault();
        TakeoffTemplateNode framing = config.Template.Roots
            .FirstOrDefault(node => node.IsFolder &&
                                    string.Equals(node.Name, "framing", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("default template should include framing folder");

        string[] expected =
        [
            "Blocking for Drywall",
            "Blocking for Trusses",
            "Ribbon Board",
            "Rim Board",
            "Blocking",
            "Ledger",
            "1x3 Cross Blocking",
            "Plate",
            "Frame",
        ];

        foreach (string name in expected)
        {
            TakeoffTemplateNode item = framing.Children
                .FirstOrDefault(node => !node.IsFolder &&
                                        string.Equals(node.Name, name, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"framing preset missing: {name}");
            AssertEqual("line", item.MeasurementType, $"{name} measurement type");
        }

        TakeoffTemplateNode sqfts = config.Template.Roots.First(node => node.Name == "sqfts");
        AssertTrue(sqfts.Children.Any(node => node.Name == "rf mtl x" && node.MeasurementType == "area"),
            "default template should include roof metal area preset");
    }

    public static void RoutingUsesExistingFolderOrRootFallback()
    {
        string root = Path.Combine(Path.GetTempPath(), "opc_takeoff_template_tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "Pages"));
            Directory.CreateDirectory(Path.Combine(root, "Takeoffs"));
            File.WriteAllText(Path.Combine(root, "Data.xml"), "<Item Class=\"Folder\" Name=\"Template Test\" />");
            var job = new OurPlaneCoreJob
            {
                Name = "Template Test",
                RootPath = root,
            };

            string framing = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "framing");

            AssertEqual(
                framing,
                TakeoffTemplateRouting.ResolveDestinationFolder(job, ["framing"]),
                "existing template folder target");
            AssertEqual(
                job.TakeoffsRoot,
                TakeoffTemplateRouting.ResolveDestinationFolder(job, ["framing", "missing"]),
                "missing nested folder should fall back to root");
            AssertEqual(
                job.TakeoffsRoot,
                TakeoffTemplateRouting.ResolveDestinationFolder(job, ["walls"]),
                "missing top folder should fall back to root");
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
            // Best effort cleanup for temp test jobs.
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }
}
