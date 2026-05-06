using System.Collections.Generic;
using System.IO;

namespace OurPlaneCore;

public partial class MainWindow
{
    private IEnumerable<PageInfo> CollectPagesUnder(string path)
    {
        if (!Directory.Exists(path))
            yield break;

        if (OurPlaneCoreJobStore.TryReadPage(path) is { } page)
        {
            yield return page;
            yield break;
        }

        foreach (string child in OurPlaneCoreJobStore.GetOrderedChildDirectories(path))
        {
            foreach (PageInfo pageInfo in CollectPagesUnder(child))
                yield return pageInfo;
        }
    }
}
