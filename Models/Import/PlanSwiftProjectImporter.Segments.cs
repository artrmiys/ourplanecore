using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace OurPlaneCore;

public static partial class PlanSwiftProjectImporter
{
    private static void ImportSegments(
        PlanSwiftImportOptions options,
        PlanSwiftProjectManifest manifest,
        OurPlaneCoreJob job,
        string takeoffsRoot,
        IReadOnlyDictionary<string, ImportedPlanSwiftPage> pageByGuid,
        IReadOnlyDictionary<string, TakeoffItem> importedTakeoffsBySource,
        List<string> messages,
        ref int importedItems,
        ref int importedMeasurements)
    {
        foreach (PlanSwiftSegmentRecord segment in manifest.Segments)
        {
            if (segment.Sections.Count == 0)
                continue;
            if (options.MaxTakeoffItems > 0 && importedItems >= options.MaxTakeoffItems)
                break;
            if (options.MaxMeasurements > 0 && importedMeasurements >= options.MaxMeasurements)
                break;

            if (TryApplySegmentAsJoistArea(
                    manifest,
                    segment,
                    pageByGuid,
                    importedTakeoffsBySource,
                    messages))
            {
                continue;
            }

            string parent = EnsureRelativeFolder(takeoffsRoot, segment.ParentRelativeFolder);
            string itemName = UniqueChildDisplayName(parent, SegmentTakeoffName(segment));
            TakeoffItem imported = OurPlaneCoreJobStore.CreateTakeoffItem(
                job,
                parent,
                itemName,
                segment.ColorHex,
                "line");
            imported.Notes = BuildSegmentNotes(manifest, segment);
            importedItems++;

            foreach (PlanSwiftSectionRecord section in segment.Sections)
            {
                if (options.MaxMeasurements > 0 && importedMeasurements >= options.MaxMeasurements)
                    break;
                if (!pageByGuid.TryGetValue(section.PageGuid, out ImportedPlanSwiftPage? page))
                {
                    messages.Add($"Segment section '{section.Name}' under '{segment.Name}' references a page that was not imported.");
                    continue;
                }

                imported.Measurements.Add(CreateMeasurement(
                    manifest,
                    section,
                    page,
                    imported.FolderPath,
                    segment.ColorHex,
                    "Imported from PlanSwift Segment Section"));
                importedMeasurements++;
            }

            OurPlaneCoreJobStore.SaveTakeoffItem(imported);
        }
    }

    private static bool TryApplySegmentAsJoistArea(
        PlanSwiftProjectManifest manifest,
        PlanSwiftSegmentRecord segment,
        IReadOnlyDictionary<string, ImportedPlanSwiftPage> pageByGuid,
        IReadOnlyDictionary<string, TakeoffItem> importedTakeoffsBySource,
        List<string> messages)
    {
        string sourceParentKey = NormalizeImportRelativePath(segment.SourceParentRelativeFolder);
        if (string.IsNullOrWhiteSpace(sourceParentKey) ||
            !importedTakeoffsBySource.TryGetValue(sourceParentKey, out TakeoffItem? areaItem) ||
            OurPlaneCoreJobStore.NormalizeMeasurementType(areaItem.MeasurementType) != "area" ||
            areaItem.Measurements.All(measurement => OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) != "area"))
        {
            return false;
        }

        PlanSwiftTakeoffItemRecord? sourceAreaItem = FindSourceTakeoffItem(manifest, sourceParentKey);
        IReadOnlyList<IReadOnlyDictionary<string, string>> spacingSources = SegmentSpacingSources(segment, sourceAreaItem);
        if (!TryResolveSegmentJoistLayout(segment, segment.Sections, pageByGuid, spacingSources, out PlanSwiftJoistSegmentLayout layout))
            return false;

