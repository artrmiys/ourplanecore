using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    private sealed record PageOriginMarker(float XPt, float YPt, string UpdatedAtUtc);

    private void BtnPageAddPages_Click(object sender, RoutedEventArgs e) =>
        BtnImport_Click(sender, e);

    private void BtnPageBatchRename_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<PageInfo> pages = PageToolTargetPages();
        if (pages.Count == 0)
        {
            TxtStatus.Text = "Batch Rename: select one or more pages first.";
            return;
        }

        string currentNames = string.Join(Environment.NewLine, pages.Select(page => page.Name));
        string? raw = ShowMultilineInputDialog(
            $"Rename {pages.Count} page(s), one name per line:",
            currentNames,
            "Batch Rename Pages");
        if (raw == null)
            return;

        string[] names = raw
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .ToArray();
        if (names.Length != pages.Count)
        {
            PostStatusWarning($"Expected {pages.Count} names, got {names.Length}.");
            return;
        }

        try
        {
            bool reloadActiveTab = false;
            string? selectAfter = null;
            for (int i = 0; i < pages.Count; i++)
            {
                PageInfo page = pages[i];
                if (string.Equals(page.Name, names[i], StringComparison.OrdinalIgnoreCase))
                    continue;

                string renamed = OurPlanCoreJobStore.RenamePageAllowDuplicateName(page.FolderPath, names[i]);
                reloadActiveTab = UpdatePageReferencesForMovedPath(page.FolderPath, renamed) || reloadActiveTab;
                selectAfter ??= renamed;
            }

            ReloadPagesTree(selectAfter ?? pages[0].FolderPath);
            ReloadActivePageTabAfterPathChange(reloadActiveTab);
            TxtStatus.Text = $"Batch Rename: updated {pages.Count} page(s).";
        }
        catch (Exception ex)
        {
            ShowOperationError("Batch Rename Pages", ex);
        }
    }

    private void BtnPageRotateLeft_Click(object sender, RoutedEventArgs e) =>
        ApplyCurrentPageImageOperation(PageImageOperation.RotateLeft, "Rotate Left");

    private void BtnPageRotateRight_Click(object sender, RoutedEventArgs e) =>
        ApplyCurrentPageImageOperation(PageImageOperation.RotateRight, "Rotate Right");

    private void BtnPageRotate180_Click(object sender, RoutedEventArgs e) =>
        ApplyCurrentPageImageOperation(PageImageOperation.Rotate180, "Rotate 180");

    private void BtnPageFlipVertical_Click(object sender, RoutedEventArgs e) =>
        ApplyCurrentPageImageOperation(PageImageOperation.FlipVertical, "Flip Vertical");

    private void BtnPageFlipHorizontal_Click(object sender, RoutedEventArgs e) =>
        ApplyCurrentPageImageOperation(PageImageOperation.FlipHorizontal, "Flip Horizontal");

    private void BtnPageInvert_Click(object sender, RoutedEventArgs e) =>
        ApplyCurrentPageImageOperation(PageImageOperation.Invert, "Invert");

    private void BtnPageLevel_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage == null)
        {
            TxtStatus.Text = "Level: open a page first.";
            return;
        }

        string? raw = ShowInputDialog(
            "Clockwise page rotation in degrees (-45 to 45):",
            "0",
            "Level Page");
        if (raw == null)
            return;

        if (!TryParseLevelDegrees(raw, out double degrees))
        {
            PostStatusWarning("Enter a number from -45 to 45.");
            return;
        }

        if (Math.Abs(degrees) < 0.01)
        {
            _viewport.ZoomFit();
            TxtStatus.Text = "Level: page view reset to fit.";
            return;
        }

        ApplyPageLevelRotation(_currentPage, degrees);
    }

    private void BtnPageBatchRotate_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MakeMenuItem("Rotate Selected Left", true, () => ApplySelectedPageImageOperation(PageImageOperation.RotateLeft, "Batch Rotate Left")));
        menu.Items.Add(MakeMenuItem("Rotate Selected Right", true, () => ApplySelectedPageImageOperation(PageImageOperation.RotateRight, "Batch Rotate Right")));
        menu.Items.Add(MakeMenuItem("Rotate Selected 180", true, () => ApplySelectedPageImageOperation(PageImageOperation.Rotate180, "Batch Rotate 180")));
        if (sender is Button button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
        }

        menu.IsOpen = true;
    }

    private void BtnPageCropNew_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null || _currentPage == null)
        {
            TxtStatus.Text = "Crop New Page: open a job and page first.";
            return;
        }

        SKRect crop = _viewport.GetVisiblePdfRect();
        if (crop.IsEmpty || crop.Width < 2 || crop.Height < 2)
        {
            TxtStatus.Text = "Crop New Page: zoom/pan to a visible page area first.";
            return;
        }

        try
        {
            string output = PageToolOutputPdfPath(_currentPage, "crop");
            PageImageOperationService.RenderOperationToPdf(_currentPage, PageImageOperation.Crop, output, crop);
            string parent = Path.GetDirectoryName(_currentPage.FolderPath) ?? _currentJob.PagesRoot;
            PageInfo created = OurPlanCoreJobStore.CreatePageFromPdf(
                _currentJob,
                output,
                $"{_currentPage.Name} Crop",
                parent,
                pdfPage: 0,
                scaleMetersPerPt: _currentPage.ScaleMetersPerPt);
            ReloadPagesTree(created.FolderPath);
            OpenPageInActiveTab(created);
            TxtStatus.Text = $"Crop New Page: created {created.Name}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Crop New Page", ex);
        }
    }

    private void BtnPageCopyToClipboard_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage == null)
        {
            TxtStatus.Text = "Copy Page: open a page first.";
            return;
        }

        try
        {
            string pngPath = Path.Combine(Path.GetTempPath(), $"ourplancore-page-{Guid.NewGuid():N}.png");
            PageImageOperationService.RenderPageToPng(_currentPage, pngPath);
            var files = new StringCollection { pngPath };
            var data = new DataObject();
            data.SetFileDropList(files);
            data.SetText(pngPath);
            Clipboard.SetDataObject(data, copy: true);
            TxtStatus.Text = $"Copy Page: rendered PNG copied to clipboard ({Path.GetFileName(pngPath)}).";
        }
        catch (Exception ex)
        {
            ShowOperationError("Copy Page", ex);
        }
    }

    private void BtnPageSetOrigin_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage == null)
        {
            TxtStatus.Text = "Set Origin: open a page first.";
            return;
        }

        SKRect visible = _viewport.GetVisiblePdfRect();
        var origin = new PageOriginMarker(
            (visible.Left + visible.Right) / 2f,
            (visible.Top + visible.Bottom) / 2f,
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        SavePageOrigin(_currentPage.FolderPath, origin);
        TxtStatus.Text = $"Set Origin: saved at {origin.XPt:0.#}, {origin.YPt:0.#} pt.";
    }

    private void BtnPageOffsetOrigin_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage == null)
        {
            TxtStatus.Text = "Offset Origin: open a page first.";
            return;
        }

        PageOriginMarker origin = LoadPageOrigin(_currentPage.FolderPath) ?? new PageOriginMarker(0, 0, "");
        string? raw = ShowInputDialog("Offset X,Y in PDF points:", "0, 0", "Offset Origin");
        if (raw == null)
            return;

        string[] parts = raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 ||
            !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float dx) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float dy))
        {
            PostStatusWarning("Enter two numbers, for example: 12, -6.");
            return;
        }

        var updated = new PageOriginMarker(
            origin.XPt + dx,
            origin.YPt + dy,
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        SavePageOrigin(_currentPage.FolderPath, updated);
        TxtStatus.Text = $"Offset Origin: saved at {updated.XPt:0.#}, {updated.YPt:0.#} pt.";
    }

    private void BtnPageClose_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPageTab() is { } tab)
            ClosePageTab(tab);
        else
            TxtStatus.Text = "Close Page: no page tab is open.";
    }

    private void ApplyCurrentPageImageOperation(PageImageOperation operation, string label)
    {
        if (_currentPage == null)
        {
            TxtStatus.Text = $"{label}: open a page first.";
            return;
        }

        ApplyPageImageOperationToPages([_currentPage], operation, label);
    }

    private void ApplySelectedPageImageOperation(PageImageOperation operation, string label)
    {
        IReadOnlyList<PageInfo> pages = PageToolTargetPages();
        if (pages.Count == 0)
        {
            TxtStatus.Text = $"{label}: select one or more pages first.";
            return;
        }

        ApplyPageImageOperationToPages(pages, operation, label);
    }

    private void ApplyPageImageOperationToPages(IReadOnlyList<PageInfo> pages, PageImageOperation operation, string label)
    {
        if (_currentJob == null || pages.Count == 0)
            return;

        try
        {
            SaveCurrentPageAnnotations();
            string? activeFolder = _currentPage?.FolderPath;
            foreach (PageInfo page in pages)
            {
                string output = PageToolOutputPdfPath(page, OperationSlug(operation));
                PageImageOperationResult result = PageImageOperationService.RenderOperationToPdf(page, operation, output);
                if (operation != PageImageOperation.Invert)
                    TransformPageOverlays(page.FolderPath, operation, result.OriginalWidthPt, result.OriginalHeightPt);
                OurPlanCoreJobStore.ReplacePagePdf(page.FolderPath, output);
            }

            if (!string.IsNullOrWhiteSpace(activeFolder) &&
                pages.Any(page => IsSamePageFolder(page.FolderPath, activeFolder)) &&
                OurPlanCoreJobStore.TryReadPage(activeFolder) is { } updated)
            {
                ReloadActivePageAfterPdfReplacement(updated);
            }

            RefreshPagesTakeoffIndicators();
            RefreshAllTotals();
            TxtStatus.Text = pages.Count == 1
                ? $"{label}: updated {pages[0].Name}."
                : $"{label}: updated {pages.Count} pages.";
        }
        catch (Exception ex)
        {
            ShowOperationError(label, ex);
        }
    }

    private void ApplyPageLevelRotation(PageInfo page, double degrees)
    {
        if (_currentJob == null)
            return;

        try
        {
            SaveCurrentPageAnnotations();
            string degreeSlug = degrees.ToString("0.###", CultureInfo.InvariantCulture)
                .Replace("-", "neg")
                .Replace(".", "_");
            string output = PageToolOutputPdfPath(page, $"level_{degreeSlug}");
            PageImageOperationResult result = PageImageOperationService.RenderRotationToPdf(page, degrees, output);
            TransformPageOverlays(
                page.FolderPath,
                point => PageImageOperationService.TransformRotationPoint(
                    point,
                    result.OriginalWidthPt,
                    result.OriginalHeightPt,
                    degrees));
            OurPlanCoreJobStore.ReplacePagePdf(page.FolderPath, output);
            if (OurPlanCoreJobStore.TryReadPage(page.FolderPath) is { } updated)
                ReloadActivePageAfterPdfReplacement(updated);
            RefreshPagesTakeoffIndicators();
            RefreshAllTotals();
            TxtStatus.Text = $"Level: rotated {page.Name} {degrees:0.###} degrees.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Level Page", ex);
        }
    }

    private IReadOnlyList<PageInfo> PageToolTargetPages()
    {
        if (SelectedPagesTreeAnchor() is { } anchor)
        {
            IReadOnlyList<PageInfo> selected = SelectedPagesFromPagesTree(anchor);
            if (selected.Count > 0)
                return selected;
        }

        return _currentPage != null ? [_currentPage] : [];
    }

    private string PageToolOutputPdfPath(PageInfo page, string operation)
    {
        string root = _currentJob?.RootPath ?? Path.GetDirectoryName(page.FolderPath) ?? ".";
        string folder = Path.Combine(root, "sources", "page_tools");
        Directory.CreateDirectory(folder);
        string safeName = OurPlanCoreJobStore.SanitizeName(page.Name, 80);
        string fileName = $"{safeName}_{operation}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        return OurPlanCoreJobStore.UniqueFilePath(Path.Combine(folder, fileName));
    }

    private static string OperationSlug(PageImageOperation operation) =>
        operation.ToString().ToLowerInvariant();

    private void TransformPageOverlays(string pageFolder, PageImageOperation operation, float widthPt, float heightPt)
    {
        TransformPageOverlays(
            pageFolder,
            point => PageImageOperationService.TransformPoint(point, widthPt, heightPt, operation));
    }

    private void TransformPageOverlays(string pageFolder, Func<SKPoint, SKPoint> transform)
    {
        foreach (TakeoffItem item in _takeoffItems)
        {
            bool changed = false;
            foreach (Measurement measurement in item.Measurements.Where(measurement => IsSamePageFolder(measurement.PageFolder, pageFolder)))
            {
                TransformPointList(measurement.Points, transform);
                foreach (List<SKPoint> hole in measurement.Holes)
                    TransformPointList(hole, transform);
                changed = true;
            }

            if (changed)
                QueueTakeoffAutosave(item);
        }

        List<PageAnnotation> annotations = IsSamePageFolder(_currentPage?.FolderPath ?? "", pageFolder)
            ? _viewport.GetPageAnnotations().ToList()
            : OurPlanCoreJobStore.LoadPageAnnotations(pageFolder);
        foreach (PageAnnotation annotation in annotations)
            TransformPointList(annotation.Points, transform);
        OurPlanCoreJobStore.SavePageAnnotations(pageFolder, annotations);
        TransformPageOrigin(pageFolder, transform);
    }

    private static void TransformPointList(List<SKPoint> points, Func<SKPoint, SKPoint> transform)
    {
        for (int i = 0; i < points.Count; i++)
            points[i] = transform(points[i]);
    }

    private static string PageOriginPath(string pageFolder) =>
        Path.Combine(pageFolder, "page_origin.json");

    private void ReloadActivePageAfterPdfReplacement(PageInfo updated)
    {
        PageTabState? tab = FindPageTab(updated.FolderPath) ?? _activePageTab;
        if (tab == null || !IsSamePageFolder(tab.PageFolder, updated.FolderPath))
        {
            OpenPageInActiveTab(updated);
            return;
        }

        tab.ViewState = null;
        LoadPageFromTab(tab, updated);
    }

    private static void TransformPageOrigin(string pageFolder, Func<SKPoint, SKPoint> transform)
    {
        PageOriginMarker? origin = LoadPageOrigin(pageFolder);
        if (origin == null)
            return;

        SKPoint point = transform(new SKPoint(origin.XPt, origin.YPt));
        SavePageOrigin(
            pageFolder,
            new PageOriginMarker(point.X, point.Y, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
    }

    private static bool TryParseLevelDegrees(string raw, out double degrees)
    {
        degrees = 0;
        string clean = raw.Trim().Replace(",", ".");
        return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out degrees) &&
               degrees >= -45 &&
               degrees <= 45;
    }

    private static void SavePageOrigin(string pageFolder, PageOriginMarker origin)
    {
        IoUtil.WriteAllTextAtomic(
            PageOriginPath(pageFolder),
            JsonSerializer.Serialize(origin, OurPlanCoreJobStore.JsonOptions));
    }

    private static PageOriginMarker? LoadPageOrigin(string pageFolder)
    {
        string path = PageOriginPath(pageFolder);
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<PageOriginMarker>(File.ReadAllText(path), OurPlanCoreJobStore.JsonOptions);
    }
}
