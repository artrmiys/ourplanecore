using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private bool _sheetOverlayPropertiesInitialized;
    private string _sheetOverlayPropertiesPageFolder = "";

    private void SheetOverlayPropertiesPanel_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeSheetOverlayPropertiesPanel();
        RefreshSheetOverlayProperties(_currentPage);
    }

    private void PagesSideTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || !ReferenceEquals(e.OriginalSource, PagesSideTabs))
            return;

        if (e.RemovedItems.Contains(SheetOverlayPropertiesTab))
            SheetOverlayPropertiesTab_Unselected(SheetOverlayPropertiesTab, e);
        if (e.AddedItems.Contains(SheetOverlayPropertiesTab))
            SheetOverlayPropertiesTab_Selected(SheetOverlayPropertiesTab, e);
    }

    private void SheetOverlayPropertiesTab_Selected(object sender, RoutedEventArgs e)
    {
        InitializeSheetOverlayPropertiesPanel();
        if (_currentPage == null || !IsModuleEnabled(ModuleId.SheetOverlay))
        {
            _viewport.SetSheetOverlaySelectionActive(false);
            return;
        }

        _sheetOverlayPropertiesPageFolder = _currentPage.FolderPath;
        RefreshSheetOverlayProperties(_currentPage, activateFrame: true);
    }

    private void SheetOverlayPropertiesTab_Unselected(object sender, RoutedEventArgs e)
    {
        CancelSheetOverlayPropertiesPreview(postStatus: false);
        _viewport.SetSheetOverlaySelectionActive(false);
    }

    private void InitializeSheetOverlayPropertiesPanel()
    {
        if (_sheetOverlayPropertiesInitialized)
            return;

        OverlayPropertiesPanel.CommandRequested += OverlayPropertiesPanel_CommandRequested;
        OverlayPropertiesPanel.TransformPreviewRequested += OverlayPropertiesPanel_TransformPreviewRequested;
        OverlayPropertiesPanel.TransformCommitRequested += OverlayPropertiesPanel_TransformCommitRequested;
        OverlayPropertiesPanel.TransformCancelRequested += OverlayPropertiesPanel_TransformCancelRequested;
        OverlayPropertiesPanel.UndoRequested += OverlayPropertiesPanel_UndoRequested;
        OverlayPropertiesPanel.OpacityPreviewRequested += OverlayPropertiesPanel_OpacityPreviewRequested;
        OverlayPropertiesPanel.OpacityCommitRequested += OverlayPropertiesPanel_OpacityCommitRequested;
        _viewport.SheetOverlayTransformChanged += OnSheetOverlayTransformChangedForProperties;
        _viewport.SheetOverlayTransformPreviewChanged += OnSheetOverlayTransformPreviewChanged;
        _sheetOverlayPropertiesInitialized = true;
    }

    private void ShowSheetOverlayProperties(PageInfo page)
    {
        if (!RequireModule(ModuleId.SheetOverlay, "Overlay Properties"))
            return;

        PageInfo latest = ReadLatestSheetOverlayPage(page);
        if (_currentPage == null || !SameFolder(_currentPage.FolderPath, latest.FolderPath))
            OpenPageInActiveTab(latest);

        PageInfo target = _currentPage != null &&
                          SameFolder(_currentPage.FolderPath, latest.FolderPath)
            ? _currentPage
            : latest;
        _sheetOverlayPropertiesPageFolder = target.FolderPath;
        PagesSideTabs.SelectedItem = SheetOverlayPropertiesTab;
        RefreshSheetOverlayProperties(target, activateFrame: true);
    }

    private void RefreshSheetOverlayProperties(
        PageInfo? page,
        bool activateFrame = false)
    {
        if (!_sheetOverlayPropertiesInitialized)
            return;

        PageInfo? latest = page == null
            ? null
            : OurPlanCoreJobStore.TryReadPage(page.FolderPath) ?? page;
        if (latest == null)
        {
            OverlayPropertiesPanel.SetState(EmptySheetOverlayPropertiesState());
            if (activateFrame)
                _viewport.SetSheetOverlaySelectionActive(false);
            return;
        }

        bool isCurrentPage =
            _currentPage != null &&
            SameFolder(_currentPage.FolderPath, latest.FolderPath);
        if (isCurrentPage)
            _currentPage = latest;
        _sheetOverlayPropertiesPageFolder = latest.FolderPath;

        bool hasOverlay = !string.IsNullOrWhiteSpace(latest.OverlayPageFolder);
        PageInfo? sourcePage = hasOverlay
            ? OurPlanCoreJobStore.TryReadPage(latest.OverlayPageFolder)
            : null;
        SheetOverlayTransformSnapshot? viewportTransform =
            isCurrentPage
                ? _viewport.CurrentSheetOverlayTransform()
                : null;
        double offsetRange = viewportTransform?.SuggestedOffsetRangePt ??
                             Math.Max(
                                 720,
                                 Math.Max(
                                     Math.Abs(latest.OverlayOffsetXPt),
                                     Math.Abs(latest.OverlayOffsetYPt)) * 1.25 + 72);

        IReadOnlyList<SheetOverlayLayerChoice> layerChoices = latest.OverlayLayers
            .Select((layer, index) => new SheetOverlayLayerChoice(
                layer.Id,
                $"{index + 1}. {OverlayPageName(layer.SourcePageFolder)}"))
            .ToList();
        OverlayPropertiesPanel.SetState(new SheetOverlayPropertiesState(
            latest.Name,
            sourcePage?.Name ?? OverlaySourceFallbackName(latest.OverlayPageFolder),
            sourcePage?.PdfPath ?? latest.OverlayPageFolder,
            latest.ActiveOverlayId,
            layerChoices,
            hasOverlay,
            latest.OverlayVisible,
            IsCurrentJobReadOnly,
            latest.OverlayOffsetXPt,
            latest.OverlayOffsetYPt,
            latest.OverlayScale,
            latest.OverlayRotationDegrees,
            latest.OverlayColor,
            latest.OverlayOpacity,
            offsetRange));
        if (activateFrame)
        {
            _viewport.SetSheetOverlaySelectionActive(
                hasOverlay &&
                latest.OverlayVisible &&
                isCurrentPage &&
                _viewport.HasSheetOverlay &&
                IsModuleEnabled(ModuleId.SheetOverlay),
                canEdit: !IsCurrentJobReadOnly);
        }
    }

    private void OverlayPropertiesPanel_CommandRequested(
        object? sender,
        SheetOverlayPropertiesCommandEventArgs e)
    {
        PageInfo? page = ResolveSheetOverlayPropertiesPage();
        if (page == null)
        {
            TxtStatus.Text = "Open a sheet before editing overlay properties.";
            return;
        }

        if (e.Command == SheetOverlayPropertiesCommand.OpenSource)
        {
            OpenSheetOverlaySource(page);
            return;
        }

        if (IsCurrentJobReadOnly)
        {
            TxtStatus.Text = "Read-only job: overlay properties cannot be changed.";
            RefreshSheetOverlayProperties(page);
            return;
        }

        CancelSheetOverlayPropertiesPreview(postStatus: false);
        switch (e.Command)
        {
            case SheetOverlayPropertiesCommand.Add:
                ChooseSheetOverlayAutoSelectCandidate(page, addAsNew: true);
                break;
            case SheetOverlayPropertiesCommand.ChooseReplace:
                ChooseSheetOverlayAutoSelectCandidate(page);
                break;
            case SheetOverlayPropertiesCommand.NextMatch:
                AutoSelectAndFitSheetOverlay(
                    page,
                    replaceExistingOverlay: true,
                    skipCurrentOverlay: true);
                break;
            case SheetOverlayPropertiesCommand.Clear:
                ClearPageOverlay(page);
                _viewport.SetSheetOverlaySelectionActive(false);
                RefreshSheetOverlayProperties(ReadLatestSheetOverlayPage(page));
                break;
            case SheetOverlayPropertiesCommand.SelectLayer:
                ActivateSheetOverlayLayer(page, e.Value, showProperties: false);
                RefreshSheetOverlayProperties(ReadLatestSheetOverlayPage(page), activateFrame: true);
                break;
            case SheetOverlayPropertiesCommand.MoveUp:
                MoveSheetOverlayLayer(page, page.ActiveOverlayId, 1);
                break;
            case SheetOverlayPropertiesCommand.MoveDown:
                MoveSheetOverlayLayer(page, page.ActiveOverlayId, -1);
                break;
            case SheetOverlayPropertiesCommand.AutoFit:
                AutoFitSheetOverlay(page);
                break;
            case SheetOverlayPropertiesCommand.FindBest:
                AutoSelectAndFitSheetOverlay(page, replaceExistingOverlay: true);
                break;
            case SheetOverlayPropertiesCommand.EditByPoints:
                BeginSheetOverlayPointEdit(page);
                break;
            case SheetOverlayPropertiesCommand.ResetTransform:
                SetSheetOverlayTransform(
                    page,
                    0,
                    0,
                    1,
                    0,
                    "Overlay transform reset.");
                break;
            case SheetOverlayPropertiesCommand.SetVisibility:
                SetSheetOverlayVisibility(page, e.ToggleValue == true);
                break;
            case SheetOverlayPropertiesCommand.SetColor:
                SetPageSheetOverlayColor(page, e.Value);
                break;
            case SheetOverlayPropertiesCommand.ChooseCustomColor:
                ChooseCustomSheetOverlayColor(page);
                break;
        }
    }

    private void ChooseCustomSheetOverlayColor(PageInfo page)
    {
        var dialog = new OverlayColorPaletteDialog(page.OverlayColor)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
            SetPageSheetOverlayColor(page, dialog.SelectedColor);
    }

    private void OverlayPropertiesPanel_OpacityPreviewRequested(
        object? sender,
        SheetOverlayOpacityEventArgs e)
    {
        PageInfo? page = ResolveSheetOverlayPropertiesPage();
        if (page == null || IsCurrentJobReadOnly)
            return;

        _viewport.PreviewSheetOverlayOpacity(
            page.FolderPath,
            page.OverlayPageFolder,
            page.ActiveOverlayId,
            (float)e.Opacity);
    }

    private void OverlayPropertiesPanel_OpacityCommitRequested(
        object? sender,
        SheetOverlayOpacityEventArgs e)
    {
        PageInfo? page = ResolveSheetOverlayPropertiesPage();
        if (page == null || IsCurrentJobReadOnly)
            return;

        SetPageSheetOverlayOpacity(page, e.Opacity);
    }

    private void OverlayPropertiesPanel_TransformPreviewRequested(
        object? sender,
        SheetOverlayTransformEventArgs e)
    {
        PageInfo? page = ResolveSheetOverlayPropertiesPage();
        if (page == null ||
            IsCurrentJobReadOnly ||
            string.IsNullOrWhiteSpace(page.OverlayPageFolder))
        {
            return;
        }

        if (_currentPage == null || !SameFolder(_currentPage.FolderPath, page.FolderPath))
        {
            TxtStatus.Text = "Open the overlay target sheet before editing its transform.";
            RefreshSheetOverlayProperties(page);
            return;
        }

        Controls.SheetOverlayTransformValues values = e.Values;
        SheetOverlayTransformSnapshot? preview = _viewport.PreviewSheetOverlayTransform(
            (float)values.OffsetXPt,
            (float)values.OffsetYPt,
            (float)values.OverlayScale,
            (float)values.OverlayRotationDegrees,
            preserveCenterForScale: e.Component == SheetOverlayTransformComponent.Scale);
        if (preview == null)
        {
            TxtStatus.Text = "Overlay transform is waiting for the source sheet to finish loading.";
            RefreshSheetOverlayProperties(page);
            return;
        }

        TxtStatus.Text =
            $"Overlay preview: X {preview.OffsetXPt:0.###}, Y {preview.OffsetYPt:0.###}, " +
            $"scale {preview.OverlayScale:0.###}x, rotation {preview.OverlayRotationDegrees:0.###} deg.";
    }

    private void OverlayPropertiesPanel_TransformCommitRequested(object? sender, EventArgs e)
    {
        _viewport.CommitSheetOverlayTransformPreview("Overlay transform updated.");
        OverlayPropertiesPanel.CompletePendingPreview();
    }

    private void OverlayPropertiesPanel_TransformCancelRequested(object? sender, EventArgs e) =>
        CancelSheetOverlayPropertiesPreview(postStatus: true);

    private void OverlayPropertiesPanel_UndoRequested(object? sender, EventArgs e)
    {
        if (IsCurrentJobReadOnly)
        {
            TxtStatus.Text = "Read-only job: overlay changes cannot be undone.";
            return;
        }

        _viewport.UndoLast();
    }

    private void OnSheetOverlayTransformChanged(SheetOverlayTransformChange change)
    {
        if (_currentPage == null ||
            string.IsNullOrWhiteSpace(_currentPage.OverlayPageFolder) ||
            !SameFolder(_currentPage.FolderPath, change.TargetPageFolder) ||
            !SameFolder(_currentPage.OverlayPageFolder, change.OverlayPageFolder) ||
            !string.Equals(
                _currentPage.ActiveOverlayId,
                change.OverlayId,
                StringComparison.OrdinalIgnoreCase))
        {
            AppLog.Warn(
                $"Ignored stale sheet overlay transform; target='{change.TargetPageFolder}'; " +
                $"source='{change.OverlayPageFolder}'; current='{_currentPage?.FolderPath ?? ""}'; " +
                $"currentSource='{_currentPage?.OverlayPageFolder ?? ""}'.");
            return;
        }

        if (!EnsureCurrentJobWritable("change the sheet overlay"))
            return;

        OurPlanCoreJobStore.SavePageOverlayTransform(
            change.TargetPageFolder,
            change.OverlayId,
            change.OffsetXPt,
            change.OffsetYPt,
            change.OverlayScale,
            change.OverlayRotationDegrees);

        if (OurPlanCoreJobStore.TryReadPage(change.TargetPageFolder) is { } updated)
        {
            _currentPage = updated;
            ClearReciprocalSheetOverlay(updated);
            RefreshPageOverlayTreeNode(updated);
            RefreshDetachedSheetOverlaysForPage(updated.FolderPath);
        }

        TxtStatus.Text = change.Status;
    }

    private void OnSheetOverlayTransformChangedForProperties(SheetOverlayTransformChange change)
    {
        OverlayPropertiesPanel.CompletePendingPreview();
        RefreshSheetOverlayProperties(_currentPage, activateFrame: true);
    }

    private void OnSheetOverlayTransformPreviewChanged(SheetOverlayTransformSnapshot transform)
    {
        if (!_sheetOverlayPropertiesInitialized)
            return;

        OverlayPropertiesPanel.ApplyPreviewValues(new Controls.SheetOverlayTransformValues(
            transform.OffsetXPt,
            transform.OffsetYPt,
            transform.OverlayScale,
            transform.OverlayRotationDegrees));
    }

    private void CancelSheetOverlayPropertiesPreview(bool postStatus)
    {
        _viewport.CancelSheetOverlayTransformPreview(postStatus);
        if (_sheetOverlayPropertiesInitialized)
            OverlayPropertiesPanel.CompletePendingPreview();
    }

    private void SetSheetOverlayVisibility(PageInfo page, bool visible)
        => SetSheetOverlayVisibility(page, page.ActiveOverlayId, visible);

    private void SetSheetOverlayVisibility(
        PageInfo page,
        string overlayId,
        bool visible)
    {
        PageInfo latest = ReadLatestSheetOverlayPage(page);
        SheetOverlayLayerInfo? layer = latest.OverlayLayers.FirstOrDefault(item =>
            string.Equals(item.Id, overlayId, StringComparison.OrdinalIgnoreCase));
        if (layer != null && layer.IsVisible != visible)
        {
            TogglePageOverlayVisibility(latest, overlayId);
            return;
        }

        if (_currentPage != null && SameFolder(_currentPage.FolderPath, latest.FolderPath))
        {
            RefreshSheetOverlayProperties(
                latest,
                activateFrame: SheetOverlayPropertiesTab.IsSelected);
        }
    }

    private PageInfo? ResolveSheetOverlayPropertiesPage()
    {
        string folder = !string.IsNullOrWhiteSpace(_sheetOverlayPropertiesPageFolder)
            ? _sheetOverlayPropertiesPageFolder
            : _currentPage?.FolderPath ?? "";
        if (string.IsNullOrWhiteSpace(folder))
            return null;

        return OurPlanCoreJobStore.TryReadPage(folder) ??
               (_currentPage != null && SameFolder(_currentPage.FolderPath, folder)
                   ? _currentPage
                   : null);
    }

    private void OnSheetOverlayPageChanging()
    {
        CancelSheetOverlayPropertiesPreview(postStatus: false);
        _viewport.SetSheetOverlaySelectionActive(false);
        _sheetOverlayPropertiesPageFolder = "";
    }

    private void OnSheetOverlayPageOpened(PageInfo page)
    {
        _sheetOverlayPropertiesPageFolder = page.FolderPath;
        RefreshSheetOverlayProperties(page);
    }

    private void OnSheetOverlayPageCleared()
    {
        OnSheetOverlayPageChanging();
        if (_sheetOverlayPropertiesInitialized)
            OverlayPropertiesPanel.SetState(EmptySheetOverlayPropertiesState());
    }

    private void ApplySheetOverlayJobAccessState()
    {
        if (!_sheetOverlayPropertiesInitialized)
            return;

        bool activateFrame =
            SheetOverlayPropertiesTab.IsSelected &&
            IsModuleEnabled(ModuleId.SheetOverlay);
        RefreshSheetOverlayProperties(_currentPage, activateFrame);
        if (!activateFrame)
            _viewport.SetSheetOverlaySelectionActive(false);
    }

    private void ActivateCurrentSheetOverlayFrameAfterBitmapLoad(PageInfo page)
    {
        if (!_sheetOverlayPropertiesInitialized ||
            !SheetOverlayPropertiesTab.IsSelected ||
            !IsModuleEnabled(ModuleId.SheetOverlay) ||
            _currentPage == null ||
            !SameFolder(_currentPage.FolderPath, page.FolderPath))
        {
            return;
        }

        _viewport.SetSheetOverlaySelectionActive(
            active: page.OverlayVisible &&
                    !string.IsNullOrWhiteSpace(page.ActiveOverlayId),
            canEdit: !IsCurrentJobReadOnly);
    }

    private void ClearSheetOverlayViewportAndSelection()
    {
        CancelSheetOverlayPropertiesPreview(postStatus: false);
        _viewport.SetSheetOverlaySelectionActive(false);
        _viewport.ClearSheetOverlay();
    }

    private void FinishSheetOverlayStateRefresh(PageInfo page, string status)
    {
        RefreshPageOverlayTreeNode(page);
        RefreshDetachedSheetOverlaysForPage(page.FolderPath);
        if (_currentPage != null && SameFolder(_currentPage.FolderPath, page.FolderPath))
        {
            RefreshSheetOverlayProperties(
                page,
                activateFrame: _sheetOverlayPropertiesInitialized &&
                               SheetOverlayPropertiesTab.IsSelected);
        }
        TxtStatus.Text = status;
    }

    private static SheetOverlayPropertiesState EmptySheetOverlayPropertiesState() =>
        new(
            "",
            "",
            "",
            "",
            [],
            false,
            false,
            true,
            0,
            0,
            1,
            0,
            DefaultSheetOverlayColor,
            DefaultSheetOverlayOpacity,
            720);

    private static string OverlaySourceFallbackName(string folder) =>
        string.IsNullOrWhiteSpace(folder)
            ? "Not set"
            : Path.GetFileName(folder.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
}
