using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OurPlaneCore;

public sealed class AppSettings
{
    public string JobsRootPath { get; set; } = "";
    public List<string> JobsRootPaths { get; set; } = [];
    public string LastJobPath { get; set; } = "";
    public string LastPageFolder { get; set; } = "";
    public string UnitMode { get; set; } = "Imperial";
    public string Theme { get; set; } = "Light";
    public string ViewportBackground { get; set; } = "#FFFFFF";
    public bool ShowMeasurementLabels { get; set; } = true;
    public bool ShowLineLabels { get; set; } = true;
    public bool ShowAreaLabels { get; set; } = true;
    public bool ShowCountLabels { get; set; }
    public double MeasurementLabelScale { get; set; } = 1.0;
    public bool ShowSheetLegend { get; set; } = true;
    public string SheetLegendAnchor { get; set; } = "BottomLeft";
    public double SheetLegendScale { get; set; } = 1.0;
    public double SheetHeaderScale { get; set; } = 1.0;
    public bool ScaleSheetOverlaysWithPage { get; set; } = false;
    public bool ScaleMeasurementLabelsWithPage { get; set; } = false;
    public bool ScaleSheetHeaderWithPage { get; set; } = false;
    public double MassingFloorAssemblyFeet { get; set; } = SmartMassingDraftService.DefaultFloorAssemblyFeet;
    public double MassingLevelSpacingFeet { get; set; } = SmartMassingDraftService.DefaultLevelSpacingFeet;
    public double LeftPanelWidth { get; set; } = 200.0;
    public double RightPanelWidth { get; set; } = 220.0;
    public string OpenAiModel { get; set; } = OpenAiRequestRunner.DefaultModel;
    public string FolderTemplateMode { get; set; } = "AUTO";
    public List<RecentJobInfo> RecentJobs { get; set; } = [];
}

public sealed class RecentJobInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string LastOpenedUtc { get; set; } = "";
    public string ThumbnailPath { get; set; } = "";
    public bool IsPinned { get; set; }
}

public sealed class OpenAiKeyStatus
{
    public bool Found { get; init; }
    public string Source { get; init; } = "missing";
    public string Description { get; init; } = "";
}

public sealed class OpenAiModelStatus
{
    public string Model { get; init; } = OpenAiRequestRunner.DefaultModel;
    public string Source { get; init; } = "default fallback";
}

public static class AppSettingsStore
{
    public static readonly string[] SuggestedOpenAiModels =
    [
        OpenAiRequestRunner.DefaultModel,
        "gpt-5",
        "gpt-4.1",
        "gpt-4.1-mini",
        "gpt-4o",
        "gpt-4o-mini",
    ];

    private const int MaxRecentJobs = 16;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string SettingsPath
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "OurPlaneCore", "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))
                ?? new AppSettings();
            NormalizeJobsRoots(settings);
            NormalizeRecentJobs(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        NormalizeJobsRoots(settings);
        string? dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static IReadOnlyList<string> CurrentJobsRootPaths(AppSettings settings)
    {
        NormalizeJobsRoots(settings);
        return settings.JobsRootPaths.ToList();
    }

    public static void AddJobsRoot(AppSettings settings, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rootPath.Trim());
        }
        catch
        {
            fullPath = rootPath.Trim();
        }

