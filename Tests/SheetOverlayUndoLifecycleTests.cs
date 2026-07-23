using System.Reflection;
using OurPlanCore.Controls;
using SkiaSharp;

internal static class SheetOverlayUndoLifecycleTests
{
    public static void PreviewCannotCrossOverlayBinding()
    {
        RunOnStaThread(() =>
        {
            string target = Path.Combine(Path.GetTempPath(), "onc_overlay_binding_target");
            string sourceA = Path.Combine(Path.GetTempPath(), "onc_overlay_binding_source_a");
            string sourceB = Path.Combine(Path.GetTempPath(), "onc_overlay_binding_source_b");
            var viewport = CreateViewport(target, sourceA);

            try
            {
                viewport.SetSheetOverlaySelectionActive(true);
                int commits = 0;
                viewport.SheetOverlayTransformChanged += _ => commits++;
                AssertNotNull(
                    viewport.PreviewSheetOverlayTransform(8, 3, 1.1f, 4, preserveCenterForScale: false),
                    "overlay A preview should start");

                viewport.PrepareSheetOverlayReload(target, sourceB);
                viewport.SetSheetOverlay(
                    new SKBitmap(20, 10),
                    200,
                    100,
                    "Overlay B",
                    offsetXPt: 30,
                    offsetYPt: 6,
                    overlayPageFolder: sourceB);
                viewport.CommitSheetOverlayTransformPreview("stale preview");

                SheetOverlayTransformSnapshot current = viewport.CurrentSheetOverlayTransform() ??
                    throw new InvalidOperationException("replacement overlay transform is missing");
                AssertNear(30, current.OffsetXPt, "replacement overlay X");
                AssertNear(6, current.OffsetYPt, "replacement overlay Y");
                AssertEqual(0, commits, "old preview must not commit under the replacement source");
            }
            finally
            {
                viewport.ClearSheetOverlay();
            }
        });
    }

    public static void SameBindingReloadKeepsOverlayUndo()
    {
        RunOnStaThread(() =>
        {
            string target = Path.Combine(Path.GetTempPath(), "onc_overlay_reload_target");
            string source = Path.Combine(Path.GetTempPath(), "onc_overlay_reload_source");
            var viewport = CreateViewport(target, source);

            try
            {
                int commits = 0;
                viewport.SheetOverlayTransformChanged += _ => commits++;
                AssertTrue(
                    viewport.TryCommitSheetOverlayTransform(
                        target,
                        source,
                        12,
                        -4,
                        1.2f,
                        7,
                        "Overlay changed."),
                    "initial transform should commit");

                viewport.PrepareSheetOverlayReload(target, source);
                viewport.UndoLast();
                AssertEqual(1, commits, "Undo must wait without popping while the same overlay bitmap reloads");

                viewport.SetSheetOverlay(
                    new SKBitmap(20, 10),
                    200,
                    100,
                    "Reloaded overlay",
                    offsetXPt: 12,
                    offsetYPt: -4,
                    overlayScale: 1.2f,
                    overlayRotationDegrees: 7,
                    overlayPageFolder: source);
                viewport.UndoLast();

                SheetOverlayTransformSnapshot restored = viewport.CurrentSheetOverlayTransform() ??
                    throw new InvalidOperationException("reloaded overlay transform is missing");
                AssertNear(0, restored.OffsetXPt, "reloaded overlay undo X");
                AssertNear(0, restored.OffsetYPt, "reloaded overlay undo Y");
                AssertNear(1, restored.OverlayScale, "reloaded overlay undo scale");
                AssertNear(0, restored.OverlayRotationDegrees, "reloaded overlay undo rotation");
                AssertEqual(2, commits, "reloaded overlay must publish the preserved undo exactly once");
            }
            finally
            {
                viewport.ClearSheetOverlay();
            }
        });
    }

