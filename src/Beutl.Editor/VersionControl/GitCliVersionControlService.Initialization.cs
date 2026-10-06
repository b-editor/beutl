namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private async Task EnsureRepositoryHygieneSerializedCoreAsync(CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        (GitAvailability availability, IGitCliRunner? runner)
            = await GetGitRuntimeCoreAsync(cancellationToken).ConfigureAwait(false);
        if (availability.State != GitAvailabilityState.Installed || runner is null)
        {
            throw new InvalidOperationException("Git is not available.");
        }

        try
        {
            await EnsureRepositoryHygienePreflightCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
            when (ex is VersionControlConflictedException or DetachedHeadNotSupportedException)
        {
            // Opening keeps such a repository tracked until the state is resolved outside Beutl, so the
            // next commit finishes this hygiene instead of recording a snapshot without it.
            _hygieneDeferred = true;
            throw;
        }

        bool lfsRequested = _installationLocator.Config.UseLfsWhenAvailable;
        await EnsureRepositoryHygieneCoreAsync(
                repository,
                runner,
                lfsRequested && availability.LfsInstalled,
                // Every machine using this repository shares the block. Only turning the
                // setting off while the project is open is a decision about it.
                removeManagedLfs: !lfsRequested && _lastLfsRequested == true,
                cancellationToken)
            .ConfigureAwait(false);
        _lastLfsRequested = lfsRequested;
        _hygieneDeferred = false;
    }

    private async Task InitializeCoreAsync(
        InitOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options.TargetRepository);
        string projectRoot = options.TargetRepository.ProjectRoot;

        (GitAvailability availability, IGitCliRunner? nullableRunner)
            = await GetGitRuntimeCoreAsync(cancellationToken).ConfigureAwait(false);
        if (availability.State != GitAvailabilityState.Installed || nullableRunner is null)
        {
            throw new InvalidOperationException("Git is not available.");
        }

        IGitCliRunner runner = nullableRunner;
        RepositoryInfo? discoveredRepository = Directory.Exists(projectRoot)
            ? await DiscoverRepositoryCoreAsync(
                    projectRoot,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false)
            : null;
        RepositoryInfo repository;
        if (discoveredRepository is { IsNestedInForeignRepo: true })
        {
            if (!MatchesRepositorySelection(discoveredRepository, options.TargetRepository))
            {
                throw new EnclosingRepositoryConsentRequiredException(discoveredRepository);
            }

            repository = discoveredRepository;
        }
        else if (discoveredRepository is not null)
        {
            if (!MatchesRepositorySelection(discoveredRepository, options.TargetRepository))
            {
                throw new InvalidOperationException(
                    "The selected repository does not match the repository containing the project.");
            }

            repository = discoveredRepository;
        }
        else
        {
            if (options.TargetRepository.IsNestedInForeignRepo)
            {
                throw new InvalidOperationException(
                    "The selected existing repository no longer contains the project.");
            }

            repository = options.TargetRepository;
        }

        ValidateProjectSnapshotLayout(repository.ProjectRoot);

        if (Repository is not null
            && !VersionControlPathComparison.AreSameCanonicalPath(Repository.ProjectRoot, projectRoot)
            && !MatchesRepositorySelection(Repository, repository))
        {
            throw new InvalidOperationException(
                "This service is already associated with a different project.");
        }

        GitIdentity? identity = options.Identity;
        if (identity is not null)
        {
            ValidateIdentity(identity);
        }
        else if (Directory.Exists(repository.RepoRoot))
        {
            identity = await GetIdentityCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (identity is null)
        {
            throw new GitIdentityRequiredException();
        }

        EnsureHygienePathsAreSafe(repository);
        if (discoveredRepository is not null)
        {
            await EnsureInitializationPreflightCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            string? ignoredPath = await FindIgnoredRequiredProjectPathBeforeInitAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            ThrowIfRequiredProjectPathIgnored(ignoredPath);
        }

        if (discoveredRepository is null)
        {
            Directory.CreateDirectory(projectRoot);
            Repository = repository;
            // Follow init.defaultBranch as git init does. An invalid value makes git init itself fail, so
            // main replaces it for that one command.
            string? configuredBranch = await GetConfiguredInitialBranchAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            bool useConfiguredBranch = configuredBranch is not null
                                       && await IsValidInitialBranchNameAsync(
                                               repository,
                                               runner,
                                               configuredBranch,
                                               cancellationToken)
                                           .ConfigureAwait(false);
            string[] initArguments = configuredBranch is null || useConfiguredBranch
                ? ["init"]
                : ["-c", "init.defaultBranch=main", "init"];
            string initialBranch = useConfiguredBranch ? configuredBranch! : "main";
            await runner.RunAsync(
                repository,
                initArguments,
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            await runner.RunAsync(
                repository,
                ["symbolic-ref", "HEAD", $"refs/heads/{initialBranch}"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);

            RepositoryInfo? initializedRepository = await DiscoverRepositoryCoreAsync(
                    projectRoot,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            if (initializedRepository is not null
                && !MatchesRepositorySelection(
                    initializedRepository,
                    options.TargetRepository))
            {
                throw new InvalidOperationException(
                    "The initialized repository could not be resolved safely.");
            }

            if (initializedRepository is not null)
            {
                repository = initializedRepository;
                Repository = repository;
            }
        }
        else
        {
            Repository = repository;
        }

        if (options.Identity is not null)
        {
            await SetLocalIdentityCoreAsync(
                    repository,
                    runner,
                    options.Identity,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        string ignorePath = Path.Combine(repository.ProjectRoot, ".gitignore");
        string attributesPath = Path.Combine(repository.ProjectRoot, ".gitattributes");
        bool useLfs = options.UseLfsWhenAvailable && availability.LfsInstalled;
        bool removeManagedLfs = false;
        if (useLfs)
        {
            useLfs = await TryInstallLfsLocallyAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            removeManagedLfs = !useLfs;
        }

        await EnsureLinesAsync(
                ignorePath,
                s_gitIgnoreLines,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureAttributesAsync(
                attributesPath,
                useLfs,
                removeManagedLfs,
                cancellationToken)
            .ConfigureAwait(false);
        _lastLfsRequested = options.UseLfsWhenAvailable;

        WorkspaceStatus status = await GetStatusCoreAsync(
                repository,
                runner,
                cancellationToken,
                CreateSnapshotExcludePathspecs(repository))
            .ConfigureAwait(false);
        if (!status.IsClean)
        {
            await RaiseLargeMediaNoticeIfNeededAsync(
                    repository,
                    runner,
                    status,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        string branchRef = await GetAttachedBranchRefCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
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
        string desiredTree = snapshot.Tree;
        string? originalTree = originalBranchTip is null
            ? null
            : await ResolveTreeAsync(
                    repository,
                    runner,
                    originalBranchTip,
                    cancellationToken)
                .ConfigureAwait(false);
        if (originalTree is null
            || !string.Equals(desiredTree, originalTree, StringComparison.OrdinalIgnoreCase))
        {
            using HeadOwnershipLease headLease = await AcquireSnapshotHeadLeaseAsync(
                    repository,
                    runner,
                    branchRef,
                    originalBranchTip,
                    cancellationToken)
                .ConfigureAwait(false);
            SnapshotCommit commit = await CreateSnapshotCommitAsync(
                    repository,
                    runner,
                    desiredTree,
                    originalBranchTip,
                    "beutl: initialize version control",
                    SnapshotKind.Init,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "The initial snapshot did not produce a commit.");
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
                        "beutl: initialize version control",
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

        TryEnsureWatcher();
        await TryRaiseLfsQuotaNoticeIfNeededAsync(
            repository,
            runner).ConfigureAwait(false);
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
    }

    private async Task EnsureInitializationPreflightCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        EnsureHygienePathsAreSafe(repository);
        await GetAttachedBranchRefCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        await EnsureRepositoryStatusAndIgnorePreflightCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnsureRepositoryHygienePreflightCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        EnsureHygienePathsAreSafe(repository);
        await GetCheckedOutBranchTipCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        await EnsureRepositoryStatusAndIgnorePreflightCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnsureRepositoryStatusAndIgnorePreflightCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        WorkspaceStatus status = await GetStatusCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        ThrowIfConflicted(status);
        string? ignoredPath = await FindIgnoredRequiredProjectPathAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        ThrowIfRequiredProjectPathIgnored(ignoredPath);
    }

    private async Task EnsureRepositoryHygieneCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        bool useLfs,
        bool removeManagedLfs,
        CancellationToken cancellationToken)
    {
        EnsureHygienePathsAreSafe(repository);
        string ignorePath = Path.Combine(repository.ProjectRoot, ".gitignore");
        string attributesPath = Path.Combine(repository.ProjectRoot, ".gitattributes");
        await ReadHygieneFileSnapshotAsync(ignorePath, cancellationToken).ConfigureAwait(false);
        await ReadHygieneFileSnapshotAsync(attributesPath, cancellationToken).ConfigureAwait(false);
        if (useLfs)
        {
            useLfs = await TryInstallLfsLocallyAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            // Git LFS is installed but could not be enabled here, so its filters would commit
            // pointers that no hook pushes.
            removeManagedLfs |= !useLfs;
        }

        await EnsureLinesAsync(
            ignorePath,
            s_gitIgnoreLines,
            cancellationToken).ConfigureAwait(false);
        await EnsureAttributesAsync(
            attributesPath,
            useLfs,
            removeManagedLfs,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> GetConfiguredInitialBranchAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["config", "--get", "init.defaultBranch"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string name = result.Stdout.TrimEnd('\r', '\n');
            return name.Length == 0 ? null : name;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return null;
        }
    }

    private static async Task<bool> IsValidInitialBranchNameAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["check-ref-format", "--branch", name],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return string.Equals(result.Stdout.TrimEnd('\r', '\n'), name, StringComparison.Ordinal);
        }
        catch (GitOperationException)
        {
            return false;
        }
    }

    private static async Task<RepositoryInfo?> DiscoverRepositoryCoreAsync(
        string projectRoot,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string normalizedProjectRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
        if (normalizedProjectRoot.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Repository discovery does not support control characters in project paths.",
                nameof(projectRoot));
        }

        var discoveryContext = new RepositoryInfo(normalizedProjectRoot, normalizedProjectRoot);
        try
        {
            GitCommandResult rootResult = await runner.RunAsync(
                discoveryContext,
                ["rev-parse", "--show-toplevel"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            GitCommandResult prefixResult = await runner.RunAsync(
                discoveryContext,
                ["rev-parse", "--show-prefix"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string root = ParseRepositoryDiscoveryPath(
                rootResult.Stdout,
                allowEmpty: false,
                description: "repository root");
            string prefix = ParseRepositoryDiscoveryPath(
                prefixResult.Stdout,
                allowEmpty: true,
                description: "project prefix");
            string repoRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            string resolvedProjectRoot = GetDiscoveredProjectRoot(
                repoRoot,
                prefix);
            if (!RepositoryPathComparer.AreEquivalent(
                    resolvedProjectRoot,
                    normalizedProjectRoot))
            {
                throw new InvalidOperationException(
                    "Git repository discovery returned a project root that does not match the requested path.");
            }

            return new RepositoryInfo(repoRoot, resolvedProjectRoot);
        }
        catch (GitOperationException ex) when (IsNotRepositoryFailure(ex))
        {
            return null;
        }
    }

    private static string ParseRepositoryDiscoveryPath(
        string stdout,
        bool allowEmpty,
        string description)
    {
        if (stdout.Length == 0)
        {
            if (allowEmpty)
            {
                return string.Empty;
            }

            throw new InvalidOperationException(
                $"Git repository discovery returned an empty {description}.");
        }

        if (!stdout.EndsWith('\n'))
        {
            throw new InvalidOperationException(
                $"Git repository discovery returned an invalid {description} record.");
        }

        string value = stdout[..^1];
        if (value.EndsWith('\r'))
        {
            value = value[..^1];
        }

        if ((!allowEmpty && value.Length == 0) || value.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"Git repository discovery returned an invalid {description}.");
        }

        return value;
    }

    private static string GetDiscoveredProjectRoot(string repoRoot, string prefix)
    {
        string normalizedPrefix = NormalizeGitPath(prefix);
        if (Path.IsPathFullyQualified(normalizedPrefix)
            || normalizedPrefix
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Contains("..", StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Git repository discovery returned an invalid project prefix.");
        }

        string platformPrefix = normalizedPrefix.Replace('/', Path.DirectorySeparatorChar);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            Path.Combine(repoRoot, platformPrefix)));
    }

    private static bool MatchesRepositorySelection(
        RepositoryInfo discovered,
        RepositoryInfo selected)
    {
        return discovered.IsNestedInForeignRepo == selected.IsNestedInForeignRepo
               && RepositoryPathComparer.AreEquivalent(
                   discovered.RepoRoot,
                   selected.RepoRoot)
               && RepositoryPathComparer.AreEquivalent(
                   discovered.ProjectRoot,
                   selected.ProjectRoot);
    }

    private async Task<bool> HasVersionTrackingOptInCoreAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        if (await IsRepositoryHygieneAppliedCoreAsync(repository.ProjectRoot, cancellationToken)
                .ConfigureAwait(false))
        {
            return true;
        }

        // The hygiene files can be deleted or checked out away, so the durable record of an
        // earlier opt-in is a snapshot Beutl itself committed for this project. A repository with
        // no readable history has none, and asking again is the safe answer.
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                [
                    "log",
                    "--no-show-signature",
                    "--max-count=1",
                    "--format=%H",
                    "--grep=^Beutl-Snapshot: ",
                    "HEAD",
                    "--",
                    repository.Pathspec,
                ],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return !string.IsNullOrWhiteSpace(result.Stdout);
        }
        catch (GitOperationException)
        {
            return false;
        }
    }

    private async Task<bool> HasCheckedOutCommitCoreAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await runner.RunAsync(
                repository,
                ["rev-parse", "--verify", "--quiet", "HEAD^{commit}"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            // --quiet reports a revision that does not resolve, such as an unborn branch, as exit
            // code 1 without output. Any other failure still surfaces to the caller.
            return false;
        }
    }
}
