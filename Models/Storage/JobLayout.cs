using System.IO;

namespace OurPlaneCore;

internal static class JobLayout
{
    public static OurPlaneCoreJob CreateJob(string parentDir, string jobName)
    {
        string displayName = OurPlaneCoreJobStore.NormalizeDisplayName(jobName, 120);
        string root = Path.Combine(parentDir, OurPlaneCoreJobStore.SanitizeName(displayName, 120));
        Directory.CreateDirectory(root);
        OurPlaneCoreJobStore.WriteItemDataXml(root, "Folder", displayName, 0);

        EnsureBaseFolders(root, displayName);
        return LoadJob(root);
    }

    public static OurPlaneCoreJob LoadJob(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException(rootPath);

        if (!File.Exists(Path.Combine(rootPath, "Data.xml")))
            OurPlaneCoreJobStore.WriteItemDataXml(rootPath, "Folder", Path.GetFileName(rootPath), 0);

        string name = OurPlaneCoreJobStore.ReadName(rootPath) ?? Path.GetFileName(rootPath);
        var job = new OurPlaneCoreJob { Name = name, RootPath = rootPath };
        EnsureBaseFolders(rootPath, name);
        return job;
    }

    public static string EnsureFolder(string parentFolder, string name)
    {
        string displayName = OurPlaneCoreJobStore.NormalizeDisplayName(name, 120);
        string path = Path.Combine(parentFolder, OurPlaneCoreJobStore.SanitizeName(displayName, 120));
        Directory.CreateDirectory(path);
        string dataXml = Path.Combine(path, "Data.xml");
        if (!File.Exists(dataXml))
            OurPlaneCoreJobStore.WriteItemDataXml(path, "Folder", displayName, OurPlaneCoreJobStore.GetNextOrderIndex(parentFolder));
        return path;
    }

    public static string CreateFolder(string parentFolder, string name)
    {
        string displayName = OurPlaneCoreJobStore.NormalizeDisplayName(name, 120);
        string folderName = OurPlaneCoreJobStore.SanitizeName(displayName, 120);
        string path = Path.Combine(parentFolder, folderName);
        if (Directory.Exists(path))
            throw new IOException($"'{displayName}' already exists in this folder.");

        Directory.CreateDirectory(path);
        OurPlaneCoreJobStore.WriteItemDataXml(path, "Folder", displayName, OurPlaneCoreJobStore.GetNextOrderIndex(parentFolder));
        return path;
    }

    public static string CreateFolderAllowDuplicateName(string parentFolder, string name)
    {
        string displayName = OurPlaneCoreJobStore.NormalizeDisplayName(name, 120);
        string folderName = OurPlaneCoreJobStore.SanitizeName(displayName, 120);
        string path = OurPlaneCoreJobStore.UniqueDirectoryPath(Path.Combine(parentFolder, folderName));
        Directory.CreateDirectory(path);
        OurPlaneCoreJobStore.WriteItemDataXml(path, "Folder", displayName, OurPlaneCoreJobStore.GetNextOrderIndex(parentFolder));
        return path;
    }

    public static string DefaultImportFolder(OurPlaneCoreJob job)
    {
        return EnsureFolder(job.PagesRoot, "00. imported");
    }

    private static void EnsureBaseFolders(string rootPath, string jobName)
    {
        EnsureFolder(rootPath, "sources");
        string pages = EnsureFolder(rootPath, "Pages");
        EnsureFolder(pages, "--------others");
        EnsureFolder(rootPath, "Takeoffs");
        SmartContextStore.EnsureProjectContext(rootPath, jobName);
    }
}
