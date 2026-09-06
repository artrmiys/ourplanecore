using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OurPlanCore;

internal static class SheetOverlayLayerStore
{
    private const string FileName = "sheet_overlays.json";
    private const string LegacyLayerId = "legacy-overlay";
    private const double LegacyVisualOpacity = 1.0;

    public static string ManifestPath(string pageFolder) =>
        Path.Combine(pageFolder, FileName);

    public static bool HasManifest(string pageFolder) =>
        File.Exists(ManifestPath(pageFolder));

    public static SheetOverlayLayerCollection Load(string pageFolder, SourceInfo? legacySource = null)
    {
        string path = ManifestPath(pageFolder);
        DataFileResult<SheetOverlayLayerManifest> loaded = DataFileReader.Read(path, json =>
        {
            var manifest = JsonSerializer.Deserialize<SheetOverlayLayerManifest>(json) ?? throw new JsonException("Empty overlay document.");
            if (manifest.Layers == null || manifest.Layers.Any(layer => layer == null)) throw new JsonException("Invalid overlay layers.");
            foreach (var layer in manifest.Layers)
                if (!string.IsNullOrWhiteSpace(layer.SourcePageFolder))
                    _ = PageReferenceSafety.Resolve(pageFolder, layer.SourcePageFolder);
            return manifest;
        });
        if (loaded.Value != null) return NormalizeManifest(pageFolder, loaded.Value);
        if (loaded.State != DataFileState.Missing) return new SheetOverlayLayerCollection();

        legacySource ??= PageStore.ReadSource(pageFolder);
        return FromLegacy(pageFolder, legacySource);
    }

    public static string Add(
        string pageFolder,
        string sourcePageFolder,
        string color,
        double opacity)
    {
        SheetOverlayLayerCollection current = Load(pageFolder);
        var layers = current.Layers.Select(layer => layer.Clone()).ToList();
        string id = Guid.NewGuid().ToString("N");
        layers.Add(new SheetOverlayLayerInfo
        {
            Id = id,
            SourcePageFolder = NormalizeAbsoluteSource(pageFolder, sourcePageFolder),
            Color = NormalizeColor(color),
            Opacity = NormalizeOpacity(opacity),
            IsVisible = true,
        });
        Save(pageFolder, new SheetOverlayLayerCollection
        {
            ActiveOverlayId = id,
            Layers = layers,
        });
        return id;
    }

