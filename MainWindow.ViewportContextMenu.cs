using System;
using System.Collections.Generic;
using System.Windows.Controls;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void AddMeasurementClipboardMenuItems(ContextMenu menu, ViewportContextRequest request)
    {
        Measurement? clickedMeasurement = request.Measurement;
        var selected = _viewport.GetSelectedMeasurements();
        bool copyClicked = selected.Count == 0 && clickedMeasurement != null;
        bool canCopy = selected.Count > 0 || copyClicked;
        string copyLabel = selected.Count > 1 ? "Copy Selected Measurements" : "Copy Measurement";
        menu.Items.Add(MakeMenuItem(copyLabel, canCopy, () =>
        {
            IReadOnlyList<Measurement> source = selected.Count > 0
                ? selected
                : clickedMeasurement == null
                    ? Array.Empty<Measurement>()
                    : new List<Measurement> { clickedMeasurement };
            CopyMeasurementsToClipboard(source);
        }));

        int clipboardCount = _measurementClipboard?.Entries.Count ?? 0;
        menu.Items.Add(MakeMenuItem(
            clipboardCount > 0 ? $"Paste {clipboardCount} Measurement(s) to This Sheet" : "Paste Measurements to This Sheet",
            _currentPage != null && clipboardCount > 0,
            () => PasteMeasurementsFromClipboard(new SKPoint(request.PdfX, request.PdfY))));
    }

    private void AddPdfAiMenuItems(ContextMenu menu, ViewportContextRequest request)
    {
        menu.Items.Add(MakeMenuItem("Save AI crop here", true, () =>
            SaveAiCropObservation(request)));
        menu.Items.Add(MakeMenuItem("Save AI marker here", true, () =>
            SaveAiMarker(request)));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem("Ask AI about this point / area", true, () =>
            SaveViewportObservation(
                request,
                "ai_request",
                "Ask AI",
                "Pending AI request:\nExplain what is important around this point/area on the plan.")));

        menu.Items.Add(MakeMenuItem("Read text near point", true, () =>
            SaveViewportObservation(
                request,
                "text_read_request",
                "Read Text Near Point",
                "Pending OCR request:\nRead and summarize text near this point.")));

        menu.Items.Add(MakeMenuItem("Save observation here", true, () =>
            SaveViewportObservation(
                request,
                "manual",
                "Save Observation",
                "Observation:\n")));

        menu.Items.Add(MakeMenuItem("Add as pending check", true, () =>
            SaveViewportObservation(
                request,
                "pending_check",
                "Pending Check",
                "Pending check:\nVerify this area before final takeoff.")));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem("Suggest takeoff item here", true, () =>
            SuggestTakeoffItemFromContext(request)));

        menu.Items.Add(MakeMenuItem("Trace wall from this point", true, () =>
            SaveViewportObservation(
                request,
                "trace_request",
                "Trace Wall",
                "Pending SmartTrace request:\nTrace wall/linear segment from this point, preview before apply.")));

        menu.Items.Add(MakeMenuItem("Trace closed area", true, () =>
            SaveViewportObservation(
                request,
                "trace_area_request",
                "Trace Closed Area",
                "Pending SmartTrace request:\nTrace closed area from this point, preview before apply.")));

        menu.Items.Add(MakeMenuItem("Check missed takeoffs on this page", true, () =>
            SaveViewportObservation(
                request,
                "missed_takeoff_check",
                "Check Missed Takeoffs",
                "Pending SmartCheck request:\nReview this page for possible missed takeoffs.")));
    }

    private void AddMeasurementEditMenuItems(ContextMenu menu, ViewportContextRequest request)
    {
        Measurement measurement = request.Measurement!;
        bool hasItem = TryResolveTakeoffItemForMeasurement(measurement, out TakeoffItem item);
        string entryTitle = hasItem ? MeasurementEntryTitle(item) : "Measurement";

        menu.Items.Add(MakeMenuItem(
            $"{entryTitle} Properties...",
            hasItem,
            () => EditSectionProperties(item, measurement)));
        menu.Items.Add(MakeMenuItem(
            $"Rename {entryTitle}",
            hasItem,
            () => RenameSection(item, measurement)));

        if (measurement.MType is "line" or "area")
        {
            menu.Items.Add(MakeMenuItem("Insert Vertex Here", true, () =>
                _viewport.InsertMeasurementVertex(measurement, request.PdfX, request.PdfY)));
            menu.Items.Add(MakeMenuItem(
                "Remove Nearest Vertex",
                CanRemoveMeasurementVertex(measurement),
                () => _viewport.RemoveNearestMeasurementVertex(measurement, request.PdfX, request.PdfY)));
        }

        menu.Items.Add(MakeMenuItem(
            $"Delete {entryTitle}",
            hasItem,
            () => DeleteSection(item, measurement)));
    }

    private void AddMeasurementAiMenuItems(ContextMenu menu, ViewportContextRequest request)
    {
        Measurement measurement = request.Measurement!;
        menu.Items.Add(MakeMenuItem("Save measurement AI crop", true, () =>
            SaveAiCropObservation(request)));
        menu.Items.Add(MakeMenuItem("Save measurement as AI marker", true, () =>
            SaveAiMarker(request)));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem("Explain measurement", true, () =>
            SaveViewportObservation(
                request,
                "measurement_explain_request",
                "Explain Measurement",
                $"Pending AI request:\nExplain this measurement and whether it matches the selected takeoff item.\n\n{FormatMeasurementSummary(measurement)}")));

        menu.Items.Add(MakeMenuItem("Find similar", true, () =>
            SaveViewportObservation(
                request,
                "find_similar_request",
                "Find Similar",
                $"Pending SmartCheck request:\nFind similar measurements or plan conditions.\n\n{FormatMeasurementSummary(measurement)}")));

        menu.Items.Add(MakeMenuItem("Link to observation", true, () =>
            SaveViewportObservation(
                request,
                "measurement_link_request",
                "Link Measurement",
                $"Pending link request:\nConnect this measurement to a project observation/note.\n\n{FormatMeasurementSummary(measurement)}")));

        menu.Items.Add(MakeMenuItem("Create note from measurement", true, () =>
            SaveViewportObservation(
                request,
                "measurement_note",
                "Measurement Note",
                $"Measurement note:\n{FormatMeasurementSummary(measurement)}")));
    }
}
