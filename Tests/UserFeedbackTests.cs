using System.Reflection;
using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;
using OurPlanCore;
using OurPlanCore.Controls;
using SkiaSharp;

internal static class UserFeedbackTests
{
    public static void BeamOffsetPreservesLengthAndOpposesCount()
    {
        foreach (SKPoint end in new[] { new SKPoint(200, 0), new SKPoint(0, 200), new SKPoint(120, 160), new SKPoint(-120, -160) })
        foreach (float side in new[] { -1f, 1f })
        {
            SKPoint start = new(50, 50);
            SKPoint finish = start + end;
            SKPoint center = start + new SKPoint(end.X / 2, end.Y / 2);
            SKPoint count = center + new SKPoint(-end.Y * side, end.X * side);
            SKPoint offset = BeamTakeoffService.DimensionOffset(start, finish, count, 28);
            Check(offset.X * (count.X - center.X) + offset.Y * (count.Y - center.Y) < 0, "dimension must oppose Count");
            Near(28, MeasurementGeometry.Distance(SKPoint.Empty, offset), "offset magnitude");
            Near(MeasurementGeometry.Distance(start, finish), MeasurementGeometry.Distance(start + offset, finish + offset), "measured length");
        }
        Near(0, BeamAnnotationConfig.NormalizeDimensionOffset(-12), "offset lower bound");
        Near(28, BeamAnnotationConfig.NormalizeDimensionOffset(double.NaN), "invalid offset fallback");
        Near(42, new BeamAnnotationConfig { DimensionOffsetPx = 42 }.Clone().DimensionOffsetPx, "settings clone");
    }

    public static void JoistNotePersistsThroughBothFormats()
    {
        WithFolder(root =>
        {
            Measurement area = Area();
            area.JoistNoteOffsetX = 190;
            area.JoistNoteOffsetY = -35;
            area.JoistNotePositionSet = true;
            string folder = Path.Combine(root, "Takeoffs", "Joists");
            Directory.CreateDirectory(folder);
            var item = new TakeoffItem { FolderPath = folder, Name = "Joists", MeasurementType = "area", IsJoistTakeoff = true, JoistMoveNote = true };
            item.Measurements.Add(area);
            TakeoffStore.SaveMeasurements(folder, item.Measurements);
            Measurement loaded = TakeoffStore.LoadMeasurements(folder).Single();
            Near(190, loaded.JoistNoteOffsetX, "measurements X");
            Near(-35, loaded.JoistNoteOffsetY, "measurements Y");
            Check(loaded.JoistNotePositionSet, "measurement explicit position survives save");
            string pdf = Path.Combine(root, "sheet.pdf");
            ProjectFile.Save(pdf, 0.01, UnitMode.Imperial, [item]);
            TakeoffItem restored = ProjectFile.Restore(pdf).items.Single();
            Check(restored.JoistMoveNote, "legacy takeoff move flag");
            Near(190, restored.Measurements.Single().JoistNoteOffsetX, "legacy X");
            Near(-35, restored.Measurements.Single().JoistNoteOffsetY, "legacy Y");
            Check(restored.Measurements.Single().JoistNotePositionSet, "legacy explicit position survives save");
            item.JoistMoveNote = false;
            OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
            Check(!area.JoistMoveNote, "turning off locks movement");
            Near(190, area.JoistNoteOffsetX, "locking preserves position");
            Check(!new Measurement().JoistMoveNote && !new TakeoffItem().JoistMoveNote, "movement defaults off");
        });
    }

