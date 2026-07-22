using System.Collections.Generic;
using System.Linq;

namespace OurPlanCore;

public enum ProjectStorageCategory
{
    Canonical,
    RebuildableRaster,
    ExactDuplicateSource,
    OrphanSource,
    RecoveryHistory,
    Other,
}

public enum ProjectStorageReferenceKind
{
    CurrentSourceJson,
    CurrentSourcePdfMetadata,
    RecoverySourceJson,
    RecoverySourcePdfMetadata,
}

public sealed record ProjectStorageReference(
    string MetadataPath,
    string RawTarget,
    string ResolvedTargetPath,
    ProjectStorageReferenceKind Kind,
    bool TargetExists,
    bool TargetsJobSource);

public sealed record ProjectStorageFileEntry(
    string FullPath,
    string RelativePath,
    long Length,
    ProjectStorageCategory Category,
    bool IsReferencedSource,
    string Sha256);

public sealed record ProjectStorageCategorySummary(
    ProjectStorageCategory Category,
    int FileCount,
    long Bytes);

public sealed record ProjectStorageDuplicateGroup(
    string Sha256,
    long FileLength,
    string RetainedPath,
    IReadOnlyList<string> DuplicatePaths,
    long PotentialSavingsBytes);

public sealed record ProjectStorageSnapJsonReport(
    string RelativePath,
    long CurrentBytes,
    long CompactBytes,
    long PotentialSavingsBytes,
    bool IsValidJson,
    string Error);

public sealed class ProjectStorageAnalysis
{
    public required string JobRoot { get; init; }

    public required IReadOnlyList<ProjectStorageFileEntry> Files { get; init; }

    public required IReadOnlyList<ProjectStorageReference> References { get; init; }

    public required IReadOnlyList<ProjectStorageDuplicateGroup> DuplicateGroups { get; init; }

    public required IReadOnlyList<ProjectStorageSnapJsonReport> SnapJsonReports { get; init; }

    public required IReadOnlyList<ProjectStorageCategorySummary> Categories { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public long TotalBytes => Files.Sum(file => file.Length);

    public long PotentialDuplicateSavingsBytes =>
        DuplicateGroups.Sum(group => group.PotentialSavingsBytes);

    public long PotentialSnapJsonSavingsBytes =>
        SnapJsonReports.Sum(report => report.PotentialSavingsBytes);

    public ProjectStorageCategorySummary Category(ProjectStorageCategory category) =>
        Categories.FirstOrDefault(item => item.Category == category) ??
        new ProjectStorageCategorySummary(category, 0, 0);
}
