using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private sealed record LfsAttributeQueryResult(
        HashSet<string> CoveredPaths,
        bool IsComplete);

    private sealed record LfsPrefetchTarget(
        string Reference,
        IReadOnlyList<string> PathArguments);

    private async Task PrefetchBranchLfsObjectsCoreAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ValidateSwitchBranchName(name);
        IReadOnlyList<BranchInfo> branches = await GetBranchesCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        // A branch only origin has so far is checked out from its remote-tracking ref.
        string reference = !ContainsLocalBranch(branches, name) && ContainsOriginOnlyBranch(branches, name)
            ? $"{OriginRefPrefix}{name}"
            : name;
        await PrefetchLfsObjectsCoreAsync(
                reference,
                LfsPrefetchScope.RepositoryWide,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Task PrefetchCommitLfsObjectsCoreAsync(
        string sha,
        LfsPrefetchScope scope,
        CancellationToken cancellationToken)
    {
        GitRevisionValidator.ValidateCommitId(sha, nameof(sha));
        return PrefetchLfsObjectsCoreAsync(sha, scope, cancellationToken);
    }

    private async Task PrefetchLfsObjectsCoreAsync(
        string reference,
        LfsPrefetchScope scope,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        (GitAvailability availability, IGitCliRunner? runner) = await GetGitRuntimeCoreAsync(
                cancellationToken)
            .ConfigureAwait(false);
        if (availability.State != GitAvailabilityState.Installed || runner is null)
        {
            throw new InvalidOperationException("Git is not available.");
        }

        if (!availability.LfsInstalled)
        {
            if (await TargetContainsLfsPointerAsync(
                    repository,
                    runner,
                    reference,
                    scope,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "The target revision contains Git LFS files, but Git LFS is not installed. Install Git LFS before switching or restoring this revision.");
            }

            return;
        }

        LfsPrefetchTarget target = await GetLfsPrefetchTargetAsync(
                repository,
                runner,
                reference,
                scope,
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<RemoteInfo> remotes = await GetRemotesCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        if (remotes.Count == 0)
        {
            if (await HasUncachedLfsObjectsAsync(
                    repository,
                    runner,
                    target.Reference,
                    target.PathArguments,
                    cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "The transition requires Git LFS objects that are missing or corrupt, and no remote is configured.");
            }

            return;
        }

        try
        {
            await runner.RunAsync(
                repository,
                [
                    .. s_lfsPathFilterOverrides,
                    "-c",
                    "lfs.fetchrecentalways=false",
                    "lfs",
                    "fetch",
                    .. target.PathArguments,
                    remotes[0].Name,
                    target.Reference,
                ],
                GitCommandOptions.Network with
                {
                    MaxStdoutBytes = MaxLfsFetchOutputBytes,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException ex)
        {
            // The objects may already be cached, so an unreachable endpoint must not turn an
            // otherwise working transition into an error. What it must not do is let the caller
            // close the project and leave the checkout's own smudge filter to download the missing
            // content uncancellably, so the failure is only absorbed when the target needs nothing
            // that is not already in the local object store.
            if (await HasUncachedLfsObjectsAsync(
                    repository,
                    runner,
                    target.Reference,
                    target.PathArguments,
                    cancellationToken)
                    .ConfigureAwait(false))
            {
                throw;
            }

            _logger.LogWarning(
                ex,
                "Could not prefetch Git LFS objects for '{Reference}', but every object it needs is already cached.",
                reference);
        }
    }

    private static async Task<bool> TargetContainsLfsPointerAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string reference,
        LfsPrefetchScope scope,
        CancellationToken cancellationToken)
    {
        var grepArguments = new List<string>
        {
            "grep",
            "-l",
            "-z",
            "--full-name",
            "--no-textconv",
            "--no-ext-grep",
            "-F",
            "-e",
            "version https://git-lfs.github.com/spec/v1",
            "-e",
            "version http://git-media.io/v/2",
            "-e",
            "version https://hawser.github.com/spec/v1",
            reference,
        };
        if (scope == LfsPrefetchScope.ProjectPathspec && repository.Pathspec != ".")
        {
            grepArguments.Add("--");
            grepArguments.Add(repository.Pathspec);
        }
        else if (scope != LfsPrefetchScope.RepositoryWide
                 && scope != LfsPrefetchScope.ProjectPathspec)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        GitCommandResult candidates;
        try
        {
            candidates = await runner.RunAsync(
                    repository,
                    grepArguments,
                    GitCommandOptions.Local with
                    {
                        MaxStdoutBytes = MaxLfsObjectListOutputBytes,
                        CaptureStdoutBytes = true,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return false;
        }

        if (candidates.StdoutTruncated)
        {
            throw new InvalidOperationException(
                "The target revision contains too many possible Git LFS pointer files to inspect safely.");
        }

        string candidateOutput;
        try
        {
            candidateOutput = new UTF8Encoding(false, true).GetString(
                candidates.StdoutBytes
                ?? throw new InvalidOperationException(
                    "Git did not return the Git LFS pointer candidate paths."));
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidOperationException(
                "Git returned a non-UTF-8 Git LFS pointer candidate path.",
                ex);
        }

        IReadOnlyList<string> records = GitCliRunner.SplitNullSeparated(candidateOutput);
        if (records.Count > MaxLfsPointerCandidates)
        {
            throw new InvalidOperationException(
                "The target revision contains too many possible Git LFS pointer files to inspect safely.");
        }

        string expectedPrefix = reference + ":";
        foreach (string record in records)
        {
            if (!record.StartsWith(expectedPrefix, StringComparison.Ordinal)
                || record.Length == expectedPrefix.Length)
            {
                throw new InvalidOperationException(
                    "Git returned an invalid Git LFS pointer candidate path.");
            }

            string objectExpression = reference + ":" + record[expectedPrefix.Length..];
            GitCommandResult blob = await runner.RunAsync(
                    repository,
                    ["cat-file", "blob", objectExpression],
                    GitCommandOptions.Local with
                    {
                        MaxStdoutBytes = GitLfsFormat.MaxPointerBytes,
                        CaptureStdoutBytes = true,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (GitLfsFormat.IsLfsPointer(blob.StdoutBytes
                                                   ?? throw new InvalidOperationException(
                                                       "Git did not return the Git LFS pointer candidate bytes.")))
            {
                return true;
            }
        }

        return false;
    }

    // Fails safe: anything that stops this from proving the objects are present - an unreadable
    // listing, an unknown storage layout, an unparsable line - counts as uncached, so the caller
    // aborts while it still can instead of closing the project first.
    private async Task<bool> HasUncachedLfsObjectsAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string reference,
        IReadOnlyList<string> lfsPathArguments,
        CancellationToken cancellationToken)
    {
        string storage;
        try
        {
            storage = await GetLfsObjectStorageAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitOperationException ex)
        {
            LogWarningBestEffort(
                ex,
                "Could not locate the Git LFS object storage required by a transition target.");
            return true;
        }

        GitCommandResult listed;
        IReadOnlyList<string> requiredObjects;
        try
        {
            listed = await runner.RunAsync(
                repository,
                [
                    .. s_lfsPathFilterOverrides,
                    "lfs",
                    "ls-files",
                    "--long",
                    "--json",
                    .. lfsPathArguments,
                    reference,
                ],
                GitCommandOptions.Local with
                {
                    MaxStdoutBytes = MaxLfsObjectListOutputBytes,
                },
                cancellationToken).ConfigureAwait(false);

            if (listed.StdoutTruncated
                || !GitLfsFormat.TryParseCanonicalLfsObjectList(listed.Stdout, out requiredObjects))
            {
                return true;
            }
        }
        catch (GitOperationException jsonFailure)
        {
            // --json was added in Git LFS 3.2. Older clients can still prove their cache from the
            // legacy listing: only the OID prefix is a record boundary, so embedded filename
            // newlines are continuations rather than malformed records.
            try
            {
                listed = await runner.RunAsync(
                    repository,
                    [
                        .. s_lfsPathFilterOverrides,
                        "lfs",
                        "ls-files",
                        "--long",
                        .. lfsPathArguments,
                        reference,
                    ],
                    GitCommandOptions.Local with
                    {
                        MaxStdoutBytes = MaxLfsObjectListOutputBytes,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (GitOperationException legacyFailure)
            {
                LogWarningBestEffort(
                    new AggregateException(jsonFailure, legacyFailure),
                    "Could not list the Git LFS objects required by a transition target.");
                return true;
            }

            if (listed.StdoutTruncated
                || !GitLfsFormat.TryParseCanonicalLfsObjectLines(listed.Stdout, out requiredObjects))
            {
                return true;
            }
        }

        var verifiedObjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (string oid in requiredObjects)
        {
            if (verifiedObjects.Add(oid)
                && !await IsCachedLfsObjectValidAsync(
                        storage,
                        oid,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> IsCachedLfsObjectValidAsync(
        string storage,
        string oid,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(storage, oid[..2], oid[2..4], oid);
        try
        {
            await using var stream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });
            using SHA256 sha256 = SHA256.Create();
            byte[] actual = await sha256.ComputeHashAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            byte[] expected = Convert.FromHexString(oid);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<LfsPrefetchTarget> GetLfsPrefetchTargetAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string reference,
        LfsPrefetchScope scope,
        CancellationToken cancellationToken)
    {
        if (scope == LfsPrefetchScope.RepositoryWide
            || (scope == LfsPrefetchScope.ProjectPathspec && repository.Pathspec == "."))
        {
            return new LfsPrefetchTarget(reference, []);
        }

        if (scope != LfsPrefetchScope.ProjectPathspec)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        // Git LFS parses --include as a comma-separated list of gitignore globs, not as a
        // literal Git pathspec. Use it only when every character is literal in that grammar.
        // For any other legal repository path, resolve the exact subtree with Git's literal
        // pathspec handling and let LFS scan that tree object without a path filter.
        if (repository.Pathspec.All(static character =>
                character is >= 'a' and <= 'z'
                    or >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '/' or '.' or '_' or '-'))
        {
            return new LfsPrefetchTarget(
                reference,
                [
                    $"--include={repository.Pathspec}/**",
                    "--exclude=",
                ]);
        }

        GitCommandResult tree = await runner.RunAsync(
                repository,
                [
                    "ls-tree",
                    "-d",
                    "-z",
                    "--format=%(objectname)",
                    reference,
                    "--",
                    repository.Pathspec,
                ],
                GitCommandOptions.Local with
                {
                    MaxStdoutBytes = 128,
                },
                cancellationToken)
            .ConfigureAwait(false);
        string[] objectIds = GitCliRunner.SplitNullSeparated(tree.Stdout).ToArray();
        if (tree.StdoutTruncated || objectIds.Length != 1)
        {
            throw new InvalidOperationException(
                "The target revision does not contain exactly one project subtree for Git LFS prefetch.");
        }

        string treeId = objectIds[0];
        GitRevisionValidator.ValidateCommitId(treeId, nameof(treeId));
        return new LfsPrefetchTarget(treeId, []);
    }

    private static async Task<string> GetLfsObjectStorageAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult gitDirectory = await runner.RunAsync(
                repository,
                ["rev-parse", "--git-common-dir"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        string commonDirectory = gitDirectory.Stdout.Trim();
        if (commonDirectory.Length == 0)
        {
            throw new InvalidOperationException("Git returned an empty common directory.");
        }

        string root = Path.GetFullPath(
            Path.IsPathFullyQualified(commonDirectory)
                ? commonDirectory
                : Path.Combine(repository.RepoRoot, commonDirectory));
        GitCommandResult configured = await runner.RunAsync(
            repository,
            ["config", "--get", "--default", "", "lfs.storage"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        string storage = configured.Stdout.Trim();
        if (storage.Length == 0)
        {
            return Path.Combine(root, "lfs", "objects");
        }

        string storageRoot = Path.IsPathFullyQualified(storage)
            ? storage
            : Path.Combine(root, storage);
        return Path.Combine(storageRoot, "objects");
    }

    private async Task<bool> TryInstallLfsLocallyAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        // A repository whose LFS filters and hooks are already in place, from its own setup or a
        // machine-wide git lfs install, needs nothing written to it.
        if (await IsLfsInstalledForRepositoryAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            return true;
        }

        try
        {
            await runner.RunAsync(
                repository,
                ["lfs", "install", "--local"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GitOperationException ex)
        {
            LogWarningBestEffort(
                ex,
                "Git LFS could not be enabled locally; continuing without Beutl-managed LFS rules.");
            await RaiseLfsInstallFailedNoticeIfNeededAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            return false;
        }
    }

    private static async Task<bool> IsLfsInstalledForRepositoryAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult filters;
        try
        {
            filters = await runner.RunAsync(
                repository,
                ["config", "--get-regexp", "^filter\\.lfs\\."],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return false;
        }

        // A key alone does not run Git LFS: an empty or unrelated command leaves content unfiltered.
        // Git uses the last value listed for a key.
        var configuredFilters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in filters.Stdout.Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] fields = line.Split(' ', 2);
            configuredFilters[fields[0]] = fields.Length > 1 ? fields[1] : string.Empty;
        }

        if (!RunsLfsCommand(configuredFilters, "filter.lfs.clean", "git-lfs clean")
            || !RunsLfsCommand(configuredFilters, "filter.lfs.smudge", "git-lfs smudge")
            || !RunsLfsCommand(configuredFilters, "filter.lfs.process", "git-lfs filter-process"))
        {
            return false;
        }

        GitCommandResult hooksRecord = await runner.RunAsync(
            repository,
            ["rev-parse", "--git-path", "hooks"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        string hooksPath = hooksRecord.Stdout.TrimEnd('\r', '\n');
        string hooksDirectory = Path.GetFullPath(
            Path.IsPathFullyQualified(hooksPath)
                ? hooksPath
                : Path.Combine(repository.RepoRoot, hooksPath));
        foreach (string hook in s_lfsHookNames)
        {
            string hookPath = Path.Combine(hooksDirectory, hook);
            string contents;
            try
            {
                // Git skips a hook it cannot execute, so the text of one proves nothing.
                if (!OperatingSystem.IsWindows()
                    && (File.GetUnixFileMode(hookPath) & UnixFileMode.UserExecute) == 0)
                {
                    return false;
                }

                contents = await File.ReadAllTextAsync(hookPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }

            // Only a command runs; the same text in a comment does nothing.
            string command = $"git lfs {hook}";
            if (!contents.Split('\n').Any(line =>
                    !line.TrimStart().StartsWith('#')
                    && line.Contains(command, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RunsLfsCommand(
        IReadOnlyDictionary<string, string> configuredFilters,
        string key,
        string command)
    {
        return configuredFilters.TryGetValue(key, out string? value)
               && value.Contains(command, StringComparison.Ordinal);
    }

    internal static async Task<HashSet<string>> GetEffectiveLfsPathsAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        IReadOnlyList<string> repoRelativePaths,
        CancellationToken cancellationToken)
    {
        var coveredPaths = new HashSet<string>(StringComparer.Ordinal);
        var chunk = new List<string>();
        int expectedOutputBytes = 0;
        foreach (string path in repoRelativePaths)
        {
            int pathOutputBytes = Encoding.UTF8.GetByteCount(path) + 20;
            if (chunk.Count > 0
                && pathOutputBytes > MaxLfsAttributeOutputBytes - expectedOutputBytes)
            {
                LfsAttributeQueryResult result = await QueryEffectiveLfsPathsAsync(
                        repository,
                        runner,
                        chunk,
                        cancellationToken)
                    .ConfigureAwait(false);
                coveredPaths.UnionWith(result.CoveredPaths);
                if (!result.IsComplete)
                {
                    return coveredPaths;
                }

                chunk.Clear();
                expectedOutputBytes = 0;
            }

            chunk.Add(path);
            expectedOutputBytes = pathOutputBytes > MaxLfsAttributeOutputBytes - expectedOutputBytes
                ? MaxLfsAttributeOutputBytes
                : expectedOutputBytes + pathOutputBytes;
        }

        if (chunk.Count > 0)
        {
            LfsAttributeQueryResult result = await QueryEffectiveLfsPathsAsync(
                    repository,
                    runner,
                    chunk,
                    cancellationToken)
                .ConfigureAwait(false);
            coveredPaths.UnionWith(result.CoveredPaths);
        }

        return coveredPaths;
    }

    private static async Task<LfsAttributeQueryResult> QueryEffectiveLfsPathsAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        IReadOnlyList<string> repoRelativePaths,
        CancellationToken cancellationToken)
    {
        var standardInput = new StringBuilder();
        foreach (string path in repoRelativePaths)
        {
            standardInput.Append(path).Append('\0');
        }

        GitCommandResult result;
        try
        {
            result = await runner.RunAsync(
                repository,
                ["check-attr", "--stdin", "-z", "filter"],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    MaxStdoutBytes: MaxLfsAttributeOutputBytes,
                    StandardInput: standardInput.ToString()),
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException)
        {
            return new([], false);
        }
        catch (TimeoutException)
        {
            return new([], false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new([], false);
        }

        if (result.ExitCode != 0 || result.Stderr.Length != 0)
        {
            return new([], false);
        }

        var coveredPaths = new HashSet<string>(StringComparer.Ordinal);
        int position = 0;
        for (int i = 0; i < repoRelativePaths.Count; i++)
        {
            if (!TryReadNullTerminatedField(result.Stdout, ref position, out string path)
                || !TryReadNullTerminatedField(result.Stdout, ref position, out string attribute)
                || !TryReadNullTerminatedField(result.Stdout, ref position, out string value))
            {
                return new(coveredPaths, false);
            }

            if (!string.Equals(path, repoRelativePaths[i], StringComparison.Ordinal)
                || !string.Equals(attribute, "filter", StringComparison.Ordinal))
            {
                return new(coveredPaths, false);
            }

            if (string.Equals(value, "lfs", StringComparison.Ordinal))
            {
                coveredPaths.Add(repoRelativePaths[i]);
            }
        }

        bool isComplete = !result.StdoutTruncated && position == result.Stdout.Length;
        return isComplete
            ? new(coveredPaths, true)
            : new([], false);
    }

    private static bool TryReadNullTerminatedField(
        string value,
        ref int position,
        out string field)
    {
        int end = value.IndexOf('\0', position);
        if (end < 0)
        {
            field = string.Empty;
            return false;
        }

        field = value[position..end];
        position = end + 1;
        return true;
    }

    private async Task<bool> IsLfsActiveAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        (GitAvailability availability, _) = await GetGitRuntimeCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!availability.LfsInstalled)
        {
            return false;
        }

        string prefix = GetProjectPathPrefix(repository);
        // A placeholder per media type in the folder Beutl imports media into shows whether media
        // would go through LFS; the media the project already tracks covers rules scoped elsewhere.
        GitCommandResult tracked = await runner.RunAsync(
                repository,
                ["ls-files", "-z", "--", CreateSnapshotBasePathspec(repository)],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    MaxStdoutBytes: MaxSnapshotTreeInspectionBytes,
                    UseLiteralPathspecs: false),
                cancellationToken)
            .ConfigureAwait(false);
        string trackedListing = tracked.StdoutTruncated
            ? tracked.Stdout[..(tracked.Stdout.LastIndexOf('\0') + 1)]
            : tracked.Stdout;
        string[] mediaPaths = s_mediaExtensions
            .Select(extension => $"{prefix}resources/beutl-required-media{extension}")
            .Concat(GitCliRunner.SplitNullSeparated(trackedListing)
                .Where(static path => s_mediaExtensions.Contains(Path.GetExtension(path))))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        HashSet<string> coveredPaths = await GetEffectiveLfsPathsAsync(
                repository,
                runner,
                mediaPaths,
                cancellationToken)
            .ConfigureAwait(false);
        return coveredPaths.Count > 0;
    }
}