        IReadOnlyList<Measurement> areaMeasurements = areaItem.Measurements
            .Where(measurement => OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area")
            .ToList();
        Dictionary<Measurement, PlanSwiftJoistSegmentLayout> linkedLayouts =
            ResolveLinkedSegmentLayouts(segment, areaMeasurements, pageByGuid, spacingSources);
        string segmentColor = ResolveSegmentImportColor(segment);

        JoistTakeoffDefaults.ApplyToNewJoistArea(areaItem);
        areaItem.IsJoistTakeoff = true;
        areaItem.Color = segmentColor;
        areaItem.JoistDirectionDegrees = layout.DirectionDegrees;
        areaItem.JoistSpacingInches = layout.SpacingInches;
        areaItem.JoistAddEndJoist = false;
        areaItem.Notes = AppendPlanSwiftSegmentNote(areaItem.Notes, manifest, segment, layout);

        foreach (Measurement measurement in areaMeasurements)
        {
            PlanSwiftJoistSegmentLayout measurementLayout = linkedLayouts.TryGetValue(measurement, out PlanSwiftJoistSegmentLayout linked)
                ? linked
                : layout;
            measurement.Color = segmentColor;
            measurement.JoistEnabled = true;
            measurement.JoistDirectionLocked = true;
            measurement.JoistDirectionDegrees = measurementLayout.DirectionDegrees;
            measurement.JoistSpacingInches = measurementLayout.SpacingInches;
            measurement.JoistAddEndJoist = false;
        }

        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(areaItem);
        OurPlaneCoreJobStore.SaveTakeoffItem(areaItem);
        string linkedNote = linkedLayouts.Count > 0
            ? $" using {linkedLayouts.Count.ToString(CultureInfo.InvariantCulture)} linked area section direction(s)"
            : "";
        messages.Add(
            $"Applied PlanSwift segment '{segment.Name}' as joist direction on '{areaItem.Name}' " +
            $"({layout.DirectionDegrees:0.#} deg, {layout.SpacingInches:0.###}\" O.C.){linkedNote}.");
        return true;
    }

