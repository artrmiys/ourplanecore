using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace OurPlanCore;

public partial class App : Application
{
    internal static string StartupProjectPath { get; private set; } = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupProjectPath = ResolveStartupProjectPath(e.Args);
        AppDataMigration.RunCritical();
        base.OnStartup(e);

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += AppDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        AppLog.Info("Application startup.");
        AppLog.Info($"Version {AppVersion.Display}.");
        Task.Run(() => AppLog.PruneOldLogs());
        Task.Run(AppDataMigration.RunDeferred);
#if !DEBUG
        Task.Run(OurPlanFileAssociationService.EnsureRegisteredForCurrentExecutable);
#endif
        _ = PdfLayerRenderService.PrewarmWorkersAsync();
    }

    private static string ResolveStartupProjectPath(IReadOnlyList<string> args)
    {
        foreach (string argument in args)
        {
            if (string.IsNullOrWhiteSpace(argument))
                continue;
            try
            {
                string path = Path.GetFullPath(argument.Trim());
                if (File.Exists(path) && OurPlanPackageFormat.HasPackageExtension(path))
                    return path;
            }
            catch
            {
                // Ignore unrelated or malformed command-line arguments.
            }
        }
        return "";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info($"Application exit {e.ApplicationExitCode}.");
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryFlushPendingAutosave();
        AppLog.Error(e.Exception, "Unhandled UI exception.");
        MessageBox.Show(
            "An unexpected error was logged. The app will try to keep running.",
            AppIdentity.ProductName,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void AppDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            AppLog.Error(ex, "Unhandled application exception.");
        else
            AppLog.Error(new InvalidOperationException(e.ExceptionObject?.ToString() ?? "Unknown exception"), "Unhandled application exception.");
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error(e.Exception, "Unobserved task exception.");
        e.SetObserved();
    }

    private static void TryFlushPendingAutosave()
    {
        try
        {
            if (Current?.MainWindow is MainWindow mainWindow)
                mainWindow.FlushPendingAutosave();
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Failed to flush pending autosave during exception handling.");
        }
    }
}
