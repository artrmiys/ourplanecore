using System;
using System.IO;

namespace OurPlanCore;

public static class IoUtil
{
    private static readonly object[] AtomicWriteGates = CreateAtomicWriteGates();

    public static void WriteAllTextAtomic(string path, string contents)
    {
        lock (AtomicWriteGate(path))
            WriteAllTextAtomicLocked(path, contents);
    }

    private static void WriteAllTextAtomicLocked(string path, string contents)
    {
        string operation = $"write '{Path.GetFileName(path)}'";
        JobWriteAccess.Demand(path, operation);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string tempPath = string.IsNullOrWhiteSpace(directory)
            ? $"{path}.{Guid.NewGuid():N}.tmp"
            : Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, contents);
            JobWriteAccess.Demand(path, operation);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
                {
                    File.Move(tempPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            throw;
        }
    }

    public static void WriteStreamAtomic(string path, Action<Stream> writeContents)
    {
        ArgumentNullException.ThrowIfNull(writeContents);
        lock (AtomicWriteGate(path))
            WriteStreamAtomicLocked(path, writeContents);
    }

    private static void WriteStreamAtomicLocked(string path, Action<Stream> writeContents)
    {
        string operation = $"write '{Path.GetFileName(path)}'";
        JobWriteAccess.Demand(path, operation);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string tempPath = string.IsNullOrWhiteSpace(directory)
            ? $"{path}.{Guid.NewGuid():N}.tmp"
            : Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 1024 * 1024,
                       FileOptions.SequentialScan))
            {
                writeContents(output);
                output.Flush(flushToDisk: true);
            }

            JobWriteAccess.Demand(path, operation);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
                {
                    File.Move(tempPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            throw;
        }
    }

    private static object AtomicWriteGate(string path)
    {
        string key;
        try
        {
            key = Path.GetFullPath(path);
        }
        catch
        {
            key = path ?? "";
        }

        int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(key) & int.MaxValue;
        return AtomicWriteGates[hash % AtomicWriteGates.Length];
    }

    private static object[] CreateAtomicWriteGates()
    {
        var gates = new object[64];
        for (int index = 0; index < gates.Length; index++)
            gates[index] = new object();
        return gates;
    }
}
