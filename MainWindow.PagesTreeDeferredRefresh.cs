using System;
using System.Collections.Generic;
using System.Linq;

namespace OurPlanCore;

public partial class MainWindow
{
    private const int MaxImmediatePageTakeoffIndicatorRefreshCount = 40;
    private readonly HashSet<string> _dirtyPageTakeoffIndicatorFolders = new(StringComparer.OrdinalIgnoreCase);

    private void RefreshPageTakeoffIndicatorsForFoldersOrDefer(IReadOnlyList<string> pageFolders)
    {
        if (pageFolders.Count == 0)
            return;

        if (pageFolders.Count <= MaxImmediatePageTakeoffIndicatorRefreshCount)
        {
            RefreshPageTakeoffIndicatorsForFolders(pageFolders);
            return;
        }

        MarkPageTakeoffIndicatorsDirty(pageFolders);
        if (_currentPage != null)
            TryRefreshDirtyPageTakeoffIndicator(_currentPage.FolderPath);
    }

    private void MarkPageTakeoffIndicatorsDirty(IEnumerable<string> pageFolders)
    {
        foreach (string pageFolder in pageFolders)
        {
            if (!string.IsNullOrWhiteSpace(pageFolder))
                _dirtyPageTakeoffIndicatorFolders.Add(NormalizePathForCompare(pageFolder));
        }
    }

    private bool TryRefreshDirtyPageTakeoffIndicator(string? pageFolder)
    {
        if (string.IsNullOrWhiteSpace(pageFolder))
            return false;

        string pageKey = NormalizePathForCompare(pageFolder);
        if (!_dirtyPageTakeoffIndicatorFolders.Remove(pageKey))
            return false;

        RefreshPageTakeoffIndicatorsForFolder(pageFolder);
        return true;
    }

    private void ClearDirtyPageTakeoffIndicators() =>
        _dirtyPageTakeoffIndicatorFolders.Clear();
}
