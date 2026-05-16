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
        string query = TreeSearchQuery(PagesTreeSearchBox.Text);
        WithTreeExpansionTrackingSuppressed(() =>
        {
            foreach (TreeViewItem item in PagesTree.Items)
                ApplyPagesTreeSearchFilter(item, query);
        });
    }

    private bool ApplyPagesTreeSearchFilter(TreeViewItem item, string query)
    {
        bool filtering = query.Length > 0;
        bool selfMatch = !filtering || PageTreeSearchText(item).Contains(query, StringComparison.OrdinalIgnoreCase);
        bool childMatch = false;
        foreach (TreeViewItem child in item.Items)
            childMatch |= ApplyPagesTreeSearchFilter(child, query);

        bool visible = selfMatch || childMatch;
        item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (filtering && childMatch)
            item.IsExpanded = true;
        return visible;
    }

    private static string PageTreeSearchText(TreeViewItem item) =>
        item.Tag switch
        {
            PageInfo page => page.Name,
            PageFolderNode folder => folder.Name,
            _ => "",
        };

    private void ApplyTakeoffsTreeSearchFilter()
    {
        string query = TreeSearchQuery(TakeoffsTreeSearchBox.Text);
        WithTreeExpansionTrackingSuppressed(() =>
        {
            foreach (TreeViewItem item in TakeoffsTree.Items)
                ApplyTakeoffsTreeSearchFilter(item, query);
        });
    }

    private bool ApplyTakeoffsTreeSearchFilter(TreeViewItem item, string query)
    {
        bool filtering = query.Length > 0;
        bool selfMatch = !filtering || TakeoffTreeSearchText(item).Contains(query, StringComparison.OrdinalIgnoreCase);
        bool childMatch = false;
        foreach (TreeViewItem child in item.Items)
            childMatch |= ApplyTakeoffsTreeSearchFilter(child, query);

        bool visible = selfMatch || childMatch;
        item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (filtering && childMatch)
            item.IsExpanded = true;
        return visible;
    }

    private static string TakeoffTreeSearchText(TreeViewItem item) =>
        item.Tag switch
        {
            TakeoffItem takeoff => takeoff.Name,
            TakeoffFolderNode folder => folder.Name,
            TakeoffMeasurementNode node => node.Measurement.Name,
            _ => "",
        };

    private static string TreeSearchQuery(string? value) =>
        (value ?? "").Trim();
}
