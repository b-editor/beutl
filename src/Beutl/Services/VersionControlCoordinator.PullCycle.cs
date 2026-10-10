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

            if (!await ConfirmPullAsync(confirmationCancellation.Token))
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
        catch (VersionControlLifecycleUnavailableException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pull because the project lifecycle changed before confirmation.");
            return new RemoteOpResult.Failed(PullProjectChangedMessage);
        }
        catch (InvalidOperationException ex) when (ex is not VersionControlConflictedException)
        {
            _logger.LogWarning(ex, "The pull failed before confirmation.");
            return new RemoteOpResult.Failed(ex.Message);
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
        catch (VersionControlLifecycleUnavailableException ex)
        {
            _logger.LogInformation(
                ex,
                "Skipped pull because the project lifecycle changed after confirmation.");
            return new RemoteOpResult.Failed(PullProjectChangedMessage);
        }
        catch (InvalidOperationException ex) when (ex is not VersionControlConflictedException)
        {
            // Every version-control domain failure derives from InvalidOperationException, such as a
            // missing Git LFS or a missing identity for the safety snapshot. Its own reason is what the
            // user can act on.
            _logger.LogWarning(ex, "The pull failed after confirmation.");
            return new RemoteOpResult.Failed(ex.Message);
        }
    }

    // The token follows the project/service epoch, so a close stops the preflight while it holds the
    // operation gate.
    private async Task<RemoteOpResult?> RunPullPreflightCycleAsync(
        CancellationToken cancellationToken)
    {
        using OperationLease operation = await BeginOperationAsync(
            cancellationToken,
            lifecycle: true);
        IProjectVersionControlBackend ownedService = GetTrackedBackend();
        return await ExecuteExclusiveOnUiThreadAsync(
            ownedService,
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

    // Like the restore and the branch switch, the pull takes its project transition before the operation
    // gate.
    private async Task<RemoteOpResult> RunPullMutationCycleAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfOperationUnavailable(lifecycle: true);
        Project project = GetOpenProject();
        string projectFile = GetProjectFile(project);
        IProjectVersionControlBackend ownedService = GetTrackedBackend();
        cancellationToken.ThrowIfCancellationRequested();
        await using ProjectService.ProjectTransitionScope transition =
            await _projectService.BeginVersionControlTransitionAsync(
                this,
                cancellationToken);
        using OperationLease operation = await BeginOperationAsync(
            cancellationToken,
            lifecycle: true);
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

            return await ExecuteExclusiveOnUiThreadAsync(
                ownedService,
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
