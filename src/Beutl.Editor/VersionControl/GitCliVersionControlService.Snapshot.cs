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

    private static async Task EnsureSnapshotTreeContainsNoGitlinksAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string tree,
        CancellationToken cancellationToken)
    {
        GitCommandResult entries = await runner.RunAsync(
                repository,
                ["ls-tree", "-r", "-z", tree, "--", repository.Pathspec],
                GitCommandOptions.Local with
                {
                    MaxStdoutBytes = MaxSnapshotTreeInspectionBytes,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (entries.StdoutTruncated)
        {
            // Each entry of the full listing carries an object name and a path, so a large project,
            // such as one holding a rendered frame sequence, overflows it long before a nested
            // repository matters. Modes alone stay small enough to scan the whole tree.
            GitCommandResult modes = await runner.RunAsync(
                    repository,
                    ["ls-tree", "-r", "-z", "--format=%(objectmode)", tree, "--", repository.Pathspec],
                    GitCommandOptions.Local with
                    {
                        MaxStdoutBytes = MaxSnapshotTreeInspectionBytes,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (modes.StdoutTruncated)
            {
                throw new InvalidOperationException(
                    "Git could not safely inspect the complete project tree for nested repositories.");
            }

            if (GitCliRunner.SplitNullSeparated(modes.Stdout).Contains("160000", StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "The project tree contains a nested Git repository that cannot be snapshotted safely.");
            }

            return;
        }

        string? gitlink = GitCliRunner.SplitNullSeparated(entries.Stdout)
            .FirstOrDefault(static entry => entry.StartsWith("160000 ", StringComparison.Ordinal));
        if (gitlink is null)
        {
            return;
        }

        int pathSeparator = gitlink.IndexOf('\t');
        string path = pathSeparator >= 0 ? gitlink[(pathSeparator + 1)..] : gitlink;
        throw new InvalidOperationException(
            $"The nested Git repository '{path}' cannot be snapshotted safely.");
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
        return await ObserveSnapshotCommitAsync(repository, runner, branchRef, parent)
            .ConfigureAwait(false);
    }

    private static string CreateSnapshotCommitMessage(string message, SnapshotKind kind)
    {
        return $"{message.Trim()}\n\nBeutl-Snapshot: {kind.ToString().ToLowerInvariant()}\n";
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
