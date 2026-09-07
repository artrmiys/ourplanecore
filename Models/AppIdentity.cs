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

    public static string RoamingRoot => ProductRoamingRoot(ProductName);

    public static string LocalRoot => ProductLocalRoot(ProductName);

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
