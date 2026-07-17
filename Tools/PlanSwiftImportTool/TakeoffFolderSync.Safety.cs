using System.Text.Json;
using Docnet.Core;
using Docnet.Core.Models;
using OurPlanCore;
using SkiaSharp;

internal static partial class TakeoffFolderSync
{
    private static JobAccessSessionToken ReplaceAccess(
        JobAccessSessionToken current,
        string jobRoot,
        JobAccessMode mode)
    {
        CloseAccess(ref current);
        return JobWriteAccess.RegisterJob(jobRoot, mode);
    }

    private static void CloseAccess(ref JobAccessSessionToken token)
    {
        if (token.IsEmpty)
            return;

        JobWriteAccess.Close(token);
        token = default;
    }

    private static HeldJobGuards HoldJobGuards(params string[] jobRoots)
    {
        var streams = new List<FileStream>();
        try
        {
            foreach (string jobRoot in jobRoots
                         .Select(Path.GetFullPath)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string leasePath = Path.Combine(jobRoot, ".~lock");
                if (File.Exists(leasePath))
                {
                    throw new InvalidOperationException(
                        $"Job is locked. Close OurPlanCore before syncing: {leasePath}");
                }

                string guardPath = Path.Combine(jobRoot, ".~lock.guard");
                var stream = new FileStream(
                    guardPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose | FileOptions.WriteThrough);
                streams.Add(stream);
                if (File.Exists(leasePath))
                {
                    throw new InvalidOperationException(
                        $"Job became locked while sync was starting: {leasePath}");
                }
            }

            return new HeldJobGuards(streams);
        }
        catch
        {
            foreach (FileStream stream in streams.AsEnumerable().Reverse())
                stream.Dispose();
            throw;
        }
    }

