using Beutl.Language;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private enum PullRelation
    {
        Equal,
        LocalBehind,
        LocalAhead,
        Diverged,
    }

    private sealed record PullFetchTarget(
        IReadOnlyList<string> Arguments,
        string UpstreamRef,
        RemoteOpResult? Refusal = null);

    private sealed record BranchUpstreamConfiguration(
        string RemoteName,
        string RemoteRef);

    private sealed record FetchedUpstream(
        string Commit,
        PullRelation Relation,
        RemoteOpResult? Failure = null);

    private async Task SetRemoteCoreAsync(
        GitRemoteUrl remote,
        CancellationToken cancellationToken)
    {
        // Like plain Git, configuring the remote and pushing leave the worktree and the index alone, so
        // an unresolved conflict does not block them.
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        bool isFirstRemote = (await GetRemotesCoreAsync(cancellationToken).ConfigureAwait(false)).Count == 0;
        string? credentialHelper = await remote.StoreCredentialsAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        string url = remote.Url;
        if (isFirstRemote && !remote.HasCredentials)
        {
            await runner.RunAsync(
                repository,
                ["remote", "add", "origin", url],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await UpdateLocalConfigAtomicallyAsync(
                repository,
                runner,
                async (stagingPath, updateCancellation) =>
                {
                    if (remote.HasCredentials)
                    {
                        if (remote.Username is { } username)
                        {
                            await runner.RunAsync(
                                repository,
                                ["config", "--file", stagingPath, "--replace-all", $"credential.{url}.username", username],
                                GitCommandOptions.Local,
                                updateCancellation).ConfigureAwait(false);
                        }
                        await runner.RunAsync(
                            repository,
                            ["config", "--file", stagingPath, "--replace-all", $"credential.{url}.useHttpPath", "true"],
                            GitCommandOptions.Local,
                            updateCancellation).ConfigureAwait(false);
                        if (credentialHelper is not null)
                        {
                            string helperKey = $"credential.{url}.helper";
                            await runner.RunAsync(
                                repository,
                                ["config", "--file", stagingPath, "--replace-all", helperKey, ""],
                                GitCommandOptions.Local,
                                updateCancellation).ConfigureAwait(false);
                            await runner.RunAsync(
                                repository,
                                ["config", "--file", stagingPath, "--add", helperKey, credentialHelper],
                                GitCommandOptions.Local,
                                updateCancellation).ConfigureAwait(false);
                        }
                    }

                    if (isFirstRemote)
                    {
                        await runner.RunAsync(
                            repository,
                            ["config", "--file", stagingPath, "--replace-all", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*"],
                            GitCommandOptions.Local,
                            updateCancellation).ConfigureAwait(false);
                    }

                    // Separate push URLs stay as the user configured them, as with git remote set-url. Earlier
                    // Beutl versions also wrote the fetch URL as the push URL, and that copy would keep pushes
                    // going to the old repository, so a push URL that only repeats the old fetch URL goes.
                    string[] fetchUrls = await ReadStagedConfigValuesAsync("remote.origin.url");
                    string[] pushUrls = await ReadStagedConfigValuesAsync("remote.origin.pushurl");
                    if (fetchUrls.Length == 1
                        && pushUrls.Length > 0
                        && pushUrls.All(pushUrl => string.Equals(pushUrl, fetchUrls[0], StringComparison.Ordinal)))
                    {
                        await runner.RunAsync(
                            repository,
                            ["config", "--file", stagingPath, "--unset-all", "remote.origin.pushurl"],
                            GitCommandOptions.Local,
                            updateCancellation).ConfigureAwait(false);
                    }

                    await runner.RunAsync(
                        repository,
                        ["config", "--file", stagingPath, "--replace-all", "remote.origin.url", url],
                        GitCommandOptions.Local,
                        updateCancellation).ConfigureAwait(false);

                    async Task<string[]> ReadStagedConfigValuesAsync(string key)
                    {
                        try
                        {
                            GitCommandResult values = await runner.RunAsync(
                                repository,
                                ["config", "--file", stagingPath, "--get-all", key],
                                GitCommandOptions.Local,
                                updateCancellation).ConfigureAwait(false);
                            return values.Stdout.Split(
                                '\n',
                                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        }
                        catch (GitOperationException ex) when (ex.ExitCode == 1)
                        {
                            return [];
                        }
                    }
                },
                "remote update",
                cancellationToken).ConfigureAwait(false);
        }

        await TryRaiseLfsQuotaNoticeIfNeededAsync(
            repository,
            runner).ConfigureAwait(false);

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
    }

    private async Task<RemoteOpResult> PushCoreAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CheckedOutBranchTip currentTip = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            BranchUpstreamConfiguration? upstream = await GetBranchUpstreamConfigurationAsync(
                    repository,
                    runner,
                    currentTip.RefName,
                    cancellationToken)
                .ConfigureAwait(false);
            string branchName = GetBranchShortName(currentTip.RefName);
            string remoteRef = upstream is not null
                && string.Equals(upstream.RemoteName, "origin", StringComparison.Ordinal)
                && IsValidLocalBranchRef(upstream.RemoteRef)
                ? upstream.RemoteRef
                : $"refs/heads/{branchName}";
            var arguments = new List<string>(s_lfsUploadAgentOverrides)
            {
                "push",
                "--progress",
            };
            if (upstream is null)
            {
                arguments.Add("-u");
            }

            arguments.Add("origin");
            arguments.Add($"{currentTip.RefName}:{remoteRef}");
            await runner.RunAsync(
                repository,
                arguments,
                GitCommandOptions.Network,
                cancellationToken,
                progress).ConfigureAwait(false);
            await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
            return new RemoteOpResult.Success();
        }
        catch (GitOperationException ex)
        {
            CaptureRecoverableLock(ex);
            return MapRemoteFailure(ex);
        }
    }

    private static async Task<BranchUpstreamConfiguration?> GetBranchUpstreamConfigurationAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string localBranchRef,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "for-each-ref",
                "--format=%(refname)%00%(upstream:remotename)%00%(upstream:remoteref)",
                localBranchRef,
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        foreach (string record in result.Stdout
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Split('\0');
            if (fields.Length != 3
                || !string.Equals(fields[0], localBranchRef, StringComparison.Ordinal))
            {
                continue;
            }

            string remoteName = fields[1];
            string remoteRef = fields[2];
            if (remoteName.Length == 0 && remoteRef.Length == 0)
            {
                return null;
            }

            return new BranchUpstreamConfiguration(remoteName, remoteRef);
        }

        throw new GitOperationException(
            128,
            $"The captured local branch '{localBranchRef}' no longer exists.");
    }

    private async Task<PullPreflightResult> PreflightPullCoreAsync(
        CheckedOutBranchTip expectedCurrent,
        CancellationToken cancellationToken)
    {
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        ValidateAttachedBranchTip(expectedCurrent, nameof(expectedCurrent));
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureCheckedOutTipUnchangedAsync(
                repository,
                runner,
                expectedCurrent,
                "The checked-out branch changed before the pull preflight started.",
                cancellationToken)
            .ConfigureAwait(false);

        FetchedUpstream upstream = await FetchUpstreamAsync(
                repository,
                runner,
                expectedCurrent,
                cancellationToken)
            .ConfigureAwait(false);
        if (upstream.Failure is not null)
        {
            return new PullPreflightResult(
                upstream.Failure,
                RequiresTransition: false,
                UpstreamCommit: null);
        }

        string upstreamCommit = upstream.Commit;
        PullRelation relation = upstream.Relation;

        await EnsureCheckedOutTipUnchangedAsync(
                repository,
                runner,
                expectedCurrent,
                "The checked-out branch changed while the pull preflight was running.",
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);

        if (relation == PullRelation.LocalBehind
            && repository.Pathspec != "."
            && !await IsOutsideProjectCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            // The pull refuses unrelated changes elsewhere in the repository, but only after the
            // project has been closed for it. Report them while the project is still open.
            return new PullPreflightResult(
                new RemoteOpResult.RepositoryDirty(),
                RequiresTransition: false,
                UpstreamCommit: null);
        }

        return relation switch
        {
            PullRelation.LocalBehind => new PullPreflightResult(
                new RemoteOpResult.Success(),
                RequiresTransition: true,
                upstreamCommit),
            PullRelation.Equal or PullRelation.LocalAhead => new PullPreflightResult(
                new RemoteOpResult.Success(),
                RequiresTransition: false,
                UpstreamCommit: null),
            _ => new PullPreflightResult(
                new RemoteOpResult.Diverged(),
                RequiresTransition: false,
                UpstreamCommit: null),
        };
    }

    private async Task<FastForwardPullResult> PullFastForwardCoreAsync(
        CheckedOutBranchTip expectedCurrent,
        ProjectCheckpoint? checkpoint,
        string projectFile,
        CancellationToken cancellationToken)
    {
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        ValidateAttachedBranchTip(expectedCurrent, nameof(expectedCurrent));
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureWorktreeMutationAllowed();
        await EnsureCheckedOutTipUnchangedAsync(
                repository,
                runner,
                expectedCurrent,
                "The checked-out branch changed before the fast-forward pull started.",
                cancellationToken)
            .ConfigureAwait(false);

        WorktreeStateFingerprint? checkpointState = null;
        string? checkpointTree = null;
        if (checkpoint is null)
        {
            if (!await IsWholeRepositoryCleanAsync(repository, runner, cancellationToken)
                    .ConfigureAwait(false))
            {
                return new FastForwardPullResult(
                    new RemoteOpResult.RepositoryDirty(),
                    expectedCurrent);
            }
        }
        else
        {
            await ValidateCheckpointAsync(repository, runner, checkpoint, cancellationToken)
                .ConfigureAwait(false);
            if (!EqualsBranchTip(checkpoint.BaseTip, expectedCurrent))
            {
                throw new InvalidOperationException(
                    "The project checkpoint does not belong to the expected pull tip.");
            }

            checkpointState = await CaptureWorktreeStateAsync(
                    repository,
                    runner,
                    expectedCurrent.Commit,
                    ".",
                    cancellationToken)
                .ConfigureAwait(false);
            checkpointTree = await ResolveTreeAsync(
                    repository,
                    runner,
                    checkpoint.Commit,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    checkpointState.Tree,
                    checkpointTree,
                    StringComparison.OrdinalIgnoreCase)
                || !await IsWholeIndexCleanAsync(repository, runner, cancellationToken)
                    .ConfigureAwait(false))
            {
                return new FastForwardPullResult(
                    new RemoteOpResult.RepositoryDirty(),
                    expectedCurrent);
            }
        }

        FetchedUpstream upstream = await FetchUpstreamAsync(
                repository,
                runner,
                expectedCurrent,
                cancellationToken)
            .ConfigureAwait(false);
        if (upstream.Failure is not null)
        {
            return new FastForwardPullResult(upstream.Failure, expectedCurrent);
        }

        string upstreamCommit = upstream.Commit;
        PullRelation relation = upstream.Relation;
        await EnsureCheckedOutTipUnchangedAsync(
                repository,
                runner,
                expectedCurrent,
                "The checked-out branch changed while the fast-forward pull was being prepared.",
                cancellationToken)
            .ConfigureAwait(false);

        if (relation == PullRelation.Diverged)
        {
            return new FastForwardPullResult(new RemoteOpResult.Diverged(), expectedCurrent);
        }

        if (relation == PullRelation.LocalAhead
            || relation == PullRelation.Equal && checkpoint is null)
        {
            return new FastForwardPullResult(new RemoteOpResult.Success(), expectedCurrent);
        }

        string? ignoredCollision = await FindIgnoredIncomingPathAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                upstreamCommit,
                cancellationToken)
            .ConfigureAwait(false);
        if (ignoredCollision is not null)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(
                    $"The pull would overwrite the ignored path '{ignoredCollision}'."),
                expectedCurrent);
        }

        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);

        if (checkpoint is not null)
        {
            return await PullCheckpointedProjectCoreAsync(
                    repository,
                    runner,
                    expectedCurrent,
                    upstreamCommit,
                    checkpoint,
                    checkpointState!,
                    checkpointTree!,
                    projectFile,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await PullCleanWorktreeCoreAsync(
                repository,
                runner,
                expectedCurrent,
                upstreamCommit,
                projectFile,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<FastForwardPullResult> PullCleanWorktreeCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CheckedOutBranchTip expectedCurrent,
        string upstreamCommit,
        string projectFile,
        CancellationToken cancellationToken)
    {
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
        await EnsureCheckedOutTipUnchangedAsync(
                repository,
                runner,
                expectedCurrent,
                "The checked-out branch changed while the fast-forward pull was being prepared.",
                cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(expectedWorktree.Tree, expectedTree, StringComparison.OrdinalIgnoreCase)
            || !await IsWholeRepositoryCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            return new FastForwardPullResult(
                new RemoteOpResult.RepositoryDirty(),
                expectedCurrent);
        }

        string? ignoredCollision = await FindIgnoredIncomingPathAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                upstreamCommit,
                cancellationToken)
            .ConfigureAwait(false);
        if (ignoredCollision is not null)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(
                    $"The pull would overwrite the ignored path '{ignoredCollision}'."),
                expectedCurrent);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        var pulledTip = new CheckedOutBranchTip(expectedCurrent.RefName, upstreamCommit);
        TreeTransitionResult transitionResult = await ApplyTreeTransitionAsync(
            repository,
            runner,
            expectedCurrent,
            pulledTip,
            expectedCurrent.Commit,
            upstreamCommit,
            "pull: fast-forward",
            indexPlan: null,
            CancellationToken.None,
            validatePreparedTarget: () =>
                ValidateRecoveryProjectFilePhysicalContainment(repository, projectFile))
            .ConfigureAwait(false);
        if (transitionResult.Outcome != TreeTransitionOutcome.AppliedTarget)
        {
            if (transitionResult.Error is GitOperationException operationException)
            {
                CaptureRecoverableLock(operationException);
            }

            RemoteOpResult failure = transitionResult.Outcome switch
            {
                TreeTransitionOutcome.OwnershipLost => new RemoteOpResult.Failed(
                    transitionResult.Error?.Message
                    ?? "The repository changed while the fast-forward pull was being applied."),
                TreeTransitionOutcome.RestoredCurrent when transitionResult.Error is GitOperationException gitException
                    => MapRemoteFailure(gitException),
                _ => new RemoteOpResult.Failed(
                    transitionResult.Error?.Message
                    ?? "The fast-forward pull could not be applied safely."),
            };
            return new FastForwardPullResult(
                failure,
                transitionResult.ActualTip ?? expectedCurrent,
                ToPullTransitionState(transitionResult.Outcome),
                pulledTip);
        }

        try
        {
            ValidateRecoveryProjectFilePhysicalContainment(repository, projectFile);
        }
        catch (Exception ex)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(ex.Message),
                pulledTip,
                PullTransitionState.Applied,
                pulledTip);
        }

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new FastForwardPullResult(
            new RemoteOpResult.Success(),
            pulledTip,
            PullTransitionState.Applied,
            pulledTip);
    }

    private async Task<FastForwardPullResult> PullCheckpointedProjectCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CheckedOutBranchTip expectedCurrent,
        string upstreamCommit,
        ProjectCheckpoint checkpoint,
        WorktreeStateFingerprint expectedCheckpointState,
        string checkpointTree,
        string projectFile,
        CancellationToken cancellationToken)
    {
        string mergedTree = await BuildMergedTreeAsync(
                repository,
                runner,
                checkpoint.BaseTip.Commit,
                upstreamCommit,
                checkpoint.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        GitCommandResult commit = await runner.RunAsync(
            repository,
            [
                "commit-tree",
                mergedTree,
                "-p",
                upstreamCommit,
                "-m",
                PullSafetyCommitMessage,
                "-m",
                "Beutl-Snapshot: safety",
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        var safetyTip = new CheckedOutBranchTip(expectedCurrent.RefName, commit.Stdout.Trim());

        cancellationToken.ThrowIfCancellationRequested();
        PendingPullRecovery recovery = await PersistPendingPullRecoveryCoreAsync(
                checkpoint,
                safetyTip,
                projectFile,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await ValidateCheckpointAsync(repository, runner, checkpoint, CancellationToken.None)
                .ConfigureAwait(false);
            CheckedOutBranchTip ownershipTip = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);
            WorktreeStateFingerprint ownershipState = await CaptureWorktreeStateAsync(
                    repository,
                    runner,
                    expectedCurrent.Commit,
                    ".",
                    CancellationToken.None)
                .ConfigureAwait(false);
            string? ignoredCollision = await FindIgnoredIncomingPathAsync(
                    repository,
                    runner,
                    expectedCurrent.Commit,
                    upstreamCommit,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!EqualsBranchTip(ownershipTip, expectedCurrent))
            {
                return new FastForwardPullResult(
                    new RemoteOpResult.Failed(
                        "The checked-out branch changed while the checkpointed pull was being prepared."),
                    ownershipTip,
                    PullTransitionState.OwnershipLost,
                    safetyTip,
                    recovery);
            }

            if (ownershipState != expectedCheckpointState
                || !string.Equals(
                    ownershipState.Tree,
                    checkpointTree,
                    StringComparison.OrdinalIgnoreCase)
                || !await IsWholeIndexCleanAsync(repository, runner, CancellationToken.None)
                    .ConfigureAwait(false))
            {
                return new FastForwardPullResult(
                    new RemoteOpResult.RepositoryDirty(),
                    expectedCurrent,
                    Recovery: recovery);
            }

            if (ignoredCollision is not null)
            {
                return new FastForwardPullResult(
                    new RemoteOpResult.Failed(
                        $"The pull would overwrite the ignored path '{ignoredCollision}'."),
                    expectedCurrent,
                    Recovery: recovery);
            }

            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(ex.Message),
                expectedCurrent,
                PullTransitionState.RecoveryFailed,
                safetyTip,
                recovery);
        }

        TreeTransitionResult transitionResult;
        try
        {
            transitionResult = await ApplyTreeTransitionAsync(
                repository,
                runner,
                expectedCurrent,
                safetyTip,
                checkpoint.Commit,
                safetyTip.Commit,
                "pull: fast-forward with project checkpoint",
                new TreeTransitionIndexPlan(
                    PrepareCommit: checkpoint.Commit,
                    RestoreCommit: expectedCurrent.Commit),
                CancellationToken.None,
                validatePreparedTarget: () =>
                    ValidateRecoveryProjectFilePhysicalContainment(repository, projectFile))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(ex.Message),
                expectedCurrent,
                PullTransitionState.RecoveryFailed,
                safetyTip,
                recovery);
        }

        if (transitionResult.Outcome != TreeTransitionOutcome.AppliedTarget)
        {
            if (transitionResult.Error is GitOperationException gitException)
            {
                CaptureRecoverableLock(gitException);
            }
            return new FastForwardPullResult(
                transitionResult.Outcome == TreeTransitionOutcome.OwnershipLost
                    ? new RemoteOpResult.Failed(
                        transitionResult.Error?.Message
                        ?? "The repository changed while the checkpointed pull was being applied.")
                    : transitionResult.Error is GitOperationException operationException
                        ? MapRemoteFailure(operationException)
                        : new RemoteOpResult.Failed(
                            transitionResult.Error?.Message
                            ?? "The checkpointed pull could not be applied safely."),
                transitionResult.ActualTip ?? expectedCurrent,
                ToPullTransitionState(transitionResult.Outcome),
                safetyTip,
                recovery);
        }

        try
        {
            ValidateRecoveryProjectFilePhysicalContainment(repository, projectFile);
        }
        catch (Exception ex)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(ex.Message),
                safetyTip,
                PullTransitionState.RecoveryFailed,
                safetyTip,
                recovery);
        }

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new FastForwardPullResult(
            new RemoteOpResult.Success(),
            safetyTip,
            PullTransitionState.Applied,
            safetyTip,
            recovery);
    }

    // Fetches the branch's upstream and relates it to the expected tip. A refused or failed fetch comes back as
    // Failure, which each caller reports in its own result.
    private async Task<FetchedUpstream> FetchUpstreamAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CheckedOutBranchTip expectedCurrent,
        CancellationToken cancellationToken)
    {
        bool hasOrigin = (await GetRemotesCoreAsync(cancellationToken).ConfigureAwait(false)).Count > 0;
        PullFetchTarget fetchTarget = await ResolvePullFetchTargetAsync(
                repository,
                runner,
                hasOrigin,
                expectedCurrent.RefName,
                cancellationToken)
            .ConfigureAwait(false);
        if (fetchTarget.Refusal is not null)
        {
            return new FetchedUpstream(string.Empty, default, fetchTarget.Refusal);
        }

        try
        {
            await runner.RunAsync(
                repository,
                fetchTarget.Arguments,
                GitCommandOptions.Network,
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException ex)
        {
            CaptureRecoverableLock(ex);
            return new FetchedUpstream(string.Empty, default, MapRemoteFailure(ex));
        }

        string upstreamRef = fetchTarget.UpstreamRef;
        GitCommandResult upstreamResult;
        try
        {
            upstreamResult = await runner.RunAsync(
                repository,
                ["rev-parse", "--verify", $"{upstreamRef}^{{commit}}"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException ex)
        {
            CaptureRecoverableLock(ex);
            return new FetchedUpstream(string.Empty, default, MapRemoteFailure(ex));
        }

        string upstreamCommit = upstreamResult.Stdout.Trim();
        PullRelation relation = await GetPullRelationAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                upstreamCommit,
                cancellationToken)
            .ConfigureAwait(false);
        return new FetchedUpstream(upstreamCommit, relation);
    }

    private static PullTransitionState ToPullTransitionState(TreeTransitionOutcome outcome)
    {
        return outcome switch
        {
            TreeTransitionOutcome.OwnershipLost => PullTransitionState.OwnershipLost,
            TreeTransitionOutcome.RecoveryFailed => PullTransitionState.RecoveryFailed,
            _ => PullTransitionState.Unchanged,
        };
    }

    private static async Task<PullFetchTarget> ResolvePullFetchTargetAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        bool hasOrigin,
        string localBranchRef,
        CancellationToken cancellationToken)
    {
        string? configuredUpstream = await TryGetUpstreamRefAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        string branchName = GetBranchShortName(localBranchRef);
        string upstreamRef = $"{OriginRefPrefix}{branchName}";
        if (configuredUpstream is not null
            && !configuredUpstream.StartsWith(OriginRefPrefix, StringComparison.Ordinal))
        {
            // Beutl pulls only from origin, whether or not the repository has one. Fast-forwarding to
            // origin's branch of the same name would follow history this branch does not track.
            return new PullFetchTarget(
                [],
                upstreamRef,
                new RemoteOpResult.Failed(Strings.VersionControl_PullUpstreamOnAnotherRemote));
        }

        if (!hasOrigin)
        {
            return new PullFetchTarget(["fetch"], "@{upstream}");
        }

        if (configuredUpstream is not null
            && configuredUpstream.Length > OriginRefPrefix.Length)
        {
            upstreamRef = configuredUpstream;
            branchName = configuredUpstream[OriginRefPrefix.Length..];
        }

        return new PullFetchTarget(
            [
                "fetch",
                "origin",
                $"+refs/heads/{branchName}:{upstreamRef}",
            ],
            upstreamRef);
    }
}
