using OurPlanCore;
using System.Text;
using System.Text.Json;

internal static class ProjectStorageAnalyzerTests
{
    public static void AnalysisClassifiesReferencesDuplicatesRasterAndRecovery()
    {
        WithTempJob(root =>
        {
            string sources = Directory.CreateDirectory(Path.Combine(root, "sources")).FullName;
            string pages = Directory.CreateDirectory(Path.Combine(root, "Pages")).FullName;
            Directory.CreateDirectory(Path.Combine(root, "Takeoffs"));
            WriteText(Path.Combine(root, "Data.xml"), "<PXML />");
            WriteText(Path.Combine(root, "scratch.tmp"), "other");

            string active = WriteText(Path.Combine(sources, "active.pdf"), "same-pdf");
            string activeCopy = WriteText(Path.Combine(sources, "active-copy.pdf"), "same-pdf");
            string metadataOnly = WriteText(Path.Combine(sources, "metadata-only.pdf"), "metadata");
            string snapshotOnly = WriteText(Path.Combine(sources, "snapshot-only.pdf"), "snapshot");
            string recoveryMetadata = WriteText(Path.Combine(sources, "recovery-metadata.pdf"), "recovery");
            string orphan = WriteText(Path.Combine(sources, "orphan.pdf"), "orphan");

            string currentPage = Directory.CreateDirectory(Path.Combine(pages, "A101")).FullName;
            WriteJson(Path.Combine(currentPage, "source.json"), new
            {
                pdf = Path.GetRelativePath(currentPage, active),
                page = 0,
            });
            WriteJson(Path.Combine(currentPage, "source_pdf.json"), new
            {
                pdf_path = metadataOnly,
                page_index = 0,
            });
            WriteText(Path.Combine(currentPage, "Data.xml"), "<PXML />");

            string raster = Directory.CreateDirectory(Path.Combine(currentPage, "raster")).FullName;
            WriteText(Path.Combine(raster, "working.webp"), "rebuildable");
            WriteText(
                Path.Combine(raster, "snap.json"),
                "{\r\n  \"schema_version\": 2,\r\n  \"points\": [\r\n    { \"x\": 1, \"y\": 2 }\r\n  ]\r\n}\r\n");

            string snapshotRoot = Directory.CreateDirectory(
                Path.Combine(root, ".snapshots", "20260722_120000_manual")).FullName;
            string snapshotPage = Directory.CreateDirectory(
                Path.Combine(snapshotRoot, "Pages", "S201")).FullName;
            string logicalSnapshotPage = Path.Combine(pages, "S201");
            WriteJson(Path.Combine(snapshotPage, "source.json"), new
            {
                pdf = Path.GetRelativePath(logicalSnapshotPage, snapshotOnly),
                page = 1,
            });

            string originalRoot = Path.Combine(Path.GetPathRoot(root) ?? "C:\\", "Archived", "OriginalJob");
            WriteJson(Path.Combine(snapshotRoot, "snapshot_manifest.json"), new
            {
                schema_version = 1,
                job_root = originalRoot,
            });
            WriteJson(Path.Combine(snapshotPage, "source_pdf.json"), new
            {
                pdf_path = Path.Combine(originalRoot, "sources", Path.GetFileName(recoveryMetadata)),
                page_index = 1,
            });

            long bytesBefore = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
            ProjectStorageAnalysis analysis = ProjectStorageAnalyzer.Analyze(root);

            AssertEqual(bytesBefore, analysis.TotalBytes, "all job bytes should be classified exactly once");
            AssertCategory(analysis, active, ProjectStorageCategory.Canonical);
            AssertCategory(analysis, activeCopy, ProjectStorageCategory.ExactDuplicateSource);
            AssertCategory(analysis, metadataOnly, ProjectStorageCategory.Canonical);
            AssertCategory(analysis, snapshotOnly, ProjectStorageCategory.Canonical);
            AssertCategory(analysis, recoveryMetadata, ProjectStorageCategory.Canonical);
            AssertCategory(analysis, orphan, ProjectStorageCategory.UnreferencedPageSource);
            AssertCategory(
                analysis,
                Path.Combine(raster, "working.webp"),
                ProjectStorageCategory.RebuildableRaster);
            AssertCategory(
                analysis,
                Path.Combine(snapshotPage, "source.json"),
                ProjectStorageCategory.RecoveryHistory);
            AssertCategory(
                analysis,
                Path.Combine(root, "scratch.tmp"),
                ProjectStorageCategory.Other);

            AssertTrue(
                analysis.References.Any(reference =>
                    reference.Kind == ProjectStorageReferenceKind.CurrentSourceJson &&
                    SamePath(reference.ResolvedTargetPath, active)),
                "current source.json reference");
            AssertTrue(
                analysis.References.Any(reference =>
                    reference.Kind == ProjectStorageReferenceKind.CurrentSourcePdfMetadata &&
                    SamePath(reference.ResolvedTargetPath, metadataOnly)),
                "current source_pdf.json reference");
            AssertTrue(
                analysis.References.Any(reference =>
                    reference.Kind == ProjectStorageReferenceKind.RecoverySourceJson &&
                    SamePath(reference.ResolvedTargetPath, snapshotOnly)),
                "recovery source.json reference should resolve through the logical current Pages folder");
            AssertTrue(
                analysis.References.Any(reference =>
                    reference.Kind == ProjectStorageReferenceKind.RecoverySourcePdfMetadata &&
                    SamePath(reference.ResolvedTargetPath, recoveryMetadata) &&
                    reference.TargetExists),
                "recovery source_pdf.json reference should rebase snapshot_manifest job_root");

            ProjectStorageDuplicateGroup duplicate = AssertSingle(analysis.DuplicateGroups, "duplicate group");
            AssertEqual(new FileInfo(activeCopy).Length, duplicate.PotentialSavingsBytes, "duplicate savings");
            AssertTrue(
                duplicate.DuplicatePaths.Any(path => EndsWithPath(path, Path.Combine("sources", "active-copy.pdf"))),
                "unreferenced duplicate should be the removable copy");
            AssertEqual(duplicate.PotentialSavingsBytes, analysis.PotentialDuplicateSavingsBytes, "total duplicate savings");

            ProjectStorageSnapJsonReport snap = AssertSingle(analysis.SnapJsonReports, "snap.json report");
            AssertTrue(snap.IsValidJson, "snap.json should parse");
            AssertTrue(snap.CompactBytes < snap.CurrentBytes, "pretty snap.json should have compact potential");
            AssertEqual(snap.PotentialSavingsBytes, analysis.PotentialSnapJsonSavingsBytes, "total snap savings");
            AssertEqual(0, analysis.Warnings.Count, "valid fixture warnings");
            AssertTrue(analysis.ReferenceScanComplete, "valid fixture reference scan");
            AssertEqual(
                analysis.Files.Count,
                analysis.Categories.Sum(category => category.FileCount),
                "category counts should include every file once");
            AssertEqual(
                analysis.Files.Count,
                analysis.Files.Select(file => file.FullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                "file entries should have unique paths");
        });
    }

    public static void AnalysisIsReadOnlyForMalformedMetadataAndSnapJson()
    {
        WithTempJob(root =>
        {
            string sources = Directory.CreateDirectory(Path.Combine(root, "sources")).FullName;
            string page = Directory.CreateDirectory(Path.Combine(root, "Pages", "Bad")).FullName;
            string source = WriteText(Path.Combine(sources, "orphan.pdf"), "orphan");
            string sourceJson = WriteText(Path.Combine(page, "source.json"), "{ malformed");
            string raster = Directory.CreateDirectory(Path.Combine(page, "raster")).FullName;
            string snapJson = WriteText(Path.Combine(raster, "snap.json"), "[ invalid");

            IReadOnlyDictionary<string, FileState> before = Fingerprint(root);
            ProjectStorageAnalysis analysis = ProjectStorageAnalyzer.Analyze(root);
            IReadOnlyDictionary<string, FileState> after = Fingerprint(root);

            AssertEqual(before.Count, after.Count, "analysis must not create or quarantine files");
            foreach ((string path, FileState expected) in before)
            {
                AssertTrue(after.ContainsKey(path), $"preserve {path}");
                FileState actual = after[path];
                AssertEqual(expected.Length, actual.Length, $"preserve length for {path}");
                AssertEqual(expected.LastWriteUtcTicks, actual.LastWriteUtcTicks, $"preserve timestamp for {path}");
                AssertEqual(expected.Content, actual.Content, $"preserve content for {path}");
            }

            AssertCategory(analysis, source, ProjectStorageCategory.SourceNeedsReview);
            AssertCategory(analysis, sourceJson, ProjectStorageCategory.Canonical);
            AssertCategory(analysis, snapJson, ProjectStorageCategory.RebuildableRaster);
            ProjectStorageSnapJsonReport snap = AssertSingle(analysis.SnapJsonReports, "malformed snap report");
            AssertFalse(snap.IsValidJson, "malformed snap should be reported, not rewritten");
            AssertEqual(0L, snap.PotentialSavingsBytes, "malformed snap savings");
            AssertTrue(analysis.Warnings.Count >= 2, "malformed metadata and snap warnings");
            AssertFalse(analysis.ReferenceScanComplete, "malformed metadata makes source findings incomplete");
            AssertEqual(0L, analysis.PotentialDuplicateSavingsBytes, "incomplete references disable duplicate savings");
        });
    }

    public static void AnalysisProtectsEveryReferencedExactDuplicate()
    {
        WithTempJob(root =>
        {
            string sources = Directory.CreateDirectory(Path.Combine(root, "sources")).FullName;
            string pages = Directory.CreateDirectory(Path.Combine(root, "Pages")).FullName;
            string first = WriteText(Path.Combine(sources, "first.pdf"), "identical-source");
            string second = WriteText(Path.Combine(sources, "second.pdf"), "identical-source");

            string currentPage = Directory.CreateDirectory(Path.Combine(pages, "A101")).FullName;
            WriteJson(Path.Combine(currentPage, "source.json"), new
            {
                pdf = Path.GetRelativePath(currentPage, first),
            });

            string snapshotRoot = Directory.CreateDirectory(
                Path.Combine(root, ".snapshots", "20260722_130000_manual")).FullName;
            string snapshotPage = Directory.CreateDirectory(
                Path.Combine(snapshotRoot, "Pages", "A102")).FullName;
            string logicalPage = Path.Combine(pages, "A102");
            WriteJson(Path.Combine(snapshotPage, "source.json"), new
            {
                pdf = Path.GetRelativePath(logicalPage, second),
            });

            ProjectStorageAnalysis protectedAnalysis = ProjectStorageAnalyzer.Analyze(root);
            AssertTrue(protectedAnalysis.ReferenceScanComplete, "both duplicate references should resolve");
            AssertEqual(0, protectedAnalysis.DuplicateGroups.Count, "referenced duplicates are not savings");
            AssertEqual(0L, protectedAnalysis.PotentialDuplicateSavingsBytes, "referenced duplicate bytes are protected");
            AssertCategory(protectedAnalysis, first, ProjectStorageCategory.Canonical);
            AssertCategory(protectedAnalysis, second, ProjectStorageCategory.Canonical);

            string spare = WriteText(Path.Combine(sources, "spare.pdf"), "identical-source");
            ProjectStorageAnalysis withSpare = ProjectStorageAnalyzer.Analyze(root);
            ProjectStorageDuplicateGroup group = AssertSingle(withSpare.DuplicateGroups, "unreferenced duplicate group");
            AssertEqual(new FileInfo(spare).Length, group.PotentialSavingsBytes, "only spare copy is a candidate");
            AssertTrue(
                group.DuplicatePaths.Any(path => EndsWithPath(path, Path.Combine("sources", "spare.pdf"))),
                "only unreferenced spare should be listed");
            AssertCategory(withSpare, spare, ProjectStorageCategory.ExactDuplicateSource);
        });
    }

    public static void AnalysisHandlesValidNonObjectReferenceMetadata()
    {
        WithTempJob(root =>
        {
            string source = WriteText(Path.Combine(root, "sources", "candidate.pdf"), "source");
            string page = Directory.CreateDirectory(Path.Combine(root, "Pages", "A201")).FullName;
            WriteText(Path.Combine(page, "source.json"), "[]");

            ProjectStorageAnalysis analysis = ProjectStorageAnalyzer.Analyze(root);

            AssertFalse(analysis.ReferenceScanComplete, "array metadata should require review, not crash");
            AssertCategory(analysis, source, ProjectStorageCategory.SourceNeedsReview);
            AssertTrue(
                analysis.Warnings.Any(warning => warning.Contains("Expected a JSON object", StringComparison.Ordinal)),
                "wrong-shape metadata warning");
        });
    }

    public static void AnalysisReportsExternalPageDependencies()
    {
        WithTempJob(root =>
        {
            string external = root + ".external.pdf";
            try
            {
                WriteText(external, "external-source");
                string page = Directory.CreateDirectory(Path.Combine(root, "Pages", "A301")).FullName;
                WriteJson(Path.Combine(page, "source.json"), new { pdf = external });

                ProjectStorageAnalysis analysis = ProjectStorageAnalyzer.Analyze(root);

                AssertTrue(analysis.ReferenceScanComplete, "an existing external target is readable metadata");
                AssertEqual(1, analysis.ExternalReferenceCount, "external dependency count");
                AssertTrue(
                    analysis.References.Any(reference =>
                        reference.TargetExists && !reference.TargetsJob && SamePath(reference.ResolvedTargetPath, external)),
                    "external dependency reference");
                AssertTrue(
                    analysis.Warnings.Any(warning => warning.Contains("non-portable", StringComparison.OrdinalIgnoreCase)),
                    "external dependency portability warning");
            }
            finally
            {
                try { File.Delete(external); } catch { }
            }
        });
    }

    private static IReadOnlyDictionary<string, FileState> Fingerprint(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path),
                path => new FileState(
                    new FileInfo(path).Length,
                    File.GetLastWriteTimeUtc(path).Ticks,
                    Convert.ToHexString(File.ReadAllBytes(path))),
                StringComparer.OrdinalIgnoreCase);

    private static void AssertCategory(
        ProjectStorageAnalysis analysis,
        string fullPath,
        ProjectStorageCategory expected)
    {
        ProjectStorageFileEntry entry = analysis.Files.Single(file => SamePath(file.FullPath, fullPath));
        AssertEqual(expected, entry.Category, $"category for {entry.RelativePath}");
    }

    private static T AssertSingle<T>(IReadOnlyList<T> values, string message)
    {
        if (values.Count != 1)
            throw new InvalidOperationException($"{message}: expected one, actual {values.Count}");
        return values[0];
    }

    private static string WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static void WriteJson(string path, object value)
    {
        string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        WriteText(path, json);
    }

    private static void WithTempJob(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "ourplancore-storage-" + Guid.NewGuid().ToString("N"));
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
