using System.Text;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private async Task RaiseLfsInstallFailedNoticeIfNeededAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        await PresentOneTimeNoticeAsync(
                repository,
                runner,
                GetNoticeAcknowledgementKey(LfsInstallFailedNoticeConfigKeyPrefix, repository),
                new VersionControlPolicyNotice.LfsInstallFailed(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RaiseLfsQuotaNoticeIfNeededAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RemoteInfo> remotes = await GetRemotesCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        string? remoteUrl = remotes.FirstOrDefault()?.Url;
        if (remoteUrl is null)
        {
            return;
        }

        if (!await IsLfsActiveAsync(repository, runner, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await PresentOneTimeNoticeAsync(
                repository,
                runner,
                GetNoticeAcknowledgementKey(LfsQuotaNoticeConfigKeyPrefix, repository),
                new VersionControlPolicyNotice.LfsRemoteQuota(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RaiseLargeMediaNoticeIfNeededAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        WorkspaceStatus status,
        CancellationToken cancellationToken)
    {
        string acknowledgementKey = GetNoticeAcknowledgementKey(LargeMediaNoticeConfigKeyPrefix, repository);
        (GitAvailability availability, _) = await GetGitRuntimeCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        if (await GetLocalBooleanConfigAsync(
                repository,
                runner,
                acknowledgementKey,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        long thresholdBytes = Math.Max(
            0L,
            (long)_installationLocator.Config.LargeMediaWarningThresholdMb * 1024 * 1024);
        var candidates = new List<(FileChange Change, string Path)>();
        foreach (FileChange change in status.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? path = GetLargeMediaPath(
                repository,
                change.Path,
                thresholdBytes);
            if (path is not null)
            {
                candidates.Add((change, path));
            }
        }

        HashSet<string> lfsCoveredPaths = availability.LfsInstalled
            ? await GetEffectiveLfsPathsAsync(
                    repository,
                    runner,
                    candidates.Select(static candidate => candidate.Change.Path).ToArray(),
                    cancellationToken)
                .ConfigureAwait(false)
            : [];
        foreach ((FileChange change, string path) in candidates)
        {
            if (lfsCoveredPaths.Contains(change.Path))
            {
                continue;
            }

            if (!TryGetFileLength(path, out long sizeBytes) || sizeBytes <= thresholdBytes)
            {
                continue;
            }

            if (!await PresentPolicyNoticeAsync(
                new VersionControlPolicyNotice.LargeMediaWithoutLfs(
                        NormalizeGitPath(Path.GetRelativePath(repository.ProjectRoot, path)),
                        sizeBytes),
                    cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                await SetLocalConfigValueAsync(
                    repository,
                    runner,
                    acknowledgementKey,
                    "true",
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogWarningBestEffort(
                    ex,
                    "Failed to persist the large-media notice acknowledgement.");
            }

            return;
        }
    }

    // An automatic snapshot has no dialog of its own, so the first one without an identity asks for it
    // as a manual commit does, and the snapshot is recorded with the answer. A dismissal is not asked
    // again: later snapshots are skipped with the one-time notice instead.
    private async Task<bool> TryRequestMissingIdentityAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string acknowledgementKey = GetNoticeAcknowledgementKey(MissingIdentityNoticeConfigKeyPrefix, repository);
        if (_identityRequest is not null
            && !await GetLocalBooleanConfigAsync(
                    repository,
                    runner,
                    acknowledgementKey,
                    cancellationToken)
                .ConfigureAwait(false)
            && await _identityRequest(cancellationToken).ConfigureAwait(false) is { } identity)
        {
            await SetLocalIdentityCoreAsync(repository, runner, identity, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        await RaiseMissingIdentityNoticeIfNeededAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        return false;
    }

    // Ignore rules are the user's to set, so a snapshot leaves an ignored file out as plain Git does.
    // Saying so once keeps a rule that also matches project files from going unnoticed.
    private async Task RaiseIgnoredProjectFilesNoticeIfNeededAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string acknowledgementKey = GetNoticeAcknowledgementKey(
            IgnoredProjectFilesNoticeConfigKeyPrefix,
            repository);
        if (await GetLocalBooleanConfigAsync(
                repository,
                runner,
                acknowledgementKey,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        IReadOnlyList<string> ignoredPaths;
        try
        {
            ignoredPaths = await FindIgnoredProjectFilesAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWarningBestEffort(ex, "Failed to list the project files that Git ignores.");
            return;
        }

        if (ignoredPaths.Count == 0)
        {
            return;
        }

        await PresentOneTimeNoticeAsync(
                repository,
                runner,
                acknowledgementKey,
                new VersionControlPolicyNotice.IgnoredProjectFiles(ignoredPaths),
                cancellationToken)
            .ConfigureAwait(false);
    }

    // Project-relative paths that the ignore rules keep out of a snapshot, an ignored folder named
    // once. Beutl's own per-user state and scratch files are ignored on purpose and left out.
    private static async Task<IReadOnlyList<string>> FindIgnoredProjectFilesAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
                repository,
                [
                    "ls-files",
                    "--others",
                    "--ignored",
                    "--exclude-standard",
                    "--directory",
                    "--full-name",
                    "-z",
                    "--",
                    CreateSnapshotBasePathspec(repository),
                ],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    MaxStdoutBytes: MaxIgnoredProjectFileOutputBytes,
                    UseLiteralPathspecs: false),
                cancellationToken)
            .ConfigureAwait(false);
        string prefix = GetProjectPathPrefix(repository);
        var paths = new List<string>();
        foreach (string path in GitCliRunner.SplitNullSeparated(result.Stdout))
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
            {
                // A folder of the enclosing repository that holds the project is ignored as a whole.
                if (path.EndsWith('/') && prefix.StartsWith(path, StringComparison.Ordinal))
                {
                    paths.Add(path);
                }

                continue;
            }

            string projectRelativePath = path[prefix.Length..];
            if (projectRelativePath.Length == 0)
            {
                paths.Add(path);
                continue;
            }

            if (IsTemporaryProjectFile(projectRelativePath.TrimEnd('/'))
                || projectRelativePath
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Any(static segment => string.Equals(
                        segment,
                        ".beutl",
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            paths.Add(projectRelativePath);
        }

        // Git can also list the untracked folder that holds an ignored file; the file says more.
        return paths
            .Where(path => !path.EndsWith('/')
                           || !paths.Any(other => other.Length > path.Length
                                                  && other.StartsWith(path, StringComparison.Ordinal)))
            .ToArray();
    }

    private async Task RaiseMissingIdentityNoticeIfNeededAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        await PresentOneTimeNoticeAsync(
                repository,
                runner,
                GetNoticeAcknowledgementKey(MissingIdentityNoticeConfigKeyPrefix, repository),
                new VersionControlPolicyNotice.MissingIdentity(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    // A notice is shown once per project: presenting it sets acknowledgementKey, and a set key skips it.
    private async Task PresentOneTimeNoticeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string acknowledgementKey,
        VersionControlPolicyNotice notice,
        CancellationToken cancellationToken)
    {
        if (await GetLocalBooleanConfigAsync(
                repository,
                runner,
                acknowledgementKey,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (!await PresentPolicyNoticeAsync(
                notice,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await SetLocalConfigValueAsync(
            repository,
            runner,
            acknowledgementKey,
            "true",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PresentPolicyNoticeAsync(
        VersionControlPolicyNotice notice,
        CancellationToken cancellationToken)
    {
        if (_policyNoticeSink is null)
        {
            return false;
        }

        try
        {
            await _policyNoticeSink(notice, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static string GetNoticeAcknowledgementKey(string prefix, RepositoryInfo repository)
    {
        return prefix + GetConfigKeyHash(repository.Pathspec);
    }

    private static string GetConfigKeyHash(string value)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static string? GetLargeMediaPath(
        RepositoryInfo repository,
        string repoRelativePath,
        long thresholdBytes)
    {
        string normalizedPath = NormalizeGitPath(repoRelativePath);
        string projectRelativePath;
        if (repository.Pathspec == ".")
        {
            projectRelativePath = normalizedPath;
        }
        else if (normalizedPath.StartsWith($"{repository.Pathspec}/", StringComparison.Ordinal))
        {
            projectRelativePath = normalizedPath[(repository.Pathspec.Length + 1)..];
        }
        else
        {
            return null;
        }

        if (!s_mediaExtensions.Contains(Path.GetExtension(projectRelativePath)))
        {
            return null;
        }

        string path = Path.GetFullPath(Path.Combine(
            repository.ProjectRoot,
            projectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!TryGetFileLength(path, out long length) || length <= thresholdBytes)
        {
            return null;
        }

        return path;
    }

    private static bool TryGetFileLength(string path, out long length)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                length = 0;
                return false;
            }

            length = file.Length;
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or System.Security.SecurityException)
        {
            length = 0;
            return false;
        }
    }

    private async Task TryRaiseLfsQuotaNoticeIfNeededAsync(
        RepositoryInfo repository,
        IGitCliRunner runner)
    {
        try
        {
            await RaiseLfsQuotaNoticeIfNeededAsync(
                repository,
                runner,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWarningBestEffort(
                ex,
                "Failed to publish the Git LFS quota notice after configuring the remote.");
        }
    }
}
