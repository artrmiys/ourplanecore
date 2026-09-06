using System.IO;

namespace OurPlanCore;

internal static class JobLayout
{
    public static OurPlanCoreJob CreateJob(string parentDir, string jobName)
    {
        string displayName = OurPlanCoreJobStore.NormalizeDisplayName(jobName, 120);
        string root = Path.Combine(parentDir, OurPlanCoreJobStore.SanitizeName(displayName, 120));
        JobWriteAccess.Demand(root, "create job");
        Directory.CreateDirectory(root);
        OurPlanCoreJobStore.WriteItemDataXml(root, "Folder", displayName, 0);

        EnsureBaseFolders(root, displayName);
        return LoadJob(root);
    }

    public static OurPlanCoreJob LoadJob(string rootPath) =>
        LoadJob(rootPath, JobAccessMode.Writable);

    public static OurPlanCoreJob LoadJob(string rootPath, JobAccessMode accessMode)
    {
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException(rootPath);
        if (accessMode == JobAccessMode.Closed)
            throw new ArgumentOutOfRangeException(nameof(accessMode), "A closed job cannot be loaded.");
        if (accessMode == JobAccessMode.ReadOnly && JobOperationJournal.HasPending(rootPath))
            throw new IOException("A bulk operation is still pending. Retry when it completes or let the editor recover the project first.");

        // Drop any cached Data.xml from a previously-open job so stale paths
        // don't accumulate (and a job folder changed on disk re-reads cleanly).
        OurPlanCoreJobStore.ClearMetadataCache();

        if (accessMode == JobAccessMode.Writable)
        {
            JobOperationJournal.RecoverPending(rootPath);
            JobWriteAccess.Demand(rootPath, "prepare job storage");
            if (!File.Exists(Path.Combine(rootPath, "Data.xml")))
                OurPlanCoreJobStore.WriteItemDataXml(rootPath, "Folder", Path.GetFileName(rootPath), 0);
        }

        string name = OurPlanCoreJobStore.ReadName(rootPath) ?? Path.GetFileName(rootPath);
        var job = new OurPlanCoreJob { Name = name, RootPath = rootPath };
        if (accessMode == JobAccessMode.Writable)
            EnsureBaseFolders(rootPath, name);
        return job;
    }

    public static string EnsureFolder(string parentFolder, string name)
    {
        string displayName = OurPlanCoreJobStore.NormalizeDisplayName(name, 120);
        string path = Path.Combine(parentFolder, OurPlanCoreJobStore.SanitizeName(displayName, 120));
        JobWriteAccess.Demand(path, "ensure folder");
        Directory.CreateDirectory(path);
        string dataXml = Path.Combine(path, "Data.xml");
        if (!File.Exists(dataXml))
            OurPlanCoreJobStore.WriteItemDataXml(path, "Folder", displayName, OurPlanCoreJobStore.GetNextOrderIndex(parentFolder));
        return path;
    }

    public static string CreateFolder(string parentFolder, string name)
    {
        string displayName = OurPlanCoreJobStore.NormalizeDisplayName(name, 120);
        string folderName = OurPlanCoreJobStore.SanitizeName(displayName, 120);
        string path = Path.Combine(parentFolder, folderName);
        if (Directory.Exists(path))
            throw new IOException($"'{displayName}' already exists in this folder.");

        JobWriteAccess.Demand(path, "create folder");
        Directory.CreateDirectory(path);
        OurPlanCoreJobStore.WriteItemDataXml(path, "Folder", displayName, OurPlanCoreJobStore.GetNextOrderIndex(parentFolder));
        return path;
    }

    public static string CreateFolderAllowDuplicateName(string parentFolder, string name)
    {
        string displayName = OurPlanCoreJobStore.NormalizeDisplayName(name, 120);
        string folderName = OurPlanCoreJobStore.SanitizeName(displayName, 120);
        string path = OurPlanCoreJobStore.UniqueDirectoryPath(Path.Combine(parentFolder, folderName));
        JobWriteAccess.Demand(path, "create folder");
        Directory.CreateDirectory(path);
        OurPlanCoreJobStore.WriteItemDataXml(path, "Folder", displayName, OurPlanCoreJobStore.GetNextOrderIndex(parentFolder));
        return path;
    }

    public static string DefaultImportFolder(OurPlanCoreJob job)
    {
        return EnsureFolder(job.PagesRoot, "00. imported");
    }

    private static void EnsureBaseFolders(string rootPath, string jobName)
    {
        JobWriteAccess.Demand(rootPath, "prepare job folders");
        EnsureFolder(rootPath, "sources");
        string pages = EnsureFolder(rootPath, "Pages");
        EnsureFolder(pages, "--------others");
        EnsureFolder(rootPath, "Takeoffs");
        SmartContextStore.EnsureProjectContext(rootPath, jobName);
    }
}
