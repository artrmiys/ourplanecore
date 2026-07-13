using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OurPlanCore;

public static partial class SmartLearningStore
{
    private static IReadOnlyList<T> LoadJsonLines<T>(string path)
    {
        if (!File.Exists(path))
            return [];

        var records = new List<T>();
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                T? record = JsonSerializer.Deserialize<T>(line);
                if (record != null)
                    records.Add(record);
            }
            catch (Exception ex)
            {
                AppLog.Warn(ex, $"Learning history row could not be read from {path}");
                // Keep learning history readable even if a hand-edited row is bad.
            }
        }

        return records;
    }

    private static T? LoadJson<T>(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<T>(File.ReadAllText(path))
                : default;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Load learning JSON failed for {path}");
            return default;
        }
    }

    private static Dictionary<string, int> CountValues(IEnumerable<string> values) =>
        values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

    private static void EnsureFile(string path, string initialContent)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        if (!File.Exists(path))
        {
            try
            {
                IoUtil.WriteAllTextAtomic(path, initialContent);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
            }
        }
    }
}
