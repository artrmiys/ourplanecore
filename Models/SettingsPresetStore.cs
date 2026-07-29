using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OurPlanCore;

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

    private static string JobPath(OurPlanCoreJob job) =>
        Path.Combine(job.RootPath, "AI_Context", "settings", "folder_template.json");

    private static string GlobalPageSortPath() =>
        Path.Combine(SmartContextStore.GlobalRoot, "presets", "page_sort.json");

    private static string JobPageSortPath(OurPlanCoreJob job) =>
        Path.Combine(job.RootPath, "AI_Context", "settings", "page_sort.json");

    private static string GlobalSheetMetadataPath() =>
        Path.Combine(SmartContextStore.GlobalRoot, "presets", "sheet_metadata.json");

    private static string JobSheetMetadataPath(OurPlanCoreJob job) =>
        Path.Combine(job.RootPath, "AI_Context", "settings", "sheet_metadata.json");

    private static string GlobalRasterDpiPresetPath() =>
        Path.Combine(SmartContextStore.GlobalRoot, "presets", "raster_dpi_presets.json");

    private static string JobRasterDpiPresetPath(OurPlanCoreJob job) =>
        Path.Combine(job.RootPath, "AI_Context", "settings", "raster_dpi_presets.json");

    private static string GlobalExcelMacroExportPath() =>
        Path.Combine(SmartContextStore.GlobalRoot, "presets", "excel_macro_export.json");

    private static string JobExcelMacroExportPath(OurPlanCoreJob job) =>
        Path.Combine(job.RootPath, "AI_Context", "settings", "excel_macro_export.json");

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
        JobWriteAccess.Demand(path, "save settings preset");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    public static FolderTemplateConfig? LoadGlobal() => LoadJson<FolderTemplateConfig>(GlobalPath());
    public static void SaveGlobal(FolderTemplateConfig c) => SaveJson(GlobalPath(), c);

    public static FolderTemplateConfig? LoadJobOverride(OurPlanCoreJob job) =>
        LoadJson<FolderTemplateConfig>(JobPath(job));

    public static void SaveJobOverride(OurPlanCoreJob job, FolderTemplateConfig c) =>
        SaveJson(JobPath(job), c);

    public static void ClearJobOverride(OurPlanCoreJob job)
    {
        string p = JobPath(job);
        if (File.Exists(p))
            JobWriteAccess.Demand(p, "clear job settings override");
        try
        {
            if (File.Exists(p))
                File.Delete(p);
        }
        catch
        {
            // best effort
        }
    }

    // Effective config: per-job override → global → built-in defaults.
    public static FolderTemplateConfig Resolve(OurPlanCoreJob? job)
    {
        if (job != null && LoadJobOverride(job) is { } j)
            return j;
        if (LoadGlobal() is { } g)
            return g;
        return FolderTemplateConfig.BuildDefault();
    }

    // Make the edited templates apply everywhere (menus, From Pages, etc.).
    public static void InstallProviders(OurPlanCoreJob? job)
    {
        FolderTemplateConfig cfg = Resolve(job);
        PlanSwiftFolderTemplateService.PageFoldersOverride = mode => cfg.PageFoldersFor(mode);
        PlanSwiftFolderTemplateService.TakeoffTreeOverride = mode => cfg.TreeFor(mode);
    }

    // ── Page-sort rules (Sort A/S, Sort D/Sec/WT) ────────────────────────
    public static PageSortConfig? LoadGlobalPageSort() =>
        LoadJson<PageSortConfig>(GlobalPageSortPath());

    public static void SaveGlobalPageSort(PageSortConfig c) =>
        SaveJson(GlobalPageSortPath(), c);

    public static PageSortConfig? LoadJobPageSortOverride(OurPlanCoreJob job) =>
        LoadJson<PageSortConfig>(JobPageSortPath(job));

    public static void SaveJobPageSortOverride(OurPlanCoreJob job, PageSortConfig c) =>
        SaveJson(JobPageSortPath(job), c);

    public static void ClearJobPageSortOverride(OurPlanCoreJob job)
    {
        string p = JobPageSortPath(job);
        if (File.Exists(p))
            JobWriteAccess.Demand(p, "clear job page-sort override");
        try
        {
            if (File.Exists(p))
                File.Delete(p);
        }
        catch
        {
            // best effort
        }
    }

    // Effective page-sort config: per-job override → global → built-in defaults.
    public static PageSortConfig ResolvePageSort(OurPlanCoreJob? job)
    {
        if (job != null && LoadJobPageSortOverride(job) is { } j)
            return PageSortConfig.UpgradeForCurrentSchema(j);
        if (LoadGlobalPageSort() is { } g)
            return PageSortConfig.UpgradeForCurrentSchema(g);
        return PageSortConfig.UpgradeForCurrentSchema(PageSortConfig.BuildDefault());
    }

    public static void InstallPageSortProvider(OurPlanCoreJob? job) =>
        PageSortRulesService.Install(ResolvePageSort(job));

    // Sheet metadata policy: per-job override → global → legacy-compatible defaults.
    public static SheetMetadataConfig? LoadGlobalSheetMetadata() =>
        LoadJson<SheetMetadataConfig>(GlobalSheetMetadataPath());

    public static void SaveGlobalSheetMetadata(SheetMetadataConfig config) =>
        SaveJson(
            GlobalSheetMetadataPath(),
            SheetMetadataConfig.UpgradeForCurrentSchema(config));

    public static SheetMetadataConfig? LoadJobSheetMetadataOverride(OurPlanCoreJob job) =>
        LoadJson<SheetMetadataConfig>(JobSheetMetadataPath(job));

    public static void SaveJobSheetMetadataOverride(
        OurPlanCoreJob job,
        SheetMetadataConfig config) =>
        SaveJson(
            JobSheetMetadataPath(job),
            SheetMetadataConfig.UpgradeForCurrentSchema(config));

    public static bool ClearJobSheetMetadataOverride(OurPlanCoreJob job)
    {
        string path = JobSheetMetadataPath(job);
        if (File.Exists(path))
            JobWriteAccess.Demand(path, "clear job sheet-metadata override");
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return !File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    public static SheetMetadataConfig ResolveSheetMetadata(OurPlanCoreJob? job)
    {
        if (job != null && LoadJobSheetMetadataOverride(job) is { } jobConfig)
            return SheetMetadataConfig.UpgradeForCurrentSchema(jobConfig);
        if (LoadGlobalSheetMetadata() is { } globalConfig)
            return SheetMetadataConfig.UpgradeForCurrentSchema(globalConfig);
        return SheetMetadataConfig.BuildDefault();
    }

    public static void InstallSheetMetadataProvider(OurPlanCoreJob? job) =>
        SheetMetadataRulesService.Install(ResolveSheetMetadata(job));

    // Sheet Manager raster DPI buttons: per-job override -> global -> defaults.
    public static RasterDpiPresetConfig? LoadGlobalRasterDpiPresets() =>
        LoadJson<RasterDpiPresetConfig>(GlobalRasterDpiPresetPath());

    public static void SaveGlobalRasterDpiPresets(RasterDpiPresetConfig config) =>
        SaveJson(
            GlobalRasterDpiPresetPath(),
            RasterDpiPresetConfig.UpgradeForCurrentSchema(config));

    public static RasterDpiPresetConfig? LoadJobRasterDpiPresetOverride(OurPlanCoreJob job) =>
        LoadJson<RasterDpiPresetConfig>(JobRasterDpiPresetPath(job));

    public static void SaveJobRasterDpiPresetOverride(
        OurPlanCoreJob job,
        RasterDpiPresetConfig config) =>
        SaveJson(
            JobRasterDpiPresetPath(job),
            RasterDpiPresetConfig.UpgradeForCurrentSchema(config));

    public static RasterDpiPresetConfig ResolveRasterDpiPresets(OurPlanCoreJob? job)
    {
        if (job != null && LoadJobRasterDpiPresetOverride(job) is { } jobConfig)
            return RasterDpiPresetConfig.UpgradeForCurrentSchema(jobConfig);
        if (LoadGlobalRasterDpiPresets() is { } globalConfig)
            return RasterDpiPresetConfig.UpgradeForCurrentSchema(globalConfig);
        return RasterDpiPresetConfig.BuildDefault();
    }

    public static void InstallRasterDpiPresetProvider(OurPlanCoreJob? job) =>
        RasterDpiPresetService.Install(ResolveRasterDpiPresets(job));

    // Excel macro actions: per-job override -> global -> the TemplateCom contract.
    public static ExcelMacroExportConfig? LoadGlobalExcelMacroExport() =>
        LoadJson<ExcelMacroExportConfig>(GlobalExcelMacroExportPath()) is { } config
            ? ExcelMacroExportConfig.UpgradeForCurrentSchema(config)
            : null;

    public static void SaveGlobalExcelMacroExport(ExcelMacroExportConfig config) =>
        SaveJson(
            GlobalExcelMacroExportPath(),
            ExcelMacroExportConfig.UpgradeForCurrentSchema(config));

    public static ExcelMacroExportConfig? LoadJobExcelMacroExportOverride(OurPlanCoreJob job) =>
        LoadJson<ExcelMacroExportConfig>(JobExcelMacroExportPath(job)) is { } config
            ? ExcelMacroExportConfig.UpgradeForCurrentSchema(config)
            : null;

    public static void SaveJobExcelMacroExportOverride(
        OurPlanCoreJob job,
        ExcelMacroExportConfig config) =>
        SaveJson(
            JobExcelMacroExportPath(job),
            ExcelMacroExportConfig.UpgradeForCurrentSchema(config));

    public static bool ClearJobExcelMacroExportOverride(OurPlanCoreJob job)
    {
        string path = JobExcelMacroExportPath(job);
        if (File.Exists(path))
            JobWriteAccess.Demand(path, "clear job Excel macro export override");
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return !File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    public static ExcelMacroExportConfig ResolveExcelMacroExport(OurPlanCoreJob? job)
    {
        if (job != null && LoadJobExcelMacroExportOverride(job) is { } jobConfig)
            return ExcelMacroExportConfig.UpgradeForCurrentSchema(jobConfig);
        if (LoadGlobalExcelMacroExport() is { } globalConfig)
            return ExcelMacroExportConfig.UpgradeForCurrentSchema(globalConfig);
        return ExcelMacroExportConfig.BuildDefault();
    }

    public static void InstallExcelMacroExportProvider(OurPlanCoreJob? job) =>
        ExcelMacroExportConfigProvider.Install(ResolveExcelMacroExport(job));
}
