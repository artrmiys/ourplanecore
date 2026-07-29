using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OurPlanCore.Controls;

namespace OurPlanCore;

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
        bool currentPageHasSheetOverlay = _currentPage != null &&
                                          !string.IsNullOrWhiteSpace(_currentPage.OverlayPageFolder);
        bool hasRightTabs = _rightWorkspaceTabs != null;
        bool hasSheetTakeoffTargets = _currentPage != null && TakeoffsForPage(_currentPage.FolderPath).Any();
        IReadOnlyList<Measurement> selectedMeasurements = _viewport.GetSelectedMeasurements();
        int selectedMeasurementCount = selectedMeasurements.Count;
        int selectedLineMeasurementCount = selectedMeasurements.Count(IsPointAlongLineSource);
        int selectedAreaMeasurementCount = selectedMeasurements.Count(m => m.MType == "area");

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
        string Shortcut(string sequence) => KeyboardShortcutKeys.EnglishLayoutDisplay(sequence);

        Add("file.open", "Open / Import", "File", "Ctrl+O", "Open the job picker. The top toolbar button also contains PDF import.");
        Add("file.openRecent", "Open Recent Job", "File", "Ctrl+Shift+O", "Open the Recent Jobs picker.");
        Add("file.openJobsFolder", "Open Jobs Folder", "File", "", "Open a root folder that contains multiple jobs.");
        Add("file.newJob", "New PDF Job", "File", "", "Create a new OurPlanCore job from a folder of PDFs.");
        Add("file.blankJob", "Blank Job", "File", "", "Create an empty OurPlanCore job without selecting PDFs.");
        Add("file.sampleJob", "Create Sample Job", "File", "", "Create and open a guided local sample project.");
        Add("file.importPlanSwift", "Import PlanSwift Job", "File", "", "Convert a read-only PlanSwift job into a new OurPlanCore job.");
        Add("file.importPlanSwiftCurrent", "Import PlanSwift to Current Job", "File", "", "Import a read-only PlanSwift job under 01. planswift in the current job.", hasJob, "Open or create a job first.");
        Add("file.importPdf", "Import PDF(s)", "File", "", "Import one or many PDF files into the current job.", hasJob, "Open or create a job first.");
        Add("file.importPdfFolder", "Import PDF Folder", "File", "", "Recursively import every PDF file from a selected folder into the current job.", hasJob, "Open or create a job first.");
        Add("file.importPdfTakeoffs", "Import PDF Takeoffs", "File", "", "Create a new job from PDF takeoffs or import PDF rulers/takeoffs into the current job.");
        Add("file.exportPdf", "Export PDF", "File", "", "Export selected/all sheets to a PDF.", hasJob, "Open or create a job first.");
        Add("file.save", "Save", "File", "Ctrl+S", "Save current takeoff data.", hasJob, "Open or create a job first.");
        Add("file.exportCsv", "Export CSV", "File", "", "Export takeoff rows to CSV.", hasJob, "Open or create a job first.");
        Add("file.exportTxt", "Export TXT", "File", "", "Export selected/all takeoffs in PlanSwift text format.", hasJob, "Open or create a job first.");
        Add("file.exportExcel", "Export Excel", "File", "", "Export selected/all takeoffs to .xlsx columns J/K/L from J10.", hasJob, "Open or create a job first.");
        Add("file.exportCurrentExcel", "Export to Current Excel", "File", "", "Write selected takeoff folder/item rows to the active cell in an open Excel workbook.", hasJob, "Open or create a job first.");
        Add("file.excelMacroSqft", "Excel Macro: SQFT", "File", "", "Append selected SQFT rows to TemplateCom and run A2_SQFT_calc.", hasJob, "Open a job and select a Takeoffs scope first.");
        Add("file.excelMacroWalls", "Excel Macro: Walls", "File", "", "Append selected wall rows with numeric floor groups and run A3_Walls_Calc_AllGroup.", hasJob, "Open a job and select a Takeoffs scope first.");
        Add("file.excelMacroOpenings", "Excel Macro: Openings", "File", "", "Append selected openings from Z158 with numeric floor groups and run A5_Openings.", hasJob, "Open a job and select a Takeoffs scope first.");

        Add("view.fit", "Fit Page", "View", Shortcut("f"), "Fit the active page to the viewport.", hasPage, "Select a page first.");
        Add("view.zoomIn", "Zoom In", "View", "Ctrl++", "Zoom into the active page.", hasPage, "Select a page first.");
        Add("view.zoomOut", "Zoom Out", "View", "Ctrl+-", "Zoom out of the active page.", hasPage, "Select a page first.");
        Add("view.addBookmark", "Add Bookmark", "View", KeyboardShortcutKeys.DualLayoutDisplay("bk"), "Name and save the active sheet and current zoom/pan as a bookmark.", hasPage, "Select a page first.");
        Add("view.toggleTheme", "Toggle Dark Theme", "View", "", "Switch between light and dark UI theme.");
        Add("view.viewportBackground", "Page Background", "View", "", "Open page background presets.");
        Add("view.toggleInbox", "Toggle AI Inbox", "View", "", "Collapse or expand the AI Inbox panel.");
        Add("view.mainView", "Show Main View", "Workspace", "", "Switch to the drawing canvas workspace.");
        Add("view.sheetManager", "Show Sheet Manager", "Workspace", "", "Switch to the sheet table / PDF metadata workspace.");
        Add("view.takeoffManager", "Show Takeoff Manager", "Workspace", "", "Switch to the takeoff item manager workspace.");
        Add("view.reportBuilder", "Show Report Builder", "Workspace", "", "Switch to the spreadsheet-style report builder workspace.");
        Add("view.quickCalc", "Quick Calculator", "View", "", "Toggle the slide-out calculator with feet-inches math.");
        Add("view.materialsManager", "Show Materials", "Workspace", "", "Switch to the material extraction workspace.");
        Add("view.aiManager", "Show AI Manager", "Workspace", "", "Switch to the AI inbox and marker manager workspace.");
        Add("view.3dManager", "Show 3D Viewer", "Workspace", "", "Switch to the clean 3D viewer workspace.");
        Add("view.takeoffsTab", "Show Takeoffs Tab", "View", "", "Select the Takeoffs workspace tab.", hasRightTabs, "Workspace tabs are not ready yet.");
        Add("view.estimatingTab", "Show Estimating Tab", "View", "", "Select the Estimating workspace tab.", hasRightTabs, "Workspace tabs are not ready yet.");
        Add("view.3dTab", "Show 3D Tab", "View", "", "Select the right-side 3D wall viewer tab.", hasRightTabs, "Workspace tabs are not ready yet.");

        Add("tool.select", "Select Tool", "Tools", Shortcut("e"), "Use box selection and measurement editing.");
        Add("tool.pan", "Pan Tool", "Tools", Shortcut("v"), "Use left-button panning; right-button pan still works.");
        Add("tool.scale", "Scale Tool", "Tools", Shortcut("s"), "Draw a calibration line for sheet scale.");
        Add("tool.ruler", "Ruler Tool", "Tools", Shortcut("r"), "Measure a temporary distance without creating a takeoff.");
        Add("tool.highlight", "Highlighter Tool", "Tools", Shortcut("h"), "Draw a transparent highlighter markup.");
        Add("tool.drawLine", "Draw Line / Extra Joists", "Tools", Shortcut("d"), "D toggles continuous Extra Joists for one selected Joist Area segment; otherwise it selects Draw Line.");
        Add("tool.drawBox", "Draw Box Tool", "Tools", "", "Draw a page annotation box.");
        Add("tool.note", "Note Tool", "Tools", Shortcut("n"), "Write a text note on the active sheet.", hasPage, "Select a page first.");
        Add("tool.count", "Count Tool", "Tools", Shortcut("p"), "Record count marks into a takeoff item.", hasJob && hasPage, "Open a job and select a page first.");
        Add("tool.similar", "Similar Count", "Tools", "", "Find look-alike symbols on the active sheet, name the result, and add reviewed markers as a Count item.", hasJob && hasPage, "Open a job and select a page first.");
        Add("tool.pointAlongLine", "Points Along Line(s)", "Tools", "", "Create Count points along the selected Line measurement(s) at a spacing.", hasJob && hasPage && selectedLineMeasurementCount > 0, "Select one or more Line measurements first.");
        Add("tool.line", "Line Tool", "Tools", Shortcut("l"), "Record line measurements into a takeoff item.", hasJob && hasPage, "Open a job and select a page first.");
        Add("tool.area", "Area Tool", "Tools", Shortcut("a"), "Record area measurements into a takeoff item.", hasJob && hasPage, "Open a job and select a page first.");
        Add("tool.joistArea", "J Area Tool", "Tools", Shortcut("j"), "Create a joist area takeoff and record area measurements.", hasJob && hasPage, "Open a job and select a page first.");
        Add("tool.beam", "Beam Tool", "Tools", Shortcut("b"), "Measure a beam length, create a Count item, and place the first Count mark.", hasJob && hasPage, "Open a job and select a page first.");
        Add("tool.openings", "Openings Tool", "Tools", Shortcut("o"), "Measure an opening box, create a size-only Count item, and place the mark in the center.", hasJob && hasPage, "Open a job and select a page first.");
        Add("tool.areaCut", "Area Cut Tool", "Tools", Shortcut("x"), "Cut a hole out of the selected area measurement.", hasPage, "Select a page first.");
        Add("tool.toggleRecord", "Toggle Record", "Tools", "Space", "Toggle digitizer record mode for the current drawing tool.", hasJob && hasPage, "Open a job and select a page first.");
        Add("tool.toggleSnap", "Toggle Snap", "Tools", "F3", "Toggle snap to existing takeoff points.");
        Add("tool.togglePdfSnap", "Toggle PDF Snap", "Tools", "Ctrl+F3", "Toggle snap to PDF vector corners and points.");
        Add("tool.toggleOrtho", "Toggle Ortho", "Tools", "F8", "Toggle horizontal/vertical Ortho; Shift temporarily forces it.");

        Add("edit.copyMeasurements", "Copy Selected Measurements", "Edit", "Ctrl+C", "Copy the selected measurements.", selectedMeasurementCount > 0, "Select one or more measurements first.");
        Add("edit.pasteMeasurements", "Paste Measurements", "Edit", "Ctrl+V", "Paste copied measurements to the active page.", _measurementClipboard != null && hasPage, "Copy measurements and select a page first.");
        Add("edit.mergeMeasurements", "Merge Selected Measurements", "Edit", "Ctrl+M", "Move selected measurement segments into another takeoff.", selectedMeasurementCount > 0, "Select one or more measurements first.");
        Add("edit.splitMeasurements", "Split Selected Measurements", "Edit", "Ctrl+Shift+M", "Move selected measurements or Count marks into a new takeoff.", selectedMeasurementCount > 0, "Select one or more measurements first.");
        Add("edit.combineUnion", "Combine Areas: Union", "Edit", "", "Merge the selected Areas into one.", selectedAreaMeasurementCount > 1, "Select two or more Area measurements first.");
        Add("edit.combineSubtract", "Combine Areas: Subtract", "Edit", "", "Subtract the later-selected Areas from the first-selected one.", selectedAreaMeasurementCount > 1, "Select two or more Area measurements first.");
        Add("edit.combineIntersect", "Combine Areas: Intersect", "Edit", "", "Keep only the region shared by all selected Areas.", selectedAreaMeasurementCount > 1, "Select two or more Area measurements first.");
        Add("edit.combineRemoveOverlap", "Combine Areas: Remove Overlap", "Edit", "", "Trim overlaps between selected Areas so nothing is counted twice; the first-selected Area wins.", selectedAreaMeasurementCount > 1, "Select two or more Area measurements first.");
        Add("edit.combineDivide", "Combine Areas: Divide", "Edit", "", "Split selected Areas into exclusive parts plus a separate overlap area.", selectedAreaMeasurementCount > 1, "Select two or more Area measurements first.");

        Add("pages.sortAz", "Sort Pages A-Z", "Pages", "", "Sort children of the selected Pages folder (or root) alphabetically.", hasJob, "Open or create a job first.");
        Add("pages.sortZa", "Sort Pages Z-A", "Pages", "", "Sort children of the selected Pages folder (or root) in reverse alphabetical order.", hasJob, "Open or create a job first.");
        Add("pages.sortArchStruct", "Sort Pages A/S", "Pages", "", "Move A sheets to Arch, S sheets to Struct, and trailing '-' sheets to others.", hasJob, "Open or create a job first.");
        Add("pages.blankSheet", "Blank Sheet", "Pages", "", "Create an empty sheet in the current job.", hasJob, "Open or create a job first.");
        Add("pages.sortSuffix", "Sort Pages D/Sec/WT", "Pages", "", "Move suffix sheets into details/sections/units and reorder v/wt/ft/sv/sw at Pages root.", hasJob, "Open or create a job first.");
        Add("pages.repairLinks", "Repair Measurement Links", "Pages", "", "Reconnect stale measurement page links after page renames/imports.", hasJob, "Open or create a job first.");
        Add("pages.autoFolders", "Auto Page Folders", "Pages", "", "Create the standard page folder tree.", hasJob, "Open or create a job first.");
        Add("pages.autoName", "Auto Name PDF", "Pages", "", "Preview and apply PDF sheet names.", hasJob, "Open or create a job first.");
        Add("pages.autoScale", "Auto Scale PDF", "Pages", "", "Preview and apply PDF sheet scales.", hasJob, "Open or create a job first.");
        Add("pages.autoNameScale", "Auto Name + Scale PDF", "Pages", "", "Preview and apply PDF sheet names and scales.", hasJob, "Open or create a job first.");
        Add("pages.setScale", "Set Scale", "Pages", "F4", "Set one scale on the selected sheets, or on the active sheet when there is no Pages selection.", hasJob && hasPage, "Open a job and select a sheet first.");
        Add("pages.nameScaleSetup", "Name / Scale Setup", "Pages", "F5", "Open the manual Name / Scale window for the active sheet.", hasJob && hasPage, "Open a job and select a sheet first.");
        Add("pages.aiFillMetadata", "AI Fill PDF Metadata", "Pages", "", "Queue GPT fallback for missing sheet metadata.", hasJob, "Open or create a job first.");
        Add("sheet.overlay", "Overlay", "Sheets", "", "Choose a matching sheet overlay for the active sheet.", hasJob && hasPage, "Open a job and select a sheet first.");
        Add("sheet.overlayPoints", "Move Overlay by Points", "Sheets", "", "Align the active sheet overlay by matching points.", hasPage && currentPageHasSheetOverlay, "Set an overlay on the active sheet first.");
        Add("materials.extract", "Extract Materials", "Materials", "", "Extract material evidence and schedules from the current job PDFs.", hasJob, "Open or create a job first.");
        Add("layers.allOn", "PDF Layers All On", "PDF Layers", "", "Turn all active page PDF layers on.", BtnLayersOn.IsEnabled, "Select a PDF page with layers first.");
        Add("layers.allOff", "PDF Layers All Off", "PDF Layers", "", "Turn all active page PDF layers off.", BtnLayersOff.IsEnabled, "Select a PDF page with layers first.");
        Add("layers.clearHighlight", "Clear PDF Layer Highlights", "PDF Layers", "", "Clear highlighted PDF layers.", BtnLayersClearHi.IsEnabled, "Select a PDF page with layers first.");

        Add("takeoffs.newFolder", "New Takeoff Folder", "Takeoffs", "", "Create a takeoff folder under the selected/root folder.", hasJob, "Open or create a job first.");
        Add("takeoffs.newItem", "New Takeoff Item", "Takeoffs", Shortcut("t"), "Create a takeoff item under the selected/root folder.", hasJob, "Open or create a job first.");
        Add("takeoffs.refresh", "Refresh Takeoffs Tree", "Takeoffs", "", "Reload the Takeoffs tree from the current job folder.", hasJob, "Open or create a job first.");
        Add("takeoffs.activeFind", "Show Active Takeoff", "Takeoffs", "", "Scroll the Takeoffs tree to the active target item.", _activeItem != null, "Select a takeoff item first.");
        Add("takeoffs.activeProperties", "Active Takeoff Properties", "Takeoffs", "", "Edit properties for the active target item.", _activeItem != null, "Select a takeoff item first.");
        Add("takeoffs.activeRecord", "Record Active Takeoff", "Takeoffs", "Space", "Start or stop recording into the active target item.", _activeItem != null && hasPage, "Select a takeoff item and sheet first.");
        Add("takeoffs.activePrevious", "Previous Takeoff Target", "Takeoffs", "", "Switch the active target to the previous takeoff item.", _takeoffItems.Count > 1, "Create at least two takeoff items first.");
        Add("takeoffs.activeNext", "Next Takeoff Target", "Takeoffs", "", "Switch the active target to the next takeoff item.", _takeoffItems.Count > 1, "Create at least two takeoff items first.");
        Add("takeoffs.activeSheetNext", "Next Sheet Takeoff Target", "Takeoffs", "", "Switch to the next takeoff item measured on the active sheet.", hasSheetTakeoffTargets, "Select a sheet with measured takeoffs first.");
        Add("takeoffs.sortAz", "Sort Takeoffs A-Z", "Takeoffs", "", "Sort children of the selected Takeoffs folder (or root) alphabetically.", hasJob, "Open or create a job first.");
        Add("takeoffs.sortZa", "Sort Takeoffs Z-A", "Takeoffs", "", "Sort children of the selected Takeoffs folder (or root) in reverse alphabetical order.", hasJob, "Open or create a job first.");
        Add("takeoffs.sortWalls", "Sort Takeoffs: Walls", "Takeoffs", "", "Sort wall takeoffs in the selected folder and subfolders: ext, corr, dem, then 2x6/2x4 sizes.", hasJob, "Open or create a job first.");
        Add("takeoffs.sortDetails", "Sort Takeoffs: Details by Sheet", "Takeoffs", "", "Group detail takeoffs like 1/A501 by the sheet after the slash, ordered as in the Pages tree.", hasJob, "Open or create a job first.");
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

        return items
            .Where(item => IsCommandAvailableForModules(item.Id))
            .ToList();
    }

    private void ExecuteCommandPaletteItem(string id)
    {
        if (TryGetCommandModule(id, out ModuleId requiredModule) &&
            !RequireModule(requiredModule, $"Command '{id}'"))
        {
            return;
        }
        if (id == "pages.aiFillMetadata" &&
            !RequireModule(ModuleId.SheetManager, "AI Fill PDF Metadata"))
        {
            return;
        }

        switch (id)
        {
            case "file.open": BtnOpen_Click(this, new RoutedEventArgs()); break;
            case "file.openRecent": ShowRecentJobPicker(); break;
            case "file.openJobsFolder": BtnOpenJobsFolder_Click(this, new RoutedEventArgs()); break;
            case "file.newJob": BtnNewJob_Click(this, new RoutedEventArgs()); break;
            case "file.blankJob": BtnBlankJob_Click(this, new RoutedEventArgs()); break;
            case "file.sampleJob": CreateSampleJob(); break;
            case "file.importPlanSwift": BtnImportPlanSwiftJob_Click(this, new RoutedEventArgs()); break;
            case "file.importPlanSwiftCurrent": BtnImportPlanSwiftToCurrentJob_Click(this, new RoutedEventArgs()); break;
            case "file.importPdf": BtnImport_Click(this, new RoutedEventArgs()); break;
            case "file.importPdfFolder": BtnImportPdfFolder_Click(this, new RoutedEventArgs()); break;
            case "file.importPdfTakeoffs": BtnImportPdfTakeoffs_Click(this, new RoutedEventArgs()); break;
            case "file.exportPdf": BtnExportPdf_Click(this, new RoutedEventArgs()); break;
            case "file.save": BtnSave_Click(this, new RoutedEventArgs()); break;
            case "file.exportCsv": BtnExportCsv_Click(this, new RoutedEventArgs()); break;
            case "file.exportTxt": BtnExportTxt_Click(this, new RoutedEventArgs()); break;
            case "file.exportExcel": BtnExportExcel_Click(this, new RoutedEventArgs()); break;
            case "file.exportCurrentExcel": BtnExportCurrentExcel_Click(this, new RoutedEventArgs()); break;
            case "file.excelMacroSqft": BtnExcelMacroSqft_Click(this, new RoutedEventArgs()); break;
            case "file.excelMacroWalls": BtnExcelMacroWalls_Click(this, new RoutedEventArgs()); break;
            case "file.excelMacroOpenings": BtnExcelMacroOpenings_Click(this, new RoutedEventArgs()); break;

            case "view.fit": BtnFit_Click(this, new RoutedEventArgs()); break;
            case "view.zoomIn": BtnZoomIn_Click(this, new RoutedEventArgs()); break;
            case "view.zoomOut": BtnZoomOut_Click(this, new RoutedEventArgs()); break;
            case "view.addBookmark": AddBookmarkFromShortcut(); break;
            case "view.toggleTheme":
                ApplyTheme(!string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase), persist: true);
                break;
            case "view.viewportBackground":
                TopMainTabs.SelectedIndex = 1;
                ComboDisplayPageBackground.Focus();
                ComboDisplayPageBackground.IsDropDownOpen = true;
                break;
            case "view.toggleInbox": BtnToggleInbox_Click(BtnToggleInbox, new RoutedEventArgs()); break;
            case "view.mainView": SelectWorkspaceTab("MainView"); break;
            case "view.sheetManager": SelectWorkspaceTab("SheetManager"); break;
            case "view.takeoffManager": SelectWorkspaceTab("TakeoffManager"); break;
            case "view.reportBuilder": SelectWorkspaceTab("ReportBuilder"); break;
            case "view.quickCalc": ToggleQuickCalcPanel(); break;
            case "view.materialsManager": SelectWorkspaceTab("MaterialsManager"); break;
            case "view.aiManager": SelectWorkspaceTab("AiManager"); break;
            case "view.3dManager": SelectWorkspaceTab("3DManager"); break;
            case "view.takeoffsTab": SelectRightWorkspaceTab("Takeoffs"); break;
            case "view.estimatingTab": SelectRightWorkspaceTab("Estimating"); break;
            case "view.3dTab": SelectRightWorkspaceTab("3D"); break;

            case "tool.select": SetTool("select"); break;
            case "tool.pan": SetTool("pan"); break;
            case "tool.scale": SetTool("scale"); break;
            case "tool.ruler": SetTool("ruler"); break;
            case "tool.highlight": SetTool("drawhighlight"); break;
            case "tool.drawLine": SetTool("drawline"); break;
            case "tool.drawBox": SetTool("drawrect"); break;
            case "tool.note": SetTool("note"); break;
            case "tool.count": SetTool("point", forceNewTakeoff: true); break;
            case "tool.similar": BtnSimilarCount_Click(this, new RoutedEventArgs()); break;
            case "tool.pointAlongLine": CreatePointsAlongSelectedLines(); break;
            case "tool.line": SetTool("line", forceNewTakeoff: true); break;
            case "tool.area": SetTool("area", forceNewTakeoff: true); break;
            case "tool.joistArea": SetTool("joistarea", forceNewTakeoff: true); break;
            case "tool.beam": SetTool("beam"); break;
            case "tool.openings": SetTool("openings"); break;
            case "tool.areaCut": SetTool("areacut"); break;
            case "tool.toggleRecord":
                if (_recordButton != null)
                    _recordButton.IsChecked = _recordButton.IsChecked != true;
                break;
            case "tool.toggleSnap": SetSnapMode(!_viewport.SnapEnabled); break;
            case "tool.togglePdfSnap": SetPdfSnapMode(!_viewport.PdfSnapEnabled); break;
            case "tool.toggleOrtho": SetOrthoMode(!_viewport.OrthoEnabled); break;

            case "edit.copyMeasurements": CopyMeasurementsToClipboard(_viewport.GetSelectedMeasurements()); break;
            case "edit.pasteMeasurements": PasteMeasurementsFromClipboard(); break;
            case "edit.mergeMeasurements": MergeSelectedMeasurementsToPromptedTakeoff(); break;
            case "edit.splitMeasurements": SplitSelectedMeasurementsToNewTakeoff(); break;
            case "edit.combineUnion": _viewport.CombineSelectedAreas(Controls.AreaCombineMode.Union); break;
            case "edit.combineSubtract": _viewport.CombineSelectedAreas(Controls.AreaCombineMode.Subtract); break;
            case "edit.combineIntersect": _viewport.CombineSelectedAreas(Controls.AreaCombineMode.Intersect); break;
            case "edit.combineRemoveOverlap": _viewport.CombineSelectedAreas(Controls.AreaCombineMode.RemoveOverlap); break;
            case "edit.combineDivide": _viewport.CombineSelectedAreas(Controls.AreaCombineMode.Divide); break;

            case "pages.sortAz": SortPagesAlphabetically(descending: false); break;
            case "pages.sortZa": SortPagesAlphabetically(descending: true); break;
            case "pages.sortArchStruct": BtnSortPagesArchStruct_Click(this, new RoutedEventArgs()); break;
            case "pages.blankSheet": BtnNewBlankPage_Click(this, new RoutedEventArgs()); break;
            case "pages.sortSuffix": BtnSortPagesSuffix_Click(this, new RoutedEventArgs()); break;
            case "pages.repairLinks": BtnRepairMeasurementPageLinks_Click(this, new RoutedEventArgs()); break;
            case "pages.autoFolders": BtnAutoPageFolders_Click(this, new RoutedEventArgs()); break;
            case "pages.autoName": BtnAutoRenamePdf_Click(this, new RoutedEventArgs()); break;
            case "pages.autoScale": BtnAutoScalePdf_Click(this, new RoutedEventArgs()); break;
            case "pages.autoNameScale": BtnAutoRenameScalePdf_Click(this, new RoutedEventArgs()); break;
            case "pages.setScale": SetSelectedPagesScaleFromShortcut(); break;
            case "pages.nameScaleSetup": BtnFloatingPageSetup_Click(this, new RoutedEventArgs()); break;
            case "pages.aiFillMetadata": BtnQueuePdfMetadataFallback_Click(this, new RoutedEventArgs()); break;
            case "sheet.overlay": ChooseCurrentSheetOverlayCandidate(); break;
            case "sheet.overlayPoints": BeginCurrentSheetOverlayPointEdit(); break;
            case "materials.extract":
                SelectWorkspaceTab("MaterialsManager");
                BtnMaterialsExtract_Click(this, new RoutedEventArgs());
                break;
            case "layers.allOn": BtnLayersOn_Click(this, new RoutedEventArgs()); break;
            case "layers.allOff": BtnLayersOff_Click(this, new RoutedEventArgs()); break;
            case "layers.clearHighlight": BtnLayersClearHi_Click(this, new RoutedEventArgs()); break;

            case "takeoffs.newFolder": BtnNewTakeoffFolder_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.newItem": BtnNewItem_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.refresh": BtnRefreshTakeoffsTree_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.activeFind": BtnActiveTakeoffFind_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.activeProperties": BtnActiveTakeoffProperties_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.activeRecord": BtnActiveTakeoffRecord_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.activePrevious": BtnActiveTakeoffPrevious_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.activeNext": BtnActiveTakeoffNext_Click(this, new RoutedEventArgs()); break;
            case "takeoffs.activeSheetNext": MoveActiveSheetTakeoffTarget(1); break;
            case "takeoffs.sortAz": SortTakeoffsAlphabetically(descending: false); break;
            case "takeoffs.sortZa": SortTakeoffsAlphabetically(descending: true); break;
            case "takeoffs.sortWalls": SortTakeoffsWalls(); break;
            case "takeoffs.sortDetails": SortTakeoffsDetails(); break;
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
        if (TryGetWorkspaceModule(key, out ModuleId module) && !RequireModule(module, $"Open {key}"))
            return;

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
