namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private static async Task<GitIdentity?> GetIdentityCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string? name = await TryGetConfigValueAsync(
            repository,
            runner,
            "user.name",
            cancellationToken).ConfigureAwait(false);
        string? email = await TryGetConfigValueAsync(
            repository,
            runner,
            "user.email",
            cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email)
            ? null
            : new GitIdentity(name, email);
    }

    private async Task SetLocalIdentityCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        GitIdentity identity,
        CancellationToken cancellationToken)
    {
        await UpdateLocalConfigAtomicallyAsync(
            repository,
            runner,
            async (stagingPath, updateCancellation) =>
            {
                await runner.RunAsync(
                    repository,
                    ["config", "--file", stagingPath, "--replace-all", "user.name", identity.Name],
                    GitCommandOptions.Local,
                    updateCancellation).ConfigureAwait(false);
                await runner.RunAsync(
                    repository,
                    ["config", "--file", stagingPath, "--replace-all", "user.email", identity.Email],
                    GitCommandOptions.Local,
                    updateCancellation).ConfigureAwait(false);
            },
            "identity update",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateLocalConfigAtomicallyAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        Func<string, CancellationToken, Task> stageUpdate,
        string operationName,
        CancellationToken cancellationToken)
    {
        string configPath = await ResolveGitPathAsync(
                repository,
                runner,
                "config",
                cancellationToken)
            .ConfigureAwait(false);
        string lockPath = configPath + ".lock";
        string configDirectory = Path.GetDirectoryName(configPath)
                                 ?? throw new InvalidOperationException(
                                     "The local Git configuration has no parent directory.");
        string stagingPath = Path.Combine(
            configDirectory,
            $".beutl-config-{Guid.NewGuid():N}.tmp");
        FileStream lockStream;
        try
        {
            lockStream = new FileStream(
                lockPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GitOperationException(
                128,
                $"Unable to acquire the local Git configuration lock '{lockPath}': {ex.Message}");
        }

        bool committed = false;
        bool lockPathOwned = true;
        try
        {
            byte[] originalConfig;
            byte[] stagedConfig;
            FileAttributes originalAttributes;
            UnixFileMode? originalUnixMode = null;
            await using (lockStream)
            {
                EnsureLocalConfigPathIsRegular(configPath);
                originalConfig = await File.ReadAllBytesAsync(configPath, cancellationToken)
                    .ConfigureAwait(false);
                originalAttributes = File.GetAttributes(configPath);
                if (!OperatingSystem.IsWindows())
                {
                    originalUnixMode = File.GetUnixFileMode(configPath);
                }

                await using (var stagingStream = new FileStream(
                                 stagingPath,
                                 new FileStreamOptions
                                 {
                                     Mode = FileMode.CreateNew,
                                     Access = FileAccess.Write,
                                     Share = FileShare.None,
                                     Options = FileOptions.Asynchronous,
                                 }))
                {
                    await stagingStream.WriteAsync(originalConfig, cancellationToken)
                        .ConfigureAwait(false);
                    await stagingStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                await stageUpdate(stagingPath, cancellationToken).ConfigureAwait(false);

                stagedConfig = await File.ReadAllBytesAsync(stagingPath, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                byte[] currentConfig = await File.ReadAllBytesAsync(
                        configPath,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!originalConfig.AsSpan().SequenceEqual(currentConfig))
                {
                    throw new InvalidOperationException(
                        $"The local Git configuration changed while the {operationName} was staged.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                await lockStream.WriteAsync(stagedConfig, CancellationToken.None)
                    .ConfigureAwait(false);
                await lockStream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                lockStream.Flush(flushToDisk: true);
            }

            File.SetAttributes(lockPath, originalAttributes);
            if (!OperatingSystem.IsWindows() && originalUnixMode is { } unixMode)
            {
                File.SetUnixFileMode(lockPath, unixMode);
            }

            EnsureLocalConfigPathIsRegular(configPath);
            byte[] finalConfig = await File.ReadAllBytesAsync(
                    configPath,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!originalConfig.AsSpan().SequenceEqual(finalConfig))
            {
                throw new InvalidOperationException(
                    $"The local Git configuration changed before the staged {operationName} was committed.");
            }

            if (_beforeFileCommit is not null)
            {
                await _beforeFileCommit(configPath, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            string displacedPath = AtomicFileExchange.ReplacePreservingTarget(
                configPath,
                lockPath);
            lockPathOwned = false;
            if (_afterFileExchange is not null)
            {
                await _afterFileExchange(configPath, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await VerifyLocalConfigExchangeAsync(
                    configPath,
                    displacedPath,
                    originalConfig,
                    stagedConfig,
                    operationName)
                .ConfigureAwait(false);
            committed = true;
        }
        finally
        {
            TryDeleteOwnedLocalConfigFile(stagingPath + ".lock");
            TryDeleteOwnedLocalConfigFile(stagingPath);
            if (!committed && lockPathOwned)
            {
                try
                {
                    File.Delete(lockPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogWarningBestEffort(
                        ex,
                        $"Failed to release the local Git configuration lock after a {operationName} failure.");
                }
            }
        }
    }

    private async Task VerifyLocalConfigExchangeAsync(
        string configPath,
        string displacedPath,
        byte[] expectedConfig,
        byte[] stagedConfig,
        string operationName)
    {
        byte[] displacedConfig;
        try
        {
            displacedConfig = await ReadRegularLocalConfigAsync(displacedPath)
                .ConfigureAwait(false);
        }
        catch (Exception inspectionFailure)
        {
            throw new InvalidOperationException(
                $"The local Git configuration changed while the staged {operationName} was committed; the displaced entry was retained at '{displacedPath}'.",
                inspectionFailure);
        }

        if (expectedConfig.AsSpan().SequenceEqual(displacedConfig))
        {
            byte[] committedConfig = await ReadRegularLocalConfigAsync(configPath)
                .ConfigureAwait(false);
            if (!stagedConfig.AsSpan().SequenceEqual(committedConfig))
            {
                if (!TryDeleteOwnedLocalConfigFile(displacedPath))
                {
                    throw new InvalidOperationException(
                        $"The local Git configuration changed while the staged {operationName} was committed; the later edit was preserved and the verified prior contents were retained at '{displacedPath}' because cleanup failed.");
                }

                throw new InvalidOperationException(
                    $"The local Git configuration changed while the staged {operationName} was committed; the later edit was preserved.");
            }

            if (!TryDeleteOwnedLocalConfigFile(displacedPath))
            {
                throw new InvalidOperationException(
                    $"The local Git configuration was updated, but its verified prior contents could not be removed from '{displacedPath}'.");
            }

            return;
        }

        string recoveredCandidatePath;
        try
        {
            recoveredCandidatePath = AtomicFileExchange.ReplacePreservingTarget(
                configPath,
                displacedPath);
        }
        catch (Exception rollbackFailure)
        {
            throw new InvalidOperationException(
                $"The local Git configuration changed while the staged {operationName} was committed; the external edit was retained at '{displacedPath}' because it could not be restored safely.",
                rollbackFailure);
        }

        byte[] recoveredCandidate = await ReadRegularLocalConfigAsync(recoveredCandidatePath)
            .ConfigureAwait(false);
        if (!stagedConfig.AsSpan().SequenceEqual(recoveredCandidate))
        {
            throw new InvalidOperationException(
                $"The local Git configuration changed more than once during the staged {operationName}. The earlier external edit was restored and the later contents were retained at '{recoveredCandidatePath}'.");
        }

        if (!TryDeleteOwnedLocalConfigFile(recoveredCandidatePath))
        {
            throw new InvalidOperationException(
                $"The external local Git configuration edit was restored, but the displaced replacement could not be removed from '{recoveredCandidatePath}'.");
        }

        throw new InvalidOperationException(
            $"The local Git configuration changed while the staged {operationName} was committed; the external edit was preserved.");
    }

    private static async Task<byte[]> ReadRegularLocalConfigAsync(string path)
    {
        EnsureLocalConfigPathIsRegular(path);
        byte[] contents = await File.ReadAllBytesAsync(path, CancellationToken.None)
            .ConfigureAwait(false);
        EnsureLocalConfigPathIsRegular(path);
        return contents;
    }

    private bool TryDeleteOwnedLocalConfigFile(string path)
    {
        try
        {
            return _deleteOwnedLocalConfigFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWarningBestEffort(
                ex,
                "Failed to remove an owned temporary Git configuration file.");
            return false;
        }
    }

    private static bool DeleteOwnedLocalConfigFile(string path)
    {
        File.Delete(path);
        return !Path.Exists(path);
    }

    private static void EnsureLocalConfigPathIsRegular(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists
            || file.LinkTarget is not null
            || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"The local Git configuration path '{path}' is not a regular file.");
        }
    }

    private static void ValidateIdentity(GitIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Email);
    }

    private static async Task<bool> GetLocalBooleanConfigAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string key,
        CancellationToken cancellationToken)
    {
        string? value = await TryGetConfigValueAsync(
            repository,
            runner,
            key,
            cancellationToken).ConfigureAwait(false);
        return bool.TryParse(value, out bool parsed) && parsed;
    }

    private static async Task SetLocalConfigValueAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await runner.RunAsync(
            repository,
            ["config", "--local", key, value],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> TryGetConfigValueAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["config", "--get", key],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string value = result.Stdout.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return null;
        }
    }
}
