using System;
using System.Windows.Controls;

namespace OurPlaneCore;

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
        bool candidateIsCurrent = hasCurrentPage && SameFolder(_currentPage!.FolderPath, candidatePage.FolderPath);

        var menu = new MenuItem { Header = "Sheet Overlay" };
        menu.Items.Add(MakeMenuItem("Use This Sheet as Current Overlay", canSetOverlay, () => SetCurrentSheetOverlay(candidatePage)));
        menu.Items.Add(MakeMenuItem(
            "Auto Select + Fit This Sheet",
            _currentJob != null,
            () => AutoFitSheetOverlay(candidatePage)));
        menu.Items.Add(MakeMenuItem("Clear Current Sheet Overlay", currentHasOverlay, ClearCurrentSheetOverlay));
        if (currentHasOverlay)
            menu.Items.Add(BuildSheetOverlayAdjustmentMenu(_currentPage!, "Adjust Current Overlay"));
        if (candidateHasOverlay && !candidateIsCurrent)
            menu.Items.Add(BuildSheetOverlayAdjustmentMenu(candidatePage, "Adjust This Sheet Overlay"));
        menu.Items.Add(new Separator());

        var colorMenu = new MenuItem { Header = "Current Overlay Color", IsEnabled = currentHasOverlay };
        foreach ((string label, string hex) in SheetOverlayColors)
            colorMenu.Items.Add(MakeCurrentSheetOverlayColorMenuItem(label, hex, currentHasOverlay));
        menu.Items.Add(colorMenu);

        if (!candidateHasOverlay && !currentHasOverlay)
            menu.Items.Add(MakeMenuItem("No overlay configured", false, () => { }));

        return menu;
    }

    private ContextMenu BuildPageOverlayContextMenu(PageOverlayNode node)
    {
        var menu = new ContextMenu();
        bool isCurrent = _currentPage != null && SameFolder(_currentPage.FolderPath, node.Page.FolderPath);
        menu.Items.Add(MakeMenuItem(
            node.Page.OverlayVisible ? "Hide Overlay" : "Show Overlay",
            true,
            () => TogglePageOverlayVisibility(node.Page)));
        menu.Items.Add(BuildSheetOverlayAdjustmentMenu(node.Page, "Adjust Overlay"));
        menu.Items.Add(new Separator());

        var colorMenu = new MenuItem { Header = "Color" };
        foreach ((string label, string hex) in SheetOverlayColors)
            colorMenu.Items.Add(MakePageOverlayColorMenuItem(node.Page, label, hex));
        menu.Items.Add(colorMenu);
        menu.Items.Add(MakeMenuItem("Clear Overlay", true, () =>
            ClearPageOverlay(node.Page)));
        if (!isCurrent)
            menu.Items.Add(MakeMenuItem("Open Sheet", true, () => OpenPageInActiveTab(node.Page)));
        return menu;
    }

    private bool AddCurrentSheetOverlayAdjustmentMenuItems(ContextMenu menu)
    {
        if (_currentPage == null)
            return false;

        PageInfo currentPage = _currentPage;
        if (string.IsNullOrWhiteSpace(currentPage.OverlayPageFolder))
        {
            menu.Items.Add(MakeMenuItem(
                "Auto Select + Fit Sheet Overlay",
                _currentJob != null,
                () => AutoFitSheetOverlay(currentPage)));
            return true;
        }

        menu.Items.Add(BuildSheetOverlayAdjustmentMenu(currentPage, "Compare Overlay"));
        return true;
    }

    private MenuItem BuildSheetOverlayAdjustmentMenu(PageInfo page, string header)
    {
        bool hasOverlay = !string.IsNullOrWhiteSpace(page.OverlayPageFolder);
        var menu = new MenuItem { Header = header, IsEnabled = hasOverlay };
        menu.Items.Add(MakeMenuItem("Auto Fit", hasOverlay, () => AutoFitSheetOverlay(page)));
        menu.Items.Add(MakeMenuItem("Edit by Points", hasOverlay, () => BeginSheetOverlayPointEdit(page)));
        menu.Items.Add(MakeMenuItem("Edit Transform...", hasOverlay, () => EditSheetOverlayTransform(page)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Move Left 6 pt", hasOverlay, () => NudgeSheetOverlay(page, -6, 0)));
        menu.Items.Add(MakeMenuItem("Move Right 6 pt", hasOverlay, () => NudgeSheetOverlay(page, 6, 0)));
        menu.Items.Add(MakeMenuItem("Move Up 6 pt", hasOverlay, () => NudgeSheetOverlay(page, 0, -6)));
        menu.Items.Add(MakeMenuItem("Move Down 6 pt", hasOverlay, () => NudgeSheetOverlay(page, 0, 6)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Scale Up 5%", hasOverlay, () => ScaleSheetOverlay(page, 1.05)));
        menu.Items.Add(MakeMenuItem("Scale Down 5%", hasOverlay, () => ScaleSheetOverlay(page, 1 / 1.05)));
        menu.Items.Add(MakeMenuItem("Rotate Left 1 deg", hasOverlay, () => RotateSheetOverlay(page, -1)));
        menu.Items.Add(MakeMenuItem("Rotate Right 1 deg", hasOverlay, () => RotateSheetOverlay(page, 1)));
        menu.Items.Add(MakeMenuItem("Reset Transform", hasOverlay, () =>
            SetSheetOverlayTransform(page, 0, 0, 1, 0, "Overlay transform reset.")));
        return menu;
    }

    private MenuItem MakePageOverlayColorMenuItem(PageInfo page, string label, string hex)
    {
        var item = MakeMenuItem(label, true, () => SetPageSheetOverlayColor(page, hex));
        item.IsCheckable = true;
        item.IsChecked = string.Equals(page.OverlayColor, hex, StringComparison.OrdinalIgnoreCase);
        return item;
    }

    private MenuItem MakeCurrentSheetOverlayColorMenuItem(string label, string hex, bool isEnabled)
    {
        var item = MakeMenuItem(label, isEnabled, () => SetCurrentSheetOverlayColor(hex));
        item.IsCheckable = true;
        item.IsChecked = _currentPage != null &&
                         string.Equals(CurrentSheetOverlayColor(), hex, StringComparison.OrdinalIgnoreCase);
        return item;
    }
}
