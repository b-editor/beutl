using Avalonia.Threading;
using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private async Task<IDisposable> SuspendEditorsAsync(CancellationToken cancellationToken)
    {
        if (_dispatcher.CheckAccess())
        {
            return _editorService.SuspendEditors();
        }

        return await _dispatcher.InvokeAsync(
            () => _editorService.SuspendEditors(),
            DispatcherPriority.Normal,
            cancellationToken);
    }

    // Shows the activity and suspends the editors in one dispatcher job, and the returned handle undoes both
    // in the reverse order. Beginning the activity off the UI thread would publish it only after the editors
    // are disabled, and a render in between would show them disabled without it.
    private async Task<IDisposable> SuspendEditorsBehindActivityAsync(
        ProjectLifecycleActivity activity,
        CancellationToken cancellationToken)
    {
        if (_dispatcher.CheckAccess())
        {
            return Suspend();
        }

        return await _dispatcher.InvokeAsync(Suspend, DispatcherPriority.Normal, cancellationToken);

        IDisposable Suspend()
        {
            IDisposable presentation = _editorService.BeginLifecycleActivity(activity);
            try
            {
                return new PresentedEditorSuspension(_editorService.SuspendEditors(), presentation);
            }
            catch
            {
                presentation.Dispose();
                throw;
            }
        }
    }

    private async Task ReleaseEditorSuspensionAsync(IDisposable? suspension)
    {
        if (suspension is null)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            suspension.Dispose();
            return;
        }

        await _dispatcher.InvokeAsync(suspension.Dispose);
    }

    private static bool BranchTipsEqual(CheckedOutBranchTip left, CheckedOutBranchTip right)
    {
        return string.Equals(left.RefName, right.RefName, StringComparison.Ordinal)
               && string.Equals(left.Commit, right.Commit, StringComparison.OrdinalIgnoreCase);
    }

    private static CheckedOutBranchTip GetExpectedTipAfterCommit(
        CheckedOutBranchTip previousTip,
        CommitResult result)
    {
        return result switch
        {
            CommitResult.Committed { Revision: CommitRevision.Known revision }
                => new CheckedOutBranchTip(
                    previousTip.RefName,
                    revision.Sha),
            CommitResult.NoChanges or CommitResult.SkippedNoIdentity => previousTip,
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
    }

    private static CheckedOutBranchTip GetExpectedTipAfterCommitAll(
        CheckedOutBranchTip previousTip,
        CommitResult result,
        CheckedOutBranchTip observedTip)
    {
        if (result is CommitResult.Committed { Revision: CommitRevision.Unavailable })
        {
            if (!string.Equals(
                    observedTip.RefName,
                    previousTip.RefName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The checked-out branch changed while the snapshot commit revision was resolved.");
            }

            return observedTip;
        }

        return GetExpectedTipAfterCommit(previousTip, result);
    }

    private void EnsureProjectReopened(string projectFile)
    {
        string? reopenedPath = _projectService.CurrentProject.Value?.Uri?.LocalPath;
        if (reopenedPath is null || !PathsEqual(reopenedPath, projectFile))
        {
            throw new InvalidOperationException(
                "The project could not be reopened after restoring files.");
        }
    }

    private IProjectVersionControlBackend GetTrackedBackend()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IProjectVersionControlBackend service = GetOperationReadyBackend()
                                                ?? throw new InvalidOperationException(
                                                    "Version control is not available.");
        if (service.Repository is null)
        {
            throw new InvalidOperationException(
                "The open project is not tracked with Git.");
        }

        return service;
    }

    private IProjectVersionControlBackend? GetOperationReadyBackend()
    {
        lock (_stateGate)
        {
            return ReferenceEquals(_state.OwnedService, _state.VisibleService)
                ? _state.OwnedService
                : null;
        }
    }

    private IProjectVersionControlBackend? GetOwnedBackend()
    {
        lock (_stateGate)
        {
            return _state.OwnedService;
        }
    }

    private Project GetOpenProject()
    {
        return _projectService.CurrentProject.Value
               ?? throw new InvalidOperationException("No project is open.");
    }

    private static string GetProjectFile(Project project)
    {
        return project.Uri?.LocalPath
               ?? throw new InvalidOperationException("The project has no file path.");
    }

    private IDisposable? TryBeginWorktreeMutation(
        IProjectFileWriteLease? completedWrite = null)
    {
        IDisposable? mutation = _editorService.TryBeginWorktreeMutation(completedWrite);
        if (mutation is not null)
        {
            return mutation;
        }

        PublishNotification(() =>
            NotificationService.ShowWarning(
                Strings.VersionControl,
                Strings.VersionControl_WorkspaceBusy));
        return null;
    }

    private async Task<bool> TrySaveOpenProjectAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _editorService.SaveProjectFilesAsync(project, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // OnSave runs third-party editor code and real file I/O, and this runs outside the
            // cycle's own error handling, so a throw here must not escape as a raw message.
            _logger.LogError(
                ex,
                "Failed to save the open project before changing version-controlled files.");
            return false;
        }
    }

    // Returns null when the requested safety point could not be established. The caller observed a
    // dirty workspace before invoking this method, so NoChanges is safe only if a fresh status says
    // the workspace became clean; a hook can otherwise empty only the temporary index while leaving
    // the worktree dirty.
    private async Task<CommitResult?> CommitSafetySnapshotAsync(
        IProjectVersionControlTransaction service,
        string message,
        CancellationToken cancellationToken)
    {
        CommitResult result = await service.CommitAllAsync(
            message,
            SnapshotKind.Safety,
            cancellationToken);
        if (result is CommitResult.SkippedNoIdentity)
        {
            PublishNotification(() =>
                NotificationService.ShowWarning(
                    Strings.VersionControl,
                    Strings.VersionControl_MissingIdentityNotice));
            return null;
        }

        if (result is CommitResult.NoChanges)
        {
            WorkspaceStatus status = await service.GetStatusAsync(cancellationToken);
            if (!status.IsClean)
            {
                PublishNotification(() =>
                    NotificationService.ShowWarning(
                        Strings.VersionControl,
                        Strings.VersionControl_SaveSnapshotFailed));
                return null;
            }
        }

        return result;
    }

    private bool EnsureRepositoryIsNotConflicted(WorkspaceStatus status)
    {
        if (!status.HasConflicts)
        {
            return true;
        }

        PublishNotification(() =>
            NotificationService.ShowWarning(
                Strings.VersionControl,
                Strings.VersionControl_ConflictGuidance));
        return false;
    }

    private async Task CloseProjectForOperationAsync(
        ProjectService.ProjectTransitionScope transition,
        CancellationToken cancellationToken)
    {
        await transition.CloseProjectAsync(cancellationToken);

        if (_projectService.CurrentProject.Value is not null)
        {
            throw new InvalidOperationException(
                "The project could not be closed before changing version-controlled files.");
        }
    }

    private async Task ReopenProjectAsync(
        ProjectService.ProjectTransitionScope transition,
        string projectFile)
    {
        RepositoryInfo repository = GetOwnedBackend()?.Repository
                                    ?? throw new InvalidOperationException(
                                        "The repository is unavailable before reopening the project.");
        EnsureProjectFileIsPhysicallyContained(repository, projectFile);
        await transition.OpenProjectAsync(projectFile);
        EnsureProjectReopened(projectFile);
    }

    private bool HandleCycleFailure(
        Exception exception,
        Exception? recoveryFailure,
        string operation,
        CancellationToken cancellationToken)
    {
        if (recoveryFailure is not null)
        {
            var combined = new AggregateException(
                "The version-control operation and recovery both failed.",
                exception,
                recoveryFailure);
            _logger.LogError(
                combined,
                "Failed to complete version-control operation {Operation}, and the original state could not be recovered.",
                operation);
            PublishNotification(() =>
                NotificationService.ShowError(
                    Strings.VersionControl_ErrorTitle,
                    string.Format(
                        Strings.VersionControl_RecoveryFailed,
                        GetErrorText(exception),
                        GetErrorText(recoveryFailure))));
            return false;
        }

        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        _logger.LogError(
            exception,
            "Failed to complete version-control operation {Operation}.",
            operation);
        PublishNotification(() =>
            NotificationService.ShowError(
                Strings.VersionControl_ErrorTitle,
                GetErrorText(exception)));
        return false;
    }

    private static void EnsureAutomaticSnapshotWasNotSkipped(CommitResult result)
    {
        if (result is CommitResult.SkippedNoIdentity)
        {
            throw new GitIdentityRequiredException();
        }
    }

    private static string GetShortSha(string sha)
    {
        return sha[..Math.Min(7, sha.Length)];
    }

    private static string GetErrorText(Exception exception)
    {
        return exception is GitOperationException { Stderr.Length: > 0 } gitException
            ? gitException.Stderr
            : exception.Message;
    }

    private static string GetProjectRoot(Project project)
    {
        string projectPath = project.Uri?.LocalPath
                             ?? throw new InvalidOperationException("The project has no file path.");
        return Path.GetDirectoryName(projectPath)
               ?? throw new InvalidOperationException("The project file has no parent directory.");
    }

    // Enables the editors again before hiding the activity that covers them.
    private sealed class PresentedEditorSuspension(
        IDisposable suspension,
        IDisposable presentation) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                suspension.Dispose();
            }
            finally
            {
                presentation.Dispose();
            }
        }
    }
}
