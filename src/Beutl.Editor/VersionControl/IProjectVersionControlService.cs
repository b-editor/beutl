namespace Beutl.Editor.VersionControl;

public interface IProjectVersionControlService : IDisposable
{
    RepositoryInfo? Repository { get; }

    Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken);

    Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken);

    event EventHandler<WorkspaceStatus>? StatusChanged;
}
