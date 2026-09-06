using System.IO;
using System.Text.Json;

namespace OurPlanCore;

internal static class KeyboardShortcutStore
{
    public static string GlobalPath => Path.Combine(SmartContextStore.GlobalRoot, "presets", "keyboard_shortcuts.json");
    public static string JobPath(OurPlanCoreJob job) => Path.Combine(job.RootPath, "AI_Context", "settings", "keyboard_shortcuts.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static KeyboardShortcutConfiguration Resolve(OurPlanCoreJob? job)
    {
        KeyboardShortcutConfiguration? local = job == null ? null : Load(JobPath(job));
        KeyboardShortcutConfiguration? global = Load(GlobalPath);
        return local is { InheritGlobal: false } ? local : global ?? KeyboardShortcutConfiguration.BuildDefault();
    }

    public static IReadOnlyList<ProtectedDataFile> Issues(OurPlanCoreJob? job) => DataFileReader.Issues(GlobalPath)
        .Concat(job == null ? [] : DataFileReader.Issues(JobPath(job))).ToArray();

    public static string IssueSummary(OurPlanCoreJob? job) => string.Join("\n", Issues(job).Select(issue =>
        (string.Equals(issue.Path, GlobalPath, StringComparison.OrdinalIgnoreCase) ? "Global" : "This job") +
        " shortcuts: " + issue.State + ". Original files are protected. Use Recover settings to retry, restore or reset."));

    private static KeyboardShortcutConfiguration? Load(string path) => DataFileReader.Read(path, Parse).Value;

    public static KeyboardShortcutConfiguration Parse(string text)
    {
        KeyboardShortcutConfiguration config = JsonSerializer.Deserialize<KeyboardShortcutConfiguration>(text)
            ?? throw new JsonException("Shortcut settings are empty.");
        config.Validate();
        return config;
    }

    public static void Save(string path, KeyboardShortcutConfiguration config)
    {
        config.Validate();
        _ = Load(path); // Validate the target even when another scope supplied the active profile.
        if (DataFileReader.IsProtected(path))
            throw new IOException("Keyboard shortcuts are protected after a read failure. Use Recover settings in the Keyboard Shortcuts window. Existing files are retained.");
        JobWriteAccess.Demand(path, "save keyboard shortcuts");
        DataFileReader.Demand(path, "save keyboard shortcuts");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(config, Options));
    }

    public static string Export(KeyboardShortcutConfiguration config)
    {
        config.Validate();
        return JsonSerializer.Serialize(config, Options);
    }

    public static void Recover(string path, string? restoreFrom = null, bool reset = false)
    {
        if (!reset)
        {
            if (restoreFrom != null) _ = Parse(File.ReadAllText(restoreFrom));
            DataFileReader.RestoreOrRetry(path, restoreFrom);
            return;
        }
        string temporary = Path.Combine(Path.GetTempPath(), "opc-shortcut-reset-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(temporary, Export(KeyboardShortcutConfiguration.BuildDefault()));
            DataFileReader.RestoreOrRetry(path, temporary);
        }
        finally { File.Delete(temporary); }
    }
}
