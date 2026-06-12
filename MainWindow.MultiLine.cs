using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

// Multiline: a line takeoff item can carry up to two offset companions.
// Drawing a line on the owner also drops a parallel line into each companion,
// so one pass of the mouse produces 2-3 ordinary, independently editable lines.
public partial class MainWindow
{
    private List<TakeoffItem> CreateMultiLineCompanions(TakeoffItem item, IReadOnlyList<NewItemOffsetSpec> specs)
    {
        var companions = new List<TakeoffItem>();
        if (_currentJob == null || specs.Count == 0)
            return companions;

        string parent = Path.GetDirectoryName(item.FolderPath) ?? _currentJob.TakeoffsRoot;
        foreach (NewItemOffsetSpec spec in specs)
        {
            TakeoffItem companion = CreateUniqueTakeoffItem(spec.Name, spec.Color, "line", parent);
            companions.Add(companion);
            item.MultiLineOffsets.Add(new MultiLineOffsetConfig
            {
                Name = spec.Name,
                Color = spec.Color,
                Meters = spec.Meters,
                RightSide = spec.RightSide,
                CompanionFolder = companion.FolderPath,
            });
        }

        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        return companions;
    }

    private void GenerateMultiLineOffsets(TakeoffItem item, Measurement m)
    {
        if (item.MultiLineOffsets.Count == 0 ||
            OurPlaneCoreJobStore.NormalizeMeasurementType(m.MType) != "line" ||
            m.Points.Count < 2)
        {
            return;
        }

        if (m.ScaleMetersPerPt <= 0)
        {
            TxtStatus.Text = "Offset lines skipped: set the sheet scale first.";
            return;
        }

        var generated = new List<Measurement>();
        var changedItems = new List<TakeoffItem>();
        foreach (MultiLineOffsetConfig config in item.MultiLineOffsets)
        {
            TakeoffItem? companion = ResolveMultiLineCompanion(config);
            if (companion == null || config.Meters <= 0)
                continue;

            float distancePt = (float)(config.Meters / m.ScaleMetersPerPt);
            if (config.RightSide)
                distancePt = -distancePt;
            List<SkiaSharp.SKPoint> offsetPoints = MeasurementGeometry.OffsetPolyline(m.Points, distancePt);
            if (offsetPoints.Count < 2)
                continue;

            var offset = new Measurement
            {
                MType = "line",
                Points = offsetPoints,
                Color = companion.Color,
                PageFolder = m.PageFolder,
                TakeoffFolder = companion.FolderPath,
                ScaleMetersPerPt = m.ScaleMetersPerPt,
            };
            companion.Measurements.Add(offset);
            OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(companion);
            generated.Add(offset);
            changedItems.Add(companion);
        }

        if (generated.Count == 0)
        {
            if (item.MultiLineOffsets.Count > 0)
                TxtStatus.Text = "Offset lines skipped: companion takeoff items were not found.";
            return;
        }

        _viewport.AddGeneratedMeasurements(generated);
        foreach (TakeoffItem companion in changedItems)
        {
            RefreshTreeItem(companion);
            QueueTakeoffAutosave(companion);
        }
        using (UsePageMeasurementLookup())
        {
            RefreshTakeoffRowVisualsForItems(changedItems);
            RefreshPageTakeoffIndicatorsForFolder(m.PageFolder);
            RefreshSheetLegend();
        }
        UpdateTotalDisplay();
        TxtStatus.Text = $"Added line + {generated.Count} offset line(s).";
    }

    private TakeoffItem? ResolveMultiLineCompanion(MultiLineOffsetConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.CompanionFolder))
            return null;

        return _takeoffItems.FirstOrDefault(candidate =>
            string.Equals(candidate.FolderPath, config.CompanionFolder, StringComparison.OrdinalIgnoreCase));
    }
}
