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
        if (_disposed || !InspectsOpening(projectFile))
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

    // Of the coordinators of an editor service, one scans each open, so a file is warned about once: the coordinator
    // tracking the loaded project at that file, or else the one waiting for a project.
    private bool InspectsOpening(string projectFile)
    {
        EditorWorkspace? owner = _projectService.Sessions.Find(projectFile) is { } session
            ? _editorService.WorkspaceFor(session)
            : null;
        return owner?.VersionControl is { } tracking
            ? ReferenceEquals(tracking, this)
            : _workspace is null;
    }
}