    public static string ReplaceActive(
        string pageFolder,
        string sourcePageFolder,
        string color,
        double opacity)
    {
        SheetOverlayLayerCollection current = Load(pageFolder);
        SheetOverlayLayerInfo? active = current.ActiveLayer;
        string normalizedSource = NormalizeAbsoluteSource(pageFolder, sourcePageFolder);
        if (string.IsNullOrWhiteSpace(normalizedSource))
        {
            Remove(pageFolder, active?.Id ?? "");
            return "";
        }

        string id = active?.Id ?? "";
        if (string.IsNullOrWhiteSpace(id))
            id = Guid.NewGuid().ToString("N");
        bool sameSource = active != null &&
                          SameFolder(active.SourcePageFolder, normalizedSource);
        var replacement = new SheetOverlayLayerInfo
        {
            Id = id,
            SourcePageFolder = normalizedSource,
            IsVisible = sameSource ? active!.IsVisible : true,
            Color = NormalizeColor(color),
            Opacity = NormalizeOpacity(opacity),
            OffsetXPt = sameSource ? active!.OffsetXPt : 0,
            OffsetYPt = sameSource ? active!.OffsetYPt : 0,
            Scale = sameSource ? active!.Scale : 1,
            RotationDegrees = sameSource ? active!.RotationDegrees : 0,
        };

        var layers = current.Layers.Select(layer => layer.Clone()).ToList();
        int index = layers.FindIndex(layer =>
            string.Equals(layer.Id, id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            layers[index] = replacement;
        else
            layers.Add(replacement);
        Save(pageFolder, new SheetOverlayLayerCollection
        {
            ActiveOverlayId = id,
            Layers = layers,
        });
        return id;
    }

    public static void SetActive(string pageFolder, string overlayId)
    {
        SheetOverlayLayerCollection current = Load(pageFolder);
        if (!current.Layers.Any(layer =>
                string.Equals(layer.Id, overlayId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Save(pageFolder, new SheetOverlayLayerCollection
        {
            ActiveOverlayId = overlayId,
            Layers = current.Layers,
        });
    }

    public static void SetVisibility(string pageFolder, string overlayId, bool isVisible) =>
        Update(pageFolder, overlayId, layer => Copy(layer, isVisible: isVisible));

    public static void SetColor(string pageFolder, string overlayId, string color) =>
        Update(pageFolder, overlayId, layer => Copy(layer, color: NormalizeColor(color)));

    public static void SetOpacity(string pageFolder, string overlayId, double opacity) =>
        Update(pageFolder, overlayId, layer => Copy(layer, opacity: NormalizeOpacity(opacity)));

    public static void SetTransform(
        string pageFolder,
        string overlayId,
        double offsetXPt,
        double offsetYPt,
        double scale,
        double rotationDegrees) =>
        Update(
            pageFolder,
            overlayId,
            layer => Copy(
                layer,
                offsetXPt: NormalizeOffset(offsetXPt),
                offsetYPt: NormalizeOffset(offsetYPt),
                scale: NormalizeScale(scale),
                rotationDegrees: NormalizeRotation(rotationDegrees)));

    public static void Move(string pageFolder, string overlayId, int delta)
    {
        SheetOverlayLayerCollection current = Load(pageFolder);
        var layers = current.Layers.Select(layer => layer.Clone()).ToList();
        int from = layers.FindIndex(layer =>
            string.Equals(layer.Id, overlayId, StringComparison.OrdinalIgnoreCase));
        int to = Math.Clamp(from + delta, 0, Math.Max(0, layers.Count - 1));
        if (from < 0 || from == to)
            return;

        SheetOverlayLayerInfo layer = layers[from];
        layers.RemoveAt(from);
        layers.Insert(to, layer);
        Save(pageFolder, new SheetOverlayLayerCollection
        {
            ActiveOverlayId = current.ActiveOverlayId,
            Layers = layers,
        });
    }

    public static void Remove(string pageFolder, string overlayId)
    {
        SheetOverlayLayerCollection current = Load(pageFolder);
        var layers = current.Layers
            .Where(layer => !string.Equals(layer.Id, overlayId, StringComparison.OrdinalIgnoreCase))
            .Select(layer => layer.Clone())
            .ToList();
        string activeId = current.ActiveOverlayId;
        if (!layers.Any(layer => string.Equals(layer.Id, activeId, StringComparison.OrdinalIgnoreCase)))
            activeId = layers.LastOrDefault()?.Id ?? "";
        Save(pageFolder, new SheetOverlayLayerCollection
        {
            ActiveOverlayId = activeId,
            Layers = layers,
        });
    }

    public static void SaveSnapshot(string pageFolder, SheetOverlayLayerCollection collection) =>
        Save(pageFolder, collection);

    private static void Update(
        string pageFolder,
        string overlayId,
        Func<SheetOverlayLayerInfo, SheetOverlayLayerInfo> update)
    {
        SheetOverlayLayerCollection current = Load(pageFolder);
        string id = string.IsNullOrWhiteSpace(overlayId)
            ? current.ActiveLayer?.Id ?? ""
            : overlayId;
        bool changed = false;
        var layers = current.Layers.Select(layer =>
        {
            if (!string.Equals(layer.Id, id, StringComparison.OrdinalIgnoreCase))
                return layer.Clone();
            changed = true;
            return update(layer);
        }).ToList();
        if (!changed)
            return;

        Save(pageFolder, new SheetOverlayLayerCollection
        {
            ActiveOverlayId = current.ActiveOverlayId,
            Layers = layers,
        });
    }

    private static void Save(string pageFolder, SheetOverlayLayerCollection collection)
    {
        JobWriteAccess.Demand(pageFolder, "save sheet overlay layers");
        SheetOverlayLayerCollection normalized = NormalizeCollection(pageFolder, collection);
        var manifest = new SheetOverlayLayerManifest
        {
            ActiveOverlayId = normalized.ActiveLayer?.Id ?? "",
            Layers = normalized.Layers.Select(layer => new SheetOverlayLayerEntry
            {
                Id = layer.Id,
                SourcePageFolder = MakeRelativeSource(pageFolder, layer.SourcePageFolder),
                IsVisible = layer.IsVisible,
                Color = layer.Color,
                Opacity = layer.Opacity,
                OffsetXPt = layer.OffsetXPt,
                OffsetYPt = layer.OffsetYPt,
                Scale = layer.Scale,
                RotationDegrees = layer.RotationDegrees,
            }).ToList(),
        };

        string path = ManifestPath(pageFolder);
        JobWriteAccess.Demand(path, "save sheet overlay layers");
        IoUtil.WriteAllTextAtomic(
            path,
            JsonSerializer.Serialize(manifest, OurPlanCoreJobStore.JsonOptions));
        PageStore.WriteLegacyOverlayMirror(pageFolder, normalized.ActiveLayer);
    }

    private static SheetOverlayLayerCollection NormalizeManifest(
        string pageFolder,
        SheetOverlayLayerManifest manifest) =>
        NormalizeCollection(
            pageFolder,
            new SheetOverlayLayerCollection
            {
                ActiveOverlayId = manifest.ActiveOverlayId,
                Layers = manifest.Layers.Select(entry => new SheetOverlayLayerInfo
                {
                    Id = entry.Id,
                    SourcePageFolder = entry.SourcePageFolder,
                    IsVisible = entry.IsVisible,
                    Color = entry.Color,
                    Opacity = entry.Opacity,
                    OffsetXPt = entry.OffsetXPt,
                    OffsetYPt = entry.OffsetYPt,
                    Scale = entry.Scale,
                    RotationDegrees = entry.RotationDegrees,
                }).ToList(),
            });

    private static SheetOverlayLayerCollection NormalizeCollection(
        string pageFolder,
        SheetOverlayLayerCollection collection)
    {
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var layers = new List<SheetOverlayLayerInfo>();
        foreach (SheetOverlayLayerInfo layer in collection.Layers)
        {
            string source = NormalizeAbsoluteSource(pageFolder, layer.SourcePageFolder);
            if (string.IsNullOrWhiteSpace(source))
                continue;
            string id = string.IsNullOrWhiteSpace(layer.Id) || !usedIds.Add(layer.Id)
                ? Guid.NewGuid().ToString("N")
                : layer.Id;
            usedIds.Add(id);
            layers.Add(new SheetOverlayLayerInfo
            {
                Id = id,
                SourcePageFolder = source,
                IsVisible = layer.IsVisible,
                Color = NormalizeColor(layer.Color),
                Opacity = NormalizeOpacity(layer.Opacity),
                OffsetXPt = NormalizeOffset(layer.OffsetXPt),
                OffsetYPt = NormalizeOffset(layer.OffsetYPt),
                Scale = NormalizeScale(layer.Scale),
                RotationDegrees = NormalizeRotation(layer.RotationDegrees),
            });
        }

        string activeId = layers.Any(layer =>
            string.Equals(layer.Id, collection.ActiveOverlayId, StringComparison.OrdinalIgnoreCase))
            ? collection.ActiveOverlayId
            : layers.LastOrDefault()?.Id ?? "";
        return new SheetOverlayLayerCollection
        {
            ActiveOverlayId = activeId,
            Layers = layers,
        };
    }

    private static SheetOverlayLayerCollection FromLegacy(string pageFolder, SourceInfo? source)
    {
        if (source == null || string.IsNullOrWhiteSpace(source.OverlayPageFolder))
            return new SheetOverlayLayerCollection();

        return new SheetOverlayLayerCollection
        {
            ActiveOverlayId = LegacyLayerId,
            Layers =
            [
                new SheetOverlayLayerInfo
                {
                    Id = LegacyLayerId,
                    SourcePageFolder = NormalizeAbsoluteSource(pageFolder, source.OverlayPageFolder),
                    IsVisible = source.OverlayVisible,
                    Color = NormalizeColor(source.OverlayColor),
                    Opacity = LegacyVisualOpacity,
                    OffsetXPt = NormalizeOffset(source.OverlayOffsetXPt),
                    OffsetYPt = NormalizeOffset(source.OverlayOffsetYPt),
                    Scale = NormalizeScale(source.OverlayScale),
                    RotationDegrees = NormalizeRotation(source.OverlayRotationDegrees),
                },
            ],
        };
    }

    private static SheetOverlayLayerInfo Copy(
        SheetOverlayLayerInfo layer,
        bool? isVisible = null,
        string? color = null,
        double? opacity = null,
        double? offsetXPt = null,
        double? offsetYPt = null,
        double? scale = null,
        double? rotationDegrees = null) =>
        new()
        {
            Id = layer.Id,
            SourcePageFolder = layer.SourcePageFolder,
            IsVisible = isVisible ?? layer.IsVisible,
            Color = color ?? layer.Color,
            Opacity = opacity ?? layer.Opacity,
            OffsetXPt = offsetXPt ?? layer.OffsetXPt,
            OffsetYPt = offsetYPt ?? layer.OffsetYPt,
            Scale = scale ?? layer.Scale,
            RotationDegrees = rotationDegrees ?? layer.RotationDegrees,
        };

    private static string NormalizeAbsoluteSource(string pageFolder, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "";
        try
        {
            return string.IsNullOrWhiteSpace(pageFolder) ? Path.GetFullPath(source) : PageReferenceSafety.Resolve(pageFolder, source);
        }
        catch
        {
            return "";
        }
    }

    private static string MakeRelativeSource(string pageFolder, string source)
    {
        try
        {
            return Path.GetRelativePath(pageFolder, NormalizeAbsoluteSource(pageFolder, source));
        }
        catch
        {
            return source;
        }
    }

    private static bool SameFolder(string left, string right) =>
        string.Equals(
            NormalizeAbsoluteSource("", left).TrimEnd(Path.DirectorySeparatorChar),
            NormalizeAbsoluteSource("", right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeColor(string color) =>
        string.IsNullOrWhiteSpace(color) ? "#E53935" : color.Trim();

    private static double NormalizeOpacity(double value) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? 1.0
            : Math.Clamp(value, 0.05, 1.0);

    private static double NormalizeOffset(double value) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? 0
            : Math.Clamp(value, -100000, 100000);

    private static double NormalizeScale(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) || value <= 0
            ? 1
            : Math.Clamp(value, 0.05, 20);

    private static double NormalizeRotation(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;
        double normalized = value % 360;
        if (normalized > 180)
            normalized -= 360;
        if (normalized <= -180)
            normalized += 360;
        return normalized;
    }
}
