using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private const string PullProjectChangedMessage =
        "The open project changed while the pull was being prepared.";

    private async Task<RemoteOpResult> RunPullCycleAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? confirmationCancellation = null;
        try
        {
            confirmationCancellation =
                CreateProjectServiceEpochCancellation(cancellationToken);
            RemoteOpResult? preliminaryResult =
                await RunPullPreflightCycleAsync(confirmationCancellation.Token);
            if (preliminaryResult is not null)
            {
                return preliminaryResult;
            }

            if (!await ConfirmPullAsync(confirmationCancellation.Token).ConfigureAwait(false))
            {
                return new RemoteOpResult.Failed(string.Empty);
            }
        }
        catch (OperationCanceledException)
            when (confirmationCancellation?.IsCancellationRequested == true
                  && !cancellationToken.IsCancellationRequested)
        {
            return new RemoteOpResult.Failed(
                "The open project changed while the pull was awaiting confirmation.");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pull because its project/service epoch was unavailable before confirmation.");
            return new RemoteOpResult.Failed(PullProjectChangedMessage);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pull because the project lifecycle changed before confirmation.");
            return new RemoteOpResult.Failed(PullProjectChangedMessage);
        }
        finally
        {
            confirmationCancellation?.Dispose();
        }

        PullMutationOutcome outcome;
        try
        {
            outcome = await RunPullMutationCycleAsync(cancellationToken);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pull because its backend retired after confirmation.");
            return new RemoteOpResult.Failed(PullProjectChangedMessage);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pull because the project lifecycle changed after confirmation.");
            return new RemoteOpResult.Failed(PullProjectChangedMessage);
        }

        if (outcome.Recovery is not null)
        {
            await OfferUncertainPullRecoveryAsync(
                    outcome.Recovery,
                    outcome.ProjectFile)
                .ConfigureAwait(false);
        }

        return outcome.Result;
    }

    private async Task<RemoteOpResult?> RunPullPreflightCycleAsync(
        CancellationToken cancellationToken)
    {
        await BeginLifecycleOperationAsync(cancellationToken);
        bool gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            ThrowIfLifecycleOperationUnavailable();
            IProjectVersionControlBackend ownedService = GetTrackedBackend();
            return await ownedService.ExecuteExclusiveAsync(
                async service =>
                {
                    WorkspaceStatus status = await service.GetStatusAsync(cancellationToken);
                    if (!EnsureRepositoryIsNotConflicted(status))
                    {
                        return new RemoteOpResult.Failed(
                            Strings.VersionControl_ConflictGuidance);
                    }

                    CheckedOutBranchTip originalHead =
                        await service.GetCheckedOutBranchTipAsync(cancellationToken);
                    PullPreflightResult preflight = await service.PreflightPullAsync(
                        originalHead,
                        cancellationToken);
                    return preflight.Result is RemoteOpResult.Success
                           && preflight.RequiresTransition
                        ? null
                        : preflight.Result;
                },
                cancellationToken);
        }
        finally
        {
            FinishLifecycleOperation(gateEntered);
        }
    }

    private async Task<PullMutationOutcome> RunPullMutationCycleAsync(
        CancellationToken cancellationToken)
    {
        await BeginLifecycleOperationAsync(cancellationToken);
        bool gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            ThrowIfLifecycleOperationUnavailable();
            Project project = GetOpenProject();
            string projectFile = GetProjectFile(project);
            IProjectVersionControlBackend ownedService = GetTrackedBackend();
            cancellationToken.ThrowIfCancellationRequested();
            await using ProjectService.ProjectTransitionScope transition =
                await _projectService.BeginVersionControlTransitionAsync(
                    this,
                    cancellationToken);
            ThrowIfLifecycleOperationUnavailable();
            using IDisposable? worktreeMutation = TryBeginWorktreeMutation();
            if (worktreeMutation is null)
            {
                return new PullMutationOutcome(
                    new RemoteOpResult.Failed(Strings.VersionControl_WorkspaceBusy),
                    null,
                    projectFile);
            }

            try
            {
                if (!ReferenceEquals(_projectService.CurrentProject.Value, project)
                    || !ReferenceEquals(GetOwnedBackend(), ownedService))
                {
                    return new PullMutationOutcome(
                        new RemoteOpResult.Failed(PullProjectChangedMessage),
                        null,
                        projectFile);
                }

                return await ownedService.ExecuteExclusiveAsync(
                    service => PullWithinTransactionAsync(
                        service,
                        project,
                        projectFile,
                        transition,
                        cancellationToken),
                    cancellationToken);
            }
            finally
            {
                FinishInternalTransition();
            }
        }
        finally
        {
            FinishLifecycleOperation(gateEntered);
        }
    }

    private async Task<PullMutationOutcome> PullWithinTransactionAsync(
        IProjectVersionControlTransaction service,
        Project project,
        string projectFile,
        ProjectService.ProjectTransitionScope transition,
        CancellationToken cancellationToken)
    {
        // Held until the project is closed further down, so an edit made while the
        // preflight and checkpoint awaits run cannot miss the safety checkpoint.
        using IDisposable editorSuspension = _editorService.SuspendEditors();
        if (!await TrySaveOpenProjectAsync(project, cancellationToken))
        {
            return new PullMutationOutcome(
                new RemoteOpResult.Failed(
                    "The open project could not be saved before pulling."),
                null,
                projectFile);
        }

        WorkspaceStatus status = await service.GetStatusAsync(cancellationToken);
        if (!EnsureRepositoryIsNotConflicted(status))
        {
            return new PullMutationOutcome(
                new RemoteOpResult.Failed(
                    Strings.VersionControl_ConflictGuidance),
                null,
                projectFile);
        }

        CheckedOutBranchTip originalHead =
            await service.GetCheckedOutBranchTipAsync(cancellationToken);
        PullPreflightResult preflight = await service.PreflightPullAsync(
            originalHead,
            cancellationToken);
        if (preflight.Result is not RemoteOpResult.Success
            || !preflight.RequiresTransition)
        {
            return new PullMutationOutcome(preflight.Result, null, projectFile);
        }

        ProjectCheckpoint? checkpoint = status.IsClean
            ? null
            : await service.CreateProjectCheckpointAsync(
                PullSafetySnapshotMessage,
                CancellationToken.None);
        bool projectClosed = false;
        CheckedOutBranchTip expectedCurrentHead = originalHead;
        PendingPullRecovery? pendingRecovery = null;
        PullTransitionState pullTransitionState = PullTransitionState.Unchanged;
        // Same reason as the restore and branch-switch paths: the fast-forward
        // checkout runs uncancellable with the project closed, so the objects its
        // LFS smudge filter needs are pulled in here, while this is still
        // cancellable and the project is still open. The preflight's fetch moved
        // the remote-tracking ref and not the local branch, so the prefetch has to
        // name the fetched commit - a branch name would resolve to the pre-pull tip
        // and miss exactly the objects the checkout is about to need.
        if (preflight.UpstreamCommit is { } upstreamCommit)
        {
            await service.PrefetchCommitLfsObjectsAsync(
                upstreamCommit,
                LfsPrefetchScope.RepositoryWide,
                cancellationToken);
        }
        try
        {
            await CloseProjectForOperationAsync(transition, CancellationToken.None);
            projectClosed = true;
            FastForwardPullResult pull = await service.PullFastForwardAsync(
                originalHead,
                checkpoint,
                projectFile,
                cancellationToken);

            RemoteOpResult result = pull.Result;
            expectedCurrentHead = pull.Tip;
            pendingRecovery = pull.Recovery;
            if (pendingRecovery is not null)
            {
                PublishPendingPullRecoveriesChanged();
            }
            pullTransitionState = pull.TransitionState;
            if (pullTransitionState is PullTransitionState.OwnershipLost
                or PullTransitionState.RecoveryFailed)
            {
                return UncertainPullOutcome(pendingRecovery, projectFile);
            }

            if (result is not RemoteOpResult.Success)
            {
                Exception? recoveryFailure = await TryRecoverPullAsync(
                    service,
                    originalHead,
                    expectedCurrentHead,
                    checkpoint,
                    transition,
                    projectFile);
                if (recoveryFailure is not null)
                {
                    _logger.LogError(
                        recoveryFailure,
                        "Failed to recover a pull after {PullError}.",
                        GetRemoteOperationError(result));
                    return UncertainPullOutcome(pendingRecovery, projectFile);
                }

                await TryCompletePullRecoveryAsync(
                    service,
                    pendingRecovery,
                    checkpoint);
                return new PullMutationOutcome(result, null, projectFile);
            }

            CheckedOutBranchTip verifiedHead =
                await service.GetCheckedOutBranchTipAsync(CancellationToken.None);
            if (!BranchTipsEqual(verifiedHead, expectedCurrentHead))
            {
                throw new InvalidOperationException(
                    "The repository ref changed before the pulled project could be reopened.");
            }

            await ReopenProjectAsync(transition, projectFile);
            await TryCompletePullRecoveryAsync(
                service,
                pendingRecovery,
                checkpoint);
            return new PullMutationOutcome(new RemoteOpResult.Success(), null, projectFile);
        }
        catch (Exception ex)
        {
            if (projectClosed
                && pullTransitionState is PullTransitionState.OwnershipLost
                    or PullTransitionState.RecoveryFailed)
            {
                _logger.LogError(
                    ex,
                    "The pull transition became uncertain after the project was closed.");
                return UncertainPullOutcome(pendingRecovery, projectFile);
            }

            Exception? recoveryFailure = projectClosed
                ? await TryRecoverPullAsync(
                    service,
                    originalHead,
                    expectedCurrentHead,
                    checkpoint,
                    transition,
                    projectFile)
                : null;
            if (ex is OperationCanceledException
                && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (projectClosed && recoveryFailure is null)
            {
                await TryCompletePullRecoveryAsync(
                    service,
                    pendingRecovery,
                    checkpoint);
            }

            if (recoveryFailure is not null)
            {
                _logger.LogError(
                    recoveryFailure,
                    "Failed to recover a pull after {PullError}.",
                    GetErrorText(ex));
                return UncertainPullOutcome(pendingRecovery, projectFile);
            }

            _logger.LogError(ex, "Failed to pull project versions.");
            return new PullMutationOutcome(
                new RemoteOpResult.Failed(GetErrorText(ex)),
                null,
                projectFile);
        }
    }

    // The pull left the repository in a state only the pending recovery can settle, so that recovery is
    // offered once the pull cycle has released its gates.
    private static PullMutationOutcome UncertainPullOutcome(
        PendingPullRecovery? recovery,
        string projectFile)
    {
        return new PullMutationOutcome(
            new RemoteOpResult.Failed(Strings.VersionControl_PullTransitionUncertain),
            recovery,
            projectFile);
    }

    private async Task OfferUncertainPullRecoveryAsync(
        PendingPullRecovery recovery,
        string projectFile)
    {
        bool recovered;
        using (NonTransactionalOperationLease? operation =
               TryBeginNonTransactionalOperation(CancellationToken.None))
        {
            if (operation is null)
            {
                return;
            }

            recovered = await TryRecoverPendingPullBeforeOpeningAsync(
                    projectFile,
                    operation.CancellationToken,
                    recovery.Id)
                .ConfigureAwait(false);
        }

        if (!recovered || !File.Exists(projectFile))
        {
            return;
        }

        try
        {
            await _projectService.OpenProject(projectFile).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "The pending pull state {RecoveryId} was recovered, but its project could not be opened.",
                recovery.Id);
        }
    }

    private async Task<Exception?> TryRecoverPullAsync(
        IProjectVersionControlTransaction service,
        CheckedOutBranchTip originalHead,
        CheckedOutBranchTip expectedCurrentHead,
        ProjectCheckpoint? checkpoint,
        ProjectService.ProjectTransitionScope transition,
        string projectFile)
    {
        try
        {
            CheckedOutBranchTip actualHead = await service.GetCheckedOutBranchTipAsync(CancellationToken.None);
            if (!BranchTipsEqual(actualHead, expectedCurrentHead))
            {
                throw new InvalidOperationException(
                    "The repository HEAD changed while the pull was being recovered.");
            }

            if (!BranchTipsEqual(actualHead, originalHead))
            {
                BranchTipRollbackResult rollback = await service.TryRollbackBranchTipAsync(
                    expectedCurrentHead,
                    originalHead,
                    CancellationToken.None);
                switch (rollback)
                {
                    case BranchTipRollbackResult.RolledBack:
                        break;
                    case BranchTipRollbackResult.RefChanged changed:
                        throw new InvalidOperationException(
                            $"The repository ref changed to '{changed.ActualCommit}' while the pull was being recovered.");
                    case BranchTipRollbackResult.UnsafeRepositoryState:
                        throw new InvalidOperationException(
                            "The repository contains changes that prevent a safe pull rollback.");
                }
            }

            if (checkpoint is not null)
            {
                await service.RestoreProjectCheckpointAsync(
                    checkpoint,
                    CancellationToken.None);
            }

            CheckedOutBranchTip recoveredHead = await service.GetCheckedOutBranchTipAsync(
                CancellationToken.None);
            if (!BranchTipsEqual(recoveredHead, originalHead))
            {
                throw new InvalidOperationException(
                    "The repository ref changed after the pull state was restored.");
            }

            await ReopenProjectAsync(transition, projectFile);
            return null;
        }
        catch (Exception recoveryException)
        {
            return recoveryException;
        }
    }

    private async Task TryDeleteCheckpointAsync(
        IProjectVersionControlTransaction service,
        ProjectCheckpoint? checkpoint)
    {
        if (checkpoint is null)
        {
            return;
        }

        try
        {
            await service.DeleteProjectCheckpointAsync(
                checkpoint,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete completed project checkpoint {CheckpointRef}.",
                checkpoint.RefName);
        }
    }

    private async Task TryCompletePullRecoveryAsync(
        IProjectVersionControlTransaction service,
        PendingPullRecovery? recovery,
        ProjectCheckpoint? checkpoint)
    {
        if (recovery is null)
        {
            await TryDeleteCheckpointAsync(service, checkpoint);
            return;
        }

        try
        {
            await service.CompletePendingPullRecoveryAsync(
                recovery,
                CancellationToken.None);
            CompletePendingPullRecoveryPublication(recovery.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete completed pending pull recovery {RecoveryId}.",
                recovery.Id);
        }
    }

    private static string GetRemoteOperationError(RemoteOpResult result)
    {
        return result switch
        {
            RemoteOpResult.AuthFailed failed => failed.Guidance,
            RemoteOpResult.Failed failed => failed.Stderr,
            RemoteOpResult.Diverged => Strings.VersionControl_Diverged,
            RemoteOpResult.Offline => Strings.VersionControl_Offline,
            RemoteOpResult.RepositoryDirty => Strings.VersionControl_RepositoryDirty,
            RemoteOpResult.Success => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
    }

    private sealed record PullMutationOutcome(
        RemoteOpResult Result,
        PendingPullRecovery? Recovery,
        string ProjectFile);
}
