using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.VisualBasic.FileIO;

namespace OurPlanCore;

public partial class MainWindow
{
    private void RunPagesMutation(string operation, Action action)
    {
        if (EnsureCurrentJobWritable(operation))
            action();
    }

    private async Task RunPagesMutationAsync(string operation, Func<Task> action)
    {
        if (EnsureCurrentJobWritable(operation))
            await action();
    }

    private void ClearCurrentPageIfAffected(string affectedPath)
    {
        RemovePageTabsForAffectedPath(affectedPath);

        if (_currentPage == null) return;
        if (!OurPlanCoreJobStore.IsSameOrDescendant(affectedPath, _currentPage.FolderPath))
            return;

        OnSheetOverlayPageCleared();
        _currentPage = null;
        _currentPdfPath = "";
        _viewport.ClearPage();
        ApplyRulerVisibilityToViewport();
    }

    private static void DeleteDirectoryToRecycle(string path)
    {
        JobWriteAccess.Demand(path, "delete a Pages folder");
        FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    private static void OpenFolderInExplorer(string folder)
    {
        if (!Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true,
        });
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsSamePageFolder(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(NormalizePathForCompare(left), NormalizePathForCompare(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePathForCompare(string path)
    {
        string trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0)
            return "";

        try
        {
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return trimmed;
        }
    }

    private void ShowOperationError(string operation, Exception ex)
    {
        MessageBox.Show(ex.Message, operation, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowFolderTemplateErrors(string title, FolderTemplateResult result) =>
        ShowFolderTemplateErrors(title, result.ErrorMessages);

    private void ShowFolderTemplateErrors(string title, IReadOnlyList<string> errors)
    {
        if (errors.Count == 0)
            return;

        string message = string.Join(Environment.NewLine, errors.Take(8));
        if (errors.Count > 8)
            message += Environment.NewLine + $"...and {errors.Count - 8} more.";
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
