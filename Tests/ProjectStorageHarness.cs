using OurPlanCore;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal static class ProjectStorageHarness
{
    public static int Run(string[] args)
    {
        if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine("Usage: storage-analysis <job-root>");
            return 2;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            ProjectStorageAnalysis analysis = ProjectStorageAnalyzer.Analyze(args[1]);
            stopwatch.Stop();

            var report = new
            {
                analysis.JobRoot,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                FileCount = analysis.Files.Count,
                analysis.TotalBytes,
                analysis.ReferenceScanComplete,
                ReferenceCount = analysis.References.Count,
                analysis.ExternalReferenceCount,
                DuplicateGroupCount = analysis.DuplicateGroups.Count,
                analysis.PotentialDuplicateSavingsBytes,
                SnapJsonFileCount = analysis.SnapJsonReports.Count,
                ValidSnapJsonFileCount = analysis.SnapJsonReports.Count(report => report.IsValidJson),
                analysis.PotentialSnapJsonSavingsBytes,
                Categories = analysis.Categories.Select(category => new
                {
                    Category = category.Category.ToString(),
                    category.FileCount,
                    category.Bytes,
                }),
                Warnings = analysis.Warnings.Take(30),
            };
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    public static int RunCompactSmoke(string[] args)
    {
        if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine("Usage: storage-compact-smoke <copied-job-root>");
            return 2;
        }

        try
        {
            ProjectStorageCompactionPlan plan = ProjectStorageCompactor.BuildPlan(args[1]);
            Dictionary<string, string> before = plan.Candidates.ToDictionary(
                candidate => candidate.RelativePath,
                candidate => SemanticJsonHash(candidate.FullPath),
                StringComparer.OrdinalIgnoreCase);
            ProjectStorageCompactionResult result = ProjectStorageCompactor.Execute(plan);

            foreach (ProjectStorageCompactionCandidate candidate in plan.Candidates)
            {
                if (!File.Exists(candidate.FullPath))
                    throw new InvalidOperationException($"Compaction removed {candidate.RelativePath}.");
                string after = SemanticJsonHash(candidate.FullPath);
                if (!string.Equals(before[candidate.RelativePath], after, StringComparison.Ordinal))
                    throw new InvalidOperationException($"JSON semantics changed for {candidate.RelativePath}.");
            }

            var report = new
            {
                result.JobRoot,
                CandidateCount = plan.Candidates.Count,
                plan.CompactableFileCount,
                result.CompactedFileCount,
                result.BytesSaved,
                result.IssueCount,
                SemanticEqualityVerified = true,
            };
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return result.HasFailures || result.IssueCount > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static string SemanticJsonHash(string path)
    {
        using FileStream input = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(input);
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(document.RootElement);
        return Convert.ToHexString(SHA256.HashData(canonical));
    }
}
