using System.Text.Json;
using OurPlanCore;

internal static class OurPlanPackageSmokeHarness
{
    public static int Create()
    {
        string parent = Path.Combine(
            Path.GetTempPath(),
            "ourplancore-package-runtime-smoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(parent, "OurPlan Runtime Smoke");
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "g001", job.PagesRoot);
        string package = Path.Combine(parent, "OurPlan Runtime Smoke.ourplan");
        OurPlanPackageSession session = OurPlanPackageWriter.Create(job.RootPath, package, job.Name);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            root = parent,
            package = session.PackagePath,
            workspace = session.WorkspaceRoot,
            page = page.FolderPath,
            revision = session.BaseRevisionId,
        }));
        return 0;
    }
}
