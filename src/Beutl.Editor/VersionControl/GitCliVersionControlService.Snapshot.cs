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
        await EnsureProjectFileIsVersionedAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
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

            // The project can change while the identity prompt is open, and the large-media notice
            // reads the changes that are about to be staged.
            status = await GetSnapshotStatusCoreAsync(cancellationToken).ConfigureAwait(false);
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
        // Checked again right before staging: a notice or prompt shown since can have given the user
        // time to change the ignore rules.
        await EnsureProjectFileIsVersionedAsync(repository, runner, cancellationToken)
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
        try
        {
            // Exit code 1 means the index differs from HEAD within the project.
            await runner.RunAsync(
                    repository,
                    ["diff", "--cached", "--quiet", "--", .. pathspecs],
                    pathspecOptions with { ExecutionKind = GitCommandExecutionKind.Local },
                    cancellationToken)
                .ConfigureAwait(false);
            return null;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
        }

        // The message goes through standard input, so its length is not bound by the command line.
        var arguments = new List<string>
        {
            "commit",
            "--quiet",
            "--only",
            "--file=-",
        };
        if (kind != SnapshotKind.Manual)
        {
            // Only a commit the user asks for follows commit.gpgSign; an automatic snapshot must not
            // wait on a signer.
            arguments.Add("--no-gpg-sign");
        }

        arguments.Add("--");
        arguments.AddRange(pathspecs);
        string branchRef = await GetAttachedBranchRefCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        string? parent = await TryResolveCommitAsync(repository, runner, branchRef, cancellationToken)
            .ConfigureAwait(false);
        // Hooks and a signer are the user's own programs and can wait on the user, so only
        // cancellation stops them.
        try
        {
            await runner.RunAsync(
                    repository,
                    arguments,
                    pathspecOptions with
                    {
                        ExecutionKind = GitCommandExecutionKind.LocalUnbounded,
                        EnvironmentOverrides = new Dictionary<string, string?> { ["GIT_EDITOR"] = ":" },
                        StandardInput = CreateSnapshotCommitMessage(message, kind),
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A post-commit hook runs once the commit is made, so cancelling it does not undo the
            // snapshot; a commit that already moved the branch is reported as saved.
            if (await ObserveSnapshotCommitAsync(repository, runner, branchRef, parent)
                    .ConfigureAwait(false) is CommitRevision.Known published)
            {
                return published;
            }

            throw;
        }

        return await ObserveSnapshotCommitAsync(repository, runner, branchRef, parent)
            .ConfigureAwait(false);
    }

    private static string CreateSnapshotCommitMessage(string message, SnapshotKind kind)
    {
        // Only the line breaks at the end make way for the trailer; the rest is left to Git's cleanup
        // mode, which may be verbatim.
        return $"{message.TrimEnd('\r', '\n')}\n\nBeutl-Snapshot: {kind.ToString().ToLowerInvariant()}\n";
    }

    // git commit moves the branch HEAD named when it ran. A post-commit hook or another Git process
    // can switch HEAD afterwards, which leaves that branch on the snapshot, or move the branch itself,
    // so the branch names this snapshot only while it is a child of the commit it was made on.
    // Otherwise the snapshot is still saved, but which commit it is cannot be told.
    private static async Task<CommitRevision> ObserveSnapshotCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string branchRef,
        string? parent)
    {
        try
        {
            string? head = await TryResolveCommitAsync(
                    repository,
                    runner,
                    branchRef,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (head is null)
            {
                return new CommitRevision.Unavailable();
            }

            GitCommandResult parents = await runner.RunAsync(
                    repository,
                    ["rev-list", "--parents", "-n", "1", head],
                    GitCommandOptions.Local,
                    CancellationToken.None)
                .ConfigureAwait(false);
            string[] ids = parents.Stdout.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool madeOnParent = parent is null
                ? ids.Length == 1
                : ids.Length == 2 && string.Equals(ids[1], parent, StringComparison.OrdinalIgnoreCase);
            return madeOnParent
                ? new CommitRevision.Known(head)
                : new CommitRevision.Unavailable();
        }
        catch (GitOperationException)
        {
            return new CommitRevision.Unavailable();
        }
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

        // The safety snapshot leaves ignored files out, so git restore would replace one at a path the
        // source tracks with nothing to restore it from. The pull and the branch switch refuse the same.
        string? ignoredCollision = await FindIgnoredPathRestoredFromAsync(
                repository,
                runner,
                source,
                cancellationToken)
            .ConfigureAwait(false);
        if (ignoredCollision is not null)
        {
            throw new InvalidOperationException(
                $"Restoring the project would overwrite the ignored file '{ignoredCollision}'.");
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
                    "The project could not be restored, and its files could not be put back to the checked-out version. That version still holds the project as it was before the restore.",
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

    // Paths the source adds to HEAD are untracked here; one that exists on disk and that an ignore rule
    // matches is a file the restore would overwrite.
    private async Task<string?> FindIgnoredPathRestoredFromAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string source,
        CancellationToken cancellationToken)
    {
        GitCommandResult added = await runner.RunAsync(
                repository,
                [
                    "diff",
                    "--name-only",
                    "--no-renames",
                    "--diff-filter=A",
                    "-z",
                    "HEAD",
                    source,
                    "--",
                    .. CreateSnapshotPathspecs(repository),
                ],
                GitCommandOptions.Local with { UseLiteralPathspecs = false },
                cancellationToken)
            .ConfigureAwait(false);
        string repositoryRoot = Path.GetFullPath(repository.RepoRoot);
        string[] existingPaths = GitCliRunner.SplitNullSeparated(added.Stdout)
            .Where(path =>
            {
                try
                {
                    string fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, path));
                    return File.Exists(fullPath) || Directory.Exists(fullPath);
                }
                catch (Exception ex) when (ex is ArgumentException
                                               or NotSupportedException
                                               or PathTooLongException)
                {
                    // A name this platform cannot hold cannot exist on disk either.
                    return false;
                }
            })
            .ToArray();
        if (existingPaths.Length == 0)
        {
            return null;
        }

        try
        {
            GitCommandResult ignored = await runner.RunAsync(
                    repository,
                    ["check-ignore", "--stdin", "-z"],
                    new GitCommandOptions(
                        GitCommandExecutionKind.Local,
                        StandardInput: string.Join('\0', existingPaths) + '\0',
                        UseLiteralPathspecs: false),
                    cancellationToken)
                .ConfigureAwait(false);
            return GitCliRunner.SplitNullSeparated(ignored.Stdout).FirstOrDefault();
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return null;
        }
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

    // The project file is what a version reopens, so a rule that ignores it, such as a global *.bep,
    // would leave every snapshot unable to restore the project. That one rule is refused instead of
    // only reported; a tracked project file is recorded whatever the ignore rules say.
    private async Task EnsureProjectFileIsVersionedAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        // A project file that is gone is not one the ignore rules left out.
        if (_projectFile is null
            || !RepositoryPathComparer.IsContainedWithin(repository.ProjectRoot, _projectFile)
            || !File.Exists(_projectFile))
        {
            return;
        }

        string path = GetRepositoryRelativeProjectFilePath(repository, _projectFile);
        try
        {
            // Exit code 1: not ignored. Git never reports a tracked file as ignored.
            await runner.RunAsync(
                    repository,
                    ["check-ignore", "--stdin", "-z"],
                    new GitCommandOptions(
                        GitCommandExecutionKind.Local,
                        StandardInput: path + "\0",
                        UseLiteralPathspecs: false),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The repository's ignore rules leave the project file '{path}' out of versions. "
            + "Update the ignore rules so the project file can be recorded.");
    }

    private IReadOnlyList<string> CreateSnapshotPathspecs(RepositoryInfo repository)
    {
        return [CreateSnapshotBasePathspec(repository), .. CreateSnapshotExcludePathspecs(repository)];
    }
}
