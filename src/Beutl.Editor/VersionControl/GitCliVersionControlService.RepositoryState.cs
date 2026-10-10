using Beutl.Language;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private async Task<CheckedOutBranchTip> GetCheckedOutBranchTipCoreAsync(CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        return await GetCheckedOutBranchTipCoreAsync(repository, runner, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CheckedOutBranchTip> GetCheckedOutBranchTipCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string refName = await GetAttachedBranchRefCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        GitCommandResult commit = await runner.RunAsync(
            repository,
            ["rev-parse", "--verify", $"{refName}^{{commit}}"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return new CheckedOutBranchTip(refName, commit.Stdout.Trim());
    }

    private static async Task<string> GetAttachedBranchRefCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult symbolicRef;
        try
        {
            symbolicRef = await runner.RunAsync(
                repository,
                ["symbolic-ref", "--quiet", "HEAD"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            throw new DetachedHeadNotSupportedException();
        }

        string refName = symbolicRef.Stdout.Trim();
        if (!refName.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            throw new DetachedHeadNotSupportedException();
        }

        return refName;
    }

    private static void ValidateBranchTipForRollback(
        CheckedOutBranchTip expectedCurrent,
        CheckedOutBranchTip target)
    {
        ValidateAttachedBranchTip(expectedCurrent, nameof(expectedCurrent));
        ValidateAttachedBranchTip(target, nameof(target));
        if (!string.Equals(expectedCurrent.RefName, target.RefName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The rollback heads must identify the same local branch.",
                nameof(target));
        }
    }

    private static void ValidateAttachedBranchTip(CheckedOutBranchTip tip, string paramName)
    {
        ArgumentNullException.ThrowIfNull(tip, paramName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tip.RefName, paramName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tip.Commit, paramName);
        if (!IsValidLocalBranchRef(tip.RefName))
        {
            throw new ArgumentException("An attached local branch tip is required.", paramName);
        }

        GitRevisionValidator.ValidateCommitId(tip.Commit, paramName);
    }

    private static bool IsValidLocalBranchRef(string refName)
    {
        const string Prefix = "refs/heads/";
        if (!refName.StartsWith(Prefix, StringComparison.Ordinal)
            || refName.Length == Prefix.Length
            || refName.EndsWith("/", StringComparison.Ordinal)
            || refName.EndsWith(".", StringComparison.Ordinal)
            || refName.Contains("//", StringComparison.Ordinal)
            || refName.Contains("..", StringComparison.Ordinal)
            || refName.Contains("@{", StringComparison.Ordinal)
            || refName.Any(static character => character <= ' '
                || character == '\u007f'
                || character is '~' or '^' or ':' or '?' or '*' or '[' or '\\'))
        {
            return false;
        }

        foreach (string component in refName.Split('/'))
        {
            if (component.Length == 0
                || component.StartsWith(".", StringComparison.Ordinal)
                || component.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetBranchShortName(string refName)
    {
        const string Prefix = "refs/heads/";
        if (!refName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An attached local branch ref is required.",
                nameof(refName));
        }

        return refName[Prefix.Length..];
    }

    private static async Task<CheckedOutBranchTip?> TryGetCheckedOutBranchTipAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetCheckedOutBranchTipCoreAsync(repository, runner, cancellationToken).ConfigureAwait(false);
        }
        catch (DetachedHeadNotSupportedException)
        {
            return null;
        }
    }

    private static async Task<string?> TryResolveCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string revision,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["rev-parse", "--verify", "--quiet", $"{revision}^{{commit}}"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string commit = result.Stdout.Trim();
            return string.IsNullOrEmpty(commit) ? null : commit;
        }
        catch (GitOperationException ex) when (ex.ExitCode is 1 or 128
                                                && !ex.IsRepositoryLockFailure)
        {
            return null;
        }
    }

    private static async Task<string?> TryResolveObjectAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string revision,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["rev-parse", "--verify", "--quiet", revision],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string objectId = result.Stdout.Trim();
            return string.IsNullOrEmpty(objectId) ? null : objectId;
        }
        catch (GitOperationException ex) when (ex.ExitCode is 1 or 128)
        {
            return null;
        }
    }

    private static async Task<string> ResolveTreeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string revision,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
            repository,
            ["rev-parse", "--verify", $"{revision}^{{tree}}"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return result.Stdout.Trim();
    }

    private static async Task EnsureCheckedOutTipUnchangedAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CheckedOutBranchTip expected,
        string changedMessage,
        CancellationToken cancellationToken)
    {
        CheckedOutBranchTip currentTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(currentTip, expected))
        {
            throw new InvalidOperationException(changedMessage);
        }
    }

    // Runs commit-tree and confirms the commit object exists before the caller refers to it.
    private static async Task<string> CommitTreeAndVerifyAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        IReadOnlyList<string> arguments,
        GitCommandOptions options,
        string missingCommitMessage,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken)
            .ConfigureAwait(false);
        string commit = result.Stdout.Trim();
        if (commit.Length == 0)
        {
            throw new InvalidOperationException(missingCommitMessage);
        }

        await runner.RunAsync(
                repository,
                ["cat-file", "-e", commit + "^{commit}"],
                GitCommandOptions.Local,
                cancellationToken)
            .ConfigureAwait(false);

        return commit;
    }

    private static async Task<bool> IsWholeRepositoryCleanAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
            repository,
            ["status", "--porcelain=v1", "--untracked-files=all", "-z"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return result.Stdout.Length == 0;
    }

    private static async Task<bool> IsOutsideProjectCleanAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "status",
                "--porcelain=v1",
                "--untracked-files=all",
                "-z",
                "--",
                ":/",
                $":(top,exclude,literal){repository.Pathspec}",
            ],
            new GitCommandOptions(GitCommandExecutionKind.Local) { UseLiteralPathspecs = false },
            cancellationToken).ConfigureAwait(false);
        return result.Stdout.Length == 0;
    }

    private static async Task<bool> IsProjectCleanAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "status",
                "--porcelain=v1",
                "--untracked-files=all",
                "-z",
                "--",
                repository.Pathspec,
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return result.Stdout.Length == 0;
    }

    private static async Task<bool> IsProjectIndexCleanAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(
                repository,
                ["diff", "--cached", "--quiet", "--", repository.Pathspec],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return false;
        }
    }

    private static async Task<bool> IsWholeIndexCleanAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(
                repository,
                ["diff", "--cached", "--quiet", "--", "."],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return false;
        }
    }

    private static async Task<bool> IsAncestorAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string ancestor,
        string descendant,
        CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(
                repository,
                ["merge-base", "--is-ancestor", ancestor, descendant],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return false;
        }
    }

    private static async Task<PullRelation> GetPullRelationAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string localCommit,
        string upstreamCommit,
        CancellationToken cancellationToken)
    {
        if (string.Equals(localCommit, upstreamCommit, StringComparison.OrdinalIgnoreCase))
        {
            return PullRelation.Equal;
        }

        if (await IsAncestorAsync(
                repository,
                runner,
                localCommit,
                upstreamCommit,
                cancellationToken).ConfigureAwait(false))
        {
            return PullRelation.LocalBehind;
        }

        return await IsAncestorAsync(
                repository,
                runner,
                upstreamCommit,
                localCommit,
                cancellationToken).ConfigureAwait(false)
            ? PullRelation.LocalAhead
            : PullRelation.Diverged;
    }

    private static async Task<string> ResolveGitPathAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string gitPath,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
            repository,
            ["rev-parse", "--git-path", gitPath],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        string path = result.Stdout.TrimEnd('\r', '\n');
        return Path.GetFullPath(
            Path.IsPathFullyQualified(path)
                ? path
                : Path.Combine(repository.RepoRoot, path));
    }

    private static Task<GitCommandResult> ResetIndexAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string commit,
        string pathspec)
    {
        return string.Equals(pathspec, ".", StringComparison.Ordinal)
            ? runner.RunAsync(
                repository,
                ["read-tree", "--reset", commit],
                GitCommandOptions.Local,
                CancellationToken.None)
            : runner.RunAsync(
                repository,
                ["restore", $"--source={commit}", "--staged", "--", pathspec],
                GitCommandOptions.Local,
                CancellationToken.None);
    }

    private static async Task<bool> IsIndexAtCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string commit,
        string pathspec,
        CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(
                repository,
                ["diff", "--cached", "--quiet", commit, "--", pathspec],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return false;
        }
    }

    private static string GetCheckpointRefPrefix(RepositoryInfo repository)
    {
        return $"refs/beutl/safety/{GetConfigKeyHash(repository.Pathspec)}/";
    }

    private static string GetPendingRecoveryRefPrefix(RepositoryInfo repository)
    {
        return $"refs/beutl/recovery/{GetConfigKeyHash(repository.Pathspec)}/";
    }

    private static bool EqualsBranchTip(CheckedOutBranchTip left, CheckedOutBranchTip right)
    {
        return string.Equals(left.RefName, right.RefName, StringComparison.Ordinal)
               && string.Equals(left.Commit, right.Commit, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateUniqueTempPath(string prefix)
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"{prefix}-{Guid.NewGuid():N}");
    }

    private static GitCommandOptions CreateTemporaryIndexOptions(
        string indexPath,
        GitCommandExecutionKind executionKind = GitCommandExecutionKind.Local,
        bool useLiteralPathspecs = true)
    {
        return new GitCommandOptions(
            executionKind,
            new Dictionary<string, string?>
            {
                ["GIT_INDEX_FILE"] = indexPath,
            },
            UseLiteralPathspecs: useLiteralPathspecs);
    }

    private static void TryDeleteTemporaryIndex(string path)
    {
        try
        {
            File.Delete(path);
            File.Delete($"{path}.lock");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task EnsureNotConflictedCoreAsync(CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        WorkspaceStatus status = await GetStatusCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        ThrowIfConflicted(status);
    }

    private static async Task EnsureNoExternalRepositoryOperationAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "rev-parse" };
        foreach (string operationRef in s_repositoryOperationRefs)
        {
            arguments.Add("--git-path");
            arguments.Add(operationRef);
        }

        GitCommandResult result = await runner.RunAsync(
            repository,
            arguments,
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        string stdout = result.Stdout.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!stdout.EndsWith('\n'))
        {
            throw new InvalidOperationException(
                "Git returned an invalid repository-operation path list.");
        }

        string[] paths = stdout[..^1].Split('\n');
        if (paths.Length != s_repositoryOperationRefs.Length
            || paths.Any(static path => path.Length == 0 || path.Any(char.IsControl)))
        {
            throw new InvalidOperationException(
                "Git returned an invalid repository-operation path list.");
        }

        foreach (string path in paths)
        {
            string fullPath = Path.GetFullPath(
                Path.IsPathFullyQualified(path)
                    ? path
                    : Path.Combine(repository.RepoRoot, path));
            if (RepositoryOperationPathExists(fullPath))
            {
                throw new VersionControlConflictedException(
                    Strings.VersionControl_ConflictGuidance);
            }
        }
    }

    private static bool RepositoryOperationPathExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"The Git repository-operation path '{path}' could not be inspected safely.",
                ex);
        }
    }

    private static void ThrowIfConflicted(WorkspaceStatus status)
    {
        if (status.HasConflicts)
        {
            throw new VersionControlConflictedException(Strings.VersionControl_ConflictGuidance);
        }
    }
}
