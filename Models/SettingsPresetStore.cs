using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OurPlaneCore;

// Global presets (+ optional per-job override) for Settings sections.
// Global:  %APPDATA%/OurPlaneCore (SmartContextStore.GlobalRoot) /presets/<section>.json
// Per-job: <job>/AI_Context/settings/<section>.json
public sealed class FolderPlanPresets
{
    public List<FolderPlan> Presets { get; set; } = [];
}

public static class SettingsPresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static string GlobalPath(string section) =>
        Path.Combine(SmartContextStore.GlobalRoot, "presets", $"{section}.json");

    private static string JobPath(OurPlaneCoreJob job, string section) =>
        Path.Combine(job.RootPath, "AI_Context", "settings", $"{section}.json");

    private static T? LoadJson<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;
            string text = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<T>(text);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    // ── From Pages presets (global) ──────────────────────────────────────
    public static FolderPlanPresets LoadFromPagesPresets() =>
        LoadJson<FolderPlanPresets>(GlobalPath("frompages")) ?? new FolderPlanPresets();

    public static void SaveFromPagesPresets(FolderPlanPresets presets) =>
        SaveJson(GlobalPath("frompages"), presets);

    // ── From Pages active plan: per-job override, else global "active" ────
    public static FolderPlan? LoadJobFromPagesPlan(OurPlaneCoreJob job) =>
        LoadJson<FolderPlan>(JobPath(job, "frompages_active"));

    public static void SaveJobFromPagesPlan(OurPlaneCoreJob job, FolderPlan plan) =>
        SaveJson(JobPath(job, "frompages_active"), plan);

    public static void ClearJobFromPagesPlan(OurPlaneCoreJob job)
    {
        try
        {
            string path = JobPath(job, "frompages_active");
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }

    public static FolderPlan? LoadGlobalActiveFromPagesPlan() =>
        LoadJson<FolderPlan>(GlobalPath("frompages_active"));

    public static void SaveGlobalActiveFromPagesPlan(FolderPlan plan) =>
        SaveJson(GlobalPath("frompages_active"), plan);

    // Effective plan: per-job override → global active → built default.
    public static FolderPlan ResolveFromPagesPlan(OurPlaneCoreJob job, string requestedMode = "AUTO")
    {
        FolderPlan? jobPlan = LoadJobFromPagesPlan(job);
        if (jobPlan is { TopFolders.Count: > 0 } or { SubTree.Count: > 0 })
            return jobPlan;

        FolderPlan? global = LoadGlobalActiveFromPagesPlan();
        if (global is { SubTree.Count: > 0 })
        {
            // keep stored sub-tree/mode, refresh top folders from this job's pages
            global.TopFolders = PlanSwiftFolderTemplateService.CollectCapsGroupNames(job).ToList();
            return global;
        }

        return PlanSwiftFolderTemplateService.BuildDefaultPlan(job, requestedMode);
    }
}
