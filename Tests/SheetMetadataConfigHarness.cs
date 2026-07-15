using OurPlanCore;

internal static class SheetMetadataConfigHarness
{
    public static int InstallGlobalPrecise()
    {
        string path = Path.Combine(
            SmartContextStore.GlobalRoot,
            "presets",
            "sheet_metadata.json");
        try
        {
            SheetMetadataConfig precise = SheetMetadataConfig.BuildPreciseV2();
            if (File.Exists(path))
            {
                SheetMetadataConfig? current = SettingsPresetStore.LoadGlobalSheetMetadata();
                if (current != null && current.HasSameBehaviorAs(precise))
                {
                    Console.WriteLine($"Precise v2 global config already active: {path}");
                    return 0;
                }

                string backup = path + ".pre-precise-v2-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
                File.Copy(path, backup, overwrite: false);
                Console.WriteLine($"Existing global config preserved: {backup}");
            }

            SettingsPresetStore.SaveGlobalSheetMetadata(precise);
            SheetMetadataConfig? saved = SettingsPresetStore.LoadGlobalSheetMetadata();
            if (saved == null ||
                saved.DetectorMode != SheetMetadataDetectorMode.PreciseV2 ||
                saved.ImportPolicy != SheetMetadataImportPolicy.Preview ||
                !saved.HasSameBehaviorAs(precise))
            {
                Console.Error.WriteLine("Saved global sheet metadata config failed verification.");
                return 1;
            }

            Console.WriteLine($"Installed global Precise v2 / Preview config: {path}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to install global Precise v2 config: {ex.Message}");
            return 1;
        }
    }
}
