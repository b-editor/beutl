using Beutl.Editor.VersionControl;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private Task InspectProjectOpeningAsync(string projectFile)
    {
        return RunOnUiThreadAsync(() => InspectProjectOpeningCoreAsync(projectFile));
    }

    // Reads the project's files only, so it needs no operation gate, and the disposal stops it.
    private async Task InspectProjectOpeningCoreAsync(string projectFile)
    {
        if (_disposed)
        {
            return;
        }

        CancellationToken cancellationToken = _lifetimeCancellation.Token;
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
