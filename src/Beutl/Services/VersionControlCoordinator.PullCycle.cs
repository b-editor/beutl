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

        try
        {
            return await RunPullMutationCycleAsync(cancellationToken);
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

    private async Task<RemoteOpResult> RunPullMutationCycleAsync(
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
                return new RemoteOpResult.Failed(Strings.VersionControl_WorkspaceBusy);
            }

            try
            {
                if (!ReferenceEquals(_projectService.CurrentProject.Value, project)
                    || !ReferenceEquals(GetOwnedBackend(), ownedService))
                {
                    return new RemoteOpResult.Failed(PullProjectChangedMessage);
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

    private async Task<RemoteOpResult> PullWithinTransactionAsync(
        IProjectVersionControlTransaction service,
        Project project,
        string projectFile,
        ProjectService.ProjectTransitionScope transition,
        CancellationToken cancellationToken)
    {
        // Held until the project is closed further down, so an edit made while the preflight and
        // the prefetch run is neither lost nor left out of the files the pull sets aside.
        using IDisposable editorSuspension = _editorService.SuspendEditors();
        if (!await TrySaveOpenProjectAsync(project, cancellationToken))
        {
            return new RemoteOpResult.Failed(
                "The open project could not be saved before pulling.");
        }

        WorkspaceStatus status = await service.GetStatusAsync(cancellationToken);
        if (!EnsureRepositoryIsNotConflicted(status))
        {
            return new RemoteOpResult.Failed(Strings.VersionControl_ConflictGuidance);
        }

        CheckedOutBranchTip originalHead =
            await service.GetCheckedOutBranchTipAsync(cancellationToken);
        PullPreflightResult preflight = await service.PreflightPullAsync(
            originalHead,
            cancellationToken);
        if (preflight.Result is not RemoteOpResult.Success
            || !preflight.RequiresTransition
            || preflight.UpstreamCommit is not { } upstreamCommit)
        {
            return preflight.Result;
        }

        // Same reason as the restore and branch-switch paths: the merge runs uncancellable with
        // the project closed, so the objects its LFS smudge filter needs are pulled in here, while
        // this is still cancellable and the project is still open. The preflight's fetch moved the
        // remote-tracking ref and not the local branch, so the prefetch has to name the fetched
        // commit - a branch name would resolve to the pre-pull tip and miss exactly the objects the
        // merge is about to need.
        await service.PrefetchCommitLfsObjectsAsync(
            upstreamCommit,
            LfsPrefetchScope.RepositoryWide,
            cancellationToken);
        try
        {
            await CloseProjectForOperationAsync(transition, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close the project before pulling.");
            return new RemoteOpResult.Failed(GetErrorText(ex));
        }

        // Whatever Git reports, the project reopens on the files Git left.
        RemoteOpResult result;
        try
        {
            result = await service.PullFastForwardAsync(upstreamCommit, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pull project versions.");
            result = new RemoteOpResult.Failed(GetErrorText(ex));
        }

        try
        {
            await ReopenProjectAsync(transition, projectFile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reopen the project after pulling.");
            return new RemoteOpResult.Failed(
                result is RemoteOpResult.Success
                    ? GetErrorText(ex)
                    : string.Format(
                        Strings.VersionControl_RecoveryFailed,
                        GetRemoteOperationError(result),
                        GetErrorText(ex)));
        }

        return result;
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
}
