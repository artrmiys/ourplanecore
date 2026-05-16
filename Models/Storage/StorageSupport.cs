using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace OurPlaneCore;

internal static class StorageSupport
{
    private static readonly object CorruptJsonLock = new();
    private static readonly List<string> CorruptJsonFiles = [];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static IReadOnlyList<string> DrainCorruptJsonFiles()
    {
        lock (CorruptJsonLock)
        {
            var files = CorruptJsonFiles.ToList();
            CorruptJsonFiles.Clear();
            return files;
        }
    }

    public static bool IsPageFolder(string folder) =>
        File.Exists(Path.Combine(folder, "source.json"));

    public static string DisplayName(string folder) =>
        ReadName(folder) ?? Path.GetFileName(folder);

    public static string? ReadName(string folder)
    {
        XElement? root = ReadDataRoot(folder);
        return root?.Attribute("Name")?.Value;
    }

    public static string? ReadClass(string folder)
    {
        XElement? root = ReadDataRoot(folder);
        return root?.Attribute("Class")?.Value;
    }

    public static string SanitizeName(string name, int maxLength)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        char[] chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        string cleaned = NormalizeDisplayName(new string(chars), maxLength);
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength].TrimEnd();
    }

    public static string NormalizeDisplayName(string name, int maxLength)
    {
        string cleaned = (name ?? "").Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Untitled";
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength].TrimEnd();
    }

    public static bool IsSameOrDescendant(string possibleParent, string possibleChild)
    {
        string parent = FullPathWithSeparator(possibleParent);
        string child = FullPathWithSeparator(possibleChild);
        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeMeasurementType(string value)
    {
        string clean = (value ?? "").Trim().ToLowerInvariant();
        return clean switch
        {
            "point" or "count" or "counts" or "ea" or "each" => "point",
            "area" or "sf" or "sqft" or "square" => "area",
            "line" or "linear" or "lf" or "ft" => "line",
            _ => "line",
        };
    }

    public static void WriteItemDataXml(string folder, string itemClass, string name, int orderIndex)
    {
        string guid = Guid.NewGuid().ToString().ToUpperInvariant();
        var root = new XElement("Item",
            new XAttribute("Class", itemClass),
            new XAttribute("Name", name),
            new XAttribute("GUID", guid),
            new XElement("Properties",
                new XElement("Property", new XAttribute("Name", "OrderIndex"), new XAttribute("Value", orderIndex)),
                new XElement("Property", new XAttribute("Name", "Name"), new XAttribute("Value", name)),
                new XElement("Property", new XAttribute("Name", "Type"), new XAttribute("Value", itemClass)),
                new XElement("Property", new XAttribute("Name", "GUID"), new XAttribute("Value", guid))));

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        doc.Save(Path.Combine(folder, "Data.xml"));
    }

    public static void UpdateItemName(string folder, string name)
    {
        string path = Path.Combine(folder, "Data.xml");
        if (!File.Exists(path)) return;

        XDocument doc = XDocument.Load(path);
        XElement? root = doc.Root;
        if (root == null) return;

        bool changed = false;
        if (!string.Equals(root.Attribute("Name")?.Value, name, StringComparison.Ordinal))
        {
            root.SetAttributeValue("Name", name);
            changed = true;
        }

        changed = SetProperty(root, "Name", name) || changed;
        if (changed)
            doc.Save(path);
    }

    public static void SetProperty(string folder, string propertyName, string value)
    {
        string path = Path.Combine(folder, "Data.xml");
        if (!File.Exists(path)) return;

        XDocument doc = XDocument.Load(path);
        XElement? root = doc.Root;
        if (root == null) return;

        if (SetProperty(root, propertyName, value))
            doc.Save(path);
    }

    public static string? ReadProperty(string folder, string propertyName)
    {
        XElement? root = ReadDataRoot(folder);
        return root?
            .Element("Properties")?
            .Elements("Property")
            .FirstOrDefault(p => p.Attribute("Name")?.Value == propertyName)?
            .Attribute("Value")?
            .Value;
    }

    public static string UniqueDirectoryPath(string desiredPath)
    {
        if (!Directory.Exists(desiredPath)) return desiredPath;

        for (int i = 2; ; i++)
        {
            string candidate = $"{desiredPath} ({i})";
            if (!Directory.Exists(candidate)) return candidate;
        }
    }

    public static string UniqueFilePath(string desiredPath)
    {
        if (!File.Exists(desiredPath)) return desiredPath;

        string dir = Path.GetDirectoryName(desiredPath) ?? "";
        string name = Path.GetFileNameWithoutExtension(desiredPath);
        string ext = Path.GetExtension(desiredPath);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    public static IEnumerable<string> EnumerateSelfAndDescendants(string rootFolder)
    {
        yield return rootFolder;
        foreach (string dir in Directory.EnumerateDirectories(rootFolder, "*", SearchOption.AllDirectories))
            yield return dir;
    }

    public static void QuarantineCorruptJson(string path, string context, Exception exception)
    {
        AppLog.Error(exception, $"{context} failed for {path}");
        string targetPath = "";
        try
        {
            if (File.Exists(path))
            {
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                targetPath = UniqueCorruptJsonPath($"{path}.corrupt-{timestamp}");
                File.Move(path, targetPath);
            }
        }
        catch (Exception moveException)
        {
            AppLog.Warn(moveException, $"Failed to quarantine corrupt JSON {path}");
        }

        lock (CorruptJsonLock)
        {
            CorruptJsonFiles.Add(string.IsNullOrWhiteSpace(targetPath)
                ? path
                : $"{path} -> {targetPath}");
        }
    }

    private static bool SetProperty(XElement root, string propertyName, string value)
    {
        XElement props = root.Element("Properties") ?? new XElement("Properties");
        if (props.Parent == null)
            root.Add(props);

        XElement? prop = props.Elements("Property")
            .FirstOrDefault(p => p.Attribute("Name")?.Value == propertyName);
        if (prop == null)
        {
            prop = new XElement("Property", new XAttribute("Name", propertyName));
            props.Add(prop);
        }
        else if (string.Equals(prop.Attribute("Value")?.Value, value, StringComparison.Ordinal))
        {
            return false;
        }

        prop.SetAttributeValue("Value", value);
        return true;
    }

    private static XElement? ReadDataRoot(string folder)
    {
        string path = Path.Combine(folder, "Data.xml");
        if (!File.Exists(path)) return null;

        try
        {
            return XDocument.Load(path).Root;
        }
        catch
        {
            return null;
        }
    }

    private static string FullPathWithSeparator(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full + Path.DirectorySeparatorChar;
    }

    private static string UniqueCorruptJsonPath(string desiredPath)
    {
        if (!File.Exists(desiredPath))
            return desiredPath;

        for (int i = 2; ; i++)
        {
            string candidate = $"{desiredPath}-{i}";
            if (!File.Exists(candidate))
                return candidate;
        }
    }
}
