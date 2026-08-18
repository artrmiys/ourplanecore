using System.IO.Compression;
using OurPlanCore;

internal static class OurPlanPackageArchiveQuotaTests
{
    public static void LimitsAreHighButFinite()
    {
        AssertEqual(32L * 1024 * 1024, OurPlanPackageArchive.MaxManifestBytes,
            "manifest quota");
        AssertEqual(16L * 1024 * 1024 * 1024, OurPlanPackageArchive.MaxObjectBytes,
            "per-object quota");
        AssertEqual(128L * 1024 * 1024 * 1024, OurPlanPackageArchive.MaxTotalProjectBytes,
            "total project quota");
        AssertEqual(50_001, OurPlanPackageArchive.MaxArchiveEntries,
            "archive entry quota");

        OurPlanPackageArchive.ValidateArchiveEntryCount(OurPlanPackageArchive.MaxArchiveEntries);
        OurPlanPackageArchive.ValidateManifestEntrySize(OurPlanPackageArchive.MaxManifestBytes);
        AssertThrows<OurPlanPackageValidationException>(
            () => OurPlanPackageArchive.ValidateArchiveEntryCount(
                OurPlanPackageArchive.MaxArchiveEntries + 1),
            "archive entry quota was not enforced");
        AssertThrows<OurPlanPackageValidationException>(
            () => OurPlanPackageArchive.ValidateManifestEntrySize(
                OurPlanPackageArchive.MaxManifestBytes + 1L),
            "manifest entry quota was not enforced");

        RawZip64CountsArePreflighted();
    }

    public static void DeclaredObjectAndTotalSizesAreRejected()
    {
        OurPlanPackageManifest oversizedObject = BuildManifest(fileCount: 2);
        oversizedObject.Files[1].Length = OurPlanPackageArchive.MaxObjectBytes + 1;
        AssertThrows<OurPlanPackageValidationException>(
            () => OurPlanPackageArchive.ValidateManifest(oversizedObject),
            "oversized declared object was accepted");

        const int objectsAtTotalLimit = 8;
        OurPlanPackageManifest exactLimit = BuildManifest(objectsAtTotalLimit + 1);
        exactLimit.Files[0].Length = 0;
        foreach (OurPlanPackageFileManifest file in exactLimit.Files.Skip(1))
            file.Length = OurPlanPackageArchive.MaxObjectBytes;
        OurPlanPackageArchive.ValidateManifest(exactLimit);

        OurPlanPackageManifest overTotal = BuildManifest(objectsAtTotalLimit + 2);
        overTotal.Files[0].Length = 0;
        foreach (OurPlanPackageFileManifest file in overTotal.Files.Skip(1))
            file.Length = OurPlanPackageArchive.MaxObjectBytes;
        AssertThrows<OurPlanPackageValidationException>(
            () => OurPlanPackageArchive.ValidateManifest(overTotal),
            "declared total above the supported project size was accepted");
    }

    public static void OversizedCompressedManifestIsRejectedBeforeJsonRead()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ourplan_archive_quota_tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string package = Path.Combine(root, "oversized-manifest.ourplan");
        try
        {
            using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry(
                    OurPlanPackageFormat.ManifestEntryName,
                    CompressionLevel.Fastest);
                using Stream output = entry.Open();
                byte[] zeroes = new byte[1024 * 1024];
                int fullChunks = OurPlanPackageArchive.MaxManifestBytes / zeroes.Length;
                for (int index = 0; index < fullChunks; index++)
                    output.Write(zeroes);
                output.WriteByte(0);
            }

