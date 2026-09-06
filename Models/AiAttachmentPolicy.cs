using System.IO;
using System.Text;

namespace OurPlanCore;

internal static class AiAttachmentPolicy
{
    internal const long MaxTextBytes = 2 * 1024 * 1024;
    internal const long MaxImageBytes = 25 * 1024 * 1024;
    private static readonly string[] Images = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff"];

    public static IReadOnlyList<string> Validate(OurPlanCoreJob job, SmartAiRequest request)
    {
        SmartContextFileId.Require(request.Id, "AI request ID");
        if (Encoding.UTF8.GetByteCount(System.Text.Json.JsonSerializer.Serialize(request)) > 1024 * 1024)
            throw new InvalidDataException("AI request text exceeds 1 MB.");
        var files = new List<string>();
        IEnumerable<string> crops = new[] { request.CropPath }.Concat(request.ContextCropPaths ?? []);
        foreach (string value in crops.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (files.Count >= 16) throw new InvalidDataException("An AI request can attach at most 16 images.");
            string path = SafeJobPathResolver.ResolveRelative(job.AIContextRoot, value);
            files.Add(SafeJobPathResolver.RequireFile(job.AIContextRoot, path, job.AIContextRoot, MaxImageBytes, Images));
        }
        if (files.Sum(path => new FileInfo(path).Length) > 64L * 1024 * 1024)
            throw new InvalidDataException("AI image attachments exceed the 64 MB request limit.");
        if (!string.IsNullOrWhiteSpace(request.LayerManifestPath))
        {
            string path = SafeJobPathResolver.ResolveRelative(job.RootPath, request.LayerManifestPath);
            if (!Path.GetFileName(path).Equals("layers.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The layer attachment must be a project layers.json file.");
            files.Add(SafeJobPathResolver.RequireFile(job.RootPath, path, job.RootPath, MaxTextBytes, ".json"));
        }
        if (string.Equals(request.Type, "roof_recognition_request", StringComparison.OrdinalIgnoreCase))
            AddContext(SmartMassingDraftService.ModelPath(job));
        if (new[] { "roof_recognition_request", "find_similar_marker_request", "crop_bookmark_request" }
            .Contains(request.Type, StringComparer.OrdinalIgnoreCase))
            AddContext(Path.Combine(job.AIContextRoot, "exports", "markers_context.json"));
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        void AddContext(string path)
        {
            path = SafeJobPathResolver.ResolveInside(job.RootPath, path, job.RootPath);
            if (File.Exists(path))
                files.Add(SafeJobPathResolver.RequireFile(job.RootPath, path, job.RootPath, MaxTextBytes, ".json"));
        }
    }

    public static string ReadText(OurPlanCoreJob job, string path)
    {
        path = SafeJobPathResolver.RequireFile(job.RootPath, path, job.RootPath, MaxTextBytes, ".json", ".jsonl");
        if (new FileInfo(path).Length > MaxTextBytes)
            throw new InvalidDataException("AI text context exceeds 2 MB.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > MaxTextBytes) throw new InvalidDataException("AI text context exceeds 2 MB.");
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
