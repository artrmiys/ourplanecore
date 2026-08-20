using System;
using System.Collections.Generic;

namespace OurPlanCore;

public sealed record PdfSheetMetadataGuidancePlanItem(
    PdfSheetMetadataCropProfile Profile,
    PageInfo SamplePage,
    int PageCount);

public static class PdfSheetMetadataGuidancePlanner
{
    public static IReadOnlyList<PdfSheetMetadataGuidancePlanItem> Build(
        OurPlanCoreJob job,
        IReadOnlyList<PageInfo> pages)
    {
        ArgumentNullException.ThrowIfNull(job);
        return Build(
            pages,
            profile => PdfSheetMetadataCropService.HasExactJobTemplate(job, profile));
    }

    internal static IReadOnlyList<PdfSheetMetadataGuidancePlanItem> Build(
        IReadOnlyList<PageInfo> pages,
        Func<PdfSheetMetadataCropProfile, bool> hasDedicatedTemplate)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(hasDedicatedTemplate);

        var groups = new Dictionary<PdfSheetMetadataCropProfile, List<PageInfo>>
        {
            [PdfSheetMetadataCropProfile.Default] = [],
            [PdfSheetMetadataCropProfile.Architectural] = [],
            [PdfSheetMetadataCropProfile.Structural] = [],
        };

        foreach (PageInfo page in pages)
            groups[PdfSheetMetadataCropService.ResolveProfile(page)].Add(page);

        var plan = new List<PdfSheetMetadataGuidancePlanItem>();
        foreach (PdfSheetMetadataCropProfile profile in new[]
                 {
                     PdfSheetMetadataCropProfile.Architectural,
                     PdfSheetMetadataCropProfile.Structural,
                 })
        {
            List<PageInfo> profilePages = groups[profile];
            if (profilePages.Count == 0 || hasDedicatedTemplate(profile))
                continue;

            plan.Add(new PdfSheetMetadataGuidancePlanItem(
                profile,
                profilePages[profilePages.Count / 2],
                profilePages.Count));
        }

        List<PageInfo> unknownPages = groups[PdfSheetMetadataCropProfile.Default];
        if (unknownPages.Count > 0)
        {
            foreach (int index in BuildStratifiedSampleIndices(unknownPages.Count))
            {
                plan.Add(new PdfSheetMetadataGuidancePlanItem(
                    PdfSheetMetadataCropProfile.Default,
                    unknownPages[index],
                    unknownPages.Count));
            }
        }

        return plan;
    }

    private static IReadOnlyList<int> BuildStratifiedSampleIndices(int count)
    {
        var indices = new List<int>(Math.Min(4, count));
        AddUnique(count / 3);
        AddUnique(count * 2 / 3);
        AddUnique(count / 2);

        while (indices.Count < Math.Min(4, count))
        {
            int bestIndex = -1;
            int bestDistance = -1;
            for (int candidate = 0; candidate < count; candidate++)
            {
                if (indices.Contains(candidate))
                    continue;

                int nearestDistance = int.MaxValue;
                foreach (int existing in indices)
                    nearestDistance = Math.Min(nearestDistance, Math.Abs(candidate - existing));
                if (nearestDistance > bestDistance)
                {
                    bestIndex = candidate;
                    bestDistance = nearestDistance;
                }
            }

            if (bestIndex < 0)
                break;
            indices.Add(bestIndex);
        }

        return indices;

        void AddUnique(int index)
        {
            if (index >= 0 && index < count && !indices.Contains(index))
                indices.Add(index);
        }
    }
}
