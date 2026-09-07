namespace Beutl.Editor.VersionControl;

public interface IProjectVersionControlCoordinator
{
    event EventHandler? PendingPullRecoveriesChanged;

    Task<CommitResult> CommitManualAsync(
        string message,
        CancellationToken cancellationToken);

    Task<bool> RestoreAsync(
        string sha,
        CancellationToken cancellationToken);

    Task<bool> RestoreToNewBranchAsync(
        string sha,
        string branchName,
        CancellationToken cancellationToken);

    Task<bool> CreateBranchAsync(
        string branchName,
        CancellationToken cancellationToken);

    Task<bool> SwitchBranchAsync(
        string branchName,
        CancellationToken cancellationToken);

    Task SetRemoteAsync(
        string url,
        CancellationToken cancellationToken);

    Task SetLocalIdentityAsync(
        GitIdentity identity,
        CancellationToken cancellationToken);

    Task<RemoteOpResult> PushAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken);

    Task<RemoteOpResult> PullAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectRecoveryInfo>> GetPendingPullRecoveriesAsync(
        CancellationToken cancellationToken);

    Task<ProjectRecoveryResult> RecoverPendingPullAsync(
        string recoveryId,
        CancellationToken cancellationToken);
}
