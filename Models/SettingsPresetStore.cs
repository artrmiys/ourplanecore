using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OurPlaneCore;

// Editable folder templates that drive the three auto-create features:
//  • PageFolders  -> "Auto Page Folders" (Pages tree, flat list per mode)
//  • TakeoffTree  -> "Auto Takeoff Tree" (Takeoffs tree, nested per mode)
//                    and the per-folder sub-tree used by "From Pages".
public sealed class FolderTemplateConfig
{
    // mode ("COM"/"EWP") -> flat page-folder names
    public Dictionary<string, List<string>> PageFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // mode ("COM"/"EWP") -> nested takeoff tree
    public Dictionary<string, List<FolderPlanNode>> TakeoffTree { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public FolderTemplateConfig Clone()
    {
        var c = new FolderTemplateConfig();
        foreach (var (k, v) in PageFolders)
            c.PageFolders[k] = [.. v];
        foreach (var (k, v) in TakeoffTree)
            c.TakeoffTree[k] = v.Select(n => n.Clone()).ToList();
        return c;
    }

    public static FolderTemplateConfig BuildDefault()
    {
        var c = new FolderTemplateConfig();
        foreach (string mode in new[] { "COM", "EWP" })
        {
            c.PageFolders[mode] = PlanSwiftFolderTemplateService.DefaultPageFolders(mode).ToList();
            c.TakeoffTree[mode] = PlanSwiftFolderTemplateService.HardcodedSubTree(mode);
        }
        return c;
    }

    public List<string> PageFoldersFor(string mode) =>
        PageFolders.TryGetValue(mode, out var v) && v.Count > 0
            ? v
            : PlanSwiftFolderTemplateService.DefaultPageFolders(mode).ToList();

    public List<FolderPlanNode> TreeFor(string mode) =>
        TakeoffTree.TryGetValue(mode, out var v) && v.Count > 0
            ? v
            : PlanSwiftFolderTemplateService.HardcodedSubTree(mode);
}

public static class SettingsPresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string GlobalPath() =>
        Path.Combine(SmartContextStore.GlobalRoot, "presets", "folder_template.json");

    private static string JobPath(OurPlaneCoreJob job) =>
        Path.Combine(job.RootPath, "AI_Context", "settings", "folder_template.json");

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

    public static FolderTemplateConfig? LoadGlobal() => LoadJson<FolderTemplateConfig>(GlobalPath());
    public static void SaveGlobal(FolderTemplateConfig c) => SaveJson(GlobalPath(), c);

    public static FolderTemplateConfig? LoadJobOverride(OurPlaneCoreJob job) =>
        LoadJson<FolderTemplateConfig>(JobPath(job));

    public static void SaveJobOverride(OurPlaneCoreJob job, FolderTemplateConfig c) =>
        SaveJson(JobPath(job), c);

    public static void ClearJobOverride(OurPlaneCoreJob job)
    {
        try
        {
            string p = JobPath(job);
            if (File.Exists(p))
                File.Delete(p);
        }
        catch
        {
            // best effort
        }
    }

    // Effective config: per-job override → global → built-in defaults.
    public static FolderTemplateConfig Resolve(OurPlaneCoreJob? job)
    {
        if (job != null && LoadJobOverride(job) is { } j)
            return j;
        if (LoadGlobal() is { } g)
            return g;
        return FolderTemplateConfig.BuildDefault();
    }

    // Make the edited templates apply everywhere (menus, From Pages, etc.).
    public static void InstallProviders(OurPlaneCoreJob? job)
    {
        FolderTemplateConfig cfg = Resolve(job);
        PlanSwiftFolderTemplateService.PageFoldersOverride = mode => cfg.PageFoldersFor(mode);
        PlanSwiftFolderTemplateService.TakeoffTreeOverride = mode => cfg.TreeFor(mode);
    }
}
