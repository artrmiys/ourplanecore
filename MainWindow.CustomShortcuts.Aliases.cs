namespace OurPlanCore;

public partial class MainWindow
{
    // Visible controls that call an existing keyboard command share its assignment and defaults.
    private static readonly IReadOnlyDictionary<string, string> KeyboardControlCommandAliases = new Dictionary<string, string>
    {
        ["BtnOpen"] = "file.open", ["BtnSave"] = "file.save", ["BtnFit"] = "view.fit",
        ["BtnZoomIn"] = "view.zoomIn", ["BtnZoomOut"] = "view.zoomOut",
        ["BtnPan"] = "tool.pan", ["BtnSelect"] = "tool.select", ["BtnScale"] = "tool.scale",
        ["BtnRuler"] = "tool.ruler", ["BtnPitch"] = "tool.pitch", ["BtnPoint"] = "tool.count",
        ["BtnLine"] = "tool.line", ["BtnArea"] = "tool.area", ["BtnJoistArea"] = "tool.joistArea",
        ["BtnBeam"] = "tool.beam", ["BtnOpenings"] = "tool.openings", ["BtnAreaCut"] = "tool.areaCut",
        ["BtnHighlight"] = "tool.highlight", ["BtnDrawLine"] = "tool.drawLine", ["BtnDrawRect"] = "tool.drawBox", ["BtnNote"] = "tool.note",
        ["BtnMirrorHorizontal"] = "edit.mirrorHorizontal", ["BtnMirrorVertical"] = "edit.mirrorVertical",
        ["BtnSnap"] = "tool.toggleSnap", ["BtnPdfSnap"] = "tool.togglePdfSnap",
        ["BtnOrtho"] = "tool.toggleOrtho", ["BtnBoxMode"] = "tool.toggleBox",
        ["BtnActiveTakeoffRecord"] = "tool.toggleRecord", ["BtnNewItem"] = "takeoffs.newItem",
        ["BtnNewTakeoffFolder"] = "takeoffs.newFolder", ["BtnRefreshTakeoffsTree"] = "takeoffs.refresh",
        ["BtnActiveTakeoffFind"] = "takeoffs.activeFind", ["BtnActiveTakeoffProperties"] = "takeoffs.activeProperties",
        ["BtnActiveTakeoffPrevious"] = "takeoffs.activePrevious", ["BtnActiveTakeoffNext"] = "takeoffs.activeNext",
        ["BtnOpenJobsFolder"] = "file.openJobsFolder", ["BtnNewJob"] = "file.newJob", ["BtnBlankJob"] = "file.blankJob",
        ["BtnImport"] = "file.importPdf", ["BtnImportPdfFolder"] = "file.importPdfFolder", ["BtnImportPdfTakeoffs"] = "file.importPdfTakeoffs",
        ["BtnExportPdf"] = "file.exportPdf", ["BtnExportCsv"] = "file.exportCsv", ["BtnExportTxt"] = "file.exportTxt",
        ["BtnExportExcel"] = "file.exportExcel", ["BtnExportCurrentExcel"] = "file.exportCurrentExcel",
        ["BtnSortPagesArchStruct"] = "pages.sortArchStruct", ["BtnSortPagesSuffix"] = "pages.sortSuffix",
        ["BtnAutoPageFolders"] = "pages.autoFolders", ["BtnAutoRenamePdf"] = "pages.autoName", ["BtnAutoScalePdf"] = "pages.autoScale",
        ["BtnAutoRenameScalePdf"] = "pages.autoNameScale", ["BtnFloatingPageSetup"] = "pages.nameScaleSetup",
        ["BtnAutoTakeoffTree"] = "takeoffs.autoTree", ["BtnAutoTakeoffFromPages"] = "takeoffs.fromPages",
        ["BtnSimilarCount"] = "tool.similar", ["BtnToggleInbox"] = "view.toggleInbox",
        ["BtnLayersOn"] = "layers.allOn", ["BtnLayersOff"] = "layers.allOff", ["BtnLayersClearHi"] = "layers.clearHighlight",
        ["BtnOpenAiSettings"] = "ai.settings", ["BtnAddObservation"] = "ai.addObservation", ["BtnRunAi"] = "ai.runSelected",
    };
}
