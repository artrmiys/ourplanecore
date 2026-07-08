using System.Windows;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private PageBookmarksController? _bookmarksController;

    private void InitializeBookmarksTab()
    {
        if (PagesSideTabs == null)
            return;

        _bookmarksController = new PageBookmarksController(
            owner: this,
            pagesSideTabs: PagesSideTabs,
            dockToggleButton: BtnDockBookmarksBelowPages,
            dockContentHost: BookmarksDockContentHost,
            dockSplitter: BookmarksDockSplitter,
            dockPanel: BookmarksDockPanel,
            dockSplitterRow: BookmarksDockSplitterRow,
            dockRow: BookmarksDockRow,
            setStatus: message => TxtStatus.Text = message,
            currentJob: () => _currentJob,
            currentPage: () => _currentPage,
            viewport: _viewport,
            openBookmarkView: OpenPageBookmarkView);
        _bookmarksController.Initialize();
    }

    private void BookmarkDockToggle_Changed(object sender, RoutedEventArgs e) =>
        _bookmarksController?.BookmarkDockToggleChanged(sender);

    private void LoadPageBookmarksForJob() =>
        _bookmarksController?.LoadForJob();

    private void AddBookmarkFromShortcut() =>
        _bookmarksController?.AddFromShortcut();

    private void OpenPageBookmarkView(PageInfo page, PdfViewport.ViewState viewState)
    {
        SaveCurrentPageScale();
        SaveActivePageTabViewState();

        PageTabState? tab = FindPageTab(page.FolderPath) ?? SelectedPageTab();
        if (tab == null)
        {
            tab = new PageTabState(page.FolderPath, page.Name);
            _pageTabs.Add(tab);
        }

        tab.PageFolder = page.FolderPath;
        tab.PageName = page.Name;
        tab.ViewState = viewState;
        LoadPageFromTab(tab, page);
    }
}
