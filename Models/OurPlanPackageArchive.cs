using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace OurPlanCore;

public static class OurPlanPackageArchive
{
    internal const int MaxManifestBytes = 32 * 1024 * 1024;
    internal const int MaxLogicalFiles = 50_000;
    internal const int MaxArchiveEntries = 50_001;
    internal const long MaxObjectBytes = 16L * 1024 * 1024 * 1024;
    internal const long MaxTotalProjectBytes = 128L * 1024 * 1024 * 1024;
    private const long ExtractionSafetyMarginBytes = 2L * 1024 * 1024 * 1024;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true,
    };

    public static OurPlanPackageManifest ReadManifest(string packagePath, bool verifyObjects = true)
    {
        string fullPath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The OurPlan project file does not exist.", fullPath);

        try
        {
            using FileStream stream = OpenPackageReadStream(fullPath);
            PreflightArchiveEntryCount(stream);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            OurPlanPackageManifest manifest = ReadAndValidateManifest(archive);
            if (verifyObjects)
                VerifyObjects(archive, manifest);
            return manifest;
        }
        catch (OurPlanPackageException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            throw new OurPlanPackageValidationException(
                $"'{Path.GetFileName(fullPath)}' is not a valid OurPlan project: {ex.Message}", ex);
        }
    }

    public static bool TryReadManifest(string packagePath, out OurPlanPackageManifest? manifest)
    {
        try
        {
            manifest = ReadManifest(packagePath, verifyObjects: false);
            return true;
        }
        catch
        {
            manifest = null;
            return false;
        }
    }

    public static void Extract(string packagePath, string destinationRoot)
    {
        string packageFullPath = Path.GetFullPath(packagePath);
        string destination = Path.GetFullPath(destinationRoot);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException($"The extraction folder is not empty: {destination}");

        try
        {
            using FileStream stream = OpenPackageReadStream(packageFullPath);
            PreflightArchiveEntryCount(stream);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            OurPlanPackageManifest manifest = ReadAndValidateManifest(archive);
            long totalLogicalBytes = TotalLogicalBytes(manifest);
            EnsureFreeSpace(destination, totalLogicalBytes);
            Directory.CreateDirectory(destination);

            var materializedObjects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long materializedLogicalBytes = 0;
            long decompressedObjectBytes = 0;
            foreach (OurPlanPackageFileManifest file in manifest.Files)
            {
                materializedLogicalBytes = AddToTotalQuota(
                    materializedLogicalBytes,
                    file.Length,
                    "The package expands beyond the maximum supported project size.");
                string outputPath = ResolveOutputPath(destination, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                if (materializedObjects.TryGetValue(file.ObjectSha256, out string? existingPath))
                {
                    File.Copy(existingPath, outputPath, overwrite: false);
                }
                else
                {
                    ZipArchiveEntry entry = archive.GetEntry(
                        OurPlanPackageFormat.ObjectEntryName(file.ObjectSha256))
                        ?? throw new OurPlanPackageValidationException(
                            $"Package object {file.ObjectSha256} is missing.");
                    ExtractAndVerify(
                        entry,
                        outputPath,
                        file.ObjectSha256,
                        file.Length,
                        ref decompressedObjectBytes);
                    materializedObjects[file.ObjectSha256] = outputPath;
                }

                File.SetLastWriteTimeUtc(outputPath, new DateTime(file.LastWriteUtcTicks, DateTimeKind.Utc));
            }
        }
        catch
        {
            TryDeleteIncompleteDirectory(destination);
            throw;
        }
    }

    internal static FileStream OpenPackageReadStream(string path) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            1024 * 1024,
            FileOptions.SequentialScan);

    internal static OurPlanPackageManifest ReadAndValidateManifest(ZipArchive archive)
    {
        Dictionary<string, ZipArchiveEntry> entries = BuildEntryIndex(archive);
        if (!entries.TryGetValue(OurPlanPackageFormat.ManifestEntryName, out ZipArchiveEntry? manifestEntry))
            throw new OurPlanPackageValidationException("The package manifest is missing.");
        ValidateManifestEntrySize(manifestEntry.Length);

        byte[] manifestBytes = ReadBoundedManifest(manifestEntry);
        OurPlanPackageManifest manifest = JsonSerializer.Deserialize<OurPlanPackageManifest>(
            manifestBytes,
            JsonOptions) ?? throw new OurPlanPackageValidationException("The package manifest is empty.");

        ValidateManifest(manifest);
        HashSet<string> expectedEntries = manifest.Files
            .Select(file => OurPlanPackageFormat.ObjectEntryName(file.ObjectSha256))
            .Append(OurPlanPackageFormat.ManifestEntryName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string expected in expectedEntries)
        {
            if (!entries.ContainsKey(expected))
                throw new OurPlanPackageValidationException($"Required package entry '{expected}' is missing.");
        }

        foreach (IGrouping<string, OurPlanPackageFileManifest> group in manifest.Files
                     .GroupBy(file => file.ObjectSha256, StringComparer.OrdinalIgnoreCase))
        {
            ZipArchiveEntry entry = entries[OurPlanPackageFormat.ObjectEntryName(group.Key)];
            ValidateObjectSize(entry.Length, $"Package object {group.Key}");
            if (entry.Length != group.First().Length)
            {
                throw new OurPlanPackageValidationException(
                    $"Package object {group.Key} has a declared length mismatch.");
            }
        }

        foreach (string actual in entries.Keys)
        {
            if (!expectedEntries.Contains(actual))
                throw new OurPlanPackageValidationException($"Unexpected package entry '{actual}'.");
        }

        return manifest;
    }

    internal static void ValidateManifest(OurPlanPackageManifest manifest)
    {
        if (!string.Equals(manifest.Format, OurPlanPackageFormat.FormatId, StringComparison.Ordinal))
            throw new OurPlanPackageValidationException("This file is not an OurPlanCore project package.");
        if (manifest.SchemaVersion != OurPlanPackageFormat.SchemaVersion)
        {
            throw new OurPlanPackageValidationException(
                $"Unsupported .ourplan schema {manifest.SchemaVersion}. This build supports schema {OurPlanPackageFormat.SchemaVersion}.");
        }
        if (!Guid.TryParse(manifest.ProjectId, out _) || !Guid.TryParse(manifest.RevisionId, out _))
            throw new OurPlanPackageValidationException("The package project or revision identifier is invalid.");
        if (!string.IsNullOrWhiteSpace(manifest.ParentRevisionId) &&
            !Guid.TryParse(manifest.ParentRevisionId, out _))
        {
            throw new OurPlanPackageValidationException("The package parent revision identifier is invalid.");
        }
        if (manifest.Files == null || manifest.Files.Count == 0 || manifest.Files.Count > MaxLogicalFiles)
            throw new OurPlanPackageValidationException("The package file list has an invalid size.");

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var objectLengths = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long totalLogicalBytes = 0;
        foreach (OurPlanPackageFileManifest file in manifest.Files)
        {
            string normalized = NormalizeLogicalPath(file.Path);
            if (!string.Equals(file.Path, normalized, StringComparison.Ordinal))
                throw new OurPlanPackageValidationException($"Non-canonical package path '{file.Path}'.");
            if (!paths.Add(normalized))
                throw new OurPlanPackageValidationException($"Duplicate or case-colliding package path '{file.Path}'.");
            if (!IsSha256(file.ObjectSha256))
                throw new OurPlanPackageValidationException($"Invalid object hash for '{file.Path}'.");
            if (file.Length < 0 || file.LastWriteUtcTicks < DateTime.MinValue.Ticks ||
                file.LastWriteUtcTicks > DateTime.MaxValue.Ticks)
            {
                throw new OurPlanPackageValidationException($"Invalid file metadata for '{file.Path}'.");
            }

            ValidateObjectSize(file.Length, $"Package object for '{file.Path}'");
            totalLogicalBytes = AddToTotalQuota(
                totalLogicalBytes,
                file.Length,
                "The package declares more than the maximum supported project size.");

            if (objectLengths.TryGetValue(file.ObjectSha256, out long existingLength) &&
                existingLength != file.Length)
            {
                throw new OurPlanPackageValidationException(
                    $"Object {file.ObjectSha256} has conflicting declared lengths.");
            }
            objectLengths[file.ObjectSha256] = file.Length;
        }

        if (!paths.Contains("Data.xml"))
            throw new OurPlanPackageValidationException("The package has no root Data.xml project file.");

        OurPlanPackageSemanticValidator.ValidateManifest(manifest.Files);
    }

    internal static string NormalizeLogicalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.Contains('\\') ||
            path.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(path))
        {
            throw new OurPlanPackageValidationException($"Unsafe package path '{path}'.");
        }

        string[] segments = path.Split('/');
        if (segments.Length == 0)
            throw new OurPlanPackageValidationException($"Unsafe package path '{path}'.");
        foreach (string segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
                segment.Length > 255 || segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsReservedDeviceName(segment))
            {
                throw new OurPlanPackageValidationException($"Unsafe package path '{path}'.");
            }
        }

        return string.Join('/', segments);
    }

    private static Dictionary<string, ZipArchiveEntry> BuildEntryIndex(ZipArchive archive)
    {
        ValidateArchiveEntryCount(archive.Entries.Count);

        var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long totalObjectBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(name) || name.EndsWith("/", StringComparison.Ordinal))
                throw new OurPlanPackageValidationException("Directory or unnamed ZIP entries are not allowed.");
            if (!result.TryAdd(name, entry))
                throw new OurPlanPackageValidationException($"Duplicate package entry '{name}'.");

            if (name.Equals(OurPlanPackageFormat.ManifestEntryName, StringComparison.OrdinalIgnoreCase))
            {
                ValidateManifestEntrySize(entry.Length);
            }
            else
            {
                ValidateObjectSize(entry.Length, $"Package ZIP entry '{name}'");
                totalObjectBytes = AddToTotalQuota(
                    totalObjectBytes,
                    entry.Length,
                    "The package ZIP entries expand beyond the maximum supported project size.");
            }
        }
        return result;
    }

    internal static void ValidateArchiveEntryCount(int entryCount)
    {
        if (entryCount <= 0 || entryCount > MaxArchiveEntries)
            throw new OurPlanPackageValidationException("The package ZIP entry list has an invalid size.");
    }

    internal static void PreflightArchiveEntryCount(Stream stream)
    {
        if (!stream.CanSeek)
            return;

        long originalPosition = stream.Position;
        try
        {
            const int endOfCentralDirectoryBytes = 22;
            long length = stream.Length;
            if (length < endOfCentralDirectoryBytes)
                return;

            int tailLength = (int)Math.Min(
                length,
                endOfCentralDirectoryBytes + (long)ushort.MaxValue);
            byte[] tail = new byte[tailLength];
            stream.Position = length - tailLength;
            stream.ReadExactly(tail);

            int endIndex = FindEndOfCentralDirectory(tail);
            if (endIndex < 0)
                return;

            ushort classicCount = BinaryPrimitives.ReadUInt16LittleEndian(
                tail.AsSpan(endIndex + 10, sizeof(ushort)));
            if (classicCount != ushort.MaxValue)
            {
                ValidateArchiveEntryCount(classicCount);
                return;
            }

            long endOffset = length - tailLength + endIndex;
            if (TryReadZip64EntryCount(stream, endOffset, out ulong zip64Count))
                ValidateRawArchiveEntryCount(zip64Count);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static int FindEndOfCentralDirectory(byte[] tail)
    {
        const uint signature = 0x06054b50;
        const int fixedBytes = 22;
        for (int index = tail.Length - fixedBytes; index >= 0; index--)
        {
            ReadOnlySpan<byte> candidate = tail.AsSpan(index);
            if (BinaryPrimitives.ReadUInt32LittleEndian(candidate) == signature &&
                index + fixedBytes +
                BinaryPrimitives.ReadUInt16LittleEndian(candidate.Slice(20, 2)) == tail.Length)
            {
                return index;
            }
        }
        return -1;
    }

    private static bool TryReadZip64EntryCount(Stream stream, long endOffset, out ulong entryCount)
    {
        entryCount = 0;
        const int locatorBytes = 20;
        long locatorOffset = endOffset - locatorBytes;
        Span<byte> locator = stackalloc byte[locatorBytes];
        if (!TryReadAt(stream, locatorOffset, locator) ||
            BinaryPrimitives.ReadUInt32LittleEndian(locator) != 0x07064b50)
        {
            return false;
        }

        ulong recordOffsetValue = BinaryPrimitives.ReadUInt64LittleEndian(locator.Slice(8, 8));
        if (recordOffsetValue > long.MaxValue)
            return false;
        long recordOffset = (long)recordOffsetValue;

        Span<byte> record = stackalloc byte[56];
        if (!TryReadAt(stream, recordOffset, record) ||
            BinaryPrimitives.ReadUInt32LittleEndian(record) != 0x06064b50)
        {
            return false;
        }

        ulong recordSize = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(4, 8));
        long bytesBeforeLocator = locatorOffset - recordOffset;
        if (bytesBeforeLocator < record.Length || recordSize != (ulong)(bytesBeforeLocator - 12))
            return false;

        entryCount = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(32, 8));
        return true;
    }

    private static bool TryReadAt(Stream stream, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset > stream.Length - buffer.Length)
            return false;
        stream.Position = offset;
        int readTotal = 0;
        while (readTotal < buffer.Length)
        {
            int read = stream.Read(buffer[readTotal..]);
            if (read == 0)
                return false;
            readTotal += read;
        }
        return true;
    }

    private static void ValidateRawArchiveEntryCount(ulong entryCount)
    {
        if (entryCount == 0 || entryCount > (ulong)MaxArchiveEntries)
            throw new OurPlanPackageValidationException("The package ZIP entry list has an invalid size.");
    }

    internal static void ValidateManifestEntrySize(long length)
    {
        if (length <= 0 || length > MaxManifestBytes)
            throw new OurPlanPackageValidationException("The package manifest has an invalid size.");
    }

    private static void ValidateObjectSize(long length, string label)
    {
        if (length < 0 || length > MaxObjectBytes)
        {
            throw new OurPlanPackageValidationException(
                $"{label} exceeds the maximum supported object size.");
        }
    }

    private static long AddToTotalQuota(long current, long increment, string message)
    {
        if (increment < 0 || current < 0 || increment > MaxTotalProjectBytes - current)
            throw new OurPlanPackageValidationException(message);
        return current + increment;
    }

    private static byte[] ReadBoundedManifest(ZipArchiveEntry entry)
    {
        int expectedLength = checked((int)entry.Length);
        byte[] bytes = new byte[expectedLength];
        using Stream input = entry.Open();
        int written = 0;
        while (written < bytes.Length)
        {
            int read = input.Read(bytes, written, bytes.Length - written);
            if (read == 0)
                break;
            written += read;
        }

        if (written != bytes.Length || input.ReadByte() != -1)
        {
            throw new OurPlanPackageValidationException(
                "The package manifest expands beyond or ends before its declared size.");
        }
        return bytes;
    }

    private static void VerifyObjects(ZipArchive archive, OurPlanPackageManifest manifest)
    {
        long decompressedBytes = 0;
        foreach (IGrouping<string, OurPlanPackageFileManifest> group in manifest.Files
                     .GroupBy(file => file.ObjectSha256, StringComparer.OrdinalIgnoreCase))
        {
            OurPlanPackageFileManifest file = group.First();
            ZipArchiveEntry entry = archive.GetEntry(OurPlanPackageFormat.ObjectEntryName(group.Key))
                ?? throw new OurPlanPackageValidationException($"Package object {group.Key} is missing.");
            if (entry.Length != file.Length)
                throw new OurPlanPackageValidationException($"Package object {group.Key} has the wrong length.");
            using Stream input = entry.Open();
            string actualHash = HashStream(input, file.Length, ref decompressedBytes);
            if (!actualHash.Equals(group.Key, StringComparison.OrdinalIgnoreCase))
                throw new OurPlanPackageValidationException($"Package object {group.Key} failed SHA-256 validation.");
        }
    }

    private static void ExtractAndVerify(
        ZipArchiveEntry entry,
        string outputPath,
        string expectedHash,
        long expectedLength,
        ref long totalDecompressedBytes)
    {
        using Stream input = entry.Open();
        using var output = new FileStream(
            outputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.SequentialScan);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        long written = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (read > expectedLength - written)
            {
                throw new OurPlanPackageValidationException(
                    $"Package object {expectedHash} expands beyond its declared length.");
            }
            totalDecompressedBytes = AddToTotalQuota(
                totalDecompressedBytes,
                read,
                "Package objects decompress beyond the maximum supported project size.");
            output.Write(buffer, 0, read);
            hash.AppendData(buffer, 0, read);
            written += read;
        }
        output.Flush(flushToDisk: true);

        string actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (written != expectedLength || !actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new OurPlanPackageValidationException(
                $"Package object {expectedHash} failed validation while extracting.");
        }
    }

    private static string HashStream(
        Stream input,
        long expectedLength,
        ref long totalDecompressedBytes)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        long readTotal = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (read > expectedLength - readTotal)
            {
                throw new OurPlanPackageValidationException(
                    "A package object expands beyond its declared length.");
            }
            totalDecompressedBytes = AddToTotalQuota(
                totalDecompressedBytes,
                read,
                "Package objects decompress beyond the maximum supported project size.");
            hash.AppendData(buffer, 0, read);
            readTotal += read;
        }
        if (readTotal != expectedLength)
            throw new OurPlanPackageValidationException("A package object ended before its declared length.");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string ResolveOutputPath(string destination, string logicalPath)
    {
        string path = Path.GetFullPath(Path.Combine(destination, logicalPath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = Path.TrimEndingDirectorySeparator(destination) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new OurPlanPackageValidationException($"Package path escapes the workspace: {logicalPath}");
        return path;
    }

    private static bool IsSha256(string value) =>
        value?.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsReservedDeviceName(string segment)
    {
        string stem = segment.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               (stem.Length == 4 &&
                (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                stem[3] is >= '1' and <= '9');
    }

    private static void EnsureFreeSpace(string destination, long requiredBytes)
    {
        if (requiredBytes < 0 || requiredBytes > MaxTotalProjectBytes)
        {
            throw new OurPlanPackageValidationException(
                "The package declares more than the maximum supported project size.");
        }

        try
        {
            string root = Path.GetPathRoot(destination) ?? "";
            if (string.IsNullOrWhiteSpace(root))
                return;
            long available = new DriveInfo(root).AvailableFreeSpace;
            if (requiredBytes > available ||
                available - requiredBytes < ExtractionSafetyMarginBytes)
            {
                throw new IOException(
                    $"Not enough free space to open this project. Required about {requiredBytes:N0} bytes; available {available:N0} bytes.");
            }
        }
        catch (ArgumentException)
        {
            // Some virtual/network roots do not expose free-space data.
        }
    }

    private static long TotalLogicalBytes(OurPlanPackageManifest manifest)
    {
        long total = 0;
        foreach (OurPlanPackageFileManifest file in manifest.Files)
        {
            total = AddToTotalQuota(
                total,
                file.Length,
                "The package declares more than the maximum supported project size.");
        }
        return total;
    }

    private static void TryDeleteIncompleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // The incomplete private workspace is harmless and can be cleaned later.
        }
    }
}
