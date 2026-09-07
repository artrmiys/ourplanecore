using System;
using System.Collections.Generic;
using System.Linq;

namespace OurPlanCore;

public enum ViewportRenderWorkKind
{
    Preview,
    FullPage,
    Layer,
    Detail,
    DetailPrefetch,
    RasterRefresh,
}

public enum ViewportRenderPriority
{
    Immediate,
    Interactive,
    Background,
}

public sealed record ViewportRenderWorkItem(
    long Sequence,
    ViewportRenderWorkKind Kind,
    ViewportRenderPriority Priority,
    string PageFolder,
    int PdfPage,
    float RenderScale,
    DateTime QueuedUtc);

public sealed record ViewportRenderSchedulerSnapshot(
    int ImmediateCount,
    int InteractiveCount,
    int BackgroundCount,
    long LastSequence)
{
    public int PendingCount => ImmediateCount + InteractiveCount + BackgroundCount;
}

public sealed class ViewportRenderScheduler
{
    private readonly object _gate = new();
    private readonly Queue<ViewportRenderWorkItem> _immediate = new();
    private readonly Queue<ViewportRenderWorkItem> _interactive = new();
    private readonly Queue<ViewportRenderWorkItem> _background = new();
    private long _sequence;

    public ViewportRenderWorkItem Enqueue(
        ViewportRenderWorkKind kind,
        ViewportRenderPriority priority,
        string pageFolder,
        int pdfPage,
        float renderScale,
        bool replaceSamePageAndKind = true)
    {
        lock (_gate)
        {
            if (replaceSamePageAndKind)
                RemoveMatching(kind, pageFolder, pdfPage);

            var item = new ViewportRenderWorkItem(
                ++_sequence,
                kind,
                priority,
                pageFolder ?? "",
                Math.Max(0, pdfPage),
                Math.Clamp(renderScale, 0.10f, 4.0f),
                DateTime.UtcNow);
            QueueFor(priority).Enqueue(item);
            return item;
        }
    }

    public bool TryDequeue(out ViewportRenderWorkItem? item)
    {
        lock (_gate)
        {
            if (TryDequeue(_immediate, out item) ||
                TryDequeue(_interactive, out item) ||
                TryDequeue(_background, out item))
            {
                return true;
            }

            item = null;
            return false;
        }
    }

    public int ClearPage(string pageFolder)
    {
        lock (_gate)
        {
            return RemoveWhere(item =>
                string.Equals(item.PageFolder, pageFolder ?? "", StringComparison.OrdinalIgnoreCase));
        }
    }

    public ViewportRenderSchedulerSnapshot CaptureSnapshot()
    {
        lock (_gate)
        {
            return new ViewportRenderSchedulerSnapshot(
                _immediate.Count,
                _interactive.Count,
                _background.Count,
                _sequence);
        }
    }

    private Queue<ViewportRenderWorkItem> QueueFor(ViewportRenderPriority priority) =>
        priority switch
        {
            ViewportRenderPriority.Immediate => _immediate,
            ViewportRenderPriority.Interactive => _interactive,
            _ => _background,
        };

    private void RemoveMatching(ViewportRenderWorkKind kind, string pageFolder, int pdfPage) =>
        RemoveWhere(item =>
            item.Kind == kind &&
            item.PdfPage == Math.Max(0, pdfPage) &&
            string.Equals(item.PageFolder, pageFolder ?? "", StringComparison.OrdinalIgnoreCase));

    private int RemoveWhere(Func<ViewportRenderWorkItem, bool> predicate)
    {
        int removed = 0;
        removed += FilterQueue(_immediate, predicate);
        removed += FilterQueue(_interactive, predicate);
        removed += FilterQueue(_background, predicate);
        return removed;
    }

    private static int FilterQueue(
        Queue<ViewportRenderWorkItem> queue,
        Func<ViewportRenderWorkItem, bool> predicate)
    {
        if (queue.Count == 0)
            return 0;

        var keep = queue.Where(item => !predicate(item)).ToList();
        int removed = queue.Count - keep.Count;
        queue.Clear();
        foreach (ViewportRenderWorkItem item in keep)
            queue.Enqueue(item);
        return removed;
    }

    private static bool TryDequeue(
        Queue<ViewportRenderWorkItem> queue,
        out ViewportRenderWorkItem? item)
    {
        if (queue.Count > 0)
        {
            item = queue.Dequeue();
            return true;
        }

        item = null;
        return false;
    }
}
