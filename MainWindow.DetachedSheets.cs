using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    private bool _synchronizingOrthoAcrossViewports;

    private void ConfigureDetachedSheetWindow(DetachedSheetWindow window, UnitMode unitMode)
    {
        PdfViewport viewport = window.Viewport;
        viewport.ActiveAnnotationColor = _annotationColor;
        viewport.ActiveAnnotationStrokeWidth = _annotationStrokeWidth;
        viewport.SnapEnabled = _viewport.SnapEnabled;
        viewport.PdfSnapEnabled = _viewport.PdfSnapEnabled;
        viewport.OrthoEnabled = _viewport.OrthoEnabled;
        viewport.BoxModeEnabled = _viewport.BoxModeEnabled;
        viewport.IsReadOnlyMode = IsCurrentJobReadOnly;
        ApplyDetachedActiveTakeoff(viewport, _activeItem);
        ApplyDetachedTool(window, IsCurrentJobReadOnly ? "select" : _activeTool);

        viewport.StatusChanged += message => TxtStatus.Text = $"{window.Page.Name}: {message}";
        // Tool hotkeys pressed inside a detached window drive the same global
        // tool state as the main viewport (SetTool propagates back to every
        // detached window), keeping record/tool behavior identical everywhere.
        viewport.ToolChanged += OnToolChanged;
        viewport.MeasurementAdded += measurement => OnDetachedMeasurementAdded(window, measurement, unitMode);
        viewport.MeasurementsAdded += measurements => OnDetachedMeasurementsAdded(window, measurements, unitMode);
        viewport.MeasurementRemoved += measurement => OnDetachedMeasurementsRemoved(window, [measurement], unitMode);
        viewport.MeasurementsRemoved += measurements => OnDetachedMeasurementsRemoved(window, measurements, unitMode);
        viewport.MeasurementChanged += measurement => OnDetachedMeasurementsChanged(window, [measurement], unitMode);
        viewport.MeasurementsChanged += measurements => OnDetachedMeasurementsChanged(window, measurements, unitMode);
        // Selecting measurements on a detached sheet syncs the Takeoffs tree,
        // estimate list, and page highlights exactly like the main viewport.
        viewport.MeasurementSelectionChanged += OnViewportMeasurementSelectionChanged;
        viewport.MeasurementsSelectionChanged += OnViewportMeasurementsSelectionChanged;
        viewport.CopyMeasurementsRequested += CopyMeasurementsToClipboard;
        viewport.PasteMeasurementsRequested += at => PasteMeasurementsFromClipboardInto(viewport, window.Page, at);
        viewport.TakeoffRenameRequested += OnViewportTakeoffRenameRequested;
        viewport.SnapChanged += OnViewportSnapChanged;
        viewport.PdfSnapChanged += OnViewportPdfSnapChanged;
        viewport.BoxModeChanged += OnViewportBoxModeChanged;
        viewport.ContextRequested += request => OnDetachedViewportContextRequested(window, request);
        viewport.ScaleChanged += scale => OnDetachedPageScaleChanged(window, scale);
        viewport.PageAnnotationAdded += _ => SaveDetachedPageAnnotations(window);
        viewport.PageAnnotationRemoved += _ => SaveDetachedPageAnnotations(window);
        viewport.PageAnnotationChanged += _ => SaveDetachedPageAnnotations(window);
        viewport.PageAnnotationTextRequested += RequestPageAnnotationText;
        viewport.OrthoChanged += SynchronizeOrthoAcrossViewports;
        viewport.JoistDirectionCaptured += (area, start, end) => OnDetachedJoistDirectionCaptured(window, area, start, end, unitMode);
        // A focused detached window gets the SAME global shortcuts as the main
        // window (Space record toggle, T new takeoff, F4 scale, F5 page setup,
        // bookmark sequence, Ctrl+O/M/S...). MainWindow's handler never fires
        // on its own while a detached window owns keyboard focus.
        window.PreviewKeyDown += MainWindow_GlobalPreviewKeyDown;
    }

    private void OnDetachedPageScaleChanged(DetachedSheetWindow window, double scaleMetersPerPt)
    {
        if (!IsCurrentJobWritable || scaleMetersPerPt <= 0)
            return;

        ApplyScaleToPagesCore([window.Page], scaleMetersPerPt, allowClear: false, updateStatus: true);
    }

    private void SynchronizeOrthoAcrossViewports(bool enabled)
    {
        if (_synchronizingOrthoAcrossViewports)
            return;

        _synchronizingOrthoAcrossViewports = true;
        try
        {
            _viewport.OrthoEnabled = enabled;
            foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
                window.Viewport.OrthoEnabled = enabled;
        }
        finally
        {
            _synchronizingOrthoAcrossViewports = false;
        }
    }

    // Snap / PDF Snap / Box mode are global drawing constraints, so toggling
    // them from any window (ribbon buttons or F3 / Ctrl+F3 / F9 hotkeys)
    // applies to the main viewport and every detached sheet alike.
    private void SynchronizeSnapAcrossViewports(bool enabled)
    {
        if (_synchronizingOrthoAcrossViewports)
            return;

        _synchronizingOrthoAcrossViewports = true;
        try
        {
            _viewport.SnapEnabled = enabled;
            foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
                window.Viewport.SnapEnabled = enabled;
        }
        finally
        {
            _synchronizingOrthoAcrossViewports = false;
        }
    }

    private void SynchronizePdfSnapAcrossViewports(bool enabled)
    {
        if (_synchronizingOrthoAcrossViewports)
            return;

        _synchronizingOrthoAcrossViewports = true;
        try
        {
            _viewport.PdfSnapEnabled = enabled;
            foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
                window.Viewport.PdfSnapEnabled = enabled;
        }
        finally
        {
            _synchronizingOrthoAcrossViewports = false;
        }
    }

    private void SynchronizeBoxModeAcrossViewports(bool enabled)
    {
        if (_synchronizingOrthoAcrossViewports)
            return;

        _synchronizingOrthoAcrossViewports = true;
        try
        {
            _viewport.BoxModeEnabled = enabled;
            foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
                window.Viewport.BoxModeEnabled = enabled;
        }
        finally
        {
            _synchronizingOrthoAcrossViewports = false;
        }
    }

    // Lightweight live push while a Display slider is being dragged: updates the
    // detached viewports' scale properties without rebuilding legends/measurements
    // (the full RefreshTakeoffDisplay runs on slider commit).
    private void ApplyLiveDisplayScalesToDetachedSheets()
    {
        foreach (DetachedSheetWindow window in _detachedSheetWindows)
        {
            PdfViewport viewport = window.Viewport;
            viewport.MeasurementLabelScale = _settings.MeasurementLabelScale;
            viewport.MeasurementStrokeScale = _settings.ViewportMeasurementStrokeScale;
            viewport.PointSizeScale = _settings.ViewportPointSizeScale;
            viewport.RulerStrokeWidth = _settings.ViewportRulerStrokeWidth;
            viewport.AreaEdgeScale = _settings.ViewportAreaEdgeScale;
            viewport.AreaFillOpacity = _settings.ViewportAreaFillOpacity;
            viewport.PdfSnapBridgeToleranceScreenPx = _settings.ViewportPdfSnapBridgeTolerancePx;
            viewport.ZoomWheelFactor = _settings.ViewportZoomWheelFactor;
            viewport.InvalidateVisual();
        }
    }

    // Minimal takeoff-focused right-click menu for detached sheets: copy/paste
    // to THIS sheet, takeoff/section properties, rename, joist actions, and
    // per-sheet visibility. Page-bound extras of the main canvas menu (AI
    // crops, sheet overlay, 3D) stay main-window-only because they write to
    // the main window's current page.
    private void OnDetachedViewportContextRequested(DetachedSheetWindow window, ViewportContextRequest request)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before using the canvas context menu.";
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = window.Viewport,
            Placement = PlacementMode.MousePoint,
        };

        string point = $"PDF {request.PdfX:F0}, {request.PdfY:F0}";
        menu.Items.Add(new MenuItem
        {
            Header = request.Measurement != null
                ? $"Measurement - {request.Measurement.MType} @ {point}"
                : $"{window.Page.Name} @ {point}",
            IsEnabled = false,
        });
        menu.Items.Add(new Separator());

        var selected = window.Viewport.GetSelectedMeasurements();
        Measurement? clickedMeasurement = request.Measurement;
        IReadOnlyList<Measurement> copySource = selected.Count > 0
            ? selected
            : clickedMeasurement != null
                ? [clickedMeasurement]
                : [];
        menu.Items.Add(MakeMenuItem(
            selected.Count > 1 ? "Copy Selected Measurements" : "Copy Measurement",
            copySource.Count > 0,
            () => CopyMeasurementsToClipboard(copySource)));

        if (!IsCurrentJobReadOnly)
        {
            int clipboardCount = _measurementClipboard?.Entries.Count ?? 0;
            menu.Items.Add(MakeWritableViewportMenuItem(
                clipboardCount > 0
                    ? $"Paste {clipboardCount} Measurement(s) to This Sheet"
                    : "Paste Measurements to This Sheet",
                clipboardCount > 0,
                "paste measurements",
                () => PasteMeasurementsFromClipboardInto(
                    window.Viewport,
                    window.Page,
                    new SKPoint((float)request.PdfX, (float)request.PdfY))));
        }

        if (clickedMeasurement != null)
        {
            bool hasItem = TryResolveTakeoffItemForMeasurement(clickedMeasurement, out TakeoffItem item);
            string entryTitle = hasItem ? MeasurementEntryTitle(item) : "Measurement";

            menu.Items.Add(new Separator());
            menu.Items.Add(MakeWritableViewportMenuItem(
                "Takeoff Properties...",
                hasItem,
                "edit takeoff properties",
                () => EditViewportTakeoffProperties(item)));
            menu.Items.Add(MakeWritableViewportMenuItem(
                $"{entryTitle} Properties...",
                hasItem,
                "edit section properties",
                () => EditSectionProperties(item, clickedMeasurement)));
            menu.Items.Add(MakeWritableViewportMenuItem(
                $"Rename {entryTitle}",
                hasItem,
                "rename a takeoff section",
                () => RenameSection(item, clickedMeasurement)));

            if (IsModuleEnabled(ModuleId.AdvancedTakeoffTools) &&
                OurPlanCoreJobStore.NormalizeMeasurementType(clickedMeasurement.MType) == "area")
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(MakeWritableViewportMenuItem(
                    hasItem && item.IsJoistArea ? "Joist Properties..." : "Use Area As Joists...",
                    hasItem,
                    "edit joist properties",
                    () => EditViewportTakeoffProperties(item)));
                menu.Items.Add(MakeWritableViewportMenuItem(
                    "Refresh Regular Joists in All Area Segments",
                    hasItem && item.IsJoistArea,
                    "add joists",
                    () => AddJoistsToAllAreas(item)));
                menu.Items.Add(MakeWritableViewportMenuItem(
                    "Set / Reset Joist Direction",
                    hasItem,
                    "set a joist direction",
                    () => BeginDetachedJoistDirectionCapture(window, item, clickedMeasurement)));
                menu.Items.Add(MakeWritableViewportMenuItem(
                    "Delete Nearest Extra Joist",
                    hasItem && item.IsJoistArea && clickedMeasurement.ExtraJoists.Count > 0,
                    "delete an Extra Joist",
                    () => window.Viewport.DeleteNearestExtraJoist(
                        clickedMeasurement,
                        new SKPoint((float)request.PdfX, (float)request.PdfY))));
            }

            if (hasItem)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(MakeMenuItem(
                    IsPageTakeoffVisible(window.Page, item) ? "Hide on This Sheet" : "Show on This Sheet",
                    true,
                    () => TogglePageTakeoffVisibility(window.Page, item)));
            }
        }

        menu.IsOpen = true;
    }

    // RefreshTakeoffDisplay rebuilds the viewport's measurement list, which
    // clears its selection and raises empty selection-changed events. Those
    // must not wipe the Takeoffs tree selection, so every refresh goes through
    // this guard.
    private void RefreshDetachedTakeoffDisplay(DetachedSheetWindow window, UnitMode unitMode)
    {
        if (_currentJob == null)
            return;

        bool wasSyncing = _syncingViewportSelectionFromTakeoffItem;
        _syncingViewportSelectionFromTakeoffItem = true;
        try
        {
            window.RefreshTakeoffDisplay(_currentJob, _takeoffItems, _settings, unitMode);
        }
        finally
        {
            _syncingViewportSelectionFromTakeoffItem = wasSyncing;
        }
    }

    private void RefreshDetachedSheetRenderQuality()
    {
        foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
            window.Viewport.RefreshRenderQuality();
    }

    private void RefreshDetachedSheetStaticRasterDpi()
    {
        foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
            window.Viewport.RefreshStaticRasterDpi();
    }

    private void RefreshDetachedSheetsForPage(string pageFolder)
    {
        if (_currentJob == null || _detachedSheetWindows.Count == 0)
            return;

        UnitMode unitMode = _settings.UnitMode == UnitMode.Metric.ToString()
            ? UnitMode.Metric
            : UnitMode.Imperial;
        foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
        {
            if (IsSamePageFolder(window.Page.FolderPath, pageFolder))
                RefreshDetachedTakeoffDisplay(window, unitMode);
        }
    }

    private bool HasDetachedSheetForPage(string pageFolder) =>
        _detachedSheetWindows.Any(window => IsSamePageFolder(window.Page.FolderPath, pageFolder));

    private void SelectMeasurementsInDetachedSheets(string pageFolder, IReadOnlyList<Measurement> measurements)
    {
        RunWithDetachedSelectionSyncGuard(() =>
        {
            foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
            {
                if (IsSamePageFolder(window.Page.FolderPath, pageFolder))
                    window.Viewport.SelectMeasurements(measurements);
            }
        });
    }

    private void SelectMeasurementsInDetachedSheetsByPage(IReadOnlyList<Measurement> measurements)
    {
        RunWithDetachedSelectionSyncGuard(() =>
        {
            foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
            {
                var onPage = measurements
                    .Where(measurement => IsSamePageFolder(measurement.PageFolder, window.Page.FolderPath))
                    .ToList();
                if (onPage.Count > 0)
                    window.Viewport.SelectMeasurements(onPage);
            }
        });
    }

    // Mirrors the main-viewport behavior of highlighting a takeoff selected in
    // the Takeoffs tree: each detached window highlights that takeoff's
    // measurements on ITS OWN page (skipped when the takeoff has none there,
    // matching how the main canvas keeps its selection in that case).
    private void SelectTakeoffMeasurementsInDetachedSheets(IReadOnlyList<TakeoffItem> items)
    {
        RunWithDetachedSelectionSyncGuard(() =>
        {
            foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
            {
                var measurements = items
                    .SelectMany(item => MeasurementsForTakeoffOnPage(item, window.Page.FolderPath))
                    .Distinct()
                    .ToList();
                if (measurements.Count > 0)
                    window.Viewport.SelectMeasurements(measurements);
            }
        });
    }

    // Tree -> canvas selection pushes must not bounce back through the
    // detached viewports' selection-changed events into the tree again.
    private void RunWithDetachedSelectionSyncGuard(Action action)
    {
        bool wasSyncing = _syncingViewportSelectionFromTakeoffItem;
        _syncingViewportSelectionFromTakeoffItem = true;
        try
        {
            action();
        }
        finally
        {
            _syncingViewportSelectionFromTakeoffItem = wasSyncing;
        }
    }

    private void RefreshDetachedPageScale(string pageFolder, double scaleMetersPerPt)
    {
        foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
        {
            if (IsSamePageFolder(window.Page.FolderPath, pageFolder))
                window.RefreshPageScale(scaleMetersPerPt);
        }
    }

    private void ApplyDetachedActiveTakeoff(PdfViewport viewport, TakeoffItem? item)
    {
        if (item == null)
        {
            viewport.ActiveTakeoffFolder = "";
            viewport.ActiveCountSymbol = _newCountSymbol;
            return;
        }

        viewport.ActiveColor = item.Color;
        viewport.ActiveTakeoffFolder = item.FolderPath;
        viewport.ActiveCountSymbol = item.CountSymbol;
    }

    private void ApplyDetachedTool(DetachedSheetWindow window, string tool)
    {
        string requestedTool = string.IsNullOrWhiteSpace(tool) ? "select" : tool;
        if (IsRecordTool(requestedTool))
        {
            string measurementType = RecordMeasurementType(requestedTool);
            bool joistArea = IsJoistAreaTool(requestedTool);
            if (_activeItem == null || !CanRecordIntoActiveTakeoff(_activeItem, measurementType, joistArea))
            {
                window.Viewport.SetTool("select");
                TxtStatus.Text = $"{window.Page.Name}: select a matching takeoff item in the main Takeoffs tree before drawing in detached view.";
                return;
            }

            ApplyDetachedActiveTakeoff(window.Viewport, _activeItem);
        }

        window.Viewport.SetTool(ViewportToolName(requestedTool));
    }

    private void OnDetachedMeasurementAdded(DetachedSheetWindow window, Measurement measurement, UnitMode unitMode)
    {
        if (!IsCurrentJobWritable)
        {
            RejectDetachedWrite(window, unitMode);
            return;
        }

        if (!TryResolveDetachedTakeoffItem(window.Viewport, measurement, out TakeoffItem item))
        {
            window.Viewport.DeleteMeasurements([measurement]);
            TxtStatus.Text = $"{window.Page.Name}: no matching takeoff item is active for {MeasurementTypeTitle(measurement.MType)}.";
            return;
        }

        _activeItem = item;
        EnsureTakeoffItemFolder(item);
        measurement.PageFolder = window.Page.FolderPath;
        measurement.TakeoffFolder = item.FolderPath;
        if (measurement.ScaleMetersPerPt <= 0)
            measurement.ScaleMetersPerPt = window.Viewport.ScaleMetersPerPt;
        if (!item.Measurements.Contains(measurement))
            item.Measurements.Add(measurement);

        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        RefreshAfterDetachedTakeoffChange(window, [item], [measurement.PageFolder], unitMode);
        if (item.IsJoistArea && OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area")
            BeginDetachedJoistDirectionCapture(window, item, measurement);
    }

    private void OnDetachedMeasurementsAdded(DetachedSheetWindow window, IReadOnlyList<Measurement> measurements, UnitMode unitMode)
    {
        foreach (Measurement measurement in measurements)
            OnDetachedMeasurementAdded(window, measurement, unitMode);
    }

    private void OnDetachedMeasurementsRemoved(DetachedSheetWindow window, IReadOnlyList<Measurement> measurements, UnitMode unitMode)
    {
        if (!IsCurrentJobWritable)
        {
            RejectDetachedWrite(window, unitMode);
            return;
        }

        if (measurements.Count == 0)
            return;

        var removed = measurements.Distinct().ToHashSet();
        var changedItems = new List<TakeoffItem>();
        foreach (TakeoffItem item in _takeoffItems)
        {
            int before = item.Measurements.Count;
            item.Measurements.RemoveAll(removed.Contains);
            if (item.Measurements.Count != before)
                changedItems.Add(item);
        }

        RefreshAfterDetachedTakeoffChange(
            window,
            changedItems,
            measurements.Select(measurement => measurement.PageFolder),
            unitMode);
    }

    private void OnDetachedMeasurementsChanged(DetachedSheetWindow window, IReadOnlyList<Measurement> measurements, UnitMode unitMode)
    {
        if (!IsCurrentJobWritable)
        {
            RejectDetachedWrite(window, unitMode);
            return;
        }

        if (measurements.Count == 0)
            return;

        var changedItems = measurements
            .Select(FindTakeoffItemForMeasurement)
            .Where(item => item != null)
            .Cast<TakeoffItem>()
            .Distinct()
            .ToList();
        foreach (Measurement measurement in measurements)
        {
            TakeoffItem? item = FindTakeoffItemForMeasurement(measurement);
            if (item == null)
                continue;

            measurement.PageFolder = window.Page.FolderPath;
            measurement.TakeoffFolder = item.FolderPath;
            if (measurement.ScaleMetersPerPt <= 0)
                measurement.ScaleMetersPerPt = window.Viewport.ScaleMetersPerPt;
            OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        }

        RefreshAfterDetachedTakeoffChange(
            window,
            changedItems,
            measurements.Select(measurement => measurement.PageFolder),
            unitMode);
    }

    private bool TryResolveDetachedTakeoffItem(PdfViewport viewport, Measurement measurement, out TakeoffItem item)
    {
        string measurementType = OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType);
        if (!string.IsNullOrWhiteSpace(viewport.ActiveTakeoffFolder))
        {
            TakeoffItem? byViewport = FindTakeoffItemByFolder(viewport.ActiveTakeoffFolder, measurementType);
            if (byViewport != null)
            {
                byViewport.MeasurementType = measurementType;
                item = byViewport;
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(measurement.TakeoffFolder))
        {
            TakeoffItem? byMeasurement = FindTakeoffItemByFolder(measurement.TakeoffFolder, measurementType);
            if (byMeasurement != null)
            {
                byMeasurement.MeasurementType = measurementType;
                item = byMeasurement;
                return true;
            }
        }

        if (_activeItem != null &&
            OurPlanCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType) == measurementType)
        {
            _activeItem.MeasurementType = measurementType;
            item = _activeItem;
            return true;
        }

        item = null!;
        return false;
    }

    private void RefreshAfterDetachedTakeoffChange(
        DetachedSheetWindow window,
        IEnumerable<TakeoffItem> changedItems,
        IEnumerable<string?> pageFolders,
        UnitMode unitMode)
    {
        var items = changedItems.Distinct().ToList();
        foreach (TakeoffItem item in items)
        {
            RefreshTreeItem(item);
            QueueTakeoffAutosave(item);
        }

        using (UsePageMeasurementLookup())
        {
            RefreshTakeoffRowVisualsForItems(items);
            foreach (string pageFolder in pageFolders
                         .Where(folder => !string.IsNullOrWhiteSpace(folder))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Cast<string>())
            {
                RefreshPageTakeoffIndicatorsForFolder(pageFolder);
            }

            RefreshSheetLegend();
        }

        RefreshDetachedTakeoffDisplay(window, unitMode);
        _viewport.SetMeasurements(_takeoffItems.SelectMany(takeoff => takeoff.Measurements));
        RefreshEstimateTable();
        UpdateTotalDisplay();
    }

    private void SaveDetachedPageAnnotations(DetachedSheetWindow window)
    {
        if (!IsCurrentJobWritable)
        {
            TxtStatus.Text = $"{window.Page.Name}: read-only; markup changes were not saved.";
            return;
        }

        try
        {
            OurPlanCoreJobStore.SavePageAnnotations(
                window.Page.FolderPath,
                window.Viewport.GetPageAnnotations());
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"{window.Page.Name}: annotation save skipped: {ex.Message}";
        }
    }

    private void BeginDetachedJoistDirectionCapture(DetachedSheetWindow window, TakeoffItem item, Measurement area)
    {
        item.IsJoistTakeoff = true;
        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        if (window.Viewport.BeginJoistDirectionCapture(area))
            TxtStatus.Text = $"{window.Page.Name}: draw a two-point line parallel to joists for {item.Name}.";
    }

    private void OnDetachedJoistDirectionCaptured(
        DetachedSheetWindow window,
        Measurement area,
        SKPoint start,
        SKPoint end,
        UnitMode unitMode)
    {
        if (!IsCurrentJobWritable)
        {
            RejectDetachedWrite(window, unitMode);
            return;
        }

        TakeoffItem? item = FindTakeoffItemForMeasurement(area);
        if (item == null)
            return;

        if (!TryDirectionFromPoints(start, end, out double directionDegrees))
        {
            TxtStatus.Text = $"{window.Page.Name}: joist direction line is too short.";
            return;
        }

        item.IsJoistTakeoff = true;
        item.JoistDirectionDegrees = directionDegrees;
        area.JoistDirectionDegrees = directionDegrees;
        area.JoistDirectionLocked = true;
        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        RefreshAfterDetachedTakeoffChange(window, [item], [area.PageFolder], unitMode);

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(area, window.Viewport.ScaleMetersPerPt);
        TxtStatus.Text = $"{window.Page.Name}: joists generated for {item.Name}, direction {directionDegrees:0.#} deg, {JoistTakeoffCalculator.FormatDiagnostics(layout, unitMode)}.";
    }

    private void RejectDetachedWrite(DetachedSheetWindow window, UnitMode unitMode)
    {
        if (_currentJob != null)
        {
            LoadTakeoffsForJob();
            RefreshDetachedTakeoffDisplay(window, unitMode);
        }
        TxtStatus.Text = $"{window.Page.Name}: this job is read-only.";
    }
}
