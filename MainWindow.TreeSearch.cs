using System;
using System.Windows;
using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void PagesTreeSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyPagesTreeSearchFilter();

    private void TakeoffsTreeSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyTakeoffsTreeSearchFilter();

    private void ApplyPagesTreeSearchFilter()
    {
        string folderQuery = TreeSearchQuery(PagesFolderSearchBox.Text);
        string pageQuery = TreeSearchQuery(PagesTreeSearchBox.Text);
        WithTreeExpansionTrackingSuppressed(() =>
        {
            foreach (TreeViewItem item in PagesTree.Items)
                ApplyPagesTreeSearchFilter(item, folderQuery, pageQuery, ancestorFolderMatch: false);
        });
    }

    private bool ApplyPagesTreeSearchFilter(
        TreeViewItem item,
        string folderQuery,
        string pageQuery,
        bool ancestorFolderMatch)
    {
        bool folderFiltering = folderQuery.Length > 0;
        bool pageFiltering = pageQuery.Length > 0;
        bool filtering = folderFiltering || pageFiltering;
        if (!filtering)
        {
            item.Visibility = Visibility.Visible;
            foreach (TreeViewItem child in item.Items)
                ApplyPagesTreeSearchFilter(child, folderQuery, pageQuery, ancestorFolderMatch: false);
            return true;
        }

        bool folderSelfMatch = folderFiltering &&
                               item.Tag is PageFolderNode &&
                               PageTreeFolderSearchText(item).Contains(folderQuery, StringComparison.OrdinalIgnoreCase);
        bool folderScopeMatch = ancestorFolderMatch || folderSelfMatch;
        bool pageSelfMatch = pageFiltering &&
                             item.Tag is PageInfo &&
                             PageTreePageSearchText(item).Contains(pageQuery, StringComparison.OrdinalIgnoreCase);
        bool childMatch = false;
        foreach (TreeViewItem child in item.Items)
            childMatch |= ApplyPagesTreeSearchFilter(child, folderQuery, pageQuery, folderScopeMatch);

        bool visible = PageTreeNodeVisible(
            item,
            folderFiltering,
            pageFiltering,
            folderSelfMatch,
            folderScopeMatch,
            pageSelfMatch,
            childMatch);
        item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (filtering && childMatch)
            item.IsExpanded = true;
        return visible;
    }

    private static bool PageTreeNodeVisible(
        TreeViewItem item,
        bool folderFiltering,
        bool pageFiltering,
        bool folderSelfMatch,
        bool folderScopeMatch,
        bool pageSelfMatch,
        bool childMatch)
    {
        if (folderFiltering && pageFiltering)
            return folderSelfMatch ||
                   childMatch ||
                   item.Tag is PageInfo && folderScopeMatch && pageSelfMatch;

        if (folderFiltering)
            return folderScopeMatch || childMatch;

        return item.Tag is PageInfo && pageSelfMatch || childMatch;
    }

    private static string PageTreeFolderSearchText(TreeViewItem item) =>
        item.Tag is PageFolderNode folder ? folder.Name : "";

    private static string PageTreePageSearchText(TreeViewItem item) =>
        item.Tag is PageInfo page ? page.Name : "";

    private void ApplyTakeoffsTreeSearchFilter()
    {
        string folderQuery = TreeSearchQuery(TakeoffsFolderSearchBox.Text);
        string takeoffQuery = TreeSearchQuery(TakeoffsTreeSearchBox.Text);
        WithTreeExpansionTrackingSuppressed(() =>
        {
            foreach (TreeViewItem item in TakeoffsTree.Items)
                ApplyTakeoffsTreeSearchFilter(item, folderQuery, takeoffQuery, ancestorFolderMatch: false);
        });
    }

    private bool ApplyTakeoffsTreeSearchFilter(
        TreeViewItem item,
        string folderQuery,
        string takeoffQuery,
        bool ancestorFolderMatch)
    {
        bool folderFiltering = folderQuery.Length > 0;
        bool takeoffFiltering = takeoffQuery.Length > 0;
        bool filtering = folderFiltering || takeoffFiltering;
        if (!filtering)
        {
            item.Visibility = Visibility.Visible;
            foreach (TreeViewItem child in item.Items)
                ApplyTakeoffsTreeSearchFilter(child, folderQuery, takeoffQuery, ancestorFolderMatch: false);
            return true;
        }

        bool folderSelfMatch = folderFiltering &&
                               item.Tag is TakeoffFolderNode &&
                               TakeoffTreeFolderSearchText(item).Contains(folderQuery, StringComparison.OrdinalIgnoreCase);
        bool folderScopeMatch = ancestorFolderMatch || folderSelfMatch;
        bool takeoffSelfMatch = takeoffFiltering &&
                                IsTakeoffSearchItem(item) &&
                                TakeoffTreeItemSearchText(item).Contains(takeoffQuery, StringComparison.OrdinalIgnoreCase);
        bool childMatch = false;
        foreach (TreeViewItem child in item.Items)
            childMatch |= ApplyTakeoffsTreeSearchFilter(child, folderQuery, takeoffQuery, folderScopeMatch);

        bool visible = TakeoffTreeNodeVisible(
            item,
            folderFiltering,
            takeoffFiltering,
            folderSelfMatch,
            folderScopeMatch,
            takeoffSelfMatch,
            childMatch);
        item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (filtering && childMatch)
            item.IsExpanded = true;
        return visible;
    }

    private static bool TakeoffTreeNodeVisible(
        TreeViewItem item,
        bool folderFiltering,
        bool takeoffFiltering,
        bool folderSelfMatch,
        bool folderScopeMatch,
        bool takeoffSelfMatch,
        bool childMatch)
    {
        if (folderFiltering && takeoffFiltering)
            return folderSelfMatch ||
                   childMatch ||
                   IsTakeoffSearchItem(item) && folderScopeMatch && takeoffSelfMatch;

        if (folderFiltering)
            return folderScopeMatch || childMatch;

        return IsTakeoffSearchItem(item) && takeoffSelfMatch || childMatch;
    }

    private static string TakeoffTreeFolderSearchText(TreeViewItem item) =>
        item.Tag is TakeoffFolderNode folder ? folder.Name : "";

    private static string TakeoffTreeItemSearchText(TreeViewItem item) =>
        item.Tag switch
        {
            TakeoffItem takeoff => takeoff.Name,
            TakeoffMeasurementNode node => node.Measurement.Name,
            _ => "",
        };

    private static bool IsTakeoffSearchItem(TreeViewItem item) =>
        item.Tag is TakeoffItem or TakeoffMeasurementNode;

    private static string TreeSearchQuery(string? value) =>
        (value ?? "").Trim();
}
