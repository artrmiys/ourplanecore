using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OurPlaneCore.Controls;

public enum JobPickerAction
{
    None,
    OpenSelected,
    BrowseJob,
    BrowseJobsFolder,
    NewJob,
    CreateSample,
}

public sealed class JobPickerItem : INotifyPropertyChanged
{
    private bool _isPinned;
    private bool _isRecent;
    private string _sourceLabel = "";

    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string ThumbnailPath { get; init; } = "";
    public DateTime? LastOpenedUtc { get; init; }
    public bool Exists { get; init; }
    public string RootPath { get; init; } = "";

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned == value)
                return;

            _isPinned = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GroupOrder));
            OnPropertyChanged(nameof(GroupKey));
        }
    }

    public bool IsRecent
    {
        get => _isRecent;
        set
        {
            if (_isRecent == value)
                return;

            _isRecent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GroupOrder));
            OnPropertyChanged(nameof(GroupKey));
        }
    }

    public string SourceLabel
    {
        get => _sourceLabel;
        set
        {
            if (_sourceLabel == value)
                return;

            _sourceLabel = value;
            OnPropertyChanged();
        }
    }

    public string Status => Exists ? "Ready" : "Missing";
    public string RelativeDate => JobPickerFormatting.RelativeDate(LastOpenedUtc);
    public ImageSource? ThumbnailImage => LoadThumbnail(ThumbnailPath);

    // Stable group ordering: Pinned (0) → Recent (1) → From "<root>" (2) → Unavailable (3).
    public int GroupOrder => !Exists ? 3 : IsPinned ? 0 : IsRecent ? 1 : 2;

    public string GroupKey => GroupOrder switch
    {
        0 => "★ Pinned",
        1 => "🕒 Recent",
        3 => "⚠ Unavailable",
        _ => $"📁 From \"{ShortRootName(RootPath)}\"",
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static ImageSource? LoadThumbnail(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static string ShortRootName(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return "Local";

        string trimmed = rootPath.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                                          System.IO.Path.AltDirectorySeparatorChar);
        string name = System.IO.Path.GetFileName(trimmed);
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        string? root = System.IO.Path.GetPathRoot(trimmed);
        return string.IsNullOrWhiteSpace(root) ? trimmed : root!;
    }
}

public static class JobPickerFormatting
{
    public static string RelativeDate(DateTime? utc)
    {
        if (utc is not { } u)
            return "";

        DateTime local = u.ToLocalTime();
        DateTime nowLocal = DateTime.Now;
        TimeSpan delta = nowLocal - local;

        if (delta.TotalSeconds < 0)
            return local.ToString("MMM d, yyyy HH:mm", CultureInfo.InvariantCulture);

        if (local.Date == nowLocal.Date)
        {
            if (delta.TotalMinutes < 1)
                return "Just now";
            if (delta.TotalMinutes < 60)
                return $"{(int)delta.TotalMinutes}m ago";
            return $"Today {local:HH:mm}";
        }

        if (local.Date == nowLocal.Date.AddDays(-1))
            return "Yesterday";

        if (delta.TotalDays < 7)
            return $"{(int)delta.TotalDays}d ago";

        return local.Year == nowLocal.Year
            ? local.ToString("MMM d", CultureInfo.InvariantCulture)
            : local.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
