namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private sealed record SnapshotTreeCapture(
        string Tree,
        string IndexPath,
        IndexFileSnapshot Index,
        IReadOnlyList<string> TemporaryPathspecsToReconcile);

    private sealed record SnapshotIndexCommandPlan(
        IReadOnlyList<IReadOnlyList<string>> Commands,
        IReadOnlyList<string> TemporaryPathspecsToReconcile);

    private sealed record SnapshotTreeBuildResult(
        string Tree,
        IReadOnlyList<string> TemporaryPathspecsToReconcile);

    private IReadOnlyList<string> CreateSnapshotExcludePathspecs(RepositoryInfo repository)
    {
        string prefix = repository.Pathspec == "."
            ? string.Empty
            : EscapeGitGlobPath(repository.Pathspec) + "/";
        // Broad staging always excludes `.tmp` scratch files. Serialized `.tmp` sidecars are
        // added by exact literal path through CreateSnapshotIndexCommands so one required sidecar
        // never widens the snapshot to every temporary file in the project.
        return s_ignoredOptionalProjectPathspecSuffixes
            .Select(suffix => $":(top,exclude,glob){prefix}{suffix}")
            .ToArray();
    }

    private async Task<SnapshotIndexCommandPlan> CreateSnapshotIndexCommandsAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string? baseCommit,
        CancellationToken cancellationToken)
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

        var commands = new List<IReadOnlyList<string>> { addArguments };
        IReadOnlyList<string> requiredTemporaryPathspecs =
            GetRequiredTemporaryRepositoryPathspecs(repository);
        IReadOnlySet<string> previousRequiredTemporaryPaths = baseCommit is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : await GetRequiredTemporaryProjectPathsAtCommitAsync(
                    repository,
                    runner,
                    baseCommit,
                    cancellationToken)
                .ConfigureAwait(false);
        string[] noLongerRequiredPathspecs = previousRequiredTemporaryPaths
            .Where(previousPath => !_requiredTemporaryProjectPaths.Any(
                currentPath => AreSameProjectRelativePath(
                    repository.ProjectRoot,
                    previousPath,
                    currentPath)))
            .Select(path => CreateRequiredTemporaryPathspec(repository, path))
            .ToArray();
        string[] pathspecsToRemove = requiredTemporaryPathspecs
            .Concat(noLongerRequiredPathspecs)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (pathspecsToRemove.Length > 0)
        {
            // Removing current paths refreshes their blob or records a physical deletion. Removing
            // paths required by the base graph but not the current graph prevents a dereferenced
            // sidecar from leaking into the next tree. Unrelated tracked scratch files stay in the
            // base tree and are not widened into the snapshot.
            commands.Add(
            [
                "--literal-pathspecs",
                "update-index",
                "--force-remove",
                "--",
                .. pathspecsToRemove.Select(GetRepositoryPathFromLiteralPathspec),
            ]);
        }

        string[] existingPathspecs = requiredTemporaryPathspecs
            .Where(pathspec => File.Exists(GetProjectPathFromLiteralPathspec(repository, pathspec)))
            .ToArray();
        if (existingPathspecs.Length > 0)
        {
            commands.Add(
            [
                "-c",
                "advice.addIgnoredFile=false",
                "add",
                "-A",
                "-f",
                "--",
                .. existingPathspecs,
            ]);
        }

        return new SnapshotIndexCommandPlan(commands, pathspecsToRemove);
    }

    private IReadOnlyList<IReadOnlyList<string>> CreateSnapshotIndexReconciliationCommands(
        RepositoryInfo repository,
        string commit,
        IReadOnlyList<string> temporaryPathspecsToReconcile)
    {
        var resetProject = new List<string>
        {
            "reset",
            "-q",
            commit,
            "--",
            CreateSnapshotBasePathspec(repository),
        };
        resetProject.AddRange(CreateSnapshotExcludePathspecs(repository));

        var commands = new List<IReadOnlyList<string>> { resetProject };
        if (temporaryPathspecsToReconcile.Count > 0)
        {
            commands.Add(
            [
                "reset",
                "-q",
                commit,
                "--",
                .. temporaryPathspecsToReconcile,
            ]);
        }

        return commands;
    }

    private IReadOnlyList<string> GetRequiredTemporaryRepositoryPathspecs(
        RepositoryInfo repository)
    {
        return _requiredTemporaryProjectPaths
            .Select(path => CreateRequiredTemporaryPathspec(repository, path))
            .ToArray();
    }

    private static string CreateRequiredTemporaryPathspec(
        RepositoryInfo repository,
        string projectRelativePath)
    {
        string prefix = GetProjectPathPrefix(repository);
        return $":(top,literal){prefix}{projectRelativePath}";
    }

    private static string GetProjectPathFromLiteralPathspec(
        RepositoryInfo repository,
        string pathspec)
    {
        const string literalPrefix = ":(top,literal)";
        string repositoryRelativePath = pathspec[literalPrefix.Length..];
        string projectRelativePath = repository.Pathspec == "."
            ? repositoryRelativePath
            : repositoryRelativePath[(repository.Pathspec.Length + 1)..];
        return Path.Combine(
            repository.ProjectRoot,
            projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetRepositoryPathFromLiteralPathspec(string pathspec)
    {
        const string literalPrefix = ":(top,literal)";
        return pathspec[literalPrefix.Length..];
    }

    private static string CreateSnapshotBasePathspec(RepositoryInfo repository)
    {
        return repository.Pathspec == "."
            ? "."
            : $":(top,literal){repository.Pathspec}";
    }

    private async Task<SnapshotTreeBuildResult> BuildSnapshotTreeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string? baseCommit,
        CancellationToken cancellationToken)
    {
        string temporaryIndex = CreateUniqueTempPath("beutl-git-index");
        GitCommandOptions indexOptions = CreateTemporaryIndexOptions(
            temporaryIndex,
            GitCommandExecutionKind.LocalWithLfs,
            useLiteralPathspecs: false);

        try
        {
            await runner.RunAsync(
                    repository,
                    baseCommit is null
                        ? ["read-tree", "--empty"]
                        : ["read-tree", baseCommit],
                    indexOptions with { ExecutionKind = GitCommandExecutionKind.Local },
                    cancellationToken)
                .ConfigureAwait(false);
            SnapshotIndexCommandPlan indexPlan = await CreateSnapshotIndexCommandsAsync(
                    repository,
                    runner,
                    baseCommit,
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (IReadOnlyList<string> command in indexPlan.Commands)
            {
                await runner.RunAsync(
                        repository,
                        command,
                        indexOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            GitCommandResult tree = await runner.RunAsync(
                    repository,
                    ["write-tree"],
                    indexOptions with { ExecutionKind = GitCommandExecutionKind.Local },
                    cancellationToken)
                .ConfigureAwait(false);
            string treeId = tree.Stdout.Trim();
            GitRevisionValidator.ValidateCommitId(treeId, nameof(treeId));
            await EnsureSnapshotTreeContainsNoGitlinksAsync(
                    repository,
                    runner,
                    treeId,
                    cancellationToken)
                .ConfigureAwait(false);
            return new SnapshotTreeBuildResult(
                treeId,
                indexPlan.TemporaryPathspecsToReconcile);
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
        }
    }

    private async Task<SnapshotTreeCapture> BuildSnapshotTreeForCapturedHeadAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string branchRef,
        string? expectedBranchTip,
        CancellationToken cancellationToken)
    {
        using HeadOwnershipLease headLease = await AcquireSnapshotHeadLeaseAsync(
                repository,
                runner,
                branchRef,
                expectedBranchTip,
                cancellationToken)
            .ConfigureAwait(false);
        string indexPath = await ResolveGitPathAsync(
                repository,
                runner,
                "index",
                cancellationToken)
            .ConfigureAwait(false);
        IndexFileSnapshot index = await CaptureIndexFileSnapshotAsync(
                indexPath,
                cancellationToken)
            .ConfigureAwait(false);
        SnapshotTreeBuildResult tree = await BuildSnapshotTreeAsync(
                repository,
                runner,
                expectedBranchTip,
                cancellationToken)
            .ConfigureAwait(false);
        return new SnapshotTreeCapture(
            tree.Tree,
            indexPath,
            index,
            tree.TemporaryPathspecsToReconcile);
    }

    private async Task<HeadOwnershipLease> AcquireSnapshotHeadLeaseAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string branchRef,
        string? expectedBranchTip,
        CancellationToken cancellationToken)
    {
        string headPath = await ResolveGitPathAsync(
                repository,
                runner,
                "HEAD",
                cancellationToken)
            .ConfigureAwait(false);
        HeadOwnershipLease lease = await HeadOwnershipLease.AcquireAsync(
                repository,
                runner,
                headPath,
                branchRef,
                ex => LogWarningBestEffort(
                    ex,
                    "Failed to release the protected Git HEAD lock after a snapshot operation."),
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            string? currentBranchTip = await TryResolveCommitAsync(
                    repository,
                    runner,
                    branchRef,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    currentBranchTip,
                    expectedBranchTip,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ProjectCheckpointStateChangedException();
            }

            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
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

    private async Task RunPostCommitHookBestEffortAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        SnapshotCommit commit,
        string indexPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunCommitHookAsync(
                    repository,
                    runner,
                    "post-commit",
                    [],
                    new GitCommandOptions(
                        GitCommandExecutionKind.Local,
                        new Dictionary<string, string?>
                        {
                            ["GIT_EDITOR"] = ":",
                            ["GIT_INDEX_FILE"] = indexPath,
                            ["GIT_COMMIT_EDITMSG"] = commit.MessagePath,
                            ["GIT_AUTHOR_NAME"] = commit.Author.Name,
                            ["GIT_AUTHOR_EMAIL"] = commit.Author.Email,
                            ["GIT_AUTHOR_DATE"] = commit.Author.Date,
                            ["GIT_COMMITTER_NAME"] = commit.Committer.Name,
                            ["GIT_COMMITTER_EMAIL"] = commit.Committer.Email,
                            ["GIT_COMMITTER_DATE"] = commit.Committer.Date,
                        }),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // post-commit cannot reject a commit that is already durable. Match Git's one-way
            // lifecycle: report the hook failure, or its cancellation, for diagnostics without making
            // callers retry.
            LogWarningBestEffort(
                ex,
                "The post-commit hook failed after the snapshot commit became durable.");
        }
    }

    private async Task<bool> ReleaseSnapshotHeadLeaseForPostCommitAsync(HeadOwnershipLease headLease)
    {
        try
        {
            await headLease.VerifyStillOwnedAsync(CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogWarningBestEffort(
                ex,
                "The post-commit hook was skipped because the checked-out branch changed after the snapshot became durable.");
            return false;
        }
        finally
        {
            headLease.Dispose();
        }
    }

    // publicationRef is the captured branch when the update runs from a temporary worktree, or HEAD
    // when it runs in the project worktree; branchRef only names the captured branch in diagnostics.
    private async Task PublishSnapshotCommitAsync(
        RepositoryInfo publicationRepository,
        IGitCliRunner runner,
        string publicationRef,
        string branchRef,
        string? expectedOldCommit,
        string commit,
        string reflogMessage)
    {
        // Through HEAD, a lost update-ref response cannot be judged by where HEAD or a branch points
        // afterwards: HEAD may have been switched or detached, and a retry or another Git process can
        // bring a branch to the content-addressed commit. Git records an update through HEAD in HEAD's
        // reflog whether HEAD named a branch or was detached, so a marker unique to this attempt in the
        // reflog message identifies the update.
        string? publicationMarker = string.Equals(publicationRef, branchRef, StringComparison.Ordinal)
            ? null
            : $"publication {Guid.NewGuid():N}";
        try
        {
            await runner.RunAsync(
                    publicationRepository,
                    [
                        "update-ref",
                        "--create-reflog",
                        "-m",
                        publicationMarker is null ? reflogMessage : $"{reflogMessage} ({publicationMarker})",
                        publicationRef,
                        commit,
                        expectedOldCommit ?? string.Empty,
                    ],
                    GitCommandOptions.Local,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return;
        }
        catch (Exception publicationException)
        {
            string? observedCommit;
            bool published;
            try
            {
                observedCommit = await TryResolveCommitWithRetryAsync(
                        publicationRepository,
                        runner,
                        publicationRef)
                    .ConfigureAwait(false);
                published = publicationMarker is null
                    ? string.Equals(observedCommit, commit, StringComparison.OrdinalIgnoreCase)
                    : await ReflogRecordsPublicationAsync(
                            publicationRepository,
                            runner,
                            publicationMarker,
                            commit,
                            searchHead: observedCommit is not null)
                        .ConfigureAwait(false);
            }
            catch (Exception observationException)
            {
                throw new AggregateException(
                    $"The snapshot ref '{branchRef}' could not be published or observed safely.",
                    publicationException,
                    observationException);
            }

            if (published)
            {
                LogWarningBestEffort(
                    publicationException,
                    "Git reported a snapshot publication failure after the captured branch was updated.");
                return;
            }

            if (string.Equals(
                    observedCommit,
                    expectedOldCommit,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw;
            }

            throw new AggregateException(
                $"The captured branch '{branchRef}' changed before the snapshot could be published.",
                publicationException,
                new ProjectCheckpointStateChangedException());
        }
    }

    // Git records an update through HEAD in HEAD's reflog and, when HEAD named a branch, in that
    // branch's reflog too; --create-reflog writes both even with core.logAllRefUpdates off. git log
    // cannot walk the reflog of an unborn HEAD, so HEAD is searched only while it names a commit, and
    // the branch reflogs still hold the entry after HEAD is switched to an unborn branch.
    private static async Task<bool> ReflogRecordsPublicationAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string publicationMarker,
        string commit,
        bool searchHead)
    {
        string[] headRevision = searchHead ? ["HEAD"] : [];
        GitCommandResult entries = await runner.RunAsync(
                repository,
                [
                    "log",
                    "--walk-reflogs",
                    "--max-count=1",
                    "--format=%H",
                    "--fixed-strings",
                    $"--grep-reflog={publicationMarker}",
                    "--branches",
                    .. headRevision,
                ],
                GitCommandOptions.Local,
                CancellationToken.None)
            .ConfigureAwait(false);
        return entries.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(entry => string.Equals(entry, commit, StringComparison.OrdinalIgnoreCase));
    }

    private async Task PublishSnapshotAndReconcileIndexAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string branchRef,
        string? expectedOldCommit,
        string commit,
        SnapshotTreeCapture snapshot,
        HeadOwnershipLease headLease,
        string reflogMessage,
        CancellationToken cancellationToken)
    {
        // With the files backend the lease holds HEAD.lock, which also keeps Git from updating the
        // branch checked out in this worktree, so the update runs from a detached temporary worktree
        // where HEAD is not involved and the lock is what keeps HEAD on the captured branch. With the
        // reftable backend HEAD.lock guards nothing, and Git cannot verify a symbolic HEAD in the same
        // transaction that updates its target. The update therefore goes through HEAD, as git commit
        // does: under Git's own lock the branch HEAD names at that moment must still be at the
        // expected tip, and only that branch moves. A HEAD switched to another branch at the same tip
        // in that window receives the commit on that branch, and a HEAD detached at the tip receives
        // it directly; the post-commit hook then sees the ownership change.
        bool publishThroughHead = headLease.HeadStoredInReftable;
        string? refUpdateWorktreePath = publishThroughHead
            ? null
            : CreateUniqueTempPath("beutl-git-ref-update");
        RepositoryInfo publicationRepository = refUpdateWorktreePath is null
            ? repository
            : new RepositoryInfo(refUpdateWorktreePath, refUpdateWorktreePath);
        string publicationRef = publishThroughHead ? "HEAD" : branchRef;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (refUpdateWorktreePath is not null)
            {
                await AddRefUpdateWorktreeAsync(
                        repository,
                        runner,
                        refUpdateWorktreePath,
                        commit,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await headLease.VerifyStillOwnedAsync(cancellationToken).ConfigureAwait(false);
            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            IndexFileSnapshot indexAfter = await TransformIndexSnapshotAsync(
                    repository,
                    runner,
                    snapshot.IndexPath,
                    snapshot.Index,
                    CreateSnapshotIndexReconciliationCommands(
                        repository,
                        commit,
                        snapshot.TemporaryPathspecsToReconcile),
                    new GitCommandOptions(GitCommandExecutionKind.Local)
                    {
                        UseLiteralPathspecs = false,
                    },
                    $"The Git index '{snapshot.IndexPath}' changed after the snapshot tree was captured; the live index was left untouched.",
                    cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await headLease.VerifyStillOwnedAsync(CancellationToken.None).ConfigureAwait(false);
                await PublishSnapshotCommitAsync(
                        publicationRepository,
                        runner,
                        publicationRef,
                        branchRef,
                        expectedOldCommit,
                        commit,
                        reflogMessage)
                    .ConfigureAwait(false);
            }
            catch (Exception publicationException)
            {
                try
                {
                    await RestoreFailedIndexSnapshotAsync(
                            snapshot.IndexPath,
                            snapshot.Index,
                            indexAfter)
                        .ConfigureAwait(false);
                }
                catch (Exception restoreException)
                {
                    throw new AggregateException(
                        "The snapshot could not be published and the prior index could not be restored.",
                        publicationException,
                        restoreException);
                }

                throw;
            }

            CacheHistoricalRequiredTemporaryPaths(
                commit,
                _requiredTemporaryProjectPaths);
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
        }
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

        await ValidateProjectSnapshotLayoutPreferringConflictAsync(repository, cancellationToken)
            .ConfigureAwait(false);
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        string branchRef = await GetAttachedBranchRefCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        string? ignoredPath = await FindIgnoredExistingRequiredProjectPathAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        ThrowIfRequiredProjectPathIgnored(ignoredPath);
        WorkspaceStatus status = await GetSnapshotStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfConflicted(status);
        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);

        (string? originalBranchTip, SnapshotTreeCapture snapshot) = await CaptureBranchSnapshotAsync(
                repository,
                runner,
                branchRef,
                cancellationToken)
            .ConfigureAwait(false);
        string desiredTree = snapshot.Tree;
        if (originalBranchTip is not null)
        {
            string originalTree = await ResolveTreeAsync(
                    repository,
                    runner,
                    originalBranchTip,
                    cancellationToken)
                .ConfigureAwait(false);
            if (string.Equals(desiredTree, originalTree, StringComparison.OrdinalIgnoreCase))
            {
                return new CommitResult.NoChanges();
            }
        }
        else if (status.IsClean && _requiredTemporaryProjectPaths.Count == 0)
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

        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        using HeadOwnershipLease headLease = await AcquireSnapshotHeadLeaseAsync(
                repository,
                runner,
                branchRef,
                originalBranchTip,
                cancellationToken)
            .ConfigureAwait(false);
        SnapshotCommit? commit = await CreateSnapshotCommitAsync(
                repository,
                runner,
                desiredTree,
                originalBranchTip,
                message,
                kind,
                cancellationToken)
            .ConfigureAwait(false);
        if (commit is null)
        {
            return new CommitResult.NoChanges();
        }

        await PublishSnapshotAndRunPostCommitHookAsync(
                repository,
                runner,
                branchRef,
                originalBranchTip,
                commit,
                snapshot,
                headLease,
                $"beutl: {kind.ToString().ToLowerInvariant()} snapshot",
                cancellationToken)
            .ConfigureAwait(false);

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new CommitResult.Committed(new CommitRevision.Known(commit.Commit));
    }

    private async Task ValidateProjectSnapshotLayoutPreferringConflictAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateProjectSnapshotLayout(repository.ProjectRoot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not OutOfMemoryException)
        {
            // Git can begin a merge after the initial status check and write conflict
            // markers before the project graph is deserialized. Prefer the conflict
            // guidance when that race is observed, but preserve unrelated parse errors.
            try
            {
                await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (VersionControlConflictedException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception)
            {
            }

            throw;
        }
    }

    private async Task<(string? OriginalBranchTip, SnapshotTreeCapture Snapshot)> CaptureBranchSnapshotAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string branchRef,
        CancellationToken cancellationToken)
    {
        string? originalBranchTip = await TryResolveCommitAsync(
                repository,
                runner,
                branchRef,
                cancellationToken)
            .ConfigureAwait(false);
        SnapshotTreeCapture snapshot = await BuildSnapshotTreeForCapturedHeadAsync(
                repository,
                runner,
                branchRef,
                originalBranchTip,
                cancellationToken)
            .ConfigureAwait(false);
        return (originalBranchTip, snapshot);
    }

    // The commit message file stays until the post-commit hook has run, which reads it.
    private async Task PublishSnapshotAndRunPostCommitHookAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string branchRef,
        string? originalBranchTip,
        SnapshotCommit commit,
        SnapshotTreeCapture snapshot,
        HeadOwnershipLease headLease,
        string reflogMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await PublishSnapshotAndReconcileIndexAsync(
                    repository,
                    runner,
                    branchRef,
                    originalBranchTip,
                    commit.Commit,
                    snapshot,
                    headLease,
                    reflogMessage,
                    cancellationToken)
                .ConfigureAwait(false);
            if (await ReleaseSnapshotHeadLeaseForPostCommitAsync(headLease).ConfigureAwait(false))
            {
                await RunPostCommitHookBestEffortAsync(
                        repository,
                        runner,
                        commit,
                        snapshot.IndexPath,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            TryDeleteTemporaryIndex(commit.MessagePath);
        }
    }
}
