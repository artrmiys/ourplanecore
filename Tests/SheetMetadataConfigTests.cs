using System.Text.Json;
using OurPlanCore;

internal static class SheetMetadataConfigTests
{
    public static void DefaultsDisableAutomaticImportAnalysis()
    {
        SheetMetadataConfig builtIn = SheetMetadataConfig.BuildDefault();
        SheetMetadataConfig legacy = SheetMetadataConfig.BuildLegacy();
        SheetMetadataConfig precise = SheetMetadataConfig.BuildPreciseV2();
        SheetMetadataConfig ideal = SheetMetadataConfig.BuildIdealV3();

        AssertEqual(SheetMetadataDetectorMode.IdealV3, builtIn.DetectorMode, "built-in default must use guided Ideal v3");
        AssertEqual(SheetMetadataImportPolicy.ManualOnly, builtIn.ImportPolicy, "built-in default must skip import analysis");
        AssertEqual(SheetMetadataImportPolicy.LegacyAutoApply, legacy.ImportPolicy, "explicit Legacy preset must keep migration behavior");
        AssertEqual(SheetMetadataImportPolicy.Preview, precise.ImportPolicy, "Precise preset must keep its reviewed workflow");
        AssertEqual(SheetMetadataDetectorMode.IdealV3, ideal.DetectorMode, "Ideal preset must select the v3 detector");
        AssertEqual(SheetMetadataImportPolicy.ManualOnly, ideal.ImportPolicy, "Ideal preset must skip import analysis by default");
        AssertTrue(ideal.EnableSheetIndexEvidence, "Ideal preset must use sheet-index evidence");
        AssertTrue(ideal.PreserveExistingManualName, "Ideal preset must preserve reviewed names");
        AssertEqual(SheetMetadataConfig.IdealV3PresetName, ideal.PresetName, "Ideal preset name");
    }

    public static void ManualPolicySkipsImportButReviewsExplicitAnalysis()
    {
        string materials = File.ReadAllText(RepoFile("MainWindow.Materials.cs"));
        int importStart = materials.IndexOf(
            "private async Task<MaterialReportSheetMetadataSummary> TryApplySheetMetadataAfterPdfImportAsync(",
            StringComparison.Ordinal);
        AssertTrue(importStart >= 0, "metadata import flow must exist");
        int explicitStart = materials.IndexOf(
            "private async Task<MaterialReportSheetMetadataSummary> AnalyzeAndApplySheetMetadataAsync(",
            importStart,
            StringComparison.Ordinal);
        AssertTrue(explicitStart > importStart, "explicit metadata analysis flow must follow PDF import flow");
        string importFlow = materials[importStart..explicitStart];

        int manualGuard = importFlow.IndexOf(
            "if (policy == SheetMetadataImportPolicy.ManualOnly)",
            StringComparison.Ordinal);
        int analyzerCall = importFlow.IndexOf(
            "await AnalyzeAndApplySheetMetadataAsync(",
            StringComparison.Ordinal);
        AssertTrue(manualGuard >= 0 && analyzerCall > manualGuard, "ManualOnly must return before automatic PDF analysis starts");
        AssertTrue(importFlow.Contains("PDF import sheet metadata skipped by ManualOnly policy", StringComparison.Ordinal),
            "manual-only import skip must be visible in the runtime log");

        int explicitEnd = materials.IndexOf("private void RefreshMaterialsManager()", explicitStart, StringComparison.Ordinal);
        AssertTrue(explicitEnd > explicitStart, "explicit metadata analysis flow must have a stable boundary");
        string explicitFlow = materials[explicitStart..explicitEnd];
        AssertTrue(
            explicitFlow.Contains("SheetMetadataImportPolicy.ManualOnly;", StringComparison.Ordinal) &&
            explicitFlow.Contains("if (reviewedByUser)", StringComparison.Ordinal),
            "an explicit Analyze action under ManualOnly must open the review flow before applying results");

        string manager = File.ReadAllText(RepoFile("MainWindow.WorkspaceManagers.cs"));
        int managerOffer = manager.IndexOf("await OfferPdfMetadataGuidanceIfNeededAsync(", StringComparison.Ordinal);
        int managerAnalyze = manager.IndexOf("() => AnalyzePdfPages(job, pages", managerOffer, StringComparison.Ordinal);
        AssertTrue(
            managerOffer >= 0 && managerAnalyze > managerOffer,
            "Sheet Manager Name / Scale actions must run the same bounded A/S guidance before full analysis");

        string dialog = File.ReadAllText(RepoFile("Dialogs/PdfMetadataCropTemplateDialog.cs"));
        AssertTrue(
            dialog.Contains("This sheet is:", StringComparison.Ordinal) &&
            dialog.Contains("SelectedProfile = profile", StringComparison.Ordinal),
            "an unresolved mixed set must let the user explicitly classify the representative sheet as A/S/Other");
    }

