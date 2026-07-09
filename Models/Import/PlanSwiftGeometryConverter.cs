using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace OurPlaneCore;

public static class PlanSwiftGeometryConverter
{
    private const double DefaultImageDpi = 72.0;

    public static string MeasurementTypeFromClass(string className)
    {
        string clean = (className ?? "").Trim();
        if (clean.Contains("Count", StringComparison.OrdinalIgnoreCase))
            return "point";
        if (clean.Contains("Area", StringComparison.OrdinalIgnoreCase))
            return "area";
        return "line";
    }

    public static double ScaleMetersPerPoint(double scaleX, string scaleUnits)
    {
        if (scaleX <= 0 || double.IsNaN(scaleX) || double.IsInfinity(scaleX))
            return 0;

        string units = (scaleUnits ?? "").Trim().ToUpperInvariant();
        double metersPerUnit = units switch
        {
            "MM" => 0.001,
            "CM" => 0.01,
            "M" or "METER" or "METERS" => 1.0,
            "IN" or "INCH" or "INCHES" => 0.0254,
            _ => 0.3048,
        };

        return metersPerUnit / scaleX;
    }

    public static PlanSwiftPageNormalization NormalizePageImage(
        int pixelWidth,
        int pixelHeight,
        double dpiX,
        double dpiY)
    {
        int widthPx = Math.Max(1, pixelWidth);
        int heightPx = Math.Max(1, pixelHeight);
        double cleanDpiX = NormalizeImageDpi(dpiX);
        double cleanDpiY = NormalizeImageDpi(dpiY);

        double widthIn = widthPx / cleanDpiX;
        double heightIn = heightPx / cleanDpiY;
        string source = $"image dpi {cleanDpiX:0.##} x {cleanDpiY:0.##}";
        string message = "";

        if (TryInferStandardPageSize(
                widthPx,
                heightPx,
                widthIn,
                heightIn,
                cleanDpiX,
                cleanDpiY,
                out double standardWidthIn,
                out double standardHeightIn,
                out string standardName))
        {
            widthIn = standardWidthIn;
            heightIn = standardHeightIn;
            source = $"standard sheet {standardName}";
            message =
                $"PlanSwift raster page looked oversized at default DPI; normalized to {standardWidthIn:0.##} x {standardHeightIn:0.##} in.";
        }

        double widthPt = widthIn * 72.0;
        double heightPt = heightIn * 72.0;
        return new PlanSwiftPageNormalization(
            widthPx,
            heightPx,
            cleanDpiX,
            cleanDpiY,
            widthPt,
            heightPt,
            widthPt / widthPx,
            heightPt / heightPx,
            source,
            message);
    }

    public static double AdjustScaleForPageNormalization(
        double rawScaleMetersPerPoint,
        PlanSwiftPageNormalization normalization)
    {
        if (rawScaleMetersPerPoint <= 0 ||
            normalization.CoordinateScaleX <= 0 ||
            double.IsNaN(normalization.CoordinateScaleX) ||
            double.IsInfinity(normalization.CoordinateScaleX))
        {
            return rawScaleMetersPerPoint;
        }

        return rawScaleMetersPerPoint / normalization.CoordinateScaleX;
    }

    public static bool HasUniformPageNormalization(PlanSwiftPageNormalization normalization) =>
        Math.Abs(normalization.CoordinateScaleX - normalization.CoordinateScaleY) <=
        Math.Max(0.001, Math.Max(Math.Abs(normalization.CoordinateScaleX), Math.Abs(normalization.CoordinateScaleY)) * 0.01);

