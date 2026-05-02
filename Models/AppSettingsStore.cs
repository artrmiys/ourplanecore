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
}

public static class AppSettingsStore
{
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
}
