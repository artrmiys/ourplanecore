using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OurPlanCore;

public partial class MainWindow
{
    private static IEnumerable<int> BuildPreviewWarmupOrder(int count, int activeIndex, int warmupLimit)
    {
        if (count <= 0 || warmupLimit <= 0)
            yield break;

        int limit = Math.Clamp(warmupLimit, 0, count);
        bool[] emitted = new bool[count];

        int priorityLocalRadius = Math.Min(
            ViewportRenderPolicy.JobOpenPreviewWarmupPriorityLocalRadius,
            ViewportRenderPolicy.JobOpenPreviewWarmupLocalRadius);
        foreach (int index in BuildLocalPageWarmupOrder(count, activeIndex)
                     .Take(1 + (priorityLocalRadius * 2)))
        {
            if (TryMarkWarmupIndex(index, emitted))
                yield return index;
        }

        foreach (int index in BuildEvenlyDistributedWarmupIndexes(
                     count,
                     Math.Min(ViewportRenderPolicy.JobOpenPreviewWarmupSpreadAnchorCount, limit)))
        {
            if (TryMarkWarmupIndex(index, emitted))
                yield return index;
        }

        foreach (int index in BuildLocalPageWarmupOrder(count, activeIndex)
                     .Take(1 + (ViewportRenderPolicy.JobOpenPreviewWarmupLocalRadius * 2)))
        {
            if (TryMarkWarmupIndex(index, emitted))
                yield return index;
        }

        foreach (int index in BuildEvenlyDistributedWarmupIndexes(count, limit))
        {
            if (TryMarkWarmupIndex(index, emitted))
                yield return index;
        }

        foreach (int index in BuildLocalPageWarmupOrder(count, activeIndex))
        {
            if (TryMarkWarmupIndex(index, emitted))
                yield return index;
        }
    }

    private static IEnumerable<int> BuildLocalPageWarmupOrder(int count, int activeIndex)
    {
        if (count <= 0)
            yield break;

        bool[] emitted = new bool[count];
        if (activeIndex >= 0 && activeIndex < count)
        {
            emitted[activeIndex] = true;
            yield return activeIndex;

            for (int offset = 1; offset < count; offset++)
            {
                int next = activeIndex + offset;
                if (next < count)
                {
                    emitted[next] = true;
                    yield return next;
                }

                int previous = activeIndex - offset;
                if (previous >= 0)
                {
                    emitted[previous] = true;
                    yield return previous;
                }
            }
        }

        for (int i = 0; i < count; i++)
        {
            if (!emitted[i])
                yield return i;
        }
    }

    private static IEnumerable<int> BuildEvenlyDistributedWarmupIndexes(int count, int sampleCount)
    {
        if (count <= 0 || sampleCount <= 0)
            yield break;

        if (count <= sampleCount)
        {
            for (int i = 0; i < count; i++)
                yield return i;
            yield break;
        }

        var indexes = new SortedSet<int> { 0, count / 2, count - 1 };
        double step = (double)(count - 1) / Math.Max(1, sampleCount - 1);
        for (int i = 0; indexes.Count < sampleCount && i < sampleCount; i++)
            indexes.Add((int)Math.Round(i * step));

        for (int i = 0; indexes.Count < sampleCount && i < count; i++)
            indexes.Add(i);

        foreach (int index in indexes.Take(sampleCount))
            yield return index;
    }

    private static bool TryMarkWarmupIndex(int index, bool[] emitted)
    {
        if (index < 0 || index >= emitted.Length || emitted[index])
            return false;

        emitted[index] = true;
        return true;
    }

    private static IReadOnlyList<PageInfo> LoadPagesForPreviewPrefetch(string pagesRoot)
    {
        if (!Directory.Exists(pagesRoot))
            return Array.Empty<PageInfo>();

        var pages = new List<PageInfo>();
        LoadPagesForPreviewPrefetch(pagesRoot, pages);
        return pages;
    }

    private static void LoadPagesForPreviewPrefetch(string folderPath, List<PageInfo> pages)
    {
        if (!Directory.Exists(folderPath))
            return;

        if (OurPlanCoreJobStore.TryReadPage(folderPath) is { } page)
        {
            if (File.Exists(page.PdfPath))
                pages.Add(page);
            return;
        }

        foreach (string child in OurPlanCoreJobStore.GetOrderedChildDirectories(folderPath))
            LoadPagesForPreviewPrefetch(child, pages);
    }
}
