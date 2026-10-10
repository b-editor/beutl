using Beutl.Editor.VersionControl;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private Task InspectProjectOpeningAsync(string projectFile)
    {
        return RunOnUiThreadAsync(() => InspectProjectOpeningCoreAsync(projectFile));
    }

    private async Task InspectProjectOpeningCoreAsync(string projectFile)
    {
        using NonTransactionalOperationLease? operation =
            TryBeginNonTransactionalOperation(CancellationToken.None);
        if (operation is null)
        {
            return;
        }

        CancellationToken cancellationToken = operation.CancellationToken;
        string? markerFile = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            cancellationToken);
        if (markerFile is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WarnConflictMarkersAsync(markerFile);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
