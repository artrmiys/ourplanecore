using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void BtnFloatingPageSetup_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before Page Setup.";
            return;
        }

        if (_currentPage == null)
        {
            PageInfo? firstPage = CollectPagesUnder(_currentJob.PagesRoot).FirstOrDefault();
            if (firstPage == null)
            {
                TxtStatus.Text = "No pages are available for Page Setup.";
                return;
            }

            OpenPageInActiveTab(firstPage);
        }

        if (_pageSetupWindow != null)
        {
            RefreshFloatingPageSetup();
            PositionFloatingPageSetup();
            _pageSetupWindow.Activate();
            return;
        }

        _pageSetupWindow = new PageSetupWindow
        {
            Owner = this,
        };
        _pageSetupWindow.ApplyRequested += PageSetupWindow_ApplyRequested;
        _pageSetupWindow.NavigateRequested += PageSetupWindow_NavigateRequested;
        _pageSetupWindow.Closed += (_, _) => _pageSetupWindow = null;
        RefreshFloatingPageSetup();
        _pageSetupWindow.Show();
        PositionFloatingPageSetup();
    }

    private void PageSetupWindow_ApplyRequested(object? sender, EventArgs e)
    {
        if (TryApplyFloatingPageSetup(out PageInfo? appliedPage))
        {
            RefreshFloatingPageSetup(appliedPage?.FolderPath, selectName: false);
            _pageSetupWindow?.ShowStatus($"Applied {appliedPage?.Name ?? "page"}.");
        }
    }

    private void PageSetupWindow_NavigateRequested(object? sender, PageSetupNavigateEventArgs e)
    {
        if (_currentJob == null || _currentPage == null)
            return;

        string currentPathBeforeApply = _currentPage.FolderPath;
        IReadOnlyList<PageInfo> beforePages = CollectPagesUnder(_currentJob.PagesRoot).ToList();
        int beforeIndex = PageSetupPageIndex(beforePages, currentPathBeforeApply);
        if (beforeIndex < 0)
            beforeIndex = 0;

        if (!TryApplyFloatingPageSetup(out PageInfo? appliedPage))
            return;

        IReadOnlyList<PageInfo> pages = CollectPagesUnder(_currentJob.PagesRoot).ToList();
        int index = appliedPage != null
            ? PageSetupPageIndex(pages, appliedPage.FolderPath)
            : -1;
        if (index < 0)
            index = Math.Clamp(beforeIndex, 0, Math.Max(0, pages.Count - 1));

        int targetIndex = Math.Clamp(index + Math.Sign(e.Direction), 0, pages.Count - 1);
        if (targetIndex == index || pages.Count == 0)
        {
            RefreshFloatingPageSetup(appliedPage?.FolderPath, selectName: false);
            return;
        }

        OpenPageInActiveTab(pages[targetIndex]);
        RefreshFloatingPageSetup(pages[targetIndex].FolderPath, selectName: true);
    }

    private bool TryApplyFloatingPageSetup(out PageInfo? appliedPage)
    {
        appliedPage = null;
        if (_pageSetupWindow == null || _currentJob == null || _currentPage == null)
            return false;

        string pageName = _pageSetupWindow.PageName;
        if (string.IsNullOrWhiteSpace(pageName))
        {
            _pageSetupWindow.ShowStatus("Page name is required.");
            return false;
        }

        string scaleText = _pageSetupWindow.ScaleText;
        double scaleMetersPerPt = 0;
        bool hasScale = !string.IsNullOrWhiteSpace(scaleText);
        if (hasScale &&
            !PdfSheetMetadataService.TryParseScaleMetersPerPt(scaleText, out scaleMetersPerPt))
        {
            _pageSetupWindow.ShowStatus("Invalid scale. Example: 1/8\" = 1'0\" or 1:96.");
            return false;
        }

        PageInfo sourcePage = _currentPage;
        string originalPath = sourcePage.FolderPath;
        string currentPath = originalPath;
        string originalName = OurPlaneCoreJobStore.DisplayName(originalPath);
        bool renamed = false;
        bool scaled = false;

        try
        {
            if (hasScale)
            {
                scaled = ApplyFloatingPageSetupScale(currentPath, scaleMetersPerPt);
            }

            if (!string.Equals(pageName, originalName, StringComparison.OrdinalIgnoreCase))
            {
                string renamedPath = OurPlaneCoreJobStore.RenamePageAllowDuplicateName(currentPath, pageName);
                bool reloadActiveTab = UpdatePageReferencesForMovedPath(currentPath, renamedPath);
                currentPath = renamedPath;
                renamed = true;
                ReloadPagesTree(currentPath);
                ReloadActivePageTabAfterPathChange(reloadActiveTab);
            }

            appliedPage = OurPlaneCoreJobStore.TryReadPage(currentPath) ?? _currentPage;
            if (appliedPage != null)
                WriteFloatingPageSetupMetadata(
                    appliedPage,
                    pageName,
                    hasScale ? scaleMetersPerPt : appliedPage.ScaleMetersPerPt,
                    hasScale ? scaleText : "");

            if (!renamed)
            {
                RefreshPagesTakeoffIndicators();
                SelectNodeByFolder(currentPath);
            }

            RefreshAllTotals();
            string scaleLabel = hasScale
                ? PageSetupScaleStatusText(scaleText, scaleMetersPerPt)
                : "";
            TxtStatus.Text = BuildFloatingPageSetupStatus(pageName, renamed, scaled, scaleLabel);
            return true;
        }
        catch (Exception ex)
        {
            ShowOperationError("Page Setup", ex);
            _pageSetupWindow?.ShowStatus(ex.Message);
            return false;
        }
    }

    private bool ApplyFloatingPageSetupScale(string pageFolder, double scaleMetersPerPt)
    {
        if (scaleMetersPerPt <= 0)
            return false;

        bool changed = _currentPage == null ||
                       Math.Abs(_currentPage.ScaleMetersPerPt - scaleMetersPerPt) > 0.000000001;

        _viewport.ScaleMetersPerPt = scaleMetersPerPt;
        if (_currentPage != null)
            _currentPage.ScaleMetersPerPt = scaleMetersPerPt;
        ApplyScaleToCurrentPageMeasurements(scaleMetersPerPt);
        OurPlaneCoreJobStore.SavePageScale(pageFolder, scaleMetersPerPt);
        UpdateScaleUi(scaleMetersPerPt);
        return changed;
    }

    private void RefreshFloatingPageSetup(string? preferredPageFolder = null, bool selectName = false)
    {
        if (_pageSetupWindow == null || _currentJob == null)
            return;

        IReadOnlyList<PageInfo> pages = CollectPagesUnder(_currentJob.PagesRoot).ToList();
        if (pages.Count == 0)
        {
            _pageSetupWindow.SetPage("", "", -1, 0, "");
            return;
        }

        string pageFolder = !string.IsNullOrWhiteSpace(preferredPageFolder)
            ? preferredPageFolder
            : _currentPage?.FolderPath ?? pages[0].FolderPath;
        int index = PageSetupPageIndex(pages, pageFolder);
        if (index < 0)
            index = 0;

        PageInfo page = pages[index];
        _pageSetupWindow.SetPage(
            page.Name,
            PageSetupScaleDisplayText(page),
            index,
            pages.Count,
            page.FolderPath,
            selectName);
    }

    private void PositionFloatingPageSetup()
    {
        if (_pageSetupWindow == null)
            return;

        try
        {
            Point screenPoint = ViewportSurfaceHost.PointToScreen(new Point(14, 14));
            PresentationSource? source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
                screenPoint = source.CompositionTarget.TransformFromDevice.Transform(screenPoint);

            _pageSetupWindow.Left = screenPoint.X;
            _pageSetupWindow.Top = screenPoint.Y;
        }
        catch
        {
            _pageSetupWindow.Left = Left + 260;
            _pageSetupWindow.Top = Top + 120;
        }
    }

    private static int PageSetupPageIndex(IReadOnlyList<PageInfo> pages, string pageFolder)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            if (IsSamePageFolder(pages[i].FolderPath, pageFolder))
                return i;
        }

        return -1;
    }

    private static string BuildFloatingPageSetupStatus(string pageName, bool renamed, bool scaled, string scaleLabel)
    {
        if (renamed && scaled)
            return $"Page setup applied: {pageName}, {scaleLabel}.";
        if (renamed)
            return $"Page renamed: {pageName}.";
        if (scaled)
            return $"Page scale applied: {scaleLabel}.";

        return $"Page setup checked: {pageName}.";
    }

    private static string PageSetupScaleDisplayText(PageInfo page)
    {
        if (page.ScaleMetersPerPt <= 0)
            return "";

        string metadataText = OurPlaneCoreJobStore.ReadSourcePdfMetadata(page.FolderPath)?.EffectiveScaleText.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(metadataText) &&
            PdfSheetMetadataService.TryParseScaleMetersPerPt(metadataText, out double metadataScale) &&
            Math.Abs(metadataScale - page.ScaleMetersPerPt) <= 0.000000001)
        {
            return metadataText;
        }

        return PdfSheetMetadataService.FormatImperialScale(page.ScaleMetersPerPt);
    }

    private static string PageSetupScaleStatusText(string scaleText, double scaleMetersPerPt)
    {
        string trimmed = scaleText.Trim();
        return !string.IsNullOrWhiteSpace(trimmed)
            ? trimmed
            : PdfSheetMetadataService.FormatImperialScale(scaleMetersPerPt);
    }

    private static void WriteFloatingPageSetupMetadata(
        PageInfo page,
        string pageName,
        double scaleMetersPerPt,
        string manualScaleText = "")
    {
        PdfSheetMetadata metadata = OurPlaneCoreJobStore.ReadSourcePdfMetadata(page.FolderPath)
            ?? CreateManualSheetMetadata(page);

        metadata.GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        metadata.Source = string.IsNullOrWhiteSpace(metadata.Source) ? "manual" : metadata.Source;
        metadata.PdfPath = page.PdfPath;
        metadata.PageIndex = page.PdfPage;
        metadata.PageNumber = page.PdfPage + 1;
        metadata.SheetLabel = pageName;
        metadata.RenameCandidate = pageName;

        if (scaleMetersPerPt > 0)
        {
            string scaleText = string.IsNullOrWhiteSpace(manualScaleText)
                ? PdfSheetMetadataService.FormatImperialScale(scaleMetersPerPt)
                : manualScaleText.Trim();
            metadata.SkipScale = false;
            metadata.SelectedScaleText = scaleText;
            metadata.ScaleText = scaleText;
            metadata.SelectedScaleRatio = scaleMetersPerPt / ViewportConstants.PdfPointMeters;
            metadata.SelectedScaleMetersPerPt = scaleMetersPerPt;
        }

        OurPlaneCoreJobStore.WriteSourcePdfMetadata(page.FolderPath, metadata);
    }
}
