internal static class GuideScreenshotCaptureRegressionTests
{
    public static void CaptureUsesIsolatedSettingsFile()
    {
        string script = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "Tools",
            "capture_guide_screenshots.ps1"));

        int saveIndex = script.IndexOf(
            "$oldSettingsPath = $env:OURPLANCORE_SETTINGS_PATH",
            StringComparison.Ordinal);
        int isolateIndex = script.IndexOf(
            "$env:OURPLANCORE_SETTINGS_PATH = $captureSettingsPath",
            StringComparison.Ordinal);
        int launchIndex = script.IndexOf(
            "Start-Process -FilePath \"dotnet\"",
            StringComparison.Ordinal);
        int restoreIndex = script.LastIndexOf(
            "$env:OURPLANCORE_SETTINGS_PATH = $oldSettingsPath",
            StringComparison.Ordinal);

        AssertTrue(
            script.Contains(
                "$captureSettingsPath = Join-Path $captureDir \"settings.json\"",
                StringComparison.Ordinal),
            "guide capture must keep its settings inside the temporary capture directory");
        AssertTrue(
            saveIndex >= 0 &&
            isolateIndex > saveIndex &&
            launchIndex > isolateIndex &&
            restoreIndex > launchIndex,
            "guide capture must isolate settings before app launch and restore the caller environment in finally");
    }

    private static string RepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ourplancore.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find ourplancore repo root.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
