using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService :
    IProjectVersionControlBackend
{
    private const int PendingPullRecoveryFormatVersion = 1;
    private const int MaxPendingRecoveryListBytes = 1024 * 1024;
    private const int MaxPendingRecoveryDescriptorBytes = 64 * 1024;
    private const int MaxHistoricalGraphListBytes = 4 * 1024 * 1024;
    private const int MaxHistoricalGraphFileCount = 16 * 1024;
    private const long MaxHistoricalGraphBytes = 128L * 1024 * 1024;
    private const int MaxHistoricalGraphCacheEntries = 32;
    private const int MaxHistoricalArchivePathspecCharacters = 16 * 1024;
    // A repository can restrict which LFS paths are hydrated (lfs.fetchinclude / lfs.fetchexclude).
    // A transition has to reopen the project on its real media, so its checkout clears those
    // filters: an excluded pointer is copied through unchanged, which would leave pointer text in
    // the work tree where the media belongs. Repository-wide prefetches use the same cleared
    // baseline; project restore narrows the scan with an explicit include or exact subtree.
    // Offers Beutl's own LFS upload agent to its pushes; see HostedGitLfsTransferAgent.
    private static readonly IReadOnlyList<string> s_lfsUploadAgentOverrides =
        HostedGitLfsTransferAgent.GitConfigArguments(Environment.ProcessPath, Assembly.GetEntryAssembly()?.Location);

    private static readonly string[] s_lfsPathFilterOverrides =
    [
        "-c",
        "lfs.fetchinclude=",
        "-c",
        "lfs.fetchexclude=",
    ];

    private static readonly JsonSerializerOptions s_recoveryJsonOptions =
        new(JsonSerializerOptions.Strict);
    private static readonly UTF8Encoding s_strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly byte[] s_utf8Bom = [0xef, 0xbb, 0xbf];

    internal const int MaxDiffBytes = 1024 * 1024;
    internal const string DiffTruncationMarker = "\n--- Diff truncated at 1 MB ---\n";
    private const string OriginRefPrefix = "refs/remotes/origin/";
    private const string LfsQuotaNoticeConfigKeyPrefix = "beutl.lfsQuotaNoticeShown-";
    private const string LargeMediaNoticeConfigKeyPrefix = "beutl.largeMediaNoticeShown-";
    private const string MissingIdentityNoticeConfigKeyPrefix = "beutl.missingIdentityNoticeShown-";
    private const string LfsInstallFailedNoticeConfigKeyPrefix = "beutl.lfsInstallFailedNoticeShown-";
    private static readonly string[] s_lfsHookNames = ["pre-push", "post-checkout", "post-commit", "post-merge"];
    private const string PullSafetyCommitMessage = "beutl: safety snapshot before pull";
    private const string ManagedLfsBeginMarker = "# BEGIN BEUTL MANAGED LFS";
    private const string ManagedLfsEndMarker = "# END BEUTL MANAGED LFS";
    private const int MaxHygieneWriteAttempts = 3;
    private const int MaxIgnoredRequiredPathOutputBytes = 256 * 1024;
    private const int MaxLfsAttributeOutputBytes = 256 * 1024;
    private const int MaxLfsFetchOutputBytes = 64 * 1024;
    private const int MaxLfsObjectListOutputBytes = 4 * 1024 * 1024;
    private const int MaxLfsPointerCandidates = 256;
    private const int MaxSnapshotTreeInspectionBytes = 4 * 1024 * 1024;
    private const int MaxCommitMessageBytes = 1024 * 1024;

    private static readonly string[] s_gitIgnoreLines =
    [
        "**/.beutl/",
        "*.[tT][mM][pP]",
    ];

    private static readonly string[] s_textAttributeLines =
    [
        "*.[bB][eE][pP] text eol=lf",
        "*.[sS][cC][eE][nN][eE] text eol=lf",
        "*.[bB][eE][lL][mM] text eol=lf",
        ".gitignore text eol=lf",
        ".gitattributes text eol=lf",
    ];

    // Stable union of the existing policy, Engine built-in decoders, the FFmpeg and
    // MF/AVF decoders, and SharedFilePickerOptions.OpenImage. Do not derive this from
    // DecoderRegistry: repository attributes must not vary with platform or extension load state.
    private static readonly string[] s_supportedMediaExtensions =
    [
        ".mp4",
        ".mov",
        ".mkv",
        ".avi",
        ".wmv",
        ".flv",
        ".webm",
        ".wav",
        ".mp3",
        ".flac",
        ".aac",
        ".m4a",
        ".ogg",
        ".opus",
        ".wma",
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".bmp",
        ".webp",
        ".tiff",
        ".tif",
        // Engine built-in decoder additions.
        ".wave",
        ".apng",
        // FFmpeg decoder additions.
        ".264",
        ".mpeg",
        ".ts",
        ".mts",
        ".m2ts",
        // Media Foundation and AVFoundation decoder additions.
        ".sami",
        ".smi",
        ".m4v",
        ".adts",
        ".asf",
        ".3gp",
        ".3gp2",
        ".3gpp",
        // SharedFilePickerOptions.OpenImage additions.
        ".ico",
        ".wbmp",
        ".pkm",
        ".ktx",
        ".astc",
        ".dng",
        ".heif",
        ".avif",
    ];

    private static readonly string[] s_lfsAttributeLines =
        s_supportedMediaExtensions
            .Select(static extension =>
                $"**/*{CreateCaseInsensitiveGlob(extension)} "
                + "filter=lfs diff=lfs merge=lfs -text")
            .ToArray();

    private static readonly HashSet<string> s_mediaExtensions = new(
        s_supportedMediaExtensions,
        StringComparer.OrdinalIgnoreCase);

    internal static bool IsSupportedMediaPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return s_mediaExtensions.Contains(Path.GetExtension(path));
    }

    // Beutl writes these itself, so they are checked by name. Every other file the project needs is in
    // its reference graph and checked there, which leaves out files the project no longer uses.
    private static readonly string[] s_ignoredRequiredProjectPathspecSuffixes =
    [
        ".gitignore",
        ".gitattributes",
    ];

    private const string TemporaryFilePathspecSuffix = "**/*.[tT][mM][pP]";

    private static readonly string[] s_ignoredOptionalProjectPathspecSuffixes =
    [
        "**/.[bB][eE][uU][tT][lL]/**",
        TemporaryFilePathspecSuffix,
    ];

    private static readonly string[] s_repositoryOperationRefs =
    [
        "MERGE_HEAD",
        "CHERRY_PICK_HEAD",
        "REVERT_HEAD",
        "rebase-merge",
        "rebase-apply",
        "sequencer",
    ];

    private static string CreateCaseInsensitiveGlob(string value)
    {
        var builder = new StringBuilder(value.Length * 4);
        foreach (char character in value)
        {
            if (character is >= 'a' and <= 'z')
            {
                builder.Append('[')
                    .Append(character)
                    .Append(char.ToUpperInvariant(character))
                    .Append(']');
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private readonly GitInstallationLocator _installationLocator;
    private readonly Func<string, IGitCliRunner> _runnerFactory;
    private readonly Func<bool> _isWorktreeMutationAllowed;
    private readonly string? _projectFile;
    // The LFS setting the last hygiene run applied, which tells a change made while the project is
    // open apart from a machine where LFS was never on.
    private bool? _lastLfsRequested;
    private bool _hygieneDeferred;
    private readonly Dictionary<string, IReadOnlySet<string>> _historicalRequiredTemporaryPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<VersionControlPolicyNotice, CancellationToken, Task>? _policyNoticeSink;
    private readonly Func<CancellationToken, Task<GitIdentity?>>? _identityRequest;
    private readonly Func<string, CancellationToken, Task>? _beforeHygieneFileReplace;
    private readonly Func<string, CancellationToken, Task>? _beforeFileCommit;
    private readonly Func<string, CancellationToken, Task>? _afterFileExchange;
    private readonly Func<string, bool> _deleteVerifiedHygieneFile;
    private readonly Func<string, bool> _deleteOwnedLocalConfigFile;
    private readonly Action<Action> _statusNotificationScheduler;
    private readonly Action<Action> _lockNotificationScheduler;
    private readonly ILogger _logger;
    private readonly bool _createWatcherWhenRepositoryAvailable;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _lifetimeSync = new();
    private readonly object _runtimeSync = new();
    private readonly ConcurrentQueue<WorkspaceStatus> _statusNotifications = new();
    private IReadOnlySet<string> _requiredTemporaryProjectPaths =
        new HashSet<string>(StringComparer.Ordinal);
    private RepositoryWatcher? _watcher;
    private GitAvailability? _cachedAvailability;
    private IGitCliRunner? _runner;
    private Task<CommitResult?>? _retirementTask;
    private int _configurationRevision;
    private int _lifetimeState;
    private int _resourcesDisposed;
    private int _statusNotificationDrainScheduled;
    private int _watcherRefreshPending;
    private int _watcherRefreshScheduled;
    private long _statusNotificationSequence;
    private int _watcherRefreshInvalidated;

    public GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository = null)
        : this(
            installationLocator,
            repository,
            repository is null ? null : new RepositoryWatcher(repository),
            static gitPath => new GitCliRunner(gitPath),
            createWatcherWhenRepositoryAvailable: true,
            isWorktreeMutationAllowed: static () => true,
            projectFile: null,
            policyNoticeSink: null,
            identityRequest: null,
            beforeHygieneFileReplace: null,
            beforeFileCommit: null,
            afterFileExchange: null,
            deleteVerifiedHygieneFile: null,
            deleteOwnedLocalConfigFile: null,
            statusNotificationScheduler: null,
            lockNotificationScheduler: null,
            logger: null)
    {
    }

    internal GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository,
        Func<bool> isWorktreeMutationAllowed,
        Func<VersionControlPolicyNotice, CancellationToken, Task>? policyNoticeSink = null,
        string? projectFile = null,
        Func<CancellationToken, Task<GitIdentity?>>? identityRequest = null)
        : this(
            installationLocator,
            repository,
            repository is null ? null : new RepositoryWatcher(repository),
            static gitPath => new GitCliRunner(gitPath),
            createWatcherWhenRepositoryAvailable: true,
            isWorktreeMutationAllowed: isWorktreeMutationAllowed,
            projectFile: projectFile,
            policyNoticeSink: policyNoticeSink,
            identityRequest: identityRequest,
            beforeHygieneFileReplace: null,
            beforeFileCommit: null,
            afterFileExchange: null,
            deleteVerifiedHygieneFile: null,
            deleteOwnedLocalConfigFile: null,
            statusNotificationScheduler: null,
            lockNotificationScheduler: null,
            logger: null)
    {
    }

    internal GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository,
        RepositoryWatcher? watcher,
        Func<string, IGitCliRunner> runnerFactory,
        ILogger? logger = null,
        Func<string, CancellationToken, Task>? beforeHygieneFileReplace = null,
        Func<string, CancellationToken, Task>? beforeFileCommit = null,
        Func<string, CancellationToken, Task>? afterFileExchange = null,
        Func<string, bool>? deleteVerifiedHygieneFile = null,
        Func<string, bool>? deleteOwnedLocalConfigFile = null,
        Func<VersionControlPolicyNotice, CancellationToken, Task>? policyNoticeSink = null,
        Action<Action>? statusNotificationScheduler = null,
        Action<Action>? lockNotificationScheduler = null,
        string? projectFile = null,
        Func<CancellationToken, Task<GitIdentity?>>? identityRequest = null,
        Func<bool>? isWorktreeMutationAllowed = null)
        : this(
            installationLocator,
            repository,
            watcher,
            runnerFactory,
            createWatcherWhenRepositoryAvailable: false,
            isWorktreeMutationAllowed: isWorktreeMutationAllowed ?? (static () => true),
            projectFile: projectFile,
            policyNoticeSink,
            identityRequest: identityRequest,
            beforeHygieneFileReplace: beforeHygieneFileReplace,
            beforeFileCommit: beforeFileCommit,
            afterFileExchange: afterFileExchange,
            deleteVerifiedHygieneFile: deleteVerifiedHygieneFile,
            deleteOwnedLocalConfigFile: deleteOwnedLocalConfigFile,
            statusNotificationScheduler: statusNotificationScheduler,
            lockNotificationScheduler: lockNotificationScheduler,
            logger: logger)
    {
    }

    private GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository,
        RepositoryWatcher? watcher,
        Func<string, IGitCliRunner> runnerFactory,
        bool createWatcherWhenRepositoryAvailable,
        Func<bool> isWorktreeMutationAllowed,
        string? projectFile,
        Func<VersionControlPolicyNotice, CancellationToken, Task>? policyNoticeSink,
        Func<CancellationToken, Task<GitIdentity?>>? identityRequest,
        Func<string, CancellationToken, Task>? beforeHygieneFileReplace,
        Func<string, CancellationToken, Task>? beforeFileCommit,
        Func<string, CancellationToken, Task>? afterFileExchange,
        Func<string, bool>? deleteVerifiedHygieneFile,
        Func<string, bool>? deleteOwnedLocalConfigFile,
        Action<Action>? statusNotificationScheduler,
        Action<Action>? lockNotificationScheduler,
        ILogger? logger)
    {
        _installationLocator = installationLocator
                               ?? throw new ArgumentNullException(nameof(installationLocator));
        if (watcher is not null && repository is null)
        {
            throw new ArgumentException(
                "A watcher can only be supplied for an associated repository.",
                nameof(watcher));
        }

        Repository = repository;
        _watcher = watcher;
        _runnerFactory = runnerFactory ?? throw new ArgumentNullException(nameof(runnerFactory));
        _isWorktreeMutationAllowed = isWorktreeMutationAllowed
                                     ?? throw new ArgumentNullException(
                                         nameof(isWorktreeMutationAllowed));
        _projectFile = projectFile is null ? null : Path.GetFullPath(projectFile);
        _policyNoticeSink = policyNoticeSink;
        _identityRequest = identityRequest;
        _beforeHygieneFileReplace = beforeHygieneFileReplace;
        _beforeFileCommit = beforeFileCommit;
        _afterFileExchange = afterFileExchange;
        _deleteVerifiedHygieneFile = deleteVerifiedHygieneFile
                                     ?? TryDeleteVerifiedHygieneFile;
        _deleteOwnedLocalConfigFile = deleteOwnedLocalConfigFile
                                      ?? DeleteOwnedLocalConfigFile;
        _statusNotificationScheduler = statusNotificationScheduler ?? ScheduleStatusNotificationDrain;
        _lockNotificationScheduler = lockNotificationScheduler ?? ScheduleLockNotification;
        _logger = logger ?? Log.CreateLogger<GitCliVersionControlService>();
        _createWatcherWhenRepositoryAvailable = createWatcherWhenRepositoryAvailable;
        if (_watcher is not null)
        {
            if (repository is not null)
            {
                try
                {
                    IReadOnlySet<string> serializedPaths =
                        GetSerializedProjectRelativePaths(repository.ProjectRoot);
                    _requiredTemporaryProjectPaths = serializedPaths
                        .Where(static path => IsTemporaryProjectFile(path))
                        .ToHashSet(StringComparer.Ordinal);
                    _watcher.UpdateRequiredPaths(serializedPaths);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    LogWarningBestEffort(
                        ex,
                        "The initial repository watcher paths could not be read from the serialized project graph.");
                }
            }

            _watcher.Changed += OnRepositoryChanged;
        }

        _installationLocator.Config.ConfigurationChanged += OnVersionControlConfigChanged;
    }

    public RepositoryInfo? Repository { get; private set; }

    public RepositoryLockInfo? RecoverableLock { get; private set; }

    public event EventHandler<WorkspaceStatus>? StatusChanged;

    public event EventHandler<RepositoryLockInfo>? RecoverableLockAvailable;

    public Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            async () => (await GetGitRuntimeCoreAsync(cancellationToken).ConfigureAwait(false)).Availability,
            cancellationToken);
    }

    public Task<RepositoryInfo?> DiscoverRepositoryAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        return RunSerializedAsync(
            async () =>
            {
                IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
                return await DiscoverRepositoryCoreAsync(projectRoot, runner, cancellationToken)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task InitializeAsync(InitOptions options, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);
        return RunSerializedAsync(
            () => InitializeCoreAsync(options, cancellationToken),
            cancellationToken);
    }

    public Task EnsureRepositoryHygieneAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            () => EnsureRepositoryHygieneSerializedCoreAsync(cancellationToken),
            cancellationToken);
    }

    public Task<bool> HasVersionTrackingOptInAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(repository);
        return RunSerializedAsync(
            () => HasVersionTrackingOptInCoreAsync(repository, cancellationToken),
            cancellationToken);
    }

    public Task<bool> HasCheckedOutCommitAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(repository);
        return RunSerializedAsync(
            () => HasCheckedOutCommitCoreAsync(repository, cancellationToken),
            cancellationToken);
    }

    public Task<CommitResult> CommitAllAsync(
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return RunSerializedAsync(
            () => CommitAllCoreAsync(message, kind, cancellationToken),
            cancellationToken);
    }

    public Task<PendingPullRecovery> PersistPendingPullRecoveryAsync(
        ProjectCheckpoint checkpoint,
        CheckedOutBranchTip targetTip,
        string projectFile,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(targetTip);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFile);
        return RunSerializedAsync(
            () => PersistPendingPullRecoveryCoreAsync(
                checkpoint,
                targetTip,
                projectFile,
                cancellationToken),
            cancellationToken);
    }

    public Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            async () => (await GetStatusCoreAsync(cancellationToken).ConfigureAwait(false)) with
            {
                NotificationSequence = Volatile.Read(ref _statusNotificationSequence),
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        return RunSerializedAsync(
            () => GetHistoryCoreAsync(skip, take, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<FileChange>> GetCommitFilesAsync(
        string sha,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        GitRevisionValidator.ValidateCommitId(sha, nameof(sha));
        return RunSerializedAsync(
            () => GetCommitFilesCoreAsync(sha, cancellationToken),
            cancellationToken);
    }

    public Task<string> GetDiffAsync(
        string sha,
        string? path,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        GitRevisionValidator.ValidateCommitId(sha, nameof(sha));
        return RunSerializedAsync(
            () => GetDiffCoreAsync(sha, path, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            () => GetBranchesCoreAsync(cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<RemoteInfo>> GetRemotesAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            () => GetRemotesCoreAsync(cancellationToken),
            cancellationToken);
    }

    public Task SetRemoteAsync(
        string url,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        GitRemoteUrl remote = GitRemoteUrl.Parse(url);
        return RunSerializedAsync(
            () => SetRemoteCoreAsync(remote, cancellationToken),
            cancellationToken);
    }

    public Task<RemoteOpResult> PushAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            () => PushCoreAsync(progress, cancellationToken),
            cancellationToken);
    }

    public Task<GitIdentity?> GetIdentityAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            async () =>
            {
                RepositoryInfo repository = GetRepository();
                IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
                return await GetIdentityCoreAsync(repository, runner, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task SetLocalIdentityAsync(
        GitIdentity identity,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Email);
        return RunSerializedAsync(
            async () =>
            {
                RepositoryInfo repository = GetRepository();
                await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
                IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
                await SetLocalIdentityCoreAsync(
                        repository,
                        runner,
                        identity,
                        cancellationToken)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task<bool> RemoveRecoverableLockAsync(
        RepositoryLockInfo expectedLock,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(expectedLock);
        return RunSerializedAsync(
            async () =>
            {
                RepositoryInfo repository = GetRepository();
                IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
                RepositoryLockInfo? lockInfo = RecoverableLock;
                if (!ReferenceEquals(lockInfo, expectedLock))
                {
                    return false;
                }

                bool removed = runner.RemoveRecoverableRepositoryLock(repository, expectedLock);
                if (removed)
                {
                    RecoverableLock = null;
                }

                return removed;
            },
            cancellationToken);
    }

    public void Dispose()
    {
        Task retirement = RetireAsync(finalSnapshot: null);
        if (!retirement.IsCompletedSuccessfully)
        {
            _ = ObserveRetirementAsync(retirement);
        }
    }

    Task<TResult> IProjectVersionControlBackend.ExecuteExclusiveAsync<TResult>(
        Func<IProjectVersionControlTransaction, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        return ExecuteExclusiveCoreAsync(operation, cancellationToken);
    }

    public Task<CommitResult?> RetireAsync(ProjectVersionControlFinalSnapshot? finalSnapshot)
    {
        lock (_lifetimeSync)
        {
            if (_retirementTask is not null)
            {
                return _retirementTask;
            }

            if ((ServiceLifetimeState)_lifetimeState == ServiceLifetimeState.Retired)
            {
                return Task.FromResult<CommitResult?>(null);
            }

            _lifetimeState = (int)ServiceLifetimeState.Retiring;
            _retirementTask = RetireCoreAsync(finalSnapshot);
            return _retirementTask;
        }
    }

    private async Task<TResult> ExecuteExclusiveCoreAsync<TResult>(
        Func<IProjectVersionControlTransaction, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await operation(new Transaction(this)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CaptureRecoverableLock(ex);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<CommitResult?> RetireCoreAsync(
        ProjectVersionControlFinalSnapshot? finalSnapshot)
    {
        await Task.Yield();
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (finalSnapshot is not null && Repository is not null)
            {
                return await CommitAllCoreAsync(
                        finalSnapshot.Message,
                        finalSnapshot.Kind,
                        CancellationToken.None,
                        presentMissingIdentityNotice: false)
                    .ConfigureAwait(false);
            }

            return null;
        }
        catch (Exception ex)
        {
            CaptureRecoverableLock(ex);
            throw;
        }
        finally
        {
            DisposeResources();
            Volatile.Write(ref _lifetimeState, (int)ServiceLifetimeState.Retired);
            _operationGate.Release();
        }
    }

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
        {
            return;
        }

        RepositoryWatcher? watcher;
        lock (_lifetimeSync)
        {
            watcher = _watcher;
            _watcher = null;
            if (watcher is not null)
            {
                watcher.Changed -= OnRepositoryChanged;
            }
        }

        watcher?.Dispose();
        _installationLocator.Config.ConfigurationChanged -= OnVersionControlConfigChanged;
    }

    private async Task ObserveRetirementAsync(Task retirement)
    {
        try
        {
            await retirement.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retire the project version-control service.");
        }
    }

    public Task<IReadOnlyList<string>> GetTrackedReservedPathsAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            async () =>
            {
                RepositoryInfo repository = GetRepository();
                IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
                return await GetTrackedReservedPathsCoreAsync(repository, runner, cancellationToken)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task UntrackReservedPathsAsync(
        IReadOnlyList<string> reservedPaths,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(reservedPaths);
        if (reservedPaths.Count == 0)
        {
            return Task.CompletedTask;
        }

        return RunSerializedAsync(
            async () =>
            {
                RepositoryInfo repository = GetRepository();
                IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
                await TryUntrackReservedPathsCoreAsync(
                        repository,
                        runner,
                        reservedPaths,
                        cancellationToken)
                    .ConfigureAwait(false);
                await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    private static string NormalizeGitPath(string path)
        => OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    private void EnsureWorktreeMutationAllowed()
    {
        if (!_isWorktreeMutationAllowed())
        {
            throw new InvalidOperationException(
                "The project must be closed before changing version-controlled project files.");
        }
    }

    private async Task<IGitCliRunner> GetInstalledRunnerCoreAsync(CancellationToken cancellationToken)
    {
        (GitAvailability availability, IGitCliRunner? runner)
            = await GetGitRuntimeCoreAsync(cancellationToken).ConfigureAwait(false);
        if (availability.State != GitAvailabilityState.Installed || runner is null)
        {
            throw new InvalidOperationException("Git is not available.");
        }

        return runner;
    }

    private RepositoryInfo GetRepository()
    {
        return Repository
               ?? throw new InvalidOperationException(
                   "The project is not associated with a Git repository.");
    }

    private async Task<(GitAvailability Availability, IGitCliRunner? Runner)> GetGitRuntimeCoreAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            int revision;
            lock (_runtimeSync)
            {
                if (_cachedAvailability is not null)
                {
                    return (_cachedAvailability, _runner);
                }

                revision = _configurationRevision;
            }

            GitAvailability availability = await _installationLocator
                .LocateAsync(cancellationToken)
                .ConfigureAwait(false);
            IGitCliRunner? runner = availability.State == GitAvailabilityState.Installed
                                    && availability.GitPath is not null
                ? _runnerFactory(availability.GitPath)
                : null;

            lock (_runtimeSync)
            {
                if (revision != _configurationRevision)
                {
                    continue;
                }

                _cachedAvailability = availability;
                _runner = runner;
                return (availability, runner);
            }
        }
    }

    private async Task<T> RunSerializedAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await Task.Run(operation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CaptureRecoverableLock(ex);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task RunSerializedAsync(
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await Task.Run(operation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CaptureRecoverableLock(ex);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void LogWarningBestEffort(Exception exception, string message)
    {
        try
        {
            _logger.LogWarning(exception, message);
        }
        catch
        {
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    private bool IsDisposed
        => (ServiceLifetimeState)Volatile.Read(ref _lifetimeState) != ServiceLifetimeState.Active;

    private enum ServiceLifetimeState
    {
        Active,
        Retiring,
        Retired,
    }
}
