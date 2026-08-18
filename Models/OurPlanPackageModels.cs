using System.IO;
using System.Text.Json.Serialization;

namespace OurPlanCore;

public static class OurPlanPackageFormat
{
    public const string Extension = ".ourplan";
    public const string FormatId = "ourplancore.project";
    public const int SchemaVersion = 1;
    public const string ManifestEntryName = "manifest.json";
    public const string ObjectFolderName = "objects";
    public const string WorkspaceMarkerFileName = ".ourplan-workspace.json";
    public const string WorkspaceClaimFileName = ".ourplan-workspace.claim";

    public static bool HasPackageExtension(string path) =>
        string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);

    public static string EnsureExtension(string path) =>
        HasPackageExtension(path) ? path : path + Extension;

    public static string ObjectEntryName(string sha256) =>
        $"{ObjectFolderName}/{sha256.ToLowerInvariant()}";
}

public sealed class OurPlanPackageManifest
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = OurPlanPackageFormat.FormatId;

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = OurPlanPackageFormat.SchemaVersion;

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("revision_id")]
    public string RevisionId { get; set; } = "";

    [JsonPropertyName("parent_revision_id")]
    public string ParentRevisionId { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("created_utc")]
    public string CreatedUtc { get; set; } = "";

    [JsonPropertyName("saved_utc")]
    public string SavedUtc { get; set; } = "";

    [JsonPropertyName("files")]
    public List<OurPlanPackageFileManifest> Files { get; set; } = [];
}

public sealed class OurPlanPackageFileManifest
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("object_sha256")]
    public string ObjectSha256 { get; set; } = "";

    [JsonPropertyName("length")]
    public long Length { get; set; }

    [JsonPropertyName("last_write_utc_ticks")]
    public long LastWriteUtcTicks { get; set; }
}

public sealed class OurPlanPackageSession
{
    private bool _hasUnpackagedChanges;

    public required string PackagePath { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required string ProjectId { get; init; }
    public required string DisplayName { get; set; }
    public required string BaseRevisionId { get; set; }
    public required OurPlanPackageFingerprint BaseFingerprint { get; set; }
    public bool HasUnpackagedChanges
    {
        get => _hasUnpackagedChanges;
        set
        {
            bool changed = _hasUnpackagedChanges != value;
            _hasUnpackagedChanges = value;
            if (changed && value)
                DirtyStateChanged?.Invoke(this);
        }
    }

    public bool IsRecoverySession { get; internal set; }
    public string RecoveryReason { get; internal set; } = "";
    public IReadOnlyList<OurPlanPackageRecoveryInfo> AvailableRecoverySessions { get; internal set; } = [];

    internal string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    internal bool MarkerSessionOpen { get; set; } = true;
    internal string BaseInventoryRevisionId { get; set; } = "";
    internal List<OurPlanWorkspaceInventoryEntry> BaseInventory { get; set; } = [];
    internal Action<OurPlanPackageSession>? DirtyStateChanged { get; set; }
    internal OurPlanWorkspaceClaim? WorkspaceClaim { get; set; }
    internal string ClaimedMarkerSessionId { get; set; } = "";
    internal string ExpectedMarkerVersionToken { get; set; } = "";

    internal void SetDirtyWithoutNotification(bool value) => _hasUnpackagedChanges = value;
}

public readonly record struct OurPlanPackageFingerprint(
    long Length,
    long LastWriteUtcTicks,
    long ChangeTimeFileTime = 0,
    uint VolumeSerialNumber = 0,
    uint FileIdHigh = 0,
    uint FileIdLow = 0)
{
    public static OurPlanPackageFingerprint Read(string path)
    {
        OurPlanLocalFileStamp stamp = OurPlanLocalFileStamp.Read(path);
        return new OurPlanPackageFingerprint(
            stamp.Length,
            stamp.LastWriteUtcTicks,
            stamp.ChangeTimeFileTime,
            stamp.VolumeSerialNumber,
            stamp.FileIdHigh,
            stamp.FileIdLow);
    }
}

public sealed record OurPlanPackageSaveResult(
    string PackagePath,
    string RevisionId,
    int LogicalFileCount,
    int UniqueObjectCount,
    long SourceBytes,
    long PackageBytes);

public enum OurPlanRecoveryKind
{
    UnsavedChanges,
    InterruptedSession,
    PackageChanged,
    PackageUnavailable,
}

public sealed record OurPlanPackageRecoveryInfo(
    string PackagePath,
    string WorkspaceRoot,
    string ProjectId,
    string BaseRevisionId,
    string DisplayName,
    OurPlanRecoveryKind Kind,
    DateTime StateUpdatedUtc);

public sealed record OurPlanManagedWorkspaceReservation(
    string ProjectId,
    string RevisionId,
    string DisplayName,
    string ImportParentRoot,
    string ExpectedJobRoot,
    string WorkspaceRoot);

internal sealed class OurPlanWorkspaceInventoryEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("object_sha256")]
    public string ObjectSha256 { get; set; } = "";

    [JsonPropertyName("length")]
    public long Length { get; set; }

    [JsonPropertyName("last_write_utc_ticks")]
    public long LastWriteUtcTicks { get; set; }

    [JsonPropertyName("local_stamp")]
    public OurPlanLocalFileStamp? LocalStamp { get; set; }

    // Package bytes can intentionally differ from the live workspace bytes when
    // an absolute legacy reference is made portable in the archive. Keep both
    // identities so a clean workspace does not look permanently dirty.
    [JsonPropertyName("workspace_sha256")]
    public string WorkspaceSha256 { get; set; } = "";

    [JsonPropertyName("workspace_length")]
    public long WorkspaceLength { get; set; } = -1;

    [JsonPropertyName("workspace_last_write_utc_ticks")]
    public long WorkspaceLastWriteUtcTicks { get; set; }
}

