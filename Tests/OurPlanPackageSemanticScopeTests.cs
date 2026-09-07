using System.Text;
using OurPlanCore;

internal static class OurPlanPackageSemanticScopeTests
{
    public static void OpaqueJsonAttachmentsRemainByteExact()
    {
        WithTempJob("opaque-json", (root, job) =>
        {
            var expected = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["attachments/vendor_payload.json"] = Utf8("{vendor-specific:not-json"),
                ["attachments/vendor_feed.jsonl"] = Utf8("first opaque row\nsecond opaque row\n"),
                ["attachments/source.json"] = Utf8("{opaque-page-like-name"),
                ["attachments/walls_model.json"] = Utf8("{opaque-3d-like-name"),
                ["attachments/Data.xml"] = Utf8("<vendor-not-ourplan"),
                ["attachments/AI_Context/requests/x.json"] = Utf8("{opaque-nested-ai-like-name"),
                ["AI_Context/vendor/plugin-state.json"] = Utf8("plugin=opaque;{]"),
                ["AI_Context/responses/provider.openai.raw.json"] = Utf8("not-json-provider-body"),
            };
            foreach ((string relative, byte[] bytes) in expected)
            {
                string path = Path.Combine(job.RootPath, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }

            string package = Path.Combine(root, "opaque-json.ourplan");
            OurPlanPackageSession session = OurPlanPackageWriter.Create(job.RootPath, package, job.Name);
            string extracted = Path.Combine(root, "extracted");
            OurPlanPackageManifest manifest = OurPlanPackageArchive.ReadManifest(package, verifyObjects: true);
            OurPlanPackageArchive.Extract(package, extracted);
            OurPlanPackageWorkspace.ValidateWorkspaceForOpen(
                extracted,
                manifest,
                requireExactManifestFiles: true);

            foreach ((string relative, byte[] bytes) in expected)
            {
                string extractedPath = Path.Combine(
                    extracted,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                AssertBytes(bytes, File.ReadAllBytes(extractedPath), $"opaque attachment changed: {relative}");
            }
            OurPlanPackageWorkspace.MarkSessionClosed(session);
        });
    }

    public static void MalformedAuthoritativeStoresAreRejected()
    {
        AssertMalformedAuthoritativeRejected(
            "takeoff-measurements",
            job =>
            {
                string takeoff = CreateItemFolder(job.TakeoffsRoot, "Walls", "Item");
                File.WriteAllText(Path.Combine(takeoff, "measurements.json"), "{broken");
            });
        AssertMalformedAuthoritativeRejected(
            "ai-observations",
            job =>
            {
                Directory.CreateDirectory(job.AIContextRoot);
                File.WriteAllText(
                    Path.Combine(job.AIContextRoot, "observations.jsonl"),
                    "{\"id\":\"valid\"}\nnot-json\n");
            });
        AssertMalformedAuthoritativeRejected(
            "job-settings",
            job =>
            {
                string settings = Path.Combine(job.AIContextRoot, "settings");
                Directory.CreateDirectory(settings);
                File.WriteAllText(Path.Combine(settings, "folder_template.json"), "[broken");
            });
    }

    public static void StructuredDataSizeLimitsRejectManifestBeforeExtraction()
    {
        var measurements = new OurPlanPackageFileManifest
        {
            Path = "Takeoffs/Walls/measurements.json",
            Length = 32L * 1024 * 1024 + 1,
        };
        AssertThrows<OurPlanPackageValidationException>(
            () => OurPlanPackageSemanticValidator.ValidateManifest([measurements]),
            "oversized measurements declaration was accepted");

        var project = new OurPlanPackageFileManifest
        {
            Path = "AI_Context/project.json",
            Length = 64L * 1024 * 1024 + 1,
        };
        AssertThrows<OurPlanPackageValidationException>(
            () => OurPlanPackageSemanticValidator.ValidateManifest([project]),
            "oversized app JSON declaration was accepted");

        // Unknown attachments remain governed only by the archive-wide quota.
        OurPlanPackageSemanticValidator.ValidateManifest(
        [
            new OurPlanPackageFileManifest
            {
                Path = "attachments/vendor_payload.json",
                Length = 64L * 1024 * 1024 + 1,
            },
        ]);
    }

    private static void AssertMalformedAuthoritativeRejected(
        string name,
        Action<OurPlanCoreJob> prepare)
    {
        WithTempJob(name, (root, job) =>
        {
            prepare(job);
            AssertThrows<OurPlanPackageValidationException>(
                () => OurPlanPackageWriter.Create(
                    job.RootPath,
                    Path.Combine(root, name + ".ourplan"),
                    job.Name),
                $"malformed authoritative store was accepted: {name}");
        });
    }

    private static string CreateItemFolder(string parent, string name, string itemClass)
    {
        string path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        OurPlanCoreJobStore.WriteItemDataXml(path, itemClass, name, 0);
        return path;
    }

    private static void WithTempJob(string name, Action<string, OurPlanCoreJob> action)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ourplan_semantic_scope_tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(root, name);
            action(root, job);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

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

    private static void TryDelete(string path)
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
}
