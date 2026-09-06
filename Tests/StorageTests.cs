using OurPlanCore;
using Docnet.Core;
using Docnet.Core.Models;
using SkiaSharp;
using System.Xml.Linq;

internal static class StorageTests
{
    public static void JobLayoutCreateAndLoadEnsuresBaseFolders()
    {
        WithTempParent(parent =>
        {
            OurPlanCoreJob created = OurPlanCoreJobStore.CreateJob(parent, "Layout Job");
            OurPlanCoreJob loaded = OurPlanCoreJobStore.LoadJob(created.RootPath);

            AssertEqual("Layout Job", loaded.Name, "loaded job name");
            AssertTrue(File.Exists(Path.Combine(created.RootPath, "Data.xml")), "root Data.xml should exist");
            AssertTrue(Directory.Exists(Path.Combine(created.RootPath, "sources")), "sources folder should exist");
            AssertFalse(Directory.Exists(Path.Combine(created.PagesRoot, "00. imported")), "import folder should not be created on job load");
            AssertFalse(Directory.Exists(Path.Combine(created.PagesRoot, "imported")), "plain imported folder should not be created");
            AssertTrue(Directory.Exists(Path.Combine(created.PagesRoot, "--------others")), "others folder should exist");
            AssertTrue(Directory.Exists(created.TakeoffsRoot), "takeoffs root should exist");

            string expectedImport = Path.Combine(created.PagesRoot, "00. imported");
            AssertEqual(FullPath(expectedImport), FullPath(OurPlanCoreJobStore.DefaultImportFolder(loaded)), "default import folder");
            AssertTrue(Directory.Exists(expectedImport), "default import folder is created on demand");
        });
    }

    public static void PageImportWritesLayerManifestAndMetadata()
    {
        WithTempJob("Page Import", job =>
        {
            string sourcePdf = CreateSourcePdf(job, "plans.pdf");
            var layers = new List<PdfLayerInfo>
            {
                new() { Number = 2, Name = "Walls", IsOn = true },
                new() { Number = 1, Name = "Grid", IsOn = false },
            };
            var cache = new Dictionary<int, IReadOnlyList<PdfLayerInfo>> { [0] = layers };

            IReadOnlyList<PageInfo> pages = OurPlanCoreJobStore.ImportPdf(
                job,
                sourcePdf,
                ["A101", "A102"],
                job.PagesRoot,
                cache);

            AssertEqual("2", pages.Count.ToString(), "imported page count");
            AssertTrue(pages[0].PdfLayersCached, "first page should use cached layers");
            AssertFalse(pages[1].PdfLayersCached, "second page should not be cached");
            SourceInfo source = OurPlanCoreJobStore.ReadSource(pages[0].FolderPath)
                ?? throw new InvalidOperationException("source missing");
            AssertFalse(Path.IsPathRooted(source.Pdf), "stored pdf path should be relative");

            PageLayerManifest manifest = OurPlanCoreJobStore.ReadPageLayerManifest(pages[0].FolderPath)
                ?? throw new InvalidOperationException("layer manifest missing");
            AssertEqual("2", manifest.LayerCount.ToString(), "manifest layer count");
            AssertEqual("1", manifest.Layers[0].Number.ToString(), "manifest layers sorted by number");
            AssertTrue(OurPlanCoreJobStore.ReadPageLayerManifest(pages[1].FolderPath) == null, "uncached page has no layer manifest");

            var metadata = new PdfSheetMetadata
            {
                GeneratedAtUtc = "keep-existing",
                SheetLabel = "A101",
                SheetKey = "a101",
                SelectedScaleMetersPerPt = 0.25,
                Layers = layers,
            };
            OurPlanCoreJobStore.WriteSourcePdfMetadata(pages[0].FolderPath, metadata);
            PdfSheetMetadata reloaded = OurPlanCoreJobStore.ReadSourcePdfMetadata(pages[0].FolderPath)
                ?? throw new InvalidOperationException("source metadata missing");

            AssertEqual("keep-existing", reloaded.GeneratedAtUtc, "metadata timestamp should not be rewritten");
            AssertEqual("A101", reloaded.SheetLabel, "metadata sheet label");
            AssertClose(0.25, reloaded.SelectedScaleMetersPerPt, "metadata selected scale");
            AssertEqual("2", reloaded.Layers.Count.ToString(), "metadata layers");
        });
    }

