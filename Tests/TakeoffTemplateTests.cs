using OurPlaneCore;

internal static class TakeoffTemplateTests
{
    public static void DefaultsIncludeFramingLinePresets()
    {
        TakeoffTemplateConfig config = TakeoffTemplateConfig.BuildDefault();
        string[] framingExpected =
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

        foreach (string name in framingExpected)
            AssertTemplateItem(config, ["framing"], name, "line");

        AssertTemplateItem(config, ["sqfts"], "rf mtl x", "area");
        AssertTemplateItem(config, ["sqfts"], "overframe x", "area");
        AssertTemplateItem(config, ["gables", "gable trusses"], "gable truss", "area");
        AssertTemplateItem(config, ["gables", "gable stick"], "gable stick", "area");
        AssertTemplateItem(config, ["eves rakes", "eves"], "Eve", "line");
        AssertTemplateItem(config, ["eves rakes", "rakes"], "Rake", "line");
        AssertTemplateItem(config, ["trussheel"], "Truss Heel", "line");
        AssertTemplateItem(config, ["walls"], "corners", "point");
        AssertTemplateItem(config, ["walls", "1st floor walls"], "corners", "point");
        AssertTemplateItem(config, ["walls", "1st floor walls"], "ext", "line");
        AssertTemplateItem(config, ["walls", "1st floor walls"], "ext 2x6", "line");
        AssertTemplateItem(config, ["walls", "1st floor walls"], "ext 2x4", "line");
        AssertTemplateItem(config, ["walls", "1st floor walls"], "cor 2x4", "line");
        AssertTemplateItem(config, ["walls", "1st floor walls"], "cor 2x6", "line");
        AssertTemplateItem(config, ["walls", "1st floor walls"], "cor (2) 2x4", "line");
        AssertTemplateItem(config, ["walls", "1st floor walls"], "cor (2) 2x6", "line");
        AssertTemplateColor(config, ["walls"], "corners", "#FF1744");
        AssertTemplateColor(config, ["walls"], "ext", "#2E7D32");
        AssertTemplateColor(config, ["walls", "1st floor walls"], "ext 2x6", "#00B8D4");
        AssertTemplateMissingItem(config, ["walls"], "unit");
        AssertTemplateMissingItem(config, ["walls"], "corr");
        AssertTemplateMissingItem(config, ["walls"], "parapet");
        AssertTemplateMissingItem(config, ["walls"], "shaft");
        AssertDistinctTemplateColors(
            config,
            ["walls"],
            ["corners", "ext", "ext 2x6", "ext 2x4", "ext 2x8", "cor", "cor 2x4", "cor 2x6"]);
        AssertTemplateItem(config, ["parapets"], "prpt 0.0 0.0 0.0", "line");
        AssertTemplateMissingItem(config, ["parapets"], "parapet");
        AssertTemplateItem(config, ["walls", "shaft walls"], "shaft 1st", "line");
        AssertTemplateItem(config, ["walls", "shaft walls"], "shaft 5th", "line");
        AssertTemplateMissingItem(config, ["walls", "shaft walls"], "ext");
        AssertTemplateMissingFolder(config, ["framing", "1st floor framing"]);
        AssertTemplateMissingFolder(config, ["framing", "roof framing"]);
        AssertTemplateMissingFolder(config, ["units"]);
        AssertTemplateMissingFolder(config, ["shear walls - holdowns - ties"]);
        AssertTemplateMissingFolder(config, ["siding"]);
        AssertTemplateMissingFolder(config, ["trims"]);
        AssertTemplateMissingFolder(config, ["drywalls"]);
        AssertEqual(
            TakeoffTemplateConfig.CurrentBuiltInVersion.ToString(),
            config.BuiltInVersion.ToString(),
            "default template built-in version");
    }