    public static void JoistNoteDragUndoCancelAndReadOnly()
    {
        RunSta(() =>
        {
            Measurement area = Area();
            var viewport = new PdfViewport();
            Set(viewport, "_pageFolder", area.PageFolder);
            viewport.SetMeasurements([area]);
            SKRect bounds = (SKRect)Call(viewport, "JoistNoteBounds", area)!;
            SKPoint start = new(bounds.MidX, bounds.MidY);
            Check(!(bool)Call(viewport, "TryBeginJoistNoteDrag", start)!, "default note is locked");
            area.JoistMoveNote = true;
            Check((bool)Call(viewport, "TryBeginJoistNoteDrag", start)!, "enabled note is draggable");
            Call(viewport, "UpdateJoistNoteDrag", start + new SKPoint(125, 40));
            Call(viewport, "FinishJoistNoteDrag", false);
            Near(start.X + 125, area.JoistNoteAnchor().X, "drag follows table center X");
            Near(start.Y + 40, area.JoistNoteAnchor().Y, "drag follows table center Y");
            viewport.UndoLast();
            Near(0, area.JoistNoteOffsetX, "undo X");
            Check(!area.JoistNotePositionSet, "undo restores default placement");
            Check((bool)Call(viewport, "TryBeginJoistNoteDrag", start)!, "second drag");
            Call(viewport, "UpdateJoistNoteDrag", start + new SKPoint(70, 60));
            Call(viewport, "FinishJoistNoteDrag", true);
            Near(0, area.JoistNoteOffsetX, "cancel X");
            viewport.IsReadOnlyMode = true;
            Check(!(bool)Call(viewport, "TryBeginJoistNoteDrag", start)!, "read-only blocks note drag");
            Check(area.Points.SequenceEqual(Area().Points), "moving note cannot move geometry");
        });
    }

    public static void PreviewMatchesExportAndReactsToSettings()
    {
        WithFolder(root =>
        {
            OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(root, "Preview");
            PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "Preview sheet", job.PagesRoot);
            Measurement area = Area();
            area.PageFolder = page.FolderPath;
            area.JoistNoteOffsetX = 200;
            var item = new TakeoffItem { Name = "Test joists", IsJoistTakeoff = true, MeasurementType = "area" };
            var input = new PdfExportPageInput(page, [new(item, [area])],
                [new PageAnnotation { Kind = "dimension", Points = [new(100, 600), new(500, 600)], ScaleMetersPerPt = 0.01 }]);
            var options = new PdfExportOptions(true, true, true, UnitMode.Imperial, "top-right", 1, 1, true, true, true, true, 1, 1, 1);
            using var session = new PdfExporter.PreviewSession();
            PdfExporter.PreviewFrame first = session.Render(input, options);
            string export = Path.Combine(root, "export.pdf");
            var result = PdfExporter.TryExport([input], export, options);
            Check(result.Ok, result.Error);
            Check(RenderPdf(first.PdfBytes).SequenceEqual(RenderPdf(File.ReadAllBytes(export))), "preview must match actual exported PDF pixels");
            var changed = options with { IncludeMeasurements = false, IncludeLegend = false, IncludeAnnotations = false };
            PdfExporter.PreviewFrame second = session.Render(input, changed);
            Check(!RenderPdf(first.PdfBytes).SequenceEqual(RenderPdf(second.PdfBytes)), "include settings must update the preview");
            PdfExporter.PreviewFrame third = session.Render(input, options with { LegendScale = 3, MeasurementLabelScale = 2.5 });
            Check(!RenderPdf(first.PdfBytes).SequenceEqual(RenderPdf(third.PdfBytes)), "size settings must update the preview");
        });
    }

    internal static Measurement Area() => new()
    {
        MType = "area", PageFolder = Path.Combine(Path.GetTempPath(), "onc-note-page"), JoistEnabled = true,
        JoistDirectionLocked = true, JoistType = "TJI", ScaleMetersPerPt = 0.01,
        Points = [new(100, 100), new(400, 100), new(400, 400), new(100, 400)],
    };

    private static byte[] RenderPdf(byte[] bytes)
    {
        using var doc = DocLib.Instance.GetDocReader(bytes, new PageDimensions(1100, 1100));
        using var page = doc.GetPageReader(0);
        return page.GetImage();
    }

    internal static object? Call(object target, string name, params object?[] args) =>
        target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    internal static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Near(double expected, double actual, string message) =>
        Check(Math.Abs(expected - actual) < 0.001, $"{message}: expected {expected}, got {actual}");
    private static void WithFolder(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "onc-feedback-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally
        {
            // PDF metadata readers may keep a cached source handle until process exit.
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new InvalidOperationException(failure.Message, failure);
    }
}
