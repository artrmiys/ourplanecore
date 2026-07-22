using OurPlanCore;
using System.Text;
using System.Text.Json;

internal static class ProjectStorageCompactorTests
{
    public static void PreviewIsReadOnlyAndTargetsOnlyRasterSnapJson()
    {
        WithTempJob(root =>
        {
            string valid = RasterSnapPath(root, "A101");
            WriteText(valid, PrettyJson());
            string invalid = RasterSnapPath(root, "A102");
            WriteText(invalid, "{ invalid");
            string pageDecoy = WriteText(
                Path.Combine(root, "Pages", "A101", "snap.json"),
                PrettyJson());
            string outsideDecoy = WriteText(
                Path.Combine(root, "Other", "raster", "snap.json"),
                PrettyJson());

            IReadOnlyDictionary<string, FileState> before = Fingerprint(root);
            ProjectStorageCompactionPlan plan = ProjectStorageCompactor.BuildPlan(root);
            IReadOnlyDictionary<string, FileState> after = Fingerprint(root);

            AssertFingerprintsEqual(before, after, "preview must not mutate the job");
            ProjectStorageCompactionCandidate candidate = AssertSingle(plan.Candidates, "valid candidate");
            AssertTrue(SamePath(candidate.FullPath, valid), "candidate must be Pages/**/raster/snap.json");
            AssertTrue(candidate.PotentialSavingsBytes > 0, "pretty JSON should have savings");
            AssertEqual(1, plan.SkippedFiles.Count, "invalid eligible JSON should be reported");
            AssertTrue(
                EndsWithPath(plan.SkippedFiles[0].RelativePath, Path.Combine("A102", "raster", "snap.json")),
                "invalid eligible path should be reported");
            AssertFalse(
                plan.Candidates.Any(item => SamePath(item.FullPath, pageDecoy) || SamePath(item.FullPath, outsideDecoy)),
                "decoy snap.json files must stay outside the plan");
        });
    }

    public static void CompactPreservesJsonSemanticsAndReportsSavings()
    {
        WithTempJob(root =>
        {
            string path = RasterSnapPath(root, "A201");
            string original = PrettyJson();
            WriteText(path, original);
            long bytesBefore = new FileInfo(path).Length;
            using JsonDocument expected = JsonDocument.Parse(original);

            ProjectStorageCompactionPlan plan = ProjectStorageCompactor.BuildPlan(root);
            ProjectStorageCompactionResult result = ProjectStorageCompactor.Execute(plan);

            ProjectStorageCompactionFileResult file = AssertSingle(result.Files, "compaction result");
            AssertEqual(ProjectStorageCompactionStatus.Compacted, file.Status, "valid pretty JSON status");
            AssertEqual(1, result.CompactedFileCount, "compacted file count");
            AssertTrue(result.BytesSaved > 0, "reported bytes saved");
            AssertEqual(bytesBefore - new FileInfo(path).Length, result.BytesSaved, "actual bytes saved");
            AssertTrue(new FileInfo(path).Length < bytesBefore, "file should be smaller");

            using JsonDocument actual = JsonDocument.Parse(File.ReadAllText(path));
            AssertTrue(
                JsonElement.DeepEquals(expected.RootElement, actual.RootElement),
                "compacted JSON must preserve the complete JSON value");
        });
    }

    public static void InvalidOrChangedJsonIsSkippedWithoutMutation()
    {
        WithTempJob(root =>
        {
            string invalid = RasterSnapPath(root, "Bad");
            string invalidText = "[ invalid";
            WriteText(invalid, invalidText);
            ProjectStorageCompactionPlan invalidPlan = ProjectStorageCompactor.BuildPlan(root);
            ProjectStorageCompactionResult invalidResult = ProjectStorageCompactor.Execute(invalidPlan);

            ProjectStorageCompactionFileResult invalidFile = AssertSingle(invalidResult.Files, "invalid result");
            AssertEqual(
                ProjectStorageCompactionStatus.SkippedInvalidJson,
                invalidFile.Status,
                "invalid JSON status");
            AssertEqual(invalidText, File.ReadAllText(invalid), "invalid JSON must remain byte-for-byte unchanged");

            string changed = RasterSnapPath(root, "Changed");
            WriteText(changed, PrettyJson());
            ProjectStorageCompactionPlan changedPlan = ProjectStorageCompactor.BuildPlan(root);
            string replacement = "{\"new\":true}";
            WriteText(changed, replacement);
            ProjectStorageCompactionResult changedResult = ProjectStorageCompactor.Execute(changedPlan);
            ProjectStorageCompactionFileResult changedFile = changedResult.Files.Single(file =>
                EndsWithPath(file.RelativePath, Path.Combine("Changed", "raster", "snap.json")));

            AssertEqual(
                ProjectStorageCompactionStatus.SkippedChangedSincePreview,
                changedFile.Status,
                "stale preview status");
            AssertEqual(replacement, File.ReadAllText(changed), "changed JSON must not be overwritten");
        });
    }

    private static string PrettyJson() =>
        """
        {
          "schema_version": 2,
          "label": "Joists / перекрытие",
          "enabled": true,
          "points": [
            { "x": 1.25, "y": 2.5 },
            { "x": -3, "y": 4e2 }
          ],
          "metadata": null
        }
        """;

    private static string RasterSnapPath(string root, string pageName) =>
        Path.Combine(root, "Pages", pageName, "raster", "snap.json");

    private static IReadOnlyDictionary<string, FileState> Fingerprint(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path),
                path => new FileState(
                    new FileInfo(path).Length,
                    File.GetLastWriteTimeUtc(path).Ticks,
                    Convert.ToHexString(File.ReadAllBytes(path))),
                StringComparer.OrdinalIgnoreCase);

    private static void AssertFingerprintsEqual(
        IReadOnlyDictionary<string, FileState> expected,
        IReadOnlyDictionary<string, FileState> actual,
        string message)
    {
        AssertEqual(expected.Count, actual.Count, message + " file count");
        foreach ((string path, FileState state) in expected)
        {
            AssertTrue(actual.TryGetValue(path, out FileState? current), $"{message}: preserve {path}");
            AssertEqual(state, current, $"{message}: preserve {path}");
        }
    }

    private static string WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static void WithTempJob(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "ourplancore-compact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static T AssertSingle<T>(IReadOnlyList<T> values, string message)
    {
        if (values.Count != 1)
            throw new InvalidOperationException($"{message}: expected one, actual {values.Count}");
        return values[0];
    }

    private static bool SamePath(string left, string right) =>
        Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool EndsWithPath(string path, string suffix) =>
        path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .EndsWith(
                suffix.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected '{expected}', actual '{actual}'");
    }

    private sealed record FileState(long Length, long LastWriteUtcTicks, string Content);
}
