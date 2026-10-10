using Beutl.Language;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private const string PullStashMessage = "beutl: local changes before pull";

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

    // Fast-forwards to the commit the preflight fetched. The project's local changes would stop the
    // merge whenever the pull touches the same files, so they wait in a stash entry and come back
    // afterwards. When they cannot come back, Git keeps that entry and leaves the conflicts in the
    // worktree, and the user resolves them as with any stash.
    private async Task<RemoteOpResult> PullFastForwardCoreAsync(
        string upstreamCommit,
        CancellationToken cancellationToken)
    {
        GitRevisionValidator.ValidateCommitId(upstreamCommit, nameof(upstreamCommit));
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await GetAttachedBranchRefCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        await EnsureNoExternalRepositoryOperationAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        EnsureWorktreeMutationAllowed();

        string? stash = await StashProjectChangesAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            // Like the branch switch, the merge refuses to overwrite an ignored file the pull adds.
            await runner.RunAsync(
                    repository,
                    [.. s_lfsPathFilterOverrides, "merge", "--ff-only", "--no-overwrite-ignore", upstreamCommit],
                    new GitCommandOptions(GitCommandExecutionKind.LocalWithLfs),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitOperationException mergeFailure)
        {
            CaptureRecoverableLock(mergeFailure);
            string? failedPop = stash is not null
                ? await TryPopStashAsync(repository, runner, stash).ConfigureAwait(false)
                : null;
            await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
            return failedPop is null
                ? MapRemoteFailure(mergeFailure)
                : new RemoteOpResult.Failed(JoinMessages(
                    mergeFailure.Stderr,
                    string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        Strings.VersionControl_PullFailedChangesKeptInStashFormat,
                        PullStashMessage),
                    failedPop));
        }
        catch when (stash is not null)
        {
            // A failure that is not Git's own still puts the local changes back.
            await TryPopStashAsync(repository, runner, stash).ConfigureAwait(false);
            throw;
        }

        string? failedRestore = stash is not null
            ? await TryPopStashAsync(repository, runner, stash).ConfigureAwait(false)
            : null;
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return failedRestore is null
            ? new RemoteOpResult.Success()
            : new RemoteOpResult.Failed(JoinMessages(
                string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Strings.VersionControl_PullChangesKeptInStashFormat,
                    PullStashMessage),
                failedRestore));
    }

    // Returns the stash entry Git made, or null: git stash push succeeds without one when the project
    // has nothing to stash. The snapshot pathspecs keep .beutl state and .tmp files where they are.
    private async Task<string?> StashProjectChangesAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string? previous = await TryResolveCommitAsync(repository, runner, "refs/stash", cancellationToken)
            .ConfigureAwait(false);
        await runner.RunAsync(
                repository,
                [
                    .. s_lfsPathFilterOverrides,
                    "stash",
                    "push",
                    "--include-untracked",
                    "-m",
                    PullStashMessage,
                    "--",
                    .. CreateSnapshotPathspecs(repository),
                ],
                new GitCommandOptions(GitCommandExecutionKind.LocalWithLfs) { UseLiteralPathspecs = false },
                cancellationToken)
            .ConfigureAwait(false);
        string? current = await TryResolveCommitAsync(repository, runner, "refs/stash", CancellationToken.None)
            .ConfigureAwait(false);
        return string.Equals(previous, current, StringComparison.OrdinalIgnoreCase) ? null : current;
    }

    // Returns null once the stash entry is applied and dropped. Otherwise git stash pop keeps the entry
    // and this returns what Git reported, which is empty for a plain conflict because Git names the
    // conflicted files on its standard output. The entry is found by its commit, because a hook run by
    // the merge may push another one on top of it.
    private async Task<string?> TryPopStashAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string stashCommit)
    {
        try
        {
            GitCommandResult entries = await runner.RunAsync(
                    repository,
                    ["stash", "list", "--format=%H"],
                    GitCommandOptions.Local,
                    CancellationToken.None)
                .ConfigureAwait(false);
            int position = entries.Stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList()
                .FindIndex(entry => string.Equals(entry, stashCommit, StringComparison.OrdinalIgnoreCase));
            if (position < 0)
            {
                return $"The stash entry {stashCommit} is no longer in the stash list.";
            }

            string entry = $"stash@{{{position}}}";
            try
            {
                // --index brings back what was staged as well. Git refuses before touching anything
                // when the staged changes no longer apply, and the changes then come back unstaged.
                await runner.RunAsync(
                        repository,
                        [.. s_lfsPathFilterOverrides, "stash", "pop", "--index", entry],
                        new GitCommandOptions(GitCommandExecutionKind.LocalWithLfs),
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (GitOperationException ex)
                when (ex.Stderr.Contains("Try without --index", StringComparison.OrdinalIgnoreCase))
            {
                await runner.RunAsync(
                        repository,
                        [.. s_lfsPathFilterOverrides, "stash", "pop", entry],
                        new GitCommandOptions(GitCommandExecutionKind.LocalWithLfs),
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            return null;
        }
        catch (GitOperationException ex)
        {
            LogWarningBestEffort(
                ex,
                "The project's local changes could not be reapplied after the pull and stay in the stash.");
            return ex.Stderr;
        }
    }

    private static string JoinMessages(params string[] messages)
    {
        return string.Join(
            " ",
            messages
                .Where(static message => !string.IsNullOrWhiteSpace(message))
                .Select(static message => message.Trim()));
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
