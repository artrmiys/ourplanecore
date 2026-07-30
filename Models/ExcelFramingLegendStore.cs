using System;
using System.IO;

namespace OurPlanCore;

public static class ExcelFramingLegendStore
{
    public static string Load(OurPlanCoreJob? job)
    {
        if (job == null)
            return "";
        string path = PathFor(job);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch
        {
            return "";
        }
    }

    public static void Save(OurPlanCoreJob job, string text)
    {
        string path = PathFor(job);
        JobWriteAccess.Demand(path, "save Excel framing legend");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        IoUtil.WriteAllTextAtomic(
            path,
            (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    internal static string PathFor(OurPlanCoreJob job) =>
        Path.Combine(
            job.AIContextRoot,
            "settings",
            "excel_framing_legend.txt");
}
