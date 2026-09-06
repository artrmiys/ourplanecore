using OurPlanCore;

internal static class AppIdentityPreviewProfileTests
{
    public static void ExecutableMarkerWinsOverExtractedBundleMarker() => InTemporaryDirectory(root =>
    {
        string installed = Marker(root, "installed", "OurPlanCore Preview 2.2.7");
        string extracted = Marker(root, "extracted", "OurPlanCore Preview Older");
        string selected = AppIdentity.ResolvePreviewMarkerPath(
            Path.Combine(Path.GetDirectoryName(installed)!, "ourplancore.exe"), Path.GetDirectoryName(extracted)!);
        Equal(installed, selected, "The installed EXE marker must take precedence over extracted content.");
        Equal("OurPlanCore Preview 2.2.7", AppIdentity.ReadPreviewProfileName(selected),
            "The selected marker must supply the version-specific profile.");
    });

    public static void DotnetHostUsesApplicationMarker() => InTemporaryDirectory(root =>
    {
        string host = Marker(root, "host", "OurPlanCore Preview Wrong Host");
        string app = Marker(root, "app", "OurPlanCore Preview Hosted");
        string selected = AppIdentity.ResolvePreviewMarkerPath(
            Path.Combine(Path.GetDirectoryName(host)!, "DOTNET.EXE"), Path.GetDirectoryName(app)!);
        Equal(app, selected, "A marker beside the shared dotnet host must not choose the app profile.");
        Equal("OurPlanCore Preview Hosted", AppIdentity.ReadPreviewProfileName(selected), "Hosted profile name.");
    });

    public static void MissingOrUnavailableProcessMarkerUsesBaseDirectory() => InTemporaryDirectory(root =>
    {
        string marker = Marker(root, "app", "OurPlanCore Preview Fallback");
        string directory = Path.GetDirectoryName(marker)!;
        foreach (string? processPath in new[] { Path.Combine(root, "unmarked", "ourplancore.exe"), null, "", "ourplancore.exe" })
            Equal(marker, AppIdentity.ResolvePreviewMarkerPath(processPath, directory), "Safe app-directory fallback.");
        string missing = AppIdentity.ResolvePreviewMarkerPath(null, Path.Combine(root, "empty"));
        Equal("OurPlanCore Preview", AppIdentity.ReadPreviewProfileName(missing), "Missing marker retains the existing default.");
    });

    public static void InvalidOrEmptyMarkerRetainsExistingDefault() => InTemporaryDirectory(root =>
    {
        string marker = Marker(root, "installed", "");
        foreach (string content in new[] { "", "  ", "Unrelated profile", "OurPlanCore Preview ../other", "OurPlanCore Preview " + new string('x', 600) })
        {
            File.WriteAllText(marker, content);
            Equal("OurPlanCore Preview", AppIdentity.ReadPreviewProfileName(marker), "Existing marker validation must remain unchanged.");
        }
        File.WriteAllText(marker, "  OurPlanCore Preview 2.2.7  ");
        Equal("OurPlanCore Preview 2.2.7", AppIdentity.ReadPreviewProfileName(marker), "Valid marker trimming.");
    });

    private static string Marker(string root, string directoryName, string content)
    {
        string directory = Path.Combine(root, directoryName);
        Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, "ourplancore.preview");
        File.WriteAllText(marker, content);
        return marker;
    }

    private static void InTemporaryDirectory(Action<string> test)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("opc-profile-marker-");
        try { test(directory.FullName); }
        finally { directory.Delete(recursive: true); }
    }

    private static void Equal(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }
}
