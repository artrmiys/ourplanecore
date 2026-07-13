using OurPlaneCore;

internal static class ModuleFeatureTests
{
    public static void CatalogContainsEveryModuleEnabledByDefault()
    {
        ModuleId[] ids = Enum.GetValues<ModuleId>();
        AssertEqual(ids.Length, ModuleFeatureCatalog.All.Count, "module catalog count");
        AssertEqual(ids.Length, ModuleFeatureCatalog.All.Select(definition => definition.Id).Distinct().Count(), "unique module ids");

        foreach (ModuleFeatureDefinition definition in ModuleFeatureCatalog.All)
        {
            AssertTrue(!string.IsNullOrWhiteSpace(definition.Group), $"{definition.Id} group");
            AssertTrue(!string.IsNullOrWhiteSpace(definition.Name), $"{definition.Id} name");
            AssertTrue(!string.IsNullOrWhiteSpace(definition.Description), $"{definition.Id} description");
            AssertTrue(definition.DefaultEnabled, $"{definition.Id} default should remain enabled");
        }

        AssertEqual("Sheet Manager", ModuleFeatureCatalog.Get(ModuleId.SheetManager).Name, "sheet manager catalog name");
        AssertEqual("Excel Integration", ModuleFeatureCatalog.Get(ModuleId.ExcelIntegration).Name, "excel integration catalog name");
    }

    public static void CloneAndUpgradePreserveEditsAndAddMissingModules()
    {
        var legacy = new ModuleFeatureConfig
        {
            SchemaVersion = 0,
            States = new Dictionary<string, bool>
            {
                ["ai"] = false,
                ["FutureModule"] = false,
            },
        };

        ModuleFeatureConfig upgraded = legacy.UpgradeForCurrentSchema();

        AssertEqual(ModuleFeatureConfig.CurrentSchemaVersion, upgraded.SchemaVersion, "upgraded schema");
        AssertFalse(upgraded.IsEnabled(ModuleId.Ai), "upgraded AI edit");
        AssertTrue(upgraded.IsEnabled(ModuleId.SheetManager), "missing sheet manager default");
        AssertTrue(upgraded.IsEnabled(ModuleId.ExcelIntegration), "missing excel integration default");
        AssertTrue(upgraded.States.TryGetValue("FutureModule", out bool futureEnabled) && !futureEnabled, "unknown future state preservation");
        AssertEqual(ModuleFeatureCatalog.All.Count + 1, upgraded.States.Count, "upgraded state count");
        AssertTrue(upgraded.States.Keys.Contains("Ai", StringComparer.Ordinal), "known state id normalization");

        ModuleFeatureConfig clone = upgraded.Clone();
        clone.SetEnabled(ModuleId.Ai, true);
        AssertTrue(clone.IsEnabled(ModuleId.Ai), "clone edit");
        AssertFalse(upgraded.IsEnabled(ModuleId.Ai), "clone must not mutate source");
        AssertEqual(0, legacy.SchemaVersion, "upgrade must not mutate legacy schema");
        AssertEqual(2, legacy.States.Count, "upgrade must not mutate legacy states");
    }

    public static void GlobalStoreRoundTripsAtomically()
    {
        using var scope = new IsolatedGlobalStoreScope();
        ModuleFeatureConfig config = ModuleFeatureConfig.BuildDefault();
        config.SetEnabled(ModuleId.ExcelIntegration, false);
        ModuleFeatureStore.SaveGlobal(config);

        string expectedPath = Path.Combine(scope.RootPath, "presets", "modules.json");
        AssertEqual(expectedPath, ModuleFeatureStore.GlobalConfigPath, "global module path");
        AssertTrue(File.Exists(expectedPath), "global module file");

        ModuleFeatureConfig loaded = ModuleFeatureStore.LoadGlobal()
            ?? throw new InvalidOperationException("global module config was not loaded");
        AssertFalse(loaded.IsEnabled(ModuleId.ExcelIntegration), "global Excel state round trip");
        AssertTrue(loaded.IsEnabled(ModuleId.SheetManager), "global sheet manager state round trip");

        string directory = Path.GetDirectoryName(expectedPath)!;
        AssertEqual(0, Directory.EnumerateFiles(directory, ".modules.json.*.tmp").Count(), "atomic temp cleanup");
    }