    private static PlanSwiftTakeoffItemRecord? FindSourceTakeoffItem(
        PlanSwiftProjectManifest manifest,
        string sourceParentKey) =>
        manifest.TakeoffItems.FirstOrDefault(item =>
            string.Equals(
                NormalizeImportRelativePath(item.RelativeFolder),
                sourceParentKey,
                StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> SegmentSpacingSources(
        PlanSwiftSegmentRecord segment,
        PlanSwiftTakeoffItemRecord? sourceAreaItem) =>
        sourceAreaItem == null
            ? [segment.Properties]
            : [segment.Properties, sourceAreaItem.Properties];

    private static Dictionary<Measurement, PlanSwiftJoistSegmentLayout> ResolveLinkedSegmentLayouts(
        PlanSwiftSegmentRecord segment,
        IReadOnlyList<Measurement> areaMeasurements,
        IReadOnlyDictionary<string, ImportedPlanSwiftPage> pageByGuid,
        IReadOnlyList<IReadOnlyDictionary<string, string>> spacingSources)
    {
        var measurementByGuid = areaMeasurements
            .Select(measurement => (Key: PlanSwiftXml.NormalizeGuid(measurement.Id), Measurement: measurement))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Measurement, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<Measurement, PlanSwiftJoistSegmentLayout>();

        foreach (IGrouping<string, PlanSwiftSectionRecord> group in segment.Sections
                     .GroupBy(section => LinkedAreaSectionGuid(section, segment), StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key) ||
                !measurementByGuid.TryGetValue(group.Key, out Measurement? measurement))
            {
                continue;
            }

            if (TryResolveSegmentJoistLayout(segment, group.ToList(), pageByGuid, spacingSources, out PlanSwiftJoistSegmentLayout layout))
                result[measurement] = layout;
        }

        return result;
    }

    private static string LinkedAreaSectionGuid(PlanSwiftSectionRecord section, PlanSwiftSegmentRecord segment)
    {
        foreach (string key in new[] { "Area Section", "Section Link", "Joist Area" })
        {
            if (section.Properties.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
                return PlanSwiftXml.NormalizeGuid(value);
        }

        return segment.Properties.TryGetValue("Joist Area", out string? segmentValue)
            ? PlanSwiftXml.NormalizeGuid(segmentValue)
            : "";
    }

    private static bool TryResolveSegmentJoistLayout(
        PlanSwiftSegmentRecord segment,
        IReadOnlyList<PlanSwiftSectionRecord> sections,
        IReadOnlyDictionary<string, ImportedPlanSwiftPage> pageByGuid,
        IReadOnlyList<IReadOnlyDictionary<string, string>> spacingSources,
        out PlanSwiftJoistSegmentLayout layout)
    {
        var lines = new List<PlanSwiftSegmentLine>();
        foreach (PlanSwiftSectionRecord section in sections)
        {
            if (!pageByGuid.TryGetValue(section.PageGuid, out ImportedPlanSwiftPage? page) ||
                section.Points.Count < 2)
            {
                continue;
            }

            SKPoint start = TransformPoint(section.Points[0], page.Normalization);
            SKPoint end = TransformPoint(section.Points[^1], page.Normalization);
            double length = Distance(start, end);
            if (length <= 0.001)
                continue;

            double direction = NormalizePlanSwiftDirectionDegrees(Math.Atan2(end.Y - start.Y, end.X - start.X) * 180.0 / Math.PI);
            lines.Add(new PlanSwiftSegmentLine(start, end, direction, length, page.Page.ScaleMetersPerPt));
        }

        if (lines.Count == 0)
        {
            layout = default;
            return false;
        }

        PlanSwiftSegmentLine primary = lines.OrderByDescending(line => line.Length).First();
        double spacingInches = TryCalculateSegmentSpacingInches(lines, primary, out double calculatedSpacing)
            ? calculatedSpacing
            : TryReadSegmentSpacingInches(spacingSources, out double propertySpacing)
                ? propertySpacing
                : 16.0;

        layout = new PlanSwiftJoistSegmentLayout(
            primary.DirectionDegrees,
            Math.Clamp(spacingInches, 0.001, 240.0));
        return true;
    }

    private static bool TryCalculateSegmentSpacingInches(
        IReadOnlyList<PlanSwiftSegmentLine> lines,
        PlanSwiftSegmentLine primary,
        out double spacingInches)
    {
        double radians = primary.DirectionDegrees * Math.PI / 180.0;
        double nx = -Math.Sin(radians);
        double ny = Math.Cos(radians);
        var projections = lines
            .Where(line => DirectionDeltaDegrees(line.DirectionDegrees, primary.DirectionDegrees) <= 15.0)
            .Select(line =>
            {
                SKPoint midpoint = new((line.Start.X + line.End.X) / 2f, (line.Start.Y + line.End.Y) / 2f);
                return midpoint.X * nx + midpoint.Y * ny;
            })
            .OrderBy(value => value)
            .ToList();

        var distances = new List<double>();
        for (int i = 1; i < projections.Count; i++)
        {
            double distance = Math.Abs(projections[i] - projections[i - 1]);
            if (distance > 0.001)
                distances.Add(distance);
        }

        if (distances.Count == 0 || primary.ScaleMetersPerPt <= 0)
        {
            spacingInches = 0;
            return false;
        }

        distances.Sort();
        double medianPdfDistance = distances[distances.Count / 2];
        spacingInches = medianPdfDistance * primary.ScaleMetersPerPt / 0.0254;
        return spacingInches > 0.001;
    }

    private static bool TryReadSegmentSpacingInches(
        IReadOnlyList<IReadOnlyDictionary<string, string>> propertySources,
        out double spacingInches)
    {
        foreach (IReadOnlyDictionary<string, string> properties in propertySources)
        {
            foreach (string key in new[] { "Spacing", "Joist Spacing", "O.C. Spacing", "O.C.", "OC", "On Center" })
            {
                if (!properties.TryGetValue(key, out string? value) ||
                    string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                Match match = Regex.Match(value, @"[-+]?\d+(?:\.\d+)?");
                if (match.Success &&
                    double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
                    parsed > 0)
                {
                    spacingInches = parsed;
                    return true;
                }
            }
        }

        spacingInches = 0;
        return false;
    }

    private static string ResolveSegmentImportColor(PlanSwiftSegmentRecord segment)
    {
        string color = NormalizeColorHex(segment.ColorHex);
        return IsUsableSegmentImportColor(color)
            ? color
            : StableSegmentImportColor(segment);
    }

    private static string NormalizeColorHex(string color)
    {
        string clean = (color ?? "").Trim();
        if (!clean.StartsWith("#", StringComparison.Ordinal) || clean.Length != 7)
            return "";

        return clean.ToUpperInvariant();
    }

    private static bool IsUsableSegmentImportColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return false;
        if (!int.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return false;

        int r = (rgb >> 16) & 0xFF;
        int g = (rgb >> 8) & 0xFF;
        int b = rgb & 0xFF;
        return Math.Max(r, Math.Max(g, b)) < 242 || Math.Min(r, Math.Min(g, b)) < 220;
    }

    private static string StableSegmentImportColor(PlanSwiftSegmentRecord segment)
    {
        string[] palette =
        [
            "#E53935",
            "#1E88E5",
            "#43A047",
            "#FB8C00",
            "#8E24AA",
            "#00ACC1",
            "#D81B60",
            "#7CB342",
            "#5E35B1",
            "#F4511E",
        ];
        string seed = $"{segment.Guid}|{segment.RelativeFolder}|{segment.Name}";
        uint hash = 2166136261;
        foreach (char ch in seed)
        {
            hash ^= ch;
            hash *= 16777619;
        }

        return palette[(int)(hash % (uint)palette.Length)];
    }

    private static string AppendPlanSwiftSegmentNote(
        string existingNotes,
        PlanSwiftProjectManifest manifest,
        PlanSwiftSegmentRecord segment,
        PlanSwiftJoistSegmentLayout layout)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(existingNotes))
            parts.Add(existingNotes.Trim());
        parts.Add(
            $"Imported PlanSwift Segment as joist area direction from {Path.GetRelativePath(manifest.SourceJobPath, segment.SourceFolder)} " +
            $"({layout.DirectionDegrees:0.#} deg, {layout.SpacingInches:0.###}\" O.C.).");
        AddPropertyNote(parts, segment, "Type");
        AddPropertyNote(parts, segment, "Default");
        AddPropertyNote(parts, segment, "Joist Length");
        AddPropertyNote(parts, segment, "Pitch");
        return string.Join(Environment.NewLine, parts.Distinct(StringComparer.Ordinal));
    }

    private static double DirectionDeltaDegrees(double a, double b)
    {
        double delta = Math.Abs(NormalizePlanSwiftDirectionDegrees(a) - NormalizePlanSwiftDirectionDegrees(b));
        return delta > 90.0 ? 180.0 - delta : delta;
    }

    private static double NormalizePlanSwiftDirectionDegrees(double degrees)
    {
        double normalized = degrees % 180.0;
        return normalized < 0.0 ? normalized + 180.0 : normalized;
    }

    private static double Distance(SKPoint a, SKPoint b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string SegmentTakeoffName(PlanSwiftSegmentRecord segment)
    {
        string baseName = string.IsNullOrWhiteSpace(segment.ParentName)
            ? segment.Name
            : segment.ParentName;
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "PlanSwift Segment";

        return $"{baseName} - PlanSwift segments";
    }

    private static string BuildSegmentNotes(PlanSwiftProjectManifest manifest, PlanSwiftSegmentRecord segment)
    {
        var parts = new List<string>
        {
            $"Imported generated PlanSwift Segment geometry from {Path.GetRelativePath(manifest.SourceJobPath, segment.SourceFolder)}.",
        };
        AddPropertyNote(parts, segment, "Type");
        AddPropertyNote(parts, segment, "Qty");
        AddPropertyNote(parts, segment, "Default");
        AddPropertyNote(parts, segment, "Joist Length");
        AddPropertyNote(parts, segment, "Pitch");
        AddPropertyNote(parts, segment, "Section Type");
        return string.Join(Environment.NewLine, parts.Distinct(StringComparer.Ordinal));
    }

    private static void AddPropertyNote(List<string> parts, PlanSwiftSegmentRecord segment, string propertyName)
    {
        if (segment.Properties.TryGetValue(propertyName, out string? value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{propertyName}: {value}");
        }
    }

    private readonly record struct PlanSwiftJoistSegmentLayout(
        double DirectionDegrees,
        double SpacingInches);

    private readonly record struct PlanSwiftSegmentLine(
        SKPoint Start,
        SKPoint End,
        double DirectionDegrees,
        double Length,
        double ScaleMetersPerPt);

}
