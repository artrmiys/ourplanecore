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
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public static partial class PdfLayerRenderService
{
    private sealed class RenderRequest
    {
        public string Pdf { get; set; } = "";
        public int Page { get; set; }
        public double Scale { get; set; }
        public string Image { get; set; } = "";
        public bool InlineImage { get; set; }
        public int InlineImageMaxPixels { get; set; }
        public bool InlineRawImage { get; set; }
        public int InlineRawImageMaxPixels { get; set; }
        public bool RawImageFile { get; set; }
        public Dictionary<string, bool> Layers { get; set; } = [];
        public List<int> Highlight { get; set; } = [];
        public List<LayerDto>? VisibleLayers { get; set; }
        public RectDto? Clip { get; set; }
    }

    private sealed class LayerListRequest
    {
        public string Pdf { get; set; } = "";
        public int Page { get; set; }
    }

    private sealed class LayerTraceRequest
    {
        public string Pdf { get; set; } = "";
        public int Page { get; set; }
        public int Layer { get; set; }
        public string LayerName { get; set; } = "";
        public string Mode { get; set; } = "";
        public float? PointX { get; set; }
        public float? PointY { get; set; }
        public int MaxMeasurements { get; set; } = 48;
        public List<LayerDto>? VisibleLayers { get; set; }
    }

    private sealed class LayerProbeRequest
    {
        public string Pdf { get; set; } = "";
        public int Page { get; set; }
        public float PointX { get; set; }
        public float PointY { get; set; }
        public float Tolerance { get; set; } = 24;
        public int MaxCandidates { get; set; } = 12;
        public List<LayerDto>? VisibleLayers { get; set; }
    }

    private sealed class RenderResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public float WidthPt { get; set; }
        public float HeightPt { get; set; }
        public string Image { get; set; } = "";
        public string ImageBase64 { get; set; } = "";
        public string ImageRawBase64 { get; set; } = "";
        public string ImageRawFile { get; set; } = "";
        public int ImageRawWidth { get; set; }
        public int ImageRawHeight { get; set; }
        public int ImageRawChannels { get; set; }
        public List<LayerDto> Layers { get; set; } = [];
        public RectDto? Clip { get; set; }
    }

    private sealed class LayerListResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public List<LayerDto> Layers { get; set; } = [];
    }

    private sealed class LayerTraceResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public int Layer { get; set; }
        public string LayerName { get; set; } = "";
        public string Mode { get; set; } = "";
        public List<LayerTraceMeasurementDto> Measurements { get; set; } = [];
    }

    private sealed class LayerProbeResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public List<LayerProbeCandidateDto> Candidates { get; set; } = [];
    }

    private sealed class LayerTraceMeasurementDto
    {
        public string MType { get; set; } = "";
        public string Name { get; set; } = "";
        public string Notes { get; set; } = "";
        public List<PointDto> Points { get; set; } = [];
    }

    private sealed class PointDto
    {
        public float X { get; set; }
        public float Y { get; set; }
    }

    private sealed class LayerProbeCandidateDto
    {
        public int Layer { get; set; }
        public string LayerName { get; set; } = "";
        public float Distance { get; set; }
        public RectDto Bounds { get; set; } = new();
    }

    private sealed class RectDto
    {
        public float X0 { get; set; }
        public float Y0 { get; set; }
        public float X1 { get; set; }
        public float Y1 { get; set; }

        public static RectDto FromSKRect(SKRect rect) => new()
        {
            X0 = rect.Left,
            Y0 = rect.Top,
            X1 = rect.Right,
            Y1 = rect.Bottom,
        };

        public SKRect ToSKRect() => new(X0, Y0, X1, Y1);
    }

    private sealed class WorkerRequest<TRequest>
    {
        public string Id { get; set; } = "";
        public string Action { get; set; } = "";
        public TRequest? Request { get; set; }
    }

    private sealed class WorkerResponse<TResponse>
    {
        public string Id { get; set; } = "";
        public TResponse? Response { get; set; }
    }

    private sealed class LayerDto
    {
        public int Xref { get; set; }
        public string Name { get; set; } = "";
        public bool On { get; set; }

        public static LayerDto FromInfo(PdfLayerInfo info) => new()
        {
            Xref = info.Number,
            Name = info.Name,
            On = info.IsOn,
        };
    }

    private static string LayerTraceModeKey(PdfLayerTraceMode mode) => mode switch
    {
        PdfLayerTraceMode.Edge => "edge",
        PdfLayerTraceMode.Point => "point",
        PdfLayerTraceMode.AllEdges => "all_edges",
        _ => "full",
    };
}
