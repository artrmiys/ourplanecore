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
            AppSettingsStore.CurrentJobsRootPaths(_settings))
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
            return;

        HandleJobPickerAction(dialog.SelectedAction, dialog.SelectedJobPath);
    }

    private void ShowStartupJobPickerIfUseful()
    {
        if (_currentJob != null)
            return;

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
                lastOpened: FormatRecentJobTime(recent.LastOpenedUtc),
                source: "Recent",
                isPinned: recent.IsPinned,
                isRecent: true,
                rootPath: rootPath);
        }

        foreach (string rootPath in roots)
        {
            if (!Directory.Exists(rootPath))
                continue;

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
                    lastOpened: "",
                    source: $"Jobs: {Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}",
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
        string lastOpened,
        string source,
        bool isPinned,
        bool isRecent,
        string rootPath = "")
    {
        string key = NormalizeJobPath(path);
        if (!seen.Add(key))
            return;

        bool exists = Directory.Exists(path) && IsJobFolder(path);
        items.Add(new JobPickerItem(
            string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name,
            path,
            thumbnailPath,
            lastOpened,
            source,
            exists,
            isPinned,
            isRecent,
            rootPath));
    }

    private void HandleJobPickerAction(JobPickerAction action, string selectedJobPath)
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
                CreateJobFromDialog();
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
            AppSettingsStore.CurrentJobsRootPaths(_settings))
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        HandleJobPickerAction(dialog.SelectedAction, dialog.SelectedJobPath);
    }

    private void CreateJobFromDialog()
    {
        string? parent = SelectFolder("Choose parent folder for the new job", _settings.JobsRootPath);
        if (parent == null)
            return;

        string? name = ShowInputDialog("Job name:", "New Job", "New Job");
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            _settings.JobsRootPath = parent;
            AppSettingsStore.AddJobsRoot(_settings, parent);
            SaveAppSettings();
            var job = OurPlaneCoreJobStore.CreateJob(parent, name);
            OpenJob(job.RootPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot create job:\n{ex.Message}", "New Job",
                            MessageBoxButton.OK, MessageBoxImage.Error);
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

    private static string FormatRecentJobTime(string value)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime utc))
            return "";

        return utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
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
