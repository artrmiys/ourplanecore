using System;
using System.IO;
using System.Text.Json;

namespace SmartTakeoffs;

public sealed class AppSettings
{
    public string JobsRootPath { get; set; } = "";
    public string LastJobPath { get; set; } = "";
    public string LastPageFolder { get; set; } = "";
    public string UnitMode { get; set; } = "Imperial";
    public string Theme { get; set; } = "Light";
    public string ViewportBackground { get; set; } = "#FFFFFF";
    public string OpenAiModel { get; set; } = OpenAiRequestRunner.DefaultModel;
    public string FolderTemplateMode { get; set; } = "AUTO";
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string SettingsPath
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "SmartTakeoffs", "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))
                ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        string? dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
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
        string? envModel = Environment.GetEnvironmentVariable("SMARTTAKEOFFS_OPENAI_MODEL", EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(envModel))
        {
            return new OpenAiModelStatus
            {
                Model = NormalizeOpenAiModel(envModel),
                Source = "SMARTTAKEOFFS_OPENAI_MODEL process environment",
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
