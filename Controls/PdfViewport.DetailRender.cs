using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private SKBitmap? _detailBitmap;
    private SKRect _detailPdfRect;
    private float _detailBitmapScale;
    private string _detailPageKey = "";
    private DetailRenderRequest? _pendingDetailRender;
    private bool _detailRenderInProgress;
    private int _detailRenderVersion;

    private sealed record DetailRenderRequest(
        int Version,
        string PdfPath,
        int PdfIndex,
        string PageFolder,
        SKRect ClipRect,
        float RenderScale,
        Dictionary<int, bool> LayerStates,
        HashSet<int> HighlightedLayers,
        IReadOnlyList<PdfLayerInfo>? CachedLayers);

    private void ClearDetailRender()
    {
        _pendingDetailRender = null;
        _detailRenderVersion++;
        ClearDetailRenderBitmap();
    }

    private void ClearDetailRenderBitmap()
    {
        _detailBitmap?.Dispose();
        _detailBitmap = null;
        _detailPdfRect = SKRect.Empty;
        _detailBitmapScale = 0;
        _detailPageKey = "";
    }

    private void QueueDetailRenderIfNeeded(bool force)
    {
        if (!TryBuildDetailRenderRequest(force, out DetailRenderRequest? request))
            return;
        if (request == null)
            return;

        if (!force && IsSameDetailRequest(_pendingDetailRender, request))
            return;

        _pendingDetailRender = request;
        _ = StartNextDetailRenderAsync();
    }

    private bool TryBuildDetailRenderRequest(bool force, out DetailRenderRequest? request)
    {
        request = null;
        if (!ViewportRenderPolicy.DetailRenderEnabled ||
            string.IsNullOrWhiteSpace(_pdfPath) ||
            _pageBitmap == null ||
            _pdfW <= 0 ||
            _pdfH <= 0 ||
            _bitmapScale <= 0 ||
            _showingPreviousPageDuringSwitch)
        {
            return false;
        }

        SKRect clip = ClampPdfRectToPage(GetVisiblePdfRect(ViewportRenderPolicy.DetailRenderPaddingScreenPx));
        if (clip.Width <= 0 || clip.Height <= 0)
            return false;

        float targetScale = ViewportRenderPolicy.SelectDetailRenderScale(
            _zoom,
            clip.Width,
            clip.Height,
            _bitmapScale);
        if (targetScale <= 0)
            return false;

        if (!force && DetailRenderCoversCurrentView(targetScale))
            return false;

        int version = ++_detailRenderVersion;
        request = new DetailRenderRequest(
            version,
            _pdfPath,
            _pdfIndex,
            _pageFolder,
            clip,
            targetScale,
            EffectiveLayerStates(),
            EffectiveHighlightedLayers(),
            LayerRenderCachedLayers());
        return true;
    }

    private bool DetailRenderCoversCurrentView(float targetScale)
    {
        if (_detailBitmap == null ||
            _detailBitmapScale <= 0 ||
            !string.Equals(_detailPageKey, DetailPageKey(_pdfPath, _pdfIndex, _pageFolder), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_detailBitmapScale < targetScale * 0.92f)
            return false;

        SKRect visible = ClampPdfRectToPage(GetVisiblePdfRect());
        return RectContains(_detailPdfRect, visible, tolerancePt: 0.5f);
    }

    private async Task StartNextDetailRenderAsync()
    {
        if (_detailRenderInProgress || _pendingDetailRender == null)
            return;

        DetailRenderRequest request = _pendingDetailRender;
        _pendingDetailRender = null;
        _detailRenderInProgress = true;
        try
        {
            Stopwatch renderWatch = Stopwatch.StartNew();
            var renderResult = await PdfLayerRenderService.TryRenderAsync(
                request.PdfPath,
                request.PdfIndex,
                request.RenderScale,
                request.LayerStates,
                request.HighlightedLayers,
                request.CachedLayers,
                request.ClipRect);
            renderWatch.Stop();
            ReportViewportRenderProfile(
                "detail",
                request.PageFolder,
                request.PdfPath,
                request.PdfIndex,
                request.RenderScale,
                renderWatch.ElapsedMilliseconds,
                fromCache: false,
                request.ClipRect);

            if (!IsCurrentDetailRequest(request))
                return;

            if (renderResult.Ok)
                ApplyDetailRenderResult(request, renderResult.Result);
            else if (!string.IsNullOrWhiteSpace(renderResult.Error))
                AppLog.Warn($"Viewport detail render unavailable: {renderResult.Error}");
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Viewport detail render failed.");
        }
        finally
        {
            _detailRenderInProgress = false;
            if (_pendingDetailRender != null)
                _ = StartNextDetailRenderAsync();
        }
    }

    private void ApplyDetailRenderResult(DetailRenderRequest request, PdfLayerRenderResult render)
    {
        var bitmap = SKBitmap.Decode(render.ImageBytes);
        if (bitmap == null)
        {
            AppLog.Warn("Viewport detail render returned an unreadable image.");
            return;
        }

        if (!IsCurrentDetailRequest(request))
        {
            bitmap.Dispose();
            return;
        }

        SKRect clip = ClampPdfRectToPage(render.ClipRect ?? request.ClipRect);
        if (clip.Width <= 0 || clip.Height <= 0)
        {
            bitmap.Dispose();
            return;
        }

        _detailBitmap?.Dispose();
        _detailBitmap = bitmap;
        _detailPdfRect = clip;
        _detailBitmapScale = clip.Width > 0 ? bitmap.Width / clip.Width : request.RenderScale;
        _detailPageKey = DetailPageKey(request.PdfPath, request.PdfIndex, request.PageFolder);
        RequestRepaint();
    }

    private void DrawDetailRenderTile(SKCanvas canvas)
    {
        if (_detailBitmap == null ||
            _detailBitmapScale <= 0 ||
            _detailPdfRect.Width <= 0 ||
            _detailPdfRect.Height <= 0 ||
            !string.Equals(_detailPageKey, DetailPageKey(_pdfPath, _pdfIndex, _pageFolder), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SKRect visible = ClampPdfRectToPage(GetVisiblePdfRect());
        if (!Intersects(_detailPdfRect, visible))
            return;

        using var paint = new SKPaint
        {
            IsAntialias = false,
            FilterQuality = _zoom > _detailBitmapScale * 1.05f ? SKFilterQuality.High : SKFilterQuality.Medium,
        };
        var src = new SKRect(0, 0, _detailBitmap.Width, _detailBitmap.Height);
        var dst = new SKRect(
            (_detailPdfRect.Left - _panX) * _zoom,
            (_detailPdfRect.Top - _panY) * _zoom,
            (_detailPdfRect.Right - _panX) * _zoom,
            (_detailPdfRect.Bottom - _panY) * _zoom);
        canvas.DrawBitmap(_detailBitmap, src, dst, paint);
    }

    private void ReportViewportRenderProfile(
        string kind,
        string pageFolder,
        string pdfPath,
        int pdfIndex,
        float renderScale,
        long elapsedMs,
        bool fromCache,
        SKRect? clipRect)
    {
        string clip = clipRect.HasValue
            ? $" clip={clipRect.Value.Left:0.#},{clipRect.Value.Top:0.#},{clipRect.Value.Right:0.#},{clipRect.Value.Bottom:0.#};"
            : "";
        AppLog.Info(
            $"Viewport render profile kind={kind}; elapsed={elapsedMs}ms; cache={fromCache}; " +
            $"zoom={_zoom:0.###}; bitmapScale={_bitmapScale:0.###}; renderedScale={_renderedScale:0.###}; " +
            $"targetScale={renderScale:0.###};{clip} page='{pageFolder}'; " +
            $"pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pdfIndex + 1}");
    }

    private bool IsCurrentDetailRequest(DetailRenderRequest request) =>
        request.Version == _detailRenderVersion &&
        string.Equals(request.PdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
        request.PdfIndex == _pdfIndex &&
        string.Equals(request.PageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameDetailRequest(DetailRenderRequest? existing, DetailRenderRequest next) =>
        existing != null &&
        string.Equals(existing.PdfPath, next.PdfPath, StringComparison.OrdinalIgnoreCase) &&
        existing.PdfIndex == next.PdfIndex &&
        string.Equals(existing.PageFolder, next.PageFolder, StringComparison.OrdinalIgnoreCase) &&
        Math.Abs(existing.RenderScale - next.RenderScale) < 0.02f &&
        RectNearlyEquals(existing.ClipRect, next.ClipRect, tolerancePt: 0.5f);

    private SKRect ClampPdfRectToPage(SKRect rect)
    {
        if (_pdfW <= 0 || _pdfH <= 0)
            return SKRect.Empty;

        float left = Math.Clamp(rect.Left, 0, _pdfW);
        float top = Math.Clamp(rect.Top, 0, _pdfH);
        float right = Math.Clamp(rect.Right, left, _pdfW);
        float bottom = Math.Clamp(rect.Bottom, top, _pdfH);
        return right > left && bottom > top ? new SKRect(left, top, right, bottom) : SKRect.Empty;
    }

    private static bool RectContains(SKRect outer, SKRect inner, float tolerancePt) =>
        outer.Left <= inner.Left + tolerancePt &&
        outer.Top <= inner.Top + tolerancePt &&
        outer.Right >= inner.Right - tolerancePt &&
        outer.Bottom >= inner.Bottom - tolerancePt;

    private static bool RectNearlyEquals(SKRect a, SKRect b, float tolerancePt) =>
        Math.Abs(a.Left - b.Left) <= tolerancePt &&
        Math.Abs(a.Top - b.Top) <= tolerancePt &&
        Math.Abs(a.Right - b.Right) <= tolerancePt &&
        Math.Abs(a.Bottom - b.Bottom) <= tolerancePt;

    private static bool Intersects(SKRect a, SKRect b) =>
        a.Left < b.Right &&
        a.Right > b.Left &&
        a.Top < b.Bottom &&
        a.Bottom > b.Top;

    private static string DetailPageKey(string pdfPath, int pdfIndex, string pageFolder) =>
        string.Join('|', pdfPath.ToLowerInvariant(), pdfIndex, pageFolder.ToLowerInvariant());
}
