using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OurPlanCore;

public partial class MainWindow
{
    private async Task OfferPdfMetadataGuidanceIfNeededAsync(
        OurPlanCoreJob job,
        IReadOnlyList<PageInfo> pages,
        string statusPrefix)
    {
        if (SheetMetadataRulesService.Active.DetectorMode != SheetMetadataDetectorMode.IdealV3)
            return;

        IReadOnlyList<PdfSheetMetadataGuidancePlanItem> plan =
            PdfSheetMetadataGuidancePlanner.Build(job, pages);
        var handledProfiles = new HashSet<PdfSheetMetadataCropProfile>();
        foreach (PdfSheetMetadataCropProfile profile in new[]
                 {
                     PdfSheetMetadataCropProfile.Architectural,
                     PdfSheetMetadataCropProfile.Structural,
                 })
        {
            if (PdfSheetMetadataCropService.HasExactJobTemplate(job, profile))
                handledProfiles.Add(profile);
        }

        foreach (PdfSheetMetadataGuidancePlanItem item in plan)
        {
            if (item.Profile == PdfSheetMetadataCropProfile.Default &&
                handledProfiles.Contains(PdfSheetMetadataCropProfile.Architectural) &&
                handledProfiles.Contains(PdfSheetMetadataCropProfile.Structural))
            {
                break;
            }
            if (item.Profile != PdfSheetMetadataCropProfile.Default && handledProfiles.Contains(item.Profile))
                continue;

            if (!EnsureExpectedJobWritable(job, "check sheet metadata layout"))
                return;

            PdfSheetMetadataAnalysisItem probe = await ProbeSheetMetadataLayoutAsync(
                job,
                item,
                statusPrefix);
            if (!EnsureExpectedJobWritable(job, "show sheet metadata layout guidance"))
                return;

            if (!NeedsSheetMetadataLayoutGuidance(probe.Metadata))
            {
                PdfSheetMetadataCropProfile detectedProfile = ResolveGuidanceProfile(item, probe.Metadata);
                if (detectedProfile != PdfSheetMetadataCropProfile.Default)
                    handledProfiles.Add(detectedProfile);
                AppLog.Info(
                    $"Sheet metadata guidance not needed; profile={item.Profile}; " +
                    $"sample={item.SamplePage.Name}; pages={item.PageCount}.");
                continue;
            }

            PdfSheetMetadataCropProfile profile = ResolveGuidanceProfile(item, probe.Metadata);
            if (PdfSheetMetadataCropService.HasExactJobTemplate(job, profile))
            {
                if (profile != PdfSheetMetadataCropProfile.Default)
                    handledProfiles.Add(profile);
                continue;
            }

            string profileName = PdfSheetMetadataCropService.ProfileDisplayName(profile);
            TxtStatus.Text =
                $"{statusPrefix}: show Sheet title / number, then Scale on the middle {profileName} sheet.";
            AppLog.Info(
                $"Sheet metadata guidance requested; profile={profile}; sample={item.SamplePage.Name}; " +
                $"pages={item.PageCount}; probe_ok={probe.Ok}; error={probe.Error}");

            PdfMetadataCropTemplateSelection? saved = ShowPdfMetadataCropTemplateDialog(
                item.SamplePage,
                showSavedMessage: false,
                requestedProfile: profile,
                guidedNameAndScale: true);
            if (saved == null)
            {
                AppLog.Info(
                    $"Sheet metadata guidance cancelled; profile={profile}; sample={item.SamplePage.Name}.");
                continue;
            }

            profile = saved.Profile;
            if (profile != PdfSheetMetadataCropProfile.Default)
                handledProfiles.Add(profile);
            profileName = PdfSheetMetadataCropService.ProfileDisplayName(profile);
            TxtStatus.Text =
                $"{profileName} layout saved. The full analysis will reuse it for this job.";
        }
    }

    private async Task<PdfSheetMetadataAnalysisItem> ProbeSheetMetadataLayoutAsync(
        OurPlanCoreJob job,
        PdfSheetMetadataGuidancePlanItem item,
        string statusPrefix)
    {
        string profileName = PdfSheetMetadataCropService.ProfileDisplayName(item.Profile);
        using (ShowBusyOverlay(
                   $"{statusPrefix}: checking one middle {profileName} sheet..."))
        {
            await WaitForBusyOverlayRenderAsync();
            if (!EnsureExpectedJobWritable(job, "check sheet metadata layout"))
            {
                return new PdfSheetMetadataAnalysisItem(
                    item.SamplePage,
                    false,
                    null,
                    "The active job changed or became read-only.",
                    false);
            }

            IReadOnlyList<PdfSheetMetadataAnalysisItem> result = await Task.Run(() =>
                PdfSheetMetadataService.AnalyzePages(
                    job,
                    [item.SamplePage],
                    persistMetadata: false,
                    forceReanalyze: true));
            return result.Single();
        }
    }

    private static PdfSheetMetadataCropProfile ResolveGuidanceProfile(
        PdfSheetMetadataGuidancePlanItem item,
        PdfSheetMetadata? metadata)
    {
        if (item.Profile != PdfSheetMetadataCropProfile.Default)
            return item.Profile;

        PdfSheetMetadataCropProfile detected =
            PdfSheetMetadataCropService.ResolveProfile(metadata, item.SamplePage);
        return detected;
    }

    private static bool NeedsSheetMetadataLayoutGuidance(PdfSheetMetadata? metadata)
    {
        if (metadata == null ||
            string.Equals(metadata.Confidence, "no-text", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(metadata.Source, "pdf-empty-text", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(metadata.SheetLabel) ||
            string.IsNullOrWhiteSpace(metadata.SheetTitle))
        {
            return true;
        }

        return !metadata.SkipScale &&
               metadata.SelectedScaleMetersPerPt <= 0 &&
               (string.IsNullOrWhiteSpace(metadata.Suffix) ||
                SheetMetadataRulesService.Active.IsScaleCapableSuffix(metadata.Suffix));
    }
}
