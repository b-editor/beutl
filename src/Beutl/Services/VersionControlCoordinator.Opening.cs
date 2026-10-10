using Beutl.Editor.VersionControl;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private async Task InspectProjectOpeningAsync(string projectFile)
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
