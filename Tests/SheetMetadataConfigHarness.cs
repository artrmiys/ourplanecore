using OurPlanCore;

internal static class SheetMetadataConfigHarness
{
    public static int DisableAutomaticImportAnalysis()
    {
        string path = Path.Combine(
            SmartContextStore.GlobalRoot,
            "presets",
            "sheet_metadata.json");
        try
        {
            SheetMetadataConfig target =
                SettingsPresetStore.LoadGlobalSheetMetadata() ?? SheetMetadataConfig.BuildDefault();
            if (target.ImportPolicy == SheetMetadataImportPolicy.ManualOnly)
            {
                Console.WriteLine($"Automatic import analysis is already disabled: {path}");
                return 0;
            }

            if (File.Exists(path))
            {
                string backup = path + ".pre-manual-import-" +
                                DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
                if (File.Exists(backup))
                    backup += "-" + Guid.NewGuid().ToString("N")[..8];
                File.Copy(path, backup, overwrite: false);
                Console.WriteLine($"Existing global config preserved: {backup}");
            }

            target.ImportPolicy = SheetMetadataImportPolicy.ManualOnly;
            SettingsPresetStore.SaveGlobalSheetMetadata(target);
            SheetMetadataConfig? saved = SettingsPresetStore.LoadGlobalSheetMetadata();
            if (saved == null ||
                saved.ImportPolicy != SheetMetadataImportPolicy.ManualOnly ||
                !saved.HasSameBehaviorAs(target))
            {
                Console.Error.WriteLine("Saved ManualOnly sheet metadata config failed verification.");
                return 1;
            }

            Console.WriteLine($"Automatic PDF-import metadata analysis disabled: {path}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to disable automatic import analysis: {ex.Message}");
            return 1;
        }
    }

    public static int InstallGlobalIdeal() => InstallGlobal(
        SheetMetadataConfig.BuildIdealV3(),
        SheetMetadataDetectorMode.IdealV3,
        "ideal-v3",
        "Ideal v3");

    public static int InstallGlobalPrecise()
        => InstallGlobal(
            SheetMetadataConfig.BuildPreciseV2(),
            SheetMetadataDetectorMode.PreciseV2,
            "precise-v2",
            "Precise v2");

    private static int InstallGlobal(
        SheetMetadataConfig target,
        SheetMetadataDetectorMode detectorMode,
        string backupLabel,
        string displayName)
    {
        string path = Path.Combine(
            SmartContextStore.GlobalRoot,
            "presets",
            "sheet_metadata.json");
        try
        {
            if (File.Exists(path))
            {
                SheetMetadataConfig? current = SettingsPresetStore.LoadGlobalSheetMetadata();
                if (current != null && current.HasSameBehaviorAs(target))
                {
                    Console.WriteLine($"{displayName} global config already active: {path}");
                    return 0;
                }

                string backup = path + $".pre-{backupLabel}-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
                File.Copy(path, backup, overwrite: false);
                Console.WriteLine($"Existing global config preserved: {backup}");
            }

            SettingsPresetStore.SaveGlobalSheetMetadata(target);
            SheetMetadataConfig? saved = SettingsPresetStore.LoadGlobalSheetMetadata();
            if (saved == null ||
                saved.DetectorMode != detectorMode ||
                saved.ImportPolicy != target.ImportPolicy ||
                !saved.HasSameBehaviorAs(target))
            {
                Console.Error.WriteLine("Saved global sheet metadata config failed verification.");
                return 1;
            }

            Console.WriteLine($"Installed global {displayName} / {target.ImportPolicy} config: {path}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to install global {displayName} config: {ex.Message}");
            return 1;
        }
    }
}
