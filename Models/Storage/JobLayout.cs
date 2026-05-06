using System.IO;

namespace OurPlaneCore;

internal static class JobLayout
{
    public static OurPlaneCoreJob CreateJob(string parentDir, string jobName)
    {
        string root = Path.Combine(parentDir, OurPlaneCoreJobStore.SanitizeName(jobName, 120));
        Directory.CreateDirectory(root);
        OurPlaneCoreJobStore.WriteItemDataXml(root, "Folder", jobName, 0);

        EnsureBaseFolders(root, jobName);
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
        string path = Path.Combine(parentFolder, OurPlaneCoreJobStore.SanitizeName(name, 120));
        Directory.CreateDirectory(path);
        string dataXml = Path.Combine(path, "Data.xml");
        if (!File.Exists(dataXml))
            OurPlaneCoreJobStore.WriteItemDataXml(path, "Folder", name, OurPlaneCoreJobStore.GetNextOrderIndex(parentFolder));
        return path;
    }

    public static string CreateFolder(string parentFolder, string name)
    {
        string cleanName = OurPlaneCoreJobStore.SanitizeName(name, 120);
        string path = Path.Combine(parentFolder, cleanName);
        if (Directory.Exists(path))
            throw new IOException($"'{cleanName}' already exists in this folder.");

        Directory.CreateDirectory(path);
        OurPlaneCoreJobStore.WriteItemDataXml(path, "Folder", cleanName, OurPlaneCoreJobStore.GetNextOrderIndex(parentFolder));
        return path;
    }

    public static string DefaultImportFolder(OurPlaneCoreJob job)
    {
        string imported = EnsureFolder(job.PagesRoot, "00. imported");
        return EnsureFolder(imported, "Arch");
    }

    private static void EnsureBaseFolders(string rootPath, string jobName)
    {
        EnsureFolder(rootPath, "sources");
        string pages = EnsureFolder(rootPath, "Pages");
        string imported = EnsureFolder(pages, "00. imported");
        EnsureFolder(imported, "Arch");
        EnsureFolder(imported, "Struct");
        EnsureFolder(pages, "--------others");
        EnsureFolder(rootPath, "Takeoffs");
        SmartContextStore.EnsureProjectContext(rootPath, jobName);
    }
}
