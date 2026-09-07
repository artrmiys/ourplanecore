using System.Text.Json;
using OurPlanCore;

internal static partial class OurPlanPackageTests
{
    public static void MovedAndRenamedPackageCanOpenEditSaveAndReopen()
    {
        using var fixture = PackageFixture.Create();
        OurPlanPackageSession created = fixture.CreatePackage();
        string originalPackage = created.PackagePath;
        string originalWorkspace = fixture.Job.RootPath;
        string relocatedRoot = Path.Combine(fixture.Parent, "relocated-package-root");
        string relocatedPackage = Path.Combine(relocatedRoot, "Renamed Project.ourplan");
        var managed = new List<OurPlanPackageSession>();

        try
        {
            OurPlanPackageWorkspace.MarkSessionClosed(created);
            Directory.CreateDirectory(relocatedRoot);
            File.Move(originalPackage, relocatedPackage);
            TryDelete(originalWorkspace);

            AssertFalse(File.Exists(originalPackage), "the original package path must be gone");
            AssertFalse(Directory.Exists(originalWorkspace), "the original source job must be gone");

            OurPlanPackageSession opened = OurPlanPackageWorkspace.Open(relocatedPackage);
            managed.Add(opened);
            OurPlanCoreJob openedJob = OurPlanCoreJobStore.LoadJob(opened.WorkspaceRoot);
            PageInfo openedPage = OurPlanCoreJobStore.TryReadPage(
                                      Path.Combine(openedJob.PagesRoot, "A1.01"))
                                  ?? throw new InvalidOperationException(
                                      "the relocated package page was not loadable");
            AssertTrue(File.Exists(openedPage.PdfPath),
                "the relocated package must resolve its embedded PDF without the source job");
            AssertTrue(OurPlanCoreJobStore.IsSameOrDescendant(openedJob.RootPath, openedPage.PdfPath),
                "the relocated package PDF must resolve inside its extracted workspace");

            string bookmarksPath = Path.Combine(opened.WorkspaceRoot, "bookmarks.json");
            File.WriteAllText(bookmarksPath, "{\"items\":[42]}");
            opened.HasUnpackagedChanges = true;
            string revisionBeforeSave = opened.BaseRevisionId;
            OurPlanPackageSaveResult save = OurPlanPackageWriter.Save(opened);
            AssertFalse(save.RevisionId.Equals(revisionBeforeSave, StringComparison.OrdinalIgnoreCase),
                "editing the relocated package must publish a new revision");
            OurPlanPackageWorkspace.MarkSessionClosed(opened);

            OurPlanPackageSession reopened = OurPlanPackageWorkspace.Open(relocatedPackage);
            managed.Add(reopened);
            AssertTrue(
                File.ReadAllText(Path.Combine(reopened.WorkspaceRoot, "bookmarks.json"))
                    .Contains("[42]", StringComparison.Ordinal),
                "the edit must survive save and reopen at the relocated package path");
            OurPlanCoreJob reopenedJob = OurPlanCoreJobStore.LoadJob(reopened.WorkspaceRoot);
            PageInfo reopenedPage = OurPlanCoreJobStore.TryReadPage(
                                        Path.Combine(reopenedJob.PagesRoot, "A1.01"))
                                    ?? throw new InvalidOperationException(
                                        "the saved relocated package page was not loadable");
            AssertTrue(File.Exists(reopenedPage.PdfPath),
                "the embedded PDF must remain loadable after editing and reopening");
        }
        finally
        {
            CloseAndDeleteManagedWorkspaces(managed);
        }
    }

    public static void LegacyFolderCopyRebasesReferencesAndSurvivesSourceRemoval()
    {
        using var fixture = PackageFixture.Create();
        string sourceRoot = fixture.Job.RootPath;
        string sourceMetadataPath = Path.Combine(fixture.PageFolder, "source.json");
        SourceInfo source = JsonSerializer.Deserialize<SourceInfo>(
                                File.ReadAllText(sourceMetadataPath))
                            ?? throw new InvalidOperationException("fixture page metadata was missing");
        source.Pdf = Path.GetFullPath(Path.Combine(fixture.PageFolder, source.Pdf));
        File.WriteAllText(sourceMetadataPath, JsonSerializer.Serialize(source));
        File.WriteAllText(
            Path.Combine(sourceRoot, "bookmarks.json"),
            JsonSerializer.Serialize(new
            {
                page_folder = fixture.PageFolder,
                crop_image_path = Path.Combine(
                    fixture.PageFolder,
                    RasterSheetCacheService.CacheFolderName,
                    "active.png"),
            }));

        string destination = Path.Combine(
            fixture.Parent,
            "relocated-legacy-root",
            "Renamed Legacy Project");
        OurPlanPackageWorkspace.ExportLegacyCopy(sourceRoot, destination);
        TryDelete(sourceRoot);

        AssertFalse(Directory.Exists(sourceRoot), "the legacy source job must be removable");
        OurPlanPackagePortability.ValidateExtractedReferences(destination);
        OurPlanCoreJob loaded = OurPlanCoreJobStore.LoadJob(destination);
        string copiedPageFolder = Path.Combine(loaded.PagesRoot, "A1.01");
        PageInfo page = OurPlanCoreJobStore.TryReadPage(copiedPageFolder)
                        ?? throw new InvalidOperationException("the exported legacy page was not loadable");
        SourceInfo copiedSource = JsonSerializer.Deserialize<SourceInfo>(
                                      File.ReadAllText(Path.Combine(copiedPageFolder, "source.json")))
                                  ?? throw new InvalidOperationException(
                                      "the exported legacy page metadata was missing");
        using JsonDocument copiedBookmarks = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(destination, "bookmarks.json")));
        string copiedBookmarkPage = copiedBookmarks.RootElement
            .GetProperty("page_folder")
            .GetString() ?? "";
        string copiedBookmarkCrop = copiedBookmarks.RootElement
            .GetProperty("crop_image_path")
            .GetString() ?? "";

        AssertFalse(Path.IsPathRooted(copiedSource.Pdf),
            "legacy export must replace an absolute source PDF path with a relative reference");
        AssertFalse(copiedSource.Pdf.Contains(sourceRoot, StringComparison.OrdinalIgnoreCase),
            "legacy export metadata must not retain the removed source root");
        AssertFalse(
            Path.IsPathRooted(copiedBookmarkPage) ||
            Path.IsPathRooted(copiedBookmarkCrop) ||
            copiedBookmarkPage.Contains(sourceRoot, StringComparison.OrdinalIgnoreCase) ||
            copiedBookmarkCrop.Contains(sourceRoot, StringComparison.OrdinalIgnoreCase),
            "legacy export bookmark paths must not retain the removed source root");
        AssertTrue(
            Directory.Exists(Path.GetFullPath(Path.Combine(destination, copiedBookmarkPage))) &&
            File.Exists(Path.GetFullPath(Path.Combine(destination, copiedBookmarkCrop))),
            "legacy export bookmark page and crop references must resolve inside the copied job");
        AssertTrue(File.Exists(page.PdfPath),
            "the exported legacy page must resolve its copied PDF after source removal");
        AssertTrue(OurPlanCoreJobStore.IsSameOrDescendant(destination, page.PdfPath),
            "the exported legacy page PDF must resolve inside the destination job");
        AssertFalse(Directory.Exists(Path.Combine(destination, ".snapshots")),
            "legacy export must still exclude recovery snapshots");
        AssertFalse(File.Exists(Path.Combine(destination, ".~lock")),
            "legacy export must still exclude the source write lease");
    }
}
