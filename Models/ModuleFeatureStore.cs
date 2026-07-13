using System;
using System.IO;
using System.Text.Json;

namespace OurPlanCore;

public static class ModuleFeatureStore
{
    private const string FileName = "modules.json";
    public const string GlobalRootOverrideEnvironmentVariable = "OURPLANCORE_MODULE_SETTINGS_ROOT";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string GlobalConfigPath
    {
        get
        {
            string? overrideRoot = AppIdentity.GetEnvironmentVariable(GlobalRootOverrideEnvironmentVariable);
            string root = string.IsNullOrWhiteSpace(overrideRoot)
                ? SmartContextStore.GlobalRoot
                : Path.GetFullPath(overrideRoot);
            return Path.Combine(root, "presets", FileName);
        }
    }

    public static string GetJobConfigPath(OurPlanCoreJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (string.IsNullOrWhiteSpace(job.RootPath))
            throw new ArgumentException("The job root path is required.", nameof(job));

        return Path.Combine(job.RootPath, "AI_Context", "settings", FileName);
    }

    public static ModuleFeatureConfig? LoadGlobal() =>
        Load(GlobalConfigPath);

    public static void SaveGlobal(ModuleFeatureConfig config) =>
        Save(GlobalConfigPath, config);

    public static ModuleFeatureConfig? LoadJobOverride(OurPlanCoreJob job) =>
        Load(GetJobConfigPath(job));

    public static void SaveJobOverride(OurPlanCoreJob job, ModuleFeatureConfig config) =>
        Save(GetJobConfigPath(job), config);

    public static void ClearJobOverride(OurPlanCoreJob job)
    {
        string path = GetJobConfigPath(job);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn(ex, $"Module settings job override could not be cleared for {path}");
            throw;
        }
    }

    public static ModuleFeatureConfig Resolve(OurPlanCoreJob? job)
    {
        if (job != null && LoadJobOverride(job) is { } jobConfig)
            return jobConfig;

        if (LoadGlobal() is { } globalConfig)
            return globalConfig;

        return ModuleFeatureConfig.BuildDefault();
    }

    private static ModuleFeatureConfig? Load(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            string json = File.ReadAllText(path);
            ModuleFeatureConfig? config = JsonSerializer.Deserialize<ModuleFeatureConfig>(json, JsonOptions);
            return config == null
                ? null
                : ModuleFeatureConfig.UpgradeForCurrentSchema(config);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn(ex, $"Module settings could not be read from {path}");
            return null;
        }
    }

    private static void Save(string path, ModuleFeatureConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ModuleFeatureConfig writable = ModuleFeatureConfig.UpgradeForCurrentSchema(config);
        IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(writable, JsonOptions));
    }
}
