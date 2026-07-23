using System;
using System.Collections.Generic;
using System.Windows.Controls;
using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    private void AddMeasurementClipboardMenuItems(ContextMenu menu, ViewportContextRequest request)
    {
        Measurement? clickedMeasurement = request.Measurement;
        var selected = _viewport.GetSelectedMeasurements();
        int selectedCutouts = _viewport.SelectedCutRegionCount;
        bool copyClicked = selected.Count == 0 && clickedMeasurement != null;
        bool canCopy = selected.Count > 0 || selectedCutouts > 0 || copyClicked;
        string copyLabel = selectedCutouts > 0
            ? selected.Count > 0
                ? $"Copy {selected.Count} Measurement(s) + {selectedCutouts} Cutout(s)"
                : $"Copy {selectedCutouts} Cutout(s)"
            : selected.Count > 1
                ? "Copy Selected Measurements"
                : "Copy Measurement";
        menu.Items.Add(MakeMenuItem(copyLabel, canCopy, () =>
        {
            if (_viewport.CopyCurrentMeasurementAndCutRegionSelection())
                return;

            IReadOnlyList<Measurement> source = selected.Count > 0
                ? selected
                : clickedMeasurement == null
                    ? Array.Empty<Measurement>()
                    : new List<Measurement> { clickedMeasurement };
            CopyMeasurementsToClipboard(source);
        }));

        if (IsCurrentJobReadOnly)
            return;

        int clipboardCount = _measurementClipboard?.Entries.Count ?? 0;
        int cutoutClipboardCount = _viewport.CutRegionClipboardCount;
        bool pasteMixed = _viewport.HasCurrentMixedMeasurementCutRegionClipboard &&
                          clipboardCount > 0 &&
                          cutoutClipboardCount > 0;
        bool pasteCutouts = _viewport.HasCurrentCutRegionClipboard && cutoutClipboardCount > 0;
        bool pasteMeasurements = _viewport.HasCurrentMeasurementClipboard && clipboardCount > 0;
        string pasteLabel = pasteMixed
            ? $"Paste {clipboardCount} Measurement(s) + {cutoutClipboardCount} Cutout(s)"
            : pasteCutouts
                ? $"Paste {cutoutClipboardCount} Cutout(s)"
                : clipboardCount > 0
                    ? $"Paste {clipboardCount} Measurement(s) to This Sheet"
                    : "Paste Measurements to This Sheet";
        menu.Items.Add(MakeWritableViewportMenuItem(
            pasteLabel,
            _currentPage != null && (pasteMixed || pasteCutouts || pasteMeasurements),
            "paste measurements and cutouts",
            () => _viewport.PasteCurrentMeasurementAndCutRegionClipboard(
                new SKPoint(request.PdfX, request.PdfY))));

        IReadOnlyList<Measurement> selection = selected.Count > 0
            ? selected
            : clickedMeasurement == null
                ? Array.Empty<Measurement>()
                : [clickedMeasurement];
        var selectedSet = new HashSet<Measurement>(selection);
        var pointSelections = _viewport.GetSelectedPointVertexSelections()
            .Where(pointSelection => selectedSet.Contains(pointSelection.Measurement))
            .ToDictionary(
                pointSelection => pointSelection.Measurement,
                pointSelection => pointSelection.PointIndices);
        if (pointSelections.Count == 0 &&
            clickedMeasurement != null &&
            selectedSet.Contains(clickedMeasurement) &&
            OurPlanCoreJobStore.NormalizeMeasurementType(clickedMeasurement.MType) == "point" &&
            request.PointVertexIndex >= 0 &&
            request.PointVertexIndex < clickedMeasurement.Points.Count)
        {
            pointSelections[clickedMeasurement] = [request.PointVertexIndex];
        }

        bool isCountSelection = selection.Count > 0 && selection.All(measurement =>
            OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "point");
        int countMarkCount = pointSelections.Count > 0
            ? pointSelections.Values.Sum(indices => indices.Count)
            : selection.Sum(measurement => measurement.Points.Count);
        string splitLabel = isCountSelection
            ? $"Split {CountMarkLabel(countMarkCount)}..."
            : selection.Count > 1
                ? $"Split {selection.Count} Segment(s)..."
                : "Split Segment...";
        if (IsModuleEnabled(ModuleId.AdvancedTakeoffTools))
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeWritableViewportMenuItem(
                selection.Count > 1 ? $"Merge {selection.Count} Segment(s)..." : "Merge Segment...",
                selection.Count > 0,
                "merge takeoff segments",
                () => MergeSelectedMeasurementsToPromptedTakeoff(selection)));
            menu.Items.Add(MakeWritableViewportMenuItem(
                splitLabel,
                selection.Count > 0,
                isCountSelection ? "split Count marks" : "split takeoff segments",
                () => SplitSelectedMeasurementsToNewTakeoff(
                    selection,
                    explicitPointSelection: pointSelections.Count > 0 ? pointSelections : null)));
        }
    }

    private void AddPdfAiMenuItems(ItemsControl menu, ViewportContextRequest request)
    {
        menu.Items.Add(MakeMenuItem("AI crop here -> note", true, () =>
            _viewport.BeginAiCropNoteSelection(new SKPoint(request.PdfX, request.PdfY))));
        menu.Items.Add(MakeMenuItem("AI quick box here -> note", true, async () =>
            await ReadAiCropIntoNoteAsync(request)));
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

        menu.Items.Add(MakeWritableViewportMenuItem(
            "Takeoff Properties...",
            hasItem,
            "edit takeoff properties",
            () => EditViewportTakeoffProperties(item)));
        menu.Items.Add(MakeWritableViewportMenuItem(
            $"{entryTitle} Properties...",
            hasItem,
            "edit section properties",
            () => EditSectionProperties(item, measurement)));
        AddViewportCountDisplayMenuItem(menu, request);
        if (IsModuleEnabled(ModuleId.AdvancedTakeoffTools) &&
            OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area")
        {
            menu.Items.Add(MakeWritableViewportMenuItem(
                "Trace Walls Inside Area...",
                hasItem && _currentJob != null && _currentPage != null,
                "trace walls inside an area",
                () => TraceWallsFromAreaSection(item, measurement)));
            menu.Items.Add(MakeWritableViewportMenuItem(
                hasItem && item.IsJoistArea ? "Joist Properties..." : "Use Area As Joists...",
                hasItem,
                "edit joist properties",
                () => EditViewportTakeoffProperties(item)));
            menu.Items.Add(MakeWritableViewportMenuItem(
                "Refresh Regular Joists in All Area Segments",
                hasItem && item.IsJoistArea,
                "add joists",
                () => AddJoistsToAllAreas(item)));
            menu.Items.Add(MakeWritableViewportMenuItem(
                "Start Extra Joists Mode (D)",
                hasItem && item.IsJoistArea && measurement.JoistDirectionLocked,
                "add an Extra Joist",
                () => StartExtraJoistPlacement(item, measurement)));
            menu.Items.Add(MakeWritableViewportMenuItem(
                "Delete Nearest Extra Joist",
                hasItem && item.IsJoistArea && measurement.ExtraJoists.Count > 0,
                "delete an Extra Joist",
                () => _viewport.DeleteNearestExtraJoist(
                    measurement,
                    new SKPoint((float)request.PdfX, (float)request.PdfY))));
            menu.Items.Add(MakeWritableViewportMenuItem(
                "Set / Reset Joist Direction",
                hasItem,
                "set a joist direction",
                () => SetJoistDirectionForSection(item, measurement)));
            menu.Items.Add(MakeWritableViewportMenuItem(
                "Set Direction for All Areas",
                hasItem,
                "set joist directions",
                () => SetJoistDirectionForAllAreas(item, measurement)));
        }
        if (IsModuleEnabled(ModuleId.AdvancedTakeoffTools) &&
            OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "line")
        {
            IReadOnlyList<Measurement> selectedLineSources = _viewport.GetSelectedMeasurements()
                .Where(IsPointAlongLineSource)
                .ToList();
            IReadOnlyList<Measurement> pointSources =
                selectedLineSources.Count > 1 && selectedLineSources.Contains(measurement)
                    ? selectedLineSources
                    : [measurement];
            menu.Items.Add(MakeWritableViewportMenuItem(
                pointSources.Count == 1
                    ? "Create Count Points Along Line..."
                    : $"Create Count Points Along {pointSources.Count} Lines...",
                hasItem && _currentJob != null,
                "create count points along lines",
                () => CreatePointsAlongLines(pointSources, pointSources.Count == 1 ? item : null)));
        }
        menu.Items.Add(MakeWritableViewportMenuItem(
            $"Rename {entryTitle}",
            hasItem,
            "rename a takeoff section",
            () => RenameSection(item, measurement)));

        if (measurement.MType is "line" or "area")
        {
            menu.Items.Add(MakeWritableViewportMenuItem("Insert Vertex Here", true, "insert a measurement vertex", () =>
                _viewport.InsertMeasurementVertex(measurement, request.PdfX, request.PdfY)));
            menu.Items.Add(MakeWritableViewportMenuItem(
                "Remove Nearest Vertex",
                CanRemoveMeasurementVertex(measurement),
                "remove a measurement vertex",
                () => _viewport.RemoveNearestMeasurementVertex(measurement, request.PdfX, request.PdfY)));
        }

        menu.Items.Add(MakeWritableViewportMenuItem(
            $"Delete {entryTitle}",
            hasItem,
            "delete a takeoff section",
            () => DeleteSection(item, measurement)));
    }

    private void EditViewportTakeoffProperties(TakeoffItem item)
    {
        if (!EnsureCurrentJobWritable("edit takeoff properties"))
            return;

        if (FindTakeoffTreeItem(item) is not { } tvi)
        {
            TxtStatus.Text = "Takeoff item is not visible in the Takeoffs tree.";
            return;
        }

        EditTakeoffItemProperties(tvi, item);
    }

    private void AddAnnotationEditMenuItems(ContextMenu menu, ViewportContextRequest request)
    {
        PageAnnotation annotation = request.Annotation!;
        string title = MarkupTitle(annotation);
        IReadOnlyList<PageAnnotation> selected = _viewport.GetSelectedPageAnnotations();
        IReadOnlyList<PageAnnotation> source =
            selected.Count > 0 && selected.Contains(annotation)
                ? selected
                : [annotation];
        menu.Items.Add(MakeMenuItem(
            source.Count > 1 ? $"Copy {source.Count} Selected Markups" : $"Copy {title}",
            true,
            () => _viewport.CopyPageAnnotations(source)));
        menu.Items.Add(MakeWritableViewportMenuItem(
            _viewport.AnnotationClipboardCount > 0
                ? $"Paste {_viewport.AnnotationClipboardCount} Markup(s) Here"
                : "Paste Markups Here",
            _viewport.AnnotationClipboardCount > 0,
            "paste page markups",
            () => _viewport.PasteCopiedPageAnnotations(new SKPoint(request.PdfX, request.PdfY))));

        if (OurPlanCoreJobStore.NormalizePageAnnotationKind(annotation.Kind) == "note")
        {
            menu.Items.Add(MakeWritableViewportMenuItem(
                "Edit Note...",
                true,
                "edit a page note",
                () => EditPageNoteAnnotation(annotation)));
        }

        menu.Items.Add(MakeWritableViewportMenuItem(
            source.Count > 1 ? $"Delete {source.Count} Selected Markups" : $"Delete {title}",
            true,
            "delete page markups",
            () =>
            {
                if (_viewport.DeletePageAnnotations(source))
                    SaveCurrentPageAnnotations();
            }));
    }

    private void EditPageNoteAnnotation(PageAnnotation annotation)
    {
        if (!EnsureCurrentJobWritable("edit a page note"))
            return;

        string? text = ShowMultilineInputDialog("Note text:", annotation.Text, "Edit Sheet Note");
        if (text == null)
            return;

        if (_viewport.UpdatePageAnnotationText(annotation, text))
            SaveCurrentPageAnnotations();
    }

    private static string MarkupTitle(PageAnnotation annotation)
    {
        string kind = OurPlanCoreJobStore.NormalizePageAnnotationKind(annotation.Kind);
        return kind switch
        {
            "note" => "Note Markup",
            "dimension" => "Dimension Markup",
            "arrow" => "Arrow Markup",
            "rectangle" => "Box Markup",
            _ => "Drawing Markup",
        };
    }

    private MenuItem MakeWritableViewportMenuItem(
        string header,
        bool isEnabled,
        string operation,
        Action action) =>
        MakeMenuItem(header, isEnabled && !IsCurrentJobReadOnly, () =>
        {
            if (EnsureCurrentJobWritable(operation))
                action();
        });

    private void AddMeasurementAiMenuItems(ItemsControl menu, ViewportContextRequest request)
    {
        Measurement measurement = request.Measurement!;
        menu.Items.Add(MakeMenuItem("AI crop here -> note", true, async () =>
            await ReadAiCropIntoNoteAsync(request)));
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
