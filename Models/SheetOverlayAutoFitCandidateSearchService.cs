using System;
using System.Collections.Generic;
using System.Linq;

namespace OurPlaneCore;

public sealed record SheetOverlayAutoFitCandidateInput(
    PageInfo Page,
    PdfGeometrySnapResult Snap,
    string Source,
    int SearchRank);

public sealed record SheetOverlayAutoFitCandidateMatch(
    PageInfo Page,
    SheetOverlayAutoFitResult Fit,
    string Source,
    int SearchRank);

public static class SheetOverlayAutoFitCandidateSearchService
{
    public const double MinimumAutoSelectConfidence = 0.18;
    public const int MinimumAutoSelectMatchedSamples = 10;

    public static bool TryFindBest(
        PdfGeometrySnapResult baseSnap,
        IEnumerable<SheetOverlayAutoFitCandidateInput> candidates,
        out SheetOverlayAutoFitCandidateMatch match)
    {
        bool ok = TryFindBest(baseSnap, candidates, out match, out _);
        return ok;
    }

    public static bool TryFindBest(
        PdfGeometrySnapResult baseSnap,
        IEnumerable<SheetOverlayAutoFitCandidateInput> candidates,
        out SheetOverlayAutoFitCandidateMatch match,
        out IReadOnlyList<SheetOverlayAutoFitCandidateMatch> topMatches)
    {
        match = default!;
        var matches = new List<SheetOverlayAutoFitCandidateMatch>();

        foreach (SheetOverlayAutoFitCandidateInput candidate in candidates)
        {
            if (candidate.Page == null ||
                !SheetOverlayAutoFitService.TryFit(baseSnap, candidate.Snap, out SheetOverlayAutoFitResult fit) ||
                !IsAutoSelectable(fit))
            {
                continue;
            }

            var current = new SheetOverlayAutoFitCandidateMatch(
                candidate.Page,
                fit,
                candidate.Source,
                candidate.SearchRank);
            matches.Add(current);
        }

        topMatches = matches
            .OrderByDescending(match => match, SheetOverlayAutoFitCandidateMatchComparer.Instance)
            .Take(5)
            .ToList();

        if (topMatches.Count == 0)
            return false;

        match = topMatches[0];
        return true;
    }

    private static bool IsAutoSelectable(SheetOverlayAutoFitResult fit) =>
        fit.Ok &&
        fit.MatchedSamples >= MinimumAutoSelectMatchedSamples &&
        fit.Confidence >= MinimumAutoSelectConfidence;

    private static bool IsBetter(
        SheetOverlayAutoFitCandidateMatch left,
        SheetOverlayAutoFitCandidateMatch right)
    {
        int confidence = CompareDescending(left.Fit.Confidence, right.Fit.Confidence, tolerance: 0.01);
        if (confidence != 0)
            return confidence > 0;

        int matched = left.Fit.MatchedSamples.CompareTo(right.Fit.MatchedSamples);
        if (matched != 0)
            return matched > 0;

        int samples = left.Fit.SampleCount.CompareTo(right.Fit.SampleCount);
        if (samples != 0)
            return samples > 0;

        int rank = right.SearchRank.CompareTo(left.SearchRank);
        if (rank != 0)
            return rank > 0;

        return string.Compare(
            left.Page.Name,
            right.Page.Name,
            StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static int CompareDescending(double left, double right, double tolerance)
    {
        double delta = left - right;
        if (Math.Abs(delta) <= tolerance)
            return 0;

        return delta > 0 ? 1 : -1;
    }

    private sealed class SheetOverlayAutoFitCandidateMatchComparer : IComparer<SheetOverlayAutoFitCandidateMatch>
    {
        public static readonly SheetOverlayAutoFitCandidateMatchComparer Instance = new();

        public int Compare(SheetOverlayAutoFitCandidateMatch? left, SheetOverlayAutoFitCandidateMatch? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return -1;
            if (right == null)
                return 1;

            if (IsBetter(left, right))
                return 1;
            if (IsBetter(right, left))
                return -1;

            return 0;
        }
    }
}
