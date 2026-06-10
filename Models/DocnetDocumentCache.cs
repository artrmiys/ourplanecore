using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;

namespace OurPlaneCore;

/// <summary>
/// Small LRU cache of parsed Docnet documents so repeated preview renders of
/// the same PDF skip the expensive re-open/re-parse step. Evicted readers are
/// disposed only once no render is using them.
/// </summary>
public static class DocnetDocumentCache
{
    private const int MaxEntries = 6;

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly LinkedList<Entry> Lru = new();

    internal sealed class Entry
    {
        public required string Key { get; init; }
        public required IDocReader Reader { get; init; }
        public object RenderLock { get; } = new();
        public int ActiveUses;
        public bool Evicted;
        public LinkedListNode<Entry>? LruNode;
    }

    public readonly struct Lease : IDisposable
    {
        private readonly Entry? _entry;

        internal Lease(Entry entry) => _entry = entry;

        public IDocReader Reader => _entry!.Reader;

        /// <summary>Serializes page renders on the shared cached reader.</summary>
        public object RenderLock => _entry!.RenderLock;

        public void Dispose()
        {
            if (_entry != null)
                Release(_entry);
        }
    }

    public static Lease Acquire(IDocLib docLib, string pdfPath, float scale)
    {
        string key = CacheKey(pdfPath, scale);
        lock (CacheLock)
        {
            if (Entries.TryGetValue(key, out Entry? cached))
                return LeaseLocked(cached);
        }

        // Parse outside the cache lock so one slow PDF does not block renders
        // of other documents.
        IDocReader reader = docLib.GetDocReader(pdfPath, new PageDimensions(scale));

        List<IDocReader>? evicted = null;
        try
        {
            lock (CacheLock)
            {
                if (Entries.TryGetValue(key, out Entry? raced))
                {
                    // Another thread parsed the same document first; keep theirs.
                    IDocReader duplicate = reader;
                    Lease lease = LeaseLocked(raced);
                    duplicate.Dispose();
                    return lease;
                }

                var entry = new Entry { Key = key, Reader = reader, ActiveUses = 1 };
                entry.LruNode = Lru.AddFirst(entry);
                Entries[key] = entry;
                evicted = TrimLocked();
                return new Lease(entry);
            }
        }
        finally
        {
            if (evicted != null)
            {
                foreach (IDocReader old in evicted)
                {
                    try { old.Dispose(); }
                    catch { /* native handle already gone */ }
                }
            }
        }
    }

    private static Lease LeaseLocked(Entry entry)
    {
        if (entry.LruNode != null)
        {
            Lru.Remove(entry.LruNode);
            Lru.AddFirst(entry.LruNode);
        }

        entry.ActiveUses++;
        return new Lease(entry);
    }

    private static List<IDocReader>? TrimLocked()
    {
        List<IDocReader>? evicted = null;
        while (Entries.Count > MaxEntries && Lru.Last != null)
        {
            Entry oldest = Lru.Last.Value;
            Lru.RemoveLast();
            oldest.LruNode = null;
            Entries.Remove(oldest.Key);
            if (oldest.ActiveUses > 0)
            {
                oldest.Evicted = true;
                continue;
            }

            evicted ??= [];
            evicted.Add(oldest.Reader);
        }

        return evicted;
    }

    private static void Release(Entry entry)
    {
        IDocReader? toDispose = null;
        lock (CacheLock)
        {
            entry.ActiveUses--;
            if (entry.Evicted && entry.ActiveUses == 0)
                toDispose = entry.Reader;
        }

        if (toDispose != null)
        {
            try { toDispose.Dispose(); }
            catch { /* native handle already gone */ }
        }
    }

    private static string CacheKey(string pdfPath, float scale)
    {
        string scaleKey = scale.ToString("0.####", CultureInfo.InvariantCulture);
        try
        {
            var info = new FileInfo(pdfPath);
            return string.Join(
                '|',
                info.FullName.ToUpperInvariant(),
                info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                info.Length.ToString(CultureInfo.InvariantCulture),
                scaleKey);
        }
        catch
        {
            return string.Join('|', pdfPath.ToUpperInvariant(), scaleKey);
        }
    }
}
