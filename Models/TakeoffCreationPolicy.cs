using System;

namespace OurPlanCore;

public static class TakeoffCreationPolicy
{
    public static string NewItemParentFolder(OurPlanCoreJob? job) =>
        job?.TakeoffsRoot ?? "";

    public static string NewFolderParentFolder(
        OurPlanCoreJob? job,
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
