using Beutl.Editor.VersionControl;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private async Task<bool> RunBranchCycleAsync(
        string branchName,
        bool create,
        CancellationToken cancellationToken)
    {
        await BeginLifecycleOperationAsync(cancellationToken);
        bool gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            ThrowIfLifecycleOperationUnavailable();
            await using ProjectService.ProjectTransitionScope transition =
                await _projectService.BeginVersionControlTransitionAsync(this, cancellationToken);
            ThrowIfLifecycleOperationUnavailable();
            using IDisposable? worktreeMutation = TryBeginWorktreeMutation();
            if (worktreeMutation is null)
            {
                return false;
            }

            Project project = GetOpenProject();
            string projectFile = GetProjectFile(project);
            IProjectVersionControlBackend ownedService = GetTrackedBackend();
            return await ownedService.ExecuteExclusiveAsync(
                async service =>
                {
                    if (create)
                    {
                        return await CreateBranchAtCheckedOutCommitAsync(
                            service,
                            branchName,
                            cancellationToken);
                    }

                    if (!await SwitchTargetExistsAsync(
                            service,
                            branchName,
                            cancellationToken))
                    {
                        return false;
                    }

                    WorkspaceStatus status = await service.GetStatusAsync(cancellationToken);
                    if (!EnsureRepositoryIsNotConflicted(status))
                    {
                        return false;
                    }

                    if (string.Equals(status.Branch, branchName, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    if (!await ConfirmSwitchBranchAsync(branchName, cancellationToken))
                    {
                        return false;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
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

                    status = await service.GetStatusAsync(CancellationToken.None);
                    if (!EnsureRepositoryIsNotConflicted(status))
                    {
                        return false;
                    }

                    CheckedOutBranchTip originalTip = await service.GetCheckedOutBranchTipAsync(
                        CancellationToken.None);
                    if (!status.IsClean)
                    {
                        CommitResult? result = await CommitSafetySnapshotAsync(
                            service,
                            SwitchSafetySnapshotMessage,
                            CancellationToken.None);
                        if (result is null)
                        {
                            return false;
                        }

                        CheckedOutBranchTip committedTip = await service.GetCheckedOutBranchTipAsync(
                            CancellationToken.None);
                        originalTip = GetExpectedTipAfterCommitAll(
                            originalTip,
                            result,
                            committedTip);
                        if (!BranchTipsEqual(committedTip, originalTip))
                        {
                            throw new InvalidOperationException(
                                "The branch ref changed while the switch safety snapshot was committed.");
                        }
                    }

                    if (!await SwitchTargetExistsAsync(
                            service,
                            branchName,
                            CancellationToken.None))
                    {
                        return false;
                    }

                    // The switch below runs uncancellable with the project closed, and its LFS
                    // smudge filter would download missing objects there - a stalled endpoint
                    // would strand the closed project. Pull them in first, while the operation
                    // is still cancellable and the project is still open.
                    await service.PrefetchBranchLfsObjectsAsync(branchName, cancellationToken);

                    CheckedOutBranchTip expectedResultTip = originalTip;
                    bool projectClosed = false;
                    try
                    {
                        await CloseProjectForOperationAsync(transition, CancellationToken.None);
                        projectClosed = true;
                        try
                        {
                            await service.SwitchBranchAsync(
                                branchName,
                                CancellationToken.None);
                        }
                        catch
                        {
                            expectedResultTip = await service.GetCheckedOutBranchTipAsync(
                                CancellationToken.None);
                            throw;
                        }

                        expectedResultTip = await service.GetCheckedOutBranchTipAsync(
                            CancellationToken.None);
                        await ReopenProjectAsync(transition, projectFile);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Exception? recoveryFailure = projectClosed
                            ? await TryRestoreOriginalStateAsync(
                                service,
                                originalTip,
                                expectedResultTip,
                                RecoveryKind.Branch,
                                transition,
                                projectFile)
                            : null;
                        return HandleCycleFailure(
                            ex,
                            recoveryFailure,
                            $"branch '{branchName}'",
                            cancellationToken);
                    }
                    finally
                    {
                        FinishInternalTransition();
                    }
                },
                cancellationToken);
        }
        finally
        {
            FinishLifecycleOperation(gateEntered);
        }
    }

    // Like git switch, a branch that so far exists only on origin can be switched to, which creates
    // the local branch tracking it. The name still has to match exactly.
    private static async Task<bool> SwitchTargetExistsAsync(
        IProjectVersionControlTransaction service,
        string branchName,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BranchInfo> branches = await service.GetBranchesAsync(cancellationToken);
        BranchInfo? target = branches.FirstOrDefault(branch =>
            string.Equals(branch.Name, branchName, StringComparison.Ordinal));
        return target is not null
               && (!target.IsRemote
                   || await service.CanCreateBranchAsync(branchName, cancellationToken));
    }

    // Like git switch -c, a new branch starts at the checked-out commit and changes no file. The
    // project therefore stays open with its unsaved edits and undo history, and nothing needs a
    // safety snapshot.
    private async Task<bool> CreateBranchAtCheckedOutCommitAsync(
        IProjectVersionControlTransaction service,
        string branchName,
        CancellationToken cancellationToken)
    {
        if (!await CanCreateBranchAsync(service, branchName, cancellationToken))
        {
            return false;
        }

        WorkspaceStatus status = await service.GetStatusAsync(cancellationToken);
        if (!EnsureRepositoryIsNotConflicted(status))
        {
            return false;
        }

        CheckedOutBranchTip tip = await service.GetCheckedOutBranchTipAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // The new branch becomes the checked-out one, so a pull or recovery still awaiting confirmation
        // no longer describes the branch it would change. A switch cancels those when it closes the
        // project; creating a branch keeps the project open, so it cancels them here.
        AdvanceProjectServiceEpoch();
        try
        {
            await service.CreateBranchAsync(branchName, tip.Commit, CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            return HandleCycleFailure(
                ex,
                recoveryFailure: null,
                $"branch '{branchName}'",
                cancellationToken);
        }
    }

    private static Task<bool> CanCreateBranchAsync(
        IProjectVersionControlTransaction service,
        string branchName,
        CancellationToken cancellationToken)
        => service.CanCreateBranchAsync(branchName, cancellationToken);
}
