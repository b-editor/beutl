namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private sealed record WorktreeStateFingerprint(string Tree, string IndexEntries);

    private enum TreeTransitionOutcome
    {
        AppliedTarget,
        RestoredCurrent,
        OwnershipLost,
        RecoveryFailed,
    }

    private sealed record TreeTransitionResult(
        TreeTransitionOutcome Outcome,
        Exception? Error = null,
        CheckedOutBranchTip? ActualTip = null);

    private sealed record TreeTransitionIndexPlan(
        string? PrepareCommit = null,
        string? FinalCommit = null,
        string? RestoreCommit = null,
        string Pathspec = ".");

    private sealed record TreeTransitionContext(
        RepositoryInfo Repository,
        IGitCliRunner Runner,
        CheckedOutBranchTip CurrentHead,
        CheckedOutBranchTip TargetHead,
        string CurrentTreeCommit,
        string TargetTreeCommit,
        TreeTransitionIndexPlan? IndexPlan,
        RepositoryInfo RefUpdateRepository,
        GitCommandOptions CheckoutOptions)
    {
        public string Pathspec => IndexPlan?.Pathspec ?? ".";
    }

    private sealed record TreeTransitionAttempt(
        WorktreeStateFingerprint OriginalState,
        WorktreeStateFingerprint PreparedState,
        string CurrentTree,
        bool WorktreeMutationAttempted,
        bool TargetPrepared);

    private static async Task<WorktreeStateFingerprint> CaptureWorktreeStateAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string baseCommit,
        string pathspec,
        CancellationToken cancellationToken)
    {
        string temporaryIndex = CreateUniqueTempPath("beutl-git-index");
        GitCommandOptions indexOptions = CreateTemporaryIndexOptions(temporaryIndex);

        try
        {
            await runner.RunAsync(
                repository,
                ["read-tree", baseCommit],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            await runner.RunAsync(
                repository,
                ["add", "-A", "--", pathspec],
                indexOptions with { ExecutionKind = GitCommandExecutionKind.LocalWithLfs },
                cancellationToken).ConfigureAwait(false);
            GitCommandResult tree = await runner.RunAsync(
                repository,
                ["write-tree"],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            GitCommandResult indexEntries = await runner.RunAsync(
                repository,
                ["ls-files", "--stage", "-z", "--", pathspec],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return new WorktreeStateFingerprint(tree.Stdout.Trim(), indexEntries.Stdout);
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
        }
    }

    private static async Task<string> BuildProjectTreeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string baseCommit,
        string sourceCommit,
        CancellationToken cancellationToken)
    {
        string temporaryIndex = CreateUniqueTempPath("beutl-git-index");
        GitCommandOptions indexOptions = CreateTemporaryIndexOptions(temporaryIndex);

        try
        {
            await runner.RunAsync(
                repository,
                ["read-tree", baseCommit],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            await runner.RunAsync(
                repository,
                [
                    "restore",
                    $"--source={sourceCommit}",
                    "--staged",
                    "--",
                    repository.Pathspec,
                ],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            GitCommandResult tree = await runner.RunAsync(
                repository,
                ["write-tree"],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            return tree.Stdout.Trim();
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
        }
    }

    private static async Task<string> BuildMergedTreeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string mergeBase,
        string currentCommit,
        string incomingCommit,
        CancellationToken cancellationToken)
    {
        string temporaryIndex = CreateUniqueTempPath("beutl-git-index");
        GitCommandOptions indexOptions = CreateTemporaryIndexOptions(temporaryIndex);

        try
        {
            await runner.RunAsync(
                repository,
                ["read-tree", "-m", mergeBase, currentCommit, incomingCommit],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            GitCommandResult tree = await runner.RunAsync(
                repository,
                ["write-tree"],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            return tree.Stdout.Trim();
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
        }
    }

    private static async Task<string?> FindIgnoredIncomingPathAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string currentCommit,
        string incomingCommit,
        CancellationToken cancellationToken)
    {
        GitCommandResult changed = await runner.RunAsync(
            repository,
            [
                "diff",
                "--name-only",
                "--diff-filter=ACR",
                "-z",
                currentCommit,
                incomingCommit,
                "--",
                ".",
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> changedPaths = GitCliRunner.SplitNullSeparated(changed.Stdout);
        string repositoryRoot = Path.GetFullPath(repository.RepoRoot);
        string[] existingPaths = changedPaths
            .Where(path =>
            {
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, path));
                }
                catch (Exception ex) when (ex is ArgumentException
                                               or NotSupportedException
                                               or PathTooLongException)
                {
                    return false;
                }

                return VersionControlPathComparison.IsSameOrDescendant(repositoryRoot, fullPath)
                       && (File.Exists(fullPath) || Directory.Exists(fullPath));
            })
            .ToArray();
        if (existingPaths.Length == 0)
        {
            return null;
        }

        string input = string.Join('\0', existingPaths) + '\0';
        try
        {
            GitCommandResult ignored = await runner.RunAsync(
                repository,
                ["check-ignore", "--stdin", "-z"],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    StandardInput: input,
                    UseLiteralPathspecs: false),
                cancellationToken).ConfigureAwait(false);
            return GitCliRunner.SplitNullSeparated(ignored.Stdout).FirstOrDefault();
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return null;
        }
    }

    private async Task<TreeTransitionResult> ApplyTreeTransitionAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CheckedOutBranchTip currentHead,
        CheckedOutBranchTip targetHead,
        string currentTreeCommit,
        string targetTreeCommit,
        string reflogMessage,
        TreeTransitionIndexPlan? indexPlan,
        CancellationToken cancellationToken,
        Action? validatePreparedTarget = null)
    {
        if (!string.Equals(currentHead.RefName, targetHead.RefName, StringComparison.Ordinal))
        {
            throw new ArgumentException("A tree transition must remain on the same local branch.");
        }

        string headPath = await ResolveGitPathAsync(
                repository,
                runner,
                "HEAD",
                cancellationToken)
            .ConfigureAwait(false);
        string indexPath = await ResolveGitPathAsync(
                repository,
                runner,
                "index",
                cancellationToken)
            .ConfigureAwait(false);
        string refUpdateWorktreePath = CreateUniqueTempPath("beutl-git-ref-update");
        try
        {
            await AddRefUpdateWorktreeAsync(
                    repository,
                    runner,
                    refUpdateWorktreePath,
                    currentTreeCommit,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RemoveRefUpdateWorktreeBestEffortAsync(
                    repository,
                    runner,
                    refUpdateWorktreePath)
                .ConfigureAwait(false);
            return new TreeTransitionResult(
                TreeTransitionOutcome.RestoredCurrent,
                ex,
                currentHead);
        }

        var transition = new TreeTransitionContext(
            repository,
            runner,
            currentHead,
            targetHead,
            currentTreeCommit,
            targetTreeCommit,
            indexPlan,
            new RepositoryInfo(
                refUpdateWorktreePath,
                refUpdateWorktreePath),
            new GitCommandOptions(
                GitCommandExecutionKind.LocalWithLfs,
                new Dictionary<string, string?>
                {
                    ["GIT_WORK_TREE"] = repository.RepoRoot,
                    ["GIT_INDEX_FILE"] = indexPath,
                }));
        bool mutationStarted = false;
        try
        {
            using HeadOwnershipLease lease = await HeadOwnershipLease.AcquireAsync(
                    repository,
                    runner,
                    headPath,
                    currentHead.RefName,
                    ex => LogWarningBestEffort(
                        ex,
                        "Failed to release the protected Git HEAD lock."),
                    CancellationToken.None)
                .ConfigureAwait(false);
            CheckedOutBranchTip actualHead = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!EqualsBranchTip(actualHead, currentHead))
            {
                return new TreeTransitionResult(
                    TreeTransitionOutcome.OwnershipLost,
                    ActualTip: actualHead);
            }

            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);

            WorktreeStateFingerprint originalState = await CaptureWorktreeStateAsync(
                    repository,
                    runner,
                    currentTreeCommit,
                    transition.Pathspec,
                    CancellationToken.None)
                .ConfigureAwait(false);
            string currentTree = await ResolveTreeAsync(
                    repository,
                    runner,
                    currentTreeCommit,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!string.Equals(originalState.Tree, currentTree, StringComparison.OrdinalIgnoreCase))
            {
                return new TreeTransitionResult(TreeTransitionOutcome.OwnershipLost);
            }

            WorktreeStateFingerprint preparedState = originalState;
            bool worktreeMutationAttempted = false;
            bool targetPrepared = false;
            try
            {
                if (indexPlan?.PrepareCommit is { } prepareCommit)
                {
                    await EnsureNoExternalRepositoryOperationAsync(
                            repository,
                            runner,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    mutationStarted = true;
                    await ResetIndexAsync(
                            repository,
                            runner,
                            prepareCommit,
                            indexPlan.Pathspec)
                        .ConfigureAwait(false);
                    preparedState = await CaptureWorktreeStateAsync(
                            repository,
                            runner,
                            currentTreeCommit,
                            indexPlan.Pathspec,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }

                string? ignoredCollision = await FindIgnoredIncomingPathAsync(
                        repository,
                        runner,
                        currentTreeCommit,
                        targetTreeCommit,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (ignoredCollision is not null)
                {
                    throw new InvalidOperationException(
                        $"The tree transition would overwrite the ignored path '{ignoredCollision}'.");
                }

                await EnsureNoExternalRepositoryOperationAsync(
                        repository,
                        runner,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                mutationStarted = true;
                worktreeMutationAttempted = true;
                await CheckoutTransitionTreeAsync(transition, targetTreeCommit).ConfigureAwait(false);

                if (indexPlan?.FinalCommit is { } finalCommit)
                {
                    await ResetIndexWithoutExternalOperationAsync(
                            repository,
                            runner,
                            finalCommit,
                            indexPlan.Pathspec)
                        .ConfigureAwait(false);
                }

                WorktreeStateFingerprint targetState = await CaptureWorktreeStateAsync(
                        repository,
                        runner,
                        targetTreeCommit,
                        transition.Pathspec,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                string targetTree = await ResolveTreeAsync(
                        repository,
                        runner,
                        targetTreeCommit,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                string expectedIndexCommit = indexPlan?.FinalCommit ?? targetTreeCommit;
                if (!string.Equals(targetState.Tree, targetTree, StringComparison.OrdinalIgnoreCase)
                    || !await IsIndexAtCommitAsync(
                            repository,
                            runner,
                            expectedIndexCommit,
                            transition.Pathspec,
                            CancellationToken.None)
                        .ConfigureAwait(false))
                {
                    throw new ProjectCheckpointStateChangedException();
                }

                validatePreparedTarget?.Invoke();
                targetPrepared = true;

                string? branchCommit = await TryResolveCommitAsync(
                        repository,
                        runner,
                        currentHead.RefName,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!string.Equals(
                        branchCommit,
                        currentHead.Commit,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new TreeTransitionResult(
                        TreeTransitionOutcome.OwnershipLost,
                        ActualTip: await TryGetCheckedOutBranchTipAsync(
                                repository,
                                runner,
                                CancellationToken.None)
                            .ConfigureAwait(false));
                }

                await lease.VerifyStillOwnedAsync(CancellationToken.None).ConfigureAwait(false);
                await EnsureNoExternalRepositoryOperationAsync(
                        repository,
                        runner,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                await runner.RunAsync(
                    transition.RefUpdateRepository,
                    [
                        "update-ref",
                        "-m",
                        reflogMessage,
                        currentHead.RefName,
                        targetHead.Commit,
                        currentHead.Commit,
                    ],
                    GitCommandOptions.Local,
                    CancellationToken.None).ConfigureAwait(false);
                return new TreeTransitionResult(TreeTransitionOutcome.AppliedTarget);
            }
            catch (Exception transitionException)
            {
                return await RecoverFailedTreeTransitionAsync(
                        transition,
                        new TreeTransitionAttempt(
                            originalState,
                            preparedState,
                            currentTree,
                            worktreeMutationAttempted,
                            targetPrepared),
                        transitionException)
                    .ConfigureAwait(false);
            }
        }
        catch (ProjectCheckpointStateChangedException ex)
        {
            return new TreeTransitionResult(
                TreeTransitionOutcome.OwnershipLost,
                ex,
                await TryGetCheckedOutBranchTipAsync(
                        repository,
                        runner,
                        CancellationToken.None)
                    .ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return new TreeTransitionResult(
                mutationStarted
                    ? TreeTransitionOutcome.RecoveryFailed
                    : TreeTransitionOutcome.RestoredCurrent,
                ex,
                currentHead);
        }
        finally
        {
            await RemoveRefUpdateWorktreeBestEffortAsync(
                    repository,
                    runner,
                    refUpdateWorktreePath)
                .ConfigureAwait(false);
        }
    }

    // Runs inside the failed transition's catch, while the HEAD lease is still held.
    private static async Task<TreeTransitionResult> RecoverFailedTreeTransitionAsync(
        TreeTransitionContext transition,
        TreeTransitionAttempt attempt,
        Exception transitionException)
    {
        RepositoryInfo repository = transition.Repository;
        IGitCliRunner runner = transition.Runner;
        CheckedOutBranchTip currentHead = transition.CurrentHead;
        TreeTransitionIndexPlan? indexPlan = transition.IndexPlan;
        string? branchCommit = await TryResolveCommitAsync(
                repository,
                runner,
                currentHead.RefName,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (string.Equals(
                branchCommit,
                transition.TargetHead.Commit,
                StringComparison.OrdinalIgnoreCase)
            && attempt.TargetPrepared)
        {
            return new TreeTransitionResult(TreeTransitionOutcome.AppliedTarget);
        }

        if (!string.Equals(
                branchCommit,
                currentHead.Commit,
                StringComparison.OrdinalIgnoreCase))
        {
            return new TreeTransitionResult(
                TreeTransitionOutcome.OwnershipLost,
                transitionException,
                await TryGetCheckedOutBranchTipAsync(
                        repository,
                        runner,
                        CancellationToken.None)
                    .ConfigureAwait(false));
        }

        try
        {
            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (VersionControlConflictedException externalOperationException)
        {
            return new TreeTransitionResult(
                TreeTransitionOutcome.OwnershipLost,
                externalOperationException,
                currentHead);
        }
        catch (Exception recoveryGuardException)
        {
            return new TreeTransitionResult(
                TreeTransitionOutcome.RecoveryFailed,
                new AggregateException(
                    "The tree transition failed and rollback safety could not be established.",
                    transitionException,
                    recoveryGuardException),
                currentHead);
        }

        try
        {
            WorktreeStateFingerprint failedState = await CaptureWorktreeStateAsync(
                    repository,
                    runner,
                    transition.CurrentTreeCommit,
                    transition.Pathspec,
                    CancellationToken.None)
                .ConfigureAwait(false);
            string targetTree = await ResolveTreeAsync(
                    repository,
                    runner,
                    transition.TargetTreeCommit,
                    CancellationToken.None)
                .ConfigureAwait(false);
            bool worktreeOwned = string.Equals(
                                     failedState.Tree,
                                     attempt.OriginalState.Tree,
                                     StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(
                                     failedState.Tree,
                                     targetTree,
                                     StringComparison.OrdinalIgnoreCase);
            bool indexOwned = await IsFailedTransitionIndexOwnedAsync(
                    transition,
                    attempt,
                    failedState)
                .ConfigureAwait(false);
            if (!indexOwned)
            {
                return new TreeTransitionResult(
                    TreeTransitionOutcome.OwnershipLost,
                    transitionException,
                    currentHead);
            }

            if (!worktreeOwned)
            {
                return await RestoreIndexAfterRefusedCheckoutAsync(
                        transition,
                        attempt,
                        transitionException)
                    .ConfigureAwait(false);
            }

            if (attempt.WorktreeMutationAttempted
                && string.Equals(
                    failedState.Tree,
                    targetTree,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (await AlignTransitionHeadForRecoveryAsync(
                            transition,
                            transitionException)
                        .ConfigureAwait(false) is { } alignmentFailure)
                {
                    return alignmentFailure;
                }

                await ResetIndexWithoutExternalOperationAsync(
                        repository,
                        runner,
                        transition.TargetTreeCommit,
                        transition.Pathspec)
                    .ConfigureAwait(false);
                await EnsureNoExternalRepositoryOperationAsync(
                        repository,
                        runner,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                await CheckoutTransitionTreeAsync(transition, transition.CurrentTreeCommit).ConfigureAwait(false);
            }

            if (indexPlan?.RestoreCommit is { } restoreCommit)
            {
                await ResetIndexWithoutExternalOperationAsync(
                        repository,
                        runner,
                        restoreCommit,
                        indexPlan.Pathspec)
                    .ConfigureAwait(false);
            }
            else if (!string.Equals(
                         failedState.IndexEntries,
                         attempt.OriginalState.IndexEntries,
                         StringComparison.Ordinal))
            {
                await ResetIndexWithoutExternalOperationAsync(
                        repository,
                        runner,
                        transition.CurrentTreeCommit,
                        transition.Pathspec)
                    .ConfigureAwait(false);
            }

            return await VerifyRecoveredTreeTransitionAsync(
                    transition,
                    attempt,
                    transitionException)
                .ConfigureAwait(false);
        }
        catch (VersionControlConflictedException recoveryException)
        {
            return new TreeTransitionResult(
                TreeTransitionOutcome.OwnershipLost,
                recoveryException,
                currentHead);
        }
        catch (Exception recoveryException)
        {
            return new TreeTransitionResult(
                TreeTransitionOutcome.RecoveryFailed,
                new AggregateException(
                    "The tree transition failed and its current state could not be restored.",
                    transitionException,
                    recoveryException),
                currentHead);
        }
    }

    // Whether the index still holds a state this transition started from or wrote; any other index is another
    // writer's and is not reset.
    private static async Task<bool> IsFailedTransitionIndexOwnedAsync(
        TreeTransitionContext transition,
        TreeTransitionAttempt attempt,
        WorktreeStateFingerprint failedState)
    {
        RepositoryInfo repository = transition.Repository;
        IGitCliRunner runner = transition.Runner;
        TreeTransitionIndexPlan? indexPlan = transition.IndexPlan;
        return string.Equals(
                   failedState.IndexEntries,
                   attempt.OriginalState.IndexEntries,
                   StringComparison.Ordinal)
               || string.Equals(
                   failedState.IndexEntries,
                   attempt.PreparedState.IndexEntries,
                   StringComparison.Ordinal)
               || await IsIndexAtCommitAsync(
                       repository,
                       runner,
                       transition.TargetTreeCommit,
                       transition.Pathspec,
                       CancellationToken.None)
                   .ConfigureAwait(false)
               || (indexPlan?.PrepareCommit is { } expectedPrepareCommit
                   && await IsIndexAtCommitAsync(
                           repository,
                           runner,
                           expectedPrepareCommit,
                           indexPlan.Pathspec,
                           CancellationToken.None)
                       .ConfigureAwait(false))
               || (indexPlan?.FinalCommit is { } expectedFinalCommit
                   && await IsIndexAtCommitAsync(
                           repository,
                           runner,
                           expectedFinalCommit,
                           indexPlan.Pathspec,
                           CancellationToken.None)
                       .ConfigureAwait(false));
    }

    // The worktree holds neither tree, so it is left alone and only the index this transition changed is put back.
    private static async Task<TreeTransitionResult> RestoreIndexAfterRefusedCheckoutAsync(
        TreeTransitionContext transition,
        TreeTransitionAttempt attempt,
        Exception transitionException)
    {
        RepositoryInfo repository = transition.Repository;
        IGitCliRunner runner = transition.Runner;
        string refusedRestoreCommit = transition.IndexPlan?.RestoreCommit ?? transition.CurrentTreeCommit;
        await ResetIndexWithoutExternalOperationAsync(
                repository,
                runner,
                refusedRestoreCommit,
                transition.Pathspec)
            .ConfigureAwait(false);
        WorktreeStateFingerprint refusedState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                transition.CurrentTreeCommit,
                transition.Pathspec,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!string.Equals(
                refusedState.IndexEntries,
                attempt.OriginalState.IndexEntries,
                StringComparison.Ordinal))
        {
            return new TreeTransitionResult(
                TreeTransitionOutcome.RecoveryFailed,
                new AggregateException(
                    "The checkout was refused and the original index could not be restored.",
                    transitionException),
                transition.CurrentHead);
        }

        CheckedOutBranchTip? refusedTip = await TryGetCheckedOutBranchTipAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        return new TreeTransitionResult(
            TreeTransitionOutcome.OwnershipLost,
            transitionException,
            refusedTip);
    }

    // Moves the temporary worktree's HEAD to the target tree that is on disk, so checking the current tree out again
    // starts from what the failed checkout left. Null when HEAD is aligned; otherwise the result that ends recovery.
    private static async Task<TreeTransitionResult?> AlignTransitionHeadForRecoveryAsync(
        TreeTransitionContext transition,
        Exception transitionException)
    {
        string? transitionHead = await TryResolveCommitAsync(
                transition.RefUpdateRepository,
                transition.Runner,
                "HEAD",
                CancellationToken.None)
            .ConfigureAwait(false);
        if (string.Equals(
                transitionHead,
                transition.CurrentTreeCommit,
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await transition.Runner.RunAsync(
                    transition.RefUpdateRepository,
                    [
                        "update-ref",
                        "--no-deref",
                        "-m",
                        "beutl align temporary transition head for recovery",
                        "HEAD",
                        transition.TargetTreeCommit,
                        transition.CurrentTreeCommit,
                    ],
                    GitCommandOptions.Local,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception alignmentException)
            {
                transitionHead = await TryResolveCommitAsync(
                        transition.RefUpdateRepository,
                        transition.Runner,
                        "HEAD",
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (string.Equals(
                        transitionHead,
                        transition.TargetTreeCommit,
                        StringComparison.OrdinalIgnoreCase))
                {
                    // The update reached Git even though the runner lost its response.
                }
                else if (string.Equals(
                             transitionHead,
                             transition.CurrentTreeCommit,
                             StringComparison.OrdinalIgnoreCase))
                {
                    return new TreeTransitionResult(
                        TreeTransitionOutcome.RecoveryFailed,
                        new AggregateException(
                            "The temporary transition head could not be aligned for recovery.",
                            transitionException,
                            alignmentException),
                        transition.CurrentHead);
                }
                else
                {
                    return new TreeTransitionResult(
                        TreeTransitionOutcome.OwnershipLost,
                        new AggregateException(
                            "The temporary transition head changed while recovery was being prepared.",
                            transitionException,
                            alignmentException),
                        transition.CurrentHead);
                }
            }
        }
        else if (!string.Equals(
                     transitionHead,
                     transition.TargetTreeCommit,
                     StringComparison.OrdinalIgnoreCase))
        {
            return new TreeTransitionResult(
                TreeTransitionOutcome.OwnershipLost,
                transitionException,
                transition.CurrentHead);
        }

        return null;
    }

    private static async Task<TreeTransitionResult> VerifyRecoveredTreeTransitionAsync(
        TreeTransitionContext transition,
        TreeTransitionAttempt attempt,
        Exception transitionException)
    {
        RepositoryInfo repository = transition.Repository;
        IGitCliRunner runner = transition.Runner;
        WorktreeStateFingerprint recoveredState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                transition.CurrentTreeCommit,
                transition.Pathspec,
                CancellationToken.None)
            .ConfigureAwait(false);
        string expectedRestoreCommit = transition.IndexPlan?.RestoreCommit ?? transition.CurrentTreeCommit;
        if (!string.Equals(
                recoveredState.Tree,
                attempt.CurrentTree,
                StringComparison.OrdinalIgnoreCase)
            || !await IsIndexAtCommitAsync(
                    repository,
                    runner,
                    expectedRestoreCommit,
                    transition.Pathspec,
                    CancellationToken.None)
                .ConfigureAwait(false))
        {
            return new TreeTransitionResult(
                TreeTransitionOutcome.RecoveryFailed,
                new AggregateException(
                    "The tree transition failed and the original tree could not be verified.",
                    transitionException),
                transition.CurrentHead);
        }

        CheckedOutBranchTip? recoveredTip = await TryGetCheckedOutBranchTipAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (recoveredTip is null || !EqualsBranchTip(recoveredTip, transition.CurrentHead))
        {
            return new TreeTransitionResult(
                TreeTransitionOutcome.OwnershipLost,
                transitionException,
                recoveredTip);
        }

        return new TreeTransitionResult(
            TreeTransitionOutcome.RestoredCurrent,
            transitionException,
            transition.CurrentHead);
    }

    private static Task<GitCommandResult> CheckoutTransitionTreeAsync(
        TreeTransitionContext transition,
        string commit)
    {
        return transition.Runner.RunAsync(
            transition.RefUpdateRepository,
            [
                .. s_lfsPathFilterOverrides,
                "-c",
                "core.hooksPath=/dev/null",
                "checkout",
                "--detach",
                "--no-overwrite-ignore",
                commit,
            ],
            transition.CheckoutOptions,
            CancellationToken.None);
    }

    private static async Task ResetIndexWithoutExternalOperationAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string commit,
        string pathspec)
    {
        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        await ResetIndexAsync(
                repository,
                runner,
                commit,
                pathspec)
            .ConfigureAwait(false);
    }

    // A detached worktree with no checkout, from which Git commands run without involving this worktree's HEAD.
    private static Task AddRefUpdateWorktreeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string worktreePath,
        string commit,
        CancellationToken cancellationToken)
    {
        return runner.RunAsync(
            repository,
            [
                "worktree",
                "add",
                "--detach",
                "--no-checkout",
                worktreePath,
                commit,
            ],
            GitCommandOptions.Local,
            cancellationToken);
    }

    private async Task RemoveRefUpdateWorktreeBestEffortAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string worktreePath)
    {
        Exception? cleanupFailure = null;
        try
        {
            await runner.RunAsync(
                repository,
                ["worktree", "remove", "--force", worktreePath],
                GitCommandOptions.Local,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            cleanupFailure = ex;
        }

        try
        {
            if (Directory.Exists(worktreePath))
            {
                Directory.Delete(worktreePath, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            cleanupFailure = cleanupFailure is null
                ? ex
                : new AggregateException(cleanupFailure, ex);
        }

        if (cleanupFailure is not null)
        {
            LogWarningBestEffort(
                cleanupFailure,
                "Failed to remove a temporary detached Git worktree used for a ref update.");
        }
    }

    private static void EnsureTreeTransitionApplied(
        TreeTransitionResult result,
        string message)
    {
        if (result.Outcome == TreeTransitionOutcome.AppliedTarget)
        {
            return;
        }

        if (result.Error is GitOperationException operationException)
        {
            throw operationException;
        }

        throw new InvalidOperationException(
            $"{message} Outcome: {result.Outcome}.",
            result.Error);
    }
}
