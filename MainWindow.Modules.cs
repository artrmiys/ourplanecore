using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private ModuleFeatureConfig _moduleFeatures = ModuleFeatureStore.Resolve(null);
    private bool _moduleUiApplied;
    private bool _lastAiModuleEnabled = true;
    private bool _lastThreeDModuleEnabled = true;
    private bool _lastSheetOverlayModuleEnabled = true;

    private bool IsModuleEnabled(ModuleId module) => _moduleFeatures.IsEnabled(module);

    private static bool TryGetWorkspaceModule(string? tag, out ModuleId module)
    {
        module = tag switch
        {
            "SheetManager" => ModuleId.SheetManager,
            "TakeoffManager" => ModuleId.TakeoffManager,
            "ReportBuilder" => ModuleId.ReportBuilder,
            "MaterialsManager" => ModuleId.Materials,
            "AiManager" => ModuleId.Ai,
            "3DManager" => ModuleId.ThreeD,
            _ => default,
        };
        return tag is "SheetManager" or "TakeoffManager" or "ReportBuilder" or
            "MaterialsManager" or "AiManager" or "3DManager";
    }

    private static bool TryGetCommandModule(string id, out ModuleId module)
    {
        module = id switch
        {
            "view.addBookmark" => ModuleId.Bookmarks,
            "view.toggleInbox" or "view.aiManager" or "pages.aiFillMetadata" => ModuleId.Ai,
            "view.sheetManager" or "pages.autoName" or "pages.autoScale" or "pages.autoNameScale" => ModuleId.SheetManager,
            "view.takeoffManager" => ModuleId.TakeoffManager,
            "view.reportBuilder" => ModuleId.ReportBuilder,
            "view.materialsManager" or "materials.extract" => ModuleId.Materials,
            "view.3dManager" or "view.3dTab" or "takeoffs.roofBase" => ModuleId.ThreeD,
            "view.estimatingTab" => ModuleId.Estimating,
            "view.quickCalc" => ModuleId.QuickCalculator,
            "pages.detach" or "pages.tileM2" => ModuleId.DetachedSheets,
            "pages.nameScaleSetup" => ModuleId.SheetManager,
            "sheet.overlay" or "sheet.overlayPoints" => ModuleId.SheetOverlay,
            "layers.allOn" or "layers.allOff" or "layers.clearHighlight" => ModuleId.PdfLayers,
            "tool.ruler" or "tool.highlight" or "tool.drawLine" or "tool.drawBox" or "tool.note" => ModuleId.Annotations,
            "tool.similar" or "tool.pointAlongLine" or "tool.joistArea" or "tool.beam" or
                "tool.openings" or "tool.areaCut" or "edit.mergeMeasurements" or
                "edit.splitMeasurements" or "edit.combineUnion" or "edit.combineSubtract" or
                "edit.combineIntersect" or "edit.combineRemoveOverlap" or "edit.combineDivide" => ModuleId.AdvancedTakeoffTools,
            "pages.autoFolders" or "takeoffs.autoTree" or "takeoffs.fromPages" => ModuleId.TakeoffAutomation,
            "file.exportExcel" or "file.exportCurrentExcel" => ModuleId.ExcelIntegration,
            "file.exportPdf" => ModuleId.PdfOutput,
            _ when id.StartsWith("ai.", StringComparison.OrdinalIgnoreCase) => ModuleId.Ai,
            _ => default,
        };

        return id.StartsWith("ai.", StringComparison.OrdinalIgnoreCase) || id is
            "view.addBookmark" or "view.toggleInbox" or "view.aiManager" or "pages.aiFillMetadata" or
            "view.sheetManager" or "pages.autoName" or "pages.autoScale" or "pages.autoNameScale" or
            "view.takeoffManager" or "view.reportBuilder" or "view.materialsManager" or "materials.extract" or
            "view.3dManager" or "view.3dTab" or "takeoffs.roofBase" or "view.estimatingTab" or
            "view.quickCalc" or "sheet.overlay" or "sheet.overlayPoints" or
            "pages.detach" or "pages.tileM2" or "pages.nameScaleSetup" or
            "layers.allOn" or "layers.allOff" or "layers.clearHighlight" or
            "tool.ruler" or "tool.highlight" or "tool.drawLine" or "tool.drawBox" or "tool.note" or
            "tool.similar" or "tool.pointAlongLine" or "tool.joistArea" or "tool.beam" or
            "tool.openings" or "tool.areaCut" or "edit.mergeMeasurements" or
            "edit.splitMeasurements" or "edit.combineUnion" or "edit.combineSubtract" or
            "edit.combineIntersect" or "edit.combineRemoveOverlap" or "edit.combineDivide" or
            "pages.autoFolders" or "takeoffs.autoTree" or "takeoffs.fromPages" or
            "file.exportExcel" or "file.exportCurrentExcel" or "file.exportPdf";
    }

    private bool RequireModule(ModuleId module, string operation)
    {
        if (IsModuleEnabled(module))
            return true;

        string name = ModuleFeatureCatalog.Definition(module).Name;
        TxtStatus.Text = $"{operation}: module '{name}' is disabled in Settings > Modules.";
        return false;
    }

    private bool IsCommandAvailableForModules(string id)
    {
        if (TryGetCommandModule(id, out ModuleId module) && !IsModuleEnabled(module))
            return false;

        return id != "pages.aiFillMetadata" || IsModuleEnabled(ModuleId.SheetManager);
    }

    private void ReloadModuleFeatures(bool announce = false)
    {
        _moduleFeatures = ModuleFeatureStore.Resolve(_currentJob);
        ApplyModuleAvailability(announce);
        if (_moduleSettingsSourceText != null)
            BindModulesSettings();
    }

    private void ApplyModuleAvailability(bool announce = false)
    {
        bool aiEnabled = IsModuleEnabled(ModuleId.Ai);
        bool threeDEnabled = IsModuleEnabled(ModuleId.ThreeD);

        EnsureVisibleWorkspaceFallback();
        ApplyNamedModuleShell(aiEnabled, threeDEnabled);

        SetVisible(ChkDisplayPdfLayers, IsModuleEnabled(ModuleId.PdfLayers));
        PdfLayerRenderService.PdfLayersEnabled =
            IsModuleEnabled(ModuleId.PdfLayers) && _settings.PdfLayersEnabled;
        if (!IsModuleEnabled(ModuleId.PdfLayers))
        {
            _viewport.SetPdfLayerTraceEnabled(false);
            ViewportLayerTraceBar.Visibility = Visibility.Collapsed;
        }
        else if (_moduleUiApplied)
        {
            OnLayersChanged(_viewport.CurrentPdfLayers);
            _viewport.InvalidateVisual();
        }
        if (!IsModuleEnabled(ModuleId.SheetOverlay))
        {
            _sheetOverlayLoadVersion++;
            _viewport.ClearSheetOverlay();
        }
        else if (_moduleUiApplied && _currentPage != null)
        {
            QueueSheetOverlayLoadForPageOpen(_currentPage);
        }
        if (_moduleUiApplied && _lastSheetOverlayModuleEnabled != IsModuleEnabled(ModuleId.SheetOverlay) &&
            _currentJob != null)
        {
            RefreshPagesTakeoffIndicators();
        }

        if (!IsModuleEnabled(ModuleId.SheetManager))
        {
            CancelActiveSheetManagerWorkForModuleDisable();
            _pageSetupWindow?.Close();
        }

        ApplyAiModuleShell(aiEnabled);
        ApplyToolModuleShell();
        ApplyDynamicModuleTabs();
        ApplyQuickCalculatorModule();
        ApplyOutputSettingsModuleAvailability();
        ApplyBookmarksModuleAvailability();
        ApplyTemplatesModuleAvailability();

        if (!IsModuleEnabled(ModuleId.DetachedSheets))
            CloseDetachedSheetsForModuleDisable();
        if (!IsModuleEnabled(ModuleId.Materials))
            CancelActiveMaterialsWorkForModuleDisable();

        if (_moduleUiApplied && _lastAiModuleEnabled && !aiEnabled)
            CancelActiveAiWorkForModuleDisable();
        if (!aiEnabled)
        {
            _viewport.ClearAiActionDraftPreview();
            _viewport.ClearAiMarkers();
        }
        else if (_moduleUiApplied && !_lastAiModuleEnabled)
        {
            LoadPersistedMarkerVisibility();
            LoadObservationsInbox();
            RefreshAiMarkersOverlay();
        }

        if (!threeDEnabled)
            DeactivateThreeDForModuleDisable();
        else if (_moduleUiApplied && !_lastThreeDModuleEnabled)
            ReactivateThreeDForModuleEnable();

        EnsureVisibleWorkspaceFallback();
        BuildSideCommandStrips();
        _lastAiModuleEnabled = aiEnabled;
        _lastThreeDModuleEnabled = threeDEnabled;
        _lastSheetOverlayModuleEnabled = IsModuleEnabled(ModuleId.SheetOverlay);
        _moduleUiApplied = true;

        if (announce)
            TxtStatus.Text = "Module settings applied.";
    }

    private void ApplyNamedModuleShell(bool aiEnabled, bool threeDEnabled)
    {
        SetVisible(AnnotationCommandTab, IsModuleEnabled(ModuleId.Annotations));
        SetVisible(PdfLayersSideTab, IsModuleEnabled(ModuleId.PdfLayers));
        SetVisible(SheetManagerWorkspaceTab, IsModuleEnabled(ModuleId.SheetManager));
        SetVisible(TakeoffManagerWorkspaceTab, IsModuleEnabled(ModuleId.TakeoffManager));
        SetVisible(ReportBuilderWorkspaceTab, IsModuleEnabled(ModuleId.ReportBuilder));
        SetVisible(MaterialsWorkspaceTab, IsModuleEnabled(ModuleId.Materials));
        SetVisible(AiManagerWorkspaceTab, aiEnabled);
        SetVisible(ThreeDManagerWorkspaceTab, threeDEnabled);

        bool sheetManager = IsModuleEnabled(ModuleId.SheetManager);
        SetVisible(BtnMainAutoRenamePdf, sheetManager);
        SetVisible(BtnMainAutoScalePdf, sheetManager);
        SetVisible(BtnMainAutoRenameScalePdf, sheetManager);
        SetVisible(BtnMainAiFill, aiEnabled && sheetManager);
        SetVisible(BtnMainAiCropHints, aiEnabled && sheetManager);
        SetVisible(BtnSheetManagerAiFill, aiEnabled);
        SetVisible(BtnSheetManagerAiCropHints, aiEnabled);
        SetVisible(BtnOpenAiSettings, aiEnabled);
        SetVisible(BtnTakeoffManagerOpenEstimating, IsModuleEnabled(ModuleId.Estimating));
        SetVisible(BtnExportCurrentExcelCell, IsModuleEnabled(ModuleId.ExcelIntegration));
        SetVisible(BtnTakeoffManagerExportExcel, IsModuleEnabled(ModuleId.ExcelIntegration));
        SetVisible(BtnTakeoffManagerCurrentExcel, IsModuleEnabled(ModuleId.ExcelIntegration));

        bool detachedSheets = IsModuleEnabled(ModuleId.DetachedSheets);
        SetVisible(BtnPagesTileSecondMonitor, detachedSheets);
        SetVisible(BtnPagesTileSecondMonitorVertical, detachedSheets);
        SetVisible(BtnSheetManagerDetach, detachedSheets);
        SetVisible(BtnSheetManagerTileM2, detachedSheets);
        SetVisible(BtnSheetManagerAutoFolders, IsModuleEnabled(ModuleId.TakeoffAutomation));
        SetVisible(BtnTakeoffManagerAutoTree, IsModuleEnabled(ModuleId.TakeoffAutomation));
        SetVisible(BtnTakeoffManagerFromPages, IsModuleEnabled(ModuleId.TakeoffAutomation));

        Visibility estimatingVisibility = IsModuleEnabled(ModuleId.Estimating)
            ? Visibility.Visible
            : Visibility.Collapsed;
        TakeoffManagerPriceColumn.Visibility = estimatingVisibility;
        TakeoffManagerCostColumn.Visibility = estimatingVisibility;
        SetVisible(BtnMainExportPdf, IsModuleEnabled(ModuleId.PdfOutput));
        SetVisible(BtnSheetManagerExportPdf, IsModuleEnabled(ModuleId.PdfOutput));
    }

    private void ApplyAiModuleShell(bool enabled)
    {
        InboxPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        InboxSplitter.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        InboxRow.MinHeight = enabled ? 30 : 0;
        InboxRow.Height = enabled
            ? new GridLength(_inboxExpanded ? Math.Max(30, _inboxExpandedHeight) : 30)
            : new GridLength(0);
        InboxSplitterRow.Height = enabled && _inboxExpanded
            ? new GridLength(4)
            : new GridLength(0);
        if (!enabled)
        {
            ObservationsListView.Items.Clear();
            InboxBadge.Visibility = Visibility.Collapsed;
            TxtInboxCount.Text = "0";
        }
    }

    private void ApplyToolModuleShell()
    {
        bool annotations = IsModuleEnabled(ModuleId.Annotations);
        bool advanced = IsModuleEnabled(ModuleId.AdvancedTakeoffTools);
        _viewport.SetAnnotationsModuleEnabled(annotations);
        SetVisible(BtnRuler, annotations);
        SetVisible(_chkOutputPdfMarkups, annotations);
        SetVisible(BtnSimilarCount, advanced);
        SetVisible(BtnPointAlongLine, advanced);
        SetVisible(BtnJoistArea, advanced);
        SetVisible(BtnBeam, advanced);
        SetVisible(BtnOpenings, advanced);
        SetVisible(BtnAreaCut, advanced);
        SetVisible(BtnMergeSelectedMeasurements, advanced);
        SetVisible(BtnSplitSelectedMeasurements, advanced);
        SetVisible(BtnCombineAreas, advanced);
        if (!IsToolAllowedByModules(_activeTool))
            ApplyToolSelection("select");
    }

    private bool IsToolAllowedByModules(string tool)
    {
        if (tool is "ruler" or "drawhighlight" or "drawline" or "drawarrow" or
            "drawrect" or "drawcloud" or "drawarea" or "note")
        {
            return IsModuleEnabled(ModuleId.Annotations);
        }

        if (tool is "joistarea" or "beam" or "openings" or "areacut")
            return IsModuleEnabled(ModuleId.AdvancedTakeoffTools);

        return true;
    }

    private void ApplyDynamicModuleTabs()
    {
        SetVisible(_estimateTab, IsModuleEnabled(ModuleId.Estimating));
        SetVisible(_threeDTab, IsModuleEnabled(ModuleId.ThreeD));
        SetVisible(_templatesTab, IsModuleEnabled(ModuleId.TakeoffTemplates));

        if (!IsModuleEnabled(ModuleId.Estimating) && _estimatingWindow != null)
            _estimatingWindow.Close();
    }

    private void ApplyQuickCalculatorModule()
    {
        if (!IsModuleEnabled(ModuleId.QuickCalculator))
            QuickCalcHost.Visibility = Visibility.Collapsed;
    }

    private void EnsureVisibleWorkspaceFallback()
    {
        if (WorkspaceTabs.SelectedItem is TabItem workspace && workspace.Visibility != Visibility.Visible)
            WorkspaceTabs.SelectedItem = MainViewWorkspaceTab;

        if (PagesSideTabs.SelectedItem is TabItem pageTab && pageTab.Visibility != Visibility.Visible)
            PagesSideTabs.SelectedIndex = 0;

        if (_rightWorkspaceTabs?.SelectedItem is TabItem rightTab && rightTab.Visibility != Visibility.Visible)
            _rightWorkspaceTabs.SelectedIndex = 0;
    }

    private static void SetVisible(FrameworkElement? element, bool visible)
    {
        if (element != null)
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyModuleAvailabilityToMenu(ItemsControl menu)
    {
        foreach (object entry in menu.Items)
        {
            if (entry is not MenuItem item)
                continue;

            string header = item.Header?.ToString() ?? "";
            ModuleId? module = header switch
            {
                "Roof Base from area" => ModuleId.ThreeD,
                "Auto Tree" or "From Pages" or "Auto Folders" => ModuleId.TakeoffAutomation,
                "Auto Create Folders" or "Auto Create Tree" or "Create Folders From Pages" => ModuleId.TakeoffAutomation,
                "Name / Scale…" => ModuleId.SheetManager,
                "PDF Metadata" or "Learning" => ModuleId.SheetManager,
                "Sheet Overlay" => ModuleId.SheetOverlay,
                "Detach to windows" or "Tile M2" => ModuleId.DetachedSheets,
                "Export Excel" or "Export to Current Excel" or "Current Excel" => ModuleId.ExcelIntegration,
                "Open Estimating" => ModuleId.Estimating,
                _ => null,
            };
            if (module.HasValue)
                item.Visibility = IsModuleEnabled(module.Value) ? Visibility.Visible : Visibility.Collapsed;
            if (item.Items.Count > 0)
                ApplyModuleAvailabilityToMenu(item);
        }

        CollapseRedundantMenuSeparators(menu);
    }

    private static void CollapseRedundantMenuSeparators(ItemsControl menu)
    {
        var entries = menu.Items.Cast<object>().ToList();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] is not Separator separator)
                continue;

            bool before = entries.Take(i).Any(IsVisibleMenuEntry);
            bool after = entries.Skip(i + 1).Any(IsVisibleMenuEntry);
            bool previousSeparator = i > 0 && entries[i - 1] is Separator previous && previous.Visibility == Visibility.Visible;
            separator.Visibility = before && after && !previousSeparator
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private static bool IsVisibleMenuEntry(object entry) =>
        entry is FrameworkElement element && element.Visibility == Visibility.Visible;
}