    private static void EnsurePhysicalJobTrees(params string[] jobRoots)
    {
        foreach (string jobRoot in jobRoots)
        {
            var pending = new Stack<string>();
            pending.Push(CanonicalizePathForComparison(jobRoot));
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                RejectReparsePoint(current, "job path");
                foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"Sync does not follow reparse points: {entry}");
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                        pending.Push(entry);
                }
            }
        }
    }

    private static string CanonicalizePathForComparison(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException($"Path has no root: {path}");
        string current = root;
        string relative = fullPath[root.Length..];
        string[] segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            string candidate = Path.Combine(current, segments[i]);
            if (!Directory.Exists(candidate))
            {
                for (int remaining = i; remaining < segments.Length; remaining++)
                    current = Path.Combine(current, segments[remaining]);
                break;
            }

            var directory = new DirectoryInfo(candidate);
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                FileSystemInfo resolved = directory.ResolveLinkTarget(returnFinalTarget: true)
                    ?? throw new InvalidOperationException(
                        $"Cannot resolve reparse point: {candidate}");
                current = resolved.FullName;
            }
            else
            {
                current = candidate;
            }
        }

        return Path.GetFullPath(current).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static void RejectReparsePoint(string path, string label)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"{label} is a reparse point: {path}");
    }

    private static HashSet<string> ValidateTargetMeasurementIds(
        IEnumerable<TakeoffItem> allTargetItems,
        string targetFolder)
    {
        var allIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outsideIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TakeoffItem item in allTargetItems)
        {
            ValidateMeasurementFile(item, requireMeasurements: false);
            bool outside = !IsInside(targetFolder, item.FolderPath);
            foreach (Measurement measurement in item.Measurements)
            {
                string id = NormalizeId(measurement.Id);
                if (string.IsNullOrWhiteSpace(id) || !allIds.Add(id))
                {
                    throw new InvalidOperationException(
                        $"Target job contains a missing or duplicate measurement ID: {id}");
                }

                if (outside)
                    outsideIds.Add(id);
            }
        }

        return outsideIds;
    }

    private static void ValidateCompatibleItems(
        TakeoffItem source,
        TakeoffItem target,
        string relativeItemPath)
    {
        string sourceType = OurPlanCoreJobStore.NormalizeMeasurementType(source.MeasurementType);
        string targetType = OurPlanCoreJobStore.NormalizeMeasurementType(target.MeasurementType);
        if (!string.Equals(source.Name, target.Name, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sourceType, targetType, StringComparison.OrdinalIgnoreCase) ||
            source.IsJoistArea != target.IsJoistArea)
        {
            throw new InvalidOperationException(
                $"Staged and target takeoff metadata do not match: {relativeItemPath}");
        }
    }

    private static void ValidateMeasurementFile(
        TakeoffItem item,
        bool requireMeasurements)
    {
        string path = Path.Combine(item.FolderPath, "measurements.json");
        if (!File.Exists(path))
        {
            if (requireMeasurements || item.Measurements.Count > 0)
                throw new FileNotFoundException("Required measurements.json is missing.", path);
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            int count;
            if (root.ValueKind == JsonValueKind.Array)
            {
                count = root.GetArrayLength();
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetProperty("measurements", out JsonElement measurements) &&
                     measurements.ValueKind == JsonValueKind.Array)
            {
                count = measurements.GetArrayLength();
            }
            else
            {
                throw new InvalidDataException("Unsupported measurements.json shape.");
            }

            if (count != item.Measurements.Count || (requireMeasurements && count == 0))
            {
                throw new InvalidDataException(
                    $"Loaded measurement count does not match {path}: " +
                    $"{item.Measurements.Count} loaded, {count} stored.");
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"Cannot safely sync unreadable measurement data: {path}. {ex.Message}",
                ex);
        }
    }

    private static Dictionary<string, Measurement> BuildMeasurementIdentityMap(
        OurPlanCoreJob job,
        TakeoffItem item)
    {
        var byIdentity =
            new Dictionary<string, Measurement>(StringComparer.OrdinalIgnoreCase);
        foreach (Measurement measurement in item.Measurements)
        {
            string identity = MeasurementIdentity(job, measurement);
            if (!byIdentity.TryAdd(identity, measurement))
            {
                throw new InvalidOperationException(
                    $"Target item has duplicate PlanSwift measurement identity: {item.Name}");
            }
        }

        return byIdentity;
    }

    private static string MeasurementIdentity(
        OurPlanCoreJob job,
        Measurement measurement)
    {
        string pageFolder = RequirePageInsideJob(job, measurement.PageFolder);
        string notes = (measurement.Notes ?? "").Trim();
        if (!notes.StartsWith("Imported from PlanSwift:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Measurement is missing a stable PlanSwift identity: {measurement.Id}");
        }

        string relativePage = Path.GetRelativePath(job.PagesRoot, pageFolder);
        string measurementType =
            OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType);
        return string.Join(
            "\u001F",
            relativePage,
            measurementType,
            (measurement.Name ?? "").Trim(),
            notes);
    }

    private static string RequirePageInsideJob(
        OurPlanCoreJob job,
        string pageFolder)
    {
        string fullPage = Path.GetFullPath(pageFolder);
        if (!IsInside(job.PagesRoot, fullPage) ||
            OurPlanCoreJobStore.TryReadPage(fullPage) == null)
        {
            throw new InvalidOperationException(
                $"Measurement page is outside or unreadable: {pageFolder}");
        }

        return fullPage;
    }

    private static bool MeasurementContentMatches(
        Measurement expected,
        Measurement actual)
    {
        return string.Equals(NormalizeId(expected.Id), NormalizeId(actual.Id), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(expected.Name, actual.Name, StringComparison.Ordinal) &&
            string.Equals(expected.Notes, actual.Notes, StringComparison.Ordinal) &&
            string.Equals(expected.MType, actual.MType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(expected.Color, actual.Color, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(expected.CountSymbol, actual.CountSymbol, StringComparison.OrdinalIgnoreCase) &&
            SameFullPath(expected.PageFolder, actual.PageFolder) &&
            SameFullPath(expected.TakeoffFolder, actual.TakeoffFolder) &&
            NearlyEqual(expected.ScaleMetersPerPt, actual.ScaleMetersPerPt) &&
            expected.JoistEnabled == actual.JoistEnabled &&
            string.Equals(expected.JoistType, actual.JoistType, StringComparison.Ordinal) &&
            NearlyEqual(expected.JoistSpacingInches, actual.JoistSpacingInches) &&
            NearlyEqual(expected.JoistDirectionDegrees, actual.JoistDirectionDegrees) &&
            expected.JoistDirectionLocked == actual.JoistDirectionLocked &&
            expected.JoistDirectionFollowsAreaRotation == actual.JoistDirectionFollowsAreaRotation &&
            expected.JoistAddEndJoist == actual.JoistAddEndJoist &&
            string.Equals(expected.JoistPitch, actual.JoistPitch, StringComparison.Ordinal) &&
            string.Equals(expected.JoistLengthRounding, actual.JoistLengthRounding, StringComparison.Ordinal) &&
            expected.JoistShowLabels == actual.JoistShowLabels &&
            expected.JoistDetailedLabels == actual.JoistDetailedLabels &&
            PointsMatch(expected.Points, actual.Points) &&
            HolesMatch(expected.Holes, actual.Holes);
    }

    private static bool HolesMatch(
        IReadOnlyList<List<SKPoint>> expected,
        IReadOnlyList<List<SKPoint>> actual)
    {
        if (expected.Count != actual.Count)
            return false;
        for (int i = 0; i < expected.Count; i++)
        {
            if (!PointsMatch(expected[i], actual[i]))
                return false;
        }

        return true;
    }

    private static bool PointsMatch(
        IReadOnlyList<SKPoint> expected,
        IReadOnlyList<SKPoint> actual)
    {
        if (expected.Count != actual.Count)
            return false;
        for (int i = 0; i < expected.Count; i++)
        {
            if (!NearlyEqual(expected[i].X, actual[i].X) ||
                !NearlyEqual(expected[i].Y, actual[i].Y))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameFullPath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static bool PdfPageDimensionsMatch(PageInfo source, PageInfo target)
    {
        (int Width, int Height) sourceSize = ReadPdfPageDimensions(source);
        (int Width, int Height) targetSize = ReadPdfPageDimensions(target);
        return Math.Abs(sourceSize.Width - targetSize.Width) <= 1 &&
            Math.Abs(sourceSize.Height - targetSize.Height) <= 1;
    }

    private static (int Width, int Height) ReadPdfPageDimensions(PageInfo page)
    {
        if (!File.Exists(page.PdfPath))
            throw new FileNotFoundException("Mapped page PDF is missing.", page.PdfPath);

        using var document = DocLib.Instance.GetDocReader(
            page.PdfPath,
            new PageDimensions(1));
        using var pageReader = document.GetPageReader(page.PdfPage);
        return (pageReader.GetPageWidth(), pageReader.GetPageHeight());
    }

    private static bool NearlyEqual(double left, double right)
    {
        double tolerance = Math.Max(1e-9, Math.Max(Math.Abs(left), Math.Abs(right)) * 1e-9);
        return Math.Abs(left - right) <= tolerance;
    }

    private sealed record MappedPage(
        string TargetFolder,
        PageInfo SourcePage,
        PageInfo TargetPage);

    private sealed class HeldJobGuards : IDisposable
    {
        private List<FileStream>? _streams;

        public HeldJobGuards(List<FileStream> streams)
        {
            _streams = streams;
        }

        public void Dispose()
        {
            List<FileStream>? streams = Interlocked.Exchange(ref _streams, null);
            if (streams == null)
                return;

            foreach (FileStream stream in streams.AsEnumerable().Reverse())
                stream.Dispose();
        }
    }
}
