using System.Windows.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private MenuItem BuildSheetOverlayMenu(PageInfo candidatePage)
    {
        bool hasCurrentPage = _currentPage != null;
        bool canSetOverlay = hasCurrentPage &&
                             !SameFolder(_currentPage!.FolderPath, candidatePage.FolderPath);
        bool currentHasOverlay = hasCurrentPage &&
                                 !string.IsNullOrWhiteSpace(_currentPage!.OverlayPageFolder);
        bool candidateHasOverlay = !string.IsNullOrWhiteSpace(candidatePage.OverlayPageFolder);
        bool candidateIsCurrent = hasCurrentPage &&
                                  SameFolder(_currentPage!.FolderPath, candidatePage.FolderPath);
        PageInfo propertiesPage = candidateIsCurrent || candidateHasOverlay
            ? candidatePage
            : _currentPage ?? candidatePage;

        var menu = new MenuItem { Header = "Sheet Overlay" };
        if (IsCurrentJobReadOnly)
        {
            menu.Items.Add(MakeMenuItem(
                "Overlay Properties...",
                true,
                () => ShowSheetOverlayProperties(propertiesPage)));
            menu.Items.Add(MakeMenuItem(
                "Read-only: overlay editing is disabled",
                false,
                () => { }));
            return menu;
        }

        menu.Items.Add(MakeMenuItem(
            "Use This Sheet as Current Overlay",
            canSetOverlay,
            () => SetCurrentSheetOverlay(candidatePage)));
        menu.Items.Add(MakeMenuItem(
            "Overlay Properties...",
            hasCurrentPage || candidateHasOverlay,
            () => ShowSheetOverlayProperties(propertiesPage)));
        menu.Items.Add(MakeMenuItem(
            "Clear Current Sheet Overlay",
            currentHasOverlay,
            ClearCurrentSheetOverlay));
        return menu;
    }

    private ContextMenu BuildPageOverlayContextMenu(PageOverlayNode node)
    {
        var menu = new ContextMenu();
        bool hasOverlay = !string.IsNullOrWhiteSpace(node.Page.OverlayPageFolder);
        menu.Items.Add(MakeMenuItem(
            "Overlay Properties...",
            true,
            () => ShowSheetOverlayProperties(node.Page)));
        if (IsCurrentJobReadOnly)
        {
            menu.Items.Add(MakeMenuItem(
                "Open Overlay Sheet",
                hasOverlay,
                () => OpenSheetOverlaySource(node.Page)));
            menu.Items.Add(MakeMenuItem(
                "Read-only: overlay editing is disabled",
                false,
                () => { }));
            return menu;
        }

        menu.Items.Add(MakeMenuItem(
            node.Page.OverlayVisible ? "Hide Overlay" : "Show Overlay",
            hasOverlay,
            () => SetSheetOverlayVisibility(node.Page, !node.Page.OverlayVisible)));
        menu.Items.Add(MakeMenuItem(
            "Open Overlay Sheet",
            hasOverlay,
            () => OpenSheetOverlaySource(node.Page)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(
            "Clear Overlay",
            hasOverlay,
            () =>
            {
                ClearPageOverlay(node.Page);
            }));
        return menu;
    }

    private bool AddCurrentSheetOverlayAdjustmentMenuItems(ContextMenu menu)
    {
        if (_currentPage == null)
            return false;

        PageInfo currentPage = _currentPage;
        menu.Items.Add(MakeMenuItem(
            "Overlay Properties...",
            true,
            () => ShowSheetOverlayProperties(currentPage)));
        menu.Items.Add(MakeMenuItem(
            "Auto Fit Overlay",
            !IsCurrentJobReadOnly && _currentJob != null,
            () => AutoFitSheetOverlay(currentPage)));
        return true;
    }
}
