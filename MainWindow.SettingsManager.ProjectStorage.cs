using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OurPlanCore;

public partial class MainWindow
{
    private int _projectStorageAnalysisGeneration;
    private CancellationTokenSource? _projectStorageAnalysisCancellation;
    private ProjectStorageAnalysis? _projectStorageAnalysis;
    private ProjectStorageCompactionPlan? _projectStorageCompactionPlan;
    private string _projectStorageAnalysisJobRoot = "";
    private TextBlock? _projectStorageStatus;
    private TextBlock? _projectStorageOverview;
    private StackPanel? _projectStorageCategories;
    private TextBlock? _projectStorageSafePreview;
    private TextBlock? _projectStorageReviewFindings;
    private TextBlock? _projectStorageDetails;
    private Button? _projectStorageAnalyzeButton;
    private Button? _projectStoragePreviewButton;
    private Button? _projectStorageCompactButton;
    private ComboBox? _newProjectStorageFormatCombo;

    private FrameworkElement BuildProjectStoragePanel()
    {
        var root = new DockPanel { Margin = new Thickness(2) };

        var formatPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        formatPanel.Children.Add(Header("Default format for new projects"));
        formatPanel.Children.Add(new TextBlock
        {
            Text = "OurPlan Project stores a portable project as one .ourplan file. Legacy folder remains available for compatibility.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });
        var formatRow = new WrapPanel();
        _newProjectStorageFormatCombo = new ComboBox
        {
            Width = 310,
            Margin = new Thickness(0, 0, 8, 0),
            ItemsSource = new[]
            {
                "OurPlan Project (*.ourplan) — Recommended",
                "Legacy project folder",
            },
            SelectedIndex = UseLegacyFolderForNewProjects() ? 1 : 0,
        };
        _newProjectStorageFormatCombo.SelectionChanged += (_, _) =>
        {
            _settings.NewProjectStorageFormat = _newProjectStorageFormatCombo.SelectedIndex == 1
                ? "LegacyFolder"
                : "OurPlan";
            SaveAppSettings();
        };
        formatRow.Children.Add(_newProjectStorageFormatCombo);
        formatRow.Children.Add(MgrButton("Reset recommended", (_, _) =>
        {
            _settings.NewProjectStorageFormat = "OurPlan";
            _newProjectStorageFormatCombo.SelectedIndex = 0;
            SaveAppSettings();
        }));
        formatPanel.Children.Add(formatRow);
        DockPanel.SetDock(formatPanel, Dock.Top);
        root.Children.Add(formatPanel);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        _projectStorageAnalyzeButton = MgrButton(
            "Analyze project",
            async (_, _) => await AnalyzeProjectStorageAsync(previewOnly: false),
            primary: true);
        _projectStoragePreviewButton = MgrButton(
            "Preview safe compact",
            async (_, _) => await AnalyzeProjectStorageAsync(previewOnly: true));
        actions.Children.Add(_projectStorageAnalyzeButton);
        actions.Children.Add(_projectStoragePreviewButton);
        _projectStorageCompactButton = MgrButton(
            "Compact snap.json...",
            async (_, _) => await CompactProjectStorageAsync());
        _projectStorageCompactButton.IsEnabled = false;
        actions.Children.Add(_projectStorageCompactButton);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        _projectStorageStatus = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
        };
        DockPanel.SetDock(_projectStorageStatus, Dock.Top);
        root.Children.Add(_projectStorageStatus);

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "Project Storage keeps the job portable while showing where its disk space is used. " +
                   "Analysis is read-only. Duplicate and orphan source files are review findings only and are never removed here.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        content.Children.Add(Header("Current project"));
        _projectStorageOverview = StorageTextBlock();
        content.Children.Add(_projectStorageOverview);

        content.Children.Add(Header("Storage breakdown"));
        _projectStorageCategories = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        content.Children.Add(_projectStorageCategories);

        content.Children.Add(Header("Safe compact preview"));
        _projectStorageSafePreview = StorageTextBlock();
        content.Children.Add(_projectStorageSafePreview);

        content.Children.Add(Header("Review-only findings"));
        _projectStorageReviewFindings = StorageTextBlock();
        content.Children.Add(_projectStorageReviewFindings);

        content.Children.Add(Header("Details"));
        _projectStorageDetails = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.5,
            Margin = new Thickness(0, 0, 8, 10),
        };
        content.Children.Add(_projectStorageDetails);

        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content,
        });

        BindProjectStorageSettings();
        return root;
    }

    private bool UseLegacyFolderForNewProjects() =>
        string.Equals(
            _settings.NewProjectStorageFormat,
            "LegacyFolder",
            StringComparison.OrdinalIgnoreCase);

    private static TextBlock StorageTextBlock() => new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10),
    };

    private void BindProjectStorageSettings()
    {
        if (_projectStorageStatus == null)
            return;

        if (_currentJob == null)
        {
            InvalidateProjectStorageAnalysis();
            _projectStorageStatus.Text = "Open a job to analyze project storage.";
            RenderEmptyProjectStorage("No job is open.");
            return;
        }

        if (!SameJobPath(_projectStorageAnalysisJobRoot, _currentJob.RootPath) ||
            _projectStorageAnalysis == null)
        {
            InvalidateProjectStorageAnalysis();
            _projectStorageStatus.Text =
                $"Ready to analyze {_currentJob.Name}. Large source PDFs are hashed only when equal-size candidates exist.";
            RenderEmptyProjectStorage("Press Analyze project to build a read-only report.");
            return;
        }

        RenderProjectStorageAnalysis(_projectStorageAnalysis, previewOnly: false);
    }

    private void InvalidateProjectStorageAnalysis()
    {
        _projectStorageAnalysisGeneration++;
        _projectStorageAnalysisCancellation?.Cancel();
        _projectStorageAnalysisCancellation?.Dispose();
        _projectStorageAnalysisCancellation = null;
        _projectStorageAnalysis = null;
        _projectStorageCompactionPlan = null;
        _projectStorageAnalysisJobRoot = "";
        if (_projectStorageCompactButton != null)
            _projectStorageCompactButton.IsEnabled = false;
    }

    private async Task AnalyzeProjectStorageAsync(bool previewOnly)
    {
        if (_currentJob == null)
        {
            if (_projectStorageStatus != null)
                _projectStorageStatus.Text = "Open a job before analyzing storage.";
            return;
        }

        string jobRoot = _currentJob.RootPath;
        string jobName = _currentJob.Name;
        _projectStorageAnalysisCancellation?.Cancel();
        _projectStorageAnalysisCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _projectStorageAnalysisCancellation = cancellation;
        _projectStorageCompactionPlan = null;
        int generation = ++_projectStorageAnalysisGeneration;
        SetProjectStorageButtonsEnabled(false);
        if (_projectStorageStatus != null)
        {
            _projectStorageStatus.Text = previewOnly
                ? $"Building safe compact preview for {jobName}..."
                : $"Analyzing {jobName}...";
        }

        try
        {
            (ProjectStorageAnalysis Analysis, ProjectStorageCompactionPlan? Plan) outcome = await Task.Run(
                () =>
                {
                    ProjectStorageAnalysis analysis =
                        ProjectStorageAnalyzer.Analyze(jobRoot, cancellation.Token);
                    ProjectStorageCompactionPlan? plan = previewOnly
                        ? ProjectStorageCompactor.BuildPlan(jobRoot, cancellation.Token)
                        : null;
                    return (analysis, plan);
                },
                cancellation.Token);
            if (generation != _projectStorageAnalysisGeneration ||
                _currentJob == null ||
                !SameJobPath(jobRoot, _currentJob.RootPath))
            {
                return;
            }

            _projectStorageAnalysis = outcome.Analysis;
            _projectStorageCompactionPlan = outcome.Plan;
            _projectStorageAnalysisJobRoot = jobRoot;
            RenderProjectStorageAnalysis(outcome.Analysis, previewOnly);
        }
        catch (OperationCanceledException)
        {
            // A newer analysis or a job switch owns the panel now.
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Project storage analysis failed for '{jobRoot}'.");
            if (generation == _projectStorageAnalysisGeneration && _projectStorageStatus != null)
                _projectStorageStatus.Text = $"Storage analysis failed: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_projectStorageAnalysisCancellation, cancellation))
            {
                _projectStorageAnalysisCancellation = null;
                cancellation.Dispose();
            }
            if (generation == _projectStorageAnalysisGeneration)
                SetProjectStorageButtonsEnabled(true);
        }
    }

    private void SetProjectStorageButtonsEnabled(bool enabled)
    {
        if (_projectStorageAnalyzeButton != null)
            _projectStorageAnalyzeButton.IsEnabled = enabled;
        if (_projectStoragePreviewButton != null)
            _projectStoragePreviewButton.IsEnabled = enabled;
        if (_projectStorageCompactButton != null)
        {
            _projectStorageCompactButton.IsEnabled =
                enabled &&
                IsCurrentJobWritable &&
                _projectStorageCompactionPlan?.TotalPotentialSavingsBytes > 0;
        }
    }

    private void RenderEmptyProjectStorage(string overview)
    {
        if (_projectStorageOverview != null)
            _projectStorageOverview.Text = overview;
        _projectStorageCategories?.Children.Clear();
        if (_projectStorageSafePreview != null)
            _projectStorageSafePreview.Text = "No preview yet.";
        if (_projectStorageReviewFindings != null)
            _projectStorageReviewFindings.Text = "No findings yet.";
        if (_projectStorageDetails != null)
            _projectStorageDetails.Text = "";
    }

    private void RenderProjectStorageAnalysis(ProjectStorageAnalysis analysis, bool previewOnly)
    {
        if (_projectStorageOverview != null)
        {
            _projectStorageOverview.Text =
                $"{Path.GetFileName(analysis.JobRoot)}  |  {analysis.Files.Count:N0} files  |  " +
                $"{FormatProjectStorageBytes(analysis.TotalBytes)} total";
        }

        RenderProjectStorageCategories(analysis);

        int validSnapFiles = analysis.SnapJsonReports.Count(report => report.IsValidJson);
        int compactableFiles = _projectStorageCompactionPlan?.CompactableFileCount ??
            analysis.SnapJsonReports.Count(report => report.IsValidJson && report.PotentialSavingsBytes > 0);
        long compactSavings = _projectStorageCompactionPlan?.TotalPotentialSavingsBytes ??
            analysis.PotentialSnapJsonSavingsBytes;
        if (_projectStorageSafePreview != null)
        {
            _projectStorageSafePreview.Text =
                $"{validSnapFiles:N0} valid raster snap.json file(s); {compactableFiles:N0} currently compactable. " +
                $"Formatting-only compact can save {FormatProjectStorageBytes(compactSavings)}. " +
                "This preserves the same JSON data and does not remove source PDFs, recovery history, or active raster images.";
        }

        ProjectStorageCategorySummary duplicates = analysis.Category(ProjectStorageCategory.ExactDuplicateSource);
        ProjectStorageCategorySummary unreferenced =
            analysis.Category(ProjectStorageCategory.UnreferencedPageSource);
        ProjectStorageCategorySummary needsReview =
            analysis.Category(ProjectStorageCategory.SourceNeedsReview);
        if (_projectStorageReviewFindings != null)
        {
            _projectStorageReviewFindings.Text =
                $"Exact duplicate source candidates: {duplicates.FileCount:N0} / {FormatProjectStorageBytes(duplicates.Bytes)}. " +
                $"Not referenced by page metadata: {unreferenced.FileCount:N0} / {FormatProjectStorageBytes(unreferenced.Bytes)}. " +
                $"Needs review after incomplete metadata scan: {needsReview.FileCount:N0} / {FormatProjectStorageBytes(needsReview.Bytes)}. " +
                $"External page dependencies: {analysis.ExternalReferenceCount:N0}. " +
                "These figures are informational; paths can still be needed by external workflows or future recovery.";
        }

        if (_projectStorageDetails != null)
            _projectStorageDetails.Text = BuildProjectStorageDetails(analysis);

        if (_projectStorageStatus != null)
        {
            _projectStorageStatus.Text = previewOnly
                ? $"Safe compact preview ready: {FormatProjectStorageBytes(compactSavings)} available."
                : $"Analysis complete ({(analysis.ReferenceScanComplete ? "all page references read" : "metadata review required")}). " +
                  $"{analysis.ExternalReferenceCount:N0} external dependency(ies); " +
                  $"{analysis.Warnings.Count:N0} warning(s); no files changed.";
        }

        SetProjectStorageButtonsEnabled(true);
    }

    private async Task CompactProjectStorageAsync()
    {
        if (_currentJob == null || !IsCurrentJobWritable)
        {
            if (_projectStorageStatus != null)
                _projectStorageStatus.Text = "Open this job with write access before compacting.";
            return;
        }

        ProjectStorageCompactionPlan? plan = _projectStorageCompactionPlan;
        string jobRoot = _currentJob.RootPath;
        if (plan == null || !SameJobPath(plan.JobRoot, jobRoot))
        {
            if (_projectStorageStatus != null)
                _projectStorageStatus.Text = "Run Preview safe compact first.";
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            $"Compact {plan.CompactableFileCount:N0} raster snap.json file(s) and save approximately " +
            $"{FormatProjectStorageBytes(plan.TotalPotentialSavingsBytes)}?\n\n" +
            "Only JSON formatting whitespace will be removed. Source PDFs, active raster images, " +
            "recovery history, takeoffs, and measurements will not be deleted.",
            "Compact Project Storage",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information,
            MessageBoxResult.Cancel);
        if (confirmation != MessageBoxResult.OK)
            return;

        int generation = ++_projectStorageAnalysisGeneration;
        var cancellation = new CancellationTokenSource();
        _projectStorageAnalysisCancellation = cancellation;
        SetProjectStorageButtonsEnabled(false);
        if (_projectStorageStatus != null)
            _projectStorageStatus.Text = "Compacting snap.json files atomically...";

        try
        {
            ProjectStorageCompactionResult result = await Task.Run(() =>
            {
                using IDisposable? writeActivity =
                    JobFileWriteActivity.TryBeginBackgroundWriteForProjectPath(jobRoot);
                if (writeActivity == null)
                    throw new OperationCanceledException("A package checkpoint started before compaction.");
                return ProjectStorageCompactor.Execute(plan, cancellation.Token);
            },
                cancellation.Token);
            if (generation != _projectStorageAnalysisGeneration ||
                _currentJob == null ||
                !SameJobPath(jobRoot, _currentJob.RootPath))
            {
                return;
            }

            _projectStorageCompactionPlan = null;
            _projectStorageAnalysis = null;
            _projectStorageAnalysisJobRoot = "";
            string resultText =
                $"Compacted {result.CompactedFileCount:N0} file(s); saved " +
                $"{FormatProjectStorageBytes(result.BytesSaved)}; {result.IssueCount:N0} issue(s).";
            if (_projectStorageStatus != null)
                _projectStorageStatus.Text = resultText + " Run Analyze project to refresh the report.";
            TxtStatus.Text = resultText;
            RenderEmptyProjectStorage(resultText);
        }
        catch (OperationCanceledException)
        {
            // A job switch or window shutdown canceled the active compact safely.
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Project storage compact failed for '{jobRoot}'.");
            if (_projectStorageStatus != null)
            {
                _projectStorageStatus.Text =
                    $"Compact stopped: {ex.Message} Some already-processed snap.json files may be compacted; " +
                    "no project files were deleted.";
            }
        }
        finally
        {
            if (ReferenceEquals(_projectStorageAnalysisCancellation, cancellation))
            {
                _projectStorageAnalysisCancellation = null;
                cancellation.Dispose();
            }
            if (generation == _projectStorageAnalysisGeneration)
                SetProjectStorageButtonsEnabled(true);
        }
    }

    private void RenderProjectStorageCategories(ProjectStorageAnalysis analysis)
    {
        if (_projectStorageCategories == null)
            return;

        _projectStorageCategories.Children.Clear();
        ProjectStorageCategory[] order =
        [
            ProjectStorageCategory.Canonical,
            ProjectStorageCategory.RebuildableRaster,
            ProjectStorageCategory.RecoveryHistory,
            ProjectStorageCategory.ExactDuplicateSource,
            ProjectStorageCategory.UnreferencedPageSource,
            ProjectStorageCategory.SourceNeedsReview,
            ProjectStorageCategory.Other,
        ];
        foreach (ProjectStorageCategory category in order)
        {
            ProjectStorageCategorySummary summary = analysis.Category(category);
            _projectStorageCategories.Children.Add(new TextBlock
            {
                Text = $"{ProjectStorageCategoryLabel(category),-30} {summary.FileCount,7:N0} files   " +
                       $"{FormatProjectStorageBytes(summary.Bytes),12}",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10.5,
                Margin = new Thickness(0, 1, 0, 1),
            });
        }
    }

    private static string BuildProjectStorageDetails(ProjectStorageAnalysis analysis)
    {
        var lines = new List<string>();
        foreach (ProjectStorageDuplicateGroup group in analysis.DuplicateGroups.Take(8))
        {
            lines.Add($"DUP  {FormatProjectStorageBytes(group.PotentialSavingsBytes),9}  keep {group.RetainedPath}");
            lines.AddRange(group.DuplicatePaths.Take(3).Select(path => $"     candidate {path}"));
        }

        foreach (ProjectStorageFileEntry orphan in analysis.Files
                     .Where(file => file.Category is ProjectStorageCategory.UnreferencedPageSource or
                         ProjectStorageCategory.SourceNeedsReview)
                     .OrderByDescending(file => file.Length)
                     .Take(12))
        {
            lines.Add($"REVIEW {FormatProjectStorageBytes(orphan.Length),9}  {orphan.RelativePath}");
        }

        foreach (string warning in analysis.Warnings.Take(10))
            lines.Add($"WARN {warning}");

        if (lines.Count == 0)
            lines.Add("No duplicate, orphan, or warning details.");
        else if (analysis.DuplicateGroups.Count > 8 ||
                 analysis.Category(ProjectStorageCategory.UnreferencedPageSource).FileCount +
                     analysis.Category(ProjectStorageCategory.SourceNeedsReview).FileCount > 12 ||
                 analysis.Warnings.Count > 10)
        {
            lines.Add("...additional findings omitted from this compact view.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ProjectStorageCategoryLabel(ProjectStorageCategory category) => category switch
    {
        ProjectStorageCategory.Canonical => "Portable project data",
        ProjectStorageCategory.RebuildableRaster => "Rebuildable page raster",
        ProjectStorageCategory.RecoveryHistory => "Recovery history",
        ProjectStorageCategory.ExactDuplicateSource => "Duplicate source (review)",
        ProjectStorageCategory.UnreferencedPageSource => "Not page-referenced (review)",
        ProjectStorageCategory.SourceNeedsReview => "Source needs metadata review",
        _ => "Other files",
    };

    private static string FormatProjectStorageBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int suffix = 0;
        while (value >= 1024 && suffix < suffixes.Length - 1)
        {
            value /= 1024;
            suffix++;
        }

        return suffix == 0 ? $"{value:N0} {suffixes[suffix]}" : $"{value:N1} {suffixes[suffix]}";
    }
}
