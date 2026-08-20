using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OurPlanCore;

internal static class OurPlanPackageHardeningTests
{
    public static void WorkspaceWatcherIgnoresOnlyControlAtomicTemps()
    {
        string root = Path.Combine(Path.GetTempPath(), "ourplan-watcher-root");
        MethodInfo predicate = typeof(MainWindow).GetMethod(
            "IsTransientPackageWorkspacePath",
            BindingFlags.NonPublic | BindingFlags.Static) ??
            throw new InvalidOperationException("Package workspace transient predicate was not found.");
        bool IsTransient(string path) =>
            predicate.Invoke(null, [root, path]) is true;
        const string token = "0123456789abcdef0123456789abcdef";
        string leaseTemp = Path.Combine(
            root,
            $".{JobLeaseFileStore.LeaseFileName}.{token}.tmp");
        string guardTemp = Path.Combine(
            root,
            $".{JobLeaseFileStore.GuardFileName}.{token}.tmp");
        string markerTemp = Path.Combine(
            root,
            $".{OurPlanPackageFormat.WorkspaceMarkerFileName}.{token}.tmp");
        string claimTemp = Path.Combine(
            root,
            $".{OurPlanPackageFormat.WorkspaceClaimFileName}.{token}.tmp");
        string leaseReplaceTemp = Path.Combine(
            root,
            $"{JobLeaseFileStore.LeaseFileName}~RF25433af.TMP");
        string guardReplaceTemp = Path.Combine(
            root,
            $"{JobLeaseFileStore.GuardFileName}~RF25433af.TMP");
        string markerReplaceTemp = Path.Combine(
            root,
            $"{OurPlanPackageFormat.WorkspaceMarkerFileName}~RF25433af.TMP");
        string claimReplaceTemp = Path.Combine(
            root,
            $"{OurPlanPackageFormat.WorkspaceClaimFileName}~RF25433af.TMP");
        string projectTemp = Path.Combine(root, $".source.json.{token}.tmp");
        string projectReplaceTemp = Path.Combine(root, "source.json~RF25433af.TMP");

        AssertTrue(
            IsTransient(leaseTemp),
            "lease heartbeat atomic temp must not dirty the package session");
        AssertTrue(
            IsTransient(guardTemp),
            "lease guard atomic temp must not dirty the package session");
        AssertTrue(
            IsTransient(markerTemp),
            "workspace marker atomic temp must not dirty the package session");
        AssertTrue(
            IsTransient(claimTemp),
            "workspace claim atomic temp must not dirty the package session");
        AssertTrue(
            IsTransient(leaseReplaceTemp),
            "lease heartbeat File.Replace temp must not dirty the package session");
        AssertTrue(
            IsTransient(guardReplaceTemp),
            "lease guard File.Replace temp must not dirty the package session");
        AssertTrue(
            IsTransient(markerReplaceTemp),
            "workspace marker File.Replace temp must not dirty the package session");
        AssertTrue(
            IsTransient(claimReplaceTemp),
            "workspace claim File.Replace temp must not dirty the package session");
        AssertFalse(
            IsTransient(projectTemp),
            "normal project atomic temp must remain visible to the package watcher");
        AssertFalse(
            IsTransient(projectReplaceTemp),
            "normal project File.Replace temp must remain visible to the package watcher");
    }

