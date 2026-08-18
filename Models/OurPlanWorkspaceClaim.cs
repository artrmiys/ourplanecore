using System.IO;

namespace OurPlanCore;

internal sealed class OurPlanWorkspaceClaim : IDisposable
{
    private FileStream? _handle;

    public OurPlanWorkspaceClaim(string workspaceRoot, string sessionId, FileStream handle)
    {
        WorkspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        SessionId = sessionId;
        _handle = handle;
    }

    public string WorkspaceRoot { get; }
    public string SessionId { get; }
    public bool IsHeld => _handle != null;

    public bool Owns(string workspaceRoot, string sessionId) =>
        IsHeld &&
        SessionId.Equals(sessionId, StringComparison.Ordinal) &&
        WorkspaceRoot.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot)),
            StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        FileStream? handle = Interlocked.Exchange(ref _handle, null);
        handle?.Dispose();
    }
}
