namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private async Task<ProjectCheckpoint> CreateProjectCheckpointCoreAsync(
        string message,
        CancellationToken cancellationToken)
    {
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        RepositoryInfo repository = GetRepository();
        ValidateProjectSnapshotLayout(repository.ProjectRoot);
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        CheckedOutBranchTip baseHead = await GetCheckedOutBranchTipCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (!await IsProjectIndexCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new ProjectCheckpointStagedChangesException();
        }

        GitIdentity? identity = await GetIdentityCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            await RaiseMissingIdentityNoticeIfNeededAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            throw new GitIdentityRequiredException();
        }

        string temporaryIndex = CreateUniqueTempPath("beutl-git-index");
        GitCommandOptions indexOptions = CreateTemporaryIndexOptions(temporaryIndex);

        try
        {
            await runner.RunAsync(
                repository,
                ["read-tree", baseHead.Commit],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            SnapshotIndexCommandPlan indexPlan = await CreateSnapshotIndexCommandsAsync(
                    repository,
                    runner,
                    baseHead.Commit,
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (IReadOnlyList<string> command in indexPlan.Commands)
            {
                await runner.RunAsync(
                        repository,
                        command,
                        indexOptions with
                        {
                            ExecutionKind = GitCommandExecutionKind.LocalWithLfs,
                            UseLiteralPathspecs = false,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            GitCommandResult tree = await runner.RunAsync(
                repository,
                ["write-tree"],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            string treeId = tree.Stdout.Trim();
            await EnsureSnapshotTreeContainsNoGitlinksAsync(
                    repository,
                    runner,
                    treeId,
                    cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            GitCommandResult commit = await runner.RunAsync(
                repository,
                [
                    "commit-tree",
                    treeId,
                    "-p",
                    baseHead.Commit,
                    "-m",
                    message.Trim(),
                    "-m",
                    "Beutl-Snapshot: safety",
                ],
                indexOptions,
                CancellationToken.None).ConfigureAwait(false);
            string checkpointCommit = commit.Stdout.Trim();
            string checkpointRef = GetCheckpointRefPrefix(repository)
                                   + Guid.NewGuid().ToString("N");
            var checkpoint = new ProjectCheckpoint(checkpointRef, checkpointCommit, baseHead);
            await PublishNewRefAsync(
                    repository,
                    runner,
                    checkpointRef,
                    checkpointCommit,
                    "beutl safety checkpoint",
                    peelToCommit: true,
                    "The safety checkpoint ref publication failed and its durable result could not be observed.",
                    static refName => new ProjectCheckpointChangedException(refName))
                .ConfigureAwait(false);
            return checkpoint;
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
        }
    }

    // Creates a ref that must not exist yet. A failure Git reports is judged by the ref: at objectId the update
    // landed; a missing ref rethrows the failure; any other value means someone else wrote the ref.
    private static async Task PublishNewRefAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string refName,
        string objectId,
        string reflogMessage,
        bool peelToCommit,
        string observationFailureMessage,
        Func<string, Exception> createChangedException)
    {
        try
        {
            await runner.RunAsync(
                repository,
                [
                    "update-ref",
                    "--create-reflog",
                    "-m",
                    reflogMessage,
                    refName,
                    objectId,
                    string.Empty,
                ],
                GitCommandOptions.Local,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception publicationException)
        {
            string? observedObject;
            try
            {
                observedObject = peelToCommit
                    ? await TryResolveCommitAsync(
                            repository,
                            runner,
                            refName,
                            CancellationToken.None)
                        .ConfigureAwait(false)
                    : await TryResolveObjectAsync(
                            repository,
                            runner,
                            refName,
                            CancellationToken.None)
                        .ConfigureAwait(false);
            }
            catch (Exception observationException)
            {
                throw new AggregateException(
                    observationFailureMessage,
                    publicationException,
                    observationException);
            }

            if (!string.Equals(
                    observedObject,
                    objectId,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (observedObject is null)
                {
                    throw;
                }

                throw createChangedException(refName);
            }
        }
    }

    private async Task RestoreProjectCheckpointCoreAsync(
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken,
        Action? validatePreparedTarget = null)
    {
        EnsureWorktreeMutationAllowed();
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await ValidateCheckpointAsync(repository, runner, checkpoint, cancellationToken)
            .ConfigureAwait(false);
        CheckedOutBranchTip currentHead = await GetCheckedOutBranchTipCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(currentHead, checkpoint.BaseTip))
        {
            throw new InvalidOperationException(
                "The project checkpoint can only be restored directly at its original head.");
        }

        WorktreeStateFingerprint currentState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                checkpoint.BaseTip.Commit,
                repository.Pathspec,
                cancellationToken)
            .ConfigureAwait(false);
        string checkpointTree = await ResolveTreeAsync(
                repository,
                runner,
                checkpoint.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(currentState.Tree, checkpointTree, StringComparison.OrdinalIgnoreCase)
            && await IsProjectIndexCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            validatePreparedTarget?.Invoke();
            return;
        }

        if (!await IsProjectCleanAsync(repository, runner, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The project must be clean before restoring a project checkpoint.");
        }

        string baseTree = await ResolveTreeAsync(
                repository,
                runner,
                checkpoint.BaseTip.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(currentState.Tree, baseTree, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectCheckpointStateChangedException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        CheckedOutBranchTip ownershipTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        WorktreeStateFingerprint ownershipState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                checkpoint.BaseTip.Commit,
                repository.Pathspec,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(ownershipTip, checkpoint.BaseTip)
            || ownershipState != currentState)
        {
            throw new InvalidOperationException(
                "The project changed before its checkpoint could be restored.");
        }

        TreeTransitionResult transitionResult = await ApplyTreeTransitionAsync(
            repository,
            runner,
            checkpoint.BaseTip,
            checkpoint.BaseTip,
            checkpoint.BaseTip.Commit,
            checkpoint.Commit,
            "beutl restore project checkpoint",
            new TreeTransitionIndexPlan(
                FinalCommit: checkpoint.BaseTip.Commit,
                RestoreCommit: checkpoint.BaseTip.Commit,
                Pathspec: repository.Pathspec),
            CancellationToken.None,
            validatePreparedTarget).ConfigureAwait(false);
        EnsureTreeTransitionApplied(
            transitionResult,
            "The project checkpoint could not be restored safely.");
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
    }

    private async Task<CommitResult> CommitProjectTreeCoreAsync(
        CheckedOutBranchTip expectedCurrent,
        string sourceCommit,
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        GitRevisionValidator.ValidateCommitId(sourceCommit, nameof(sourceCommit));
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        EnsureWorktreeMutationAllowed();
        ValidateAttachedBranchTip(expectedCurrent, nameof(expectedCurrent));
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureCheckedOutTipUnchangedAsync(
                repository,
                runner,
                expectedCurrent,
                "The checked-out branch changed before the project tree transition started.",
                cancellationToken)
            .ConfigureAwait(false);

        string? resolvedSource = await TryResolveCommitAsync(
                repository,
                runner,
                sourceCommit,
                cancellationToken)
            .ConfigureAwait(false);
        if (resolvedSource is null)
        {
            throw new ArgumentException(
                "The project tree source must resolve to a commit.",
                nameof(sourceCommit));
        }

        if (!await IsProjectCleanAsync(repository, runner, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The project must be clean before committing a project tree transition.");
        }

        GitIdentity? identity = await GetIdentityCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            if (kind != SnapshotKind.Manual)
            {
                await RaiseMissingIdentityNoticeIfNeededAsync(
                        repository,
                        runner,
                        cancellationToken)
                    .ConfigureAwait(false);
                return new CommitResult.SkippedNoIdentity();
            }

            throw new GitIdentityRequiredException();
        }

        WorktreeStateFingerprint expectedState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                repository.Pathspec,
                cancellationToken)
            .ConfigureAwait(false);
        string expectedTree = await ResolveTreeAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(expectedState.Tree, expectedTree, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The project index or worktree changed before the project tree transition started.");
        }

        string desiredTree = await BuildProjectTreeAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                resolvedSource,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(desiredTree, expectedTree, StringComparison.OrdinalIgnoreCase))
        {
            return new CommitResult.NoChanges();
        }

        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        GitCommandResult commit = await runner.RunAsync(
            repository,
            [
                "commit-tree",
                desiredTree,
                "-p",
                expectedCurrent.Commit,
                "-m",
                message.Trim(),
                "-m",
                $"Beutl-Snapshot: {kind.ToString().ToLowerInvariant()}",
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        var committedTip = new CheckedOutBranchTip(
            expectedCurrent.RefName,
            commit.Stdout.Trim());

        cancellationToken.ThrowIfCancellationRequested();
        CheckedOutBranchTip ownershipTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        WorktreeStateFingerprint ownershipState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                repository.Pathspec,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(ownershipTip, expectedCurrent)
            || ownershipState != expectedState
            || !await IsProjectCleanAsync(repository, runner, CancellationToken.None)
                .ConfigureAwait(false))
        {
            throw new ProjectCheckpointStateChangedException();
        }

        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        TreeTransitionResult applyResult = await ApplyTreeTransitionAsync(
            repository,
            runner,
            expectedCurrent,
            committedTip,
            expectedCurrent.Commit,
            committedTip.Commit,
            $"commit: {message.Trim()}",
            new TreeTransitionIndexPlan(Pathspec: repository.Pathspec),
            CancellationToken.None).ConfigureAwait(false);
        EnsureTreeTransitionApplied(
            applyResult,
            "The project tree transition could not be applied safely.");
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new CommitResult.Committed(new CommitRevision.Known(committedTip.Commit));
    }

    private async Task<BranchTipRollbackResult> TryRollbackBranchTipCoreAsync(
        CheckedOutBranchTip expectedCurrent,
        CheckedOutBranchTip target,
        CancellationToken cancellationToken)
    {
        EnsureWorktreeMutationAllowed();
        ValidateBranchTipForRollback(expectedCurrent, target);
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        CheckedOutBranchTip? actualHead = await TryGetCheckedOutBranchTipAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (actualHead is null
            || !string.Equals(actualHead.RefName, expectedCurrent.RefName, StringComparison.Ordinal)
            || !string.Equals(actualHead.Commit, expectedCurrent.Commit, StringComparison.OrdinalIgnoreCase))
        {
            return new BranchTipRollbackResult.RefChanged(actualHead?.Commit);
        }

        if (!await IsAncestorAsync(
                repository,
                runner,
                target.Commit,
                expectedCurrent.Commit,
                cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException(
                "The rollback target must be an ancestor of the expected current head.",
                nameof(target));
        }

        if (!await IsWholeRepositoryCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            return new BranchTipRollbackResult.UnsafeRepositoryState();
        }

        WorktreeStateFingerprint expectedWorktree = await CaptureWorktreeStateAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                ".",
                cancellationToken)
            .ConfigureAwait(false);
        string expectedTree = await ResolveTreeAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(expectedWorktree.Tree, expectedTree, StringComparison.OrdinalIgnoreCase)
            || !await IsWholeRepositoryCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            return new BranchTipRollbackResult.UnsafeRepositoryState();
        }

        cancellationToken.ThrowIfCancellationRequested();
        TreeTransitionResult rollbackResult = await ApplyTreeTransitionAsync(
            repository,
            runner,
            expectedCurrent,
            target,
            expectedCurrent.Commit,
            target.Commit,
            "beutl rollback fast-forward pull",
            indexPlan: null,
            CancellationToken.None).ConfigureAwait(false);
        if (rollbackResult.Outcome == TreeTransitionOutcome.OwnershipLost)
        {
            if (rollbackResult.Error is VersionControlConflictedException)
            {
                return new BranchTipRollbackResult.UnsafeRepositoryState();
            }

            return new BranchTipRollbackResult.RefChanged(
                rollbackResult.ActualTip?.Commit);
        }

        if (rollbackResult.Outcome != TreeTransitionOutcome.AppliedTarget)
        {
            if (rollbackResult.Outcome == TreeTransitionOutcome.RecoveryFailed
                && rollbackResult.Error is not null)
            {
                throw rollbackResult.Error;
            }

            return new BranchTipRollbackResult.UnsafeRepositoryState();
        }

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new BranchTipRollbackResult.RolledBack();
    }

    private async Task<bool> DeleteProjectCheckpointCoreAsync(
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        ValidateCheckpointRef(repository, checkpoint);
        string? currentCommit = await TryResolveCommitAsync(
                repository,
                runner,
                checkpoint.RefName,
                cancellationToken)
            .ConfigureAwait(false);
        if (currentCommit is null)
        {
            return false;
        }

        if (!string.Equals(currentCommit, checkpoint.Commit, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectCheckpointChangedException(checkpoint.RefName);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await runner.RunAsync(
            repository,
            ["update-ref", "-d", checkpoint.RefName, checkpoint.Commit],
            GitCommandOptions.Local,
            CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    private static async Task ValidateCheckpointAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ValidateCheckpointRef(repository, checkpoint);
        string? currentCommit = await TryResolveCommitAsync(
                repository,
                runner,
                checkpoint.RefName,
                cancellationToken)
            .ConfigureAwait(false);
        string? parentCommit = await TryResolveCommitAsync(
                repository,
                runner,
                $"{checkpoint.Commit}^1",
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(currentCommit, checkpoint.Commit, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                parentCommit,
                checkpoint.BaseTip.Commit,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectCheckpointChangedException(checkpoint.RefName);
        }
    }

    private static void ValidateCheckpointRef(
        RepositoryInfo repository,
        ProjectCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.RefName);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.Commit);
        ArgumentNullException.ThrowIfNull(checkpoint.BaseTip);
        string prefix = GetCheckpointRefPrefix(repository);
        if (!checkpoint.RefName.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(checkpoint.RefName[prefix.Length..], "N", out _))
        {
            throw new ArgumentException(
                "The checkpoint does not belong to this project.",
                nameof(checkpoint));
        }

        GitRevisionValidator.ValidateCommitId(checkpoint.Commit, nameof(checkpoint));
        ValidateAttachedBranchTip(checkpoint.BaseTip, nameof(checkpoint));
    }
}
