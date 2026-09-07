using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private const string ViewportAreaPreviewSmokeEnv = "OURPLANCORE_VIEWPORT_AREA_PREVIEW_SMOKE";
    private const string ViewportAreaPreviewSmokeReportEnv = "OURPLANCORE_VIEWPORT_AREA_PREVIEW_REPORT";

    private async Task<bool> TryRunViewportAreaPreviewSmokeAsync()
    {
        if (!IsTruthyEnvironment(ViewportAreaPreviewSmokeEnv))
            return false;

        var report = new ViewportAreaPreviewSmokeReport
        {
            StartedUtc = DateTime.UtcNow,
            ReportPath = ResolveViewportAreaPreviewSmokeReportPath(),
        };
        bool ownsRecorder = false;

        try
        {
            if (_currentJob == null || _currentPage == null)
                throw new InvalidOperationException("No current job page was opened before Area preview smoke.");
            if (ViewportPerformanceRecorder.IsActive)
                throw new InvalidOperationException("Another viewport performance recorder run is already active.");

            await WaitForViewportPageRenderAsync(_currentPage, timeoutMs: 15000);
            await WaitForViewportPagePaintAsync(_currentPage, timeoutMs: 15000);
            report.JobPath = _currentJob.RootPath;
            report.PageFolder = _currentPage.FolderPath;

            ViewportPerformanceRecorder.BeginRun(_currentJob.RootPath, "viewport-area-preview-smoke");
            ownsRecorder = true;
            report.Probe = await _viewport.RunAreaPreviewPerformanceProbeAsync();
        }
        catch (Exception ex)
        {
            report.Failures.Add(ex.ToString());
        }
        finally
        {
            if (ownsRecorder)
                report.Performance = ViewportPerformanceRecorder.EndRun();

            report.FinishedUtc = DateTime.UtcNow;
            report.Passed = report.Failures.Count == 0 &&
                            report.Probe?.Passed == true &&
                            report.Performance?.Summary.PaintFrameCount > 0;
            WriteViewportAreaPreviewSmokeReport(report);
            Application.Current.Shutdown(report.Passed ? 0 : 2);
        }

        return true;
    }

    private static string ResolveViewportAreaPreviewSmokeReportPath()
    {
        string? configured = Environment.GetEnvironmentVariable(ViewportAreaPreviewSmokeReportEnv);
        return !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : Path.Combine(Path.GetTempPath(), $"ourplancore-area-preview-{Environment.ProcessId}.json");
    }

    private static void WriteViewportAreaPreviewSmokeReport(ViewportAreaPreviewSmokeReport report)
    {
        string? directory = Path.GetDirectoryName(report.ReportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(
            report.ReportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        AppLog.Info($"Viewport Area preview smoke report: '{report.ReportPath}'; passed={report.Passed}.");
    }

    private sealed class ViewportAreaPreviewSmokeReport
    {
        public bool Passed { get; set; }
        public string ReportPath { get; set; } = "";
        public string JobPath { get; set; } = "";
        public string PageFolder { get; set; } = "";
        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }
        public AreaPreviewPerformanceProbeResult? Probe { get; set; }
        public ViewportPerformanceRun? Performance { get; set; }
        public List<string> Failures { get; set; } = [];
    }
}
