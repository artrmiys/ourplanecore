using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OurPlanCore;

public sealed record ExcelWorkbookCandidate(
    string Name,
    bool IsActive,
    bool HasRequiredWorksheet,
    bool IsMacroEnabled);

public sealed record ExcelWorkbookSelection(
    bool Success,
    int CandidateIndex,
    bool UsedActiveWorkbook,
    string Message)
{
    public static ExcelWorkbookSelection Failure(string message) =>
        new(false, -1, false, message);
}

public static class ExcelWorkbookSelectionPolicy
{
    public static ExcelWorkbookSelection Resolve(
        string configuredWorkbookName,
        string requiredWorksheetName,
        IReadOnlyList<ExcelWorkbookCandidate> candidates)
    {
        string configured = (configuredWorkbookName ?? "").Trim();
        string worksheet = (requiredWorksheetName ?? "").Trim();
        if (configured.Length == 0 || worksheet.Length == 0)
        {
            return ExcelWorkbookSelection.Failure(
                "Excel workbook and worksheet names are required.");
        }

        int activeIndex = IndexOfActive(candidates);
        ExcelWorkbookSelection? activeFailure = null;
        if (activeIndex >= 0)
        {
            ExcelWorkbookCandidate activeCandidate = candidates[activeIndex];
            ExcelWorkbookSelection activeSelection = ValidateCandidate(
                activeCandidate,
                activeIndex,
                worksheet,
                usedActiveWorkbook: true);
            if (activeSelection.Success)
                return activeSelection;
            activeFailure = activeSelection;
        }

        int configuredIndex = IndexOfName(candidates, configured);
        if (configuredIndex >= 0 && configuredIndex != activeIndex)
        {
            ExcelWorkbookCandidate configuredCandidate = candidates[configuredIndex];
            return ValidateCandidate(
                configuredCandidate,
                configuredIndex,
                worksheet,
                usedActiveWorkbook: false);
        }

        if (activeFailure != null)
        {
            if (configuredIndex >= 0)
                return activeFailure;
            return ExcelWorkbookSelection.Failure(
                $"Configured workbook '{configured}' is not open. {activeFailure.Message} " +
                OpenWorkbookSuffix(candidates));
        }

        return ExcelWorkbookSelection.Failure(
            $"Configured workbook '{configured}' is not open and Excel has no active workbook. " +
            OpenWorkbookSuffix(candidates));
    }

    public static bool IsMacroEnabledWorkbookName(string? workbookName)
    {
        string extension = Path.GetExtension(workbookName ?? "");
        return extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xlsb", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xls", StringComparison.OrdinalIgnoreCase);
    }

    private static ExcelWorkbookSelection ValidateCandidate(
        ExcelWorkbookCandidate candidate,
        int candidateIndex,
        string worksheet,
        bool usedActiveWorkbook)
    {
        if (!candidate.IsMacroEnabled)
        {
            return ExcelWorkbookSelection.Failure(
                $"Workbook '{candidate.Name}' is not macro-enabled. " +
                "Activate an .xlsm, .xlsb, or .xls workbook.");
        }

        if (!candidate.HasRequiredWorksheet)
        {
            return ExcelWorkbookSelection.Failure(
                $"Workbook '{candidate.Name}' does not contain worksheet '{worksheet}'.");
        }

        string source = usedActiveWorkbook
            ? "active workbook"
            : "configured workbook";
        return new ExcelWorkbookSelection(
            true,
            candidateIndex,
            usedActiveWorkbook,
            $"Using {source} '{candidate.Name}'.");
    }

    private static int IndexOfName(
        IReadOnlyList<ExcelWorkbookCandidate> candidates,
        string workbookName)
    {
        for (int index = 0; index < candidates.Count; index++)
        {
            if (string.Equals(
                    candidates[index].Name,
                    workbookName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static int IndexOfActive(IReadOnlyList<ExcelWorkbookCandidate> candidates)
    {
        for (int index = 0; index < candidates.Count; index++)
        {
            if (candidates[index].IsActive)
                return index;
        }

        return -1;
    }

    private static string OpenWorkbookSuffix(
        IReadOnlyList<ExcelWorkbookCandidate> candidates)
    {
        string names = string.Join(
            ", ",
            candidates
                .Select(candidate => candidate.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name)));
        return names.Length == 0
            ? "No workbooks are open."
            : $"Open workbooks: {names}.";
    }
}
