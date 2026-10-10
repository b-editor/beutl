using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private Task<bool> RunRestoreCycleAsync(
        string sha,
        string? branchName,
        CancellationToken cancellationToken)
    {
        return RunProjectTransitionCycleAsync(
            async transition =>
            {
                Project project = GetOpenProject();
                string projectFile = GetProjectFile(project);
                IProjectVersionControlBackend ownedService = GetTrackedBackend();
                if (ownedService.Repository is null)
                {
                    throw new InvalidOperationException(
                        "The open project is not tracked with Git.");
                }

                return await ownedService.ExecuteExclusiveAsync(
                    service => RestoreWithinTransactionAsync(
                        service,
                        sha,
                        branchName,
                        project,
                        projectFile,
                        transition,
                        cancellationToken),
                    cancellationToken);
            },
            cancellationToken);
    }

    private async Task<bool> RestoreWithinTransactionAsync(
        IProjectVersionControlTransaction service,
        string sha,
        string? branchName,
        Project project,
        string projectFile,
        ProjectService.ProjectTransitionScope transition,
        CancellationToken cancellationToken)
    {
        if (!await CheckRestorePreconditionsAsync(
                service,
                sha,
                branchName,
                projectFile,
                cancellationToken))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (branchName is not null
            && !await service.CanCreateBranchAsync(
                branchName,
                CancellationToken.None))
        {
            return false;
        }

        // Held until the project is closed further down: the awaits between this save
        // and the close run real Git commands, and an edit made in that window would
        // miss the safety snapshot and be discarded when the editors close.
        using IDisposable editorSuspension = _editorService.SuspendEditors();
        if (!await TrySaveOpenProjectAsync(project, CancellationToken.None))
        {
            PublishNotification(() =>
                NotificationService.ShowError(
                    Strings.VersionControl,
                    MessageStrings.OperationFailed));
            return false;
        }

        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);
        if (!EnsureRepositoryIsNotConflicted(status))
        {
            return false;
        }

        CheckedOutBranchTip? originalTip = await GetTipAfterSafetySnapshotAsync(
            service,
            status,
            RestoreSafetySnapshotMessage,
            "The branch ref changed while the restore safety snapshot was committed.");
        if (originalTip is null)
        {
            return false;
        }

        if (branchName is not null
            && !await service.CanCreateBranchAsync(
                branchName,
                CancellationToken.None))
        {
            return false;
        }

        bool projectClosed = false;
        bool restoreCommitted = false;
        // The restore below runs uncancellable with the project closed, and its LFS
        // smudge filter would download missing objects there - a stalled endpoint would
        // strand the closed project. Pull them in first, while the operation is still
        // cancellable and the project is still open.
        await service.PrefetchCommitLfsObjectsAsync(
            sha,
            LfsPrefetchScope.ProjectPathspec,
            cancellationToken);
        try
        {
            await CloseProjectForOperationAsync(transition, CancellationToken.None);
            projectClosed = true;

            if (branchName is not null)
            {
                // Branching at the selected commit would check that whole tree out, so in an
                // enclosing repository it would roll back files outside the project and could
                // overwrite ignored ones. Branch from the current tip instead and restore only
                // the project tree on top of it.
                await service.CreateBranchAsync(
                    branchName,
                    originalTip.Commit,
                    CancellationToken.None);
            }

            CommitResult restoreResult = await service.RestoreProjectTreeAsync(
                sha,
                GetRestoreCommitMessage(sha),
                SnapshotKind.Restore,
                CancellationToken.None);
            EnsureAutomaticSnapshotWasNotSkipped(restoreResult);
            restoreCommitted = restoreResult is CommitResult.Committed;

            await ReopenProjectAsync(transition, projectFile);
            return true;
        }
        catch (Exception ex)
        {
            Exception? recoveryFailure = null;
            bool returnedToOriginalBranch = false;
            if (projectClosed)
            {
                (recoveryFailure, returnedToOriginalBranch) = await TryRestoreOriginalStateAsync(
                    service,
                    originalTip,
                    revertCommittedRestore: restoreCommitted && branchName is null,
                    transition,
                    projectFile);
            }

            if (recoveryFailure is null && returnedToOriginalBranch && branchName is not null)
            {
                PublishRetainedRestoreBranchWarning(branchName);
            }

            if (recoveryFailure is not null)
            {
                PublishUnrecoveredRestoreFailure(ex, recoveryFailure, sha);
                return false;
            }

            if (ex is OperationCanceledException
                && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            PublishRestoreFailure(ex, sha);
            return false;
        }
        finally
        {
            FinishInternalTransition();
        }
    }

    private async Task<bool> CheckRestorePreconditionsAsync(
        IProjectVersionControlTransaction service,
        string sha,
        string? branchName,
        string projectFile,
        CancellationToken cancellationToken)
    {
        if (branchName is not null
            && !await service.CanCreateBranchAsync(
                branchName,
                cancellationToken))
        {
            return false;
        }

        if (!await service.RevisionContainsProjectFileAsync(
                sha,
                projectFile,
                cancellationToken))
        {
            PublishNotification(() =>
                NotificationService.ShowWarning(
                    Strings.VersionControl,
                    string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        Strings.VersionControl_RevisionMissingProject,
                        GetShortSha(sha))));
            return false;
        }

        WorkspaceStatus status = await service.GetStatusAsync(cancellationToken);
        if (!EnsureRepositoryIsNotConflicted(status))
        {
            return false;
        }

        return await ConfirmRestoreAsync(cancellationToken);
    }

    private static string GetRestoreCommitMessage(string sha)
    {
        return $"beutl: restore project state from {GetShortSha(sha)}";
    }

    private void PublishUnrecoveredRestoreFailure(
        Exception exception,
        Exception recoveryFailure,
        string sha)
    {
        var combined = new AggregateException(
            CombinedFailureMessage,
            exception,
            recoveryFailure);
        _logger.LogError(
            combined,
            "Failed to restore project version {Commit}, and the original state could not be recovered.",
            sha);
        PublishNotification(() =>
            NotificationService.ShowError(
                Strings.VersionControl_ErrorTitle,
                string.Format(
                    Strings.VersionControl_RecoveryFailed,
                    GetErrorText(exception),
                    GetErrorText(recoveryFailure))));
    }

    private void PublishRestoreFailure(Exception exception, string sha)
    {
        _logger.LogError(exception, "Failed to restore project version {Commit}.", sha);
        PublishNotification(() =>
            NotificationService.ShowError(
                Strings.VersionControl_ErrorTitle,
                GetErrorText(exception)));
    }

    // Brings the closed project back to where the operation started and reopens it. When HEAD
    // moved to another branch - a switch, or a restore to a new branch - git switch returns to the
    // original one, and the result reports that a branch the restore created stays behind. A
    // restore committed in place is undone by restoring the original tree as a new commit, so
    // history is never rewritten. A restore that failed before its commit has already put the
    // project's files back.
    private async Task<(Exception? Failure, bool ReturnedToOriginalBranch)> TryRestoreOriginalStateAsync(
        IProjectVersionControlTransaction service,
        CheckedOutBranchTip originalTip,
        bool revertCommittedRestore,
        ProjectService.ProjectTransitionScope transition,
        string projectFile)
    {
        bool returnedToOriginalBranch = false;
        try
        {
            CheckedOutBranchTip currentTip = await service.GetCheckedOutBranchTipAsync(
                CancellationToken.None);
            if (!string.Equals(currentTip.RefName, originalTip.RefName, StringComparison.Ordinal))
            {
                await service.SwitchBranchAsync(
                    GetLocalBranchName(originalTip.RefName),
                    CancellationToken.None);
                returnedToOriginalBranch = true;
            }
            else if (revertCommittedRestore)
            {
                CommitResult recovery = await service.RestoreProjectTreeAsync(
                    originalTip.Commit,
                    RestoreRecoveryMessage,
                    SnapshotKind.Recovery,
                    CancellationToken.None);
                EnsureAutomaticSnapshotWasNotSkipped(recovery);
            }
        }
        catch (Exception recoveryException)
        {
            return (recoveryException, returnedToOriginalBranch);
        }

        try
        {
            await ReopenProjectAsync(transition, projectFile);
            return (null, returnedToOriginalBranch);
        }
        catch (Exception reopenException)
        {
            return (reopenException, returnedToOriginalBranch);
        }
    }

    private void PublishRetainedRestoreBranchWarning(string branchName)
    {
        _logger.LogWarning(
            "Retained failed restore branch {Branch} for manual cleanup.",
            branchName);
        PublishNotification(() =>
            NotificationService.ShowWarning(
                Strings.VersionControl,
                string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.VersionControl_RestoreBranchRetainedFormat,
                    branchName)));
    }

    private static string GetLocalBranchName(string refName)
    {
        const string Prefix = "refs/heads/";
        if (!refName.StartsWith(Prefix, StringComparison.Ordinal)
            || refName.Length == Prefix.Length)
        {
            throw new ArgumentException("A local branch ref is required.", nameof(refName));
        }

        return refName[Prefix.Length..];
    }
}
