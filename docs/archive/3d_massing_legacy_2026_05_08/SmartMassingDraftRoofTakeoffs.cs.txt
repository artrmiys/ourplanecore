using System.IO;
using SkiaSharp;

namespace OurPlaneCore;

public static partial class SmartMassingDraftService
{
    private static void AddTakeoffRoofGuides(
        OurPlaneCoreJob job,
        SmartMassingDraft draft,
        IReadOnlyList<TakeoffItem> allItems,
        IReadOnlyDictionary<int, MassingTakeoffTransform> footprintTransforms)
    {
        SmartMassingFootprint? footprint = RoofFootprint(draft);
        if (footprint == null)
            return;

        footprintTransforms.TryGetValue(footprint.Level, out MassingTakeoffTransform? transform);
        List<TakeoffItem> roofItems = RoofTakeoffItemsForFootprint(allItems, footprint, transform);
        string roofType = roofItems.Any(item => RoofGuideKind(item) is "rake" or "gable" or "gable_area")
            ? "gable"
            : "unknown";
        draft.Roof.Type = roofType;
        draft.Roof.Guides = BuildRoofGuides(job, draft, [], roofType, "", []);

        if (roofItems.Count == 0)
            return;

        List<SmartMassingRoofGuide> measuredGuides = BuildMeasuredRoofGuides(
            roofItems,
            transform,
            out List<string> sourceIds,
            out List<string> skipped);
        if (measuredGuides.Count == 0)
        {
            draft.UnresolvedQuestions.Add("Roof-labeled takeoffs were found, but no eave/rake/gable guide could be linked. Check sheet scale and source page.");
            foreach (string message in skipped.Take(3))
                draft.UnresolvedQuestions.Add(message);
            return;
        }

        draft.Roof.Guides.AddRange(measuredGuides);
        draft.Roof.SourceMarkerIds = draft.Roof.SourceMarkerIds
            .Concat(sourceIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        draft.Roof.Confidence = Math.Max(draft.Roof.Confidence, roofType == "gable" ? 0.32 : 0.24);
        draft.Roof.Notes = AppendSentence(
            draft.Roof.Notes,
            $"Linked {measuredGuides.Count} roof guide(s) from eave/rake/gable takeoffs.");
        draft.Assumptions.Add($"Roof takeoffs linked automatically to level {footprint.Level} by source page/level context: {string.Join(", ", roofItems.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase))}.");
        if (skipped.Count > 0)
            draft.UnresolvedQuestions.Add($"{skipped.Count} roof takeoff measurement(s) were skipped because they could not be transformed onto the top footprint page.");
    }

    private static List<TakeoffItem> RoofTakeoffItemsForFootprint(
        IReadOnlyList<TakeoffItem> allItems,
        SmartMassingFootprint footprint,
        MassingTakeoffTransform? transform)
    {
        List<TakeoffItem> roofItems = allItems
            .Where(IsRoofGuideTakeoff)
            .Where(item => item.Measurements.Any(measurement => measurement.Points.Count >= 2))
            .ToList();
        if (roofItems.Count <= 1)
            return roofItems;

        List<TakeoffItem> matched = roofItems
            .Where(item => RoofTakeoffMatchesFootprint(item, footprint, transform))
            .ToList();
        return matched.Count > 0 ? matched : roofItems;
    }

    private static bool RoofTakeoffMatchesFootprint(
        TakeoffItem item,
        SmartMassingFootprint footprint,
        MassingTakeoffTransform? transform)
    {
        if (transform != null &&
            item.Measurements.Any(measurement => SamePath(measurement.PageFolder, transform.PageFolder)))
        {
            return true;
        }

        return TryParseLevel(RoofTakeoffText(item), out int level) &&
               level == footprint.Level;
    }

    private static List<SmartMassingRoofGuide> BuildMeasuredRoofGuides(
        IReadOnlyList<TakeoffItem> roofItems,
        MassingTakeoffTransform? transform,
        out List<string> sourceIds,
        out List<string> skipped)
    {
        sourceIds = [];
        skipped = [];
        var guides = new List<SmartMassingRoofGuide>();
        if (transform == null)
        {
            skipped.Add("Roof takeoffs need a footprint transform. Build the footprint from scaled sqft/wall takeoffs first.");
            return guides;
        }

        int index = 0;
        foreach (TakeoffItem item in roofItems)
        {
            string kind = RoofGuideKind(item);
            string label = RoofGuideLabel(item, kind);
            foreach (Measurement measurement in item.Measurements.Where(measurement => measurement.Points.Count >= 2))
            {
                if (!CanUseRoofMeasurement(measurement, transform))
                {
                    skipped.Add($"Skipped {item.Name}: measurement is not on the top footprint source page.");
                    continue;
                }

                double scaleMetersPerPt = ResolveMeasurementScaleMetersPerPt(measurement);
                if (scaleMetersPerPt <= 0)
                {
                    skipped.Add($"Skipped {item.Name}: source sheet scale is missing.");
                    continue;
                }

                string sourceId = MeasurementSourceId(item, measurement);
                sourceIds.Add(sourceId);
                guides.Add(new SmartMassingRoofGuide
                {
                    Id = $"roof_takeoff_{kind}_{++index}",
                    Kind = kind,
                    Label = label,
                    Points = measurement.Points
                        .Select(point => ToRoofGuidePoint(point, transform, scaleMetersPerPt, sourceId))
                        .ToList(),
                    Confidence = kind is "rake" or "gable" or "gable_area" ? 0.42 : 0.34,
                    SourceMarkerIds = [sourceId],
                    Notes = $"Measured roof guide from takeoff '{item.Name}'. Review before accepting roof geometry.",
                });
            }
        }

        sourceIds = sourceIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return guides;
    }

    private static bool CanUseRoofMeasurement(Measurement measurement, MassingTakeoffTransform transform) =>
        string.IsNullOrWhiteSpace(measurement.PageFolder) ||
        SamePath(measurement.PageFolder, transform.PageFolder);

    private static SmartMassingPoint ToRoofGuidePoint(
        SKPoint point,
        MassingTakeoffTransform transform,
        double scaleMetersPerPt,
        string sourceId) =>
        new()
        {
            X = Math.Round((point.X - transform.OriginPdfPoint.X) * scaleMetersPerPt / MetersPerFoot, 3),
            Y = Math.Round((point.Y - transform.OriginPdfPoint.Y) * scaleMetersPerPt / MetersPerFoot, 3),
            SourceMarkerId = sourceId,
        };

    private static bool IsRoofGuideTakeoff(TakeoffItem item) =>
        IsEaveTakeoff(item) ||
        IsRakeTakeoff(item) ||
        IsGableTakeoff(item);

    private static bool IsEaveTakeoff(TakeoffItem item)
    {
        string text = RoofTakeoffText(item);
        return ContainsEaveTerm(text);
    }

    private static bool IsRakeTakeoff(TakeoffItem item)
    {
        string text = RoofTakeoffText(item);
        return ContainsRakeTerm(text);
    }

    private static bool IsGableTakeoff(TakeoffItem item)
    {
        string text = RoofTakeoffText(item);
        return ContainsGableTerm(text);
    }

    private static string RoofGuideKind(TakeoffItem item)
    {
        string labelText = RoofTakeoffLabelText(item);
        if (ContainsRakeTerm(labelText))
            return "rake";
        if (ContainsGableTerm(labelText))
            return OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "area"
                ? "gable_area"
                : "gable";
        if (ContainsEaveTerm(labelText))
            return "eave";

        string fullText = RoofTakeoffText(item);
        if (ContainsRakeTerm(fullText))
            return "rake";
        if (ContainsGableTerm(fullText))
            return OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "area"
                ? "gable_area"
                : "gable";
        return "eave";
    }

    private static string RoofGuideLabel(TakeoffItem item, string kind) =>
        kind switch
        {
            "rake" => $"Measured rake: {item.Name}",
            "gable" => $"Measured gable: {item.Name}",
            "gable_area" => $"Measured gable area: {item.Name}",
            _ => $"Measured eave: {item.Name}",
        };

    private static string RoofTakeoffText(TakeoffItem item) =>
        NormalizeTakeoffName($"{item.Name} {item.Notes} {item.FolderPath}");

    private static string RoofTakeoffLabelText(TakeoffItem item) =>
        NormalizeTakeoffName($"{item.Name} {item.Notes}");

    private static bool ContainsEaveTerm(string text) =>
        text.Contains(" eave ", StringComparison.Ordinal) ||
        text.Contains(" eaves ", StringComparison.Ordinal) ||
        text.Contains(" eve ", StringComparison.Ordinal) ||
        text.Contains(" fascia ", StringComparison.Ordinal);

    private static bool ContainsRakeTerm(string text) =>
        text.Contains(" rake ", StringComparison.Ordinal) ||
        text.Contains(" rakes ", StringComparison.Ordinal);

    private static bool ContainsGableTerm(string text) =>
        text.Contains(" gable ", StringComparison.Ordinal) ||
        text.Contains(" gables ", StringComparison.Ordinal);

    private static string AppendSentence(string text, string sentence) =>
        string.IsNullOrWhiteSpace(text)
            ? sentence
            : $"{text.Trim()} {sentence}";

    private static bool SamePath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record MassingTakeoffTransform(
        string PageFolder,
        string PageName,
        SKPoint OriginPdfPoint,
        double ScaleMetersPerPt);
}
