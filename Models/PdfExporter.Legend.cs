using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;
using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

public static partial class PdfExporter
{
    private static void DrawLegend(
        SKCanvas canvas,
        float width,
        float height,
        IReadOnlyList<PdfExportTakeoffInput> takeoffs,
        PageInfo page,
        PdfExportOptions options)
    {
        var entries = takeoffs
            .Select(takeoff =>
            {
                if (takeoff.Measurements.Count == 0)
                    return null;

                TakeoffItem item = takeoff.Item;
                return new SheetLegendEntry(
                    item.Color,
                    item.Name,
                    SheetLegendQuantityTextForPage(item, takeoff.Measurements, page, options.UnitMode),
                    SheetLegendTypeTitle(item),
                    SheetLegendTypeSign(item),
                    [],
                    TakeoffGlyphKind(item));
            })
            .Where(entry => entry != null)
            .Cast<SheetLegendEntry>()
            .ToList();
        SheetOverlayRenderer.DrawLegend(
            canvas,
            entries,
            0,
            0,
            width,
            height,
            0,
            0,
            width,
            height,
            options.LegendAnchor,
            (float)Math.Clamp(options.LegendScale, 0.25, AppSettingsStore.PdfExportScaleMax));
    }

    private static void DrawSheetHeader(
        SKCanvas canvas,
        float width,
        float height,
        PageInfo page,
        PdfExportOptions options)
    {
        SheetOverlayRenderer.DrawHeader(
            canvas,
            0,
            0,
            width,
            height,
            0,
            0,
            width,
            height,
            FormatSheetScale(page.ScaleMetersPerPt),
            FormatSheetSize(width, height),
            (float)Math.Clamp(options.HeaderScale, 0.25, AppSettingsStore.PdfExportScaleMax));
    }

    private static string FormatSheetScale(double scaleMetersPerPt)
    {
        if (scaleMetersPerPt <= 0)
            return "Scale: not set";

        string scale = PdfSheetMetadataService.FormatImperialScale(scaleMetersPerPt);
        return string.IsNullOrWhiteSpace(scale)
            ? "Scale: not set"
            : $"Scale: {scale}";
    }

    private static string FormatSheetSize(float widthPt, float heightPt)
    {
        double widthIn = widthPt / 72.0;
        double heightIn = heightPt / 72.0;
        return $"{widthIn:F2} x {heightIn:F2}";
    }

    private static string SheetLegendQuantityTextForPage(
        TakeoffItem item,
        IReadOnlyList<Measurement> measurements,
        PageInfo page,
        UnitMode unitMode)
    {
        string measurementType = OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double fallbackScale = page.ScaleMetersPerPt;

        if (measurementType == "point")
            return Units.FormatCount(measurements.Sum(measurement => measurement.Points.Count));

        bool hasScale = fallbackScale > 0 || measurements.Any(measurement => measurement.ScaleMetersPerPt > 0);
        if (item.IsJoistArea)
        {
            return hasScale
                ? Units.FormatArea(measurements.Sum(measurement => measurement.AreaValue(fallbackScale)), unitMode)
                : $"{measurements.Sum(measurement => measurement.Points.Count)} pts";
        }

        if (!hasScale)
        {
            if (measurementType == "line")
                return $"{measurements.Sum(measurement => Math.Max(0, measurement.Points.Count - 1))} seg";
            if (measurementType == "area")
                return $"{measurements.Sum(measurement => measurement.Points.Count)} pts";
        }

        double total = measurements.Sum(measurement => measurement.Value(fallbackScale));
        return measurementType switch
        {
            "line" => Units.FormatLength(total, unitMode),
            "area" => Units.FormatArea(total, unitMode),
            _ => Units.FormatCount(total),
        };
    }

    private static string SheetLegendTypeTitle(TakeoffItem item) =>
        item.IsJoistArea ? "Area" : TakeoffTypeTitle(item);

    private static string SheetLegendTypeSign(TakeoffItem item) =>
        item.IsJoistArea ? MeasurementTypeSign("area") : TakeoffTypeSign(item);

    private static MeasurementGlyphKind TakeoffGlyphKind(TakeoffItem item) =>
        MeasurementGlyph.Parse(
            OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType),
            joist: item.IsJoistArea,
            countSymbol: item.CountSymbol);

    private static string TakeoffTypeTitle(TakeoffItem item) =>
        item.IsJoistArea ? "Joist" : MeasurementTypeTitle(item.MeasurementType);

    private static string MeasurementTypeTitle(string measurementType) =>
        OurPlanCoreJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "point" => "Count",
            "area" => "Area",
            _ => "Line",
        };

    private static string TakeoffTypeSign(TakeoffItem item) =>
        item.IsJoistArea ? "РІвЂ“РЋРІвЂўВ±" : MeasurementTypeSign(item.MeasurementType);

    private static string MeasurementTypeSign(string measurementType) =>
        OurPlanCoreJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "point" => "РІвЂ”вЂ№",
            "area" => "РІвЂ“РЋ",
            _ => "РІвЂўВ±",
        };
}
