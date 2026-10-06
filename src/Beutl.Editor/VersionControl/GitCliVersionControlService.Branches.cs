namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private async Task<bool> CanCreateBranchCoreAsync(
        string name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["check-ref-format", "--branch", name],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string validatedName = RemoveSingleTrailingLineEnding(result.Stdout);
            if (!string.Equals(validatedName, name, StringComparison.Ordinal))
            {
                return false;
            }

            IReadOnlyList<BranchInfo> branches = await GetLocalBranchesCoreAsync(cancellationToken)
                .ConfigureAwait(false);
            StringComparison branchNameComparison = await UsesCaseInsensitiveFilesRefStorageAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (branches.Any(branch => BranchNamesConflict(
                    branch.Name,
                    name,
                    branchNameComparison)))
            {
                return false;
            }

            return !await HasLooseBranchPathCollisionAsync(
                    repository,
                    runner,
                    name,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitOperationException)
        {
            return false;
        }
    }

    private static string RemoveSingleTrailingLineEnding(string value)
    {
        if (value.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return value[..^2];
        }

        return value.EndsWith('\n') ? value[..^1] : value;
    }

    private async Task CreateBranchCoreAsync(
        string name,
        string startPoint,
        CancellationToken cancellationToken)
    {
        GitRevisionValidator.ValidateCommitId(startPoint, nameof(startPoint));
        if (!await CanCreateBranchCoreAsync(name, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException(
                "The branch must be a valid, unused local branch name.",
                nameof(name));
        }

        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        // An abbreviated ID is a valid start point, but HEAD reads back as a full ID, so the comparison,
        // the switch and the cleanup all use the commit the start point resolves to.
        string startCommit = await TryResolveCommitAsync(repository, runner, startPoint, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new ArgumentException(
                "The start point must identify a commit in the repository.",
                nameof(startPoint));
        // Like git switch -c, a branch that starts at the checked-out commit changes no file, so it does
        // not need the project to be closed. Any other start point rewrites the worktree.
        bool projectOpen = !_isWorktreeMutationAllowed();
        if (projectOpen)
        {
            CheckedOutBranchTip head = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(head.Commit, startCommit, StringComparison.OrdinalIgnoreCase))
            {
                EnsureWorktreeMutationAllowed();
            }
        }

        // With the project open, the switch would still run a post-checkout hook, which can rewrite
        // project files behind the editors, so hooks stay off for it. As one command, the switch also
        // keeps HEAD, the index and the files on one commit when another Git process moves HEAD after
        // the check above.
        string[] hooksOverride = projectOpen ? ["-c", "core.hooksPath=/dev/null"] : [];
        try
        {
            await runner.RunAsync(
                repository,
                [
                    .. s_lfsPathFilterOverrides,
                    .. hooksOverride,
                    "switch",
                    "--no-overwrite-ignore",
                    "-c",
                    name,
                    startCommit,
                ],
                new GitCommandOptions(GitCommandExecutionKind.LocalWithLfs),
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException ex) when (ex.Stderr.Contains("already exists", StringComparison.Ordinal))
        {
            // Another Git process created the branch after the name check, so the switch created nothing
            // and the branch it found is not this attempt's to remove.
            throw;
        }
        catch (Exception switchFailure)
        {
            // git switch -c creates the branch before it moves HEAD, so a failed switch can leave the
            // branch behind, and a retry would find the name taken. Once HEAD has moved, a switch with
            // hooks off has done all its work; with hooks on, the post-checkout hook can still fail it.
            bool headMoved = await RemoveBranchUnlessHeadMovedAsync(
                    repository,
                    runner,
                    $"refs/heads/{name}",
                    startCommit,
                    switchFailure)
                .ConfigureAwait(false);
            if (!headMoved || !projectOpen)
            {
                throw;
            }
        }

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
    }

    // Returns true when HEAD reached the branch even though the switch reported failure. Otherwise the
    // branch goes, but only while it still points at the commit this attempt created it on; when HEAD
    // cannot be read, the branch stays rather than leaving HEAD on a deleted ref.
    private static async Task<bool> RemoveBranchUnlessHeadMovedAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string branchRef,
        string startPoint,
        Exception switchFailure)
    {
        string headRef;
        try
        {
            GitCommandResult symbolicRef = await runner.RunAsync(
                    repository,
                    ["symbolic-ref", "--quiet", "HEAD"],
                    GitCommandOptions.Local,
                    CancellationToken.None)
                .ConfigureAwait(false);
            headRef = symbolicRef.Stdout.Trim();
        }
        catch (Exception ex) when (ex is GitOperationException or TimeoutException)
        {
            return false;
        }

        if (string.Equals(headRef, branchRef, StringComparison.Ordinal))
        {
            return true;
        }

        // The switch can fail before it creates the branch, as when another Git process holds the
        // index, and then there is nothing to remove.
        try
        {
            await runner.RunAsync(
                    repository,
                    ["rev-parse", "--verify", "--quiet", branchRef],
                    GitCommandOptions.Local,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is GitOperationException or TimeoutException)
        {
            return false;
        }

        try
        {
            await runner.RunAsync(
                    repository,
                    ["update-ref", "-d", branchRef, startPoint],
                    GitCommandOptions.Local,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception cleanupFailure)
        {
            throw new AggregateException(
                $"Switching to the new branch '{branchRef}' failed, and the branch could not be removed.",
                switchFailure,
                cleanupFailure);
        }

        return false;
    }

    private async Task SwitchBranchCoreAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ValidateSwitchBranchName(name);
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        EnsureWorktreeMutationAllowed();
        IReadOnlyList<BranchInfo> branches = await GetBranchesCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        bool originOnly = !ContainsLocalBranch(branches, name);
        if (originOnly
            && (!ContainsOriginOnlyBranch(branches, name)
                || !await CanCreateBranchCoreAsync(name, cancellationToken).ConfigureAwait(false)))
        {
            throw new ArgumentException(
                "The branch must exactly name an existing local branch or a branch only origin has.",
                nameof(name));
        }

        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        // As git switch does for a branch only one remote has, create the local branch tracking it.
        IReadOnlyList<string> arguments = originOnly
            ?
            [
                .. s_lfsPathFilterOverrides,
                "switch",
                "--no-overwrite-ignore",
                "-c",
                name,
                "--track",
                $"{OriginRefPrefix}{name}",
            ]
            : [.. s_lfsPathFilterOverrides, "switch", "--no-overwrite-ignore", name];
        await runner.RunAsync(
            repository,
            arguments,
            new GitCommandOptions(GitCommandExecutionKind.LocalWithLfs),
            cancellationToken).ConfigureAwait(false);
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
    }

    private static void ValidateSwitchBranchName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name[0] == '-')
        {
            throw new ArgumentException(
                "The branch name must not be interpreted as a Git command-line option.",
                nameof(name));
        }
    }

    private static bool ContainsLocalBranch(
        IReadOnlyList<BranchInfo> branches,
        string name)
    {
        return branches.Any(branch =>
            !branch.IsRemote
            && string.Equals(branch.Name, name, StringComparison.Ordinal));
    }

    private static bool ContainsOriginOnlyBranch(
        IReadOnlyList<BranchInfo> branches,
        string name)
    {
        return branches.Any(branch =>
            branch.IsRemote
            && string.Equals(branch.Name, name, StringComparison.Ordinal));
    }

    private static bool BranchNamesConflict(
        string existingName,
        string candidateName,
        StringComparison comparison)
    {
        return string.Equals(existingName, candidateName, comparison)
               || existingName.StartsWith($"{candidateName}/", comparison)
               || candidateName.StartsWith($"{existingName}/", comparison);
    }

    private static async Task<bool> UsesCaseInsensitiveFilesRefStorageAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult storage = await runner.RunAsync(
                repository,
                ["config", "--local", "--get", "extensions.refStorage"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(
                    storage.Stdout.Trim(),
                    "files",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            // The traditional files backend omits extensions.refStorage.
        }

        string headsDirectory = await ResolveGitPathAsync(
                repository,
                runner,
                "refs/heads",
                cancellationToken)
            .ConfigureAwait(false);
        return IsDirectoryStorageCaseInsensitive(headsDirectory);
    }

    private static bool IsDirectoryStorageCaseInsensitive(string directory)
    {
        try
        {
            DirectoryInfo? current = new DirectoryInfo(directory);
            while (current is not null && !current.Exists)
            {
                current = current.Parent;
            }

            while (current?.Parent is not null)
            {
                string aliasName = current.Name.ToUpperInvariant();
                if (string.Equals(aliasName, current.Name, StringComparison.Ordinal))
                {
                    aliasName = current.Name.ToLowerInvariant();
                }

                if (!string.Equals(aliasName, current.Name, StringComparison.Ordinal))
                {
                    string aliasPath = Path.Combine(current.Parent.FullName, aliasName);
                    if (!Directory.Exists(aliasPath))
                    {
                        return false;
                    }

                    bool distinctAliasExists = current.Parent
                        .EnumerateDirectories()
                        .Any(candidate => string.Equals(
                            candidate.Name,
                            aliasName,
                            StringComparison.Ordinal));
                    return !distinctAliasExists;
                }

                current = current.Parent;
            }

            return OperatingSystem.IsWindows();
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException)
        {
            // Conservatively reject case aliases when the files backend cannot be inspected.
            return true;
        }
    }

    private static async Task<bool> HasLooseBranchPathCollisionAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string candidateName,
        CancellationToken cancellationToken)
    {
        string headsDirectory = await ResolveGitPathAsync(
                repository,
                runner,
                "refs/heads",
                cancellationToken)
            .ConfigureAwait(false);
        string candidatePath = Path.Combine(
            headsDirectory,
            candidateName.Replace('/', Path.DirectorySeparatorChar));
        if (Path.Exists(candidatePath))
        {
            return true;
        }

        string? parent = Path.GetDirectoryName(candidatePath);
        while (parent is not null
               && !string.Equals(parent, headsDirectory, StringComparison.Ordinal))
        {
            if (File.Exists(parent))
            {
                return true;
            }

            parent = Path.GetDirectoryName(parent);
        }

        return false;
    }
}
