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
                                 _currentPage!.OverlayLayers.Count > 0;
        bool candidateHasOverlay = candidatePage.OverlayLayers.Count > 0;
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
            "Add This Sheet as New Overlay",
            canSetOverlay,
            () => AddCurrentSheetOverlay(candidatePage)));
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
        bool hasOverlay = node.Page.OverlayLayers.Any(layer =>
            string.Equals(layer.Id, node.Layer.Id, StringComparison.OrdinalIgnoreCase));
        int layerIndex = node.Page.OverlayLayers
            .Select((layer, index) => (layer, index))
            .FirstOrDefault(pair =>
                string.Equals(pair.layer.Id, node.Layer.Id, StringComparison.OrdinalIgnoreCase))
            .index;
        menu.Items.Add(MakeMenuItem(
            "Overlay Properties...",
            true,
            () => ActivateSheetOverlayLayer(node.Page, node.Layer.Id, showProperties: true)));
        if (IsCurrentJobReadOnly)
        {
            menu.Items.Add(MakeMenuItem(
                "Open Overlay Sheet",
                hasOverlay,
                () => OpenSheetOverlaySource(node.Page, node.Layer)));
            menu.Items.Add(MakeMenuItem(
                "Read-only: overlay editing is disabled",
                false,
                () => { }));
            return menu;
        }

        menu.Items.Add(MakeMenuItem(
            node.Layer.IsVisible ? "Hide Overlay" : "Show Overlay",
            hasOverlay,
            () => SetSheetOverlayVisibility(node.Page, node.Layer.Id, !node.Layer.IsVisible)));
        menu.Items.Add(MakeMenuItem(
            "Open Overlay Sheet",
            hasOverlay,
            () => OpenSheetOverlaySource(node.Page, node.Layer)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(
            "Move Layer Up",
            hasOverlay && layerIndex < node.Page.OverlayLayers.Count - 1,
            () => MoveSheetOverlayLayer(node.Page, node.Layer.Id, 1)));
        menu.Items.Add(MakeMenuItem(
            "Move Layer Down",
            hasOverlay && layerIndex > 0,
            () => MoveSheetOverlayLayer(node.Page, node.Layer.Id, -1)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(
            "Remove Overlay Layer",
            hasOverlay,
            () => RemoveSheetOverlayLayer(node.Page, node.Layer.Id)));
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
        menu.Items.Add(MakeMenuItem(
            "Fit by 2 Points",
            !IsCurrentJobReadOnly &&
            !string.IsNullOrWhiteSpace(currentPage.OverlayPageFolder),
            () => BeginSheetOverlayPointEdit(currentPage)));
        return true;
    }
}