internal sealed record OurPlanSavedWorkspaceFileState(
    OurPlanLocalFileStamp Stamp,
    string Sha256);

internal sealed class OurPlanWorkspaceMarker
{
    [JsonPropertyName("marker_schema_version")]
    public int MarkerSchemaVersion { get; set; } = 4;

    [JsonPropertyName("format")]
    public string Format { get; set; } = OurPlanPackageFormat.FormatId;

    [JsonPropertyName("package_path")]
    public string PackagePath { get; set; } = "";

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("revision_id")]
    public string RevisionId { get; set; } = "";

    [JsonPropertyName("extracted_utc")]
    public string ExtractedUtc { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("package_length")]
    public long PackageLength { get; set; }

    [JsonPropertyName("package_last_write_utc_ticks")]
    public long PackageLastWriteUtcTicks { get; set; }

    [JsonPropertyName("package_change_time_filetime")]
    public long PackageChangeTimeFileTime { get; set; }

    [JsonPropertyName("package_volume_serial")]
    public uint PackageVolumeSerialNumber { get; set; }

    [JsonPropertyName("package_file_id_high")]
    public uint PackageFileIdHigh { get; set; }

    [JsonPropertyName("package_file_id_low")]
    public uint PackageFileIdLow { get; set; }

    [JsonPropertyName("dirty")]
    public bool Dirty { get; set; }

    [JsonPropertyName("session_open")]
    public bool SessionOpen { get; set; }

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("marker_version_token")]
    public string MarkerVersionToken { get; set; } = "";

    [JsonPropertyName("process_id")]
    public int ProcessId { get; set; }

    [JsonPropertyName("process_start_utc_ticks")]
    public long ProcessStartUtcTicks { get; set; }

    [JsonPropertyName("state_updated_utc")]
    public string StateUpdatedUtc { get; set; } = "";

    [JsonPropertyName("base_inventory")]
    public List<OurPlanWorkspaceInventoryEntry> BaseInventory { get; set; } = [];
}

internal sealed record OurPlanPackageSourceFile(
    string FullPath,
    string LogicalPath,
    long Length,
    long LastWriteUtcTicks);

public class OurPlanPackageException : IOException
{
    public OurPlanPackageException(string message) : base(message)
    {
    }

    public OurPlanPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class OurPlanPackageConflictException : OurPlanPackageException
{
    public OurPlanPackageConflictException(string message) : base(message)
    {
    }
}

public sealed class OurPlanPackageValidationException : OurPlanPackageException
{
    public OurPlanPackageValidationException(string message) : base(message)
    {
    }

    public OurPlanPackageValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
