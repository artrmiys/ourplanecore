using System;
using System.IO;

namespace OurPlanCore;

/// <summary>
/// Canonical application identity plus the narrowly-scoped compatibility names
/// needed while upgrading existing installations.
/// </summary>
public static class AppIdentity
{
    public const string ProductName = "OurPlanCore";
    public const string ExecutableName = "ourplancore";
    public const string EnvironmentPrefix = "OURPLANCORE";

    internal const string LegacyProductName = "OurPlaneCore";
    internal const string LegacyExecutableName = "ourplanecore";
    internal const string LegacyEnvironmentPrefix = "OURPLANECORE";

    private static readonly bool IsPreviewBuild = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(AppIdentity).Assembly)?.InformationalVersion.Contains("-preview", StringComparison.OrdinalIgnoreCase) == true;

    private static readonly string PreviewMarkerPath = ResolvePreviewMarkerPath(Environment.ProcessPath, AppContext.BaseDirectory);

    public static bool IsIsolatedPreview => IsPreviewBuild || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OURPLANCORE_PROFILE_ROOT")) || File.Exists(PreviewMarkerPath);

    private static readonly string PreviewProfileName = ReadPreviewProfileName(PreviewMarkerPath);

    private static string PreviewProfileRoot => Environment.GetEnvironmentVariable("OURPLANCORE_PROFILE_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), PreviewProfileName);

    // A separately installed preview may name its own profile in the marker.
    // This keeps a still-running earlier preview from sharing settings and jobs.
    // Existing empty markers and builds without a marker retain their old root.
    internal static string ResolvePreviewMarkerPath(string? processPath, string baseDirectory)
    {
        // A self-extracting bundle's BaseDirectory is its extraction directory.
        // The installation's marker belongs beside the actual executable.
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            try
            {
                if (Path.IsPathFullyQualified(processPath) &&
                    !string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    string? directory = Path.GetDirectoryName(processPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        string marker = Path.Combine(directory, "ourplancore.preview");
                        if (File.Exists(marker)) return marker;
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
        }

        // Framework-dependent launches use the app directory, not the dotnet host.
        return Path.Combine(baseDirectory, "ourplancore.preview");
    }

    internal static string ReadPreviewProfileName(string marker)
    {
        const string fallback = "OurPlanCore Preview";
        try
        {
            if (!File.Exists(marker) || new FileInfo(marker).Length > 512) return fallback;
            string name = File.ReadAllText(marker).Trim();
            return name.StartsWith(fallback + " ", StringComparison.Ordinal) && name.Length <= 100 &&
                name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !Path.IsPathRooted(name)
                ? name : fallback;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return fallback; }
    }

    public static string RoamingRoot => IsIsolatedPreview ? Path.Combine(PreviewProfileRoot, "roaming") : ProductRoamingRoot(ProductName);

    public static string LocalRoot => IsIsolatedPreview ? Path.Combine(PreviewProfileRoot, "local") : ProductLocalRoot(ProductName);

    internal static string LegacyRoamingRoot => ProductRoamingRoot(LegacyProductName);

    internal static string LegacyLocalRoot => ProductLocalRoot(LegacyProductName);

    /// <summary>
    /// Reads the current environment variable first and falls back to its
    /// pre-rename equivalent when the current value is blank.
    /// </summary>
    public static string? GetEnvironmentVariable(string suffixOrCurrentName)
    {
        string suffix = NormalizeEnvironmentVariableSuffix(suffixOrCurrentName);
        string? current = Environment.GetEnvironmentVariable(EnvironmentVariableName(suffix));
        return !string.IsNullOrWhiteSpace(current)
            ? current
            : Environment.GetEnvironmentVariable(LegacyEnvironmentVariableName(suffix));
    }

    /// <summary>
    /// Target-specific overload of <see cref="GetEnvironmentVariable(string)"/>.
    /// </summary>
    public static string? GetEnvironmentVariable(string suffixOrCurrentName, EnvironmentVariableTarget target)
    {
        string suffix = NormalizeEnvironmentVariableSuffix(suffixOrCurrentName);
        string? current = Environment.GetEnvironmentVariable(EnvironmentVariableName(suffix), target);
        return !string.IsNullOrWhiteSpace(current)
            ? current
            : Environment.GetEnvironmentVariable(LegacyEnvironmentVariableName(suffix), target);
    }

    public static string EnvironmentVariableName(string suffixOrCurrentName) =>
        BuildEnvironmentVariableName(EnvironmentPrefix, NormalizeEnvironmentVariableSuffix(suffixOrCurrentName));

    internal static string LegacyEnvironmentVariableName(string suffixOrCurrentName) =>
        BuildEnvironmentVariableName(LegacyEnvironmentPrefix, NormalizeEnvironmentVariableSuffix(suffixOrCurrentName));

    internal static string ProductRoamingRoot(string productName, string? roamingBase = null)
    {
        string root = roamingBase ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, productName);
    }

    internal static string ProductLocalRoot(string productName, string? localBase = null)
    {
        string root = localBase ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, productName);
    }

    private static string BuildEnvironmentVariableName(string prefix, string suffix)
    {
        return $"{prefix}_{suffix.Trim().TrimStart('_').ToUpperInvariant()}";
    }

    private static string NormalizeEnvironmentVariableSuffix(string suffixOrCurrentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suffixOrCurrentName);
        string value = suffixOrCurrentName.Trim().TrimStart('_').ToUpperInvariant();
        string currentPrefix = EnvironmentPrefix + "_";
        string legacyPrefix = LegacyEnvironmentPrefix + "_";
        if (value.StartsWith(currentPrefix, StringComparison.Ordinal))
            value = value[currentPrefix.Length..];
        else if (value.StartsWith(legacyPrefix, StringComparison.Ordinal))
            value = value[legacyPrefix.Length..];

        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
