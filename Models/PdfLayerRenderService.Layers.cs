using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

public static partial class PdfLayerRenderService
{
    internal static bool TryReadVisibleLayers(
        string pdfPath,
        int pageIndex,
        out IReadOnlyList<PdfLayerInfo> layers,
        out string error)
    {
        layers = [];
        error = "";

        string tempDir = Path.Combine(Path.GetTempPath(), "OurPlanCore", Guid.NewGuid().ToString("N"));
        string inputPath = Path.Combine(tempDir, "input.json");
        string outputPath = Path.Combine(tempDir, "output.json");

        try
        {
            Directory.CreateDirectory(tempDir);
            var request = new LayerListRequest
            {
                Pdf = pdfPath,
                Page = pageIndex,
            };

            if (!TryInvokeWorker("layers", request, out LayerListResponse? response, out error) &&
                !TryRunFileCommand("layers", request, inputPath, outputPath, out response, out error))
                return false;

            if (response == null || !response.Ok)
            {
                error = response?.Error ?? "PyMuPDF did not return a layer response.";
                return false;
            }

            layers = response.Layers
                .Select(l => new PdfLayerInfo { Number = l.Xref, Name = l.Name, IsOn = l.On })
                .ToList();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"TryReadVisibleLayers failed for {pdfPath} page {pageIndex}");
            error = ex.Message;
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch { }
        }
    }

    internal static bool TryTraceLayer(
        string pdfPath,
        int pageIndex,
        int layerNumber,
        string layerName,
        PdfLayerTraceMode mode,
        SKPoint? pickPoint,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        out PdfLayerTraceResult result,
        out string error)
    {
        result = new PdfLayerTraceResult();
        error = "";

        try
        {
            var request = new LayerTraceRequest
            {
                Pdf = pdfPath,
                Page = pageIndex,
                Layer = layerNumber,
                LayerName = layerName,
                Mode = LayerTraceModeKey(mode),
                PointX = pickPoint?.X,
                PointY = pickPoint?.Y,
                MaxMeasurements = mode == PdfLayerTraceMode.AllEdges ? 48 : 1,
                VisibleLayers = cachedLayers?.Select(LayerDto.FromInfo).ToList(),
            };

            if (!TryInvokeHelper("layertrace", request, out LayerTraceResponse? response, out error))
                return false;

            if (response == null || !response.Ok)
            {
                error = response?.Error ?? "PyMuPDF did not return a layer trace response.";
                return false;
            }

            result = new PdfLayerTraceResult
            {
                Layer = response.Layer,
                LayerName = response.LayerName,
                Mode = response.Mode,
                Measurements = response.Measurements
                    .Select(m => new PdfLayerTraceMeasurement
                    {
                        MType = m.MType,
                        Name = m.Name,
                        Notes = m.Notes,
                        Points = m.Points.Select(p => new SKPoint(p.X, p.Y)).ToList(),
                    })
                    .ToList(),
            };
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"TryTraceLayer failed for {pdfPath} page {pageIndex} layer {layerNumber}");
            error = ex.Message;
            return false;
        }
    }

    internal static bool TryProbeLayers(
        string pdfPath,
        int pageIndex,
        SKPoint point,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        out PdfLayerProbeResult result,
        out string error)
    {
        result = new PdfLayerProbeResult();
        error = "";

        try
        {
            var request = new LayerProbeRequest
            {
                Pdf = pdfPath,
                Page = pageIndex,
                PointX = point.X,
                PointY = point.Y,
                Tolerance = 24,
                MaxCandidates = 12,
                VisibleLayers = cachedLayers?.Select(LayerDto.FromInfo).ToList(),
            };

            if (!TryInvokeHelper("layerprobe", request, out LayerProbeResponse? response, out error))
                return false;

            if (response == null || !response.Ok)
            {
                error = response?.Error ?? "PyMuPDF did not return a layer probe response.";
                return false;
            }

            result = new PdfLayerProbeResult
            {
                Candidates = response.Candidates
                    .Where(candidate => !string.IsNullOrWhiteSpace(candidate.LayerName))
                    .Select(candidate => new PdfLayerProbeCandidate
                    {
                        Layer = candidate.Layer,
                        LayerName = candidate.LayerName,
                        Distance = candidate.Distance,
                        Bounds = new SKRect(
                            candidate.Bounds.X0,
                            candidate.Bounds.Y0,
                            candidate.Bounds.X1,
                            candidate.Bounds.Y1),
                    })
                    .ToList(),
            };
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"TryProbeLayers failed for {pdfPath} page {pageIndex}");
            error = ex.Message;
            return false;
        }
    }
}