    public static void PortableThreeDAnnotationsAndPdfMetadataRoundTrip()
    {
        WithTempJob("portable-3d", (root, job) =>
        {
            string page = CreateFolder(job.PagesRoot, "A101", "Page");
            string takeoffA = CreateFolder(job.TakeoffsRoot, "Walls", "Folder");
            string takeoffB = CreateFolder(job.TakeoffsRoot, "Roof", "Folder");
            string pdf = Path.Combine(job.RootPath, "sources", "plans.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
            File.WriteAllBytes(pdf, Encoding.ASCII.GetBytes("%PDF-1.7\nportable\n%%EOF"));
            File.WriteAllText(
                Path.Combine(page, "source.json"),
                JsonSerializer.Serialize(new SourceInfo
                {
                    Pdf = Path.GetRelativePath(page, pdf),
                    Page = 0,
                }));
            OurPlanCoreJobStore.WriteSourcePdfMetadata(page, new PdfSheetMetadata
            {
                PdfPath = pdf,
                PageIndex = 0,
                PageNumber = 1,
            });
            string annotations = Path.Combine(page, "annotations.json");
            File.WriteAllText(annotations, JsonSerializer.Serialize(new
            {
                page_folder = page,
                annotations = Array.Empty<object>(),
            }));

            var model = new ThreeDWallModel
            {
                Walls =
                [
                    new ThreeDWallSegment
                    {
                        TakeoffFolder = takeoffA,
                        PageFolder = page,
                        GroupKey = $"L1|{takeoffA}",
                    },
                ],
                Slabs =
                [
                    new ThreeDFloorSlab
                    {
                        TakeoffFolder = $"{takeoffA}|{takeoffB}",
                        PageFolder = page,
                        GroupKey = takeoffA,
                    },
                ],
                RoofGuides = [new ThreeDRoofGuide { PageFolder = page }],
                RoofIssues = [new ThreeDRoofIssue { PageFolder = page }],
            };
            ThreeDModelStore.Save(job, model);
            string modelPath = ThreeDModelStore.ModelPath(job);
            byte[] sourceModel = File.ReadAllBytes(modelPath);
            byte[] sourceAnnotations = File.ReadAllBytes(annotations);

            string package = Path.Combine(root, "portable.ourplan");
            OurPlanPackageSession session = OurPlanPackageWriter.Create(job.RootPath, package, job.Name);
            AssertBytes(sourceModel, File.ReadAllBytes(modelPath), "3D source metadata changed");
            AssertBytes(sourceAnnotations, File.ReadAllBytes(annotations), "annotation source changed");

            string extracted = Path.Combine(root, "extracted");
            OurPlanPackageArchive.Extract(package, extracted);
            string portableJson = File.ReadAllText(Path.Combine(extracted, "3D_Context", "walls_model.json"));
            AssertFalse(portableJson.Contains(job.RootPath, StringComparison.OrdinalIgnoreCase),
                "package 3D metadata leaked its source root");
            AssertFalse(File.ReadAllText(Path.Combine(extracted, "Pages", "A101", "annotations.json"))
                    .Contains(job.RootPath, StringComparison.OrdinalIgnoreCase),
                "package annotations leaked their source root");

            var extractedJob = new OurPlanCoreJob { Name = job.Name, RootPath = extracted };
            ThreeDWallModel? loaded = ThreeDModelStore.Load(extractedJob);
            AssertTrue(loaded != null, "portable 3D model did not load");
            AssertPath(Path.Combine(extracted, "Takeoffs", "Walls"), loaded!.Walls[0].TakeoffFolder);
            AssertPath(Path.Combine(extracted, "Pages", "A101"), loaded.Walls[0].PageFolder);
            string[] slabFolders = loaded.Slabs[0].TakeoffFolder.Split('|');
            AssertPath(Path.Combine(extracted, "Takeoffs", "Walls"), slabFolders[0]);
            AssertPath(Path.Combine(extracted, "Takeoffs", "Roof"), slabFolders[1]);
            AssertPath(Path.Combine(extracted, "Pages", "A101"), loaded.RoofGuides[0].PageFolder);
            AssertPath(Path.Combine(extracted, "Pages", "A101"), loaded.RoofIssues[0].PageFolder);

            PdfSheetMetadata? metadata = OurPlanCoreJobStore.ReadSourcePdfMetadata(
                Path.Combine(extracted, "Pages", "A101"));
            AssertTrue(metadata != null, "portable source_pdf metadata did not load");
            AssertPath(Path.Combine(extracted, "sources", "plans.pdf"), metadata!.PdfPath);
            OurPlanPackageWorkspace.MarkSessionClosed(session);
        });
    }

    public static void AiOpaqueProviderIdsRemainPortable()
    {
        WithTempJob("ai-opaque", (root, job) =>
        {
            string responses = Path.Combine(job.AIContextRoot, "responses");
            Directory.CreateDirectory(responses);
            File.WriteAllText(
                Path.Combine(responses, "safe.openai.raw.json"),
                "{\"id\":\"resp.with:opaque/value\",\"nested\":{\"id\":\"also.opaque\"}}");
            string materials = Path.Combine(job.AIContextRoot, "materials");
            Directory.CreateDirectory(materials);
            File.WriteAllText(
                Path.Combine(materials, "materials_unique_by_page.json"),
                "{\"items\":[{\"id\":\"plans.pdf:p1:schedule:0\"}]}");
            string requests = Path.Combine(job.AIContextRoot, "requests");
            Directory.CreateDirectory(requests);
            File.WriteAllText(
                Path.Combine(requests, "walls_model.json"),
                "{\"id\":\"walls_model\",\"TakeoffFolder\":\"C:\\\\outside\"}");

            string package = Path.Combine(root, "opaque.ourplan");
            OurPlanPackageSession session = OurPlanPackageWriter.Create(job.RootPath, package, job.Name);
            _ = OurPlanPackageArchive.ReadManifest(package, verifyObjects: true);
            AssertFalse(session.HasUnpackagedChanges, "opaque provider IDs left the package dirty");
            OurPlanPackageWorkspace.MarkSessionClosed(session);
        });
    }

    public static void OpeningPortableProjectDoesNotDirtyTheProject()
    {
        WithTempJob("clean-ai-read", (root, job) =>
        {
            _ = SmartContextStore.EnsureProjectContext(job.RootPath, job.Name);
            string page = CreateFolder(job.PagesRoot, "A101", "Page");
            string pdf = Path.Combine(job.RootPath, "sources", "plans.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
            File.WriteAllBytes(pdf, Encoding.ASCII.GetBytes("%PDF-1.7\nclean close\n%%EOF"));
            File.WriteAllText(
                Path.Combine(page, "source.json"),
                JsonSerializer.Serialize(new SourceInfo
                {
                    Pdf = Path.GetRelativePath(page, pdf),
                    Page = 0,
                    ScaleMetersPerPt = 0.01,
                    PdfLayersCached = true,
                    PdfLayers = [new PdfLayerInfo { Number = 1, Name = "Plan", IsOn = true }],
                }));
            OurPlanCoreJobStore.SavePageScale(page, 0.02);
            OurPlanCoreJobStore.SavePageScale(page, 0.01);
            string package = Path.Combine(root, "clean-ai-read.ourplan");
            OurPlanPackageSession created = OurPlanPackageWriter.Create(
                job.RootPath,
                package,
                job.Name);
            OurPlanPackageWorkspace.MarkSessionClosed(created);

            OurPlanPackageSession opened = OurPlanPackageWorkspace.Open(package);
            string projectJson = Path.Combine(opened.WorkspaceRoot, "AI_Context", "project.json");
            byte[] before = File.ReadAllBytes(projectJson);
            OurPlanPackageFingerprint packageBefore = OurPlanPackageFingerprint.Read(package);
            var openedJob = new OurPlanCoreJob
            {
                Name = job.Name,
                RootPath = opened.WorkspaceRoot,
            };

            OurPlanCoreJob loadedJob = OurPlanCoreJobStore.LoadJob(
                openedJob.RootPath,
                JobAccessMode.Writable);
            _ = SmartContextStore.LoadHiddenMarkerTypes(loadedJob);
            string openedPage = Path.Combine(opened.WorkspaceRoot, "Pages", "A101");
            PageInfo? loadedPage = OurPlanCoreJobStore.TryReadPage(openedPage);
            AssertTrue(loadedPage != null, "clean-close fixture page did not load");
            OurPlanCoreJobStore.SavePageScale(openedPage, loadedPage!.ScaleMetersPerPt);
            AssertBytes(before, File.ReadAllBytes(projectJson), "opening the project rewrote project.json");
            _ = OurPlanPackageWriter.Save(opened);
            AssertTrue(
                packageBefore == OurPlanPackageFingerprint.Read(package),
                "opening the project republished an unchanged package");
            OurPlanPackageWorkspace.MarkSessionClosed(opened);
        });
    }

    public static void UnsafeAiIdsAndEmbeddedPathsAreRejected()
    {
        string root = NewRoot();
        try
        {
            AssertRejectedWorkspace(
                root,
                "unsafe-id",
                new Dictionary<string, byte[]>
                {
                    ["AI_Context/requests/safe.json"] = Utf8("{\"id\":\"../escape\"}"),
                });
            AssertRejectedWorkspace(
                root,
                "external-bookmark",
                new Dictionary<string, byte[]>
                {
                    ["bookmarks.json"] = Utf8("{\"items\":[{\"page_folder\":\"C:\\\\Windows\"}]}"),
                });
            AssertRejectedWorkspace(
                root,
                "undo-injection",
                new Dictionary<string, byte[]>
                {
                    [".undo/hidden.json"] = Utf8("{}"),
                });
            OurPlanCoreJob idTestJob = OurPlanCoreJobStore.CreateJob(root, "id-test");
            AssertThrows<OurPlanPackageValidationException>(
                () => SmartContextFileId.JsonPath(
                    idTestJob,
                    "requests",
                    "../escape",
                    "unsafe test id"),
                "unsafe filename identifier escaped its parent");
        }
        finally
        {
            Delete(root);
        }
    }

    public static void OverwritingInvalidTargetPublishesSuccessfully()
    {
        WithTempJob("overwrite-invalid", (root, job) =>
        {
            string target = Path.Combine(root, "existing.ourplan");
            File.WriteAllText(target, "not a package");
            OurPlanPackageSession session = OurPlanPackageWriter.SaveAs(
                job.RootPath,
                target,
                job.Name,
                overwriteExisting: true);
            OurPlanPackageManifest manifest = OurPlanPackageArchive.ReadManifest(target, verifyObjects: true);
            AssertEqual(session.BaseRevisionId, manifest.RevisionId, "published revision mismatch");
            OurPlanPackageWorkspace.MarkSessionClosed(session);
        });
    }

    public static void OverwritingPackageWithCorruptObjectPublishesSuccessfully()
    {
        WithTempJob("overwrite-corrupt-object", (root, job) =>
        {
            string target = Path.Combine(root, "corrupt-object.ourplan");
            OurPlanPackageSession original = OurPlanPackageWriter.Create(job.RootPath, target, job.Name);
            OurPlanPackageWorkspace.MarkSessionClosed(original);
            OurPlanPackageManifest originalManifest = OurPlanPackageArchive.ReadManifest(
                target,
                verifyObjects: false);
            OurPlanPackageFileManifest payload = originalManifest.Files
                .First(file => file.Length > 0);
            string entryName = OurPlanPackageFormat.ObjectEntryName(payload.ObjectSha256);
            using (ZipArchive archive = ZipFile.Open(target, ZipArchiveMode.Update))
            {
                archive.GetEntry(entryName)!.Delete();
                ZipArchiveEntry replacement = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
                using Stream output = replacement.Open();
                byte[] corrupt = Enumerable.Repeat((byte)0xA5, checked((int)payload.Length)).ToArray();
                output.Write(corrupt);
            }
            AssertThrows<OurPlanPackageValidationException>(
                () => OurPlanPackageArchive.ReadManifest(target, verifyObjects: true),
                "corrupt package fixture unexpectedly verified");

            OurPlanPackageSession session = OurPlanPackageWriter.SaveAs(
                job.RootPath,
                target,
                job.Name,
                overwriteExisting: true);
            OurPlanPackageManifest manifest = OurPlanPackageArchive.ReadManifest(target, verifyObjects: true);
            AssertEqual(session.BaseRevisionId, manifest.RevisionId, "replacement revision mismatch");
            OurPlanPackageWorkspace.MarkSessionClosed(session);
        });
    }

    public static void LocalPublishStagingIsScopedByFullTargetPath()
    {
        WithTempJob("publish-staging", (root, job) =>
        {
            string firstTarget = Path.Combine(root, "A", "project.ourplan");
            string secondTarget = Path.Combine(root, "B", "project.ourplan");
            string firstStage = StagingFor(firstTarget);
            string secondStage = StagingFor(secondTarget);
            Directory.CreateDirectory(firstStage);
            Directory.CreateDirectory(secondStage);
            string staleFirst = Path.Combine(firstStage, $".project.ourplan.{Guid.NewGuid():N}.tmp");
            string staleSecond = Path.Combine(secondStage, $".project.ourplan.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(staleFirst, "stale A");
            File.WriteAllText(staleSecond, "stale B");
            File.SetLastWriteTimeUtc(staleFirst, DateTime.UtcNow.AddDays(-3));
            File.SetLastWriteTimeUtc(staleSecond, DateTime.UtcNow.AddDays(-3));

            Directory.CreateDirectory(Path.GetDirectoryName(firstTarget)!);
            OurPlanPackageSession session = OurPlanPackageWriter.Create(job.RootPath, firstTarget, job.Name);
            AssertFalse(File.Exists(staleFirst), "own stale package temp was not removed");
            AssertTrue(File.Exists(staleSecond), "same-name target cleanup crossed project identity");
            OurPlanPackageWorkspace.MarkSessionClosed(session);
            Delete(secondStage);
        });
    }

    public static void ExplicitRecoveryKeepsWorkspaceWithPartialJson()
    {
        string root = NewRoot();
        OurPlanPackageSession? recovered = null;
        string managedProjectRoot = "";
        try
        {
            (OurPlanCoreJob job, string projectId, _) =
                OurPlanPackageWorkspace.CreateNewJob("partial-recovery");
            managedProjectRoot = Path.GetDirectoryName(job.RootPath) ?? "";
            string package = Path.Combine(root, "partial-recovery.ourplan");
            OurPlanPackageSession original = OurPlanPackageWriter.SaveAs(
                job.RootPath,
                package,
                job.Name,
                overwriteExisting: false,
                projectId: projectId);
            string takeoff = CreateFolder(job.TakeoffsRoot, "Walls", "Item");
            File.WriteAllText(Path.Combine(takeoff, "measurements.json"), "{partial");
            OurPlanPackageWorkspace.MarkDirty(original);
            OurPlanPackageWorkspace.MarkSessionClosed(original);

            AssertTrue(
                OurPlanPackageWorkspace.TryOpenRecoverySession(package, out recovered) &&
                recovered != null,
                "partial authoritative JSON made the explicit recovery unreachable");
            AssertTrue(recovered!.IsRecoverySession, "recovered workspace was not marked as recovery");
            AssertTrue(
                File.Exists(Path.Combine(recovered.WorkspaceRoot, "Takeoffs", "Walls", "measurements.json")),
                "partial recovery data was discarded");
        }
        finally
        {
            if (recovered != null)
                OurPlanPackageWorkspace.MarkSessionClosed(recovered);
            Delete(root);
            if (!string.IsNullOrWhiteSpace(managedProjectRoot) &&
                Guid.TryParse(Path.GetFileName(managedProjectRoot), out _))
            {
                Delete(managedProjectRoot);
            }
        }
    }

    public static void RecoveryStillRejectsParseableExternalReferences()
    {
        WithTempJob("unsafe-recovery-reference", (_, job) =>
        {
            File.WriteAllText(
                Path.Combine(job.RootPath, "bookmarks.json"),
                "{\"items\":[{\"page_folder\":\"C:\\\\Windows\"}]}");
            AssertThrows<OurPlanPackageValidationException>(
                () => OurPlanPackagePortability.ValidateRecoveryReferences(job.RootPath),
                "parseable recovery metadata escaped the project root");
        });
    }

    public static void CrashRollbackIsNeverAutoDeleted()
    {
        WithTempJob("rollback-retention", (root, job) =>
        {
            string target = Path.Combine(root, "rollback-retention.ourplan");
            string stage = StagingFor(target);
            Directory.CreateDirectory(stage);
            string rollback = Path.Combine(
                stage,
                $".rollback-retention.ourplan.{Guid.NewGuid():N}.rollback.tmp");
            File.WriteAllText(rollback, "possible external cloud revision");
            File.SetLastWriteTimeUtc(rollback, DateTime.UtcNow.AddDays(-30));

            OurPlanPackageSession session = OurPlanPackageWriter.Create(
                job.RootPath,
                target,
                job.Name);
            AssertTrue(File.Exists(rollback), "unknown crash rollback was auto-deleted");
            OurPlanPackageWorkspace.MarkSessionClosed(session);
            Delete(stage);
        });
    }

    public static void PortableProvenanceDoesNotLeakWorkspacePaths()
    {
        WithTempJob("portable-provenance", (root, job) =>
        {
            string learning = Path.Combine(job.AIContextRoot, "learning");
            string materials = Path.Combine(job.AIContextRoot, "materials");
            Directory.CreateDirectory(learning);
            Directory.CreateDirectory(materials);
            string pdf = Path.Combine(job.RootPath, "sources", "plans.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
            File.WriteAllText(pdf, "pdf fixture");
            string feedback = Path.Combine(learning, "sheet_feedback.jsonl");
            string summary = Path.Combine(learning, "project_learning_summary.json");
            string material = Path.Combine(materials, "materials_unique_by_page.json");
            File.WriteAllText(
                feedback,
                JsonSerializer.Serialize(new { job_root = job.RootPath, source_pdf = pdf }) + "\n");
            File.WriteAllText(summary, JsonSerializer.Serialize(new { job_root = job.RootPath }));
            File.WriteAllText(
                material,
                JsonSerializer.Serialize(new
                {
                    source_path = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "private-source.pdf"),
                }));
            byte[] originalFeedback = File.ReadAllBytes(feedback);
            byte[] originalSummary = File.ReadAllBytes(summary);
            byte[] originalMaterial = File.ReadAllBytes(material);

            string package = Path.Combine(root, "portable-provenance.ourplan");
            OurPlanPackageSession session = OurPlanPackageWriter.Create(job.RootPath, package, job.Name);
            AssertBytes(originalFeedback, File.ReadAllBytes(feedback), "learning source was mutated");
            AssertBytes(originalSummary, File.ReadAllBytes(summary), "summary source was mutated");
            AssertBytes(originalMaterial, File.ReadAllBytes(material), "materials source was mutated");

            string extracted = Path.Combine(root, "provenance-extracted");
            OurPlanPackageArchive.Extract(package, extracted);
            string portableText = string.Join(
                "\n",
                File.ReadAllText(Path.Combine(extracted, "AI_Context", "learning", "sheet_feedback.jsonl")),
                File.ReadAllText(Path.Combine(extracted, "AI_Context", "learning", "project_learning_summary.json")),
                File.ReadAllText(Path.Combine(extracted, "AI_Context", "materials", "materials_unique_by_page.json")));
            AssertFalse(
                portableText.Contains(job.RootPath, StringComparison.OrdinalIgnoreCase),
                "portable provenance leaked its private workspace root");
            AssertTrue(portableText.Contains("\"job_root\":\".\"", StringComparison.Ordinal) ||
                       portableText.Contains("\"job_root\": \".\"", StringComparison.Ordinal),
                "portable provenance did not replace the job root");
            AssertTrue(portableText.Contains("private-source.pdf", StringComparison.Ordinal),
                "portable provenance lost the useful source basename");
            OurPlanPackageWorkspace.MarkSessionClosed(session);
        });
    }

    public static void BackgroundWritesStayBoundToCurrentProject()
    {
        string root = NewRoot();
        string first = Path.Combine(root, "first");
        string second = Path.Combine(root, "second");
        Directory.CreateDirectory(Path.Combine(first, "Pages", "A101"));
        Directory.CreateDirectory(Path.Combine(second, "Pages", "B101"));
        try
        {
            JobFileWriteActivity.SetCurrentJobRoot(first);
            using IDisposable? current = JobFileWriteActivity.TryBeginBackgroundWriteForProjectPath(
                Path.Combine(first, "Pages", "A101"));
            AssertTrue(current != null, "current project background write was rejected");
            current!.Dispose();

            JobFileWriteActivity.SetCurrentJobRoot(second);
            using IDisposable? stale = JobFileWriteActivity.TryBeginBackgroundWriteForProjectPath(
                Path.Combine(first, "Pages", "A101"));
            AssertTrue(stale == null, "stale project background write survived a job switch");
        }
        finally
        {
            JobFileWriteActivity.SetCurrentJobRoot(null);
            Delete(root);
        }
    }

    public static void LegacyPageFoldersStayPortableAndContained()
    {
        WithTempJob("legacy-page-contract", (root, job) =>
        {
            string page = CreateFolder(job.PagesRoot, "Legacy", "Folder");
            string pdf = Path.Combine(job.RootPath, "sources", "legacy.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
            File.WriteAllText(pdf, "%PDF legacy fixture");
            string sourcePath = Path.Combine(page, "source.json");
            File.WriteAllText(
                sourcePath,
                JsonSerializer.Serialize(new { pdf, page = 0 }));

            string package = Path.Combine(root, "legacy-page.ourplan");
            OurPlanPackageSession session = OurPlanPackageWriter.Create(job.RootPath, package, job.Name);
            string extracted = Path.Combine(root, "legacy-page-extracted");
            OurPlanPackageArchive.Extract(package, extracted);
            string portableSource = File.ReadAllText(
                Path.Combine(extracted, "Pages", "Legacy", "source.json"));
            AssertFalse(portableSource.Contains(job.RootPath, StringComparison.OrdinalIgnoreCase),
                "legacy page source kept its original absolute project path");
            PageInfo? loaded = OurPlanCoreJobStore.TryReadPage(
                Path.Combine(extracted, "Pages", "Legacy"));
            AssertTrue(loaded != null, "legacy Class=Folder page was not loadable after transfer");
            AssertPath(Path.Combine(extracted, "sources", "legacy.pdf"), loaded!.PdfPath);
            OurPlanPackageWorkspace.MarkSessionClosed(session);

            File.WriteAllText(
                sourcePath,
                JsonSerializer.Serialize(new { pdf = @"C:\Windows\external.pdf", page = 0 }));
            AssertThrows<OurPlanPackageValidationException>(
                () => OurPlanPackageWriter.Create(
                    job.RootPath,
                    Path.Combine(root, "unsafe-legacy-page.ourplan"),
                    job.Name),
                "legacy page accepted an external PDF reference");
        });
    }

    private static void AssertRejectedWorkspace(
        string parent,
        string name,
        IReadOnlyDictionary<string, byte[]> additions)
    {
        string package = Path.Combine(parent, name + ".ourplan");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["Data.xml"] = Utf8("<Item Class=\"Folder\" Name=\"Unsafe\" />"),
            ["Pages/Data.xml"] = Utf8("<Item Class=\"Folder\" Name=\"Pages\" />"),
            ["Takeoffs/Data.xml"] = Utf8("<Item Class=\"Folder\" Name=\"Takeoffs\" />"),
        };
        foreach ((string path, byte[] bytes) in additions)
            files[path] = bytes;
        WriteSyntheticPackage(package, files);
        string extracted = Path.Combine(parent, name + "-extracted");
        OurPlanPackageManifest manifest = OurPlanPackageArchive.ReadManifest(package, verifyObjects: true);
        OurPlanPackageArchive.Extract(package, extracted);
        AssertThrows<OurPlanPackageValidationException>(
            () => OurPlanPackageWorkspace.ValidateWorkspaceForOpen(
                extracted,
                manifest,
                requireExactManifestFiles: true),
            $"unsafe package '{name}' was accepted");
        Delete(extracted);
    }

    private static void WriteSyntheticPackage(
        string path,
        IReadOnlyDictionary<string, byte[]> files)
    {
        var manifest = new OurPlanPackageManifest
        {
            ProjectId = Guid.NewGuid().ToString("N"),
            RevisionId = Guid.NewGuid().ToString("N"),
            DisplayName = "Synthetic",
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            SavedUtc = DateTime.UtcNow.ToString("O"),
            Files = files.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
            {
                string sha = Convert.ToHexString(SHA256.HashData(item.Value)).ToLowerInvariant();
                return new OurPlanPackageFileManifest
                {
                    Path = item.Key,
                    ObjectSha256 = sha,
                    Length = item.Value.LongLength,
                    LastWriteUtcTicks = DateTime.UtcNow.Ticks,
                };
            }).ToList(),
        };
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (IGrouping<string, OurPlanPackageFileManifest> objectGroup in manifest.Files.GroupBy(
                     file => file.ObjectSha256,
                     StringComparer.OrdinalIgnoreCase))
        {
            OurPlanPackageFileManifest file = objectGroup.First();
            ZipArchiveEntry entry = archive.CreateEntry(OurPlanPackageFormat.ObjectEntryName(file.ObjectSha256));
            using Stream stream = entry.Open();
            stream.Write(files[file.Path]);
        }
        ZipArchiveEntry manifestEntry = archive.CreateEntry(OurPlanPackageFormat.ManifestEntryName);
        using Stream manifestStream = manifestEntry.Open();
        JsonSerializer.Serialize(manifestStream, manifest, OurPlanPackageArchive.JsonOptions);
    }

    private static string StagingFor(string target)
    {
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                Path.GetFullPath(target).ToUpperInvariant())))
            .ToLowerInvariant()[..32];
        return Path.Combine(AppIdentity.LocalRoot, "package-publish-staging", identity);
    }

    private static string CreateFolder(string parent, string name, string itemClass)
    {
        string path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        OurPlanCoreJobStore.WriteItemDataXml(path, itemClass, name, 0);
        return path;
    }

    private static void WithTempJob(string name, Action<string, OurPlanCoreJob> action)
    {
        string root = NewRoot();
        try
        {
            OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(root, name);
            action(root, job);
        }
        finally
        {
            Delete(root);
        }
    }

    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "ourplan_hardening_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static void AssertPath(string expected, string actual) =>
        AssertEqual(Path.GetFullPath(expected), Path.GetFullPath(actual), "portable path mismatch");

    private static void AssertBytes(byte[] expected, byte[] actual, string message)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
            throw new InvalidOperationException(message);
    }

    private static void AssertThrows<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!expected.Equals(actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
