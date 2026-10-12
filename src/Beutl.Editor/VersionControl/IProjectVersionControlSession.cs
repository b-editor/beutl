using Reactive.Bindings;

namespace Beutl.Editor.VersionControl;

internal interface IProjectVersionControlSession
{
    IReadOnlyReactiveProperty<bool> IsGitAvailable { get; }

    IReadOnlyReactiveProperty<bool> IsTracked { get; }

    /// <summary>
    /// Records that the project was explicitly saved, so a Save snapshot can be committed. It never
    /// waits for another version-control operation, such as a push: the snapshot then runs once that
    /// operation has finished and records the project files as they are by then.
    /// </summary>
    /// <param name="completedWrite">
    /// The reservation of a finished save, which the snapshot can take over. The caller still disposes
    /// it, and disposing one that was taken over is a no-op. With <see langword="null"/> the snapshot
    /// competes for the workspace and is skipped when another operation holds it.
    /// </param>
    Task NotifySavedAsync(
        IProjectFileWriteLease? completedWrite = null,
        CancellationToken cancellationToken = default);
}
