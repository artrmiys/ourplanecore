using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private static readonly RoutedCommand OpenCommandPaletteCommand =
        new(nameof(OpenCommandPaletteCommand), typeof(MainWindow));

    private void ShowCommandPalette()
    {
        var dialog = new CommandPaletteDialog(BuildCommandPaletteItems())
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SelectedCommandId))
            return;

        ExecuteCommandPaletteItem(dialog.SelectedCommandId);
    }

    private IReadOnlyList<CommandPaletteItem> BuildCommandPaletteItems()
    {
        bool hasJob = _currentJob != null;
        bool hasPage = _currentPage != null;
        bool hasRightTabs = _rightWorkspaceTabs != null;
        bool hasSheetTakeoffTargets = _currentPage != null && TakeoffsForPage(_currentPage.FolderPath).Any();
        int selectedMeasurementCount = _viewport.GetSelectedMeasurements().Count;

        var items = new List<CommandPaletteItem>();
        void Add(
            string id,
            string title,
            string group,
            string shortcut,
            string description,
            bool canExecute = true,
            string disabledReason = "") =>
            items.Add(new CommandPaletteItem(id, title, group, shortcut, description, canExecute, disabledReason));

        Add("file.open", "Open Job", "File", "Ctrl+O", "Open the internal job picker.");
        Add("file.openRecent", "Open Recent Job", "File", "Ctrl+Shift+O", "Open the Recent Jobs picker.");
        Add("file.openJobsFolder", "Open Jobs Folder", "File", "", "Open a root folder that contains multiple jobs.");
        Add("file.newJob", "New Job", "File", "", "Create a new OurPlaneCore job folder.");
        Add("file.sampleJob", "Create Sample Job", "File", "", "Create and open a small local sample job.");
        Add("file.importPdf", "Import PDF", "File", "", "Import PDF pages into the current job.", hasJob, "Open or create a job first.");
        Add("file.exportPdf", "Export PDF", "File", "", "Export selected/all sheets to a PDF.", hasJob, "Open or create a job first.");
        Add("file.save", "Save", "File", "Ctrl+S", "Save current takeoff data.", hasJob, "Open or create a job first.");
        Add("file.exportCsv", "Export CSV", "File", "", "Export takeoff rows to CSV.", hasJob, "Open or create a job first.");
        Add("file.exportTxt", "Export TXT", "File", "", "Export selected/all takeoffs in PlanSwift text format.", hasJob, "Open or create a job first.");
        Add("file.exportExcel", "Export Excel", "File", "", "Export selected/all takeoffs to .xlsx columns J/K/L from J10.", hasJob, "Open or create a job first.");

        Add("view.fit", "Fit Page", "View", "F", "Fit the active page to the viewport.", hasPage, "Select a page first.");
        Add("view.zoomIn", "Zoom In", "View", "Ctrl++", "Zoom into the active page.", hasPage, "Select a page first.");
        Add("view.zoomOut", "Zoom Out", "View", "Ctrl+-", "Zoom out of the active page.", hasPage, "Select a page first.");
        Add("view.toggleTheme", "Toggle Dark Theme", "View", "", "Switch between light and dark UI theme.");
        Add("view.viewportBackground", "Viewport Background", "View", "", "Open viewport background presets.");
        Add("view.toggleInbox", "Toggle AI Inbox", "View", "", "Collapse or expand the AI Inbox panel.");
        Add("view.mainView", "Show Main View", "Workspace", "", "Switch to the drawing canvas workspace.");
        Add("view.sheetManager", "Show Sheet Manager", "Workspace", "", "Switch to the sheet table / PDF metadata workspace.");
        Add("view.takeoffManager", "Show Takeoff Manager", "Workspace", "", "Switch to the takeoff item manager workspace.");
        Add("view.aiManager", "Show AI Manager", "Workspace", "", "Switch to the AI inbox and marker manager workspace.");
        Add("view.3dManager", "Show 3D Manager", "Workspace", "", "Switch to the 3D draft manager workspace.");
        Add("view.takeoffsTab", "Show Takeoffs Tab", "View", "", "Select the Takeoffs workspace tab.", hasRightTabs, "Workspace tabs are not ready yet.");
        Add("view.estimatingTab", "Show Estimating Tab", "View", "", "Select the Estimating workspace tab.", hasRightTabs, "Workspace tabs are not ready yet.");
        Add("view.massingTab", "Show 3D Massing Tab", "View", "", "Select the 3D Massing workspace tab.", hasRightTabs, "Workspace tabs are not ready yet.");

        Add("tool.select", "Select Tool", "Tools", "E", "Use box selection and measurement editing.");
        Add("tool.pan", "Pan Tool", "Tools", "V", "Use left-button panning; right-button pan still works.");
        Add("tool.scale", "Scale Tool", "Tools", "S", "Draw a calibration line for sheet scale.");
        Add("tool.count", "Count Tool", "Tools", "P", "Record count marks into a takeoff item.", hasJob && hasPage, "Open a job and select a page first.");
        Add("tool.line", "Line Tool", "Tools", "L", "Record line measurements into a takeoff item.", hasJob && hasPage, "Open a job and select a page first.");
        Add("tool.area", "Area Tool", "Tools", "A", "Record area measurements into a takeoff item.", hasJob && hasPage, "Open a job and select a page first.");
        Add("tool.toggleRecord", "Toggle Record", "Tools", "R", "Toggle digitizer record mode for the current drawing tool.", hasJob && hasPage, "Open a job and select a page first.");
        Add("tool.toggleSnap", "Toggle Snap", "Tools", "F3", "Toggle snap to existing takeoff points.");
        Add("tool.toggleOrtho", "Toggle Ortho", "Tools", "F8", "Toggle 90/45-degree ortho constraint.");

        Add("edit.copyMeasurements", "Copy Selected Measurements", "Edit", "Ctrl+C", "Copy the selected measurements.", selectedMeasurementCount > 0, "Select one or more measurements first.");
        Add("edit.pasteMeasurements", "Paste Measurements", "Edit", "Ctrl+V", "Paste copied measurements to the active page.", _measurementClipboard != null && hasPage, "Copy measurements and select a page first.");

        Add("pages.sortArchStruct", "Sort Pages A/S", "Pages", "", "Move A sheets to Arch, S sheets to Struct, and trailing '-' sheets to others.", hasJob, "Open or create a job first.");
        Add("pages.sortSuffix", "Sort Pages D/Sec/WT", "Pages", "", "Move suffix sheets into details/sections/units and reorder v/wt/ft/sv/sw at Pages root.", hasJob, "Open or create a job first.");
        Add("pages.repairLinks", "Repair Measurement Links", "Pages", "", "Reconnect stale measurement page links after page renames/imports.", hasJob, "Open or create a job first.");
        Add("pages.autoFolders", "Auto Page Folders", "Pages", "", "Create the standard page folder tree.", hasJob, "Open or create a job first.");
        Add("pages.autoName", "Auto Name PDF", "Pages", "", "Preview and apply PDF sheet names.", hasJob, "Open or create a job first.");
        Add("pages.autoScale", "Auto Scale PDF", "Pages", "", "Preview and apply PDF sheet scales.", hasJob, "Open or create a job first.");
        Add("pages.autoNameScale", "Auto Name + Scale PDF", "Pages", "", "Preview and apply PDF sheet names and scales.", hasJob, "Open or create a job first.");
        Add("pages.aiFillMetadata", "AI Fill PDF Metadata", "Pages", "", "Queue GPT fallback for missing sheet metadata.", hasJob, "Open or create a job first.");
        Add("layers.allOn", "PDF Layers All On", "PDF Layers", "", "Turn all active page PDF layers on.", BtnLayersOn.IsEnabled, "Select a PDF page with layers first.");
        Add("layers.allOff", "PDF Layers All Off", "PDF Layers", "", "Turn all active page PDF layers off.", BtnLayersOff.IsEnabled, "Select a PDF page with layers first.");
        Add("layers.clearHighlight", "Clear PDF Layer Highlights", "PDF Layers", "", "Clear highlighted PDF layers.", BtnLayersClearHi.IsEnabled, "Select a PDF page with layers first.");

        Add("takeoffs.newFolder", "New Takeoff Folder", "Takeoffs", "", "Create a takeoff folder under the selected/root folder.", hasJob, "Open or create a job first.");
        Add("takeoffs.newItem", "New Takeoff Item", "Takeoffs", "T", "Create a takeoff item under the selected/root folder.", hasJob, "Open or create a job first.");
        Add("takeoffs.activeFind", "Show Active Takeoff", "Takeoffs", "", "Scroll the Takeoffs tree to the active target item.", _activeItem != null, "Select a takeoff item first.");
        Add("takeoffs.activeProperties", "Active Takeoff Properties", "Takeoffs", "", "Edit properties for the active target item.", _activeItem != null, "Select a takeoff item first.");
        Add("takeoffs.activeRecord", "Record Active Takeoff", "Takeoffs", "Space", "Start or stop recording into the active target item.", _activeItem != null && hasPage, "Select a takeoff item and sheet first.");
        Add("takeoffs.activePrevious", "Previous Takeoff Target", "Takeoffs", "", "Switch the active target to the previous takeoff item.", _takeoffItems.Count > 1, "Create at least two takeoff items first.");
        Add("takeoffs.activeNext", "Next Takeoff Target", "Takeoffs", "", "Switch the active target to the next takeoff item.", _takeoffItems.Count > 1, "Create at least two takeoff items first.");
        Add("takeoffs.activeSheetNext", "Next Sheet Takeoff Target", "Takeoffs", "", "Switch to the next takeoff item measured on the active sheet.", hasSheetTakeoffTargets, "Select a sheet with measured takeoffs first.");
        Add("takeoffs.autoTree", "Auto Takeoff Tree", "Takeoffs", "", "Create the standard takeoff folder tree.", hasJob, "Open or create a job first.");
        Add("takeoffs.fromPages", "Create Takeoffs From Pages", "Takeoffs", "", "Create top takeoff folders from CAPS page/folder names.", hasJob, "Open or create a job first.");

        Add("ai.settings", "OpenAI Settings", "AI", "", "Review key status and AI model settings.");
        Add("ai.addObservation", "Add AI Observation", "AI", "", "Save a manual AI observation for the active job/page.", hasJob && hasPage, "Open a job and select a page first.");
        Add("ai.runSelected", "Run Selected AI Request", "AI", "", "Run the selected AI Inbox request.", hasJob && !_isRunningAiRequest, "Open a job and select an AI request first.");
        Add("ai.runNewBookmarks", "Run New Crop Bookmarks", "AI", "", "Send only new crop bookmarks to OpenAI.", hasJob && !_isRunningAiRequest, "Open a job first.");
        Add("ai.retryFailedBookmarks", "Retry Failed Crop Bookmarks", "AI", "", "Retry only failed crop bookmarks.", hasJob && !_isRunningAiRequest, "Open a job first.");
        Add("ai.createMarkerSet", "Create Marker Set", "AI", "", "Save the current marker filters as a marker set.", hasJob, "Open a job first.");
        Add("ai.manageMarkerSets", "Manage Marker Sets", "AI", "", "Open marker set management.", hasJob, "Open a job first.");
        Add("ai.exportMarkers", "Export Marker Context", "AI", "", "Export visible marker context JSON.", hasJob, "Open a job first.");
        Add("ai.open3dWindow", "Open 3D Window", "3D Massing", "", "Open the detached orbitable 3D viewport with saved marker points.", hasJob, "Open a job first.");
        Add("ai.build3d", "Build 3D Draft", "3D Massing", "", "Build the 3D massing draft from reviewed markers.", hasJob, "Open a job first.");
        Add("ai.build3dFromWalls", "Build 3D From Takeoffs", "3D Massing", "", "Build the 3D massing draft from Walls/Areas/Sqft level Line/Area measurements.", hasJob, "Open a job first.");
        Add("ai.autoRoof", "Auto Roof", "3D Massing", "", "Queue reviewable roof-marker candidates from the active sheet.", hasJob && hasPage, "Open a job and select a page first.");
        Add("ai.reviewRoof", "Review Roof", "3D Massing", "", "Review and edit roof type, pitch, notes, and guides.", _massingReviewRoofButton?.IsEnabled == true, "Build or load a 3D draft with roof guides first.");
        Add("ai.reviewOpenings", "Review Openings", "3D Massing", "", "Review projected door/window/opening markers.", _massingReviewOpeningsButton?.IsEnabled == true, "Build or load a 3D draft with opening candidates first.");
        Add("ai.accept3d", "Accept 3D Draft", "3D Massing", "", "Mark the current 3D massing draft as reviewed context.", _massingAcceptDraftButton?.IsEnabled == true, "Build or load a 3D draft first.");

        return items;
    }

    private void ExecuteCommandPaletteItem(string id)
    {
        switch (id)
        {
            case "file.open": BtnOpen_Click(this, new RoutedEventArgs()); break;
            case "file.openRecent": ShowRecentJobPicker(); break;
            case "file.openJobsFolder": BtnOpenJobsFolder_Click(this, new RoutedEventArgs()); break;
            case "file.newJob": BtnNewJob_Click(this, new RoutedEventArgs()); break;
            case "file.sampleJob": CreateSampleJob(); break;
            case "file.importPdf": BtnImport_Click(this, new RoutedEventArgs()); break;
            case "file.exportPdf": BtnExportPdf_Click(this, new RoutedEventArgs()); break;
            case "file.save": BtnSave_Click(this, new RoutedEventArgs()); break;
            case "file.exportCsv": BtnExportCsv_Click(this, new RoutedEventArgs()); break;
            case "file.exportTxt": BtnExportTxt_Click(this, new RoutedEventArgs()); break;
            case "file.exportExcel": BtnExportExcel_Click(this, new RoutedEventArgs()); break;

            case "view.fit": BtnFit_Click(this, new RoutedEventArgs()); break;
            case "view.zoomIn": BtnZoomIn_Click(this, new RoutedEventArgs()); break;
            case "view.zoomOut": BtnZoomOut_Click(this, new RoutedEventArgs()); break;
            case "view.toggleTheme":
                ApplyTheme(!string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase), persist: true);
                break;
            case "view.viewportBackground": BtnViewportBg_Click(BtnDisplayViewportBg, new RoutedEventArgs()); break;
            case "view.toggleInbox": BtnToggleInbox_Click(BtnToggleInbox, new RoutedEventArgs()); break;
            case "view.mainView": SelectWorkspaceTab("MainView"); break;
            case "view.sheetManager": SelectWorkspaceTab("SheetManager"); break;
            case "view.takeoffManager": SelectWorkspaceTab("TakeoffManager"); break;
            case "view.aiManager": SelectWorkspaceTab("AiManager"); break;
            case "view.3dManager": SelectWorkspaceTab("3DManager"); break;
            case "view.takeoffsTab": SelectRightWorkspaceTab("Takeoffs"); break;
            case "view.estimatingTab": SelectRightWorkspaceTab("Estimating"); break;
            case "view.massingTab": SelectRightWorkspaceTab("3D Massing"); break;

            case "tool.select": SetTool("select"); break;
            case "tool.pan": SetTool("pan"); break;
            case "tool.scale": SetTool("scale"); break;
            case "tool.count": SetTool("point"); break;
            case "tool.line": SetTool("line"); break;
            case "tool.area": SetTool("area"); break;
            case "tool.toggleRecord":
                if (_recordButton != null)
                    _recordButton.IsChecked = _recordButton.IsChecked != true;
                break;
            case "tool.toggleSnap": SetSnapMode(!_viewport.SnapEnabled); break;
            case "tool.toggleOrtho": SetOrthoMode(!_viewport.OrthoEnabled); break;

            case "edit.copyMeasurements": CopyMeasurementsToClipboard(_viewport.GetSelectedMeasurements()); break;
            case "edit.pasteMeasurements": PasteMeasurementsFromClipboard(); break;

            case "pages.sortArchStruct": BtnSortPagesArchStruct_Click(this, new RoutedEventArgs()); break;
            case "pages.sortSuffix": BtnSortPagesSuffix_Click(this, new RoutedEventArgs()); break;
            case "pages.repairLinks": BtnRepairMeasurementPageLinks_Click(this, new RoutedEventArgs()); break;
            case "pages.autoFolders": BtnAutoPageFolders_Click(this, new RoutedEventArgs()); break;
            case "pages.autoName": BtnAutoRenamePdf_Click(this, new RoutedEventArgs()); break;
            case "pages.autoScale": BtnAutoScalePdf_Click(this, new RoutedEventArgs()); break;
            case "pages.autoNameScale": BtnAutoRenameScalePdf_Click(this, new RoutedEventArgs()); break;
            case "pages.aiFillMetadata": BtnQueuePdfMetadataFallback_Click(this, new RoutedEventArgs()); break;
            case "layers.allOn": BtnLayersOn_Click(this, new RoutedEventArgs()); break;
            case "layers.allOff": BtnLayersOff_Click(this, new RoutedEventArgs()); break;
            case "layers.clearHighlight": BtnLayersClearHi_Click(this, new RoutedEventArgs()); break;

            case "takeoffs.newFolder": BtnNewTakeoffFolder_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.newItem": BtnNewItem_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.activeFind": BtnActiveTakeoffFind_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.activeProperties": BtnActiveTakeoffProperties_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.activeRecord": BtnActiveTakeoffRecord_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.activePrevious": BtnActiveTakeoffPrevious_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.activeNext": BtnActiveTakeoffNext_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.activeSheetNext": MoveActiveSheetTakeoffTarget(1); break;
            case "takeoffs.autoTree": BtnAutoTakeoffTree_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.fromPages": BtnAutoTakeoffFromPages_Click(this, new RoutedEventArgs()); break;

            case "ai.settings": BtnOpenAiSettings_Click(this, new RoutedEventArgs()); break;
            case "ai.addObservation": BtnAddObservation_Click(this, new RoutedEventArgs()); break;
            case "ai.runSelected": BtnRunAi_Click(this, new RoutedEventArgs()); break;
            case "ai.runNewBookmarks": BtnRunNewBookmarks_Click(this, new RoutedEventArgs()); break;
            case "ai.retryFailedBookmarks": BtnRetryFailedBookmarks_Click(this, new RoutedEventArgs()); break;
            case "ai.createMarkerSet": BtnCreateMarkerSet_Click(this, new RoutedEventArgs()); break;
            case "ai.manageMarkerSets": BtnManageMarkerSets_Click(this, new RoutedEventArgs()); break;
            case "ai.exportMarkers": BtnExportMarkers_Click(this, new RoutedEventArgs()); break;
            case "ai.open3dWindow": OpenMassing3DWindow(); break;
            case "ai.build3d": BtnBuildMassingDraft_Click(this, new RoutedEventArgs()); break;
            case "ai.build3dFromWalls": BuildMassingDraftFromWallTakeoffs(); break;
            case "ai.autoRoof": BtnDetectRoof_Click(this, new RoutedEventArgs()); break;
            case "ai.reviewRoof": BtnReviewRoof_Click(this, new RoutedEventArgs()); break;
            case "ai.reviewOpenings": BtnReviewOpenings_Click(this, new RoutedEventArgs()); break;
            case "ai.accept3d": BtnAcceptMassingDraft_Click(this, new RoutedEventArgs()); break;

            default:
                TxtStatus.Text = $"Unknown command palette action: {id}.";
                break;
        }
    }

    private void SelectRightWorkspaceTab(string header)
    {
        if (_rightWorkspaceTabs == null)
            return;

        foreach (TabItem item in _rightWorkspaceTabs.Items.OfType<TabItem>())
        {
            if (!string.Equals(item.Header?.ToString(), header, StringComparison.OrdinalIgnoreCase))
                continue;

            _rightWorkspaceTabs.SelectedItem = item;
            return;
        }
    }

    private void SelectWorkspaceTab(string key)
    {
        foreach (TabItem item in WorkspaceTabs.Items.OfType<TabItem>())
        {
            if (!string.Equals(item.Tag?.ToString(), key, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Header?.ToString(), key, StringComparison.OrdinalIgnoreCase))
                continue;

            WorkspaceTabs.SelectedItem = item;
            return;
        }
    }
}
