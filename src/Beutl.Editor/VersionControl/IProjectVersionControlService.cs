namespace Beutl.Editor.VersionControl;

public interface IProjectVersionControlService
{
    RepositoryInfo? Repository { get; }

    Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken);

    Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FileChange>> GetCommitFilesAsync(
        string sha,
        CancellationToken cancellationToken);

    Task<string> GetDiffAsync(
        string sha,
        string? path,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RemoteInfo>> GetRemotesAsync(
        CancellationToken cancellationToken);

    Task<GitIdentity?> GetIdentityAsync(CancellationToken cancellationToken);

    event EventHandler<WorkspaceStatus>? StatusChanged;
}

internal interface IProjectVersionControlBackend :
    IProjectVersionControlService,
    IRepositoryLockRecoveryService,
    IDisposable
{
    Task<RepositoryInfo?> DiscoverRepositoryAsync(
        string projectRoot,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the repository-relative <c>.beutl/</c> and <c>*.tmp</c> entries the repository already
    /// tracks. Generated ignore rules cannot untrack these, and snapshot status hides their
    /// modifications, so they leave the repository permanently dirty for the pull precondition.
    /// </summary>
    Task<IReadOnlyList<string>> GetTrackedReservedPathsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Drops the given entries from the index and records that in its own commit. The files stay on
    /// disk. Requires the user's consent: a repository may be sharing them deliberately.
    /// </summary>
    Task UntrackReservedPathsAsync(
        IReadOnlyList<string> reservedPaths,
        CancellationToken cancellationToken);

    Task InitializeAsync(InitOptions options, CancellationToken cancellationToken);

    Task EnsureRepositoryHygieneAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reports whether this repository already records an opt-in for the project: either the
    /// generated ignore and attribute rules are in place, or its history already carries a
    /// snapshot Beutl committed for the project. That record is what distinguishes a repository
    /// version tracking was enabled for from one the user created and Beutl has never managed.
    /// </summary>
    Task<bool> HasVersionTrackingOptInAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken);

    Task<CommitResult> CommitAllAsync(
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken);

    Task SetRemoteAsync(string url, CancellationToken cancellationToken);

    Task<RemoteOpResult> PushAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken);

    Task SetLocalIdentityAsync(
        GitIdentity identity,
        CancellationToken cancellationToken);

    Task<TResult> ExecuteExclusiveAsync<TResult>(
        Func<IProjectVersionControlTransaction, Task<TResult>> operation,
        CancellationToken cancellationToken);

    Task RetireAsync(ProjectVersionControlFinalSnapshot? finalSnapshot);
}

public interface IRepositoryLockRecoveryService
{
    RepositoryLockInfo? RecoverableLock { get; }

    Task<bool> RemoveRecoverableLockAsync(CancellationToken cancellationToken);

    event EventHandler<RepositoryLockInfo>? RecoverableLockAvailable;
}

internal interface IProjectVersionControlTransaction
{
    Task<CommitResult> CommitAllAsync(
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken);

    Task<CheckedOutBranchTip> GetCheckedOutBranchTipAsync(CancellationToken cancellationToken);

    Task<PullPreflightResult> PreflightPullAsync(
        CheckedOutBranchTip expectedCurrent,
        CancellationToken cancellationToken);

    Task<ProjectCheckpoint> CreateProjectCheckpointAsync(
        string message,
        CancellationToken cancellationToken);

    Task<PendingPullRecovery> PersistPendingPullRecoveryAsync(
        ProjectCheckpoint checkpoint,
        CheckedOutBranchTip targetTip,
        string projectFile,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingPullRecovery>> GetPendingPullRecoveriesAsync(
        CancellationToken cancellationToken);

    Task<PendingPullRecoveryOutcome> RecoverPendingPullRecoveryAsync(
        PendingPullRecovery recovery,
        CancellationToken cancellationToken);

    Task CompletePendingPullRecoveryAsync(
        PendingPullRecovery recovery,
        CancellationToken cancellationToken);

    Task RestoreProjectCheckpointAsync(
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken);

    Task<CommitResult> CommitProjectTreeAsync(
        CheckedOutBranchTip expectedCurrent,
        string sourceCommit,
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken);

    Task<bool> RevisionContainsProjectFileAsync(
        string sha,
        string projectFile,
        CancellationToken cancellationToken);

    Task<BranchTipRollbackResult> TryRollbackBranchTipAsync(
        CheckedOutBranchTip expectedCurrent,
        CheckedOutBranchTip target,
        CancellationToken cancellationToken);

    Task<bool> DeleteProjectCheckpointAsync(
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken);

    Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(CancellationToken cancellationToken);

    Task<bool> CanCreateBranchAsync(
        string name,
        CancellationToken cancellationToken);

    Task CreateBranchAsync(
        string name,
        string startPoint,
        CancellationToken cancellationToken);

    /// <summary>
    /// Downloads the Git LFS objects the named branch needs, so a later switch does not have to
    /// reach the network while the project is closed. Best effort: a failure leaves the switch to
    /// fall back to whatever is already cached.
    /// </summary>
    Task PrefetchBranchLfsObjectsAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// The same prefetch for a target commit, so pull and restore do not reach the network from
    /// their uncancellable checkout either. Best effort, exactly like the branch variant.
    /// </summary>
    Task PrefetchCommitLfsObjectsAsync(string sha, CancellationToken cancellationToken);

    Task SwitchBranchAsync(string name, CancellationToken cancellationToken);

    Task<FastForwardPullResult> PullFastForwardAsync(
        CheckedOutBranchTip expectedCurrent,
        ProjectCheckpoint? checkpoint,
        string projectFile,
        CancellationToken cancellationToken);
}

internal sealed record ProjectVersionControlFinalSnapshot(
    string Message,
    SnapshotKind Kind);
