using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    private const string DefaultSheetOverlayColor = "#E53935";
    private const double DefaultSheetOverlayOpacity = 0.55;
    private readonly SheetOverlayBitmapCache _sheetOverlayBitmapCache = new(maxEntries: 8);
    private int _sheetOverlayLoadVersion;

    private static readonly IReadOnlyList<(string Label, string Hex)> SheetOverlayColors =
    [
        ("Red", "#E53935"),
        ("Blue", "#1E88E5"),
        ("Green", "#43A047"),
        ("Orange", "#FB8C00"),
        ("Magenta", "#D81B60"),
        ("Gray", "#546E7A"),
    ];

    private MenuItem BuildSheetOverlayMenu(PageInfo candidatePage)
    {
        bool hasCurrentPage = _currentPage != null;
        bool canSetOverlay = hasCurrentPage &&
                             !SameFolder(_currentPage!.FolderPath, candidatePage.FolderPath);
        bool hasOverlay = hasCurrentPage &&
                          !string.IsNullOrWhiteSpace(_currentPage!.OverlayPageFolder);

        var menu = new MenuItem { Header = "Sheet Overlay" };
        menu.Items.Add(MakeMenuItem("Use This Sheet as Overlay", canSetOverlay, () => SetCurrentSheetOverlay(candidatePage)));
        menu.Items.Add(MakeMenuItem("Clear Current Sheet Overlay", hasOverlay, ClearCurrentSheetOverlay));
        menu.Items.Add(MakeMenuItem(
            "Edit Overlay by Points",
            !string.IsNullOrWhiteSpace(candidatePage.OverlayPageFolder),
            () => BeginSheetOverlayPointEdit(candidatePage)));
        menu.Items.Add(new Separator());

        var colorMenu = new MenuItem { Header = "Color" };
        foreach ((string label, string hex) in SheetOverlayColors)
            colorMenu.Items.Add(MakeSheetOverlayColorMenuItem(label, hex, hasOverlay));
        menu.Items.Add(colorMenu);

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
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Edit Overlay by Points", true, () => BeginSheetOverlayPointEdit(node.Page)));
        menu.Items.Add(MakeMenuItem("Edit Transform...", true, () => EditSheetOverlayTransform(node.Page)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Move Left 6 pt", true, () => NudgeSheetOverlay(node.Page, -6, 0)));
        menu.Items.Add(MakeMenuItem("Move Right 6 pt", true, () => NudgeSheetOverlay(node.Page, 6, 0)));
        menu.Items.Add(MakeMenuItem("Move Up 6 pt", true, () => NudgeSheetOverlay(node.Page, 0, -6)));
        menu.Items.Add(MakeMenuItem("Move Down 6 pt", true, () => NudgeSheetOverlay(node.Page, 0, 6)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Scale Up 5%", true, () => ScaleSheetOverlay(node.Page, 1.05)));
        menu.Items.Add(MakeMenuItem("Scale Down 5%", true, () => ScaleSheetOverlay(node.Page, 1 / 1.05)));
        menu.Items.Add(MakeMenuItem("Reset Transform", true, () => SetSheetOverlayTransform(node.Page, 0, 0, 1, "Overlay transform reset.")));
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

    private MenuItem MakePageOverlayColorMenuItem(PageInfo page, string label, string hex)
    {
        var item = MakeMenuItem(label, true, () => SetPageSheetOverlayColor(page, hex));
        item.IsCheckable = true;
        item.IsChecked = string.Equals(page.OverlayColor, hex, StringComparison.OrdinalIgnoreCase);
        return item;
    }

    private MenuItem MakeSheetOverlayColorMenuItem(string label, string hex, bool isEnabled)
    {
        var item = MakeMenuItem(label, isEnabled, () => SetCurrentSheetOverlayColor(hex));
        item.IsCheckable = true;
        item.IsChecked = _currentPage != null &&
                         string.Equals(CurrentSheetOverlayColor(), hex, StringComparison.OrdinalIgnoreCase);
        return item;
    }

    private void SetCurrentSheetOverlay(PageInfo overlayPage)
    {
        if (_currentPage == null)
        {
            TxtStatus.Text = "Open a sheet before setting an overlay.";
            return;
        }

        if (SameFolder(_currentPage.FolderPath, overlayPage.FolderPath))
        {
            TxtStatus.Text = "A sheet cannot overlay itself.";
            return;
        }

        OurPlaneCoreJobStore.SavePageOverlay(
            _currentPage.FolderPath,
            overlayPage.FolderPath,
            CurrentSheetOverlayColor(),
            CurrentSheetOverlayOpacity());
        OurPlaneCoreJobStore.SavePageOverlayVisibility(_currentPage.FolderPath, true);

        ReloadCurrentSheetOverlay($"Overlay set: {overlayPage.Name}");
    }

    private void ClearCurrentSheetOverlay()
    {
        if (_currentPage == null)
            return;

        OurPlaneCoreJobStore.ClearPageOverlay(_currentPage.FolderPath);
        if (OurPlaneCoreJobStore.TryReadPage(_currentPage.FolderPath) is { } updated)
            _currentPage = updated;
        _viewport.ClearSheetOverlay();
        RefreshPageOverlayTreeNode(_currentPage);
        TxtStatus.Text = "Sheet overlay cleared.";
    }

    private void SetCurrentSheetOverlayColor(string color)
    {
        if (_currentPage == null || string.IsNullOrWhiteSpace(_currentPage.OverlayPageFolder))
        {
            TxtStatus.Text = "Set a sheet overlay before choosing overlay color.";
            return;
        }

        OurPlaneCoreJobStore.SavePageOverlay(
            _currentPage.FolderPath,
            _currentPage.OverlayPageFolder,
            color,
            CurrentSheetOverlayOpacity());

        ReloadCurrentSheetOverlay($"Overlay color: {color}");
    }

    private void SetPageSheetOverlayColor(PageInfo page, string color)
    {
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder))
            return;

        OurPlaneCoreJobStore.SavePageOverlay(
            page.FolderPath,
            page.OverlayPageFolder,
            color,
            page.OverlayOpacity);
        RefreshPageOverlayState(page.FolderPath, $"Overlay color: {color}");
    }

    private void NudgeSheetOverlay(PageInfo page, double dxPt, double dyPt) =>
        SetSheetOverlayTransform(
            page,
            page.OverlayOffsetXPt + dxPt,
            page.OverlayOffsetYPt + dyPt,
            page.OverlayScale,
            $"Overlay moved: X {FormatOverlayNumber(page.OverlayOffsetXPt + dxPt)}, Y {FormatOverlayNumber(page.OverlayOffsetYPt + dyPt)}.");

    private void ScaleSheetOverlay(PageInfo page, double factor) =>
        SetSheetOverlayTransform(
            page,
            page.OverlayOffsetXPt,
            page.OverlayOffsetYPt,
            page.OverlayScale * factor,
            $"Overlay scale: {FormatOverlayNumber(page.OverlayScale * factor)}x.");

    private void EditSheetOverlayTransform(PageInfo page)
    {
        if (!ShowSheetOverlayTransformDialog(page, out double xPt, out double yPt, out double scale))
            return;

        SetSheetOverlayTransform(page, xPt, yPt, scale, "Overlay transform updated.");
    }

    private void SetSheetOverlayTransform(
        PageInfo page,
        double offsetXPt,
        double offsetYPt,
        double overlayScale,
        string status)
    {
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder))
            return;

        OurPlaneCoreJobStore.SavePageOverlayTransform(page.FolderPath, offsetXPt, offsetYPt, overlayScale);
        RefreshPageOverlayState(page.FolderPath, status);
    }

    private void BeginSheetOverlayPointEdit(PageInfo page)
    {
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder))
        {
            TxtStatus.Text = "Set a sheet overlay before editing it.";
            return;
        }

        if (_currentPage == null || !SameFolder(_currentPage.FolderPath, page.FolderPath))
            OpenPageInActiveTab(page);

        if (_currentPage == null || !SameFolder(_currentPage.FolderPath, page.FolderPath))
        {
            TxtStatus.Text = "Open the sheet before editing its overlay.";
            return;
        }

        _viewport.BeginSheetOverlayPointEdit();
    }

    private void OnSheetOverlayTransformChanged(SheetOverlayTransformChange change)
    {
        if (_currentPage == null || string.IsNullOrWhiteSpace(_currentPage.OverlayPageFolder))
            return;

        OurPlaneCoreJobStore.SavePageOverlayTransform(
            _currentPage.FolderPath,
            change.OffsetXPt,
            change.OffsetYPt,
            change.OverlayScale);

        if (OurPlaneCoreJobStore.TryReadPage(_currentPage.FolderPath) is { } updated)
        {
            _currentPage = updated;
            RefreshPageOverlayTreeNode(updated);
        }

        TxtStatus.Text = change.Status;
    }

    private void ClearPageOverlay(PageInfo page)
    {
        OurPlaneCoreJobStore.ClearPageOverlay(page.FolderPath);
        if (_currentPage != null && SameFolder(_currentPage.FolderPath, page.FolderPath))
        {
            if (OurPlaneCoreJobStore.TryReadPage(page.FolderPath) is { } updated)
                _currentPage = updated;
            _viewport.ClearSheetOverlay();
        }

        if (OurPlaneCoreJobStore.TryReadPage(page.FolderPath) is { } refreshed)
            RefreshPageOverlayTreeNode(refreshed);
        else
            RefreshPagesTakeoffIndicators();
        TxtStatus.Text = "Sheet overlay cleared.";
    }

    private void RefreshPageOverlayState(string pageFolder, string status)
    {
        PageInfo? updated = OurPlaneCoreJobStore.TryReadPage(pageFolder);
        if (updated == null)
            return;

        if (_currentPage != null && SameFolder(_currentPage.FolderPath, pageFolder))
        {
            _currentPage = updated;
            LoadSheetOverlay(updated);
        }

        RefreshPageOverlayTreeNode(updated);
        TxtStatus.Text = status;
    }

    private void RefreshPageOverlayTreeNode(PageInfo page)
    {
        if (FindPageTreeItemByFolder(page.FolderPath) is not { } item)
        {
            RefreshPagesTakeoffIndicators();
            return;
        }

        bool wasExpanded = item.IsExpanded;
        item.Tag = page;
        item.Header = BuildPageHeader(page);
        RebuildPageTakeoffNodes(item, page);
        item.IsExpanded = wasExpanded;
        ApplyPagesMultiSelectionVisuals();
        ApplyPagesTreeSearchFilter();
    }

    private void ReloadCurrentSheetOverlay(string status)
    {
        if (_currentPage == null)
            return;

        if (OurPlaneCoreJobStore.TryReadPage(_currentPage.FolderPath) is { } updated)
            _currentPage = updated;

        LoadSheetOverlay(_currentPage);
        RefreshPageOverlayTreeNode(_currentPage);
        TxtStatus.Text = status;
    }

    private void LoadSheetOverlay(PageInfo page)
    {
        int version = ++_sheetOverlayLoadVersion;
        _viewport.ClearSheetOverlay();
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder) || !page.OverlayVisible)
            return;

        if (!TryBuildSheetOverlayBitmap(
                page,
                ViewportRenderPolicy.SheetOverlayViewportRenderScale,
                allowRender: false,
                out SKBitmap? bitmap,
                out float widthPt,
                out float heightPt,
                out string overlayName,
                out string error) ||
            bitmap == null)
        {
            _ = LoadSheetOverlayAsync(page, version);
            return;
        }

        ApplySheetOverlayBitmapToViewport(page, bitmap, widthPt, heightPt, overlayName);
    }

    private void TryApplyCachedSheetOverlay(PageInfo page)
    {
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder) || !page.OverlayVisible)
            return;

        if (!TryBuildSheetOverlayBitmap(
                page,
                ViewportRenderPolicy.SheetOverlayViewportRenderScale,
                allowRender: false,
                out SKBitmap? bitmap,
                out float widthPt,
                out float heightPt,
                out string overlayName,
                out _) ||
            bitmap == null)
        {
            return;
        }

        ApplySheetOverlayBitmapToViewport(page, bitmap, widthPt, heightPt, overlayName);
    }

    private async Task LoadSheetOverlayAsync(PageInfo page, int version)
    {
        SheetOverlayBuildResult result = await Task.Run(() =>
        {
            bool ok = TryBuildSheetOverlayBitmap(
                page,
                ViewportRenderPolicy.SheetOverlayViewportRenderScale,
                allowRender: true,
                out SKBitmap? bitmap,
                out float widthPt,
                out float heightPt,
                out string overlayName,
                out string error);
            return new SheetOverlayBuildResult(ok, bitmap, widthPt, heightPt, overlayName, error);
        });

        if (version != _sheetOverlayLoadVersion ||
            _currentPage == null ||
            !SameFolder(_currentPage.FolderPath, page.FolderPath))
        {
            result.Bitmap?.Dispose();
            return;
        }

        if (!result.Ok || result.Bitmap == null)
        {
            TxtStatus.Text = $"Sheet overlay unavailable: {result.Error}";
            return;
        }

        PageInfo target = _currentPage;
        ApplySheetOverlayBitmapToViewport(
            target,
            result.Bitmap,
            result.WidthPt,
            result.HeightPt,
            result.OverlayName);
    }

    private void ApplySheetOverlayBitmapToViewport(
        PageInfo page,
        SKBitmap bitmap,
        float widthPt,
        float heightPt,
        string overlayName)
    {
        PageInfo? overlayPage = OurPlaneCoreJobStore.TryReadPage(page.OverlayPageFolder);
        _viewport.SetSheetOverlay(
            bitmap,
            widthPt,
            heightPt,
            overlayName,
            (float)page.OverlayOffsetXPt,
            (float)page.OverlayOffsetYPt,
            (float)page.OverlayScale,
            overlayPage?.PdfPath ?? "",
            overlayPage?.PdfPage ?? 0,
            OverlaySnapLayers(overlayPage));
    }

    private static IReadOnlyList<PdfLayerInfo>? OverlaySnapLayers(PageInfo? overlayPage) =>
        overlayPage is { PdfLayersCached: true, PdfLayers.Count: > 0 }
            ? overlayPage.PdfLayers
            : null;

    private (bool Ok, string Error) DrawPdfExportSheetOverlay(
        SKCanvas canvas,
        PageInfo page,
        float pageWidthPt,
        float pageHeightPt)
    {
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder) || !page.OverlayVisible)
            return (true, "");

        SKBitmap? bitmap = null;
        try
        {
            if (!TryBuildSheetOverlayBitmap(
                    page,
                    ViewportRenderPolicy.SheetOverlayExportRenderScale,
                    allowRender: true,
                    out bitmap,
                    out float overlayWidthPt,
                    out float overlayHeightPt,
                    out string overlayName,
                    out string error) ||
                bitmap == null)
            {
                return (false, $"Could not render overlay for sheet '{page.Name}': {error}");
            }

            using var paint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.Medium,
            };
            var dest = new SKRect(
                (float)page.OverlayOffsetXPt,
                (float)page.OverlayOffsetYPt,
                (float)(page.OverlayOffsetXPt + overlayWidthPt * page.OverlayScale),
                (float)(page.OverlayOffsetYPt + overlayHeightPt * page.OverlayScale));
            canvas.DrawBitmap(bitmap, dest, paint);
            return (true, "");
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private bool TryBuildSheetOverlayBitmap(
        PageInfo page,
        float renderScale,
        bool allowRender,
        out SKBitmap? overlayBitmap,
        out float widthPt,
        out float heightPt,
        out string overlayName,
        out string error)
    {
        overlayBitmap = null;
        widthPt = 0;
        heightPt = 0;
        overlayName = "";
        error = "";

        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder))
            return false;

        if (!Directory.Exists(page.OverlayPageFolder))
        {
            error = "overlay sheet folder is missing.";
            return false;
        }

        PageInfo? overlayPage = OurPlaneCoreJobStore.TryReadPage(page.OverlayPageFolder);
        if (overlayPage == null)
        {
            error = "overlay sheet source is missing.";
            return false;
        }

        string cacheKey = BuildSheetOverlayCacheKey(page, overlayPage, renderScale);
        if (_sheetOverlayBitmapCache.TryGet(cacheKey, out SheetOverlayBitmapCache.Entry? cached) &&
            cached != null)
        {
            overlayBitmap = cached.Bitmap;
            widthPt = cached.WidthPt;
            heightPt = cached.HeightPt;
            overlayName = cached.OverlayName;
            return true;
        }

        if (SheetOverlayRenderCache.TryRead(
                page,
                overlayPage,
                renderScale,
                out SKBitmap? persisted,
                out widthPt,
                out heightPt) &&
            persisted != null)
        {
            overlayBitmap = persisted;
            overlayName = overlayPage.Name;
            _sheetOverlayBitmapCache.Put(cacheKey, overlayBitmap, widthPt, heightPt, overlayName);
            AppLog.Info(
                $"Sheet overlay cache hit; base='{page.FolderPath}'; overlay='{overlayPage.FolderPath}'; scale={renderScale:0.###}");
            return true;
        }

        if (!allowRender)
        {
            error = "overlay is not cached yet.";
            return false;
        }

        var layerStates = overlayPage.PdfLayers
            .GroupBy(layer => layer.Number)
            .ToDictionary(group => group.Key, group => group.First().IsOn);

        Stopwatch renderWatch = Stopwatch.StartNew();
        if (!PdfLayerRenderService.TryRender(
                overlayPage.PdfPath,
                overlayPage.PdfPage,
                renderScale: renderScale,
                layerStates,
                highlightedLayers: [],
                overlayPage.PdfLayersCached ? overlayPage.PdfLayers : null,
                out PdfLayerRenderResult render,
                out error))
        {
            return false;
        }
        renderWatch.Stop();
        if (renderWatch.ElapsedMilliseconds >= ViewportRenderPolicy.SlowRenderLogMs)
        {
            AppLog.Info(
                $"Sheet overlay render {renderWatch.ElapsedMilliseconds}ms; base='{page.FolderPath}'; " +
                $"overlay='{overlayPage.FolderPath}'; scale={renderScale:0.###}");
        }

        using SKBitmap? sourceBitmap = SKBitmap.Decode(render.ImageBytes);
        if (sourceBitmap == null)
        {
            error = "overlay raster could not be decoded.";
            return false;
        }

        overlayBitmap = BuildTintedSheetOverlayBitmap(sourceBitmap, page.OverlayColor, page.OverlayOpacity);
        widthPt = render.WidthPt;
        heightPt = render.HeightPt;
        overlayName = overlayPage.Name;
        _sheetOverlayBitmapCache.Put(cacheKey, overlayBitmap, widthPt, heightPt, overlayName);
        SheetOverlayRenderCache.TryWrite(page, overlayPage, renderScale, overlayBitmap, widthPt, heightPt);
        return true;
    }

    private sealed record SheetOverlayBuildResult(
        bool Ok,
        SKBitmap? Bitmap,
        float WidthPt,
        float HeightPt,
        string OverlayName,
        string Error);

    private static string BuildSheetOverlayCacheKey(PageInfo page, PageInfo overlayPage, float renderScale)
    {
        var info = new FileInfo(overlayPage.PdfPath);
        return string.Join(
            '|',
            overlayPage.FolderPath.ToLowerInvariant(),
            info.Exists ? info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "0",
            info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) : "0",
            overlayPage.PdfPage.ToString(CultureInfo.InvariantCulture),
            Math.Round(renderScale, 3).ToString(CultureInfo.InvariantCulture),
            page.OverlayColor,
            page.OverlayOpacity.ToString("0.###", CultureInfo.InvariantCulture),
            string.Join(';', overlayPage.PdfLayers
                .OrderBy(layer => layer.Number)
                .Select(layer => $"{layer.Number}:{layer.IsOn}:{layer.Name}")));
    }

    private void TogglePageOverlayVisibility(PageInfo page)
    {
        bool visible = !page.OverlayVisible;
        OurPlaneCoreJobStore.SavePageOverlayVisibility(page.FolderPath, visible);
        PageInfo updated = OurPlaneCoreJobStore.TryReadPage(page.FolderPath) ?? page;
        if (_currentPage != null && SameFolder(_currentPage.FolderPath, page.FolderPath))
        {
            _currentPage = updated;
            LoadSheetOverlay(updated);
        }

        RefreshPageOverlayTreeNode(updated);
        TxtStatus.Text = visible
            ? $"Sheet overlay shown on {updated.Name}."
            : $"Sheet overlay hidden on {updated.Name}.";
    }

    private bool ShowSheetOverlayTransformDialog(
        PageInfo page,
        out double offsetXPt,
        out double offsetYPt,
        out double overlayScale)
    {
        offsetXPt = page.OverlayOffsetXPt;
        offsetYPt = page.OverlayOffsetYPt;
        overlayScale = page.OverlayScale;

        var dialog = new Window
        {
            Title = "Sheet Overlay Transform",
            Owner = this,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Overlay: {OverlayPageName(page)}",
            Margin = new Thickness(0, 0, 0, 10),
        });

        AddLabeledTextBox(panel, "X offset (pt):", FormatOverlayNumber(page.OverlayOffsetXPt), out TextBox xBox);
        AddLabeledTextBox(panel, "Y offset (pt):", FormatOverlayNumber(page.OverlayOffsetYPt), out TextBox yBox);
        AddLabeledTextBox(panel, "Scale:", FormatOverlayNumber(page.OverlayScale), out TextBox scaleBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "OK", Width = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 78, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        double resultX = offsetXPt;
        double resultY = offsetYPt;
        double resultScale = overlayScale;
        ok.Click += (_, _) =>
        {
            if (!TryParseOverlayNumber(xBox.Text, out resultX) ||
                !TryParseOverlayNumber(yBox.Text, out resultY) ||
                !TryParseOverlayNumber(scaleBox.Text, out resultScale) ||
                resultScale <= 0)
            {
                MessageBox.Show("Enter numeric X, Y, and positive Scale values.", "Sheet Overlay Transform",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) => { xBox.Focus(); xBox.SelectAll(); };

        if (dialog.ShowDialog() != true)
            return false;

        offsetXPt = resultX;
        offsetYPt = resultY;
        overlayScale = resultScale;
        return true;
    }

    private static void AddLabeledTextBox(Panel panel, string label, string value, out TextBox box)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        box = new TextBox
        {
            Text = value,
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(box);
    }

    private static bool TryParseOverlayNumber(string value, out double result) =>
        double.TryParse(
            (value ?? "").Replace(",", ".", StringComparison.Ordinal),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);

    private static string FormatOverlayNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string OverlayPageName(PageInfo page)
    {
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder))
            return "none";

        return OurPlaneCoreJobStore.TryReadPage(page.OverlayPageFolder)?.Name
            ?? OurPlaneCoreJobStore.DisplayName(page.OverlayPageFolder);
    }

    private static SKBitmap BuildTintedSheetOverlayBitmap(SKBitmap source, string colorHex, double opacity)
    {
        SKColor color = ParseOverlayColor(colorHex);
        double alphaScale = Math.Clamp(opacity, 0.05, 1.0);
        var tinted = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        tinted.Erase(SKColors.Transparent);

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                SKColor pixel = source.GetPixel(x, y);
                int whiteDistance = Math.Max(
                    Math.Max(255 - pixel.Red, 255 - pixel.Green),
                    255 - pixel.Blue);
                int alpha = (int)Math.Round(whiteDistance * alphaScale * (pixel.Alpha / 255.0) * 1.15);
                alpha = Math.Clamp(alpha, 0, 235);
                if (alpha < 8)
                    continue;

                tinted.SetPixel(x, y, new SKColor(color.Red, color.Green, color.Blue, (byte)alpha));
            }
        }

        return tinted;
    }

    private string CurrentSheetOverlayColor() =>
        string.IsNullOrWhiteSpace(_currentPage?.OverlayColor)
            ? DefaultSheetOverlayColor
            : _currentPage!.OverlayColor;

    private double CurrentSheetOverlayOpacity()
    {
        double opacity = _currentPage?.OverlayOpacity ?? DefaultSheetOverlayOpacity;
        return double.IsNaN(opacity) || double.IsInfinity(opacity) || opacity <= 0
            ? DefaultSheetOverlayOpacity
            : Math.Clamp(opacity, 0.05, 1.0);
    }

    private static SKColor ParseOverlayColor(string colorHex)
    {
        try
        {
            return SKColor.Parse(string.IsNullOrWhiteSpace(colorHex) ? DefaultSheetOverlayColor : colorHex);
        }
        catch
        {
            return SKColor.Parse(DefaultSheetOverlayColor);
        }
    }

    private static bool SameFolder(string left, string right)
    {
        try
        {
            string l = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string r = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class SheetOverlayBitmapCache
    {
        private readonly int _maxEntries;
        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _entries = [];
        private long _clock;

        public SheetOverlayBitmapCache(int maxEntries)
        {
            _maxEntries = Math.Max(1, maxEntries);
        }

        public bool TryGet(string key, out Entry? entry)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out Entry? cached))
                {
                    cached.LastUsed = ++_clock;
                    entry = new Entry(
                        cached.Bitmap.Copy(),
                        cached.WidthPt,
                        cached.HeightPt,
                        cached.OverlayName,
                        cached.LastUsed);
                    return true;
                }
            }

            entry = null;
            return false;
        }

        public void Put(string key, SKBitmap bitmap, float widthPt, float heightPt, string overlayName)
        {
            SKBitmap copy = bitmap.Copy();
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out Entry? existing))
                {
                    existing.Bitmap.Dispose();
                    existing.Bitmap = copy;
                    existing.WidthPt = widthPt;
                    existing.HeightPt = heightPt;
                    existing.OverlayName = overlayName;
                    existing.LastUsed = ++_clock;
                    return;
                }

                _entries[key] = new Entry(copy, widthPt, heightPt, overlayName, ++_clock);
                Trim();
            }
        }

        private void Trim()
        {
            while (_entries.Count > _maxEntries)
            {
                string oldestKey = "";
                long oldest = long.MaxValue;
                foreach (var pair in _entries)
                {
                    if (pair.Value.LastUsed >= oldest)
                        continue;

                    oldest = pair.Value.LastUsed;
                    oldestKey = pair.Key;
                }

                if (string.IsNullOrWhiteSpace(oldestKey))
                    return;

                _entries[oldestKey].Bitmap.Dispose();
                _entries.Remove(oldestKey);
            }
        }

        public sealed class Entry
        {
            public Entry(SKBitmap bitmap, float widthPt, float heightPt, string overlayName, long lastUsed)
            {
                Bitmap = bitmap;
                WidthPt = widthPt;
                HeightPt = heightPt;
                OverlayName = overlayName;
                LastUsed = lastUsed;
            }

            public SKBitmap Bitmap { get; set; }
            public float WidthPt { get; set; }
            public float HeightPt { get; set; }
            public string OverlayName { get; set; }
            public long LastUsed { get; set; }
        }
    }
}
