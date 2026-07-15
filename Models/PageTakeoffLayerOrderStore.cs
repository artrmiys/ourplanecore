using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace OurPlanCore;

public static class PageTakeoffLayerOrderStore
{
    private const string FileName = "takeoff_layers.json";

    public static IReadOnlyList<string> Load(string pageFolder)
    {
        string path = Path.Combine(pageFolder, FileName);
        if (!File.Exists(path))
            return [];

        try
        {
            string json = File.ReadAllText(path);
            PageTakeoffLayerOrderFile? file = System.Text.Json.JsonSerializer.Deserialize<PageTakeoffLayerOrderFile>(
                json,
                OurPlanCoreJobStore.JsonOptions);
            return Normalize(file?.Order);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            AppLog.Warn(ex, $"Failed to read takeoff layer order from {path}");
            return [];
        }
    }

    public static void Save(string pageFolder, IReadOnlyList<string> order)
    {
        string path = Path.Combine(pageFolder, FileName);
        JobWriteAccess.Demand(path, "save takeoff layer order");
        var file = new PageTakeoffLayerOrderFile
        {
            Order = Normalize(order).ToList(),
        };

        IoUtil.WriteAllTextAtomic(
            path,
            System.Text.Json.JsonSerializer.Serialize(file, OurPlanCoreJobStore.JsonOptions));
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string>? order) =>
        order?
            .Select(entry => (entry ?? "").Trim().Replace('\\', '/').Trim('/'))
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    private sealed class PageTakeoffLayerOrderFile
    {
        [JsonPropertyName("order")]
        public List<string> Order { get; set; } = [];
    }
}
