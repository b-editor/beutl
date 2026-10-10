namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private IReadOnlyList<string> CreateSnapshotExcludePathspecs(RepositoryInfo repository)
    {
        string prefix = repository.Pathspec == "."
            ? string.Empty
            : EscapeGitGlobPath(repository.Pathspec) + "/";
        // Broad staging always excludes Beutl's per-user state and `.tmp` scratch files, the same
        // paths the generated ignore rules name.
        return s_ignoredOptionalProjectPathspecSuffixes
            .Select(suffix => $":(top,exclude,glob){prefix}{suffix}")
            .ToArray();
    }

    private IReadOnlyList<string> CreateSnapshotIndexCommand(RepositoryInfo repository)
    {
        var addArguments = new List<string>
        {
            "-c",
            "advice.addIgnoredFile=false",
            "add",
            "-A",
            "--",
            CreateSnapshotBasePathspec(repository),
        };
        addArguments.AddRange(CreateSnapshotExcludePathspecs(repository));
        return addArguments;
    }

    private static string CreateSnapshotBasePathspec(RepositoryInfo repository)
    {
        return repository.Pathspec == "."
            ? "."
            : $":(top,literal){repository.Pathspec}";
    }

    private async Task<CommitResult> CommitAllCoreAsync(
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken,
        bool presentMissingIdentityNotice = true)
    {
        RepositoryInfo repository = GetRepository();
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        if (_hygieneDeferred)
        {
            await EnsureRepositoryHygieneSerializedCoreAsync(cancellationToken).ConfigureAwait(false);
        }

        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await GetAttachedBranchRefCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        WorkspaceStatus status = await GetSnapshotStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfConflicted(status);
        // Before the no-changes check: a project folder that is ignored as a whole never changes.
        await RaiseIgnoredProjectFilesNoticeIfNeededAsync(
            repository,
            runner,
            cancellationToken).ConfigureAwait(false);
        if (status.IsClean)
        {
            return new CommitResult.NoChanges();
        }

        GitIdentity? identity = await GetIdentityCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            if (kind == SnapshotKind.Manual)
            {
                throw new GitIdentityRequiredException();
            }

            if (!presentMissingIdentityNotice
                || !await TryRequestMissingIdentityAsync(repository, runner, cancellationToken)
                    .ConfigureAwait(false))
            {
                return new CommitResult.SkippedNoIdentity();
            }
        }

        await RaiseLargeMediaNoticeIfNeededAsync(
            repository,
            runner,
            status,
            cancellationToken).ConfigureAwait(false);

        CommitRevision? revision = await CommitProjectSnapshotAsync(
                repository,
                runner,
                message,
                kind,
                cancellationToken)
            .ConfigureAwait(false);
        if (revision is null)
        {
            return new CommitResult.NoChanges();
        }

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new CommitResult.Committed(revision);
    }

    // Stages the project and commits it with git commit --only, which records the project scope alone
    // and leaves anything else the user has staged in place. Git runs the hooks, signs the commit and
    // updates the branch under its own locks; returns null when staging found nothing to record.
    private async Task<CommitRevision?> CommitProjectSnapshotAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        await EnsureNoExternalRepositoryOperationAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        await EnsureNoNestedRepositoryWouldBeStagedAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<string> pathspecs = CreateSnapshotPathspecs(repository);
        var pathspecOptions = new GitCommandOptions(GitCommandExecutionKind.LocalWithLfs)
        {
            UseLiteralPathspecs = false,
        };
        await runner.RunAsync(
                repository,
                CreateSnapshotIndexCommand(repository),
                pathspecOptions,
                cancellationToken)
            .ConfigureAwait(false);
        GitCommandOptions stagedOptions = pathspecOptions with { ExecutionKind = GitCommandExecutionKind.Local };
        GitCommandResult projectStaged = await runner.RunAsync(
                repository,
                ["diff", "--cached", "--name-only", "--no-renames", "-z", "--", .. pathspecs],
                stagedOptions,
                cancellationToken)
            .ConfigureAwait(false);
        int projectStagedCount = GitCliRunner.SplitNullSeparated(projectStaged.Stdout).Count;
        if (projectStagedCount == 0)
        {
            return null;
        }

        GitCommandResult allStaged = await runner.RunAsync(
                repository,
                ["diff", "--cached", "--name-only", "--no-renames", "-z"],
                stagedOptions,
                cancellationToken)
            .ConfigureAwait(false);
        // git commit --only reads the project's changed files from the worktree again, through their
        // clean filters, which for Git LFS media means reading whole files once more. It is needed only
        // to leave something the user staged outside the project out of the snapshot.
        bool stagedOutsideProject = GitCliRunner.SplitNullSeparated(allStaged.Stdout).Count != projectStagedCount;

        var arguments = new List<string>
        {
            "commit",
            "--quiet",
            "-m",
            message.Trim(),
            "-m",
            $"Beutl-Snapshot: {kind.ToString().ToLowerInvariant()}",
        };
        if (kind != SnapshotKind.Manual)
        {
            // Only a commit the user asks for follows commit.gpgSign; an automatic snapshot must not
            // wait on a signer.
            arguments.Add("--no-gpg-sign");
        }

        if (stagedOutsideProject)
        {
            arguments.Add("--only");
            arguments.Add("--");
            arguments.AddRange(pathspecs);
        }

        // Hooks and a signer are the user's own programs and can wait on the user, so only
        // cancellation stops them.
        await runner.RunAsync(
                repository,
                arguments,
                pathspecOptions with
                {
                    ExecutionKind = GitCommandExecutionKind.LocalUnbounded,
                    EnvironmentOverrides = new Dictionary<string, string?> { ["GIT_EDITOR"] = ":" },
                },
                cancellationToken)
            .ConfigureAwait(false);
        GitCommandResult head = await runner.RunAsync(
                repository,
                ["rev-parse", "--verify", "HEAD"],
                GitCommandOptions.Local,
                CancellationToken.None)
            .ConfigureAwait(false);
        string commit = head.Stdout.Trim();
        GitRevisionValidator.ValidateCommitId(commit, nameof(commit));
        return new CommitRevision.Known(commit);
    }

    // Restores the project scope from the source commit and records it as a snapshot. git restore runs
    // without overlay, so files the source does not have are deleted; the snapshot pathspecs leave
    // .beutl state and .tmp files alone. Until the snapshot is recorded, a failure puts the scope back to
    // HEAD, so the project reopens on the version it was closed on.
    private async Task<CommitResult> RestoreProjectTreeCoreAsync(
        string sourceCommit,
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        GitRevisionValidator.ValidateCommitId(sourceCommit, nameof(sourceCommit));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await GetAttachedBranchRefCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        await EnsureNoExternalRepositoryOperationAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        EnsureWorktreeMutationAllowed();
        string source = await TryResolveCommitAsync(repository, runner, sourceCommit, cancellationToken)
                            .ConfigureAwait(false)
                        ?? throw new ArgumentException(
                            "The restore source must identify a commit in the repository.",
                            nameof(sourceCommit));

        // Checked before any file changes: without an identity the restore could not be recorded.
        if (await GetIdentityCoreAsync(repository, runner, cancellationToken).ConfigureAwait(false) is null)
        {
            if (kind == SnapshotKind.Manual)
            {
                throw new GitIdentityRequiredException();
            }

            await RaiseMissingIdentityNoticeIfNeededAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false);
            return new CommitResult.SkippedNoIdentity();
        }

        CommitRevision? revision;
        try
        {
            await RestoreProjectScopeAsync(repository, runner, source, cancellationToken)
                .ConfigureAwait(false);
            revision = await CommitProjectSnapshotAsync(
                    repository,
                    runner,
                    message,
                    kind,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception restoreFailure)
        {
            try
            {
                await RestoreProjectScopeAsync(repository, runner, "HEAD", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException(
                    "The project could not be restored, and its files could not be put back to the checked-out version.",
                    restoreFailure,
                    rollbackFailure);
            }

            throw;
        }

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return revision is null
            ? new CommitResult.NoChanges()
            : new CommitResult.Committed(revision);
    }

    private Task<GitCommandResult> RestoreProjectScopeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string source,
        CancellationToken cancellationToken)
    {
        return runner.RunAsync(
            repository,
            [
                .. s_lfsPathFilterOverrides,
                "restore",
                $"--source={source}",
                "--staged",
                "--worktree",
                "--",
                .. CreateSnapshotPathspecs(repository),
            ],
            new GitCommandOptions(GitCommandExecutionKind.LocalWithLfs) { UseLiteralPathspecs = false },
            cancellationToken);
    }

    private IReadOnlyList<string> CreateSnapshotPathspecs(RepositoryInfo repository)
    {
        return [CreateSnapshotBasePathspec(repository), .. CreateSnapshotExcludePathspecs(repository)];
    }
}