    public static void ResolveUsesWholeJobThenGlobalThenDefault()
    {
        using var scope = new IsolatedGlobalStoreScope();
        string jobRoot = Path.Combine(Path.GetTempPath(), "opc_module_tests", Guid.NewGuid().ToString("N"));
        var job = new OurPlaneCoreJob
        {
            Name = "Module Feature Test",
            RootPath = jobRoot,
        };

        try
        {
            ModuleFeatureConfig global = ModuleFeatureConfig.BuildDefault();
            global.SetEnabled(ModuleId.Ai, false);
            ModuleFeatureStore.SaveGlobal(global);

            ModuleFeatureConfig jobOverride = ModuleFeatureConfig.BuildDefault();
            jobOverride.SetEnabled(ModuleId.SheetManager, false);
            ModuleFeatureStore.SaveJobOverride(job, jobOverride);

            string expectedJobPath = Path.Combine(jobRoot, "AI_Context", "settings", "modules.json");
            AssertEqual(expectedJobPath, ModuleFeatureStore.GetJobConfigPath(job), "job module path");
            AssertTrue(File.Exists(expectedJobPath), "job module file");

            ModuleFeatureConfig resolvedJob = ModuleFeatureStore.Resolve(job);
            AssertFalse(resolvedJob.IsEnabled(ModuleId.SheetManager), "job override sheet manager state");
            AssertTrue(resolvedJob.IsEnabled(ModuleId.Ai), "job config should replace rather than merge global config");

            ModuleFeatureStore.ClearJobOverride(job);
            AssertFalse(File.Exists(expectedJobPath), "cleared job override");
            ModuleFeatureConfig resolvedGlobal = ModuleFeatureStore.Resolve(job);
            AssertFalse(resolvedGlobal.IsEnabled(ModuleId.Ai), "global AI state after job clear");
            AssertTrue(resolvedGlobal.IsEnabled(ModuleId.SheetManager), "global sheet manager state after job clear");

            File.Delete(ModuleFeatureStore.GlobalConfigPath);
            ModuleFeatureConfig resolvedDefault = ModuleFeatureStore.Resolve(null);
            AssertTrue(ModuleFeatureCatalog.All.All(definition => resolvedDefault.IsEnabled(definition.Id)), "default fallback states");
        }
        finally
        {
            TryDeleteDirectory(jobRoot);
        }
    }

    public static void MalformedConfigFallsBackSafely()
    {
        using var scope = new IsolatedGlobalStoreScope();
        Directory.CreateDirectory(Path.GetDirectoryName(ModuleFeatureStore.GlobalConfigPath)!);
        File.WriteAllText(ModuleFeatureStore.GlobalConfigPath, "{ this is not valid json");

        AssertTrue(ModuleFeatureStore.LoadGlobal() == null, "malformed global config should not load");
        ModuleFeatureConfig fallback = ModuleFeatureStore.Resolve(null);
        AssertTrue(fallback.IsEnabled(ModuleId.Ai), "malformed config AI fallback");
        AssertTrue(fallback.IsEnabled(ModuleId.ThreeD), "malformed config 3D fallback");
        AssertTrue(fallback.IsEnabled(ModuleId.SheetManager), "malformed config sheet manager fallback");
    }

