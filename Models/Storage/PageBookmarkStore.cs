using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OurPlaneCore;

internal static class PageBookmarkStore
{
    public static List<PageBookmark> LoadPageBookmarks(OurPlaneCoreJob job)
    {
        string path = PageBookmarksJsonPath(job);
        if (!File.Exists(path))
            return [];

        try
        {
            var dtos = JsonSerializer.Deserialize<List<PageBookmarkDto>>(
                File.ReadAllText(path),
                OurPlaneCoreJobStore.JsonOptions) ?? [];
            return dtos.Select(dto => ToBookmark(job, dto))
                .Where(bookmark => !string.IsNullOrWhiteSpace(bookmark.PageFolder))
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            OurPlaneCoreJobStore.QuarantineCorruptJson(path, "LoadPageBookmarks", ex);
            return [];
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"LoadPageBookmarks failed for {path}");
            return [];
        }
    }

    public static void SavePageBookmarks(OurPlaneCoreJob job, IEnumerable<PageBookmark> bookmarks)
    {
        var dtos = bookmarks
            .Where(bookmark => !string.IsNullOrWhiteSpace(bookmark.PageFolder))
            .Select(bookmark => ToDto(job, bookmark))
            .ToList();

        try
        {
            IoUtil.WriteAllTextAtomic(
                PageBookmarksJsonPath(job),
                JsonSerializer.Serialize(dtos, OurPlaneCoreJobStore.JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(PageBookmarksJsonPath(job))}': {ex.Message}", ex);
        }
    }

    public static string PageBookmarksJsonPath(OurPlaneCoreJob job) =>
        Path.Combine(job.RootPath, "bookmarks.json");

    private static PageBookmark ToBookmark(OurPlaneCoreJob job, PageBookmarkDto dto)
    {
        string pageFolder = ResolveJobPath(job, dto.PageFolder);
        PageInfo? page = string.IsNullOrWhiteSpace(pageFolder)
            ? null
            : OurPlaneCoreJobStore.TryReadPage(pageFolder);
        string pageName = string.IsNullOrWhiteSpace(dto.PageName)
            ? page?.Name ?? ""
            : dto.PageName.Trim();

        return new PageBookmark
        {
            Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString("N") : dto.Id.Trim(),
            Name = string.IsNullOrWhiteSpace(dto.Name) ? DefaultBookmarkName(pageName) : dto.Name.Trim(),
            PageFolder = pageFolder,
            PageName = pageName,
            Type = NormalizeBookmarkType(dto.Type),
            Zoom = dto.Zoom > 0 ? dto.Zoom : 1f,
            PanX = dto.PanX,
            PanY = dto.PanY,
            CropImagePath = ResolveJobPath(job, dto.CropImagePath),
            CropLeft = dto.CropLeft,
            CropTop = dto.CropTop,
            CropRight = dto.CropRight,
            CropBottom = dto.CropBottom,
            CreatedAtUtc = string.IsNullOrWhiteSpace(dto.CreatedAtUtc) ? DateTime.UtcNow.ToString("O") : dto.CreatedAtUtc.Trim(),
            UpdatedAtUtc = string.IsNullOrWhiteSpace(dto.UpdatedAtUtc) ? DateTime.UtcNow.ToString("O") : dto.UpdatedAtUtc.Trim(),
        };
    }

    private static PageBookmarkDto ToDto(OurPlaneCoreJob job, PageBookmark bookmark) =>
        new()
        {
            Id = string.IsNullOrWhiteSpace(bookmark.Id) ? Guid.NewGuid().ToString("N") : bookmark.Id.Trim(),
            Name = bookmark.Name?.Trim() ?? "",
            PageFolder = JobRelativePath(job, bookmark.PageFolder),
            PageName = bookmark.PageName?.Trim() ?? "",
            Type = NormalizeBookmarkType(bookmark.Type),
            Zoom = bookmark.Zoom > 0 ? bookmark.Zoom : 1f,
            PanX = bookmark.PanX,
            PanY = bookmark.PanY,
            CropImagePath = JobRelativePath(job, bookmark.CropImagePath),
            CropLeft = bookmark.CropLeft,
            CropTop = bookmark.CropTop,
            CropRight = bookmark.CropRight,
            CropBottom = bookmark.CropBottom,
            CreatedAtUtc = bookmark.CreatedAtUtc?.Trim() ?? "",
            UpdatedAtUtc = bookmark.UpdatedAtUtc?.Trim() ?? "",
        };

    private static string DefaultBookmarkName(string pageName) =>
        string.IsNullOrWhiteSpace(pageName) ? "Bookmark" : $"{pageName} view";

    private static string NormalizeBookmarkType(string type)
    {
        string clean = (type ?? "").Trim();
        return clean.Equals("crop_image", StringComparison.OrdinalIgnoreCase)
            ? "crop_image"
            : "view";
    }

    private static string ResolveJobPath(OurPlaneCoreJob job, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        string clean = path.Trim();
        try
        {
            if (Path.IsPathRooted(clean))
                return Path.GetFullPath(clean);

            return Path.GetFullPath(Path.Combine(
                job.RootPath,
                clean.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch
        {
            return "";
        }
    }

    private static string JobRelativePath(OurPlaneCoreJob job, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            string full = Path.GetFullPath(path);
            if (OurPlaneCoreJobStore.IsSameOrDescendant(job.RootPath, full))
                return Path.GetRelativePath(job.RootPath, full).Replace('\\', '/');
        }
        catch
        {
            // Fall through to original path.
        }

        return path.Trim();
    }
}
