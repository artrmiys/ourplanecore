using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private static readonly JsonSerializerOptions PasteTakeoffPropertyOptions = CreatePasteTakeoffPropertyOptions();
    internal Action? BeforeMeasurementPasteCommitForTests { get; set; }

    private bool EnsureMeasurementPasteWritable(PdfViewport viewport, PageInfo page)
    {
        if (!EnsureCurrentJobWritable("paste measurements", showDialog: false))
            return false;
        if (viewport.IsReadOnlyMode || !IsPathInsidePagesRoot(page.FolderPath))
        {
            TxtStatus.Text = "Cannot paste measurements: this sheet is read-only or belongs to another project.";
            return false;
        }
        return true;
    }

    private void AttachMeasurementPasteTakeoffUndo(
        PdfViewport viewport, OurPlanCoreJob job, IReadOnlyCollection<Measurement> pasted,
        IEnumerable<TakeoffItem> createdTargets)
    {
        var created = createdTargets.Distinct().Select(item => new PasteCreatedTakeoff(
            item, item.FolderPath, PasteTakeoffProperties(item))).ToArray();
        viewport.AttachAddedMeasurementsUndoCompletion(pasted, () => CompleteMeasurementPasteUndo(job, created, viewport));
    }

    private string? CompleteMeasurementPasteUndo(
        OurPlanCoreJob job, IReadOnlyList<PasteCreatedTakeoff> created, PdfViewport viewport)
    {
        try
        {
            string? note = created.Count == 0 ? null : RemoveUnchangedPasteTakeoffs(job, created);
            RefreshOtherViewportsAfterPaste(viewport);
            return note;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Could not refresh all sheets after undoing pasted measurements.");
            return "Measurements were undone; another sheet could not be refreshed: " + ex.Message;
        }
    }

    private string? RemoveUnchangedPasteTakeoffs(
        OurPlanCoreJob job, IReadOnlyList<PasteCreatedTakeoff> created)
    {
        if (!IsExpectedJobWritable(job))
            return "New takeoff items were kept because their project is no longer writable.";
        // Undo only owns these newly created, still-empty and unedited items.
        // Subsequent renames, property edits, added measurements or extra files remain intact.
        var removable = created.Where(snapshot =>
            _takeoffItems.Contains(snapshot.Item) && snapshot.Item.Measurements.Count == 0 &&
            string.Equals(snapshot.Item.FolderPath, snapshot.Folder, StringComparison.OrdinalIgnoreCase) &&
            PasteTakeoffProperties(snapshot.Item) == snapshot.Properties &&
            HasOnlyPasteTakeoffFiles(snapshot.Folder)).ToArray();
        if (removable.Length == 0)
            return "New takeoff items with later edits were kept.";
        try
        {
            // The normal measurement-removal callbacks have queued the empty state.
            // Flush it before moving folders so no delayed autosave can recreate them.
            FlushTakeoffAutosaves();
            foreach (PasteCreatedTakeoff snapshot in removable)
            {
                Exception? failure = MoveUncommittedTakeoffFoldersToRecovery([snapshot.Item]);
                if (failure != null) throw failure;
                if (FindTakeoffTreeItem(snapshot.Item) is { } treeItem) RemoveTreeItem(treeItem);
                _takeoffItems.Remove(snapshot.Item);
                if (ReferenceEquals(_activeItem, snapshot.Item))
                {
                    _activeItem = null;
                    _viewport.ActiveTakeoffFolder = "";
                }
            }
            RefreshEstimateTable();
            UpdateTotalDisplay();
            return removable.Length == created.Count ? null : "New takeoff items with later edits were kept.";
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Undo pasted measurements could not remove every unchanged new takeoff item.");
            return "Measurements were undone; some new takeoff items were kept: " + ex.Message;
        }
    }

    private static string PasteTakeoffProperties(TakeoffItem item) =>
        JsonSerializer.Serialize(item, PasteTakeoffPropertyOptions);

    private static JsonSerializerOptions CreatePasteTakeoffPropertyOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(TakeoffItem)) return;
            for (int i = info.Properties.Count - 1; i >= 0; i--)
                if (info.Properties[i].Name is nameof(TakeoffItem.Measurements) or nameof(TakeoffItem.HasPendingJoistDirections))
                    info.Properties.RemoveAt(i);
        });
        // A large paste must not serialize its geometry just to compare item properties.
        return new JsonSerializerOptions { TypeInfoResolver = resolver };
    }

    private static bool HasOnlyPasteTakeoffFiles(string folder)
    {
        try
        {
            return Directory.Exists(folder) && !Directory.EnumerateDirectories(folder).Any() &&
                Directory.EnumerateFiles(folder).All(path =>
                    string.Equals(Path.GetFileName(path), "Data.xml", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(path), "measurements.json", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private sealed record PasteCreatedTakeoff(TakeoffItem Item, string Folder, string Properties);
}
