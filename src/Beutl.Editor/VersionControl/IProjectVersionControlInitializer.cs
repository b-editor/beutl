namespace Beutl.Editor.VersionControl;

public interface IProjectVersionControlInitializer
{
    Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken);

    Task<bool> InitializeCurrentProjectAsync(
        Func<IProjectVersionControlService, Task<bool>> requestIdentityAsync,
        CancellationToken cancellationToken);
}
