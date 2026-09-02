using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace OurPlanCore;

public partial class MainWindow
{
    private OurPlanPackageSession? _currentPackageSession;
    private OurPlanPackageSession? _openingPackageSession;
    private FileSystemWatcher? _packageWorkspaceWatcher;
    private FileSystemWatcher? _packageArtifactWatcher;
    private CancellationTokenSource? _packageArtifactCheckCts;
    private string _packageSaveStatus = "";
    private long _packageWorkspaceGeneration;
    private int _packageSaveInProgress;
    private int _packageOperationActive;
    private int _packageWorkspacePruneStarted;

    private bool HasCurrentPackageSession =>
        _currentJob != null &&
        _currentPackageSession != null &&
        SameJobPath(_currentJob.RootPath, _currentPackageSession.WorkspaceRoot);

    private string CurrentDocumentPath() =>
        HasCurrentPackageSession ? _currentPackageSession!.PackagePath : _currentJob?.RootPath ?? "";

    private bool CanSaveAsCurrentProject =>
        _currentJob != null && (HasCurrentPackageSession || IsCurrentJobWritable);

    private string SaveAsDisabledReason() => _currentJob == null
        ? "Open or create a project first."
        : CanSaveAsCurrentProject
            ? ""
            : "This project folder is open read-only and cannot be saved to a new location.";