    public static void BlankPageCreationWritesRenderablePdfAndMetadata()
    {
        WithTempJob("Blank Page", job =>
        {
            PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "Blank A", job.PagesRoot);

            AssertEqual("Blank A", page.Name, "blank page name");
            AssertTrue(File.Exists(page.PdfPath), "blank pdf should be created");
            AssertEqual("0", page.PdfPage.ToString(), "blank page index");
            AssertClose(0, page.ScaleMetersPerPt, "blank page starts unscaled");

            SourceInfo source = OurPlanCoreJobStore.ReadSource(page.FolderPath)
                ?? throw new InvalidOperationException("blank source missing");
            AssertFalse(Path.IsPathRooted(source.Pdf), "blank source pdf path should be relative");
            AssertTrue(source.Pdf.EndsWith(".blank.pdf", StringComparison.OrdinalIgnoreCase), "blank source pdf suffix");

            PdfSheetMetadata metadata = OurPlanCoreJobStore.ReadSourcePdfMetadata(page.FolderPath)
                ?? throw new InvalidOperationException("blank metadata missing");
            AssertEqual("manual-blank", metadata.Source, "blank metadata source");
            AssertEqual("Blank A", metadata.SheetLabel, "blank metadata sheet label");
            AssertEqual("Blank A", metadata.RenameCandidate, "blank metadata rename candidate");
            AssertClose(36 * 72, metadata.WidthPt, "blank metadata width");
            AssertClose(24 * 72, metadata.HeightPt, "blank metadata height");

            using var doc = DocLib.Instance.GetDocReader(page.PdfPath, new PageDimensions(1.0));
            AssertEqual("1", doc.GetPageCount().ToString(), "blank pdf page count");
        });
    }

    public static void PageImportKeepsMultiplePdfSourcesInOneFolder()
    {
        WithTempJob("Multi PDF Import", job =>
        {
            string archPdf = CreateSourcePdf(job, "arch.pdf");
            string structPdf = CreateSourcePdf(job, "struct.pdf");

            IReadOnlyList<PageInfo> archPages = OurPlanCoreJobStore.ImportPdf(
                job,
                archPdf,
                ["A101", "A102"],
                job.PagesRoot);
            IReadOnlyList<PageInfo> structPages = OurPlanCoreJobStore.ImportPdf(
                job,
                structPdf,
                ["S101"],
                job.PagesRoot);

            AssertEqual("2", archPages.Count.ToString(), "arch page count");
            AssertEqual("1", structPages.Count.ToString(), "struct page count");
            AssertEqual(
                "3",
                Directory.EnumerateDirectories(job.PagesRoot)
                    .Count(folder => OurPlanCoreJobStore.TryReadPage(folder) != null)
                    .ToString(),
                "total imported page folder count");

            SourceInfo archFirstSource = OurPlanCoreJobStore.ReadSource(archPages[0].FolderPath)
                ?? throw new InvalidOperationException("arch first source missing");
            SourceInfo archSecondSource = OurPlanCoreJobStore.ReadSource(archPages[1].FolderPath)
                ?? throw new InvalidOperationException("arch second source missing");
            SourceInfo structSource = OurPlanCoreJobStore.ReadSource(structPages[0].FolderPath)
                ?? throw new InvalidOperationException("struct source missing");

            AssertTrue(archFirstSource.Pdf.EndsWith("arch.pdf", StringComparison.OrdinalIgnoreCase), "arch first source pdf");
            AssertTrue(archSecondSource.Pdf.EndsWith("arch.pdf", StringComparison.OrdinalIgnoreCase), "arch second source pdf");
            AssertTrue(structSource.Pdf.EndsWith("struct.pdf", StringComparison.OrdinalIgnoreCase), "struct source pdf");
            AssertEqual("0", archFirstSource.Page.ToString(), "arch first source page");
            AssertEqual("1", archSecondSource.Page.ToString(), "arch second source page");
            AssertEqual("0", structSource.Page.ToString(), "struct source page");
        });
    }

    public static void PageCopyAndMovePreserveSourceOverlayAndLayers()
    {
        WithTempJob("Page Copy Move", job =>
        {
            PageInfo basePage = CreatePageItem(job, "S101");
            PageInfo overlayPage = CreatePageItem(job, "S102");
            OurPlanCoreJobStore.SavePageOverlay(basePage.FolderPath, overlayPage.FolderPath, "#1E88E5", 0.42);
            OurPlanCoreJobStore.SavePageOverlayTransform(basePage.FolderPath, 12.5, -7.25, 1.2, -4.5);
            OurPlanCoreJobStore.SavePageOverlayVisibility(basePage.FolderPath, false);
            OurPlanCoreJobStore.SavePageHiddenTakeoffs(basePage.FolderPath, ["Walls", "Joists"]);
            OurPlanCoreJobStore.SavePageHiddenMeasurements(basePage.FolderPath, ["m-old-1", "m-old-2"]);
            OurPlanCoreJobStore.SavePageLayerCache(
                basePage.FolderPath,
                [new PdfLayerInfo { Number = 1, Name = "Walls", IsOn = true }]);

            string copyParent = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Copies");
            string copiedPath = OurPlanCoreJobStore.CopyNode(basePage.FolderPath, copyParent);
            string secondCopiedPath = OurPlanCoreJobStore.CopyNode(basePage.FolderPath, copyParent);
            PageInfo copiedPage = OurPlanCoreJobStore.TryReadPage(copiedPath)
                ?? throw new InvalidOperationException("copied page missing");
            PageInfo secondCopiedPage = OurPlanCoreJobStore.TryReadPage(secondCopiedPath)
                ?? throw new InvalidOperationException("second copied page missing");
            AssertFalse(string.Equals(copiedPath, secondCopiedPath, StringComparison.OrdinalIgnoreCase), "second copied page should use another hidden folder");
            AssertEqual("S101", OurPlanCoreJobStore.DisplayName(copiedPath), "copied page display name");
            AssertEqual("S101", copiedPage.Name, "copied page info name");
            AssertEqual("S101", OurPlanCoreJobStore.DisplayName(secondCopiedPath), "second copied page display name");
            AssertEqual("S101", secondCopiedPage.Name, "second copied page info name");
            AssertPageSourceState(copiedPath, overlayPage.FolderPath);
            AssertPageSourceState(secondCopiedPath, overlayPage.FolderPath);

            string moveParent = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Moved");
            string movedPath = OurPlanCoreJobStore.MoveNode(copiedPath, moveParent);
            AssertPageSourceState(movedPath, overlayPage.FolderPath);
        });
    }

    public static void PageCorruptSourceJsonIsQuarantined()
    {
        WithTempJob("Corrupt Source", job =>
        {
            string pageFolder = OurPlanCoreJobStore.EnsureFolder(job.PagesRoot, "Broken");
            string sourcePath = Path.Combine(pageFolder, "source.json");
            File.WriteAllText(sourcePath, "{ bad json");
            _ = OurPlanCoreJobStore.DrainCorruptJsonFiles();

            PageInfo? page = OurPlanCoreJobStore.TryReadPage(pageFolder);
            IReadOnlyList<string> quarantined = OurPlanCoreJobStore.DrainCorruptJsonFiles();

            AssertTrue(page == null, "corrupt source should not load as page");
            AssertFalse(File.Exists(sourcePath), "corrupt source should be moved away");
            AssertEqual("1", Directory.GetFiles(pageFolder, "source.json.corrupt-*").Length.ToString(), "corrupt file count");
            AssertTrue(quarantined.Any(path => path.Contains("source.json", StringComparison.OrdinalIgnoreCase)), "quarantine report");
        });
    }

    public static void PageSourceJsonRepairsFromSheetMetadata()
    {
        WithTempJob("Repair Source", job =>
        {
            PageInfo page = CreatePageItem(job, "A300");
            OurPlanCoreJobStore.WriteSourcePdfMetadata(
                page.FolderPath,
                new PdfSheetMetadata
                {
                    Source = "test",
                    PdfPath = page.PdfPath,
                    PageIndex = page.PdfPage,
                    PageNumber = page.PdfPage + 1,
                    SheetLabel = "A300",
                    RenameCandidate = "A300",
                    SelectedScaleMetersPerPt = 0.3048,
                    Layers =
                    [
                        new PdfLayerInfo { Number = 7, Name = "Walls", IsOn = true },
                    ],
                });

            string sourcePath = Path.Combine(page.FolderPath, "source.json");
            File.Delete(sourcePath); // Only genuinely missing source metadata may be reconstructed automatically.
            _ = OurPlanCoreJobStore.DrainCorruptJsonFiles();

            PageInfo repaired = OurPlanCoreJobStore.TryReadPage(page.FolderPath)
                ?? throw new InvalidOperationException("page source should repair from sheet metadata");
            IReadOnlyList<string> quarantined = OurPlanCoreJobStore.DrainCorruptJsonFiles();
            SourceInfo repairedSource = OurPlanCoreJobStore.ReadSource(page.FolderPath)
                ?? throw new InvalidOperationException("repaired source missing");
            PageLayerManifest manifest = OurPlanCoreJobStore.ReadPageLayerManifest(page.FolderPath)
                ?? throw new InvalidOperationException("repaired layer manifest missing");

            AssertEqual("A300", repaired.Name, "repaired page name");
            AssertTrue(File.Exists(repaired.PdfPath), "repaired page pdf should resolve");
            AssertClose(0.3048, repaired.ScaleMetersPerPt, "repaired page scale");
            AssertTrue(repaired.PdfLayersCached, "repaired page should keep metadata layers cached");
            AssertFalse(Path.IsPathRooted(repairedSource.Pdf), "repaired source pdf should be relative");
            AssertEqual("0", Directory.GetFiles(page.FolderPath, "source.json.corrupt-*").Length.ToString(), "missing source has no corrupt backup");
            AssertFalse(quarantined.Any(path => path.Contains("source.json", StringComparison.OrdinalIgnoreCase)), "missing source is not quarantined");
            AssertEqual("1", manifest.LayerCount.ToString(), "repaired layer manifest count");
        });
    }

    public static void PageOverlayReferencesRebaseAfterPageMove()
    {
        WithTempJob("Overlay Rebase", job =>
        {
            PageInfo basePage = CreatePageItem(job, "S101");
            PageInfo overlayPage = CreatePageItem(job, "S102");
            OurPlanCoreJobStore.SavePageOverlay(basePage.FolderPath, overlayPage.FolderPath, "#1E88E5", 0.42);
            OurPlanCoreJobStore.SavePageOverlayTransform(basePage.FolderPath, 12.5, -7.25, 1.2, -4.5);
            OurPlanCoreJobStore.SavePageOverlayVisibility(basePage.FolderPath, false);

            string movedParent = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Moved");
            string movedOverlayPath = OurPlanCoreJobStore.MoveNode(overlayPage.FolderPath, movedParent);
            int changed = OurPlanCoreJobStore.RebasePageOverlayReferences(
                job.PagesRoot,
                [(overlayPage.FolderPath, movedOverlayPath)]);

            PageInfo updatedBase = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
                ?? throw new InvalidOperationException("base page missing after overlay rebase");
            AssertEqual("1", changed.ToString(), "overlay rebase count");
            AssertEqual(movedOverlayPath, updatedBase.OverlayPageFolder, "rebased overlay path");
            AssertClose(12.5, updatedBase.OverlayOffsetXPt, "overlay x survives rebase");
            AssertClose(-7.25, updatedBase.OverlayOffsetYPt, "overlay y survives rebase");
            AssertClose(1.2, updatedBase.OverlayScale, "overlay scale survives rebase");
            AssertClose(-4.5, updatedBase.OverlayRotationDegrees, "overlay rotation survives rebase");
            AssertFalse(updatedBase.OverlayVisible, "overlay visibility survives rebase");
        });
    }

    public static void PageSourceJsonRepairRestoresReciprocalOverlay()
    {
        WithTempJob("Repair Reciprocal Overlay", job =>
        {
            PageInfo basePage = CreatePageItem(job, "S201");
            PageInfo overlayPage = CreatePageItem(job, "S202");
            OurPlanCoreJobStore.SavePageOverlay(basePage.FolderPath, overlayPage.FolderPath, "#43A047", 0.82);
            OurPlanCoreJobStore.SavePageOverlayTransform(basePage.FolderPath, 10, -4, 1.25, 8);
            PageInfo syncedBase = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
                ?? throw new InvalidOperationException("base page missing before reciprocal sync");
            AssertTrue(SheetOverlayReciprocalService.TrySync(syncedBase, out _), "reciprocal overlay should sync");

            OurPlanCoreJobStore.WriteSourcePdfMetadata(
                basePage.FolderPath,
                new PdfSheetMetadata
                {
                    Source = "test",
                    PdfPath = basePage.PdfPath,
                    PageIndex = basePage.PdfPage,
                    PageNumber = basePage.PdfPage + 1,
                    SheetLabel = "S201",
                    RenameCandidate = "S201",
                    SelectedScaleMetersPerPt = 0.3048,
                });
            File.Delete(Path.Combine(basePage.FolderPath, "source.json"));

            PageInfo repaired = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
                ?? throw new InvalidOperationException("page source should repair from reciprocal overlay");

            AssertEqual(overlayPage.FolderPath, repaired.OverlayPageFolder, "repaired overlay path");
            AssertEqual("#43A047", repaired.OverlayColor, "repaired overlay color");
            AssertClose(0.82, repaired.OverlayOpacity, "repaired overlay opacity");
            AssertClose(10, repaired.OverlayOffsetXPt, "repaired overlay x");
            AssertClose(-4, repaired.OverlayOffsetYPt, "repaired overlay y");
            AssertClose(1.25, repaired.OverlayScale, "repaired overlay scale");
            AssertClose(8, repaired.OverlayRotationDegrees, "repaired overlay rotation");
        });
    }

    public static void PageAnnotationsSaveLoadNormalizeDefaults()
    {
        WithTempJob("Page Annotations", job =>
        {
            PageInfo page = CreatePageItem(job, "A200");
            var annotation = new PageAnnotation
            {
                Kind = "rect",
                Text = "Field note",
                Color = "",
                ScaleMetersPerPt = 0.25,
                Hidden = true,
                Points =
                [
                    new SKPoint(0, 0),
                    new SKPoint(10, 0),
                    new SKPoint(10, 10),
                    new SKPoint(0, 10),
                ],
            };

            OurPlanCoreJobStore.SavePageAnnotations(page.FolderPath, [annotation]);
            List<PageAnnotation> loaded = OurPlanCoreJobStore.LoadPageAnnotations(page.FolderPath);

            AssertEqual("1", loaded.Count.ToString(), "loaded annotation count");
            AssertEqual("rectangle", loaded[0].Kind, "annotation kind normalized");
            AssertEqual("Field note", loaded[0].Text, "annotation text preserved");
            AssertEqual("#1565C0", loaded[0].Color, "annotation default color");
            AssertClose(5.0, loaded[0].StrokeWidth, "annotation default stroke width");
            AssertEqual(page.FolderPath, loaded[0].PageFolder, "annotation page default");
            AssertClose(0.25, loaded[0].ScaleMetersPerPt, "annotation scale");
            AssertTrue(loaded[0].Hidden, "annotation hidden flag preserved");
            AssertEqual("4", loaded[0].Points.Count.ToString(), "annotation corner count preserved");
            AssertClose(10, loaded[0].Points[1].X, "annotation second corner x");
            AssertEqual("dimension", OurPlanCoreJobStore.NormalizePageAnnotationKind("ruler"), "ruler alias");
            AssertEqual("highlight", OurPlanCoreJobStore.NormalizePageAnnotationKind("highlighter"), "highlighter alias");
            AssertEqual("note", OurPlanCoreJobStore.NormalizePageAnnotationKind("text"), "text note alias");
        });
    }

    public static void PageAnnotationsUnchangedSaveIsIdempotent()
    {
        WithTempJob("Page Annotation Idempotence", job =>
        {
            PageInfo page = CreatePageItem(job, "A203");
            var annotation = new PageAnnotation
            {
                Kind = "line",
                PageFolder = page.FolderPath,
                Points = [new SKPoint(1, 2), new SKPoint(10, 20)],
            };

            OurPlanCoreJobStore.SavePageAnnotations(page.FolderPath, [annotation]);
            string path = OurPlanCoreJobStore.PageAnnotationsJsonPath(page.FolderPath);
            File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc));
            long beforeTicks = File.GetLastWriteTimeUtc(path).Ticks;

            OurPlanCoreJobStore.SavePageAnnotations(page.FolderPath, [annotation]);

            AssertEqual(
                beforeTicks.ToString(),
                File.GetLastWriteTimeUtc(path).Ticks.ToString(),
                "unchanged annotation save timestamp");
        });
    }

    public static void PageAnnotationsDeleteLastPersistsEmptyState()
    {
        WithTempJob("Page Annotation Delete Last", job =>
        {
            PageInfo page = CreatePageItem(job, "A204");
            var annotation = new PageAnnotation
            {
                Kind = "note",
                Text = "Delete me",
                PageFolder = page.FolderPath,
                Points = [new SKPoint(1, 2), new SKPoint(10, 20)],
            };

            OurPlanCoreJobStore.SavePageAnnotations(page.FolderPath, [annotation]);
            OurPlanCoreJobStore.SavePageAnnotations(page.FolderPath, Array.Empty<PageAnnotation>());

            string path = OurPlanCoreJobStore.PageAnnotationsJsonPath(page.FolderPath);
            AssertTrue(File.Exists(path), "delete-last annotation file should record the empty state");
            AssertEqual(
                "0",
                OurPlanCoreJobStore.LoadPageAnnotations(page.FolderPath).Count.ToString(),
                "delete-last annotation count");
        });
    }

    public static void PageAnnotationsFollowMovedPageFolder()
    {
        WithTempJob("Page Annotation Move", job =>
        {
            PageInfo page = CreatePageItem(job, "A202");
            var annotation = new PageAnnotation
            {
                Kind = "note",
                Text = "Move me",
                PageFolder = page.FolderPath,
                Points =
                [
                    new SKPoint(1, 1),
                    new SKPoint(20, 1),
                    new SKPoint(20, 12),
                    new SKPoint(1, 12),
                ],
            };

            OurPlanCoreJobStore.SavePageAnnotations(page.FolderPath, [annotation]);
            string targetParent = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Moved");
            string movedPath = OurPlanCoreJobStore.MoveNode(page.FolderPath, targetParent);

            List<PageAnnotation> loaded = OurPlanCoreJobStore.LoadPageAnnotations(movedPath);

            AssertEqual("1", loaded.Count.ToString(), "moved annotation count");
            AssertEqual("Move me", loaded[0].Text, "moved annotation text");
            AssertEqual(FullPath(movedPath), FullPath(loaded[0].PageFolder), "moved annotation page folder");
        });
    }

    public static void PageCorruptAnnotationsJsonIsQuarantined()
    {
        WithTempJob("Corrupt Annotations", job =>
        {
            PageInfo page = CreatePageItem(job, "A201");
            string path = OurPlanCoreJobStore.PageAnnotationsJsonPath(page.FolderPath);
            File.WriteAllText(path, "{ bad json");
            _ = OurPlanCoreJobStore.DrainCorruptJsonFiles();

            List<PageAnnotation> annotations = OurPlanCoreJobStore.LoadPageAnnotations(page.FolderPath);
            IReadOnlyList<string> quarantined = OurPlanCoreJobStore.DrainCorruptJsonFiles();

            AssertEqual("0", annotations.Count.ToString(), "corrupt annotations should load empty");
            AssertFalse(File.Exists(path), "corrupt annotations should be moved away");
            AssertEqual("1", Directory.GetFiles(page.FolderPath, "annotations.json.corrupt-*").Length.ToString(), "corrupt annotations count");
            AssertTrue(quarantined.Any(entry => entry.Contains("annotations.json", StringComparison.OrdinalIgnoreCase)), "quarantine report");
        });
    }

    public static void PageBookmarksSaveLoadUseJobRelativePageFolders()
    {
        WithTempJob("Page Bookmarks", job =>
        {
            PageInfo page = CreatePageItem(job, "A300");
            var bookmark = new PageBookmark
            {
                Id = "bookmark-1",
                Name = "Lobby detail",
                PageFolder = page.FolderPath,
                PageName = page.Name,
                Type = "crop_image",
                Zoom = 1.75f,
                PanX = 12.5f,
                PanY = -3.25f,
                CropImagePath = Path.Combine(job.RootPath, "bookmark_crops", "lobby.png"),
                CropLeft = 1,
                CropTop = 2,
                CropRight = 101,
                CropBottom = 202,
                CreatedAtUtc = "2026-01-01T00:00:00.0000000Z",
                UpdatedAtUtc = "2026-01-01T00:00:00.0000000Z",
            };

            OurPlanCoreJobStore.SavePageBookmarks(job, [bookmark]);
            string json = File.ReadAllText(OurPlanCoreJobStore.PageBookmarksJsonPath(job));
            List<PageBookmark> loaded = OurPlanCoreJobStore.LoadPageBookmarks(job);

            AssertTrue(json.Contains("Pages/", StringComparison.Ordinal), "bookmark page path should be job-relative");
            AssertTrue(json.Contains("bookmark_crops/lobby.png", StringComparison.Ordinal), "bookmark crop image path should be job-relative");
            AssertEqual("1", loaded.Count.ToString(), "loaded bookmark count");
            AssertEqual("Lobby detail", loaded[0].Name, "loaded bookmark name");
            AssertEqual("crop_image", loaded[0].Type, "loaded bookmark type");
            AssertEqual(FullPath(page.FolderPath), FullPath(loaded[0].PageFolder), "loaded bookmark page path");
            AssertEqual(FullPath(Path.Combine(job.RootPath, "bookmark_crops", "lobby.png")), FullPath(loaded[0].CropImagePath), "loaded crop image path");
            AssertClose(1.75, loaded[0].Zoom, "loaded bookmark zoom");
            AssertClose(12.5, loaded[0].PanX, "loaded bookmark pan x");
            AssertClose(-3.25, loaded[0].PanY, "loaded bookmark pan y");
            AssertClose(1, loaded[0].CropLeft, "loaded crop left");
            AssertClose(2, loaded[0].CropTop, "loaded crop top");
            AssertClose(101, loaded[0].CropRight, "loaded crop right");
            AssertClose(202, loaded[0].CropBottom, "loaded crop bottom");
        });
    }

    public static void PageCorruptBookmarksJsonIsQuarantined()
    {
        WithTempJob("Corrupt Bookmarks", job =>
        {
            string path = OurPlanCoreJobStore.PageBookmarksJsonPath(job);
            File.WriteAllText(path, "{ bad json");
            _ = OurPlanCoreJobStore.DrainCorruptJsonFiles();

            List<PageBookmark> bookmarks = OurPlanCoreJobStore.LoadPageBookmarks(job);
            IReadOnlyList<string> quarantined = OurPlanCoreJobStore.DrainCorruptJsonFiles();

            AssertEqual("0", bookmarks.Count.ToString(), "corrupt bookmarks should load empty");
            AssertFalse(File.Exists(path), "corrupt bookmarks should be moved away");
            AssertEqual("1", Directory.GetFiles(job.RootPath, "bookmarks.json.corrupt-*").Length.ToString(), "corrupt bookmarks count");
            AssertTrue(quarantined.Any(entry => entry.Contains("bookmarks.json", StringComparison.OrdinalIgnoreCase)), "quarantine report");
        });
    }

    public static void TakeoffSaveWritesCountersAndReloadsFallbackScale()
    {
        WithTempJob("Takeoff Save", job =>
        {
            PageInfo page = CreatePageItem(job, "A100");
            OurPlanCoreJobStore.SavePageScale(page.FolderPath, 0.5);
            TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Walls", "#FF0000", "line");
            item.Measurements.Add(new Measurement
            {
                MType = "line",
                PageFolder = page.FolderPath,
                Points = [new SKPoint(0, 0), new SKPoint(4, 0)],
            });
            item.Measurements.Add(new Measurement
            {
                MType = "point",
                PageFolder = page.FolderPath,
                Points = [new SKPoint(1, 1)],
            });

            OurPlanCoreJobStore.SaveTakeoffItem(item);
            TakeoffItem loaded = OurPlanCoreJobStore.TryReadTakeoffItem(item.FolderPath)
                ?? throw new InvalidOperationException("takeoff item missing");

            AssertEqual("2", ReadDataProperty(item.FolderPath, "MeasurementCount"), "measurement count property");
            AssertEqual("1", ReadDataProperty(item.FolderPath, "MeasuredPageCount"), "measured page count property");
            AssertEqual("2", loaded.Measurements.Count.ToString(), "loaded measurement count");
            AssertClose(0.5, loaded.Measurements[0].ScaleMetersPerPt, "fallback page scale");
        });
    }

    public static void CountDisplaySymbolPersistsOnTakeoffAndMeasurements()
    {
        WithTempJob("Count Symbol", job =>
        {
            PageInfo page = CreatePageItem(job, "A101");
            TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Holdowns", "#00AA44", "point");
            item.CountSymbol = CountDisplaySymbol.Cross;
            item.Measurements.Add(new Measurement
            {
                MType = "point",
                PageFolder = page.FolderPath,
                CountSymbol = CountDisplaySymbol.Cross,
                Points = [new SKPoint(4, 6)],
            });

            OurPlanCoreJobStore.SaveTakeoffItem(item);
            TakeoffItem loaded = OurPlanCoreJobStore.TryReadTakeoffItem(item.FolderPath)
                ?? throw new InvalidOperationException("takeoff item missing");

            AssertEqual(CountDisplaySymbol.Cross, loaded.CountSymbol, "takeoff count symbol");
            AssertEqual("1", loaded.Measurements.Count.ToString(), "loaded count measurement count");
            AssertEqual(CountDisplaySymbol.Cross, loaded.Measurements[0].CountSymbol, "measurement count symbol");
        });
    }

    public static void TakeoffCorruptMeasurementsJsonIsQuarantined()
    {
        WithTempJob("Corrupt Measurements", job =>
        {
            TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Bad", "#FF0000", "line");
            string path = Path.Combine(item.FolderPath, "measurements.json");
            File.WriteAllText(path, "{ bad json");
            _ = OurPlanCoreJobStore.DrainCorruptJsonFiles();

            List<Measurement> measurements = OurPlanCoreJobStore.LoadMeasurements(item.FolderPath);
            IReadOnlyList<string> quarantined = OurPlanCoreJobStore.DrainCorruptJsonFiles();

            AssertEqual("0", measurements.Count.ToString(), "corrupt measurements should load empty");
            AssertFalse(File.Exists(path), "corrupt measurements should be moved away");
            AssertEqual("1", Directory.GetFiles(item.FolderPath, "measurements.json.corrupt-*").Length.ToString(), "corrupt measurements count");
            AssertTrue(quarantined.Any(entry => entry.Contains("measurements.json", StringComparison.OrdinalIgnoreCase)), "quarantine report");
        });
    }

    public static void NodeSortUsesNaturalPageOrder()
    {
        WithTempJob("Natural Sort", job =>
        {
            string parent = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Sort Parent");
            string sourcePdf = CreateSourcePdf(job, "sort.pdf");
            OurPlanCoreJobStore.CreatePageFromPdf(job, sourcePdf, "A10", parent);
            OurPlanCoreJobStore.CreatePageFromPdf(job, sourcePdf, "A2", parent);
            OurPlanCoreJobStore.CreatePageFromPdf(job, sourcePdf, "A1", parent);

            OurPlanCoreJobStore.SortChildren(parent, descending: false);
            AssertEqual("A1,A2,A10", PageChildOrder(parent), "ascending natural order");

            OurPlanCoreJobStore.SortChildren(parent, descending: true);
            AssertEqual("A10,A2,A1", PageChildOrder(parent), "descending natural order");
        });
    }

    public static void DuplicatePageClonesPageAndRejectsFolder()
    {
        WithTempJob("Duplicate Page", job =>
        {
            PageInfo page = CreatePageItem(job, "D100");
            string duplicatedPath = OurPlanCoreJobStore.DuplicatePage(page.FolderPath);
            string secondDuplicatedPath = OurPlanCoreJobStore.DuplicatePage(page.FolderPath);
            PageInfo duplicate = OurPlanCoreJobStore.TryReadPage(duplicatedPath)
                ?? throw new InvalidOperationException("duplicate page missing");
            PageInfo secondDuplicate = OurPlanCoreJobStore.TryReadPage(secondDuplicatedPath)
                ?? throw new InvalidOperationException("second duplicate page missing");

            AssertFalse(string.Equals(page.FolderPath, duplicatedPath, StringComparison.OrdinalIgnoreCase), "duplicate should use new folder");
            AssertFalse(string.Equals(duplicatedPath, secondDuplicatedPath, StringComparison.OrdinalIgnoreCase), "second duplicate should use another hidden folder");
            AssertEqual("D100", OurPlanCoreJobStore.DisplayName(duplicatedPath), "duplicate display name");
            AssertEqual("D100", duplicate.Name, "duplicate page info name");
            AssertEqual("D100", OurPlanCoreJobStore.DisplayName(secondDuplicatedPath), "second duplicate display name");
            AssertEqual("D100", secondDuplicate.Name, "second duplicate page info name");
            AssertTrue(File.Exists(duplicate.PdfPath), "duplicate pdf should resolve");
            AssertTrue(File.Exists(secondDuplicate.PdfPath), "second duplicate pdf should resolve");

            string folder = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Folder Only");
            bool rejected = false;
            try
            {
                _ = OurPlanCoreJobStore.DuplicatePage(folder);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            AssertTrue(rejected, "folder duplicate should be rejected");
        });
    }

    public static void PageMultipleOverlayLayersPersistCopyReorderAndRebase()
    {
        WithTempJob("Multiple Overlay Layers", job =>
        {
            PageInfo basePage = CreatePageItem(job, "A100");
            PageInfo firstSource = CreatePageItem(job, "A101");
            PageInfo secondSource = CreatePageItem(job, "A102");

            OurPlanCoreJobStore.SavePageOverlay(
                basePage.FolderPath,
                firstSource.FolderPath,
                "#1E88E5",
                0.6);
            PageInfo firstState = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
                ?? throw new InvalidOperationException("first overlay state missing");
            string firstId = firstState.ActiveOverlayId;
            string secondId = OurPlanCoreJobStore.AddPageOverlay(
                basePage.FolderPath,
                secondSource.FolderPath,
                "#D81B60",
                0.35);
            OurPlanCoreJobStore.SavePageOverlayTransform(
                basePage.FolderPath,
                secondId,
                14,
                -9,
                1.15,
                7.5);
            OurPlanCoreJobStore.SavePageOverlayColor(
                basePage.FolderPath,
                secondId,
                "#123ABC");
            OurPlanCoreJobStore.SavePageOverlayOpacity(
                basePage.FolderPath,
                secondId,
                0.27);
            OurPlanCoreJobStore.SavePageOverlayVisibility(
                basePage.FolderPath,
                secondId,
                false);

            PageInfo layered = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
                ?? throw new InvalidOperationException("layered page missing");
            AssertEqual("2", layered.OverlayLayers.Count.ToString(), "overlay layer count");
            AssertEqual(secondId, layered.ActiveOverlayId, "new layer becomes active");
            AssertEqual("#123ABC", layered.OverlayColor, "active layer color");
            AssertClose(0.27, layered.OverlayOpacity, "active layer opacity");
            AssertClose(14, layered.OverlayOffsetXPt, "active layer x");
            AssertClose(-9, layered.OverlayOffsetYPt, "active layer y");
            AssertClose(1.15, layered.OverlayScale, "active layer scale");
            AssertClose(7.5, layered.OverlayRotationDegrees, "active layer rotation");
            AssertFalse(layered.OverlayVisible, "active layer visibility");
            AssertTrue(
                File.Exists(Path.Combine(basePage.FolderPath, "sheet_overlays.json")),
                "overlay manifest exists");

            SourceInfo mirror = OurPlanCoreJobStore.ReadSource(basePage.FolderPath)
                ?? throw new InvalidOperationException("legacy overlay mirror missing");
            AssertEqual(
                secondSource.FolderPath,
                Path.GetFullPath(Path.Combine(basePage.FolderPath, mirror.OverlayPageFolder)),
                "legacy source mirror tracks active layer");

            OurPlanCoreJobStore.MovePageOverlayLayer(basePage.FolderPath, secondId, -1);
            OurPlanCoreJobStore.SetActivePageOverlay(basePage.FolderPath, firstId);
            PageInfo reordered = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
                ?? throw new InvalidOperationException("reordered page missing");
            AssertEqual(secondId, reordered.OverlayLayers[0].Id, "layer moved down");
            AssertEqual(firstId, reordered.ActiveOverlayId, "active layer selection persists");

            string copyParent = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Copies");
            string copiedPath = OurPlanCoreJobStore.CopyNode(basePage.FolderPath, copyParent);
            PageInfo copied = OurPlanCoreJobStore.TryReadPage(copiedPath)
                ?? throw new InvalidOperationException("copied layered page missing");
            AssertEqual("2", copied.OverlayLayers.Count.ToString(), "copied overlay layer count");
            AssertEqual(firstId, copied.ActiveOverlayId, "copied active layer");

            string movedParent = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Moved");
            string movedSecondSource = OurPlanCoreJobStore.MoveNode(secondSource.FolderPath, movedParent);
            int changed = OurPlanCoreJobStore.RebasePageOverlayReferences(
                job.PagesRoot,
                [(secondSource.FolderPath, movedSecondSource)]);
            AssertEqual("2", changed.ToString(), "both layered targets rebase");
            PageInfo rebased = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
                ?? throw new InvalidOperationException("rebased layered page missing");
            AssertEqual(
                movedSecondSource,
                rebased.OverlayLayers[0].SourcePageFolder,
                "moved overlay source rebased");

            OurPlanCoreJobStore.RemovePageOverlay(basePage.FolderPath, secondId);
            PageInfo removed = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
                ?? throw new InvalidOperationException("layer removal state missing");
            AssertEqual("1", removed.OverlayLayers.Count.ToString(), "one overlay remains");
            AssertEqual(firstId, removed.ActiveOverlayId, "remaining layer stays active");
        });
    }

    private static void AssertPageSourceState(string pageFolder, string overlayPageFolder)
    {
        PageInfo page = OurPlanCoreJobStore.TryReadPage(pageFolder)
            ?? throw new InvalidOperationException("page missing");
        PageLayerManifest manifest = OurPlanCoreJobStore.ReadPageLayerManifest(pageFolder)
            ?? throw new InvalidOperationException("layer manifest missing");

        AssertTrue(File.Exists(page.PdfPath), "copied page pdf should resolve");
        AssertEqual(overlayPageFolder, page.OverlayPageFolder, "overlay page path should survive rewrite");
        AssertClose(12.5, page.OverlayOffsetXPt, "overlay x should survive rewrite");
        AssertClose(-7.25, page.OverlayOffsetYPt, "overlay y should survive rewrite");
        AssertClose(1.2, page.OverlayScale, "overlay scale should survive rewrite");
        AssertClose(-4.5, page.OverlayRotationDegrees, "overlay rotation should survive rewrite");
        AssertFalse(page.OverlayVisible, "overlay visibility should survive rewrite");
        AssertEqual("Walls,Joists", string.Join(",", page.HiddenTakeoffs), "hidden takeoffs should survive rewrite");
        AssertEqual("m-old-1,m-old-2", string.Join(",", page.HiddenMeasurements), "hidden measurements should survive rewrite");
        AssertEqual("1", manifest.LayerCount.ToString(), "layer manifest should survive rewrite");
    }

    private static PageInfo CreatePageItem(OurPlanCoreJob job, string name) =>
        OurPlanCoreJobStore.CreatePageFromPdf(job, CreateSourcePdf(job, "source.pdf"), name, job.PagesRoot);

    private static string CreateSourcePdf(OurPlanCoreJob job, string fileName)
    {
        string sourcePdf = Path.Combine(job.RootPath, fileName);
        if (!File.Exists(sourcePdf))
            File.WriteAllText(sourcePdf, "%PDF-1.4 test");
        return sourcePdf;
    }

    private static void WithTempJob(string name, Action<OurPlanCoreJob> action)
    {
        WithTempParent(parent =>
        {
            OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(parent, name);
            action(job);
        });
    }

    private static void WithTempParent(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "onc_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static string FullPath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string ReadDataProperty(string folder, string propertyName)
    {
        string path = Path.Combine(folder, "Data.xml");
        XElement root = XDocument.Load(path).Root
            ?? throw new InvalidOperationException("missing Data.xml root");
        return root.Element("Properties")?
            .Elements("Property")
            .FirstOrDefault(prop => string.Equals((string?)prop.Attribute("Name"), propertyName, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("Value")
            ?.Value ?? "";
    }

    private static string PageChildOrder(string parentFolder) =>
        string.Join(",", OurPlanCoreJobStore.GetOrderedChildDirectories(parentFolder)
            .Select(OurPlanCoreJobStore.DisplayName));

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
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }

    private static void AssertClose(double expected, double actual, string message, double tolerance = 0.000001)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
