using OurPlanCore;

internal static class SheetMetadataConfigHarness
{
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
                saved.ImportPolicy != SheetMetadataImportPolicy.Preview ||
                !saved.HasSameBehaviorAs(target))
            {
                Console.Error.WriteLine("Saved global sheet metadata config failed verification.");
                return 1;
            }

            Console.WriteLine($"Installed global {displayName} / Preview config: {path}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to install global {displayName} config: {ex.Message}");
            return 1;
        }
    }
}
