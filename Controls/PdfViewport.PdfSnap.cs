using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OurPlanCore.Models;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private bool _pdfSnapEnabled;
    private PdfSnapPointIndex _pdfSnapIndex = PdfSnapPointIndex.Empty;
    private string _pdfSnapCacheKey = "";
    private string _pdfSnapInProgressCacheKey = "";
    private bool _pdfSnapLoadInProgress;
    private bool _pdfSnapReloadPending;
    private int _pdfSnapLoadVersion;
    private int _rasterSheetVisualSegmentVersion;
    private int _rasterSheetVisualContentGeneration;
    private string _overlayPdfSnapPath = "";
    private int _overlayPdfSnapPageIndex;
    private string _overlayPdfSnapName = "";
    private IReadOnlyList<PdfLayerInfo>? _overlayPdfSnapLayers;
    private PdfSnapPointIndex _overlayPdfSnapIndex = PdfSnapPointIndex.Empty;
    private string _overlayPdfSnapCacheKey = "";
    private string _overlayPdfSnapInProgressCacheKey = "";
    private bool _overlayPdfSnapLoadInProgress;
    private bool _overlayPdfSnapReloadPending;
    private int _overlayPdfSnapLoadVersion;

    public bool PdfSnapEnabled
    {
        get => _pdfSnapEnabled;
        set
        {
            if (_pdfSnapEnabled == value)
                return;

            _pdfSnapEnabled = value;
            SetSnapPreview(null);
            PdfSnapChanged?.Invoke(_pdfSnapEnabled);
            if (_pdfSnapEnabled)
            {
                QueuePdfSnapPointLoad(force: false);
                QueueOverlayPdfSnapPointLoad(force: false);
            }
            PostRecordPrompt();
            PostStatus(_pdfSnapEnabled
                ? "PDF Snap on: current sheet and sheet overlay vector points/lines."
                : "PDF Snap off.");
        }
    }

    public event Action<bool>? PdfSnapChanged;

    /// <summary>
    /// Reads the full vector segment set for the current page (visible layers
    /// only), independent of whether PDF Snap mode is on. Prefers the raster
    /// snap index when one exists, falling back to a live PyMuPDF read.
    /// </summary>
    public async Task<(IReadOnlyList<PdfGeometrySnapSegment> Segments, string Error)>
        ReadPdfVectorSegmentsForCurrentPageAsync()
    {
        string pdfPath = _pdfPath;
        int pdfIndex = _pdfIndex;
        string pageFolder = _pageFolder;
        if (string.IsNullOrWhiteSpace(pdfPath))
            return ([], "No PDF page is open in the viewport.");

        IReadOnlyList<PdfLayerInfo>? layers = CurrentPdfSnapVisibleLayers();
        RasterSheetSource? rasterSheet = _rasterSheetSource?.Clone();

        var rasterSnap = await Task.Run(() =>
        {
            bool ok = RasterSheetCacheService.TryReadSnapIndex(
                pageFolder,
                pdfPath,
                rasterSheet,
                out PdfGeometrySnapResult snapIndex,
                out _);
            return (Ok: ok, SnapIndex: snapIndex);
        });
        if (rasterSnap.Ok && rasterSnap.SnapIndex.Segments.Count > 0)
            return (rasterSnap.SnapIndex.Segments, "");

        var result = await PdfGeometrySnapService.TryReadSnapPointsAsync(pdfPath, pdfIndex, layers);
        return result.Ok ? (result.Result.Segments, "") : ([], result.Error);
    }

    public async Task<(IReadOnlyList<PdfGeometrySnapSegment> Segments, string Error, string Source)>
        ReadWallTraceSegmentsForCurrentPageAsync()
    {
        (IReadOnlyList<PdfGeometrySnapSegment> vectorSegments, string vectorError) =
            await ReadPdfVectorSegmentsForCurrentPageAsync();
        if (vectorSegments.Count > 0)
            return (vectorSegments, "", "vector");

        (IReadOnlyList<PdfGeometrySnapSegment> rasterSegments, string rasterError) =
            await ReadRasterImageSegmentsForCurrentPageAsync();
        if (rasterSegments.Count > 0)
            return (rasterSegments, "", "raster-image");

        string error = CombineWallTraceSegmentErrors(vectorError, rasterError);
        return ([], error, "");
    }

    public Task<(IReadOnlyList<PdfGeometrySnapSegment> Segments, string Error)>
        ReadWallTraceRasterImageSegmentsForCurrentPageAsync() =>
        ReadRasterImageSegmentsForCurrentPageAsync();

    private async Task<(IReadOnlyList<PdfGeometrySnapSegment> Segments, string Error)>
        ReadRasterImageSegmentsForCurrentPageAsync()
    {
        string pdfPath = _pdfPath;
        int pdfIndex = _pdfIndex;
        string pageFolder = _pageFolder;
        RasterSheetSource? rasterSheet = _rasterSheetSource?.Clone();

        if (TryCopyCurrentPageBitmapForRasterFeatures(
                pdfPath,
                pdfIndex,
                pageFolder,
                out SKBitmap pageBitmap,
                out float widthPt,
                out float heightPt))
        {
            (IReadOnlyList<PdfGeometrySnapSegment> segments, string error) =
                await ExtractRasterImageSegmentsAsync(pageBitmap, widthPt, heightPt, "current page raster");
            if (segments.Count > 0)
                return (segments, "");

            (IReadOnlyList<PdfGeometrySnapSegment> cachedSegments, string cachedError) =
                await ReadCachedRasterImageSegmentsAsync(pageFolder, pdfPath, pdfIndex, rasterSheet);
            if (cachedSegments.Count > 0)
                return (cachedSegments, "");

            return ([], CombineWallTraceSegmentErrors(error, cachedError));
        }

        return await ReadCachedRasterImageSegmentsAsync(pageFolder, pdfPath, pdfIndex, rasterSheet);
    }

    private bool TryCopyCurrentPageBitmapForRasterFeatures(
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        out SKBitmap bitmap,
        out float widthPt,
        out float heightPt)
    {
        bitmap = new SKBitmap();
        widthPt = 0;
        heightPt = 0;
        if (!IsPageBitmapFor(pdfPath, pdfIndex, pageFolder) ||
            _pageBitmap == null ||
            _pdfW <= 0 ||
            _pdfH <= 0)
        {
            return false;
        }

        SKBitmap? copy = _pageBitmap.Copy();
        if (copy == null || copy.Width <= 0 || copy.Height <= 0)
        {
            copy?.Dispose();
            return false;
        }

        bitmap = copy;
        widthPt = _pdfW;
        heightPt = _pdfH;
        return true;
    }

    private static Task<(IReadOnlyList<PdfGeometrySnapSegment> Segments, string Error)>
        ExtractRasterImageSegmentsAsync(
            SKBitmap bitmap,
            float widthPt,
            float heightPt,
            string source)
    {
        return Task.Run(() =>
        {
            using (bitmap)
            {
                if (SheetOverlayRasterFeatureService.TryExtractSnap(
                        bitmap,
                        widthPt,
                        heightPt,
                        out PdfGeometrySnapResult snap,
                        out string error))
                {
                    return ((IReadOnlyList<PdfGeometrySnapSegment>)snap.Segments, "");
                }

                return ((IReadOnlyList<PdfGeometrySnapSegment>)[], $"{source}: {error}");
            }
        });
    }

    private static Task<(IReadOnlyList<PdfGeometrySnapSegment> Segments, string Error)>
        ReadCachedRasterImageSegmentsAsync(
            string pageFolder,
            string pdfPath,
            int pdfIndex,
            RasterSheetSource? rasterSheet)
    {
        return Task.Run(() =>
        {
            string error = "";
            if (TryReadRasterImageSegments(pageFolder, pdfPath, rasterSheet, "ready raster", out var segments, out error))
                return (segments, "");

            PageInfo? page = string.IsNullOrWhiteSpace(pageFolder)
                ? null
                : OurPlanCoreJobStore.TryReadPage(pageFolder);
            string pageError = "";
            if (page?.RasterSheet != null &&
                TryReadRasterImageSegments(page.FolderPath, page.PdfPath, page.RasterSheet, "page raster", out segments, out pageError))
            {
                return (segments, "");
            }

            error = CombineWallTraceSegmentErrors(error, pageError);
            if (page == null || string.IsNullOrWhiteSpace(page.PdfPath) || !File.Exists(page.PdfPath))
                return ((IReadOnlyList<PdfGeometrySnapSegment>)[], error);

            int pinnedDpi = RasterSheetCacheService.PinnedRasterDpi(page.RasterSheet);
            float buildScale = pinnedDpi > 0
                ? RasterSheetCacheService.RasterDpiToRenderScale(pinnedDpi)
                : RasterSheetCacheService.DefaultRenderScale;
            RasterSheetBuildResult build;
            using (IDisposable? writeActivity =
                   JobFileWriteActivity.TryBeginBackgroundWriteForProjectPath(page.FolderPath))
            {
                if (writeActivity == null)
                {
                    return ((IReadOnlyList<PdfGeometrySnapSegment>)[],
                        CombineWallTraceSegmentErrors(error, "raster build skipped during project checkpoint"));
                }

                build = RasterSheetCacheService.BuildCachePreservingEnabled(page, buildScale);
            }
            if (!build.Ok || build.Source == null)
            {
                string buildError = string.IsNullOrWhiteSpace(build.Error)
                    ? "raster build failed"
                    : build.Error;
                return ((IReadOnlyList<PdfGeometrySnapSegment>)[], CombineWallTraceSegmentErrors(error, buildError));
            }

            if (TryReadRasterImageSegments(page.FolderPath, page.PdfPath, build.Source, "built raster", out segments, out string builtError))
                return (segments, "");

            return ((IReadOnlyList<PdfGeometrySnapSegment>)[], CombineWallTraceSegmentErrors(error, builtError));
        });
    }

    private static bool TryReadRasterImageSegments(
        string pageFolder,
        string pdfPath,
        RasterSheetSource? rasterSheet,
        string source,
        out IReadOnlyList<PdfGeometrySnapSegment> segments,
        out string error)
    {
        segments = [];
        error = "";
        if (!RasterSheetCacheService.TryReadReady(
                pageFolder,
                pdfPath,
                rasterSheet,
                out RasterSheetBitmapResult raster,
                out string reason))
        {
            error = $"{source}: {reason}";
            return false;
        }

        using (raster.Bitmap)
        {
            if (!SheetOverlayRasterFeatureService.TryExtractSnap(
                    raster.Bitmap,
                    raster.WidthPt,
                    raster.HeightPt,
                    out PdfGeometrySnapResult snap,
                    out string extractError))
            {
                error = $"{source}: {extractError}";
                return false;
            }

            segments = snap.Segments;
            return segments.Count > 0;
        }
    }

    private static string CombineWallTraceSegmentErrors(string first, string second)
    {
        first = (first ?? "").Trim();
        second = (second ?? "").Trim();
        if (first.Length == 0)
            return second;
        if (second.Length == 0)
            return first;
        return $"{first}; {second}";
    }

    /// <summary>
    /// Word bounding boxes for the current page, in the same PDF point space
    /// as the vector segments. Empty on failure (best-effort helper data).
    /// </summary>
    public async Task<IReadOnlyList<SKRect>> ReadPdfTextRectsForCurrentPageAsync()
    {
        string pdfPath = _pdfPath;
        int pdfIndex = _pdfIndex;
        if (string.IsNullOrWhiteSpace(pdfPath))
            return [];

        var result = await PdfGeometrySnapService.TryReadTextRectsAsync(pdfPath, pdfIndex);
        return result.Ok ? result.Rects : [];
    }

    /// <summary>
    /// Filled path boxes (wall poche) with fill luminance for the current
    /// page. Empty on failure or when the sheet has no filled walls
    /// (best-effort data).
    /// </summary>
    public async Task<IReadOnlyList<WallCenterlineTracer.FillZone>> ReadPdfWallFillZonesForCurrentPageAsync()
    {
        string pdfPath = _pdfPath;
        int pdfIndex = _pdfIndex;
        if (string.IsNullOrWhiteSpace(pdfPath))
            return [];

        var result = await PdfGeometrySnapService.TryReadWallFillZonesAsync(pdfPath, pdfIndex);
        return result.Ok ? result.Zones : [];
    }

    private void ResetPdfSnapCache()
    {
        _pdfSnapIndex = PdfSnapPointIndex.Empty;
        _pdfSnapCacheKey = "";
        _pdfSnapInProgressCacheKey = "";
        _pdfSnapReloadPending = false;
        _pdfSnapLoadVersion++;
    }

    private void ClearRasterSheetVisualSegments()
    {
        _rasterSheetVisualSegments = [];
        _rasterSheetVisualSegmentVersion++;
        _rasterSheetVisualContentGeneration++;
    }

    private void QueueRasterSheetVisualSegmentsLoad(
        string pageFolder,
        string pdfPath,
        int pdfIndex,
        RasterSheetSource? rasterSheet)
    {
        _rasterSheetVisualSegments = [];
        _rasterSheetVisualContentGeneration++;
        int version = ++_rasterSheetVisualSegmentVersion;
        _ = LoadRasterSheetVisualSegmentsAsync(version, pageFolder, pdfPath, pdfIndex, rasterSheet?.Clone());
    }

    private async Task LoadRasterSheetVisualSegmentsAsync(
        int version,
        string pageFolder,
        string pdfPath,
        int pdfIndex,
        RasterSheetSource? rasterSheet)
    {
        try
        {
            var read = await Task.Run(() =>
            {
                bool ok = RasterSheetCacheService.TryReadSnapIndex(
                    pageFolder,
                    pdfPath,
                    rasterSheet,
                    out PdfGeometrySnapResult snapIndex,
                    out _);
                return (Ok: ok, SnapIndex: snapIndex);
            });
            if (version != _rasterSheetVisualSegmentVersion ||
                !IsCurrentPageRasterTarget(pdfPath, pdfIndex, pageFolder))
            {
                return;
            }

            _rasterSheetVisualSegments = read.Ok ? read.SnapIndex.Segments : [];
            _rasterSheetVisualContentGeneration++;
            RequestRepaint();
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport raster sheet visual segments failed for {Path.GetFileName(pdfPath)}");
        }
    }

    private void SetOverlayPdfSnapSource(
        string pdfPath,
        int pageIndex,
        string overlayName,
        IReadOnlyList<PdfLayerInfo>? layers)
    {
        _overlayPdfSnapPath = pdfPath ?? "";
        _overlayPdfSnapPageIndex = pageIndex;
        _overlayPdfSnapName = overlayName ?? "";
        _overlayPdfSnapLayers = layers;
        ResetOverlayPdfSnapCache();
        QueueOverlayPdfSnapPointLoad(force: true);
    }

    private void ClearOverlayPdfSnapSource()
    {
        _overlayPdfSnapPath = "";
        _overlayPdfSnapPageIndex = 0;
        _overlayPdfSnapName = "";
        _overlayPdfSnapLayers = null;
        ResetOverlayPdfSnapCache();
    }

    private void ResetOverlayPdfSnapCache()
    {
        _overlayPdfSnapIndex = PdfSnapPointIndex.Empty;
        _overlayPdfSnapCacheKey = "";
        _overlayPdfSnapInProgressCacheKey = "";
        _overlayPdfSnapReloadPending = false;
        _overlayPdfSnapLoadVersion++;
    }

    private void QueuePdfSnapPointLoad(bool force)
    {
        if (!_pdfSnapEnabled || string.IsNullOrWhiteSpace(_pdfPath))
            return;

        IReadOnlyList<PdfLayerInfo>? layers = CurrentPdfSnapVisibleLayers();
        RasterSheetSource? rasterSheet = _rasterSheetSource?.Clone();
        string cacheKey = PdfSnapCacheKey(
            _pdfPath,
            _pdfIndex,
            _pageFolder,
            PdfSnapLayerStateKey(layers),
            PdfSnapRasterStateKey(rasterSheet));
        if (!force && string.Equals(_pdfSnapCacheKey, cacheKey, StringComparison.Ordinal))
            return;

        if (_pdfSnapLoadInProgress)
        {
            if (string.Equals(_pdfSnapInProgressCacheKey, cacheKey, StringComparison.Ordinal))
                return;

            _pdfSnapReloadPending = true;
            return;
        }

        _pdfSnapLoadInProgress = true;
        _pdfSnapInProgressCacheKey = cacheKey;
        int version = ++_pdfSnapLoadVersion;
        _ = LoadPdfSnapPointsAsync(version, _pdfPath, _pdfIndex, _pageFolder, layers, rasterSheet, cacheKey);
    }

    private async Task LoadPdfSnapPointsAsync(
        int version,
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        IReadOnlyList<PdfLayerInfo>? layers,
        RasterSheetSource? rasterSheet,
        string cacheKey)
    {
        try
        {
            var rasterSnap = await Task.Run(() =>
            {
                bool ok = RasterSheetCacheService.TryReadSnapIndex(
                    pageFolder,
                    pdfPath,
                    rasterSheet,
                    out PdfGeometrySnapResult snapIndex,
                    out string rasterSnapReason);
                return (Ok: ok, SnapIndex: snapIndex, Reason: rasterSnapReason);
            });
            if (rasterSnap.Ok)
            {
                if (!IsCurrentPdfSnapRequest(version, pdfPath, pdfIndex, pageFolder))
                    return;

                _pdfSnapIndex = new PdfSnapPointIndex(rasterSnap.SnapIndex.Points, rasterSnap.SnapIndex.Segments);
                _pdfSnapCacheKey = cacheKey;
                if (_pdfSnapEnabled)
                    PostStatus($"PDF Snap ready from raster index: {_pdfSnapIndex.Count} strict black line points/segments.");
                return;
            }

            var result = await PdfGeometrySnapService.TryReadSnapPointsAsync(pdfPath, pdfIndex, layers);
            if (!IsCurrentPdfSnapRequest(version, pdfPath, pdfIndex, pageFolder))
                return;

            if (!result.Ok)
            {
                _pdfSnapIndex = PdfSnapPointIndex.Empty;
                _pdfSnapCacheKey = cacheKey;
                PostStatus($"PDF Snap unavailable: {result.Error}");
                return;
            }

            _pdfSnapIndex = new PdfSnapPointIndex(result.Result.Points, result.Result.Segments);
            _pdfSnapCacheKey = cacheKey;
            if (_pdfSnapEnabled)
            {
                string fallback = !string.IsNullOrWhiteSpace(rasterSnap.Reason)
                    ? $" ({rasterSnap.Reason}; live PDF)"
                    : "";
                PostStatus($"PDF Snap ready: {_pdfSnapIndex.Count} sheet points/lines{fallback}.");
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "PDF snap point load failed.");
            if (IsCurrentPdfSnapRequest(version, pdfPath, pdfIndex, pageFolder))
                PostStatus($"PDF Snap failed: {ex.Message}");
        }
        finally
        {
            _pdfSnapLoadInProgress = false;
            _pdfSnapInProgressCacheKey = "";
            if (_pdfSnapReloadPending)
            {
                _pdfSnapReloadPending = false;
                QueuePdfSnapPointLoad(force: true);
            }
        }
    }

    private bool IsCurrentPdfSnapRequest(int version, string pdfPath, int pdfIndex, string pageFolder) =>
        version == _pdfSnapLoadVersion &&
        _pdfSnapEnabled &&
        string.Equals(pdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
        pdfIndex == _pdfIndex &&
        string.Equals(pageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase);

    private void QueueOverlayPdfSnapPointLoad(bool force)
    {
        if (!_pdfSnapEnabled || string.IsNullOrWhiteSpace(_overlayPdfSnapPath))
            return;

        string cacheKey = PdfSnapCacheKey(
            _overlayPdfSnapPath,
            _overlayPdfSnapPageIndex,
            _overlayPdfSnapName,
            PdfSnapLayerStateKey(_overlayPdfSnapLayers));
        if (!force && string.Equals(_overlayPdfSnapCacheKey, cacheKey, StringComparison.Ordinal))
            return;

        if (_overlayPdfSnapLoadInProgress)
        {
            if (string.Equals(_overlayPdfSnapInProgressCacheKey, cacheKey, StringComparison.Ordinal))
                return;

            _overlayPdfSnapReloadPending = true;
            return;
        }

        _overlayPdfSnapLoadInProgress = true;
        _overlayPdfSnapInProgressCacheKey = cacheKey;
        int version = ++_overlayPdfSnapLoadVersion;
        _ = LoadOverlayPdfSnapPointsAsync(
            version,
            _overlayPdfSnapPath,
            _overlayPdfSnapPageIndex,
            _overlayPdfSnapName,
            _overlayPdfSnapLayers,
            cacheKey);
    }

    private async Task LoadOverlayPdfSnapPointsAsync(
        int version,
        string pdfPath,
        int pdfIndex,
        string overlayName,
        IReadOnlyList<PdfLayerInfo>? layers,
        string cacheKey)
    {
        try
        {
            var result = await PdfGeometrySnapService.TryReadSnapPointsAsync(pdfPath, pdfIndex, layers);
            if (!IsCurrentOverlayPdfSnapRequest(version, pdfPath, pdfIndex, overlayName))
                return;

            if (!result.Ok)
            {
                _overlayPdfSnapIndex = PdfSnapPointIndex.Empty;
                _overlayPdfSnapCacheKey = cacheKey;
                PostStatus($"Overlay PDF Snap unavailable: {result.Error}");
                return;
            }

            _overlayPdfSnapIndex = new PdfSnapPointIndex(result.Result.Points, result.Result.Segments);
            _overlayPdfSnapCacheKey = cacheKey;
            if (_pdfSnapEnabled)
                PostStatus($"PDF Snap ready: {_pdfSnapIndex.Count} sheet points/lines, {_overlayPdfSnapIndex.Count} overlay points/lines.");
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Overlay PDF snap point load failed.");
            if (IsCurrentOverlayPdfSnapRequest(version, pdfPath, pdfIndex, overlayName))
                PostStatus($"Overlay PDF Snap failed: {ex.Message}");
        }
        finally
        {
            _overlayPdfSnapLoadInProgress = false;
            _overlayPdfSnapInProgressCacheKey = "";
            if (_overlayPdfSnapReloadPending)
            {
                _overlayPdfSnapReloadPending = false;
                QueueOverlayPdfSnapPointLoad(force: true);
            }
        }
    }

    private bool IsCurrentOverlayPdfSnapRequest(int version, string pdfPath, int pdfIndex, string overlayName) =>
        version == _overlayPdfSnapLoadVersion &&
        _pdfSnapEnabled &&
        string.Equals(pdfPath, _overlayPdfSnapPath, StringComparison.OrdinalIgnoreCase) &&
        pdfIndex == _overlayPdfSnapPageIndex &&
        string.Equals(overlayName, _overlayPdfSnapName, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<PdfLayerInfo>? CurrentPdfSnapVisibleLayers()
    {
        IReadOnlyList<PdfLayerInfo>? source = _cachedLayers?.Count > 0
            ? _cachedLayers
            : _layers.Count > 0
                ? _layers
                    .Select(layer => new PdfLayerInfo
                    {
                        Number = layer.Number,
                        Name = layer.Name,
                        IsOn = layer.IsOn,
                    })
                    .ToList()
                : null;

        if (source == null || source.Count == 0)
            return null;

        return source
            .Select(layer => new PdfLayerInfo
            {
                Number = layer.Number,
                Name = layer.Name,
                IsOn = _layerStates.TryGetValue(layer.Number, out bool on) ? on : layer.IsOn,
            })
            .ToList();
    }

    private static string PdfSnapCacheKey(
        string pdfPath,
        int pdfIndex,
        string pageFolder,
        string layerStateKey,
        string rasterStateKey = "") =>
        $"{pdfPath}|{pdfIndex}|{pageFolder}|{layerStateKey}|{rasterStateKey}";

    private static string PdfSnapLayerStateKey(IReadOnlyList<PdfLayerInfo>? layers) =>
        layers == null || layers.Count == 0
            ? "no-layers"
            : string.Join("|", layers.Select(layer => $"{layer.Number}:{layer.IsOn}"));

    private static string PdfSnapRasterStateKey(RasterSheetSource? rasterSheet) =>
        rasterSheet == null || string.IsNullOrWhiteSpace(rasterSheet.SnapIndex)
            ? "live-pdf"
            : string.Join(
                ":",
                "raster",
                rasterSheet.SnapIndex,
                rasterSheet.SnapGeneratedAtUtc,
                rasterSheet.SnapPointCount,
                rasterSheet.SnapSegmentCount);

    private bool TryFindPdfSnapPoint(SKPoint rawPdf, float tolerancePt, out SKPoint snapped, out string snapKind)
    {
        snapped = default;
        snapKind = "";
        if (!PdfSnapEnabled)
            return false;

        if (!IsPdfSnapCacheCurrent())
            QueuePdfSnapPointLoad(force: false);

        bool found = false;
        float best = tolerancePt * tolerancePt;
        SKPoint bestPoint = default;
        string bestKind = "";

        void Consider(SKPoint candidate, string kind)
        {
            float distance = DistanceSquared(rawPdf, candidate);
            if (distance >= best)
                return;

            best = distance;
            bestPoint = candidate;
            bestKind = kind;
            found = true;
        }

        if (TryFindBasePdfSnapPoint(rawPdf, tolerancePt, out SKPoint baseSnap, out string baseKind))
            Consider(baseSnap, baseKind);

        if (TryFindOverlayPdfSnapPoint(rawPdf, tolerancePt, out SKPoint overlaySnap, out string overlayKind))
            Consider(overlaySnap, overlayKind);

        snapped = bestPoint;
        snapKind = bestKind;
        return found;
    }

    private bool TryFindBasePdfSnapPoint(SKPoint rawPdf, float tolerancePt, out SKPoint snapped, out string snapKind)
    {
        snapped = default;
        snapKind = "";
        if (!PdfSnapEnabled)
            return false;

        if (!IsPdfSnapCacheCurrent())
            QueuePdfSnapPointLoad(force: false);

        if (!_pdfSnapIndex.TryFind(rawPdf, tolerancePt, out PdfGeometrySnapPoint snap))
            return false;

        snapped = snap.Point;
        snapKind = NormalizePdfSnapKind(snap.Kind, overlay: false);
        return true;
    }

    private bool IsPdfSnapCacheCurrent()
    {
        if (string.IsNullOrWhiteSpace(_pdfSnapCacheKey) || string.IsNullOrWhiteSpace(_pdfPath))
            return false;

        IReadOnlyList<PdfLayerInfo>? layers = CurrentPdfSnapVisibleLayers();
        string cacheKey = PdfSnapCacheKey(
            _pdfPath,
            _pdfIndex,
            _pageFolder,
            PdfSnapLayerStateKey(layers),
            PdfSnapRasterStateKey(_rasterSheetSource));
        return string.Equals(_pdfSnapCacheKey, cacheKey, StringComparison.Ordinal);
    }

    private bool TryFindOverlayPdfSnapPoint(SKPoint rawPdf, float tolerancePt, out SKPoint snapped, out string snapKind)
    {
        snapped = default;
        snapKind = "";
        if (!PdfSnapEnabled ||
            _sheetOverlayBitmap == null ||
            string.IsNullOrWhiteSpace(_overlayPdfSnapPath))
        {
            return false;
        }

        if (!IsOverlayPdfSnapCacheCurrent())
            QueueOverlayPdfSnapPointLoad(force: false);

        float scale = Math.Max(_sheetOverlayScale, 0.001f);
        SKPoint local = OverlayDisplayToLocal(rawPdf);
        if (!_overlayPdfSnapIndex.TryFind(local, tolerancePt / scale, out PdfGeometrySnapPoint snap))
            return false;

        snapped = OverlayLocalToDisplay(snap.Point);
        snapKind = NormalizePdfSnapKind(snap.Kind, overlay: true);
        return true;
    }

    private bool IsOverlayPdfSnapCacheCurrent()
    {
        if (string.IsNullOrWhiteSpace(_overlayPdfSnapCacheKey) || string.IsNullOrWhiteSpace(_overlayPdfSnapPath))
            return false;

        string cacheKey = PdfSnapCacheKey(
            _overlayPdfSnapPath,
            _overlayPdfSnapPageIndex,
            _overlayPdfSnapName,
            PdfSnapLayerStateKey(_overlayPdfSnapLayers));
        return string.Equals(_overlayPdfSnapCacheKey, cacheKey, StringComparison.Ordinal);
    }

    private static string NormalizePdfSnapKind(string kind, bool overlay)
    {
        bool corner = string.Equals(kind, "pdf-corner", StringComparison.OrdinalIgnoreCase);
        bool line = string.Equals(kind, "pdf-line", StringComparison.OrdinalIgnoreCase);
        if (overlay)
            return corner ? "pdf-overlay-corner" : line ? "pdf-overlay-line" : "pdf-overlay-point";
        return corner ? "pdf-corner" : line ? "pdf-line" : "pdf-point";
    }
}
