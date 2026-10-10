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

    private IReadOnlyList<string> CreateSnapshotPathspecs(RepositoryInfo repository)
    {
        return [CreateSnapshotBasePathspec(repository), .. CreateSnapshotExcludePathspecs(repository)];
    }
}