        string key = NormalizePath(fullPath);
        var roots = (settings.JobsRootPaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !string.Equals(NormalizePath(path), key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        roots.Insert(0, fullPath);
        settings.JobsRootPaths = roots;
        settings.JobsRootPath = fullPath;
        NormalizeJobsRoots(settings);
    }

    public static void AddRecentJob(AppSettings settings, string jobPath, string jobName)
    {
        if (string.IsNullOrWhiteSpace(jobPath))
            return;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(jobPath.Trim());
        }
        catch
        {
            fullPath = jobPath.Trim();
        }

        string cleanName = string.IsNullOrWhiteSpace(jobName)
            ? Path.GetFileName(fullPath)
            : jobName.Trim();

        string normalizedFullPath = NormalizePath(fullPath);
        var existingMatch = (settings.RecentJobs ?? [])
            .FirstOrDefault(j => string.Equals(NormalizePath(j.Path), normalizedFullPath, StringComparison.OrdinalIgnoreCase));
        var existing = (settings.RecentJobs ?? [])
            .Where(j => !string.IsNullOrWhiteSpace(j.Path))
            .Where(j => !string.Equals(NormalizePath(j.Path), normalizedFullPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        existing.Insert(0, new RecentJobInfo
        {
            Name = cleanName,
            Path = fullPath,
            LastOpenedUtc = DateTime.UtcNow.ToString("O"),
            ThumbnailPath = existingMatch?.ThumbnailPath ?? "",
            IsPinned = existingMatch?.IsPinned ?? false,
        });

        settings.RecentJobs = TrimRecentJobsPreservingPinned(existing);
    }

    public static void SetRecentJobPinned(AppSettings settings, string jobPath, string jobName, bool pinned)
    {
        if (string.IsNullOrWhiteSpace(jobPath))
            return;

        string key = NormalizePath(jobPath);
        var list = (settings.RecentJobs ?? [])
            .Where(j => !string.IsNullOrWhiteSpace(j.Path))
            .ToList();
        RecentJobInfo? existing = list.FirstOrDefault(j =>
            string.Equals(NormalizePath(j.Path), key, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new RecentJobInfo
            {
                Name = string.IsNullOrWhiteSpace(jobName) ? Path.GetFileName(jobPath) : jobName.Trim(),
                Path = jobPath.Trim(),
                LastOpenedUtc = DateTime.UtcNow.ToString("O"),
            };
        }
        else
        {
            list.Remove(existing);
        }

        existing.IsPinned = pinned;
        if (string.IsNullOrWhiteSpace(existing.Name))
            existing.Name = string.IsNullOrWhiteSpace(jobName) ? Path.GetFileName(jobPath) : jobName.Trim();

        if (pinned)
        {
            list.Insert(0, existing);
        }
        else
        {
            int insertAt = list.FindLastIndex(j => j.IsPinned) + 1;
            list.Insert(Math.Max(0, insertAt), existing);
        }

        settings.RecentJobs = TrimRecentJobsPreservingPinned(list);
    }

    public static void RemoveRecentJob(AppSettings settings, string jobPath)
    {
        if (string.IsNullOrWhiteSpace(jobPath))
            return;

        string key = NormalizePath(jobPath);
        settings.RecentJobs = (settings.RecentJobs ?? [])
            .Where(j => !string.Equals(NormalizePath(j.Path), key, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static void UpdateRecentJobThumbnail(AppSettings settings, string jobPath, string thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(jobPath) || string.IsNullOrWhiteSpace(thumbnailPath))
            return;

        string key = NormalizePath(jobPath);
        foreach (var recent in settings.RecentJobs ?? [])
        {
            if (!string.Equals(NormalizePath(recent.Path), key, StringComparison.OrdinalIgnoreCase))
                continue;

            recent.ThumbnailPath = thumbnailPath.Trim();
            return;
        }
    }

    public static void NormalizeRecentJobs(AppSettings settings)
    {
        var unique = new List<RecentJobInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in settings.RecentJobs ?? [])
        {
            if (string.IsNullOrWhiteSpace(job.Path))
                continue;

            string key = NormalizePath(job.Path);
            if (!seen.Add(key))
                continue;

            unique.Add(new RecentJobInfo
            {
                Name = string.IsNullOrWhiteSpace(job.Name) ? Path.GetFileName(job.Path) : job.Name.Trim(),
                Path = job.Path.Trim(),
                LastOpenedUtc = job.LastOpenedUtc ?? "",
                ThumbnailPath = job.ThumbnailPath ?? "",
                IsPinned = job.IsPinned,
            });
        }

        settings.RecentJobs = TrimRecentJobsPreservingPinned(unique);
    }

    public static void NormalizeJobsRoots(AppSettings settings)
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                return;

            string clean;
            try
            {
                clean = Path.GetFullPath(root.Trim());
            }
            catch
            {
                clean = root.Trim();
            }

            string key = NormalizePath(clean);
            if (seen.Add(key))
                roots.Add(clean);
        }

        AddRoot(settings.JobsRootPath);
        foreach (string root in settings.JobsRootPaths ?? [])
            AddRoot(root);

        settings.JobsRootPaths = roots;
        settings.JobsRootPath = roots.FirstOrDefault() ?? "";
    }

    private static List<RecentJobInfo> TrimRecentJobsPreservingPinned(IReadOnlyList<RecentJobInfo> recentJobs)
    {
        var pinned = recentJobs.Where(j => j.IsPinned).ToList();
        int remaining = Math.Max(0, MaxRecentJobs - pinned.Count);
        pinned.AddRange(recentJobs.Where(j => !j.IsPinned).Take(remaining));
        return pinned;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public static OpenAiKeyStatus GetOpenAiKeyStatus()
    {
        string? processKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(processKey))
        {
            return new OpenAiKeyStatus
            {
                Found = true,
                Source = "process environment",
                Description = "OPENAI_API_KEY is available in the current process.",
            };
        }

        try
        {
            string? userKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(userKey))
            {
                return new OpenAiKeyStatus
                {
                    Found = true,
                    Source = "Windows user environment",
                    Description = "OPENAI_API_KEY is saved in the current Windows user environment.",
                };
            }
        }
        catch (Exception ex)
        {
            return new OpenAiKeyStatus
            {
                Found = false,
                Source = "user environment unavailable",
                Description = ex.Message,
            };
        }

        return new OpenAiKeyStatus
        {
            Found = false,
            Source = "missing",
            Description = "OPENAI_API_KEY was not found in process or Windows user environment.",
        };
    }

    public static string ReadOpenAiApiKey()
    {
        string? processKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(processKey))
            return processKey.Trim();

        try
        {
            string? userKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.User);
            return string.IsNullOrWhiteSpace(userKey) ? "" : userKey.Trim();
        }
        catch
        {
            return "";
        }
    }

    public static void SaveOpenAiKeyToUserEnvironment(string apiKey)
    {
        string clean = apiKey.Trim();
        if (string.IsNullOrWhiteSpace(clean))
            throw new ArgumentException("OpenAI API key is required.", nameof(apiKey));

        Environment.SetEnvironmentVariable("OPENAI_API_KEY", clean, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", clean, EnvironmentVariableTarget.Process);
    }

    public static void ClearOpenAiUserEnvironmentKey()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null, EnvironmentVariableTarget.User);
    }

    public static OpenAiModelStatus GetOpenAiModelStatus(AppSettings settings)
    {
        string? envModel = Environment.GetEnvironmentVariable("OURPLANECORE_OPENAI_MODEL", EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(envModel))
        {
            return new OpenAiModelStatus
            {
                Model = NormalizeOpenAiModel(envModel),
                Source = "OURPLANECORE_OPENAI_MODEL process environment",
            };
        }

        if (!string.IsNullOrWhiteSpace(settings.OpenAiModel))
        {
            return new OpenAiModelStatus
            {
                Model = NormalizeOpenAiModel(settings.OpenAiModel),
                Source = "app settings",
            };
        }

        return new OpenAiModelStatus
        {
            Model = OpenAiRequestRunner.DefaultModel,
            Source = "default fallback",
        };
    }

    public static string ResolveOpenAiModel(AppSettings settings) =>
        GetOpenAiModelStatus(settings).Model;

    public static string NormalizeOpenAiModel(string? model)
    {
        string clean = model?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(clean) ? OpenAiRequestRunner.DefaultModel : clean;
    }
}
