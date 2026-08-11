using System.Text.Json;
using OurPlanCore;

internal static class RuntimeSmokeJobHarness
{
    public static int Create()
    {
        string parent = Path.Combine(
            Path.GetTempPath(),
            "ourplancore-runtime-smoke",
            Guid.NewGuid().ToString("N"));
        OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(parent, "IdealV3RuntimeSmoke");
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "g001 n", job.PagesRoot);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            root = parent,
            job = job.RootPath,
            page = page.FolderPath,
        }));
        return 0;
    }
}