    public static void UpgradeMergesWikiPresetsIntoOldConfigs()
    {
        var oldConfig = new TakeoffTemplateConfig
        {
            BuiltInVersion = 1,
            Template = new TakeoffTemplate
            {
                Name = "Old User Template",
                Roots =
                [
                    new TakeoffTemplateNode
                    {
                        Name = "sqfts",
                        IsFolder = true,
                        Children =
                        [
                            new TakeoffTemplateNode
                            {
                                Name = "custom sqft",
                                IsFolder = false,
                                MeasurementType = "area",
                                Color = "#123456",
                            },
                        ],
                    },
                    new TakeoffTemplateNode
                    {
                        Name = "walls",
                        IsFolder = true,
                        Children =
                        [
                            new TakeoffTemplateNode
                            {
                                Name = "ext",
                                IsFolder = false,
                                MeasurementType = "line",
                                Color = "#FF4444",
                            },
                            new TakeoffTemplateNode
                            {
                                Name = "corners",
                                IsFolder = false,
                                MeasurementType = "line",
                                Color = "#FF4444",
                            },
                            new TakeoffTemplateNode
                            {
                                Name = "unit",
                                IsFolder = false,
                                MeasurementType = "line",
                                Color = "#00B8D4",
                            },
                            new TakeoffTemplateNode
                            {
                                Name = "corr",
                                IsFolder = false,
                                MeasurementType = "line",
                                Color = "#651FFF",
                            },
                            new TakeoffTemplateNode
                            {
                                Name = "custom wall",
                                IsFolder = false,
                                MeasurementType = "line",
                                Color = "#010203",
                            },
                            new TakeoffTemplateNode
                            {
                                Name = "shaft walls",
                                IsFolder = true,
                                Children =
                                [
                                    new TakeoffTemplateNode
                                    {
                                        Name = "ext",
                                        IsFolder = false,
                                        MeasurementType = "line",
                                        Color = "#FF4444",
                                    },
                                ],
                            },
                        ],
                    },
                    new TakeoffTemplateNode
                    {
                        Name = "parapets",
                        IsFolder = true,
                        Children =
                        [
                            new TakeoffTemplateNode
                            {
                                Name = "parapet",
                                IsFolder = false,
                                MeasurementType = "line",
                                Color = "#7E57C2",
                            },
                        ],
                    },
                    new TakeoffTemplateNode
                    {
                        Name = "framing",
                        IsFolder = true,
                        Children =
                        [
                            new TakeoffTemplateNode
                            {
                                Name = "Post",
                                IsFolder = false,
                                MeasurementType = "line",
                                Color = "#8BC34A",
                            },
                            new TakeoffTemplateNode
                            {
                                Name = "roof framing",
                                IsFolder = true,
                            },
                        ],
                    },
                    new TakeoffTemplateNode
                    {
                        Name = "units",
                        IsFolder = true,
                    },
                    new TakeoffTemplateNode
                    {
                        Name = "siding",
                        IsFolder = true,
                    },
                ],
            },
        };

        string globalConfigPath = Path.Combine(
            SmartContextStore.GlobalRoot,
            "presets",
            "takeoff_templates.json");
        Directory.CreateDirectory(Path.GetDirectoryName(globalConfigPath)!);
        File.WriteAllText(globalConfigPath, System.Text.Json.JsonSerializer.Serialize(new
        {
            oldConfig.Template,
        }));

        TakeoffTemplateConfig upgraded = TakeoffTemplateStore.ResolveConfig(null);

        AssertTemplateItem(upgraded, ["sqfts"], "custom sqft", "area");
        AssertTemplateItem(upgraded, ["sqfts"], "overframe x", "area");
        AssertTemplateItem(upgraded, ["eves rakes", "rakes"], "Rake", "line");
        AssertTemplateItem(upgraded, ["walls"], "corners", "point");
        AssertTemplateItem(upgraded, ["walls", "1st floor walls"], "corners", "point");
        AssertTemplateColor(upgraded, ["walls"], "ext", "#2E7D32");
        AssertTemplateItem(upgraded, ["walls"], "ext 2x6", "line");
        AssertTemplateMissingItem(upgraded, ["walls"], "unit");
        AssertTemplateMissingItem(upgraded, ["walls"], "corr");
        AssertTemplateColor(upgraded, ["walls"], "custom wall", "#010203");
        AssertTemplateItem(upgraded, ["walls", "shaft walls"], "shaft 1st", "line");
        AssertTemplateMissingItem(upgraded, ["walls", "shaft walls"], "ext");
        AssertTemplateItem(upgraded, ["parapets"], "prpt 0.0 0.0 0.0", "line");
        AssertTemplateMissingItem(upgraded, ["parapets"], "parapet");
        AssertTemplateItem(upgraded, ["framing"], "Blocking for Drywall", "line");
        AssertTemplateMissingItem(upgraded, ["framing"], "Post");
        AssertTemplateMissingFolder(upgraded, ["framing", "roof framing"]);
        AssertTemplateMissingFolder(upgraded, ["units"]);
        AssertTemplateMissingFolder(upgraded, ["siding"]);
        AssertEqual(
            TakeoffTemplateConfig.CurrentBuiltInVersion.ToString(),
            upgraded.BuiltInVersion.ToString(),
            "upgraded template built-in version");
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
            string evesRakes = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "eves rakes");
            string eves = OurPlaneCoreJobStore.CreateTakeoffFolder(job, evesRakes, "eves");

