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

        CheckedOutBranchTip expectedResultTip = originalTip;
        CheckedOutBranchTip? createdBranchTip = null;
        bool projectClosed = false;
        // The checkout below runs uncancellable with the project closed, and its LFS
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

            if (branchName is null)
            {
                CommitResult restoreResult = await service.CommitProjectTreeAsync(
                    originalTip,
                    sha,
                    GetRestoreCommitMessage(sha),
                    SnapshotKind.Restore,
                    CancellationToken.None);

                expectedResultTip = GetExpectedTipAfterCommit(
                    originalTip,
                    restoreResult);
                EnsureAutomaticSnapshotWasNotSkipped(restoreResult);
            }
            else
            {
                CheckedOutBranchTip branchTip;
                try
                {
                    // Branching at the selected commit would check that whole tree out,
                    // so in an enclosing repository it would roll back files outside the
                    // project and could overwrite ignored ones. Branch from the current
                    // tip instead and apply only the project tree on top of it.
                    await service.CreateBranchAsync(
                        branchName,
                        originalTip.Commit,
                        CancellationToken.None);
                    createdBranchTip = new CheckedOutBranchTip(
                        $"refs/heads/{branchName}",
                        originalTip.Commit);
                    branchTip = await service.GetCheckedOutBranchTipAsync(
                        CancellationToken.None);
                    expectedResultTip = branchTip;
                    if (!BranchTipsEqual(branchTip, createdBranchTip))
                    {
                        throw new InvalidOperationException(
                            "The restore branch changed immediately after it was created.");
                    }
                }
                catch
                {
                    expectedResultTip = await service.GetCheckedOutBranchTipAsync(
                        CancellationToken.None);
                    throw;
                }

                CommitResult restoreResult = await service.CommitProjectTreeAsync(
                    branchTip,
                    sha,
                    GetRestoreCommitMessage(sha),
                    SnapshotKind.Restore,
                    CancellationToken.None);

                expectedResultTip = GetExpectedTipAfterCommit(
                    branchTip,
                    restoreResult);
                createdBranchTip = expectedResultTip;
                EnsureAutomaticSnapshotWasNotSkipped(restoreResult);
            }

            await ReopenProjectAsync(transition, projectFile);
            return true;
        }
        catch (Exception ex)
        {
            Exception? recoveryFailure = null;
            if (projectClosed)
            {
                recoveryFailure = await TryRestoreOriginalStateAsync(
                    service,
                    originalTip,
                    expectedResultTip,
                    branchName is null ? RecoveryKind.Restore : RecoveryKind.Branch,
                    transition,
                    projectFile);
            }

            if (recoveryFailure is null && createdBranchTip is not null)
            {
                PublishRetainedRestoreBranchWarning(createdBranchTip);
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

    private async Task<Exception?> TryRestoreOriginalStateAsync(
        IProjectVersionControlTransaction service,
        CheckedOutBranchTip originalTip,
        CheckedOutBranchTip expectedResultTip,
        RecoveryKind recoveryKind,
        ProjectService.ProjectTransitionScope transition,
        string projectFile)
    {
        try
        {
            CheckedOutBranchTip actualTip = await service.GetCheckedOutBranchTipAsync(
                CancellationToken.None);
            if (!BranchTipsEqual(actualTip, expectedResultTip))
            {
                throw new InvalidOperationException(
                    "The checked-out branch changed before the operation could be recovered.");
            }

            if (recoveryKind == RecoveryKind.Branch)
            {
                if (!BranchTipsEqual(actualTip, originalTip))
                {
                    await service.SwitchBranchAsync(
                        GetLocalBranchName(originalTip.RefName),
                        CancellationToken.None);
                    CheckedOutBranchTip restoredTip = await service.GetCheckedOutBranchTipAsync(
                        CancellationToken.None);
                    if (!BranchTipsEqual(restoredTip, originalTip))
                    {
                        throw new InvalidOperationException(
                            "The original branch ref changed while the branch operation was being recovered.");
                    }
                }
            }
            else
            {
                if (!string.Equals(
                        actualTip.RefName,
                        originalTip.RefName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The restore operation is no longer on its original branch.");
                }

                CommitResult recovery = await service.CommitProjectTreeAsync(
                    expectedResultTip,
                    originalTip.Commit,
                    RestoreRecoveryMessage,
                    SnapshotKind.Recovery,
                    CancellationToken.None);
                EnsureAutomaticSnapshotWasNotSkipped(recovery);
                CheckedOutBranchTip expectedRecoveryTip = GetExpectedTipAfterCommit(
                    expectedResultTip,
                    recovery);
                CheckedOutBranchTip verifiedRecoveryTip = await service.GetCheckedOutBranchTipAsync(
                    CancellationToken.None);
                if (!BranchTipsEqual(verifiedRecoveryTip, expectedRecoveryTip))
                {
                    throw new InvalidOperationException(
                        "The branch ref changed while the restore operation was being recovered.");
                }
            }
        }
        catch (Exception recoveryException)
        {
            return recoveryException;
        }

        try
        {
            await ReopenProjectAsync(transition, projectFile);
            return null;
        }
        catch (Exception reopenException)
        {
            return reopenException;
        }
    }

    private void PublishRetainedRestoreBranchWarning(CheckedOutBranchTip createdBranchTip)
    {
        string branchName = GetLocalBranchName(createdBranchTip.RefName);
        _logger.LogWarning(
            "Retained failed restore branch {BranchRef} at {Commit} for manual cleanup.",
            createdBranchTip.RefName,
            createdBranchTip.Commit);
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

    private enum RecoveryKind
    {
        Branch,
        Restore,
    }
}
