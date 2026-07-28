namespace Beutl.Editor.VersionControl;

public interface IVersionControlRestoreCoordinator
{
    Task<bool> RestoreAsync(
        string sha,
        CancellationToken cancellationToken);

    Task<bool> RestoreToNewBranchAsync(
        string sha,
        string branchName,
        CancellationToken cancellationToken);
}
