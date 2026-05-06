using OurPlaneCore;

internal static class TreeExpansionStateTests
{
    public static void StartsCollapsedAndTracksUserOpenedPaths()
    {
        var state = new TreeExpansionState();
        string folder = Path.Combine(Path.GetTempPath(), "opc_tree_state", "A");

        AssertEqual("0", state.Count.ToString(), "new tree should start collapsed");
        AssertTrue(state.Add(folder), "first expand should add path");
        AssertFalse(state.Add(folder + Path.DirectorySeparatorChar), "same path with trailing separator should be duplicate");
        AssertTrue(state.Contains(folder), "expanded path should be tracked");
        AssertEqual("1", state.Count.ToString(), "duplicate path should not increase count");

        AssertTrue(state.Remove(folder), "collapse should remove path");
        AssertFalse(state.Contains(folder), "collapsed path should be absent");
    }

    public static void RestoresSnapshotAcrossReload()
    {
        string first = Path.Combine(Path.GetTempPath(), "opc_tree_state", "Reload", "A");
        string second = Path.Combine(Path.GetTempPath(), "opc_tree_state", "Reload", "B");
        var state = new TreeExpansionState();
        state.Add(first);
        state.Add(second);

        var restored = new TreeExpansionState();
        restored.ReplaceWith(state.Snapshot());

        AssertEqual("2", restored.Count.ToString(), "snapshot count");
        AssertTrue(restored.Contains(first), "first restored");
        AssertTrue(restored.Contains(second), "second restored");
    }

    public static void RebasesMovedDescendants()
    {
        string oldRoot = Path.Combine(Path.GetTempPath(), "opc_tree_state", "Old");
        string oldChild = Path.Combine(oldRoot, "Child");
        string untouched = Path.Combine(Path.GetTempPath(), "opc_tree_state", "Other");
        string newRoot = Path.Combine(Path.GetTempPath(), "opc_tree_state", "New");
        string newChild = Path.Combine(newRoot, "Child");
        var state = new TreeExpansionState();
        state.Add(oldRoot);
        state.Add(oldChild);
        state.Add(untouched);

        state.Rebase(oldRoot, newRoot);

        AssertFalse(state.Contains(oldRoot), "old root should be removed");
        AssertFalse(state.Contains(oldChild), "old child should be removed");
        AssertTrue(state.Contains(newRoot), "new root should be tracked");
        AssertTrue(state.Contains(newChild), "new child should be tracked");
        AssertTrue(state.Contains(untouched), "unrelated path should remain");
        AssertEqual("3", state.Count.ToString(), "rebase should preserve count");
    }

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
}
