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

    Task InitializeAsync(InitOptions options, CancellationToken cancellationToken);

    Task EnsureRepositoryHygieneAsync(CancellationToken cancellationToken);

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

    Task<ProjectCheckpoint> CreateProjectCheckpointAsync(
        string message,
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

    Task<BranchTipRollbackResult> TryRollbackBranchTipAsync(
        CheckedOutBranchTip expectedCurrent,
        CheckedOutBranchTip target,
        CancellationToken cancellationToken);

    Task<bool> DeleteProjectCheckpointAsync(
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken);

    Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task CreateBranchAsync(
        string name,
        string startPoint,
        CancellationToken cancellationToken);

    Task SwitchBranchAsync(string name, CancellationToken cancellationToken);

    Task<FastForwardPullResult> PullFastForwardAsync(
        CheckedOutBranchTip expectedCurrent,
        ProjectCheckpoint? checkpoint,
        CancellationToken cancellationToken);
}

internal sealed record ProjectVersionControlFinalSnapshot(
    string Message,
    SnapshotKind Kind);
