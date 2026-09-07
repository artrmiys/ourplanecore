using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace OurPlanCore;

public partial class MainWindow
{
    private PdfOutputPreviewWindow? _pdfOutputPreview;
    private Action? _queuePdfOutputPreview;

    private Button BuildPdfPreviewButton()
    {
        var button = new Button
        {
            Content = "Preview", Margin = new Thickness(5, 2, 5, 2),
            ToolTip = "Preview the current sheet's exported PDF. Updates live when PDF Output settings change.",
            Style = TryFindResource("TopCommandButton") as Style,
        };
        button.Click += (_, _) => OpenPdfOutputPreview();
        return button;
    }

    private void QueuePdfOutputPreview() => _queuePdfOutputPreview?.Invoke();

    private void OpenPdfOutputPreview()
    {
        if (!RequireModule(ModuleId.PdfOutput, "PDF Preview")) return;
        PageInfo? page = _detachedSheetNavigationTarget?.Page ?? _currentPage;
        if (page == null || _currentJob == null)
        {
            TxtStatus.Text = "Open a sheet to preview PDF output.";
            return;
        }
        _pdfOutputPreview?.Close();
        var window = new PdfOutputPreviewWindow(page.Name)
        {
            Owner = this, ZoomWheelFactor = _settings.ViewportZoomWheelFactor,
        };
        _pdfOutputPreview = window;
        OurPlanCoreJob job = _currentJob;
        var session = new PdfExporter.PreviewSession();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        int revision = 0;
        bool rendering = false;
        bool closed = false;

        void Queue()
        {
            if (closed) return;
            window.ZoomWheelFactor = _settings.ViewportZoomWheelFactor;
            revision++;
            window.SetUpdating();
            if (!rendering) timer.Start();
        }

        async void Render(object? sender, EventArgs e)
        {
            timer.Stop();
            if (closed || rendering) return;
            rendering = true;
            try
            {
                int rendered;
                do
                {
                    rendered = revision;
                    if (!ReferenceEquals(job, _currentJob))
                    {
                        window.Close();
                        return;
                    }
                    PdfExportPageInput input = CapturePdfPreviewInput(page);
                    PdfExportOptions options = BuildPdfExportOptions(_settings.PdfExportIncludeMeasurements,
                        _settings.PdfExportIncludeAnnotations, _settings.PdfExportShowSheetLegend,
                        _settings.PdfExportMeasurementStrokeScale);
                    PdfSheetOverlayExportRenderer? overlay = IsModuleEnabled(ModuleId.SheetOverlay)
                        ? DrawPdfExportSheetOverlay : null;
                    PdfExporter.PreviewFrame frame = await Task.Run(() => session.Render(input, options, overlay));
                    if (!closed) window.SetFrame(frame, rendered == revision);
                } while (!closed && rendered != revision);
            }
            catch (Exception ex)
            {
                AppLog.Warn(ex, "PDF preview failed.");
                if (!closed) window.SetError($"Preview failed: {ex.Message}");
            }
            finally
            {
                rendering = false;
                if (closed) session.Dispose();
            }
        }

        timer.Tick += Render;
        window.Closed += (_, _) =>
        {
            closed = true;
            timer.Stop();
            timer.Tick -= Render;
            if (!rendering) session.Dispose();
            if (ReferenceEquals(_pdfOutputPreview, window))
            {
                _pdfOutputPreview = null;
                _queuePdfOutputPreview = null;
            }
        };
        window.SaveRequested += () => SavePdfPreview(window, page);
        _queuePdfOutputPreview = Queue;
        window.Show();
        Queue();
    }

    private PdfExportPageInput CapturePdfPreviewInput(PageInfo page)
    {
        PdfExportPageInput input;
        using (UsePageMeasurementLookup()) input = BuildPdfExportPages([page])[0];
        var annotations = _detachedSheetWindows.FirstOrDefault(w => IsSamePageFolder(w.Page.FolderPath, page.FolderPath))
            ?.Viewport.GetPageAnnotations() ?? (IsSamePageFolder(_currentPage?.FolderPath, page.FolderPath)
                ? _viewport.GetPageAnnotations() : input.Annotations);
        PdfExportTakeoffInput Snapshot(PdfExportTakeoffInput takeoff) => new(
            new TakeoffItem
            {
                Name = takeoff.Item.Name, Color = takeoff.Item.Color, MeasurementType = takeoff.Item.MeasurementType,
                CountSymbol = takeoff.Item.CountSymbol, IsJoistTakeoff = takeoff.Item.IsJoistTakeoff,
                JoistType = takeoff.Item.JoistType, JoistPitch = takeoff.Item.JoistPitch,
                JoistLengthRounding = takeoff.Item.JoistLengthRounding,
            }, takeoff.Measurements.Select(m => m.Snapshot()).ToList());
        return input with
        {
            Takeoffs = input.Takeoffs.Select(Snapshot).ToList(),
            MeasurementLayers = input.MeasurementLayers?.Select(Snapshot).ToList(),
            Annotations = annotations.Select(a => new PageAnnotation
            {
                Id = a.Id, Kind = a.Kind, Text = a.Text, Color = a.Color, StrokeWidth = a.StrokeWidth,
                PageFolder = a.PageFolder, ScaleMetersPerPt = a.ScaleMetersPerPt, Hidden = a.Hidden, Points = a.Points.ToList(),
            }).ToList(),
        };
    }

    private void SavePdfPreview(PdfOutputPreviewWindow window, PageInfo page)
    {
        if (window.PdfBytes is not { } bytes) return;
        var save = new SaveFileDialog
        {
            Filter = "PDF files|*.pdf", DefaultExt = ".pdf", AddExtension = true,
            FileName = page.Name, InitialDirectory = InitialProjectExportDirectory(),
        };
        if (save.ShowDialog(window) != true || !ConfirmProjectExportDestination(save.FileName)) return;
        try
        {
            IoUtil.WriteStreamAtomic(save.FileName, stream => stream.Write(bytes));
            TxtStatus.Text = $"Saved preview PDF -> {save.FileName}";
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Could not save preview PDF.");
            window.SetError($"Could not save PDF: {ex.Message}");
        }
    }
}
