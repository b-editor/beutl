using Beutl.Language;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private async Task<bool> RevisionContainsProjectFileCoreAsync(
        string sha,
        string projectFile,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        string relativeProjectFile = GetRecoveryProjectFile(repository, projectFile);
        try
        {
            await runner.RunAsync(
                repository,
                ["cat-file", "-e", $"{sha}:{relativeProjectFile}"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 128)
        {
            return false;
        }
    }

    internal static WorkspaceStatus ParseStatus(string output)
    {
        string? branch = null;
        string? headCommit = null;
        bool isDetachedHead = false;
        int ahead = 0;
        int behind = 0;
        bool hasConflicts = false;
        var changes = new List<FileChange>();
        IReadOnlyList<string> records = GitCliRunner.SplitNullSeparated(output);

        for (int index = 0; index < records.Count; index++)
        {
            string record = records[index];
            if (record.StartsWith("# branch.oid ", StringComparison.Ordinal))
            {
                string oid = record["# branch.oid ".Length..];
                headCommit = oid == "(initial)" ? null : oid;
            }
            else if (record.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                string head = record["# branch.head ".Length..];
                isDetachedHead = head == "(detached)";
                branch = isDetachedHead ? null : head;
            }
            else if (record.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                string[] values = record["# branch.ab ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (string value in values)
                {
                    if (value.Length < 2)
                    {
                        continue;
                    }

                    if (value[0] == '+'
                        && int.TryParse(value.AsSpan(1), out int parsedAhead))
                    {
                        ahead = parsedAhead;
                    }
                    else if (value[0] == '-'
                             && int.TryParse(value.AsSpan(1), out int parsedBehind))
                    {
                        behind = parsedBehind;
                    }
                }
            }
            else if (record.StartsWith("1 ", StringComparison.Ordinal))
            {
                string statusCode = GetField(record, 1);
                string path = GetTailAfterSpaces(record, 8);
                changes.Add(new FileChange(path, MapStatus(statusCode)));
                hasConflicts |= statusCode.Contains('U');
            }
            else if (record.StartsWith("2 ", StringComparison.Ordinal))
            {
                string statusCode = GetField(record, 1);
                string renameOrCopy = GetField(record, 8);
                string path = GetTailAfterSpaces(record, 9);
                string? oldPath = ++index < records.Count ? records[index] : null;
                changes.Add(renameOrCopy.StartsWith('C')
                    ? new FileChange(path, FileChangeStatus.Added)
                    : new FileChange(path, FileChangeStatus.Renamed, oldPath));
                hasConflicts |= statusCode.Contains('U');
            }
            else if (record.StartsWith("u ", StringComparison.Ordinal))
            {
                string path = GetTailAfterSpaces(record, 10);
                changes.Add(new FileChange(path, FileChangeStatus.Modified));
                hasConflicts = true;
            }
            else if (record.StartsWith("? ", StringComparison.Ordinal))
            {
                changes.Add(new FileChange(record[2..], FileChangeStatus.Added));
            }
        }

        return new WorkspaceStatus(branch, ahead, behind, changes, hasConflicts, isDetachedHead)
        {
            HeadCommit = headCommit,
        };
    }

    internal static IReadOnlyList<CommitInfo> ParseHistory(string output)
    {
        string[] fields = output.Split('\0');
        var commits = new List<CommitInfo>(fields.Length / 6);
        int index = 0;
        while (index + 5 < fields.Length)
        {
            if (!DateTimeOffset.TryParse(
                    fields[index + 3],
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out DateTimeOffset authorDate))
            {
                break;
            }

            commits.Add(new CommitInfo(
                fields[index],
                fields[index + 1],
                fields[index + 4],
                fields[index + 2],
                authorDate,
                ParseSnapshotKind(fields[index + 5])));
            index += 6;
            while (index < fields.Length && fields[index].Length == 0)
            {
                index++;
            }
        }

        return commits;
    }

    internal static IReadOnlyList<FileChange> ParseCommitFiles(string output)
    {
        IReadOnlyList<string> fields = GitCliRunner.SplitNullSeparated(output);
        var changes = new List<FileChange>();
        for (int index = 0; index < fields.Count;)
        {
            string status = fields[index++].Trim();
            if (status.Length == 0 || index >= fields.Count)
            {
                break;
            }

            char statusCode = status[0];
            if (statusCode is 'R' or 'C')
            {
                if (index + 1 >= fields.Count)
                {
                    break;
                }

                string oldPath = fields[index++];
                string path = fields[index++];
                changes.Add(statusCode == 'C'
                    ? new FileChange(path, FileChangeStatus.Added)
                    : new FileChange(path, FileChangeStatus.Renamed, oldPath));
            }
            else
            {
                string path = fields[index++];
                changes.Add(new FileChange(path, MapNameStatus(statusCode)));
            }
        }

        return changes;
    }

    internal static IReadOnlyList<BranchInfo> ParseBranches(string output)
    {
        var branches = new List<BranchInfo>();
        foreach (string record in output
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Split('\0');
            if (fields.Length < 3 || string.IsNullOrWhiteSpace(fields[0]))
            {
                continue;
            }

            string upstream = fields[2].Trim();
            branches.Add(new BranchInfo(
                fields[0],
                fields[1].Trim() == "*",
                string.IsNullOrEmpty(upstream) ? null : upstream));
        }

        return branches;
    }

    // Branches that so far exist only on origin, the one remote Beutl works with, which git switch also
    // offers to check out. origin/HEAD only names the remote's default branch.
    internal static IReadOnlyList<BranchInfo> ParseOriginOnlyBranches(
        string output,
        IReadOnlyList<BranchInfo> localBranches)
    {
        var branches = new List<BranchInfo>();
        foreach (string record in output
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Split('\0');
            if (fields.Length < 2
                || fields[1].Length > 0
                || !fields[0].StartsWith(OriginRefPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string name = fields[0][OriginRefPrefix.Length..];
            if (name.Length > 0 && !ContainsLocalBranch(localBranches, name))
            {
                branches.Add(new BranchInfo(name, IsCurrent: false, UpstreamName: null, IsRemote: true));
            }
        }

        return branches;
    }

    private static string GetField(string record, int fieldIndex)
    {
        string[] fields = record.Split(' ', fieldIndex + 2, StringSplitOptions.None);
        return fields.Length > fieldIndex ? fields[fieldIndex] : string.Empty;
    }

    private static string GetTailAfterSpaces(string record, int spaces)
    {
        int position = -1;
        for (int index = 0; index < spaces; index++)
        {
            position = record.IndexOf(' ', position + 1);
            if (position < 0)
            {
                return string.Empty;
            }
        }

        return record[(position + 1)..];
    }

    private static FileChangeStatus MapStatus(string statusCode)
    {
        if (statusCode.Contains('R'))
        {
            return FileChangeStatus.Renamed;
        }

        if (statusCode.Contains('C'))
        {
            return FileChangeStatus.Added;
        }

        if (statusCode.Contains('D'))
        {
            return FileChangeStatus.Deleted;
        }

        if (statusCode.Contains('A') || statusCode == "??")
        {
            return FileChangeStatus.Added;
        }

        return FileChangeStatus.Modified;
    }

    private async Task<WorkspaceStatus> GetStatusCoreAsync(CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        WorkspaceStatus status = await GetStatusCoreAsync(
                repository,
                runner,
                cancellationToken,
                extraPathspecs: null)
            .ConfigureAwait(false);
        if (status.Branch is null
            || (await GetRemotesCoreAsync(cancellationToken).ConfigureAwait(false)).Count == 0)
        {
            return status;
        }

        // Counts stay against origin even when the branch tracks a different remote, but the origin
        // branch is whichever one this branch actually tracks: synthesizing it from the local name
        // answers for an unrelated branch whenever the two names differ.
        string? upstream = await TryGetUpstreamRefAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (upstream is not null && upstream.StartsWith(OriginRefPrefix, StringComparison.Ordinal))
        {
            // Porcelain status already counted against this upstream. Avoid starting two more
            // processes and walking the same history again on every background refresh.
            return status;
        }

        string originBranchRef = $"{OriginRefPrefix}{status.Branch}";
        if (!await RefExistsAsync(repository, runner, originBranchRef, cancellationToken)
                .ConfigureAwait(false))
        {
            return status with { Ahead = 0, Behind = 0 };
        }

        GitCommandResult counts = await runner.RunAsync(
            repository,
            ["rev-list", "--left-right", "--count", $"HEAD...{originBranchRef}"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        (int ahead, int behind) = ParseAheadBehindCounts(counts.Stdout);
        return status with { Ahead = ahead, Behind = behind };
    }

    private static async Task<string?> TryGetUpstreamRefAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["rev-parse", "--symbolic-full-name", "@{upstream}"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string upstream = result.Stdout.Trim();
            return upstream.Length == 0 ? null : upstream;
        }
        catch (GitOperationException)
        {
            // No upstream configured, which git reports as a failure rather than empty output.
            return null;
        }
    }

    // Verified rather than matched: for-each-ref treats its operand as a pattern, so asking for
    // refs/remotes/origin/foo also succeeds when only refs/remotes/origin/foo/bar exists.
    private static async Task<bool> RefExistsAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string refName,
        CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(
                repository,
                ["show-ref", "--verify", "--quiet", refName],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (GitOperationException)
        {
            return false;
        }
    }

    private async Task<WorkspaceStatus> GetSnapshotStatusCoreAsync(
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        return await GetStatusCoreAsync(
                repository,
                runner,
                cancellationToken,
                CreateSnapshotExcludePathspecs(repository))
            .ConfigureAwait(false);
    }

    private static async Task<WorkspaceStatus> GetStatusCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? extraPathspecs = null)
    {
        string projectPathspec = extraPathspecs is null
            ? repository.Pathspec
            : CreateSnapshotBasePathspec(repository);
        var arguments = new List<string>
        {
            "status",
            "--porcelain=v2",
            "--branch",
            "--ahead-behind",
            "--untracked-files=all",
            "-z",
            "--",
            projectPathspec,
        };
        if (extraPathspecs is not null)
        {
            arguments.AddRange(extraPathspecs);
        }

        GitCommandResult result = await runner.RunAsync(
            repository,
            arguments,
            new GitCommandOptions(
                GitCommandExecutionKind.Local,
                UseLiteralPathspecs: extraPathspecs is null),
            cancellationToken).ConfigureAwait(false);
        WorkspaceStatus status = ParseStatus(result.Stdout);
        if (!repository.IsNestedInForeignRepo || status.HasConflicts)
        {
            return status;
        }

        GitCommandResult unmerged = await runner.RunAsync(
            repository,
            ["ls-files", "--unmerged"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(unmerged.Stdout)
            ? status
            : status with { HasConflicts = true };
    }

    private async Task<IReadOnlyList<CommitInfo>> GetHistoryCoreAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "-c",
                "trailer.separators=:=",
                "log",
                "--no-show-signature",
                "--format=%H%x00%h%x00%an%x00%aI%x00%s%x00%(trailers:key=Beutl-Snapshot,valueonly)%x00",
                "-z",
                $"--skip={skip}",
                "-n",
                take.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--",
                repository.Pathspec,
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return ParseHistory(result.Stdout);
    }

    private async Task<IReadOnlyList<FileChange>> GetCommitFilesCoreAsync(
        string sha,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "show",
                "--no-show-signature",
                "--first-parent",
                "--name-status",
                "--format=",
                "-z",
                sha,
                "--",
                repository.Pathspec,
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return ParseCommitFiles(result.Stdout);
    }

    private async Task<string> GetDiffCoreAsync(
        string sha,
        string? path,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        string pathspec = path is null
            ? repository.Pathspec
            : ValidateDiffPath(repository, path);
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "show",
                "--no-show-signature",
                "--first-parent",
                "--no-color",
                "--format=",
                "--no-ext-diff",
                "--unified=3",
                sha,
                "--",
                pathspec,
            ],
            GitCommandOptions.Local with { MaxStdoutBytes = MaxDiffBytes },
            cancellationToken).ConfigureAwait(false);
        return result.StdoutTruncated
            ? string.Concat(result.Stdout, DiffTruncationMarker)
            : result.Stdout;
    }

    private async Task<IReadOnlyList<BranchInfo>> GetBranchesCoreAsync(
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        GitCommandResult result = await runner.RunAsync(
            repository,
            ["for-each-ref", "--format=%(refname)%00%(HEAD)%00%(upstream:lstrip=2)%00%(symref)",
                "refs/heads/", OriginRefPrefix],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        var locals = new List<BranchInfo>();
        var remotes = new List<string>();
        foreach (string record in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.TrimEnd('\r').Split('\0');
            if (fields.Length != 4)
            {
                continue;
            }
            if (fields[0].StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                locals.Add(new BranchInfo(fields[0]["refs/heads/".Length..], fields[1].Trim() == "*",
                    string.IsNullOrEmpty(fields[2]) ? null : fields[2]));
            }
            else if (fields[0].StartsWith(OriginRefPrefix, StringComparison.Ordinal) && fields[3].Length == 0)
            {
                remotes.Add(fields[0][OriginRefPrefix.Length..]);
            }
        }

        var localNames = locals.Select(static branch => branch.Name).ToHashSet(StringComparer.Ordinal);
        return [.. locals, .. remotes.Where(name => name.Length > 0 && !localNames.Contains(name))
            .Select(static name => new BranchInfo(name, false, null, IsRemote: true))];
    }

    private async Task<IReadOnlyList<BranchInfo>> GetLocalBranchesCoreAsync(
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "for-each-ref",
                "--format=%(refname:lstrip=2)%00%(HEAD)%00%(upstream:lstrip=2)",
                "refs/heads",
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return ParseBranches(result.Stdout);
    }

    private async Task<IReadOnlyList<RemoteInfo>> GetRemotesCoreAsync(
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["remote", "get-url", "origin"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string url = result.Stdout.Trim();
            return string.IsNullOrEmpty(url) ? [] : [new RemoteInfo("origin", url)];
        }
        catch (GitOperationException ex) when (IsMissingRemoteFailure(ex))
        {
            return [];
        }
    }

    private static SnapshotKind ParseSnapshotKind(string trailer)
    {
        string[] values = trailer.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length != 1)
        {
            return SnapshotKind.Manual;
        }

        return values[0].ToLowerInvariant() switch
        {
            "manual" => SnapshotKind.Manual,
            "save" => SnapshotKind.Save,
            "close" => SnapshotKind.Close,
            "safety" => SnapshotKind.Safety,
            "restore" => SnapshotKind.Restore,
            "recovery" => SnapshotKind.Recovery,
            "init" => SnapshotKind.Init,
            _ => SnapshotKind.Manual,
        };
    }

    private static bool IsNotRepositoryFailure(GitOperationException exception)
    {
        return exception.Stderr.Contains(
                   "not a git repository",
                   StringComparison.OrdinalIgnoreCase)
               || exception.Stderr.Contains(
                   "not in a git directory",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingRemoteFailure(GitOperationException exception)
    {
        return exception.Stderr.Contains(
                   "No such remote",
                   StringComparison.OrdinalIgnoreCase)
               || exception.Stderr.Contains(
                   "does not appear to be a git repository",
                   StringComparison.OrdinalIgnoreCase);
    }

    internal static RemoteOpResult MapRemoteFailure(GitOperationException exception)
    {
        string stderr = exception.Stderr;
        if (ContainsAny(
                stderr,
                "non-fast-forward",
                "not possible to fast-forward",
                "fetch first",
                "divergent branches",
                "[rejected]"))
        {
            return new RemoteOpResult.Diverged();
        }

        if (ContainsAny(
                stderr,
                "authentication failed",
                "permission denied",
                "could not read username",
                "publickey",
                "access denied",
                "authorization failed"))
        {
            return new RemoteOpResult.AuthFailed(Strings.VersionControl_AuthenticationFailed);
        }

        if (ContainsAny(
                stderr,
                "could not resolve host",
                "failed to connect",
                "network is unreachable",
                "connection timed out",
                "connection refused",
                "could not read from remote repository"))
        {
            return new RemoteOpResult.Offline();
        }

        return new RemoteOpResult.Failed(stderr);
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (value.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static FileChangeStatus MapNameStatus(char status)
    {
        return status switch
        {
            'A' => FileChangeStatus.Added,
            'D' => FileChangeStatus.Deleted,
            _ => FileChangeStatus.Modified,
        };
    }

    private static string ValidateDiffPath(RepositoryInfo repository, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = NormalizeGitPath(path);
        if (Path.IsPathFullyQualified(path)
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Split('/').Any(static segment => segment == ".."))
        {
            throw new ArgumentException("The diff path must be repository-relative.", nameof(path));
        }

        if (repository.Pathspec != "."
            && !string.Equals(normalized, repository.Pathspec, StringComparison.Ordinal)
            && !normalized.StartsWith($"{repository.Pathspec}/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The diff path must be inside the project pathspec.",
                nameof(path));
        }

        return normalized;
    }

    private static (int Ahead, int Behind) ParseAheadBehindCounts(string output)
    {
        string[] values = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (values.Length != 2
            || !int.TryParse(values[0], out int ahead)
            || !int.TryParse(values[1], out int behind))
        {
            throw new InvalidOperationException("Git returned invalid ahead/behind counts.");
        }

        return (ahead, behind);
    }
}