    public static void BatchWorkerFailureDoesNotReplaySlowFileCommand()
    {
        string worker = File.ReadAllText(RepoFile("Models/PdfLayerRenderService.Worker.cs"));
        int helperStart = worker.IndexOf("internal static bool TryInvokeHelper<TRequest, TResponse>(", StringComparison.Ordinal);
        AssertTrue(helperStart >= 0, "shared PDF helper invocation flow must exist");
        int helperEnd = worker.IndexOf("private static bool TryInvokeWorker<TRequest, TResponse>(", helperStart, StringComparison.Ordinal);
        AssertTrue(helperEnd > helperStart, "shared PDF helper invocation flow must have a stable boundary");
        string helper = worker[helperStart..helperEnd];

        int batchGuard = helper.IndexOf("string.Equals(action, \"sheetmeta_batch\"", StringComparison.Ordinal);
        int stop = helper.IndexOf("return false;", batchGuard, StringComparison.Ordinal);
        int fileFallback = helper.IndexOf("TryRunFileCommand(action", StringComparison.Ordinal);
        AssertTrue(batchGuard >= 0 && stop > batchGuard && fileFallback > stop,
            "sheetmeta_batch worker failure must stop before the slow file-command fallback");
        AssertTrue(helper.Contains("slow file-command replay disabled", StringComparison.Ordinal),
            "disabled batch replay must leave an actionable runtime log message");
    }

    public static void LegacyPreviewDoesNotSwitchDetector()
    {
        SheetMetadataConfig config = SheetMetadataConfig.BuildLegacy();
        config.ImportPolicy = SheetMetadataImportPolicy.Preview;
        config.PresetName = "Custom Legacy";

        SheetMetadataConfig upgraded = SheetMetadataConfig.UpgradeForCurrentSchema(config);

        AssertEqual(SheetMetadataDetectorMode.Legacy, upgraded.DetectorMode, "workflow policy must not select the detector");
        AssertEqual(SheetMetadataImportPolicy.Preview, upgraded.ImportPolicy, "preview policy must survive upgrade");
    }

    public static void SchemaOnePreciseMigrationRestoresCollections()
    {
        var old = new SheetMetadataConfig
        {
            SchemaVersion = 1,
            PresetName = SheetMetadataConfig.PreciseV2PresetName,
            EnableTitleBlockEvidence = true,
            ScaleCapableSuffixes = [],
            NoScaleSuffixes = [],
            CompoundSuffixes = [],
            SuffixRules = [],
        };

        SheetMetadataConfig upgraded = SheetMetadataConfig.UpgradeForCurrentSchema(old);

        AssertEqual(SheetMetadataConfig.CurrentSchemaVersion, upgraded.SchemaVersion, "schema must migrate");
        AssertEqual(SheetMetadataDetectorMode.PreciseV2, upgraded.DetectorMode, "old precise preset must migrate to precise engine");
        AssertTrue(upgraded.SuffixRules.Count > 20, "missing v1 suffix catalog must be restored");
        AssertTrue(upgraded.NoScaleTerminalTokens.Contains("d"), "missing terminal policy must be restored");
        AssertTrue(upgraded.EnableTitleBlockLabelEvidence, "old title-block toggle must migrate to label evidence");
        AssertTrue(upgraded.EnableTitleBlockScaleEvidence, "old title-block toggle must migrate to scale evidence");
    }

    public static void SchemaTwoEmptyRulesRemainAuthoritative()
    {
        SheetMetadataConfig config = SheetMetadataConfig.BuildPreciseV2();
        config.SuffixRules = [];
        config.NoScaleTerminalTokens = [];

        SheetMetadataConfig upgraded = SheetMetadataConfig.UpgradeForCurrentSchema(config);

        AssertEqual(0, upgraded.SuffixRules.Count, "explicit empty v2 rules must stay empty");
        AssertEqual(0, upgraded.NoScaleTerminalTokens.Count, "explicit empty terminal policy must stay empty");
    }

