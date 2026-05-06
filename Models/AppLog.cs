using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace OurPlaneCore;

public static class AppLog
{
    private const long MaxLogBytes = 10L * 1024L * 1024L;
    private static readonly object Sync = new();

    public static void Info(string message) =>
        Write("INFO", message, null);

    public static void Warn(string message) =>
        Write("WARN", message, null);

    public static void Warn(Exception exception, string context) =>
        Write("WARN", context, exception);

    public static void Error(Exception exception, string context) =>
        Write("ERROR", context, exception);

    public static void Error(string context, Exception exception) =>
        Write("ERROR", context, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                string logPath = ResolveLogPath();
                string? directory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string line = string.Join(
                    '\t',
                    DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                    level,
                    Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture),
                    Clean(message),
                    exception == null ? "" : Clean(exception.ToString()));
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never break the app path that asked for a log entry.
        }
    }

    private static string ResolveLogPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string directory = Path.Combine(appData, "OurPlaneCore", "logs");
        string date = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string basePath = Path.Combine(directory, $"app-{date}.log");
        if (!File.Exists(basePath) || new FileInfo(basePath).Length <= MaxLogBytes)
            return basePath;

        for (int i = 1; i < 1000; i++)
        {
            string candidate = Path.Combine(directory, $"app-{date}-{i}.log");
            if (!File.Exists(candidate) || new FileInfo(candidate).Length <= MaxLogBytes)
                return candidate;
        }

        return Path.Combine(directory, $"app-{date}-{Guid.NewGuid():N}.log");
    }

    private static string Clean(string value) =>
        (value ?? "")
            .Replace('\t', ' ')
            .Replace("\r", " ")
            .Replace("\n", " ");
}
