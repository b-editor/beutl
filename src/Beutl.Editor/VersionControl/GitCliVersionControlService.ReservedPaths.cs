using Microsoft.Extensions.Logging;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private static async Task<string> CreateReservedPathCleanupCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string tree,
        string parentCommit,
        CancellationToken cancellationToken)
    {
        return await CommitTreeAndVerifyAsync(
                repository,
                runner,
                [
                    "commit-tree",
                    tree,
                    "-p",
                    parentCommit,
                    "-m",
                    "beutl: stop tracking reserved project state",
                    "-m",
                    "Beutl-Snapshot: init",
                ],
                GitCommandOptions.Local,
                "Git did not return the reserved-path cleanup commit.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsReservedProjectPath(RepositoryInfo repository, string repositoryRelativePath)
    {
        if (IsTemporaryProjectFile(repositoryRelativePath))
        {
            return true;
        }

        // Only the part inside the project decides. A .beutl folder above the project in an enclosing
        // repository is not Beutl state.
        string projectRelativePath = repository.Pathspec != "."
                                     && repositoryRelativePath.StartsWith(
                                         repository.Pathspec + "/",
                                         StringComparison.Ordinal)
            ? repositoryRelativePath[(repository.Pathspec.Length + 1)..]
            : repositoryRelativePath;
        foreach (string segment in projectRelativePath.Split('/'))
        {
            if (string.Equals(segment, ".beutl", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<IReadOnlyList<string>> GetTrackedReservedPathsCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult listed = await runner.RunAsync(
            repository,
            ["ls-files", "-z", "--", CreateSnapshotBasePathspec(repository)],
            new GitCommandOptions(GitCommandExecutionKind.Local) { UseLiteralPathspecs = false },
            cancellationToken).ConfigureAwait(false);
        return listed.Stdout
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => IsReservedProjectPath(repository, path))
            .Where(path => !IsRequiredTemporaryRepositoryPath(repository, path))
            .ToArray();
    }

    private bool IsRequiredTemporaryRepositoryPath(
        RepositoryInfo repository,
        string repositoryRelativePath)
    {
        string repositoryPath = Path.Combine(
            repository.RepoRoot,
            repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!RepositoryPathComparer.IsContainedWithin(repository.ProjectRoot, repositoryPath))
        {
            return false;
        }

        foreach (string requiredPath in _requiredTemporaryProjectPaths)
        {
            string requiredProjectPath = Path.Combine(
                repository.ProjectRoot,
                requiredPath.Replace('/', Path.DirectorySeparatorChar));
            if (VersionControlPathComparison.AreSameCanonicalPath(repositoryPath, requiredProjectPath))
            {
                return true;
            }
        }

        return false;
    }

    // .gitignore never untracks what is already tracked, and snapshot status excludes these paths -
    // so a project that is clean to Beutl still leaves the repository dirty for the pull
    // precondition, with no way out from inside the app. Drop them from the index (the files stay on
    // disk) and record that in its own commit: the initialization commit is pathspec-limited with
    // the very excludes that hide these paths, so it would leave the deletion staged forever.
    private async Task TryUntrackReservedPathsCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        IReadOnlyList<string> reservedPaths,
        CancellationToken cancellationToken)
    {
        string? temporaryIndex = null;
        string? refUpdateWorktreePath = null;
        bool cleanupRefPublished = false;
        try
        {
            string branchRef = await GetAttachedBranchRefCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            CheckedOutBranchTip expectedHead = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(branchRef, expectedHead.RefName, StringComparison.Ordinal))
            {
                throw new ProjectCheckpointStateChangedException();
            }

            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            refUpdateWorktreePath = CreateUniqueTempPath("beutl-git-ref-update");
            await AddRefUpdateWorktreeAsync(
                    repository,
                    runner,
                    refUpdateWorktreePath,
                    expectedHead.Commit,
                    cancellationToken)
                .ConfigureAwait(false);
            var refUpdateRepository = new RepositoryInfo(
                refUpdateWorktreePath,
                refUpdateWorktreePath);

            string headPath = await ResolveGitPathAsync(
                    repository,
                    runner,
                    "HEAD",
                    cancellationToken)
                .ConfigureAwait(false);
            using HeadOwnershipLease headLease = await HeadOwnershipLease.AcquireAsync(
                    repository,
                    runner,
                    headPath,
                    expectedHead.RefName,
                    ex => LogWarningBestEffort(
                        ex,
                        "Failed to release the protected Git HEAD lock while untracking reserved project paths."),
                    cancellationToken)
                .ConfigureAwait(false);

            temporaryIndex = CreateUniqueTempPath("beutl-git-index");
            GitCommandOptions indexOptions = CreateTemporaryIndexOptions(temporaryIndex);
            var removeArguments = new List<string>
            {
                "update-index",
                "--force-remove",
                "--",
            };
            removeArguments.AddRange(reservedPaths);
            string desiredTree = await BuildReservedPathCleanupTreeAsync(
                    repository,
                    runner,
                    expectedHead.Commit,
                    removeArguments,
                    indexOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            string currentTree = await ResolveTreeAsync(
                    repository,
                    runner,
                    expectedHead.Commit,
                    cancellationToken)
                .ConfigureAwait(false);

            // If the reserved paths are only staged additions, there is no tree change to publish.
            // Do not mutate the live index: a detached ref movement can race this no-op and the
            // staged-only additions belong to the caller, not to reserved-path hygiene.
            if (string.Equals(desiredTree, currentTree, StringComparison.OrdinalIgnoreCase))
            {
                await ReconcileDurableCleanupWithoutTreeChangeAsync(
                        repository,
                        runner,
                        refUpdateRepository,
                        expectedHead,
                        reservedPaths,
                        removeArguments,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            string cleanupCommit = await CreateReservedPathCleanupCommitAsync(
                    repository,
                    runner,
                    desiredTree,
                    expectedHead.Commit,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await VerifyUntrackHeadOwnershipAsync(
                    repository,
                    runner,
                    expectedHead)
                .ConfigureAwait(false);
            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);

            await PublishReservedPathCleanupAsync(
                    refUpdateRepository,
                    runner,
                    expectedHead,
                    cleanupCommit)
                .ConfigureAwait(false);
            cleanupRefPublished = true;

            await ReconcilePublishedReservedPathCleanupAsync(
                    repository,
                    runner,
                    refUpdateRepository,
                    expectedHead,
                    cleanupCommit,
                    removeArguments)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // All tree construction happens in the temporary index. Cancellation before the ref
            // publication therefore leaves both the live index and the branch untouched.
            throw;
        }
        catch (GitOperationException ex) when (ex.IsRepositoryLockFailure)
        {
            // Preserve lock failures for the serialized-operation boundary, which records the
            // recoverable lock instead of silently leaving a stale HEAD.lock/index.lock behind.
            throw;
        }
        catch (Exception ex)
        {
            LogWarningBestEffort(
                ex,
                cleanupRefPublished
                    ? "Reserved-path cleanup was committed, but the live index could not be reconciled safely."
                    : "Could not stop tracking reserved project paths; pulls will report the repository dirty until they are untracked manually.");
        }
        finally
        {
            if (refUpdateWorktreePath is not null)
            {
                await RemoveRefUpdateWorktreeBestEffortAsync(
                        repository,
                        runner,
                        refUpdateWorktreePath)
                    .ConfigureAwait(false);
            }

            if (temporaryIndex is not null)
            {
                TryDeleteTemporaryIndex(temporaryIndex);
            }
        }
    }

    // Builds the cleanup tree in the temporary index behind indexOptions: the base commit without the reserved paths.
    private static async Task<string> BuildReservedPathCleanupTreeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string baseCommit,
        IReadOnlyList<string> removeArguments,
        GitCommandOptions indexOptions,
        CancellationToken cancellationToken)
    {
        await runner.RunAsync(
                repository,
                ["read-tree", baseCommit],
                indexOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await runner.RunAsync(
                repository,
                removeArguments,
                indexOptions,
                cancellationToken)
            .ConfigureAwait(false);

        GitCommandResult desiredTreeResult = await runner.RunAsync(
                repository,
                ["write-tree"],
                indexOptions,
                cancellationToken)
            .ConfigureAwait(false);
        return desiredTreeResult.Stdout.Trim();
    }

    private async Task ReconcileDurableCleanupWithoutTreeChangeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        RepositoryInfo refUpdateRepository,
        CheckedOutBranchTip expectedHead,
        IReadOnlyList<string> reservedPaths,
        IReadOnlyList<string> removeArguments,
        CancellationToken cancellationToken)
    {
        if (await IsReservedPathCleanupCommitAsync(
                    repository,
                    runner,
                    expectedHead.Commit,
                    reservedPaths,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            _logger.LogInformation(
                "The reserved-path cleanup commit is already durable; reconciling only the live index when its ownership can be proven.");
            await ReconcileReservedPathsInLiveIndexAsync(
                    repository,
                    runner,
                    refUpdateRepository,
                    expectedHead.RefName,
                    expectedHead.Commit,
                    removeArguments,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation(
                "Reserved project paths are staged additions with no cleanup commit; leaving the live index untouched.");
        }
    }

    // Returns once the cleanup commit is the branch tip. A reported failure is judged by the tip: at the cleanup
    // commit the update landed; still at the expected commit after a lock failure, that failure is rethrown.
    private static async Task PublishReservedPathCleanupAsync(
        RepositoryInfo refUpdateRepository,
        IGitCliRunner runner,
        CheckedOutBranchTip expectedHead,
        string cleanupCommit)
    {
        try
        {
            await runner.RunAsync(
                    refUpdateRepository,
                    [
                        "update-ref",
                        "-m",
                        "beutl: stop tracking reserved project state",
                        expectedHead.RefName,
                        cleanupCommit,
                        expectedHead.Commit,
                    ],
                    GitCommandOptions.Local,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception publicationException)
        {
            string? observedTip;
            try
            {
                observedTip = await TryResolveCommitWithRetryAsync(
                        refUpdateRepository,
                        runner,
                        expectedHead.RefName)
                    .ConfigureAwait(false);
            }
            catch (Exception observationException)
            {
                if (observationException is GitOperationException
                    {
                        IsRepositoryLockFailure: true,
                    } observationLockException)
                {
                    throw observationLockException;
                }

                throw new AggregateException(
                    "The reserved-path cleanup ref update failed and its result could not be observed after a retry.",
                    publicationException,
                    observationException);
            }

            if (!string.Equals(
                    observedTip,
                    cleanupCommit,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (publicationException is GitOperationException
                    {
                        IsRepositoryLockFailure: true,
                    } lockException
                    && string.Equals(
                        observedTip,
                        expectedHead.Commit,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw lockException;
                }

                throw new AggregateException(
                    "The reserved-path cleanup ref update was not published because the branch tip changed.",
                    publicationException,
                    new InvalidOperationException(
                        $"Expected branch '{expectedHead.RefName}' at '{expectedHead.Commit}', but observed '{observedTip ?? "<unborn>"}'."));
            }
        }
    }

    private async Task ReconcilePublishedReservedPathCleanupAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        RepositoryInfo refUpdateRepository,
        CheckedOutBranchTip expectedHead,
        string cleanupCommit,
        IReadOnlyList<string> removeArguments)
    {
        string? reconciledTip = await TryResolveCommitWithRetryAsync(
                refUpdateRepository,
                runner,
                expectedHead.RefName)
            .ConfigureAwait(false);
        if (!string.Equals(
                reconciledTip,
                cleanupCommit,
                StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Reserved-path cleanup was published, but branch {Branch} moved to {ObservedTip} before the live index could be reconciled; leaving the index untouched.",
                expectedHead.RefName,
                reconciledTip ?? "<unborn>");
            return;
        }

        await ReconcileReservedPathsInLiveIndexAsync(
                repository,
                runner,
                refUpdateRepository,
                expectedHead.RefName,
                cleanupCommit,
                removeArguments,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task VerifyUntrackHeadOwnershipAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CheckedOutBranchTip expectedHead)
    {
        CheckedOutBranchTip actualHead = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(actualHead, expectedHead))
        {
            throw new ProjectCheckpointStateChangedException();
        }
    }

    private static async Task<bool> IsReservedPathCleanupCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string commit,
        IReadOnlyList<string> reservedPaths,
        CancellationToken cancellationToken)
    {
        var historyArguments = new List<string>
        {
            "log",
            "--first-parent",
            "--format=%H",
            "-z",
            "--max-count=128",
            commit,
            "--",
        };
        historyArguments.AddRange(reservedPaths);
        GitCommandResult history = await runner.RunAsync(
            repository,
            historyArguments,
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        foreach (string candidate in history.Stdout.Split(
                     '\0',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (await IsExactReservedPathCleanupCommitAsync(
                        repository,
                        runner,
                        candidate,
                        reservedPaths,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> IsExactReservedPathCleanupCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string commit,
        IReadOnlyList<string> reservedPaths,
        CancellationToken cancellationToken)
    {
        GitCommandResult message = await runner.RunAsync(
            repository,
            ["show", "-s", "--format=%s%n%b", commit],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        if (!message.Stdout.StartsWith(
                "beutl: stop tracking reserved project state\n",
                StringComparison.Ordinal)
            || !message.Stdout.Contains(
                "Beutl-Snapshot: init",
                StringComparison.Ordinal))
        {
            return false;
        }

        GitCommandResult parent = await runner.RunAsync(
            repository,
            ["rev-parse", "--verify", $"{commit}^{{commit}}^"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        var diffArguments = new List<string>
        {
            "diff",
            "--name-only",
            "-z",
            parent.Stdout.Trim(),
            commit,
            "--",
        };
        diffArguments.AddRange(reservedPaths);
        GitCommandResult changed = await runner.RunAsync(
            repository,
            diffArguments,
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        string[] changedPaths = changed.Stdout.Split(
            '\0',
            StringSplitOptions.RemoveEmptyEntries);
        return changedPaths.Length > 0
               && changedPaths.All(path => reservedPaths.Contains(path, StringComparer.Ordinal));
    }

    private async Task ReconcileReservedPathsInLiveIndexAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        RepositoryInfo refUpdateRepository,
        string branchRef,
        string expectedTip,
        IReadOnlyList<string> removeArguments,
        CancellationToken cancellationToken)
    {
        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        string liveIndexPath = await ResolveGitPathAsync(
                repository,
                runner,
                "index",
                cancellationToken)
            .ConfigureAwait(false);
        IndexFileSnapshot liveIndexBefore = await CaptureIndexFileSnapshotAsync(
                liveIndexPath,
                cancellationToken)
            .ConfigureAwait(false);
        IndexFileSnapshot liveIndexAfter = await TransformIndexSnapshotAsync(
                repository,
                runner,
                liveIndexPath,
                liveIndexBefore,
                removeArguments,
                GitCommandOptions.Local,
                "The live Git index changed while reserved paths were being reconciled; it was left untouched.",
                cancellationToken)
            .ConfigureAwait(false);

        bool externalOperationStarted = false;
        try
        {
            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (VersionControlConflictedException)
        {
            externalOperationStarted = true;
        }

        string? finalTip;
        try
        {
            finalTip = await TryResolveCommitWithRetryAsync(
                    refUpdateRepository,
                    runner,
                    branchRef)
                .ConfigureAwait(false);
        }
        catch (Exception observationException)
        {
            try
            {
                await ApplyIndexSnapshotAsync(
                        liveIndexPath,
                        expectedCurrent: liveIndexAfter,
                        replacement: liveIndexBefore,
                        mismatchMessage:
                            "The branch tip became unobservable after reserved-path reconciliation and the live index changed concurrently; the external index state was preserved.")
                    .ConfigureAwait(false);
            }
            catch (IndexRollbackAmbiguousException rollbackException)
            {
                LogWarningBestEffort(
                    rollbackException,
                    "The branch tip became unobservable after reserved-path reconciliation; a concurrent index change was preserved instead of restoring the prior index.");
            }

            LogWarningBestEffort(
                observationException,
                "The branch tip could not be observed after reserved-path index reconciliation; the prior live index was restored when ownership could be proven.");
            if (observationException is GitOperationException
                {
                    IsRepositoryLockFailure: true,
                } observationLockException)
            {
                throw observationLockException;
            }

            return;
        }

        if (!externalOperationStarted
            && string.Equals(finalTip, expectedTip, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await ApplyIndexSnapshotAsync(
                    liveIndexPath,
                    expectedCurrent: liveIndexAfter,
                    replacement: liveIndexBefore,
                    mismatchMessage:
                        "The branch moved after reserved-path reconciliation and the live index changed concurrently; the external index state was preserved.")
                .ConfigureAwait(false);
        }
        catch (IndexRollbackAmbiguousException rollbackException)
        {
            LogWarningBestEffort(
                rollbackException,
                "The branch moved after reserved-path reconciliation; a concurrent index change was preserved instead of restoring the prior index.");
        }
    }
}
