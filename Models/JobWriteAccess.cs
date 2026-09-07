using System;
using System.Collections.Generic;
using System.IO;

namespace OurPlanCore;

public enum JobAccessMode
{
    Closed,
    Writable,
    ReadOnly,
}

public readonly record struct JobAccessSessionToken(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;
}

public sealed class JobWriteDeniedException : IOException
{
    public JobWriteDeniedException(
        string path,
        string operation,
        string jobRoot,
        JobAccessMode accessMode)
        : base($"Cannot {operation}: job '{jobRoot}' is {Describe(accessMode)}.")
    {
        Path = path;
        Operation = operation;
        JobRoot = jobRoot;
        AccessMode = accessMode;
    }

    public string Path { get; }

    public string Operation { get; }

    public string JobRoot { get; }

    public JobAccessMode AccessMode { get; }

    private static string Describe(JobAccessMode mode) => mode switch
    {
        JobAccessMode.ReadOnly => "open read-only",
        JobAccessMode.Closed => "closed",
        _ => "not writable",
    };
}

/// <summary>
/// Process-wide write boundary for open jobs. Callers register a normalized
/// job root before loading it, retain the returned token, and use that token
/// for mode changes. Closed registrations deliberately remain as tombstones
/// so late background work cannot write to a job that was already switched.
/// </summary>
public static class JobWriteAccess
{
    private sealed class Registration
    {
        public required string RootPath { get; init; }
        public required JobAccessSessionToken Token { get; init; }
        public required JobAccessMode Mode { get; set; }
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<string, Registration> ByRoot =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Guid, Registration> ByToken = [];

    public static JobAccessSessionToken RegisterJob(string jobRoot, JobAccessMode mode)
    {
        if (mode == JobAccessMode.Closed)
            throw new ArgumentOutOfRangeException(nameof(mode), "A new job session must be writable or read-only.");

        string root = Normalize(jobRoot);
        lock (Sync)
        {
            if (ByRoot.TryGetValue(root, out Registration? current) &&
                current.Mode != JobAccessMode.Closed)
            {
                throw new InvalidOperationException($"Job '{root}' already has an active access session.");
            }

            if (current != null)
                ByToken.Remove(current.Token.Value);

            var token = new JobAccessSessionToken(Guid.NewGuid());
            var registration = new Registration
            {
                RootPath = root,
                Token = token,
                Mode = mode,
            };
            ByRoot[root] = registration;
            ByToken[token.Value] = registration;
            return token;
        }
    }

    public static void SetMode(JobAccessSessionToken token, JobAccessMode mode)
    {
        Registration registration = RegistrationFor(token);
        lock (Sync)
        {
            if (!ByToken.TryGetValue(token.Value, out Registration? current) ||
                !ReferenceEquals(current, registration))
            {
                throw new InvalidOperationException("The job access session is no longer current.");
            }

            if (current.Mode == JobAccessMode.Closed && mode != JobAccessMode.Closed)
                throw new InvalidOperationException("A closed job access session cannot be reopened.");
            if (current.Mode == JobAccessMode.ReadOnly && mode == JobAccessMode.Writable)
                throw new InvalidOperationException("A read-only job access session cannot be promoted to writable.");

            current.Mode = mode;
        }
    }

    public static void Close(JobAccessSessionToken token) =>
        SetMode(token, JobAccessMode.Closed);

    public static JobAccessMode GetMode(string path)
    {
        string fullPath = Normalize(path);
        lock (Sync)
            return FindRegistration(fullPath)?.Mode ?? JobAccessMode.Closed;
    }

    public static bool IsWriteAllowed(string path)
    {
        string fullPath = Normalize(path);
        lock (Sync)
        {
            Registration? registration = FindRegistration(fullPath);
            return registration == null ||
                registration.Mode == JobAccessMode.Writable ||
                IsLeaseMetadata(registration.RootPath, fullPath);
        }
    }

    public static void Demand(string path, string operation)
    {
        string fullPath = Normalize(path);
        lock (Sync)
        {
            Registration? registration = FindRegistration(fullPath);
            if (registration == null ||
                registration.Mode == JobAccessMode.Writable ||
                IsLeaseMetadata(registration.RootPath, fullPath))
            {
                return;
            }

            throw new JobWriteDeniedException(
                fullPath,
                string.IsNullOrWhiteSpace(operation) ? "write" : operation.Trim(),
                registration.RootPath,
                registration.Mode);
        }
    }

    internal static void ResetForTests()
    {
        lock (Sync)
        {
            ByRoot.Clear();
            ByToken.Clear();
        }
    }

    private static Registration RegistrationFor(JobAccessSessionToken token)
    {
        if (token.IsEmpty)
            throw new ArgumentException("The job access session token is empty.", nameof(token));

        lock (Sync)
        {
            return ByToken.TryGetValue(token.Value, out Registration? registration)
                ? registration
                : throw new InvalidOperationException("The job access session is no longer current.");
        }
    }

    private static Registration? FindRegistration(string fullPath)
    {
        Registration? best = null;
        foreach (Registration registration in ByRoot.Values)
        {
            if (!IsSameOrDescendant(registration.RootPath, fullPath))
                continue;

            if (best == null || registration.RootPath.Length > best.RootPath.Length)
                best = registration;
        }

        return best;
    }

    private static bool IsLeaseMetadata(string jobRoot, string fullPath)
    {
        string relative = Path.GetRelativePath(jobRoot, fullPath);
        return string.Equals(relative, ".~lock", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relative, ".~lock.guard", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relative, OurPlanPackageFormat.WorkspaceMarkerFileName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relative, OurPlanPackageFormat.WorkspaceClaimFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrDescendant(string root, string path)
    {
        if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase))
            return true;

        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A path is required.", nameof(path));

        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        if (!string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath;
    }
}