            AssertTrue(
                new FileInfo(package).Length < OurPlanPackageArchive.MaxManifestBytes,
                "test fixture should be compressed and small on disk");
            AssertThrows<OurPlanPackageValidationException>(
                () => OurPlanPackageArchive.ReadManifest(package, verifyObjects: false),
                "oversized expanded manifest was accepted");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static OurPlanPackageManifest BuildManifest(int fileCount)
    {
        var manifest = new OurPlanPackageManifest
        {
            ProjectId = Guid.NewGuid().ToString("N"),
            RevisionId = Guid.NewGuid().ToString("N"),
            DisplayName = "Quota fixture",
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            SavedUtc = DateTime.UtcNow.ToString("O"),
        };
        for (int index = 0; index < fileCount; index++)
        {
            manifest.Files.Add(new OurPlanPackageFileManifest
            {
                Path = index == 0 ? "Data.xml" : $"payload/{index:D6}.bin",
                ObjectSha256 = index.ToString("x64"),
                Length = 1,
                LastWriteUtcTicks = DateTime.UtcNow.Ticks,
            });
        }
        return manifest;
    }

    private static void RawZip64CountsArePreflighted()
    {
        using MemoryStream classic = BuildClassicEndRecord(entryCount: 1);
        OurPlanPackageArchive.PreflightArchiveEntryCount(classic);
        AssertEqual(0, classic.Position, "classic ZIP preflight changed the stream position");

        using MemoryStream supported = BuildZip64EndRecords(
            (ulong)OurPlanPackageArchive.MaxArchiveEntries);
        OurPlanPackageArchive.PreflightArchiveEntryCount(supported);
        AssertEqual(0, supported.Position, "ZIP64 preflight changed the stream position");

        using MemoryStream oversized = BuildZip64EndRecords(
            (ulong)OurPlanPackageArchive.MaxArchiveEntries + 1);
        oversized.Position = oversized.Length - sizeof(ushort);
        oversized.WriteByte(1);
        oversized.WriteByte(0);
        oversized.WriteByte(0x7f);
        oversized.Position = 0;
        OurPlanPackageValidationException directError = AssertThrows<OurPlanPackageValidationException>(
            () => OurPlanPackageArchive.PreflightArchiveEntryCount(oversized),
            "oversized raw ZIP64 entry count was not rejected before archive open");
        AssertTrue(
            directError.Message.Contains("ZIP entry list", StringComparison.Ordinal),
            "raw ZIP64 count used the wrong validation error");
        AssertEqual(0, oversized.Position, "failed ZIP64 preflight changed the stream position");

        string root = Path.Combine(
            Path.GetTempPath(),
            "ourplan_raw_zip64_quota_tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string package = Path.Combine(root, "oversized-count.ourplan");
            File.WriteAllBytes(package, oversized.ToArray());
            OurPlanPackageValidationException openError = AssertThrows<OurPlanPackageValidationException>(
                () => OurPlanPackageArchive.ReadManifest(package, verifyObjects: false),
                "ReadManifest reached ZipArchive before rejecting the raw ZIP64 count");
            AssertTrue(
                openError.Message.Contains("ZIP entry list", StringComparison.Ordinal),
                "ReadManifest did not report the raw entry-count preflight error");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static MemoryStream BuildClassicEndRecord(ushort entryCount)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x06054b50u);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(entryCount);
            writer.Write(entryCount);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((ushort)0);
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildZip64EndRecords(ulong entryCount)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x06064b50u);
            writer.Write(44UL);
            writer.Write((ushort)45);
            writer.Write((ushort)45);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(entryCount);
            writer.Write(entryCount);
            writer.Write(0UL);
            writer.Write(0UL);

            writer.Write(0x07064b50u);
            writer.Write(0u);
            writer.Write(0UL);
            writer.Write(1u);

            writer.Write(0x06054b50u);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(ushort.MaxValue);
            writer.Write(ushort.MaxValue);
            writer.Write(uint.MaxValue);
            writer.Write(uint.MaxValue);
            writer.Write((ushort)0);
        }
        stream.Position = 0;
        return stream;
    }

    private static T AssertThrows<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex)
        {
            return ex;
        }
        throw new InvalidOperationException(message);
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual(long expected, long actual, string message)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
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
