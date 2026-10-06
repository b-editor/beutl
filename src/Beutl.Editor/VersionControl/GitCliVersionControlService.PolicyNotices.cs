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
        IReadOnlySet<string> serializedFileSources =
            GetSerializedFileSourceRelativePaths(repository.ProjectRoot);
        var candidates = new List<(FileChange Change, string Path)>();
        foreach (FileChange change in status.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? path = GetLargeMediaPath(
                repository,
                change.Path,
                serializedFileSources,
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
        IReadOnlySet<string> serializedFileSources,
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

        if (!s_mediaExtensions.Contains(Path.GetExtension(projectRelativePath))
            && !serializedFileSources.Contains(projectRelativePath))
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
