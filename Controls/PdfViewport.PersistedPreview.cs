using System;
using System.IO;
using System.Threading.Tasks;
using OurPlaneCore;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private bool QueuePersistedPreviewRenderAfterFirstRepaint(
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        float renderScale,
        ViewState? restoreView,
        bool fitAfter,
        bool queueSharpBaseAfterPreview,
        string? statusAfter)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || pdfIndex < 0)
            return false;

        int version = ++_persistedPreviewRenderVersion;
        _ = StartPersistedPreviewRenderAfterFirstRepaintAsync(
            version,
            pdfPath,
            pdfIndex,
            pageFolder,
            renderScale,
            restoreView,
            fitAfter,
            queueSharpBaseAfterPreview,
            statusAfter);
        return true;
    }

    private async Task StartPersistedPreviewRenderAfterFirstRepaintAsync(
        int version,
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        float renderScale,
        ViewState? restoreView,
        bool fitAfter,
        bool queueSharpBaseAfterPreview,
        string? statusAfter)
    {
        CachedBitmapRender? render = null;
        try
        {
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            if (!IsCurrentPersistedPreviewRenderTarget(pdfPath, pdfIndex, pageFolder, version))
                return;

            render = await Task.Run(() =>
            {
                return TryReadPersistedPreviewBitmap(pdfPath, pdfIndex, renderScale, out CachedBitmapRender loaded)
                    ? loaded
                    : null;
            });
            if (render == null)
                return;

            if (!IsCurrentPersistedPreviewRenderTarget(pdfPath, pdfIndex, pageFolder, version) ||
                ShouldSkipPersistedPreviewRender(render))
            {
                render.Bitmap.Dispose();
                render = null;
                return;
            }

            string cacheKey = DocnetRenderCacheKey(pdfPath, pdfIndex, renderScale);
            ApplyPreviewBitmapRender(render.Bitmap, render.WidthPt, render.HeightPt, render.BitmapScale, restoreView, fitAfter);
            PersistedPreviewBitmapCache.Put(cacheKey, render.WidthPt, render.HeightPt, render.BitmapScale, render.Bitmap);
            DocnetRenderCache.Put(cacheKey, render.WidthPt, render.HeightPt, render.BitmapScale, render.Bitmap);
            AppLog.Info(
                $"Viewport PyMuPDF preview async cache hit; page='{pageFolder}'; " +
                $"pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pdfIndex + 1}; scale={renderScale:0.###}");
            ReportViewportRenderProfile(
                "preview-async",
                pageFolder,
                pdfPath,
                pdfIndex,
                renderScale,
                elapsedMs: 0,
                fromCache: true,
                clipRect: null);
            if (!string.IsNullOrWhiteSpace(statusAfter))
                PostStatus(statusAfter);
            if (queueSharpBaseAfterPreview)
                QueueSharpBaseRenderAfterPreview(pdfPath, pdfIndex, pageFolder);
            RequestRepaint();
            render = null;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport persisted preview async load failed for {Path.GetFileName(pdfPath)} page {pdfIndex + 1}");
        }
        finally
        {
            render?.Bitmap.Dispose();
        }
    }

    private bool IsCurrentPersistedPreviewRenderTarget(
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        int version) =>
        version == _persistedPreviewRenderVersion &&
        string.Equals(pdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
        pdfIndex == _pdfIndex &&
        string.Equals(pageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase);

    private bool ShouldSkipPersistedPreviewRender(CachedBitmapRender render)
    {
        if (!IsPageBitmapFor(_pdfPath, _pdfIndex, _pageFolder) ||
            _pageBitmap == null ||
            _bitmapScale <= render.BitmapScale * 1.05f)
        {
            return false;
        }

        return !ViewportRenderPolicy.ShouldPreferLowerScalePageBitmapForNavigation(
            _zoom,
            _bitmapScale,
            render.BitmapScale);
    }

    private static bool TryReadPersistedPreviewBitmap(
        string pdfPath,
        int pdfIndex,
        float renderScale,
        out CachedBitmapRender render)
    {
        render = new CachedBitmapRender(0, 0, 1, new SKBitmap());
        if (!PdfPreviewRenderCache.TryReadCleanPreview(pdfPath, pdfIndex, renderScale, out PdfLayerRenderResult preview) &&
            !PdfPreviewRenderCache.TryReadCleanRender(pdfPath, pdfIndex, renderScale, out preview))
        {
            return false;
        }

        SKBitmap? bitmap = SKBitmap.Decode(preview.ImageBytes);
        if (bitmap == null)
            return false;

        float bitmapScale = preview.WidthPt > 0 ? bitmap.Width / preview.WidthPt : renderScale;
        render = new CachedBitmapRender(preview.WidthPt, preview.HeightPt, bitmapScale, bitmap);
        return true;
    }
}
