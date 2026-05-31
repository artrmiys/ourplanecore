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

public sealed class PdfLayerRenderResult
{
    public byte[] ImageBytes { get; init; } = [];
    public float WidthPt { get; init; }
    public float HeightPt { get; init; }
    public IReadOnlyList<PdfLayer> Layers { get; init; } = [];
    public bool LayersCaptured { get; init; }
}

public sealed class PdfLayerTraceResult
{
    public int Layer { get; init; }
    public string LayerName { get; init; } = "";
    public string Mode { get; init; } = "";
    public IReadOnlyList<PdfLayerTraceMeasurement> Measurements { get; init; } = [];
}

public sealed class PdfLayerTraceMeasurement
{
    public string MType { get; init; } = "";
    public string Name { get; init; } = "";
    public string Notes { get; init; } = "";
    public IReadOnlyList<SKPoint> Points { get; init; } = [];
}

public sealed class PdfLayerProbeResult
{
    public IReadOnlyList<PdfLayerProbeCandidate> Candidates { get; init; } = [];
}

public sealed class PdfLayerProbeCandidate
{
    public int Layer { get; init; }
    public string LayerName { get; init; } = "";
    public float Distance { get; init; }
    public SKRect Bounds { get; init; }
}