    public static void RequiredSurfacesAreWiredThroughTheModuleGate()
    {
        string xaml = ReadRepoFile("MainWindow.xaml");
        string modules = ReadRepoFile("MainWindow.Modules.cs");
        string settings = ReadRepoFile("MainWindow.SettingsManager.Modules.cs");
        string pagesMenu = ReadRepoFile("MainWindow.PagesCommands.cs");
        string viewportMenu = ReadRepoFile("MainWindow.ViewportCallbacks.cs");
        string takeoffMenu = ReadRepoFile("MainWindow.TakeoffsMenus.cs");
        string pageLegend = ReadRepoFile("MainWindow.PageTakeoffLegend.cs");
        string pdfExport = ReadRepoFile("MainWindow.PdfExport.cs");
        string sheetManager = ReadRepoFile("MainWindow.WorkspaceManagers.cs");
        string pageTabs = ReadRepoFile("MainWindow.PageTabs.cs");
        string displaySettings = ReadRepoFile("MainWindow.DisplaySettings.cs");
        string displayOverlaySettings = ReadRepoFile("MainWindow.DisplaySettings.OverlayMenus.cs");
        string bookmarks = ReadRepoFile("PageBookmarksController.cs");
        string templates = ReadRepoFile("MainWindow.Templates.cs");
        string settingsManager = ReadRepoFile("MainWindow.SettingsManager.cs");
        string materials = ReadRepoFile("MainWindow.Materials.cs");
        string viewportModules = ReadRepoFile(Path.Combine("Controls", "PdfViewport.ModuleVisibility.cs"));
        string takeoffProperties = ReadRepoFile("MainWindow.TakeoffsProperties.cs");

        string[] namedWorkspaces =
        [
            "SheetManagerWorkspaceTab",
            "TakeoffManagerWorkspaceTab",
            "ReportBuilderWorkspaceTab",
            "MaterialsWorkspaceTab",
            "AiManagerWorkspaceTab",
            "ThreeDManagerWorkspaceTab",
        ];
        foreach (string workspace in namedWorkspaces)
        {
            AssertTrue(xaml.Contains($"x:Name=\"{workspace}\"", StringComparison.Ordinal), $"named workspace {workspace}");
            AssertTrue(modules.Contains($"SetVisible({workspace}", StringComparison.Ordinal), $"workspace gate {workspace}");
        }

        AssertTrue(settings.Contains("ModuleFeatureStore.SaveGlobal(_moduleDraft)", StringComparison.Ordinal), "global module save");
        AssertTrue(settings.Contains("ModuleFeatureStore.SaveJobOverride(_currentJob, _moduleDraft)", StringComparison.Ordinal), "job module save");
        AssertTrue(settings.Contains("ModuleFeatureStore.ClearJobOverride(_currentJob)", StringComparison.Ordinal), "job module override clear");
        AssertTrue(pagesMenu.Contains("ApplyModuleAvailabilityToMenu(menu)", StringComparison.Ordinal), "Pages context-menu gate");
        AssertTrue(viewportMenu.Contains("IsModuleEnabled(ModuleId.Ai)", StringComparison.Ordinal), "viewport AI menu gate");
        AssertTrue(viewportMenu.Contains("IsModuleEnabled(ModuleId.ThreeD)", StringComparison.Ordinal), "viewport 3D menu gate");
        AssertTrue(takeoffMenu.Contains("IsModuleEnabled(ModuleId.AdvancedTakeoffTools)", StringComparison.Ordinal), "takeoff advanced-menu gate");
        AssertTrue(pageLegend.Contains("IsModuleEnabled(ModuleId.SheetOverlay)", StringComparison.Ordinal), "page overlay tree gate");
        AssertTrue(pdfExport.Contains("RequireModule(ModuleId.PdfOutput", StringComparison.Ordinal), "PDF export execution gate");
        AssertTrue(sheetManager.Contains("CancelActiveSheetManagerWorkForModuleDisable", StringComparison.Ordinal), "Sheet Manager active-work cancellation");
        AssertTrue(pageTabs.Contains("RequireModule(ModuleId.DetachedSheets", StringComparison.Ordinal), "detached sheet execution gate");
        AssertTrue(pageTabs.Contains("CloseDetachedSheetsForModuleDisable", StringComparison.Ordinal), "detached window close on module disable");
        AssertTrue(displaySettings.Contains("IsModuleEnabled(ModuleId.PdfLayers) && _settings.PdfLayersEnabled", StringComparison.Ordinal), "display settings PDF-layer gate");
        AssertTrue(displayOverlaySettings.Contains("IsModuleEnabled(ModuleId.PdfLayers) && _settings.PdfLayersEnabled", StringComparison.Ordinal), "overlay settings PDF-layer gate");
        AssertTrue(bookmarks.Contains("_pageBookmarks.Clear();", StringComparison.Ordinal) && bookmarks.Contains("LoadForJob();", StringComparison.Ordinal), "bookmark job isolation and restore");
        AssertTrue(templates.Contains("_templatesModuleEnabled == enabled", StringComparison.Ordinal), "template dock transition guard");
        AssertTrue(settings.Contains("_moduleDraft = _moduleFeatures.Clone()", StringComparison.Ordinal), "live module draft binding");
        AssertTrue(settingsManager.Contains("RequireModule(ModuleId.TakeoffAutomation", StringComparison.Ordinal), "settings automation execution gate");
        AssertTrue(materials.Contains("CancelActiveMaterialsWorkForModuleDisable", StringComparison.Ordinal), "materials active-work cancellation");
        AssertTrue(viewportModules.Contains("SetAnnotationsModuleEnabled", StringComparison.Ordinal), "annotation viewport visibility gate");
        AssertTrue(takeoffProperties.Contains("RequireModule(ModuleId.Estimating", StringComparison.Ordinal), "estimating price execution gate");

        string allModuleSurfaces = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(RepoRoot(), "MainWindow*.cs")
                .Select(File.ReadAllText)
                .Append(ReadRepoFile("PageBookmarksController.cs")));
        foreach (ModuleId id in Enum.GetValues<ModuleId>())
            AssertTrue(allModuleSurfaces.Contains($"ModuleId.{id}", StringComparison.Ordinal), $"module gate reference for {id}");
    }

    private sealed class IsolatedGlobalStoreScope : IDisposable
    {
        private readonly string? _previousRoot;

        public IsolatedGlobalStoreScope()
        {
            _previousRoot = Environment.GetEnvironmentVariable(
                ModuleFeatureStore.GlobalRootOverrideEnvironmentVariable);
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "opc_module_tests",
                Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(
                ModuleFeatureStore.GlobalRootOverrideEnvironmentVariable,
                RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(
                ModuleFeatureStore.GlobalRootOverrideEnvironmentVariable,
                _previousRoot);
            TryDeleteDirectory(RootPath);
        }
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    private static string RepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ourplanecore.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find ourplanecore repo root.");
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
            // Best-effort temp cleanup only.
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }
}
