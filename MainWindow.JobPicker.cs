using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private static readonly RoutedCommand OpenRecentJobsCommand =
        new(nameof(OpenRecentJobsCommand), typeof(MainWindow));

    private void ShowRecentJobPicker()
    {
        var dialog = new JobPickerDialog(
            BuildJobPickerItems(),
            _settings.JobsRootPath,
            SetRecentJobPinned,
            RemoveRecentJob,
            RemoveJobsRoot,
            AppSettingsStore.CurrentJobsRootPaths(_settings))
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
            return;

        HandleJobPickerAction(dialog.SelectedAction, dialog.SelectedJobPath, dialog.SelectedJobsRootPath);
    }

    private void ShowStartupJobPickerIfUseful()
    {
        if (_currentJob != null)
            return;

        UpdateNoJobOverlay();   // show the empty-state Start card behind the picker
        ShowRecentJobPicker();
    }

    private IReadOnlyList<JobPickerItem> BuildJobPickerItems()
    {
        var items = new List<JobPickerItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var roots = AppSettingsStore.CurrentJobsRootPaths(_settings);
        foreach (var recent in _settings.RecentJobs ?? [])
        {
            if (string.IsNullOrWhiteSpace(recent.Path))
                continue;

            string path = recent.Path.Trim();
            string rootPath = RootForJobPath(path, roots);
            AddJobPickerItem(
                items,
                seen,
                name: string.IsNullOrWhiteSpace(recent.Name) ? Path.GetFileName(path) : recent.Name.Trim(),
                path,
                thumbnailPath: File.Exists(recent.ThumbnailPath)
                    ? recent.ThumbnailPath
                    : JobThumbnailService.ExistingThumbnailPath(path),
                lastOpenedUtc: ParseRecentJobTime(recent.LastOpenedUtc),
                sourceLabel: BuildSourceLabel(rootPath, fallback: "Recent"),
                isPinned: recent.IsPinned,
                isRecent: true,
                rootPath: rootPath);
        }

        foreach (string rootPath in roots)
        {
            if (!Directory.Exists(rootPath))
                continue;

            string sourceLabel = BuildSourceLabel(rootPath, fallback: "Local");

            foreach (string folder in Directory.EnumerateDirectories(rootPath)
                         .Where(IsJobFolder)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                AddJobPickerItem(
                    items,
                    seen,
                    name: Path.GetFileName(folder),
                    path: folder,
                    thumbnailPath: JobThumbnailService.ExistingThumbnailPath(folder),
                    lastOpenedUtc: ReadJobDataMtimeUtc(folder),
                    sourceLabel: sourceLabel,
                    isPinned: false,
                    isRecent: false,
                    rootPath: rootPath);
            }
        }

        return items;
    }

    private static void AddJobPickerItem(
        List<JobPickerItem> items,
        HashSet<string> seen,
        string name,
        string path,
        string thumbnailPath,
        DateTime? lastOpenedUtc,
        string sourceLabel,
        bool isPinned,
        bool isRecent,
        string rootPath = "")
    {
        string key = NormalizeJobPath(path);
        if (!seen.Add(key))
            return;

        bool exists = Directory.Exists(path) && IsJobFolder(path);
        items.Add(new JobPickerItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name,
            Path = path,
            ThumbnailPath = thumbnailPath,
            LastOpenedUtc = lastOpenedUtc,
            SourceLabel = sourceLabel,
            Exists = exists,
            IsPinned = isPinned,
            IsRecent = isRecent,
            RootPath = rootPath,
        });
    }

    private static DateTime? ReadJobDataMtimeUtc(string jobFolder)
    {
        try
        {
            string dataXml = Path.Combine(jobFolder, "Data.xml");
            if (!File.Exists(dataXml))
                return null;

            return File.GetLastWriteTimeUtc(dataXml);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildSourceLabel(string rootPath, string fallback)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return fallback;

        string trimmed = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private void HandleJobPickerAction(JobPickerAction action, string selectedJobPath, string selectedJobsRootPath = "")
    {
        switch (action)
        {
            case JobPickerAction.OpenSelected:
                OpenJobSafely(selectedJobPath);
                break;
            case JobPickerAction.BrowseJob:
                OpenJobFromFolderDialog();
                break;
            case JobPickerAction.BrowseJobsFolder:
                OpenJobFromJobsRootDialog();
                break;
            case JobPickerAction.NewJob:
                CreateJobFromDialog(selectedJobsRootPath);
                break;
            case JobPickerAction.BlankJob:
                CreateBlankJobFromDialog(selectedJobsRootPath);
                break;
            case JobPickerAction.CreateSample:
                CreateSampleJob();
                break;
        }
    }

    private void OpenJobFromFolderDialog()
    {
        string? folder = SelectFolder("Select OurPlaneCore job folder", _settings.JobsRootPath);
        if (folder == null)
            return;

        OpenJobSafely(folder);
    }

    private void OpenJobFromJobsRootDialog()
    {
        string initial = Directory.Exists(_settings.JobsRootPath)
            ? _settings.JobsRootPath
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string? root = SelectFolder("Select folder with OurPlaneCore jobs", initial);
        if (root == null)
            return;

        _settings.JobsRootPath = root;
        AppSettingsStore.AddJobsRoot(_settings, root);
        SaveAppSettings();

        var jobs = BuildJobPickerItems()
            .Where(item => item.Exists)
            .ToList();
        if (jobs.Count == 0)
        {
            MessageBox.Show("No OurPlaneCore jobs found in configured job folders.", "Open Jobs Folder",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new JobPickerDialog(
            jobs,
            _settings.JobsRootPath,
            SetRecentJobPinned,
            RemoveRecentJob,
            RemoveJobsRoot,
            AppSettingsStore.CurrentJobsRootPaths(_settings))
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        HandleJobPickerAction(dialog.SelectedAction, dialog.SelectedJobPath, dialog.SelectedJobsRootPath);
    }

    private void CreateJobFromDialog(string? preferredParent = null)
    {
        string? pdfFolder = SelectFolder("Select folder with PDFs for the new job", NewJobInitialFolder(preferredParent));
        if (pdfFolder == null)
            return;

        IReadOnlyList<string> pdfPaths = PdfImportSourceFinder.FindPdfFilesRecursive(pdfFolder);
        if (pdfPaths.Count == 0)
        {
            MessageBox.Show("No PDF files were found in the selected folder or its subfolders.",
                            "New Job",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
            return;
        }

        string? name = ShowInputDialog("Job name:", DefaultNewJobName(pdfFolder), "New Job");
        if (string.IsNullOrWhiteSpace(name))
            return;

        string parent = ResolveNewJobParent(preferredParent) ?? SampleJobService.DefaultJobsRoot;

        try
        {
            Directory.CreateDirectory(parent);
            _settings.JobsRootPath = parent;
            AppSettingsStore.AddJobsRoot(_settings, parent);
            SaveAppSettings();
            var job = OurPlaneCoreJobStore.CreateJob(parent, name);
            OpenJob(job.RootPath);
            QueuePdfImportForNewJob(pdfPaths, pdfFolder);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot create job:\n{ex.Message}", "New Job",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void QueuePdfImportForNewJob(IReadOnlyList<string> pdfPaths, string pdfFolder)
    {
        TxtStatus.Text = $"New job created. Importing {pdfPaths.Count} PDF file(s) from {pdfFolder}.";
        Dispatcher.BeginInvoke(
            new Action(async () =>
            {
                await RunAsyncUiHandler(
                    () => ImportPdfPathsAsync(pdfPaths, this, confirmPageNames: false),
                    "Import PDF failed.",
                    "Import PDF");
            }),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void CreateBlankJobFromDialog(string? preferredParent = null)
    {
        string? name = ShowInputDialog("Job name:", "New Job", "Blank Job");
        if (string.IsNullOrWhiteSpace(name))
            return;

        string parent = ResolveNewJobParent(preferredParent) ?? SampleJobService.DefaultJobsRoot;

        try
        {
            Directory.CreateDirectory(parent);
            _settings.JobsRootPath = parent;
            AppSettingsStore.AddJobsRoot(_settings, parent);
            SaveAppSettings();

            OurPlaneCoreJob job = OurPlaneCoreJobStore.CreateJob(parent, name);
            OpenJob(job.RootPath);
            TxtStatus.Text = $"Blank job created: {job.Name}. Use Blank Sheet to add an empty sheet.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot create blank job:\n{ex.Message}", "Blank Job",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string DefaultNewJobName(string pdfFolder)
    {
        string clean = pdfFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(clean);
        return string.IsNullOrWhiteSpace(name) ? "New Job" : name;
    }

    private string? ResolveNewJobParent(string? preferredParent)
    {
        foreach (string candidate in NewJobParentCandidates(preferredParent))
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private string? NewJobInitialFolder(string? preferredParent)
    {
        foreach (string candidate in NewJobParentCandidates(preferredParent))
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Directory.Exists(desktop) ? desktop : null;
    }

    private IEnumerable<string> NewJobParentCandidates(string? preferredParent)
    {
        if (!string.IsNullOrWhiteSpace(preferredParent))
            yield return preferredParent.Trim();

        if (_currentJob != null)
        {
            string? currentParent = Path.GetDirectoryName(_currentJob.RootPath);
            if (!string.IsNullOrWhiteSpace(currentParent))
                yield return currentParent;
        }

        if (!string.IsNullOrWhiteSpace(_settings.JobsRootPath))
            yield return _settings.JobsRootPath.Trim();

        foreach (string root in AppSettingsStore.CurrentJobsRootPaths(_settings))
        {
            if (!string.IsNullOrWhiteSpace(root))
                yield return root.Trim();
        }

        foreach (var recent in _settings.RecentJobs ?? [])
        {
            if (string.IsNullOrWhiteSpace(recent.Path))
                continue;

            string? recentParent = Path.GetDirectoryName(recent.Path);
            if (!string.IsNullOrWhiteSpace(recentParent))
                yield return recentParent;
        }
    }

    private void CreateSampleJob()
    {
        string parent = Directory.Exists(_settings.JobsRootPath)
            ? _settings.JobsRootPath
            : SampleJobService.DefaultJobsRoot;

        try
        {
            Directory.CreateDirectory(parent);
            _settings.JobsRootPath = parent;
            AppSettingsStore.AddJobsRoot(_settings, parent);
            SaveAppSettings();
            OurPlaneCoreJob job = SampleJobService.CreateSampleJob(parent);
            OpenJob(job.RootPath);
            TxtStatus.Text = $"Sample job created: {job.Name}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot create sample job:\n{ex.Message}", "Sample Job",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenJobSafely(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
            OpenJob(folder);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot open job:\n{ex.Message}", "Open Job",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void QueueRecentJobThumbnailGeneration(OurPlaneCoreJob job)
    {
        string jobRoot = job.RootPath;
        Task.Run(() =>
        {
            bool ok = JobThumbnailService.TryCreateThumbnail(job, out string thumbnailPath, out string error);
            return (ok, thumbnailPath, error);
        }).ContinueWith(task =>
        {
            if (task.IsFaulted)
                return;

            var (ok, thumbnailPath, error) = task.Result;
            if (!ok)
                return;

            AppSettingsStore.UpdateRecentJobThumbnail(_settings, jobRoot, thumbnailPath);
            SaveAppSettings();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void SetRecentJobPinned(string jobPath, string jobName, bool pinned)
    {
        AppSettingsStore.SetRecentJobPinned(_settings, jobPath, jobName, pinned);
        SaveAppSettings();
    }

    private void RemoveRecentJob(string jobPath)
    {
        AppSettingsStore.RemoveRecentJob(_settings, jobPath);
        SaveAppSettings();
    }

    private void RemoveJobsRoot(string rootPath)
    {
        AppSettingsStore.RemoveJobsRoot(_settings, rootPath);
        SaveAppSettings();
    }

    private static DateTime? ParseRecentJobTime(string value)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime utc))
            return null;

        return utc;
    }

    private static string RootForJobPath(string jobPath, IEnumerable<string> roots)
    {
        string candidate = NormalizeJobPath(jobPath);
        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            string normalizedRoot = NormalizeJobPath(root);
            string prefix = normalizedRoot + Path.DirectorySeparatorChar;
            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return normalizedRoot;
        }

        return "";
    }

    private static string NormalizeJobPath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