            AssertEqual(
                framing,
                TakeoffTemplateRouting.ResolveDestinationFolder(job, ["framing"]),
                "existing template folder target");
            AssertEqual(
                eves,
                TakeoffTemplateRouting.ResolveDestinationFolder(job, ["eves rakes", "eves"]),
                "existing nested template folder target");
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

    private static void AssertTemplateItem(
        TakeoffTemplateConfig config,
        IReadOnlyList<string> folderPath,
        string name,
        string measurementType)
    {
        TakeoffTemplateNode item = FindTemplateItem(config, folderPath, name);
        AssertEqual(measurementType, item.MeasurementType, $"{name} measurement type");
    }

    private static void AssertTemplateColor(
        TakeoffTemplateConfig config,
        IReadOnlyList<string> folderPath,
        string name,
        string color)
    {
        TakeoffTemplateNode item = FindTemplateItem(config, folderPath, name);
        AssertEqual(color, item.Color, $"{name} template color");
    }

    private static void AssertTemplateMissingItem(
        TakeoffTemplateConfig config,
        IReadOnlyList<string> folderPath,
        string name)
    {
        TakeoffTemplateNode folder = FindFolder(config.Template.Roots, folderPath);
        bool exists = folder.Children.Any(node =>
            !node.IsFolder &&
            string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase));
        AssertFalse(exists, $"template preset should be absent under {string.Join("/", folderPath)}: {name}");
    }

    private static void AssertTemplateMissingFolder(
        TakeoffTemplateConfig config,
        IReadOnlyList<string> folderPath)
    {
        bool exists = TryFindFolder(config.Template.Roots, folderPath, out _);
        AssertFalse(exists, $"template folder should be absent: {string.Join("/", folderPath)}");
    }

    private static void AssertDistinctTemplateColors(
        TakeoffTemplateConfig config,
        IReadOnlyList<string> folderPath,
        IReadOnlyList<string> names)
    {
        var colors = names
            .Select(name => FindTemplateItem(config, folderPath, name).Color)
            .ToList();
        AssertEqual(colors.Count.ToString(), colors.Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(),
            $"{string.Join("/", folderPath)} colors should be unique across checked wall presets");
    }

    private static TakeoffTemplateNode FindTemplateItem(
        TakeoffTemplateConfig config,
        IReadOnlyList<string> folderPath,
        string name)
    {
        TakeoffTemplateNode folder = FindFolder(config.Template.Roots, folderPath);
        return folder.Children
            .FirstOrDefault(node => !node.IsFolder &&
                                    string.Equals(node.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"template preset missing under {string.Join("/", folderPath)}: {name}");
    }

    private static TakeoffTemplateNode FindFolder(
        IEnumerable<TakeoffTemplateNode> roots,
        IReadOnlyList<string> folderPath)
    {
        if (TryFindFolder(roots, folderPath, out TakeoffTemplateNode? found) && found != null)
            return found;

        throw new InvalidOperationException($"template folder missing: {string.Join("/", folderPath)}");
    }

    private static bool TryFindFolder(
        IEnumerable<TakeoffTemplateNode> roots,
        IReadOnlyList<string> folderPath,
        out TakeoffTemplateNode? folder)
    {
        IEnumerable<TakeoffTemplateNode> current = roots;
        folder = null;
        foreach (string segment in folderPath)
        {
            folder = current.FirstOrDefault(node =>
                node.IsFolder &&
                string.Equals(node.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (folder == null)
                return false;
            current = folder.Children;
        }

        return folder != null;
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
}
