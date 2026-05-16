using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OurPlaneCore;

internal sealed class PlanSwiftDataItem
{
    public string FolderPath { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string Name { get; init; } = "";
    public string Guid { get; init; } = "";
    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> PropertyGuids { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string Property(string name) =>
        Properties.TryGetValue(name, out string? value) ? value : "";

    public string PropertyGuid(string name) =>
        PropertyGuids.TryGetValue(name, out string? value) ? value : "";
}

internal static class PlanSwiftXml
{
    public static bool TryReadItem(string folderPath, out PlanSwiftDataItem item)
    {
        item = new PlanSwiftDataItem();
        string dataPath = Path.Combine(folderPath, "Data.xml");
        if (!File.Exists(dataPath))
            return false;

        try
        {
            XDocument doc = XDocument.Load(dataPath);
            XElement root = doc.Root ?? throw new InvalidDataException("Missing XML root.");
            item = ReadItem(folderPath, root);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }
    }

    public static double ParseDouble(string value)
    {
        string clean = (value ?? "").Trim();
        if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            return parsed;

        clean = clean.Replace(',', '.');
        return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
            ? parsed
            : 0;
    }

    public static bool ParseBool(string value, bool fallback = false)
    {
        string clean = (value ?? "").Trim();
        if (string.Equals(clean, "1", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(clean, "0", StringComparison.OrdinalIgnoreCase))
            return false;

        return bool.TryParse(clean, out bool parsed) ? parsed : fallback;
    }

    public static int ParseInt(string value)
    {
        string clean = (value ?? "").Trim();
        if (int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            return parsed;

        return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric)
            ? (int)Math.Round(numeric)
            : 0;
    }

    public static string NormalizeGuid(string value)
    {
        string clean = (value ?? "").Trim();
        if (clean.Length == 0)
            return "";

        clean = clean.Trim('{', '}');
        return clean.ToUpperInvariant();
    }

    public static string DecodeName(string value)
    {
        string clean = (value ?? "").Trim();
        if (clean.Length == 0)
            return "Untitled";

        try
        {
            return Uri.UnescapeDataString(clean);
        }
        catch
        {
            return clean;
        }
    }

    private static PlanSwiftDataItem ReadItem(string folderPath, XElement root)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var propertyGuids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement prop in root.Element("Properties")?.Elements("Property") ?? [])
        {
            string name = prop.Attribute("Name")?.Value ?? "";
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string value = prop.Attribute("Value")?.Value ?? prop.Value.Trim();
            properties[name] = value;
            string guid = prop.Attribute("GUID")?.Value ?? "";
            if (!string.IsNullOrWhiteSpace(guid))
                propertyGuids[name] = guid;
        }

        string className = root.Attribute("Class")?.Value ?? properties.GetValueOrDefault("Type", "");
        string nameValue = root.Attribute("Name")?.Value ?? properties.GetValueOrDefault("Name", Path.GetFileName(folderPath));
        string guidValue = root.Attribute("GUID")?.Value ?? properties.GetValueOrDefault("GUID", "");

        return new PlanSwiftDataItem
        {
            FolderPath = folderPath,
            ClassName = className.Trim(),
            Name = PlanSwiftXml.DecodeName(nameValue),
            Guid = NormalizeGuid(guidValue),
            Properties = properties,
            PropertyGuids = propertyGuids,
        };
    }
}
