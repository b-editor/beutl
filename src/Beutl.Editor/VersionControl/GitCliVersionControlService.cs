using System.Text;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.VersionControl;

internal sealed class GitCliVersionControlService :
    IProjectVersionControlBackend
{
    private sealed record LfsAttributeQueryResult(
        HashSet<string> CoveredPaths,
        bool IsComplete);

    private sealed record WorktreeStateFingerprint(string Tree, string IndexEntries);

    private enum TreeTransitionOutcome
    {
        AppliedTarget,
        RestoredCurrent,
        OwnershipLost,
        RecoveryFailed,
    }

    private sealed record TreeTransitionResult(
        TreeTransitionOutcome Outcome,
        Exception? Error = null,
        CheckedOutBranchTip? ActualTip = null);

    private sealed record TreeTransitionIndexPlan(
        string? PrepareCommit = null,
        string? FinalCommit = null,
        string? RestoreCommit = null,
        string Pathspec = ".");

    private sealed class HeadOwnershipLease : IDisposable
    {
        private readonly Action<Exception>? _releaseFailureSink;
        private FileStream? _stream;

        private HeadOwnershipLease(
            string lockPath,
            FileStream stream,
            Action<Exception>? releaseFailureSink)
        {
            LockPath = lockPath;
            _stream = stream;
            _releaseFailureSink = releaseFailureSink;
        }

        public string LockPath { get; }

        public static HeadOwnershipLease Acquire(
            string headPath,
            string expectedRefName,
            Action<Exception>? releaseFailureSink)
        {
            string lockPath = headPath + ".lock";
            FileStream stream;
            try
            {
                stream = new FileStream(
                    lockPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.WriteThrough);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new GitOperationException(
                    128,
                    $"Unable to acquire the worktree HEAD lock '{lockPath}': {ex.Message}");
            }

            var lease = new HeadOwnershipLease(lockPath, stream, releaseFailureSink);
            try
            {
                string expected = $"ref: {expectedRefName}\n";
                string actual = File.ReadAllText(headPath, new UTF8Encoding(false));
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    throw new ProjectCheckpointStateChangedException();
                }

                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            FileStream? stream = Interlocked.Exchange(ref _stream, null);
            if (stream is null)
            {
                return;
            }

            try
            {
                stream.Dispose();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _releaseFailureSink?.Invoke(ex);
            }

            try
            {
                File.Delete(LockPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _releaseFailureSink?.Invoke(ex);
            }
        }
    }

    internal const int MaxDiffBytes = 1024 * 1024;
    internal const string DiffTruncationMarker = "\n--- Diff truncated at 1 MB ---\n";
    private const string LfsQuotaNoticeConfigKeyPrefix = "beutl.lfsQuotaNoticeShown-";
    private const string LargeMediaNoticeConfigKeyPrefix = "beutl.largeMediaNoticeShown-";
    private const string MissingIdentityNoticeConfigKeyPrefix = "beutl.missingIdentityNoticeShown-";
    private const string PullSafetyCommitMessage = "beutl: safety snapshot before pull";
    private const string ManagedLfsBeginMarker = "# BEGIN BEUTL MANAGED LFS";
    private const string ManagedLfsEndMarker = "# END BEUTL MANAGED LFS";
    private const int MaxHygieneWriteAttempts = 3;
    private const int MaxIgnoredRequiredPathOutputBytes = 256 * 1024;
    private const int MaxLfsAttributeOutputBytes = 256 * 1024;

    private static readonly string[] s_gitIgnoreLines =
    [
        "**/.beutl/",
        "*.tmp",
    ];

    private static readonly string[] s_textAttributeLines =
    [
        "*.bep text eol=lf",
        "*.scene text eol=lf",
        "*.belm text eol=lf",
        ".gitignore text eol=lf",
        ".gitattributes text eol=lf",
    ];

    private static readonly string[] s_lfsAttributeLines =
    [
        "resources/**/*.mp4 filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.mov filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.mkv filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.avi filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.wmv filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.flv filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.webm filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.wav filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.mp3 filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.flac filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.aac filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.m4a filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.ogg filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.opus filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.wma filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.png filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.jpg filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.jpeg filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.gif filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.bmp filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.webp filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.tiff filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.tif filter=lfs diff=lfs merge=lfs -text",
    ];

    private static readonly HashSet<string> s_mediaExtensions = new(
        s_lfsAttributeLines.Select(static line =>
            Path.GetExtension(line.Split(' ', 2)[0])),
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> s_projectFileExtensions = new(
        [".bep", ".scene", ".belm"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] s_ignoredRequiredProjectPathspecSuffixes =
    [
        "**/*.[bB][eE][pP]",
        "**/*.[sS][cC][eE][nN][eE]",
        "**/*.[bB][eE][lL][mM]",
        "**/[rR][eE][sS][oO][uU][rR][cC][eE][sS]/**",
        ".gitignore",
        ".gitattributes",
    ];

    private static readonly string[] s_ignoredOptionalProjectPathspecSuffixes =
    [
        "**/.[bB][eE][uU][tT][lL]/**",
        "**/*.[tT][mM][pP]",
    ];

    private readonly GitInstallationLocator _installationLocator;
    private readonly Func<string, IGitCliRunner> _runnerFactory;
    private readonly Func<bool> _isWorktreeMutationAllowed;
    private readonly Func<VersionControlPolicyNotice, CancellationToken, Task>? _policyNoticeSink;
    private readonly Func<string, CancellationToken, Task>? _beforeHygieneFileReplace;
    private readonly Func<string, CancellationToken, Task>? _beforeHygieneFileCommit;
    private readonly ILogger _logger;
    private readonly bool _createWatcherWhenRepositoryAvailable;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _lifetimeSync = new();
    private readonly object _runtimeSync = new();
    private RepositoryWatcher? _watcher;
    private GitAvailability? _cachedAvailability;
    private IGitCliRunner? _runner;
    private Task? _retirementTask;
    private int _configurationRevision;
    private int _lifetimeState;
    private int _resourcesDisposed;

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
            policyNoticeSink: null,
            beforeHygieneFileReplace: null,
            beforeHygieneFileCommit: null,
            logger: null)
    {
    }

    internal GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository,
        Func<bool> isWorktreeMutationAllowed,
        Func<VersionControlPolicyNotice, CancellationToken, Task>? policyNoticeSink = null)
        : this(
            installationLocator,
            repository,
            repository is null ? null : new RepositoryWatcher(repository),
            static gitPath => new GitCliRunner(gitPath),
            createWatcherWhenRepositoryAvailable: true,
            isWorktreeMutationAllowed: isWorktreeMutationAllowed,
            policyNoticeSink: policyNoticeSink,
            beforeHygieneFileReplace: null,
            beforeHygieneFileCommit: null,
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
        Func<string, CancellationToken, Task>? beforeHygieneFileCommit = null,
        Func<VersionControlPolicyNotice, CancellationToken, Task>? policyNoticeSink = null)
        : this(
            installationLocator,
            repository,
            watcher,
            runnerFactory,
            createWatcherWhenRepositoryAvailable: false,
            isWorktreeMutationAllowed: static () => true,
            policyNoticeSink,
            beforeHygieneFileReplace: beforeHygieneFileReplace,
            beforeHygieneFileCommit: beforeHygieneFileCommit,
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
        Func<VersionControlPolicyNotice, CancellationToken, Task>? policyNoticeSink,
        Func<string, CancellationToken, Task>? beforeHygieneFileReplace,
        Func<string, CancellationToken, Task>? beforeHygieneFileCommit,
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
        _policyNoticeSink = policyNoticeSink;
        _beforeHygieneFileReplace = beforeHygieneFileReplace;
        _beforeHygieneFileCommit = beforeHygieneFileCommit;
        _logger = logger ?? Log.CreateLogger<GitCliVersionControlService>();
        _createWatcherWhenRepositoryAvailable = createWatcherWhenRepositoryAvailable;
        if (_watcher is not null)
        {
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
            async () =>
            {
                RepositoryInfo repository = GetRepository();
                (GitAvailability availability, IGitCliRunner? runner)
                    = await GetGitRuntimeCoreAsync(cancellationToken).ConfigureAwait(false);
                if (availability.State != GitAvailabilityState.Installed || runner is null)
                {
                    throw new InvalidOperationException("Git is not available.");
                }

                await EnsureRepositoryHygienePreflightCoreAsync(
                        repository,
                        runner,
                        cancellationToken)
                    .ConfigureAwait(false);
                bool useLfs = _installationLocator.Config.UseLfsWhenAvailable
                              && availability.LfsInstalled;
                await EnsureRepositoryHygieneCoreAsync(
                        repository,
                        runner,
                        useLfs,
                        cancellationToken)
                    .ConfigureAwait(false);
            },
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

    public Task<CheckedOutBranchTip> GetCheckedOutBranchTipAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            () => GetCheckedOutBranchTipCoreAsync(cancellationToken),
            cancellationToken);
    }

    public Task<ProjectCheckpoint> CreateProjectCheckpointAsync(
        string message,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return RunSerializedAsync(
            () => CreateProjectCheckpointCoreAsync(message, cancellationToken),
            cancellationToken);
    }

    public Task RestoreProjectCheckpointAsync(
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(checkpoint);
        return RunSerializedAsync(
            () => RestoreProjectCheckpointCoreAsync(checkpoint, cancellationToken),
            cancellationToken);
    }

    public Task<CommitResult> CommitProjectTreeAsync(
        CheckedOutBranchTip expectedCurrent,
        string sourceCommit,
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        GitRevisionValidator.ValidateCommitId(sourceCommit, nameof(sourceCommit));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return RunSerializedAsync(
            () => CommitProjectTreeCoreAsync(
                expectedCurrent,
                sourceCommit,
                message,
                kind,
                cancellationToken),
            cancellationToken);
    }

    public Task<BranchTipRollbackResult> TryRollbackBranchTipAsync(
        CheckedOutBranchTip expectedCurrent,
        CheckedOutBranchTip target,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(target);
        return RunSerializedAsync(
            () => TryRollbackBranchTipCoreAsync(expectedCurrent, target, cancellationToken),
            cancellationToken);
    }

    public Task<bool> DeleteProjectCheckpointAsync(
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(checkpoint);
        return RunSerializedAsync(
            () => DeleteProjectCheckpointCoreAsync(checkpoint, cancellationToken),
            cancellationToken);
    }

    public Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            () => GetStatusCoreAsync(cancellationToken),
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

    public Task CreateBranchAsync(
        string name,
        string startPoint,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        GitRevisionValidator.ValidateCommitId(startPoint, nameof(startPoint));
        return RunSerializedAsync(
            () => CreateBranchCoreAsync(name, startPoint, cancellationToken),
            cancellationToken);
    }

    public Task SwitchBranchAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return RunSerializedAsync(
            () => SwitchBranchCoreAsync(name, cancellationToken),
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
        ValidateRemoteUrl(url);
        return RunSerializedAsync(
            () => SetRemoteCoreAsync(url, cancellationToken),
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

    public Task<FastForwardPullResult> PullFastForwardAsync(
        CheckedOutBranchTip expectedCurrent,
        ProjectCheckpoint? checkpoint,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        return RunSerializedAsync(
            () => PullFastForwardCoreAsync(expectedCurrent, checkpoint, cancellationToken),
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

    public Task<bool> RemoveRecoverableLockAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            async () =>
            {
                RepositoryInfo repository = GetRepository();
                IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
                RepositoryLockInfo? lockInfo = RecoverableLock;
                if (lockInfo is null)
                {
                    return false;
                }

                bool removed = runner.RemoveRecoverableRepositoryLock(repository, lockInfo);
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

    public Task RetireAsync(ProjectVersionControlFinalSnapshot? finalSnapshot)
    {
        lock (_lifetimeSync)
        {
            if (_retirementTask is not null)
            {
                return _retirementTask;
            }

            if ((ServiceLifetimeState)_lifetimeState == ServiceLifetimeState.Retired)
            {
                return Task.CompletedTask;
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
        catch (GitOperationException ex)
        {
            CaptureRecoverableLock(ex);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task RetireCoreAsync(ProjectVersionControlFinalSnapshot? finalSnapshot)
    {
        await Task.Yield();
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (finalSnapshot is not null && Repository is not null)
            {
                await CommitAllCoreAsync(
                        finalSnapshot.Message,
                        finalSnapshot.Kind,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (GitOperationException ex)
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

    internal static WorkspaceStatus ParseStatus(string output)
    {
        string? branch = null;
        int ahead = 0;
        int behind = 0;
        bool hasConflicts = false;
        var changes = new List<FileChange>();
        IReadOnlyList<string> records = GitCliRunner.SplitNullSeparated(output);

        for (int index = 0; index < records.Count; index++)
        {
            string record = records[index];
            if (record.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                string head = record["# branch.head ".Length..];
                branch = head == "(detached)" ? null : head;
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
                string path = GetTailAfterSpaces(record, 9);
                string? oldPath = ++index < records.Count ? records[index] : null;
                changes.Add(new FileChange(path, FileChangeStatus.Renamed, oldPath));
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

        return new WorkspaceStatus(branch, ahead, behind, changes, hasConflicts);
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
                changes.Add(new FileChange(path, FileChangeStatus.Renamed, oldPath));
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
        if (statusCode.Contains('R') || statusCode.Contains('C'))
        {
            return FileChangeStatus.Renamed;
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

    private async Task<ProjectCheckpoint> CreateProjectCheckpointCoreAsync(
        string message,
        CancellationToken cancellationToken)
    {
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        CheckedOutBranchTip baseHead = await GetCheckedOutBranchTipCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (!await IsProjectIndexCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new ProjectCheckpointStagedChangesException();
        }

        GitIdentity? identity = await GetIdentityCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            await RaiseMissingIdentityNoticeIfNeededAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            throw new GitIdentityRequiredException();
        }

        string temporaryIndex = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-index-{Guid.NewGuid():N}");
        var indexOptions = new GitCommandOptions(
            GitCommandExecutionKind.Local,
            new Dictionary<string, string?>
            {
                ["GIT_INDEX_FILE"] = temporaryIndex,
            });

        try
        {
            await runner.RunAsync(
                repository,
                ["read-tree", baseHead.Commit],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            await runner.RunAsync(
                repository,
                ["add", "-A", "--", repository.Pathspec],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            GitCommandResult tree = await runner.RunAsync(
                repository,
                ["write-tree"],
                indexOptions,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            GitCommandResult commit = await runner.RunAsync(
                repository,
                [
                    "commit-tree",
                    tree.Stdout.Trim(),
                    "-p",
                    baseHead.Commit,
                    "-m",
                    message.Trim(),
                    "-m",
                    "Beutl-Snapshot: safety",
                ],
                indexOptions,
                CancellationToken.None).ConfigureAwait(false);
            string checkpointCommit = commit.Stdout.Trim();
            string checkpointRef = GetCheckpointRefPrefix(repository)
                                   + Guid.NewGuid().ToString("N");
            var checkpoint = new ProjectCheckpoint(checkpointRef, checkpointCommit, baseHead);
            try
            {
                await runner.RunAsync(
                    repository,
                    [
                        "update-ref",
                        "--create-reflog",
                        "-m",
                        "beutl safety checkpoint",
                        checkpointRef,
                        checkpointCommit,
                        string.Empty,
                    ],
                    GitCommandOptions.Local,
                    CancellationToken.None).ConfigureAwait(false);
                return checkpoint;
            }
            catch (Exception publicationException)
            {
                string? observedCommit;
                try
                {
                    observedCommit = await TryResolveCommitAsync(
                            repository,
                            runner,
                            checkpointRef,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception observationException)
                {
                    throw new AggregateException(
                        "The safety checkpoint ref publication failed and its durable result could not be observed.",
                        publicationException,
                        observationException);
                }

                if (string.Equals(
                        observedCommit,
                        checkpointCommit,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return checkpoint;
                }

                if (observedCommit is null)
                {
                    throw;
                }

                throw new ProjectCheckpointChangedException(checkpointRef);
            }
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
        }
    }

    private async Task RestoreProjectCheckpointCoreAsync(
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        EnsureWorktreeMutationAllowed();
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await ValidateCheckpointAsync(repository, runner, checkpoint, cancellationToken)
            .ConfigureAwait(false);
        CheckedOutBranchTip currentHead = await GetCheckedOutBranchTipCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(currentHead, checkpoint.BaseTip))
        {
            throw new InvalidOperationException(
                "The project checkpoint can only be restored directly at its original head.");
        }

        WorktreeStateFingerprint currentState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                checkpoint.BaseTip.Commit,
                repository.Pathspec,
                cancellationToken)
            .ConfigureAwait(false);
        string checkpointTree = await ResolveTreeAsync(
                repository,
                runner,
                checkpoint.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(currentState.Tree, checkpointTree, StringComparison.OrdinalIgnoreCase)
            && await IsProjectIndexCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        if (!await IsProjectCleanAsync(repository, runner, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The project must be clean before restoring a project checkpoint.");
        }

        string baseTree = await ResolveTreeAsync(
                repository,
                runner,
                checkpoint.BaseTip.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(currentState.Tree, baseTree, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectCheckpointStateChangedException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        CheckedOutBranchTip ownershipTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        WorktreeStateFingerprint ownershipState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                checkpoint.BaseTip.Commit,
                repository.Pathspec,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(ownershipTip, checkpoint.BaseTip)
            || ownershipState != currentState)
        {
            throw new InvalidOperationException(
                "The project changed before its checkpoint could be restored.");
        }

        TreeTransitionResult transitionResult = await ApplyTreeTransitionAsync(
            repository,
            runner,
            checkpoint.BaseTip,
            checkpoint.BaseTip,
            checkpoint.BaseTip.Commit,
            checkpoint.Commit,
            "beutl restore project checkpoint",
            new TreeTransitionIndexPlan(
                FinalCommit: checkpoint.BaseTip.Commit,
                RestoreCommit: checkpoint.BaseTip.Commit,
                Pathspec: repository.Pathspec),
            CancellationToken.None).ConfigureAwait(false);
        EnsureTreeTransitionApplied(
            transitionResult,
            "The project checkpoint could not be restored safely.");
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
    }

    private async Task<CommitResult> CommitProjectTreeCoreAsync(
        CheckedOutBranchTip expectedCurrent,
        string sourceCommit,
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        GitRevisionValidator.ValidateCommitId(sourceCommit, nameof(sourceCommit));
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        EnsureWorktreeMutationAllowed();
        ValidateAttachedBranchTip(expectedCurrent, nameof(expectedCurrent));
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        CheckedOutBranchTip currentTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(currentTip, expectedCurrent))
        {
            throw new InvalidOperationException(
                "The checked-out branch changed before the project tree transition started.");
        }

        string? resolvedSource = await TryResolveCommitAsync(
                repository,
                runner,
                sourceCommit,
                cancellationToken)
            .ConfigureAwait(false);
        if (resolvedSource is null)
        {
            throw new ArgumentException(
                "The project tree source must resolve to a commit.",
                nameof(sourceCommit));
        }

        if (!await IsProjectCleanAsync(repository, runner, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The project must be clean before committing a project tree transition.");
        }

        GitIdentity? identity = await GetIdentityCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            if (kind != SnapshotKind.Manual)
            {
                await RaiseMissingIdentityNoticeIfNeededAsync(
                        repository,
                        runner,
                        cancellationToken)
                    .ConfigureAwait(false);
                return new CommitResult.SkippedNoIdentity();
            }

            throw new GitIdentityRequiredException();
        }

        WorktreeStateFingerprint expectedState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                repository.Pathspec,
                cancellationToken)
            .ConfigureAwait(false);
        string expectedTree = await ResolveTreeAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(expectedState.Tree, expectedTree, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The project index or worktree changed before the project tree transition started.");
        }

        string desiredTree = await BuildProjectTreeAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                resolvedSource,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(desiredTree, expectedTree, StringComparison.OrdinalIgnoreCase))
        {
            return new CommitResult.NoChanges();
        }

        GitCommandResult commit = await runner.RunAsync(
            repository,
            [
                "commit-tree",
                desiredTree,
                "-p",
                expectedCurrent.Commit,
                "-m",
                message.Trim(),
                "-m",
                $"Beutl-Snapshot: {kind.ToString().ToLowerInvariant()}",
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        var committedTip = new CheckedOutBranchTip(
            expectedCurrent.RefName,
            commit.Stdout.Trim());

        cancellationToken.ThrowIfCancellationRequested();
        CheckedOutBranchTip ownershipTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        WorktreeStateFingerprint ownershipState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                repository.Pathspec,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(ownershipTip, expectedCurrent)
            || ownershipState != expectedState
            || !await IsProjectCleanAsync(repository, runner, CancellationToken.None)
                .ConfigureAwait(false))
        {
            throw new ProjectCheckpointStateChangedException();
        }

        TreeTransitionResult applyResult = await ApplyTreeTransitionAsync(
            repository,
            runner,
            expectedCurrent,
            committedTip,
            expectedCurrent.Commit,
            committedTip.Commit,
            $"commit: {message.Trim()}",
            new TreeTransitionIndexPlan(Pathspec: repository.Pathspec),
            CancellationToken.None).ConfigureAwait(false);
        EnsureTreeTransitionApplied(
            applyResult,
            "The project tree transition could not be applied safely.");
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new CommitResult.Committed(new CommitRevision.Known(committedTip.Commit));
    }

    private async Task<BranchTipRollbackResult> TryRollbackBranchTipCoreAsync(
        CheckedOutBranchTip expectedCurrent,
        CheckedOutBranchTip target,
        CancellationToken cancellationToken)
    {
        EnsureWorktreeMutationAllowed();
        ValidateBranchTipForRollback(expectedCurrent, target);
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        CheckedOutBranchTip? actualHead = await TryGetCheckedOutBranchTipAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (actualHead is null
            || !string.Equals(actualHead.RefName, expectedCurrent.RefName, StringComparison.Ordinal)
            || !string.Equals(actualHead.Commit, expectedCurrent.Commit, StringComparison.OrdinalIgnoreCase))
        {
            return new BranchTipRollbackResult.RefChanged(actualHead?.Commit);
        }

        if (!await IsAncestorAsync(
                repository,
                runner,
                target.Commit,
                expectedCurrent.Commit,
                cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException(
                "The rollback target must be an ancestor of the expected current head.",
                nameof(target));
        }

        if (!await IsWholeRepositoryCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            return new BranchTipRollbackResult.UnsafeRepositoryState();
        }

        WorktreeStateFingerprint expectedWorktree = await CaptureWorktreeStateAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                ".",
                cancellationToken)
            .ConfigureAwait(false);
        string expectedTree = await ResolveTreeAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(expectedWorktree.Tree, expectedTree, StringComparison.OrdinalIgnoreCase)
            || !await IsWholeRepositoryCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            return new BranchTipRollbackResult.UnsafeRepositoryState();
        }

        cancellationToken.ThrowIfCancellationRequested();
        TreeTransitionResult rollbackResult = await ApplyTreeTransitionAsync(
            repository,
            runner,
            expectedCurrent,
            target,
            expectedCurrent.Commit,
            target.Commit,
            "beutl rollback fast-forward pull",
            indexPlan: null,
            CancellationToken.None).ConfigureAwait(false);
        if (rollbackResult.Outcome == TreeTransitionOutcome.OwnershipLost)
        {
            return new BranchTipRollbackResult.RefChanged(
                rollbackResult.ActualTip?.Commit);
        }

        if (rollbackResult.Outcome != TreeTransitionOutcome.AppliedTarget)
        {
            if (rollbackResult.Outcome == TreeTransitionOutcome.RecoveryFailed
                && rollbackResult.Error is not null)
            {
                throw rollbackResult.Error;
            }

            return new BranchTipRollbackResult.UnsafeRepositoryState();
        }

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new BranchTipRollbackResult.RolledBack();
    }

    private async Task<bool> DeleteProjectCheckpointCoreAsync(
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        ValidateCheckpointRef(repository, checkpoint);
        string? currentCommit = await TryResolveCommitAsync(
                repository,
                runner,
                checkpoint.RefName,
                cancellationToken)
            .ConfigureAwait(false);
        if (currentCommit is null)
        {
            return false;
        }

        if (!string.Equals(currentCommit, checkpoint.Commit, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectCheckpointChangedException(checkpoint.RefName);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await runner.RunAsync(
            repository,
            ["update-ref", "-d", checkpoint.RefName, checkpoint.Commit],
            GitCommandOptions.Local,
            CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    private static async Task ValidateCheckpointAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ValidateCheckpointRef(repository, checkpoint);
        string? currentCommit = await TryResolveCommitAsync(
                repository,
                runner,
                checkpoint.RefName,
                cancellationToken)
            .ConfigureAwait(false);
        string? parentCommit = await TryResolveCommitAsync(
                repository,
                runner,
                $"{checkpoint.Commit}^1",
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(currentCommit, checkpoint.Commit, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                parentCommit,
                checkpoint.BaseTip.Commit,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectCheckpointChangedException(checkpoint.RefName);
        }
    }

    private static void ValidateCheckpointRef(
        RepositoryInfo repository,
        ProjectCheckpoint checkpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.RefName);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.Commit);
        ArgumentNullException.ThrowIfNull(checkpoint.BaseTip);
        if (!checkpoint.RefName.StartsWith(
                GetCheckpointRefPrefix(repository),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The checkpoint does not belong to this project.",
                nameof(checkpoint));
        }
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
        ArgumentException.ThrowIfNullOrWhiteSpace(tip.RefName, paramName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tip.Commit, paramName);
        if (!tip.RefName.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            throw new ArgumentException("An attached local branch tip is required.", paramName);
        }
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

    private static async Task<WorktreeStateFingerprint> CaptureWorktreeStateAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string baseCommit,
        string pathspec,
        CancellationToken cancellationToken)
    {
        string temporaryIndex = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-index-{Guid.NewGuid():N}");
        var indexOptions = new GitCommandOptions(
            GitCommandExecutionKind.Local,
            new Dictionary<string, string?>
            {
                ["GIT_INDEX_FILE"] = temporaryIndex,
            });

        try
        {
            await runner.RunAsync(
                repository,
                ["read-tree", baseCommit],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            await runner.RunAsync(
                repository,
                ["add", "-A", "--", pathspec],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            GitCommandResult tree = await runner.RunAsync(
                repository,
                ["write-tree"],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            GitCommandResult indexEntries = await runner.RunAsync(
                repository,
                ["ls-files", "--stage", "-z", "--", pathspec],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return new WorktreeStateFingerprint(tree.Stdout.Trim(), indexEntries.Stdout);
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
        }
    }

    private static async Task<string> BuildProjectTreeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string baseCommit,
        string sourceCommit,
        CancellationToken cancellationToken)
    {
        string temporaryIndex = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-index-{Guid.NewGuid():N}");
        var indexOptions = new GitCommandOptions(
            GitCommandExecutionKind.Local,
            new Dictionary<string, string?>
            {
                ["GIT_INDEX_FILE"] = temporaryIndex,
            });

        try
        {
            await runner.RunAsync(
                repository,
                ["read-tree", baseCommit],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            await runner.RunAsync(
                repository,
                [
                    "restore",
                    $"--source={sourceCommit}",
                    "--staged",
                    "--",
                    repository.Pathspec,
                ],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            GitCommandResult tree = await runner.RunAsync(
                repository,
                ["write-tree"],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            return tree.Stdout.Trim();
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
        }
    }

    private static async Task<string> BuildMergedTreeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string mergeBase,
        string currentCommit,
        string incomingCommit,
        CancellationToken cancellationToken)
    {
        string temporaryIndex = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-index-{Guid.NewGuid():N}");
        var indexOptions = new GitCommandOptions(
            GitCommandExecutionKind.Local,
            new Dictionary<string, string?>
            {
                ["GIT_INDEX_FILE"] = temporaryIndex,
            });

        try
        {
            await runner.RunAsync(
                repository,
                ["read-tree", "-m", mergeBase, currentCommit, incomingCommit],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            GitCommandResult tree = await runner.RunAsync(
                repository,
                ["write-tree"],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            return tree.Stdout.Trim();
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
        }
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

    private static async Task<string?> FindIgnoredIncomingPathAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string currentCommit,
        string incomingCommit,
        CancellationToken cancellationToken)
    {
        GitCommandResult changed = await runner.RunAsync(
            repository,
            [
                "diff",
                "--name-only",
                "--diff-filter=ACR",
                "-z",
                currentCommit,
                incomingCommit,
                "--",
                ".",
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> changedPaths = GitCliRunner.SplitNullSeparated(changed.Stdout);
        string repositoryRoot = Path.GetFullPath(repository.RepoRoot);
        string repositoryPrefix = Path.TrimEndingDirectorySeparator(repositoryRoot)
                                  + Path.DirectorySeparatorChar;
        string[] existingPaths = changedPaths
            .Where(path =>
            {
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, path));
                }
                catch (Exception ex) when (ex is ArgumentException
                                               or NotSupportedException
                                               or PathTooLongException)
                {
                    return false;
                }

                return fullPath.StartsWith(repositoryPrefix, PathComparison)
                       && (File.Exists(fullPath) || Directory.Exists(fullPath));
            })
            .ToArray();
        if (existingPaths.Length == 0)
        {
            return null;
        }

        string input = string.Join('\0', existingPaths) + '\0';
        try
        {
            GitCommandResult ignored = await runner.RunAsync(
                repository,
                ["check-ignore", "--stdin", "-z"],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    StandardInput: input,
                    UseLiteralPathspecs: false),
                cancellationToken).ConfigureAwait(false);
            return GitCliRunner.SplitNullSeparated(ignored.Stdout).FirstOrDefault();
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return null;
        }
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

    private async Task<TreeTransitionResult> ApplyTreeTransitionAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CheckedOutBranchTip currentHead,
        CheckedOutBranchTip targetHead,
        string currentTreeCommit,
        string targetTreeCommit,
        string reflogMessage,
        TreeTransitionIndexPlan? indexPlan,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(currentHead.RefName, targetHead.RefName, StringComparison.Ordinal))
        {
            throw new ArgumentException("A tree transition must remain on the same local branch.");
        }

        string headPath = await ResolveGitPathAsync(
                repository,
                runner,
                "HEAD",
                cancellationToken)
            .ConfigureAwait(false);
        string indexPath = await ResolveGitPathAsync(
                repository,
                runner,
                "index",
                cancellationToken)
            .ConfigureAwait(false);
        string refUpdateWorktreePath = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-ref-update-{Guid.NewGuid():N}");
        try
        {
            await runner.RunAsync(
                repository,
                [
                    "worktree",
                    "add",
                    "--detach",
                    "--no-checkout",
                    refUpdateWorktreePath,
                    currentTreeCommit,
                ],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RemoveRefUpdateWorktreeBestEffortAsync(
                    repository,
                    runner,
                    refUpdateWorktreePath)
                .ConfigureAwait(false);
            return new TreeTransitionResult(
                TreeTransitionOutcome.RestoredCurrent,
                ex,
                currentHead);
        }

        var refUpdateRepository = new RepositoryInfo(
            refUpdateWorktreePath,
            refUpdateWorktreePath);
        var transitionCheckoutOptions = new GitCommandOptions(
            GitCommandExecutionKind.Local,
            new Dictionary<string, string?>
            {
                ["GIT_WORK_TREE"] = repository.RepoRoot,
                ["GIT_INDEX_FILE"] = indexPath,
            });
        bool mutationStarted = false;
        try
        {
            using HeadOwnershipLease lease = HeadOwnershipLease.Acquire(
                headPath,
                currentHead.RefName,
                ex => LogWarningBestEffort(
                    ex,
                    "Failed to release the protected Git HEAD lock."));
            CheckedOutBranchTip actualHead = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!EqualsBranchTip(actualHead, currentHead))
            {
                return new TreeTransitionResult(
                    TreeTransitionOutcome.OwnershipLost,
                    ActualTip: actualHead);
            }

            WorktreeStateFingerprint originalState = await CaptureWorktreeStateAsync(
                    repository,
                    runner,
                    currentTreeCommit,
                    indexPlan?.Pathspec ?? ".",
                    CancellationToken.None)
                .ConfigureAwait(false);
            string currentTree = await ResolveTreeAsync(
                    repository,
                    runner,
                    currentTreeCommit,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!string.Equals(originalState.Tree, currentTree, StringComparison.OrdinalIgnoreCase))
            {
                return new TreeTransitionResult(TreeTransitionOutcome.OwnershipLost);
            }

            WorktreeStateFingerprint preparedState = originalState;
            bool worktreeMutationAttempted = false;
            bool targetPrepared = false;
            try
            {
                if (indexPlan?.PrepareCommit is { } prepareCommit)
                {
                    mutationStarted = true;
                    await ResetIndexAsync(
                            repository,
                            runner,
                            prepareCommit,
                            indexPlan.Pathspec)
                        .ConfigureAwait(false);
                    preparedState = await CaptureWorktreeStateAsync(
                            repository,
                            runner,
                            currentTreeCommit,
                            indexPlan.Pathspec,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }

                string? ignoredCollision = await FindIgnoredIncomingPathAsync(
                        repository,
                        runner,
                        currentTreeCommit,
                        targetTreeCommit,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (ignoredCollision is not null)
                {
                    throw new InvalidOperationException(
                        $"The tree transition would overwrite the ignored path '{ignoredCollision}'.");
                }

                mutationStarted = true;
                worktreeMutationAttempted = true;
                await runner.RunAsync(
                    refUpdateRepository,
                    [
                        "-c",
                        "core.hooksPath=/dev/null",
                        "checkout",
                        "--detach",
                        "--no-overwrite-ignore",
                        targetTreeCommit,
                    ],
                    transitionCheckoutOptions,
                    CancellationToken.None).ConfigureAwait(false);

                if (indexPlan?.FinalCommit is { } finalCommit)
                {
                    await ResetIndexAsync(
                            repository,
                            runner,
                            finalCommit,
                            indexPlan.Pathspec)
                        .ConfigureAwait(false);
                }

                WorktreeStateFingerprint targetState = await CaptureWorktreeStateAsync(
                        repository,
                        runner,
                        targetTreeCommit,
                        indexPlan?.Pathspec ?? ".",
                        CancellationToken.None)
                    .ConfigureAwait(false);
                string targetTree = await ResolveTreeAsync(
                        repository,
                        runner,
                        targetTreeCommit,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                string expectedIndexCommit = indexPlan?.FinalCommit ?? targetTreeCommit;
                if (!string.Equals(targetState.Tree, targetTree, StringComparison.OrdinalIgnoreCase)
                    || !await IsIndexAtCommitAsync(
                            repository,
                            runner,
                            expectedIndexCommit,
                            indexPlan?.Pathspec ?? ".",
                            CancellationToken.None)
                        .ConfigureAwait(false))
                {
                    throw new ProjectCheckpointStateChangedException();
                }

                targetPrepared = true;

                string? branchCommit = await TryResolveCommitAsync(
                        repository,
                        runner,
                        currentHead.RefName,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!string.Equals(
                        branchCommit,
                        currentHead.Commit,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new TreeTransitionResult(
                        TreeTransitionOutcome.OwnershipLost,
                        ActualTip: await TryGetCheckedOutBranchTipAsync(
                                repository,
                                runner,
                                CancellationToken.None)
                            .ConfigureAwait(false));
                }

                await runner.RunAsync(
                    refUpdateRepository,
                    [
                        "update-ref",
                        "-m",
                        reflogMessage,
                        currentHead.RefName,
                        targetHead.Commit,
                        currentHead.Commit,
                    ],
                    GitCommandOptions.Local,
                    CancellationToken.None).ConfigureAwait(false);
                return new TreeTransitionResult(TreeTransitionOutcome.AppliedTarget);
            }
            catch (Exception transitionException)
            {
                string? branchCommit = await TryResolveCommitAsync(
                        repository,
                        runner,
                        currentHead.RefName,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (string.Equals(
                        branchCommit,
                        targetHead.Commit,
                        StringComparison.OrdinalIgnoreCase)
                    && targetPrepared)
                {
                    return new TreeTransitionResult(TreeTransitionOutcome.AppliedTarget);
                }

                if (!string.Equals(
                        branchCommit,
                        currentHead.Commit,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new TreeTransitionResult(
                        TreeTransitionOutcome.OwnershipLost,
                        transitionException,
                        await TryGetCheckedOutBranchTipAsync(
                                repository,
                                runner,
                                CancellationToken.None)
                            .ConfigureAwait(false));
                }

                try
                {
                    WorktreeStateFingerprint failedState = await CaptureWorktreeStateAsync(
                            repository,
                            runner,
                            currentTreeCommit,
                            indexPlan?.Pathspec ?? ".",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    string targetTree = await ResolveTreeAsync(
                            repository,
                            runner,
                            targetTreeCommit,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    bool worktreeOwned = string.Equals(
                                             failedState.Tree,
                                             originalState.Tree,
                                             StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(
                                             failedState.Tree,
                                             targetTree,
                                             StringComparison.OrdinalIgnoreCase);
                    bool indexOwned = string.Equals(
                                          failedState.IndexEntries,
                                          originalState.IndexEntries,
                                          StringComparison.Ordinal)
                                      || string.Equals(
                                          failedState.IndexEntries,
                                          preparedState.IndexEntries,
                                          StringComparison.Ordinal)
                                      || await IsIndexAtCommitAsync(
                                              repository,
                                              runner,
                                              targetTreeCommit,
                                              indexPlan?.Pathspec ?? ".",
                                              CancellationToken.None)
                                          .ConfigureAwait(false)
                                      || (indexPlan?.PrepareCommit is { } expectedPrepareCommit
                                          && await IsIndexAtCommitAsync(
                                                  repository,
                                                  runner,
                                                  expectedPrepareCommit,
                                                  indexPlan.Pathspec,
                                                  CancellationToken.None)
                                              .ConfigureAwait(false))
                                      || (indexPlan?.FinalCommit is { } expectedFinalCommit
                                          && await IsIndexAtCommitAsync(
                                                  repository,
                                                  runner,
                                                  expectedFinalCommit,
                                                  indexPlan.Pathspec,
                                                  CancellationToken.None)
                                              .ConfigureAwait(false));
                    if (!indexOwned)
                    {
                        return new TreeTransitionResult(
                            TreeTransitionOutcome.OwnershipLost,
                            transitionException,
                            currentHead);
                    }

                    if (!worktreeOwned)
                    {
                        string refusedRestoreCommit = indexPlan?.RestoreCommit ?? currentTreeCommit;
                        await ResetIndexAsync(
                                repository,
                                runner,
                                refusedRestoreCommit,
                                indexPlan?.Pathspec ?? ".")
                            .ConfigureAwait(false);
                        WorktreeStateFingerprint refusedState = await CaptureWorktreeStateAsync(
                                repository,
                                runner,
                                currentTreeCommit,
                                indexPlan?.Pathspec ?? ".",
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        if (!string.Equals(
                                refusedState.IndexEntries,
                                originalState.IndexEntries,
                                StringComparison.Ordinal))
                        {
                            return new TreeTransitionResult(
                                TreeTransitionOutcome.RecoveryFailed,
                                new AggregateException(
                                    "The checkout was refused and the original index could not be restored.",
                                    transitionException),
                                currentHead);
                        }

                        CheckedOutBranchTip? refusedTip = await TryGetCheckedOutBranchTipAsync(
                                repository,
                                runner,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        return new TreeTransitionResult(
                            TreeTransitionOutcome.OwnershipLost,
                            transitionException,
                            refusedTip);
                    }

                    if (worktreeMutationAttempted
                        && string.Equals(
                            failedState.Tree,
                            targetTree,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        string? transitionHead = await TryResolveCommitAsync(
                                refUpdateRepository,
                                runner,
                                "HEAD",
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        if (string.Equals(
                                transitionHead,
                                currentTreeCommit,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                await runner.RunAsync(
                                    refUpdateRepository,
                                    [
                                        "update-ref",
                                        "--no-deref",
                                        "-m",
                                        "beutl align temporary transition head for recovery",
                                        "HEAD",
                                        targetTreeCommit,
                                        currentTreeCommit,
                                    ],
                                    GitCommandOptions.Local,
                                    CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (Exception alignmentException)
                            {
                                transitionHead = await TryResolveCommitAsync(
                                        refUpdateRepository,
                                        runner,
                                        "HEAD",
                                        CancellationToken.None)
                                    .ConfigureAwait(false);
                                if (string.Equals(
                                        transitionHead,
                                        targetTreeCommit,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    // The update reached Git even though the runner lost its response.
                                }
                                else if (string.Equals(
                                             transitionHead,
                                             currentTreeCommit,
                                             StringComparison.OrdinalIgnoreCase))
                                {
                                    return new TreeTransitionResult(
                                        TreeTransitionOutcome.RecoveryFailed,
                                        new AggregateException(
                                            "The temporary transition head could not be aligned for recovery.",
                                            transitionException,
                                            alignmentException),
                                        currentHead);
                                }
                                else
                                {
                                    return new TreeTransitionResult(
                                        TreeTransitionOutcome.OwnershipLost,
                                        new AggregateException(
                                            "The temporary transition head changed while recovery was being prepared.",
                                            transitionException,
                                            alignmentException),
                                        currentHead);
                                }
                            }
                        }
                        else if (!string.Equals(
                                     transitionHead,
                                     targetTreeCommit,
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            return new TreeTransitionResult(
                                TreeTransitionOutcome.OwnershipLost,
                                transitionException,
                                currentHead);
                        }

                        await ResetIndexAsync(
                                repository,
                                runner,
                                targetTreeCommit,
                                indexPlan?.Pathspec ?? ".")
                            .ConfigureAwait(false);
                        await runner.RunAsync(
                            refUpdateRepository,
                            [
                                "-c",
                                "core.hooksPath=/dev/null",
                                "checkout",
                                "--detach",
                                "--no-overwrite-ignore",
                                currentTreeCommit,
                            ],
                            transitionCheckoutOptions,
                            CancellationToken.None).ConfigureAwait(false);
                    }

                    if (indexPlan?.RestoreCommit is { } restoreCommit)
                    {
                        await ResetIndexAsync(
                                repository,
                                runner,
                                restoreCommit,
                                indexPlan.Pathspec)
                            .ConfigureAwait(false);
                    }
                    else if (!string.Equals(
                                 failedState.IndexEntries,
                                 originalState.IndexEntries,
                                 StringComparison.Ordinal))
                    {
                        await ResetIndexAsync(
                                repository,
                                runner,
                                currentTreeCommit,
                                indexPlan?.Pathspec ?? ".")
                            .ConfigureAwait(false);
                    }

                    WorktreeStateFingerprint recoveredState = await CaptureWorktreeStateAsync(
                            repository,
                            runner,
                            currentTreeCommit,
                            indexPlan?.Pathspec ?? ".",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    string expectedRestoreCommit = indexPlan?.RestoreCommit ?? currentTreeCommit;
                    if (!string.Equals(
                            recoveredState.Tree,
                            currentTree,
                            StringComparison.OrdinalIgnoreCase)
                        || !await IsIndexAtCommitAsync(
                                repository,
                                runner,
                                expectedRestoreCommit,
                                indexPlan?.Pathspec ?? ".",
                                CancellationToken.None)
                            .ConfigureAwait(false))
                    {
                        return new TreeTransitionResult(
                            TreeTransitionOutcome.RecoveryFailed,
                            new AggregateException(
                                "The tree transition failed and the original tree could not be verified.",
                                transitionException),
                            currentHead);
                    }

                    CheckedOutBranchTip? recoveredTip = await TryGetCheckedOutBranchTipAsync(
                            repository,
                            runner,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    if (recoveredTip is null || !EqualsBranchTip(recoveredTip, currentHead))
                    {
                        return new TreeTransitionResult(
                            TreeTransitionOutcome.OwnershipLost,
                            transitionException,
                            recoveredTip);
                    }

                    return new TreeTransitionResult(
                        TreeTransitionOutcome.RestoredCurrent,
                        transitionException,
                        currentHead);
                }
                catch (Exception recoveryException)
                {
                    return new TreeTransitionResult(
                        TreeTransitionOutcome.RecoveryFailed,
                        new AggregateException(
                            "The tree transition failed and its current state could not be restored.",
                            transitionException,
                            recoveryException),
                        currentHead);
                }
            }
        }
        catch (ProjectCheckpointStateChangedException ex)
        {
            return new TreeTransitionResult(
                TreeTransitionOutcome.OwnershipLost,
                ex,
                await TryGetCheckedOutBranchTipAsync(
                        repository,
                        runner,
                        CancellationToken.None)
                    .ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return new TreeTransitionResult(
                mutationStarted
                    ? TreeTransitionOutcome.RecoveryFailed
                    : TreeTransitionOutcome.RestoredCurrent,
                ex,
                currentHead);
        }
        finally
        {
            await RemoveRefUpdateWorktreeBestEffortAsync(
                    repository,
                    runner,
                    refUpdateWorktreePath)
                .ConfigureAwait(false);
        }
    }

    private async Task RemoveRefUpdateWorktreeBestEffortAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string worktreePath)
    {
        Exception? cleanupFailure = null;
        try
        {
            await runner.RunAsync(
                repository,
                ["worktree", "remove", "--force", worktreePath],
                GitCommandOptions.Local,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            cleanupFailure = ex;
        }

        try
        {
            if (Directory.Exists(worktreePath))
            {
                Directory.Delete(worktreePath, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            cleanupFailure = cleanupFailure is null
                ? ex
                : new AggregateException(cleanupFailure, ex);
        }

        if (cleanupFailure is not null)
        {
            LogWarningBestEffort(
                cleanupFailure,
                "Failed to remove a temporary detached Git worktree used for a ref update.");
        }
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

    private static void EnsureTreeTransitionApplied(
        TreeTransitionResult result,
        string message)
    {
        if (result.Outcome == TreeTransitionOutcome.AppliedTarget)
        {
            return;
        }

        if (result.Error is GitOperationException operationException)
        {
            throw operationException;
        }

        throw new InvalidOperationException(
            $"{message} Outcome: {result.Outcome}.",
            result.Error);
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

    private static bool EqualsBranchTip(CheckedOutBranchTip left, CheckedOutBranchTip right)
    {
        return string.Equals(left.RefName, right.RefName, StringComparison.Ordinal)
               && string.Equals(left.Commit, right.Commit, StringComparison.OrdinalIgnoreCase);
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

    private async Task<WorkspaceStatus> GetStatusCoreAsync(CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        return await GetStatusCoreAsync(repository, runner, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WorkspaceStatus> GetStatusCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "status",
                "--porcelain=v2",
                "--branch",
                "--untracked-files=all",
                "-z",
                "--",
                repository.Pathspec,
            ],
            GitCommandOptions.Local,
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
                "log",
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
            ["show", "--name-status", "--format=", "-z", sha, "--", repository.Pathspec],
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
            ["show", "--format=", "--no-ext-diff", "--unified=3", sha, "--", pathspec],
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
            [
                "for-each-ref",
                "--format=%(refname:short)%00%(HEAD)%00%(upstream:short)",
                "refs/heads",
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return ParseBranches(result.Stdout);
    }

    private async Task CreateBranchCoreAsync(
        string name,
        string startPoint,
        CancellationToken cancellationToken)
    {
        GitRevisionValidator.ValidateCommitId(startPoint, nameof(startPoint));
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        EnsureWorktreeMutationAllowed();
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await runner.RunAsync(
            repository,
            ["switch", "-c", name, startPoint],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
    }

    private async Task SwitchBranchCoreAsync(
        string name,
        CancellationToken cancellationToken)
    {
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        EnsureWorktreeMutationAllowed();
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await runner.RunAsync(
            repository,
            ["switch", name],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
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

    private async Task SetRemoteCoreAsync(
        string url,
        CancellationToken cancellationToken)
    {
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        bool isFirstRemote = (await GetRemotesCoreAsync(cancellationToken).ConfigureAwait(false)).Count == 0;
        await runner.RunAsync(
            repository,
            isFirstRemote
                ? ["remote", "add", "origin", url]
                : ["remote", "set-url", "origin", url],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);

        if (isFirstRemote)
        {
            await TryRaiseLfsQuotaNoticeIfNeededAsync(
                repository,
                runner).ConfigureAwait(false);
        }

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
    }

    private async Task<RemoteOpResult> PushCoreAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await runner.RunAsync(
                repository,
                ["push", "--progress", "-u", "origin", "HEAD"],
                GitCommandOptions.Network,
                cancellationToken,
                progress).ConfigureAwait(false);
            await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
            return new RemoteOpResult.Success();
        }
        catch (GitOperationException ex)
        {
            CaptureRecoverableLock(ex);
            return MapRemoteFailure(ex);
        }
    }

    private async Task<FastForwardPullResult> PullFastForwardCoreAsync(
        CheckedOutBranchTip expectedCurrent,
        ProjectCheckpoint? checkpoint,
        CancellationToken cancellationToken)
    {
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        EnsureWorktreeMutationAllowed();
        ValidateAttachedBranchTip(expectedCurrent, nameof(expectedCurrent));
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        CheckedOutBranchTip currentTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(currentTip, expectedCurrent))
        {
            throw new InvalidOperationException(
                "The checked-out branch changed before the fast-forward pull started.");
        }

        WorktreeStateFingerprint? checkpointState = null;
        string? checkpointTree = null;
        if (checkpoint is null)
        {
            if (!await IsWholeRepositoryCleanAsync(repository, runner, cancellationToken)
                    .ConfigureAwait(false))
            {
                return new FastForwardPullResult(
                    new RemoteOpResult.RepositoryDirty(),
                    expectedCurrent);
            }
        }
        else
        {
            await ValidateCheckpointAsync(repository, runner, checkpoint, cancellationToken)
                .ConfigureAwait(false);
            if (!EqualsBranchTip(checkpoint.BaseTip, expectedCurrent))
            {
                throw new InvalidOperationException(
                    "The project checkpoint does not belong to the expected pull tip.");
            }

            checkpointState = await CaptureWorktreeStateAsync(
                    repository,
                    runner,
                    expectedCurrent.Commit,
                    ".",
                    cancellationToken)
                .ConfigureAwait(false);
            checkpointTree = await ResolveTreeAsync(
                    repository,
                    runner,
                    checkpoint.Commit,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    checkpointState.Tree,
                    checkpointTree,
                    StringComparison.OrdinalIgnoreCase)
                || !await IsWholeIndexCleanAsync(repository, runner, cancellationToken)
                    .ConfigureAwait(false))
            {
                return new FastForwardPullResult(
                    new RemoteOpResult.RepositoryDirty(),
                    expectedCurrent);
            }
        }

        try
        {
            await runner.RunAsync(
                repository,
                ["fetch"],
                GitCommandOptions.Network,
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException ex)
        {
            CaptureRecoverableLock(ex);
            return new FastForwardPullResult(MapRemoteFailure(ex), expectedCurrent);
        }

        GitCommandResult upstreamResult;
        try
        {
            upstreamResult = await runner.RunAsync(
                repository,
                ["rev-parse", "--verify", "@{upstream}^{commit}"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException ex)
        {
            CaptureRecoverableLock(ex);
            return new FastForwardPullResult(MapRemoteFailure(ex), expectedCurrent);
        }

        string upstreamCommit = upstreamResult.Stdout.Trim();
        if (!await IsAncestorAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                upstreamCommit,
                cancellationToken).ConfigureAwait(false))
        {
            return new FastForwardPullResult(new RemoteOpResult.Diverged(), expectedCurrent);
        }

        if (string.Equals(
                upstreamCommit,
                expectedCurrent.Commit,
                StringComparison.OrdinalIgnoreCase)
            && checkpoint is null)
        {
            return new FastForwardPullResult(new RemoteOpResult.Success(), expectedCurrent);
        }

        string? ignoredCollision = await FindIgnoredIncomingPathAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                upstreamCommit,
                cancellationToken)
            .ConfigureAwait(false);
        if (ignoredCollision is not null)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(
                    $"The pull would overwrite the ignored path '{ignoredCollision}'."),
                expectedCurrent);
        }

        if (checkpoint is not null)
        {
            return await PullCheckpointedProjectCoreAsync(
                    repository,
                    runner,
                    expectedCurrent,
                    upstreamCommit,
                    checkpoint,
                    checkpointState!,
                    checkpointTree!,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        WorktreeStateFingerprint expectedWorktree = await CaptureWorktreeStateAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                ".",
                cancellationToken)
            .ConfigureAwait(false);
        string expectedTree = await ResolveTreeAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        currentTip = await GetCheckedOutBranchTipCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(currentTip, expectedCurrent))
        {
            throw new InvalidOperationException(
                "The checked-out branch changed while the fast-forward pull was being prepared.");
        }

        if (!string.Equals(expectedWorktree.Tree, expectedTree, StringComparison.OrdinalIgnoreCase)
            || !await IsWholeRepositoryCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            return new FastForwardPullResult(
                new RemoteOpResult.RepositoryDirty(),
                expectedCurrent);
        }

        ignoredCollision = await FindIgnoredIncomingPathAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                upstreamCommit,
                cancellationToken)
            .ConfigureAwait(false);
        if (ignoredCollision is not null)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(
                    $"The pull would overwrite the ignored path '{ignoredCollision}'."),
                expectedCurrent);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var pulledTip = new CheckedOutBranchTip(expectedCurrent.RefName, upstreamCommit);
        TreeTransitionResult transitionResult = await ApplyTreeTransitionAsync(
            repository,
            runner,
            expectedCurrent,
            pulledTip,
            expectedCurrent.Commit,
            upstreamCommit,
            "pull: fast-forward",
            indexPlan: null,
            CancellationToken.None).ConfigureAwait(false);
        if (transitionResult.Outcome != TreeTransitionOutcome.AppliedTarget)
        {
            if (transitionResult.Error is GitOperationException operationException)
            {
                CaptureRecoverableLock(operationException);
            }

            RemoteOpResult failure = transitionResult.Outcome switch
            {
                TreeTransitionOutcome.OwnershipLost => new RemoteOpResult.Failed(
                    transitionResult.Error?.Message
                    ?? "The repository changed while the fast-forward pull was being applied."),
                TreeTransitionOutcome.RestoredCurrent when transitionResult.Error is GitOperationException gitException
                    => MapRemoteFailure(gitException),
                _ => new RemoteOpResult.Failed(
                    transitionResult.Error?.Message
                    ?? "The fast-forward pull could not be applied safely."),
            };
            return new FastForwardPullResult(
                failure,
                transitionResult.ActualTip ?? expectedCurrent,
                transitionResult.Outcome switch
                {
                    TreeTransitionOutcome.OwnershipLost => PullTransitionState.OwnershipLost,
                    TreeTransitionOutcome.RecoveryFailed => PullTransitionState.RecoveryFailed,
                    _ => PullTransitionState.Unchanged,
                });
        }

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new FastForwardPullResult(
            new RemoteOpResult.Success(),
            pulledTip,
            PullTransitionState.Applied);
    }

    private async Task<FastForwardPullResult> PullCheckpointedProjectCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CheckedOutBranchTip expectedCurrent,
        string upstreamCommit,
        ProjectCheckpoint checkpoint,
        WorktreeStateFingerprint expectedCheckpointState,
        string checkpointTree,
        CancellationToken cancellationToken)
    {
        string mergedTree = await BuildMergedTreeAsync(
                repository,
                runner,
                checkpoint.BaseTip.Commit,
                upstreamCommit,
                checkpoint.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        GitCommandResult commit = await runner.RunAsync(
            repository,
            [
                "commit-tree",
                mergedTree,
                "-p",
                upstreamCommit,
                "-m",
                PullSafetyCommitMessage,
                "-m",
                "Beutl-Snapshot: safety",
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        var safetyTip = new CheckedOutBranchTip(expectedCurrent.RefName, commit.Stdout.Trim());

        await ValidateCheckpointAsync(repository, runner, checkpoint, cancellationToken)
            .ConfigureAwait(false);
        CheckedOutBranchTip ownershipTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        WorktreeStateFingerprint ownershipState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                ".",
                cancellationToken)
            .ConfigureAwait(false);
        string? ignoredCollision = await FindIgnoredIncomingPathAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                upstreamCommit,
                cancellationToken)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(ownershipTip, expectedCurrent))
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(
                    "The checked-out branch changed while the checkpointed pull was being prepared."),
                ownershipTip,
                PullTransitionState.OwnershipLost);
        }

        if (ownershipState != expectedCheckpointState
            || !string.Equals(
                ownershipState.Tree,
                checkpointTree,
                StringComparison.OrdinalIgnoreCase)
            || !await IsWholeIndexCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            return new FastForwardPullResult(
                new RemoteOpResult.RepositoryDirty(),
                expectedCurrent);
        }

        if (ignoredCollision is not null)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(
                    $"The pull would overwrite the ignored path '{ignoredCollision}'."),
                expectedCurrent);
        }

        cancellationToken.ThrowIfCancellationRequested();
        TreeTransitionResult transitionResult = await ApplyTreeTransitionAsync(
            repository,
            runner,
            expectedCurrent,
            safetyTip,
            checkpoint.Commit,
            safetyTip.Commit,
            "pull: fast-forward with project checkpoint",
            new TreeTransitionIndexPlan(
                PrepareCommit: checkpoint.Commit,
                RestoreCommit: expectedCurrent.Commit),
            CancellationToken.None).ConfigureAwait(false);
        if (transitionResult.Outcome != TreeTransitionOutcome.AppliedTarget)
        {
            if (transitionResult.Error is GitOperationException gitException)
            {
                CaptureRecoverableLock(gitException);
            }
            return new FastForwardPullResult(
                transitionResult.Outcome == TreeTransitionOutcome.OwnershipLost
                    ? new RemoteOpResult.Failed(
                        transitionResult.Error?.Message
                        ?? "The repository changed while the checkpointed pull was being applied.")
                    : transitionResult.Error is GitOperationException operationException
                        ? MapRemoteFailure(operationException)
                        : new RemoteOpResult.Failed(
                            transitionResult.Error?.Message
                            ?? "The checkpointed pull could not be applied safely."),
                transitionResult.ActualTip ?? expectedCurrent,
                transitionResult.Outcome switch
                {
                    TreeTransitionOutcome.OwnershipLost => PullTransitionState.OwnershipLost,
                    TreeTransitionOutcome.RecoveryFailed => PullTransitionState.RecoveryFailed,
                    _ => PullTransitionState.Unchanged,
                });
        }

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new FastForwardPullResult(
            new RemoteOpResult.Success(),
            safetyTip,
            PullTransitionState.Applied);
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

        if (Repository is not null
            && !string.Equals(Repository.ProjectRoot, projectRoot, PathComparison)
            && !IsSameRepository(Repository, repository))
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
            await runner.RunAsync(
                repository,
                ["init"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            await runner.RunAsync(
                repository,
                ["symbolic-ref", "HEAD", "refs/heads/main"],
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

        bool useLfs = options.UseLfsWhenAvailable && availability.LfsInstalled;
        await EnsureRepositoryHygieneCoreAsync(
                repository,
                runner,
                useLfs,
                cancellationToken)
            .ConfigureAwait(false);

        WorkspaceStatus status = await GetStatusCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        if (!status.IsClean)
        {
            await RaiseLargeMediaNoticeIfNeededAsync(
                    repository,
                    runner,
                    status,
                    cancellationToken)
                .ConfigureAwait(false);
            await runner.RunAsync(
                repository,
                ["add", "-A", "--", repository.Pathspec],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            await runner.RunAsync(
                repository,
                [
                    "commit",
                        "-m",
                        "beutl: initialize version control",
                        "-m",
                        "Beutl-Snapshot: init",
                        "--",
                        repository.Pathspec,
                ],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        }

        TryEnsureWatcher();
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
    }

    private static async Task EnsureInitializationPreflightCoreAsync(
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

    private static async Task EnsureRepositoryHygienePreflightCoreAsync(
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

    private static async Task EnsureRepositoryStatusAndIgnorePreflightCoreAsync(
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
        CancellationToken cancellationToken)
    {
        EnsureHygienePathsAreSafe(repository);
        if (useLfs)
        {
            await runner.RunAsync(
                repository,
                ["lfs", "install", "--local"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        }

        await EnsureLinesAsync(
            Path.Combine(repository.ProjectRoot, ".gitignore"),
            s_gitIgnoreLines,
            cancellationToken).ConfigureAwait(false);
        await EnsureAttributesAsync(
            Path.Combine(repository.ProjectRoot, ".gitattributes"),
            useLfs,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<RepositoryInfo?> DiscoverRepositoryCoreAsync(
        string projectRoot,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string normalizedProjectRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
        var discoveryContext = new RepositoryInfo(normalizedProjectRoot, normalizedProjectRoot);
        try
        {
            GitCommandResult result = await runner.RunAsync(
                discoveryContext,
                ["rev-parse", "--show-toplevel", "--show-prefix"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string[] lines = result.Stdout
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n');
            if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
            {
                throw new InvalidOperationException(
                    "Git repository discovery returned an empty repository root.");
            }

            string repoRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(lines[0]));
            string resolvedProjectRoot = GetDiscoveredProjectRoot(
                repoRoot,
                lines.Length > 1 ? lines[1] : string.Empty);
            return new RepositoryInfo(repoRoot, resolvedProjectRoot);
        }
        catch (GitOperationException ex) when (IsNotRepositoryFailure(ex))
        {
            return null;
        }
    }

    private static string GetDiscoveredProjectRoot(string repoRoot, string prefix)
    {
        string normalizedPrefix = prefix.Replace('\\', '/');
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
               && string.Equals(discovered.Pathspec, selected.Pathspec, PathComparison)
               && RepositoryPathComparer.AreEquivalent(
                   discovered.RepoRoot,
                   selected.RepoRoot)
               && RepositoryPathComparer.AreEquivalent(
                   discovered.ProjectRoot,
                   selected.ProjectRoot);
    }

    private static bool IsSameRepository(RepositoryInfo left, RepositoryInfo right)
    {
        return RepositoryPathComparer.AreEquivalent(left.RepoRoot, right.RepoRoot)
               && RepositoryPathComparer.AreEquivalent(left.ProjectRoot, right.ProjectRoot)
               && string.Equals(left.Pathspec, right.Pathspec, PathComparison);
    }

    private async Task<CommitResult> CommitAllCoreAsync(
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await GetAttachedBranchRefCoreAsync(repository, runner, cancellationToken).ConfigureAwait(false);
        string? ignoredPath = await FindIgnoredExistingRequiredProjectPathAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        ThrowIfRequiredProjectPathIgnored(ignoredPath);
        WorkspaceStatus status = await GetStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfConflicted(status);
        if (status.IsClean)
        {
            return new CommitResult.NoChanges();
        }

        GitIdentity? identity = await GetIdentityCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            if (kind != SnapshotKind.Manual)
            {
                await RaiseMissingIdentityNoticeIfNeededAsync(
                        repository,
                        runner,
                        cancellationToken)
                    .ConfigureAwait(false);
                return new CommitResult.SkippedNoIdentity();
            }

            throw new GitIdentityRequiredException();
        }

        await RaiseLargeMediaNoticeIfNeededAsync(
            repository,
            runner,
            status,
            cancellationToken).ConfigureAwait(false);

        await runner.RunAsync(
            repository,
            ["add", "-A", "--", repository.Pathspec],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);

        var arguments = new List<string>
        {
            "commit",
            "-m",
            message,
        };
        if (kind != SnapshotKind.Manual)
        {
            arguments.Add("-m");
            arguments.Add($"Beutl-Snapshot: {kind.ToString().ToLowerInvariant()}");
        }

        arguments.Add("--");
        arguments.Add(repository.Pathspec);
        await runner.RunAsync(
            repository,
            arguments,
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        CommitResult result = await ResolveCommittedResultAsync(repository, runner)
            .ConfigureAwait(false);
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return result;
    }

    private async Task<CommitResult> ResolveCommittedResultAsync(
        RepositoryInfo repository,
        IGitCliRunner runner)
    {
        try
        {
            GitCommandResult revParse = await runner.RunAsync(
                repository,
                ["rev-parse", "HEAD"],
                GitCommandOptions.Local,
                CancellationToken.None).ConfigureAwait(false);
            string sha = revParse.Stdout.Trim();
            if (string.IsNullOrEmpty(sha))
            {
                throw new InvalidOperationException(
                    "Git did not report the revision created by the successful commit.");
            }

            return new CommitResult.Committed(new CommitRevision.Known(sha));
        }
        catch (Exception ex)
        {
            LogWarningBestEffort(
                ex,
                "Failed to resolve the revision created by a successful Git commit.");
            return new CommitResult.Committed(new CommitRevision.Unavailable());
        }
    }

    private async Task EnsureNotConflictedCoreAsync(CancellationToken cancellationToken)
    {
        WorkspaceStatus status = await GetStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfConflicted(status);
    }

    private static void ThrowIfConflicted(WorkspaceStatus status)
    {
        if (status.HasConflicts)
        {
            throw new VersionControlConflictedException(Strings.VersionControl_ConflictGuidance);
        }
    }

    private static SnapshotKind ParseSnapshotKind(string trailer)
    {
        return trailer.Trim().ToLowerInvariant() switch
        {
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
        string normalized = path.Replace('\\', '/');
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

    private static async Task SetLocalIdentityCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        GitIdentity identity,
        CancellationToken cancellationToken)
    {
        await runner.RunAsync(
            repository,
            ["config", "--local", "user.name", identity.Name],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        await runner.RunAsync(
            repository,
            ["config", "--local", "user.email", identity.Email],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateIdentity(GitIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Email);
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

        string acknowledgementKey = LfsQuotaNoticeConfigKeyPrefix
                                    + GetConfigKeyHash(repository.Pathspec);
        if (!await IsLfsActiveAsync(repository, cancellationToken).ConfigureAwait(false)
            || await GetLocalBooleanConfigAsync(
                repository,
                runner,
                acknowledgementKey,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (!await PresentPolicyNoticeAsync(
                new VersionControlPolicyNotice.LfsRemoteQuota(),
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

    private async Task RaiseLargeMediaNoticeIfNeededAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        WorkspaceStatus status,
        CancellationToken cancellationToken)
    {
        string acknowledgementKey = LargeMediaNoticeConfigKeyPrefix
                                    + GetConfigKeyHash(repository.Pathspec);
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
            string? path = GetLargeMediaPath(repository, change.Path, thresholdBytes);
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
                        Path.GetRelativePath(repository.ProjectRoot, path).Replace('\\', '/'),
                        sizeBytes),
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
            return;
        }
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

    private async Task RaiseMissingIdentityNoticeIfNeededAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string acknowledgementKey = MissingIdentityNoticeConfigKeyPrefix
                                    + GetConfigKeyHash(repository.Pathspec);
        if (await GetLocalBooleanConfigAsync(
                repository,
                runner,
                acknowledgementKey,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (!await PresentPolicyNoticeAsync(
                new VersionControlPolicyNotice.MissingIdentity(),
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

    private static string GetConfigKeyHash(string value)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private async Task<bool> IsLfsActiveAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        (GitAvailability availability, _) = await GetGitRuntimeCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!availability.LfsInstalled)
        {
            return false;
        }

        string attributesPath = Path.Combine(repository.ProjectRoot, ".gitattributes");
        if (!File.Exists(attributesPath))
        {
            return false;
        }

        string contents = await File.ReadAllTextAsync(attributesPath, cancellationToken)
            .ConfigureAwait(false);
        return contents.Contains("filter=lfs", StringComparison.Ordinal);
    }

    private static string? GetLargeMediaPath(
        RepositoryInfo repository,
        string repoRelativePath,
        long thresholdBytes)
    {
        string normalizedPath = repoRelativePath.Replace('\\', '/');
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

        if (!projectRelativePath.StartsWith("resources/", StringComparison.OrdinalIgnoreCase)
            || !s_mediaExtensions.Contains(Path.GetExtension(projectRelativePath)))
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

    private async Task EnsureLinesAsync(
        string path,
        IReadOnlyList<string> requiredLines,
        CancellationToken cancellationToken)
    {
        await UpdateHygieneFileAsync(
            path,
            lines =>
            {
                foreach (string requiredLine in requiredLines)
                {
                    if (!lines.Contains(requiredLine, StringComparer.Ordinal))
                    {
                        lines.Add(requiredLine);
                    }
                }

                return lines;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAttributesAsync(
        string path,
        bool useLfs,
        CancellationToken cancellationToken)
    {
        await UpdateHygieneFileAsync(
            path,
            lines =>
            {
                int? managedBlockIndex = RemoveManagedLfsBlocks(lines);
                foreach (string requiredLine in s_textAttributeLines)
                {
                    if (!lines.Contains(requiredLine, StringComparer.Ordinal))
                    {
                        lines.Add(requiredLine);
                    }
                }

                if (useLfs)
                {
                    int insertionIndex = managedBlockIndex is { } existingIndex
                        ? Math.Min(existingIndex, lines.Count)
                        : 0;
                    lines.InsertRange(
                        insertionIndex,
                        [ManagedLfsBeginMarker, .. s_lfsAttributeLines, ManagedLfsEndMarker]);
                }

                return lines;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static int? RemoveManagedLfsBlocks(List<string> lines)
    {
        int? firstBlockIndex = null;
        int index = 0;
        while (index < lines.Count)
        {
            if (!string.Equals(lines[index], ManagedLfsBeginMarker, StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            int end = lines.FindIndex(
                index + 1,
                static line => string.Equals(
                    line,
                    ManagedLfsEndMarker,
                    StringComparison.Ordinal));
            if (end < 0)
            {
                index++;
                continue;
            }

            firstBlockIndex ??= index;
            lines.RemoveRange(index, end - index + 1);
        }

        return firstBlockIndex;
    }

    private async Task UpdateHygieneFileAsync(
        string path,
        Func<List<string>, List<string>> updateLines,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaxHygieneWriteAttempts; attempt++)
        {
            HygieneFileSnapshot snapshot = await ReadHygieneFileSnapshotAsync(
                    path,
                    cancellationToken)
                .ConfigureAwait(false);
            List<string> lines = ReadHygieneLines(snapshot.Contents);
            string contents = string.Join('\n', updateLines(lines)) + '\n';
            if (snapshot.Exists
                && string.Equals(snapshot.Contents, contents, StringComparison.Ordinal))
            {
                return;
            }

            string temporaryPath = await WriteTemporaryHygieneFileAsync(
                    path,
                    contents,
                    snapshot,
                    cancellationToken)
                .ConfigureAwait(false);
            try
            {
                if (_beforeHygieneFileReplace is not null)
                {
                    await _beforeHygieneFileReplace(path, cancellationToken).ConfigureAwait(false);
                }

                HygieneFileSnapshot current = await ReadHygieneFileSnapshotAsync(
                        path,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (current != snapshot)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (_beforeHygieneFileCommit is not null)
                {
                    await _beforeHygieneFileCommit(path, cancellationToken).ConfigureAwait(false);
                }

                HygieneFileSnapshot finalSnapshot = await ReadHygieneFileSnapshotAsync(
                        path,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (finalSnapshot != snapshot)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    File.Move(temporaryPath, path, overwrite: snapshot.Exists);
                }
                catch (IOException) when (!snapshot.Exists && File.Exists(path))
                {
                    continue;
                }

                return;
            }
            finally
            {
                TryDeleteHygieneTemporaryFile(temporaryPath);
            }
        }

        throw new InvalidOperationException(
            $"Repository hygiene could not update '{path}' because it kept changing.");
    }

    private static async Task<string> WriteTemporaryHygieneFileAsync(
        string path,
        string contents,
        HygieneFileSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(path)
                           ?? throw new InvalidOperationException(
                               $"The repository hygiene path '{path}' has no parent directory.");
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             new FileStreamOptions
                             {
                                 Mode = FileMode.CreateNew,
                                 Access = FileAccess.Write,
                                 Share = FileShare.None,
                                 Options = FileOptions.Asynchronous,
                             }))
            await using (var writer = new StreamWriter(
                             stream,
                             new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(contents.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }

            CopyHygieneFileMetadata(temporaryPath, snapshot);
            return temporaryPath;
        }
        catch
        {
            TryDeleteHygieneTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static void CopyHygieneFileMetadata(
        string temporaryPath,
        HygieneFileSnapshot snapshot)
    {
        if (snapshot.Attributes is { } attributes)
        {
            File.SetAttributes(temporaryPath, attributes);
        }

        if (!OperatingSystem.IsWindows() && snapshot.UnixMode is { } unixMode)
        {
            File.SetUnixFileMode(temporaryPath, unixMode);
        }
    }

    private static async Task<HygieneFileSnapshot> ReadHygieneFileSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        EnsureHygienePathIsSafe(path);
        try
        {
            if (!File.Exists(path))
            {
                return new HygieneFileSnapshot(
                    Exists: false,
                    Contents: null,
                    Attributes: null,
                    UnixMode: null);
            }

            string contents = await File.ReadAllTextAsync(path, cancellationToken)
                .ConfigureAwait(false);
            FileAttributes attributes = File.GetAttributes(path);
            UnixFileMode? unixMode = null;
            if (!OperatingSystem.IsWindows())
            {
                unixMode = File.GetUnixFileMode(path);
            }

            EnsureHygienePathIsSafe(path);
            return new HygieneFileSnapshot(
                Exists: true,
                contents,
                attributes,
                unixMode);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            EnsureHygienePathIsSafe(path);
            return new HygieneFileSnapshot(
                Exists: false,
                Contents: null,
                Attributes: null,
                UnixMode: null);
        }
    }

    private static List<string> ReadHygieneLines(string? contents)
    {
        if (string.IsNullOrEmpty(contents))
        {
            return [];
        }

        var lines = new List<string>();
        using var reader = new StringReader(contents);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static void TryDeleteHygieneTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record HygieneFileSnapshot(
        bool Exists,
        string? Contents,
        FileAttributes? Attributes,
        UnixFileMode? UnixMode);

    private static void EnsureHygienePathsAreSafe(RepositoryInfo repository)
    {
        EnsureHygienePathIsSafe(Path.Combine(repository.ProjectRoot, ".gitignore"));
        EnsureHygienePathIsSafe(Path.Combine(repository.ProjectRoot, ".gitattributes"));
    }

    private static void EnsureHygienePathIsSafe(string path)
    {
        try
        {
            var file = new FileInfo(path);
            file.Refresh();
            if (file.LinkTarget is not null
                || (file.Exists && (file.Attributes & FileAttributes.ReparsePoint) != 0)
                || Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Repository hygiene requires '{path}' to be a regular file.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"The repository hygiene path '{path}' could not be inspected safely.",
                ex);
        }
    }

    private static async Task<string?> FindIgnoredRequiredProjectPathAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string prefix = repository.Pathspec == "." ? string.Empty : repository.Pathspec + "/";
        var paths = GetRequiredProjectRelativePaths(repository.ProjectRoot)
            .Select(path => prefix + path)
            .ToList();
        if (repository.Pathspec != ".")
        {
            paths.Add(repository.Pathspec + "/");
        }

        return await FindIgnoredPathAsync(
                repository,
                runner,
                paths,
                environmentOverrides: null,
                includeTrackedFiles: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<string?> FindIgnoredExistingRequiredProjectPathAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> pathspecs = CreateIgnoredRequiredProjectPathspecs(repository);
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "ls-files",
                "--others",
                "--ignored",
                "--exclude-standard",
                "-z",
                "--",
                .. pathspecs,
            ],
            new GitCommandOptions(
                GitCommandExecutionKind.Local,
                MaxStdoutBytes: MaxIgnoredRequiredPathOutputBytes,
                UseLiteralPathspecs: false),
            cancellationToken).ConfigureAwait(false);
        if (result.StdoutTruncated
            || !HasOnlyExcludedBeutlDirectoryWarnings(repository, result.Stderr))
        {
            throw new InvalidOperationException(
                "Git could not safely determine whether required project files are ignored.");
        }

        return GitCliRunner.SplitNullSeparated(result.Stdout).FirstOrDefault();
    }

    private static bool HasOnlyExcludedBeutlDirectoryWarnings(
        RepositoryInfo repository,
        string stderr)
    {
        if (stderr.Length == 0)
        {
            return true;
        }

        if (!stderr.EndsWith('\n'))
        {
            return false;
        }

        const string warningPrefix = "warning: could not open directory '";
        const string pathTerminator = "': ";
        int lineStart = 0;
        while (lineStart < stderr.Length)
        {
            int lineEnd = stderr.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                return false;
            }

            ReadOnlySpan<char> line = stderr.AsSpan(lineStart, lineEnd - lineStart);
            if (!line.IsEmpty && line[^1] == '\r')
            {
                line = line[..^1];
            }

            if (line.IsEmpty
                || !IsExcludedBeutlDirectoryWarning(repository, line, warningPrefix, pathTerminator))
            {
                return false;
            }

            lineStart = lineEnd + 1;
        }

        return true;
    }

    private static bool IsExcludedBeutlDirectoryWarning(
        RepositoryInfo repository,
        ReadOnlySpan<char> line,
        string warningPrefix,
        string pathTerminator)
    {
        if (!line.StartsWith(warningPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> remainder = line[warningPrefix.Length..];
        int terminatorIndex = remainder.IndexOf(pathTerminator, StringComparison.Ordinal);
        if (terminatorIndex <= 0)
        {
            return false;
        }

        ReadOnlySpan<char> warningPath = remainder[..terminatorIndex];
        ReadOnlySpan<char> reason = remainder[(terminatorIndex + pathTerminator.Length)..];
        if (reason.IsEmpty
            || warningPath.Length < 2
            || warningPath[^1] != '/'
            || warningPath[0] == '/')
        {
            return false;
        }

        warningPath = warningPath[..^1];
        foreach (char character in warningPath)
        {
            if (character is '\'' or '"' or '\\' || char.IsControl(character))
            {
                return false;
            }
        }

        if (reason.Trim().IsEmpty)
        {
            return false;
        }

        foreach (char character in reason)
        {
            if (character is '\'' or '"' or '\\' || char.IsControl(character))
            {
                return false;
            }
        }

        ReadOnlySpan<char> projectPath = repository.Pathspec.AsSpan();
        if (repository.Pathspec != "."
            && (warningPath.Length <= projectPath.Length
                || !warningPath[..projectPath.Length].Equals(projectPath, PathComparison)
                || warningPath[projectPath.Length] != '/'))
        {
            return false;
        }

        ReadOnlySpan<char> relativePath = repository.Pathspec == "."
            ? warningPath
            : warningPath[(projectPath.Length + 1)..];
        int componentStart = 0;
        bool isInBeutlStateDirectory = false;
        while (componentStart < relativePath.Length)
        {
            int separator = relativePath[componentStart..].IndexOf('/');
            int componentLength = separator < 0
                ? relativePath.Length - componentStart
                : separator;
            ReadOnlySpan<char> component = relativePath.Slice(componentStart, componentLength);
            if (component.IsEmpty || component.SequenceEqual(".") || component.SequenceEqual(".."))
            {
                return false;
            }

            isInBeutlStateDirectory |= component.Equals(
                ".beutl",
                StringComparison.OrdinalIgnoreCase);
            if (separator < 0)
            {
                return isInBeutlStateDirectory;
            }

            componentStart += componentLength + 1;
        }

        return false;
    }

    private static IReadOnlyList<string> CreateIgnoredRequiredProjectPathspecs(
        RepositoryInfo repository)
    {
        string prefix = repository.Pathspec == "."
            ? string.Empty
            : EscapeGitGlobPath(repository.Pathspec) + "/";
        var result = new List<string>(
            s_ignoredRequiredProjectPathspecSuffixes.Length
            + s_ignoredOptionalProjectPathspecSuffixes.Length);
        foreach (string suffix in s_ignoredRequiredProjectPathspecSuffixes)
        {
            result.Add($":(top,glob){prefix}{suffix}");
        }

        foreach (string suffix in s_ignoredOptionalProjectPathspecSuffixes)
        {
            result.Add($":(top,exclude,glob){prefix}{suffix}");
        }

        return result;
    }

    private static string EscapeGitGlobPath(string path)
    {
        var builder = new StringBuilder(path.Length);
        foreach (char character in path)
        {
            if (character is '\\' or '*' or '?' or '[' or ']')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static async Task<string?> FindIgnoredRequiredProjectPathBeforeInitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(repository.ProjectRoot))
        {
            return null;
        }

        string probeRoot = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-ignore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(probeRoot);
        try
        {
            var probeRepository = new RepositoryInfo(probeRoot, probeRoot);
            await runner.RunAsync(
                probeRepository,
                ["init"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            var environmentOverrides = new Dictionary<string, string?>
            {
                ["GIT_DIR"] = Path.Combine(probeRoot, ".git"),
                ["GIT_WORK_TREE"] = repository.ProjectRoot,
            };
            return await FindIgnoredPathAsync(
                    probeRepository,
                    runner,
                    GetRequiredProjectRelativePaths(repository.ProjectRoot),
                    environmentOverrides,
                    includeTrackedFiles: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDeleteIgnoreProbeDirectory(probeRoot);
        }
    }

    private static async Task<string?> FindIgnoredPathAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        IEnumerable<string> paths,
        IReadOnlyDictionary<string, string?>? environmentOverrides,
        bool includeTrackedFiles,
        CancellationToken cancellationToken)
    {
        string input = string.Join(
            '\0',
            paths.Distinct(StringComparer.Ordinal)) + '\0';
        if (input.Length == 1)
        {
            return null;
        }

        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                includeTrackedFiles
                    ? ["check-ignore", "--no-index", "--stdin", "-z"]
                    : ["check-ignore", "--stdin", "-z"],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    EnvironmentOverrides: environmentOverrides,
                    StandardInput: input,
                    UseLiteralPathspecs: false),
                cancellationToken).ConfigureAwait(false);
            return GitCliRunner.SplitNullSeparated(result.Stdout).FirstOrDefault();
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> GetRequiredProjectRelativePaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal)
        {
            ".gitignore",
            ".gitattributes",
            "beutl-required-project.bep",
            "beutl-required-project.scene",
            "beutl-required-project.belm",
        };
        foreach (string extension in s_mediaExtensions)
        {
            paths.Add($"resources/beutl-required-media{extension}");
        }

        if (Directory.Exists(projectRoot))
        {
            foreach (string path in EnumerateRequiredProjectFiles(projectRoot))
            {
                paths.Add(Path.GetRelativePath(projectRoot, path).Replace('\\', '/'));
            }
        }

        return [.. paths];
    }

    private static IEnumerable<string> EnumerateRequiredProjectFiles(string projectRoot)
    {
        var pending = new Stack<(string Directory, bool IsResourceDirectory)>();
        pending.Push((projectRoot, IsResourceDirectory: false));
        var options = new EnumerationOptions { AttributesToSkip = 0 };
        while (pending.TryPop(out (string Directory, bool IsResourceDirectory) item))
        {
            foreach (string file in Directory.EnumerateFiles(item.Directory, "*", options))
            {
                string extension = Path.GetExtension(file);
                if (!string.Equals(extension, ".tmp", StringComparison.OrdinalIgnoreCase)
                    && (item.IsResourceDirectory
                        || s_projectFileExtensions.Contains(extension)))
                {
                    yield return file;
                }
            }

            foreach (string child in Directory.EnumerateDirectories(item.Directory, "*", options))
            {
                string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(child));
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0
                    && !string.Equals(
                        name,
                        ".beutl",
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        name,
                        ".git",
                        StringComparison.OrdinalIgnoreCase))
                {
                    pending.Push((
                        child,
                        item.IsResourceDirectory
                        || string.Equals(name, "resources", StringComparison.OrdinalIgnoreCase)));
                }
            }
        }
    }

    private static void TryDeleteIgnoreProbeDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void ThrowIfRequiredProjectPathIgnored(string? path)
    {
        if (path is not null)
        {
            throw new InvalidOperationException(
                $"The required project path '{path}' is ignored by the repository. "
                + "Update the repository's ignore rules before enabling version control.");
        }
    }

    private static void ValidateRemoteUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrEmpty(uri.UserInfo))
        {
            return;
        }

        bool isSsh = string.Equals(uri.Scheme, "ssh", StringComparison.OrdinalIgnoreCase);
        bool hasPassword = Uri.UnescapeDataString(uri.UserInfo)
            .Contains(':');
        if (!isSsh || hasPassword)
        {
            throw new ArgumentException(
                "Remote URLs must not embed credentials. Configure a Git credential helper instead.",
                nameof(url));
        }
    }

    private async Task QueueStatusChangedCoreAsync(CancellationToken cancellationToken)
    {
        WorkspaceStatus status = await GetStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        QueueStatusChanged(status);
    }

    private async Task TryQueueStatusChangedCoreAsync()
    {
        try
        {
            await QueueStatusChangedCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWarningBestEffort(
                ex,
                "Failed to publish version-control status after a durable Git operation.");
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

    private void QueueStatusChanged(WorkspaceStatus status)
    {
        ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                var payload = ((GitCliVersionControlService Service, WorkspaceStatus Status))state!;
                payload.Service.NotifyStatusChanged(payload.Status);
            },
            (this, status),
            preferLocal: false);
    }

    private void NotifyStatusChanged(WorkspaceStatus status)
    {
        if (IsDisposed || StatusChanged is not { } handlers)
        {
            return;
        }

        foreach (EventHandler<WorkspaceStatus> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, status);
            }
            catch (Exception ex)
            {
                LogWarningBestEffort(
                    ex,
                    "Failed to notify a version-control status subscriber.");
            }
        }
    }

    private RepositoryInfo GetRepository()
    {
        return Repository
               ?? throw new InvalidOperationException(
                   "The project is not associated with a Git repository.");
    }

    private void EnsureWatcher()
    {
        lock (_lifetimeSync)
        {
            if (IsDisposed
                || !_createWatcherWhenRepositoryAvailable
                || _watcher is not null
                || Repository is null)
            {
                return;
            }

            _watcher = new RepositoryWatcher(Repository);
            _watcher.Changed += OnRepositoryChanged;
        }
    }

    private void TryEnsureWatcher()
    {
        try
        {
            EnsureWatcher();
        }
        catch (Exception ex)
        {
            LogWarningBestEffort(
                ex,
                "Failed to start repository watching after initializing version control.");
        }
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
        catch (GitOperationException ex)
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
        catch (GitOperationException ex)
        {
            CaptureRecoverableLock(ex);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void OnRepositoryChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed)
        {
            _ = RefreshStatusFromWatcherAsync();
        }
    }

    private void CaptureRecoverableLock(GitOperationException exception)
    {
        IGitCliRunner? runner = _runner;
        RepositoryInfo? repository = Repository;
        if (!exception.IsRepositoryLockFailure
            || repository is null
            || runner is null)
        {
            return;
        }

        RepositoryLockInfo? lockInfo = runner.GetRecoverableRepositoryLock(repository);
        if (lockInfo is null)
        {
            return;
        }

        RecoverableLock = lockInfo;
        ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                var payload = ((
                    GitCliVersionControlService Service,
                    RepositoryLockInfo LockInfo))state!;
                payload.Service.NotifyRecoverableLockAvailable(payload.LockInfo);
            },
            (this, lockInfo),
            preferLocal: false);
    }

    private void NotifyRecoverableLockAvailable(RepositoryLockInfo lockInfo)
    {
        if (IsDisposed
            || !Equals(RecoverableLock, lockInfo)
            || RecoverableLockAvailable is not { } handlers)
        {
            return;
        }

        foreach (EventHandler<RepositoryLockInfo> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, lockInfo);
            }
            catch (Exception ex)
            {
                LogWarningBestEffort(
                    ex,
                    "Failed to notify a recoverable repository-lock subscriber.");
            }
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

    private void OnVersionControlConfigChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        lock (_runtimeSync)
        {
            _configurationRevision++;
            _cachedAvailability = null;
            _runner = null;
        }
    }

    private async Task RefreshStatusFromWatcherAsync()
    {
        try
        {
            WorkspaceStatus status = await GetStatusAsync(CancellationToken.None).ConfigureAwait(false);
            QueueStatusChanged(status);
        }
        catch (Exception) when (IsDisposed)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to refresh version-control status after a repository change.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    private bool IsDisposed
        => (ServiceLifetimeState)Volatile.Read(ref _lifetimeState) != ServiceLifetimeState.Active;

    private sealed class Transaction : IProjectVersionControlTransaction
    {
        private readonly GitCliVersionControlService _service;

        public Transaction(GitCliVersionControlService service)
        {
            _service = service;
        }

        public Task<CommitResult> CommitAllAsync(
            string message,
            SnapshotKind kind,
            CancellationToken cancellationToken)
            => _service.CommitAllCoreAsync(message, kind, cancellationToken);

        public Task<CheckedOutBranchTip> GetCheckedOutBranchTipAsync(
            CancellationToken cancellationToken)
            => _service.GetCheckedOutBranchTipCoreAsync(cancellationToken);

        public Task<ProjectCheckpoint> CreateProjectCheckpointAsync(
            string message,
            CancellationToken cancellationToken)
            => _service.CreateProjectCheckpointCoreAsync(message, cancellationToken);

        public Task RestoreProjectCheckpointAsync(
            ProjectCheckpoint checkpoint,
            CancellationToken cancellationToken)
            => _service.RestoreProjectCheckpointCoreAsync(checkpoint, cancellationToken);

        public Task<CommitResult> CommitProjectTreeAsync(
            CheckedOutBranchTip expectedCurrent,
            string sourceCommit,
            string message,
            SnapshotKind kind,
            CancellationToken cancellationToken)
            => _service.CommitProjectTreeCoreAsync(
                expectedCurrent,
                sourceCommit,
                message,
                kind,
                cancellationToken);

        public Task<BranchTipRollbackResult> TryRollbackBranchTipAsync(
            CheckedOutBranchTip expectedCurrent,
            CheckedOutBranchTip target,
            CancellationToken cancellationToken)
            => _service.TryRollbackBranchTipCoreAsync(expectedCurrent, target, cancellationToken);

        public Task<bool> DeleteProjectCheckpointAsync(
            ProjectCheckpoint checkpoint,
            CancellationToken cancellationToken)
            => _service.DeleteProjectCheckpointCoreAsync(checkpoint, cancellationToken);

        public Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken)
            => _service.GetStatusCoreAsync(cancellationToken);

        public Task CreateBranchAsync(
            string name,
            string startPoint,
            CancellationToken cancellationToken)
            => _service.CreateBranchCoreAsync(name, startPoint, cancellationToken);

        public Task SwitchBranchAsync(string name, CancellationToken cancellationToken)
            => _service.SwitchBranchCoreAsync(name, cancellationToken);

        public Task<FastForwardPullResult> PullFastForwardAsync(
            CheckedOutBranchTip expectedCurrent,
            ProjectCheckpoint? checkpoint,
            CancellationToken cancellationToken)
            => _service.PullFastForwardCoreAsync(expectedCurrent, checkpoint, cancellationToken);
    }

    private enum ServiceLifetimeState
    {
        Active,
        Retiring,
        Retired,
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

internal static class GitRevisionValidator
{
    public static void ValidateCommitId(string revision, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision, paramName);
        if (revision.Length is < 4 or > 64
            || revision.Any(static character => character is not (>= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F')))
        {
            throw new ArgumentException(
                "The commit revision must be a hexadecimal object ID between 4 and 64 characters.",
                paramName);
        }
    }
}
