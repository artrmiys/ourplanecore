using OurPlanCore;

internal static class BeamAnnotationConfigTests
{
    public static void DefaultsStayOffAndRedUntilEnabled()
    {
        BeamAnnotationConfig config = BeamAnnotationConfig.BuildDefault();
        AssertFalse(config.KeepLineAnnotation, "built-in Beam line must be opt-in");
        AssertEqual("#FF0000", config.LineColor, "built-in Beam line color");
        AssertTrue(
            BeamAnnotationConfig.TryNormalizeColor("ff3366", out string normalized) &&
            normalized == "#FF3366",
            "Beam line color accepts normalized hex");
        AssertFalse(
            BeamAnnotationConfig.TryNormalizeColor("red", out _),
            "named colors are rejected so persisted settings remain deterministic");

        BeamAnnotationConfigProvider.Install(new BeamAnnotationConfig
        {
            KeepLineAnnotation = true,
            LineColor = "#123abc",
        });
        BeamAnnotationConfig installed = BeamAnnotationConfigProvider.Current;
        AssertTrue(installed.KeepLineAnnotation, "installed Beam line setting");
        AssertEqual("#123ABC", installed.LineColor, "installed Beam line color");
    }

    public static void BeamDialogAndSettingsWireTheCompanionLine()
    {
        string beam = ReadRepoFile("MainWindow.BeamTool.cs");
        string viewport = ReadRepoFile(Path.Combine("Controls", "PdfViewport.Beam.cs"));
        string dialog = ReadRepoFile(Path.Combine("Dialogs", "NewItemDialog.cs"));
        string settings = ReadRepoFile("MainWindow.SettingsManager.BeamAnnotation.cs");
        string manager = ReadRepoFile("MainWindow.SettingsManager.cs");
        string picker = ReadRepoFile(Path.Combine("Controls", "ColorSwatchPicker.cs"));
        string toolControls = ReadRepoFile("MainWindow.ToolControls.cs");

        AssertContainsAll(
            beam,
            "showBeamAnnotationOption: true",
            "defaultKeepBeamAnnotationLine: _beamAnnotationConfig.KeepLineAnnotation",
            "viewport.AddBeamAnnotationLine(");
        AssertContainsAll(
            viewport,
            "Kind = \"line\"",
            "BeamAnnotationConfig.NormalizeColor(color)",
            "PageAnnotationAdded?.Invoke(annotation)");
        AssertContainsAll(
            dialog,
            "KeepBeamAnnotationLine",
            "BeamAnnotationLineColor",
            "The existing blue dimension stays",
            "ColorSwatchPicker");
        AssertContainsAll(
            settings,
            "Save global default",
            "Save as this job",
            "Clear job override",
            "Reset",
            "ColorSwatchPicker");
        AssertFalse(
            dialog.Contains("#RRGGBB", StringComparison.Ordinal) ||
            settings.Contains("Line color (hex)", StringComparison.Ordinal),
            "Beam color UI must use visual swatches instead of visible hex input");
        AssertContainsAll(
            picker,
            "AnnotationColorPalette.Presets",
            "Saved color",
            "SelectedColorChanged");
        AssertContainsAll(
            toolControls,
            "AnnotationColorPalette.Presets");
        AssertContainsAll(
            manager,
            "SettingsPresetStore.InstallBeamAnnotationProvider(_currentJob)",
            "SettingsPresetStore.ResolveBeamAnnotation(_currentJob)",
            "AppendBeamAnnotationSettings(root)");
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

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

    private static void AssertContainsAll(string text, params string[] values)
    {
        foreach (string value in values)
            AssertTrue(text.Contains(value, StringComparison.Ordinal), $"Expected source marker '{value}'.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: expected '{expected}', actual '{actual}'.");
    }
}
