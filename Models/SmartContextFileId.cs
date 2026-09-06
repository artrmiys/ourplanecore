using System.IO;

namespace OurPlanCore;

internal static class SmartContextFileId
{
    public static string Require(string value, string label)
    {
        string clean = value?.Trim() ?? "";
        if (!string.Equals(clean, value, StringComparison.Ordinal) || !SafeJobPathResolver.IsSafeId(clean))
        {
            throw new OurPlanPackageValidationException(
                $"Invalid {label}. Only 1-128 ASCII letters, digits, '_' and '-' are allowed.");
        }
        return clean;
    }

    public static bool IsValid(string value) =>
        SafeJobPathResolver.IsSafeId(value);

    public static string JsonPath(
        OurPlanCoreJob job,
        string folderName,
        string id,
        string label) =>
        FilePath(job, folderName, id, ".json", label);

    public static string FilePath(
        OurPlanCoreJob job,
        string folderName,
        string id,
        string suffix,
        string label)
    {
        string cleanId = Require(id, label);
        string folder = Path.GetFullPath(Path.Combine(job.AIContextRoot, folderName));
        string path = Path.GetFullPath(Path.Combine(folder, cleanId + suffix));
        if (!Path.GetDirectoryName(path)!.Equals(folder, StringComparison.OrdinalIgnoreCase))
            throw new OurPlanPackageValidationException($"Invalid {label} path.");
        return SafeJobPathResolver.ResolveInside(job.RootPath, path, job.AIContextRoot);
    }
}
