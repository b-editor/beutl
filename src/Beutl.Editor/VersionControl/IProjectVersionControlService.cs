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

    Task<GitIdentity?> GetIdentityAsync(CancellationToken cancellationToken);

    Task SetLocalIdentityAsync(
        GitIdentity identity,
        CancellationToken cancellationToken);

    event EventHandler<WorkspaceStatus>? StatusChanged;
}
