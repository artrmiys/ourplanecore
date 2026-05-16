using System;
using System.Collections.Generic;

namespace OurPlaneCore;

public partial class MainWindow
{
    private static readonly bool PageMeasurementLookupEnabled = true;
    private PageMeasurementLookup? _pageMeasurementLookup;

    private IDisposable UsePageMeasurementLookup()
    {
        if (!PageMeasurementLookupEnabled)
            return NoopDisposable.Instance;

        if (_pageMeasurementLookup != null)
            return NoopDisposable.Instance;

        _pageMeasurementLookup = BuildPageMeasurementLookup();
        return new PageMeasurementLookupScope(this);
    }

    private PageMeasurementLookup BuildPageMeasurementLookup()
    {
        var lookup = new PageMeasurementLookup();
        foreach (TakeoffItem item in _takeoffItems)
        {
            if (string.IsNullOrWhiteSpace(item.FolderPath))
                continue;

            string itemKey = NormalizePathForCompare(item.FolderPath);
            string legendKey = TakeoffLegendOrderKey(item);
            if (string.IsNullOrWhiteSpace(itemKey) || string.IsNullOrWhiteSpace(legendKey))
                continue;

            foreach (Measurement measurement in item.Measurements)
            {
                if (string.IsNullOrWhiteSpace(measurement.PageFolder))
                    continue;

                string pageKey = NormalizePathForCompare(measurement.PageFolder);
                if (string.IsNullOrWhiteSpace(pageKey))
                    continue;

                lookup.Add(pageKey, itemKey, legendKey, item, measurement);
            }
        }

        return lookup;
    }

    private bool TryGetIndexedTakeoffsForPage(string pageFolder, out IReadOnlyList<TakeoffItem> takeoffs)
    {
        if (!PageMeasurementLookupEnabled || _pageMeasurementLookup == null)
        {
            takeoffs = [];
            return false;
        }

        takeoffs = _pageMeasurementLookup.GetTakeoffs(NormalizePathForCompare(pageFolder));
        return true;
    }

    private bool TryGetIndexedMeasurementsForTakeoffOnPage(
        TakeoffItem item,
        string pageFolder,
        out IReadOnlyList<Measurement> measurements)
    {
        if (!PageMeasurementLookupEnabled ||
            _pageMeasurementLookup == null ||
            string.IsNullOrWhiteSpace(item.FolderPath))
        {
            measurements = [];
            return false;
        }

        measurements = _pageMeasurementLookup.GetMeasurements(
            NormalizePathForCompare(pageFolder),
            NormalizePathForCompare(item.FolderPath));
        return true;
    }

    private sealed class PageMeasurementLookup
    {
        private static readonly IReadOnlyList<TakeoffItem> EmptyTakeoffs = Array.Empty<TakeoffItem>();
        private static readonly IReadOnlyList<Measurement> EmptyMeasurements = Array.Empty<Measurement>();

        private readonly Dictionary<string, PageMeasurementBucket> _byPage = new(StringComparer.OrdinalIgnoreCase);

        public void Add(
            string pageKey,
            string itemKey,
            string legendKey,
            TakeoffItem item,
            Measurement measurement)
        {
            if (!_byPage.TryGetValue(pageKey, out PageMeasurementBucket? page))
            {
                page = new PageMeasurementBucket();
                _byPage[pageKey] = page;
            }

            page.Add(itemKey, legendKey, item, measurement);
        }

        public IReadOnlyList<TakeoffItem> GetTakeoffs(string pageKey) =>
            _byPage.TryGetValue(pageKey, out PageMeasurementBucket? page)
                ? page.Takeoffs
                : EmptyTakeoffs;

        public IReadOnlyList<Measurement> GetMeasurements(string pageKey, string itemKey) =>
            _byPage.TryGetValue(pageKey, out PageMeasurementBucket? page)
                ? page.GetMeasurements(itemKey)
                : EmptyMeasurements;

        private sealed class PageMeasurementBucket
        {
            private readonly Dictionary<string, TakeoffMeasurementBucket> _byItem = new(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _seenLegendKeys = new(StringComparer.OrdinalIgnoreCase);
            private readonly List<TakeoffItem> _takeoffs = [];

            public IReadOnlyList<TakeoffItem> Takeoffs => _takeoffs;

            public void Add(
                string itemKey,
                string legendKey,
                TakeoffItem item,
                Measurement measurement)
            {
                if (!_byItem.TryGetValue(itemKey, out TakeoffMeasurementBucket? bucket))
                {
                    bucket = new TakeoffMeasurementBucket();
                    _byItem[itemKey] = bucket;
                }

                bucket.Measurements.Add(measurement);
                if (_seenLegendKeys.Add(legendKey))
                    _takeoffs.Add(item);
            }

            public IReadOnlyList<Measurement> GetMeasurements(string itemKey) =>
                _byItem.TryGetValue(itemKey, out TakeoffMeasurementBucket? bucket)
                    ? bucket.Measurements
                    : EmptyMeasurements;
        }

        private sealed class TakeoffMeasurementBucket
        {
            public List<Measurement> Measurements { get; } = [];
        }
    }

    private sealed class PageMeasurementLookupScope : IDisposable
    {
        private MainWindow? _owner;

        public PageMeasurementLookupScope(MainWindow owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (_owner == null)
                return;

            _owner._pageMeasurementLookup = null;
            _owner = null;
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();
        public void Dispose()
        {
        }
    }
}
