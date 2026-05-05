using System;

namespace SmartTakeoffs;

public static class TakeoffCreationPolicy
{
    public static string NewItemParentFolder(SmartTakeoffsJob? job) =>
        job?.TakeoffsRoot ?? "";

    public static string NewFolderParentFolder(
        SmartTakeoffsJob? job,
        string? selectedFolder,
        string? selectedItemParentFolder,
        string? activeParentFolder,
        Func<string, bool> directoryExists)
    {
        if (job == null)
            return "";

        if (!string.IsNullOrWhiteSpace(selectedFolder))
            return selectedFolder;

        if (!string.IsNullOrWhiteSpace(selectedItemParentFolder))
            return selectedItemParentFolder;

        if (!string.IsNullOrWhiteSpace(activeParentFolder) &&
            directoryExists(activeParentFolder))
        {
            return activeParentFolder;
        }

        return job.TakeoffsRoot;
    }
}
