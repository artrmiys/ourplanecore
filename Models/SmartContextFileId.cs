using System.IO;

namespace OurPlanCore;

internal static class SmartContextFileId
{
    public static string Require(string value, string label)
    {
        string clean = value?.Trim() ?? "";
        if (clean.Length is < 1 or > 128 ||
            clean.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            throw new OurPlanPackageValidationException(
                $"Invalid {label}. Only 1-128 ASCII letters, digits, '_' and '-' are allowed.");
        }
        return clean;
    }

    public static bool IsValid(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

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
        return path;
    }
}