    public static void HostCommitCancelsPreviewIntoOneUndoAction()
    {
        RunOnStaThread(() =>
        {
            string target = Path.Combine(Path.GetTempPath(), "onc_overlay_atomic_target");
            string source = Path.Combine(Path.GetTempPath(), "onc_overlay_atomic_source");
            var viewport = CreateViewport(target, source);

            try
            {
                viewport.SetSheetOverlaySelectionActive(true);
                int commits = 0;
                viewport.SheetOverlayTransformChanged += _ => commits++;
                AssertNotNull(
                    viewport.PreviewSheetOverlayTransform(5, 2, 1, 0, preserveCenterForScale: false),
                    "interactive preview should start");
                AssertTrue(
                    viewport.TryCommitSheetOverlayTransform(
                        target,
                        source,
                        20,
                        9,
                        1,
                        0,
                        "Host transform."),
                    "host transform should replace the pending preview");
                AssertEqual(1, commits, "host transform must create one commit");

                viewport.UndoLast();
                SheetOverlayTransformSnapshot restored = viewport.CurrentSheetOverlayTransform() ??
                    throw new InvalidOperationException("overlay transform is missing after undo");
                AssertNear(0, restored.OffsetXPt, "atomic host undo X");
                AssertNear(0, restored.OffsetYPt, "atomic host undo Y");
                AssertEqual(2, commits, "one Ctrl+Z must restore the complete host transform");

                viewport.UndoLast();
                AssertEqual(2, commits, "there must not be a duplicate overlay undo entry");
            }
            finally
            {
                viewport.ClearSheetOverlay();
            }
        });
    }

    public static void OverlayBindingTracksTargetRebaseAndRejectsSourceMismatch()
    {
        RunOnStaThread(() =>
        {
            string target = Path.Combine(Path.GetTempPath(), "onc_overlay_rebase_target");
            string movedTarget = Path.Combine(Path.GetTempPath(), "onc_overlay_rebase_target_moved");
            string source = Path.Combine(Path.GetTempPath(), "onc_overlay_rebase_source");
            string movedSource = Path.Combine(Path.GetTempPath(), "onc_overlay_rebase_source_moved");
            var viewport = CreateViewport(target, source);

            try
            {
                AssertTrue(
                    viewport.HasSheetOverlayBinding(target, source),
                    "initial overlay binding should match its target and source");
                AssertTrue(
                    !viewport.HasSheetOverlayBinding(target, movedSource),
                    "a moved source path must not retain the old bitmap as a matching binding");
                AssertTrue(
                    viewport.TryRebindCurrentPageFolder(target, movedTarget, "", 0),
                    "active target page should rebind without reloading its unchanged PDF");
                AssertTrue(
                    viewport.HasSheetOverlayBinding(movedTarget, source),
                    "target-page rebind must keep the live overlay binding undoable");
                AssertTrue(
                    !viewport.HasSheetOverlayBinding(target, source),
                    "the old target path must stop matching after rebind");
            }
            finally
            {
                viewport.ClearSheetOverlay();
            }
        });
    }

    private static PdfViewport CreateViewport(string target, string source)
    {
        var viewport = new PdfViewport();
        SetPrivateField(viewport, "_pageFolder", target);
        viewport.SetSheetOverlay(
            new SKBitmap(20, 10),
            200,
            100,
            "Overlay",
            overlayPageFolder: source);
        return viewport;
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
            throw new InvalidOperationException(failure.Message, failure);
    }

    private static void SetPrivateField<T>(object instance, string name, T value)
    {
        FieldInfo field = instance.GetType().GetField(
            name,
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new MissingFieldException(instance.GetType().FullName, name);
        field.SetValue(instance, value);
    }

    private static void AssertNear(float expected, float actual, string message)
    {
        if (MathF.Abs(expected - actual) > 0.0001f)
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }

    private static void AssertEqual(int expected, int actual, string message)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }

    private static void AssertNotNull(object? value, string message)
    {
        if (value == null)
            throw new InvalidOperationException(message);
    }

    private static void AssertTrue(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }
}
