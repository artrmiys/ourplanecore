using System.Reflection;

namespace OurPlaneCore;

public static class AppVersion
{
    // Single source of truth is <Version> in ourplanecore.csproj; this strips
    // the "+commit" suffix InformationalVersion may carry.
    public static string Display { get; } = ResolveDisplay();

    private static string ResolveDisplay()
    {
        string? informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
            return "2.0.0";

        int plus = informational.IndexOf('+');
        return plus > 0 ? informational[..plus] : informational;
    }
}
