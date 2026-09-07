using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private readonly Dictionary<DetachedSheetWindow, int> _detachedSheetOverlayLoadVersions = [];

    private void QueueDetachedSheetOverlays(
        DetachedSheetWindow window,
        PageInfo page,
        float? requestedRenderScale = null)
    {
        int version = _detachedSheetOverlayLoadVersions.TryGetValue(window, out int current)
            ? current + 1
            : 1;
        _detachedSheetOverlayLoadVersions[window] = version;

        if (!IsModuleEnabled(ModuleId.SheetOverlay) ||
            page.OverlayLayers.Count == 0 ||
            !page.OverlayLayers.Any(layer => layer.IsVisible))
        {
            window.Viewport.SetSheetOverlaySelectionActive(false);
            window.Viewport.ClearSheetOverlay();
            return;
        }

        float zoom = requestedRenderScale is > 0
            ? requestedRenderScale.Value
            : Math.Max(1f, window.Viewport.CaptureViewState().Zoom);
        float renderScale = SelectSheetOverlayViewportRenderScale(
            page,
            requestedRenderScale: zoom);
        _ = LoadDetachedSheetOverlaysAsync(window, page, version, renderScale);
    }

    private async Task LoadDetachedSheetOverlaysAsync(
        DetachedSheetWindow window,
        PageInfo page,
        int version,
        float renderScale)
    {
        SheetOverlayLayersBuildResult result = await Task.Run(() =>
        {
            bool ok = TryBuildSheetOverlayLayers(
                page,
                renderScale,
                allowRender: true,
                out IReadOnlyList<SheetOverlayBitmapLayer> layers,
                out string error);
            return new SheetOverlayLayersBuildResult(ok, layers, error);
        });

        bool staleWindow =
            !_detachedSheetOverlayLoadVersions.TryGetValue(window, out int latestVersion) ||
            latestVersion != version ||
            !_detachedSheetWindows.Contains(window) ||
            !IsSamePageFolder(window.Page.FolderPath, page.FolderPath);
        if (staleWindow)
        {
            DisposeSheetOverlayBitmapLayers(result.Layers);
            return;
        }

        PageInfo latest = OurPlanCoreJobStore.TryReadPage(page.FolderPath) ?? page;
        if (!string.Equals(
                SheetOverlayRevisionKey(latest),
                SheetOverlayRevisionKey(page),
                StringComparison.Ordinal))
        {
            DisposeSheetOverlayBitmapLayers(result.Layers);
            QueueDetachedSheetOverlays(window, latest);
            return;
        }

        if (!result.Ok)
        {
            DisposeSheetOverlayBitmapLayers(result.Layers);
            window.Viewport.ClearSheetOverlay();
            TxtStatus.Text = $"{page.Name}: detached overlay unavailable: {result.Error}";
            return;
        }

        window.Viewport.SetSheetOverlaySelectionActive(false);
        window.Viewport.SetSheetOverlayLayers(
            result.Layers,
            activeOverlayId: page.ActiveOverlayId,
            targetPageFolder: page.FolderPath);
    }

    private void RefreshDetachedSheetOverlaysForPage(string pageFolder)
    {
        if (_detachedSheetWindows.Count == 0)
            return;

        PageInfo? page = OurPlanCoreJobStore.TryReadPage(pageFolder);
        if (page == null)
            return;
        foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
        {
            if (IsSamePageFolder(window.Page.FolderPath, pageFolder))
                QueueDetachedSheetOverlays(window, page);
        }
    }

    private void ForgetDetachedSheetOverlay(DetachedSheetWindow window)
    {
        _detachedSheetOverlayLoadVersions.Remove(window);
        window.Viewport.ClearSheetOverlay();
    }
}
