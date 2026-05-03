using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SmartTakeoffs.Controls;

namespace SmartTakeoffs;

public partial class MainWindow
{
    private static readonly RoutedCommand OpenRecentJobsCommand =
        new(nameof(OpenRecentJobsCommand), typeof(MainWindow));

    private void ShowRecentJobPicker()
    {
        var dialog = new JobPickerDialog(BuildJobPickerItems(), _settings.JobsRootPath)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
            return;

        HandleJobPickerAction(dialog.SelectedAction, dialog.SelectedJobPath);
    }

    private void ShowStartupJobPickerIfUseful()
    {
        if (_currentJob != null || BuildJobPickerItems().Count == 0)
            return;

        ShowRecentJobPicker();
    }

    private IReadOnlyList<JobPickerItem> BuildJobPickerItems()
    {
        var items = new List<JobPickerItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var recent in _settings.RecentJobs ?? [])
        {
            if (string.IsNullOrWhiteSpace(recent.Path))
                continue;

            string path = recent.Path.Trim();
            AddJobPickerItem(
                items,
                seen,
                name: string.IsNullOrWhiteSpace(recent.Name) ? Path.GetFileName(path) : recent.Name.Trim(),
                path,
                lastOpened: FormatRecentJobTime(recent.LastOpenedUtc),
                source: "Recent");
        }

        if (Directory.Exists(_settings.JobsRootPath))
        {
            foreach (string folder in Directory.EnumerateDirectories(_settings.JobsRootPath)
                         .Where(IsJobFolder)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                AddJobPickerItem(
                    items,
                    seen,
                    name: Path.GetFileName(folder),
                    path: folder,
                    lastOpened: "",
                    source: "Jobs Folder");
            }
        }

        return items;
    }

    private static void AddJobPickerItem(
        List<JobPickerItem> items,
        HashSet<string> seen,
        string name,
        string path,
        string lastOpened,
        string source)
    {
        string key = NormalizeJobPath(path);
        if (!seen.Add(key))
            return;

        bool exists = Directory.Exists(path) && IsJobFolder(path);
        items.Add(new JobPickerItem(
            string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name,
            path,
            lastOpened,
            source,
            exists));
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
        }
    }

    private void OpenJobFromFolderDialog()
    {
        string? folder = SelectFolder("Select SmartTakeoffs job folder", _settings.JobsRootPath);
        if (folder == null)
            return;

        OpenJobSafely(folder);
    }

    private void OpenJobFromJobsRootDialog()
    {
        string initial = Directory.Exists(_settings.JobsRootPath)
            ? _settings.JobsRootPath
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string? root = SelectFolder("Select folder with SmartTakeoffs jobs", initial);
        if (root == null)
            return;

        _settings.JobsRootPath = root;
        SaveAppSettings();

        var jobs = Directory.EnumerateDirectories(root)
            .Where(IsJobFolder)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(folder => new JobPickerItem(
                Path.GetFileName(folder),
                folder,
                "",
                "Jobs Folder",
                true))
            .ToList();
        if (jobs.Count == 0)
        {
            MessageBox.Show("No SmartTakeoffs jobs found in that folder.", "Open Jobs Folder",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new JobPickerDialog(jobs, root)
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
            SaveAppSettings();
            var job = SmartTakeoffsJobStore.CreateJob(parent, name);
            OpenJob(job.RootPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot create job:\n{ex.Message}", "New Job",
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

    private static string FormatRecentJobTime(string value)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime utc))
            return "";

        return utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
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
