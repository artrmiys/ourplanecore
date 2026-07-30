using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    private void AddCurrentSheetOverlay(PageInfo overlayPage)
    {
        if (!RequireModule(ModuleId.SheetOverlay, "Add Sheet Overlay"))
            return;
        if (_currentPage == null)
        {
            TxtStatus.Text = "Open a sheet before adding an overlay.";
            return;
        }
        if (SameFolder(_currentPage.FolderPath, overlayPage.FolderPath))
        {
            TxtStatus.Text = "A sheet cannot overlay itself.";
            return;
        }

        OurPlanCoreJobStore.AddPageOverlay(
            _currentPage.FolderPath,
            overlayPage.FolderPath,
            CurrentSheetOverlayColor(),
            DefaultSheetOverlayOpacity);
        ReloadCurrentSheetOverlay($"Overlay layer added: {overlayPage.Name}");
    }

    private void ActivateSheetOverlayLayer(
        PageInfo page,
        string overlayId,
        bool showProperties)
    {
        PageInfo latest = ReadLatestSheetOverlayPage(page);
        if (!latest.OverlayLayers.Any(layer =>
                string.Equals(layer.Id, overlayId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (!string.Equals(
                latest.ActiveOverlayId,
                overlayId,
                StringComparison.OrdinalIgnoreCase) &&
            !IsCurrentJobReadOnly)
        {
            OurPlanCoreJobStore.SetActivePageOverlay(latest.FolderPath, overlayId);
            latest = ReadLatestSheetOverlayPage(latest);
            if (_currentPage != null && SameFolder(_currentPage.FolderPath, latest.FolderPath))
            {
                _currentPage = latest;
                LoadSheetOverlay(latest);
            }
            RefreshPageOverlayTreeNode(latest);
        }

        if (showProperties)
            ShowSheetOverlayProperties(latest);
    }

    private void OpenSheetOverlaySource(
        PageInfo page,
        SheetOverlayLayerInfo layer)
    {
        PageInfo? overlayPage = OurPlanCoreJobStore.TryReadPage(layer.SourcePageFolder);
        if (overlayPage == null)
        {
            TxtStatus.Text = "Overlay sheet source is missing.";
            return;
        }

        OpenPageInActiveTab(overlayPage);
        TxtStatus.Text = $"Opened overlay sheet: {overlayPage.Name}.";
    }

    private void MoveSheetOverlayLayer(PageInfo page, string overlayId, int delta)
    {
        OurPlanCoreJobStore.MovePageOverlayLayer(page.FolderPath, overlayId, delta);
        RefreshPageOverlayState(
            page.FolderPath,
            delta > 0 ? "Overlay layer moved up." : "Overlay layer moved down.");
    }

    private void RemoveSheetOverlayLayer(PageInfo page, string overlayId)
    {
        OurPlanCoreJobStore.RemovePageOverlay(page.FolderPath, overlayId);
        RefreshPageOverlayState(page.FolderPath, "Overlay layer removed.");
    }

    private void SetPageSheetOverlayOpacity(PageInfo page, double opacity)
    {
        if (string.IsNullOrWhiteSpace(page.ActiveOverlayId))
            return;

        OurPlanCoreJobStore.SavePageOverlayOpacity(
            page.FolderPath,
            page.ActiveOverlayId,
            opacity);
        RefreshPageOverlayState(
            page.FolderPath,
            $"Overlay opacity: {opacity * 100:0}%.");
    }

    private bool TryBuildSheetOverlayLayers(
        PageInfo page,
        float renderScale,
        bool allowRender,
        out IReadOnlyList<SheetOverlayBitmapLayer> layers,
        out string error)
    {
        var built = new List<SheetOverlayBitmapLayer>();
        foreach (SheetOverlayLayerInfo layer in page.OverlayLayers.Where(item => item.IsVisible))
        {
            PageInfo layerPage = BuildSheetOverlayLayerPage(page, layer);
            if (!TryBuildSheetOverlayBitmap(
                    layerPage,
                    renderScale,
                    allowRender,
                    out SKBitmap? bitmap,
                    out float widthPt,
                    out float heightPt,
                    out string overlayName,
                    out error) ||
                bitmap == null)
            {
                DisposeSheetOverlayBitmapLayers(built);
                layers = [];
                return false;
            }

            PageInfo? sourcePage = OurPlanCoreJobStore.TryReadPage(layer.SourcePageFolder);
            built.Add(new SheetOverlayBitmapLayer(
                layer.Id,
                layer.SourcePageFolder,
                overlayName,
                bitmap,
                widthPt,
                heightPt,
                (float)layer.OffsetXPt,
                (float)layer.OffsetYPt,
                (float)layer.Scale,
                (float)layer.RotationDegrees,
                (float)layer.Opacity,
                renderScale,
                sourcePage?.PdfPath ?? "",
                sourcePage?.PdfPage ?? 0,
                OverlaySnapLayers(sourcePage) ?? []));
        }

        layers = built;
        error = "";
        return true;
    }

    private static PageInfo BuildSheetOverlayLayerPage(
        PageInfo targetPage,
        SheetOverlayLayerInfo layer) =>
        new()
        {
            Name = targetPage.Name,
            FolderPath = targetPage.FolderPath,
            PdfPath = targetPage.PdfPath,
            PdfPage = targetPage.PdfPage,
            ScaleMetersPerPt = targetPage.ScaleMetersPerPt,
            OverlayPageFolder = layer.SourcePageFolder,
            OverlayVisible = layer.IsVisible,
            OverlayColor = layer.Color,
            OverlayOpacity = layer.Opacity,
            OverlayOffsetXPt = layer.OffsetXPt,
            OverlayOffsetYPt = layer.OffsetYPt,
            OverlayScale = layer.Scale,
            OverlayRotationDegrees = layer.RotationDegrees,
        };

    private static void DisposeSheetOverlayBitmapLayers(
        IEnumerable<SheetOverlayBitmapLayer> layers)
    {
        foreach (SheetOverlayBitmapLayer layer in layers)
            layer.Bitmap.Dispose();
    }

    private static string SheetOverlayRevisionKey(PageInfo page) =>
        string.Join(
            "|",
            page.ActiveOverlayId,
            string.Join(
                ";",
                page.OverlayLayers.Select(layer =>
                    string.Join(
                        ",",
                        layer.Id,
                        layer.SourcePageFolder,
                        layer.IsVisible,
                        layer.Color,
                        layer.Opacity.ToString("R", CultureInfo.InvariantCulture),
                        layer.OffsetXPt.ToString("R", CultureInfo.InvariantCulture),
                        layer.OffsetYPt.ToString("R", CultureInfo.InvariantCulture),
                        layer.Scale.ToString("R", CultureInfo.InvariantCulture),
                        layer.RotationDegrees.ToString("R", CultureInfo.InvariantCulture)))));

    private sealed record SheetOverlayLayersBuildResult(
        bool Ok,
        IReadOnlyList<SheetOverlayBitmapLayer> Layers,
        string Error);
}
