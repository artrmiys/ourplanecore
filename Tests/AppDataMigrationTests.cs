using OurPlanCore;
using System.Reflection;
using System.Text.Json.Nodes;

internal static class AppDataMigrationTests
{
    public static void MigratesDurableDataWithoutOverwritingOrCopyingCaches()
    {
        string root = Path.Combine(Path.GetTempPath(), "onc_identity_migration_tests", Guid.NewGuid().ToString("N"));
        string roamingBase = Path.Combine(root, "roaming");
        string localBase = Path.Combine(root, "local");
        string sourceRoaming = Path.Combine(roamingBase, LegacyProductName());
        string destinationRoaming = Path.Combine(roamingBase, AppIdentity.ProductName);
        string sourceLocal = Path.Combine(localBase, LegacyProductName());
        string destinationLocal = Path.Combine(localBase, AppIdentity.ProductName);

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceRoaming, "thumbnails"));
            Directory.CreateDirectory(Path.Combine(sourceRoaming, "logs"));
            Directory.CreateDirectory(Path.Combine(sourceLocal, "learning"));
            Directory.CreateDirectory(Path.Combine(sourceLocal, "presets"));
            Directory.CreateDirectory(Path.Combine(sourceLocal, "render-cache", "detail-tiles"));
            Directory.CreateDirectory(destinationRoaming);

            string legacyThumbnail = Path.Combine(sourceRoaming, "thumbnails", "job.png");
            File.WriteAllText(legacyThumbnail, "thumbnail");
            File.WriteAllText(Path.Combine(sourceRoaming, "logs", "old.log"), "log");
            File.WriteAllText(Path.Combine(sourceRoaming, "templates.json"), "legacy templates");
            File.WriteAllText(Path.Combine(destinationRoaming, "templates.json"), "current templates");
            File.WriteAllText(
                Path.Combine(sourceRoaming, "settings.json"),
                $$"""
                {
                  "RecentJobs": [
                    {
                      "Name": "Test",
                      "Path": "C:\\Jobs\\Test",
                      "ThumbnailPath": {{JsonString(legacyThumbnail)}}
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(sourceLocal, "global_ai_index.jsonl"), "index");
            File.WriteAllText(Path.Combine(sourceLocal, "learning", "learned_rules.json"), "rules");
            File.WriteAllText(Path.Combine(sourceLocal, "presets", "modules.json"), "modules");
            File.WriteAllText(Path.Combine(sourceLocal, "render-cache", "detail-tiles", "tile.bin"), "cache");

            RunCriticalMigration(roamingBase, localBase);

            AssertTrue(File.Exists(Path.Combine(destinationRoaming, "settings.json")), "critical settings copied");
            AssertEqual("current templates", File.ReadAllText(Path.Combine(destinationRoaming, "templates.json")), "destination must win");
            AssertEqual("modules", File.ReadAllText(Path.Combine(destinationLocal, "presets", "modules.json")), "critical presets copied");
            AssertFalse(Directory.Exists(Path.Combine(destinationRoaming, "thumbnails")), "thumbnails must be deferred");
            AssertFalse(Directory.Exists(Path.Combine(destinationLocal, "learning")), "learning must be deferred");
            AssertFalse(File.Exists(Path.Combine(destinationLocal, "global_ai_index.jsonl")), "AI index must be deferred");
            AssertFalse(File.Exists(Path.Combine(destinationLocal, ".app-identity-migration-v1")), "marker waits for deferred migration");

            RunMigration(roamingBase, localBase);

            AssertEqual("thumbnail", File.ReadAllText(Path.Combine(destinationRoaming, "thumbnails", "job.png")), "thumbnail copied");
            AssertEqual("index", File.ReadAllText(Path.Combine(destinationLocal, "global_ai_index.jsonl")), "index copied");
            AssertEqual("rules", File.ReadAllText(Path.Combine(destinationLocal, "learning", "learned_rules.json")), "learning copied");
            AssertEqual("modules", File.ReadAllText(Path.Combine(destinationLocal, "presets", "modules.json")), "presets copied");
            AssertFalse(Directory.Exists(Path.Combine(destinationLocal, "render-cache")), "render cache must not be copied");
            AssertFalse(Directory.Exists(Path.Combine(destinationRoaming, "logs")), "logs must not be copied");
            AssertTrue(File.Exists(Path.Combine(sourceLocal, "render-cache", "detail-tiles", "tile.bin")), "source must not be deleted");
            AssertTrue(Directory.EnumerateFiles(destinationLocal, ".app-identity-migration-v1").Any(), "marker written");

            JsonNode settings = JsonNode.Parse(File.ReadAllText(Path.Combine(destinationRoaming, "settings.json")))
                ?? throw new InvalidOperationException("migrated settings missing");
            string migratedThumbnail = settings["RecentJobs"]?[0]?["ThumbnailPath"]?.GetValue<string>() ?? "";
            AssertEqual(Path.Combine(destinationRoaming, "thumbnails", "job.png"), migratedThumbnail, "thumbnail prefix normalized");
        }
        finally
        {
            TryDelete(root);
        }
    }

    public static void ProtectsLegacySettingsUntilCriticalMigrationCompletes()
    {
        string root = Path.Combine(Path.GetTempPath(), "onc_identity_settings_guard_tests", Guid.NewGuid().ToString("N"));
        string current = Path.Combine(root, "current", "settings.json");
        string legacy = Path.Combine(root, "legacy", "settings.json");
        string marker = Path.Combine(root, "local", ".app-identity-migration-v1");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
            File.WriteAllText(legacy, "legacy");

            AssertTrue(
                InvokeSettingsProtectionRequired(current, legacy, marker),
                "missing current settings must not overwrite an unmigrated legacy file");

            Directory.CreateDirectory(Path.GetDirectoryName(current)!);
            File.WriteAllText(current, "current");
            AssertFalse(
                InvokeSettingsProtectionRequired(current, legacy, marker),
                "existing current settings are safe to save");

            File.Delete(current);
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, "complete");
            AssertFalse(
                InvokeSettingsProtectionRequired(current, legacy, marker),
                "completed migration allows a fresh current settings file");
        }
        finally
        {
            TryDelete(root);
        }
    }

    public static void EnvironmentVariableUsesCurrentThenLegacyFallback()
    {
        string suffix = "IDENTITY_TEST_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        string currentName = AppIdentity.EnvironmentVariableName(suffix);
        string legacyName = LegacyEnvironmentVariableName(suffix);
        try
        {
            Environment.SetEnvironmentVariable(legacyName, "legacy", EnvironmentVariableTarget.Process);
            AssertEqual("legacy", AppIdentity.GetEnvironmentVariable(suffix) ?? "", "legacy fallback");

            Environment.SetEnvironmentVariable(currentName, "current", EnvironmentVariableTarget.Process);
            AssertEqual("current", AppIdentity.GetEnvironmentVariable(suffix) ?? "", "current value wins");
            AssertEqual(
                "current",
                AppIdentity.GetEnvironmentVariable(suffix, EnvironmentVariableTarget.Process) ?? "",
                "target overload current value wins");
            AssertEqual(
                "current",
                AppIdentity.GetEnvironmentVariable(currentName) ?? "",
                "full current variable name is accepted");
        }
        finally
        {
            Environment.SetEnvironmentVariable(currentName, null, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(legacyName, null, EnvironmentVariableTarget.Process);
        }
    }

    private static void RunMigration(string roamingBase, string localBase)
    {
        MethodInfo method = typeof(AppDataMigration).GetMethod(
            "RunForRoots",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("migration test entrypoint missing");
        method.Invoke(null, [roamingBase, localBase]);
    }

    private static void RunCriticalMigration(string roamingBase, string localBase)
    {
        MethodInfo method = typeof(AppDataMigration).GetMethod(
            "RunCriticalForRoots",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("critical migration test entrypoint missing");
        method.Invoke(null, [roamingBase, localBase]);
    }

    private static bool InvokeSettingsProtectionRequired(string current, string legacy, string marker)
    {
        MethodInfo method = typeof(AppDataMigration).GetMethod(
            "IsLegacySettingsSaveProtectionRequired",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("settings protection test entrypoint missing");
        return method.Invoke(null, [current, legacy, marker]) as bool?
            ?? throw new InvalidOperationException("settings protection result missing");
    }

    private static string LegacyProductName() =>
        typeof(AppIdentity).GetField("LegacyProductName", BindingFlags.Static | BindingFlags.NonPublic)?
            .GetRawConstantValue()?.ToString()
        ?? throw new InvalidOperationException("legacy product identity missing");

    private static string LegacyEnvironmentVariableName(string suffix) =>
        typeof(AppIdentity).GetMethod(
            "LegacyEnvironmentVariableName",
            BindingFlags.Static | BindingFlags.NonPublic)?
            .Invoke(null, [suffix])?.ToString()
        ?? throw new InvalidOperationException("legacy environment identity missing");

    private static string JsonString(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value);

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
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
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }
}
