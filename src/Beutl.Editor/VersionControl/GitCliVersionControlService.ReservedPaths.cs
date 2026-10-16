namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private const string ReservedPathCleanupMessage = "beutl: stop tracking reserved project state";

    private static bool IsReservedProjectPath(RepositoryInfo repository, string repositoryRelativePath)
    {
        // Only the part inside the project decides. A .beutl folder above the project in an enclosing
        // repository is not Beutl state.
        string projectRelativePath = repository.Pathspec != "."
                                     && repositoryRelativePath.StartsWith(
                                         repository.Pathspec + "/",
                                         StringComparison.Ordinal)
            ? repositoryRelativePath[(repository.Pathspec.Length + 1)..]
            : repositoryRelativePath;
        return ProjectPathRules.IsReserved(projectRelativePath);
    }

    private async Task<IReadOnlyList<string>> GetTrackedReservedPathsCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult listed = await runner.RunAsync(
            repository,
            ["ls-files", "-z", "--", CreateSnapshotBasePathspec(repository)],
            new GitCommandOptions(GitCommandExecutionKind.Local) { UseLiteralPathspecs = false },
            cancellationToken).ConfigureAwait(false);
        return listed.Stdout
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => IsReservedProjectPath(repository, path))
            .ToArray();
    }

    // .gitignore never untracks what is already tracked, so the pull precondition would never be met.
    // Build a commit removing these paths from HEAD in a temporary index (the files stay on disk), and
    // move the branch with update-ref only while HEAD still names the commit it was built on.
    private async Task TryUntrackReservedPathsCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        IReadOnlyList<string> reservedPaths,
        CancellationToken cancellationToken)
    {
        string? temporaryIndex = null;
        try
        {
            CheckedOutBranchTip head = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);

            temporaryIndex = CreateUniqueTempPath("beutl-git-index");
            GitCommandOptions indexOptions = CreateTemporaryIndexOptions(temporaryIndex);
            await runner.RunAsync(
                    repository,
                    ["read-tree", head.Commit],
                    indexOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            await runner.RunAsync(
                    repository,
                    ["update-index", "--force-remove", "--", .. reservedPaths],
                    indexOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            GitCommandResult cleanupTree = await runner.RunAsync(
                    repository,
                    ["write-tree"],
                    indexOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            string headTree = await ResolveTreeAsync(repository, runner, head.Commit, cancellationToken)
                .ConfigureAwait(false);

            // Paths that are only staged additions leave HEAD's tree as it is. They belong to whoever
            // staged them, so neither the branch nor the index changes.
            if (string.Equals(cleanupTree.Stdout.Trim(), headTree, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            GitCommandResult cleanupCommit = await runner.RunAsync(
                    repository,
                    [
                        "commit-tree",
                        "--no-gpg-sign",
                        cleanupTree.Stdout.Trim(),
                        "-p",
                        head.Commit,
                        "-m",
                        ReservedPathCleanupMessage,
                        "-m",
                        "Beutl-Snapshot: init",
                    ],
                    GitCommandOptions.Local,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await runner.RunAsync(
                    repository,
                    [
                        "update-ref",
                        "-m",
                        ReservedPathCleanupMessage,
                        "HEAD",
                        cleanupCommit.Stdout.Trim(),
                        head.Commit,
                    ],
                    GitCommandOptions.Local,
                    CancellationToken.None)
                .ConfigureAwait(false);

            // These files usually differ on disk from the index, and once HEAD no longer holds them
            // git rm refuses to drop such a file without -f. --cached leaves every file on disk.
            await runner.RunAsync(
                    repository,
                    ["rm", "-r", "--cached", "-f", "-q", "--ignore-unmatch", "--", .. reservedPaths],
                    GitCommandOptions.Local,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Everything before update-ref happens in the temporary index, so cancellation leaves
            // both the live index and the branch untouched.
            throw;
        }
        catch (GitOperationException ex) when (ex.IsRepositoryLockFailure)
        {
            // Preserve lock failures for the serialized-operation boundary, which records the
            // recoverable lock instead of silently leaving a stale HEAD.lock/index.lock behind.
            throw;
        }
        catch (Exception ex)
        {
            LogWarningBestEffort(
                ex,
                "Could not stop tracking reserved project paths; pulls will report the repository dirty until they are untracked manually.");
        }
        finally
        {
            if (temporaryIndex is not null)
            {
                TryDeleteTemporaryIndex(temporaryIndex);
            }
        }
    }
}
