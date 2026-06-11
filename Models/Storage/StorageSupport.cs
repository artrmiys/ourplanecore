using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace OurPlaneCore;

internal static class StorageSupport
{
    private static readonly object CorruptJsonLock = new();
    private static readonly List<string> CorruptJsonFiles = [];

    // ── Data.xml metadata cache ───────────────────────────────────────────────
    // Every node (page / takeoff / folder) stores its metadata in a per-folder
    // Data.xml. Without a cache, each ReadProperty/DisplayName/SetProperty call
    // re-parses the whole file from disk — sorting one folder of 100 children
    // costs ~200 XML parses, and one takeoff autosave costs ~22 load+save
    // round-trips. The cache keeps the parsed XDocument in memory and reloads
    // only when the file's last-write time changes (so on-disk moves/copies and
    // the GUID-regenerator are still picked up).
    private sealed class DataXmlCacheEntry
    {
        public required XDocument Doc;
        public required long WriteTicks;
    }

    private static readonly object DataXmlCacheLock = new();

    private static readonly Dictionary<string, DataXmlCacheEntry> DataXmlCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static long GetWriteTicks(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Returns the cached <see cref="XDocument"/> for a folder's Data.xml,
    /// reloading from disk only if the file changed. The returned instance is
    /// the live cached one — callers that mutate it MUST call
    /// <see cref="StoreCachedDoc"/> after saving.
    /// </summary>
    private static XDocument? GetCachedDoc(string folder)
    {
        string path = Path.Combine(folder, "Data.xml");
        long ticks = GetWriteTicks(path);
        if (ticks < 0)
        {
            lock (DataXmlCacheLock)
                DataXmlCache.Remove(folder);
            return null;
        }

        lock (DataXmlCacheLock)
        {
            if (DataXmlCache.TryGetValue(folder, out DataXmlCacheEntry? entry) &&
                entry.WriteTicks == ticks)
            {
                return entry.Doc;
            }
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(path);
        }
        catch
        {
            lock (DataXmlCacheLock)
                DataXmlCache.Remove(folder);
            return null;
        }

        lock (DataXmlCacheLock)
            DataXmlCache[folder] = new DataXmlCacheEntry { Doc = doc, WriteTicks = ticks };
        return doc;
    }

    private static void StoreCachedDoc(string folder, XDocument doc)
    {
        string path = Path.Combine(folder, "Data.xml");
        lock (DataXmlCacheLock)
            DataXmlCache[folder] = new DataXmlCacheEntry { Doc = doc, WriteTicks = GetWriteTicks(path) };
    }

    public static void InvalidateDataXmlCache(string folder)
    {
        lock (DataXmlCacheLock)
            DataXmlCache.Remove(folder);
    }

    public static void ClearDataXmlCache()
    {
        lock (DataXmlCacheLock)
            DataXmlCache.Clear();
    }

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
        SaveDataXmlAtomic(folder, doc);
        StoreCachedDoc(folder, doc);
    }

    // Data.xml holds the whole node tree's metadata; a torn write here corrupts
    // the job structure, so it must go through the same atomic temp+replace
    // path as measurements.json.
    internal static void SaveDataXmlAtomic(string folder, XDocument doc)
    {
        using var writer = new Utf8StringWriter();
        doc.Save(writer);
        IoUtil.WriteAllTextAtomic(Path.Combine(folder, "Data.xml"), writer.ToString());
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

    public static void UpdateItemName(string folder, string name)
    {
        XDocument? doc = GetCachedDoc(folder);
        XElement? root = doc?.Root;
        if (doc == null || root == null) return;

        bool changed = false;
        if (!string.Equals(root.Attribute("Name")?.Value, name, StringComparison.Ordinal))
        {
            root.SetAttributeValue("Name", name);
            changed = true;
        }

        changed = SetProperty(root, "Name", name) || changed;
        if (changed)
            SaveCachedDoc(folder, doc);
    }

    private static void SaveCachedDoc(string folder, XDocument doc)
    {
        SaveDataXmlAtomic(folder, doc);
        StoreCachedDoc(folder, doc);
    }

    public static void SetProperty(string folder, string propertyName, string value)
    {
        XDocument? doc = GetCachedDoc(folder);
        XElement? root = doc?.Root;
        if (doc == null || root == null) return;

        if (SetProperty(root, propertyName, value))
            SaveCachedDoc(folder, doc);
    }

    /// <summary>
    /// Applies several properties in a single Data.xml load + save. Replaces the
    /// pattern of calling <see cref="SetProperty(string,string,string)"/> ~20
    /// times in a row (which used to be ~20 full file round-trips per autosave).
    /// </summary>
    public static void SetProperties(string folder, IEnumerable<KeyValuePair<string, string>> properties)
    {
        XDocument? doc = GetCachedDoc(folder);
        XElement? root = doc?.Root;
        if (doc == null || root == null) return;

        bool changed = false;
        foreach (KeyValuePair<string, string> property in properties)
            changed = SetProperty(root, property.Key, property.Value) || changed;

        if (changed)
            SaveCachedDoc(folder, doc);
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

    private static XElement? ReadDataRoot(string folder) =>
        GetCachedDoc(folder)?.Root;

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