    private void SaveAsCurrentProject()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "No project is open - nothing to save.";
            return;
        }

        if (HasCurrentPackageSession)
        {
            SaveAsOurPlanProject();
            return;
        }

        if (!EnsureCurrentJobWritable("save this project to a new location"))
            return;
        SaveLegacyFolderAs();
    }

    private void OpenOurPlanProjectDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Project",
            Filter = "Project files (*.ourplan)|*.ourplan|All files (*.*)|*.*",
            DefaultExt = OurPlanPackageFormat.Extension,
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = InitialProjectArtifactDirectory(),
        };
        if (dialog.ShowDialog(this) == true)
            OpenProjectPathSafely(dialog.FileName);
    }

    private bool OpenProjectPathSafely(string path, string? initialPageFolder = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        if (OurPlanPackageFormat.HasPackageExtension(path))
            return OpenPackageProject(path, initialPageFolder);
        if (Directory.Exists(path))
            return OpenJob(path, initialPageFolder);

        MessageBox.Show(
            $"The project does not exist or is not a supported .ourplan file or project folder:\n{path}",
            "Open Project",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        return false;
    }

    private bool OpenPackageProject(string packagePath, string? initialPageFolder)
    {
        OurPlanPackageSession? session = null;
        try
        {
            string normalizedPackagePath = Path.GetFullPath(packagePath);
            if (HasCurrentPackageSession &&
                SameDocumentPath(_currentPackageSession!.PackagePath, normalizedPackagePath))
            {
                string? currentPage = ResolvePackageInitialPage(
                    _currentPackageSession,
                    normalizedPackagePath,
                    initialPageFolder);
                return OpenJob(_currentPackageSession.WorkspaceRoot, currentPage);
            }

            TxtStatus.Text = $"Opening {Path.GetFileName(normalizedPackagePath)}...";
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                session = RunResponsivePackageOperation(
                    () => OurPlanPackageWorkspace.Open(normalizedPackagePath));
            }
            catch (Exception packageError) when (packageError is IOException or UnauthorizedAccessException)
            {
                session = OfferRecoveryForUnreadablePackage(normalizedPackagePath, packageError);
                if (session == null)
                    throw;
            }

            if (!session.IsRecoverySession)
                session = OfferRecoveryInsteadOfPackage(session) ?? session;
            _openingPackageSession = session;
            string? pageToOpen = ResolvePackageInitialPage(session, normalizedPackagePath, initialPageFolder);
            bool opened = OpenJob(session.WorkspaceRoot, pageToOpen);
            if (!opened)
            {
                OurPlanPackageWorkspace.MarkSessionClosed(session);
                return false;
            }

            TxtStatus.Text = session.IsRecoverySession
                ? $"Opened recovered working copy: {session.DisplayName}. Save it to preserve the recovery."
                : $"Opened OurPlan project: {session.DisplayName}.";
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            if (session != null && !ReferenceEquals(session, _currentPackageSession))
            {
                try
                {
                    OurPlanPackageWorkspace.MarkSessionClosed(session);
                }
                catch
                {
                    // The workspace marker is best-effort after an open failure.
                }
            }
            AppLog.Error(ex, $"Failed to open OurPlan package '{packagePath}'.");
            MessageBox.Show(
                $"Cannot open the OurPlan project.\n\n{ex.Message}",
                "Open OurPlan Project",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _openingPackageSession = null;
            Mouse.OverrideCursor = null;
        }
    }

    private OurPlanPackageSession? OfferRecoveryInsteadOfPackage(OurPlanPackageSession packageSession)
    {
        OurPlanPackageRecoveryInfo? recovery = packageSession.AvailableRecoverySessions.FirstOrDefault();
        if (recovery == null)
            return packageSession;

        MessageBoxResult choice = MessageBox.Show(
            this,
            "A preserved local working copy contains changes that are not in this .ourplan file.\n\n" +
            $"Recovery type: {RecoveryKindText(recovery.Kind)}\n" +
            $"Saved locally: {recovery.StateUpdatedUtc.ToLocalTime():g}\n\n" +
            "Yes — open the recovered work\nNo — open the project file\nCancel — stop opening",
            "OurPlan Recovery Available",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (choice == MessageBoxResult.No)
            return packageSession;

        OurPlanPackageWorkspace.MarkSessionClosed(packageSession);
        if (choice == MessageBoxResult.Cancel)
            throw new OperationCanceledException("Opening the project was cancelled.");
        return RunResponsivePackageOperation(() =>
            OurPlanPackageWorkspace.TryOpenRecoverySession(recovery, out OurPlanPackageSession? recovered)
                ? recovered!
                : throw new IOException("The selected local recovery is no longer available."));
    }

    private OurPlanPackageSession? OfferRecoveryForUnreadablePackage(
        string packagePath,
        Exception packageError)
    {
        IReadOnlyList<OurPlanPackageRecoveryInfo> recoveries = RunResponsivePackageOperation(
            () => OurPlanPackageWorkspace.FindRecoverySessions(packagePath));
        OurPlanPackageRecoveryInfo? recovery = recoveries.FirstOrDefault();
        if (recovery == null)
            return null;

        MessageBoxResult choice = MessageBox.Show(
            this,
            "The .ourplan file cannot be read, but a complete local recovery workspace is available.\n\n" +
            $"File error: {packageError.Message}\n\nOpen the recovered work now?",
            "Recover OurPlan Project",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);
        if (choice != MessageBoxResult.Yes)
            throw new OperationCanceledException("Opening the local recovery was cancelled.");
        return RunResponsivePackageOperation(() =>
            OurPlanPackageWorkspace.TryOpenRecoverySession(recovery, out OurPlanPackageSession? recovered)
                ? recovered!
                : throw new IOException("The local recovery is no longer available."));
    }

    private static string RecoveryKindText(OurPlanRecoveryKind kind) => kind switch
    {
        OurPlanRecoveryKind.PackageChanged => "the cloud/project file has a newer revision",
        OurPlanRecoveryKind.PackageUnavailable => "the project file is missing or damaged",
        OurPlanRecoveryKind.InterruptedSession => "the previous session did not close cleanly",
        _ => "local unpackaged changes",
    };

    private void AdoptPackageSessionForOpenedJob(string normalizedRoot, bool reloadCurrent)
    {
        if (_openingPackageSession != null &&
            SameJobPath(_openingPackageSession.WorkspaceRoot, normalizedRoot))
        {
            if (_currentPackageSession != null &&
                !ReferenceEquals(_currentPackageSession, _openingPackageSession))
            {
                ClosePackageSessionMarker(_currentPackageSession);
            }
            _currentPackageSession = _openingPackageSession;
            StartPackageWorkspaceWatcher(_openingPackageSession);
            _packageSaveStatus = _openingPackageSession.IsRecoverySession
                ? "Save: Recovery - press Ctrl+S to update this file"
                : _openingPackageSession.HasUnpackagedChanges
                    ? "Save: Pending"
                    : CanUpdatePackageArtifact(_openingPackageSession.PackagePath)
                        ? $"Save: Saved {DateTime.Now:HH:mm:ss}"
                        : "Save: Target read-only";
            TxtStatusSave.ToolTip = _openingPackageSession.PackagePath;
            UpdateStatusBarSegments();
            StartPackageWorkspacePruneOnce();
            return;
        }

        if (reloadCurrent && HasCurrentPackageSession)
            return;

        StopPackageWorkspaceWatcher();
        if (_currentPackageSession != null)
            ClosePackageSessionMarker(_currentPackageSession);
        _currentPackageSession = null;
        _packageSaveStatus = "";
        TxtStatusSave.ToolTip = null;
    }

    private bool TrySaveCurrentPackage(string operation, bool showDialog = true)
    {
        if (!HasCurrentPackageSession)
            return true;
        SupersedeAutomaticPackageCheckpoint();
        if (!IsCurrentJobWritable)
        {
            _currentPackageSession!.HasUnpackagedChanges = true;
            _packageSaveStatus = "Save: Recovery - working copy is read-only";
            TxtStatus.Text =
                "The project working copy became read-only. The .ourplan file was not updated; use Save As or keep the local recovery.";
            UpdateStatusBarSegments();
            if (showDialog)
            {
                MessageBox.Show(
                    this,
                    "The project working copy became read-only, so the .ourplan file was not updated. " +
                    "Your file changes remain in the preserved local recovery. Use Save As to create a new project file.",
                    "OurPlan Project Not Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            return false;
        }
        if (Volatile.Read(ref _packageOperationActive) > 0)
        {
            _currentPackageSession!.HasUnpackagedChanges = true;
            _packageSaveStatus = "Save: Pending";
            TxtStatus.Text = "Another project package operation is still running.";
            UpdateStatusBarSegments();
            ScheduleAutomaticPackageCheckpoint(
                _currentPackageSession,
                waitForQuietPeriod: false,
                retryDelay: OurPlanPackageAutosaveSchedule.BusyRetryDelay);
            return false;
        }

        OurPlanPackageSession session = _currentPackageSession!;
        _packageAutosaveBlocked = false;
        try
        {
            CancelPendingPackageArtifactInspection();
            TxtStatus.Text = $"Packing {Path.GetFileName(session.PackagePath)}...";
            _packageSaveStatus = "Save: Packing...";
            UpdateStatusBarSegments();
            Mouse.OverrideCursor = Cursors.Wait;
            OurPlanPackageSaveResult? result = null;
            bool stable = false;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                long generation = Interlocked.Read(ref _packageWorkspaceGeneration);
                Interlocked.Increment(ref _packageSaveInProgress);
                try
                {
                    result = RunResponsivePackageOperation(
                        () => OurPlanPackageWriter.Save(session));
                }
                finally
                {
                    Interlocked.Decrement(ref _packageSaveInProgress);
                }

                if (generation == Interlocked.Read(ref _packageWorkspaceGeneration) &&
                    !session.HasUnpackagedChanges &&
                    PackageArtifactStillMatchesSession(session))
                {
                    stable = true;
                    break;
                }
                session.HasUnpackagedChanges = true;
                _packageSaveStatus = "Save: Pending";
                UpdateStatusBarSegments();
            }
            if (!stable || result == null)
            {
                throw new OurPlanPackageTransientException(
                    "The project kept changing while it was being packed. Wait for background work to finish and save again.");
            }
            PromoteRecoveredPackageSessionAfterSamePathSave(session);
            PersistCurrentDocumentIdentity();
            TxtStatus.Text =
                $"Saved to {Path.GetFileName(result.PackagePath)} " +
                $"({result.LogicalFileCount:N0} files, {FormatPackageBytes(result.PackageBytes)}). " +
                "Cloud upload is managed by your sync provider.";
            AppLog.Info(
                $"OurPlan package saved during {operation}: '{result.PackagePath}', " +
                $"revision={result.RevisionId}, logicalFiles={result.LogicalFileCount}, " +
                $"uniqueObjects={result.UniqueObjectCount}, bytes={result.PackageBytes}.");
            _packageSaveStatus = $"Save: Saved {DateTime.Now:HH:mm:ss}";
            TxtStatusSave.ToolTip = session.PackagePath;
            ResetAutomaticPackageCheckpointScheduler();
            UpdateStatusBarSegments();
            return true;
        }
        catch (OurPlanPackageConflictException ex)
        {
            session.HasUnpackagedChanges = true;
            _packageAutosaveBlocked = true;
            CancelScheduledAutomaticPackageCheckpoint();
            AppLog.Warn(ex, $"OurPlan package conflict during {operation}.");
            TxtStatus.Text = "Save conflict - working copy preserved.";
            _packageSaveStatus = "Save: Conflict";
            UpdateStatusBarSegments();
            if (showDialog)
            {
                MessageBox.Show(
                    $"The .ourplan file changed outside this window and was not overwritten.\n\n" +
                    $"Your complete working copy is preserved locally. Use Save As to keep it under a new name.\n\n{ex.Message}",
                    "OurPlan Project Conflict",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            return false;
        }
        catch (Exception ex)
        {
            session.HasUnpackagedChanges = true;
            AppLog.Error(ex, $"OurPlan package save failed during {operation}.");
            TxtStatus.Text = "Package save failed - working copy preserved.";
            _packageSaveStatus = "Save: Failed";
            UpdateStatusBarSegments();
            bool transientFailure = ShouldRetryAutomaticPackageCheckpoint(ex);
            _packageAutosaveBlocked = !transientFailure;
            if (transientFailure)
            {
                ScheduleAutomaticPackageCheckpoint(
                    session,
                    waitForQuietPeriod: false,
                    retryDelay: OurPlanPackageAutosaveSchedule.FailureRetryDelay(1));
            }
            else
            {
                CancelScheduledAutomaticPackageCheckpoint();
            }
            if (showDialog)
            {
                MessageBox.Show(
                    $"The .ourplan file could not be updated, so the previous file was left unchanged.\n\n" +
                    $"Your complete working copy is preserved locally.\n\n{ex.Message}",
                    "Save OurPlan Project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            return false;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void SaveAsOurPlanProject()
    {
        if (_currentJob == null || !HasCurrentPackageSession)
            return;
        SupersedeAutomaticPackageCheckpoint();
        using JobFileWriteActivity.PackageCheckpointScope copyCheckpoint =
            JobFileWriteActivity.BeginPackageCheckpoint();
        if (copyCheckpoint.HadActiveWriters || !PrepareCurrentJobForPackageCopy())
            return;

        var dialog = CreateOurPlanSaveDialog(_currentJob.Name);
        if (dialog.ShowDialog(this) != true)
            return;
        OurPlanCoreJob sourceJob = _currentJob;
        OurPlanPackageSession sourcePackage = _currentPackageSession!;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            OurPlanPackageSession session =
                SavePackageWorkspaceAs(sourceJob, sourcePackage, dialog.FileName);
            sourcePackage.DirtyStateChanged = null;
            sourcePackage.MarkerSessionOpen = false;
            _currentPackageSession = session;
            StartPackageWorkspaceWatcher(session);

            _packageSaveStatus = session.HasUnpackagedChanges
                ? "Save: Pending"
                : $"Save: Saved {DateTime.Now:HH:mm:ss}";
            TxtStatusSave.ToolTip = session.PackagePath;
            if (_currentPage != null)
                StoreLastPageForCurrentDocument(_currentPage.FolderPath);
            else
            {
                _settings.LastPageFolder = "";
                _settings.LastPageRelativePath = "";
            }
            PersistCurrentDocumentIdentity();
            RefreshJobHeaderLabels();
            TxtStatus.Text = session.HasUnpackagedChanges
                ? $"Project copy created, but the newest initialization changes remain in local recovery: {session.PackagePath}."
                : $"Saved project as: {session.PackagePath}.";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Save As OurPlan Project failed.");
            MessageBox.Show(
                $"Cannot save the project copy.\n\n{ex.Message}",
                "Save As",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private OurPlanPackageSession SavePackageWorkspaceAs(
        OurPlanCoreJob sourceJob,
        OurPlanPackageSession sourceSession,
        string destination) =>
        RunResponsivePackageOperation(() => OurPlanPackageWriter.SaveAs(
            sourceJob.RootPath,
            destination,
            sourceJob.Name,
            overwriteExisting: File.Exists(destination),
            sourceSession,
            projectId: sourceSession.ProjectId));

    private void SaveLegacyFolderAs()
    {
        if (_currentJob == null || HasCurrentPackageSession ||
            !EnsureCurrentJobWritable("save this project to a new location"))
            return;

        OurPlanCoreJob sourceJob = _currentJob;
        string? pageRelative = _currentPage != null &&
                               OurPlanCoreJobStore.IsSameOrDescendant(
                                   sourceJob.RootPath,
                                   _currentPage.FolderPath)
            ? Path.GetRelativePath(sourceJob.RootPath, _currentPage.FolderPath)
            : null;
        string? parent = SelectFolder(
            "Select parent folder for the project copy",
            InitialProjectArtifactDirectory());
        if (parent == null)
            return;
        string? name = ShowInputDialog("Project folder name:", sourceJob.Name, "Save As");
        if (string.IsNullOrWhiteSpace(name))
            return;

        string destination = Path.Combine(parent, OurPlanCoreJobStore.SanitizeName(name.Trim(), 120));
        using JobFileWriteActivity.PackageCheckpointScope copyCheckpoint =
            JobFileWriteActivity.BeginPackageCheckpoint();
        if (copyCheckpoint.HadActiveWriters ||
            !EnsureExpectedJobWritable(
                sourceJob,
                "save this project to a new location",
                showDialog: true) ||
            !TrySaveCurrentJobData("save project as") ||
            !PrepareCurrentJobForSwitch())
        {
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            RunResponsivePackageOperation(() =>
            {
                OurPlanPackageWorkspace.ExportLegacyCopy(sourceJob.RootPath, destination);
                return true;
            });
            string? copiedPage = ResolveCopiedProjectPage(destination, pageRelative);
            if (!OpenJob(destination, copiedPage, currentJobPrepared: true))
            {
                MessageBox.Show(
                    $"The project copy was created, but it could not be opened. " +
                    $"The original project remains active.\n\n{destination}",
                    "Save As",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            TxtStatus.Text = $"Saved project as: {destination}.";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Save legacy folder copy failed.");
            MessageBox.Show(
                $"Cannot save the project copy.\n\n{ex.Message}",
                "Save As",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private static string? ResolveCopiedProjectPage(string destinationRoot, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            return null;
        string candidate = Path.GetFullPath(Path.Combine(destinationRoot, relative));
        return Directory.Exists(candidate) &&
               OurPlanCoreJobStore.IsSameOrDescendant(destinationRoot, candidate)
            ? candidate
            : null;
    }

    private bool CreateNewPackageProject(
        string displayName,
        out OurPlanCoreJob? job,
        string? preferredParent = null)
    {
        var dialog = CreateOurPlanSaveDialog(displayName, preferredParent);
        if (dialog.ShowDialog(this) != true)
        {
            job = null;
            return false;
        }

        return CreateNewPackageProjectAtPath(
            displayName,
            dialog.FileName,
            out job,
            overwriteExisting: File.Exists(dialog.FileName));
    }

    private bool CreateNewPackageProjectAtPath(
        string displayName,
        string packagePath,
        out OurPlanCoreJob? job,
        bool overwriteExisting = false)
    {
        job = null;
        string destination = Path.GetFullPath(packagePath);
        if (!OurPlanPackageFormat.HasPackageExtension(destination))
            throw new ArgumentException("A new project path must use the .ourplan extension.", nameof(packagePath));
        if (!PrepareCurrentJobForSwitch())
            return false;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            (OurPlanCoreJob created, OurPlanPackageSession session) =
                RunResponsivePackageOperation(() =>
                {
                    (OurPlanCoreJob newJob, string projectId, _) =
                        OurPlanPackageWorkspace.CreateNewJob(displayName);
                    try
                    {
                        OurPlanPackageSession newSession = OurPlanPackageWriter.SaveAs(
                            newJob.RootPath,
                            destination,
                            displayName,
                            overwriteExisting,
                            projectId: projectId);
                        return (newJob, newSession);
                    }
                    catch
                    {
                        OurPlanPackageWorkspace.AbandonUnpublishedWorkspace(newJob.RootPath, projectId);
                        throw;
                    }
                });
            _openingPackageSession = session;
            // Re-enter OpenJob's checkpointed switch preparation after package creation.
            if (!OpenJob(created.RootPath))
            {
                OurPlanPackageWorkspace.MarkSessionClosed(session);
                return false;
            }
            session.HasUnpackagedChanges = true;
            job = _currentJob;
            return TrySaveCurrentPackage("new project initialization");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "New project creation failed.");
            MessageBox.Show(
                $"Cannot create the project.\n\n{ex.Message}",
                "New Project",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _openingPackageSession = null;
            Mouse.OverrideCursor = null;
        }
    }

    private SaveFileDialog CreateOurPlanSaveDialog(
        string displayName,
        string? preferredParent = null)
    {
        string safeName = OurPlanCoreJobStore.SanitizeName(
            string.IsNullOrWhiteSpace(displayName) ? "New Project" : displayName.Trim(),
            120);
        return new SaveFileDialog
        {
            Title = "Save Project",
            Filter = "Project files (*.ourplan)|*.ourplan",
            DefaultExt = OurPlanPackageFormat.Extension,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = safeName + OurPlanPackageFormat.Extension,
            InitialDirectory = !string.IsNullOrWhiteSpace(preferredParent) && Directory.Exists(preferredParent)
                ? Path.GetFullPath(preferredParent)
                : InitialProjectArtifactDirectory(),
        };
    }

    private string InitialProjectArtifactDirectory()
    {
        string current = CurrentDocumentPath();
        string? currentParent = string.IsNullOrWhiteSpace(current)
            ? null
            : Path.GetDirectoryName(current);
        if (!string.IsNullOrWhiteSpace(currentParent) && Directory.Exists(currentParent))
            return currentParent;
        if (!string.IsNullOrWhiteSpace(_settings.JobsRootPath) && Directory.Exists(_settings.JobsRootPath))
            return _settings.JobsRootPath;
        return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    private string InitialProjectExportDirectory()
    {
        if (_currentJob == null)
            return InitialProjectArtifactDirectory();
        if (!HasCurrentPackageSession)
            return _currentJob.RootPath;

        string? artifactParent = Path.GetDirectoryName(_currentPackageSession!.PackagePath);
        return !string.IsNullOrWhiteSpace(artifactParent) && Directory.Exists(artifactParent)
            ? artifactParent
            : InitialProjectArtifactDirectory();
    }

    private bool ConfirmProjectExportDestination(string outputPath)
    {
        if (!HasCurrentPackageSession ||
            !OurPlanCoreJobStore.IsSameOrDescendant(_currentPackageSession!.WorkspaceRoot, outputPath))
        {
            return true;
        }

        MessageBox.Show(
            "Choose a location outside the private OurPlan working cache. " +
            "Exports saved there would become internal project data.\n\n" +
            $"Recommended folder:\n{InitialProjectExportDirectory()}",
            "Export Location",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private void PersistCurrentDocumentIdentity()
    {
        if (_currentJob == null)
            return;
        string documentPath = CurrentDocumentPath();
        _settings.LastJobPath = documentPath;
        string? parent = Path.GetDirectoryName(documentPath);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
        {
            _settings.JobsRootPath = parent;
            AppSettingsStore.AddJobsRoot(_settings, parent);
        }
        AppSettingsStore.AddRecentJob(_settings, documentPath, _currentJob.Name);
        SaveAppSettings();
    }

    private void StoreLastPageForCurrentDocument(string pageFolder)
    {
        if (_currentJob == null)
            return;
        if (HasCurrentPackageSession)
        {
            _settings.LastPageRelativePath = Path.GetRelativePath(_currentJob.PagesRoot, pageFolder);
            _settings.LastPageFolder = "";
        }
        else
        {
            _settings.LastPageFolder = pageFolder;
            _settings.LastPageRelativePath = "";
        }
        _settings.LastJobPath = CurrentDocumentPath();
    }

    private string? ResolvePackageInitialPage(
        OurPlanPackageSession session,
        string packagePath,
        string? explicitPageFolder)
    {
        string pagesRoot = Path.Combine(session.WorkspaceRoot, "Pages");
        if (!string.IsNullOrWhiteSpace(explicitPageFolder) &&
            Directory.Exists(explicitPageFolder) &&
            OurPlanCoreJobStore.IsSameOrDescendant(pagesRoot, explicitPageFolder))
        {
            return explicitPageFolder;
        }
        if (string.IsNullOrWhiteSpace(_settings.LastPageRelativePath) ||
            !SameDocumentPath(_settings.LastJobPath, packagePath))
        {
            return null;
        }

        try
        {
            string candidate = Path.GetFullPath(Path.Combine(
                pagesRoot,
                _settings.LastPageRelativePath));
            return Directory.Exists(candidate) &&
                   OurPlanCoreJobStore.IsSameOrDescendant(pagesRoot, candidate)
                ? candidate
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool SameDocumentPath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool CanUpdatePackageArtifact(string packagePath)
    {
        try
        {
            if (!File.Exists(packagePath) ||
                (File.GetAttributes(packagePath) & FileAttributes.ReadOnly) != 0)
            {
                return false;
            }

            using var stream = new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            return stream.CanWrite;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool PackageArtifactStillMatchesSession(OurPlanPackageSession session)
    {
        try
        {
            if (!File.Exists(session.PackagePath) ||
                OurPlanPackageFingerprint.Read(session.PackagePath) != session.BaseFingerprint)
            {
                return false;
            }

            OurPlanPackageManifest manifest = OurPlanPackageArchive.ReadManifest(
                session.PackagePath,
                verifyObjects: false);
            return OurPlanPackageWorkspace.ManifestMatchesSessionBase(session, manifest);
        }
        catch
        {
            return false;
        }
    }

}