    public static IReadOnlyList<PlanSwiftPoint> ParseDigitizerPoints(string digitizerData)
    {
        string clean = (digitizerData ?? "").Trim();
        if (clean.Length == 0)
            return [];

        try
        {
            XDocument doc = XDocument.Parse(clean);
            return doc.Descendants("Point")
                .Select(TryReadPoint)
                .Where(point => point != null)
                .Cast<PlanSwiftPoint>()
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<PlanSwiftPoint> NormalizeSectionPoints(
        IReadOnlyList<PlanSwiftPoint> points,
        string measurementType,
        string boxMode,
        bool closed)
    {
        var normalized = NormalizeBoxPoints(points, measurementType, boxMode).ToList();
        if (string.Equals(measurementType, "line", StringComparison.OrdinalIgnoreCase) &&
            closed &&
            normalized.Count > 2 &&
            !SamePoint(normalized[0], normalized[^1]))
        {
            normalized.Add(normalized[0]);
        }

        return normalized;
    }

    public static IReadOnlyList<PlanSwiftPoint> NormalizeAreaHolePoints(
        IReadOnlyList<PlanSwiftPoint> points,
        string boxMode) =>
        NormalizeBoxPoints(points, "area", boxMode);

    public static bool HasUsableAreaPolygon(IReadOnlyList<PlanSwiftPoint> points)
    {
        int pointCount = points.Count;
        if (pointCount > 1 && SamePoint(points[0], points[^1]))
            pointCount--;

        if (pointCount < 3)
            return false;

        var uniquePoints = new List<PlanSwiftPoint>();
        for (int i = 0; i < pointCount; i++)
        {
            PlanSwiftPoint point = points[i];
            if (!uniquePoints.Any(existing => SamePoint(existing, point)))
                uniquePoints.Add(point);
        }

        if (uniquePoints.Count < 3)
            return false;

        double twiceArea = 0;
        for (int i = 0; i < pointCount; i++)
        {
            PlanSwiftPoint a = points[i];
            PlanSwiftPoint b = points[(i + 1) % pointCount];
            twiceArea += (a.X * b.Y) - (b.X * a.Y);
        }

        return Math.Abs(twiceArea) > 0.02;
    }

    public static string ParsePlanSwiftColor(string value, string fallback = "#FF4444")
    {
        string clean = (value ?? "").Trim();
        if (clean.StartsWith("#", StringComparison.Ordinal) && clean.Length is 7 or 9)
            return clean.Length == 9 ? "#" + clean[^6..].ToUpperInvariant() : clean.ToUpperInvariant();

        if (!int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            return fallback;

        int rgb = parsed & 0xFFFFFF;
        return $"#{rgb:X6}";
    }

    private static PlanSwiftPoint? TryReadPoint(XElement element)
    {
        string xValue = element.Attribute("X")?.Value ?? "";
        string yValue = element.Attribute("Y")?.Value ?? "";
        double x = PlanSwiftXml.ParseDouble(xValue);
        double y = PlanSwiftXml.ParseDouble(yValue);
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
            return null;

        return new PlanSwiftPoint((float)x, (float)y);
    }

    private static IReadOnlyList<PlanSwiftPoint> NormalizeBoxPoints(
        IReadOnlyList<PlanSwiftPoint> points,
        string measurementType,
        string boxMode)
    {
        if (!IsRectangularBoxMode(boxMode) || points.Count != 2)
            return points;

        if (!string.Equals(measurementType, "area", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(measurementType, "line", StringComparison.OrdinalIgnoreCase))
        {
            return points;
        }

        PlanSwiftPoint a = points[0];
        PlanSwiftPoint b = points[1];
        float left = Math.Min(a.X, b.X);
        float right = Math.Max(a.X, b.X);
        float top = Math.Min(a.Y, b.Y);
        float bottom = Math.Max(a.Y, b.Y);

        if (Math.Abs(left - right) < 0.001f || Math.Abs(top - bottom) < 0.001f)
            return points;

        return
        [
            new PlanSwiftPoint(left, top),
            new PlanSwiftPoint(right, top),
            new PlanSwiftPoint(right, bottom),
            new PlanSwiftPoint(left, bottom),
        ];
    }

    private static bool IsRectangularBoxMode(string boxMode)
    {
        string clean = (boxMode ?? "").Trim();
        return clean.Contains("4", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("8", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("box", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePoint(PlanSwiftPoint a, PlanSwiftPoint b) =>
        Math.Abs(a.X - b.X) < 0.001f && Math.Abs(a.Y - b.Y) < 0.001f;

    private static double NormalizeImageDpi(double dpi)
    {
        if (double.IsNaN(dpi) || double.IsInfinity(dpi) || dpi < 10 || dpi > 2400)
            return DefaultImageDpi;

        return dpi;
    }

    private static bool TryInferStandardPageSize(
        int pixelWidth,
        int pixelHeight,
        double widthIn,
        double heightIn,
        double dpiX,
        double dpiY,
        out double standardWidthIn,
        out double standardHeightIn,
        out string standardName)
    {
        standardWidthIn = 0;
        standardHeightIn = 0;
        standardName = "";

        bool looksLikeDefaultDpi =
            dpiX is >= 70 and <= 105 &&
            dpiY is >= 70 and <= 105;
        bool isOversized = Math.Max(widthIn, heightIn) > 48.0 &&
            Math.Min(widthIn, heightIn) > 30.0;
        if (!looksLikeDefaultDpi || !isOversized)
            return false;

        double ratio = Math.Max(widthIn, heightIn) / Math.Min(widthIn, heightIn);
        (string Name, double Width, double Height)[] candidates =
        [
            ("ARCH D", 36, 24),
            ("ARCH E1", 42, 30),
            ("ARCH E", 48, 36),
            ("ARCH C", 24, 18),
            ("ANSI D", 34, 22),
            ("ANSI E", 44, 34),
            ("ANSI C", 22, 17),
            ("Tabloid", 17, 11),
            ("Letter", 11, 8.5),
        ];

        foreach ((string name, double candidateWidth, double candidateHeight) in candidates)
        {
            double candidateRatio = Math.Max(candidateWidth, candidateHeight) / Math.Min(candidateWidth, candidateHeight);
            double ratioError = Math.Abs(ratio - candidateRatio) / candidateRatio;
            if (ratioError > 0.025)
                continue;

            double orientedWidth = widthIn >= heightIn
                ? Math.Max(candidateWidth, candidateHeight)
                : Math.Min(candidateWidth, candidateHeight);
            double orientedHeight = widthIn >= heightIn
                ? Math.Min(candidateWidth, candidateHeight)
                : Math.Max(candidateWidth, candidateHeight);
            if (orientedWidth >= widthIn * 0.95 || orientedHeight >= heightIn * 0.95)
                continue;

            double inferredDpiX = pixelWidth / orientedWidth;
            double inferredDpiY = pixelHeight / orientedHeight;
            if (inferredDpiX is < 95 or > 700 || inferredDpiY is < 95 or > 700)
                continue;

            standardWidthIn = orientedWidth;
            standardHeightIn = orientedHeight;
            standardName = $"{name} ({orientedWidth:0.##} x {orientedHeight:0.##})";
            return true;
        }

        return false;
    }
}
