namespace Beutl.Editor.VersionControl;

public interface IProjectVersionControlService : IDisposable
{
    RepositoryInfo? Repository { get; }

    Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken);

    Task InitializeAsync(InitOptions options, CancellationToken cancellationToken);

    Task<CommitResult> CommitAllAsync(
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken);

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

    Task RestoreWorktreeFromAsync(
        string sha,
        CancellationToken cancellationToken);

    Task CreateBranchFromAsync(
        string name,
        string sha,
        CancellationToken cancellationToken);

    Task SwitchBranchAsync(
        string name,
        CancellationToken cancellationToken);

    Task<GitIdentity?> GetIdentityAsync(CancellationToken cancellationToken);

    Task SetLocalIdentityAsync(
        GitIdentity identity,
        CancellationToken cancellationToken);

    event EventHandler<WorkspaceStatus>? StatusChanged;
}