    public static void NullRuleRowsDoNotCrashUpgradeOrResolve()
    {
        SheetMetadataConfig config = SheetMetadataConfig.BuildPreciseV2();
        config.SuffixRules = [null!];
        config.SheetLabelOverrides = [null!];
        SheetMetadataConfig upgraded = SheetMetadataConfig.UpgradeForCurrentSchema(config);
        AssertEqual(0, upgraded.SuffixRules.Count, "null rule rows must be filtered");
        AssertEqual(0, upgraded.SheetLabelOverrides.Count, "null override rows must be filtered");

        config.SuffixRules = [new SheetSuffixRule { Id = null!, Pattern = null!, OutputSuffix = null! }];
        config.SheetLabelOverrides = [new SheetMetadataLabelOverride
        {
            SourcePdfPattern = null!,
            SheetLabel = null!,
            OutputPageName = null!,
            OutputSuffix = null!,
            ScaleText = null!,
        }];
        upgraded = SheetMetadataConfig.UpgradeForCurrentSchema(config);
        AssertEqual("", upgraded.SuffixRules[0].Id, "null rule strings must normalize");
        AssertEqual("", upgraded.SheetLabelOverrides[0].SourcePdfPattern, "null override pattern must normalize");
        AssertEqual("", upgraded.SheetLabelOverrides[0].SheetLabel, "null override label must normalize");

        WithTempJob("sheet-config-null-rows", job =>
        {
            string path = Path.Combine(job.RootPath, "AI_Context", "settings", "sheet_metadata.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                "{\"SchemaVersion\":2,\"PresetName\":\"Precise v2\",\"DetectorMode\":\"PreciseV2\",\"SuffixRules\":[null],\"SheetLabelOverrides\":[null]}");

            SheetMetadataConfig resolved = SettingsPresetStore.ResolveSheetMetadata(job);
            AssertEqual(SheetMetadataDetectorMode.PreciseV2, resolved.DetectorMode, "valid config with null rows must still resolve");
            AssertEqual(0, resolved.SuffixRules.Count, "resolved null rules must be filtered");
        });
    }

    public static void ResolveUsesJobThenGlobalThenDefault()
    {
        string globalPath = Path.Combine(SmartContextStore.GlobalRoot, "presets", "sheet_metadata.json");
        try
        {
            SheetMetadataConfig global = SheetMetadataConfig.BuildLegacy();
            global.ImportPolicy = SheetMetadataImportPolicy.Preview;
            SettingsPresetStore.SaveGlobalSheetMetadata(global);

            WithTempJob("sheet-config-precedence", job =>
            {
                SheetMetadataConfig jobOverride = SheetMetadataConfig.BuildPreciseV2();
                SettingsPresetStore.SaveJobSheetMetadataOverride(job, jobOverride);
                AssertEqual(
                    SheetMetadataDetectorMode.PreciseV2,
                    SettingsPresetStore.ResolveSheetMetadata(job).DetectorMode,
                    "job override must win");
                AssertEqual(
                    SheetMetadataDetectorMode.Legacy,
                    SettingsPresetStore.ResolveSheetMetadata(null).DetectorMode,
                    "global config must win without a job");
                AssertTrue(SettingsPresetStore.ClearJobSheetMetadataOverride(job), "job override must clear cleanly");
                AssertEqual(
                    SheetMetadataImportPolicy.Preview,
                    SettingsPresetStore.ResolveSheetMetadata(job).ImportPolicy,
                    "global config must become active after clear");
            });
        }
        finally
        {
            if (File.Exists(globalPath))
                File.Delete(globalPath);
        }

        AssertEqual(
            SheetMetadataDetectorMode.IdealV3,
            SettingsPresetStore.ResolveSheetMetadata(null).DetectorMode,
            "built-in default must use guided Ideal v3");
    }

    public static void TerminalPolicyAndCloneAreEditableAndDeep()
    {
        SheetMetadataConfig original = SheetMetadataConfig.BuildPreciseV2();
        AssertTrue(original.ShouldSkipScaleSuffix("custom d"), "default terminal d must skip scale");

        SheetMetadataConfig clone = original.Clone();
        clone.NoScaleTerminalTokens.Remove("d");
        clone.SuffixRules[0].Keywords.Add("clone-only");

        AssertFalse(clone.ShouldSkipScaleSuffix("custom d"), "removing d in Settings must change behavior");
        AssertTrue(original.ShouldSkipScaleSuffix("custom d"), "clone edit must not affect active config");
        AssertFalse(original.SuffixRules[0].Keywords.Contains("clone-only"), "rule lists must deep-clone");
    }

    public static void PreciseCatalogKeepsProvenStructuralCases()
    {
        List<SheetSuffixRule> rules = SheetMetadataConfig.BuildPreciseV2().SuffixRules;
        SheetSuffixRule s510 = RequiredRule(rules, "struct-510-512-wood-detail");
        AssertEqual("wd d", s510.OutputSuffix, "S510-S512 details must remain wood details");
        AssertTrue(s510.SkipScale, "wood details must skip scale");
        AssertTrue(s510.Priority < RequiredRule(rules, "struct-detail").Priority, "specific structural detail must beat generic detail");
        AssertTrue(RequiredRule(rules, "struct-500-foundation-detail").Priority < RequiredRule(rules, "struct-wood-detail").Priority, "S500 foundation detail must beat body wood references");

        AssertEqual(SheetMetadataEvidenceField.SheetTitle, RequiredRule(rules, "label-code-note").ExclusionEvidenceField, "CD PLAN exclusion must read title evidence");
        AssertEqual(SheetMetadataEvidenceField.SheetTitle, RequiredRule(rules, "struct-700-sections").ExclusionEvidenceField, "S700 SECTION exclusion must read title evidence");
        AssertEqual(SheetMetadataMatchKind.SheetLabelFloor, RequiredRule(rules, "arch-label-floor").MatchKind, "A1.01-A1.08 floors must be typed rules");
        AssertEqual("shw", RequiredRule(rules, "struct-s902-shear").OutputSuffix, "S902 shear must remain typed");
        AssertTrue(RequiredRule(rules, "struct-s902-shear").Priority < RequiredRule(rules, "struct-detail").Priority, "S902 must beat generic structural detail");
        AssertEqual("d", RequiredRule(rules, "arch-900-detail").OutputSuffix, "A900 detail fallback must remain typed");
        AssertEqual("", RequiredRule(rules, "presentation-intentional-blank").OutputSuffix, "renderings must be an editable intentional blank");
        AssertTrue(RequiredRule(rules, "presentation-intentional-blank").SkipScale, "presentation sheets must skip scale");
        AssertTrue(RequiredRule(rules, "presentation-intentional-blank").Priority < RequiredRule(rules, "wall-section").Priority, "intentional presentation blank must beat compound fallback rules");
        AssertTrue(RequiredRule(rules, "accessible-unit-schedule").Priority < RequiredRule(rules, "unit-summary-schedule").Priority, "accessible unit schedule must keep its specific sc rule");
        AssertTrue(RequiredRule(rules, "finish-specifications-schedule").Priority < RequiredRule(rules, "arch-finish").Priority, "finish specification schedule must stay sc instead of generic finish f");
        AssertEqual("d", RequiredRule(rules, "struct-s5-s7-detail-sections").OutputSuffix, "Metro S5.1-S7.1 must be typed no-scale details");
        AssertTrue(RequiredRule(rules, "struct-s5-s7-detail-sections").Priority < RequiredRule(rules, "struct-700-sections").Priority, "Metro S7.1 must beat the structural 700 range");
    }

    public static void OverrideActionsDefaultToKeepAndFingerprintChanges()
    {
        var item = new SheetMetadataLabelOverride { SheetLabel = "A101", OutputPageName = "a101 1st" };
        AssertEqual(SheetMetadataOverrideAction.Keep, item.SuffixAction, "name-only override must preserve detected suffix");
        AssertEqual(SheetMetadataOverrideAction.Keep, item.ScaleAction, "name-only override must preserve detected scale");

        SheetMetadataConfig config = SheetMetadataConfig.BuildPreciseV2();
        string original = PdfSheetMetadataPolicy.ConfigFingerprint(config);
        AssertEqual(original, PdfSheetMetadataPolicy.ConfigFingerprint(config.Clone()), "equivalent config fingerprint must be stable");
        config.EnableBodyEvidence = !config.EnableBodyEvidence;
        AssertFalse(
            string.Equals(original, PdfSheetMetadataPolicy.ConfigFingerprint(config), StringComparison.Ordinal),
            "behavior change must alter config fingerprint");

        string json = JsonSerializer.Serialize(item);
        AssertTrue(json.Contains("\"SuffixAction\":\"Keep\"", StringComparison.Ordinal), "suffix action must persist");
        AssertTrue(json.Contains("\"ScaleAction\":\"Keep\"", StringComparison.Ordinal), "scale action must persist");
    }

    private static SheetSuffixRule RequiredRule(IEnumerable<SheetSuffixRule> rules, string id) =>
        rules.Single(rule => string.Equals(rule.Id, id, StringComparison.Ordinal));

    private static string RepoFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate) && File.Exists(Path.Combine(current.FullName, "ourplancore.csproj")))
                return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException($"Repository file not found: {relativePath}");
    }

    private static void WithTempJob(string name, Action<OurPlanCoreJob> action)
    {
        string parent = Path.Combine(Path.GetTempPath(), $"ourplancore-config-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(parent);
        try
        {
            action(OurPlanCoreJobStore.CreateJob(parent, name));
        }
        finally
        {
            try
            {
                Directory.Delete(parent, recursive: true);
            }
            catch
            {
                // Cleanup must not hide the assertion that failed.
            }
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);
}
