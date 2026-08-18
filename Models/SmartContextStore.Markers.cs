using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OurPlanCore;

public static partial class SmartContextStore
{
    public static SmartAiMarker SaveAiMarker(
        OurPlanCoreJob job,
        PageInfo page,
        SmartObservation observation,
        string markerType,
        string sampleKind,
        string value,
        string note,
        string cropPath,
        float pdfX,
        float pdfY,
        float cropLeft,
        float cropTop,
        float cropRight,
        float cropBottom)
    {
        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        string now = DateTime.UtcNow.ToString("O");
        string pageFolder = Path.GetRelativePath(job.RootPath, page.FolderPath);
        List<PdfLayerInfo> layers = PageLayers(page);

        var marker = new SmartAiMarker
        {
            Id = observation.Id,
            ObservationId = observation.Id,
            ProjectId = context.ProjectId,
            Page = page.Name,
            PageFolder = pageFolder,
            Type = markerType.Trim(),
            SampleKind = string.IsNullOrWhiteSpace(sampleKind) ? "positive" : sampleKind.Trim(),
            Value = value.Trim(),
            Note = note.Trim(),
            CropPath = cropPath.Trim(),
            PdfPoint = new SmartAiMarkerPoint { X = pdfX, Y = pdfY },
            PdfRect = new SmartAiMarkerRect
            {
                Left = cropLeft,
                Top = cropTop,
                Right = cropRight,
                Bottom = cropBottom,
            },
            LayerCount = layers.Count,
            Layers = layers,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        try
        {
            IoUtil.WriteAllTextAtomic(AiMarkerPath(job, marker.Id), JsonSerializer.Serialize(marker, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(AiMarkerPath(job, marker.Id))}': {ex.Message}", ex);
        }
        return marker;
    }

    private static List<PdfLayerInfo> PageLayers(PageInfo page)
    {
        PageLayerManifest? manifest = OurPlanCoreJobStore.ReadPageLayerManifest(page.FolderPath);
        IEnumerable<PdfLayerInfo> layerSource = manifest?.Layers ?? page.PdfLayers;
        return layerSource
            .OrderBy(layer => layer.Number)
            .Select(layer => new PdfLayerInfo
            {
                Number = layer.Number,
                Name = layer.Name,
                IsOn = layer.IsOn,
            })
            .ToList();
    }

    public static SmartAiMarker? LoadAiMarker(OurPlanCoreJob job, string markerId)
    {
        if (string.IsNullOrWhiteSpace(markerId))
            return null;

        return LoadJson<SmartAiMarker>(AiMarkerPath(job, markerId));
    }

    public static void SaveAiMarker(OurPlanCoreJob job, SmartAiMarker marker)
    {
        if (string.IsNullOrWhiteSpace(marker.Id))
            throw new InvalidOperationException("AI marker id is required.");

        string path = AiMarkerPath(job, marker.Id);
        JobWriteAccess.Demand(path, "save AI marker");
        marker.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ContextRoot(job.RootPath));
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(marker, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    public static bool DeleteAiMarker(OurPlanCoreJob job, string markerId)
    {
        if (string.IsNullOrWhiteSpace(markerId))
            return false;

        string path = AiMarkerPath(job, markerId);
        if (!File.Exists(path))
            return false;

        JobWriteAccess.Demand(path, "delete AI marker");

        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to delete '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    public static IReadOnlyList<SmartAiMarker> LoadAiMarkers(OurPlanCoreJob job)
    {
        string markersDir = Path.Combine(ContextRoot(job.RootPath), "markers");
        if (!Directory.Exists(markersDir))
            return [];

        return Directory.EnumerateFiles(markersDir, "*.json")
            .Select(LoadJson<SmartAiMarker>)
            .Where(marker => marker != null)
            .Select(marker => marker!)
            .OrderBy(marker => marker.CreatedAtUtc)
            .ToList();
    }

    public static string AiMarkerPath(OurPlanCoreJob job, string markerId) =>
        SmartContextFileId.JsonPath(job, "markers", markerId, "AI marker id");

    public static SmartAiMarkerSet SaveAiMarkerSet(
        OurPlanCoreJob job,
        string name,
        string description,
        string typeFilter,
        string sampleKindFilter,
        IReadOnlyList<SmartAiMarker> markers)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Marker set name is required.", nameof(name));

        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        string now = DateTime.UtcNow.ToString("O");
        var markerIds = markers
            .Select(marker => marker.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var set = new SmartAiMarkerSet
        {
            Id = $"set_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}",
            ProjectId = context.ProjectId,
            Name = name.Trim(),
            Description = description.Trim(),
            TypeFilter = typeFilter.Trim(),
            SampleKindFilter = sampleKindFilter.Trim(),
            MarkerIds = markerIds,
            MarkerCount = markerIds.Count,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        string path = AiMarkerSetPath(job, set.Id);
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(set, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }

        return set;
    }

    public static void SaveAiMarkerSet(OurPlanCoreJob job, SmartAiMarkerSet set)
    {
        if (string.IsNullOrWhiteSpace(set.Id))
            throw new ArgumentException("Marker set id is required.", nameof(set));
        if (string.IsNullOrWhiteSpace(set.Name))
            throw new ArgumentException("Marker set name is required.", nameof(set));

        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        set.SchemaVersion = 1;
        set.ProjectId = context.ProjectId;
        set.Name = set.Name.Trim();
        set.Description = set.Description.Trim();
        set.TypeFilter = set.TypeFilter.Trim();
        set.SampleKindFilter = set.SampleKindFilter.Trim();
        set.MarkerIds = set.MarkerIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        set.MarkerCount = set.MarkerIds.Count;
        if (string.IsNullOrWhiteSpace(set.CreatedAtUtc))
            set.CreatedAtUtc = DateTime.UtcNow.ToString("O");
        set.UpdatedAtUtc = DateTime.UtcNow.ToString("O");

        string path = AiMarkerSetPath(job, set.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ContextRoot(job.RootPath));
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(set, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    public static IReadOnlyList<SmartAiMarkerSet> LoadAiMarkerSets(OurPlanCoreJob job)
    {
        string setsDir = Path.Combine(ContextRoot(job.RootPath), "marker_sets");
        if (!Directory.Exists(setsDir))
            return [];

        return Directory.EnumerateFiles(setsDir, "*.json")
            .Select(LoadJson<SmartAiMarkerSet>)
            .Where(set => set != null)
            .Select(set => set!)
            .OrderBy(set => set.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(set => set.CreatedAtUtc)
            .ToList();
    }

    public static string AiMarkerSetPath(OurPlanCoreJob job, string markerSetId) =>
        SmartContextFileId.JsonPath(job, "marker_sets", markerSetId, "AI marker set id");

    public static bool DeleteAiMarkerSet(OurPlanCoreJob job, string markerSetId)
    {
        if (string.IsNullOrWhiteSpace(markerSetId))
            return false;

        string path = AiMarkerSetPath(job, markerSetId);
        if (!File.Exists(path))
            return false;

        JobWriteAccess.Demand(path, "delete AI marker set");

        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to delete '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    public static string ExportAiMarkersContext(
        OurPlanCoreJob job,
        IReadOnlyList<SmartAiMarker> markers,
        IReadOnlyList<string> hiddenMarkerTypes,
        string typeFilter,
        string sampleKindFilter)
    {
        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        var orderedMarkers = markers
            .OrderBy(marker => marker.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.SampleKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.Page, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.CreatedAtUtc)
            .ToList();
        var feedback = LoadRelevantMarkerFeedback(job, orderedMarkers);

        var export = new SmartAiMarkersContextExport
        {
            ProjectId = context.ProjectId,
            ProjectName = context.ProjectName,
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            TypeFilter = typeFilter.Trim(),
            SampleKindFilter = sampleKindFilter.Trim(),
            HiddenMarkerTypes = hiddenMarkerTypes
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MarkerCount = orderedMarkers.Count,
            Markers = orderedMarkers,
            MarkerSets = LoadAiMarkerSets(job).ToList(),
            MarkerFeedback = feedback,
            MarkerQuality = BuildMarkerQualitySummaries(orderedMarkers, feedback),
        };

        string path = AiMarkersContextExportPath(job);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ContextRoot(job.RootPath));
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(export, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }

        return path;
    }

    private static List<SmartMarkerFeedbackRecord> LoadRelevantMarkerFeedback(
        OurPlanCoreJob job,
        IReadOnlyList<SmartAiMarker> markers)
    {
        if (markers.Count == 0)
            return [];

        var markerIds = markers
            .Select(marker => marker.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var markerTypes = markers
            .Select(marker => marker.Type)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return SmartLearningStore.LoadProjectMarkerFeedback(job)
            .Where(record =>
                markerIds.Contains(record.SourceMarkerId) ||
                markerTypes.Contains(record.SourceMarkerType))
            .OrderByDescending(record => record.CreatedAtUtc)
            .Take(200)
            .ToList();
    }

    private static List<SmartMarkerQualitySummary> BuildMarkerQualitySummaries(
        IReadOnlyList<SmartAiMarker> markers,
        IReadOnlyList<SmartMarkerFeedbackRecord> feedback)
    {
        var markerCounts = markers
            .GroupBy(marker => MarkerQualityKey(marker.Type, marker.SampleKind), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return feedback
            .GroupBy(record => MarkerQualityKey(record.SourceMarkerType, record.SourceMarkerSampleKind), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                SmartMarkerFeedbackRecord first = group.First();
                string type = first.SourceMarkerType.Trim();
                string sampleKind = first.SourceMarkerSampleKind.Trim();
                var confidence = group
                    .Where(record => record.Confidence > 0)
                    .Select(record => record.Confidence)
                    .ToList();
                return new SmartMarkerQualitySummary
                {
                    MarkerType = type,
                    SampleKind = sampleKind,
                    MarkerCount = markerCounts.TryGetValue(group.Key, out int markerCount) ? markerCount : 0,
                    FeedbackCount = group.Count(),
                    AcceptedCount = group.Count(record => string.Equals(record.Outcome, "accepted", StringComparison.OrdinalIgnoreCase)),
                    RejectedCount = group.Count(record => string.Equals(record.Outcome, "rejected", StringComparison.OrdinalIgnoreCase)),
                    AppliedCount = group.Count(record => record.Applied),
                    AverageConfidence = confidence.Count == 0 ? 0 : confidence.Average(),
                    LastFeedbackAtUtc = group
                        .Select(record => record.CreatedAtUtc)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .OrderByDescending(value => value, StringComparer.Ordinal)
                        .FirstOrDefault() ?? "",
                };
            })
            .OrderBy(summary => summary.MarkerType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.SampleKind, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string MarkerQualityKey(string type, string sampleKind) =>
        $"{type.Trim()}|{sampleKind.Trim()}";

    public static string AiMarkersContextExportPath(OurPlanCoreJob job) =>
        Path.Combine(ContextRoot(job.RootPath), "exports", "markers_context.json");

    public static SmartAiCropBookmark SaveCropBookmark(OurPlanCoreJob job, SmartAiCropBookmark bookmark)
    {
        if (string.IsNullOrWhiteSpace(bookmark.CropPath))
            throw new InvalidOperationException("Crop bookmark path is required.");

        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        string now = DateTime.UtcNow.ToString("O");
        if (string.IsNullOrWhiteSpace(bookmark.Id))
        {
            bookmark.Id = $"bookmark_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
            bookmark.CreatedAtUtc = now;
        }

        bookmark.ProjectId = context.ProjectId;
        bookmark.Status = string.IsNullOrWhiteSpace(bookmark.Status) ? "new" : bookmark.Status.Trim();
        bookmark.Type = string.IsNullOrWhiteSpace(bookmark.Type) ? "crop_bookmark" : bookmark.Type.Trim();
        bookmark.UpdatedAtUtc = now;

        string path = CropBookmarkPath(job, bookmark.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ContextRoot(job.RootPath));
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(bookmark, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }

        return bookmark;
    }

    public static SmartAiCropBookmark? LoadCropBookmark(OurPlanCoreJob job, string bookmarkId)
    {
        if (string.IsNullOrWhiteSpace(bookmarkId))
            return null;

        return LoadJson<SmartAiCropBookmark>(CropBookmarkPath(job, bookmarkId));
    }

    public static IReadOnlyList<SmartAiCropBookmark> LoadCropBookmarks(OurPlanCoreJob job)
    {
        string bookmarksDir = Path.Combine(ContextRoot(job.RootPath), "crop_bookmarks");
        if (!Directory.Exists(bookmarksDir))
            return [];

        return Directory.EnumerateFiles(bookmarksDir, "*.json")
            .Select(LoadJson<SmartAiCropBookmark>)
            .Where(bookmark => bookmark != null)
            .Select(bookmark => bookmark!)
            .OrderBy(bookmark => bookmark.CreatedAtUtc)
            .ToList();
    }

    public static SmartAiCropBookmark? FindCropBookmarkByObservation(OurPlanCoreJob job, string observationId)
    {
        if (string.IsNullOrWhiteSpace(observationId))
            return null;

        return LoadCropBookmarks(job).FirstOrDefault(bookmark =>
            string.Equals(bookmark.SourceObservationId, observationId, StringComparison.OrdinalIgnoreCase));
    }

    public static string CropBookmarkPath(OurPlanCoreJob job, string bookmarkId) =>
        SmartContextFileId.JsonPath(job, "crop_bookmarks", bookmarkId, "AI crop bookmark id");
}
